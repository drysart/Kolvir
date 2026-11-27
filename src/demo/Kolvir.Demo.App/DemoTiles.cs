using SkiaSharp;

namespace Kolvir.Demo;

/// <summary>
/// The pictures the asset-loading page's synthetic URIs resolve to: numbered tiles the sample draws
/// itself, so the page is deterministic and has no network behind it.
/// </summary>
/// <remarks>
/// <para>
/// The bytes are made here rather than stored beside the page, for the same reason
/// <see cref="DemoIcon"/> makes its own and <see cref="DemoGif"/> keeps its file in the assembly: a
/// render has to work from any working directory, and a tile says which row it belongs to, which a
/// shared stock picture could not.
/// </para>
/// <para>
/// Each tile is encoded once and kept, because a retry asks for the same row's bytes again. The
/// factory calls this from its fetch thread, so the cache is locked rather than trusted.
/// </para>
/// </remarks>
internal static class DemoTiles
{
    private static readonly SKColor[] Palette =
    [
        new(0xFF2F6FED),
        new(0xFF7C3AED),
        new(0xFFEC4899),
        new(0xFF0EA5E9),
        new(0xFF10B981),
        new(0xFFF59E0B),
    ];

    private static readonly Dictionary<int, byte[]> Cache = [];

    /// <summary>The PNG bytes for one row's tile.</summary>
    /// <param name="row">The row number the tile shows. It is only used to pick a colour and draw
    /// the digit, so any integer is a valid tile.</param>
    /// <returns>An encoded 160 by 96 PNG.</returns>
    public static byte[] Png(int row)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(row, out byte[]? bytes))
            {
                bytes = Encode(row);
                Cache.Add(row, bytes);
            }

            return bytes;
        }
    }

    private static byte[] Encode(int row)
    {
        var info = new SKImageInfo(160, 96, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        int index = ((row % Palette.Length) + Palette.Length) % Palette.Length;
        canvas.Clear(Palette[index]);

        using (var paint = new SKPaint { Color = new SKColor(0x33FFFFFF), IsAntialias = true })
        {
            canvas.DrawCircle(34f, 30f, 38f, paint);
        }

        using (var font = new SKFont(SKTypeface.Default, 36f) { Edging = SKFontEdging.Antialias })
        using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.DrawText($"{row:00}", 12f, 76f, SKTextAlign.Left, font, paint);
        }

        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
