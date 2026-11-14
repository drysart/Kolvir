using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The inherited appearance model, ported from the demo application's <c>--appearance-check</c>
/// harness.
/// </summary>
/// <remarks>
/// <para>
/// A control's font, foreground and background resolve through the control itself, then the nearest
/// ancestor that assigns one, then the class default from <see cref="Theme"/>. Every test that
/// changes the appearance at runtime does so after the controls are built and attached, because that
/// is the behaviour being asserted: nothing may be captured when a control is constructed.
/// </para>
/// <para>
/// Fonts come from <see cref="Font.FromBytes"/> so the widths do not depend on which faces the
/// machine has. The two tests that paint a pixel sample a solid fill, not text, which is what keeps
/// them stable across the three platforms' rasterisers.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class InheritedAppearanceTests : HeadlessTestBase
{
    private const string Sample = "Inherited appearance";
    private const float Width = 480f;
    private const float Height = 360f;

    private static readonly SKColor AncestorColour = new(0xFFE8EEF9);
    private static readonly SKColor OverrideColour = new(0xFF2F6F4F);

    [TestMethod]
    public void FontAndColourResolveThroughAChainOfNestedPanels()
    {
        WithStage((root, _) =>
        {
            using var outerFont = Font.FromBytes(TestFont.Data, 17f);

            var outer = new Panel { Dock = DockStyle.Fill, Font = outerFont, Foreground = AncestorColour };
            var middle = NoChrome();
            var label = new Label(Sample) { Dock = DockStyle.Top, AutoSize = true };
            var outsider = new Label(Sample) { Dock = DockStyle.Top, AutoSize = true };

            middle.Controls.Add(label);
            outer.Controls.Add(middle);
            root.Controls.Add(outer);
            root.Controls.Add(outsider);

            Assert.AreSame(outerFont, label.Font, "the leaf should inherit the ancestor's font");
            Assert.AreSame(outerFont, middle.Font, "an unassigning container should inherit it too");
            Assert.AreEqual(AncestorColour, label.Foreground, "the leaf should inherit the foreground");
            Assert.AreEqual(AncestorColour, middle.Foreground);

            Assert.AreSame(Theme.DefaultFont, outsider.Font, "a control outside the chain keeps the class default");
            Assert.AreEqual(
                Theme.ColorOf(ThemeColor.Foreground),
                outsider.Foreground,
                "and the theme's foreground");
        });
    }

    [TestMethod]
    public void AnOverrideAnswersForEveryControlUnderIt()
    {
        WithStage((root, _) =>
        {
            using var outerFont = Font.FromBytes(TestFont.Data, 17f);
            using var innerFont = Font.FromBytes(TestFont.Data, 11f);

            var outer = new Panel { Dock = DockStyle.Fill, Font = outerFont, Foreground = AncestorColour };
            var middle = NoChrome();
            var deep = new Label(Sample) { Dock = DockStyle.Top, AutoSize = true };
            var stayed = new Label(Sample) { Dock = DockStyle.Top, AutoSize = true };

            middle.Controls.Add(deep);
            outer.Controls.Add(middle);
            outer.Controls.Add(stayed);
            root.Controls.Add(outer);

            float before = deep.DesiredSize.Width;

            middle.Font = innerFont;
            middle.Foreground = OverrideColour;

            float after = deep.DesiredSize.Width;

            Assert.AreSame(innerFont, deep.Font, "the nearer assignment should win");
            Assert.AreEqual(OverrideColour, deep.Foreground);
            Assert.AreSame(outerFont, stayed.Font, "a sibling outside the override keeps the outer font");
            Assert.AreEqual(AncestorColour, stayed.Foreground);

            middle.ClearFont();

            Assert.AreSame(outerFont, deep.Font, "clearing the override should fall back to the ancestor");
            Assert.AreSame(deep.Font, stayed.Font);

            Assert.AreNotEqual(before, after, "re-measuring at 11 pt should change the width");
        });
    }

    [TestMethod]
    public void AControlThatPaintsNoBackgroundDrawsNothingOverAColouredAncestor()
    {
        WithStage((root, host) =>
        {
            var stage = new Panel
            {
                Dock = DockStyle.Fill,
                Background = Background.FromColor(AncestorColour),
                Padding = new Thickness(0f),
            };

            var label = new Label("no box") { Dock = DockStyle.Top, AutoSize = false, Height = 60f };
            var card = new Panel { Dock = DockStyle.Top, Height = 80f };
            card.Controls.Add(new Label("a panel that paints"));

            stage.Controls.Add(label);
            stage.Controls.Add(card);
            root.Controls.Add(stage);

            Assert.IsTrue(label.Background.IsNone, "a label should paint no background of its own");
            Assert.IsNull(label.BackColor);

            // A panel is a container, not a card: with nothing assigned it paints nothing either,
            // so the stage's colour reaches the pixel the same way the label's does.
            Assert.IsTrue(card.Background.IsNone, "an unassigned panel should paint no background");
            Assert.IsNull(card.BackColor);

            var labelArea = Pixel(host, (int)label.Bounds.Left + 300, (int)label.Bounds.MidY);
            var cardArea = Pixel(host, (int)card.Bounds.Left + 300, (int)card.Bounds.MidY);

            Assert.AreEqual(AncestorColour, labelArea, "the label should let the stage's colour through");
            Assert.AreEqual(AncestorColour, cardArea, "and the card should paint the same colour");
        });
    }

    [TestMethod]
    public void AThemeFontChangedAfterConstructionRemeasuresAndRelaysOut()
    {
        WithStage((root, host) =>
        {
            var stack = new StackPanel { Dock = DockStyle.Fill };
            var label = new Label(Sample) { AutoSize = true };
            stack.Controls.Add(label);
            root.Controls.Add(stack);

            float before = label.DesiredSize.Width;
            string beforeFace = label.Font.FamilyName;

            // The same call an application makes at any point in its life, long after its controls
            // exist. Nothing here lays the tree out: the next frame is what notices.
            using (TestFont.Pin())
            {
                string afterFace = label.Font.FamilyName;
                Paint(host);
                float after = label.DesiredSize.Width;

                Assert.AreNotEqual(beforeFace, afterFace, "the live label should adopt the loaded face");
                Assert.AreNotEqual(before, after, "and re-measure at the new face");
            }
        });
    }

    [TestMethod]
    public void AFontAssignedToALiveSubtreeRemeasuresItAtOnce()
    {
        WithStage((root, _) =>
        {
            using var small = Font.FromBytes(TestFont.Data, 9f);
            using var large = Font.FromBytes(TestFont.Data, 26f);

            var stack = new StackPanel { Dock = DockStyle.Fill, Font = small };
            var label = new Label(Sample) { AutoSize = true };
            stack.Controls.Add(label);
            root.Controls.Add(stack);

            float before = label.DesiredSize.Width;
            float heightBefore = label.Bounds.Height;

            // No paint in between: assigning a font has to invalidate the measure itself.
            stack.Font = large;

            float after = label.DesiredSize.Width;

            Assert.AreSame(large, label.Font, "the subtree should inherit the new font");
            Assert.IsTrue(after > before, "the wider face should measure wider without a frame in between");
            Assert.IsTrue(label.Bounds.Height > heightBefore, "and the layout should have been re-run");
        });
    }

    [TestMethod]
    public void AControlBuiltBeforeTheThemeWasConfiguredAdoptsIt()
    {
        WithStage((root, _) =>
        {
            // Built and measured while the theme is still the platform default.
            var label = new Label(Sample) { AutoSize = true };
            var size = new SKSize(float.PositiveInfinity, float.PositiveInfinity);
            label.Measure(size);

            string beforeFace = label.Font.FamilyName;
            float before = label.DesiredSize.Width;

            using (TestFont.Pin())
            {
                string pinned = Theme.DefaultFont.FamilyName;
                label.Measure(size);
                float after = label.DesiredSize.Width;

                Assert.AreEqual(pinned, label.Font.FamilyName, "the existing control should adopt the pinned face");
                Assert.AreNotEqual(beforeFace, pinned, "which should be a different face from the platform's");
                Assert.AreNotEqual(before, after, "and the width should follow the face");
            }
        });
    }

    [TestMethod]
    public void APaletteSwapRecoloursALiveControlWithNoRebuild()
    {
        WithStage((root, host) =>
        {
            var panel = new Panel
            {
                Dock = DockStyle.Top,
                Height = Height,
                Background = Background.FromThemeColor(ThemeColor.Surface),
            };
            var label = new Label(Sample) { Dock = DockStyle.Top, AutoSize = true };
            panel.Controls.Add(label);
            root.Controls.Add(panel);

            var lightSurface = panel.Background.Color;
            var lightText = label.Foreground;

            Assert.AreEqual(Theme.Surface, lightSurface, "a panel reads the palette role, not a captured colour");
            Assert.AreEqual(Theme.Foreground, lightText);

            Theme.Palette = Palette.Dark;

            var darkSurface = panel.Background.Color;
            var darkText = label.Foreground;

            Assert.AreEqual(Theme.Surface, darkSurface, "the same control should follow the dark palette");
            Assert.AreEqual(Theme.Foreground, darkText);
            Assert.AreNotEqual(lightSurface, darkSurface, "the two palettes should differ");

            // The same object, repainted: the role it was given is resolved while painting.
            var painted = Pixel(host, 20, (int)panel.Bounds.MidY);
            Assert.AreEqual(Theme.Surface, painted, "the next frame should use the new palette");
        });
    }

    /// <summary>
    /// Builds a full-window stage and runs <paramref name="body"/> against it. The stage is an
    /// invisible container, so a test starts from an ordinary parent rather than from window chrome.
    /// </summary>
    private static void WithStage(Action<Panel, UiHost> body)
    {
        var root = NoChrome();
        using var scene = HeadlessScene.Create(root, Width, Height);

        body(root, scene.Host);
    }

    /// <summary>An invisible container: the ordinary parent of the controls a test is about.</summary>
    private static Panel NoChrome() => new()
    {
        Dock = DockStyle.Fill,
        Padding = new Thickness(0f),
    };

    /// <summary>Paints the whole tree and returns the colour at one logical pixel.</summary>
    private static SKColor Pixel(UiHost host, int x, int y)
    {
        using var bitmap = new SKBitmap((int)Width, (int)Height);
        using var canvas = new SKCanvas(bitmap);
        host.Paint(canvas, full: true);
        canvas.Flush();
        return bitmap.GetPixel(Math.Clamp(x, 0, (int)Width - 1), Math.Clamp(y, 0, (int)Height - 1));
    }

    /// <summary>Paints the whole tree without looking at a pixel: the frame a change is measured on.</summary>
    private static void Paint(UiHost host)
    {
        using var bitmap = new SKBitmap((int)Width, (int)Height);
        using var canvas = new SKCanvas(bitmap);
        host.Paint(canvas, full: true);
        canvas.Flush();
    }
}
