namespace Kolvir.Feature.Observables;

/// <summary>
/// One notification from an observable dictionary: a single change, or a batch of them in order.
/// </summary>
/// <typeparam name="TKey">The type of the dictionary's keys.</typeparam>
/// <typeparam name="TValue">The type of the dictionary's values.</typeparam>
/// <remarks>
/// <para>
/// <see cref="Changes"/> is length 1 for a mutation outside a batch, N for a batch, and 0 only for the
/// last event a disposal delivers. The records are in the order the changes happened - a batch collapses
/// the notifications, never the records (dev-docs/OBSERVABLE-COLLECTIONS.md section 3.3) - and never
/// more than the dictionary's <see cref="IObservableDictionary{TKey, TValue}.MaxCollectionBatchSize"/>
/// (section 3.11).
/// </para>
/// <para>
/// It is never longer than that cap. A batch that would have exceeded it drops what it collected and
/// delivers a single <see cref="ObservableDictionaryChangeType.UntrackedChanges"/> record, which is the
/// one record that cannot be applied to a copy: an observer treats it as "re-read the whole dictionary".
/// </para>
/// <para>
/// A disposed dictionary delivers one last event with no changes at all, which is how a registrant that
/// only wants to detach learns it is gone: <see cref="Dictionary"/> answers
/// <see cref="IReadOnlyObservableDictionary{TKey, TValue}.IsDisposed"/> by then, and every other event
/// arrives with it false. There is no other empty event.
/// </para>
/// </remarks>
public sealed class ObservableDictionaryChangedEventArgs<TKey, TValue> : EventArgs
{
    /// <summary>Creates the argument for one delivery.</summary>
    /// <param name="dictionary">The dictionary that changed.</param>
    /// <param name="changes">What changed, in order. Length 1 outside a batch, and empty only for the
    /// final event a disposal delivers.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> or
    /// <paramref name="changes"/> is null.</exception>
    /// <remarks>
    /// Public so that a hand-written <see cref="IObservableDictionary{TKey, TValue}"/> in another
    /// assembly can deliver its own notifications; the implementation in this package uses the same
    /// constructor.
    /// </remarks>
    public ObservableDictionaryChangedEventArgs(
        IReadOnlyObservableDictionary<TKey, TValue> dictionary,
        IReadOnlyList<ObservableDictionaryChange<TKey, TValue>> changes)
    {
        Dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        Changes = changes ?? throw new ArgumentNullException(nameof(changes));
    }

    /// <summary>What changed, in the order it happened.</summary>
    public IReadOnlyList<ObservableDictionaryChange<TKey, TValue>> Changes { get; }

    /// <summary>The dictionary that changed.</summary>
    public IReadOnlyObservableDictionary<TKey, TValue> Dictionary { get; }
}
