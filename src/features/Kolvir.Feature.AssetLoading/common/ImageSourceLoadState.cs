namespace Kolvir.Feature.AssetLoading;

/// <summary>
/// Where the load behind <c>SourceUri</c> has got to.
/// </summary>
/// <remarks>
/// <para>
/// The distinction the surface exists for is <see cref="Pending"/> against <see cref="Loading"/>: a
/// load that is queued and one that is actually in flight look identical from outside, and only the
/// manager's started notification tells them apart (dev-docs/ASSET-LOADING.md sections 2.4 and 6).
/// </para>
/// <para>
/// It is the control's own enum and not <see cref="AssetLoadPhase"/>, which reports the two phases
/// the manager acts on. <see cref="Loaded"/> and <see cref="Failed"/> are not missing from that one;
/// they are outside its subject (section 2.6).
/// </para>
/// </remarks>
public enum ImageSourceLoadState
{
    /// <summary>
    /// No URI. Nothing has been asked for, and the control is not this feature's to decorate.
    /// </summary>
    /// <remarks>
    /// This is what a control reports before a URI is assigned, and what it returns to when
    /// <c>SourceUri</c> is set to null - the first half of a
    /// retry - or when the application assigns <see cref="Image.Source"/> itself (section 6.1).
    /// </remarks>
    None,

    /// <summary>
    /// A URI was accepted and the load is queued, not started.
    /// </summary>
    /// <remarks>
    /// A placeholder, not a spinner, belongs on screen. A control that has not been attached to a
    /// tree waits here until it can reach its services, and one that was detached waits here until
    /// it is attached again (sections 6.1 and 6.2).
    /// </remarks>
    Pending,

    /// <summary>
    /// The manager said the load started, so there is work in flight.
    /// </summary>
    /// <remarks>
    /// Raised from the manager's started callback, which runs synchronously inside
    /// <c>IAssetLoadManager.LoadAsync</c> when the loader can start at once - which is why
    /// <see cref="Pending"/> is set before that call rather than after it (sections 2.4 and 6.1).
    /// </remarks>
    Loading,

    /// <summary>
    /// <see cref="Image.Source"/> holds the image the load produced.
    /// </summary>
    /// <remarks>
    /// The feature owns that image and disposes it when it is replaced, when the URI changes, and
    /// when the control is disposed (section 6.3). A handler for this state reads
    /// <see cref="Image.Source"/> and sees the loaded image, because the assignment happens first
    /// (section 6.1).
    /// </remarks>
    Loaded,

    /// <summary>
    /// The load failed; <c>SourceLoadError</c> says why.
    /// </summary>
    /// <remarks>
    /// The URI is left exactly as it was, so a retry is possible: failing is a state, not an end to
    /// the relationship. <see cref="Image.Source"/> holds
    /// <c>PlaceholderSource</c> at this point, so a handler for
    /// this state is writing over the placeholder rather than over a stale image (sections 6.1 and
    /// 6.3).
    /// </remarks>
    Failed,
}
