namespace Kolvir.Feature.AssetLoading;

/// <summary>
/// One request the manager is holding, as <see cref="IAssetLoadManager.Snapshot"/> reports it.
/// </summary>
/// <param name="Loader">The request's handle. Its identity is also what the manager keys the request
/// by, so it is the thing to look up when asking "which one is this".</param>
/// <param name="Priority">The priority read when the snapshot was taken, so a re-prioritised request
/// shows its current value rather than the one it queued with.</param>
/// <param name="Sequence">The number the request was given when it was submitted. Equal priorities
/// are ordered by it, and it is kept across a priority change, so it says which of two equal
/// priorities arrived first.</param>
/// <param name="Phase">Whether the request is queued or running.</param>
/// <param name="CanLoadNow">The loader's availability answer, read when the snapshot was taken. A
/// <see langword="false"/> at the top of the list explains a stall without any further enquiry: it is
/// the same answer the next walk would get.</param>
/// <remarks>
/// <para>
/// Deliberately minimal, and deliberately a copy: no history of finished loads, no durations, no byte
/// counters, no change notification and no formatting contract. Each of those is something a
/// diagnostics surface grows when nobody says where it stops, and each would then have to be kept
/// truthful and measured (dev-docs/ASSET-LOADING.md section 2.6).
/// </para>
/// <para>
/// The order of a snapshot is the order of the walk - priority descending, sequence ascending - so a
/// stall is read down from the top rather than assembled from the whole list.
/// </para>
/// </remarks>
public readonly record struct AssetLoadSnapshot(
    IAssetLoader Loader,
    int Priority,
    long Sequence,
    AssetLoadPhase Phase,
    bool CanLoadNow);
