namespace XPlatUI.DragDrop;

/// <summary>
/// The drag and drop feature's half of the host's drag routing. The host works out which control is
/// under the pointer, keeps track of which one the drag is over, and runs the control's own drag
/// handling; the router decides which controls accept drops and runs the handlers subscribed to
/// them.
/// </summary>
/// <remarks>
/// <para>
/// The contract is in the library and the only implementation is in <c>XPlatUI.Feature.DragDrop</c>,
/// which is what keeps drag and drop out of an application that never opts in: nothing the host
/// retains names the implementation, and a host with no router accepts nothing.
/// </para>
/// <para>
/// A router is installed per window, because what it routes to is a control tree and a control tree
/// belongs to a window. <see cref="XPlatUiHost.DragDropRouter"/> is null until one is installed.
/// </para>
/// <para>
/// Every member here runs before the host raises the matching <c>OnDrag...</c> method on the
/// control, so a handler subscribed with <c>AddDragEnterHandler</c> sets the effect the control's own
/// handling then sees. That is the order the control's virtual methods gave when the events lived on
/// <see cref="XPlatControl"/>, and it is why the host, not the router, calls the virtual: a derived
/// control's handling stays the library's to run.
/// </para>
/// </remarks>
public interface IXPlatDragDropRouter
{
    /// <summary>True when <paramref name="control"/> accepts drops.</summary>
    /// <param name="control">A control under the pointer, or one of its ancestors.</param>
    bool IsDropTarget(XPlatControl control);

    /// <summary>Runs the drag enter handlers subscribed to <paramref name="control"/>.</summary>
    /// <param name="control">The control the drag entered.</param>
    /// <param name="e">The event data; its <see cref="XPlatDragEventArgs.Effect"/> carries the answer.</param>
    void DragEnter(XPlatControl control, XPlatDragEventArgs e);

    /// <summary>Runs the drag over handlers subscribed to <paramref name="control"/>.</summary>
    /// <param name="control">The control the drag is over.</param>
    /// <param name="e">The event data; its <see cref="XPlatDragEventArgs.Effect"/> carries the answer.</param>
    void DragOver(XPlatControl control, XPlatDragEventArgs e);

    /// <summary>Runs the drag leave handlers subscribed to <paramref name="control"/>.</summary>
    /// <param name="control">The control the drag left.</param>
    void DragLeave(XPlatControl control);

    /// <summary>Runs the drop handlers subscribed to <paramref name="control"/>.</summary>
    /// <param name="control">The control the drop landed on.</param>
    /// <param name="e">The event data; its <see cref="XPlatDragEventArgs.Effect"/> carries the answer.</param>
    void DragDrop(XPlatControl control, XPlatDragEventArgs e);
}
