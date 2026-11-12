namespace Kolvir.Badges;

/// <summary>
/// The application's badge: an unread count, a short label or a dot on whatever the platform uses for
/// an application icon, and an "urgent" flag beside it.
/// </summary>
/// <remarks>
/// <para>
/// Reach it through the application - <c>app.Badge.Count = 3</c> - which is the extension the feature
/// adds. There is one per application, because two of the three platforms only have an application
/// badge: macOS has the Dock tile and the Linux desktop has the dock icon, neither of which can be
/// aimed at a window, and on Windows a process's windows share one taskbar button.
/// </para>
/// <para>
/// <b>The three presentations overlap, and the order is fixed.</b> <see cref="IsDot"/> beats
/// <see cref="Text"/>, which beats <see cref="Count"/>, so an application can set a count and a dot
/// from different parts of its code without them fighting: a dot is "something happened, I have no
/// number for it" and that is the more urgent thing to say. Setting none of them clears the badge.
/// A count of zero or less clears it too, because "no unread items" is not a badge.
/// </para>
/// <para>
/// <b>Only what the platform can show is shown.</b> <see cref="Capabilities"/> says what that is. A
/// presentation the platform cannot draw is not dropped silently: it is shown as
/// <see cref="Urgent"/> instead, so the user still learns that something happened.
/// </para>
/// <para>
/// <b>Members may be set from any thread.</b> The platform half marshals to the thread its own
/// platform requires, so a worker completing a request can set a count directly.
/// </para>
/// <para>
/// A count, a label or a dot is kept until it is changed: closing the window a notification was about
/// does not clear it, and clearing it is the application's decision.
/// </para>
/// <para>
/// <b><see cref="Urgent"/> is the exception, because it asks for a moment rather than a state.</b>
/// All three platforms draw attention for a while and then stop: Windows flashes a taskbar button
/// until its window comes to the front, macOS bounces a Dock icon until the application is activated,
/// and a Linux dock highlights the icon until its window has the focus. Once the user is back, the
/// flag no longer describes anything, so it is set back to false - and that is also what lets an
/// application ask again, since a flag that stayed true would not be a second request.
/// </para>
/// </remarks>
public sealed class AppBadge
{
    private readonly IAppBadgePresenter? _presenter;

    private Action<AppBadge>? _changed;

    private int? _count;
    private string? _text;
    private bool _isDot;
    private bool _urgent;

    /// <summary>
    /// Creates a badge that watches no windows, which is what a unit test drives: the members are set
    /// directly and there is no application whose activation could end an attention request.
    /// </summary>
    /// <param name="presenter">The platform half, or null when the feature was not opted into.</param>
    internal AppBadge(IAppBadgePresenter? presenter)
        : this(presenter, application: null)
    {
    }

    /// <summary>
    /// Creates a badge over <paramref name="presenter"/>, or over nothing when the feature was not
    /// opted into, in which case every setter is inert and the capabilities are empty.
    /// </summary>
    /// <param name="presenter">The platform half, or null when the feature was not opted into.</param>
    /// <param name="application">
    /// The application whose windows are watched for the activation that ends an attention request, or
    /// null when there is no application to watch.
    /// </param>
    internal AppBadge(IAppBadgePresenter? presenter, Application? application)
    {
        _presenter = presenter;

        if (application is null)
        {
            return;
        }

        // Watching the windows from here rather than in each platform folder, because coming back to
        // the application is the same event on all three: a window becomes active, which is what each
        // backend already reports and what each platform's attention request ends on.
        foreach (var window in application.Windows)
        {
            WatchForActivation(window);
        }

        application.AddWindowOpenedHandler((_, args) => WatchForActivation(args.Window));
    }

    /// <summary>
    /// How many unread items there are, or null for no count. Zero and negative values are the same
    /// as null: the badge goes away.
    /// </summary>
    /// <remarks>
    /// On a platform whose capabilities exclude <see cref="BadgeCapabilities.Count"/> this
    /// becomes <see cref="Urgent"/>, so the application does not have to know which platform it is
    /// on to have the user notice something.
    /// </remarks>
    public int? Count
    {
        get => _count;
        set
        {
            if (_count == value)
            {
                return;
            }

            _count = value;
            Push();
        }
    }

    /// <summary>
    /// A short label to show instead of the number, such as "99+" or "New", or null for none.
    /// </summary>
    /// <remarks>
    /// Only the platforms that report <see cref="BadgeCapabilities.Text"/> draw it; the others
    /// show <see cref="Urgent"/> instead. Empty and whitespace-only strings are treated as null.
    /// macOS clips long labels, so keep it to about four characters.
    /// </remarks>
    public string? Text
    {
        get => _text;
        set
        {
            var normalised = string.IsNullOrWhiteSpace(value) ? null : value;

            if (string.Equals(_text, normalised, StringComparison.Ordinal))
            {
                return;
            }

            _text = normalised;
            Push();
        }
    }

    /// <summary>
    /// Whether to show a dot with no number, which is the "something happened" badge. Wins over
    /// <see cref="Text"/> and <see cref="Count"/>.
    /// </summary>
    /// <remarks>
    /// The Linux desktop has no dot presentation - its protocol carries a number - so there this
    /// becomes <see cref="Urgent"/>.
    /// </remarks>
    public bool IsDot
    {
        get => _isDot;
        set
        {
            if (_isDot == value)
            {
                return;
            }

            _isDot = value;
            Push();
        }
    }

    /// <summary>
    /// Whether the platform should also draw attention to the application: a taskbar button flash, a
    /// Dock bounce or a highlight on the dock icon.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Independent of the badge itself, so a count and a flash can be asked for at once.
    /// </para>
    /// <para>
    /// Windows and macOS treat it as a nudge: setting it true while it is already true does not flash
    /// or bounce a second time, and clearing it does not take the nudge back. The Linux desktop keeps
    /// the highlight until it is cleared, so there clearing the flag is what ends it.
    /// </para>
    /// <para>
    /// In every case the request ends when the user comes back to the application, and the flag is set
    /// back to false then: the platform stops flashing, bouncing or highlighting, so reporting
    /// <c>true</c> afterwards would describe nothing, and asking again would not be a second request.
    /// A handler that wants to know when the user has seen the nudge can therefore simply read this
    /// property, or listen for it changing.
    /// </para>
    /// </remarks>
    public bool Urgent
    {
        get => _urgent;
        set
        {
            if (_urgent == value)
            {
                return;
            }

            _urgent = value;
            Push();
        }
    }

    /// <summary>
    /// What this platform can show, or <see cref="BadgeCapabilities.None"/> when the application
    /// never called the feature's <c>AddAppBadges</c>.
    /// </summary>
    public BadgeCapabilities Capabilities => _presenter?.Capabilities ?? BadgeCapabilities.None;

    /// <summary>Adds a handler that runs after the badge changed, on the thread that changed it.</summary>
    /// <param name="handler">The handler, which is given the badge.</param>
    /// <returns>A token that removes the handler.</returns>
    /// <remarks>
    /// Runs for every change, including the ones the badge makes itself: an attention request that the
    /// user answered is cleared here, not by the application, so a handler is how an application keeps
    /// a readout or a tray icon in step. It runs after the platform half has been told, and on the
    /// thread that made the change - the UI thread when the platform half is what changed it.
    /// </remarks>
    public IDisposable AddChangedHandler(Action<AppBadge> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _changed += handler;
        return new HandlerToken<Action<AppBadge>>(RemoveChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddChangedHandler"/> added.</summary>
    /// <param name="handler">The handler to remove.</param>
    public void RemoveChangedHandler(Action<AppBadge> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _changed -= handler;
    }

    /// <summary>
    /// Clears the count, the label, the dot and the urgent flag in one change, so a platform is told
    /// once rather than four times.
    /// </summary>
    public void Clear()
    {
        if (_count is null && _text is null && !_isDot && !_urgent)
        {
            return;
        }

        _count = null;
        _text = null;
        _isDot = false;
        _urgent = false;
        Push();
    }

    /// <summary>
    /// Resolves the four members into the one presentation <paramref name="capabilities"/> allows.
    /// </summary>
    /// <param name="capabilities">What the platform can show.</param>
    /// <returns>The presentation to hand the platform half.</returns>
    /// <remarks>
    /// Separate from <see cref="Push"/> so the rules - the precedence, the degradation and the
    /// treatment of a count of zero - can be read and tested without a platform half or an
    /// application. The degradation only ever adds urgency when the platform can show urgency at all.
    /// </remarks>
    internal BadgeState Resolve(BadgeCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var urgent = _urgent && capabilities.Urgent;

        if (_isDot)
        {
            return capabilities.Dot
                ? new BadgeState(BadgeKind.Dot, 0, null, urgent)
                : Attention(capabilities);
        }

        if (_text is not null)
        {
            return capabilities.Text
                ? new BadgeState(BadgeKind.Text, 0, _text, urgent)
                : Attention(capabilities);
        }

        if (_count is > 0)
        {
            return capabilities.Count
                ? new BadgeState(BadgeKind.Count, _count.Value, null, urgent)
                : Attention(capabilities);
        }

        return new BadgeState(BadgeKind.None, 0, null, urgent);
    }

    /// <summary>
    /// What a presentation the platform cannot draw turns into: nothing to show, but attention drawn
    /// if the platform can draw attention.
    /// </summary>
    private static BadgeState Attention(BadgeCapabilities capabilities) =>
        new(BadgeKind.None, 0, null, capabilities.Urgent);

    /// <summary>Hands the resolved presentation to the platform half, if the feature was opted into.</summary>
    private void Push()
    {
        _presenter?.Apply(Resolve(Capabilities));

        // After the platform half, so a handler that reads the badge sees the new state and a handler
        // that reads what the platform was told sees the same thing. The badge can change on its own -
        // coming back to the application ends an urgent request - so this is also how an application
        // hears about a change it did not make.
        _changed?.Invoke(this);
    }

    /// <summary>
    /// Clears <see cref="Urgent"/> when <paramref name="window"/> becomes the active window.
    /// </summary>
    private void WatchForActivation(Window window)
    {
        window.AddActivatedHandler(OnWindowActivated);
        window.AddClosedHandler(OnWindowClosed);
    }

    /// <summary>The user came back to the application, so the attention request is over.</summary>
    private void OnWindowActivated(object sender) => Urgent = false;

    /// <summary>Stops watching a window that closed: a closed window cannot be activated again.</summary>
    private void OnWindowClosed(object sender)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.RemoveActivatedHandler(OnWindowActivated);
        window.RemoveClosedHandler(OnWindowClosed);
    }
}
