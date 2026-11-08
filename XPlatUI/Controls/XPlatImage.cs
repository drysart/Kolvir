using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// Draws an <see cref="SKPicture"/>. The control holds a picture rather than an image, so what it
/// shows is always something a canvas can replay.
/// </summary>
/// <remarks>
/// <see cref="Load(Stream)"/> decodes a bitmap and records one draw of it into a picture at
/// identity; the paint path replays that picture into the destination rectangle. A picture that was
/// not recorded from a bitmap is set directly through <see cref="Picture"/>, which is what a vector
/// renderer or an animation player hands over.
/// The control owns the picture when it was loaded through one of the <c>Load</c> helpers or when
/// <see cref="DisposePictureWithControl"/> is set.
/// </remarks>
public class XPlatImage : XPlatControl
{
    // The sampling the control has always drawn with. The same options are recorded into the
    // picture, so replaying it under the destination's scale samples exactly as a direct draw does.
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private Action<object, Exception>? _loadFailed;
    private SKPicture? _picture;

    // A picture recorded from a decoded bitmap shares that bitmap's pixels, so the source image has
    // to outlive the picture. It is kept here and disposed with the picture; a picture set from
    // outside has no source, and a later phase hands the control pictures that have none.
    private SKImage? _pictureSource;
    private bool _ownsPicture;
    private XPlatSizeMode _sizeMode = XPlatSizeMode.Zoom;

    public XPlatImage()
    {
        AutoSize = true;
    }

    /// <summary>Adds a handler that runs when the image fails to load (for example a missing file).</summary>
    public IDisposable AddLoadFailedHandler(Action<object, Exception> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _loadFailed += handler;
        return new XPlatHandlerToken<Action<object, Exception>>(RemoveLoadFailedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddLoadFailedHandler"/> added.</summary>
    public void RemoveLoadFailedHandler(Action<object, Exception> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _loadFailed -= handler;
    }

    /// <summary>The picture to display. Not disposed unless ownership was transferred.</summary>
    public SKPicture? Picture
    {
        get => _picture;
        set
        {
            if (ReferenceEquals(_picture, value))
            {
                return;
            }

            ReleasePicture();
            _picture = value;
            PerformLayout();
            Invalidate();
        }
    }

    public XPlatSizeMode SizeMode
    {
        get => _sizeMode;
        set
        {
            if (_sizeMode == value)
            {
                return;
            }

            _sizeMode = value;
            Invalidate();
        }
    }

    public XPlatContentAlignment ImageAlign { get; set; } = XPlatContentAlignment.MiddleCenter;

    /// <summary>When true, the picture is disposed together with the control.</summary>
    public bool DisposePictureWithControl { get; set; }

    /// <summary>Loads an image from disk, replacing the current one. Returns false when loading fails.</summary>
    public bool Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Load(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _loadFailed?.Invoke(this, ex);
            return false;
        }
    }

    /// <summary>Decodes an image from a stream and records it into a picture, replacing the current one.</summary>
    public bool Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        SKImage? source = null;
        SKPicture? picture = null;
        try
        {
            using var bitmap = SKBitmap.Decode(stream);
            if (bitmap is null)
            {
                return false;
            }

            source = SKImage.FromBitmap(bitmap);
            picture = Record(source);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            picture?.Dispose();
            source?.Dispose();
            _loadFailed?.Invoke(this, ex);
            return false;
        }

        ReleasePicture();
        _picture = picture;
        _pictureSource = source;
        _ownsPicture = true;
        PerformLayout();
        Invalidate();
        return true;
    }

    /// <summary>Draws a solid color placeholder instead of a picture.</summary>
    public SKColor? PlaceholderColor { get; set; }

    protected override SKSize MeasureContent(SKSize availableSize)
    {
        if (_picture is null)
        {
            return SKSize.Empty;
        }

        var bounds = _picture.CullRect;
        return new SKSize(bounds.Width, bounds.Height);
    }

    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        base.OnPaintBackground(canvas, rect);

        if (PlaceholderColor is { } placeholder && _picture is null)
        {
            XPlatPainter.Fill(canvas, rect, placeholder, CornerRadius);
        }
    }

    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        if (_picture is null)
        {
            return;
        }

        var content = XPlatGeometry.Deflate(rect, Padding);
        if (content.Width <= 0f || content.Height <= 0f)
        {
            return;
        }

        // The recorded bounds are the picture's natural size. They come from the picture rather
        // than from the bitmap that was decoded, so a picture recorded by something else measures
        // and scales by what it actually drew.
        var bounds = _picture.CullRect;
        var destination = ComputeDestination(content, bounds.Width, bounds.Height, _sizeMode, ImageAlign);
        if (destination.Width <= 0f || destination.Height <= 0f)
        {
            return;
        }

        // Replay the picture at the destination's scale. The draw was recorded at identity with
        // the same sampling a direct image draw uses, so this paints exactly what the control
        // painted before, without a second copy of the pixels.
        canvas.Save();
        canvas.Translate(destination.Left, destination.Top);
        canvas.Scale(destination.Width / bounds.Width, destination.Height / bounds.Height);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(_picture);
        canvas.Restore();
    }

    private static SKRect ComputeDestination(SKRect content, float pictureWidth, float pictureHeight, XPlatSizeMode sizeMode, XPlatContentAlignment alignment)
    {
        if (pictureWidth <= 0f || pictureHeight <= 0f)
        {
            return SKRect.Empty;
        }

        var natural = new SKSize(pictureWidth, pictureHeight);

        return sizeMode switch
        {
            XPlatSizeMode.StretchImage => content,
            XPlatSizeMode.CenterImage => XPlatGeometry.Align(natural, content, alignment),
            XPlatSizeMode.Zoom => XPlatGeometry.Align(ScaleToFit(natural, content.Size), content, alignment),
            _ => XPlatGeometry.Align(natural, content, alignment),
        };
    }

    private static SKSize ScaleToFit(SKSize source, SKSize bounds)
    {
        if (source.Width <= 0f || source.Height <= 0f)
        {
            return SKSize.Empty;
        }

        float scale = MathF.Min(bounds.Width / source.Width, bounds.Height / source.Height);
        return new SKSize(source.Width * scale, source.Height * scale);
    }

    private static SKPicture Record(SKImage source)
    {
        using var recorder = new SKPictureRecorder();
        var bounds = new SKRect(0f, 0f, source.Width, source.Height);
        var canvas = recorder.BeginRecording(bounds);

        using var paint = new SKPaint
        {
            IsAntialias = true,
        };

        canvas.DrawImage(source, 0f, 0f, Sampling, paint);
        return recorder.EndRecording();
    }

    private void ReleasePicture()
    {
        if (_ownsPicture || DisposePictureWithControl)
        {
            _picture?.Dispose();
        }

        _picture = null;

        // Always ours: it exists only for the picture that was recorded from it.
        _pictureSource?.Dispose();
        _pictureSource = null;
        _ownsPicture = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleasePicture();
        }

        base.Dispose(disposing);
    }
}
