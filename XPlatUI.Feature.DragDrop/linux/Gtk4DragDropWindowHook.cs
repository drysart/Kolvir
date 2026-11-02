using XPlatUI.Backend.Gtk4;

namespace XPlatUI.DragDrop;

/// <summary>
/// Installs a window view's drop targets once the native window exists, and removes them again when
/// the window goes away.
/// </summary>
/// <remarks>
/// <para>
/// This is the native half of drag and drop. The portable half is
/// <see cref="XPlatDragDropWindowHook"/>, which installs the router the drags are routed through;
/// this half is what makes GTK4 send those drags in the first place.
/// </para>
/// <para>
/// Installation happens at <see cref="XPlatWindowPhase.Shown"/> rather than
/// <see cref="XPlatWindowPhase.Created"/>, because the drawing area does not exist until the
/// backend has shown the window. What was installed is registered with the window's own native
/// teardown, so it comes off while the widget still exists whether the window is closed or merely
/// disposed; the registration is kept here, keyed by window, because one hook serves every window
/// and the targets belong to the window rather than to the hook.
/// </para>
/// </remarks>
internal sealed class Gtk4DragDropWindowHook : IXPlatGtk4WindowHook
{
    private readonly Dictionary<XPlatWindow, IDisposable> _leases = [];

    /// <inheritdoc />
    public void OnPhase(XPlatWindowPhase phase, XPlatWindow window, Gtk4WindowBackend backend)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(backend);

        switch (phase)
        {
            case XPlatWindowPhase.Shown when !_leases.ContainsKey(window) && backend.View is { } view:
                var targets = new Gtk4DropTargets(view.InputArea, XPlatDragDropRouter.For(window));
                targets.Attach();
                _leases[window] = backend.RegisterNativeTeardown(targets);
                break;

            case XPlatWindowPhase.Closing:
            case XPlatWindowPhase.Disposed:
                // Closing is the useful one: the controllers come off the widget while it still
                // exists. Disposed only forgets an entry the backend has already released.
                if (_leases.Remove(window, out var lease))
                {
                    lease.Dispose();
                }

                break;
        }
    }
}
