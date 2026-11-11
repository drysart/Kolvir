namespace XPlatUI.Badges;

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
/// A badge that is set is kept until it is changed: closing the window a notification was about does
/// not clear it, and neither does activating the application. Clearing is the application's decision.
/// </para>
/// </remarks>
public sealed class XPlatAppBadge
{
    private readonly IXPlatAppBadgePresenter? _presenter;

    private int? _count;
    private string? _text;
    private bool _isDot;
    private bool _urgent;

    /// <summary>
    /// Creates a badge over <paramref name="presenter"/>, or over nothing when the feature was not
    /// opted into, in which case every setter is inert and the capabilities are empty.
    /// </summary>
    internal XPlatAppBadge(IXPlatAppBadgePresenter? presenter) => _presenter = presenter;

    /// <summary>
    /// How many unread items there are, or null for no count. Zero and negative values are the same
    /// as null: the badge goes away.
    /// </summary>
    /// <remarks>
    /// On a platform whose capabilities exclude <see cref="XPlatBadgeCapabilities.Count"/> this
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
    /// Only the platforms that report <see cref="XPlatBadgeCapabilities.Text"/> draw it; the others
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
    /// the highlight until it is cleared, which is its own behaviour rather than an invention of this
    /// library.
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
    /// What this platform can show, or <see cref="XPlatBadgeCapabilities.None"/> when the application
    /// never called the feature's <c>AddAppBadges</c>.
    /// </summary>
    public XPlatBadgeCapabilities Capabilities => _presenter?.Capabilities ?? XPlatBadgeCapabilities.None;

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
    internal XPlatBadgeState Resolve(XPlatBadgeCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var urgent = _urgent && capabilities.Urgent;

        if (_isDot)
        {
            return capabilities.Dot
                ? new XPlatBadgeState(XPlatBadgeKind.Dot, 0, null, urgent)
                : Attention(capabilities);
        }

        if (_text is not null)
        {
            return capabilities.Text
                ? new XPlatBadgeState(XPlatBadgeKind.Text, 0, _text, urgent)
                : Attention(capabilities);
        }

        if (_count is > 0)
        {
            return capabilities.Count
                ? new XPlatBadgeState(XPlatBadgeKind.Count, _count.Value, null, urgent)
                : Attention(capabilities);
        }

        return new XPlatBadgeState(XPlatBadgeKind.None, 0, null, urgent);
    }

    /// <summary>
    /// What a presentation the platform cannot draw turns into: nothing to show, but attention drawn
    /// if the platform can draw attention.
    /// </summary>
    private static XPlatBadgeState Attention(XPlatBadgeCapabilities capabilities) =>
        new(XPlatBadgeKind.None, 0, null, capabilities.Urgent);

    /// <summary>Hands the resolved presentation to the platform half, if the feature was opted into.</summary>
    private void Push() => _presenter?.Apply(Resolve(Capabilities));
}
