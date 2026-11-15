using Kolvir.Animations;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The split buttons page: the two halves, and which one the click reached.
/// </summary>
internal sealed class SplitButtonsPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public SplitButtonsPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// <see cref="SplitButton"/> and <see cref="DropDownButton"/>: the main area runs one
    /// command and the arrow opens the menu, with a disabled one beside them to show the greying.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Split buttons"));

        card.Controls.Add(new Label(
            "A split button is a command with a menu: the label runs the default action and the " +
            "arrow opens the drop-down. The drop-down button has no second half - pressing it " +
            "anywhere opens the menu.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 42f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 12f),
        });

        var row = ButtonRow();
        row.Height = 40f;

        var save = new SplitButton("Save")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        save.AddClickHandler(_ => SetStatus("Split button: the main area ran Save."));
        save.Menu.Add("Save as...", () => SetStatus("Split button: Save as... chosen from the menu."));
        save.Menu.Add("Save a copy", () => SetStatus("Split button: Save a copy chosen from the menu."));
        save.Menu.AddSeparator();
        save.Menu.Add("Export as PDF", () => SetStatus("Split button: Export as PDF chosen from the menu."));
        row.Controls.Add(save);

        var more = new DropDownButton("More actions")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        more.AddClickHandler(_ => SetStatus("Drop-down button: the menu opened from the whole face."));
        more.Menu.Add("Rename", () => SetStatus("Drop-down button: Rename chosen from the menu."));
        more.Menu.Add("Archive", () => SetStatus("Drop-down button: Archive chosen from the menu."));
        row.Controls.Add(more);

        var disabled = new SplitButton("Disabled")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Enabled = false,
            Style = ButtonStyle.Secondary,
        };
        disabled.Menu.Add("Never shown");
        row.Controls.Add(disabled);

        card.Controls.Add(row);

        card.Controls.Add(new Label(
            "The arrow is part of the measured width, Enter and Space run the default action, and " +
            "Down opens the menu. The menu is the library's own, so it opens submenus and leaves " +
            "the window the same way every other menu does.")
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
    /// The toast's fade and slide, from <c>Kolvir.Feature.Animations</c>. One message with a
    /// duration for each direction, and a switch that takes both durations away so the difference
    /// is a press away rather than a sentence.
    /// </summary>
}
