using SkiaSharp;

namespace XPlatUI.Badges;

/// <summary>
/// Draws a badge and hands it to the shell as an <c>HICON</c>, which is the only way an unpackaged
/// Windows application can put one on its taskbar button.
/// </summary>
/// <remarks>
/// <para>
/// Windows has no badge API: <c>ITaskbarList3::SetOverlayIcon</c> takes an icon and draws it over the
/// taskbar button in the small-icon slot, so everything the badge looks like is decided here. The
/// icon is drawn at exactly the size that slot asks for rather than at a fixed 32, because the shell
/// resamples whatever it is given into that slot and a resampled digit reads badly at 100%.
/// </para>
/// <para>
/// <b>The colour is the application's choice here</b>, unlike macOS where the system draws the bubble.
/// The red is the one Windows itself uses for attention, and a thin white ring keeps the badge legible
/// over both a dark and a light taskbar icon.
/// </para>
/// <para>
/// <b>Two bitmaps, and the mask is not decoration.</b> The colour bitmap is a top-down 32bpp DIB
/// carrying straight alpha, and the monochrome mask marks the pixels that are fully transparent so an
/// icon falls back correctly wherever alpha is ignored. GDI blends the colour bitmap as straight
/// alpha, so the premultiplied bytes Skia produces are expanded back first: without that every
/// antialiased edge comes out too dark.
/// </para>
/// </remarks>
internal static class Win32BadgeIcon
{
    /// <summary>The red Windows uses for attention, which is also what the platform's own overlays use.</summary>
    private static readonly SKColor Attention = new(0xD1, 0x34, 0x38);

    /// <summary>The ring that separates the red from whatever is behind it.</summary>
    private static readonly SKColor Ring = new(0xFF, 0xFF, 0xFF, 0xE6);

    /// <summary>The text colour inside the badge.</summary>
    private static readonly SKColor Ink = SKColors.White;

    /// <summary>The largest number a badge shows before it caps: two digits, which is all that fits at 16 pixels.</summary>
    private const int MaxCount = 99;

    /// <summary>
    /// The number as it is drawn: up to two digits, and a cap beyond that.
    /// </summary>
    /// <remarks>
    /// A cap rather than a truncation. Cutting the digits of 1200 to "12+" would say twelve, so
    /// everything past the cap reads as "99+", which is the convention every platform's badge uses
    /// and the only reading that stays true however large the count gets.
    /// </remarks>
    internal static string CountText(int count)
    {
        if (count <= 0)
        {
            return string.Empty;
        }

        return count <= MaxCount
            ? count.ToString(System.Globalization.CultureInfo.CurrentCulture)
            : string.Concat(MaxCount.ToString(System.Globalization.CultureInfo.CurrentCulture), "+");
    }

    /// <summary>
    /// Draws <paramref name="state"/> into an icon <paramref name="size"/> pixels square.
    /// </summary>
    /// <param name="state">The presentation to draw.</param>
    /// <param name="size">The small-icon metric for the window's DPI.</param>
    /// <returns>The icon, or <see cref="IntPtr.Zero"/> when there is nothing to show.</returns>
    internal static IntPtr Create(XPlatBadgeState state, int size)
    {
        if (size <= 0 || state.Kind == XPlatBadgeKind.None)
        {
            return IntPtr.Zero;
        }

        using var bitmap = Draw(state, size);
        return ToIcon(bitmap, size);
    }

    /// <summary>
    /// Draws the badge into a bitmap, which is what the platform is shown once it has been turned into
    /// an icon.
    /// </summary>
    /// <param name="state">The presentation to draw.</param>
    /// <param name="size">The side of the square, in pixels.</param>
    /// <returns>The pixels, which the caller owns.</returns>
    /// <remarks>
    /// Separate from <see cref="Create"/> so the drawing can be inspected pixel by pixel without GDI
    /// in the way: at 16 pixels the difference between a legible badge and a smear is a millimetre of
    /// geometry, and it is not visible in a code review.
    /// </remarks>
    internal static SKBitmap Draw(XPlatBadgeState state, int size)
    {
        var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        switch (state.Kind)
        {
            case XPlatBadgeKind.Count:
                DrawDisc(canvas, size);

                var count = CountText(state.Count);

                if (count.Length > 0)
                {
                    DrawCentredText(canvas, count, size);
                }

                break;

            case XPlatBadgeKind.Text:
                DrawDisc(canvas, size);

                if (state.Text is { Length: > 0 } text)
                {
                    DrawCentredText(canvas, text, size);
                }

                break;

            case XPlatBadgeKind.Dot:
                DrawDot(canvas, size);
                break;
        }

        return bitmap;
    }

    /// <summary>
    /// Draws the dot: a disc smaller than a count badge, because a dot says "something happened"
    /// rather than holding a number, and at the taskbar's size a full-width disc reads as an icon.
    /// </summary>
    private static void DrawDot(SKCanvas canvas, int size)
    {
        var radius = (size / 2f) - (size * 0.16f);
        var ring = Math.Max(1f, size / 14f);

        using var fill = new SKPaint { Color = Attention, IsAntialias = true };
        using var ringPaint = new SKPaint { Color = Ring, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = ring };
        canvas.DrawCircle(size / 2f, size / 2f, radius, fill);
        canvas.DrawCircle(size / 2f, size / 2f, radius - (ring / 2f), ringPaint);
    }

    /// <summary>Draws the red disc with its ring, filling the icon.</summary>
    private static void DrawDisc(SKCanvas canvas, int size)
    {
        var inset = Math.Max(0.5f, size / 32f);
        var radius = (size / 2f) - inset;
        var ring = Math.Max(1f, size / 14f);

        using var fill = new SKPaint { Color = Attention, IsAntialias = true };
        using var ringPaint = new SKPaint { Color = Ring, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = ring };
        canvas.DrawCircle(size / 2f, size / 2f, radius, fill);
        canvas.DrawCircle(size / 2f, size / 2f, radius - (ring / 2f), ringPaint);
    }

    /// <summary>
    /// Draws <paramref name="text"/> centred in the disc, shrinking it until it fits: the icon is as
    /// small as 16 pixels, and a label that overflows would be clipped by the shell rather than read.
    /// </summary>
    private static void DrawCentredText(SKCanvas canvas, string text, int size)
    {
        var available = size * 0.82f;
        var fontSize = size * 0.62f;

        using var typeface = SKTypeface.FromFamilyName(
            "Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        using var paint = new SKPaint { Color = Ink, IsAntialias = true };

        // 6 is the floor: below that the digits are a smear, so an over-long label is left to clip.
        while (fontSize > 6f)
        {
            using var probe = new SKFont(typeface, fontSize);
            if (probe.MeasureText(text) <= available)
            {
                break;
            }

            fontSize -= 0.5f;
        }

        using var font = new SKFont(typeface, fontSize);
        var width = font.MeasureText(text);
        var metrics = font.Metrics;
        var baseline = (size / 2f) + ((metrics.Ascent + metrics.Descent) / -2f);

        canvas.DrawText(text, (size - width) / 2f, baseline, SKTextAlign.Left, font, paint);
    }

    /// <summary>Turns premultiplied BGRA pixels into an icon the shell will draw.</summary>
    private static IntPtr ToIcon(SKBitmap bitmap, int size)
    {
        var pixels = bitmap.GetPixelSpan();
        var colorBytes = new byte[size * size * 4];
        var maskStride = ((size + 15) / 16) * 2;
        var maskBytes = new byte[maskStride * size];

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var offset = ((y * size) + x) * 4;
                var alpha = pixels[offset + 3];

                if (alpha == 0)
                {
                    // A set mask bit means "let the desktop through", which is the fallback an icon
                    // uses wherever its alpha channel is ignored.
                    maskBytes[(y * maskStride) + (x / 8)] |= (byte)(0x80 >> (x % 8));
                    continue;
                }

                colorBytes[offset] = Unpremultiply(pixels[offset], alpha);
                colorBytes[offset + 1] = Unpremultiply(pixels[offset + 1], alpha);
                colorBytes[offset + 2] = Unpremultiply(pixels[offset + 2], alpha);
                colorBytes[offset + 3] = alpha;
            }
        }

        var header = new Win32BadgeNativeMethods.BitmapInfoHeader
        {
            biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32BadgeNativeMethods.BitmapInfoHeader>(),
            biWidth = size,
            // Negative height makes the DIB top-down, which is the row order Skia produces.
            biHeight = -size,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,
        };

        var color = Win32BadgeNativeMethods.CreateDIBSection(
            IntPtr.Zero,
            ref header,
            Win32BadgeNativeMethods.DibRgbColors,
            out var bits,
            IntPtr.Zero,
            0);

        if (color == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            System.Runtime.InteropServices.Marshal.Copy(colorBytes, 0, bits, colorBytes.Length);

            var mask = Win32BadgeNativeMethods.CreateBitmap(size, size, 1, 1, maskBytes);

            if (mask == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            try
            {
                var info = new Win32BadgeNativeMethods.IconInfo
                {
                    fIcon = true,
                    hbmMask = mask,
                    hbmColor = color,
                };

                return Win32BadgeNativeMethods.CreateIconIndirect(ref info);
            }
            finally
            {
                Win32BadgeNativeMethods.DeleteObject(mask);
            }
        }
        finally
        {
            Win32BadgeNativeMethods.DeleteObject(color);
        }
    }

    /// <summary>Expands one premultiplied colour channel back to straight alpha.</summary>
    private static byte Unpremultiply(byte value, byte alpha) =>
        (byte)Math.Min(255, ((value * 255) + (alpha / 2)) / alpha);
}
