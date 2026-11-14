using Kolvir.Multiline;
using Kolvir.Spans;
using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The spans page: a text box whose spans carry their own formatting.
/// </summary>
internal sealed class SpanTextPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public SpanTextPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// <see cref="TextBox"/> with annotation spans: a single-line field with three marked
    /// words, and a wrapped multi-line field whose longest span crosses a soft wrap so it is drawn
    /// on two visual lines. Right-clicking a squiggle reports the span that was hit, both in the
    /// context menu and in the readout above the fields.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Text box spans"));

        var readout = ScrollCaption("Right-click a squiggle - the menu and this line name the span that was hit");
        card.Controls.Add(readout);

        const string sentence =
            "The quick brown fox jumpped over the lazzy dog, and a grammer checker noticed.";
        var single = new TextBox
        {
            Dock = XDock.Top,
            Height = 32f,
            Text = sentence,
            Spans =
            [
                Spelling(sentence, "jumpped"),
                    Spelling(sentence, "lazzy"),
                    Grammar(sentence, "grammer"),
                ],
        };
        AttachSpanMenu(single, readout);
        card.Controls.Add(Caption("Single line - two spelling spans and one grammar span"));
        card.Controls.Add(single);

        const string paragraph =
            "Spans are caller-owned: the box keeps the list by reference, never edits it, and clamps " +
            "a stale span where it is drawn instead of dropping it. This whole sentence is one " +
            "annotation, so its squiggly underline has to break at the end of the first visual line " +
            "and pick up again at the start of the next one - that is what a wrapped multi-line " +
            "field does, and it is why the marker is drawn once per visual line rather than once per " +
            "annotation.\n" +
            "Teh line below holds a short span of its own.";

        var wrapped = new TextBox
        {
            Dock = XDock.Top,
            Height = 122f,
            Multiline = true,
            Text = paragraph,
            Spans =
            [
                FromTo(paragraph, "This whole sentence", "a wrapped multi-line"),
                    Grammar(paragraph, "Teh"),
                ],
        };
        card.Controls.Add(Caption("Multi-line and wrapped - the long span crosses a soft wrap"));
        card.Controls.Add(wrapped);
        AttachSpanMenu(wrapped, readout);

        card.Controls.Add(new Label(
            "A span declares a start index, a length and how its squiggle is drawn - colour, stroke " +
            "weight, wave height and wavelength - and is meant to be subclassed, so a checker can hang " +
            "its suggestion list or rule id on it. Assigning Spans replaces the whole set by reference, " +
            "which is what a checker wants on every keystroke. The box never rewrites a span when the " +
            "text changes: an out-of-range one is clamped where it is drawn and an empty one is " +
            "skipped. The context menu event reports the span the right-click landed in through its " +
            "Span property, correctly after scrolling and after re-wrapping.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 78f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 8f, 0f, 0f),
        });

        return card;
    }

    /// <summary>A span over one word, marked the way a spelling checker marks it.</summary>
    private static TextBoxSpan Spelling(string text, string word) =>
            new(text.IndexOf(word, StringComparison.Ordinal), word.Length) { Tag = word };

    /// <summary>A span over one word in the accent colour, the way a grammar checker marks it.</summary>
    private static TextBoxSpan Grammar(string text, string word) =>
            new(text.IndexOf(word, StringComparison.Ordinal), word.Length)
            {
                Tag = word,
                ThemeColor = ThemeColor.Accent,
            };

    /// <summary>A span from the start of one word to the end of another, for a run of text.</summary>
    private static TextBoxSpan FromTo(string text, string from, string to)
    {
        int start = text.IndexOf(from, StringComparison.Ordinal);
        int end = text.IndexOf(to, StringComparison.Ordinal) + to.Length;

        return new(start, Math.Max(0, end - start)) { Tag = text[start..end] };
    }

    /// <summary>
    /// Gives one field the right-click behaviour of a text box that carries annotations, the way an
    /// application would write it: the handler is attached to the control, so nothing outside the
    /// control has to know which field was pressed, and it only <em>adds</em> to the menu the box
    /// contributed. The result reads like an editor: the suggestions for the marked run, a
    /// separator, then the ordinary Cut/Copy/Paste/Select All. The page-level menu stays a host
    /// concern because it belongs to the window rather than to any one control.
    /// </summary>
    private void AttachSpanMenu(TextBox box, Label readout)
    {
        box.AddContextMenuOpeningHandler(args =>
        {
            if (args.Span is not { } span || span.Tag is not string marked)
            {
                readout.Text = "The press missed every span - Span was null, so only the editing commands are offered";
                return;
            }

            string word = marked.Length <= 24 ? marked : marked[..21] + "...";
            readout.Text = $"Right-click hit the span on \"{word}\" - start {span.Start}, length {span.Length}";

            var menu = args.Menu ??= new ContextMenu();

            // In front of the editing block, in reading order, then one separator between the two.
            menu.Insert(0, new MenuItem("&Add to dictionary", () => SetStatus($"\"{word}\" added to the dictionary.")));
            menu.Insert(0, new MenuItem($"&Ignore \"{word}\"", () => SetStatus($"\"{word}\" added to the ignore list.")));
            menu.Insert(0, new MenuItem($"&Replace \"{word}\"", () => SetStatus($"Replace picked for \"{word}\".")));
            menu.Insert(3, MenuItem.Separator);
        });
    }

    /// <summary>
    /// Right-clicks a squiggle on the spans page as if the pointer had gone there, which is what a
    /// screenshot needs in order to show the context menu - and the readout - naming the span. It
    /// goes through the same host entry point the platform uses, so it proves the dispatch, not a
    /// shortcut around it.
    /// </summary>
    /// <param name="window">The window the sample is showing in.</param>
    public void OpenSpanDemo(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        // Nothing has painted yet, so the page has no bounds until something lays it out.
        window.Host.PerformLayout();

        if (FindSpannedBox(Root) is not { Spans: { Count: > 0 } spans } box)
        {
            return;
        }

        // The last visual-line segment rather than the first, so the picture proves that a span
        // which crosses a soft wrap still resolves from the line it wrapped onto.
        var rects = new List<SKRect>();
        box.GetSelectionRects(spans[0].Start, spans[0].Length, rects);
        if (rects.Count == 0)
        {
            return;
        }

        var segment = rects[^1];
        var origin = box.GetPositionInHost();
        window.Host.ProcessContextMenu(new SKPoint(origin.X + segment.MidX, origin.Y + segment.MidY));
    }

    /// <summary>
    /// The field the screenshot path presses in: the wrapped one when the page has one, so a span
    /// that crosses a soft wrap is the one that gets resolved, and any annotated field otherwise.
    /// </summary>
    private static TextBox? FindSpannedBox(Control control)
    {
        var found = Search(control, multilineOnly: true) ?? Search(control, multilineOnly: false);
        return found;

        static TextBox? Search(Control node, bool multilineOnly)
        {
            if (node is TextBox { Spans: { Count: > 0 } } box && (!multilineOnly || box.Multiline))
            {
                return box;
            }

            foreach (var child in node.Controls)
            {
                if (Search(child, multilineOnly) is { } match)
                {
                    return match;
                }
            }

            return null;
        }
    }

    /// <summary>A text box over a custom source, with the values the suggestion list filters.</summary>
}
