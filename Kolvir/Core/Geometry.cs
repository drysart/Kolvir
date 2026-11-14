using SkiaSharp;

namespace Kolvir;

/// <summary>Small geometry helpers shared by the layout engine.</summary>
public static class Geometry
{
    /// <summary>Returns a rectangle with non-negative width and height.</summary>
    public static SKRect Normalize(SKRect rect)
    {
        float left = MathF.Min(rect.Left, rect.Right);
        float top = MathF.Min(rect.Top, rect.Bottom);
        float right = MathF.Max(rect.Left, rect.Right);
        float bottom = MathF.Max(rect.Top, rect.Bottom);
        return new SKRect(left, top, right, bottom);
    }

    /// <summary>
    /// Shrinks a rectangle by a frame on each side. The result is normalized, so a frame wider than
    /// the rectangle gives the rectangle between the deflated edges rather than an inverted one.
    /// </summary>
    /// <param name="rect">The rectangle to shrink.</param>
    /// <param name="thickness">How much to take off each edge.</param>
    /// <returns>The deflated rectangle.</returns>
    public static SKRect Deflate(SKRect rect, Thickness thickness) =>
        Normalize(new SKRect(
            rect.Left + thickness.Left,
            rect.Top + thickness.Top,
            rect.Right - thickness.Right,
            rect.Bottom - thickness.Bottom));

    /// <summary>Shrinks a rectangle by the same amount on every side.</summary>
    /// <param name="rect">The rectangle to shrink.</param>
    /// <param name="amount">How much to take off each edge.</param>
    /// <returns>The deflated rectangle.</returns>
    public static SKRect Deflate(SKRect rect, float amount) =>
        Deflate(rect, new Thickness(amount));

    /// <summary>
    /// Grows a rectangle by a frame on each side; the inverse of
    /// <see cref="Deflate(SKRect, Thickness)"/>.
    /// </summary>
    /// <param name="rect">The rectangle to grow.</param>
    /// <param name="thickness">How much to add on each edge.</param>
    /// <returns>The inflated rectangle.</returns>
    public static SKRect Inflate(SKRect rect, Thickness thickness) =>
        Normalize(new SKRect(
            rect.Left - thickness.Left,
            rect.Top - thickness.Top,
            rect.Right + thickness.Right,
            rect.Bottom + thickness.Bottom));

    /// <summary>Grows a rectangle by the same amount on every side.</summary>
    /// <param name="rect">The rectangle to grow.</param>
    /// <param name="amount">How much to add on each edge.</param>
    /// <returns>The inflated rectangle.</returns>
    public static SKRect Inflate(SKRect rect, float amount) =>
        Inflate(rect, new Thickness(amount));

    /// <summary>
    /// Answers whether a point is inside a rectangle, treating the left and top edges as inside and
    /// the right and bottom edges as outside. That is the convention hit testing needs, or two
    /// adjacent controls would both claim the boundary between them.
    /// </summary>
    /// <param name="rect">The rectangle to test against.</param>
    /// <param name="point">The point to test.</param>
    /// <returns>True when the point is inside.</returns>
    public static bool Contains(SKRect rect, SKPoint point) =>
        point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;

    /// <summary>The centre of a rectangle, computed from its edges.</summary>
    /// <param name="rect">The rectangle to find the centre of.</param>
    /// <returns>The centre point.</returns>
    public static SKPoint Center(SKRect rect) =>
        new(rect.Left + (rect.Width / 2f), rect.Top + (rect.Height / 2f));

    /// <summary>Places a box of <paramref name="size"/> inside <paramref name="container"/>.</summary>
    public static SKRect Align(SKSize size, SKRect container, ContentAlignment alignment)
    {
        float x = alignment switch
        {
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter
                => container.Left + ((container.Width - size.Width) / 2f),
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight
                => container.Right - size.Width,
            _ => container.Left,
        };

        float y = alignment switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight
                => container.Top + ((container.Height - size.Height) / 2f),
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight
                => container.Bottom - size.Height,
            _ => container.Top,
        };

        return new SKRect(x, y, x + size.Width, y + size.Height);
    }
}
