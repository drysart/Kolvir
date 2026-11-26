using SkiaSharp;

namespace Kolvir.Tests;

/// <summary>
/// The row and column collections lay the table out on their own, and an assignment that says what
/// the table already says costs nothing.
/// </summary>
/// <remarks>
/// Layout passes are counted rather than inferred from effects where the claim is "exactly one" or
/// "none", because a pass that arranges the same thing twice leaves the same bounds behind and would
/// pass an effect-only test.
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class TableStyleTests
{
    [TestMethod]
    public void AddingThroughTheLiveListArrangesTheTable()
    {
        using var table = Empty();
        using var child = Sized(column: 0, row: 0, width: 200f, height: 10f);

        table.Controls.Add(child);

        using var scene = HeadlessScene.Create(table, 200f, 30f);

        // No declared columns, so the implicit single track hugs the only child.
        Assert.AreEqual(200f, child.Bounds.Width, "the implicit column hugs the child");

        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60f));

        Assert.AreEqual(60f, child.Bounds.Width, "the column added through the live list has been arranged");
        Assert.AreEqual(60f, table.ColumnStyles[0].Value);
    }

    [TestMethod]
    public void ReplacingAStyleByIndexArrangesTheTable()
    {
        using var table = Empty();
        using var child = Filling(column: 0, row: 0);

        table.Controls.Add(child);
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60f));

        using var scene = HeadlessScene.Create(table, 200f, 30f);
        Assert.AreEqual(60f, child.Bounds.Width);

        // The style cannot be changed, so the way to change the width is to replace it - and the
        // replacement is what has to lay the table out.
        table.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 100f);

        Assert.AreEqual(100f, child.Bounds.Width);
    }

    [TestMethod]
    public void AssigningAnEqualSequenceDoesNothing()
    {
        using var table = Empty();
        table.RowStyles = [new RowStyle(SizeType.Absolute, 30f), new RowStyle(SizeType.Percent, 50f)];
        table.ColumnStyles = [new ColumnStyle(SizeType.Absolute, 60f)];

        var rows = table.RowStyles;
        var first = rows[0];

        table.SuspendLayout();
        var passes = table.Passes;

        // A render that rebuilds its styles from scratch produces equal styles, never the same ones.
        table.RowStyles = [new RowStyle(SizeType.Absolute, 30f), new RowStyle(SizeType.Percent, 50f)];

        table.ResumeLayout();

        Assert.AreEqual(passes, table.Passes, "an equal sequence is not laid out again");
        Assert.AreSame(first, table.RowStyles[0], "the styles already on the table are kept, not rebuilt");
        Assert.AreEqual(2, table.RowCount);
    }

    [TestMethod]
    public void AssigningADifferentSequenceLaysOutExactlyOnce()
    {
        using var table = Empty();
        table.RowStyles = [new RowStyle(SizeType.Absolute, 30f)];

        var passes = table.Passes;

        table.RowStyles = [new RowStyle(SizeType.Absolute, 30f), new RowStyle(SizeType.AutoSize, 0f)];

        Assert.AreEqual(passes + 1, table.Passes, "one pass, not one per element");
        Assert.AreEqual(2, table.RowCount);
        Assert.AreEqual(SizeType.AutoSize, table.RowStyles[1].SizeType);
    }

    [TestMethod]
    public void ASequenceThatDiffersInAnyWayIsApplied()
    {
        using var table = Empty();
        table.RowStyles = [new RowStyle(SizeType.Absolute, 10f), new RowStyle(SizeType.Absolute, 20f), new RowStyle(SizeType.Absolute, 30f)];

        // A changed value.
        table.RowStyles = [new RowStyle(SizeType.Absolute, 10f), new RowStyle(SizeType.Absolute, 25f), new RowStyle(SizeType.Absolute, 30f)];
        Assert.AreEqual(25f, table.RowStyles[1].Value);

        // Reordered.
        table.RowStyles = [new RowStyle(SizeType.Absolute, 30f), new RowStyle(SizeType.Absolute, 25f), new RowStyle(SizeType.Absolute, 10f)];
        Assert.AreEqual(30f, table.RowStyles[0].Value);

        // Shortened.
        table.RowStyles = [new RowStyle(SizeType.Absolute, 30f)];
        Assert.AreEqual(1, table.RowCount);

        // Added to.
        table.RowStyles = [new RowStyle(SizeType.Absolute, 30f), new RowStyle(SizeType.Absolute, 40f)];
        Assert.AreEqual(2, table.RowCount);

        // And emptied.
        table.RowStyles = [];
        Assert.AreEqual(0, table.RowCount);
    }

    [TestMethod]
    public void AnEmptySequenceArrangesTheTableAsOneWithNoDeclaredRows()
    {
        using var table = Empty();
        using var child = Sized(column: 0, row: 0, width: 60f, height: 30f);

        table.Controls.Add(child);
        table.RowStyles = [new RowStyle(SizeType.Absolute, 10f)];
        table.ColumnStyles = [new ColumnStyle(SizeType.Absolute, 60f)];

        using var scene = HeadlessScene.Create(table, 200f, 100f);
        Assert.AreEqual(10f, child.Bounds.Height, "the declared row");

        int passes = table.Passes;
        table.RowStyles = [];

        Assert.AreEqual(passes + 1, table.Passes, "clearing the rows arranges the table again");
        Assert.AreEqual(0, table.RowCount);
        Assert.AreEqual(30f, child.Bounds.Height, "with no declared rows the implicit track hugs the child");
    }

    [TestMethod]
    public void ClearingLaysOutAgain()
    {
        using var table = Empty();
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60f));

        int passes = table.Passes;

        table.ColumnStyles.Clear();

        Assert.AreEqual(passes + 1, table.Passes);
        Assert.AreEqual(0, table.ColumnCount);
    }

    [TestMethod]
    public void ClearingAnEmptyCollectionIsNotAPass()
    {
        using var table = Empty();

        int passes = table.Passes;

        table.ColumnStyles.Clear();

        Assert.AreEqual(passes, table.Passes);
    }

    [TestMethod]
    public void ARemoveThatFindsNothingIsNotAPass()
    {
        using var table = Empty();
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));

        int passes = table.Passes;

        Assert.IsFalse(table.RowStyles.Remove(new RowStyle(SizeType.Absolute, 60f)), "that row is not there");
        Assert.AreEqual(passes, table.Passes, "nothing changed, so nothing was arranged");

        Assert.IsTrue(table.RowStyles.Remove(new RowStyle(SizeType.Absolute, 30f)), "an equal style is the row that is there");
        Assert.AreEqual(passes + 1, table.Passes);
        Assert.AreEqual(0, table.RowCount);
    }

    [TestMethod]
    public void AStyleAssignedOverItsOwnEqualIsNotAPass()
    {
        using var table = Empty();
        var style = new ColumnStyle(SizeType.Absolute, 60f);
        table.ColumnStyles.Add(style);

        int passes = table.Passes;

        table.ColumnStyles[0] = style;
        Assert.AreEqual(passes, table.Passes, "assigning the same instance changes nothing");

        table.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 60f);
        Assert.AreEqual(passes, table.Passes, "an equal style is the same instruction, so nothing is arranged");
        Assert.AreSame(style, table.ColumnStyles[0], "and the instance already there stays");
    }

    [TestMethod]
    public void BulkPopulationInsideSuspendLayoutIsOnePass()
    {
        using var table = Empty();

        int passes = table.Passes;

        table.SuspendLayout();

        for (int i = 0; i < 8; i++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 10f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20f));
        }

        table.ResumeLayout();

        Assert.AreEqual(passes + 1, table.Passes, "sixteen additions, one pass");
        Assert.AreEqual(8, table.RowCount);
        Assert.AreEqual(8, table.ColumnCount);
    }

    [TestMethod]
    public void AssigningNullThrows()
    {
        using var table = Empty();

        Assert.ThrowsExactly<ArgumentNullException>(() => table.RowStyles = null!);
        Assert.ThrowsExactly<ArgumentNullException>(() => table.ColumnStyles = null!);

        // An explicit clear is an empty sequence, not a null one.
        table.RowStyles = [];
        Assert.AreEqual(0, table.RowCount);
    }

    [TestMethod]
    public void AssigningTheLiveListToItselfIsANoOp()
    {
        using var table = Empty();
        table.ColumnStyles = [new ColumnStyle(SizeType.Absolute, 60f)];

        var columns = table.ColumnStyles;
        int passes = table.Passes;

        table.ColumnStyles = columns;

        Assert.AreEqual(passes, table.Passes);
        Assert.AreEqual(1, table.ColumnCount);
    }

    [TestMethod]
    public void ChangingTheAssignedListAfterwardsDoesNotAffectTheTable()
    {
        using var table = Empty();
        var rows = new List<RowStyle> { new RowStyle(SizeType.Absolute, 30f) };

        table.RowStyles = rows;
        rows.Add(new RowStyle(SizeType.Absolute, 60f));

        Assert.AreEqual(1, table.RowCount, "the table copied what it was given");
    }

    [TestMethod]
    public void AListKeptFromTheGetterStillWritesToTheCurrentRows()
    {
        using var table = Empty();
        table.RowStyles = [new RowStyle(SizeType.Absolute, 30f)];

        var rows = table.RowStyles;

        table.RowStyles = [new RowStyle(SizeType.Absolute, 60f), new RowStyle(SizeType.AutoSize, 0f)];

        // Nothing was swapped underneath the caller: the list it holds is the table's list, so this
        // reaches the rows the table has now.
        rows.Add(new RowStyle(SizeType.Absolute, 90f));

        Assert.AreEqual(3, table.RowCount);
        Assert.AreEqual(90f, table.RowStyles[2].Value);
    }

    [TestMethod]
    public void TheStylesOnTheTableAreTheOnesThatWereAssigned()
    {
        using var table = Empty();
        var first = new RowStyle(SizeType.Absolute, 30f);
        var second = new RowStyle(SizeType.Percent, 100f);

        table.RowStyles = [first, second];

        Assert.AreSame(first, table.RowStyles[0]);
        Assert.AreSame(second, table.RowStyles[1]);
    }

    [TestMethod]
    public void AddRowIsOnePassAndTheSameAsTheCollection()
    {
        using var table = Empty();

        int passes = table.Passes;

        table.AddRow(new RowStyle(SizeType.Absolute, 30f));
        Assert.AreEqual(passes + 1, table.Passes, "one pass per call");

        table.AddRow(SizeType.Percent, 50f);
        Assert.AreEqual(passes + 2, table.Passes);

        table.AddColumn(SizeType.Absolute, 20f);
        Assert.AreEqual(passes + 3, table.Passes);

        Assert.AreEqual(2, table.RowCount);
        Assert.AreEqual(1, table.ColumnCount);
        Assert.AreEqual(SizeType.Percent, table.RowStyles[1].SizeType);
    }

    /// <summary>A table that counts the layout passes it actually runs.</summary>
    private sealed class CountingTable : TableLayoutPanel
    {
        internal int Passes { get; private set; }

        protected override void OnLayout()
        {
            Passes++;
            base.OnLayout();
        }
    }

    private static CountingTable Empty() => new()
    {
        Dock = DockStyle.Fill,
        Padding = Thickness.Empty,
        CellPadding = Thickness.Empty,
    };

    private static Panel Filling(int column, int row) => new()
    {
        Padding = Thickness.Empty,
        Dock = DockStyle.Fill,
        TableColumn = column,
        TableRow = row,
    };

    /// <summary>A child whose measurement is fixed, so an AutoSize track has something known to fit.</summary>
    private static Panel Sized(int column, int row, float width, float height)
    {
        var child = Filling(column, row);
        child.MinimumSize = new SKSize(width, height);
        return child;
    }
}
