using SkiaSharp;

namespace Kolvir;

/// <summary>
/// Where a popup goes: the requested <see cref="PopupAlign"/> resolved against what the popup is
/// placed on, then fitted to the placement area of the host the popup belongs to.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is in one coordinate space: the client coordinates of the host the popup's
/// anchor belongs to. For a popup opened from a control in the window that is the window's host;
/// for one opened from a control inside another popup it is that popup's host, so a menu opened
/// from a menu is placed in the space of the surface the user is looking at rather than nudged by
/// the outer popup's position every time the window moves.
/// </para>
/// <para>
/// The area the fit and the clamp work against is the exception, and only in where it comes from:
/// it is the display's, which the platform answers for the window, and it is moved into the anchor's
/// space here. A popup opened inside a popup is a surface of its own, so the rectangle the popup it
/// was opened from occupies is not the limit of where it may go.
/// </para>
/// <para>
/// The popup's own size comes from <see cref="Popup.MeasureContent"/>, which is the host-space
/// size: the content and <see cref="PopupOptions.Size"/> are logical, and
/// <see cref="Popup.ScaleFactor"/> turns them into the room the popup actually takes. That is what
/// makes a popup opened inside a zoomed subtree read as part of it: its contents are scaled, and
/// so is the rectangle the fit and the clamp are worked out for.
/// </para>
/// </remarks>
internal static class PopupGeometry
{
    /// <summary>
    /// Resolves the popup's top-left corner in <paramref name="host"/>'s client coordinates.
    /// </summary>
    /// <param name="popup">The popup, for its measured size.</param>
    /// <param name="options">The alignment, the offset and whether flipping is allowed.</param>
    /// <param name="placement">The placement with <see cref="PopupPlacement.Auto"/> already resolved.</param>
    /// <param name="anchor">The anchor for <see cref="PopupPlacement.Anchor"/>, or null otherwise.</param>
    /// <param name="host">The host whose coordinates the result is in, and whose area applies.</param>
    internal static SKPoint Resolve(
        Popup popup,
        PopupOptions options,
        PopupPlacement placement,
        Control? anchor,
        UiHost host)
    {
        var size = popup.MeasureContent();
        var area = PlacementArea(host);

        var pointer = host.MousePosition;
        var reference = Reference(placement, options, anchor, pointer);

        // AtPointer on an anchor is measured from the pointer itself; a client point is already a
        // point and keeps its own. A pointer that has never been over the host falls back to the
        // reference's corner, which is the anchor's top-left.
        var at = placement == PopupPlacement.Anchor ? pointer ?? reference.Location : reference.Location;

        // A client point is the offset: reading it again as a nudge would count it twice. A pointer
        // popup takes the offset as a nudge, the same way an anchor popup does.
        var nudge = placement == PopupPlacement.ClientPoint ? SKPoint.Empty : options.Offset;
        var align = options.Align;

        var location = Add(FromAlign(align, reference, size, at), nudge);

        if (options.FlipIntoView
            && Opposite(align) is { } opposite
            && !Fits(location, size, area, align))
        {
            // The display wins: the popup opens on the other side on the requested axis. If that
            // side does not hold it either, the clamp below is the last word.
            location = Add(FromAlign(opposite, reference, size, at), nudge);
        }

        return new SKPoint(
            Clamp(location.X, size.Width, area.Left, area.Right),
            Clamp(location.Y, size.Height, area.Top, area.Bottom));
    }

    /// <summary>The rectangle the alignment is measured against, in host coordinates.</summary>
    /// <remarks>
    /// A client point and the pointer are zero-sized anchors: the alignment then reads the point
    /// itself, which is what makes <c>BelowCenter</c> centre a popup on the pointer.
    /// </remarks>
    private static SKRect Reference(
        PopupPlacement placement,
        PopupOptions options,
        Control? anchor,
        SKPoint? pointer) => placement switch
    {
        PopupPlacement.Anchor => AnchorBounds(anchor!),
        PopupPlacement.ClientPoint => ZeroSized(options.Offset),
        _ => ZeroSized(pointer ?? SKPoint.Empty),
    };

    private static SKRect ZeroSized(SKPoint point) => new(point.X, point.Y, point.X, point.Y);

    /// <summary>
    /// The anchor's rectangle in host coordinates. Its size is multiplied by the subtree's
    /// <see cref="Control.EffectiveScaleMultiplier"/>, because the origin already is: a control
    /// inside a panel at 2.0 is twice as wide on screen as its own <see cref="Control.Width"/>.
    /// </summary>
    private static SKRect AnchorBounds(Control anchor)
    {
        var origin = anchor.GetPositionInHost();
        var scale = anchor.EffectiveScaleMultiplier;

        return new SKRect(
            origin.X,
            origin.Y,
            origin.X + (anchor.Width * scale),
            origin.Y + (anchor.Height * scale));
    }

    /// <summary>The top-left corner an alignment places a popup of <paramref name="size"/> at.</summary>
    private static SKPoint FromAlign(PopupAlign align, SKRect reference, SKSize size, SKPoint at)
    {
        var left = reference.Left;
        var top = reference.Top;
        var right = reference.Right;
        var bottom = reference.Bottom;
        var middle = reference.MidX;

        return align switch
        {
            PopupAlign.BelowStart => new SKPoint(left, bottom),
            PopupAlign.BelowCenter => new SKPoint(middle - (size.Width / 2f), bottom),
            PopupAlign.BelowEnd => new SKPoint(right - size.Width, bottom),
            PopupAlign.AboveStart => new SKPoint(left, top - size.Height),
            PopupAlign.AboveCenter => new SKPoint(middle - (size.Width / 2f), top - size.Height),
            PopupAlign.AboveEnd => new SKPoint(right - size.Width, top - size.Height),
            PopupAlign.RightOf => new SKPoint(right, top),
            PopupAlign.LeftOf => new SKPoint(left - size.Width, top),
            _ => at,
        };
    }

    /// <summary>The align on the other side of the same axis, or null when an align has no other side.</summary>
    private static PopupAlign? Opposite(PopupAlign align) => align switch
    {
        PopupAlign.BelowStart => PopupAlign.AboveStart,
        PopupAlign.BelowCenter => PopupAlign.AboveCenter,
        PopupAlign.BelowEnd => PopupAlign.AboveEnd,
        PopupAlign.AboveStart => PopupAlign.BelowStart,
        PopupAlign.AboveCenter => PopupAlign.BelowCenter,
        PopupAlign.AboveEnd => PopupAlign.BelowEnd,
        PopupAlign.RightOf => PopupAlign.LeftOf,
        PopupAlign.LeftOf => PopupAlign.RightOf,
        _ => null,
    };

    /// <summary>
    /// Whether the popup fits in the area on the axis the alignment chose. The other axis is left
    /// to the clamp: there is no opposite to flip to across it.
    /// </summary>
    private static bool Fits(SKPoint location, SKSize size, SKRect area, PopupAlign align)
    {
        var rect = new SKRect(
            location.X,
            location.Y,
            location.X + size.Width,
            location.Y + size.Height);

        return align switch
        {
            PopupAlign.BelowStart or PopupAlign.BelowCenter or PopupAlign.BelowEnd
                or PopupAlign.AboveStart or PopupAlign.AboveCenter or PopupAlign.AboveEnd
                => rect.Top >= area.Top && rect.Bottom <= area.Bottom,

            PopupAlign.RightOf or PopupAlign.LeftOf
                => rect.Left >= area.Left && rect.Right <= area.Right,

            _ => true,
        };
    }

    /// <summary>
    /// Moves one axis so the popup is inside the area, or to the area's start when the popup is
    /// bigger than the area and no position can show all of it.
    /// </summary>
    private static float Clamp(float value, float size, float minimum, float maximum) =>
        size >= maximum - minimum ? minimum : Math.Clamp(value, minimum, maximum - size);

    /// <summary>
    /// The area a popup may occupy, in <paramref name="host"/>'s client coordinates: the display's
    /// work area when the platform answers for it, and the window's own client area when it does not.
    /// </summary>
    /// <remarks>
    /// The question is put to the window, not to the host the anchor happens to sit in: a popup
    /// opened inside another popup is still a surface of the window, so the room it may use is the
    /// room the window has, not the rectangle the parent popup occupies. The platform answers in
    /// the window's coordinates, so the answer is moved into the anchor's before the fit and the
    /// clamp use it - the same translation the popup's own location gets on the way out.
    /// </remarks>
    internal static SKRect PlacementArea(UiHost host)
    {
        var windowHost = host.WindowHost;
        var service = windowHost.GetService<IPopupService>() ?? PopupService.Current;

        var area = service?.GetPlacementArea(windowHost) ?? new SKRect(
            0f,
            0f,
            MathF.Max(1f, windowHost.ClientSize.Width),
            MathF.Max(1f, windowHost.ClientSize.Height));

        var origin = host.OriginInWindowHost;

        return origin == default
            ? area
            : new SKRect(
                area.Left - origin.X,
                area.Top - origin.Y,
                area.Right - origin.X,
                area.Bottom - origin.Y);
    }

    private static SKPoint Add(SKPoint point, SKPoint offset) =>
        new(point.X + offset.X, point.Y + offset.Y);
}
