namespace XPlatUI.DragDrop;

/// <summary>
/// The drag and drop feature's half of the host's drag routing. The host works out which control is
/// under the pointer and keeps track of which one the drag is over; the router decides which
/// controls accept drops and delivers the four phases to them.
/// </summary>
/// <remarks>
/// <para>
/// The host holds this interface rather than the feature's implementation, which is what keeps drag
/// and drop out of an application that never opts in: the only implementation belongs to the
/// feature, and nothing the host retains names it until an opt-in registers a router.
/// </para>
/// <para>
/// A router is installed per window, because what it routes to is a control tree and a control tree
/// belongs to a window. <see cref="XPlatUiHost.DragDropRouter"/> is null until one is installed, and
/// a host with no router accepts nothing.
/// </para>
/// </remarks>
public interface IXPlatDragDropRouter
{
    /// <summary>True when <paramref name="control"/> accepts drops.</summary>
    /// <param name="control">A control under the pointer, or one of its ancestors.</param>
    bool IsDropTarget(XPlatControl control);

    /// <summary>Delivers the start of a drag to <paramref name="control"/>.</summary>
    /// <param name="control">The control the drag entered.</param>
    /// <param name="e">The event data; its <see cref="XPlatDragEventArgs.Effect"/> carries the answer.</param>
    /// <returns>The effect the control accepted.</returns>
    XPlatDragDropEffects DragEnter(XPlatControl control, XPlatDragEventArgs e);

    /// <summary>Delivers a drag move to <paramref name="control"/>.</summary>
    /// <param name="control">The control the drag is over.</param>
    /// <param name="e">The event data; its <see cref="XPlatDragEventArgs.Effect"/> carries the answer.</param>
    /// <returns>The effect the control accepted.</returns>
    XPlatDragDropEffects DragOver(XPlatControl control, XPlatDragEventArgs e);

    /// <summary>Tells <paramref name="control"/> that the drag left it or was cancelled.</summary>
    /// <param name="control">The control the drag left.</param>
    void DragLeave(XPlatControl control);

    /// <summary>Delivers the end of a drag to <paramref name="control"/>.</summary>
    /// <param name="control">The control the drop landed on.</param>
    /// <param name="e">The event data; its <see cref="XPlatDragEventArgs.Effect"/> carries the answer.</param>
    /// <returns>The effect the control performed.</returns>
    XPlatDragDropEffects DragDrop(XPlatControl control, XPlatDragEventArgs e);
}
