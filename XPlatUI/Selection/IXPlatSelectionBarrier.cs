namespace XPlatUI;

/// <summary>
/// Marks a control that stands between a selectable control and the pointer: a press it receives
/// belongs to it alone and never starts a selection of whatever selectable control contains it.
/// </summary>
/// <remarks>
/// A selection normally walks up from the control under the pointer to the nearest selectable one,
/// so pressing a link inside a selectable label selects the label. Chrome that a selectable control
/// places in its own content - the scrollbars a text box or a wrapped panel owns - is the exception:
/// dragging its thumb scrolls, and must not drag a selection across the text underneath it.
/// </remarks>
internal interface IXPlatSelectionBarrier
{
}
