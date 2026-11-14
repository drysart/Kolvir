using SkiaSharp;

namespace Kolvir;

/// <summary>
/// The frame drawn around a control: how much of it the frame takes, where it clips, and what it
/// paints.
/// </summary>
/// <remarks>
/// <para>
/// <b>A border is immutable.</b> Its properties are read-only and a change is expressed by assigning a
/// different instance to <see cref="Control.Border"/>, the way <see cref="Thickness"/> and
/// <see cref="Background"/> are used. That is what lets one instance be shared by every control of a
/// kind - a control class keeps its frame in a <c>private static readonly</c> field and assigns it in
/// its constructor - and it is why a border has no change notification to get wrong.
/// </para>
/// <para>
/// <b>Insets and clipping are separate answers, and may disagree.</b> A nine-slice border insets
/// without clipping, a rounded border clips its corners without insetting them, and a shadow border
/// would clip square and paint round. Collapsing them into one number would be wrong for all three.
/// </para>
/// <para>
/// <b>A border paints inside the control's bounds.</b> The paint pass has already clipped to them, and
/// the subtree skip in <see cref="Control"/> relies on nothing escaping, so an outward stroke, a
/// drop shadow or a focus ring drawn outside the control is not expressible - the frame of a control is
/// its own.
/// </para>
/// <para>
/// <b><see cref="Insets"/> and <see cref="Paint"/> are given the control; the rest are not.</b> A
/// border needs the control to colour itself by state, and a border that owns a band of the control -
/// a group box caption, whose height is the control's font - needs it to know how much to take. The
/// other members describe the kind of shape rather than its measurements.
/// </para>
/// </remarks>
public abstract class Border
{
    /// <summary>No border: takes nothing, clips nothing, paints nothing.</summary>
    /// <remarks>
    /// A singleton, so the absence allocates nothing and compares by reference. It is the initial value
    /// of <see cref="Control.Border"/>, and assigning it is how an application takes away a frame a
    /// control class gave itself.
    /// </remarks>
    public static Border None { get; } = new NoneBorder();

    /// <summary>
    /// How much of the control this border takes for itself, on each edge.
    /// </summary>
    /// <param name="owner">The control the border is attached to.</param>
    /// <returns>
    /// The non-client area, in logical pixels. It comes out of the control's bounds before its
    /// padding, and <see cref="Control.Measure"/> adds it back, so a border makes a control larger
    /// rather than shrinking its content.
    /// </returns>
    /// <remarks>
    /// It may depend on the control - a caption band is measured from
    /// <see cref="Control.Font"/> - but it must not depend on the control's pointer or focus state:
    /// this is asked during layout, so an inset that followed the pointer would turn every hover into
    /// a layout pass.
    /// </remarks>
    public virtual Thickness Insets(Control owner) => Thickness.Empty;

    /// <summary>
    /// Whether this border clips the control to something narrower than its own bounds.
    /// </summary>
    /// <remarks>
    /// True for a rounded border, whose corners let whatever is behind the control show through. When
    /// it is true the clip is applied by <see cref="ClipTo"/> and the control stops reporting itself
    /// as <see cref="Control.Opaque"/>.
    /// </remarks>
    public virtual bool NarrowsClip => false;

    /// <summary>
    /// Narrows the canvas's clip to this border's shape. Only called when <see cref="NarrowsClip"/> is
    /// true.
    /// </summary>
    /// <param name="canvas">The canvas, already translated to the control's origin.</param>
    /// <param name="bounds">The control's bounds in its own coordinates.</param>
    public virtual void ClipTo(SKCanvas canvas, SKRect bounds)
    {
    }

    /// <summary>
    /// Whether every pixel inside the control's bounds is still painted with the control's own
    /// background, so nothing behind it can show through.
    /// </summary>
    /// <remarks>
    /// It is the border's share of <see cref="Control.Opaque"/>, and it is what a control's repaint
    /// cost depends on: a border that claims this and then clips its corners stops the control from
    /// repainting what is behind them.
    /// </remarks>
    public virtual bool IsRectangular => true;

    /// <summary>
    /// The corner radius a control should round its own background fill by, so the fill follows the
    /// same shape as the frame rather than a rectangle that the clip has to cut.
    /// </summary>
    public virtual float CornerRadius => 0f;

    /// <summary>
    /// Paints the border, inside <paramref name="bounds"/>.
    /// </summary>
    /// <param name="canvas">The canvas, already translated to the control's origin and clipped.</param>
    /// <param name="bounds">The control's bounds in its own coordinates.</param>
    /// <param name="owner">The control the border is attached to, whose state decides the colour.</param>
    public abstract void Paint(SKCanvas canvas, SKRect bounds, Control owner);

    private sealed class NoneBorder : Border
    {
        public override void Paint(SKCanvas canvas, SKRect bounds, Control owner)
        {
        }
    }
}
