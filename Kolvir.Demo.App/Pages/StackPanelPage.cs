using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The stack panel page: a stack in each direction, one aligned, one that scrolls.
/// </summary>
internal sealed class StackPanelPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public StackPanelPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// One control along one axis: children stretched across the stack by default, the three
    /// alignments that opt out of it, the same control turned sideways, and a stack that scrolls
    /// its own run with a docked header that stays put.
    /// </summary>
    private Panel Build()
    {
        var card = Card();

        card.Controls.Add(ScrollCaption("Vertical - each child takes its own height and the width of the stack"));
        card.Controls.Add(BuildVerticalStack());

        card.Controls.Add(ScrollCaption("Across the stack - HorizontalAlignment on each child"));
        card.Controls.Add(BuildAlignedStack());

        card.Controls.Add(ScrollCaption("Horizontal - the same control with Orientation = Horizontal"));
        card.Controls.Add(BuildHorizontalStack());

        card.Controls.Add(ScrollCaption("VerticalScroll = Automatic - the run of children is the scrollable content"));
        card.Controls.Add(BuildScrollingStack());

        return card;
    }

    /// <summary>
    /// The framing the stacks on this page share: a bordered box, so the bounds the stack arranges
    /// its children in are visible.
    /// </summary>
    private static StackPanel StackBox() => new SurfaceStackPanel()
    {
        Dock = XDock.Top,
        Padding = new Thickness(8f),
        Spacing = 6f,
        Margin = new Thickness(0f, 0f, 0f, 10f),
        Border = Framed(8f),
    };

    private StackPanel BuildVerticalStack()
    {
        var stack = StackBox();
        stack.Height = 130f;

        foreach (var text in new[] { "First", "Second", "Third" })
        {
            stack.Controls.Add(StackButton(text, Alignment.Stretch));
        }

        return stack;
    }

    private StackPanel BuildAlignedStack()
    {
        var stack = StackBox();
        stack.Height = 170f;

        stack.Controls.Add(StackButton("Near", Alignment.Near));
        stack.Controls.Add(StackButton("Center", Alignment.Center));
        stack.Controls.Add(StackButton("Far", Alignment.Far));
        stack.Controls.Add(StackButton("Stretch", Alignment.Stretch));

        return stack;
    }

    private StackPanel BuildHorizontalStack()
    {
        var stack = StackBox();
        stack.Orientation = StackOrientation.Horizontal;
        stack.Height = 60f;

        // A horizontal stack stretches its children down the stack, so the interesting property is
        // VerticalAlignment.
        foreach (var (text, alignment) in new[]
                 {
                         ("Top", Alignment.Near),
                         ("Middle", Alignment.Center),
                         ("Bottom", Alignment.Far),
                         ("Fill", Alignment.Stretch),
                     })
        {
            stack.Controls.Add(StackButton(text, alignment));
        }

        return stack;
    }

    private StackPanel BuildScrollingStack()
    {
        var stack = StackBox();
        stack.Dock = XDock.Fill;
        stack.VerticalScroll = ScrollStyle.Automatic;
        stack.Spacing = 4f;

        // A docked child is not part of the run: it keeps the top of the panel while the rows
        // scroll underneath it.
        stack.Controls.Add(new Label("Docked header - it stays put while the rows scroll")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 20f,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.MiddleLeft,
        });

        for (int index = 0; index < 40; index++)
        {
            var row = StackButton($"Row {index + 1}", Alignment.Stretch);
            row.AutoSize = false;
            row.Height = 28f;
            stack.Controls.Add(row);
        }

        return stack;
    }

    private Button StackButton(string text, Alignment alignment)
    {
        var button = new Button(text)
        {
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            HorizontalAlignment = alignment,
            VerticalAlignment = alignment,
        };
        button.AddClickHandler(_ => SetStatus($"{text} clicked - placed {alignment} across the stack."));
        return button;
    }
}
