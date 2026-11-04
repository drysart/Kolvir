using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// Shows <see cref="XPlatContextMenu"/> values with <see cref="XPlatMenuView"/>, so every platform
/// draws the same menu. It is the portable counterpart of the native presenters: install it as
/// <see cref="XPlatContextMenuPresenter.Current"/> and the menu looks the same on Windows, macOS and
/// GTK4, because it is the same Skia drawing on all three.
/// </summary>
/// <remarks>
/// A menu lives in an <see cref="XPlatPopup"/>, which gives it a surface that can hang past the edge
/// of the owning window. The view is pushed onto the owning window as an
/// <see cref="IXPlatInputSink"/> for as long as it is open, so the arrow keys and mnemonics reach it
/// even though the popup window never takes the keyboard.
/// </remarks>
public sealed class XPlatMenuPresenter : IXPlatContextMenuPresenter
{
    /// <summary>Shows <paramref name="request"/> with the portable renderer.</summary>
    public void Show(XPlatContextMenuRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Menu.IsEmpty)
        {
            return;
        }

        ShowMenu(request.Host, new XPlatMenuView(request.Menu), request.Location);
    }

    /// <summary>
    /// Shows <paramref name="view"/> at <paramref name="location"/>, in logical pixels in
    /// <paramref name="host"/>'s coordinate space, and returns the popup it is drawn in.
    /// </summary>
    /// <param name="host">The window the menu belongs to: the one that owns the keyboard.</param>
    /// <param name="view">The menu to show. It must not already be in a control tree.</param>
    /// <param name="location">Where the menu's top left corner goes.</param>
    /// <param name="closed">
    /// Run once the menu has closed, whether something was picked, the user dismissed it or the
    /// platform took it away. What a menu bar uses to un-press the button that opened it.
    /// </param>
    public static XPlatPopup ShowMenu(
        XPlatUiHost host,
        XPlatMenuView view,
        SKPoint location,
        Action? closed = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(view);

        var maximum = new SKSize(
            MathF.Max(1f, host.ClientSize.Width),
            MathF.Max(1f, host.ClientSize.Height));

        // A popup window can hang past the window it belongs to, so the display - not the window -
        // is what a menu has to stay inside. A presenter whose backend cannot say where the window
        // sits on screen keeps the menu within the window, which is all an overlay can show.
        var client = new SKRect(0f, 0f, maximum.Width, maximum.Height);
        var area = PlacementArea(host) ?? client;

        var popup = new XPlatPopup(host)
        {
            Content = view,
            Presentation = XPlatPopupPresentation.Auto,
            DismissOnOutsideClick = true,
            MaximumSize = area.Size,
        };

        IDisposable? invokedToken = null;
        IDisposable? dismissedToken = null;
        IDisposable? sizeToken = null;
        IDisposable? closedToken = null;
        var released = false;

        void Release()
        {
            if (released)
            {
                return;
            }

            released = true;

            invokedToken?.Dispose();
            dismissedToken?.Dispose();
            sizeToken?.Dispose();
            closedToken?.Dispose();

            view.Detach();
        }

        invokedToken = view.AddInvokedHandler((_, item) =>
        {
            // Close before running the command: the command may open a dialog or another menu, and
            // the menu has to be gone before it does.
            popup.Close();
            item.Invoke();
        });

        dismissedToken = view.AddDismissedHandler(_ => popup.Close());

        sizeToken = view.AddSizeChangedHandler(_ =>
        {
            if (!popup.IsOpen)
            {
                return;
            }

            var bounds = AreaOf(popup, area, client);
            popup.MaximumSize = bounds.Size;
            popup.MoveTo(Place(view, location, bounds));
        });

        closedToken = popup.AddClosedHandler(_ =>
        {
            Release();
            popup.Dispose();
            view.Dispose();
            closed?.Invoke();
        });

        view.Attach(host);
        popup.Show(Place(view, location, area));

        if (popup.ActualPresentation == XPlatPopupPresentation.Overlay && !area.Equals(client))
        {
            // Painted inside the window, so the window is as far as the menu can go after all.
            popup.MaximumSize = client.Size;
            popup.MoveTo(Place(view, location, client));
        }

        return popup;
    }

    /// <summary>
    /// Works out where the popup's top left corner goes so that the menu's own surface lands on
    /// <paramref name="location" />, kept inside <paramref name="area" />, and tells the menu where
    /// the chain of submenus has to stop on either side.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A menu measures with room around it for its drop shadow, and that margin is transparent. The
    /// surface - the part the user sees as the menu - sits one margin inside the popup, so the popup
    /// is placed one margin up and to the left of the point that was asked for.
    /// </para>
    /// <para>
    /// Only the menu itself is clamped, which is what keeps it still while a submenu is open: the
    /// room a submenu needs is not room the menu has to make by moving. A submenu that does not fit
    /// before <see cref="XPlatMenuView.SurfaceLimit" /> opens on the other side of its parent
    /// instead, and a chain that opens to the left takes the popup with it - see
    /// <see cref="XPlatMenuView.SurfaceOverhang" />.
    /// </para>
    /// </remarks>
    private static SKPoint Place(XPlatMenuView view, SKPoint location, SKRect area)
    {
        var inset = view.SurfaceInset;
        var surface = view.MeasureSurface(area.Width);

        // The margin kept for the drop shadow is transparent, but it is still part of the popup.
        // Reserving it keeps the visible surface on the display and leaves the platform layer with
        // nothing to nudge back on screen, which it would otherwise do behind the menu's back.
        var left = area.Left + inset;
        var right = area.Right - inset;

        var x = Axis(location.X, surface.Width, left, right);

        // Both limits are measured from the left edge of the root menu's surface, which is where
        // the user pointed; the shadow margin and the overhang are placement, not geometry.
        view.SurfaceLimit = right - x;
        view.SurfaceFloor = left - x;

        var measured = view.Measure(new SKSize(area.Width, area.Height));

        return new SKPoint(
            x - view.SurfaceOverhang - inset,
            Axis(location.Y, measured.Height, area.Top, area.Bottom) - inset);
    }

    /// <summary>The area the menu may use: the display, or the window when the popup is painted inside it.</summary>
    private static SKRect AreaOf(XPlatPopup popup, SKRect area, SKRect client) =>
        popup.ActualPresentation == XPlatPopupPresentation.Overlay ? client : area;

    /// <summary>
    /// The work area of the display <paramref name="host" /> is on, in its client coordinates, or
    /// null when the backend cannot tell where the window sits on screen.
    /// </summary>
    private static SKRect? PlacementArea(XPlatUiHost host)
    {
        var service = host.GetService<IXPlatPopupService>() ?? XPlatPopupService.Current;
        return service?.GetPlacementArea(host);
    }

    private static float Axis(float offset, float size, float minimum, float maximum) =>
        size >= maximum - minimum
            ? minimum
            : Math.Clamp(offset, minimum, maximum - size);
}
