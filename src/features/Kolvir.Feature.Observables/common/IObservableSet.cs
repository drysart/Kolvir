namespace Kolvir.Observables;

/// <summary>
/// A set that can be changed, which only the code that owns the state is handed.
/// </summary>
/// <typeparam name="T">The type of the items.</typeparam>
/// <remarks>
/// <para>
/// What <see cref="Observable.CreateHashSet{T}()"/> returns. The owner of a model holds one in a private
/// field and exposes it as <see cref="IReadOnlyObservableSet{T}"/>, so a consumer can read it and watch
/// it but cannot change it.
/// </para>
/// <para>
/// <b>Every change is reported at the granularity it happened.</b> An add is one
/// <see cref="ObservableSetChangeType.ItemAdded"/> naming the item; a removal is one
/// <see cref="ObservableSetChangeType.ItemRemoved"/>, carrying the item as it was stored; and a
/// <c>Clear</c> is one <see cref="ObservableSetChangeType.SetCleared"/> however many items there were.
/// A call that changes nothing raises nothing: adding an item the comparer says is already there,
/// removing one that is not, and clearing an empty set all leave the handlers alone.
/// </para>
/// <para>
/// <b>A bulk operation is one record per item, delivered as one event.</b> <c>UnionWith</c>,
/// <c>ExceptWith</c>, <c>IntersectWith</c> and <c>SymmetricExceptWith</c> change a number of items nobody
/// knows in advance, so each runs inside an implicit batch: every item that actually arrived or left is
/// one record, and the observer hears them all at once. Nested inside a caller's batch, the implicit one
/// joins it rather than delivering an event of its own.
/// </para>
/// <para>
/// <b>Delivery is ordered, never nested.</b> A change made from inside a handler is queued and delivered
/// after the event that handler is hearing, so two handlers never see records out of order. The delivery
/// *call* is counted against <see cref="Observable.MaxEventStackSize"/>, because a handler can re-enter
/// one - by disposing a set, or by changing a different collection, whose own drain then runs inside that
/// handler's frame (section 3.7).
/// </para>
/// <para>
/// <b>Batches suppress notifications, not changes.</b> Between <see cref="BeginChangeBatch"/> and
/// <see cref="EndChangeBatch"/> the set changes as usual and collects every record; the outermost end
/// delivers them all in one event, in order. A batch is capped at
/// <see cref="MaxCollectionBatchSize"/> records: past that the collected records are dropped and the
/// event carries one <see cref="ObservableSetChangeType.UntrackedChanges"/> record instead, and nothing
/// later in that batch is queued.
/// </para>
/// </remarks>
public interface IObservableSet<T> : IReadOnlyObservableSet<T>, ISet<T>
{
    /// <summary>How many items the set holds.</summary>
    /// <exception cref="ObjectDisposedException">The set is disposed.</exception>
    /// <remarks>
    /// <para>
    /// Redeclared rather than merely inherited, and that is a language requirement rather than a style
    /// choice: <see cref="ISet{T}"/> brings <see cref="ICollection{T}.Count"/> and
    /// <see cref="IReadOnlyObservableSet{T}"/> brings <see cref="IReadOnlyCollection{T}.Count"/>, two
    /// different members with the same name, so a caller of <c>set.Count</c> through this interface
    /// would be told the call is ambiguous. This declaration is the one that wins, and the
    /// implementation is the same property for all three.
    /// </para>
    /// <para>
    /// Reading publishes the count dependency, so an expression over the size is re-run when an item is
    /// added, removed or cleared, and never by a call that changed nothing.
    /// </para>
    /// </remarks>
    new int Count { get; }

    /// <summary>Whether <paramref name="item"/> is in the set.</summary>
    /// <param name="item">The item to look for.</param>
    /// <exception cref="ObjectDisposedException">The set is disposed.</exception>
    /// <remarks>
    /// <para>
    /// Redeclared for the same reason as <see cref="Count"/>: <see cref="IReadOnlySet{T}"/> and
    /// <see cref="ICollection{T}"/> both bring it, so <c>set.Contains(x)</c> through this interface would
    /// be ambiguous.
    /// </para>
    /// <para>
    /// Reading publishes the dependency for <em>that item</em>, which is what makes a rendered toggle
    /// depend on its own tag and on nothing else (section 6.2.4).
    /// </para>
    /// </remarks>
    new bool Contains(T item);

    /// <summary>Whether this set and <paramref name="other"/> hold the same items.</summary>
    /// <param name="other">What to compare with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The set is disposed.</exception>
    /// <remarks>Redeclared because <see cref="IReadOnlySet{T}"/> and <see cref="ISet{T}"/> both bring the
    /// six set-algebra questions. Reading publishes the contents dependency, because the answer is read
    /// from every item on at least one side.</remarks>
    new bool SetEquals(IEnumerable<T> other);

    /// <summary>Whether every item here is also in <paramref name="other"/>.</summary>
    /// <param name="other">What to compare with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The set is disposed.</exception>
    /// <remarks>Redeclared for the reason <see cref="SetEquals"/> gives; publishes the contents
    /// dependency.</remarks>
    new bool IsSubsetOf(IEnumerable<T> other);

    /// <summary>Whether every item in <paramref name="other"/> is also here.</summary>
    /// <param name="other">What to compare with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The set is disposed.</exception>
    /// <remarks>Redeclared for the reason <see cref="SetEquals"/> gives; publishes the contents
    /// dependency.</remarks>
    new bool IsSupersetOf(IEnumerable<T> other);

    /// <summary>Whether this set is a subset of <paramref name="other"/> and is smaller than it.</summary>
    /// <param name="other">What to compare with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The set is disposed.</exception>
    /// <remarks>Redeclared for the reason <see cref="SetEquals"/> gives; publishes the contents
    /// dependency.</remarks>
    new bool IsProperSubsetOf(IEnumerable<T> other);

    /// <summary>Whether this set is a superset of <paramref name="other"/> and is larger than it.</summary>
    /// <param name="other">What to compare with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The set is disposed.</exception>
    /// <remarks>Redeclared for the reason <see cref="SetEquals"/> gives; publishes the contents
    /// dependency.</remarks>
    new bool IsProperSupersetOf(IEnumerable<T> other);

    /// <summary>Whether this set and <paramref name="other"/> have an item in common.</summary>
    /// <param name="other">What to compare with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The set is disposed.</exception>
    /// <remarks>Redeclared for the reason <see cref="SetEquals"/> gives; publishes the contents
    /// dependency.</remarks>
    new bool Overlaps(IEnumerable<T> other);

    /// <summary>How many batches are open. Zero outside any batch.</summary>
    /// <remarks>
    /// On the interface rather than hidden, because a library that changes a set on someone else's
    /// behalf wants to know whether it is already inside a batch before opening one that would not add
    /// anything.
    /// </remarks>
    int ChangeBatchDepth { get; }

    /// <summary>How many records one event from this set may carry before it stops describing them.</summary>
    /// <value>
    /// <see cref="Observable.Default.MaxCollectionBatchSize"/> for a set that has not been changed, and
    /// whatever was last assigned for one that has.
    /// </value>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than one, which would cap every
    /// event at nothing and make every batch a rescan.</exception>
    /// <remarks>
    /// The set owns this, and it is copied from the default when the set is created: setting it here
    /// tunes this set's observers, and changing <see cref="Observable.Default.MaxCollectionBatchSize"/>
    /// tunes the ones sets created afterwards will have. A bulk operation that would carry more than
    /// this many records is delivered as one <see cref="ObservableSetChangeType.UntrackedChanges"/>
    /// record instead.
    /// </remarks>
    int MaxCollectionBatchSize { get; set; }

    /// <summary>Starts a batch, which stops the set delivering until it ends.</summary>
    /// <exception cref="ObjectDisposedException">The set is disposed.</exception>
    /// <remarks>
    /// Nesting is allowed and counted. Open one per caller that needs it and close it in the same order;
    /// only the outermost <see cref="EndChangeBatch"/> delivers, so an outer batch that several inner ones
    /// sit inside still produces one event.
    /// </remarks>
    void BeginChangeBatch();

    /// <summary>Ends a batch, delivering what it collected when it was the outermost one.</summary>
    /// <exception cref="ObjectDisposedException">The set is disposed.</exception>
    /// <exception cref="InvalidOperationException">No batch is open.</exception>
    /// <remarks>
    /// A batch that collected nothing delivers nothing, which is what makes a batch around a loop that
    /// happened to find no work free. A set disposed with a batch still open drops what it had not
    /// delivered, and the batch is gone with it.
    /// </remarks>
    void EndChangeBatch();
}
