namespace XPlatUI.Backend.AppKit;

/// <summary>
/// Hands out <see cref="AppKitPopupWindow"/> surfaces. Registered by
/// <see cref="AppKitApplicationBackend"/> once the application has started, so a build that never
/// opens a popup never reaches this code.
/// </summary>
internal sealed class AppKitPopupService : IXPlatPopupService
{
    /// <inheritdoc />
    public IXPlatPopupWindow? CreatePopup(XPlatUiHost ownerHost, bool dismissOnOutsideClick)
    {
        // Without a window of its own the owner has nothing to be a child of, so the popup layer
        // falls back to painting inside the window.
        if (ownerHost.Window?.Backend is not AppKitWindowBackend { Window: not null })
        {
            return null;
        }

        return new AppKitPopupWindow(dismissOnOutsideClick);
    }
}
