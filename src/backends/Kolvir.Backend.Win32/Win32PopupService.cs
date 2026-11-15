using SkiaSharp;
using Kolvir;

namespace Kolvir.Backend.Win32;

/// <summary>
/// Creates the Windows popup surfaces. Registering it is the whole of the Windows side of the
/// portable popup layer: the library asks for a surface and never learns how one is made, so macOS
/// and Linux replace this class with an equivalent and nothing above changes.
/// </summary>
internal sealed class Win32PopupService : IPopupService
{
    public IPopupWindow? CreatePopup(UiHost ownerHost, bool dismissOnOutsideClick, bool takesPresses)
    {
        ArgumentNullException.ThrowIfNull(ownerHost);

        // A popup needs a native window to be owned by, which only exists once the owner has been
        // shown. Returning null falls back to drawing the popup inside the window.
        if (ownerHost.Window?.Backend is not Win32WindowBackend backend || backend.Handle == IntPtr.Zero)
        {
            return null;
        }

        return new Win32PopupWindow(dismissOnOutsideClick, takesPresses);
    }

    /// <inheritdoc />
    public SKRect? GetPlacementArea(UiHost ownerHost)
    {
        ArgumentNullException.ThrowIfNull(ownerHost);

        if (ownerHost.Window?.Backend is not Win32WindowBackend backend || backend.Handle == IntPtr.Zero)
        {
            return null;
        }

        var work = NativeMethods.GetWorkArea(backend.Handle);

        // The window's own conversion is used rather than repeating the arithmetic: it is what the
        // popup uses to place itself, so the two agree to the pixel.
        var topLeft = backend.ScreenToLogical(work.Left, work.Top);
        var bottomRight = backend.ScreenToLogical(work.Right, work.Bottom);

        return new SKRect(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
    }
}
