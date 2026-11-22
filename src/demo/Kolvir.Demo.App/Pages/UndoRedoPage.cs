using Kolvir.Multiline;
using Kolvir.Spans;
using Kolvir.Undo;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The undo page: the history, the typing and what a group of edits undoes at once.
/// </summary>
internal sealed class UndoRedoPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public UndoRedoPage(DemoUi shell) : base(shell) => Initialize(Build());

    public void FocusUndoDemo() => _undoFocusTarget?.Focus();

    private TextBox? _undoFocusTarget;

    private static readonly string[] Misspellings = ["jumpped", "lazzy", "grammer", "teh"];

    /// <summary>
    /// <see cref="TextBox"/> with the undo history on. The page is built so the three things
    /// the feature promises can be seen: a burst of typing undoes as one step while a pause splits
    /// it, a replaced selection comes back with the selection it replaced, and the caret and the
    /// multi-line view return to where the edit was made. The readout follows
    /// <c>CanUndo</c>/<c>CanRedo</c> and the two counts, so a keystroke that did or did not join the
    /// run before it is visible.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Undo and redo"));

        var readout = ScrollCaption("Type, pause for more than five seconds, type again - then Ctrl+Z twice, Ctrl+Y once (Cmd on macOS)");
        card.Controls.Add(readout);

        // Text first, then the history, so the page opens with an empty one and the readout starts
        // at "Undo 0" - the assignment itself would otherwise be the first entry.
        var single = new TextBox
        {
            Dock = XDock.Top,
            Height = 32f,
            Text = "The quick brown fox",
            UndoEnabled = true,
        };

        _undoFocusTarget = single;

        void Sync()
        {
            readout.Text =
                $"Undo {single.UndoCount} - Redo {single.RedoCount} - caret {single.CaretIndex}, " +
                $"{single.SelectionLength} selected - CanUndo {(single.CanUndo ? "yes" : "no")}, " +
                $"CanRedo {(single.CanRedo ? "yes" : "no")}";
        }

        single.AddTextChangedHandler(_ => Sync());
        single.AddKeyUpHandler((_, _) => Sync());
        single.AddMouseUpHandler((_, _) => Sync());

        // The history tells the page when it changed, which is what a real application would use to
        // enable its Undo and Redo commands - including when the change came from Ctrl+Z rather
        // than from a keystroke the page can see.
        single.AddUndoStateChangedHandler(_ => Sync());
        Sync();

        card.Controls.Add(Caption("Single line - a typing run, a pause, then a replaced selection"));
        card.Controls.Add(single);

        var row = ButtonRow();
        row.Controls.Add(CommandButton("Undo", () =>
        {
            SetStatus(single.Undo()
                ? "Undo applied - the caret and the selection came back with the text."
                : "Nothing to undo.");
            Sync();
        }));
        row.Controls.Add(CommandButton("Redo", () =>
        {
            SetStatus(single.Redo()
                ? "Redo applied - the caret went back to where the change left it."
                : "Nothing to redo.");
            Sync();
        }));
        row.Controls.Add(CommandButton("Select \"brown\"", () =>
        {
            single.Select(single.Text.IndexOf("brown", StringComparison.Ordinal), 5);
            SetStatus("Selected \"brown\" - type over it and undo puts the word, and the selection, back.");
            Sync();
        }));
        row.Controls.Add(CommandButton("Clear history", () =>
        {
            single.ClearUndoStack();
            SetStatus("ClearUndoStack() dropped both sides; the text is untouched.");
            Sync();
        }));
        row.Controls.Add(CommandButton("Lock field", () =>
        {
            single.ReadOnly = !single.ReadOnly;
            SetStatus(single.ReadOnly
                ? "Read-only: the history is kept, but CanUndo and CanRedo report no."
                : "Editable again: the history that was kept is available.");
            Sync();
        }));
        card.Controls.Add(row);

        // The rows are part of the control's own editing block, so a right-click shows them next to
        // Cut, Copy and Paste, and re-opening the menu after an undo is how the page shows Redo
        // lighting up.
        card.Controls.Add(Caption(
            "Right-click the field: Undo and Redo lead the editing block, disabled while the stack " +
            "is empty. Undo from the menu, then open it again - Redo is offered."));

        // A multi-line document: the history spans line breaks like any other character, and an
        // entry carries the view's scroll offsets with it, so undo comes back to the line it left.
        var document = new TextBox
        {
            Dock = XDock.Top,
            Height = 104f,
            Multiline = true,
            Text =
                "A multi-line field records an entry the same way, with the view's two scroll " +
                "offsets on either side of the change.\n\n" +
                "Type at the end of this line, scroll away with the wheel, then press Ctrl+Z: the " +
                "text loses the characters and the view returns to where they were typed.\n\n" +
                "Undo and redo arrive as ordinary text changes, so anything watching the value - a " +
                "checker, a binding, the autocomplete list - sees them the way it sees typing.\n\n" +
                "- A run survives a pause shorter than five seconds.\n" +
                "- A longer pause starts a new entry.\n" +
                "- Backspace coalesces on a run of its own.\n" +
                "- Typing over a selection is always one entry.\n" +
                "- The caret and the selection travel with the entry.\n" +
                "- Turning Multiline off drops the history.\n\n" +
                "The bar on the right is there because this document is taller than the field, which " +
                "is what makes the restored scroll offset visible.",
            UndoEnabled = true,
        };

        var documentReadout = Caption(DescribeCaret(document) + " - scroll 0");
        card.Controls.Add(documentReadout);
        document.AddTextChangedHandler(_ =>
            documentReadout.Text = $"{DescribeCaret(document)} - scroll {document.VerticalOffset:0}");
        document.AddKeyUpHandler((_, _) =>
            documentReadout.Text = $"{DescribeCaret(document)} - scroll {document.VerticalOffset:0}");
        card.Controls.Add(document);

        // The span contract in one line of code: the box never rewrites the caller's spans, so a
        // checker re-marks on the change - and an undo is a change like any other.
        const string marked = "The quick brown fox jumpped over the lazzy dog.";
        var spelled = new TextBox
        {
            Dock = XDock.Top,
            Height = 32f,
            Text = marked,
            UndoEnabled = true,
        };
        Remark(spelled);
        spelled.AddTextChangedHandler(_ => Remark(spelled));
        card.Controls.Add(Caption("Spans are re-marked after an undo - the checker runs on the change"));
        card.Controls.Add(spelled);

        var locked = new TextBox
        {
            Dock = XDock.Top,
            Height = 32f,
            Text = "Read-only: the history is kept, but CanUndo reports no",
            ReadOnly = true,
            UndoEnabled = true,
        };
        card.Controls.Add(Caption("Read-only"));
        card.Controls.Add(locked);

        card.Controls.Add(new Label(
            "Each entry holds the caret and the selection on both sides of the change, so undo and " +
            "redo put the insertion point back where the user left it instead of at position 0 or " +
            "at the end. Characters typed within five seconds of the previous one join that " +
            "character's entry; a longer pause starts a new one; Backspace coalesces on a run of its " +
            "own, so typing and deleting never merge; and a change over a non-zero selection is " +
            "always one entry, so undo restores the replaced text rather than un-replacing one " +
            "character at a time. The history keeps the newest 100 entries and a bounded amount of " +
            "text, and it runs no timer: the window is measured from a clock, not from a tick.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 78f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 8f, 0f, 0f),
        });

        return card;
    }

    /// <summary>A button that docks into a <see cref="ButtonRow"/>.</summary>
    private static Button CommandButton(string text, Action onClick)
    {
        var button = new Button(text)
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 6f, 0f),
        };

        button.AddClickHandler(_ => onClick());
        return button;
    }

    /// <summary>
    /// Re-computes the squiggles from the current text and hands the list over. This is the whole
    /// of the application's side of the span contract, and it is what makes an undo look right:
    /// the box never edits the list, so the checker assigns a new one whenever the text changes.
    /// </summary>
    private static void Remark(TextBox box)
    {
        var text = box.Text;
        List<TextBoxSpan> spans = [];

        foreach (string word in Misspellings)
        {
            int at = text.IndexOf(word, StringComparison.Ordinal);
            if (at >= 0)
            {
                spans.Add(new TextBoxSpan(at, word.Length) { Tag = word });
            }
        }

        // An empty list removes the feature again, so a corrected field costs nothing.
        box.Spans = spans;
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
    /// Puts the caret in the undo page's single-line field, which is what <c>--undo-focus</c> is for:
    /// a screenshot or a script needs the field ready to type without hunting for it with the
    /// pointer.
    /// </summary>
}
