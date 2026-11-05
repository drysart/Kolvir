using SkiaSharp;
using XPlatUI;

namespace XPlatUI.Tests;

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
        var button = new XPlatButton("Go") { Dock = DockStyle.Fill, CornerRadius = 0f };
        using var scene = HeadlessScene.Create(button, 200f, 60f);

        var fill = scene.PixelAt(FillX(button), (int)button.Height / 2);

        Assert.AreEqual(XPlatTheme.Accent, fill, "a primary button's fill is the accent role");
    }

    [TestMethod]
    public void ADisabledButtonPaintsTheDisabledBackground()
    {
        var button = new XPlatButton("Go") { Dock = DockStyle.Fill, CornerRadius = 0f };
        using var scene = HeadlessScene.Create(button, 200f, 60f);
        button.Enabled = false;

        var fill = scene.PixelAt(FillX(button), (int)button.Height / 2);

        Assert.AreEqual(XPlatTheme.DisabledBackground, fill, "a disabled control is visibly disabled");
    }

    [TestMethod]
    public void AFocusedButtonDrawsItsFocusRing()
    {
        var button = new XPlatButton("Go") { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(button, 200f, 60f);

        button.Focus();
        using var bitmap = scene.Render();

        Assert.IsTrue(
            ContainsColour(bitmap, XPlatTheme.FocusRing),
            "a focused control should paint the focus ring colour somewhere inside its bounds");
    }

    [TestMethod]
    public void AnUnfocusedButtonDoesNotDrawARing()
    {
        var button = new XPlatButton("Go") { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(button, 200f, 60f);

        using var bitmap = scene.Render();

        Assert.IsFalse(
            ContainsColour(bitmap, XPlatTheme.FocusRing),
            "nothing focused means no ring");
    }

    [TestMethod]
    public void APanelWithoutAFrameLetsItsAncestorShowThrough()
    {
        var stage = new XPlatPanel
        {
            Dock = DockStyle.Fill,
            Padding = XPlatThickness.Empty,
            BorderWidth = 0f,
            Background = XPlatBackground.FromColor(new SKColor(0xFF102030)),
        };

        var plain = new XPlatPanel
        {
            Dock = DockStyle.Fill,
            ShowFrame = false,
            IsCard = false,
            BorderWidth = 0f,
            Padding = XPlatThickness.Empty,
        };
        stage.Controls.Add(plain);

        using var scene = HeadlessScene.Create(stage, 120f, 80f);

        Assert.AreEqual(new SKColor(0xFF102030), scene.PixelAt(60, 40), "a transparent container paints nothing");
    }

    [TestMethod]
    public void TheDarkPaletteChangesWhatASurfacePaints()
    {
        var panel = new XPlatPanel
        {
            Dock = DockStyle.Fill,
            Padding = XPlatThickness.Empty,
            BorderWidth = 0f,
            CornerRadius = 0f,
        };

        using var scene = HeadlessScene.Create(panel, 100f, 80f);
        var light = scene.PixelAt(50, 40);

        XPlatTheme.Palette = XPlatPalette.Dark;
        var dark = scene.PixelAt(50, 40);

        Assert.AreEqual(XPlatTheme.Surface, dark, "the next frame should use the dark surface role");
        Assert.AreNotEqual(light, dark, "and the two palettes should not agree");
    }

    /// <summary>
    /// A point inside a button's fill but clear of its centred caption. Sampling the centre lands on
    /// the text, and an antialiased glyph edge blends the fill with the text colour - which is what
    /// the first macOS run caught: the fill was right, the pixel was not fill.
    /// </summary>
    private static int FillX(XPlatControl button) => Math.Max(4, (int)(button.Width * 0.1f));

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
