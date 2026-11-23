using SkiaSharp;

namespace Kolvir.Feature.InView;

/// <summary>
/// Rectangle arithmetic for the visibility test, in host coordinates.
/// </summary>
/// <remarks>
/// Everything here works in the host's logical pixels, because that is the one space two controls in
/// different subtrees share. <see cref="HostRect"/> and <see cref="MapRect"/> wrap the public
/// conversions the library already has - <see cref="Control.GetPositionInHost"/> and
/// <see cref="Control.EffectiveScaleMultiplier"/> - so a control inside a scrolled, zoomed panel maps
/// exactly where it is painted without the feature reaching for any internal offset.
/// </remarks>
internal static class InViewGeometry
{
    /// <summary>The control's whole rectangle, in host coordinates.</summary>
    /// <param name="control">The control to map.</param>
    public static SKRect HostRect(Control control)
    {
        float scale = control.EffectiveScaleMultiplier;
        var origin = control.GetPositionInHost();

        return new SKRect(
            origin.X,
            origin.Y,
            origin.X + (control.Width * scale),
            origin.Y + (control.Height * scale));
    }

    /// <summary>Maps a rectangle from the control's client space into host coordinates.</summary>
    /// <param name="control">The control whose space <paramref name="local"/> is in.</param>
    /// <param name="local">The rectangle, in the control's client coordinates.</param>
    public static SKRect MapRect(Control control, SKRect local)
    {
        float scale = control.EffectiveScaleMultiplier;
        var origin = control.GetPositionInHost();

        return new SKRect(
            origin.X + (local.Left * scale),
            origin.Y + (local.Top * scale),
            origin.X + (local.Right * scale),
            origin.Y + (local.Bottom * scale));
    }

    /// <summary>The overlap of two rectangles; empty when they do not.</summary>
    /// <param name="a">The first rectangle.</param>
    /// <param name="b">The second rectangle.</param>
    public static SKRect Intersect(SKRect a, SKRect b) => new(
        MathF.Max(a.Left, b.Left),
        MathF.Max(a.Top, b.Top),
        MathF.Min(a.Right, b.Right),
        MathF.Min(a.Bottom, b.Bottom));

    /// <summary>True when the rectangle has no area, which is the answer every failed intersection gives.</summary>
    /// <param name="rect">The rectangle to test.</param>
    public static bool IsEmpty(SKRect rect) => rect.Right <= rect.Left || rect.Bottom <= rect.Top;

    /// <summary>
    /// Appends <paramref name="source"/> minus <paramref name="cut"/> to <paramref name="pieces"/>:
    /// nothing when the cut covers it, the source itself when they do not overlap, and up to four
    /// bands otherwise.
    /// </summary>
    /// <param name="source">The area being reduced.</param>
    /// <param name="cut">The area to remove.</param>
    /// <param name="pieces">Where the surviving rectangles are added.</param>
    public static void Subtract(SKRect source, SKRect cut, List<SKRect> pieces)
    {
        var overlap = Intersect(source, cut);

        if (IsEmpty(overlap))
        {
            pieces.Add(source);
            return;
        }

        if (overlap.Top > source.Top)
        {
            pieces.Add(new SKRect(source.Left, source.Top, source.Right, overlap.Top));
        }

        if (overlap.Bottom < source.Bottom)
        {
            pieces.Add(new SKRect(source.Left, overlap.Bottom, source.Right, source.Bottom));
        }

        if (overlap.Left > source.Left)
        {
            pieces.Add(new SKRect(source.Left, overlap.Top, overlap.Left, overlap.Bottom));
        }

        if (overlap.Right < source.Right)
        {
            pieces.Add(new SKRect(overlap.Right, overlap.Top, source.Right, overlap.Bottom));
        }
    }
}
