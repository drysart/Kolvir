namespace Kolvir.Feature.Observables;

/// <summary>
/// What a change batch asks the collection it belongs to, so that the queue, the batches, the cap and
/// the disposal are written once for every collection and only what a change means is per type
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 5).
/// </summary>
/// <typeparam name="TChange">The collection's record type.</typeparam>
/// <typeparam name="TArgs">The collection's event arguments.</typeparam>
/// <remarks>
/// Implemented explicitly by each collection, so the seam stays internal and the surfaces stay
/// per-type: a caller sees <c>IObservableList&lt;T&gt;</c> or <c>IObservableSet&lt;T&gt;</c> and never
/// this.
/// </remarks>
internal interface IObservableChangeTarget<TChange, TArgs>
    where TChange : struct
    where TArgs : EventArgs
{
    /// <summary>How many items the collection holds now.</summary>
    /// <remarks>
    /// Read at the start of a delivery and before any handler runs, which is the size the records being
    /// delivered left behind - a handler's own change belongs to the next event, so counting it here
    /// would put the wrong bound on an index-shaped mark.
    /// </remarks>
    int Size { get; }

    /// <summary>The record that stands in for what a batch collected when it grew past the cap.</summary>
    TChange Untracked { get; }

    /// <summary>Whether <paramref name="change"/> is the <see cref="Untracked"/> record.</summary>
    bool IsUntracked(in TChange change);

    /// <summary>The event arguments for one delivery.</summary>
    /// <param name="changes">What is being delivered, in the order it happened. Empty only for the last
    /// event a disposal delivers.</param>
    TArgs CreateArgs(IReadOnlyList<TChange> changes);

    /// <summary>Marks what the records of one delivery invalidated.</summary>
    /// <param name="changes">The records, in the order they happened.</param>
    /// <param name="size">The collection's size once they had all happened - the size it has when this
    /// is called.</param>
    void Mark(IReadOnlyList<TChange> changes, int size);

    /// <summary>Raises every dependency those marks touched, once each.</summary>
    void RaiseDependencies();

    /// <summary>Drops every dependency subscription, for a collection that is being disposed.</summary>
    void ReleaseDependencies();
}
