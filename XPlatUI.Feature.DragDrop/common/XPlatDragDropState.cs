using System.Runtime.CompilerServices;

namespace XPlatUI.DragDrop;

/// <summary>
/// The drag and drop state a control carries once the feature is in use: whether it accepts drops
/// and the handlers subscribed to its four phases.
/// </summary>
/// <remarks>
/// The state sits beside the control rather than in it, because the feature is an opt-in and the
/// base class must not carry its fields. A control that never touches the feature has no entry in
/// the table, and the table keys are weak, so a control's state goes away with the control.
/// </remarks>
internal sealed class XPlatDragDropState
{
    private static readonly ConditionalWeakTable<XPlatControl, XPlatDragDropState> States = new();

    /// <summary>True when the control accepts drops.</summary>
    public bool AllowDrop { get; set; }

    private Action<object, XPlatDragEventArgs>? DragEnter { get; set; }

    private Action<object, XPlatDragEventArgs>? DragOver { get; set; }

    private Action<object>? DragLeave { get; set; }

    private Action<object, XPlatDragEventArgs>? DragDrop { get; set; }

    /// <summary>Returns the state for <paramref name="control"/>, creating it on first use.</summary>
    /// <param name="control">The control the state belongs to.</param>
    public static XPlatDragDropState Get(XPlatControl control) =>
        States.GetValue(control, static _ => new XPlatDragDropState());

    /// <summary>Returns the state for <paramref name="control"/>, or null when it has none yet.</summary>
    /// <param name="control">The control to look up.</param>
    public static XPlatDragDropState? Find(XPlatControl control) =>
        States.TryGetValue(control, out var state) ? state : null;

    /// <summary>Runs the drag enter handlers, unless the control is disabled.</summary>
    /// <param name="control">The control the drag is over.</param>
    /// <param name="e">The event data.</param>
    public void RaiseDragEnter(XPlatControl control, XPlatDragEventArgs e)
    {
        if (control.Enabled)
        {
            DragEnter?.Invoke(control, e);
        }
    }

    /// <summary>Runs the drag over handlers, unless the control is disabled.</summary>
    /// <param name="control">The control the drag is over.</param>
    /// <param name="e">The event data.</param>
    public void RaiseDragOver(XPlatControl control, XPlatDragEventArgs e)
    {
        if (control.Enabled)
        {
            DragOver?.Invoke(control, e);
        }
    }

    /// <summary>Runs the drag leave handlers, unless the control is disabled.</summary>
    /// <param name="control">The control the drag left.</param>
    public void RaiseDragLeave(XPlatControl control)
    {
        if (control.Enabled)
        {
            DragLeave?.Invoke(control);
        }
    }

    /// <summary>Runs the drop handlers, unless the control is disabled.</summary>
    /// <param name="control">The control the drop landed on.</param>
    /// <param name="e">The event data.</param>
    public void RaiseDragDrop(XPlatControl control, XPlatDragEventArgs e)
    {
        if (control.Enabled)
        {
            DragDrop?.Invoke(control, e);
        }
    }

    /// <summary>Subscribes a drag enter handler.</summary>
    /// <param name="handler">The handler to add.</param>
    public void AddDragEnter(Action<object, XPlatDragEventArgs> handler) => DragEnter += handler;

    /// <summary>Removes a drag enter handler.</summary>
    /// <param name="handler">The handler to remove.</param>
    public void RemoveDragEnter(Action<object, XPlatDragEventArgs> handler) => DragEnter -= handler;

    /// <summary>Subscribes a drag over handler.</summary>
    /// <param name="handler">The handler to add.</param>
    public void AddDragOver(Action<object, XPlatDragEventArgs> handler) => DragOver += handler;

    /// <summary>Removes a drag over handler.</summary>
    /// <param name="handler">The handler to remove.</param>
    public void RemoveDragOver(Action<object, XPlatDragEventArgs> handler) => DragOver -= handler;

    /// <summary>Subscribes a drag leave handler.</summary>
    /// <param name="handler">The handler to add.</param>
    public void AddDragLeave(Action<object> handler) => DragLeave += handler;

    /// <summary>Removes a drag leave handler.</summary>
    /// <param name="handler">The handler to remove.</param>
    public void RemoveDragLeave(Action<object> handler) => DragLeave -= handler;

    /// <summary>Subscribes a drop handler.</summary>
    /// <param name="handler">The handler to add.</param>
    public void AddDragDrop(Action<object, XPlatDragEventArgs> handler) => DragDrop += handler;

    /// <summary>Removes a drop handler.</summary>
    /// <param name="handler">The handler to remove.</param>
    public void RemoveDragDrop(Action<object, XPlatDragEventArgs> handler) => DragDrop -= handler;
}
