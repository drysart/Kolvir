using Kolvir.Feature.VirtualDom;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The rich-label markup page: markup typed into a text box, parsed into spans, and shown in a
/// <see cref="RichLabel"/> whose spans come from the node tree.
/// </summary>
/// <remarks>
/// <para>
/// The whole card is one node tree - the text box, the label, and the lines around them - mounted into
/// a host the page owns. Nothing on the card is built imperatively and then described again: the page
/// has no control list of its own, and the one control it touches by name is the host it mounts into.
/// </para>
/// <para>
/// The reconciliation is the point. Every keystroke parses the box and assigns the label a fresh
/// <c>Spans</c> sequence, so an implementation that replaced the collection on each render would show
/// the same text and lose every span's identity. The reconciler matches each description to the live
/// <see cref="TextSpan"/> instead, so a run whose text did not change is patched in place, keeps its
/// handlers, and is never rebuilt. The keyed run at the end of the label is the visible proof: it keeps
/// its instance - and the click count kept beside it - however much the runs before it change.
/// </para>
/// <para>
/// One thing this page deliberately leaves out is <see cref="TextSpan.ForControl"/>, which puts a real
/// control in the text flow. It is a separate demonstration: it parents the control into the label, and
/// a page about text does not need to make the same point twice.
/// </para>
/// </remarks>
internal sealed class RichLabelMarkupPage : DemoPage
{
    /// <summary>
    /// The key of the run the identity check rides on. An object rather than a string so that nothing
    /// the parser produces can collide with it, and constant so the reconciler finds that run by key
    /// wherever the runs before it have moved it to.
    /// </summary>
    private static readonly object ProbeKey = new();

    /// <summary>The markup the page starts with: one line per rule the grammar has.</summary>
    private const string Sample =
        "<b>Bold</b>, <i>italic</i> and <u>underlined</u> text, and <b><i>nested</i></b> styles.\n"
        + "An unknown <blink>tag</blink> is shown as typed, and so is a stray </closer>.\n"
        + "&lt; is the one escape; an unclosed <b>tag runs to the end of the text.";

    private MountHandle? _mount;
    private string _markup = Sample;

    // The span instance that last raised the probe's click, and how many times that instance has. The
    // count belongs to the instance rather than to the page, which is what makes it restart if - and
    // only if - the reconciler rebuilds the run instead of patching it.
    private TextSpan? _probedSpan;
    private int _probeClicks;

    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public RichLabelMarkupPage(DemoUi shell) : base(shell) => Initialize(Build());

    private Panel Build()
    {
        // The host is the page's own control: it is the parent the mount owns a slice of, and it is
        // what takes the handle when the page's tree is disposed. Everything inside it comes from the
        // node tree, including the card's frame.
        var host = new MarkupHost
        {
            Dock = XDock.Fill,
            Padding = new Thickness(0f),
        };

        _mount = VirtualDomRuntime.Mount(host, View());
        host.Adopt(_mount);

        return host;
    }

    /// <summary>The card as a node, built from the mark-up the box currently holds.</summary>
    private VNode View()
    {
        var runs = RichLabelMarkup.Parse(_markup);
        var spans = new List<VTextSpan>(runs.Count + 1);

        foreach (var run in runs)
        {
            // No key: a run's position is its identity, which is what makes the reconciler patch the
            // span that is already at that index rather than making a new one.
            spans.Add(new VTextSpan
            {
                Text = run.Text,
                Bold = run.Bold,
                Italic = run.Italic,
                Underline = run.Underline,
            });
        }

        // Keyed, unlike the runs above, so it is matched by the key it has carried since the first
        // render however many runs appear or disappear in front of it. Underlined and carrying a hand
        // cursor, which is the whole of the affordance a span has: the label's foreground is the only
        // colour there is, so a clickable run says so with its shape.
        spans.Add(new VTextSpan
        {
            Key = ProbeKey,
            Text = " [click]",
            Underline = true,
            Cursor = Cursor.Hand,
            Click = ProbeClicked,
        });

        return new VPanel
        {
            Dock = XDock.Fill,
            Border = Framed(10f),
            BackColor = ThemeColor.Surface,
            Padding = new Thickness(16f),
            Children =
            [
                new VLabel
                {
                    Dock = XDock.Top,
                    AutoSize = false,
                    Height = 26f,
                    Font = Theme.EmphasizedFont,
                    TextAlign = ContentAlignment.BottomLeft,
                    Text = "Rich label from markup",
                    Margin = new Thickness(0f, 0f, 0f, 8f),
                },
                new VLabel
                {
                    Dock = XDock.Top,
                    AutoSize = false,
                    Height = 68f,
                    WrapText = true,
                    Font = Theme.CaptionFont,
                    ForeColor = ThemeColor.MutedForeground,
                    TextAlign = ContentAlignment.TopLeft,
                    Text =
                        "This card is one node tree, patches included: the box, the label and these "
                        + "lines are all described by renders, and the reconciler diffs each render into "
                        + "the controls already there. Every keystroke re-parses the box and hands the "
                        + "label a fresh list of span descriptions, and a run whose text did not change "
                        + "keeps the TextSpan it already had - and the handlers on it - instead of being "
                        + "rebuilt. The underlined [click] run at the end is keyed, so it survives even "
                        + "when the runs in front of it change; its click count restarts only if that "
                        + "span is really recreated.",
                    Margin = new Thickness(0f, 0f, 0f, 6f),
                },
                new VLabel
                {
                    Dock = XDock.Top,
                    AutoSize = false,
                    Height = 34f,
                    WrapText = true,
                    Font = Theme.CaptionFont,
                    ForeColor = ThemeColor.MutedForeground,
                    TextAlign = ContentAlignment.TopLeft,
                    Text =
                        "Tags: <b>bold</b>, <i>italic</i> and <u>underline</u>, nestable, with the "
                        + "styles combining. &lt; is a literal <. An unknown, malformed or stray closing "
                        + "tag is shown as typed; an unclosed tag runs to the end.",
                    Margin = new Thickness(0f, 0f, 0f, 8f),
                },
                new VTextBox
                {
                    Id = "markup-box",
                    Dock = XDock.Top,
                    AutoSize = false,
                    Height = 96f,
                    Multiline = true,
                    PlaceholderText = "Type markup here",
                    Text = _markup,

                    // The box reports every edit, which is the same hook the other input-driven pages
                    // bind; the page re-renders from the value rather than polling the control.
                    TextChanged = Edit,
                    Margin = new Thickness(0f, 0f, 0f, 8f),
                },
                new VLabel
                {
                    Id = "markup-readout",
                    Dock = XDock.Top,
                    AutoSize = false,
                    Height = 20f,
                    Font = Theme.CaptionFont,
                    ForeColor = ThemeColor.MutedForeground,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Text = Describe(runs.Count),
                    Margin = new Thickness(0f, 0f, 0f, 6f),
                },
                new VRichLabel
                {
                    Id = "markup-preview",
                    Dock = XDock.Fill,
                    WrapText = true,
                    Spans = spans,
                },
            ],
        };
    }

    /// <summary>The line above the label: what the render produced, and how the probe is doing.</summary>
    private string Describe(int spanCount)
    {
        var runs = $"{spanCount} run(s) from the markup";

        return _probedSpan is null
            ? runs + " - click the underlined [click] run"
            : runs + $" - that run has been clicked {_probeClicks} time(s), and restarts only if it is rebuilt";
    }

    /// <summary>
    /// The box's edit: keep the value and re-render. Re-parsing and re-rendering here, in the handler
    /// the control raises, is what makes the label follow the typing rather than a timer or a poll.
    /// </summary>
    /// <param name="sender">The box that changed.</param>
    private void Edit(object sender)
    {
        if (sender is not TextBox box || string.Equals(box.Text, _markup, StringComparison.Ordinal))
        {
            return;
        }

        _markup = box.Text;
        _mount?.Update(View());
    }

    /// <summary>
    /// Counts the probe's clicks against the span that raised them: the same instance it was last
    /// clicked on keeps counting, and a different one starts again at one.
    /// </summary>
    /// <param name="sender">The span that raised the click.</param>
    /// <param name="e">The event, whose span is the live element the click landed on.</param>
    private void ProbeClicked(object sender, SpanEventArgs e)
    {
        if (!ReferenceEquals(e.Span, _probedSpan))
        {
            _probedSpan = e.Span;
            _probeClicks = 0;
        }

        _probeClicks++;
        _mount?.Update(View());
    }

    /// <summary>
    /// The page's host: a panel that takes the mount as an owned resource, so the tree is detached
    /// when the page's controls are disposed rather than outliving the controls it patched.
    /// </summary>
    private sealed class MarkupHost : Panel
    {
        internal void Adopt(MountHandle mount) => Own(mount);
    }
}
