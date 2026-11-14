using SkiaSharp;

namespace Kolvir.Tests;

/// <summary>
/// Cell positions are attached to the control rather than held by the table, which is what lets them
/// be set in the initializer that creates the control.
/// </summary>
[TestClass]
[TestCategory("Core")]
public sealed class TableCellTests
{
    [TestMethod]
    public void ACellSetInTheInitializerPlacesTheControl()
    {
        using var table = Grid();
        using var header = Filling(column: 0, row: 0);
        using var body = Filling(column: 0, row: 1);

        table.Controls.Add(header);
        table.Controls.Add(body);

        using var scene = HeadlessScene.Create(table, 200f, 130f);

        Assert.AreEqual(new SKRect(0f, 0f, 60f, 30f), header.Bounds, "the absolute row, in the absolute column");
        Assert.AreEqual(new SKRect(0f, 30f, 60f, 130f), body.Bounds, "the percent row takes the rest");
    }

    [TestMethod]
    public void ACellSetInTheInitializerReachesTheRightColumn()
    {
        using var table = Grid();
        using var left = Filling(column: 0, row: 1);
        using var right = Filling(column: 1, row: 1);

        table.Controls.Add(left);
        table.Controls.Add(right);

        using var scene = HeadlessScene.Create(table, 210f, 130f);

        Assert.AreEqual(new SKRect(0f, 30f, 60f, 130f), left.Bounds, "the absolute column keeps its pixels");
        Assert.AreEqual(new SKRect(60f, 30f, 210f, 130f), right.Bounds, "the percent column takes the rest");
    }

    [TestMethod]
    public void AnUnsetCellIsTheFirstOne()
    {
        using var table = Grid();
        using var child = new Panel();

        table.Controls.Add(child);

        Assert.AreEqual((0, 0), table.GetCellPosition(child));
        Assert.AreEqual(0, child.TableColumn);
        Assert.AreEqual(0, child.TableRow);
    }

    [TestMethod]
    public void SettingTheCellOfAChildAlreadyInTheTableMovesIt()
    {
        using var table = Grid();
        using var child = Filling(column: 0, row: 0);
        table.Controls.Add(child);

        using var scene = HeadlessScene.Create(table, 200f, 130f);
        Assert.AreEqual(new SKRect(0f, 0f, 60f, 30f), child.Bounds, "the first cell to start with");

        // No PerformLayout here: the property is the whole of the API, so it has to be the thing that
        // lays the table out again.
        child.TableRow = 1;

        Assert.AreEqual(new SKRect(0f, 30f, 60f, 130f), child.Bounds);
    }

    [TestMethod]
    public void SettingTheCellBeforeTheAddLaysOutTheSameAsSettingItAfter()
    {
        // One scene per test, so both tables are laid out in the same one, side by side.
        using var host = new Panel
        {
            Padding = Thickness.Empty,
        };
        using var early = Placed(0f);
        using var late = Placed(210f);

        using var earlyChild = Filling(column: 1, row: 1);
        early.Controls.Add(earlyChild);

        // The same child added first and moved afterwards: the order a caller writes by hand when the
        // position has to be set from outside the initializer. Both orders have to land in one place,
        // so that moving a cell into the initializer changes nothing a render can see.
        using var lateChild = Filling(column: 0, row: 0);
        late.Controls.Add(lateChild);
        late.SetCellPosition(lateChild, 1, 1);

        host.Controls.Add(early);
        host.Controls.Add(late);

        using var scene = HeadlessScene.Create(host, 420f, 130f);

        Assert.AreEqual(new SKRect(60f, 30f, 210f, 130f), earlyChild.Bounds, "the cell given in the initializer");
        Assert.AreEqual(earlyChild.Bounds, lateChild.Bounds, "the cell moved after the add");
    }

    [TestMethod]
    public void TheCellGoesWithTheControlWhenItMovesToAnotherTable()
    {
        using var first = Grid();
        using var second = Grid();
        using var child = Filling(column: 1, row: 1);

        first.Controls.Add(child);
        Assert.AreEqual((1, 1), first.GetCellPosition(child));

        first.Controls.Remove(child);
        second.Controls.Add(child);

        Assert.AreEqual((1, 1), second.GetCellPosition(child), "the second table reads the same attached cell");
    }

    [TestMethod]
    public void APositionPastTheLastTrackClampsToIt()
    {
        using var table = Grid();
        using var child = Filling(column: 9, row: 9);
        table.Controls.Add(child);

        using var scene = HeadlessScene.Create(table, 210f, 130f);

        Assert.AreEqual(new SKRect(60f, 30f, 210f, 130f), child.Bounds, "the last cell, not an exception");
    }

    [TestMethod]
    public void TheTableKeepsNoStateOfItsOwnForAChild()
    {
        using var table = Grid();
        using var child = Filling(column: 1, row: 0);

        table.Controls.Add(child);
        table.Controls.Remove(child);

        // A table that had a map of children would still answer here, and would hold the child
        // alive for as long as it existed. The position is the control's, so it answers itself.
        Assert.AreEqual((1, 0), table.GetCellPosition(child));
        Assert.AreEqual(1, child.TableColumn);
    }

    [TestMethod]
    public void ASpanCoversTheTracksBesideItsCell()
    {
        using var table = Grid();
        using var child = Filling(column: 0, row: 0, columnSpan: 2, rowSpan: 2);
        table.Controls.Add(child);

        using var scene = HeadlessScene.Create(table, 200f, 130f);

        Assert.AreEqual(new SKRect(0f, 0f, 200f, 130f), child.Bounds, "both columns and both rows");
        Assert.AreEqual((2, 2), table.GetCellSpan(child));
    }

    [TestMethod]
    public void ASpanStopsAtTheLastTrack()
    {
        using var table = Grid();
        using var child = Filling(column: 1, row: 1, columnSpan: 9, rowSpan: 9);
        table.Controls.Add(child);

        using var scene = HeadlessScene.Create(table, 200f, 130f);

        Assert.AreEqual(new SKRect(60f, 30f, 200f, 130f), child.Bounds, "the last cell, however far it asked to go");
    }

    [TestMethod]
    public void ASpanBelowOneTakesOneTrack()
    {
        using var table = Grid();
        using var child = Filling(column: 0, row: 0, columnSpan: 0, rowSpan: -4);
        table.Controls.Add(child);

        using var scene = HeadlessScene.Create(table, 200f, 130f);

        Assert.AreEqual(new SKRect(0f, 0f, 60f, 30f), child.Bounds);
        Assert.AreEqual((1, 1), table.GetCellSpan(child));
    }

    [TestMethod]
    public void TwoAutoSizeTracksShareTheWidthASpanningChildNeeds()
    {
        using var table = PlainGrid();
        using var wide = Sized(column: 0, row: 0, columnSpan: 2, width: 120f);
        using var right = Sized(column: 1, row: 0, columnSpan: 1, width: 10f);

        table.Controls.Add(wide);
        table.Controls.Add(right);

        using var scene = HeadlessScene.Create(table, 200f, 20f);

        Assert.AreEqual(new SKRect(0f, 0f, 120f, 20f), wide.Bounds, "the span is what has to fit");
        Assert.AreEqual(60f, right.Bounds.Left, "so each AutoSize column took half of it");
    }

    [TestMethod]
    public void SettingTheSpanOfAChildAlreadyInTheTableWidensIt()
    {
        using var table = Grid();
        using var child = Filling(column: 0, row: 0);
        table.Controls.Add(child);

        using var scene = HeadlessScene.Create(table, 200f, 130f);
        Assert.AreEqual(new SKRect(0f, 0f, 60f, 30f), child.Bounds, "the one column to start with");

        // No PerformLayout here, for the same reason as the cell: the property is the whole API.
        child.TableColumnSpan = 2;

        Assert.AreEqual(new SKRect(0f, 0f, 200f, 30f), child.Bounds);
    }

    private static TableLayoutPanel Grid() => new()
    {
        Dock = DockStyle.Fill,
        Padding = Thickness.Empty,
        CellPadding = Thickness.Empty,
        ColumnStyles =
        {
            new ColumnStyle(SizeType.Absolute, 60f),
            new ColumnStyle(SizeType.Percent, 100f),
        },
        RowStyles =
        {
            new RowStyle(SizeType.Absolute, 30f),
            new RowStyle(SizeType.Percent, 100f),
        },
    };

    private static TableLayoutPanel PlainGrid() => new()
    {
        Dock = DockStyle.Fill,
        Padding = Thickness.Empty,
        CellPadding = Thickness.Empty,
        ColumnStyles =
        {
            new ColumnStyle(SizeType.AutoSize, 0f),
            new ColumnStyle(SizeType.AutoSize, 0f),
        },
        RowStyles =
        {
            new RowStyle(SizeType.Absolute, 20f),
        },
    };

    /// <summary>A control whose measurement is fixed, so an AutoSize track has something known to fit.</summary>
    private static Panel Sized(int column, int row, int columnSpan, float width)
    {
        var panel = Filling(column, row, columnSpan);
        panel.MinimumSize = new SKSize(width, 10f);
        return panel;
    }

    private static TableLayoutPanel Placed(float x)
    {
        var table = Grid();
        table.Dock = DockStyle.None;
        table.Location = new SKPoint(x, 0f);
        table.Size = new SKSize(210f, 130f);
        return table;
    }

    private static Panel Filling(int column, int row, int columnSpan = 1, int rowSpan = 1) => new()
    {
        Padding = Thickness.Empty,
        Dock = DockStyle.Fill,
        TableColumn = column,
        TableRow = row,
        TableColumnSpan = columnSpan,
        TableRowSpan = rowSpan,
    };
}
