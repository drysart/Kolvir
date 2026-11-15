using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The tool strips page: the items, the overflow and the status line they write.
/// </summary>
internal sealed class ToolStripsPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ToolStripsPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// <see cref="ToolStrip"/> and <see cref="StatusStrip"/>: docked bars that hold items
    /// rather than child controls. The first card is a mock editor whose tool bar and status bar
    /// are strips; the second drives a status strip's progress item from a row of buttons.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Tool strip and status strip"));

        var readout = ScrollCaption("Click an item, or Tab to a strip and use the arrow keys.");
        readout.Margin = new Thickness(0f, 0f, 0f, 12f);
        card.Controls.Add(readout);

        var revert = new ToolStripButton("Revert") { Enabled = false };
        revert.AddClickHandler(_ => readout.Text = "Tool strip: Revert");

        ToolStripButton ToolItem(string text, string message)
        {
            var item = new ToolStripButton(text);
            item.AddClickHandler(_ =>
            {
                readout.Text = message;
                revert.Enabled = true;
            });

            return item;
        }

        var fit = ToolItem("Fit", "Tool strip: Fit");
        fit.Alignment = ToolStripItemAlignment.Far;

        var farSeparator = new ToolStripSeparator { Alignment = ToolStripItemAlignment.Far };

        var toolbar = new ToolStrip();
        toolbar.Items.Add(ToolItem("New", "Tool strip: New"));
        toolbar.Items.Add(ToolItem("Open", "Tool strip: Open"));
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(ToolItem("Save", "Tool strip: Save"));
        toolbar.Items.Add(revert);
        toolbar.Items.Add(new ToolStripSpring());
        toolbar.Items.Add(new ToolStripLabel("Zoom: 100%"));
        toolbar.Items.Add(farSeparator);
        toolbar.Items.Add(fit);

        var editorStatus = new StatusStrip();
        editorStatus.Items.Add(new ToolStripLabel("Ready")
        {
            ForeColor = ThemeColor.MutedForeground,
        });
        editorStatus.Items.Add(new ToolStripSpring());
        editorStatus.Items.Add(new ToolStripLabel("UTF-8")
        {
            ForeColor = ThemeColor.MutedForeground,
        });
        editorStatus.Items.Add(new ToolStripSeparator());
        editorStatus.Items.Add(new ToolStripProgressBar { Width = 90f, Value = 65f });

        var editor = new SurfacePanel
        {
            Dock = XDock.Top,
            Height = 156f,
            Border = Framed(8f),
            Padding = new Thickness(0f),
            Margin = new Thickness(0f, 0f, 0f, 20f),
        };

        editor.Controls.Add(toolbar);
        editor.Controls.Add(editorStatus);
        editor.Controls.Add(new Label(
            "The tool bar and the status bar are one control each, and every item in them is a plain object " +
            "rather than a child control: nine items on the top bar and five on the bottom cost one paint pass " +
            "between them. The spring takes the slack, which is what pins Zoom and Fit to the right-hand end, " +
            "and Revert stays disabled until one of the other items is used.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Padding = new Thickness(14f, 10f),
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        card.Controls.Add(editor);
        card.Controls.Add(Heading("Progress in a status strip"));

        var percentLabel = new ToolStripLabel("0%")
        {
            ForeColor = ThemeColor.MutedForeground,
        };

        var progress = new ToolStripProgressBar { Width = 220f };

        var copyStatus = new StatusStrip();
        copyStatus.Items.Add(new ToolStripLabel("Copying files..."));
        copyStatus.Items.Add(new ToolStripSeparator());
        copyStatus.Items.Add(progress);
        copyStatus.Items.Add(percentLabel);
        copyStatus.Items.Add(new ToolStripSpring());

        var clear = new ToolStripButton("Clear") { Alignment = ToolStripItemAlignment.Far };
        clear.AddClickHandler(_ =>
        {
            progress.Value = 0f;
            SyncPercent();
            readout.Text = "Status strip: Clear";
        });

        copyStatus.Items.Add(clear);

        void SyncPercent() => percentLabel.Text = $"{(int)MathF.Round(progress.Percent * 100f)}%";

        var statusBox = new SurfacePanel
        {
            Dock = XDock.Top,
            Height = 92f,
            Border = Framed(8f),
            Padding = new Thickness(0f),
            Margin = new Thickness(0f, 0f, 0f, 16f),
        };

        statusBox.Controls.Add(copyStatus);
        statusBox.Controls.Add(new Label(
            "The bar, the percentage and the Clear button are items of one status strip, so the strip is a " +
            "single tab stop: the arrow keys move between the items and Enter activates the one under them.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Padding = new Thickness(14f, 10f),
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        var row = ButtonRow();

        var start = new Button("Start")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 6f, 0f),
        };
        start.AddClickHandler(_ =>
        {
            progress.Value = 0f;
            SyncPercent();
            readout.Text = "Copy started.";
        });

        var step = new Button("Step 10")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 6f, 0f),
        };
        step.AddClickHandler(_ =>
        {
            progress.Value = MathF.Min(progress.Maximum, progress.Value + 10f);
            SyncPercent();
            readout.Text = $"Copy at {percentLabel.Text}";
        });

        var finish = new Button("Finish")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 6f, 0f),
        };
        finish.AddClickHandler(_ =>
        {
            progress.Value = progress.Maximum;
            SyncPercent();
            readout.Text = "Copy finished.";
        });

        row.Controls.Add(start);
        row.Controls.Add(step);
        row.Controls.Add(finish);

        card.Controls.Add(row);
        card.Controls.Add(statusBox);
        return card;
    }

    /// <summary>
    /// <see cref="GroupBox"/>: the caption sits in a break in the top edge and the content is
    /// laid out below it. The right-hand box scrolls, which is where the caption band earns its
    /// keep - the rows are clipped below the caption instead of sliding under it.
    /// </summary>
}
