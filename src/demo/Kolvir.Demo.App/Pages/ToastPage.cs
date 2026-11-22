using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The toast page: the four kinds, the actions and where they appear.
/// </summary>
internal sealed class ToastPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ToastPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// <see cref="Toast"/>: the four severities, each dismissed by its own countdown or by the
    /// close glyph, with the message docked at the bottom of the page like a real one.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Toast"));

        card.Controls.Add(new Label(
            "A transient message bar. The accent bar is coloured for the severity, the message " +
            "sits beside it, and the toast dismisses itself after the duration it was given - or " +
            "the user closes it. It is an ordinary control, so a page makes room for it.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 58f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 12f),
        });

        var toast = new Toast(
            "Project saved to disk - the toast dismisses itself after four seconds.")
        {
            Dock = XDock.Bottom,
            Severity = ToastSeverity.Success,
            Margin = new Thickness(0f, 12f, 0f, 0f),
        };
        toast.AddDismissedHandler(_ => SetStatus("The toast went away - by its countdown or by the close glyph."));

        var row = ButtonRow();
        row.Height = 34f;
        row.Margin = new Thickness(0f, 0f, 0f, 8f);

        row.Controls.Add(ToastButton("Success", ToastSeverity.Success,
            "Project saved to disk - the toast dismisses itself after four seconds.", 4, toast));

        row.Controls.Add(ToastButton("Info", ToastSeverity.Info,
            "Three files were re-indexed in the background.", 4, toast));

        row.Controls.Add(ToastButton("Warning", ToastSeverity.Warning,
            "The export finished, but two images could not be embedded.", 5, toast));

        row.Controls.Add(ToastButton("Error", ToastSeverity.Error,
            "Could not reach the server. This one stays until it is dismissed.", null, toast));

        card.Controls.Add(row);

        card.Controls.Add(new Label(
            "A toast with no duration stays up: an error usually waits for the user to acknowledge " +
            "it, while a success can go away on its own. The countdown pauses while the toast is " +
            "not on screen, so a message cannot expire behind a page the user switched away from.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        card.Controls.Add(toast);

        // The page opens with a message up, so a screenshot of it shows the control.
        toast.Show();

        return card;
    }

    /// <summary>One of the toast page's buttons: sets the severity and the message, and shows it.</summary>
    private Button ToastButton(
            string text,
            ToastSeverity severity,
            string message,
            int? seconds,
            Toast toast)
    {
        var button = new Button(text)
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };

        button.AddClickHandler(_ =>
        {
            toast.Text = message;
            toast.Severity = severity;
            toast.Duration = seconds is { } value ? TimeSpan.FromSeconds(value) : null;
            toast.Show();
            SetStatus(severity is ToastSeverity.Error or ToastSeverity.Info
                ? $"Showing an {severity} toast."
                : $"Showing a {severity} toast.");
        });

        return button;
    }

    /// <summary>
    /// <see cref="BusyIndicator"/> and <see cref="Overlay"/>: a spinner on its own, and the
    /// same spinner inside a layer that stops the panel underneath from being driven.
    /// </summary>
}
