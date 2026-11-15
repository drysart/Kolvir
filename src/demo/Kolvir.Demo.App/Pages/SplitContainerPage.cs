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
        split.AddSplitterMovedHandler(_ => SetStatus($"Splitter moved: panel 1 is {split.SplitterDistance:0} px wide."));

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
        nested.Panel2.Controls.Add(new Label("Drag either splitter - the dashed band - to resize. The second panel always fills the remaining space.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        split.Panel2.Controls.Add(nested);
        section.Controls.Add(split);

        return section;
    }
}
