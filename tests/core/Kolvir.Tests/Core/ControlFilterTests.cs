using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The filter a control paints its subtree through, and the repaint it costs.
/// </summary>
/// <remarks>
/// <para>
/// The margin arithmetic is exact and belongs in a unit test; the painting is sampled the way the
/// rest of the pixel suite samples it - solid fills and one colour that either is there or is not,
/// never text or antialiased edges.
/// </para>
/// <para>
/// The repaint assertions read <see cref="UiHost.PendingUpdateBounds"/> rather than counting pixels,
/// because that is the decision the library actually makes: a filter that changes pixels in place
/// must leave a small invalidation alone, and one that reaches past them must widen it to the whole
/// control.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Pixels")]
public sealed class ControlFilterTests : HeadlessTestBase
{
    [TestMethod]
    public void ABlurReachesThreeSigma()
    {
        using var filter = ControlFilter.Blur(6f);

        Assert.IsTrue(filter.HasMargin, "a blur reaches past the pixels it is given");
        Assert.AreEqual(18f, filter.Margin, "Skia clips the kernel at three sigma");
    }

    [TestMethod]
    public void ABlurOfNothingIsNoFilterAtAll()
    {
        using var filter = ControlFilter.Blur(0f);

        Assert.IsFalse(filter.HasMargin, "a blur of nothing changes nothing");
        Assert.IsNull(filter.ImageFilter, "and it builds no Skia filter, so a control wears no layer");
    }

    [TestMethod]
    public void AChainOfFiltersAddsItsMargins()
    {
        using var blur = ControlFilter.Blur(4f);
        using var tint = ControlFilter.Tint(SKColors.Red);
        using var chain = blur.Then(tint);

        Assert.AreEqual(12f, chain.Margin, "the tint is given the blur's output, so the reach is the same");
    }

    [TestMethod]
    public void AFiltersThatRunSideBySideTakeTheLargestMargin()
    {
        using var soft = ControlFilter.Blur(2f);
        using var wide = ControlFilter.Blur(6f);
        using var merged = ControlFilter.Merge(soft, wide);

        Assert.AreEqual(18f, merged.Margin, "each branch sees the same picture, so the margins do not add");
    }

    [TestMethod]
    public void ADropShadowReachesItsBlurAndItsOffset()
    {
        using var filter = ControlFilter.DropShadow(4f, -7f, 2f, SKColors.Black);

        Assert.AreEqual(13f, filter.Margin, "six pixels of blur and seven of offset downwards");
    }

    [TestMethod]
    public void ATintChangesPixelsInPlace()
    {
        using var filter = ControlFilter.Tint(SKColors.Red);

        Assert.IsFalse(filter.HasMargin, "a tint reads one pixel and writes that pixel");
        Assert.IsNotNull(filter.ImageFilter, "but it is still a filter, so the control still wears a layer");
    }

    [TestMethod]
    public void AFilterCoversTheChildrenInsideTheControlItIsOn()
    {
        using var tint = ControlFilter.Tint(SKColors.Blue, SKBlendMode.Src);

        var stage = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = Thickness.Empty,
            Border = Border.None,
            BackColor = SKColors.White,
            Filter = tint,
        };

        var child = new Panel
        {
            Bounds = new SKRect(20f, 20f, 60f, 40f),
            Border = Border.None,
            BackColor = SKColors.Black,
        };

        stage.Controls.Add(child);
        using var scene = HeadlessScene.Create(stage, 120f, 80f);

        Assert.AreEqual(SKColors.Blue, scene.PixelAt(40, 30), "the child is inside the layer, so it is tinted too");
        Assert.AreEqual(SKColors.Blue, scene.PixelAt(5, 70), "and so is everything the control fills itself");
    }

    [TestMethod]
    public void AFilterLeavesWhatIsOutsideTheControlAlone()
    {
        using var tint = ControlFilter.Tint(SKColors.Blue, SKBlendMode.Src);

        var root = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = Thickness.Empty,
            Border = Border.None,
            BackColor = SKColors.Lime,
        };

        var stage = new Panel
        {
            Bounds = new SKRect(20f, 10f, 100f, 60f),
            Border = Border.None,
            BackColor = SKColors.White,
            Filter = tint,
        };

        root.Controls.Add(stage);
        using var scene = HeadlessScene.Create(root, 120f, 80f);

        Assert.AreEqual(SKColors.Blue, scene.PixelAt(60, 30), "the filtered control paints blue");
        Assert.AreEqual(SKColors.Lime, scene.PixelAt(110, 70), "and everything outside it is untouched");
    }

    [TestMethod]
    public void ABlurSoftensTheEdgeItIsGiven()
    {
        using var blur = ControlFilter.Blur(4f);

        var stage = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = Thickness.Empty,
            Border = Border.None,
            BackColor = SKColors.White,
            Filter = blur,
        };

        var block = new Panel
        {
            Bounds = new SKRect(40f, 20f, 80f, 60f),
            Border = Border.None,
            BackColor = SKColors.Black,
        };

        stage.Controls.Add(block);
        using var scene = HeadlessScene.Create(stage, 120f, 80f);

        Assert.AreEqual(SKColors.Black, scene.PixelAt(60, 40), "the middle of the block is still the block");

        // Two sigma beyond the block's reach, and far enough from the layer's own edge that the blur
        // is not sampling the transparency outside the control.
        Assert.AreEqual(SKColors.White, scene.PixelAt(100, 15), "and the background away from it is untouched");

        var edge = scene.PixelAt(38, 40);
        Assert.AreNotEqual(SKColors.Black, edge, "just outside the block the blur has already lightened it");
        Assert.AreNotEqual(SKColors.White, edge, "and not yet all the way back to the background");
    }

    [TestMethod]
    public void ATintLeavesTheRepaintAsSmallAsItWas()
    {
        using var tint = ControlFilter.Tint(SKColors.Blue, SKBlendMode.Src);
        var (stage, child) = FilteredStage(tint);
        using var scene = HeadlessScene.Create(stage, 120f, 80f);

        scene.Paint();
        child.BackColor = SKColors.Red;

        var expected = HostRect(child);
        Assert.AreEqual(
            expected,
            scene.Host.PendingUpdateBounds,
            "a filter that changes pixels in place must not make the control repaint whole");
    }

    [TestMethod]
    public void ABlurRepaintsTheControlItIsOn()
    {
        using var blur = ControlFilter.Blur(4f);
        var (stage, child) = FilteredStage(blur);
        using var scene = HeadlessScene.Create(stage, 120f, 80f);

        scene.Paint();
        child.BackColor = SKColors.Red;

        Assert.AreEqual(
            HostRect(stage),
            scene.Host.PendingUpdateBounds,
            "a layer cannot be rebuilt in pieces, so the whole control repaints");
    }

    [TestMethod]
    public void ANestedBlurEscalatesToTheOutermostFilteredControl()
    {
        using var blur = ControlFilter.Blur(3f);

        var outer = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = Thickness.Empty,
            Border = Border.None,
            BackColor = SKColors.White,
            Filter = blur,
        };

        var inner = new Panel
        {
            Bounds = new SKRect(30f, 20f, 270f, 140f),
            Padding = Thickness.Empty,
            Border = Border.None,
            BackColor = SKColors.Silver,
            Filter = blur,
        };

        var deep = new Panel
        {
            Bounds = new SKRect(10f, 10f, 50f, 40f),
            Border = Border.None,
            BackColor = SKColors.Black,
        };

        inner.Controls.Add(deep);
        outer.Controls.Add(inner);

        using var scene = HeadlessScene.Create(outer, 300f, 160f);

        scene.Paint();
        deep.BackColor = SKColors.Red;

        Assert.AreEqual(
            HostRect(outer),
            scene.Host.PendingUpdateBounds,
            "the inner blur's output is the outer blur's input, so the outermost one repaints");
    }

    [TestMethod]
    public void APartialRepaintOfATintedControlMatchesAFullOne()    {
        using var tint = ControlFilter.Tint(new SKColor(0x30, 0x60, 0xC0, 0xE0));
        var (stage, child) = FilteredStage(tint);
        using var scene = HeadlessScene.Create(stage, 96f, 64f);

        // A backend that keeps its pixels between frames: the first paint settles the tree, the
        // second covers only what the child's change invalidated.
        scene.Host.PartialUpdates = true;

        var info = new SKImageInfo(96, 64, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var incremental = new SKBitmap(info);

        using (var canvas = new SKCanvas(incremental))
        {
            scene.Host.Paint(canvas, full: true);
            canvas.Flush();
        }

        child.BackColor = SKColors.Red;

        using (var canvas = new SKCanvas(incremental))
        {
            scene.Host.Paint(canvas, full: false);
            canvas.Flush();
        }

        using var whole = scene.Render();

        for (int y = 0; y < info.Height; y++)
        {
            for (int x = 0; x < info.Width; x++)
            {
                Assert.AreEqual(
                    whole.GetPixel(x, y),
                    incremental.GetPixel(x, y),
                    $"the piecemeal repaint differs from a whole frame at ({x},{y})");
            }
        }
    }

    /// <summary>A filled stage with one small child, ready to be filtered.</summary>
    private static (Panel Stage, Panel Child) FilteredStage(ControlFilter filter)
    {
        var stage = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = Thickness.Empty,
            Border = Border.None,
            BackColor = SKColors.White,
            Filter = filter,
        };

        var child = new Panel
        {
            Bounds = new SKRect(20f, 20f, 60f, 40f),
            Border = Border.None,
            BackColor = SKColors.Black,
        };

        stage.Controls.Add(child);
        return (stage, child);
    }

    /// <summary>The control's bounds in host coordinates, which is what a repaint request is in.</summary>
    private static SKRectI HostRect(Control control)
    {
        var origin = control.GetPositionInHost();
        return new SKRectI(
            (int)MathF.Floor(origin.X),
            (int)MathF.Floor(origin.Y),
            (int)MathF.Ceiling(origin.X + control.Width),
            (int)MathF.Ceiling(origin.Y + control.Height));
    }
}
