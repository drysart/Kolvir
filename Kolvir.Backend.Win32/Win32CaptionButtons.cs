using SkiaSharp;

namespace Kolvir.Backend.Win32;

/// <summary>
/// The caption buttons a borderless window would otherwise have no way to show: minimising,
/// maximising or restoring, and closing, drawn into the application's own surface and hit tested
/// before anything else.
/// </summary>
/// <remarks>
/// <para>
/// Windows has caption buttons, but only on a caption - and a borderless window has none, so there is
/// nothing for the system to draw and nothing for it to hit test. The seam promises
/// <see cref="CaptionButtons.Platform"/> on every backend, so this is where Win32 keeps that
/// promise: the geometry, the glyphs and the states are the platform's, and the only thing that gives
/// the illusion away is that Skia draws them instead of the non-client painter.
/// </para>
/// <para>
/// Everything here is in logical pixels, in client coordinates, like the rest of the seam. The numbers
/// taken from the platform are the three the caption band is made of - the caption, the resize frame
/// and DWM's padded border - which is what makes the strip line up with where Windows would have put
/// it.
/// </para>
/// </remarks>
internal static class Win32CaptionButtons
{
    /// <summary>
    /// Width of one button at 100% scale. Windows uses about 46 and exposes no metric for it -
    /// <c>SM_CXSIZE</c> reports the small button, not this one - so it is a constant scaled by the
    /// window's DPI rather than a queried value.
    /// </summary>
    internal const float Width = 46f;

    /// <summary>Half the side of a glyph's path, which sits half a pixel inside its ten pixel box.</summary>
    private const float GlyphHalf = 4.5f;

    /// <summary>How much the platform rounds the corners of the square glyphs.</summary>
    private const float GlyphCorner = 1f;

    /// <summary>
    /// The caption band a 96 DPI desktop reports - 23 for the text, 4 for the frame, 4 for the padded
    /// border - used only when the platform refuses to answer for a metric at all.
    /// </summary>
    private const float CaptionBandFallback = 31f;

    /// <summary>
    /// The height of the strip, in logical pixels: the caption band the system would use, so the
    /// buttons sit where a decomposed title bar would have put them.
    /// </summary>
    /// <param name="dpi">The window's DPI.</param>
    /// <param name="scale">Logical-to-device ratio for that DPI.</param>
    /// <returns>The strip height in logical pixels.</returns>
    /// <remarks>
    /// Asks the platform for the three metrics the band is made of and hands them to
    /// <see cref="Height(int, int, int, float)"/>, which is where the arithmetic and its reasoning
    /// are. The caption probe doubles as the failure test: a call that fails returns zero for every
    /// metric, while a caption band of zero is not something a real desktop reports.
    /// </remarks>
    internal static float Height(uint dpi, float scale)
    {
        var metricsDpi = dpi == 0 ? 96u : dpi;
        var caption = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCaption, metricsDpi);
        if (caption <= 0)
        {
            // The 96 DPI band rather than the 96 DPI caption: the fallback stands in for the whole
            // thing, so it is 31 and not 23.
            return CaptionBandFallback;
        }

        return Height(
            caption,
            NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCySizeFrame, metricsDpi),
            NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxPaddedBorder, metricsDpi),
            scale);
    }

    /// <summary>
    /// The caption band from the three metrics the platform composes it out of, in logical pixels.
    /// </summary>
    /// <param name="caption">The caption's text band, <c>SM_CAPTION</c>, in device pixels.</param>
    /// <param name="frame">The resize frame, <c>SM_CYSIZEFRAME</c>, in device pixels.</param>
    /// <param name="padded">DWM's padded border, <c>SM_CXPADDEDBORDER</c>, in device pixels.</param>
    /// <param name="scale">Logical-to-device ratio for the DPI the metrics were read at.</param>
    /// <returns>The strip height in logical pixels.</returns>
    /// <remarks>
    /// <para>
    /// The sum is the band, and the caption alone is not: <c>SM_CAPTION</c> is the height of the
    /// area the text sits in, while a caption button fills the band from the top edge of the window
    /// down to the client - which is the text band, plus the resize frame above it, plus the border
    /// DWM pads around that. Measured against a decorated window rather than assumed: at 192 DPI the
    /// three read 45, 5 and 8 device pixels, the sum is 58, and the window really does put its client
    /// 58 device pixels below its top in both its normal and its maximized state. Using the caption
    /// alone left the strip 13 device pixels short, which is what a button that ends above the
    /// platform's own looks like.
    /// </para>
    /// <para>
    /// Adding these up is what the platform itself does to compute its non-client top, so the result
    /// matches Windows' own arithmetic, including how it rounds each metric at fractional scale,
    /// rather than approximating it. The padded border is legitimately zero when DWM composition is
    /// off, in which case the band is the classic caption plus frame and no special case is needed.
    /// </para>
    /// <para>
    /// Kept separate from the query so a test can pin the arithmetic without a display, which is the
    /// only part of this that can be wrong in a way a headless test would not otherwise catch.
    /// </para>
    /// </remarks>
    internal static float Height(int caption, int frame, int padded, float scale) =>
        (caption + frame + padded) / (scale > 0f ? scale : 1f);

    /// <summary>
    /// Lays the buttons out against the right edge of the client, in drawing order, together with the
    /// strip an application should keep clear.
    /// </summary>
    /// <param name="clientWidth">Client width in logical pixels.</param>
    /// <param name="height">Strip height in logical pixels.</param>
    /// <param name="maximized">Whether the middle button is a restore rather than a maximize.</param>
    /// <returns>The buttons, left to right, and their union.</returns>
    internal static (SKRect Strip, IReadOnlyList<CaptionButton> Buttons) Layout(
        float clientWidth, float height, bool maximized)
    {
        if (clientWidth <= 0f || height <= 0f)
        {
            return (SKRect.Empty, []);
        }

        var close = new SKRect(clientWidth - Width, 0f, clientWidth, height);
        var middle = new SKRect(close.Left - Width, 0f, close.Left, height);
        var minimize = new SKRect(middle.Left - Width, 0f, middle.Left, height);

        var buttons = new List<CaptionButton>(3)
        {
            new(CaptionButtonKind.Minimize, minimize),
            new(maximized ? CaptionButtonKind.Restore : CaptionButtonKind.Maximize, middle),
            new(CaptionButtonKind.Close, close),
        };

        return (new SKRect(minimize.Left, 0f, close.Right, height), buttons);
    }

    /// <summary>Which button a point is over, or -1.</summary>
    /// <param name="buttons">The laid out buttons.</param>
    /// <param name="point">A point in client coordinates, logical pixels.</param>
    /// <returns>The index of the button under the point, or -1.</returns>
    internal static int HitTest(IReadOnlyList<CaptionButton> buttons, SKPoint point)
    {
        for (var i = 0; i < buttons.Count; i++)
        {
            if (buttons[i].Bounds.Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Draws the strip: a background for the hovered or pressed button, then every glyph.
    /// </summary>
    /// <param name="canvas">The surface the application already painted into.</param>
    /// <param name="buttons">The laid out buttons.</param>
    /// <param name="hot">Index of the hovered button, or -1.</param>
    /// <param name="pressed">Index of the pressed button, or -1.</param>
    /// <param name="dark">Whether the window is using the dark appearance.</param>
    internal static void Draw(
        SKCanvas canvas,
        IReadOnlyList<CaptionButton> buttons,
        int hot,
        int pressed,
        bool dark)
    {
        for (var i = 0; i < buttons.Count; i++)
        {
            var button = buttons[i];
            var isClose = button.Kind == CaptionButtonKind.Close;
            var active = i == pressed ? true : i == hot;

            if (active)
            {
                using var fill = new SKPaint
                {
                    IsAntialias = false,

                    // Closing is the one button that changes colour rather than shading: the platform
                    // paints it red on hover and pressed alike.
                    Color = isClose
                        ? new SKColor(0xC4, 0x2B, 0x1C, pressed >= 0 && i == pressed ? (byte)0xB0 : (byte)0xFF)
                        : dark ? new SKColor(0xFF, 0xFF, 0xFF, 0x1A) : new SKColor(0x00, 0x00, 0x00, 0x10),
                };

                canvas.DrawRect(button.Bounds, fill);
            }

            var glyph = isClose && active
                ? SKColors.White
                : dark ? SKColors.White : new SKColor(0x1A, 0x1A, 0x1A);

            DrawGlyph(canvas, button, glyph);
        }
    }

    private static void DrawGlyph(SKCanvas canvas, CaptionButton button, SKColor color)
    {
        var r = button.Bounds;
        var cx = r.MidX;
        var cy = r.MidY;

        // The glyphs are ten logical pixels across including their stroke, which is what the platform
        // draws at every DPI, and the stroke is centred on the path - so the path is half a pixel in
        // from each edge of that box.
        var half = MathF.Min(GlyphHalf, (MathF.Min(r.Width, r.Height) / 2f) - 0.5f);

        using var pen = new SKPaint
        {
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color = color,
        };

        switch (button.Kind)
        {
            case CaptionButtonKind.Minimize:
                // The stroke's caps sit on the box the square glyphs use, so the line is as wide as
                // the platform's: ten logical pixels.
                var y = MathF.Round(cy);
                canvas.DrawLine(cx - half - 0.5f, y, cx + half + 0.5f, y, pen);
                break;

            case CaptionButtonKind.Maximize:
                canvas.DrawRoundRect(Square(cx, cy, half), GlyphCorner, GlyphCorner, pen);
                break;

            case CaptionButtonKind.Restore:
            {
                // Two rounded rects, a little smaller than the maximise square and offset by two
                // pixels, with the back one drawn first and *occluded* by the front one - which is
                // how the platform does it: its front square is filled, so the part of the back
                // square inside that box never shows. This backend paints over the application's own
                // content and cannot fill anything, so the same area is clipped out of the back
                // square instead, which leaves the two of its corners that stick out curving.
                var front = Square(cx - 1f, cy + 1f, half - 1f);
                var back = Square(cx + 1f, cy - 1f, half - 1f);

                // The front square's own outline, since a stroke of this width is centred on its path.
                var cover = front;
                cover.Inflate(0.5f, 0.5f);

                canvas.Save();
                canvas.ClipRect(cover, SKClipOperation.Difference, antialias: false);
                canvas.DrawRoundRect(back, GlyphCorner, GlyphCorner, pen);
                canvas.Restore();

                canvas.DrawRoundRect(front, GlyphCorner, GlyphCorner, pen);
                break;
            }

            case CaptionButtonKind.Close:
                // The platform's X fills the same ten pixel box as the square glyphs, which its
                // diagonals only reach if they run a little past the path those squares use - and it
                // has to start on a half pixel to land on whole device pixels at every scale.
                var d = half + 0.25f;
                var yc = MathF.Round(cy - 0.5f) + 0.5f;
                canvas.DrawLine(cx - d, yc - d, cx + d, yc + d, pen);
                canvas.DrawLine(cx - d, yc + d, cx + d, yc - d, pen);
                break;
        }
    }

    /// <summary>A square glyph's path, which is half a pixel inside the box it paints.</summary>
    private static SKRect Square(float cx, float cy, float half) =>
        new(cx - half, cy - half, cx + half, cy + half);
}
