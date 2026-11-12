namespace XPlatUI.Badges;

/// <summary>
/// What a platform's badge can actually show.
/// </summary>
/// <remarks>
/// <para>
/// The three platforms do not agree on what a badge is, so an application cannot assume that a count
/// it sets will be drawn the way it is drawn on another platform. Windows draws whatever the
/// application hands it, which makes every presentation available at the cost of the application
/// drawing it well; macOS shows a system-rendered bubble that takes any short string; the Linux
/// desktop takes a number and nothing else.
/// </para>
/// <para>
/// Read this before choosing what to show. A member this reports false for is not "ignored
/// silently": <see cref="XPlatAppBadge"/> degrades an unsupported presentation to
/// <see cref="XPlatAppBadge.Urgent"/>, so the user is still told something happened, and the
/// capabilities are what let an application choose a better answer itself.
/// </para>
/// </remarks>
public sealed record XPlatBadgeCapabilities
{
    /// <summary>Whether a numeric count is shown.</summary>
    public bool Count { get; init; }

    /// <summary>Whether a short text label is shown, instead of a number.</summary>
    public bool Text { get; init; }

    /// <summary>Whether a dot with no number is shown.</summary>
    public bool Dot { get; init; }

    /// <summary>Whether "urgent" is shown as something other than a badge: a flash, a bounce, a highlight.</summary>
    public bool Urgent { get; init; }

    /// <summary>Whether the platform shows anything at all.</summary>
    public bool Any => Count || Text || Dot || Urgent;

    /// <summary>Nothing is shown: the feature has not been opted into, or the platform has no badge at all.</summary>
    public static XPlatBadgeCapabilities None { get; } = new();

    /// <summary>Every presentation is available, which is Windows and macOS.</summary>
    public static XPlatBadgeCapabilities All { get; } = new()
    {
        Count = true,
        Text = true,
        Dot = true,
        Urgent = true,
    };
}
