namespace XPlatUI.DragDrop;

/// <summary>
/// Delivers drag routing to the controls that opted in: it reads
/// <c>AllowDrop</c> from the control's state and runs the control's own drag handling before the
/// handlers subscribed to it, which is the order the control's virtual methods used to give.
/// </summary>
/// <remarks>
/// Nothing in the library names this type. The feature's window hook creates one per window, so an
/// application that never opts in publishes without it.
/// </remarks>
internal sealed class XPlatDragDropRouter : IXPlatDragDropRouter
{
    /// <inheritdoc/>
    public bool IsDropTarget(XPlatControl control) =>
        XPlatDragDropState.Find(control)?.AllowDrop == true;

    /// <inheritdoc/>
    public XPlatDragDropEffects DragEnter(XPlatControl control, XPlatDragEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(e);

        XPlatDragDropState.Find(control)?.RaiseDragEnter(control, e);
        control.RaiseDragEnter(e);
        return e.Effect;
    }

    /// <inheritdoc/>
    public XPlatDragDropEffects DragOver(XPlatControl control, XPlatDragEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(e);

        XPlatDragDropState.Find(control)?.RaiseDragOver(control, e);
        control.RaiseDragOver(e);
        return e.Effect;
    }

    /// <inheritdoc/>
    public void DragLeave(XPlatControl control)
    {
        ArgumentNullException.ThrowIfNull(control);

        XPlatDragDropState.Find(control)?.RaiseDragLeave(control);
        control.RaiseDragLeave();
    }

    /// <inheritdoc/>
    public XPlatDragDropEffects DragDrop(XPlatControl control, XPlatDragEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(e);

        XPlatDragDropState.Find(control)?.RaiseDragDrop(control, e);
        control.RaiseDragDrop(e);
        return e.Effect;
    }
}
