using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The list views page: the views, the columns and the sorting.
/// </summary>
internal sealed class ListViewsPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ListViewsPage(DemoUi shell) : base(shell) => Initialize(Build());

    private static long BytesOf(ListViewItem item) => item.Tag is long bytes ? bytes : 0L;

    private static string FormatSize(long bytes) => bytes >= 1024 ? $"{bytes / 1024} KB" : $"{bytes} B";

    private static readonly (string Name, string Kind, long Bytes, string Modified)[] SampleFiles =
    {
        ("readme.txt", "Text", 4_096, "2024-05-02 09:14"),
        ("Control.cs", "Source", 62_464, "2024-05-11 18:02"),
        ("ListView.cs", "Source", 31_744, "2024-05-12 07:45"),
        ("theme-light.json", "Data", 2_048, "2024-04-28 12:30"),
        ("theme-dark.json", "Data", 2_048, "2024-04-28 12:30"),
        ("render-target.png", "Image", 151_552, "2024-05-09 22:11"),
        ("harness.exe", "Binary", 98_304, "2024-05-10 08:00"),
        ("LICENSE", "Text", 1_024, "2024-01-04 00:00"),
        ("build.log", "Log", 831_488, "2024-05-12 06:59"),
        ("a-very-long-file-name-that-should-be-trimmed-with-an-ellipsis.txt", "Text", 7_168, "2024-05-01 11:20"),
    };

    /// <summary>
    /// Two list views over the same shape of data: one in <see cref="View.Details"/> with
    /// sortable columns and grid lines, one in <see cref="View.List"/>. Both only paint the
    /// rows on screen, so the row count is there to make that claim checkable.
    /// </summary>
    private Panel Build()
    {
        var card = Card();

        var sortCaption = ScrollCaption("Click a column header to sort; drag a header edge to resize");
        card.Controls.Add(sortCaption);

        var details = new ListView
        {
            Dock = XDock.Top,
            Height = 200f,
            View = View.Details,
            GridLines = true,
            SelectionMode = SelectionMode.MultiExtended,
            VerticalScroll = ScrollStyle.Automatic,
            HorizontalScroll = ScrollStyle.Automatic,
            Margin = new Thickness(0f, 0f, 0f, 6f),
        };

        // The Size cells read "812 KB" and "4 KB", which is the wrong way round as text, so the column
        // compares the byte count the rows keep in their Tag instead.
        var sizeColumn = new ListViewColumn("Size", 76f, ContentAlignment.MiddleRight)
        {
            Comparer = (left, right) => BytesOf(left).CompareTo(BytesOf(right)),
        };

        details.Columns.Add(new ListViewColumn("Name", 168f));
        details.Columns.Add(new ListViewColumn("Kind", 104f));
        details.Columns.Add(sizeColumn);
        details.Columns.Add(new ListViewColumn("Modified", 118f));

        foreach (var row in SampleFiles)
        {
            details.Items.Add(new ListViewItem(row.Name, row.Kind, FormatSize(row.Bytes), row.Modified)
            {
                Tag = row.Bytes,
            });
        }

        details.SelectedIndex = 0;
        card.Controls.Add(details);

        var selectionCaption = ScrollCaption("1 selected");
        card.Controls.Add(selectionCaption);

        details.AddSelectedIndexChangedHandler(_ =>
        {
            int count = details.SelectedIndices.Count;
            selectionCaption.Text = count switch
            {
                0 => "no selection",
                1 => $"{details.Items[details.SelectedIndex].Text} selected",
                _ => $"{count} rows selected",
            };
        });

        details.AddItemActivatedHandler(_ =>
            SetStatus($"Opened {details.Items[details.SelectedIndex].Text}."));

        details.AddColumnClickHandler((_, e) =>
            sortCaption.Text = $"Sorted by {e.Column.Text}, {details.SortOrder}");

        var icons = new ListView
        {
            Dock = XDock.Top,
            Height = 148f,
            View = View.List,
            SelectionMode = SelectionMode.One,
            VerticalScroll = ScrollStyle.Automatic,
            HorizontalScroll = ScrollStyle.Automatic,
            Margin = new Thickness(0f, 0f, 0f, 6f),
        };

        foreach (var row in SampleFiles)
        {
            icons.Items.Add(new ListViewItem(row.Name));
        }

        icons.SelectedIndex = 2;
        card.Controls.Add(icons);

        var iconsCaption = ScrollCaption("List view");
        iconsCaption.Margin = new Thickness(0f, 0f, 0f, 6f);
        card.Controls.Add(iconsCaption);

        var buttons = ButtonRow();

        var clearSort = new Button("Clear sort")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 6f, 0f),
        };

        clearSort.AddClickHandler(_ =>
        {
            details.SortOrder = SortOrder.None;
            sortCaption.Text = "Click a column header to sort";
            SetStatus("Sorting cleared.");
        });

        var grow = new Button("Add rows")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
        };

        grow.AddClickHandler(_ =>
        {
            details.BeginUpdate();
            try
            {
                for (int index = 0; index < 500; index++)
                {
                    long bytes = (index + 1) * 1_024L;
                    details.Items.Add(new ListViewItem(
                        $"generated-{details.Items.Count:D4}.bin",
                        "File",
                        FormatSize(bytes),
                        "today")
                    {
                        Tag = bytes,
                    });
                }
            }
            finally
            {
                details.EndUpdate();
            }

            SetStatus($"{details.Items.Count} rows; only the visible ones are drawn.");
        });

        buttons.Controls.Add(clearSort);
        buttons.Controls.Add(grow);

        var lockWidths = new Button("Lock widths")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(6f, 0f, 0f, 0f),
        };

        lockWidths.AddClickHandler(_ =>
        {
            details.AllowColumnResize = !details.AllowColumnResize;
            lockWidths.Text = details.AllowColumnResize ? "Lock widths" : "Unlock widths";
            SetStatus(details.AllowColumnResize
                ? "Header edges can be dragged again."
                : "Header edges are locked; the columns cannot be resized.");
        });

        buttons.Controls.Add(lockWidths);
        card.Controls.Add(buttons);

        var countCaption = ScrollCaption($"{details.Items.Count} rows");
        card.Controls.Add(countCaption);

        return card;
    }
}
