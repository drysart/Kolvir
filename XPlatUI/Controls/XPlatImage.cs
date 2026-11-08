using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// Draws an <see cref="SKPicture"/>. The control holds a picture rather than an image, so what it
/// shows is always something a canvas can replay.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Load(Stream)"/> asks the registered <see cref="IXPlatImageFormatProvider"/>s what the
/// bytes are and takes the picture the winner produces. By default that is the built-in raster
/// decoder, which records one draw of the decoded bitmap into a picture at identity, so the control
/// reads PNG, JPEG, WebP, GIF, BMP and ICO with nothing installed. A feature package registers a
/// provider for anything else - SVG, say - and the same <c>Load</c> call then reads that too; see
/// <see cref="XPlatImageFormats"/> for the rules a load follows when several providers claim the
/// same bytes.
/// </para>
/// <para>
/// A picture that was not decoded from a stream is set directly through <see cref="Picture"/>, which
/// is also what a caller does when it wants a picture built its own way.
/// </para>
/// <para>
/// The control owns the picture when it was loaded through one of the <c>Load</c> helpers or when
/// <see cref="DisposePictureWithControl"/> is set.
/// </para>
/// </remarks>
public class XPlatImage : XPlatControl
{
    private Action<object, Exception>? _loadFailed;
    private SKPicture? _picture;
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
    /// <remarks>
    /// The file extension is passed to the providers as a hint, so a format whose content is hard to
    /// identify from its first bytes can still be recognised; it is never the deciding factor, since
    /// a stream has no name and the same provider has to work there too.
    /// </remarks>
    public bool Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Load(stream, Path.GetExtension(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _loadFailed?.Invoke(this, ex);
            return false;
        }
    }

    /// <summary>Loads an image from a stream, replacing the current one.</summary>
    /// <remarks>
    /// A stream is identified by its bytes, because it has no name. A stream that no provider
    /// recognises returns false and raises nothing; a stream that a provider recognised and then
    /// could not decode returns false and raises <see cref="AddLoadFailedHandler"/> with an
    /// <see cref="XPlatImageFormatException"/>. A provider that throws is that second case rather
    /// than an exception out of this method, so a hostile document cannot take the paint thread down
    /// with it.
    /// </remarks>
    public bool Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Load(stream, extensionHint: null);
    }

    private bool Load(Stream stream, string? extensionHint)
    {
        // A provider is promised a seekable stream, because it has to look at the start twice: once
        // to be recognised and once to be decoded. A pipe or a network stream is buffered here so
        // that promise holds for every provider rather than for the careful ones.
        Stream source = stream;
        MemoryStream? buffered = null;
        SKPicture? picture = null;

        try
        {
            if (!stream.CanSeek)
            {
                buffered = new MemoryStream();
                stream.CopyTo(buffered);
                buffered.Position = 0;
                source = buffered;
            }

            string? extension = string.IsNullOrEmpty(extensionHint)
                ? null
                : extensionHint.TrimStart('.').ToLowerInvariant();

            var provider = XPlatImageFormats.Find(ReadHeader(source));
            if (provider is null)
            {
                return false;
            }

            try
            {
                source.Position = 0;
                picture = provider.Decode(source, extension);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _loadFailed?.Invoke(this, new XPlatImageFormatException(provider.Name, ex));
                return false;
            }

            if (picture is null)
            {
                _loadFailed?.Invoke(this, new XPlatImageFormatException(provider.Name));
                return false;
            }
        }
        finally
        {
            buffered?.Dispose();
        }

        ReleasePicture();
        _picture = picture;
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

    /// <summary>Reads the bytes a provider is shown, leaving the stream where it was.</summary>
    private static ReadOnlySpan<byte> ReadHeader(Stream stream)
    {
        long origin = stream.Position;
        var header = new byte[XPlatImageFormats.HeaderLength];
        int read = 0;

        try
        {
            while (read < header.Length)
            {
                int count = stream.Read(header, read, header.Length - read);
                if (count == 0)
                {
                    break;
                }

                read += count;
            }
        }
        finally
        {
            stream.Position = origin;
        }

        return read == header.Length ? header : header.AsSpan(0, read);
    }

    private void ReleasePicture()
    {
        if (_ownsPicture || DisposePictureWithControl)
        {
            _picture?.Dispose();
        }

        _picture = null;
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
