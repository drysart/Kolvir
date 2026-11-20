namespace Kolvir.Observables;

/// <summary>
/// One notification from an observable list: a single change, or a batch of them in order.
/// </summary>
/// <typeparam name="T">The type of the list's items.</typeparam>
/// <remarks>
/// <para>
/// <see cref="Changes"/> is length 1 for a mutation outside a batch and N for a batch, and the records
/// are in the order the changes happened - a batch collapses the notifications, never the records, so an
/// observer is told everything it would have been told N times over
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 3.3) - and never more than the list's
/// <see cref="IObservableList{T}.MaxCollectionBatchSize"/> (section 3.11).
/// </para>
/// <para>
/// It is never longer than that cap. A batch that would have exceeded it drops what it collected and
/// delivers a single <see cref="ObservableListChangeType.UntrackedChanges"/> record, which is the one
/// record that cannot be applied to a copy: an observer treats it as "re-read the whole list"
/// (section 3.11).
/// </para>
/// <para>
/// A disposed list delivers one last event with no changes at all, which is how a registrant that only
/// wants to detach learns the list is gone: <see cref="List"/> answers
/// <see cref="IReadOnlyObservableList{T}.IsDisposed"/> by then, and every other event arrives with it
/// false. There is no other empty event.
/// </para>
/// </remarks>
public sealed class ObservableListChangedEventArgs<T> : EventArgs
{
    /// <summary>Creates the argument for one delivery.</summary>
    /// <param name="list">The list that changed.</param>
    /// <param name="changes">What changed, in order. Length 1 outside a batch, and empty only for the
    /// final event a disposal delivers.</param>
    /// <exception cref="ArgumentNullException"><paramref name="list"/> or <paramref name="changes"/> is null.</exception>
    /// <remarks>
    /// Public so that a hand-written <see cref="IObservableList{T}"/> in another assembly can deliver
    /// its own notifications; the implementation in this package uses the same constructor.
    /// </remarks>
    public ObservableListChangedEventArgs(
        IReadOnlyObservableList<T> list,
        IReadOnlyList<ObservableListChange<T>> changes)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(changes);

        List = list;
        Changes = changes;
    }

    /// <summary>What changed, in the order it changed.</summary>
    public IReadOnlyList<ObservableListChange<T>> Changes { get; }

    /// <summary>The list the changes happened to.</summary>
    /// <remarks>What a handler registered on several lists uses to tell them apart, and how a handler
    /// notices that this is the final event of a disposal.</remarks>
    public IReadOnlyObservableList<T> List { get; }
}
