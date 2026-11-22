using Kolvir.Animations;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The expander page: the collapse, the animation and the header's own controls.
/// </summary>
internal sealed class ExpanderPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ExpanderPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// <see cref="Expander"/>: the settings-page shape, one section open and one folded, with a
    /// disabled one to show that the cascade reaches the header as well as the content.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Expander"));

        card.Controls.Add(new Label(
            "A header with a chevron and content that folds away under it. Clicking the header, " +
            "or Enter, Space, Left and Right while it has the focus, expands and collapses the " +
            "section. The first one below animates the fold, the second jumps - the same control, " +
            "with and without a duration from the animations package.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 56f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 12f),
        });

        var column = new Panel
        {
            Dock = XDock.Top,
            Height = 340f,
            Padding = new Thickness(0f),
        };

        var animated = new Expander("Animated - 180 ms")
        {
            Dock = XDock.Top,
            Height = 128f,
            Margin = new Thickness(0f, 0f, 0f, 12f),
        };
        animated.ExpandDuration = TimeSpan.FromMilliseconds(180);
        foreach (string caption in new[] { "Show grid", "Snap to grid", "Show rulers" })
        {
            var box = new CheckBox(caption)
            {
                Dock = XDock.Top,
                AutoSize = true,
                Checked = caption != "Show rulers",
                Margin = new Thickness(0f, 0f, 0f, 4f),
            };
            box.AddCheckedChangedHandler(_ => SetStatus($"{box.Text} is {Describe(box.CheckState)}."));
            animated.Controls.Add(box);
        }

        // Tab is the point of this page as much as the fold is, so every stop says its name when
        // it takes the focus: Tab skips the boxes of a folded section and comes back to them when
        // it opens, and folding a section under one of its own boxes brings the focus up to the
        // header here.
        WatchFocus(animated, "the animated section's header");
        foreach (var box in animated.Controls)
        {
            WatchFocus(box, $"the animated section's \u201c{box.Text}\u201d box");
        }

        animated.AddExpandedChangedHandler(sender =>
        {
            var section = (Expander)sender;
            SetStatus(section.IsExpanded
                ? "The animated section is opening - the arrow turns as the content unfolds."
                : "The animated section is closing - progress and arrow come down together.");
        });

        column.Controls.Add(animated);

        var instant = new Expander("Instant - no duration")
        {
            Dock = XDock.Top,
            Height = 104f,
            Margin = new Thickness(0f, 0f, 0f, 12f),
        };
        instant.Controls.Add(new CheckBox("Trace layout") { Dock = XDock.Top, AutoSize = true });
        instant.Controls.Add(new CheckBox("Cache layers") { Dock = XDock.Top, AutoSize = true });
        WatchFocus(instant, "the instant section's header");
        foreach (var box in instant.Controls)
        {
            WatchFocus(box, $"the instant section's \u201c{box.Text}\u201d box");
        }
        instant.AddExpandedChangedHandler(sender =>
            SetStatus(((Expander)sender).IsExpanded
                ? "The instant section opened, all at once."
                : "The instant section jumped shut to its header."));
        column.Controls.Add(instant);

        var disabled = new Expander("Network (disabled)")
        {
            Dock = XDock.Top,
            Height = 84f,
            Enabled = false,
        };
        disabled.Controls.Add(new CheckBox("Use a proxy") { Dock = XDock.Top, AutoSize = true });
        column.Controls.Add(disabled);

        card.Controls.Add(column);

        card.Controls.Add(new Label(
            "A folded section keeps its content in the tree but arranges it into nothing, so the " +
            "children come back exactly as they were. ExpandProgress is what the fold is drawn " +
            "from - height and arrow together - and it follows IsExpanded on its own, so the " +
            "section without a duration behaves as it always did. The duration is an extension " +
            "member from Kolvir.Feature.Animations, which this control does not reference. " +
            "Tab is the other half of the fold: it steps over the boxes of a folded section and " +
            "goes back into them when it opens, and folding a section under one of its own boxes " +
            "brings the focus up to the header - the box it was on is no longer reachable.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>
    /// Names a control in the status line when it takes the focus, so the demo says out loud what
    /// Tab is doing - which is the only way to see a skipped stop by hand.
    /// </summary>
    private void WatchFocus(Control control, string name) =>
            control.AddGotFocusHandler(_ => SetStatus($"Focus is on {name}."));

    private static string Describe(CheckState state) => state switch
    {
        CheckState.Checked => "checked",
        CheckState.Indeterminate => "indeterminate",
        _ => "unchecked",
    };
}
