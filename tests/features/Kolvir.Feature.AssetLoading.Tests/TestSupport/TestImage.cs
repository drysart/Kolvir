using SkiaSharp;

namespace Kolvir.Feature.AssetLoading.Tests;

/// <summary>
/// An <see cref="IImageDataSource"/> whose only job is to say whether it was disposed, so a test can
/// tell whether the feature released a loaded <see cref="ImageData"/>.
/// </summary>
/// <remarks>
/// It has no timeline: one picture, no duration and nothing to wait for, which is the still-image case
/// <see cref="ImageData"/> is the degenerate case of. The picture is shared and deliberately not
/// disposed, because a test asserts on the source rather than on the bytes.
/// </remarks>
internal sealed class TestImageDataSource : IImageDataSource
{
    private static readonly SKPicture Picture = new SKPictureRecorder().EndRecording();

    /// <summary>True once <see cref="Dispose"/> has run.</summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc/>
    public TimeSpan Duration => TimeSpan.Zero;

    /// <inheritdoc/>
    public SKSize NaturalSize => new(10f, 10f);

    /// <inheritdoc/>
    public bool Loops => false;

    /// <inheritdoc/>
    public SKPicture FrameAt(TimeSpan position) => Picture;

    /// <inheritdoc/>
    public TimeSpan? TimeUntilNextFrame(TimeSpan position) => null;

    /// <inheritdoc/>
    public void Dispose() => IsDisposed = true;
}

/// <summary>One image the feature may show, and the source behind it, so a test can watch both.</summary>
internal sealed class TestImage
{
    private readonly TestImageDataSource _source = new();

    /// <summary>Creates an <see cref="ImageData"/> the feature may own.</summary>
    public TestImage() => Data = new ImageData(_source);

    /// <summary>The data a loader hands back, or a placeholder a test borrows.</summary>
    public ImageData Data { get; }

    /// <summary>True once the data behind <see cref="Data"/> has been disposed.</summary>
    public bool IsDisposed => _source.IsDisposed;
}
