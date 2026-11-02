namespace XPlatUI.DragDrop;

/// <summary>
/// Runs the drag handlers an application subscribed to a control: it reads <c>AllowDrop</c> from the
/// control's feature state and invokes the handlers the <c>AddDrag...Handler</c> members registered.
/// </summary>
/// <remarks>
/// <para>
/// The control's own <c>OnDrag...</c> handling is not run here. That is the library's to call, and the
/// host calls it straight after this returns, so a handler subscribed by the application sets the
/// effect the control's own handling then sees - the order the events gave when they lived on
/// <see cref="XPlatControl"/>.
/// </para>
/// <para>
/// Nothing in the library names this type. The feature's window hook creates one per window, so an
/// application that never opts in publishes without it.
/// </para>
/// </remarks>
internal sealed class XPlatDragDropRouter : IXPlatDragDropRouter
{
    /// <inheritdoc/>
    public bool IsDropTarget(XPlatControl control) =>
        XPlatDragDropState.Find(control)?.AllowDrop == true;

    /// <inheritdoc/>
    public void DragEnter(XPlatControl control, XPlatDragEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(e);

        XPlatDragDropState.Find(control)?.RaiseDragEnter(control, e);
    }

    /// <inheritdoc/>
    public void DragOver(XPlatControl control, XPlatDragEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(e);

        XPlatDragDropState.Find(control)?.RaiseDragOver(control, e);
    }

    /// <inheritdoc/>
    public void DragLeave(XPlatControl control)
    {
        ArgumentNullException.ThrowIfNull(control);

        XPlatDragDropState.Find(control)?.RaiseDragLeave(control);
    }

    /// <inheritdoc/>
    public void DragDrop(XPlatControl control, XPlatDragEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(e);

        XPlatDragDropState.Find(control)?.RaiseDragDrop(control, e);
    }
}
