namespace Kolvir.Feature.Observables;

/// <summary>
/// One notification from an observable set: a single change, or a batch of them in order.
/// </summary>
/// <typeparam name="T">The type of the set's items.</typeparam>
/// <remarks>
/// <para>
/// <see cref="Changes"/> is length 1 for a mutation outside a batch, N for a batch, and 0 only for the
/// last event a disposal delivers. The records are in the order the changes happened - a batch collapses
/// the notifications, never the records (dev-docs/OBSERVABLE-COLLECTIONS.md section 3.3) - and never
/// more than the set's <see cref="IObservableSet{T}.MaxCollectionBatchSize"/>
/// (section 3.11).
/// </para>
/// <para>
/// It is never longer than that cap. A batch that would have exceeded it drops what it collected and
/// delivers a single <see cref="ObservableSetChangeType.UntrackedChanges"/> record, which is the one
/// record that cannot be applied to a copy: an observer treats it as "re-read the whole set".
/// </para>
/// <para>
/// A disposed set delivers one last event with no changes at all, which is how a registrant that only
/// wants to detach learns the set is gone: <see cref="Set"/> answers
/// <see cref="IReadOnlyObservableSet{T}.IsDisposed"/> by then, and every other event arrives with it
/// false. There is no other empty event.
/// </para>
/// </remarks>
public sealed class ObservableSetChangedEventArgs<T> : EventArgs
{
    /// <summary>Creates the argument for one delivery.</summary>
    /// <param name="set">The set that changed.</param>
    /// <param name="changes">What changed, in order. Length 1 outside a batch, and empty only for the
    /// final event a disposal delivers.</param>
    /// <exception cref="ArgumentNullException"><paramref name="set"/> or <paramref name="changes"/> is null.</exception>
    /// <remarks>
    /// Public so that a hand-written <see cref="IObservableSet{T}"/> in another assembly can deliver its
    /// own notifications; the implementation in this package uses the same constructor.
    /// </remarks>
    public ObservableSetChangedEventArgs(
        IReadOnlyObservableSet<T> set,
        IReadOnlyList<ObservableSetChange<T>> changes)
    {
        Set = set ?? throw new ArgumentNullException(nameof(set));
        Changes = changes ?? throw new ArgumentNullException(nameof(changes));
    }

    /// <summary>What changed, in the order it happened.</summary>
    public IReadOnlyList<ObservableSetChange<T>> Changes { get; }

    /// <summary>The set that changed.</summary>
    public IReadOnlyObservableSet<T> Set { get; }
}
