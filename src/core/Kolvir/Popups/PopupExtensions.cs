using System.Runtime.CompilerServices;
using SkiaSharp;

namespace Kolvir;

/// <summary>
/// Shows a control in a popup and reports how it was dismissed. The popup API for the ordinary case
/// - show this control over there, tell me how it ended - without building a
/// <see cref="Popup"/> by hand.
/// </summary>
/// <remarks>
/// <para>
/// These are extension methods rather than members, the way FEATURE-COST-POLICY.md §5 asks: a popup
/// an application never shows costs it nothing, because nothing here is reachable without a call.
/// The state a control carries - its closed handlers, and the popup it is currently shown in -
/// lives in a table keyed by the control, since an extension cannot add a field to it.
/// </para>
/// <para>
/// While the popup is open the keyboard is forwarded into it: the focus goes to the first tab stop in
/// the content, Escape dismisses the popup unless <see cref="PopupOptions.EscapeCloses"/> says
/// otherwise, Tab and Shift+Tab cycle the popup's own controls, and every other key and typed
/// character reaches the control inside it that has the focus. Nothing of that depends on the
/// platform: no backend popup takes the keyboard, so the window that has it hands the keys on. Both
/// halves of a keystroke are handed on, so a control that acts on the release - a
/// <see cref="Button"/>, a <see cref="CheckBox"/> - works inside a popup as it does in a window.
/// </para>
/// <para>
/// <see cref="TryGetPopupBounds"/> answers the question the options cannot: where the popup really
/// landed once the display had its say, in the owning window's client coordinates.
/// </para>
/// <para>
/// One popup belongs to one owner surface at a time. A surface is a window, or a popup: a popup
/// whose anchor sits inside another popup belongs to that popup, so opening a menu from a menu
/// cancels only the level it was opened from and leaves the level above it alone. That is what makes
/// a second <c>ShowPopupAsync</c> on the same surface complete the first popup with
/// <see cref="PopupCloseReason.Cancelled"/>.
/// </para>
/// <para>
/// A closed handler added with <see cref="AddPopupClosedHandler"/> stays until it is removed and
/// fires for every popup the control is later shown in, so the control is the identity the caller
/// holds rather than any one popup.
/// </para>
/// </remarks>
public static class PopupExtensions
{
    private static readonly ConditionalWeakTable<Control, PopupChannel> Channels = new();
    private static readonly ConditionalWeakTable<Window, PopupSurface> WindowSurfaces = new();

    /// <summary>
    /// Shows <paramref name="content"/> in a popup belonging to <paramref name="window"/> and
    /// completes when it is dismissed.
    /// </summary>
    /// <remarks>
    /// There is no anchor control on this overload, so <see cref="PopupOptions.Placement"/>'s
    /// <see cref="PopupPlacement.Auto"/> resolves to <see cref="PopupPlacement.Pointer"/>: the
    /// default call opens at the pointer, where the user is looking. An explicit
    /// <see cref="PopupPlacement.Anchor"/> has nothing to hang from and is rejected; use
    /// <see cref="PopupPlacement.ClientPoint"/> or <see cref="PopupPlacement.Pointer"/> instead.
    /// </remarks>
    /// <param name="window">The window the popup belongs to.</param>
    /// <param name="content">The control to show. It must not already be in a control tree.</param>
    /// <param name="options">How to show it, or null for <see cref="PopupOptions"/>'s defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="options"/> asks for <see cref="PopupPlacement.Anchor"/> without an anchor control.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="content"/> is already in a control tree or already shown in a popup.
    /// </exception>
    public static Task<PopupResult> ShowPopupAsync(this Window window, Control content, PopupOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        return ShowPopup(window, anchor: null, content, options);
    }

    /// <summary>
    /// Shows <paramref name="content"/> in a popup belonging to <paramref name="window"/> and placed
    /// against <paramref name="anchor"/>.
    /// </summary>
    /// <remarks>
    /// The anchor is supplied, so <see cref="PopupOptions.Placement"/>'s
    /// <see cref="PopupPlacement.Auto"/> resolves to <see cref="PopupPlacement.Anchor"/>.
    /// </remarks>
    /// <param name="window">The window the popup belongs to.</param>
    /// <param name="content">The control to show. It must not already be in a control tree.</param>
    /// <param name="anchor">The control the popup is placed against.</param>
    /// <param name="options">How to show it, or null for <see cref="PopupOptions"/>'s defaults.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="content"/> or <paramref name="anchor"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="anchor"/> is not in a window.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="content"/> is already in a control tree or already shown in a popup.
    /// </exception>
    public static Task<PopupResult> ShowPopupAsync(
        this Window window,
        Control content,
        Control anchor,
        PopupOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(anchor);

        // Placement needs the anchor's host: it is the coordinate space the popup is placed in and
        // the host whose area the fit is measured against.
        if (anchor.Host is null)
        {
            throw NotInAWindow(anchor, nameof(anchor));
        }

        return ShowPopup(window, anchor, content, options);
    }

    /// <summary>
    /// Shows <paramref name="content"/> in a popup belonging to the surface
    /// <paramref name="anchor"/> is on, placed against it, and completes when the popup is dismissed.
    /// </summary>
    /// <remarks>
    /// The surface is the nearest popup <paramref name="anchor"/> or one of its ancestors is shown
    /// in, and otherwise the anchor's window. A control inside a popup therefore opens its popup at
    /// the level below the one it sits in rather than beside it.
    /// </remarks>
    /// <param name="anchor">The control the popup is placed against, and the owner surface it belongs to.</param>
    /// <param name="content">The control to show. It must not already be in a control tree.</param>
    /// <param name="options">How to show it, or null for <see cref="PopupOptions"/>'s defaults.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="anchor"/> or <paramref name="content"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="anchor"/> is not in a window.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="content"/> is already in a control tree or already shown in a popup.
    /// </exception>
    public static Task<PopupResult> ShowPopupAsync(this Control anchor, Control content, PopupOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        if (anchor.Host is null)
        {
            throw NotInAWindow(anchor, nameof(anchor));
        }

        if (FindSurface(anchor) is null && anchor.Window is null)
        {
            throw NotInAWindow(anchor, nameof(anchor));
        }

        return ShowPopup(anchor.Window, anchor, content, options);
    }

    /// <summary>
    /// Closes the popup <paramref name="content"/> is currently shown in, if it is shown in one.
    /// </summary>
    /// <remarks>
    /// The awaiting code reads <paramref name="result"/>, which defaults to
    /// <see cref="PopupResult.Closed"/> - reason <see cref="PopupCloseReason.Closed"/> and no
    /// value. A call for a control that is in no popup does nothing.
    /// </remarks>
    /// <param name="content">The control the popup was shown with.</param>
    /// <param name="result">What the popup closes with, or null for a plain close.</param>
    public static void ClosePopup(this Control content, PopupResult? result = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (Channels.TryGetValue(content, out var channel) && channel.Session is { } session)
        {
            session.Complete(result ?? PopupResult.Closed());
        }
    }

    /// <summary>
    /// Asks where a popup showing <paramref name="content"/> ended up: its rectangle in the client
    /// coordinates of the window that owns it, after any flip and clamp.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the answer the arguments cannot give: <see cref="PopupOptions.Align"/>,
    /// <see cref="PopupOptions.Offset"/> and <see cref="PopupOptions.Size"/> are requests, and the
    /// display has the last word - the popup flips to the far side of its anchor when it does not
    /// fit, and is then moved inside the placement area. A caller that wants to draw something
    /// beside a popup, or to show the user that the flip happened, needs the rectangle that
    /// resulted rather than the one that was asked for.
    /// </para>
    /// <para>
    /// The space is the owning window's client coordinates - the space
    /// <see cref="Control.GetPositionInHost"/> reports for a control on the window's own tree, and
    /// the one <see cref="PopupPlacement.ClientPoint"/> is read in when the window is the popup's
    /// owner. Deliberately not the space the popup was placed in: a popup opened from a control
    /// inside another popup is placed in the parent popup's coordinates, and a caller that has to
    /// know which level it is at to read a rectangle would get this wrong. The popup layer already
    /// records where a popup's own host sits in the window (a popup host's client space is the
    /// window's, moved), so the answer is translated here and every popup reports in the same space,
    /// one level deep or five.
    /// </para>
    /// <para>
    /// The size is the room the popup takes, so <see cref="PopupOptions.Size"/> and the content have
    /// already been multiplied by the subtree's scale. The rectangle is only meaningful while the
    /// popup is showing: a control that is in no popup, or whose popup has closed, is answered with
    /// false, which is what makes this safe to call from a closed handler.
    /// </para>
    /// <para>
    /// One window is worth knowing about, and it is on macOS: for the single run-loop pass in which
    /// the owning window is being moved, a popup answers false here - as if it were not showing -
    /// while it is alive and does go on following the window. A caller that places something beside
    /// the popup, from a handler for the move, therefore gets nothing at exactly the pass it wanted
    /// the answer. The rectangle has not gone and is there on the next pass: when this answers false
    /// during a move, defer to one - a callback the move scheduled, or the pass after it - rather
    /// than reading it as "closed". This is a known window in what AppKit has settled, not a defect
    /// left open: the popup is following the window, and asking it from inside the move is asking it
    /// to speak for a state the platform has not reached yet.
    /// </para>
    /// </remarks>
    /// <param name="content">The control the popup was shown with.</param>
    /// <param name="bounds">The popup's rectangle in the owning window's client coordinates.</param>
    /// <returns>True when <paramref name="content"/> is showing in a popup; false otherwise.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public static bool TryGetPopupBounds(this Control content, out SKRect bounds)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (Channels.TryGetValue(content, out var channel)
            && channel.Session is { } session
            && session.Bounds is { } resolved)
        {
            bounds = resolved;
            return true;
        }

        bounds = default;
        return false;
    }

    /// <summary>
    /// Adds a handler that runs when a popup showing <paramref name="content"/> closes, whatever the
    /// reason, and stays subscribed for every popup the control is later shown in.
    /// </summary>
    /// <param name="content">The control the handler is about.</param>
    /// <param name="handler">
    /// The callback: the popup's content control, which is <paramref name="content"/>, followed by
    /// how the popup ended.
    /// </param>
    /// <returns>A subscription that removes the handler when it is disposed.</returns>
    public static IDisposable AddPopupClosedHandler(this Control content, Action<object, PopupResult> handler)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(handler);

        Channels.GetOrCreateValue(content).Add(handler);
        return new HandlerToken<Action<object, PopupResult>>(
            h => RemovePopupClosedHandler(content, h),
            handler);
    }

    /// <summary>Removes a handler that <see cref="AddPopupClosedHandler"/> added.</summary>
    /// <param name="content">The control the handler was added for.</param>
    /// <param name="handler">The callback to remove. Removing one that is not subscribed does nothing.</param>
    public static void RemovePopupClosedHandler(this Control content, Action<object, PopupResult> handler)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(handler);

        if (Channels.TryGetValue(content, out var channel))
        {
            channel.Remove(handler);
        }
    }

    /// <summary>
    /// The one place a popup is created: validates the call, takes the owner surface from the
    /// previous popup, and starts the new one.
    /// </summary>
    private static Task<PopupResult> ShowPopup(
        Window? window,
        Control? anchor,
        Control content,
        PopupOptions? options)
    {
        ArgumentNullException.ThrowIfNull(content);

        var settings = options ?? new PopupOptions();
        var placement = ResolvePlacement(settings.Placement, anchor);

        if (placement == PopupPlacement.Anchor && anchor is null)
        {
            throw new ArgumentException(
                $"PopupOptions.Placement is {nameof(PopupPlacement.Anchor)}, which needs an anchor control. "
                + "Use the overload that takes one, or set Placement to ClientPoint or Pointer.",
                nameof(options));
        }

        if (content.Parent is not null)
        {
            throw new InvalidOperationException(
                $"The control {Describe(content)} is already in a control tree, so it cannot be shown in a popup. "
                + "A popup hosts its content, and content has one home at a time.");
        }

        var channel = Channels.GetOrCreateValue(content);

        if (channel.Session is not null)
        {
            throw new InvalidOperationException(
                $"The control {Describe(content)} is already shown in a popup. "
                + "Close that popup, or show a different control.");
        }

        var surface = FindSurface(anchor) ?? WindowSurface(window!);

        // The surface keeps one popup at a time: this is what makes a menu opened from a menu
        // replace its own level rather than the one above it.
        surface.CancelCurrent();

        var session = new PopupSession(
            content,
            channel,
            surface,
            settings,
            anchor,
            window,
            placement);

        surface.Current = session;
        channel.Session = session;

        try
        {
            session.Show();
        }
        catch
        {
            session.Abandon();
            throw;
        }

        return session.Completion;
    }

    /// <summary>
    /// Turns <see cref="PopupPlacement.Auto"/> into the placement the call supports: against the
    /// anchor when it supplied one, and at the pointer when it did not. Every other value is
    /// already decided and is returned unchanged.
    /// </summary>
    private static PopupPlacement ResolvePlacement(PopupPlacement placement, Control? anchor) =>
        placement == PopupPlacement.Auto
            ? (anchor is null ? PopupPlacement.Pointer : PopupPlacement.Anchor)
            : placement;

    /// <summary>
    /// The popup surface <paramref name="anchor"/> belongs to: the nearest control at or above it
    /// that is currently shown in a popup, and otherwise none, which leaves the caller to use the
    /// anchor's window.
    /// </summary>
    /// <remarks>
    /// Deliberately not the anchor's <see cref="UiHost"/>: a popup that is painted in its owner's
    /// overlay layer has the owner window's host, so a host match would say a popup opened inside a
    /// popup belongs to the window and cancel the window's popup instead of the one it sits in.
    /// </remarks>
    private static PopupSurface? FindSurface(Control? anchor)
    {
        for (var control = anchor; control is not null; control = control.Parent)
        {
            if (Channels.TryGetValue(control, out var channel) && channel.Session is { } session)
            {
                return session.Surface;
            }
        }

        return null;
    }

    private static PopupSurface WindowSurface(Window window) =>
        WindowSurfaces.GetValue(window, static _ => PopupSurface.ForWindow());

    private static ArgumentException NotInAWindow(Control anchor, string parameterName) =>
        new($"The control {Describe(anchor)} is not in a window, so it has no surface to show a popup on.", parameterName);

    private static string Describe(Control control) =>
        control.Id is { Length: > 0 } id
            ? $"'{id}' ({control.GetType().Name})"
            : control.Name is { Length: > 0 } name
                ? $"'{name}' ({control.GetType().Name})"
                : control.GetType().Name;
}
