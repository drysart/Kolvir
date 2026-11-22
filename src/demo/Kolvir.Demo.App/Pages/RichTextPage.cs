using Kolvir.Spans;
using Kolvir.Undo;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The rich text page: a label whose spans carry their own look.
/// </summary>
internal sealed class RichTextPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public RichTextPage(DemoUi shell) : base(shell) => Initialize(Build());

    private Panel Build()
    {
        var card = Card();

        card.Controls.Add(new Label("Rich label")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 26f,
            Font = Theme.EmphasizedFont,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Thickness(0f, 0f, 0f, 6f),
        });

        var rich = new RichLabel
        {
            Dock = XDock.Top,
            AutoSize = true,
            WrapText = true,
            Font = Theme.DefaultFont,
            Selectable = true,
        };

        rich.AddText("One flow of ");
        rich.AddSpan(new TextSpan("bold") { Bold = true });
        rich.AddText(", ");
        rich.AddSpan(new TextSpan("italic") { Italic = true });
        rich.AddText(", ");
        rich.AddSpan(new TextSpan("underline test") { Underline = true });
        rich.AddText(", ");
        rich.AddSpan(new TextSpan("colour") { ForeColor = ThemeColor.Accent });
        rich.AddText(" and ");
        rich.AddSpan(new TextSpan("struck") { Strikethrough = true });
        rich.AddText(" text, plus a ");

        var link = rich.AddLink("clickable link", span => SetStatus($"Link span clicked: \"{span.Text}\" (tag: {span.Tag ?? "none"})."));
        link.Tag = "demo";
        link.AddMouseEnterHandler(_ => SetStatus("Pointer entered a link span - spans receive mouse events."));
        link.AddMouseLeaveHandler(_ => SetStatus("Pointer left the link span."));

        rich.AddText(". Only the glyphs of a span are hit-tested.");
        rich.AddText("\nLive chips in the flow, aligned per line: ");
        rich.AddInlineControl(Chip("top"), InlineAlignment.Top);
        rich.AddText(" ");
        rich.AddInlineControl(Chip("middle"), InlineAlignment.Center);
        rich.AddText(" ");
        rich.AddInlineControl(Chip("bottom"), InlineAlignment.Bottom);

        // Shown on right-click while nothing is selected; once a range is selected the host swaps
        // in the Copy menu instead, so both paths are reachable from this one control.
        rich.ContextMenu = BuildRichTextMenu();

        card.Controls.Add(rich);
        return card;
    }

    private ContextMenu BuildRichTextMenu()
    {
        var formatting = new MenuItem("Show formatting", () =>
            SetStatus("'Show formatting' picked - a checked item shows a tick."))
        {
            Checked = true,
            Shortcut = new Shortcut(Key.F, Modifiers.Control | Modifiers.Shift),
        };

        var menu = new ContextMenu();

        menu.Add(new MenuItem("Copy rich text", () => SetStatus("'Copy rich text' picked."))
        {
            IsDefault = true,
            Shortcut = new Shortcut(Key.C),
        });

        menu.Add(new MenuItem("Copy as plain text", () => SetStatus("'Copy as plain text' picked."))
        {
            Shortcut = new Shortcut(Key.C, Modifiers.Control | Modifiers.Shift),
        });

        menu.AddSeparator();
        menu.Add(formatting);
        menu.Add(new MenuItem("Undo formatting", null) { Enabled = false, Shortcut = new Shortcut(Key.Z) });
        return menu;
    }
}
