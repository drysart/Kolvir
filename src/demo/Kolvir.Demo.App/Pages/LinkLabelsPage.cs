using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The link labels page: what a link does, and what it looks like when it is not one.
/// </summary>
internal sealed class LinkLabelsPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public LinkLabelsPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// <see cref="LinkLabel"/>: two links that hand their URI to the desktop, one that
    /// navigates in the application instead, a sentence with a link inside it, and a disabled
    /// link - which greys out rather than staying the accent colour.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Link labels"));

        var readout = ScrollCaption("Click a link, or Tab to one and press Enter.");
        card.Controls.Add(readout);

        var home = new LinkLabel("Project home", "https://github.com/drysart/Kolvir")
        {
            Dock = XDock.Top,
            Margin = new Thickness(0f, 0f, 0f, 10f),
        };

        home.AddLinkClickedHandler((_, e) =>
            readout.Text = e.Opened ? $"Handed {e.Url} to the desktop" : $"Could not open {e.Url}");

        var issue = new LinkLabel("Report an issue", "https://github.com/drysart/Kolvir/issues")
        {
            Dock = XDock.Top,
            Margin = new Thickness(0f, 0f, 0f, 10f),
        };

        issue.AddLinkClickedHandler((_, e) =>
            readout.Text = e.Opened ? $"Handed {e.Url} to the desktop" : $"Could not open {e.Url}");

        // No Url at all: the label is only a click target, and the handler decides what happens.
        var inApp = new LinkLabel("What's new (navigates inside the app)")
        {
            Dock = XDock.Top,
            AutoLaunch = false,
            Margin = new Thickness(0f, 0f, 0f, 16f),
        };

        inApp.AddLinkClickedHandler((_, e) => readout.Text = $"In-app navigation from \"{e.Span?.Text}\"");

        // A sentence with a link inside it: the label is a rich label, so the link is just one span
        // among the plain ones around it.
        var sentence = new LinkLabel
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 44f,
            WrapText = true,
            AutoLaunch = false,
            Margin = new Thickness(0f, 0f, 0f, 16f),
        };

        sentence.AddText("Every control in this library draws itself with Skia and owns no native handle, so ");
        sentence.AddLink("the whole set", span => readout.Text = $"Span \"{span.Text}\" clicked");
        sentence.AddText(" works the same way on Windows, macOS and Linux.");

        card.Controls.Add(home);
        card.Controls.Add(issue);
        card.Controls.Add(inApp);
        card.Controls.Add(sentence);

        card.Controls.Add(new LinkLabel("A disabled link", "https://example.com")
        {
            Dock = XDock.Top,
            Enabled = false,
            Margin = new Thickness(0f, 0f, 0f, 16f),
        });

        card.Controls.Add(new Label(
            "A link is an RichLabel whose text is a link span, so AddText and AddLink build a sentence around it. " +
            "Clicking one, or pressing Enter or Space while it has the focus, raises LinkClicked; with AutoLaunch on " +
            "and a Url set, the label also hands the URI to the desktop through Launcher.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 62f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>
    /// <see cref="ToolStrip"/> and <see cref="StatusStrip"/>: docked bars that hold items
    /// rather than child controls. The first card is a mock editor whose tool bar and status bar
    /// are strips; the second drives a status strip's progress item from a row of buttons.
    /// </summary>
}
