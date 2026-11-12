using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The wrapping layout: where a line breaks, how the items on a line share its breadth, and how the
/// measured size of the run depends on the room it is given.
/// </summary>
/// <remarks>
/// The items are plain controls with a fixed size, so every assertion is about the layout rather
/// than about what an item measures.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class WrapPanelTests : HeadlessTestBase
{
    [TestMethod]
    public void ItemsWrapOntoTheNextRowAtTheWidth()
    {
        var flow = new WrapPanel { Dock = DockStyle.Fill };
        var items = AddItems(flow, 5, new SKSize(60f, 30f));

        using var scene = HeadlessScene.Create(flow, 200f, 300f);

        Assert.AreEqual(0f, items[0].Bounds.Left, "the first item starts at the left edge");
        Assert.AreEqual(60f, items[1].Bounds.Left);
        Assert.AreEqual(120f, items[2].Bounds.Left, "three 60-wide items fit in 200");

        Assert.AreEqual(items[0].Bounds.Top, items[2].Bounds.Top, "the first three share a row");
        Assert.AreEqual(30f, items[3].Bounds.Top, "the fourth starts the second row");
        Assert.AreEqual(items[3].Bounds.Top, items[4].Bounds.Top);
        Assert.AreEqual(0f, items[3].Bounds.Left, "which starts at the left edge again");
    }

    [TestMethod]
    public void SpacingSeparatesItemsAndRows()
    {
        var flow = new WrapPanel { Dock = DockStyle.Fill, Spacing = 10f };
        var items = AddItems(flow, 4, new SKSize(60f, 30f));

        using var scene = HeadlessScene.Create(flow, 200f, 300f);

        Assert.AreEqual(70f, items[1].Bounds.Left, "the gap is between the items, not inside them");
        Assert.AreEqual(140f, items[2].Bounds.Left);
        Assert.AreEqual(40f, items[3].Bounds.Top, "and between the rows");
    }

    [TestMethod]
    public void ARowIsAsTallAsItsTallestItemAndTheOthersStretchToIt()
    {
        var flow = new WrapPanel { Dock = DockStyle.Fill };
        var shortItem = AddItem(flow, new SKSize(70f, 20f));
        var tallItem = AddItem(flow, new SKSize(70f, 50f));
        var next = AddItem(flow, new SKSize(70f, 20f));

        using var scene = HeadlessScene.Create(flow, 200f, 300f);

        Assert.AreEqual(50f, shortItem.Height, "Stretch is the default across a row");
        Assert.AreEqual(50f, tallItem.Height);
        Assert.AreEqual(50f, next.Top, "and the next row starts below the tallest item");
    }

    [TestMethod]
    public void TheAlignmentPlacesAnItemAcrossItsRow()
    {
        var flow = new WrapPanel { Dock = DockStyle.Fill };

        var near = AddItem(flow, new SKSize(50f, 20f), Alignment.Near);
        var center = AddItem(flow, new SKSize(50f, 20f), Alignment.Center);
        var far = AddItem(flow, new SKSize(50f, 20f), Alignment.Far);
        AddItem(flow, new SKSize(50f, 40f));   // makes the row 40 tall

        using var scene = HeadlessScene.Create(flow, 400f, 300f);

        Assert.AreEqual(0f, near.Top);
        Assert.AreEqual(10f, center.Top, "centre leaves half the leftover above");
        Assert.AreEqual(20f, far.Top, "and Far puts it at the bottom edge");

        Assert.AreEqual(20f, near.Height, "an aligned item keeps its own size across the row");
    }

    [TestMethod]
    public void AVerticalWrapPlacesColumnsTopToBottom()
    {
        var flow = new WrapPanel { Dock = DockStyle.Fill, Orientation = StackOrientation.Vertical };
        var items = AddItems(flow, 4, new SKSize(40f, 30f));

        using var scene = HeadlessScene.Create(flow, 300f, 100f);

        Assert.AreEqual(0f, items[0].Top);
        Assert.AreEqual(60f, items[2].Top, "three items fit down the 100-tall panel");
        Assert.AreEqual(40f, items[3].Left, "and the fourth starts a new column one item wide");
        Assert.AreEqual(0f, items[3].Top, "at the top of it");
    }

    [TestMethod]
    public void AnItemWiderThanThePanelOverflowsRatherThanDisappearing()
    {
        var flow = new WrapPanel { Dock = DockStyle.Fill };
        var wide = AddItem(flow, new SKSize(300f, 30f));
        var next = AddItem(flow, new SKSize(60f, 30f));

        using var scene = HeadlessScene.Create(flow, 200f, 300f);

        Assert.AreEqual(0f, wide.Left);
        Assert.AreEqual(300f, wide.Width, "the item keeps its own width");
        Assert.AreEqual(30f, next.Top, "and the next item starts a new row instead of overlapping it");
        Assert.IsTrue(flow.ScrollExtent.Width >= 300f, "so the panel scrolls to reach the rest of it");
    }

    [TestMethod]
    public void MeasuringBreaksLinesAtTheWidthItIsGiven()
    {
        var flow = new WrapPanel();
        foreach (var _ in Enumerable.Range(0, 6))
        {
            flow.Controls.Add(new Button("x") { AutoSize = false, Size = new SKSize(60f, 30f) });
        }

        float wide = flow.Measure(new SKSize(200f, 1000f)).Height;
        float narrow = flow.Measure(new SKSize(100f, 1000f)).Height;

        Assert.AreEqual(60f, wide, "two rows of three at 200 wide");
        Assert.AreEqual(180f, narrow, "six rows at 100 wide");
    }

    [TestMethod]
    public void ADockedChildTakesItsEdgeAndTheFlowUsesWhatIsLeft()
    {
        var flow = new WrapPanel { Dock = DockStyle.Fill };

        var header = new Label("Header") { Dock = DockStyle.Top, AutoSize = false, Height = 20f };
        flow.Controls.Add(header);

        var items = AddItems(flow, 3, new SKSize(60f, 30f));

        using var scene = HeadlessScene.Create(flow, 200f, 300f);

        Assert.AreEqual(20f, header.Bounds.Bottom, "the docked child keeps its band");
        Assert.AreEqual(20f, items[0].Top, "and the run starts below it");
    }

    private static Control AddItem(WrapPanel flow, SKSize size, Alignment alignment = Alignment.Stretch)
    {
        var control = new Button(string.Empty)
        {
            AutoSize = false,
            Size = size,
            VerticalAlignment = alignment,
            HorizontalAlignment = alignment,
        };

        flow.Controls.Add(control);
        return control;
    }

    private static Control[] AddItems(WrapPanel flow, int count, SKSize size)
    {
        var items = new Control[count];
        for (int i = 0; i < count; i++)
        {
            items[i] = AddItem(flow, size);
        }

        return items;
    }
}
