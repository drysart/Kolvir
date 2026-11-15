using SkiaSharp;

namespace Kolvir;

/// <summary>
/// Decides where a scrollable panel stays scrolled when its content is arranged again.
/// </summary>
/// <remarks>
/// <para>
/// A scrolling panel arranges its content in content coordinates and paints it through an offset,
/// so an arrange that changes how tall the content is moves everything the offset was pointing at.
/// With no anchor the panel keeps its offset and clamps it into the new range, which keeps the
/// distance from the top. An anchor changes that: the panel captures a little state before it
/// arranges and restores the offset with it afterwards.
/// </para>
/// <para>
/// <see cref="Capture"/> runs once, before the content is arranged, so what it reads is the state
/// the user was looking at. <see cref="Restore"/> runs once, after the content has been arranged
/// and the new extent is known, and before the offset is clamped to the new range.
/// </para>
/// <para>
/// Restore may only change the offset. Moving the content again would need another arrange pass,
/// and the scroll helper drops one that re-enters it while it is laying out, so a restore that
/// changes a child's size or position appears to do nothing.
/// </para>
/// </remarks>
public abstract class ScrollAnchor
{
    /// <summary>How close to the end of its range a panel has to be to count as scrolled to it.</summary>
    protected const float Epsilon = 0.5f;

    /// <summary>How far the panel can scroll vertically: its content less what it shows.</summary>
    protected static float VerticalRange(Panel panel) =>
        MathF.Max(0f, panel.ScrollExtent.Height - panel.ClientSize.Height);

    /// <summary>How far the panel can scroll horizontally: its content less what it shows.</summary>
    protected static float HorizontalRange(Panel panel) =>
        MathF.Max(0f, panel.ScrollExtent.Width - panel.ClientSize.Width);

    /// <summary>
    /// Records what should be held still. Called before the content is arranged, once per layout.
    /// </summary>
    /// <param name="panel">The panel that is about to arrange its content.</param>
    /// <returns>Whatever <see cref="Restore"/> needs; passed back to it unchanged.</returns>
    public abstract object? Capture(Panel panel);

    /// <summary>
    /// Puts the offset back where the captured state says it belongs. May only scroll the panel.
    /// </summary>
    /// <param name="panel">The panel whose content has just been arranged.</param>
    /// <param name="state">What <see cref="Capture"/> returned for this layout.</param>
    public abstract void Restore(Panel panel, object? state);
}

/// <summary>
/// Holds a panel that is scrolled to its end at its end, so content that gets taller because the
/// panel got narrower stays pinned to the bottom. A panel that was scrolled anywhere else keeps the
/// ordinary distance-from-the-top behaviour.
/// </summary>
/// <remarks>
/// A panel that has never been laid out counts as being at its end: it has no range to be short
/// of, and a stream that starts at the bottom is what a list of messages wants.
/// </remarks>
public sealed class BottomScrollAnchor : ScrollAnchor
{
    /// <inheritdoc />
    public override object? Capture(Panel panel) =>
        panel.VerticalOffset >= VerticalRange(panel) - Epsilon;

    /// <inheritdoc />
    public override void Restore(Panel panel, object? state)
    {
        if (state is true)
        {
            panel.ScrollTo(panel.HorizontalOffset, VerticalRange(panel));
        }
    }
}

/// <summary>
/// Holds one control still: whatever it was scrolled to, it stays that far down the viewport
/// after the content has been arranged again.
/// </summary>
/// <remarks>
/// <para>
/// The picker runs before the content is arranged, so it has to choose from what is on screen.
/// Walking a list of children and taking the first whose bottom is past
/// <see cref="Panel.VerticalOffset"/> is the usual rule for a stream of rows; the anchor then
/// remembers how far into the viewport that row's top sat.
/// </para>
/// <para>
/// When the picker chooses nothing, or the control it chose is no longer a descendant of the
/// panel, the offset is left to the ordinary clamp rather than guessed at.
/// </para>
/// <para>
/// The control is positioned in the panel's own (zoomed) space and the offset is in content
/// space, so a panel with a <see cref="Panel.ScaleFactor"/> other than one divides the difference.
/// That is exact for a control in the panel's direct subtree; a control inside a second zoomed
/// panel below it is measured in that panel's zoom as well.
/// </para>
/// </remarks>
public sealed class ControlScrollAnchor : ScrollAnchor
{
    private readonly Func<Panel, Control?> _pick;

    /// <summary>Creates an anchor that holds whatever <paramref name="pick"/> chooses.</summary>
    /// <param name="pick">
    /// Chooses the control to hold still, given the panel about to arrange. Called once per layout.
    /// </param>
    public ControlScrollAnchor(Func<Panel, Control?> pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        _pick = pick;
    }

    /// <inheritdoc />
    public override object? Capture(Panel panel)
    {
        var control = _pick(panel);

        return control is null || !IsUnder(panel, control)
            ? null
            : new Anchor(control, TopOf(panel, control));
    }

    /// <inheritdoc />
    public override void Restore(Panel panel, object? state)
    {
        if (state is not Anchor anchor || !IsUnder(panel, anchor.Control))
        {
            return;
        }

        // The control was arranged against the same offset the capture saw, so how far it moved on
        // screen is how far the offset has to move to put it back where it was.
        var moved = TopOf(panel, anchor.Control) - anchor.Top;

        if (moved != 0f)
        {
            panel.ScrollTo(panel.HorizontalOffset, panel.VerticalOffset + moved);
        }
    }

    /// <summary>Where the control sits in the panel's viewport, in content space.</summary>
    private static float TopOf(Panel panel, Control control)
    {
        var top = control.GetPositionIn(panel).Y;
        return panel.ScaleFactor == 1f ? top : top / panel.ScaleFactor;
    }

    private static bool IsUnder(Panel panel, Control control)
    {
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            if (ReferenceEquals(parent, panel))
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct Anchor(Control Control, float Top);
}
