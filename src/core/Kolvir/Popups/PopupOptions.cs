using SkiaSharp;

namespace Kolvir;

/// <summary>What a popup is placed against.</summary>
public enum PopupPlacement
{
    /// <summary>Against the anchor control the popup was opened from.</summary>
    Anchor = 0,

    /// <summary>At a point in the owning host's client coordinates.</summary>
    ClientPoint = 1,

    /// <summary>At the pointer, where it was the last time it moved over the owning host.</summary>
    Pointer = 2,
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
/// from, dismissed by a press outside it, and closed by Escape.
/// </para>
/// <para>
/// The geometry this version applies is deliberately simple: an anchor popup opens at the anchor's
/// bottom-left corner plus <see cref="Offset"/>, a client-point popup at that point, and a pointer
/// popup at the pointer. <see cref="Align"/> and <see cref="FlipIntoView"/> are requests that the
/// placement step reads; neither moves a popup here.
/// </para>
/// </remarks>
public sealed class PopupOptions
{
    /// <summary>What the popup is placed against. Defaults to <see cref="PopupPlacement.Anchor"/>.</summary>
    public PopupPlacement Placement { get; set; } = PopupPlacement.Anchor;

    /// <summary>
    /// Where the popup hangs from what it is placed on. Defaults to
    /// <see cref="PopupAlign.BelowStart"/>.
    /// </summary>
    public PopupAlign Align { get; set; } = PopupAlign.BelowStart;

    /// <summary>
    /// A nudge in logical pixels from the placed position: down and right for an anchor, or the
    /// client point itself for <see cref="PopupPlacement.ClientPoint"/>.
    /// </summary>
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
    public bool FlipIntoView { get; set; } = true;
}
