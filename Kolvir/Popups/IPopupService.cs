using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A popup window the platform layer hands out: a top level surface that floats above the owning
/// window, draws an <see cref="UiHost"/> of its own and reports when the user dismissed it.
/// </summary>
/// <remarks>
/// The platform layer owns everything about how the surface exists - an owned layered window on
/// Windows, a child window on macOS, a transient window on GTK - so the portable layer only says
/// where the popup should appear, in the owning host's coordinate space, and what size it wants.
/// </remarks>
public interface IPopupWindow : IDisposable
{
    /// <summary>
    /// The host that draws the popup's content. Its root is the control the popup was given and
    /// its client size is the size the popup asked for.
    /// </summary>
    UiHost Host { get; }

    /// <summary>
    /// Whether the platform raised <see cref="Dismissed"/> when the pointer went down outside the
    /// popup or the owning window lost activation, so the popup closes itself.
    /// </summary>
    /// <remarks>
    /// The press that dismisses a popup belongs to the popup and is not delivered to whatever it
    /// landed on, so a control that owns an open popup does not act on the same click that closed
    /// it. The overlay presentation gets that from the control it paints behind the popup; a
    /// platform window has to consume the press itself.
    /// </remarks>
    bool DismissesOnOutsideClick { get; }

    /// <summary>
    /// Whether the popup takes the pointer at all, as opposed to passing it through to whatever it
    /// covers.
    /// </summary>
    /// <remarks>
    /// This is separate from <see cref="DismissesOnOutsideClick"/> because the two answer different
    /// questions. A tooltip is dismissed by an outside press but must let that press reach the
    /// control underneath it, so it takes nothing and dismisses. A submenu is not the surface that
    /// dismisses the menu chain - its own menu does - but it has to be clickable, so it takes the
    /// pointer and does not dismiss.
    /// </remarks>
    bool TakesPresses { get; }

    /// <summary>Raised when the platform dismissed the popup on the user's behalf.</summary>
    event Action<object>? Dismissed;

    /// <summary>
    /// Shows the popup at <paramref name="location"/>, in logical pixels in the coordinate space of
    /// <paramref name="ownerHost"/>, sized to <paramref name="size"/> logical pixels. Placement
    /// within the screen's work area is the implementation's job, because only it knows where the
    /// owner surface sits on screen.
    /// </summary>
    void Show(UiHost ownerHost, SKPoint location, SKSize size);

    /// <summary>Resizes an open popup, keeping its anchor where it is.</summary>
    void UpdateBounds(SKPoint location, SKSize size);

    /// <summary>Hides the popup without disposing it.</summary>
    void Hide();
}

/// <summary>
/// Implemented by the platform layer to create popup windows. When none is registered popups fall
/// back to being painted inside the owning window, so the library never depends on a platform
/// being present.
/// </summary>
public interface IPopupService
{
    /// <summary>Creates a popup window that is not yet shown, owned by <paramref name="ownerHost"/>.</summary>
    /// <param name="ownerHost">Host the popup is anchored to and drawn above.</param>
    /// <param name="dismissOnOutsideClick">
    /// Whether a press outside the popup should close it. A drop-down list or a menu wants true; a
    /// tooltip wants false so the press reaches whatever it landed on.
    /// </param>
    /// <param name="takesPresses">
    /// Whether the popup takes the pointer, as opposed to passing it through to what it covers.
    /// </param>
    IPopupWindow? CreatePopup(UiHost ownerHost, bool dismissOnOutsideClick, bool takesPresses);

    /// <summary>
    /// The work area of the display <paramref name="ownerHost"/> is on, in the owner's client
    /// coordinates, or null when the popup will not be able to leave that client area.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A popup is a surface of its own, so the window it belongs to is not the limit of where it can
    /// go: a menu opened near the bottom or right of a window still belongs on the display, not
    /// folded back inside the window. Only the platform layer knows where the window sits on screen
    /// and how big the display is, so it answers here and the portable layer places against the
    /// answer.
    /// </para>
    /// <para>
    /// Null means "inside the window", which is what a backend that paints popups inside the window
    /// - GTK's popover, or the fallback overlay - has to say, because anything it is given beyond
    /// the window would be clipped.
    /// </para>
    /// </remarks>
    SKRect? GetPlacementArea(UiHost ownerHost);
}

/// <summary>
/// Process-wide access point for the popup service, registered by the platform layer alongside the
/// other adapters.
/// </summary>
public static class PopupService
{
    private static IPopupService? _current;

    /// <summary>
    /// The service in use. Assigning null restores the fallback, which paints popups inside the
    /// owning window.
    /// </summary>
    public static IPopupService? Current
    {
        get => _current;
        set => _current = value;
    }
}
