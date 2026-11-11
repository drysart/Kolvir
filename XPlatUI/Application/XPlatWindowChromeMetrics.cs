using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// One snapshot of what the platform still owns in a <see cref="XPlatWindowChrome.Borderless"/>
/// window: which pixels it paints, which of the application's pixels it claims for resizing, which
/// corners it claims and clips, and which caption buttons it draws.
/// </summary>
/// <remarks>
/// <para>
/// <b>The insets are two because they are two different things.</b>
/// <see cref="SurfaceInset"/> is where the platform paints and the application must not, while
/// <see cref="ResizeInset"/> is where the application paints but must not hit test controls. A single
/// thickness would be wrong on every platform: GTK's resize band lies entirely outside the
/// application's pixels, macOS's lies on top of them, and Windows has no inset at the top edge at all
/// because the client covers the whole window top.
/// </para>
/// <para>
/// <b>Corners are separate from edges.</b> macOS resizes from about 13 pt at a corner while its edge
/// band is 3 pt, and its 11 pt radius clips drawn content; neither number can be derived from the
/// other, so both are reported.
/// </para>
/// <para>
/// <b>A snapshot is immutable and dated.</b> Ask the window for it again rather than caching it: the
/// values change when the window moves to a display with a different scale factor, when it is
/// maximised or restored, and when the theme changes. <c>AddChromeMetricsChangedHandler</c> is when to
/// re-read. Equality is the record's default, which compares
/// <see cref="CaptionButtonBounds"/> by reference - use the change notification to detect change, not
/// <c>Equals</c>.
/// </para>
/// <para>
/// All geometry is in logical pixels, in client coordinates.
/// </para>
/// </remarks>
public sealed record XPlatWindowChromeMetrics
{
    /// <summary>
    /// The margin at each window edge that the platform paints, and the application must not.
    /// </summary>
    /// <remarks>
    /// Non-zero on Windows, where the window frame is still the platform's: 13 px on the left, right
    /// and bottom at 192 DPI, and 0 at the top because the client covers the window top. Zero on
    /// macOS and GTK, where the application's content view covers the whole window.
    /// </remarks>
    public XPlatThickness SurfaceInset { get; init; }

    /// <summary>
    /// How far in from each edge the platform claims a press for resizing, measured inside the
    /// application's own pixels.
    /// </summary>
    /// <remarks>
    /// An application should keep interactive controls out of this band. It is not the same as
    /// <see cref="SurfaceInset"/>: on GTK the band is 12 logical px wide but sits entirely outside the
    /// application's surface, so this is empty there and every pixel inside the content is the
    /// application's to hit test. On macOS this is 3 pt on every edge even though the application
    /// paints there.
    /// </remarks>
    public XPlatThickness ResizeInset { get; init; }

    /// <summary>
    /// The distance from a corner, along both axes, at which a press still resizes diagonally.
    /// </summary>
    /// <remarks>
    /// Corners are claimed more deeply than the edges - about 13 pt on macOS against a 3 pt edge band
    /// - so a corner control placed at <see cref="ResizeInset"/> from both edges would still be eaten.
    /// </remarks>
    public float CornerResizeExtent { get; init; }

    /// <summary>
    /// The radius of the window's rounded corners, which the platform clips the application's content
    /// to.
    /// </summary>
    /// <remarks>
    /// Zero for a square window. Anything an application draws in a corner is cut by this, so clip
    /// drawn chrome with the same radius. On GTK the radius must be set explicitly by the backend,
    /// because the theme defaults differ between versions (about 10 logical px on 4.14, about 12 on
    /// 4.22 once measured the same way).
    /// </remarks>
    public float CornerRadius { get; init; }

    /// <summary>
    /// The caption buttons the platform actually draws, which may not be the ones that were
    /// requested.
    /// </summary>
    public XPlatCaptionButtons CaptionButtons { get; init; }

    /// <summary>
    /// Which end of the title bar the caption buttons sit at. Meaningful only when
    /// <see cref="CaptionButtons"/> is <see cref="XPlatCaptionButtons.Platform"/>.
    /// </summary>
    public XPlatCaptionButtonSide CaptionButtonSide { get; init; }

    /// <summary>
    /// The union of every caption button rect, in client coordinates: the strip an application should
    /// keep its own content out of.
    /// </summary>
    /// <remarks>
    /// <see cref="SKRect.Empty"/> when the platform draws no buttons.
    /// </remarks>
    public SKRect CaptionButtonStrip { get; init; }

    /// <summary>
    /// Each caption button the platform draws, with its kind and its rect in client coordinates.
    /// </summary>
    /// <remarks>
    /// Empty, never null, when the platform draws no buttons. The buttons are in drawing order, which
    /// is also their order across the strip.
    /// </remarks>
    public IReadOnlyList<XPlatCaptionButton> CaptionButtonBounds { get; init; } = [];
}
