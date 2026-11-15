using SkiaSharp;

namespace Kolvir.Animations;

/// <summary>
/// The interpolation an <see cref="ValueAnimation{T}"/> uses for the values the framework
/// knows how to blend, as overloads of one name so a method group picks the right one.
/// </summary>
/// <remarks>
/// <para>
/// A value animation is generic over its value and takes the blend as a
/// <c>Func&lt;T, T, double, T&gt;</c>, which is the trim-safe way to do it: there is no registry to
/// scan, no reflection to find an implementation and no static state to warm up, and the generic
/// instantiation keeps exactly the blend it is given. That also means a caller extends the set of
/// supported types without touching the feature at all - pass a function:
/// <code>
/// new ValueAnimation&lt;MyColor&gt;(from, to, duration, Assign, (a, b, t) =&gt; MyColor.Blend(a, b, t));
/// </code>
/// The overloads here are only the ones worth not rewriting: the two floating-point types and the
/// four Skia geometry and colour types.
/// </para>
/// <para>
/// Nothing here clamps the fraction, so an easing function that overshoots - a curve that goes past
/// 1 and comes back - is blended faithfully. <see cref="SKColor"/> is the exception, because a
/// colour channel has nowhere to overshoot to: its channels are clamped to <c>0..255</c>.
/// </para>
/// </remarks>
public static class Interpolation
{
    /// <summary>Blends two <see cref="float"/> values.</summary>
    /// <param name="from">The value at <paramref name="t"/> 0.</param>
    /// <param name="to">The value at <paramref name="t"/> 1.</param>
    /// <param name="t">The fraction of the way from <paramref name="from"/> to <paramref name="to"/>.</param>
    public static float Lerp(float from, float to, double t) => (float)(from + ((to - from) * t));

    /// <summary>Blends two <see cref="double"/> values.</summary>
    /// <param name="from">The value at <paramref name="t"/> 0.</param>
    /// <param name="to">The value at <paramref name="t"/> 1.</param>
    /// <param name="t">The fraction of the way from <paramref name="from"/> to <paramref name="to"/>.</param>
    public static double Lerp(double from, double to, double t) => from + ((to - from) * t);

    /// <summary>
    /// Blends two colours channel by channel in straight (non-premultiplied) sRGB.
    /// </summary>
    /// <param name="from">The colour at <paramref name="t"/> 0.</param>
    /// <param name="to">The colour at <paramref name="t"/> 1.</param>
    /// <param name="t">The fraction of the way from <paramref name="from"/> to <paramref name="to"/>.</param>
    /// <remarks>
    /// Straight sRGB, not a perceptual space: a fade from a dark colour to a light one passes
    /// through the midpoint of the two in the same way the platform's own colour arithmetic does, so
    /// a colour blended here matches what the rest of the library paints. Blending in a perceptual
    /// space would look better and disagree with every other colour in the tree.
    /// </remarks>
    public static SKColor Lerp(SKColor from, SKColor to, double t) => new(
        Channel(from.Red, to.Red, t),
        Channel(from.Green, to.Green, t),
        Channel(from.Blue, to.Blue, t),
        Channel(from.Alpha, to.Alpha, t));

    /// <summary>Blends two points coordinate by coordinate.</summary>
    /// <param name="from">The point at <paramref name="t"/> 0.</param>
    /// <param name="to">The point at <paramref name="t"/> 1.</param>
    /// <param name="t">The fraction of the way from <paramref name="from"/> to <paramref name="to"/>.</param>
    public static SKPoint Lerp(SKPoint from, SKPoint to, double t) =>
        new(Lerp(from.X, to.X, t), Lerp(from.Y, to.Y, t));

    /// <summary>Blends two sizes by width and height.</summary>
    /// <param name="from">The size at <paramref name="t"/> 0.</param>
    /// <param name="to">The size at <paramref name="t"/> 1.</param>
    /// <param name="t">The fraction of the way from <paramref name="from"/> to <paramref name="to"/>.</param>
    public static SKSize Lerp(SKSize from, SKSize to, double t) =>
        new(Lerp(from.Width, to.Width, t), Lerp(from.Height, to.Height, t));

    /// <summary>Blends two rectangles edge by edge.</summary>
    /// <param name="from">The rectangle at <paramref name="t"/> 0.</param>
    /// <param name="to">The rectangle at <paramref name="t"/> 1.</param>
    /// <param name="t">The fraction of the way from <paramref name="from"/> to <paramref name="to"/>.</param>
    /// <remarks>
    /// Each edge is blended, which is not the same as blending the corner and the size: a rectangle
    /// grown from its centre has to move two edges left and two right, and that is exactly what an
    /// animation of a control's bounds wants.
    /// </remarks>
    public static SKRect Lerp(SKRect from, SKRect to, double t) => new(
        Lerp(from.Left, to.Left, t),
        Lerp(from.Top, to.Top, t),
        Lerp(from.Right, to.Right, t),
        Lerp(from.Bottom, to.Bottom, t));

    private static byte Channel(byte from, byte to, double t)
    {
        double value = from + ((to - from) * t);
        return (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0d, 255d);
    }
}
