using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// An animated GIF as an <see cref="IXPlatImageDataSource"/>: every frame decoded and composited up
/// front, with the file's own delays and loop declaration.
/// </summary>
/// <remarks>
/// <para>
/// <b>Seeking is why it is up front.</b> The timeline lets a viewer jump anywhere, backwards included,
/// and a lazy window cannot go back. Every frame is therefore a complete picture by the time this
/// class exists, and <see cref="FrameAt"/> is an array lookup.
/// </para>
/// <para>
/// <b>GIF frames are deltas, so they are composited here rather than by Skia.</b> A frame names a
/// rectangle and usually carries only the pixels inside it; whether those pixels replace or blend,
/// and what happens to them before the next frame, is decided by the frame's blend flag and the
/// previous frame's disposal method. Getting that wrong produces frames that look nearly right -
/// correctly sized, correctly timed, subtly corrupt - which is why the tests assert pixel content
/// rather than trusting the demo.
/// </para>
/// <para>
/// Three disposal methods, and what this does with each:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Keep</b> (0 and 1): the frame stays on the canvas and the next frame draws over it.
/// </item>
/// <item>
/// <b>Restore background</b> (2): the frame's rectangle is cleared to transparent before the next
/// frame. The GIF spec says "background colour", but every browser clears to transparent, the
/// background index is absent or meaningless in most animated files, and a card that fills its
/// background makes the distinction invisible - so transparent is the answer that matches what the
/// file looks like on the web.
/// </item>
/// <item>
/// <b>Restore previous</b> (3): the canvas is put back to what it was before the frame was drawn,
/// from a snapshot taken as the frame starts.
/// </item>
/// </list>
/// </remarks>
internal sealed class XPlatGifFrameSource : IXPlatImageDataSource
{
    // Nearest-neighbour: a GIF is drawn at its decoded size, and sampling it would turn a crisp
    // pixel-art animation into a blurry one.
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Nearest, SKMipmapMode.None);

    private readonly SKPicture[] _frames;
    private readonly TimeSpan[] _starts;
    private readonly TimeSpan _duration;
    private readonly SKSize _naturalSize;
    private readonly bool _loops;
    private bool _disposed;

    private XPlatGifFrameSource(SKPicture[] frames, TimeSpan[] starts, TimeSpan duration, SKSize naturalSize, bool loops)
    {
        _frames = frames;
        _starts = starts;
        _duration = duration;
        _naturalSize = naturalSize;
        _loops = loops;
    }

    /// <inheritdoc />
    public TimeSpan Duration => _duration;

    /// <inheritdoc />
    public SKSize NaturalSize => _naturalSize;

    /// <inheritdoc />
    public bool Loops => _loops;

    /// <inheritdoc />
    public SKPicture FrameAt(TimeSpan position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_frames.Length == 1)
        {
            return _frames[0];
        }

        return _frames[IndexAt(position)];
    }

    /// <inheritdoc />
    public TimeSpan? TimeUntilNextFrame(TimeSpan position)
    {
        if (_disposed || _duration <= TimeSpan.Zero)
        {
            return null;
        }

        if (_frames.Length == 1)
        {
            return null;
        }

        int index = IndexAt(position);
        var boundary = index + 1 < _starts.Length ? _starts[index + 1] : _duration;
        var wait = boundary - position;
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var frame in _frames)
        {
            frame.Dispose();
        }
    }

    /// <summary>Decodes the file into frames, or throws when it cannot be read within its limits.</summary>
    /// <exception cref="InvalidOperationException">The file could not be decoded, or is too large.</exception>
    internal static XPlatGifFrameSource Decode(Stream stream, XPlatGifOptions options)
    {
        // Read once: the loop declaration has to be walked from the file's own block structure, and
        // a provider is promised a stream it may rewind but not one it can read twice in one pass.
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var data = buffer.ToArray();

        bool loops = XPlatGifStructure.DeclaresLoop(data);

        using var codec = SKCodec.Create(new MemoryStream(data, writable: false))
            ?? throw new InvalidOperationException("The stream is not a GIF the decoder can read.");

        var size = codec.Info;
        var pixelInfo = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

        int count = codec.FrameCount;
        if (count <= 0)
        {
            // A single-image GIF reports no frames at all, and is a still picture.
            return DecodeStill(codec);
        }

        long bytesPerFrame = pixelInfo.BytesSize;
        if (bytesPerFrame * count > options.DecodedSizeLimit)
        {
            throw new InvalidOperationException(
                $"The animation needs {bytesPerFrame * count} bytes of decoded pixels for {count} frames at " +
                $"{size.Width}x{size.Height}, which is more than the {options.DecodedSizeLimit} byte limit. " +
                "Raise XPlatGifOptions.DecodedSizeLimit to load it.");
        }

        var frameInfos = codec.FrameInfo;
        var frames = new SKPicture[count];
        var starts = new TimeSpan[count];

        using var canvas = new SKBitmap(pixelInfo);
        using var snapshot = new SKBitmap(pixelInfo);
        using var drawing = new SKCanvas(canvas);

        var elapsed = TimeSpan.Zero;

        for (int index = 0; index < count; index++)
        {
            var frame = frameInfos[index];

            if (index > 0)
            {
                ApplyDisposal(drawing, canvas, snapshot, frameInfos[index - 1]);
            }

            starts[index] = elapsed;
            elapsed += Delay(frame, options);

            // A frame that is restored to "previous" needs the canvas as it was before it drew, so
            // the snapshot is taken before the delta is composited.
            if (frame.DisposalMethod == SKCodecAnimationDisposalMethod.RestorePrevious)
            {
                using var copy = canvas.Copy();
                copy.CopyTo(snapshot);
            }

            using var delta = DecodeFrame(codec, pixelInfo, index);
            Composite(drawing, delta, frame);

            frames[index] = Record(canvas);
        }

        return new XPlatGifFrameSource(frames, starts, elapsed, new SKSize(size.Width, size.Height), loops);
    }

    private static XPlatGifFrameSource DecodeStill(SKCodec codec)
    {
        var size = codec.Info;
        var pixelInfo = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var bitmap = new SKBitmap(pixelInfo);
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
        {
            throw new InvalidOperationException($"The GIF could not be decoded ({result}).");
        }

        return new XPlatGifFrameSource(
            [Record(bitmap)],
            [TimeSpan.Zero],
            TimeSpan.Zero,
            new SKSize(size.Width, size.Height),
            loops: false);
    }

    /// <summary>
    /// Decodes one frame's own pixels, with no blending and no prior frame: the delta the file holds.
    /// </summary>
    /// <remarks>
    /// Skia decodes a frame onto a prior frame when it is given one, and reconstructs the whole
    /// picture when it is not. Passing the previous frame index and a cleared buffer produces the
    /// frame's own rectangle and nothing else, which is what the compositor needs to apply the
    /// disposal and blend rules itself.
    /// </remarks>
    private static SKBitmap DecodeFrame(SKCodec codec, SKImageInfo pixelInfo, int index)
    {
        var delta = new SKBitmap(pixelInfo);
        delta.Erase(SKColors.Transparent);

        var result = codec.GetPixels(
            pixelInfo,
            delta.GetPixels(),
            new SKCodecOptions(index, index > 0 ? index - 1 : -1));

        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
        {
            delta.Dispose();
            throw new InvalidOperationException($"Frame {index} could not be decoded ({result}).");
        }

        return delta;
    }

    /// <summary>Puts the previous frame's disposal into effect before the next frame is drawn.</summary>
    private static void ApplyDisposal(SKCanvas drawing, SKBitmap canvas, SKBitmap snapshot, SKCodecFrameInfo previous)
    {
        switch (previous.DisposalMethod)
        {
            case SKCodecAnimationDisposalMethod.RestoreBackgroundColor:
                {
                    // Cleared rather than drawn over: the next frame may blend, and blending onto a
                    // transparent canvas is not the same as blending onto the old picture.
                    using var paint = new SKPaint { Color = SKColors.Transparent, BlendMode = SKBlendMode.Src };
                    drawing.DrawRect(previous.FrameRect, paint);
                    break;
                }

            case SKCodecAnimationDisposalMethod.RestorePrevious:
                {
                    using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
                    using var restore = snapshot.Copy();
                    drawing.DrawBitmap(restore, 0f, 0f, Sampling, paint);
                    break;
                }
        }
    }

    /// <summary>Draws one frame's delta onto the canvas, replacing or blending as the frame says.</summary>
    private static void Composite(SKCanvas drawing, SKBitmap delta, SKCodecFrameInfo frame)
    {
        using var restore = new SKAutoCanvasRestore(drawing, doSave: true);
        drawing.ClipRect(frame.FrameRect);

        using var paint = new SKPaint
        {
            // The blend flag is "draw this over the previous picture" rather than "replace it".
            // GIF's default is to blend, which is what makes a transparent index matter at all.
            BlendMode = frame.Blend == SKCodecAnimationBlend.Src ? SKBlendMode.Src : SKBlendMode.SrcOver,
            IsAntialias = false,
        };

        drawing.DrawBitmap(delta, 0f, 0f, Sampling, paint);
    }

    /// <summary>Records a copy of the canvas as a picture the animation keeps.</summary>
    private static SKPicture Record(SKBitmap canvas)
    {
        using var copy = canvas.Copy();
        using var image = SKImage.FromBitmap(copy);
        using var recorder = new SKPictureRecorder();

        var bounds = new SKRect(0f, 0f, image.Width, image.Height);
        var recorded = recorder.BeginRecording(bounds);
        recorded.DrawImage(image, 0f, 0f, Sampling, null);

        return recorder.EndRecording();
    }

    /// <summary>
    /// How long a frame is shown, with a zero delay replaced by the configured minimum.
    /// </summary>
    private static TimeSpan Delay(SKCodecFrameInfo frame, XPlatGifOptions options) =>
        frame.Duration <= 0 ? options.ZeroDelayMinimum : TimeSpan.FromMilliseconds(frame.Duration);

    /// <summary>The frame whose start is at or before <paramref name="position"/>.</summary>
    private int IndexAt(TimeSpan position)
    {
        if (position <= TimeSpan.Zero)
        {
            return 0;
        }

        int low = 0;
        int high = _starts.Length - 1;

        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (_starts[middle] <= position)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }
}
