using Kolvir.AutoComplete;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The autocomplete page: the three modes and the values each suggests.
/// </summary>
internal sealed class AutoCompletePage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public AutoCompletePage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// <see cref="TextBox"/> autocomplete: the three modes over a custom source, the history
    /// source, and a disabled field. The readout follows whichever field was typed in last, so the
    /// completed text can be watched while the suffix is selected.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Text box autocomplete"));

        var readout = ScrollCaption("SuggestAppend: type \"ap\" to see \"Apple\" completed and listed");
        card.Controls.Add(readout);

        string[] fruits =
        [
            "Apple", "Apricot", "Banana", "Blackberry", "Blueberry", "Cherry", "Cranberry", "Grape",
                "Lemon", "Lime", "Mango", "Orange", "Peach", "Pear", "Pineapple", "Plum", "Raspberry",
                "Strawberry",
            ];

        var suggest = AutoCompleteBox(AutoCompleteMode.Suggest, fruits);
        suggest.PlaceholderText = "Type a letter";
        suggest.AddTextChangedHandler(_ => readout.Text = $"Suggest: \"{suggest.Text}\"");
        card.Controls.Add(CaptionRow("Suggest (CustomSource)", suggest));

        var suggestAppend = AutoCompleteBox(AutoCompleteMode.SuggestAppend, fruits);
        suggestAppend.PlaceholderText = "Type a letter";
        suggestAppend.AddTextChangedHandler(_ => readout.Text =
            $"SuggestAppend: \"{suggestAppend.Text}\" ({suggestAppend.SelectionLength} selected)");
        card.Controls.Add(CaptionRow("SuggestAppend (CustomSource)", suggestAppend));

        var append = AutoCompleteBox(AutoCompleteMode.Append, fruits);
        append.PlaceholderText = "Type a letter";
        append.AddTextChangedHandler(_ => readout.Text =
            $"Append: \"{append.Text}\" ({append.SelectionLength} selected)");
        card.Controls.Add(CaptionRow("Append (CustomSource)", append));

        var history = new TextBox
        {
            AutoCompleteMode = AutoCompleteMode.Suggest,
            AutoCompleteSource = AutoCompleteSource.HistoryList,
            PlaceholderText = "Enter a value, then type it again",
        };
        history.AddTextChangedHandler(_ => readout.Text = $"HistoryList: \"{history.Text}\"");
        card.Controls.Add(CaptionRow("HistoryList (Suggest)", history));

        var disabled = AutoCompleteBox(AutoCompleteMode.Suggest, fruits);
        disabled.Enabled = false;
        disabled.Text = "Cannot be suggested";
        card.Controls.Add(CaptionRow("Disabled", disabled));

        card.Controls.Add(new Label(
            "Typing opens the list under the caret. Up and Down move the highlight, Enter takes it, " +
            "Escape closes the list without clearing the text, and the box keeps the caret the whole " +
            "time. In the two append modes the rest of the first match is appended and selected, so " +
            "the next keystroke replaces it instead of adding to it.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 66f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 6f, 0f, 0f),
        });

        return card;
    }

    /// <summary>A text box over a custom source, with the values the suggestion list filters.</summary>
    private static TextBox AutoCompleteBox(AutoCompleteMode mode, IEnumerable<string> values)
    {
        var box = new TextBox
        {
            AutoCompleteMode = mode,
            AutoCompleteSource = AutoCompleteSource.CustomSource,
        };

        foreach (string value in values)
        {
            box.AutoCompleteCustomSource.Add(value);
        }

        return box;
    }

    /// <summary>A caption with a wide field on the right, so a column of fields lines up.</summary>
}
