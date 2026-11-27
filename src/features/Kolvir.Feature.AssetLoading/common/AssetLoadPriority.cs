namespace Kolvir.Feature.AssetLoading;

/// <summary>
/// The anchors a visible-versus-background decision is expressed against.
/// </summary>
/// <remarks>
/// <para>
/// Two anchors a thousand apart, and both halves of that are the decision. The values are far apart
/// on purpose: future feature work and applications need room to slot intervening priorities between
/// "the user is looking at it" and "nothing is looking at it", so an application can define 900 for
/// "about to scroll into view" without touching these constants or the manager
/// (dev-docs/ASSET-LOADING.md section 2.5).
/// </para>
/// <para>
/// They are <see cref="int"/> constants rather than an <c>enum</c> because an enum is a closed set,
/// and a closed set cannot express "slightly less urgent than visible, more urgent than background" -
/// which is exactly the case the spacing exists to allow.
/// </para>
/// <para>
/// These are anchors for an integration, not a default for loaders: <see cref="IAssetLoader.Priority"/>
/// is the loader's own to start. If a loader ever chooses a default, the safe direction is
/// <see cref="Background"/>, because treating an unclassified load as visible lets it compete with
/// work the user is looking at - the one mistake this feature exists to prevent.
/// </para>
/// </remarks>
public static class AssetLoadPriority
{
    /// <summary>
    /// Nothing is looking at it. The priority an unclassified load should take, and the floor an
    /// application's own higher tiers sit above.
    /// </summary>
    public const int Background = 0;

    /// <summary>
    /// The user is looking at it - an integration's answer to a control that is genuinely on screen.
    /// Highest wins, so this outranks every background load queued before or after it.
    /// </summary>
    public const int Visible = 1000;
}
