using System.Globalization;
using Kolvir.Badges;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The app badge page: what the platform can show, and the badge the sample sets.
/// </summary>
internal sealed class AppBadgePage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public AppBadgePage(DemoUi shell) : base(shell) => Initialize(Build());

    // The delayed nudge: long enough to switch to another window, short enough that the
    // click is still the last thing the tester did.
    private const int BadgeUrgentCountdownSeconds = 3;

    /// <summary>The app badge page's readout: what the backend can draw and what is showing.</summary>
    private Label? _badgeReadout;

    /// <summary>
    /// The countdown behind the badge page's "Urgent in 3 s" button, so the nudge can be watched from
    /// outside the window: there is time to switch away, which is the only way to see a flash or a
    /// bounce on the two platforms that skip it while the application is frontmost.
    /// </summary>
    private Timer? _badgeUrgentTimer;

    /// <summary>Seconds left on that countdown, or zero when nothing is pending.</summary>
    private int _badgeUrgentSeconds;

    /// <summary>
    /// Reads the badge back once there is a window to read it from, and follows it afterwards: an
    /// urgent request is a moment rather than a state, and the platform half ends it when the user
    /// comes back, so the readout follows the badge rather than only the clicks that set it.
    /// </summary>
    /// <param name="window">The window the sample is displayed in.</param>
    internal override void OnWindowAttached(Window window)
    {
        window.Application?.Badge.AddChangedHandler(_ => RefreshBadgeReadout());

        RefreshBadgeReadout();
    }


    /// <summary>
    /// Page 59: the application's own badge - the count or dot on the taskbar button or dock icon.
    /// The buttons set a count, a dot, a label and an urgent nudge, and the readout reports what the
    /// backend said it can draw rather than what was asked for, because on Linux the two differ.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("App badge"));

        var buttons = Row(34f);
        buttons.Controls.Add(BadgeButton("Count 3", badge => badge.Count = 3));
        buttons.Controls.Add(BadgeButton("Count 150", badge => badge.Count = 150));
        buttons.Controls.Add(BadgeButton("Dot", badge =>
        {
            badge.Count = null;
            badge.IsDot = true;
        }));
        buttons.Controls.Add(BadgeButton("Text: 99+", badge =>
        {
            badge.IsDot = false;
            badge.Text = "99+";
        }));
        buttons.Controls.Add(BadgeButton("Urgent", badge => badge.Urgent = true));

        // Delayed on purpose: Windows and macOS skip a flash or a bounce while the window is active,
        // so the nudge is only observable from outside, and three seconds is enough time to get there.
        buttons.Controls.Add(BadgeButton("Urgent in 3 s", _ => StartUrgentCountdown()));
        buttons.Controls.Add(BadgeButton("Clear", badge => badge.Clear()));
        card.Controls.Add(buttons);

        var readout = new Label("No application attached, so there is no badge to show.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            // Room for the countdown line as well as the two that are always there.
            Height = 74f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            Margin = new Thickness(0f, 0f, 0f, 10f),
        };
        _badgeReadout = readout;
        card.Controls.Add(readout);

        card.Controls.Add(new Label(
            "Windows draws the badge itself and lays it over the taskbar button, macOS asks the Dock " +
            "for its own red bubble, and Linux is a convention rather than an API: the dock badges " +
            "only an application whose desktop entry is installed and matches the application id, so " +
            "running the sample out of its build directory can leave the dock unchanged. Ask " +
            "Capabilities first - asking for something a platform cannot draw becomes an urgent " +
            "nudge rather than nothing - and remember that a count of zero is no badge at all.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 74f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
        });

        return card;
    }

    /// <summary>A button that applies one change to the badge and reports what came of it.</summary>
    private Button BadgeButton(string text, Action<AppBadge> change)
    {
        var button = new Button(text)
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        button.AddClickHandler(_ => ApplyBadge(change));
        return button;
    }

    /// <summary>
    /// Starts the page's three-second countdown, after which the urgent nudge is applied.
    /// </summary>
    /// <remarks>
    /// The urgent flag is cleared first, because it is a transition on Windows and macOS rather than a
    /// state: setting it again while it is already set neither flashes nor bounces, so a second click
    /// would otherwise look like nothing happened. Clearing it is also what makes the countdown
    /// repeatable, which is the whole point of a button used to try the thing by hand.
    /// </remarks>
    private void StartUrgentCountdown()
    {
        if (OwnerWindow?.Application is not { } application)
        {
            return;
        }

        application.Badge.Urgent = false;

        _badgeUrgentTimer ??= CreateBadgeUrgentTimer();
        _badgeUrgentSeconds = BadgeUrgentCountdownSeconds;
        _badgeUrgentTimer.Stop();
        _badgeUrgentTimer.Start();
        RefreshBadgeReadout();
    }

    /// <summary>Cancels a countdown that has not fired yet, so an explicit change wins over a pending nudge.</summary>
    private void CancelUrgentCountdown()
    {
        if (_badgeUrgentSeconds == 0)
        {
            return;
        }

        _badgeUrgentSeconds = 0;
        _badgeUrgentTimer?.Stop();
    }

    /// <summary>Builds the timer the countdown runs on: one second a tick, stopping at zero.</summary>
    private Timer CreateBadgeUrgentTimer()
    {
        var timer = new Timer(TimeSpan.FromSeconds(1.0));
        timer.AddTickHandler(_ =>
        {
            if (--_badgeUrgentSeconds > 0)
            {
                RefreshBadgeReadout();
                return;
            }

            // Stop before applying: a tick that arrives after the last one has nothing left to do and
            // would push the count below zero.
            timer.Stop();
            ApplyBadge(badge => badge.Urgent = true);
        });
        return timer;
    }

    /// <summary>Applies <paramref name="change"/> to the attached application's badge.</summary>
    private void ApplyBadge(Action<AppBadge> change)
    {
        if (OwnerWindow?.Application is not { } application)
        {
            return;
        }

        // An explicit change is an answer to the page, so it takes the pending prompt with it.
        CancelUrgentCountdown();

        change(application.Badge);
        RefreshBadgeReadout();
    }

    /// <summary>Reports what is showing and what this backend can draw.</summary>
    private void RefreshBadgeReadout()
    {
        if (_badgeReadout is null)
        {
            return;
        }

        if (OwnerWindow?.Application is not { } application)
        {
            _badgeReadout.Text = "No application attached, so there is no badge to show.";
            return;
        }

        var badge = application.Badge;
        var capabilities = badge.Capabilities;

        var shown = badge.IsDot
            ? "a dot"
            : badge.Text is { Length: > 0 } label
                ? $"the label \u201c{label}\u201d"
                : badge.Count is > 0
                    ? $"a count of {badge.Count.Value.ToString(CultureInfo.CurrentCulture)}"
                    : "nothing";

        var pending = _badgeUrgentSeconds > 0
            ? $"The urgent nudge arrives in {_badgeUrgentSeconds.ToString(CultureInfo.CurrentCulture)} s - " +
              "switch to another window to watch for it.\n"
            : string.Empty;

        _badgeReadout.Text =
            $"Showing {shown}. Urgent: {(badge.Urgent ? "yes" : "no")}.\n" +
            pending +
            "This backend can draw: " +
            $"count {(capabilities.Count ? "yes" : "no")}, " +
            $"text {(capabilities.Text ? "yes" : "no")}, " +
            $"dot {(capabilities.Dot ? "yes" : "no")}, " +
            $"urgent {(capabilities.Urgent ? "yes" : "no")}.";
    }
}
