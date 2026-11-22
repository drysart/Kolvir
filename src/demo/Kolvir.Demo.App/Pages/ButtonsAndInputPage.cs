using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The buttons page: the three button styles, the switch that enables and disables the primary action,
/// and the primary action the labels page counts.
/// </summary>
/// <remarks>
/// The click count is the window's rather than this page's, and the labels page is what shows it. The
/// page builds the primary button and hands it to no one: the switch that disables it sits directly
/// under it, so both halves of the sample are on the page that owns them.
/// </remarks>
internal sealed class ButtonsAndInputPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ButtonsAndInputPage(DemoUi shell) : base(shell) => Initialize(Build());

    private Panel Build()
    {
        var card = Card();

        var primary = new Button("Primary action")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = ButtonStyle.Primary,
            Margin = new Thickness(0f, 0f, 0f, 10f),
        };
        primary.AddClickHandler(_ =>
        {
            int clicks = Shell.CountAClick();
            SetStatus($"Primary action clicked {clicks} time(s).");
        });
        card.Controls.Add(primary);

        var enableSwitch = new Button("Disable primary")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 0f, 10f),
        };
        enableSwitch.AddClickHandler(_ =>
        {
            primary.Enabled = !primary.Enabled;
            enableSwitch.Text = primary.Enabled ? "Disable primary" : "Enable primary";
            SetStatus(primary.Enabled ? "Primary action enabled." : "Primary action disabled.");
        });
        card.Controls.Add(enableSwitch);

        var secondary = new Button("Secondary action")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 0f, 10f),
        };
        secondary.AddClickHandler(_ => SetStatus("Secondary action clicked."));
        card.Controls.Add(secondary);

        var ghost = new Button("Ghost action")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = ButtonStyle.Ghost,
            Margin = new Thickness(0f, 0f, 0f, 10f),
        };
        ghost.AddClickHandler(_ => SetStatus("Ghost action clicked."));
        card.Controls.Add(ghost);

        card.Controls.Add(new Label("Tab moves focus between controls; Enter or Space activates the focused button.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 40f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        return card;
    }
}
