using SkiaSharp;

namespace Kolvir.Feature.AssetLoading.Http.Tests;

/// <summary>The canned response body: a real PNG, so the loader's decode is the library's real one.</summary>
internal static class TestPng
{
    /// <summary>Two by three pixels, which is enough to tell a decoded image from a failed one.</summary>
    public static byte[] Bytes { get; } = Create();

    private static byte[] Create()
    {
        using var bitmap = new SKBitmap(2, 3);
        bitmap.Erase(SKColors.CornflowerBlue);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }
}
