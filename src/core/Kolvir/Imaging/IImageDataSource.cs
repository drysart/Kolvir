using SkiaSharp;

namespace Kolvir;

/// <summary>
/// The pictures behind an <see cref="ImageData"/> and the times at which the picture changes.
/// </summary>
/// <remarks>
/// <para>
/// A source is a timeline, not a format: it says how long one pass takes, what size the pictures are
/// and which picture belongs at a position in that pass. A still image is the degenerate case - a
/// duration of <see cref="TimeSpan.Zero"/>, one picture, and nothing to wait for - which is why the
/// control has no separate picture path and why a format that produces several frames needs no
/// change in the control at all.
/// </para>
/// <para>
/// A source owns what it hands out. <see cref="FrameAt"/> returns a picture the caller must not
/// dispose, because the next position may need it again and the source keeps it alive until the
/// source itself is disposed. That is also what lets a provider cache and share pictures: the
/// control disposes the source, never the frames.
/// </para>
/// <para>
/// A source is not a clock. It answers questions about a position and never measures time itself,
/// so the same source serves a playing animation, a paused one and a control that is not displayed,
/// and a test drives all three by moving a <see cref="TimeProvider"/>.
/// </para>
/// </remarks>
public interface IImageDataSource : IDisposable
{
    /// <summary>
    /// How long one pass through the pictures takes, or <see cref="TimeSpan.Zero"/> for a still
    /// image.
    /// </summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// The size the pictures are laid out at, in device-independent pixels.
    /// </summary>
    /// <remarks>
    /// Fixed for the source and never taken from a frame: this is what <see cref="Image"/>'s
    /// <see cref="Control.AutoSize"/> measures and what every frame is scaled to, so an
    /// animation whose frames differ by a pixel cannot make the layout move under the user.
    /// </remarks>
    SKSize NaturalSize { get; }

    /// <summary>
    /// What the format itself says about repeating: true when it asks to loop, false when it plays
    /// once.
    /// </summary>
    /// <remarks>
    /// This is only the default for <see cref="ImageData.Loop"/>. A caller that wants a
    /// one-shot animation to repeat, or a looping one to stop at the end, sets that property.
    /// </remarks>
    bool Loops { get; }

    /// <summary>The picture to show at a position within one pass.</summary>
    /// <param name="position">
    /// The position in the pass. It is already wrapped into <c>[0, Duration]</c> when the data loops
    /// and clamped to the same range when it does not, so a source never has to decide what a
    /// position past the end means.
    /// </param>
    /// <returns>The picture at that position, owned by the source.</returns>
    SKPicture FrameAt(TimeSpan position);

    /// <summary>
    /// How long from <paramref name="position"/> until the picture changes, or null when nothing
    /// will change.
    /// </summary>
    /// <param name="position">The position to ask about, in the same range as <see cref="FrameAt"/>.</param>
    /// <remarks>
    /// This is what keeps a four-frames-a-second animation waking four times a second rather than
    /// once per display frame, and it is null for a still image and for a position with no successor,
    /// which is how the control knows to stop its timer.
    /// </remarks>
    TimeSpan? TimeUntilNextFrame(TimeSpan position);
}
