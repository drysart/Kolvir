using SkiaSharp;

namespace Kolvir;

/// <summary>What a popup is placed against.</summary>
public enum PopupPlacement
{
    /// <summary>
    /// Whatever the call supplied: <see cref="Anchor"/> when it named an anchor control, and
    /// <see cref="Pointer"/> when it did not. The default.
    /// </summary>
    Auto = 0,

    /// <summary>Against the anchor control the popup was opened from.</summary>
    Anchor = 1,

    /// <summary>At a point in the owning host's client coordinates.</summary>
    ClientPoint = 2,

    /// <summary>At the pointer, where it was the last time it moved over the owning host.</summary>
    Pointer = 3,
}

/// <summary>Where a popup sits against what it is placed on.</summary>
public enum PopupAlign
{
    /// <summary>The popup's top-left corner under the anchor's bottom-left corner.</summary>
    BelowStart = 0,

    /// <summary>The popup centred under the anchor.</summary>
    BelowCenter = 1,

    /// <summary>The popup's top-right corner under the anchor's bottom-right corner.</summary>
    BelowEnd = 2,

    /// <summary>The popup's bottom-left corner over the anchor's top-left corner.</summary>
    AboveStart = 3,

    /// <summary>The popup centred over the anchor.</summary>
    AboveCenter = 4,

    /// <summary>The popup's bottom-right corner over the anchor's top-right corner.</summary>
    AboveEnd = 5,

    /// <summary>The popup beside the anchor, its left edge at the anchor's right edge.</summary>
    RightOf = 6,

    /// <summary>The popup beside the anchor, its right edge at the anchor's left edge.</summary>
    LeftOf = 7,

    /// <summary>The popup at the pointer rather than against an anchor.</summary>
    AtPointer = 8,
}

/// <summary>What a popup opened through <c>ShowPopupAsync</c> is asked to do.</summary>
/// <remarks>
/// <para>
/// The default is what a drop-down list or a small dialog wants: under the anchor it was opened
/// from, dismissed by a press outside it, and closed by Escape. With no anchor to hang from, the
/// default is at the pointer, so the simplest overload of the API still opens where the user is
/// looking.
/// </para>
/// <para>
/// <see cref="Align"/> is a request. The popup is placed where it asks, but the display wins:
/// when <see cref="FlipIntoView"/> is set and the popup does not fit, it opens on the opposite side
/// of the anchor, and it is then clamped so it is fully visible whenever the placement area can
/// hold it. See <see cref="IPopupService.GetPlacementArea"/> for the area that is used.
/// </para>
/// </remarks>
public sealed class PopupOptions
{
    /// <summary>
    /// What the popup is placed against. Defaults to <see cref="PopupPlacement.Auto"/>, which is
    /// <see cref="PopupPlacement.Anchor"/> when the call supplied an anchor control and
    /// <see cref="PopupPlacement.Pointer"/> when it did not.
    /// </summary>
    public PopupPlacement Placement { get; set; } = PopupPlacement.Auto;

    /// <summary>
    /// Where the popup hangs from what it is placed on. Defaults to
    /// <see cref="PopupAlign.BelowStart"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For <see cref="PopupPlacement.ClientPoint"/> and <see cref="PopupPlacement.Pointer"/> the
    /// point takes the anchor's place, and is read as a zero-sized anchor: <c>BelowStart</c> puts
    /// the popup's top-left corner on the point, <c>BelowCenter</c> centres it horizontally on the
    /// point, and <c>Above*</c> puts its bottom edge on it.
    /// </para>
    /// <para>
    /// <see cref="PopupAlign.AtPointer"/> places the popup's top-left corner at the pointer, which
    /// is what a context menu opened from an anchor wants. A pointer that has never been over the
    /// owner surface falls back to the anchor's top-left corner.
    /// </para>
    /// </remarks>
    public PopupAlign Align { get; set; } = PopupAlign.BelowStart;

    /// <summary>
    /// A nudge applied after the alignment, in logical pixels in the owning host's coordinates.
    /// </summary>
    /// <remarks>
    /// For <see cref="PopupPlacement.ClientPoint"/> this value is the client point itself, as it is
    /// the only place a client-point popup reads its position from. For
    /// <see cref="PopupPlacement.Anchor"/> and <see cref="PopupPlacement.Pointer"/> it is a nudge
    /// from the place the alignment chose.
    /// </remarks>
    public SKPoint Offset { get; set; }

    /// <summary>
    /// The size the popup takes, in its content's coordinate space, or null to measure its content
    /// for each show.
    /// </summary>
    public SKSize? Size { get; set; }

    /// <summary>
    /// The largest the popup may measure its content, in the content's coordinate space, or null for
    /// no limit.
    /// </summary>
    /// <remarks>
    /// Null rather than <see cref="SKSize.Empty"/> on purpose: a defaulted <see cref="SKSize"/> would
    /// read as "zero sized" where the popup itself means "as large as the content needs".
    /// </remarks>
    public SKSize? MaximumSize { get; set; }

    /// <summary>Whether Escape closes the popup. Defaults to true.</summary>
    public bool EscapeCloses { get; set; } = true;

    /// <summary>
    /// Whether a press outside the popup closes it. Defaults to true, which is what a menu wants:
    /// the press belongs to the popup and is not delivered to whatever it landed on. False is a
    /// tooltip, which lets the press through.
    /// </summary>
    public bool DismissOnOutsideClick { get; set; } = true;

    /// <summary>
    /// Whether the popup flips to the other side of its anchor when it does not fit where it was
    /// asked to go. Defaults to true.
    /// </summary>
    /// <remarks>
    /// A <c>Below*</c> request flips to the matching <c>Above*</c>, <c>RightOf</c> flips to
    /// <c>LeftOf</c> and back. False disables flipping and nothing else: the popup still moves
    /// along the requested alignment when that keeps it inside the placement area.
    /// </remarks>
    public bool FlipIntoView { get; set; } = true;
}
