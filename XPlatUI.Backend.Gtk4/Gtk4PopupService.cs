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
    public IXPlatPopupWindow? CreatePopup(XPlatUiHost ownerHost, bool dismissOnOutsideClick)
    {
        if (ownerHost.Window?.Backend is not Gtk4WindowBackend { View: { } view } || view.Widget.GetMapped() is false)
        {
            return null;
        }

        return new Gtk4PopupWindow(view, dismissOnOutsideClick);
    }
}
