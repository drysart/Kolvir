using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The handful of visual facts that cannot be expressed as state: which colour a filled control
/// paints, and whether a focused control draws its ring at all.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion here samples a solid fill or searches for one exact palette colour, never text or
/// antialiased edges. That is what keeps the class stable across the three platforms' rasterisers:
/// the same palette role resolves to the same ARGB value everywhere, and the library fills it with
/// no blending.
/// </para>
/// <para>
/// This is also why the suite has no committed golden images. A whole-frame image would pin text
/// rasterisation, which differs by a fraction between platforms even with the font pinned; a
/// semantic colour does not.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Pixels")]
public sealed class PixelInvariantTests : HeadlessTestBase
{
    [TestMethod]
    public void AButtonPaintsItsFillAcrossItsMiddle()
    {
        var button = new Button("Go") { Dock = DockStyle.Fill, CornerRadius = 0f };
        using var scene = HeadlessScene.Create(button, 200f, 60f);

        var fill = scene.PixelAt(FillX(button), (int)button.Height / 2);

        Assert.AreEqual(Theme.Accent, fill, "a primary button's fill is the accent role");
    }

    [TestMethod]
    public void ADisabledButtonPaintsTheDisabledBackground()
    {
        var button = new Button("Go") { Dock = DockStyle.Fill, CornerRadius = 0f };
        using var scene = HeadlessScene.Create(button, 200f, 60f);
        button.Enabled = false;

        var fill = scene.PixelAt(FillX(button), (int)button.Height / 2);

        Assert.AreEqual(Theme.DisabledBackground, fill, "a disabled control is visibly disabled");
    }

    [TestMethod]
    public void AFocusedButtonDrawsItsFocusRing()
    {
        var button = new Button("Go") { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(button, 200f, 60f);

        button.Focus();
        using var bitmap = scene.Render();

        Assert.IsTrue(
            ContainsColour(bitmap, Theme.FocusRing),
            "a focused control should paint the focus ring colour somewhere inside its bounds");
    }

    [TestMethod]
    public void AnUnfocusedButtonDoesNotDrawARing()
    {
        var button = new Button("Go") { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(button, 200f, 60f);

        using var bitmap = scene.Render();

        Assert.IsFalse(
            ContainsColour(bitmap, Theme.FocusRing),
            "nothing focused means no ring");
    }

    [TestMethod]
    public void APanelWithoutAFrameLetsItsAncestorShowThrough()
    {
        var stage = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = Thickness.Empty,
            BorderWidth = 0f,
            Background = Background.FromColor(new SKColor(0xFF102030)),
        };

        var plain = new Panel
        {
            Dock = DockStyle.Fill,
            ShowFrame = false,
            IsCard = false,
            BorderWidth = 0f,
            Padding = Thickness.Empty,
        };
        stage.Controls.Add(plain);

        using var scene = HeadlessScene.Create(stage, 120f, 80f);

        Assert.AreEqual(new SKColor(0xFF102030), scene.PixelAt(60, 40), "a transparent container paints nothing");
    }

    [TestMethod]
    public void TheDarkPaletteChangesWhatASurfacePaints()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = Thickness.Empty,
            BorderWidth = 0f,
            CornerRadius = 0f,
        };

        using var scene = HeadlessScene.Create(panel, 100f, 80f);
        var light = scene.PixelAt(50, 40);

        Theme.Palette = Palette.Dark;
        var dark = scene.PixelAt(50, 40);

        Assert.AreEqual(Theme.Surface, dark, "the next frame should use the dark surface role");
        Assert.AreNotEqual(light, dark, "and the two palettes should not agree");
    }

    [TestMethod]
    public void StretchImageFillsTheBoxAndNormalDoesNot()
    {
        var image = new Image { Dock = DockStyle.Fill, SizeMode = SizeMode.StretchImage };
        image.Source = new ImageDataFactory().LoadFromStream(RedPng(4, 2));

        using var scene = HeadlessScene.Create(image, 100f, 100f);

        Assert.AreEqual(SKColors.Red, scene.PixelAt(2, 2), "a stretched image reaches the corner");

        image.SizeMode = SizeMode.Normal;
        Assert.AreNotEqual(SKColors.Red, scene.PixelAt(2, 2), "a natural-size image is drawn at 4x2 and centred");

        var centre = scene.PixelAt(50, 50);
        Assert.AreEqual(SKColors.Red, centre, "but it is still painted in the middle of the box");
    }

    [TestMethod]
    public void ZoomKeepsTheAspectRatioAndLeavesBands()
    {
        var image = new Image { Dock = DockStyle.Fill, SizeMode = SizeMode.Zoom };
        image.Source = new ImageDataFactory().LoadFromStream(RedPng(4, 2));

        using var scene = HeadlessScene.Create(image, 100f, 100f);

        // 4x2 zoomed into 100x100 becomes 100x50, so the middle row is image and the top row is not.
        Assert.AreEqual(SKColors.Red, scene.PixelAt(50, 50), "the zoomed image covers the middle");
        Assert.AreNotEqual(SKColors.Red, scene.PixelAt(50, 2), "and leaves the bands above and below");
    }

    [TestMethod]
    public void AnImagesSizeModeAndAlignmentPlaceThePictureWhereTheyDid()
    {
        var image = new Image { Dock = DockStyle.Fill, CornerRadius = 0f };
        using var scene = HeadlessScene.Create(image, 100f, 100f);
        image.Source = new ImageDataFactory().LoadFromStream(RedPng(4, 2));

        foreach (var mode in Enum.GetValues<SizeMode>())
        {
            image.SizeMode = mode;

            foreach (var alignment in Enum.GetValues<ContentAlignment>())
            {
                image.ImageAlign = alignment;
                var expected = ExpectedDestination(mode, alignment);
                using var bitmap = scene.Render();

                string where = $"{mode}/{alignment}";
                Assert.AreEqual(
                    SKColors.Red, bitmap.GetPixel((int)expected.Left, (int)expected.Top), $"{where}: the destination's top-left");
                Assert.AreEqual(
                    SKColors.Red, bitmap.GetPixel((int)expected.Right - 1, (int)expected.Bottom - 1), $"{where}: the destination's bottom-right");
                Assert.AreEqual(
                    SKColors.Red,
                    bitmap.GetPixel(
                        (int)(expected.Left + (expected.Width / 2f)),
                        (int)(expected.Top + (expected.Height / 2f))),
                    $"{where}: the destination's middle");

                if (expected.Left > 0f)
                {
                    Assert.AreNotEqual(
                        SKColors.Red,
                        bitmap.GetPixel(0, (int)(expected.Top + (expected.Height / 2f))),
                        $"{where}: nothing is drawn left of the destination");
                }

                if (expected.Top > 0f)
                {
                    Assert.AreNotEqual(
                        SKColors.Red,
                        bitmap.GetPixel((int)(expected.Left + (expected.Width / 2f)), 0),
                        $"{where}: nothing is drawn above the destination");
                }

                if (expected.Right < 100f)
                {
                    Assert.AreNotEqual(
                        SKColors.Red,
                        bitmap.GetPixel(99, (int)(expected.Top + (expected.Height / 2f))),
                        $"{where}: nothing is drawn right of the destination");
                }

                if (expected.Bottom < 100f)
                {
                    Assert.AreNotEqual(
                        SKColors.Red,
                        bitmap.GetPixel((int)(expected.Left + (expected.Width / 2f)), 99),
                        $"{where}: nothing is drawn below the destination");
                }
            }
        }
    }

    [TestMethod]
    public void AnImageWithNoPicturePaintsItsPlaceholder()
    {
        var placeholder = new SKColor(0xFF00C853);
        var image = new Image
        {
            Dock = DockStyle.Fill,
            CornerRadius = 0f,
            PlaceholderColor = placeholder,
        };

        using var scene = HeadlessScene.Create(image, 60f, 40f);

        Assert.AreEqual(placeholder, scene.PixelAt(30, 20), "a control with no picture shows its placeholder");
    }

    [TestMethod]
    public void ARecordedPicturePaintsExactlyWhatADirectImageDrawWould()
    {
        byte[] png = PatternPng(7, 3);

        // The background the image is painted over, from the same control with nothing to draw.
        // A scene owns an application, so it is disposed before the next one is created.
        using var background = RenderBackground();

        var image = new Image { Dock = DockStyle.Fill, CornerRadius = 0f };
        image.Source = new ImageDataFactory().LoadFromBytes(png);
        using var scene = HeadlessScene.Create(image, 101f, 67f);

        using var decoded = SKBitmap.Decode(png);
        using var reference = SKImage.FromBitmap(decoded);

        // 101x67 is deliberately awkward: Zoom scales by a fractional factor and lands on a
        // fractional top, which is where a recording mistake shows as half-pixel seams.
        float zoomScale = MathF.Min(101f / 7f, 67f / 3f);
        float zoomWidth = 7f * zoomScale;
        float zoomHeight = 3f * zoomScale;
        var zoom = new SKRect(
            (101f - zoomWidth) / 2f,
            (67f - zoomHeight) / 2f,
            (101f + zoomWidth) / 2f,
            (67f + zoomHeight) / 2f);

        foreach (var (mode, destination) in new[]
        {
            (SizeMode.StretchImage, new SKRect(0f, 0f, 101f, 67f)),
            (SizeMode.Zoom, zoom),
            (SizeMode.Normal, new SKRect(47f, 32f, 54f, 35f)),
        })
        {
            image.SizeMode = mode;
            using var actual = scene.Render();

            using var expected = new SKBitmap(actual.Info);
            using (var canvas = new SKCanvas(expected))
            {
                canvas.DrawBitmap(
                    background,
                    0f,
                    0f,
                    new SKSamplingOptions(SKFilterMode.Nearest),
                    null);
                using var paint = new SKPaint { IsAntialias = true };
                canvas.DrawImage(
                    reference,
                    destination,
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
                    paint);
                canvas.Flush();
            }

            for (int y = 0; y < actual.Height; y++)
            {
                for (int x = 0; x < actual.Width; x++)
                {
                    Assert.AreEqual(
                        expected.GetPixel(x, y),
                        actual.GetPixel(x, y),
                        $"{mode}: the picture replay differs from a direct draw at ({x},{y})");
                }
            }
        }
    }

    /// <summary>An empty image control's frame, which is what the image is drawn over.</summary>
    private static SKBitmap RenderBackground()
    {
        var empty = new Image { Dock = DockStyle.Fill, CornerRadius = 0f };
        using var scene = HeadlessScene.Create(empty, 101f, 67f);
        return scene.Render();
    }

    /// <summary>Where a 4x2 picture lands in a 100x100 box, for every mode and alignment.</summary>
    private static SKRect ExpectedDestination(SizeMode mode, ContentAlignment alignment)
    {
        float width = mode == SizeMode.StretchImage ? 100f : mode == SizeMode.Zoom ? 100f : 4f;
        float height = mode == SizeMode.StretchImage ? 100f : mode == SizeMode.Zoom ? 50f : 2f;

        bool centerX = alignment is ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter;
        bool rightX = alignment is ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight;
        bool centerY = alignment is ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight;
        bool bottomY = alignment is ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight;

        float left = centerX ? (100f - width) / 2f : rightX ? 100f - width : 0f;
        float top = centerY ? (100f - height) / 2f : bottomY ? 100f - height : 0f;
        return new SKRect(left, top, left + width, top + height);
    }

    /// <summary>A PNG whose pixels differ, so sampling and placement are visible in the output.</summary>
    private static byte[] PatternPng(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bitmap.SetPixel(
                    x,
                    y,
                    new SKColor((byte)(20 + (x * 31)), (byte)(10 + (y * 61)), (byte)(120 + (x * 7) + (y * 11))));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A solid red PNG, so an image assertion is about geometry rather than content.</summary>
    private static MemoryStream RedPng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Red);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// A point inside a button's fill but clear of its centred caption. Sampling the centre lands on
    /// the text, and an antialiased glyph edge blends the fill with the text colour - which is what
    /// the first macOS run caught: the fill was right, the pixel was not fill.
    /// </summary>
    private static int FillX(Control button) => Math.Max(4, (int)(button.Width * 0.1f));

    /// <summary>True when any pixel of <paramref name="bitmap"/> is exactly <paramref name="colour"/>.</summary>
    private static bool ContainsColour(SKBitmap bitmap, SKColor colour)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y) == colour)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
