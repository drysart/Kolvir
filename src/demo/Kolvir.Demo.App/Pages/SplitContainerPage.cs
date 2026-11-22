using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The split container page: a divider that moves, and two panes that keep their own chrome.
/// </summary>
internal sealed class SplitContainerPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public SplitContainerPage(DemoUi shell) : base(shell) => Initialize(Build());

    private Panel Build()
    {
        // The split container's own panels paint nothing, so the surface behind them is the page's.
        var section = new SurfacePanel
        {
            Dock = XDock.Fill,
            Border = Border.None,
            Padding = new Thickness(0f),
        };

        var split = new SplitContainer
        {
            Dock = XDock.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 260f,
            Panel1MinSize = 140f,
            Panel1MaxSize = 420f,
            Panel2MinSize = 180f,
        };
        // What the split measures right now, so a window resize can be watched doing what the
        // radios above advertise.
        var readout = ScrollCaption(string.Empty);

        split.AddSplitterMovedHandler(_ => Report());
        split.AddBoundsChangedHandler(_ => Report());
        Report();

        split.Panel1.Controls.Add(Heading("Navigation"));
        split.Panel1.Controls.Add(DemoButton("Dashboard", () => SetStatus("Dashboard selected.")));
        split.Panel1.Controls.Add(DemoButton("Reports", () => SetStatus("Reports selected.")));
        split.Panel1.Controls.Add(DemoButton("Settings", () => SetStatus("Settings selected.")));

        var nested = new SplitContainer
        {
            Dock = XDock.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 64f,
            Panel1MinSize = 40f,
            Panel2MinSize = 40f,
        };
        nested.Panel1.Controls.Add(Heading("Panel 1 of a horizontal split container"));
        nested.Panel2.Controls.Add(new Label(
            "Drag either splitter - the dashed band - to resize. The panel picked above is the one that " +
            "keeps its width when the window is resized; the other one takes the difference.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        split.Panel2.Controls.Add(nested);

        section.Controls.Add(FixedPanelRow(split, Report));
        section.Controls.Add(readout);
        section.Controls.Add(split);

        return section;

        void Report()
        {
            readout.Text = split.FixedPanel switch
            {
                FixedPanel.Panel2 => $"Panel 2 keeps its {split.FixedPanelSize:0} px width when the window resizes.",
                FixedPanel.None => $"Neither panel is fixed: the split keeps {Share(split):0}% of the width.",
                _ => $"Panel 1 keeps its {split.FixedPanelSize:0} px width when the window resizes.",
            };
        }
    }

    /// <summary>How much of the splitter's track the first panel takes, as a percentage.</summary>
    private static float Share(SplitContainer split) =>
        split.Width > 0f ? split.Panel1.Width / split.Width * 100f : 0f;

    /// <summary>
    /// The three answers to "which panel keeps its width", so the difference can be watched by
    /// resizing the window rather than taken on trust.
    /// </summary>
    private Control FixedPanelRow(SplitContainer split, Action report)
    {
        var row = new Panel
        {
            Dock = XDock.Top,
            Height = 30f,
            BackColor = null,
            Padding = new Thickness(0f),
            Margin = new Thickness(0f, 0f, 0f, 8f),
        };

        row.Controls.Add(new Label("On resize:")
        {
            Dock = XDock.Left,
            Width = 76f,
            AutoSize = false,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.MiddleLeft,
        });

        foreach (var (panel, caption) in new[]
        {
            (FixedPanel.Panel1, "Panel 1 keeps its width"),
            (FixedPanel.Panel2, "Panel 2 keeps its width"),
            (FixedPanel.None, "Neither (proportional)"),
        })
        {
            var radio = new RadioButton(caption)
            {
                GroupName = "split-fixed-panel",
                Dock = XDock.Left,
                Width = 200f,
                AutoSize = false,
                Checked = panel == split.FixedPanel,
                Margin = new Thickness(0f),
            };

            radio.AddCheckedChangedHandler(_ =>
            {
                if (!radio.Checked)
                {
                    return;
                }

                split.FixedPanel = panel;
                report();
                SetStatus($"Resizing the window now keeps: {caption.ToLowerInvariant()}.");
            });

            row.Controls.Add(radio);
        }

        return row;
    }
}
