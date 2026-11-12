namespace XPlatUI.Badges;

/// <summary>
/// What a badge is being asked to show, once the application's members have been resolved into one
/// presentation.
/// </summary>
/// <remarks>
/// <para>
/// The three public members overlap on purpose - <see cref="XPlatAppBadge.IsDot"/> beats
/// <see cref="XPlatAppBadge.Text"/> beats <see cref="XPlatAppBadge.Count"/> - so a platform half is
/// never asked to choose between them: it is handed one kind and the payload for that kind. The
/// resolution happens once, in <see cref="XPlatAppBadge"/>, which is also where an unsupported kind
/// is turned into <see cref="XPlatAppBadge.Urgent"/> rather than silently dropped.
/// </para>
/// <para>
/// <see cref="Urgent"/> is not part of the kind because it is independent of the badge: a count and a
/// flash of the taskbar button are both reasonable at once.
/// </para>
/// </remarks>
internal enum XPlatBadgeKind
{
    /// <summary>Nothing is shown.</summary>
    None,

    /// <summary>A number, in <see cref="XPlatBadgeState.Count"/>.</summary>
    Count,

    /// <summary>A short label, in <see cref="XPlatBadgeState.Text"/>.</summary>
    Text,

    /// <summary>A dot, with no number and no text.</summary>
    Dot,
}

/// <summary>
/// One resolved presentation: the kind, its payload and whether the platform should also attract
/// attention. Immutable, because it is the record of what a platform was told, not what it kept.
/// </summary>
/// <param name="Kind">What to show.</param>
/// <param name="Count">The number, when <paramref name="Kind"/> is <see cref="XPlatBadgeKind.Count"/>.</param>
/// <param name="Text">The label, when <paramref name="Kind"/> is <see cref="XPlatBadgeKind.Text"/>.</param>
/// <param name="Urgent">Whether the platform should also draw attention.</param>
internal readonly record struct XPlatBadgeState(XPlatBadgeKind Kind, int Count, string? Text, bool Urgent)
{
    /// <summary>Nothing shown, nothing urgent: what a badge starts as and returns to.</summary>
    internal static XPlatBadgeState Empty { get; } = new(XPlatBadgeKind.None, 0, null, false);
}
