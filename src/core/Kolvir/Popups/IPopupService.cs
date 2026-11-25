using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A popup window the platform layer hands out: a top level surface that floats above the owning
/// window, draws an <see cref="UiHost"/> of its own and reports when the user dismissed it.
/// </summary>
/// <remarks>
/// The platform layer owns everything about how the surface exists - an owned layered window on
/// Windows, a child window on macOS, a transient window on GTK - so the portable layer only says
/// where the popup should appear, in the owning window's coordinate space, and what size it wants.
/// </remarks>
public interface IPopupWindow : IDisposable
{
    /// <summary>
    /// The host that draws the popup's content. Its root is the control the popup was given and
    /// its client size is the size the popup asked for.
    /// </summary>
    UiHost Host { get; }

    /// <summary>
    /// Whether a press outside the popup closes it, which the popup layer reads from
    /// <see cref="PopupOptions.DismissOnOutsideClick"/>. A popup that says false - a tooltip - keeps
    /// itself open and lets the press reach whatever it landed on.
    /// </summary>
    /// <remarks>
    /// The press that closes a popup belongs to the popup and is not delivered to whatever it landed
    /// on, so a control that owns an open popup does not act on the same click that closed it. Where
    /// the press landed is reported through <see cref="OutsidePress"/> rather than acted on here: the
    /// portable layer owns the rule for how many levels of a chain one press closes, so no backend
    /// decides that alone. Losing activation is not a press and still dismisses the popup through
    /// <see cref="Dismissed"/>.
    /// </remarks>
    bool DismissesOnOutsideClick { get; }

    /// <summary>
    /// Told about a press that landed outside this popup, at a point in the client coordinates of the
    /// window the popup belongs to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The popup layer assigns this while the popup is shown. It walks the chain of popups from the
    /// innermost level outward, closes every level the point is outside of, and stops at the first
    /// level the point is inside or the first level that does not dismiss on an outside press. A
    /// backend that reports its presses therefore follows the same rule the overlay presentation
    /// does, instead of one of its own.
    /// </para>
    /// <para>
    /// The answer is whether the press closed a level, which is what tells the backend whether the
    /// press belonged to a popup - and so must be swallowed - or to whatever it landed on.
    /// </para>
    /// <para>
    /// The default takes nothing and reports nothing, so a popup window written against an earlier
    /// version of this interface still compiles and still works: its popups are dismissed the way
    /// they always were, by the platform raising <see cref="Dismissed"/>, one level per press. What
    /// such a backend does not get is the chain rule this member exists for - a press behind a stack
    /// of its popups closes only the innermost, because the portable layer is never told where the
    /// press landed and cannot decide how many levels it is outside of. Reporting the press is how a
    /// backend opts in; every popup window in this repository does. A backend that never sees the
    /// press needs nothing: GTK4's popovers take no grab, so the press reaches the portable dismiss
    /// catcher, which is a control in the window's own tree and reports the point itself.
    /// </para>
    /// </remarks>
    Func<SKPoint, bool>? OutsidePress
    {
        get => null;

        // Left empty on purpose: the default is "this backend does not report presses", which is a
        // working backend rather than a broken one - see the remarks on the property.
        set { }
    }

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
    /// <paramref name="windowHost"/>, sized to <paramref name="size"/> logical pixels. Placement
    /// within the screen's work area is the implementation's job, because only it knows where the
    /// owner surface sits on screen.
    /// </summary>
    /// <remarks>
    /// The surface belongs to the window rather than to whatever the popup was opened from, which is
    /// what makes a popup opened inside another popup belong to the window as well: it is placed in
    /// the window's coordinates, not in its parent popup's, so the platform never has to know that a
    /// chain of popups exists.
    /// </remarks>
    void Show(UiHost windowHost, SKPoint location, SKSize size);

    /// <summary>
    /// Moves and resizes an open popup, with <paramref name="location"/> in the same coordinate space
    /// <see cref="Show"/> takes.
    /// </summary>
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
    /// <summary>
    /// Creates a popup window that is not yet shown, owned by the window <paramref name="windowHost"/>
    /// draws.
    /// </summary>
    /// <remarks>
    /// The host asked about is always the window's own, never the host of a popup a control happens
    /// to sit in: a popup window is a surface of the window, so it is owned by the window and placed
    /// in its coordinates whichever control opened it. When the window has no native window to own
    /// it yet, null is the answer and the popup is painted inside the window instead.
    /// </remarks>
    /// <param name="windowHost">The host of the window the popup belongs to.</param>
    /// <param name="dismissOnOutsideClick">
    /// Whether a press outside the popup should close it. A drop-down list or a menu wants true; a
    /// tooltip wants false so the press reaches whatever it landed on.
    /// </param>
    /// <param name="takesPresses">
    /// Whether the popup takes the pointer, as opposed to passing it through to what it covers.
    /// </param>
    IPopupWindow? CreatePopup(UiHost windowHost, bool dismissOnOutsideClick, bool takesPresses);

    /// <summary>
    /// The work area of the display <paramref name="windowHost"/> is on, in that host's client
    /// coordinates, or null when the popup will not be able to leave that client area.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A popup is a surface of its own, so the window it belongs to is not the limit of where it can
    /// go: a menu opened near the bottom or right of a window still belongs on the display, not
    /// folded back inside the window. Only the platform layer knows where the window sits on screen
    /// and how big the display is, so it answers here and the portable layer places against the
    /// answer - the same answer whether the popup was opened from a control in the window or from
    /// one inside another popup.
    /// </para>
    /// <para>
    /// Null means "inside the window", which is what a backend that paints popups inside the window
    /// - GTK's popover, or the fallback overlay - has to say, because anything it is given beyond
    /// the window would be clipped.
    /// </para>
    /// </remarks>
    SKRect? GetPlacementArea(UiHost windowHost);
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
