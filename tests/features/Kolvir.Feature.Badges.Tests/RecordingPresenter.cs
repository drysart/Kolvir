namespace Kolvir.Badges.Tests;

/// <summary>A platform half that keeps everything it was told.</summary>
/// <param name="capabilities">What it claims to be able to show.</param>
internal sealed class RecordingPresenter(BadgeCapabilities capabilities) : IAppBadgePresenter
{
    /// <summary>Every state the badge handed over, in order.</summary>
    public List<BadgeState> Applied { get; } = [];

    /// <inheritdoc />
    public BadgeCapabilities Capabilities { get; } = capabilities;

    /// <summary>The last state handed over.</summary>
    public BadgeState Last => Applied[^1];

    /// <inheritdoc />
    public void Apply(BadgeState state) => Applied.Add(state);
}
