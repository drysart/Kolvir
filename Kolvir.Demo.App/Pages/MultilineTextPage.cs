using Kolvir.AutoComplete;
using Kolvir.Multiline;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The multi-line page: the wrapping, the scrollbars and the caret readout.
/// </summary>
internal sealed class MultilineTextPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public MultilineTextPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// <see cref="TextBox"/> with <c>Multiline</c> on: a wrapped field holding more text than
    /// fits, an unwrapped one that scrolls sideways, and a wrapped one with autocomplete. The
    /// readout follows the caret's line and column, so the keyboard can be watched working.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Multi-line text"));

        var readout = ScrollCaption("Line 1, column 1 - type, press Enter, then move with the arrows");
        card.Controls.Add(readout);

        var wrapped = new TextBox
        {
            Dock = XDock.Top,
            Height = 96f,
            Multiline = true,
            PlaceholderText = "Write something",
            Text =
                "A multi-line text box wraps at word boundaries and lays its caret out on the visual " +
                "line it is drawn on rather than on a single logical line.\n\n" +
                "Enter inserts a line break and Ctrl+Enter is left for the form around it, because a " +
                "field that spans lines still needs an accept gesture. Tab still moves the focus. Up " +
                "and Down keep the column they started in, even across a short line, and Home and End " +
                "work on the visual line rather than on the whole value.\n\n" +
                "Drag the pointer across two lines to select them, then Ctrl+C and Ctrl+V: the line " +
                "breaks survive the round trip. The bar on the right appears because this text is " +
                "taller than the field, and the wheel scrolls it.",
        };

        wrapped.AddTextChangedHandler(_ => readout.Text = DescribeCaret(wrapped));
        wrapped.AddKeyUpHandler((_, _) => readout.Text = DescribeCaret(wrapped));
        wrapped.AddMouseUpHandler((_, _) => readout.Text = DescribeCaret(wrapped));
        card.Controls.Add(Caption("Wrapped (WordWrap = true)"));
        card.Controls.Add(wrapped);

        var unwrapped = new TextBox
        {
            Dock = XDock.Top,
            Height = 62f,
            Multiline = true,
            WordWrap = false,
            Text =
                "WordWrap = false keeps every logical line on one visual line and scrolls the field " +
                "sideways instead, the way a code editor does: this sentence is deliberately long so " +
                "that the horizontal bar at the bottom of the field has somewhere to go when the " +
                "caret reaches the end of it.",
        };

        unwrapped.AddTextChangedHandler(_ => readout.Text = DescribeCaret(unwrapped));
        unwrapped.AddKeyUpHandler((_, _) => readout.Text = DescribeCaret(unwrapped));
        card.Controls.Add(Caption("Unwrapped (WordWrap = false)"));
        card.Controls.Add(unwrapped);

        var completing = new TextBox
        {
            Dock = XDock.Top,
            Height = 78f,
            Multiline = true,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.CustomSource,
            PlaceholderText = "Type \"ap\" on any line",
        };

        foreach (string fruit in new[] { "Apple", "Apricot", "Banana", "Blueberry", "Cherry", "Mango", "Peach" })
        {
            completing.AutoCompleteCustomSource.Add(fruit);
        }

        card.Controls.Add(Caption("Autocomplete on a wrapped field"));
        card.Controls.Add(completing);

        card.Controls.Add(new Label(
            "The suggestion list hangs from the caret's own line, so it opens where the user is typing " +
            "rather than under the bottom of a tall field. Taking a suggestion replaces the text on that " +
            "line and leaves every other line alone, and the append modes complete nothing while the " +
            "field spans lines, because a suffix cannot cross a line break.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 52f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 8f, 0f, 0f),
        });

        return card;
    }

    /// <summary>The caret's logical line and column, and how much is selected, for the readout.</summary>
    private static string DescribeCaret(TextBox box)
    {
        var text = box.Text;
        int caret = Math.Clamp(box.CaretIndex, 0, text.Length);
        int line = 0;
        int lineStart = 0;

        for (int i = 0; i < caret; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        int column = caret - lineStart + 1;
        int lines = 1;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                lines++;
            }
        }

        return $"Line {line + 1} of {lines}, column {column} - {text.Length} characters, " +
            $"{box.SelectionLength} selected";
    }

    /// <summary>
    /// <see cref="TextBox"/> with annotation spans: a single-line field with three marked
    /// words, and a wrapped multi-line field whose longest span crosses a soft wrap so it is drawn
    /// on two visual lines. Right-clicking a squiggle reports the span that was hit, both in the
    /// context menu and in the readout above the fields.
    /// </summary>
}
