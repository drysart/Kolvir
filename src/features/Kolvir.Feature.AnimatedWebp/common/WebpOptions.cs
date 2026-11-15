namespace Kolvir;

/// <summary>
/// What an animated WebP load can be told. Both settings are decided while the file is decoded, which
/// is why they are load-time options rather than properties on the data.
/// </summary>
/// <remarks>
/// <para>
/// Everything that can change at run time - whether the animation loops, where it is, whether it is
/// playing - lives on <see cref="ImageData"/>, not here: an option that a loaded animation could
/// change afterwards would be in the wrong place.
/// </para>
/// <para>
/// The two numbers deliberately match <c>GifOptions</c>, including the 100 ms zero-duration
/// minimum. An animated WebP and a GIF authored for the same page should not play at different speeds
/// because they went through different packages, and matching the number is cheaper than an API that
/// reaches across packages to share it.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var data = new ImageDataFactory([WebpImageFormatProvider.Shared]).LoadFromPath(
///     "spinner.webp",
///     new WebpOptions { DecodedSizeLimit = 32L * 1024 * 1024 });
/// </code>
/// </example>
public sealed class WebpOptions : IImageFormatOptions
{
    private long _decodedSizeLimit = 256L * 1024 * 1024;
    private TimeSpan _zeroDelayMinimum = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// How much decoded pixel memory the animation may use, in bytes: 256 MiB by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every frame is decoded up front, because seeking to any position is part of what the timeline
    /// promises and a lazy window cannot honour a seek backwards. That makes the cost predictable -
    /// frames x canvas width x canvas height x 4 bytes - and this is the bound that stops a small file
    /// from being a large one once it is decoded: a 2000x2000 WebP with 60 frames is 960 MB of pixels,
    /// which is more than most applications would hand over for a picture.
    /// </para>
    /// <para>
    /// A file over the limit is refused rather than partially decoded, so a load either produces a
    /// complete animation with every frame available or fails with a reason.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long DecodedSizeLimit
    {
        get => _decodedSizeLimit;
        set => _decodedSizeLimit = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "The limit must be positive.");
    }

    /// <summary>
    /// What a frame with a duration of zero is played as: 100 ms by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A zero duration is legal and means "as fast as possible", which taken literally would ask the
    /// timeline for the next frame immediately - a timer that fires at the millisecond floor for as
    /// long as the animation is visible, which is a busy loop in a control doing nothing useful. 100 ms
    /// is the minimum the GIF feature already uses and the one browsers settled on for a zero-delay
    /// frame, so an animation authored for the web already looks the way this plays it.
    /// </para>
    /// <para>
    /// Only an exact zero is replaced. A frame that declares 10 ms is played at 10 ms, because that
    /// is a duration the author chose.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public TimeSpan ZeroDelayMinimum
    {
        get => _zeroDelayMinimum;
        set => _zeroDelayMinimum = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "The minimum must be positive.");
    }
}
