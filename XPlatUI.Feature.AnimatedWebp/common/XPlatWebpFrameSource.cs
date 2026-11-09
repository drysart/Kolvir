using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// An animated WebP as an <see cref="IXPlatImageDataSource"/>: every frame decoded and composited up
/// front, with the file's own durations and loop declaration.
/// </summary>
/// <remarks>
/// <para>
/// <b>Seeking is why it is up front.</b> The timeline lets a viewer jump anywhere, backwards included,
/// and a lazy window cannot go back. Every frame is therefore a complete picture by the time this
/// class exists, and <see cref="FrameAt"/> is an array lookup.
/// </para>
/// <para>
/// <b>WebP frames are deltas too, but the flags are not GIF's.</b> An <c>ANMF</c> names a rectangle,
/// a duration, a blend bit and a disposal bit. A frame whose blend bit is set <i>replaces</i> its
/// rectangle whether or not it carries alpha; a frame whose blend bit is clear is drawn over what is
/// already there. The disposal bit has only two values, not GIF's three: keep the canvas, or clear
/// the frame's rectangle to transparent before the next frame. WebP has no "restore previous".
/// </para>
/// <para>
/// <b>Sub-rectangle frames are normal.</b> A frame usually carries only the pixels that changed, so
/// it is drawn at its own offset on a full canvas and the picture handed out is always the canvas.
/// That is why <see cref="NaturalSize"/> is fixed and a frame that differs by a pixel cannot move the
/// layout.
/// </para>
/// <para>
/// <b>One frame is a still picture.</b> A file with a single <c>ANMF</c> is treated exactly as the GIF
/// feature treats a single-frame GIF: a picture with no timeline, no loop and a duration of zero.
/// That keeps "enable the feature" from turning a one-frame file into something that claims to play.
/// </para>
/// </remarks>
internal sealed class XPlatWebpFrameSource : IXPlatImageDataSource
{
    // Nearest-neighbour: WebP's lossless mode is how pixel-art animations are authored, and sampling
    // it would turn a crisp animation into a blurry one. Animated frames are drawn at their decoded
    // size; only a still is recorded the way the built-in raster decoder records one.
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Nearest, SKMipmapMode.None);
    private static readonly SKSamplingOptions StillSampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly SKPicture[] _frames;
    private readonly TimeSpan[] _starts;
    private readonly TimeSpan _duration;
    private readonly SKSize _naturalSize;
    private readonly bool _loops;
    private bool _disposed;

    private XPlatWebpFrameSource(SKPicture[] frames, TimeSpan[] starts, TimeSpan duration, SKSize naturalSize, bool loops)
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
    internal static XPlatWebpFrameSource Decode(Stream stream, XPlatWebpOptions options)
    {
        // Read once: the RIFF chunk walk and the frame decodes both need the same bytes, and a
        // provider is promised a stream it may rewind but not one it can read twice in one pass.
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var data = buffer.ToArray();

        var animation = XPlatWebpStructure.Parse(data);

        if (animation is null)
        {
            return DecodeStill(data);
        }

        if (animation.Frames.Length == 1)
        {
            // One frame is a picture, not a one-frame animation: the GIF feature makes the same
            // choice for a single-frame GIF, and a timeline that never advances is not worth having.
            return DecodeSingleFrame(animation);
        }

        var pixelInfo = new SKImageInfo(
            animation.Width,
            animation.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        long bytesPerFrame = pixelInfo.BytesSize;
        if (bytesPerFrame > options.DecodedSizeLimit / animation.Frames.Length)
        {
            // Computed as a division so a hostile frame count cannot overflow the multiplication.
            long needed = bytesPerFrame * animation.Frames.Length;
            throw new InvalidOperationException(
                $"The animation needs {needed} bytes of decoded pixels for {animation.Frames.Length} frames at " +
                $"{animation.Width}x{animation.Height}, which is more than the {options.DecodedSizeLimit} byte limit. " +
                "Raise XPlatWebpOptions.DecodedSizeLimit to load it.");
        }

        var frames = new SKPicture[animation.Frames.Length];
        var starts = new TimeSpan[animation.Frames.Length];

        using var canvas = new SKBitmap(pixelInfo);
        using var drawing = new SKCanvas(canvas);

        var elapsed = TimeSpan.Zero;

        for (int index = 0; index < animation.Frames.Length; index++)
        {
            var frame = animation.Frames[index];

            if (index > 0)
            {
                ApplyDisposal(drawing, animation.Frames[index - 1]);
            }

            starts[index] = elapsed;
            elapsed += Delay(frame, options);

            using var delta = DecodeFrame(frame);
            Composite(drawing, delta, frame);

            frames[index] = Record(canvas, Sampling);
        }

        return new XPlatWebpFrameSource(
            frames,
            starts,
            elapsed,
            new SKSize(animation.Width, animation.Height),
            animation.Loops);
    }

    /// <summary>Decodes a still WebP the way the built-in raster decoder does.</summary>
    private static XPlatWebpFrameSource DecodeStill(byte[] data)
    {
        using var bitmap = SKBitmap.Decode(data)
            ?? throw new InvalidOperationException("The stream is not a WebP the decoder can read.");

        return new XPlatWebpFrameSource(
            [Record(bitmap, StillSampling)],
            [TimeSpan.Zero],
            TimeSpan.Zero,
            new SKSize(bitmap.Width, bitmap.Height),
            loops: false);
    }

    /// <summary>Composites an animation's only frame onto its canvas and hands back a still.</summary>
    private static XPlatWebpFrameSource DecodeSingleFrame(XPlatWebpStructure.Animation animation)
    {
        var pixelInfo = new SKImageInfo(
            animation.Width,
            animation.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        using var canvas = new SKBitmap(pixelInfo);
        using var drawing = new SKCanvas(canvas);

        var frame = animation.Frames[0];
        using var delta = DecodeFrame(frame);
        Composite(drawing, delta, frame);

        return new XPlatWebpFrameSource(
            [Record(canvas, Sampling)],
            [TimeSpan.Zero],
            TimeSpan.Zero,
            new SKSize(animation.Width, animation.Height),
            loops: false);
    }

    /// <summary>Decodes one frame's own pixels, with no compositing onto the frames before it.</summary>
    private static SKBitmap DecodeFrame(XPlatWebpStructure.Frame frame)
    {
        var bitmap = SKBitmap.Decode(frame.Encoded);
        if (bitmap is null)
        {
            throw new InvalidOperationException($"Frame {frame.Left},{frame.Top} could not be decoded.");
        }

        if (bitmap.Width != frame.Width || bitmap.Height != frame.Height)
        {
            var actual = $"{bitmap.Width}x{bitmap.Height}";
            bitmap.Dispose();
            throw new InvalidOperationException(
                $"Frame {frame.Left},{frame.Top} declares {frame.Width}x{frame.Height} but decodes to {actual}.");
        }

        return bitmap;
    }

    /// <summary>Puts the previous frame's disposal into effect before the next frame is drawn.</summary>
    private static void ApplyDisposal(SKCanvas drawing, XPlatWebpStructure.Frame previous)
    {
        if (!previous.DisposesToBackground)
        {
            return;
        }

        // Cleared rather than drawn over: the next frame may blend, and blending onto a transparent
        // canvas is not the same as blending onto the old picture.
        using var paint = new SKPaint { Color = SKColors.Transparent, BlendMode = SKBlendMode.Src };
        var rect = new SKRect(
            previous.Left,
            previous.Top,
            previous.Left + previous.Width,
            previous.Top + previous.Height);
        drawing.DrawRect(rect, paint);
    }

    /// <summary>Draws one frame's pixels onto the canvas, replacing or blending as the frame says.</summary>
    private static void Composite(SKCanvas drawing, SKBitmap delta, XPlatWebpStructure.Frame frame)
    {
        using var restore = new SKAutoCanvasRestore(drawing, doSave: true);
        drawing.ClipRect(new SKRect(
            frame.Left,
            frame.Top,
            frame.Left + frame.Width,
            frame.Top + frame.Height));

        using var paint = new SKPaint
        {
            // A set blend bit in ANMF means "do not blend": the frame replaces its rectangle. A
            // clear bit means draw over, which is what makes a transparent pixel let the old
            // picture through.
            BlendMode = frame.Blends ? SKBlendMode.SrcOver : SKBlendMode.Src,
            IsAntialias = false,
        };

        drawing.DrawBitmap(delta, frame.Left, frame.Top, Sampling, paint);
    }

    /// <summary>Records a copy of the canvas as a picture the animation keeps.</summary>
    private static SKPicture Record(SKBitmap canvas, SKSamplingOptions sampling)
    {
        using var copy = canvas.Copy();
        using var image = SKImage.FromBitmap(copy);
        using var recorder = new SKPictureRecorder();

        var bounds = new SKRect(0f, 0f, image.Width, image.Height);
        var recorded = recorder.BeginRecording(bounds);
        recorded.DrawImage(image, 0f, 0f, sampling, null);

        return recorder.EndRecording();
    }

    /// <summary>
    /// How long a frame is shown, with a zero duration replaced by the configured minimum.
    /// </summary>
    private static TimeSpan Delay(XPlatWebpStructure.Frame frame, XPlatWebpOptions options) =>
        frame.DurationMilliseconds <= 0
            ? options.ZeroDelayMinimum
            : TimeSpan.FromMilliseconds(frame.DurationMilliseconds);

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
