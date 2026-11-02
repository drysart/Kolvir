using XPlatUI.Backend.Gtk4;

namespace XPlatUI.DragDrop;

/// <summary>
/// Installs a window view's drop targets once the native window exists, and removes them again when
/// the window goes away.
/// </summary>
/// <remarks>
/// <para>
/// This is the native half of drag and drop. The portable half is
/// <see cref="XPlatDragDropWindowHook"/>, which installs the router the host routes drags through;
/// this half is what makes GTK4 send those drags in the first place.
/// </para>
/// <para>
/// Installation happens at <see cref="XPlatWindowPhase.Shown"/> rather than
/// <see cref="XPlatWindowPhase.Created"/>, because the drawing area does not exist until the
/// backend has shown the window. What was installed is parked in the backend's teardown slot, which
/// a hook instance cannot do for itself: one hook serves every window, and the targets belong to the
/// window rather than to the hook.
/// </para>
/// </remarks>
internal sealed class Gtk4DragDropWindowHook : IXPlatGtk4WindowHook
{
    /// <inheritdoc />
    public void OnPhase(XPlatWindowPhase phase, XPlatWindow window, Gtk4WindowBackend backend)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(backend);

        switch (phase)
        {
            case XPlatWindowPhase.Shown when backend.NativeTeardown is null &&
                backend.View is { } view:
                var targets = new Gtk4DropTargets(view.InputArea, view.Host);
                targets.Attach();
                backend.NativeTeardown = targets;
                break;

            case XPlatWindowPhase.Closing:
                backend.NativeTeardown?.Dispose();
                backend.NativeTeardown = null;
                break;
        }
    }
}
