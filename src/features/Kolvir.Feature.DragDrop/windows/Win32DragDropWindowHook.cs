using Kolvir.Backend.Win32;

namespace Kolvir.Feature.DragDrop;

/// <summary>
/// Registers a window's OLE drop target once the native window exists, and revokes it again before
/// the window is destroyed.
/// </summary>
/// <remarks>
/// <para>
/// This is the native half of drag and drop. The portable half is
/// <see cref="DragDropWindowHook"/>, which installs the router the drags are routed through;
/// this half is what makes Windows send those drags in the first place.
/// </para>
/// <para>
/// Registration happens at <see cref="WindowPhase.Shown"/> rather than
/// <see cref="WindowPhase.Created"/>, because the window handle does not exist until the
/// backend has shown the window. The revoke is left to the backend's own disposal, which is the
/// only point that still runs when a window is disposed without being closed. A backend that replaces
/// its native window - which is how acceleration is switched on Windows - raises
/// <see cref="WindowPhase.NativeWindowRecreated"/>, and the target is registered again on the handle
/// that replaced the old one.
/// </para>
/// </remarks>
internal sealed class Win32DragDropWindowHook : IWin32WindowHook
{
    private readonly Dictionary<Window, IDisposable> _leases = [];

    /// <inheritdoc />
    public void OnPhase(WindowPhase phase, Window window, Win32WindowBackend backend)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(backend);

        switch (phase)
        {
            case WindowPhase.Shown:
                Register(window, backend);
                break;

            case WindowPhase.NativeWindowRecreated:
                // The lease was registered on the handle the backend has just thrown away, so the target
                // has to be registered again on the one that replaced it.
                if (_leases.Remove(window, out var stale))
                {
                    stale.Dispose();
                }

                Register(window, backend);
                break;

            case WindowPhase.Disposed:
                // The backend has already released the lease along with the window; this only
                // forgets the entry, so the hook does not outlive the windows it served.
                if (_leases.Remove(window, out var lease))
                {
                    lease.Dispose();
                }

                break;
        }
    }

    /// <summary>Registers a window's drop target once, on whatever handle the backend has now.</summary>
    /// <param name="window">The window the drags are routed to.</param>
    /// <param name="backend">The native window.</param>
    private void Register(Window window, Win32WindowBackend backend)
    {
        if (_leases.ContainsKey(window))
        {
            return;
        }

        var handle = backend.Handle;
        var target = new Win32DropTarget(backend, DragDropRouter.For(window));

        // A refusal here is not an error: the thread is not in a single-threaded apartment, or the
        // window already has a target. The window then simply never sees a drop.
        if (target.Register(handle))
        {
            _leases[window] = backend.RegisterNativeTeardown(new Win32DropTargetLease(target, handle));
        }
    }

    /// <summary>Revokes a registered drop target while the window it belongs to still exists.</summary>
    private sealed class Win32DropTargetLease(Win32DropTarget target, IntPtr handle) : IDisposable
    {
        private Win32DropTarget? _target = target;

        public void Dispose()
        {
            _target?.Revoke(handle);
            _target = null;
        }
    }
}
