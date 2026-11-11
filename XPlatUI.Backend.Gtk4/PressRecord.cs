namespace XPlatUI.Backend.Gtk4;

/// <summary>
/// A pointer press, kept for as long as it is in flight.
/// </summary>
/// <remarks>
/// This is the whole input a GTK window drag needs: <c>gdk_toplevel_begin_move</c> takes the device,
/// the button, the press position and the event time, and works only while the grab that press began
/// is still in force. Everything here is captured from the press event itself, because none of it can
/// be reconstructed afterwards.
/// </remarks>
/// <param name="Device">The device the press came from, or null when GDK did not name one.</param>
/// <param name="Button">The GDK button number, as the event reported it.</param>
/// <param name="X">The press position, in surface coordinates.</param>
/// <param name="Y">The press position, in surface coordinates.</param>
/// <param name="Time">The event timestamp in milliseconds, as the compositor stamped it.</param>
internal readonly record struct PressRecord(Gdk.Device? Device, uint Button, double X, double Y, uint Time);
