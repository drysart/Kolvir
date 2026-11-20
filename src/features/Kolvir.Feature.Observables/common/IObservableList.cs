namespace Kolvir.Observables;

/// <summary>
/// A list that can be changed, which only the code that owns the state is handed.
/// </summary>
/// <typeparam name="T">The type of the items.</typeparam>
/// <remarks>
/// <para>
/// What <see cref="Observable.CreateList{T}()"/> returns. The owner of a model holds one in a
/// private field and exposes it as <see cref="IReadOnlyObservableList{T}"/>, so a consumer can read it
/// and watch it but cannot change it, and nothing in the API asks a consumer to hold the writable one.
/// </para>
/// <para>
/// <b>Every change is reported at the granularity it happened.</b> An insert is one
/// <see cref="ObservableListChangeType.ItemAdded"/> naming where; a removal is one naming where it was;
/// a rewrite of one item is one <see cref="ObservableListChangeType.ItemChanged"/> with both the old
/// and the new item; a <see cref="Move"/> is one
/// <see cref="ObservableListChangeType.ItemMoved"/> rather than a removal and an addition, because it
/// is a reposition and an observer should be able to shift a row instead of rebuilding it; and a
/// <c>Clear</c> is one <see cref="ObservableListChangeType.ListCleared"/> however many items there
/// were. A call that changes nothing raises nothing: writing an equal item over itself, clearing
/// an empty list, removing an item that is not there, and moving an item to where it already is all
/// leave the handlers alone, because an event means something a listener cares about is different.
/// </para>
/// <para>
/// <b>Delivery is ordered, never nested.</b> A change made from inside a handler is queued and
/// delivered after the event that handler is hearing, so two handlers never see records out of order -
/// which matters because an observer applies them in sequence and out of order they would corrupt its
/// copy. The queue is also why changing the list being delivered is not a depth problem: a change to it
/// is queued, so a chain of them is flat and nothing bounds how many a drain delivers. The delivery
/// *call* is a different matter and is counted against <see cref="Observable.MaxEventStackSize"/>,
/// because a handler can re-enter one - by disposing a list, or by changing a different one, whose own
/// drain then runs inside that handler's frame (section 3.7).
/// </para>
/// <para>
/// <b>Batches suppress notifications, not changes.</b> Between <see cref="BeginChangeBatch"/> and
/// <see cref="EndChangeBatch"/> the list changes as usual and collects every record; the outermost end
/// delivers them all in one event, in order. Batches nest, and only the outermost end delivers.
/// </para>
/// </remarks>
public interface IObservableList<T> : IReadOnlyObservableList<T>, IList<T>
{
    /// <summary>How many items the list holds.</summary>
    /// <exception cref="ObjectDisposedException">The list is disposed.</exception>
    /// <remarks>
    /// <para>
    /// Redeclared rather than merely inherited, and that is a language requirement rather than a style
    /// choice: <see cref="IList{T}"/> brings <see cref="ICollection{T}.Count"/> and
    /// <see cref="IReadOnlyObservableList{T}"/> brings <see cref="IReadOnlyCollection{T}.Count"/>, two
    /// different members with the same name, so a caller of <c>list.Count</c> through this interface
    /// would be told the call is ambiguous. This declaration is the one that wins, and the
    /// implementation is the same property for all three.
    /// </para>
    /// <para>
    /// Reading publishes the count dependency, so an expression over the size is re-run when an add, a
    /// removal or a clear changes it, and not when an item is written or moved.
    /// </para>
    /// </remarks>
    new int Count { get; }

    /// <summary>The item at <paramref name="index"/>.</summary>
    /// <param name="index">Which item. Reading publishes that index's dependency, before the range
    /// check, so a read that throws is still a read.</param>
    /// <value>The item there. Writing raises one
    /// <see cref="ObservableListChangeType.ItemChanged"/> unless the new item equals the old one.</value>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the list.</exception>
    /// <exception cref="ObjectDisposedException">The list is disposed.</exception>
    /// <remarks>Redeclared for the same reason as <see cref="Count"/>: the two inherited indexers are
    /// different members, and one of them has to win.</remarks>
    new T this[int index] { get; set; }

    /// <summary>How many batches are open. Zero outside any batch.</summary>
    /// <remarks>
    /// On the interface rather than hidden, because a library that changes a list on someone else's
    /// behalf wants to know whether it is already inside a batch before opening one that would not add
    /// anything.
    /// </remarks>
    int ChangeBatchDepth { get; }

    /// <summary>Starts a batch, which stops the list delivering until it ends.</summary>
    /// <exception cref="ObjectDisposedException">The list is disposed.</exception>
    /// <remarks>
    /// Nesting is allowed and counted. Open one per caller that needs it and close it in the same
    /// order; only the outermost <see cref="EndChangeBatch"/> delivers, so an outer batch that several
    /// inner ones sit inside still produces one event.
    /// </remarks>
    void BeginChangeBatch();

    /// <summary>Ends a batch, delivering what it collected when it was the outermost one.</summary>
    /// <exception cref="ObjectDisposedException">The list is disposed.</exception>
    /// <exception cref="InvalidOperationException">No batch is open.</exception>
    /// <remarks>
    /// A batch that collected nothing delivers nothing, which is what makes a batch around a loop that
    /// happened to find no work free. A list disposed with a batch still open drops what it had not
    /// delivered, and the batch is gone with it.
    /// </remarks>
    void EndChangeBatch();

    /// <summary>Moves the item at <paramref name="oldIndex"/> so that it ends up at <paramref name="newIndex"/>.</summary>
    /// <param name="oldIndex">Where the item is.</param>
    /// <param name="newIndex">Where it should be, in the list as it stands after the removal.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either index is outside the list.</exception>
    /// <exception cref="ObjectDisposedException">The list is disposed.</exception>
    /// <remarks>
    /// The same rule <c>ObservableCollection&lt;T&gt;.Move</c> uses, so moving 0 to 2 in
    /// <c>[a, b, c]</c> gives <c>[b, c, a]</c>. One record is raised with both indexes and the item on
    /// both sides, and moving an item to where it already is raises nothing. It is on this interface
    /// and not expressed as a removal plus an insertion, because those two calls would be two records
    /// an observer has to recognise as one reposition.
    /// </remarks>
    void Move(int oldIndex, int newIndex);
}
