using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The track bars page: the orientations, the ticks and the value readout.
/// </summary>
internal sealed class TrackBarsPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public TrackBarsPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// <see cref="TrackBar"/>: a horizontal bar whose value drives a vertical one, so the two
    /// orientations can be compared against the same number, plus a bar with no ticks and a
    /// disabled one.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Track bars"));

        var readout = ScrollCaption("Value 30 of 100");
        card.Controls.Add(readout);

        var horizontal = new TrackBar
        {
            Dock = XDock.Top,
            Height = 28f,
            Maximum = 100f,
            Value = 30f,
            SmallChange = 1f,
            LargeChange = 10f,
            TickFrequency = 10f,
            Margin = new Thickness(0f, 0f, 0f, 12f),
        };

        var vertical = new TrackBar
        {
            Dock = XDock.Left,
            Width = 44f,
            Orientation = Orientation.Vertical,
            Maximum = 100f,
            Value = 30f,
            SmallChange = 1f,
            LargeChange = 10f,
            TickFrequency = 10f,
            Margin = new Thickness(0f, 0f, 16f, 0f),
        };

        horizontal.AddValueChangedHandler(_ =>
        {
            vertical.Value = horizontal.Value;
            readout.Text = $"Value {horizontal.Value:0} of 100";
        });

        card.Controls.Add(horizontal);

        var row = new Panel
        {
            Dock = XDock.Top,
            Height = 150f,
            BackColor = null,
            Padding = new Thickness(0f),
            Margin = new Thickness(0f, 0f, 0f, 12f),
        };

        row.Controls.Add(new Label("The same value, drawn vertically: the knob sits at the bottom for Minimum, so dragging up raises the value.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });
        row.Controls.Add(vertical);
        card.Controls.Add(row);

        card.Controls.Add(ScrollCaption("No tick marks (TickFrequency 0), fine steps"));

        var fine = new TrackBar
        {
            Dock = XDock.Top,
            Height = 28f,
            Minimum = -50f,
            Maximum = 50f,
            Value = 0f,
            SmallChange = 0.5f,
            LargeChange = 5f,
            TickFrequency = 0f,
            Margin = new Thickness(0f, 0f, 0f, 12f),
        };

        var fineReadout = ScrollCaption("0.0");
        fine.AddValueChangedHandler(_ => fineReadout.Text = $"{fine.Value:0.0}");
        card.Controls.Add(fine);
        card.Controls.Add(fineReadout);

        card.Controls.Add(ScrollCaption("Disabled"));

        card.Controls.Add(new TrackBar
        {
            Dock = XDock.Top,
            Height = 28f,
            Maximum = 100f,
            Value = 65f,
            TickFrequency = 25f,
            Enabled = false,
            Margin = new Thickness(0f, 0f, 0f, 12f),
        });

        card.Controls.Add(new Label(
            "Pressing the knob starts a drag and the value follows the pointer; pressing the track pages by " +
            "LargeChange toward the press. Arrow keys step by SmallChange, Page Up/Down by LargeChange, " +
            "Home and End jump to the ends, and the wheel steps one SmallChange per detent.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 62f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>
    /// The in-library menus. The bar across the top of the window is drawn by the library on
    /// Windows and Linux and reachable with Alt plus a mnemonic; this page adds a context menu that
    /// uses every row shape the renderer knows - icon, check mark, submenu, shortcut hint, disabled
    /// entry and a hosted control.
    /// </summary>
}
