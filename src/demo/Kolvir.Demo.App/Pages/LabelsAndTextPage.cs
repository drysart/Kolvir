using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The labels page: what a label wraps, how it aligns, and the one readout on the sample that another
/// page writes to.
/// </summary>
/// <remarks>
/// The counter below is the window's rather than this page's: the footer's Reset clears it and the
/// buttons page advances it, so the page publishes the label it wants kept current and lets the shell
/// own the number.
/// </remarks>
internal sealed class LabelsAndTextPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public LabelsAndTextPage(DemoUi shell) : base(shell) => Initialize(Build());

    private Panel Build()
    {
        var card = Card();

        card.Controls.Add(new Label("Label")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 26f,
            Font = Theme.EmphasizedFont,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Thickness(0f, 0f, 0f, 6f),
        });

        card.Controls.Add(new Label("Labels wrap long text automatically, honour nine-way alignment and share the theme fonts.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 72f,
            WrapText = true,
            Font = Theme.DefaultFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 10f),
            Selectable = true,
        });

        var counter = new Label("Button clicks: 0")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 22f,
            Font = Theme.EmphasizedFont,
            ForeColorRole = ThemeColor.Accent,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Thickness(0f, 0f, 0f, 6f),
            Selectable = true,
        };

        card.Controls.Add(counter);
        Shell.ShowClickCount(counter);

        card.Controls.Add(new Label("Multi-line text\nis supported too.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 40f,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Selectable = true,
        });

        return card;
    }
}
