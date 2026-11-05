using System.Runtime.CompilerServices;
using SkiaSharp;

namespace XPlatUI.DragDrop;

/// <summary>
/// Routes a drag a backend reports into the control tree: it finds the control under the pointer,
/// keeps track of which one the drag is over, and runs that control's drag handlers.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of the feature's routing. The library names none of it and keeps no state for
/// it, so an application that never calls <c>AddDragDrop()</c> publishes without any of it.
/// </para>
/// <para>
/// A router belongs to one window, because what it routes to is a control tree and a control tree
/// belongs to a window. <see cref="For"/> is how the native halves find the one the portable hook
/// installed.
/// </para>
/// <para>
/// The control under the pointer receives the events when its <c>AllowDrop</c> is set; otherwise the
/// nearest ancestor that accepts drops does, so a panel can accept drops across its whole area
/// without every child opting in. A control that has <c>AllowDrop</c> but refuses this payload
/// passes the drag to its parent as well.
/// </para>
/// </remarks>
internal sealed class XPlatDragDropRouter
{
    private static readonly ConditionalWeakTable<XPlatWindow, XPlatDragDropRouter> Routers = new();

    private readonly XPlatUiHost _host;
    private XPlatControl? _target;

    internal XPlatDragDropRouter(XPlatUiHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <summary>
    /// The router installed for <paramref name="window"/>, creating it on first use. A native half
    /// calls this after the portable hook has run, so it always gets the same instance.
    /// </summary>
    /// <param name="window">The window whose control tree the router routes into.</param>
    internal static XPlatDragDropRouter For(XPlatWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return Routers.GetValue(window, static w => new XPlatDragDropRouter(w.Host));
    }

    /// <summary>Forgets the router installed for <paramref name="window"/>.</summary>
    /// <param name="window">The window that is going away.</param>
    internal static void Release(XPlatWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (Routers.TryGetValue(window, out var router))
        {
            router.Leave();
            Routers.Remove(window);
        }
    }

    /// <summary>
    /// Routes the start of a drag and returns the effect the target accepted, which the backend
    /// shows in its drag cursor.
    /// </summary>
    /// <param name="location">The pointer in host coordinates.</param>
    /// <param name="data">The payload being dragged.</param>
    /// <param name="allowedEffects">What the drag source allows the target to do.</param>
    /// <param name="modifiers">The modifier keys held down.</param>
    internal XPlatDragDropEffects DragEnter(
        SKPoint location,
        XPlatDataObject data,
        XPlatDragDropEffects allowedEffects,
        XPlatModifiers modifiers = XPlatModifiers.None) =>
        Guard(() => Move(location, data, allowedEffects, modifiers));

    /// <summary>
    /// Routes a drag move and returns the effect the target accepts. A backend that reports only
    /// moves still produces a drag enter handler when the target changes.
    /// </summary>
    /// <param name="location">The pointer in host coordinates.</param>
    /// <param name="data">The payload being dragged.</param>
    /// <param name="allowedEffects">What the drag source allows the target to do.</param>
    /// <param name="modifiers">The modifier keys held down.</param>
    internal XPlatDragDropEffects DragOver(
        SKPoint location,
        XPlatDataObject data,
        XPlatDragDropEffects allowedEffects,
        XPlatModifiers modifiers = XPlatModifiers.None) =>
        Guard(() => Move(location, data, allowedEffects, modifiers));

    /// <summary>Routes the end of a drag and returns the effect the target performed.</summary>
    /// <param name="location">The pointer in host coordinates.</param>
    /// <param name="data">The payload being dropped.</param>
    /// <param name="allowedEffects">What the drag source allows the target to do.</param>
    /// <param name="modifiers">The modifier keys held down.</param>
    internal XPlatDragDropEffects DragDrop(
        SKPoint location,
        XPlatDataObject data,
        XPlatDragDropEffects allowedEffects,
        XPlatModifiers modifiers = XPlatModifiers.None) =>
        Guard(() => Drop(location, data, allowedEffects, modifiers));

    /// <summary>Tells the control under the pointer that the drag left, or was cancelled.</summary>
    internal void DragLeave() => Guard(Leave);

    /// <summary>Finds the target, then runs its drop handlers and reports what it did.</summary>
    private XPlatDragDropEffects Drop(
        SKPoint location,
        XPlatDataObject data,
        XPlatDragDropEffects allowedEffects,
        XPlatModifiers modifiers)
    {
        _ = Move(location, data, allowedEffects, modifiers);
        if (_target is null)
        {
            return XPlatDragDropEffects.None;
        }

        var args = new XPlatDragEventArgs(data, ToLocal(_target, location), allowedEffects, modifiers);
        XPlatDragDropState.Find(_target)?.RaiseDragDrop(_target, args);
        RunControlHandling(_target, args, drop: true);

        _target = null;
        return args.Effect & allowedEffects;
    }

    /// <summary>
    /// Finds the control that accepts the drag at <paramref name="location"/>, runs its enter or
    /// over handlers, and reports the effect it settled on.
    /// </summary>
    /// <remarks>
    /// <c>AllowDrop</c> alone does not make a control the target: a control that declines the
    /// payload with <see cref="XPlatDragDropEffects.None"/> is passed over and the search continues
    /// with its parent. That is what lets a drop land on the panel behind a child that only accepts
    /// some kinds of drag, such as a text box that takes text but not files.
    /// </remarks>
    private XPlatDragDropEffects Move(
        SKPoint location,
        XPlatDataObject data,
        XPlatDragDropEffects allowedEffects,
        XPlatModifiers modifiers)
    {
        ArgumentNullException.ThrowIfNull(data);

        for (var control = HitTest(location); control is not null; control = control.Parent)
        {
            var state = XPlatDragDropState.Find(control);
            if (state?.AllowDrop != true)
            {
                continue;
            }

            var args = new XPlatDragEventArgs(data, ToLocal(control, location), allowedEffects, modifiers);

            if (ReferenceEquals(control, _target))
            {
                state.RaiseDragOver(control, args);
            }
            else
            {
                state.RaiseDragEnter(control, args);
            }

            RunControlHandling(control, args, drop: false);

            var accepted = args.Effect & allowedEffects;
            if (accepted == XPlatDragDropEffects.None)
            {
                continue;
            }

            // Only a change of target ends a drag for the control that had it. A drag that is still
            // over the same control has not left it, and telling it that it had would take away the
            // highlight it put up when the drag arrived.
            if (!ReferenceEquals(control, _target))
            {
                Leave();
            }

            _target = control;
            return accepted;
        }

        Leave();
        return XPlatDragDropEffects.None;
    }

    /// <summary>Tells the control the drag was over that the drag left.</summary>
    private void Leave()
    {
        var previous = _target;
        if (previous is null)
        {
            return;
        }

        _target = null;
        XPlatDragDropState.Find(previous)?.RaiseDragLeave(previous);
    }

    /// <summary>
    /// Runs the handling the library itself gives a control, after the application's handlers have
    /// had their say.
    /// </summary>
    /// <remarks>
    /// A text field takes text dropped on it, and that handling has to live somewhere even though
    /// the base class carries no drag and drop members. The feature owns it, so a field that is
    /// never dropped on costs nothing.
    /// </remarks>
    private static void RunControlHandling(XPlatControl control, XPlatDragEventArgs e, bool drop)
    {
        if (control is not XPlatTextBox box)
        {
            return;
        }

        if (drop)
        {
            XPlatTextBoxDrop.DragDrop(box, e);
        }
        else
        {
            XPlatTextBoxDrop.DragOver(box, e);
        }
    }

    private XPlatControl? HitTest(SKPoint location) => _host.Root?.HitTest(location);

    private static SKPoint ToLocal(XPlatControl control, SKPoint point)
    {
        var local = point;
        for (var current = control; current is not null; current = current.Parent)
        {
            local = new SKPoint(local.X - current.Left, local.Y - current.Top);
        }

        return local;
    }

    /// <summary>
    /// Runs an entry point a backend calls from a platform callback, so a failure reaches
    /// <see cref="XPlatApplication.AddUnhandledExceptionHandler"/> instead of unwinding into the
    /// native frame that called it.
    /// </summary>
    private static XPlatDragDropEffects Guard(Func<XPlatDragDropEffects> func)
    {
        try
        {
            return func();
        }
        catch (Exception ex)
        {
            XPlatApplication.HandleNativeBoundary(ex);
            return XPlatDragDropEffects.None;
        }
    }

    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            XPlatApplication.HandleNativeBoundary(ex);
        }
    }
}
