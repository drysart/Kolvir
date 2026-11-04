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

        var popup = new XPlatPopup(host)
        {
            Content = view,
            Presentation = XPlatPopupPresentation.Auto,
            DismissOnOutsideClick = true,
            MaximumSize = maximum,
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

            popup.MoveTo(Place(location, view.Measure(maximum), maximum));
        });

        closedToken = popup.AddClosedHandler(_ =>
        {
            Release();
            popup.Dispose();
            view.Dispose();
            closed?.Invoke();
        });

        view.Attach(host);
        popup.Show(Place(location, view.Measure(maximum), maximum));

        return popup;
    }

    /// <summary>
    /// Keeps a menu of <paramref name="size"/> inside a window of <paramref name="client"/>: a menu
    /// that would run off the right or bottom edge moves left or up instead, which is what a native
    /// menu does when it is opened near a corner.
    /// </summary>
    private static SKPoint Place(SKPoint location, SKSize size, SKSize client)
    {
        return new SKPoint(
            Axis(location.X, size.Width, client.Width),
            Axis(location.Y, size.Height, client.Height));
    }

    private static float Axis(float offset, float size, float available) =>
        size >= available ? 0f : Math.Clamp(offset, 0f, available - size);
}
