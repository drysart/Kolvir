using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The buttons page: the three button styles, and the primary action the footer's own buttons disable
/// and the labels page counts.
/// </summary>
/// <remarks>
/// The primary button is the window's rather than this page's: the footer has a button that disables
/// it, and its click count is shown on another page. The page builds it and hands it over, so both
/// halves of the sample point at the same control.
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
        Shell.UsePrimaryButton(primary);

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
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        return card;
    }
}
