using SkiaSharp;

namespace Kolvir;

/// <summary>Low level drawing helpers used by the built-in controls.</summary>
public static class Painter
{
    /// <summary>
    /// Fills <paramref name="rect"/> with <paramref name="color"/>, with the corners rounded when
    /// <paramref name="cornerRadius"/> is greater than zero. Nothing is drawn when the colour is
    /// fully transparent or the rectangle has no area, so a caller does not have to check.
    /// </summary>
    /// <param name="canvas">The canvas to draw into.</param>
    /// <param name="rect">The area to fill, in the canvas's current coordinates.</param>
    /// <param name="color">The fill colour, alpha included.</param>
    /// <param name="cornerRadius">Radius of the rounded corners; zero draws square corners.</param>
    public static void Fill(SKCanvas canvas, SKRect rect, SKColor color, float cornerRadius = 0f)
    {
        if (color.Alpha == 0 || rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        if (cornerRadius > 0f)
        {
            canvas.DrawRoundRect(rect, cornerRadius, cornerRadius, paint);
        }
        else
        {
            canvas.DrawRect(rect, paint);
        }
    }

    /// <summary>
    /// Fills <paramref name="rect"/> with <paramref name="color"/>, rounding each corner by its own
    /// radius. Nothing is drawn when the colour is fully transparent or the rectangle has no area.
    /// </summary>
    /// <param name="canvas">The canvas to draw into.</param>
    /// <param name="rect">The area to fill, in the canvas's current coordinates.</param>
    /// <param name="color">The fill colour, alpha included.</param>
    /// <param name="corners">The radius of each corner; square corners draw a rectangle.</param>
    public static void Fill(SKCanvas canvas, SKRect rect, SKColor color, CornerRadii corners)
    {
        if (color.Alpha == 0 || rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        var clamped = RoundedRect.Clamp(corners, rect);
        if (clamped.IsNone)
        {
            Fill(canvas, rect, color);
            return;
        }

        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        using var path = RoundedRect.Build(rect, clamped);
        canvas.DrawPath(path, paint);
    }

    /// <summary>
    /// Strokes a border of <paramref name="width"/> logical pixels on the inside of
    /// <paramref name="rect"/>, with the corners rounded when <paramref name="cornerRadius"/> is
    /// greater than zero.
    /// </summary>
    /// <param name="canvas">The canvas to draw into.</param>
    /// <param name="rect">
    /// The area the border is drawn inside, in the canvas's current coordinates.
    /// </param>
    /// <param name="color">The border colour, alpha included.</param>
    /// <param name="width">Thickness of the border; zero or less draws nothing.</param>
    /// <param name="cornerRadius">Radius of the outer corners; zero draws square corners.</param>
    /// <remarks>
    /// Skia centres a stroke on its path, so the border is stroked on a rectangle inset by half the
    /// width, and the corner radius is reduced by the same amount to keep the two concentric.
    /// Nothing is drawn when the width is not positive, the colour is fully transparent or the
    /// rectangle has no area.
    /// </remarks>
    public static void DrawBorder(SKCanvas canvas, SKRect rect, SKColor color, float width, float cornerRadius = 0f)
    {
        if (width <= 0f || color.Alpha == 0 || rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
        };

        // Stroke is centered on the path, so inset by half the stroke width to keep it inside the bounds.
        var strokeRect = Geometry.Deflate(rect, width / 2f);
        if (cornerRadius > 0f)
        {
            canvas.DrawRoundRect(strokeRect, MathF.Max(0f, cornerRadius - (width / 2f)), MathF.Max(0f, cornerRadius - (width / 2f)), paint);
        }
        else
        {
            canvas.DrawRect(strokeRect, paint);
        }
    }

    /// <summary>
    /// Strokes a border of <paramref name="width"/> logical pixels on the inside of
    /// <paramref name="rect"/>, rounding each corner by its own radius.
    /// </summary>
    /// <param name="canvas">The canvas to draw into.</param>
    /// <param name="rect">
    /// The area the border is drawn inside, in the canvas's current coordinates.
    /// </param>
    /// <param name="color">The border colour, alpha included.</param>
    /// <param name="width">Thickness of the border; zero or less draws nothing.</param>
    /// <param name="corners">The radius of the outer corners.</param>
    public static void DrawBorder(SKCanvas canvas, SKRect rect, SKColor color, float width, CornerRadii corners)
    {
        if (width <= 0f || color.Alpha == 0 || rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        var strokeRect = Geometry.Deflate(rect, width / 2f);
        var inset = RoundedRect.Clamp(corners, rect) - new CornerRadii(MathF.Max(0f, width / 2f));
        if (inset.IsNone)
        {
            DrawBorder(canvas, rect, color, width);
            return;
        }

        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
        };

        using var path = RoundedRect.Build(strokeRect, RoundedRect.Clamp(inset, strokeRect));
        canvas.DrawPath(path, paint);
    }

    /// <summary>Draws a focus rectangle just inside <paramref name="rect"/>.</summary>
    public static void DrawFocusRing(SKCanvas canvas, SKRect rect, SKColor color, float width = 2f, float cornerRadius = 0f)
    {
        var ring = Geometry.Deflate(rect, width + 1f);
        DrawBorder(canvas, ring, color, width, MathF.Max(0f, cornerRadius - width));
    }

    /// <summary>
    /// Draws a focus rectangle just inside <paramref name="rect"/>, following a radius per corner.
    /// </summary>
    /// <param name="canvas">The canvas to draw into.</param>
    /// <param name="rect">The area the ring sits inside.</param>
    /// <param name="color">The ring's colour, alpha included.</param>
    /// <param name="width">Thickness of the ring; zero draws nothing.</param>
    /// <param name="corners">The radius of the corners the ring follows.</param>
    public static void DrawFocusRing(SKCanvas canvas, SKRect rect, SKColor color, float width, CornerRadii corners)
    {
        var ring = Geometry.Deflate(rect, width + 1f);
        DrawBorder(canvas, ring, color, width, RoundedRect.Clamp(corners, rect) - new CornerRadii(width));
    }
}
