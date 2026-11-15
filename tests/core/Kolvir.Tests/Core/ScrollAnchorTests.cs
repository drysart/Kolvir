using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// Where a scrolling panel ends up after its content is arranged again: the default keeps the
/// distance from the top, and the anchors override that for a stream pinned to its end or held
/// on a row the user is reading.
/// </summary>
/// <remarks>
/// The content here gets taller as the panel gets narrower, which is what a resize does to wrapped
/// text and therefore to every chat stream. A child that kept its height would make the scroll
/// range hold still and hide whether the anchor did anything at all.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class ScrollAnchorTests : HeadlessTestBase
{
    [TestMethod]
    public void WithNoAnchorTheDistanceFromTheTopIsKept()
    {
        var panel = Scrolling();
        var content = Wrapping();
        panel.Controls.Add(content);

        using var scene = HeadlessScene.Create(panel, 400f, 100f);

        panel.ScrollTo(0f, panel.ScrollExtent.Height);
        Assert.AreEqual(200f, panel.VerticalOffset, 0.5f, "the content starts three rows of 100 tall");

        scene.Resize(220f, 100f);

        Assert.AreEqual(200f, panel.VerticalOffset, 0.5f, "no anchor keeps the offset the panel had");
        Assert.IsTrue(
            VerticalRange(panel) > 200f,
            $"the content should have grown, so {VerticalRange(panel)} should be past the old offset");
    }

    [TestMethod]
    public void BottomScrollAnchorKeepsAPanelAtItsEndAtItsEnd()
    {
        var panel = Scrolling();
        panel.ScrollAnchor = new BottomScrollAnchor();
        var content = Wrapping();
        panel.Controls.Add(content);

        using var scene = HeadlessScene.Create(panel, 400f, 100f);

        panel.ScrollTo(0f, panel.ScrollExtent.Height);
        Assert.AreEqual(200f, panel.VerticalOffset, 0.5f);

        scene.Resize(220f, 100f);

        Assert.AreEqual(
            VerticalRange(panel),
            panel.VerticalOffset,
            0.5f,
            "a panel scrolled to its end should still be at its end, however tall the content became");
        Assert.IsTrue(VerticalRange(panel) > 200f, "and the content should have grown, or this proves nothing");
    }

    [TestMethod]
    public void BottomScrollAnchorLeavesAPanelScrolledInTheMiddleWhereItWas()
    {
        var panel = Scrolling();
        panel.ScrollAnchor = new BottomScrollAnchor();
        var content = Wrapping();
        panel.Controls.Add(content);

        using var scene = HeadlessScene.Create(panel, 400f, 100f);

        panel.ScrollTo(0f, 100f);

        scene.Resize(220f, 100f);

        Assert.AreEqual(100f, panel.VerticalOffset, 0.5f, "a panel that was not at its end keeps its distance from the top");
    }

    [TestMethod]
    public void BottomScrollAnchorFollowsContentThatArrives()
    {
        var panel = Scrolling();
        panel.ScrollAnchor = new BottomScrollAnchor();
        var content = Plain();
        content.Height = 300f;
        panel.Controls.Add(content);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        panel.ScrollTo(0f, 10_000f);
        Assert.AreEqual(200f, panel.VerticalOffset, 0.5f, "the panel starts at its end");

        // A message arrives: the content gets taller without the panel being resized at all.
        content.Height = 500f;
        panel.PerformLayout();

        Assert.AreEqual(400f, panel.VerticalOffset, 0.5f, "the anchor is asked on every layout, so the panel stays at the end");
    }

    [TestMethod]
    public void ControlScrollAnchorHoldsAChosenRowWhereItWasOnScreen()
    {
        var panel = Scrolling();
        var stream = Stream();
        var first = Wrapping();
        var second = Block(200f);
        stream.Controls.Add(first);
        stream.Controls.Add(second);
        panel.Controls.Add(stream);
        panel.ScrollAnchor = new ControlScrollAnchor(_ => second);

        using var scene = HeadlessScene.Create(panel, 400f, 100f);

        // Park the second row just inside the top of the viewport, the way a reader leaves a stream.
        panel.ScrollTo(0f, 280f);
        Assert.AreEqual(20f, second.GetPositionIn(panel).Y, 0.5f, "the row sits 20 px down the viewport");

        scene.Resize(220f, 100f);

        Assert.AreEqual(
            20f,
            second.GetPositionIn(panel).Y,
            0.5f,
            "the row the user was reading stays where it was on screen, however much the rows above it grew");
        Assert.AreEqual(
            second.Bounds.Top + stream.Bounds.Top - 20f,
            panel.VerticalOffset,
            0.5f,
            "which means the panel scrolled to exactly where the row starts, less the 20 px it sits down the viewport");
        Assert.IsTrue(panel.VerticalOffset > 280f, "which means the panel had to scroll further down");
    }

    /// <summary>
    /// A zoomed container scales the position of what is inside it, so a panel that anchors a row
    /// divides the movement back out before it becomes a content-space offset.
    /// </summary>
    [TestMethod]
    public void APositionInAZoomedAncestorIsMeasuredInThatAncestorsOwnSpace()
    {
        var panel = Scrolling();
        panel.ScaleFactor = 2f;
        var content = Plain();
        content.Height = 200f;
        panel.Controls.Add(content);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        panel.ScrollTo(0f, 40f);
        Assert.AreEqual(40f, panel.VerticalOffset, 0.5f);

        // 40 content pixels of scroll is 80 of the panel's own, and the child starts at the origin.
        Assert.AreEqual(-80f, content.GetPositionIn(panel).Y, 0.5f, "the walk applies the zoom of every container it crosses");
    }

    [TestMethod]
    public void ControlScrollAnchorLeavesTheOffsetAloneWhenItsRowIsGone()
    {
        var panel = Scrolling();
        panel.ScrollAnchor = new ControlScrollAnchor(_ => new Panel());

        var content = Plain();
        content.Height = 300f;
        panel.Controls.Add(content);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        panel.ScrollTo(0f, 150f);
        scene.Resize(180f, 100f);

        Assert.AreEqual(150f, panel.VerticalOffset, 0.5f, "a row that is not under the panel anchors nothing");
    }

    [TestMethod]
    public void ClientSizeIsTheContentAreaInTheChildrensSpace()
    {
        var panel = Scrolling();
        panel.Padding = new Thickness(8f);
        panel.VerticalScroll = ScrollStyle.Always;

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        var thickness = ScrollBar.DefaultThickness;

        Assert.AreEqual(200f - thickness - 16f, panel.ClientSize.Width, 0.5f, "the gutter and the padding come out");
        Assert.AreEqual(100f - 16f, panel.ClientSize.Height, 0.5f);
        Assert.AreEqual(200f - thickness, panel.ViewportRect.Width, 0.5f, "the viewport is only the gutter less");
    }

    [TestMethod]
    public void ClientSizeIsHalvedByAScaleFactorLikeEverythingElseTheChildrenSee()
    {
        var panel = Scrolling();
        panel.ScaleFactor = 2f;

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        Assert.AreEqual(100f, panel.ClientSize.Width, 0.5f, "the children lay out in the halved space");
        Assert.AreEqual(50f, panel.ClientSize.Height, 0.5f);
    }

    [TestMethod]
    public void AChildsPositionInAnAncestorIncludesTheScrollOffsetsBetweenThem()
    {
        var panel = Scrolling();
        var stream = Stream();
        var spacer = Block(50f);
        var row = Block(300f);
        stream.Controls.Add(spacer);
        stream.Controls.Add(row);
        panel.Controls.Add(stream);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        panel.ScrollTo(0f, 30f);
        Assert.AreEqual(30f, panel.VerticalOffset, 0.5f, "the content is tall enough to scroll by 30");

        var expected = row.Bounds.Top + stream.Bounds.Top - 30f;
        Assert.AreEqual(expected, row.GetPositionIn(panel).Y, 0.5f, "the row's place on screen is its place in the content, scrolled");
        Assert.AreEqual(stream.Bounds.Top - 30f, stream.GetPositionIn(panel).Y, 0.5f, "and so is the stream's");
        Assert.AreEqual(new SKPoint(0f, 0f), panel.GetPositionIn(panel), "a control is at its own origin");
    }

    [TestMethod]
    public void APositionInSomethingThatIsNotAnAncestorThrows()
    {
        var panel = Scrolling();
        var stray = Plain();

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        Assert.ThrowsExactly<ArgumentException>(() => stray.GetPositionIn(panel));
    }

    private static float VerticalRange(Panel panel) =>
        MathF.Max(0f, panel.ScrollExtent.Height - panel.ClientSize.Height);

    private static Panel Scrolling() => new()
    {
        Padding = Thickness.Empty,
        VerticalScroll = ScrollStyle.Automatic,
    };

    private static Panel Plain() => new() { Padding = Thickness.Empty };

    private static Control Block(float height) => new Fixed(height);

    private static StackPanel Stream() => new() { Padding = Thickness.Empty, AutoSize = true };

    /// <summary>A row that is as tall as it says, whatever room it is offered.</summary>
    private sealed class Fixed : Control
    {
        private readonly float _height;

        public Fixed(float height)
        {
            _height = height;
            AutoSize = true;
        }

        protected override SKSize MeasureContent(SKSize availableSize) => new(100f, _height);
    }

    /// <summary>A row that gets taller as the room it is offered gets narrower, like wrapped text.</summary>
    private static WrappingRow Wrapping() => new();

    /// <summary>
    /// Reports one row of 100 px per 900 px of text it has to fit into, and stretches across
    /// whatever width it is offered.
    /// </summary>
    private sealed class WrappingRow : Control
    {
        public WrappingRow() => AutoSize = true;

        protected override SKSize MeasureContent(SKSize availableSize)
        {
            var width = MathF.Max(1f, MathF.Min(900f, availableSize.Width));
            var lines = MathF.Max(1f, MathF.Ceiling(900f / width));
            return new SKSize(width, lines * 100f);
        }
    }
}
