using SkiaSharp;
using XPlatUI;

namespace XPlatUI.Tests;

/// <summary>
/// <see cref="XPlatPanel.ScaleFactor"/>: a multiplier on the effective scale of the subtree, not
/// the device pixel ratio.
/// </summary>
/// <remarks>
/// Every test here is a behaviour, not an implementation detail: the space the children are
/// measured and arranged in is divided, they paint and hit test at the multiplied scale, damage is
/// mapped back into panel space, and the panel's own chrome - padding, border, scrollbars - keeps
/// its size. The default of 1.0 has to leave every one of those results exactly as they were.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class PanelScaleFactorTests : HeadlessTestBase
{
    private static readonly SKColor Red = new(0xF2, 0x2F, 0x6F);
    private static readonly SKColor Blue = new(0x2F, 0x6F, 0x8F);

    [TestMethod]
    public void TheDefaultScaleFactorLeavesLayoutAndHitTestingUntouched()
    {
        var fill = Solid(Red);
        fill.Dock = DockStyle.Fill;

        var fixedChild = new FixedControl(new SKSize(20f, 10f)) { BackColor = Blue };
        fixedChild.Bounds = new SKRect(10f, 10f, 30f, 20f);

        var panel = Group();
        panel.Padding = new XPlatThickness(8f);
        panel.Controls.Add(fill);
        panel.Controls.Add(fixedChild);

        using var scene = HeadlessScene.Create(panel, 200f, 160f);

        Assert.AreEqual(1f, panel.ScaleFactor, "the default is a multiplier of one");
        Assert.AreEqual(new SKRect(0f, 0f, 200f, 160f), panel.Bounds);
        Assert.AreEqual(new SKRect(8f, 8f, 192f, 152f), fill.Bounds, "a Fill child fills the padded area");
        Assert.AreEqual(new SKRect(10f, 10f, 30f, 20f), fixedChild.Bounds);

        Assert.AreSame(fixedChild, panel.HitTest(new SKPoint(20f, 15f)), "the fixed child is under the point");
        Assert.AreSame(fill, panel.HitTest(new SKPoint(150f, 150f)), "the panel itself is under the point");
        Assert.AreEqual(new SKPoint(20f, 15f), fixedChild.ClientToHost(new SKPoint(10f, 5f)));

        // Measure is unchanged too: the fixed child is 20 x 10 and the panel keeps its 8 px padding.
        var desired = panel.Measure(new SKSize(400f, 400f));
        Assert.AreEqual(new SKSize(36f, 26f), desired);
    }

    [TestMethod]
    public void ADoubleScaleHalvesTheSpaceAFillChildGets()
    {
        var child = Solid(Red);
        child.Dock = DockStyle.Fill;

        var panel = Group();
        panel.ScaleFactor = 2f;
        panel.Controls.Add(child);

        using var scene = HeadlessScene.Create(panel, 100f, 100f);

        Assert.AreEqual(
            new SKRect(0f, 0f, 50f, 50f),
            child.Bounds,
            "the child measures and arranges in the space the panel divides by its multiplier");

        // On screen the panel is still 100 x 100 and the child covers all of it.
        Assert.AreEqual(Red, scene.PixelAt(50, 50));
    }

    [TestMethod]
    public void AFixedChildKeepsItsLogicalSizeAndTakesTwiceTheShareOfTheChildSpace()
    {
        var child = Solid(Red);
        child.Bounds = new SKRect(0f, 0f, 20f, 20f);

        var panel = Group();
        panel.ScaleFactor = 2f;
        panel.Controls.Add(child);

        using var scene = HeadlessScene.Create(panel, 100f, 100f);

        Assert.AreEqual(new SKSize(20f, 20f), child.Size, "a fixed metric stays 20 logical px");
        Assert.AreEqual(50f, panel.ClientRect.Width / panel.ScaleFactor, "in a 50 px child space");

        // 20 logical px at a multiplier of 2 covers 40 device px.
        Assert.AreEqual(Red, scene.PixelAt(39, 39));
        Assert.AreNotEqual(Red, scene.PixelAt(41, 41));
    }

    [TestMethod]
    public void APanelSpacePointAtSixtyLandsOnTheChildAtThirty()
    {
        var child = Solid(Red);
        child.Bounds = new SKRect(0f, 0f, 40f, 40f);

        var panel = Group();
        panel.ScaleFactor = 2f;
        panel.Controls.Add(child);

        using var scene = HeadlessScene.Create(panel, 100f, 100f);

        Assert.AreSame(
            child,
            panel.HitTest(new SKPoint(60f, 60f)),
            "60 panel px is 30 child px, inside a 40 px child that 60 would have missed");
        Assert.AreNotSame(
            child,
            panel.HitTest(new SKPoint(90f, 90f)),
            "and 90 panel px is 45 child px, outside it");
    }

    [TestMethod]
    public void NestedTwoAndAHalfMatchAnUnzoomedPanel()
    {
        var root = Group();

        var outer = Group();
        outer.Bounds = new SKRect(0f, 0f, 100f, 100f);
        outer.ScaleFactor = 2f;

        var middle = Group();
        middle.Dock = DockStyle.Fill;
        middle.ScaleFactor = 0.5f;

        var inner = Group();
        inner.Dock = DockStyle.Fill;

        var leaf = Solid(Red);
        leaf.Dock = DockStyle.Fill;

        inner.Controls.Add(leaf);
        middle.Controls.Add(inner);
        outer.Controls.Add(middle);

        // A single unzoomed panel of the same size, in the same scene, is the reference.
        var flat = Group();
        flat.Bounds = new SKRect(200f, 0f, 300f, 100f);

        var flatLeaf = Solid(Red);
        flatLeaf.Dock = DockStyle.Fill;
        flat.Controls.Add(flatLeaf);

        root.Controls.Add(outer);
        root.Controls.Add(flat);

        using var scene = HeadlessScene.Create(root, 300f, 100f);

        Assert.AreEqual(new SKRect(0f, 0f, 50f, 50f), middle.Bounds);
        Assert.AreEqual(new SKRect(0f, 0f, 100f, 100f), inner.Bounds);
        Assert.AreEqual(new SKRect(0f, 0f, 100f, 100f), leaf.Bounds);
        Assert.AreEqual(1f, leaf.EffectiveScaleMultiplier, 0.0001f);

        Assert.AreEqual(flatLeaf.Bounds, leaf.Bounds, "2.0 then 0.5 cancels out");
    }

    [TestMethod]
    public void AChildInvalidationMapsToTheZoomedPanelDamage()
    {
        var child = Solid(Red);
        child.Bounds = new SKRect(0f, 0f, 50f, 50f);

        var panel = Group();
        panel.ScaleFactor = 2f;
        panel.Controls.Add(child);

        using var scene = HeadlessScene.Create(panel, 100f, 100f);

        // Paint once so the pending damage starts empty.
        scene.Paint();
        Assert.IsFalse(scene.Host.IsUpdatePending, "nothing is pending after a full frame");

        child.Invalidate(new SKRect(10f, 10f, 20f, 20f));

        Assert.AreEqual(
            new SKRectI(20, 20, 40, 40),
            scene.Host.PendingUpdateBounds,
            "the child-space rectangle is multiplied and rounded outwards");
    }

    [TestMethod]
    public void AnAutosizedZoomedPanelReportsItsDesiredSizeMultiplied()
    {
        var child = new FixedControl(new SKSize(20f, 10f));

        var panel = Group();
        panel.Controls.Add(child);

        panel.Measure(new SKSize(1000f, 1000f));
        Assert.AreEqual(new SKSize(20f, 10f), panel.DesiredSize, "an unzoomed panel measures its content as-is");

        panel.ScaleFactor = 2f;
        panel.Measure(new SKSize(1000f, 1000f));

        Assert.AreEqual(
            new SKSize(40f, 20f),
            panel.DesiredSize,
            "the same content at twice the zoom asks for twice the room, so an autosized panel does not collapse");

        // A child that asks for all the room it is offered proves the other half: the room offered
        // to the children is divided by the multiplier before measuring, not only multiplied after.
        var eager = new FillAvailableControl();
        var eagerPanel = Group();
        eagerPanel.ScaleFactor = 2f;
        eagerPanel.Controls.Add(eager);

        Assert.AreEqual(
            new SKSize(100f, 100f),
            eagerPanel.Measure(new SKSize(100f, 100f)),
            "a child given 50 x 50 content space is scaled back to the panel's own 100 x 100");
    }

    [TestMethod]
    public void NonPositiveAndNonFiniteValuesAreRejected()
    {
        var panel = Group();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => panel.ScaleFactor = 0f);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => panel.ScaleFactor = -1f);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => panel.ScaleFactor = float.NaN);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => panel.ScaleFactor = float.PositiveInfinity);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => panel.ScaleFactor = float.NegativeInfinity);

        Assert.AreEqual(1f, panel.ScaleFactor, "a rejected value leaves the last good one in place");
    }

    [TestMethod]
    public void AZoomedPanelInsideAScrollingPanelReportsAndScrollsCorrectly()
    {
        var inner = Group();
        inner.Bounds = new SKRect(0f, 0f, 100f, 300f);
        inner.ScaleFactor = 2f;
        inner.VerticalScroll = XPlatScrollStyle.Automatic;

        var innerContent = Solid(Red);
        innerContent.Bounds = new SKRect(0f, 0f, 40f, 300f);
        inner.Controls.Add(innerContent);

        var outer = Group();
        outer.VerticalScroll = XPlatScrollStyle.Automatic;
        outer.Controls.Add(inner);

        using var scene = HeadlessScene.Create(outer, 100f, 100f);

        Assert.AreEqual(
            new SKSize(100f, 300f),
            outer.ScrollExtent,
            "the outer extent is the zoomed panel's own bounds, in the outer panel's child space");
        Assert.AreEqual(
            300f,
            inner.ScrollExtent.Height,
            "the inner extent is its content's height in the inner panel's child space");
        Assert.AreEqual(
            (100f - inner.ScrollBarThickness) / 2f,
            inner.ScrollExtent.Width,
            0.01f,
            "and its width is the content area the visible bar leaves, in the same space");

        outer.ScrollTo(0f, 100f);
        Assert.AreEqual(100f, outer.VerticalOffset);
        Assert.AreEqual(-100f, inner.GetPositionInHost().Y, 0.01f, "the inner panel scrolled up by 100 px");

        // The inner panel scrolls in its content space: 100 child px is 100 * 2 panel px, and the
        // translation the children are painted through is that panel offset divided back down.
        inner.ScrollTo(0f, 100f);
        Assert.AreEqual(100f, inner.VerticalOffset);
        Assert.AreEqual(new SKPoint(0f, -100f), inner.GetChildOffset(innerContent));
    }

    [TestMethod]
    public void TheEffectiveMultiplierMultipliesThroughThreeLevels()
    {
        var outer = Group();
        outer.ScaleFactor = 2f;

        var middle = Group();
        middle.ScaleFactor = 0.5f;

        var inner = Group();
        inner.ScaleFactor = 3f;

        var leaf = Solid(Red);

        inner.Controls.Add(leaf);
        middle.Controls.Add(inner);
        outer.Controls.Add(middle);

        using var scene = HeadlessScene.Create(outer, 100f, 100f);

        Assert.AreEqual(3f, leaf.EffectiveScaleMultiplier, 0.0001f);
        Assert.AreEqual(1f, outer.EffectiveScaleMultiplier, 0.0001f, "a panel's own multiplier is for its children");
    }

    [TestMethod]
    public void ADropDownInsideAZoomedPanelIsSizedAndPlacedByTheMultiplier()
    {
        var combo = new XPlatComboBox();
        combo.Items.Add("one");
        combo.Items.Add("two");
        combo.Text = "one";
        combo.Bounds = new SKRect(0f, 0f, 100f, 30f);

        var panel = Group();
        panel.ScaleFactor = 2f;
        panel.Controls.Add(combo);

        using var scene = HeadlessScene.Create(panel, 400f, 300f);

        combo.OpenDropDown();

        var wrapper = scene.Host.Overlays.OfType<XPlatPanel>().SingleOrDefault(p => p.ScaleFactor == 2f);
        Assert.IsNotNull(wrapper, "the drop-down is hosted in a zoom wrapper");

        Assert.AreEqual(200f, wrapper.Bounds.Width, 0.01f, "the popup is the field's width on screen");
        Assert.AreEqual(60f, wrapper.Bounds.Top, 0.01f, "and hangs below the field, which is 60 px tall on screen");

        var list = wrapper.Controls[0];
        Assert.AreEqual(wrapper.Width / 2f, list.Width, 0.01f, "its content is laid out at the owner's scale");
        Assert.AreEqual(wrapper.Height / 2f, list.Height, 0.01f);
    }

    [TestMethod]
    public void ARasterImageIsFilteredWhenTheSubtreeShrinksIt()
    {
        var checker = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                checker.SetPixel(x, y, (x + y) % 2 == 0 ? SKColors.White : SKColors.Black);
            }
        }

        using var encoded = new MemoryStream();
        using (var image = SKImage.FromBitmap(checker))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            data.SaveTo(encoded);
        }

        checker.Dispose();
        encoded.Position = 0;

        var picture = new XPlatImageDataFactory().LoadFromStream(encoded);
        var view = new XPlatImage
        {
            Image = picture,
            SizeMode = XPlatSizeMode.StretchImage,
            Bounds = new SKRect(0f, 0f, 8f, 8f),
        };

        var panel = Group();
        panel.ScaleFactor = 0.25f;
        panel.Controls.Add(view);

        using var scene = HeadlessScene.Create(panel, 40f, 40f);
        using var bitmap = scene.Render();

        // Eight checker pixels shrink into one device pixel. Nearest sampling would leave pure black
        // and white; the recorded linear sampling blends them into greys.
        bool blended = false;
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red is > 32 and < 223)
                {
                    blended = true;
                }
            }
        }

        Assert.IsTrue(blended, "the downscaled checker should be filtered, not point sampled");
    }

    [TestMethod]
    public void AContextMenuInsideAZoomedPanelIsSizedAndPlacedByTheMultiplier()
    {
        var target = new XPlatButton("Go") { Bounds = new SKRect(0f, 0f, 80f, 30f) };
        target.ContextMenu = new XPlatContextMenu().Add("Row one");

        var panel = Group();
        panel.ScaleFactor = 2f;
        panel.Controls.Add(target);

        using var scene = HeadlessScene.Create(panel, 400f, 300f);

        scene.Host.ProcessContextMenu(new SKPoint(20f, 10f));

        var wrapper = scene.Host.Overlays.OfType<XPlatPanel>().SingleOrDefault(p => p.ScaleFactor == 2f);
        Assert.IsNotNull(wrapper, "the menu is hosted in a zoom wrapper");
        Assert.AreEqual(2f, wrapper.Controls[0].EffectiveScaleMultiplier, 0.0001f);
        Assert.AreEqual(wrapper.Width / 2f, wrapper.Controls[0].Width, 0.01f, "the menu view fills the content space");
        Assert.IsTrue(wrapper.Width > 40f, "and the popup is wider than the unzoomed menu would be");
    }

    private static XPlatPanel Group() => new()
    {
        ShowFrame = false,
        IsCard = false,
        BackColor = null,
        BorderWidth = 0f,
        CornerRadius = 0f,
        Padding = new XPlatThickness(0f),
    };

    private static XPlatControl Solid(SKColor color) => new XPlatControl
    {
        BackColor = color,
        BorderWidth = 0f,
        CornerRadius = 0f,
    };

    private sealed class FixedControl(SKSize size) : XPlatControl
    {
        protected override SKSize MeasureContent(SKSize availableSize) => size;
    }

    private sealed class FillAvailableControl : XPlatControl
    {
        protected override SKSize MeasureContent(SKSize availableSize) => availableSize;
    }
}
