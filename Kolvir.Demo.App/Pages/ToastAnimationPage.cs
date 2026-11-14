using Kolvir.Animations;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The toast animation page: the entrance, the exit and the timings between.
/// </summary>
internal sealed class ToastAnimationPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ToastAnimationPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// The toast's fade and slide, from <c>Kolvir.Feature.Animations</c>. One message with a
    /// duration for each direction, and a switch that takes both durations away so the difference
    /// is a press away rather than a sentence.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Toast animation"));

        card.Controls.Add(new Label(
            "The same control with a duration from the animations package. Press Show and it fades " +
            "in while sliding out of the edge it is docked against; press Dismiss and it reverses, " +
            "and only leaves the layout when the transition is over. Untick Instant and press them " +
            "again to watch it without the animation - the message appears and goes in one frame.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 58f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 12f),
        });

        var toast = new Toast("Project saved to disk - watch it slide in from the bottom edge.")
        {
            Dock = XDock.Bottom,
            Severity = ToastSeverity.Success,
            Duration = null,
            Margin = new Thickness(0f, 12f, 0f, 0f),
        };
        toast.ShowDuration = TimeSpan.FromMilliseconds(240);
        toast.DismissDuration = TimeSpan.FromMilliseconds(180);
        toast.AddDismissedHandler(_ => SetStatus("The toast has gone - the layout closed up behind it."));

        var row = ButtonRow();
        row.Height = 34f;
        row.Margin = new Thickness(0f, 0f, 0f, 8f);

        var show = new Button("Show")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        show.AddClickHandler(_ =>
        {
            toast.Show();
            SetStatus($"Showing over {toast.ShowDuration?.TotalMilliseconds} ms - it is already taking its room.");
        });

        var dismiss = new Button("Dismiss")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        dismiss.AddClickHandler(_ =>
        {
            toast.Dismiss();
            SetStatus(
                $"Dismissing over {toast.DismissDuration?.TotalMilliseconds} ms - it is out of the Tab order " +
                "before the transition starts.");
        });

        var instant = new CheckBox("Instant - no duration") { Dock = XDock.Left, AutoSize = true };
        instant.AddCheckedChangedHandler(_ =>
        {
            var duration = instant.Checked ? (TimeSpan?)null : TimeSpan.FromMilliseconds(240);
            toast.ShowDuration = duration;
            toast.DismissDuration = instant.Checked ? (TimeSpan?)null : TimeSpan.FromMilliseconds(180);
            SetStatus(instant.Checked
                ? "No durations: the toast is the control it always was."
                : "Durations are back: Show and Dismiss animate again.");
        });

        row.Controls.Add(show);
        row.Controls.Add(dismiss);
        row.Controls.Add(instant);
        card.Controls.Add(row);

        card.Controls.Add(new Label(
            "ShowProgress is the one number the message is drawn from: its opacity, and how far it " +
            "has come in from its docked edge. The two durations animate that number, so the fade " +
            "and the slide cannot disagree, and the message is taken off the screen when the number " +
            "reaches 0 rather than when Dismiss is called - which is why a cancelled dismissal still " +
            "ends with the toast gone. The close glyph and Escape both work while it is up.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        card.Controls.Add(toast);

        // The page opens with the message up, so a screenshot of it shows the control.
        toast.Show();

        return card;
    }

    /// <summary>
    /// The overlay's fade-in, from <c>Kolvir.Feature.Animations</c>: the layer dims in over the
    /// panel instead of appearing at full strength in one frame.
    /// </summary>
}
