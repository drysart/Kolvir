namespace Kolvir;

/// <summary>
/// An edge of the window, as the target of a resize the application starts itself.
/// </summary>
/// <remarks>
/// <para>
/// A value can name two adjacent edges at once - <see cref="Left"/> | <see cref="Top"/> - which is a
/// corner and resizes both axes. Opposite edges in one value are not meaningful and are ignored.
/// </para>
/// <para>
/// This is not the usual way a window is resized: every backend resizes natively at its own band, and
/// the band is described by <see cref="WindowChromeMetrics.ResizeInset"/> and
/// <see cref="WindowChromeMetrics.CornerResizeExtent"/>. The type exists so an application that
/// draws its own hamburger or grip can start a resize from it, without a flag on the window saying
/// which of the two mechanisms is in charge.
/// </para>
/// </remarks>
[Flags]
public enum ResizeEdge
{
    /// <summary>No edge. Starting a resize with this does nothing.</summary>
    None = 0,

    /// <summary>The left edge.</summary>
    Left = 1,

    /// <summary>The top edge.</summary>
    Top = 2,

    /// <summary>The right edge.</summary>
    Right = 4,

    /// <summary>The bottom edge.</summary>
    Bottom = 8,
}
