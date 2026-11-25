using SkiaSharp;

namespace Kolvir.Backend.Gtk4;

/// <summary>
/// Hands out GTK4 popup surfaces: a <see cref="Gtk.Popover"/> anchored to the owning window's
/// drawing area.
/// </summary>
/// <remarks>
/// A popover needs a parent widget inside a mapped window, so a popup can only be created for a
/// window that is already on screen; anything else falls back to the in-window overlay.
/// </remarks>
internal sealed class Gtk4PopupService : IPopupService
{
    /// <inheritdoc />
    public IPopupWindow? CreatePopup(UiHost windowHost, bool dismissOnOutsideClick, bool takesPresses)
    {
        // A popover is parented to the drawing area of the window it belongs to, so a popup opened
        // from a control inside another popup is a popover of the window too - a sibling of the one
        // it was opened from, which is what lets it draw outside that popup and past the window
        // edge. Its position is then in the window drawing area's coordinates, the same space a
        // popup opened from a control in the window uses.
        if (windowHost.Window?.Backend is not Gtk4WindowBackend { View: { } view } || view.Widget.GetMapped() is false)
        {
            return null;
        }

        return new Gtk4PopupWindow(view, dismissOnOutsideClick, takesPresses);
    }

    /// <summary>
    /// Always null: the popover is positioned inside the drawing area of the window it belongs to,
    /// so the window's client area really is as far as a popup can go here.
    /// </summary>
    /// <remarks>
    /// This is why a menu near the right of a small window opens its submenus to the left even when
    /// the display has room to the right: the room that matters is the room the popover can be drawn
    /// in. Windows and macOS answer with the display instead, because their popups are windows.
    /// </remarks>
    public SKRect? GetPlacementArea(UiHost windowHost) => null;
}
