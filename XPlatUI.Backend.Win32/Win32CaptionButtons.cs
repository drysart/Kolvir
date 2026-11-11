using SkiaSharp;

namespace XPlatUI.Backend.Win32;

/// <summary>
/// The caption buttons a borderless window would otherwise have no way to show: minimising,
/// maximising or restoring, and closing, drawn into the application's own surface and hit tested
/// before anything else.
/// </summary>
/// <remarks>
/// <para>
/// Windows has caption buttons, but only on a caption - and a borderless window has none, so there is
/// nothing for the system to draw and nothing for it to hit test. The seam promises
/// <see cref="XPlatCaptionButtons.Platform"/> on every backend, so this is where Win32 keeps that
/// promise: the geometry, the glyphs and the states are the platform's, and the only thing that gives
/// the illusion away is that Skia draws them instead of the non-client painter.
/// </para>
/// <para>
/// Everything here is in logical pixels, in client coordinates, like the rest of the seam. The one
/// number taken from the platform is the caption height, which is what makes the strip line up with
/// where Windows would have put it.
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

    /// <summary>Half the side of a glyph, at 100% scale.</summary>
    private const float GlyphRadius = 5f;

    /// <summary>
    /// The height of the strip, in logical pixels: the caption height the system would use, so the
    /// buttons sit where a decomposed title bar would have put them.
    /// </summary>
    /// <param name="dpi">The window's DPI.</param>
    /// <param name="scale">Logical-to-device ratio for that DPI.</param>
    /// <returns>The strip height in logical pixels.</returns>
    internal static float Height(uint dpi, float scale)
    {
        var device = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCaption, dpi == 0 ? 96u : dpi);
        return device > 0 ? device / (scale > 0f ? scale : 1f) : 23f;
    }

    /// <summary>
    /// Lays the buttons out against the right edge of the client, in drawing order, together with the
    /// strip an application should keep clear.
    /// </summary>
    /// <param name="clientWidth">Client width in logical pixels.</param>
    /// <param name="height">Strip height in logical pixels.</param>
    /// <param name="maximized">Whether the middle button is a restore rather than a maximize.</param>
    /// <returns>The buttons, left to right, and their union.</returns>
    internal static (SKRect Strip, IReadOnlyList<XPlatCaptionButton> Buttons) Layout(
        float clientWidth, float height, bool maximized)
    {
        if (clientWidth <= 0f || height <= 0f)
        {
            return (SKRect.Empty, []);
        }

        var close = new SKRect(clientWidth - Width, 0f, clientWidth, height);
        var middle = new SKRect(close.Left - Width, 0f, close.Left, height);
        var minimize = new SKRect(middle.Left - Width, 0f, middle.Left, height);

        var buttons = new List<XPlatCaptionButton>(3)
        {
            new(XPlatCaptionButtonKind.Minimize, minimize),
            new(maximized ? XPlatCaptionButtonKind.Restore : XPlatCaptionButtonKind.Maximize, middle),
            new(XPlatCaptionButtonKind.Close, close),
        };

        return (new SKRect(minimize.Left, 0f, close.Right, height), buttons);
    }

    /// <summary>Which button a point is over, or -1.</summary>
    /// <param name="buttons">The laid out buttons.</param>
    /// <param name="point">A point in client coordinates, logical pixels.</param>
    /// <returns>The index of the button under the point, or -1.</returns>
    internal static int HitTest(IReadOnlyList<XPlatCaptionButton> buttons, SKPoint point)
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
        IReadOnlyList<XPlatCaptionButton> buttons,
        int hot,
        int pressed,
        bool dark)
    {
        for (var i = 0; i < buttons.Count; i++)
        {
            var button = buttons[i];
            var isClose = button.Kind == XPlatCaptionButtonKind.Close;
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

    private static void DrawGlyph(SKCanvas canvas, XPlatCaptionButton button, SKColor color)
    {
        var r = button.Bounds;
        var cx = r.MidX;
        var cy = r.MidY;
        var size = MathF.Min(GlyphRadius, MathF.Min(r.Width, r.Height) / 3f);
        var half = MathF.Round(MathF.Min(GlyphRadius, size));

        using var pen = new SKPaint
        {
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color = color,
        };

        switch (button.Kind)
        {
            case XPlatCaptionButtonKind.Minimize:
                canvas.DrawLine(cx - half, MathF.Round(cy), cx + half, MathF.Round(cy), pen);
                break;

            case XPlatCaptionButtonKind.Maximize:
                canvas.DrawRect(new SKRect(cx - half, cy - half, cx + half, cy + half), pen);
                break;

            case XPlatCaptionButtonKind.Restore:
                // Two squares offset the way the platform offsets them, and the back one is
                // *occluded* by the front one rather than drawn through it. The platform fills the
                // front square with the caption colour; here the application's own paint shows
                // through, so the back square is clipped away instead of covered, which leaves the
                // 1 px gap between them that the platform's glyph has.
                var back = new SKRect(cx - half + 2f, cy - half - 2f, cx + half, cy + half);
                var front = new SKRect(cx - half, cy - half + 2f, cx + half - 2f, cy + half);
                var cut = new SKRect(front.Left - 1f, front.Top - 1f, front.Right + 1f, front.Bottom + 1f);

                canvas.Save();
                canvas.ClipRect(cut, SKClipOperation.Difference, antialias: false);
                canvas.DrawRect(back, pen);
                canvas.Restore();

                canvas.DrawRect(front, pen);
                break;

            case XPlatCaptionButtonKind.Close:
                canvas.DrawLine(cx - half, cy - half, cx + half, cy + half, pen);
                canvas.DrawLine(cx - half, cy + half, cx + half, cy - half, pen);
                break;
        }
    }
}
