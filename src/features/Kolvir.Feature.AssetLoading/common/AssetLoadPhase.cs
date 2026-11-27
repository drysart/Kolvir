namespace Kolvir.Feature.AssetLoading;

/// <summary>
/// The two states a request the manager is holding can be in.
/// </summary>
/// <remarks>
/// <para>
/// Its own enumeration rather than the image integration's <c>SourceLoadState</c>: the manager knows
/// nothing about controls or images, and the two states it acts on are exactly these. <c>Loaded</c>
/// and <c>Failed</c> are not missing from it - they are outside its subject, because a finished load
/// is not in the manager any more (dev-docs/ASSET-LOADING.md section 2.6).
/// </para>
/// <para>
/// <see cref="Queued"/> is a request waiting for its turn or for its loader's availability gate;
/// <see cref="Running"/> is one whose loader has been asked to load, which is the moment
/// <see cref="IAssetLoadManager.LoadAsync"/>'s <c>onStarted</c> was raised. That distinction is why
/// the callback exists: from the caller's side both are "I have a task and it has not finished".
/// </para>
/// </remarks>
public enum AssetLoadPhase
{
    /// <summary>Submitted and waiting: not started, and not yet costing anyone a byte.</summary>
    Queued,

    /// <summary>Started: the loader has been asked, and its task is what will end it.</summary>
    Running,
}
