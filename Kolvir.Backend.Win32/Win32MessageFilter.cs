namespace Kolvir.Backend.Win32;

/// <summary>
/// Sees a message before the window acts on it, and can stop the window acting on it at all.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam for a feature that needs a notification the backend does not model - the taskbar
/// announcing that a window's button was re-created, say - rather than a way for an application to
/// take over the window procedure. Returning <c>true</c> suppresses the window's own handling of the
/// message, painting, sizing and the caption buttons included, so <c>false</c> is the normal answer
/// and a filter that returns <c>true</c> has to mean it.
/// </para>
/// <para>
/// A filter runs on the UI thread, in the order it was added, and must not pump messages or block.
/// It may add or remove filters while it runs: the pass that is in progress walks the set it started
/// with, so a change takes effect from the next message rather than for the message being delivered.
/// A filter that throws is reported through the application's unhandled-exception hook and treated
/// as having returned <c>false</c>, so a broken feature cannot take the message loop down with it.
/// </para>
/// </remarks>
/// <param name="hwnd">The window the message is for.</param>
/// <param name="message">The message identifier.</param>
/// <param name="wParam">The message's first parameter, as the window procedure receives it.</param>
/// <param name="lParam">The message's second parameter, as the window procedure receives it.</param>
/// <returns><c>true</c> to stop the message here; <c>false</c> to let the window handle it.</returns>
public delegate bool Win32MessageFilter(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

/// <summary>
/// The window's filters, in the order they were added, dispatched over a snapshot so a filter can
/// change the set while it runs.
/// </summary>
/// <remarks>
/// Separate from <see cref="Win32WindowBackend"/> so the dispatch rules - order, a consuming filter
/// stopping the ones after it, a filter that throws not stopping them - are testable without a live
/// window, which is the same reason the backend's other decisions are split out.
/// </remarks>
internal sealed class Win32MessageFilters
{
    private readonly List<Win32MessageFilter> _filters = [];

    /// <summary>The set the pass in progress walks; empty until something is added.</summary>
    private Win32MessageFilter[] _snapshot = [];

    /// <summary>Whether there is nothing to run, which is the case for every window that has no feature attached.</summary>
    internal bool IsEmpty => _snapshot.Length == 0;

    /// <summary>Adds <paramref name="filter"/> and returns the lease that removes it again.</summary>
    internal IDisposable Add(Win32MessageFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        _filters.Add(filter);
        Refresh();
        return new Lease(this, filter);
    }

    /// <summary>Removes <paramref name="filter"/> if it is still there.</summary>
    internal void Remove(Win32MessageFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (_filters.Remove(filter))
        {
            Refresh();
        }
    }

    /// <summary>Runs the filters until one consumes the message.</summary>
    /// <returns><c>true</c> when a filter consumed the message, so the window must not handle it.</returns>
    internal bool Invoke(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        foreach (var filter in _snapshot)
        {
            bool handled;
            try
            {
                handled = filter(hwnd, message, wParam, lParam);
            }
            catch (Exception ex)
            {
                // The backend's window procedure reports into the same hook; this keeps one bad
                // filter from swallowing every message that follows it.
                Application.HandleNativeBoundary(ex);
                continue;
            }

            if (handled)
            {
                return true;
            }
        }

        return false;
    }

    private void Refresh() => _snapshot = [.. _filters];

    /// <summary>Removes one filter, once, however many times it is disposed.</summary>
    private sealed class Lease(Win32MessageFilters owner, Win32MessageFilter filter) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.Remove(filter);
        }
    }
}
