namespace XPlatUI.Badges.Tests;

/// <summary>A platform half that keeps everything it was told.</summary>
/// <param name="capabilities">What it claims to be able to show.</param>
internal sealed class RecordingPresenter(XPlatBadgeCapabilities capabilities) : IXPlatAppBadgePresenter
{
    /// <summary>Every state the badge handed over, in order.</summary>
    public List<XPlatBadgeState> Applied { get; } = [];

    /// <inheritdoc />
    public XPlatBadgeCapabilities Capabilities { get; } = capabilities;

    /// <summary>The last state handed over.</summary>
    public XPlatBadgeState Last => Applied[^1];

    /// <inheritdoc />
    public void Apply(XPlatBadgeState state) => Applied.Add(state);
}
