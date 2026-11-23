using System.Collections;

namespace Kolvir.Feature.Observables;

/// <summary>
/// A read-only view over another observable set, holding one item per equality class of the source's
/// items projected through <typeparamref name="TOuter"/>'s mapping function.
/// </summary>
/// <typeparam name="TInner">The type of the items of the set being watched.</typeparam>
/// <typeparam name="TOuter">The type of the items this view holds.</typeparam>
/// <remarks>
/// <para>
/// The set's counterpart of the mapped list view
/// (<see cref="ObservableMappedReadOnlyListView{TInner, TOuter}"/>, dev-docs/OBSERVABLE-COLLECTIONS.md
/// sections 5.4 and 6.5), and the one place the two collections' projections differ: a list projection
/// cannot drop or reorder an item, so its view is a record-for-record translation, while a set
/// projection is not injective. Two inner items the source's comparer calls different may map to outers
/// this view's comparer calls equal, so the view counts them and translates a source record into *at
/// most* one record - often none.
/// </para>
/// <para>
/// <b>The mapping is eager, cached and counted.</b> A <see cref="HashSet{T}"/> holds the distinct mapped
/// items and a <see cref="Dictionary{TKey, TValue}"/> holds how many source items currently map to each
/// of them, both on <see cref="Comparer"/>. The function runs once on construction per source item, once
/// per item the source adds, and once per item it removes - a removal has to map again because the record
/// names an inner item and the view is keyed by outer. It never runs on a clear, on a read, or to produce
/// the instance a removal reports: that is the instance the view already held. So reading this view costs
/// a set read, which is the reason it exists rather than a projection on every read.
/// </para>
/// <para>
/// <b>A change that leaves the view unchanged raises no event at all.</b> An add whose mapped item is
/// already present only increments a count; a removal that leaves another inner item on the same mapped
/// item only decrements one; a clear of an already-empty view does nothing. The view records only what
/// actually changed, so a batch with nothing pending does not drain - the same answer
/// <see cref="IObservableSet{T}"/> gives for an add its comparer calls equal
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 6.4). It is not an empty delivery, and nothing is woken.
/// </para>
/// <para>
/// <b>Mutating an item in place is not observed.</b> If <typeparamref name="TInner"/> is mutable and
/// changes internally, this view does not follow it: the source set reports items arriving and leaving,
/// not changes inside them, and nothing here can see what it is not told. Replacing the item in the
/// source set re-maps it, and so does any event that forces a resync.
/// </para>
/// <para>
/// <b>Reads publish dependencies, one kind per question</b>, exactly as the set does
/// (<see cref="IReadOnlyObservableSet{T}"/>): <see cref="IReadOnlyCollection{T}.Count"/> publishes the
/// count, <see cref="Contains"/> publishes the entry for that item, and enumeration, <see cref="CopyTo"/>
/// and the six set-algebra scans publish the contents. An expression over this view is therefore woken
/// through it and not through the source, and a reader of one item is not woken by a change to another.
/// </para>
/// <para>
/// <b>The source is not owned.</b> The view subscribes to it and reads it; it never disposes it, because
/// the same source may be read directly and by other views. <see cref="IDisposable.Dispose"/> ends this
/// view's life only: it unsubscribes, drops what had not been delivered, and delivers one last event with
/// no records, as <see cref="IReadOnlyObservableSet{T}"/> specifies. A source that is disposed first
/// delivers its own final event, which this view ignores - there can never be another change, so the view
/// keeps the contents it last had and stays readable.
/// </para>
/// <para>
/// <b>The mapping function is called from the source's notification.</b> It runs on the thread that
/// changed the source, inside the source set's delivery; it must not throw, must not change the source,
/// and must be deterministic - two calls for the same item have to produce outers this view's comparer
/// calls equal, or the counts no longer describe the view. A function that throws propagates out of the
/// source set's delivery, like any other handler's exception, and leaves this view behind the source; one
/// that is not deterministic fails visibly rather than silently, because a removal whose mapping finds
/// nothing this view holds rebuilds from the source and delivers an untracked record.
/// </para>
/// <para>
/// Single-threaded, like every collection in this package: no locks, no cross-thread delivery. It is not
/// an <see cref="IObservableEventSource"/> either, for the reason
/// <see cref="IReadOnlyObservableSet{T}"/> gives about sets.
/// </para>
/// </remarks>
public sealed class ObservableMappedReadOnlySetView<TInner, TOuter>
    : IReadOnlyObservableSet<TOuter>,
      IObservableChangeTarget<ObservableSetChange<TOuter>, ObservableSetChangedEventArgs<TOuter>>
    where TInner : notnull
    where TOuter : notnull
{
    private readonly IReadOnlyObservableSet<TInner> _inner;
    private readonly Func<TInner, TOuter> _selector;
    private readonly Action<ObservableSetChangedEventArgs<TInner>> _onInnerChanged;
    private readonly HashSet<TOuter> _items;
    private readonly Dictionary<TOuter, int> _counts;
    private readonly ObservableSetDependencies<TOuter> _dependencies;
    private readonly ObservableChangeBatch<ObservableSetChange<TOuter>, ObservableSetChangedEventArgs<TOuter>> _batch;

    /// <summary>Creates a view over <paramref name="inner"/>, projecting each item with <paramref name="selector"/>.</summary>
    /// <param name="inner">The set to watch. The view subscribes to it and does not own it.</param>
    /// <param name="selector">What turns a source item into the item this view holds. Called once per item
    /// that arrives, once per item that leaves, and once per item the source already held when this was
    /// created. It has to be deterministic.</param>
    /// <param name="comparer">What decides that two mapped items are the same item, or null for
    /// <see cref="EqualityComparer{T}.Default"/>. It is the view's identity in exactly the sense the source
    /// set's comparer is the source's, and it is what decides that two inner items collapse to one item
    /// here.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> or <paramref name="selector"/> is null.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="inner"/> is already disposed.</exception>
    /// <remarks>
    /// The contents the source already holds are mapped here, once; inner items that map to outers the
    /// comparer calls equal collapse into one item with a count. The subscription is taken afterwards,
    /// which is what keeps an event from arriving before there is a view to apply it to. Nothing is
    /// raised: there is nobody registered yet, and the mapped items are the starting state rather than a
    /// change to it.
    /// </remarks>
    public ObservableMappedReadOnlySetView(
        IReadOnlyObservableSet<TInner> inner,
        Func<TInner, TOuter> selector,
        IEqualityComparer<TOuter>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(selector);

        _inner = inner;
        _selector = selector;
        _onInnerChanged = OnInnerChanged;

        _items = new HashSet<TOuter>(comparer);

        // The hash set's own comparer, which is the one the storage settled on - a null argument means
        // the type's default, and the counts and the dependencies have to key by the same rule.
        _counts = new Dictionary<TOuter, int>(_items.Comparer);
        _dependencies = new ObservableSetDependencies<TOuter>(_items.Comparer);
        _batch = new(this);

        foreach (var item in inner)
        {
            AddMapped(selector(item));
        }

        inner.AddObservableSetChangedHandler(_onInnerChanged);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A constant of the view and not a read of anything, so it publishes no dependency and stays
    /// readable once the view is dead.
    /// </remarks>
    public bool IsDisposed => _batch.IsDisposed;

    /// <inheritdoc/>
    /// <remarks>
    /// A constant of the view and not a read of anything, so it publishes no dependency and stays
    /// readable once the view is dead. It is what makes two mapped items the same item, so a mirror built
    /// from this view's records has to use it too.
    /// </remarks>
    public IEqualityComparer<TOuter> Comparer => _items.Comparer;

    /// <inheritdoc/>
    /// <remarks>Publishes the count dependency: raised only by a record this view actually raised, which
    /// means only by a change that altered how many distinct mapped items there are.</remarks>
    public int Count
    {
        get
        {
            ThrowIfDisposed();

            _dependencies.PublishCount();
            return _items.Count;
        }
    }

    /// <summary>Whether the view holds <paramref name="item"/>.</summary>
    /// <param name="item">What to look for.</param>
    /// <returns>True when some mapped item equals it under <see cref="Comparer"/>.</returns>
    /// <exception cref="ObjectDisposedException">The view is disposed.</exception>
    /// <remarks>
    /// Publishes the dependency for that item, so a consumer asking about one item is not woken by a
    /// change to another.
    /// </remarks>
    public bool Contains(TOuter item)
    {
        ThrowIfDisposed();

        _dependencies.PublishItem(item);
        return _items.Contains(item);
    }

    /// <summary>Copies every mapped item into <paramref name="array"/>, starting at <paramref name="arrayIndex"/>.</summary>
    /// <param name="array">Where to copy them.</param>
    /// <param name="arrayIndex">The first position to write.</param>
    /// <exception cref="ObjectDisposedException">The view is disposed.</exception>
    /// <remarks>Publishes the contents dependency, because it reads every item.</remarks>
    public void CopyTo(TOuter[] array, int arrayIndex)
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        _items.CopyTo(array, arrayIndex);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Publishes the contents dependency, because an enumeration answers from every item. The enumerator
    /// is the mapped storage's own, so it is the version-checked one.
    /// </remarks>
    public IEnumerator<TOuter> GetEnumerator()
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        return _items.GetEnumerator();
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    public bool SetEquals(IEnumerable<TOuter> other) => ReadContents(_items.SetEquals, other);

    /// <inheritdoc/>
    public bool IsSubsetOf(IEnumerable<TOuter> other) => ReadContents(_items.IsSubsetOf, other);

    /// <inheritdoc/>
    public bool IsSupersetOf(IEnumerable<TOuter> other) => ReadContents(_items.IsSupersetOf, other);

    /// <inheritdoc/>
    public bool IsProperSubsetOf(IEnumerable<TOuter> other) => ReadContents(_items.IsProperSubsetOf, other);

    /// <inheritdoc/>
    public bool IsProperSupersetOf(IEnumerable<TOuter> other) => ReadContents(_items.IsProperSupersetOf, other);

    /// <inheritdoc/>
    public bool Overlaps(IEnumerable<TOuter> other) => ReadContents(_items.Overlaps, other);

    /// <inheritdoc/>
    public IDisposable AddObservableSetChangedHandler(Action<ObservableSetChangedEventArgs<TOuter>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return _batch.AddHandler(handler);
    }

    /// <inheritdoc/>
    public void RemoveObservableSetChangedHandler(Action<ObservableSetChangedEventArgs<TOuter>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _batch.RemoveHandler(handler);
    }

    /// <summary>
    /// Ends the view's life: unsubscribes from the source, drops any change that had not been delivered,
    /// delivers one last event with no changes, and lets every handler go.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The source set is not disposed and stays usable - the view does not own it, may be one of several
    /// readers of it, and a teardown that took the model's set down with the screen would be a bug. What
    /// ends here is this view's subscription to it.
    /// </para>
    /// <para>
    /// Idempotent, and safe after the source is gone, because the removal is tolerant by
    /// <see cref="IReadOnlyObservableSet{T}"/>'s contract.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (_batch.IsDisposed)
        {
            return;
        }

        try
        {
            // Before the final event, so nothing the source is delivering can reach this view afterwards.
            _inner.RemoveObservableSetChangedHandler(_onInnerChanged);
        }
        finally
        {
            _batch.Dispose();
        }
    }

    /// <summary>What this view is, for a diagnostic.</summary>
    public override string ToString() =>
        $"an observable view of {typeof(TInner).Name} as {typeof(TOuter).Name}";

    /// <inheritdoc/>
    int IObservableChangeTarget<ObservableSetChange<TOuter>, ObservableSetChangedEventArgs<TOuter>>.Size =>
        _items.Count;

    /// <inheritdoc/>
    ObservableSetChange<TOuter> IObservableChangeTarget<ObservableSetChange<TOuter>, ObservableSetChangedEventArgs<TOuter>>.Untracked =>
        ObservableSetChange<TOuter>.Untracked();

    /// <inheritdoc/>
    bool IObservableChangeTarget<ObservableSetChange<TOuter>, ObservableSetChangedEventArgs<TOuter>>.IsUntracked(
        in ObservableSetChange<TOuter> change) =>
        change.ChangeType == ObservableSetChangeType.UntrackedChanges;

    /// <inheritdoc/>
    ObservableSetChangedEventArgs<TOuter>
        IObservableChangeTarget<ObservableSetChange<TOuter>, ObservableSetChangedEventArgs<TOuter>>.CreateArgs(
            IReadOnlyList<ObservableSetChange<TOuter>> changes) =>
        new(this, changes);

    /// <inheritdoc/>
    void IObservableChangeTarget<ObservableSetChange<TOuter>, ObservableSetChangedEventArgs<TOuter>>.Mark(
        IReadOnlyList<ObservableSetChange<TOuter>> changes,
        int size) =>
        _dependencies.Mark(changes);

    /// <inheritdoc/>
    void IObservableChangeTarget<ObservableSetChange<TOuter>, ObservableSetChangedEventArgs<TOuter>>.RaiseDependencies() =>
        _dependencies.Raise();

    /// <inheritdoc/>
    void IObservableChangeTarget<ObservableSetChange<TOuter>, ObservableSetChangedEventArgs<TOuter>>.ReleaseDependencies() =>
        _dependencies.Release();

    /// <summary>Translates what the source delivered into the view's record stream, and delivers it once.</summary>
    /// <remarks>
    /// <para>
    /// Every record is applied to the storage before it is recorded, because a handler that reads the
    /// view must already see what the records it is being handed describe - the same order the source set
    /// itself mutates in.
    /// </para>
    /// <para>
    /// The loop runs inside one batch of this view's own. The source's delivery is already the unit an
    /// observer sees, so a batch of three source changes has to leave here as one event with the
    /// translated records rather than three events. The projection is not injective, so that event may
    /// carry fewer records than the source did, and no event at all when nothing in the view changed. Its
    /// batch carries the package default cap and knows nothing about a source whose owner raised its own:
    /// an event that would describe more records than the view will collapses here to one untracked
    /// record, while the view is already complete, which is the same fidelity-for-boundedness trade a
    /// single set makes, one level down.
    /// </para>
    /// </remarks>
    private void OnInnerChanged(ObservableSetChangedEventArgs<TInner> args)
    {
        if (_batch.IsDisposed)
        {
            // The source hands its handlers out as a snapshot, so one taken before our unsubscribe can
            // still call us. Nothing may touch the storage after the view is dead.
            return;
        }

        var changes = args.Changes;

        if (changes.Count == 0)
        {
            // The source's one empty event is its disposal: it can never change again. The view keeps the
            // contents it last had and stays readable - the source's death is not this view's death.
            return;
        }

        _batch.BeginChangeBatch();

        try
        {
            for (var index = 0; index < changes.Count; index++)
            {
                Translate(changes[index]);
            }
        }
        finally
        {
            // Delivers the records that were applied, even when the selector threw part way: what is
            // pending always describes what the storage now holds. An empty batch delivers nothing, which
            // is the answer for a source change that left the view unchanged.
            _batch.EndChangeBatch();
        }
    }

    private void Translate(in ObservableSetChange<TInner> change)
    {
        switch (change.ChangeType)
        {
            case ObservableSetChangeType.ItemAdded:
            {
                var item = _selector(change.Item!);

                // Only the first inner item for this mapped item is a change to the view. The rest are a
                // count, and a count moving is not something an observer of the set can apply.
                if (AddMapped(item))
                {
                    _batch.Record(ObservableSetChange<TOuter>.Add(item));
                }

                break;
            }

            case ObservableSetChangeType.ItemRemoved:
            {
                // Mapped again because the record names an inner item and the view is keyed by outer -
                // there is no cached inner-to-outer map to read. The outer this reports is the stored
                // instance, not the fresh one, which is what a consumer's own copy holds.
                var item = _selector(change.Item!);

                switch (RemoveMapped(item, out var stored))
                {
                    case RemovalOutcome.LastOneLeft:
                        _batch.Record(ObservableSetChange<TOuter>.Remove(stored));
                        break;

                    case RemovalOutcome.NotHeld:
                        // The storage and the source disagree, which means the mapping function was not
                        // deterministic. Nothing here can describe what happened, so rebuild from the
                        // source and say the change was not described - the same conservative answer an
                        // unknown record gets - so every reader is told to re-read and the view heals
                        // instead of drifting silently.
                        Resync();
                        break;

                    default:
                        // StillHeld: another inner item maps to it, so the view is unchanged.
                        break;
                }

                break;
            }

            case ObservableSetChangeType.SetCleared:
                if (ClearMapped())
                {
                    _batch.Record(ObservableSetChange<TOuter>.Clear());
                }

                break;

            default:
                // UntrackedChanges, and any kind a later version adds: the change was not described, so
                // the only safe answer is to rebuild from the source and say so - the same conservative
                // answer ObservableSetDependencies.Mark gives for an unknown record.
                Resync();
                break;
        }
    }

    /// <summary>Rebuilds the mapped contents from the source, and records that the change was not tracked.</summary>
    private void Resync()
    {
        ClearMapped();

        foreach (var item in _inner)
        {
            AddMapped(_selector(item));
        }

        _batch.Record(ObservableSetChange<TOuter>.Untracked());
    }

    /// <summary>Adds one mapping to the storage.</summary>
    /// <param name="item">The mapped item.</param>
    /// <returns>True when the view gained a distinct item, which is the only case a record describes.</returns>
    /// <remarks>
    /// The one place the two structures grow together: while a count exists the item is already in the
    /// set, and the set is only touched on the first mapping, so they cannot disagree about membership.
    /// </remarks>
    private bool AddMapped(TOuter item)
    {
        if (_counts.TryGetValue(item, out var count))
        {
            _counts[item] = count + 1;
            return false;
        }

        _items.Add(item);
        _counts.Add(item, 1);
        return true;
    }

    /// <summary>Takes one mapping out of the storage.</summary>
    /// <param name="item">The mapped item, as the mapping function produced it now.</param>
    /// <param name="stored">The instance the view held for it, meaningful only when this returns
    /// <see cref="RemovalOutcome.LastOneLeft"/>.</param>
    /// <returns>What the removal found: the last mapping for it, another inner item still on it, or
    /// nothing at all.</returns>
    /// <remarks>
    /// The one place the two structures shrink together. <see cref="RemovalOutcome.NotHeld"/> is the one
    /// outcome that is not a removal: the storage has no mapping for what the source says left, which
    /// means the mapping function was not deterministic - it produced an outer for that item on the way in
    /// that it does not produce on the way out. The caller answers that by resyncing, because a count that
    /// disagrees with the source would otherwise stay wrong and unwatched.
    /// </remarks>
    private RemovalOutcome RemoveMapped(TOuter item, out TOuter stored)
    {
        if (!_counts.TryGetValue(item, out var count))
        {
            stored = item;
            return RemovalOutcome.NotHeld;
        }

        if (count > 1)
        {
            _counts[item] = count - 1;
            stored = item;
            return RemovalOutcome.StillHeld;
        }

        if (!_items.TryGetValue(item, out var storedItem))
        {
            storedItem = item;
        }

        _items.Remove(item);
        _counts.Remove(item);
        stored = storedItem;
        return RemovalOutcome.LastOneLeft;
    }

    /// <summary>What taking one mapping out of the storage found.</summary>
    private enum RemovalOutcome
    {
        /// <summary>No mapping was held for it, so the storage and the source disagree.</summary>
        NotHeld,

        /// <summary>Another inner item still maps to it, so the view is unchanged.</summary>
        StillHeld,

        /// <summary>It was the last one, so the mapped item left the view.</summary>
        LastOneLeft,
    }

    /// <summary>Empties both structures together.</summary>
    /// <returns>True when the view held something, which is the only case a clear is a change.</returns>
    private bool ClearMapped()
    {
        if (_items.Count == 0)
        {
            return false;
        }

        _items.Clear();
        _counts.Clear();
        return true;
    }

    /// <summary>Answers one scan of the view, having published the dependency that scan reads.</summary>
    /// <param name="scan">The storage's own implementation of that scan.</param>
    /// <param name="other">What the caller passed.</param>
    /// <remarks>
    /// Every set-algebra question reads every item on at least one side, which is what the contents
    /// dependency is for (dev-docs/OBSERVABLE-COLLECTIONS.md section 6.2.4). Delegating to the storage is
    /// deliberate: its duplicate and foreign-comparer rules are the ones a set is supposed to have.
    /// </remarks>
    private bool ReadContents(Func<IEnumerable<TOuter>, bool> scan, IEnumerable<TOuter> other)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(other);

        _dependencies.PublishContents();
        return scan(other);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_batch.IsDisposed, this);
}
