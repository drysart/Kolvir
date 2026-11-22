using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The group boxes page: a captioned frame, a disabled one, and a scrolling one.
/// </summary>
internal sealed class GroupBoxesPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public GroupBoxesPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// <see cref="GroupBox"/>: the caption sits in a break in the top edge and the content is
    /// laid out below it. The right-hand box scrolls, which is where the caption band earns its
    /// keep - the rows are clipped below the caption instead of sliding under it.
    /// </summary>
    private Panel Build()
    {
        var section = new Panel
        {
            Dock = XDock.Fill,
            Padding = new Thickness(0f),
        };

        var column = new Panel
        {
            Dock = XDock.Left,
            Width = 320f,
            Padding = new Thickness(0f),
            Margin = new Thickness(0f, 0f, 16f, 0f),
        };

        var appearance = new GroupBox("Appearance")
        {
            Dock = XDock.Top,
            Height = 168f,
            Margin = new Thickness(0f, 0f, 0f, 16f),
        };
        foreach (string caption in new[] { "Show grid", "Snap to grid", "Show rulers" })
        {
            var box = new CheckBox(caption)
            {
                Dock = XDock.Top,
                Checked = caption != "Show rulers",
                Margin = new Thickness(0f, 0f, 0f, 4f),
            };
            box.AddCheckedChangedHandler(_ => SetStatus($"{box.Text} is {Describe(box.CheckState)}."));
            appearance.Controls.Add(box);
        }

        column.Controls.Add(appearance);

        // Disabling the group box disables everything inside it, so the caption, the frame and the
        // check boxes all grey out together.
        var advanced = new GroupBox("Advanced (disabled)")
        {
            Dock = XDock.Top,
            Height = 116f,
            Enabled = false,
        };

        foreach (string caption in new[] { "Trace layout", "Cache layers" })
        {
            advanced.Controls.Add(new CheckBox(caption)
            {
                Dock = XDock.Top,
                Checked = caption == "Trace layout",
            });
        }

        column.Controls.Add(advanced);

        section.Controls.Add(column);

        var rows = new GroupBox("Rows")
        {
            Dock = XDock.Fill,
        };

        // A group box scrolls its own content below the caption; the rows go in a panel of their
        // own so they can start at its top edge and scroll as one block.
        var list = new Panel
        {
            Dock = XDock.Fill,
            Padding = new Thickness(0f),
            VerticalScroll = ScrollStyle.Automatic,
            HorizontalScroll = ScrollStyle.None,
        };

        for (int i = 0; i < 24; i++)
        {
            var row = new Button($"Row {i + 1}")
            {
                AutoSize = false,
                Style = ButtonStyle.Secondary,
                Bounds = new SKRect(0f, i * 30f, 160f, (i * 30f) + 26f),
            };
            row.AddClickHandler(_ => SetStatus($"Row {i + 1} clicked inside a group box."));
            list.Controls.Add(row);
        }

        rows.Controls.Add(list);
        section.Controls.Add(rows);
        return section;
    }

    private static string Describe(CheckState state) => state switch
    {
        CheckState.Checked => "checked",
        CheckState.Indeterminate => "indeterminate",
        _ => "unchecked",
    };

    /// <summary>
    /// The three <see cref="ProgressBarStyle"/>s, a disabled bar, and a Step button, so the
    /// fill is watched rather than described. Only the marquee bar animates, and only while the
    /// window is running a frame loop to tick it.
    /// </summary>
}
