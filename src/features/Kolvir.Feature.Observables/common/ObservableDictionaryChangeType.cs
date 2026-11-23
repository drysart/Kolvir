namespace Kolvir.Feature.Observables;

/// <summary>What one change to an observable dictionary can be.</summary>
/// <remarks>
/// A dictionary's identity is its key and its payload is its value, so a change is a key arriving, a key
/// leaving, or the value beside a key being different - and a clear, which empties the whole thing
/// (dev-docs/OBSERVABLE-COLLECTIONS.md sections 7.1 and 7.4).
/// </remarks>
public enum ObservableDictionaryChangeType
{
    /// <summary>A key arrived. The record carries the key and the value.</summary>
    ItemAdded,

    /// <summary>A key left. The record carries the key and the value it had.</summary>
    ItemRemoved,

    /// <summary>A key's value is different. The record carries the key, what it was and what it is.</summary>
    /// <remarks>Raised only when the new value differs from the old one by the default value equality,
    /// which is the rule a value's write and a list's item write already follow (section 7.2.2).</remarks>
    ItemChanged,

    /// <summary>Every key was removed at once. The record carries nothing.</summary>
    DictionaryCleared,

    /// <summary>
    /// Changes happened, and this event is not telling you which ones.
    /// </summary>
    /// <remarks>
    /// The one record that is not a description of a change but an admission about the stream: an event
    /// carries at most <see cref="IObservableDictionary{TKey, TValue}.MaxCollectionBatchSize"/> records,
    /// and a batch that would have carried more drops what it collected and delivers this instead. An
    /// observer treats it as "re-read the whole dictionary" - it is the only record that cannot be
    /// applied to a copy (section 3.11).
    /// </remarks>
    UntrackedChanges,
}
