using SkiaSharp;

namespace Kolvir;

/// <summary>The geometry a rectangle with a radius per corner is drawn from.</summary>
/// <remarks>
/// Shared by everything that draws such a rectangle - a border's ring, a control's fill, a stroked
/// frame - so that the corners line up no matter which of them painted.
/// </remarks>
internal static class RoundedRect
{
    /// <summary>
    /// Appends the outline of <paramref name="rect"/> to <paramref name="builder"/>, clockwise from
    /// the end of the top-left corner's arc.
    /// </summary>
    public static void AddTo(SKPathBuilder builder, SKRect rect, CornerRadii radii)
    {
        var (topLeftStart, topLeftEnd) = AnglesOf(Corner.TopLeft);
        var (topRightStart, topRightEnd) = AnglesOf(Corner.TopRight);
        var (bottomRightStart, bottomRightEnd) = AnglesOf(Corner.BottomRight);
        var (bottomLeftStart, bottomLeftEnd) = AnglesOf(Corner.BottomLeft);

        var topLeftOval = OvalFor(rect, Corner.TopLeft, radii.TopLeft);
        var topRightOval = OvalFor(rect, Corner.TopRight, radii.TopRight);
        var bottomRightOval = OvalFor(rect, Corner.BottomRight, radii.BottomRight);
        var bottomLeftOval = OvalFor(rect, Corner.BottomLeft, radii.BottomLeft);

        builder.MoveTo(PointAt(topLeftOval, radii.TopLeft, topLeftStart));
        ArcTo(builder, topLeftOval, radii.TopLeft, topLeftStart, topLeftEnd);
        builder.LineTo(PointAt(topRightOval, radii.TopRight, topRightStart));
        ArcTo(builder, topRightOval, radii.TopRight, topRightStart, topRightEnd);
        builder.LineTo(PointAt(bottomRightOval, radii.BottomRight, bottomRightStart));
        ArcTo(builder, bottomRightOval, radii.BottomRight, bottomRightStart, bottomRightEnd);
        builder.LineTo(PointAt(bottomLeftOval, radii.BottomLeft, bottomLeftStart));
        ArcTo(builder, bottomLeftOval, radii.BottomLeft, bottomLeftStart, bottomLeftEnd);
        builder.Close();
    }

    /// <summary>The outline of a rectangle with the given radii, which the caller disposes.</summary>
    public static SKPath Build(SKRect rect, CornerRadii radii)
    {
        using var builder = new SKPathBuilder();
        AddTo(builder, rect, radii);
        return builder.Snapshot();
    }

    /// <summary>Draws the arc of one corner of a rectangle.</summary>
    public static void ArcTo(SKPathBuilder builder, SKRect oval, float radius, float from, float to)
    {
        if (radius > 0f)
        {
            builder.ArcTo(oval, from, to - from, forceMoveTo: false);
        }
    }

    /// <summary>A point on the circle a corner's arc runs on.</summary>
    public static SKPoint PointAt(SKRect oval, float radius, float degrees)
    {
        if (radius <= 0f)
        {
            return new SKPoint(oval.MidX, oval.MidY);
        }

        float radians = degrees * MathF.PI / 180f;
        return new SKPoint(
            oval.MidX + (MathF.Cos(radians) * radius),
            oval.MidY + (MathF.Sin(radians) * radius));
    }

    /// <summary>The square the arc of one corner is drawn in, for a rectangle and that corner's radius.</summary>
    public static SKRect OvalFor(SKRect rect, Corner corner, float radius)
    {
        float diameter = radius * 2f;
        return corner switch
        {
            Corner.TopLeft => new SKRect(rect.Left, rect.Top, rect.Left + diameter, rect.Top + diameter),
            Corner.TopRight => new SKRect(rect.Right - diameter, rect.Top, rect.Right, rect.Top + diameter),
            Corner.BottomRight => new SKRect(rect.Right - diameter, rect.Bottom - diameter, rect.Right, rect.Bottom),
            _ => new SKRect(rect.Left, rect.Bottom - diameter, rect.Left + diameter, rect.Bottom),
        };
    }

    /// <summary>The radius of one corner of a rectangle.</summary>
    public static float RadiusOf(CornerRadii radii, Corner corner) => corner switch
    {
        Corner.TopLeft => radii.TopLeft,
        Corner.TopRight => radii.TopRight,
        Corner.BottomRight => radii.BottomRight,
        _ => radii.BottomLeft,
    };

    /// <summary>
    /// The radii scaled down so that no pair of them is longer than the side it sits on, which is what
    /// stops two arcs from overlapping on a control too small for the radius it was given.
    /// </summary>
    public static CornerRadii Clamp(CornerRadii radii, SKRect rect)
    {
        var clamped = new CornerRadii(
            MathF.Max(0f, radii.TopLeft),
            MathF.Max(0f, radii.TopRight),
            MathF.Max(0f, radii.BottomRight),
            MathF.Max(0f, radii.BottomLeft));

        float scale = 1f;
        scale = Limit(scale, clamped.TopLeft + clamped.TopRight, rect.Width);
        scale = Limit(scale, clamped.BottomLeft + clamped.BottomRight, rect.Width);
        scale = Limit(scale, clamped.TopLeft + clamped.BottomLeft, rect.Height);
        scale = Limit(scale, clamped.TopRight + clamped.BottomRight, rect.Height);

        return scale < 1f ? clamped * scale : clamped;

        static float Limit(float current, float pair, float length) =>
            pair > length && pair > 0f ? MathF.Min(current, length / pair) : current;
    }

    /// <summary>
    /// The angles an arc of each corner sweeps, in Skia's convention: zero is three o'clock and the
    /// angle grows clockwise on a screen whose y axis points down. The four spans meet exactly where
    /// the four edges do.
    /// </summary>
    public static (float Start, float End) AnglesOf(Corner corner) => corner switch
    {
        Corner.TopLeft => (180f, 270f),
        Corner.TopRight => (270f, 360f),
        Corner.BottomRight => (0f, 90f),
        _ => (90f, 180f),
    };

    /// <summary>One corner of a rectangle, clockwise from the top left.</summary>
    public enum Corner
    {
        TopLeft,
        TopRight,
        BottomRight,
        BottomLeft,
    }
}
