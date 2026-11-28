using SkiaSharp;

namespace Kolvir.Tests;

/// <summary>
/// <see cref="RowStyle.AlignBaselines"/>: which cells take part, where a row puts them, and the
/// measure/arrange agreement an AutoSize row needs so that nothing is clipped.
/// </summary>
/// <remarks>
/// The numbers asserted are absolute positions in the table rather than only equalities, so a change
/// that moves the whole table is caught as well as one that stops the cells lining up. The face is
/// pinned for every measurement case, because the numbers a baseline assertion is about are the
/// font's own ascent.
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class TableBaselineAlignmentTests : HeadlessTestBase
{
    [TestMethod]
    public void TwoLabelsAtDifferentSizesShareOneBaseline()
    {
        using (TestFont.Pin())
        {
            var table = Table();
            var small = Sized("small", 12f);
            var large = Sized("large", 20f);

            table.Add(small, 0, 0);
            table.Add(large, 1, 0);
            table.RowStyles = [Flagged()];
            table.ColumnStyles = Columns(120f, 120f);

            using var scene = HeadlessScene.Create(table, 400f, 200f);

            // The 20 pt label's baseline is the lowest, so it stays where the row would have put it;
            // the 12 pt one moves down by 8 so that both first lines are drawn on y = 20.5. The boxes
            // keep the sizes their content asked for, which is what the row is aligning instead of.
            Assert.AreEqual(0f, large.Top, 0.01f, "the lowest baseline is the one every cell shares");
            Assert.AreEqual(8f, small.Top, 0.01f, "the smaller label moves down to meet it");
            Assert.AreEqual(16f, small.Height, 0.01f);
            Assert.AreEqual(26f, large.Height, 0.01f);

            Assert.AreEqual(20.5f, BaselineOf(small), 0.01f, "the 12 pt first line's baseline");
            Assert.AreEqual(20.5f, BaselineOf(large), 0.01f, "the 20 pt first line's baseline");
        }
    }

    [TestMethod]
    public void WithTheRowUnflaggedTheCellsArePlacedByTheirBoxes()
    {
        using (TestFont.Pin())
        {
            var table = Table();
            var small = Sized("small", 12f);
            var large = Sized("large", 20f);

            table.Add(small, 0, 0);
            table.Add(large, 1, 0);
            table.RowStyles = [Unflagged()];
            table.ColumnStyles = Columns(120f, 120f);

            using var scene = HeadlessScene.Create(table, 400f, 200f);

            // The row the table has always arranged: each cell at the top of its cell, so the two
            // first lines sit at different heights - 12.5 and 20.5 - which is what the property is
            // there to avoid.
            Assert.AreEqual(0f, small.Top, 0.01f);
            Assert.AreEqual(0f, large.Top, 0.01f);
            Assert.AreEqual(12.5f, BaselineOf(small), 0.01f);
            Assert.AreEqual(20.5f, BaselineOf(large), 0.01f);
        }
    }

    [TestMethod]
    public void AnAutoSizedRowMeasuresTallEnoughForTheShiftedContent()
    {
        using (TestFont.Pin())
        {
            var table = AutoSized(Table());
            var large = Sized("large", 20f);
            var tall = new Label("tall")
            {
                AutoSize = false,
                Width = 60f,
                Height = 30f,
                TextAlign = ContentAlignment.TopLeft,
                Font = new Font(TestFont.Family, 12f),
            };

            table.Add(tall, 0, 0);
            table.Add(large, 1, 0);
            table.RowStyles = [Flagged()];
            table.ColumnStyles = Columns(80f, 120f);

            using var scene = HeadlessScene.Create(Host(table), 400f, 300f);

            // Thirty is what the tallest child needs on its own. The 12 pt text sits at the top of a
            // thirty-tall box, so its baseline is 12.3 from the top, and meeting the 20 pt label's
            // baseline at 20.5 moves it 8.2 down - putting its bottom edge at 38.2, which is what the
            // row has to report.
            Assert.AreEqual(38.2f, table.Height, 0.01f, "the row grew to hold the shifted label");
            Assert.AreEqual(table.DesiredSize.Height, table.Height, 0.01f, "and is arranged at what it measured");

            Assert.AreEqual(8.2f, tall.Top, 0.01f);
            Assert.AreEqual(38.2f, tall.Bottom, 0.01f, "the moved label reaches exactly the measured height");
            Assert.AreEqual(BaselineOf(tall), BaselineOf(large), 0.01f, "and the two still share a baseline");

            // The measure half is only right if it accounts for the shift the arrange half applies,
            // so the check is that the arranged cells fit inside what was reported.
            Assert.IsTrue(tall.Bottom <= table.Height + 0.01f, "nothing is clipped");
            Assert.IsTrue(large.Bottom <= table.Height + 0.01f);
        }
    }

    [TestMethod]
    public void ACellThatSpansRowsTakesNoPartAndIsPlacedAsBefore()
    {
        using (TestFont.Pin())
        {
            var table = Table();
            var label = Sized("label", 12f);
            var spanning = new Label("spanning")
            {
                AutoSize = false,
                Width = 60f,
                Height = 60f,
                TextAlign = ContentAlignment.BottomLeft,
                Font = new Font(TestFont.Family, 12f),
            };

            table.Add(label, 1, 0);
            table.Add(spanning, 0, 0);
            table.SetCellSpan(spanning, 1, 2);
            table.RowStyles = [Flagged(), Flagged()];
            table.ColumnStyles = Columns(120f, 120f);

            using var scene = HeadlessScene.Create(table, 400f, 200f);

            // The two rows share the spanning cell's sixty equally, so each is thirty tall and the
            // spanned box covers both. Its baseline is nowhere near the first row's line, which is
            // the point: it takes part in neither row, so the label is not dragged down to meet it.
            Assert.AreEqual(new SKRect(0f, 0f, 60f, 60f), spanning.Bounds, "the spanned cell is placed as it always was");
            Assert.AreEqual(0f, label.Top, 0.01f, "and the row's one participant does not move");
            Assert.AreEqual(12.5f, BaselineOf(label), 0.01f);
        }
    }

    [TestMethod]
    public void AFillingCellFillsItsCellAndTakesNoPart()
    {
        using (TestFont.Pin())
        {
            var table = Table();
            var label = Sized("label", 12f);
            var filling = new Label("filling")
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(TestFont.Family, 20f),
            };

            table.Add(filling, 0, 0);
            table.Add(label, 1, 0);
            table.RowStyles = [new RowStyle(SizeType.Absolute, 40f) { AlignBaselines = true }];
            table.ColumnStyles = Columns(120f, 120f);

            using var scene = HeadlessScene.Create(table, 400f, 200f);

            // Its height is the row's, so its baseline would be a function of the height the row is
            // being measured for. It fills the cell and nothing lines up against it.
            Assert.AreEqual(new SKRect(0f, 0f, 120f, 40f), filling.Bounds, "the filling cell fills its cell");
            Assert.AreEqual(0f, label.Top, 0.01f, "the row's one participant does not move");
        }
    }

    [TestMethod]
    public void ARowWithOneParticipantIsUnchanged()
    {
        using (TestFont.Pin())
        {
            var flagged = OneCell(alignBaselines: true);
            var plain = OneCell(alignBaselines: false);

            Assert.AreEqual(plain.Child.Bounds, flagged.Child.Bounds, "a single participant has nobody to line up against");
            Assert.AreEqual(0f, flagged.Child.Top, 0.01f);
        }
    }

    [TestMethod]
    public void TwoFlaggedRowsResolveFromTheirOwnCells()
    {
        using (TestFont.Pin())
        {
            var table = Table();
            var firstSmall = Sized("small", 12f);
            var firstLarge = Sized("large", 20f);
            var secondSmall = Sized("small", 12f);
            var secondLarge = Sized("large", 20f);

            table.Add(firstSmall, 0, 0);
            table.Add(firstLarge, 1, 0);
            table.Add(secondSmall, 0, 1);
            table.Add(secondLarge, 1, 1);
            table.RowStyles = [Flagged(), Flagged()];
            table.ColumnStyles = Columns(120f, 120f);

            using var scene = HeadlessScene.Create(table, 400f, 200f);

            // Each row finds its own lowest baseline and moves its own cells. The second row's line is
            // the first row's plus the first row's height, which is what shows nothing leaked between
            // them - a shared reference would put both rows' small labels on one line.
            Assert.AreEqual(0f, firstLarge.Top, 0.01f);
            Assert.AreEqual(8f, firstSmall.Top, 0.01f);
            Assert.AreEqual(26f, secondLarge.Top, 0.01f);
            Assert.AreEqual(34f, secondSmall.Top, 0.01f);

            Assert.AreEqual(BaselineOf(firstSmall), BaselineOf(firstLarge), 0.01f);
            Assert.AreEqual(BaselineOf(secondSmall), BaselineOf(secondLarge), 0.01f);
            Assert.AreEqual(26f, BaselineOf(secondLarge) - BaselineOf(firstLarge), 0.01f, "the rows resolve apart");
        }
    }

    [TestMethod]
    public void AFixedRowAppliesTheShiftAndOverflowsAsDocumented()
    {
        using (TestFont.Pin())
        {
            var table = Table();
            var large = Sized("large", 20f);
            var tall = new Label("tall")
            {
                AutoSize = false,
                Width = 60f,
                Height = 30f,
                TextAlign = ContentAlignment.TopLeft,
                Font = new Font(TestFont.Family, 12f),
            };

            table.Add(tall, 0, 0);
            table.Add(large, 1, 0);
            table.RowStyles = [new RowStyle(SizeType.Absolute, 30f) { AlignBaselines = true }];
            table.ColumnStyles = Columns(80f, 120f);

            using var scene = HeadlessScene.Create(table, 400f, 200f);

            // The height is what the style says, so the shift is applied and the cell that was already
            // as tall as the row is pushed past its bottom: the same overflow a cell taller than a
            // fixed row has always had, rather than a row that quietly grew.
            Assert.AreEqual(30f, table.RowStyles[0].Value, "the fixed height is authoritative");
            Assert.AreEqual(8.2f, tall.Top, 0.01f, "the shift is applied");
            Assert.AreEqual(38.2f, tall.Bottom, 0.01f, "and the row does not grow to hold it");
            Assert.AreEqual(0f, large.Top, 0.01f);
            Assert.AreEqual(BaselineOf(tall), BaselineOf(large), 0.01f);
        }
    }

    [TestMethod]
    public void WithNoRowFlaggedTheTableIsArrangedExactlyAsBefore()
    {
        using (TestFont.Pin())
        {
            var flaggedOff = Table();
            var plain = Table();

            foreach (var table in new[] { flaggedOff, plain })
            {
                table.Add(Sized("small", 12f), 0, 0);
                table.Add(Sized("large", 20f), 1, 0);
                table.ColumnStyles = Columns(120f, 120f);
            }

            flaggedOff.RowStyles = [Unflagged()];
            plain.RowStyles = [RowStyle.AutoSize()];

            // One scene at a time: a process has one application, so the two tables are arranged in
            // turn and their bounds compared afterwards.
            List<SKRect> offBounds;
            List<SKRect> plainBounds;

            using (var off = HeadlessScene.Create(flaggedOff, 400f, 200f))
            {
                offBounds = flaggedOff.Controls.Select(child => child.Bounds).ToList();
            }

            using (var plainScene = HeadlessScene.Create(plain, 400f, 200f))
            {
                plainBounds = plain.Controls.Select(child => child.Bounds).ToList();
            }

            // The switch off is the arrangement the table has always made, cell for cell - which is
            // what the whole add-on has to cost a table that never turns it on.
            CollectionAssert.AreEqual(
                plainBounds,
                offBounds,
                "an unflagged row arranges its cells exactly as a plain one does");
        }
    }

    [TestMethod]
    public void ADockValueTheTableIgnoresDoesNotKeepACellOutOfTheRow()
    {
        using (TestFont.Pin())
        {
            // Dock.Top and Dock.Left have no arm in the table's placement switch, so a cell carrying
            // one is placed exactly where Dock.None puts it. A cell placed the same way has to join
            // the row the same way, or the rule would depend on an unused value.
            var none = RowWithDock(DockStyle.None);
            var top = RowWithDock(DockStyle.Top);
            var left = RowWithDock(DockStyle.Left);

            foreach (var row in new[] { none, top, left })
            {
                Assert.AreEqual(8f, row.Top, 0.01f, "the 12 pt cell joins the row and moves down");
                Assert.AreEqual(row.LargeBaseline, row.Baseline, 0.01f, "so it shares the 20 pt baseline");
            }

            Assert.AreEqual(none.Top, top.Top, 0.01f);
            Assert.AreEqual(none.Top, left.Top, 0.01f);
            Assert.AreEqual(none.Baseline, left.Baseline, 0.01f);
        }
    }

    [TestMethod]
    public void ABottomDockedCellTakesNoPart()
    {
        using (TestFont.Pin())
        {
            var table = Table();
            var bottom = new Label("bottom")
            {
                AutoSize = true,
                Dock = DockStyle.Bottom,
                Font = new Font(TestFont.Family, 12f),
            };
            var small = Sized("small", 12f);
            var large = Sized("large", 20f);

            table.Add(bottom, 0, 0);
            table.Add(small, 1, 0);
            table.Add(large, 2, 0);
            table.RowStyles = [new RowStyle(SizeType.Absolute, 60f) { AlignBaselines = true }];
            table.ColumnStyles = Columns(120f, 120f, 120f);

            using var scene = HeadlessScene.Create(table, 400f, 200f);

            // Its top is measured from the bottom of the cell, so if it took part its baseline near
            // the row's bottom would drag both labels down to meet it.
            Assert.AreEqual(0f, large.Top, 0.01f, "the row's line comes from the two labels");
            Assert.AreEqual(8f, small.Top, 0.01f);
            Assert.AreEqual(BaselineOf(small), BaselineOf(large), 0.01f);
        }
    }

    /// <summary>The first-line baseline of a control's text, in its parent's coordinates.</summary>
    private static float BaselineOf(Control control)
    {
        Assert.IsTrue(
            control.TryGetBaseline(control.Size, out float baseline),
            $"{control.GetType().Name} did not report a baseline at {control.Size}");

        return control.Top + baseline;
    }

    /// <summary>A table with the sample's framing removed, so bounds are bare numbers.</summary>
    private static TableLayoutPanel Table() => new()
    {
        Dock = DockStyle.Fill,
        Padding = Thickness.Empty,
        CellPadding = Thickness.Empty,
    };

    /// <summary>A table that asks its rows how tall they need to be.</summary>
    private static TableLayoutPanel AutoSized(TableLayoutPanel table)
    {
        table.Dock = DockStyle.Top;
        table.AutoSize = true;
        return table;
    }

    /// <summary>A host whose only child is the table, so the table can be arranged at its own height.</summary>
    private static Panel Host(TableLayoutPanel table)
    {
        var host = new Panel { Padding = Thickness.Empty };
        host.Controls.Add(table);
        return host;
    }

    /// <summary>A row that sizes to content and lines its cells up.</summary>
    private static RowStyle Flagged() => new() { AlignBaselines = true };

    /// <summary>The same row with the property off, which is the default.</summary>
    private static RowStyle Unflagged() => new() { AlignBaselines = false };

    /// <summary>A row of fixed-width columns.</summary>
    private static ColumnStyle[] Columns(params float[] widths) =>
        widths.Select(ColumnStyle.Absolute).ToArray();

    /// <summary>
    /// A flagged row of a 12 pt cell carrying <paramref name="dock"/>, a 20 pt label and a 16 pt one.
    /// </summary>
    /// <param name="dock">The dock the first cell carries.</param>
    /// <returns>Where the first cell landed and the baselines of it and the 20 pt label.</returns>
    private static (float Top, float Baseline, float LargeBaseline) RowWithDock(DockStyle dock)
    {
        var table = Table();
        var subject = Sized("subject", 12f);
        subject.Dock = dock;
        var large = Sized("large", 20f);
        var middle = Sized("middle", 16f);

        table.Add(subject, 0, 0);
        table.Add(large, 1, 0);
        table.Add(middle, 2, 0);
        table.RowStyles = [Flagged()];
        table.ColumnStyles = Columns(120f, 120f, 120f);

        using var scene = HeadlessScene.Create(table, 400f, 200f);
        return (subject.Top, BaselineOf(subject), BaselineOf(large));
    }

    /// <summary>A label in the pinned face, at the top of its own box.</summary>
    private static Label Sized(string text, float size) => new(text)
    {
        AutoSize = true,
        Font = new Font(TestFont.Family, size),
    };

    /// <summary>A one-row, one-column table with a single label in it.</summary>
    private static (TableLayoutPanel Table, Label Child) OneCell(bool alignBaselines)
    {
        var table = Table();
        var child = Sized("only", 13f);

        table.Add(child, 0, 0);
        table.RowStyles = [new RowStyle { AlignBaselines = alignBaselines }];
        table.ColumnStyles = Columns(120f);

        using var scene = HeadlessScene.Create(table, 400f, 200f);
        return (table, child);
    }
}
