namespace Kolvir;

/// <summary>
/// The rule a baseline-aligned row applies, wherever a container has one: given the boxes in a row
/// that declare a text baseline, the row's shared baseline is the lowest of them, every participant
/// moves down to meet it, and the row has to reach from that baseline down by each participant's own
/// descent.
/// </summary>
/// <remarks>
/// <para>
/// This is shared arithmetic rather than a shared arrangement: <see cref="StackPanel"/> gives each
/// child a slot of its own inside a run whose height its tallest child decides, and
/// <see cref="TableLayoutPanel"/> gives each cell its own box inside a row whose height the tracks
/// decide. Both are the same question - where does one line of text sit across a set of boxes - and
/// the two answers drifting apart would show up as one panel measuring short and clipping the other's
/// arrangement, so they ask it here.
/// </para>
/// <para>
/// The two passes are the point. <see cref="Include"/> needs only each participant's provisional top
/// and baseline, and <see cref="Reserve"/> then needs the finished <see cref="Reference"/>, so a
/// caller walks its participants twice: once to find the lowest baseline, once to find how tall the
/// row has to be. <see cref="Reserve"/> evaluates <c>Reference - baseline + height + below</c> in that
/// order, which is the expression this rule was written as before it was extracted; regrouping it
/// would be a different number in the last bit and a row that measures a hair short of what it
/// arranges.
/// </para>
/// <para>
/// Nothing here is public. A control opts in by reporting a baseline
/// (<see cref="Control.TryGetBaseline"/>) and a container opts in by having a row flag; the rule
/// between them is not a member an application calls.
/// </para>
/// </remarks>
internal struct BaselineRow
{
    private readonly float _box;
    private float _reference;
    private float _needed;

    /// <summary>Starts a row whose own box is <paramref name="box"/> tall.</summary>
    /// <param name="box">
    /// The height of the row's own box - the room the participants are being placed in, before any of
    /// them moves - which <see cref="NeededHeight"/> never reports less than. A container whose row
    /// height is exactly what this rule is working out passes zero.
    /// </param>
    public BaselineRow(float box)
    {
        _box = box;
        _reference = float.NegativeInfinity;
        _needed = box;
    }

    /// <summary>True when no participant has declared a baseline.</summary>
    /// <remarks>
    /// A caller uses this to leave the row exactly as it was: with nobody to line up against there is
    /// no shared baseline to move anything to, and <see cref="Reference"/> has no meaning.
    /// </remarks>
    public readonly bool IsEmpty => float.IsNegativeInfinity(_reference);

    /// <summary>The shared baseline, measured from the top of the row's box.</summary>
    /// <remarks>Only meaningful once at least one participant has been included: see <see cref="IsEmpty"/>.</remarks>
    public readonly float Reference => _reference;

    /// <summary>How far down a participant whose own baseline is <paramref name="baseline"/> moves.</summary>
    /// <param name="baseline">The baseline that participant reported, from the top of its own box.</param>
    /// <returns>
    /// The distance from where the participant was provisionally placed to where it belongs. Never
    /// negative: the shared baseline is the lowest one, so a participant can only move down.
    /// </returns>
    public readonly float ShiftFor(float baseline) => _reference - baseline;

    /// <summary>Records one participant's provisional place in the row, for the shared baseline.</summary>
    /// <param name="top">
    /// The top of the participant's box, measured from the top of the row's box - which already
    /// includes its margin and whatever alignment put it there.
    /// </param>
    /// <param name="baseline">Its baseline, measured from the top of its own box.</param>
    public void Include(float top, float baseline) =>
        _reference = MathF.Max(_reference, top + baseline);

    /// <summary>Records what one participant needs below the shared baseline.</summary>
    /// <param name="baseline">The baseline it reported to <see cref="Include"/>.</param>
    /// <param name="height">The height of its box.</param>
    /// <param name="below">The room its own box leaves below itself, its bottom margin for a stack.</param>
    /// <remarks>
    /// Call this only after every participant has been included: it reads <see cref="Reference"/>.
    /// </remarks>
    public void Reserve(float baseline, float height, float below) =>
        _needed = MathF.Max(_needed, _reference - baseline + height + below);

    /// <summary>The height the row needs so that every participant fits after it moves.</summary>
    /// <remarks>
    /// Never less than the height the row's own box started with, so a container that measured its
    /// row for other reasons - a taller child that reports no baseline, a fixed height - is not
    /// shrunk by this rule. It is the largest of the participants' own descents: the distance from
    /// the shared baseline down to each box's bottom edge plus the room below it.
    /// </remarks>
    public readonly float NeededHeight => _needed;
}
