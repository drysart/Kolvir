using XPlatUI;

namespace XPlatUI.Backend.Win32;

/// <summary>
/// Creates the Windows popup surfaces. Registering it is the whole of the Windows side of the
/// portable popup layer: the library asks for a surface and never learns how one is made, so macOS
/// and Linux replace this class with an equivalent and nothing above changes.
/// </summary>
internal sealed class Win32PopupService : IXPlatPopupService
{
    public IXPlatPopupWindow? CreatePopup(XPlatUiHost ownerHost, bool dismissOnOutsideClick)
    {
        ArgumentNullException.ThrowIfNull(ownerHost);

        // A popup needs a native window to be owned by, which only exists once the owner has been
        // shown. Returning null falls back to drawing the popup inside the window.
        if (ownerHost.Window?.Backend is not Win32WindowBackend backend || backend.Handle == IntPtr.Zero)
        {
            return null;
        }

        return new Win32PopupWindow(dismissOnOutsideClick);
    }
}
