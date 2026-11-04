using SkiaSharp;

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

    /// <inheritdoc />
    public SKRect? GetPlacementArea(XPlatUiHost ownerHost)
    {
        // A child window is not clipped by the frame of the window it belongs to, so the display -
        // not the window - is what a popup has to stay inside. The answer is in the owner's client
        // coordinates, which is the space the portable layer places in.
        if (ownerHost.Window?.Backend is not AppKitWindowBackend { Window: { } window })
        {
            return null;
        }

        if (AppKitApplicationBackend.ScreenForWindow(window) is not { } screen)
        {
            return null;
        }

        var work = screen.WorkingArea;
        var client = AppKitApplicationBackend.ClientOriginOf(window);

        return new SKRect(
            work.Left - client.X,
            work.Top - client.Y,
            work.Right - client.X,
            work.Bottom - client.Y);
    }
}
