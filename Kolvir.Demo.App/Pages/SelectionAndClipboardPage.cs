using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The selection page: what selecting text does, and what the clipboard sees.
/// </summary>
internal sealed class SelectionAndClipboardPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public SelectionAndClipboardPage(DemoUi shell) : base(shell) => Initialize(Build());

    private Panel Build()
    {
        var card = Card();

        card.Controls.Add(new Label("Selection")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 24f,
            Font = Theme.EmphasizedFont,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Thickness(0f, 0f, 0f, 4f),
        });

        // A drag starts on the label, passes over the whole-control button and ends in the text
        // box, so one gesture produces a partial, a whole and another partial range.
        card.Controls.Add(new Label("Drag down from this label to start a selection.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 40f,
            WrapText = true,
            Font = Theme.DefaultFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 6f),
            Selectable = true,
        });

        var whole = new Button("Whole control")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 0f, 6f),
            Selectable = true,
        };
        whole.AddClickHandler(_ => SetStatus("Whole-control button clicked - a drag over it selects the button as one unit."));
        card.Controls.Add(whole);

        card.Controls.Add(new TextBox
        {
            Dock = XDock.Top,
            Height = 32f,
            Text = "...and finish in this box.",
            Margin = new Thickness(0f, 0f, 0f, 6f),
        });

        card.Controls.Add(new Label("Right-click the selection for the native Copy menu.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>
    /// Check boxes on the left, radio buttons on the right: the checks cover the three states and
    /// the disabled look, and the radios show two groups on one card separated only by
    /// <see cref="RadioButton.GroupName"/> rather than by a container each.
    /// </summary>
}
