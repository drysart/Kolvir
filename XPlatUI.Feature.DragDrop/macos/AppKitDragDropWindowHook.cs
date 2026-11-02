using SkiaSharp;
using XPlatUI.Backend.AppKit;

namespace XPlatUI.DragDrop;

/// <summary>
/// Registers a window's view for dragged pasteboard types once the native window exists, wires the
/// view's drags into the router, and unregisters it again when the window goes away.
/// </summary>
/// <remarks>
/// <para>
/// This is the native half of drag and drop. The portable half is
/// <see cref="XPlatDragDropWindowHook"/>, which installs the router the host routes drags through;
/// this half is what makes AppKit send those drags in the first place.
/// </para>
/// <para>
/// The view cannot shed its <c>NSDraggingDestination</c> overrides, so it speaks only in its own
/// terms instead: it hands over an <see cref="AppKitDragPayload"/> and asks for an
/// <see cref="AppKitDragEffect"/>. This class translates between that and the feature's types, which
/// is what keeps the backend assembly free of any reference to the feature.
/// </para>
/// <para>
/// Registration happens at <see cref="XPlatWindowPhase.Shown"/> rather than
/// <see cref="XPlatWindowPhase.Created"/>, because the view does not exist until the backend has
/// shown the window.
/// </para>
/// </remarks>
internal sealed class AppKitDragDropWindowHook : IXPlatAppKitWindowHook, IAppKitDragSink
{
    private XPlatDragDropRouter? _router;

    /// <inheritdoc />
    public void OnPhase(XPlatWindowPhase phase, XPlatWindow window, AppKitWindowBackend backend)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(backend);

        switch (phase)
        {
            case XPlatWindowPhase.Shown:
                _router = XPlatDragDropRouter.For(window);
                backend.DragSink = this;
                break;

            case XPlatWindowPhase.Closing:
            case XPlatWindowPhase.Disposed:
                backend.DragSink = null;
                _router = null;
                break;
        }
    }

    /// <inheritdoc />
    public AppKitDragEffect DragEntered(SKPoint location, AppKitDragPayload payload) =>
        Route(location, payload, enter: true);

    /// <inheritdoc />
    public AppKitDragEffect DragUpdated(SKPoint location, AppKitDragPayload payload) =>
        Route(location, payload, enter: false);

    /// <inheritdoc />
    public void DragExited() => _router?.DragLeave();

    /// <inheritdoc />
    public AppKitDragEffect Dropped(SKPoint location, AppKitDragPayload payload)
    {
        if (_router is null || ToDataObject(payload) is not { } data)
        {
            return AppKitDragEffect.None;
        }

        return ToEffect(
            _router.DragDrop(location, data, ToEffects(payload.AllowedEffects), payload.Modifiers));
    }

    private AppKitDragEffect Route(SKPoint location, AppKitDragPayload payload, bool enter)
    {
        if (_router is null || ToDataObject(payload) is not { } data)
        {
            return AppKitDragEffect.None;
        }

        var effects = ToEffects(payload.AllowedEffects);
        var accepted = enter
            ? _router.DragEnter(location, data, effects, payload.Modifiers)
            : _router.DragOver(location, data, effects, payload.Modifiers);

        return ToEffect(accepted);
    }

    private static XPlatDataObject? ToDataObject(AppKitDragPayload payload) =>
        payload.Files.Length > 0
            ? XPlatDataObject.FromFiles(payload.Files)
            : payload.Text is { Length: > 0 } text
                ? XPlatDataObject.FromText(text)
                : null;

    private static XPlatDragDropEffects ToEffects(AppKitDragEffect effect)
    {
        var effects = XPlatDragDropEffects.None;

        if ((effect & AppKitDragEffect.Copy) != 0)
        {
            effects |= XPlatDragDropEffects.Copy;
        }

        if ((effect & AppKitDragEffect.Move) != 0)
        {
            effects |= XPlatDragDropEffects.Move;
        }

        if ((effect & AppKitDragEffect.Link) != 0)
        {
            effects |= XPlatDragDropEffects.Link;
        }

        return effects;
    }

    private static AppKitDragEffect ToEffect(XPlatDragDropEffects effect)
    {
        var result = AppKitDragEffect.None;

        if ((effect & XPlatDragDropEffects.Copy) != 0)
        {
            result |= AppKitDragEffect.Copy;
        }

        if ((effect & XPlatDragDropEffects.Move) != 0)
        {
            result |= AppKitDragEffect.Move;
        }

        if ((effect & XPlatDragDropEffects.Link) != 0)
        {
            result |= AppKitDragEffect.Link;
        }

        return result;
    }
}
