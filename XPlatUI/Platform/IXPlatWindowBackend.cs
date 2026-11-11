using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// The platform half of an <see cref="XPlatWindow"/>: it owns the native window and the
/// <see cref="XPlatUiHost"/> that paints into it, and forwards the platform's notifications so the
/// model can raise its own events.
/// </summary>
/// <remarks>
/// <para>
/// A backend creates one of these per window and keeps it alive until the native window is gone.
/// <c>XPlatWindow.Backend</c> exposes it as the escape hatch for platform-specific work: cast it to
/// the backend's own type to reach an <c>NSWindow</c> or an <c>HWND</c>.
/// </para>
/// <para>
/// <b>Input does not pass through the model.</b> The backend forwards pointer, key and text events
/// straight into <c>Host.ProcessMouseMove</c>, <c>ProcessMouseDown</c>, <c>ProcessMouseUp</c>,
/// <c>ProcessMouseLeave</c>, <c>ProcessMouseWheel</c>, <c>ProcessKeyDown</c>, <c>ProcessKeyUp</c>,
/// <c>ProcessTextInput</c>, <c>ProcessContextMenu</c> and <c>SetClientSize</c>, exactly as the
/// harnesses do today. The model adds lifecycle, services and the loop around that; it never
/// interposes on input.
/// </para>
/// </remarks>
public interface IXPlatWindowBackend : IDisposable
{
    /// <summary>The host that paints this window's control tree. The backend creates and owns it.</summary>
    XPlatUiHost Host { get; }

    /// <summary>Logical-to-physical pixel ratio of the display the window is on.</summary>
    float ScaleFactor { get; }

    /// <summary>The display the window is on, or null before it has been shown.</summary>
    XPlatScreen? Screen { get; }

    /// <summary>Title bar text.</summary>
    string Title { get; set; }

    /// <summary>
    /// The window's icon, or null to keep the platform's generic application icon.
    /// </summary>
    /// <remarks>
    /// What an icon <em>is</em> differs per platform, so each backend applies it its own way:
    /// Windows sets the small icon for the title bar and the big one for the taskbar and Alt-Tab,
    /// both sized from the window's DPI; macOS has no per-window icon and sets the application icon
    /// instead; GTK4 uses <see cref="XPlatIcon.Name"/> because the theme, not the application, owns
    /// the pixels.
    /// </remarks>
    XPlatIcon? Icon { get; set; }

    /// <summary>Client area size, in logical pixels.</summary>
    SKSize ClientSize { get; set; }

    /// <summary>Smallest client area the user may drag the window to, in logical pixels.</summary>
    SKSize MinimumClientSize { get; set; }

    /// <summary>Largest client area the user may drag the window to, in logical pixels.</summary>
    SKSize MaximumClientSize { get; set; }

    /// <summary>Top-left corner in screen coordinates, in logical pixels.</summary>
    SKPoint Location { get; set; }

    /// <summary>False for a fixed-size window.</summary>
    bool Resizable { get; set; }

    /// <summary>True to keep the window above all others.</summary>
    bool TopMost { get; set; }

    /// <summary>The window's display state.</summary>
    XPlatWindowState State { get; set; }

    /// <summary>
    /// The window that owns this one, or null. An owned window is stacked above its owner and
    /// minimizes with it.
    /// </summary>
    /// <remarks>
    /// Assigning it once the native window exists applies the relationship immediately; before that
    /// it is recorded and applied when the window is created. Closing owned windows is the model's
    /// job, so a backend never has to cascade a close.
    /// </remarks>
    IXPlatWindowBackend? Owner { get; set; }

    /// <summary>
    /// The window's menu bar, or null for none. Per window on Windows and Linux; the AppKit backend
    /// mirrors the active window's bar into the single global macOS menu bar, which is the only
    /// place the two presentations diverge.
    /// </summary>
    XPlatMenuBar? MenuBar { get; set; }

    /// <summary>
    /// How much of the window the platform draws: the ordinary decorated window, or one the
    /// application paints edge to edge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="XPlatWindowChrome.Borderless"/> does not make the window a popup. The platform keeps
    /// its resize band, snapping, tiling, animations, shadow and minimise behaviour; what it still
    /// owns is reported by <see cref="ChromeMetrics"/>. Title bar behaviour stays the application's:
    /// there is no caption region on the seam.
    /// </para>
    /// <para>
    /// <b>The default implementation throws.</b> A backend that has not adopted borderless chrome
    /// refuses the request at the call site rather than silently staying decorated, so an application
    /// finds out where it asked instead of by looking at the window.
    /// </para>
    /// </remarks>
    XPlatWindowChrome Chrome
    {
        get => XPlatWindowChrome.System;
        set => throw new NotSupportedException(
            "This backend does not implement borderless window chrome.");
    }

    /// <summary>
    /// Whether the platform draws the window's caption buttons, or the application draws its own.
    /// </summary>
    /// <remarks>
    /// This is the request; <see cref="ChromeMetrics"/> is the answer, so an application reads back
    /// what it got. <see cref="XPlatCaptionButtons.Platform"/> is honoured by every backend - Windows
    /// draws and hit-tests the buttons itself rather than leaving the application to do it. The
    /// default implementation throws, like <see cref="Chrome"/>.
    /// </remarks>
    XPlatCaptionButtons CaptionButtons
    {
        get => XPlatCaptionButtons.None;
        set => throw new NotSupportedException(
            "This backend does not implement platform caption buttons.");
    }

    /// <summary>
    /// What the platform still owns in this window, as of the last change to it.
    /// </summary>
    /// <remarks>
    /// Re-read it rather than caching it: the values move when the window changes display scale, is
    /// maximised or restored, or the theme changes, and
    /// <see cref="AddChromeMetricsChangedHandler"/> is when to re-read. A backend with nothing to
    /// report returns the empty snapshot - system chrome, no insets, no rounded corners, no buttons.
    /// </remarks>
    XPlatWindowChromeMetrics ChromeMetrics => SystemMetrics;

    /// <summary>
    /// The colour the platform should use where it treats the window as a caption, or null for the
    /// platform's own default.
    /// </summary>
    /// <remarks>
    /// Only a borderless window needs this. On Windows the non-client strip is composited with a
    /// restored frame before any handler runs, so matching it to the title bar colour is the only way
    /// to keep a restore from flashing. The default implementation throws.
    /// </remarks>
    SKColor? CaptionColor
    {
        get => null;
        set => throw new NotSupportedException(
            "This backend does not implement window caption colours.");
    }

    /// <summary>
    /// The colour the platform should use for the window's own border, or null to keep its default.
    /// </summary>
    /// <remarks>
    /// This is the border the platform draws, not one the application draws: on Windows the DWM
    /// border colour, on GTK the window's own CSS border. The default implementation throws.
    /// </remarks>
    SKColor? BorderColor
    {
        get => null;
        set => throw new NotSupportedException(
            "This backend does not implement window border colours.");
    }

    /// <summary>
    /// Starts a window drag from the press that is being handled, and reports whether the platform
    /// took it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call it synchronously from the pointer-down dispatch, before returning to the platform. The
    /// backend needs the press that is still in flight - GDK4 exposes no event serial, so a
    /// <c>begin_move</c> outside one silently does nothing - which is why this takes no arguments:
    /// the backend caches the press it dispatched.
    /// </para>
    /// <para>
    /// A <c>true</c> result means the application must not deliver the click that the same press
    /// would otherwise produce, or the control under the pointer activates when the drag ends.
    /// </para>
    /// <para>The default implementation reports that no drag was started.</para>
    /// </remarks>
    bool TryBeginDrag() => false;

    /// <summary>
    /// Starts a resize of <paramref name="edge"/> from the press that is being handled, and reports
    /// whether the platform took it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ordinary way a window resizes is the platform's own band, described by
    /// <see cref="XPlatWindowChromeMetrics.ResizeInset"/> and
    /// <see cref="XPlatWindowChromeMetrics.CornerResizeExtent"/>; this is for an application that
    /// draws its own grip. Two adjacent edges name a corner. The same synchronous rule as
    /// <see cref="TryBeginDrag"/> applies.
    /// </para>
    /// <para>The default implementation reports that no resize was started.</para>
    /// </remarks>
    bool TryBeginResize(XPlatResizeEdge edge) => false;

    /// <summary>Adds a handler that runs when <see cref="ChromeMetrics"/> changes.</summary>
    /// <remarks>
    /// It changes when the window moves to a display with a different scale factor, when the window
    /// is maximised or restored, and when the theme changes. A backend whose snapshot never moves
    /// returns a subscription that never fires.
    /// </remarks>
    IDisposable AddChromeMetricsChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new XPlatHandlerToken<Action<object>>(RemoveChromeMetricsChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddChromeMetricsChangedHandler"/> added.</summary>
    void RemoveChromeMetricsChangedHandler(Action<object> handler)
    {
    }

    /// <summary>
    /// The snapshot a backend reports when the platform owns nothing: system chrome, no insets, no
    /// rounded corners and no caption buttons.
    /// </summary>
    private static readonly XPlatWindowChromeMetrics SystemMetrics = new();

    /// <summary>
    /// Creates and shows the native window, owned by <see cref="Owner"/> when it is set, and
    /// returns without waiting for it to close. The model raises its own events from the ones
    /// below.
    /// </summary>
    void Show();

    /// <summary>
    /// Shows the window modally: disables <see cref="Owner"/> and runs a nested platform loop that
    /// returns once this window has closed. The backend must tolerate that nesting - see
    /// <see cref="IXPlatDialogService"/> for what it requires.
    /// </summary>
    void ShowDialog();

    /// <summary>Brings the window to the front and gives it focus.</summary>
    void Activate();

    /// <summary>
    /// Requests a close. The backend reports it through <see cref="AddCloseRequestedHandler"/>
    /// rather than destroying the window, so the model can raise its cancellable closing event
    /// first.
    /// </summary>
    void Close();

    /// <summary>
    /// Applies dark or light system chrome: the caption on Windows, the title bar on macOS. A
    /// backend without system chrome ignores it.
    /// </summary>
    void SetTitleBarAppearance(bool dark);

    /// <summary>Adds a handler that runs once the native window is visible.</summary>
    IDisposable AddShownHandler(Action<object> handler);

    /// <summary>Removes a handler that <see cref="AddShownHandler"/> added.</summary>
    void RemoveShownHandler(Action<object> handler);

    /// <summary>Adds a handler that runs when the user asks to close the window, including from the title bar's close button.</summary>
    IDisposable AddCloseRequestedHandler(Action<object> handler);

    /// <summary>Removes a handler that <see cref="AddCloseRequestedHandler"/> added.</summary>
    void RemoveCloseRequestedHandler(Action<object> handler);

    /// <summary>Adds a handler that runs after the native window has been destroyed.</summary>
    IDisposable AddClosedHandler(Action<object> handler);

    /// <summary>Removes a handler that <see cref="AddClosedHandler"/> added.</summary>
    void RemoveClosedHandler(Action<object> handler);

    /// <summary>Adds a handler that runs when the window becomes the active window.</summary>
    IDisposable AddActivatedHandler(Action<object> handler);

    /// <summary>Removes a handler that <see cref="AddActivatedHandler"/> added.</summary>
    void RemoveActivatedHandler(Action<object> handler);

    /// <summary>Adds a handler that runs when the window stops being the active window.</summary>
    IDisposable AddDeactivatedHandler(Action<object> handler);

    /// <summary>Removes a handler that <see cref="AddDeactivatedHandler"/> added.</summary>
    void RemoveDeactivatedHandler(Action<object> handler);

    /// <summary>Adds a handler that runs when the client area changes size, including as a result of a DPI change.</summary>
    IDisposable AddClientSizeChangedHandler(Action<object> handler);

    /// <summary>Removes a handler that <see cref="AddClientSizeChangedHandler"/> added.</summary>
    void RemoveClientSizeChangedHandler(Action<object> handler);

    /// <summary>Adds a handler that runs when the window moves to a display with a different scale factor.</summary>
    IDisposable AddScaleChangedHandler(Action<object> handler);

    /// <summary>Removes a handler that <see cref="AddScaleChangedHandler"/> added.</summary>
    void RemoveScaleChangedHandler(Action<object> handler);

    /// <summary>Adds a handler that runs when <see cref="State"/> changes.</summary>
    IDisposable AddStateChangedHandler(Action<object> handler);

    /// <summary>Removes a handler that <see cref="AddStateChangedHandler"/> added.</summary>
    void RemoveStateChangedHandler(Action<object> handler);

    /// <summary>
    /// Runs the backend's own typed window hooks for <paramref name="phase"/>.
    /// </summary>
    /// <param name="phase">The phase the window has just entered.</param>
    /// <param name="window">The window it belongs to.</param>
    /// <remarks>
    /// <para>
    /// The model calls this once per phase, after the portable <see cref="IXPlatWindowHook"/>s in
    /// an ascending phase and before them in a descending one, so a typed hook always tears down
    /// before the portable hook that set it up. The backend resolves its own
    /// <c>IXPlat&lt;Platform&gt;WindowHook</c> services from <paramref name="window"/> and runs them
    /// in registration order, reversed for a descending phase.
    /// </para>
    /// <para>
    /// An empty body is the correct implementation for a backend with no typed hooks; see
    /// <see cref="IXPlatApplicationBackend.RunApplicationHooks"/> for why the member exists anyway.
    /// </para>
    /// <para>
    /// <b>This member outlives <see cref="IDisposable.Dispose"/>.</b>
    /// <see cref="XPlatWindowPhase.Disposed"/> is raised after the model has disposed the backend, so
    /// an implementation must touch nothing native and must not assume the window still exists.
    /// </para>
    /// </remarks>
    void RunWindowHooks(XPlatWindowPhase phase, XPlatWindow window);
}
