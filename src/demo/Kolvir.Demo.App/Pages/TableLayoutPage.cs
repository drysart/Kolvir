using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The table layout page: fixed, percentage and auto columns with spans.
/// </summary>
internal sealed class TableLayoutPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public TableLayoutPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// One of each sizing style, so the difference between them is visible rather than described:
    /// column 0 is a fixed width, column 1 takes a percentage and column 2 sizes to its content,
    /// and the three rows do the same. The cells are outlined so the tracks can be read off.
    /// </summary>
    private Panel Build()
    {
        // Every child names its own cell, so the whole grid - tracks, cells and their contents - is
        // one initializer. The position is attached to the control rather than handed to the table
        // afterwards; see "Using it" in dev-docs/DEVELOPER-GUIDE.md.
        return new TableLayoutPanel
        {
            Dock = XDock.Fill,
            BackColor = null,
            Padding = new Thickness(0f),
            CellPadding = new Thickness(8f),
            ColumnStyles =
                {
                    new ColumnStyle(SizeType.Absolute, 160f),
                    new ColumnStyle(SizeType.Percent, 100f),
                    new ColumnStyle(SizeType.AutoSize, 0f),
                },
            RowStyles =
                {
                    new RowStyle(SizeType.AutoSize, 0f),
                    new RowStyle(SizeType.Percent, 100f),
                    new RowStyle(SizeType.Absolute, 90f),
                    new RowStyle(SizeType.Absolute, 58f),
                },
            Controls =
                {
                    Cell("Absolute column\n160 px", 0, 0),
                    Cell("Percent column\nall the space the other two leave", 1, 0),
                    Cell("AutoSize column\nfollows its content", 2, 0),
                    Cell("AutoSize row\nfollows its content", 0, 1),
                    Cell("The percent row and the percent column absorb whatever the fixed and AutoSize tracks leave over, which is why this cell is the one that grows and shrinks with the window.", 1, 1),
                    Cell("Drag the splitter, or resize the window, and only this column keeps its width.", 2, 1),
                    Cell("Absolute row\n90 px", 0, 2),
                    Cell("Percent row, percent column", 1, 2),
                    Cell("AutoSize\nboth ways", 2, 2),
                    Cell("One cell over all three columns: TableColumnSpan = 3", 0, 3, columnSpan: 3),
                },
        };
    }

    private static Panel Cell(string caption, int column, int row, int columnSpan = 1)
    {
        var cell = new SurfacePanel
        {
            Dock = XDock.Fill,
            Border = Framed(8f),
            Padding = new Thickness(10f),
            TableColumn = column,
            TableRow = row,
            TableColumnSpan = columnSpan,
        };

        cell.Controls.Add(new Label(caption)
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        return cell;
    }

    /// <summary>
    /// One control along one axis: children stretched across the stack by default, the three
    /// alignments that opt out of it, the same control turned sideways, and a stack that scrolls
    /// its own run with a docked header that stays put.
    /// </summary>
}
