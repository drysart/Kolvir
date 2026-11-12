using XPlatUI.Backend.Win32;

namespace XPlatUI.Badges;

/// <summary>
/// Attaches the badge to a window once its handle exists, and listens for the two things that make the
/// overlay the shell is holding stale.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shown, not Created.</b> There is no window handle to give the shell until the backend has shown
/// the window, so registration happens at <see cref="XPlatWindowPhase.Shown"/> and the badge is applied
/// there. <see cref="XPlatWindowPhase.Disposed"/> is where the icon is released, because it is the only
/// phase that still runs when a window goes away without being closed.
/// </para>
/// <para>
/// <b>Why the message filter is needed at all.</b> The shell throws an overlay icon away when it
/// re-creates a window's taskbar button - every Explorer restart - and a window whose DPI changed needs
/// an icon at the new size. Neither is a phase, so the backend's message filter is what makes the badge
/// survive them. The filter only observes those two messages; it consumes just the private one the
/// presenter posts to itself when a badge arrives from another thread.
/// </para>
/// </remarks>
internal sealed class Win32BadgeWindowHook : IXPlatWin32WindowHook
{
    /// <summary>The shell's announcement that a window's taskbar button exists, registered once.</summary>
    private static readonly uint TaskbarButtonCreated = Win32BadgeNativeMethods.RegisterWindowMessage("TaskbarButtonCreated");

    private readonly Win32AppBadgePresenter _presenter;
    private readonly Dictionary<XPlatWindow, WindowEntry> _windows = [];

    /// <param name="presenter">The application's badge, which every window shows.</param>
    internal Win32BadgeWindowHook(Win32AppBadgePresenter presenter) => _presenter = presenter;

    /// <inheritdoc />
    public void OnPhase(XPlatWindowPhase phase, XPlatWindow window, Win32WindowBackend backend)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(backend);

        switch (phase)
        {
            case XPlatWindowPhase.Shown when !_windows.ContainsKey(window):
                var handle = backend.Handle;
                var lease = backend.AddMessageFilter(OnMessage);
                _windows[window] = new WindowEntry(handle, lease);
                _presenter.Attach(handle);
                break;

            case XPlatWindowPhase.Disposed:
                if (_windows.Remove(window, out var entry))
                {
                    entry.Lease.Dispose();
                    _presenter.Detach(entry.Handle);
                }

                break;
        }
    }

    /// <summary>
    /// Re-applies the badge when it needs re-applying, and lets everything else through.
    /// </summary>
    private bool OnMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == Win32AppBadgePresenter.BadgeChangedMessage)
        {
            _presenter.Reapply(hwnd);

            // The message is this feature's own, so nothing else may see it.
            return true;
        }

        if (message == TaskbarButtonCreated || message == Win32BadgeNativeMethods.WmDpiChanged)
        {
            _presenter.Reapply(hwnd);
        }

        // Never consumed: the window still has to handle a DPI change, and the shell's announcement is
        // not the window's to act on in the first place.
        return false;
    }

    /// <summary>What the hook keeps per window: its handle and the filter that has to be removed.</summary>
    private sealed record WindowEntry(IntPtr Handle, IDisposable Lease);
}
