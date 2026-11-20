namespace Kolvir.Observables;

/// <summary>What one change to an observable set can be.</summary>
/// <remarks>
/// Three kinds and an admission. A set holds one item per equality class, so there is no
/// "the item changed but it is the same item" - adding one the comparer calls equal is a no-op - and no
/// order to move in, so there is no move either. What is left is an item arriving, an item leaving, and
/// the whole set being emptied (dev-docs/OBSERVABLE-COLLECTIONS.md sections 6.2.3 and 6.4).
/// </remarks>
public enum ObservableSetChangeType
{
    /// <summary>An item was added. The record carries it.</summary>
    /// <remarks>One record per item, whoever did the adding: <c>Add</c>, <c>UnionWith</c> or
    /// <c>SymmetricExceptWith</c>.</remarks>
    ItemAdded,

    /// <summary>An item was removed. The record carries it.</summary>
    /// <remarks>One record per item, whether it left by <c>Remove</c>, by <c>ExceptWith</c>, by
    /// <c>IntersectWith</c> or by a <c>SymmetricExceptWith</c>.</remarks>
    ItemRemoved,

    /// <summary>Every item was removed at once. The record carries nothing.</summary>
    SetCleared,

    /// <summary>
    /// Changes happened, and this event is not telling you which ones.
    /// </summary>
    /// <remarks>
    /// The one record that is not a description of a change but an admission about the stream: an event
    /// carries at most <see cref="IObservableSet{T}.MaxCollectionBatchSize"/> records, and a batch that
    /// would have carried more drops what it collected and delivers this instead. An observer treats it
    /// as "re-read the whole set" - it is the only record that cannot be applied to a copy.
    /// </remarks>
    UntrackedChanges,
}
