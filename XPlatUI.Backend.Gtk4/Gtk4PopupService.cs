using SkiaSharp;

namespace XPlatUI.Backend.Gtk4;

/// <summary>
/// Hands out GTK4 popup surfaces: a <see cref="Gtk.Popover"/> anchored to the owning window's
/// drawing area.
/// </summary>
/// <remarks>
/// A popover needs a parent widget inside a mapped window, so a popup can only be created for a
/// window that is already on screen; anything else falls back to the in-window overlay.
/// </remarks>
internal sealed class Gtk4PopupService : IXPlatPopupService
{
    /// <inheritdoc />
    public IXPlatPopupWindow? CreatePopup(XPlatUiHost ownerHost, bool dismissOnOutsideClick, bool takesPresses)
    {
        if (ownerHost.Window?.Backend is not Gtk4WindowBackend { View: { } view } || view.Widget.GetMapped() is false)
        {
            return null;
        }

        return new Gtk4PopupWindow(view, dismissOnOutsideClick, takesPresses);
    }

    /// <summary>
    /// Always null: a GTK4 popover is a widget inside the window it belongs to rather than a surface
    /// of its own, so it is clipped by that window and the window's client area really is as far as
    /// a popup can go here.
    /// </summary>
    /// <remarks>
    /// This is why a menu near the right of a small window opens its submenus to the left even when
    /// the display has room to the right: the room that matters is the room the popover can be drawn
    /// in. Windows and macOS answer with the display instead, because their popups are windows.
    /// </remarks>
    public SKRect? GetPlacementArea(XPlatUiHost ownerHost) => null;
}
