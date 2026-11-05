using SkiaSharp;
using XPlatUI;

namespace XPlatUI.Tests;

/// <summary>
/// The containers: docking, stacking, table cells, the split container and a panel's scrolling.
/// </summary>
/// <remarks>
/// Geometry assertions use a container with no chrome and no padding, so a number in a failure is a
/// layout decision rather than one of the defaults (an <see cref="XPlatPanel"/> is a card with 8 px
/// of padding until it is told otherwise).
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class ContainerLayoutTests : HeadlessTestBase
{
    [TestMethod]
    public void DockedChildrenTakeTheirEdgeAndFillTakesTheRest()
    {
        var panel = Plain();
        var top = Plain();
        var fill = Plain();
        top.Dock = DockStyle.Top;
        top.Height = 40f;
        fill.Dock = DockStyle.Fill;

        panel.Controls.Add(top);
        panel.Controls.Add(fill);

        using var scene = HeadlessScene.Create(panel, 200f, 200f);

        Assert.AreEqual(new SKRect(0f, 0f, 200f, 40f), top.Bounds);
        Assert.AreEqual(new SKRect(0f, 40f, 200f, 200f), fill.Bounds);
    }

    [TestMethod]
    public void SeveralDockedEdgesAreAppliedInDeclarationOrder()
    {
        var panel = Plain();
        var top = Plain();
        var bottom = Plain();
        var left = Plain();
        var fill = Plain();
        top.Dock = DockStyle.Top;
        top.Height = 20f;
        bottom.Dock = DockStyle.Bottom;
        bottom.Height = 30f;
        left.Dock = DockStyle.Left;
        left.Width = 50f;
        fill.Dock = DockStyle.Fill;

        panel.Controls.Add(top);
        panel.Controls.Add(bottom);
        panel.Controls.Add(left);
        panel.Controls.Add(fill);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        Assert.AreEqual(new SKRect(0f, 0f, 200f, 20f), top.Bounds);
        Assert.AreEqual(new SKRect(0f, 70f, 200f, 100f), bottom.Bounds);
        Assert.AreEqual(new SKRect(0f, 20f, 50f, 70f), left.Bounds);
        Assert.AreEqual(new SKRect(50f, 20f, 200f, 70f), fill.Bounds, "Fill takes what the edges left");
    }

    [TestMethod]
    public void PaddingShrinksTheAreaDockedChildrenArePlacedIn()
    {
        var panel = Plain();
        panel.Padding = new XPlatThickness(10f);
        var fill = Plain();
        fill.Dock = DockStyle.Fill;
        panel.Controls.Add(fill);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        Assert.AreEqual(new SKRect(10f, 10f, 190f, 90f), fill.Bounds);
    }

    [TestMethod]
    public void AVerticalStackRunsChildrenTopToBottomWithTheSpacingBetweenThem()
    {
        var stack = new XPlatStackPanel { Dock = DockStyle.Fill, Spacing = 6f };
        var first = Plain();
        var second = Plain();
        first.Height = 20f;
        second.Height = 30f;
        stack.Controls.Add(first);
        stack.Controls.Add(second);

        using var scene = HeadlessScene.Create(stack, 200f, 200f);

        Assert.AreEqual(new SKRect(0f, 0f, 200f, 20f), first.Bounds);
        Assert.AreEqual(new SKRect(0f, 26f, 200f, 56f), second.Bounds, "the second child starts one gap below the first");

        // The documented rule: the extent is never smaller than the content area, so a stack that
        // does not fill its viewport still reports the viewport.
        Assert.AreEqual(200f, stack.ScrollExtent.Height);
    }

    [TestMethod]
    public void AHorizontalStackRunsChildrenLeftToRight()
    {
        var stack = new XPlatStackPanel { Dock = DockStyle.Fill, Orientation = XPlatStackOrientation.Horizontal, Spacing = 4f };
        var first = Plain();
        var second = Plain();
        first.Width = 30f;
        second.Width = 20f;
        stack.Controls.Add(first);
        stack.Controls.Add(second);

        using var scene = HeadlessScene.Create(stack, 200f, 100f);

        Assert.AreEqual(new SKRect(0f, 0f, 30f, 100f), first.Bounds);
        Assert.AreEqual(new SKRect(34f, 0f, 54f, 100f), second.Bounds);
    }

    [TestMethod]
    public void AStackChildThatDoesNotStretchIsAlignedAcrossTheAxis()
    {
        var stack = new XPlatStackPanel { Dock = DockStyle.Fill };
        var centred = Plain();
        centred.HorizontalAlignment = XPlatAlignment.Center;
        stack.Controls.Add(centred);

        using var scene = HeadlessScene.Create(stack, 200f, 100f);

        // Adding a child runs a layout pass, and a pass on a panel that has not been sized yet
        // arranges it into nothing. Setting the size after the scene exists is what a control that
        // keeps its own size would have, so the assertion is about the alignment and not about when
        // the size was assigned.
        centred.Size = new SKSize(40f, 20f);
        stack.PerformLayout();

        Assert.AreEqual(80f, centred.Left, "a centred child should be centred across the stack");
        Assert.AreEqual(40f, centred.Width);
    }

    [TestMethod]
    public void TwoPercentColumnsSplitTheTableEvenly()
    {
        var table = PlainTable();
        table.AddColumn(XPlatSizeType.Percent, 50f);
        table.AddColumn(XPlatSizeType.Percent, 50f);
        table.AddRow(XPlatSizeType.Percent, 100f);

        var left = Plain();
        var right = Plain();
        left.Dock = DockStyle.Fill;
        right.Dock = DockStyle.Fill;
        table.Add(left, 0, 0);
        table.Add(right, 1, 0);

        using var scene = HeadlessScene.Create(table, 200f, 100f);

        Assert.AreEqual(new SKRect(0f, 0f, 100f, 100f), left.Bounds);
        Assert.AreEqual(new SKRect(100f, 0f, 200f, 100f), right.Bounds);
    }

    [TestMethod]
    public void AnAbsoluteRowAndAPercentRowShareTheHeightTheyWereGiven()
    {
        var table = PlainTable();
        table.AddColumn(XPlatSizeType.Percent, 100f);
        table.AddRow(XPlatSizeType.Absolute, 30f);
        table.AddRow(XPlatSizeType.Percent, 100f);

        var header = Plain();
        var body = Plain();
        header.Dock = DockStyle.Fill;
        body.Dock = DockStyle.Fill;
        table.Add(header, 0, 0);
        table.Add(body, 0, 1);

        using var scene = HeadlessScene.Create(table, 200f, 130f);

        Assert.AreEqual(30f, header.Height, "an absolute row keeps its pixels");
        Assert.AreEqual(new SKRect(0f, 30f, 200f, 130f), body.Bounds, "the percent row takes the rest");
    }

    [TestMethod]
    public void CellPaddingInsetsTheControlInsideItsCell()
    {
        var table = PlainTable();
        table.CellPadding = new XPlatThickness(4f);
        table.AddColumn(XPlatSizeType.Percent, 100f);
        table.AddRow(XPlatSizeType.Percent, 100f);

        var content = Plain();
        content.Dock = DockStyle.Fill;
        table.Add(content, 0, 0);

        using var scene = HeadlessScene.Create(table, 100f, 80f);

        Assert.AreEqual(new SKRect(4f, 4f, 96f, 76f), content.Bounds);
    }

    [TestMethod]
    public void AddRecordsTheCellPosition()
    {
        using var table = new XPlatTableLayoutPanel();
        table.AddColumn(XPlatSizeType.Absolute, 10f);
        table.AddColumn(XPlatSizeType.Absolute, 10f);
        table.AddRow(XPlatSizeType.Absolute, 10f);
        table.AddRow(XPlatSizeType.Absolute, 10f);

        using var child = new XPlatPanel();
        table.Add(child, 1, 0);

        Assert.AreEqual((1, 0), table.GetCellPosition(child));
        Assert.AreEqual(2, table.ColumnCount);
        Assert.AreEqual(2, table.RowCount);
    }

    [TestMethod]
    public void TheSplitContainerPutsPanel1AtTheSplitterDistance()
    {
        var split = new XPlatSplitContainer { Dock = DockStyle.Fill, SplitterDistance = 120f };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        Assert.AreEqual(new SKRect(0f, 0f, 120f, 200f), split.Panel1.Bounds);
        Assert.AreEqual(120f, split.SplitterRect.Left);
        Assert.AreEqual(6f, split.SplitterRect.Width, "the default splitter is 6 px wide");
        Assert.AreEqual(126f, split.Panel2.Left);
        Assert.AreEqual(400f, split.Panel2.Right);
    }

    [TestMethod]
    public void DraggingTheSplitterMovesItAndRaisesItsEvents()
    {
        var split = new XPlatSplitContainer { Dock = DockStyle.Fill, SplitterDistance = 120f };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        int moving = 0, moved = 0, changed = 0;
        split.AddSplitterMovingHandler(_ => moving++);
        split.AddSplitterMovedHandler(_ => moved++);
        split.AddSplitterDistanceChangedHandler(_ => changed++);

        var origin = split.GetPositionInHost();
        var start = new SKPoint(origin.X + split.SplitterRect.MidX, origin.Y + split.SplitterRect.MidY);
        var end = new SKPoint(start.X + 40f, start.Y);

        scene.Host.ProcessMouseMove(start);
        scene.Host.ProcessMouseDown(start);
        Assert.IsTrue(split.IsDragging, "a press on the splitter should start a drag");

        scene.Host.ProcessMouseMove(end);
        Assert.IsTrue(split.IsDragging, "the drag should continue while the button is down");

        scene.Host.ProcessMouseUp(end);

        Assert.IsFalse(split.IsDragging, "releasing should end the drag");
        Assert.AreEqual(160f, split.SplitterDistance, 1f, "the splitter should follow the pointer");
        Assert.IsTrue(moving > 0, "moving should have been raised");
        Assert.AreEqual(1, moved, "moved should be raised once, on release");
        Assert.AreEqual(1, changed, "the distance changed once");
    }

    [TestMethod]
    public void ADragCannotCrossPanel1MinSize()
    {
        var split = new XPlatSplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 120f,
            Panel1MinSize = 100f,
        };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        var origin = split.GetPositionInHost();
        var start = new SKPoint(origin.X + split.SplitterRect.MidX, origin.Y + split.SplitterRect.MidY);

        scene.Host.ProcessMouseMove(start);
        scene.Host.ProcessMouseDown(start);
        scene.Host.ProcessMouseMove(new SKPoint(start.X - 200f, start.Y));
        scene.Host.ProcessMouseUp(new SKPoint(start.X - 200f, start.Y));

        Assert.AreEqual(100f, split.SplitterDistance, 0.5f, "the minimum should win over the pointer");
    }

    [TestMethod]
    public void AFixedSplitterDoesNotDrag()
    {
        var split = new XPlatSplitContainer { Dock = DockStyle.Fill, SplitterDistance = 120f, IsSplitterFixed = true };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        var origin = split.GetPositionInHost();
        var start = new SKPoint(origin.X + split.SplitterRect.MidX, origin.Y + split.SplitterRect.MidY);

        scene.Host.ProcessMouseMove(start);
        scene.Host.ProcessMouseDown(start);
        scene.Host.ProcessMouseMove(new SKPoint(start.X + 40f, start.Y));
        scene.Host.ProcessMouseUp(new SKPoint(start.X + 40f, start.Y));

        Assert.IsFalse(split.IsDragging);
        Assert.AreEqual(120f, split.SplitterDistance);
    }

    [TestMethod]
    public void TheKeyboardNudgesTheSplitterByTheDocumentedStep()
    {
        var split = new XPlatSplitContainer { Dock = DockStyle.Fill, SplitterDistance = 120f };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        split.Focus();

        Assert.IsTrue(scene.Host.ProcessKeyDown(XPlatKey.Right), "the splitter should consume the key");
        Assert.AreEqual(128f, split.SplitterDistance, 0.5f, "Right should move the splitter 8 px");

        scene.Key(XPlatKey.Right);
        Assert.AreEqual(136f, split.SplitterDistance, 0.5f);

        scene.Key(XPlatKey.Left);
        scene.Key(XPlatKey.Left);
        Assert.AreEqual(120f, split.SplitterDistance, 0.5f);
    }

    [TestMethod]
    public void AnAutomaticScrollbarAppearsOnlyWhenTheContentIsLarger()
    {
        var panel = Plain();
        panel.VerticalScroll = XPlatScrollStyle.Automatic;
        var content = Plain();
        content.Bounds = new SKRect(0f, 0f, 100f, 90f);
        panel.Controls.Add(content);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        Assert.IsFalse(panel.CanScrollVertically, "content that fits should not scroll");
        Assert.IsNull(panel.VerticalScrollBar, "and should not create a bar");

        content.Height = 400f;
        panel.PerformLayout();

        Assert.IsTrue(panel.CanScrollVertically, "taller content should make the panel scrollable");
        Assert.IsNotNull(panel.VerticalScrollBar, "and create the bar");
        Assert.AreEqual(400f, panel.ScrollExtent.Height);
    }

    [TestMethod]
    public void AnAlwaysScrollbarIsReservedEvenWhenTheContentFits()
    {
        var panel = Plain();
        panel.VerticalScroll = XPlatScrollStyle.Always;
        var content = Plain();
        content.Bounds = new SKRect(0f, 0f, 50f, 50f);
        panel.Controls.Add(content);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        Assert.IsNotNull(panel.VerticalScrollBar, "Always reserves the gutter");
        Assert.IsFalse(panel.VerticalScrollBar!.IsScrollable, "but the bar is disabled when there is nothing to scroll");
        Assert.IsFalse(panel.CanScrollVertically);
        Assert.AreEqual(200f - XPlatScrollBar.DefaultThickness, panel.ViewportRect.Width, "the gutter is taken out of the viewport");
    }

    [TestMethod]
    public void ScrollingIsClampedToTheScrollableRange()
    {
        var panel = Plain();
        panel.VerticalScroll = XPlatScrollStyle.Automatic;
        var content = Plain();
        content.Height = 400f;
        panel.Controls.Add(content);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        panel.ScrollTo(0f, 10_000f);

        Assert.AreEqual(300f, panel.VerticalOffset, 0.5f, "the offset should stop at extent minus viewport");

        panel.ScrollTo(0f, -50f);
        Assert.AreEqual(0f, panel.VerticalOffset);
    }

    [TestMethod]
    public void TheWheelScrollsAPanelUnderThePointer()
    {
        var panel = Plain();
        panel.VerticalScroll = XPlatScrollStyle.Automatic;
        var content = Plain();
        content.Height = 400f;
        panel.Controls.Add(content);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        bool consumed = scene.WindowBackend.SimulateMouseWheel(new SKPoint(50f, 50f), -48f);

        Assert.IsTrue(consumed, "the panel should consume the turn");
        Assert.AreEqual(48f, panel.VerticalOffset, 0.5f, "one detent should scroll by 48 px");

        scene.WindowBackend.SimulateMouseWheel(new SKPoint(50f, 50f), 48f);
        Assert.AreEqual(0f, panel.VerticalOffset, 0.5f, "the opposite turn should scroll back");
    }

    [TestMethod]
    public void ScrollControlIntoViewBringsAnOffscreenChildIntoTheViewport()
    {
        var panel = Plain();
        panel.VerticalScroll = XPlatScrollStyle.Automatic;
        var content = Plain();
        content.Bounds = new SKRect(0f, 300f, 100f, 380f);
        panel.Controls.Add(content);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        panel.ScrollControlIntoView(content);

        Assert.IsTrue(panel.VerticalOffset > 0f, "the panel should scroll to reach the child");
        Assert.IsTrue(
            panel.VerticalOffset + panel.ViewportRect.Height >= content.Bottom - 0.5f,
            $"the child's bottom ({content.Bottom}) should be inside the viewport (offset {panel.VerticalOffset})");
    }

    [TestMethod]
    public void ADockedChildIsPinnedAndIsNotScrolled()
    {
        var panel = Plain();
        panel.VerticalScroll = XPlatScrollStyle.Always;
        var header = Plain();
        header.Dock = DockStyle.Top;
        header.Height = 20f;
        var content = Plain();
        content.Height = 400f;
        panel.Controls.Add(header);
        panel.Controls.Add(content);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        panel.ScrollTo(0f, 50f);

        Assert.AreEqual(0f, header.Top, "a docked child stays where it was laid out");
        Assert.IsTrue(panel.VerticalOffset > 0f);
    }

    [TestMethod]
    public void ASplitterKeyIsNotConsumedWhenTheSplitterIsFixed()
    {
        var split = new XPlatSplitContainer { Dock = DockStyle.Fill, IsSplitterFixed = true };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        split.Focus();
        bool handled = scene.Host.ProcessKeyDown(XPlatKey.Right);

        Assert.IsFalse(handled, "a fixed splitter should leave the key for the rest of the tree");
        Assert.AreEqual(200f, split.SplitterDistance);
    }

    private static XPlatPanel Plain() => new()
    {
        ShowFrame = false,
        IsCard = false,
        BorderWidth = 0f,
        CornerRadius = 0f,
        Padding = XPlatThickness.Empty,
    };

    private static XPlatTableLayoutPanel PlainTable() => new()
    {
        Dock = DockStyle.Fill,
        ShowFrame = false,
        IsCard = false,
        BorderWidth = 0f,
        CornerRadius = 0f,
        Padding = XPlatThickness.Empty,
        CellPadding = XPlatThickness.Empty,
    };
}
