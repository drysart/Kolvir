namespace Kolvir.DragDrop;

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
/// The router is installed at <see cref="WindowPhase.Created"/> and released at
/// <see cref="WindowPhase.Closing"/>, because a host outlives its window's visible life and
/// nothing should route a drag into a window that is going away.
/// </para>
/// </remarks>
public sealed class DragDropWindowHook : IWindowHook
{
    /// <inheritdoc/>
    public void OnPhase(WindowPhase phase, Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        switch (phase)
        {
            case WindowPhase.Created:
                _ = DragDropRouter.For(window);
                break;

            case WindowPhase.Closing:
            case WindowPhase.Disposed:
                DragDropRouter.Release(window);
                break;
        }
    }
}
