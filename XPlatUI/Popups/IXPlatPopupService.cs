using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// A popup window the platform layer hands out: a top level surface that floats above the owning
/// window, draws an <see cref="XPlatUiHost"/> of its own and reports when the user dismissed it.
/// </summary>
/// <remarks>
/// The platform layer owns everything about how the surface exists - an owned layered window on
/// Windows, a child window on macOS, a transient window on GTK - so the portable layer only says
/// where the popup should appear, in the owning host's coordinate space, and what size it wants.
/// </remarks>
public interface IXPlatPopupWindow : IDisposable
{
    /// <summary>
    /// The host that draws the popup's content. Its root is the control the popup was given and
    /// its client size is the size the popup asked for.
    /// </summary>
    XPlatUiHost Host { get; }

    /// <summary>
    /// Whether the platform raised <see cref="Dismissed"/> when the pointer went down outside the
    /// popup or the owning window lost activation, so the popup closes itself.
    /// </summary>
    bool DismissesOnOutsideClick { get; }

    /// <summary>Raised when the platform dismissed the popup on the user's behalf.</summary>
    event Action<object>? Dismissed;

    /// <summary>
    /// Shows the popup at <paramref name="location"/>, in logical pixels in the coordinate space of
    /// <paramref name="ownerHost"/>, sized to <paramref name="size"/> logical pixels. Placement
    /// within the screen's work area is the implementation's job, because only it knows where the
    /// owner surface sits on screen.
    /// </summary>
    void Show(XPlatUiHost ownerHost, SKPoint location, SKSize size);

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
public interface IXPlatPopupService
{
    /// <summary>Creates a popup window that is not yet shown, owned by <paramref name="ownerHost"/>.</summary>
    /// <param name="ownerHost">Host the popup is anchored to and drawn above.</param>
    /// <param name="dismissOnOutsideClick">
    /// Whether a press outside the popup should close it. A drop-down list or a menu wants true; a
    /// tooltip wants false so the press reaches whatever it landed on.
    /// </param>
    IXPlatPopupWindow? CreatePopup(XPlatUiHost ownerHost, bool dismissOnOutsideClick);
}

/// <summary>
/// Process-wide access point for the popup service, registered by the platform layer alongside the
/// other adapters.
/// </summary>
public static class XPlatPopupService
{
    private static IXPlatPopupService? _current;

    /// <summary>
    /// The service in use. Assigning null restores the fallback, which paints popups inside the
    /// owning window.
    /// </summary>
    public static IXPlatPopupService? Current
    {
        get => _current;
        set => _current = value;
    }
}
