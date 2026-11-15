using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A filter a control applies to everything it paints: its own background, its content and every
/// child inside it, as one picture.
/// </summary>
/// <remarks>
/// <para>
/// The control paints into a layer of its own and the filter runs when that layer is composited,
/// which is what makes a blur take the whole subtree rather than each fill and each child on its
/// own. It is the same layer <see cref="Control.Opacity"/> uses, so a control with both pays for one
/// offscreen surface rather than two.
/// </para>
/// <para>
/// <see cref="Margin"/> is the filter's promise about how far its output can reach past the pixel it
/// is given, and it decides how much the control has to repaint. A filter that answers 0 changes
/// each pixel on its own - a tint, a colour matrix, a blend - so a control wearing one keeps the
/// small partial repaints every other control gets. A filter that answers more than 0 pulls the
/// surrounding pixels in and spreads its output back out, and a layer built over only the damaged
/// area would pull the layer's transparent edge into the blur and leave a seam. Such a control
/// repaints whole: its own bounds, with its children.
/// </para>
/// <para>
/// Filters are immutable, shareable between controls and cheap to hold: build one, keep it, and
/// dispose it when no control is using it any more. Assigning a filter to a control does not
/// transfer ownership - a control never disposes one.
/// </para>
/// </remarks>
public sealed class ControlFilter : IDisposable
{
    private readonly SKColorFilter? _colorFilter;

    private ControlFilter(SKImageFilter? imageFilter, float margin, SKColorFilter? colorFilter = null)
    {
        ImageFilter = imageFilter;
        Margin = margin;
        _colorFilter = colorFilter;
    }

    /// <summary>The Skia filter the layer is composited with, or null when the filter does nothing.</summary>
    public SKImageFilter? ImageFilter { get; }

    /// <summary>
    /// How far, in logical pixels, the filter's output can reach past the pixels it was given: 0 for
    /// a filter that changes pixels in place, a finite number for one that blurs or offsets them.
    /// </summary>
    public float Margin { get; }

    /// <summary>True when the filter reaches beyond the pixels it is given, so a control wearing it repaints whole.</summary>
    public bool HasMargin => Margin > 0f;

    /// <summary>A Gaussian blur, with the same sigma on both axes.</summary>
    /// <param name="sigma">The standard deviation of the blur, in logical pixels. 0 is no blur.</param>
    public static ControlFilter Blur(float sigma) => Blur(sigma, sigma);

    /// <summary>A Gaussian blur, per axis: a motion blur rather than a soft one when the two differ.</summary>
    /// <param name="sigmaX">The horizontal standard deviation, in logical pixels.</param>
    /// <param name="sigmaY">The vertical standard deviation, in logical pixels.</param>
    /// <remarks>
    /// Skia clips the blur kernel at three sigma, so 3 x the larger sigma is what the output can
    /// reach. A blur of 0 on both axes builds no filter at all, which is what keeps
    /// <c>Blur(0)</c> a no-op rather than a layer that does nothing.
    /// </remarks>
    public static ControlFilter Blur(float sigmaX, float sigmaY)
    {
        ValidateSigma(sigmaX, nameof(sigmaX));
        ValidateSigma(sigmaY, nameof(sigmaY));

        if (sigmaX <= 0f && sigmaY <= 0f)
        {
            return new ControlFilter(null, 0f);
        }

        return new ControlFilter(
            SKImageFilter.CreateBlur(sigmaX, sigmaY),
            MathF.Ceiling(3f * MathF.Max(sigmaX, sigmaY)));
    }

    /// <summary>A copy of the control's picture, blurred, offset and drawn in behind it.</summary>
    /// <param name="offsetX">How far to the right the shadow falls, in logical pixels.</param>
    /// <param name="offsetY">How far down the shadow falls, in logical pixels.</param>
    /// <param name="sigma">The standard deviation of the shadow's blur, in logical pixels.</param>
    /// <param name="color">The shadow colour.</param>
    /// <remarks>
    /// The control's own clip still cuts the shadow off at its edges, so a shadow that has to be
    /// seen outside the control needs padding on the control or a container that does not clip.
    /// </remarks>
    public static ControlFilter DropShadow(float offsetX, float offsetY, float sigma, SKColor color)
    {
        ValidateSigma(sigma, nameof(sigma));

        return new ControlFilter(
            SKImageFilter.CreateDropShadow(offsetX, offsetY, sigma, sigma, color),
            MathF.Ceiling(3f * sigma) + MathF.Ceiling(MathF.Max(MathF.Abs(offsetX), MathF.Abs(offsetY))));
    }

    /// <summary>Recolours the control's picture, in place: the filter that makes a tint or an overlay.</summary>
    /// <param name="color">The colour to blend in.</param>
    /// <param name="blendMode">How to blend it. <see cref="SKBlendMode.SrcATop"/> tints what is already there.</param>
    public static ControlFilter Tint(SKColor color, SKBlendMode blendMode = SKBlendMode.SrcATop)
    {
        var colorFilter = SKColorFilter.CreateBlendMode(color, blendMode);

        try
        {
            return new ControlFilter(SKImageFilter.CreateColorFilter(colorFilter), 0f, colorFilter);
        }
        catch
        {
            colorFilter.Dispose();
            throw;
        }
    }

    /// <summary>Any Skia colour filter as a control filter: a matrix, a table, a luma pass, a blend.</summary>
    /// <param name="colorFilter">The colour filter to apply. It changes pixels in place, so the margin is 0.</param>
    /// <remarks>Used for anything the named factories do not cover, such as <c>SKColorFilter.CreateColorMatrix</c>.</remarks>
    public static ControlFilter FromColorFilter(SKColorFilter colorFilter)
    {
        ArgumentNullException.ThrowIfNull(colorFilter);

        try
        {
            return new ControlFilter(SKImageFilter.CreateColorFilter(colorFilter), 0f, colorFilter);
        }
        catch
        {
            colorFilter.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Any Skia image filter, with the margin the caller knows it reaches.
    /// </summary>
    /// <param name="imageFilter">The filter, or null for one that does nothing.</param>
    /// <param name="margin">
    /// How far the filter's output can reach past its input, in logical pixels. Pass
    /// <see cref="float.PositiveInfinity"/> for one whose reach cannot be bounded, such as a tile or
    /// a magnifier.
    /// </param>
    /// <remarks>
    /// Skia knows each filter's reach - <c>computeFastBounds</c> - but SkiaSharp does not bind it, so
    /// a filter built here has to be given its margin rather than asked for it. Understating it is
    /// what leaves a seam: the control will repaint only as much as the margin says.
    /// </remarks>
    public static ControlFilter Custom(SKImageFilter? imageFilter, float margin)
    {
        if (float.IsNaN(margin) || margin < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(margin), margin, "A margin cannot be negative.");
        }

        if (imageFilter is null)
        {
            return new ControlFilter(null, 0f);
        }

        return new ControlFilter(imageFilter, margin);
    }

    /// <summary>This filter, then <paramref name="next"/> applied to its result.</summary>
    /// <param name="next">The filter to apply afterwards.</param>
    /// <remarks>
    /// The two margins add: the second filter is given the first filter's output, so its output
    /// reaches as far again. A blur followed by a tint therefore costs what the blur costs and is
    /// still a blur as far as repainting is concerned, while a tint followed by a blur is a blur.
    /// </remarks>
    public ControlFilter Then(ControlFilter next)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (ImageFilter is null)
        {
            return next;
        }

        if (next.ImageFilter is null)
        {
            return this;
        }

        return new ControlFilter(
            SKImageFilter.CreateCompose(next.ImageFilter, ImageFilter),
            AddMargins(Margin, next.Margin));
    }

    /// <summary>Every filter in <paramref name="filters"/> over the same input, their outputs merged.</summary>
    /// <param name="filters">The filters to run side by side.</param>
    /// <remarks>
    /// Each branch sees the same picture, so the output reaches as far as the farthest-reaching one
    /// rather than the sum: the margins take the largest, not the total.
    /// </remarks>
    public static ControlFilter Merge(params ControlFilter[] filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        if (filters.Length == 0)
        {
            throw new ArgumentException("A merge needs at least one filter.", nameof(filters));
        }

        var imageFilters = new List<SKImageFilter>(filters.Length);
        var margin = 0f;

        foreach (var filter in filters)
        {
            ArgumentNullException.ThrowIfNull(filter);

            margin = MathF.Max(margin, filter.Margin);

            if (filter.ImageFilter is { } imageFilter)
            {
                imageFilters.Add(imageFilter);
            }
        }

        if (imageFilters.Count == 0)
        {
            return new ControlFilter(null, 0f);
        }

        return new ControlFilter(SKImageFilter.CreateMerge(imageFilters.ToArray()), margin);
    }

    /// <summary>Releases the Skia filter. A control using it must not paint afterwards.</summary>
    public void Dispose()
    {
        ImageFilter?.Dispose();
        _colorFilter?.Dispose();
    }

    private static void ValidateSigma(float sigma, string name)
    {
        if (float.IsNaN(sigma) || sigma < 0f)
        {
            throw new ArgumentOutOfRangeException(name, sigma, "A blur sigma cannot be negative.");
        }
    }

    /// <summary>Adds two margins, keeping an unbounded one unbounded rather than turning it into NaN.</summary>
    private static float AddMargins(float left, float right) =>
        float.IsInfinity(left) || float.IsInfinity(right) ? float.PositiveInfinity : left + right;
}
