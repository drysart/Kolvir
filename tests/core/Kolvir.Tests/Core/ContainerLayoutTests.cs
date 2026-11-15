using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The containers: docking, stacking, table cells, the split container and a panel's scrolling.
/// </summary>
/// <remarks>
/// Geometry assertions use a container with no chrome and no padding, so a number in a failure is a
/// layout decision rather than one of the defaults (an <see cref="Panel"/> is a card with 8 px
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
        panel.Padding = new Thickness(10f);
        var fill = Plain();
        fill.Dock = DockStyle.Fill;
        panel.Controls.Add(fill);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        Assert.AreEqual(new SKRect(10f, 10f, 190f, 90f), fill.Bounds);
    }

    [TestMethod]
    public void AVerticalStackRunsChildrenTopToBottomWithTheSpacingBetweenThem()
    {
        var stack = new StackPanel { Dock = DockStyle.Fill, Spacing = 6f };
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
        var stack = new StackPanel { Dock = DockStyle.Fill, Orientation = StackOrientation.Horizontal, Spacing = 4f };
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
        var stack = new StackPanel { Dock = DockStyle.Fill };
        var centred = Plain();
        centred.HorizontalAlignment = Alignment.Center;
        stack.Controls.Add(centred);

        using var scene = HeadlessScene.Create(stack, 200f, 100f);

        // Adding a child runs a layout pass, and a pass on a panel that has not been sized yet
        // arranges it into nothing. Setting the size after the scene exists is what a control that
        // keeps its own size would have, so the assertion is about the alignment and not about when
        // the size was assigned.
        centred.Size = new SKSize(40f, 20f);
        stack.PerformLayout();

        Assert.AreEqual(80f, (float)centred.Left, "a centred child should be centred across the stack");
        Assert.AreEqual(40f, (float)centred.Width);
    }

    [TestMethod]
    public void TwoPercentColumnsSplitTheTableEvenly()
    {
        var table = PlainTable();
        table.AddColumn(SizeType.Percent, 50f);
        table.AddColumn(SizeType.Percent, 50f);
        table.AddRow(SizeType.Percent, 100f);

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
        table.AddColumn(SizeType.Percent, 100f);
        table.AddRow(SizeType.Absolute, 30f);
        table.AddRow(SizeType.Percent, 100f);

        var header = Plain();
        var body = Plain();
        header.Dock = DockStyle.Fill;
        body.Dock = DockStyle.Fill;
        table.Add(header, 0, 0);
        table.Add(body, 0, 1);

        using var scene = HeadlessScene.Create(table, 200f, 130f);

        Assert.AreEqual(30f, (float)header.Height, "an absolute row keeps its pixels");
        Assert.AreEqual(new SKRect(0f, 30f, 200f, 130f), body.Bounds, "the percent row takes the rest");
    }

    [TestMethod]
    public void CellPaddingInsetsTheControlInsideItsCell()
    {
        var table = PlainTable();
        table.CellPadding = new Thickness(4f);
        table.AddColumn(SizeType.Percent, 100f);
        table.AddRow(SizeType.Percent, 100f);

        var content = Plain();
        content.Dock = DockStyle.Fill;
        table.Add(content, 0, 0);

        using var scene = HeadlessScene.Create(table, 100f, 80f);

        Assert.AreEqual(new SKRect(4f, 4f, 96f, 76f), content.Bounds);
    }

    [TestMethod]
    public void AddRecordsTheCellPosition()
    {
        using var table = new TableLayoutPanel();
        table.AddColumn(SizeType.Absolute, 10f);
        table.AddColumn(SizeType.Absolute, 10f);
        table.AddRow(SizeType.Absolute, 10f);
        table.AddRow(SizeType.Absolute, 10f);

        using var child = new Panel();
        table.Add(child, 1, 0);

        Assert.AreEqual((1, 0), table.GetCellPosition(child));
        Assert.AreEqual(2, table.ColumnCount);
        Assert.AreEqual(2, table.RowCount);
    }

    [TestMethod]
    public void TheSplitContainerPutsPanel1AtTheSplitterDistance()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 120f };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        Assert.AreEqual(new SKRect(0f, 0f, 120f, 200f), split.Panel1.Bounds);
        Assert.AreEqual(120f, split.SplitterRect.Left);
        Assert.AreEqual(6f, split.SplitterRect.Width, "the default splitter is 6 px wide");
        Assert.AreEqual(126f, (float)split.Panel2.Left);
        Assert.AreEqual(400f, split.Panel2.Right);
    }

    [TestMethod]
    public void DraggingTheSplitterMovesItAndRaisesItsEvents()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 120f };

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
        var split = new SplitContainer
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
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 120f, IsSplitterFixed = true };

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
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 120f };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        split.Focus();

        Assert.IsTrue(scene.Host.ProcessKeyDown(Key.Right), "the splitter should consume the key");
        Assert.AreEqual(128f, split.SplitterDistance, 0.5f, "Right should move the splitter 8 px");

        scene.Key(Key.Right);
        Assert.AreEqual(136f, split.SplitterDistance, 0.5f);

        scene.Key(Key.Left);
        scene.Key(Key.Left);
        Assert.AreEqual(120f, split.SplitterDistance, 0.5f);
    }

    [TestMethod]
    public void AnAutomaticScrollbarAppearsOnlyWhenTheContentIsLarger()
    {
        var panel = Plain();
        panel.VerticalScroll = ScrollStyle.Automatic;
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
        panel.VerticalScroll = ScrollStyle.Always;
        var content = Plain();
        content.Bounds = new SKRect(0f, 0f, 50f, 50f);
        panel.Controls.Add(content);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        Assert.IsNotNull(panel.VerticalScrollBar, "Always reserves the gutter");
        Assert.IsFalse(panel.VerticalScrollBar!.IsScrollable, "but the bar is disabled when there is nothing to scroll");
        Assert.IsFalse(panel.CanScrollVertically);
        Assert.AreEqual(200f - ScrollBar.DefaultThickness, panel.ViewportRect.Width, "the gutter is taken out of the viewport");
    }

    [TestMethod]
    public void ScrollingIsClampedToTheScrollableRange()
    {
        var panel = Plain();
        panel.VerticalScroll = ScrollStyle.Automatic;
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
        panel.VerticalScroll = ScrollStyle.Automatic;
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
        panel.VerticalScroll = ScrollStyle.Automatic;
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
        panel.VerticalScroll = ScrollStyle.Always;
        var header = Plain();
        header.Dock = DockStyle.Top;
        header.Height = 20f;
        var content = Plain();
        content.Height = 400f;
        panel.Controls.Add(header);
        panel.Controls.Add(content);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);

        panel.ScrollTo(0f, 50f);

        Assert.AreEqual(0f, (float)header.Top, "a docked child stays where it was laid out");
        Assert.IsTrue(panel.VerticalOffset > 0f);
    }

    [TestMethod]
    public void ASplitterKeyIsNotConsumedWhenTheSplitterIsFixed()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, IsSplitterFixed = true };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        split.Focus();
        bool handled = scene.Host.ProcessKeyDown(Key.Right);

        Assert.IsFalse(handled, "a fixed splitter should leave the key for the rest of the tree");
        Assert.AreEqual(200f, split.SplitterDistance);
    }

    [TestMethod]
    public void TheFirstPanelKeepsItsWidthWhenTheContainerGrows()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 120f };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        Assert.AreEqual(FixedPanel.Panel1, split.FixedPanel, "the first panel is fixed unless asked otherwise");

        scene.Resize(500f, 200f);

        Assert.AreEqual(120f, split.SplitterDistance, 0.5f, "the splitter should not have moved");
        Assert.AreEqual(374f, split.Panel2.Width, 0.5f, "the second panel took the extra hundred pixels");
    }

    [TestMethod]
    public void AFixedSecondPanelKeepsItsWidthAndTheFirstPanelTakesTheDifference()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 120f,
            FixedPanel = FixedPanel.Panel2,
        };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        float second = split.Panel2.Width;
        Assert.AreEqual(274f, second, 0.5f, "400 less the 120 and the 6 px splitter");

        scene.Resize(500f, 200f);

        Assert.AreEqual(second, split.Panel2.Width, 0.5f, "the second panel kept the width it had");
        Assert.AreEqual(220f, split.SplitterDistance, 0.5f, "so the first panel is what grew");

        scene.Resize(320f, 200f);

        Assert.AreEqual(second, split.Panel2.Width, 0.5f, "and it keeps it when the container shrinks too");
        Assert.AreEqual(40f, split.SplitterDistance, 0.5f);
    }

    [TestMethod]
    public void AFixedSecondPanelKeepsItsHeightInAHorizontalSplit()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 80f,
            FixedPanel = FixedPanel.Panel2,
        };

        using var scene = HeadlessScene.Create(split, 400f, 300f);

        float second = split.Panel2.Height;
        Assert.AreEqual(214f, second, 0.5f, "300 less the 80 and the 6 px splitter");

        scene.Resize(400f, 400f);

        Assert.AreEqual(second, split.Panel2.Height, 0.5f, "the height is the axis that matters");
        Assert.AreEqual(180f, split.SplitterDistance, 0.5f);
    }

    /// <summary>
    /// A split built while the window is small, with the second panel sized explicitly, still gives
    /// the second panel that size when the window grows.
    /// </summary>
    [TestMethod]
    public void AFixedPanelSizeSurvivesBeingSetBeforeTheWindowIsBig()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Panel1MinSize = 100f,
            Panel2MinSize = 100f,
            FixedPanel = FixedPanel.Panel2,
            FixedPanelSize = 300f,
        };

        using var scene = HeadlessScene.Create(split, 200f, 200f);

        scene.Resize(1010f, 200f);

        Assert.AreEqual(300f, split.Panel2.Width, 0.5f, "the fixed panel keeps the size it was given");
        Assert.AreEqual(704f, split.Panel1.Width, 0.5f, "and the first panel took everything else");
        Assert.AreEqual(300f, split.FixedPanelSize, 0.5f, "which is what the property reads back");
    }

    /// <summary>
    /// A cell gives its whole area to a child that docks into it, and leaves a child that does not
    /// at the size it has - which is what makes <c>Dock</c> the way to fill a cell, and what a
    /// wrapping label in a cell needs if it is not to fill its own lines into a narrow column.
    /// </summary>
    [TestMethod]
    public void ATableCellGivesItsWholeAreaToADockedChildAndNoMoreToTheRest()
    {
        var docked = new Panel { Dock = DockStyle.Fill, TableColumn = 0, TableRow = 0 };
        var loose = new Panel { Bounds = new SKRect(0f, 0f, 40f, 20f), TableColumn = 1, TableRow = 0 };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            CellPadding = Thickness.Empty,
            ColumnStyles = { ColumnStyle.Percent(50f), ColumnStyle.Percent(50f) },
            RowStyles = { RowStyle.Percent(100f) },
            Controls = { docked, loose },
        };

        using var scene = HeadlessScene.Create(table, 400f, 200f);

        Assert.AreEqual(200f, docked.Width, 0.5f, "a docked child gets the whole cell");
        Assert.AreEqual(200f, docked.Height, 0.5f);
        Assert.AreEqual(40f, loose.Width, 0.5f, "and one that is not docked keeps its own size in it");
    }

    [TestMethod]
    public void WithNoFixedPanelBothPanelsTakePartInAResize()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 120f,
            FixedPanel = FixedPanel.None,
        };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        float ratio = split.SplitterDistance / (400f - 6f);

        scene.Resize(800f, 200f);

        float after = split.SplitterDistance / (800f - 6f);
        Assert.AreEqual(ratio, after, 0.01f, "the split should keep its share of the container");
        Assert.IsTrue(split.Panel1.Width > 400f * 0.5f, "which means the first panel grew as well");
    }

    [TestMethod]
    public void AFixedSecondPanelGivesWayWhenThereIsNoRoomForBoth()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 120f,
            FixedPanel = FixedPanel.Panel2,
            Panel1MinSize = 100f,
            Panel2MinSize = 60f,
        };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        scene.Resize(180f, 200f);

        Assert.AreEqual(100f, split.Panel1.Width, 0.5f, "the first panel keeps its minimum");
        Assert.AreEqual(74f, split.Panel2.Width, 0.5f, "and the second takes what is left");
    }

    [TestMethod]
    public void DraggingStillSetsTheSplitWhenTheSecondPanelIsFixed()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 120f,
            FixedPanel = FixedPanel.Panel2,
        };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        var origin = split.GetPositionInHost();
        var start = new SKPoint(origin.X + split.SplitterRect.MidX, origin.Y + split.SplitterRect.MidY);

        scene.Host.ProcessMouseMove(start);
        scene.Host.ProcessMouseDown(start);
        scene.Host.ProcessMouseMove(new SKPoint(start.X + 40f, start.Y));
        scene.Host.ProcessMouseUp(new SKPoint(start.X + 40f, start.Y));

        Assert.AreEqual(160f, split.SplitterDistance, 1f, "a drag decides the split whatever is fixed");
        Assert.AreEqual(234f, split.Panel2.Width, 0.5f, "and the second panel took the difference");
    }

    [TestMethod]
    public void ReorientingKeepsTheDistanceRatherThanCarryingTheOldAxisAcross()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 120f,
            FixedPanel = FixedPanel.Panel2,
        };

        using var scene = HeadlessScene.Create(split, 400f, 300f);

        split.Orientation = Orientation.Horizontal;

        Assert.AreEqual(120f, split.SplitterDistance, 0.5f, "a width is not a height, so the distance stands");
    }

    [TestMethod]
    public void FixedPanelSizeReadsThePanelThatIsFixed()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 120f };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        Assert.AreEqual(120f, split.FixedPanelSize, 0.5f, "with panel 1 fixed it is panel 1's width");

        split.FixedPanel = FixedPanel.Panel2;

        Assert.AreEqual(274f, split.FixedPanelSize, 0.5f, "and with panel 2 fixed it is panel 2's");
    }

    [TestMethod]
    public void FixedPanelSizeIsAHeightInAHorizontalSplit()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 80f,
            FixedPanel = FixedPanel.Panel2,
        };

        using var scene = HeadlessScene.Create(split, 400f, 300f);

        Assert.AreEqual(214f, split.FixedPanelSize, 0.5f, "the axis the splitter runs across, not the width");
    }

    [TestMethod]
    public void SettingFixedPanelSizeMovesTheSplitter()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 120f };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        split.FixedPanelSize = 150f;

        Assert.AreEqual(150f, split.SplitterDistance, 0.5f, "asking the first panel for a width moves the splitter");
        Assert.AreEqual(150f, split.Panel1.Width, 0.5f);
    }

    [TestMethod]
    public void SettingFixedPanelSizeOnTheSecondPanelMovesTheSplitterTheOtherWay()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 120f,
            FixedPanel = FixedPanel.Panel2,
        };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        int changed = 0;
        split.AddSplitterDistanceChangedHandler(_ => changed++);

        split.FixedPanelSize = 300f;

        Assert.AreEqual(300f, split.Panel2.Width, 0.5f, "the second panel measured what was asked of it");
        Assert.AreEqual(94f, split.SplitterDistance, 0.5f, "which left the splitter 94 px from the left");
        Assert.AreEqual(1, changed, "it is still one splitter, so the distance change is the event raised");
    }

    [TestMethod]
    public void FixedPanelSizeIsHeldToThePanelMinimums()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 120f,
            Panel1MinSize = 100f,
        };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        split.FixedPanelSize = 20f;

        Assert.AreEqual(100f, split.FixedPanelSize, 0.5f, "the first panel cannot go under its minimum");
    }

    [TestMethod]
    public void WithNoFixedPanelTheSizeIsPanel1s()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 120f,
            FixedPanel = FixedPanel.None,
        };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        Assert.AreEqual(120f, split.FixedPanelSize, 0.5f, "nothing is fixed, so this follows panel 1");

        split.FixedPanelSize = 160f;

        Assert.AreEqual(160f, split.SplitterDistance, 0.5f, "and setting it moves panel 1 the way it always can");
    }

    [TestMethod]
    public void FixedPanelSizeFollowsAResizeOfTheContainer()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 120f,
            FixedPanel = FixedPanel.Panel2,
        };

        using var scene = HeadlessScene.Create(split, 400f, 200f);

        scene.Resize(500f, 200f);

        Assert.AreEqual(274f, split.FixedPanelSize, 0.5f, "the fixed panel kept its width through the resize");
        Assert.AreEqual(274f, split.Panel2.Width, 0.5f, "which is what the panel actually measures");
    }

    private static Panel Plain() => new()
    {
        Padding = Thickness.Empty,
    };

    private static TableLayoutPanel PlainTable() => new()
    {
        Dock = DockStyle.Fill,
        Padding = Thickness.Empty,
        CellPadding = Thickness.Empty,
    };
}
