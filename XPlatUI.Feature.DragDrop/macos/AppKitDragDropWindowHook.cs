using XPlatUI.Backend.AppKit;

namespace XPlatUI.DragDrop;

/// <summary>
/// Registers a window's view for dragged pasteboard types once the native window exists, and
/// unregisters it again when the window goes away.
/// </summary>
/// <remarks>
/// <para>
/// This is the native half of drag and drop. The portable half is
/// <see cref="XPlatDragDropWindowHook"/>, which installs the router the host routes drags through;
/// this half is what makes AppKit send those drags in the first place.
/// </para>
/// <para>
/// Registration happens at <see cref="XPlatWindowPhase.Shown"/> rather than
/// <see cref="XPlatWindowPhase.Created"/>, because the view does not exist until the backend has
/// shown the window.
/// </para>
/// </remarks>
internal sealed class AppKitDragDropWindowHook : IXPlatAppKitWindowHook
{
    /// <inheritdoc />
    public void OnPhase(XPlatWindowPhase phase, XPlatWindow window, AppKitWindowBackend backend)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(backend);

        switch (phase)
        {
            case XPlatWindowPhase.Shown:
                backend.EnableDragDrop();
                break;

            case XPlatWindowPhase.Closing:
                backend.DisableDragDrop();
                break;
        }
    }
}
