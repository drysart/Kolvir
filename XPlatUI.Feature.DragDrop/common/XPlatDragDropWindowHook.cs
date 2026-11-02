namespace XPlatUI.DragDrop;

/// <summary>
/// Installs the drag and drop router on every window the application creates, and takes it away
/// again when the window closes.
/// </summary>
/// <remarks>
/// <para>
/// Each backend's <c>AddDragDrop</c> registers this together with its own typed window hook, which
/// is what makes the whole feature one call for the application. The portable half lives here and
/// the native half lives in the backend, so a feature package and a backend assembly can each own
/// the part they can build.
/// </para>
/// <para>
/// The router is installed at <see cref="XPlatWindowPhase.Created"/> and removed at
/// <see cref="XPlatWindowPhase.Closing"/>, because a host outlives its window's visible life and
/// nothing should route a drag into a window that is going away.
/// </para>
/// </remarks>
public sealed class XPlatDragDropWindowHook : IXPlatWindowHook
{
    /// <inheritdoc/>
    public void OnPhase(XPlatWindowPhase phase, XPlatWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        switch (phase)
        {
            case XPlatWindowPhase.Created:
                window.Host.DragDropRouter = new XPlatDragDropRouter();
                break;

            case XPlatWindowPhase.Closing:
            case XPlatWindowPhase.Disposed:
                window.Host.DragDropRouter = null;
                break;
        }
    }
}
