using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The progress page: the three bar styles, a disabled bar, and a step button.
/// </summary>
internal sealed class ProgressBarsPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ProgressBarsPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// The three <see cref="ProgressBarStyle"/>s, a disabled bar, and a Step button, so the
    /// fill is watched rather than described. Only the marquee bar animates, and only while the
    /// window is running a frame loop to tick it.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Progress bars"));

        var readout = ScrollCaption("35%");
        card.Controls.Add(readout);

        var continuous = new ProgressBar
        {
            Dock = XDock.Top,
            Height = 18f,
            Value = 35f,
            Margin = new Thickness(0f, 0f, 0f, 16f),
        };
        card.Controls.Add(continuous);

        card.Controls.Add(ScrollCaption("Blocks"));
        card.Controls.Add(new ProgressBar
        {
            Dock = XDock.Top,
            Height = 18f,
            Value = 60f,
            Style = ProgressBarStyle.Blocks,
            Margin = new Thickness(0f, 0f, 0f, 16f),
        });

        card.Controls.Add(ScrollCaption("Marquee (no speed set, so it follows the display)"));
        card.Controls.Add(new ProgressBar
        {
            Dock = XDock.Top,
            Height = 18f,
            Style = ProgressBarStyle.Marquee,
            Margin = new Thickness(0f, 0f, 0f, 16f),
        });

        card.Controls.Add(ScrollCaption("Disabled"));
        card.Controls.Add(new ProgressBar
        {
            Dock = XDock.Top,
            Height = 18f,
            Value = 70f,
            Enabled = false,
            Margin = new Thickness(0f, 0f, 0f, 16f),
        });

        var buttons = new Panel
        {
            Dock = XDock.Top,
            Height = 32f,
            BackColor = null,
            Padding = new Thickness(0f),
            Margin = new Thickness(0f, 0f, 0f, 12f),
        };

        var step = new Button("Step")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        step.AddClickHandler(_ =>
        {
            if (continuous.Value >= continuous.Maximum)
            {
                continuous.Value = continuous.Minimum;
            }

            continuous.PerformStep();
            readout.Text = $"{continuous.Value:0}%";
            SetStatus($"Continuous bar is at {continuous.Value:0} of {continuous.Maximum:0}.");
        });
        buttons.Controls.Add(step);

        var reset = new Button("Reset")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
        };
        reset.AddClickHandler(_ =>
        {
            continuous.Value = 0f;
            readout.Text = "0%";
            SetStatus("Continuous bar reset.");
        });
        buttons.Controls.Add(reset);

        card.Controls.Add(buttons);
        card.Controls.Add(new Label("Value is clamped to Minimum..Maximum, so PerformStep wraps only because this page resets it at the top.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 34f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>
    /// <see cref="NumericUpDown"/>: the spinner is the whole point, so the fields on this page
    /// cover a plain integer, a money amount with two decimals and a coarse step, a percentage
    /// whose increment is larger than one, and a disabled field for comparison.
    /// </summary>
}
