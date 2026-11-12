namespace Kolvir.Badges;

/// <summary>
/// The per-platform half of a badge: it says what the platform can show and applies one resolved
/// presentation at a time.
/// </summary>
/// <remarks>
/// <para>
/// The feature registers exactly one of these from its <c>AddAppBadges</c>, so the presence of an
/// implementation is the same thing as the feature having been opted into. That is why
/// <see cref="AppBadge.Capabilities"/> is empty when the application references the package but
/// never called the opt-in: there is nothing to ask.
/// </para>
/// <para>
/// <b>Implementations may be called from any thread.</b> An application is free to set a count from
/// a worker, and a web request completing on a background thread is the normal case, so each
/// implementation marshals to whatever thread its platform demands rather than pushing that rule onto
/// every caller.
/// </para>
/// <para>
/// <b>The state is already resolved.</b> The kind is one of none, count, text or dot, and an
/// unsupported kind never reaches an implementation: <see cref="AppBadge"/> has already turned
/// it into urgent. An implementation that is handed <see cref="BadgeKind.Text"/> can assume its
/// capabilities said text is supported.
/// </para>
/// </remarks>
internal interface IAppBadgePresenter
{
    /// <summary>What this platform can show. Asked once at startup and never changed.</summary>
    BadgeCapabilities Capabilities { get; }

    /// <summary>
    /// Shows <paramref name="state"/>, replacing whatever was shown before, or clears the badge when
    /// the kind is <see cref="BadgeKind.None"/>.
    /// </summary>
    /// <param name="state">The presentation to show.</param>
    /// <remarks>
    /// Called for every change, including the first, and may be called with the same state twice when
    /// an application sets a value it already had elsewhere; implementations do not need to
    /// deduplicate, but must be cheap enough that they need not.
    /// </remarks>
    void Apply(BadgeState state);
}
