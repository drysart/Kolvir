using SkiaSharp;

namespace Kolvir.InView.Tests;

/// <summary>
/// The predicate itself: what survives the clip chain, and what survives being painted over.
/// </summary>
/// <remarks>
/// Every case is a real headless window, a real layout pass and a rectangle, so the answers come from
/// the same code an application runs. The trees are deliberately small and explicit: a control at a
/// stated rectangle is easier to reason about than a stack of panels that happen to land there.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("EffectiveInView")]
public sealed class EffectiveInViewTests : InViewTestBase
{
    [TestMethod]
    public void AControlScrolledPastTheTopOfAPanelIsNotInView()
    {
        var panel = new Panel
        {
            VerticalScroll = ScrollStyle.Always,
            Padding = new Thickness(0f),
            BackColor = SKColors.White,
        };

        var row = Item(new SKRect(10f, 0f, 70f, 40f));
        panel.Controls.Add(row);
        panel.Controls.Add(Item(new SKRect(0f, 100f, 60f, 300f)));

        using var scene = new InViewScene(panel, 200f, 100f);

        Assert.IsTrue(row.EffectiveInView, "the row starts inside the viewport");
        Assert.IsTrue(panel.CanScrollVertically, "the filler makes the panel scrollable");

        panel.ScrollTo(0f, 80f);

        Assert.IsFalse(row.EffectiveInView, "scrolling does not lay out, and the row is now above the viewport");
    }

    [TestMethod]
    public void AControlPartlyScrolledOutOfAPanelIsInView()
    {
        var panel = new Panel
        {
            VerticalScroll = ScrollStyle.Always,
            Padding = new Thickness(0f),
            BackColor = SKColors.White,
        };

        var row = Item(new SKRect(10f, 0f, 70f, 40f));
        panel.Controls.Add(row);
        panel.Controls.Add(Item(new SKRect(0f, 100f, 60f, 300f)));

        using var scene = new InViewScene(panel, 200f, 100f);

        panel.ScrollTo(0f, 20f);

        Assert.IsTrue(row.EffectiveInView, "twenty pixels of the row are still under the viewport");
    }

    [TestMethod]
    public void AControlBelowAPanelsViewportIsNotInView()
    {
        var panel = new Panel
        {
            VerticalScroll = ScrollStyle.Always,
            Padding = new Thickness(0f),
            BackColor = SKColors.White,
        };

        var row = Item(new SKRect(0f, 250f, 60f, 290f));
        panel.Controls.Add(row);

        using var scene = new InViewScene(panel, 200f, 100f);

        Assert.IsFalse(row.EffectiveInView);
    }

    [TestMethod]
    public void AControlMovedOutsideItsParentsBoundsIsNotInView()
    {
        var root = Root();
        var row = Item(new SKRect(250f, 10f, 290f, 30f));
        root.Controls.Add(row);

        using var scene = new InViewScene(root, 200f, 100f);

        Assert.IsFalse(row.EffectiveInView, "the parent clips everything past its right edge");

        row.Location = new SKPoint(150f, 10f);

        Assert.IsTrue(row.EffectiveInView, "moving it back is seen without a layout pass");
    }

    [TestMethod]
    public void AControlFullyCoveredByAnOpaqueSiblingIsNotInView()
    {
        var root = Root();
        var target = Item(new SKRect(20f, 20f, 80f, 80f));

        root.Controls.Add(target);
        root.Controls.Add(Cover(new SKRect(20f, 20f, 80f, 80f)));

        using var scene = new InViewScene(root, 200f, 100f);

        Assert.IsFalse(target.EffectiveInView);
    }

    [TestMethod]
    public void AControlPartlyCoveredByAnOpaqueSiblingIsInView()
    {
        var root = Root();
        var target = Item(new SKRect(20f, 20f, 80f, 80f));

        root.Controls.Add(target);
        root.Controls.Add(Cover(new SKRect(20f, 20f, 50f, 80f)));

        using var scene = new InViewScene(root, 200f, 100f);

        Assert.IsTrue(target.EffectiveInView, "the right-hand strip is still showing");
    }

    [TestMethod]
    public void AHiddenAncestorMakesEffectiveInViewFalse()
    {
        var root = Root();
        var container = new Panel { Bounds = new SKRect(0f, 0f, 200f, 100f) };
        var target = Item(new SKRect(10f, 10f, 50f, 50f));

        container.Controls.Add(target);
        root.Controls.Add(container);

        using var scene = new InViewScene(root, 200f, 100f);

        Assert.IsTrue(target.EffectiveInView);

        container.Visible = false;

        Assert.IsFalse(target.EffectiveVisible);
        Assert.IsFalse(target.EffectiveInView);
    }

    [TestMethod]
    public void ADisabledOpaqueSiblingStillCovers()
    {
        var root = Root();
        var target = Item(new SKRect(20f, 20f, 80f, 80f));
        var cover = Cover(new SKRect(20f, 20f, 80f, 80f));
        cover.Enabled = false;

        root.Controls.Add(target);
        root.Controls.Add(cover);

        using var scene = new InViewScene(root, 200f, 100f);

        Assert.IsFalse(target.EffectiveInView, "a disabled control still paints, so it still covers");
    }

    [TestMethod]
    public void ATransparentSiblingAboveDoesNotCover()
    {
        var root = Root();
        var target = Item(new SKRect(20f, 20f, 80f, 80f));
        var above = new Panel { Bounds = new SKRect(20f, 20f, 80f, 80f) };

        root.Controls.Add(target);
        root.Controls.Add(above);

        using var scene = new InViewScene(root, 200f, 100f);

        Assert.IsFalse(above.Opaque);
        Assert.IsTrue(target.EffectiveInView, "nothing is painted over the target");
    }

    [TestMethod]
    public void AnOpaqueGrandchildOfATransparentSiblingCovers()
    {
        var root = Root();
        var target = Item(new SKRect(20f, 20f, 80f, 80f));
        var wrapper = new Panel { Bounds = new SKRect(0f, 0f, 120f, 120f) };

        wrapper.Controls.Add(Cover(new SKRect(20f, 20f, 80f, 80f)));
        root.Controls.Add(target);
        root.Controls.Add(wrapper);

        using var scene = new InViewScene(root, 200f, 100f);

        Assert.IsFalse(wrapper.Opaque);
        Assert.IsFalse(target.EffectiveInView);
    }

    [TestMethod]
    public void AnOpaqueOverlayOverARootControlCoversIt()
    {
        var root = Root();
        var target = Item(new SKRect(20f, 20f, 80f, 80f));
        root.Controls.Add(target);

        using var scene = new InViewScene(root, 200f, 100f);

        Assert.IsTrue(target.EffectiveInView);

        scene.Host.AddOverlay(new Panel
        {
            Bounds = new SKRect(0f, 0f, 200f, 100f),
            BackColor = SKColors.Red,
        });

        scene.Layout();

        Assert.IsFalse(target.EffectiveInView);
    }

    [TestMethod]
    public void APanelsScrollBarIsInViewInItsGutterAndAControlUnderTheGutterIsNot()
    {
        var panel = new Panel
        {
            VerticalScroll = ScrollStyle.Always,
            Padding = new Thickness(0f),
            BackColor = SKColors.White,
        };

        var pinned = Item(new SKRect(0f, 0f, 40f, 40f));
        var underGutter = Item(new SKRect(190f, 10f, 230f, 30f));

        panel.Controls.Add(pinned);
        panel.Controls.Add(underGutter);

        using var scene = new InViewScene(panel, 200f, 100f);

        Assert.IsNotNull(panel.VerticalScrollBar);
        Assert.IsTrue(panel.VerticalScrollBar.EffectiveInView, "a bar is painted outside the content viewport");
        Assert.IsFalse(underGutter.EffectiveInView, "the content viewport ends before the gutter");
    }

    [TestMethod]
    public void AChildAddedAfterThePassCoversWithoutAnotherPass()
    {
        var root = Root();
        var target = Item(new SKRect(20f, 20f, 80f, 80f));
        root.Controls.Add(target);

        using var scene = new InViewScene(root, 200f, 100f);

        Assert.IsTrue(target.EffectiveInView);

        root.Controls.Add(Cover(new SKRect(20f, 20f, 80f, 80f)));

        Assert.IsFalse(target.EffectiveInView, "the add invalidated and arranged, and that is enough");
    }

    [TestMethod]
    public void ShowingACoverThatWasHiddenCoversOnTheNextRead()
    {
        var root = Root();
        var target = Item(new SKRect(20f, 20f, 80f, 80f));
        var cover = Cover(new SKRect(20f, 20f, 80f, 80f));

        cover.Visible = false;

        root.Controls.Add(target);
        root.Controls.Add(cover);

        using var scene = new InViewScene(root, 200f, 100f);

        Assert.IsTrue(target.EffectiveInView);

        cover.Visible = true;

        Assert.IsFalse(target.EffectiveInView, "hiding does not lay out, but it does invalidate");
    }

    [TestMethod]
    public void AnUnhostedOrEmptyControlNeverThrows()
    {
        var detached = new Panel();

        Assert.IsFalse(detached.EffectiveInView);

        var root = Root();
        var empty = new Panel { Bounds = SKRect.Empty };
        root.Controls.Add(empty);

        using var scene = new InViewScene(root, 200f, 100f);

        Assert.IsFalse(empty.EffectiveInView);
    }
}
