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
            ShowFrame = false,
            IsCard = false,
            BorderWidth = 0f,
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

    private static TableLayoutPanel Grid() => new()
    {
        Dock = DockStyle.Fill,
        ShowFrame = false,
        IsCard = false,
        BorderWidth = 0f,
        CornerRadius = 0f,
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

    private static TableLayoutPanel Placed(float x)
    {
        var table = Grid();
        table.Dock = DockStyle.None;
        table.Location = new SKPoint(x, 0f);
        table.Size = new SKSize(210f, 130f);
        return table;
    }

    private static Panel Filling(int column, int row) => new()
    {
        ShowFrame = false,
        IsCard = false,
        BorderWidth = 0f,
        CornerRadius = 0f,
        Padding = Thickness.Empty,
        Dock = DockStyle.Fill,
        TableColumn = column,
        TableRow = row,
    };
}
