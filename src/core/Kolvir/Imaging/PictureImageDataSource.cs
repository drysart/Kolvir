using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A source for one picture: the still case, where the animation has a duration of zero.
/// </summary>
/// <remarks>
/// <para>
/// This is what a format provider returns when its format has no timeline - an SVG document, a
/// decoded PNG - and what <see cref="ImageData.FromPicture"/> builds. There is one picture and
/// no waiting: <see cref="TimeUntilNextFrame"/> is always null, so a control holding this source
/// never starts a timer.
/// </para>
/// <para>
/// The picture is disposed with the source only when it was handed over: a picture a caller still
/// draws belongs to the caller, exactly as a picture set on the control did before the control had
/// a timeline.
/// </para>
/// </remarks>
public sealed class PictureImageDataSource : IImageDataSource
{
    private readonly SKPicture _picture;
    private readonly bool _disposePicture;
    private bool _disposed;

    /// <summary>Wraps a picture as a still source.</summary>
    /// <param name="picture">The picture to show.</param>
    /// <param name="disposePicture">
    /// True to dispose the picture with this source. A provider that recorded the picture for this
    /// load passes true; a caller sharing a picture between controls leaves it false.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="picture"/> is null.</exception>
    public PictureImageDataSource(SKPicture picture, bool disposePicture = false)
    {
        ArgumentNullException.ThrowIfNull(picture);

        _picture = picture;
        _disposePicture = disposePicture;

        var bounds = picture.CullRect;
        NaturalSize = new SKSize(bounds.Width, bounds.Height);
    }

    /// <summary>A still image has no timeline: this is always <see cref="TimeSpan.Zero"/>.</summary>
    public TimeSpan Duration => TimeSpan.Zero;

    /// <summary>The picture's recorded bounds, which are the size it draws at.</summary>
    public SKSize NaturalSize { get; }

    /// <summary>A still image has nothing to repeat, so this is always false.</summary>
    public bool Loops => false;

    /// <summary>The one picture, whatever the position.</summary>
    /// <param name="position">Ignored: a still image looks the same everywhere.</param>
    public SKPicture FrameAt(TimeSpan position) => _picture;

    /// <summary>A still image never changes, so there is nothing to wait for.</summary>
    /// <param name="position">Ignored.</param>
    public TimeSpan? TimeUntilNextFrame(TimeSpan position) => null;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_disposePicture)
        {
            _picture.Dispose();
        }
    }
}
