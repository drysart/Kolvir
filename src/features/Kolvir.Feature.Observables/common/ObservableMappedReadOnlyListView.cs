using System.Collections;

namespace Kolvir.Feature.Observables;

/// <summary>
/// A read-only view over another observable list, holding one item per source item projected through
/// <typeparamref name="TOuter"/>'s mapping function.
/// </summary>
/// <typeparam name="TInner">The type of the items of the list being watched.</typeparam>
/// <typeparam name="TOuter">The type of the items this view holds.</typeparam>
/// <remarks>
/// <para>
/// The first of the derived views section 5.3 of <c>dev-docs/OBSERVABLE-COLLECTIONS.md</c> left open,
/// and the easiest of them: a projection never drops an item and never reorders one, so every record
/// the source raises has exactly one record here, with the mapped values in it
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 5.4).
/// </para>
/// <para>
/// <b>The mapping is eager and cached.</b> A parallel list of the mapped items is kept beside the
/// source, index for index, and the function runs once per item that arrives: on an add, and on a
/// replacement for the new item. A move moves the mapped value it already has - the same item, in a new
/// place - and a removal or a clear drops it. So reading this view costs nothing beyond a list read,
/// which is the reason it exists rather than a projection on every read.
/// </para>
/// <para>
/// <b>Mutating an item in place is not observed.</b> If <typeparamref name="TInner"/> is mutable and
/// changes internally, this view does not follow it: the source list reports changes to its items, not
/// changes inside them, and nothing here can see what it is not told. Replacing the item in the source
/// list (<c>list[i] = ...</c>) does re-map it, and so does any event that forces a resync. An
/// application that mutates items in place has to replace them, or keep the projection out of the
/// picture for that item.
/// </para>
/// <para>
/// <b>Reads publish dependencies, one kind per question</b>, exactly as the list does
/// (<see cref="IReadOnlyObservableList{T}"/>): <see cref="IReadOnlyCollection{T}.Count"/> publishes the
/// count, <c>this[index]</c> publishes that index, and enumeration, <see cref="Contains"/>,
/// <see cref="IndexOf"/> and <see cref="CopyTo"/> publish the contents. An expression over this view is
/// therefore woken through it and not through the source, so it re-runs once per change rather than
/// once per record the source happened to raise.
/// </para>
/// <para>
/// <b>The source is not owned.</b> The view subscribes to it and reads it; it never disposes it, because
/// the same source may be read directly and by other views. <see cref="IDisposable.Dispose"/> ends this
/// view's life only: it unsubscribes, drops what had not been delivered, and delivers one last event
/// with no records, as <see cref="IReadOnlyObservableList{T}"/> specifies. A source that is disposed
/// first delivers its own final event, which this view ignores - there can never be another change, so
/// the view keeps the contents it last had and stays readable.
/// </para>
/// <para>
/// <b>The mapping function is called from the source's notification.</b> It runs on the thread that
/// changed the source, inside the source list's delivery, at most once per item that arrives; it must
/// not throw and must not change the source list or this view. A function that throws propagates out of
/// the source list's delivery, like any other handler's exception, and leaves this view behind the
/// source.
/// </para>
/// <para>
/// Single-threaded, like every collection in this package: no locks, no cross-thread delivery. It is not
/// an <see cref="IObservableEventSource"/> either, for the reason <see cref="IReadOnlyObservableList{T}"/>
/// gives about lists.
/// </para>
/// </remarks>
public sealed class ObservableMappedReadOnlyListView<TInner, TOuter>
    : IReadOnlyObservableList<TOuter>,
      IObservableChangeTarget<ObservableListChange<TOuter>, ObservableListChangedEventArgs<TOuter>>
{
    private readonly IReadOnlyObservableList<TInner> _inner;
    private readonly Func<TInner, TOuter> _selector;
    private readonly Action<ObservableListChangedEventArgs<TInner>> _onInnerChanged;
    private readonly List<TOuter> _items = [];
    private readonly ObservableListDependencies<TOuter> _dependencies = new();
    private readonly ObservableChangeBatch<ObservableListChange<TOuter>, ObservableListChangedEventArgs<TOuter>> _batch;

    /// <summary>Creates a view over <paramref name="inner"/>, projecting each item with <paramref name="selector"/>.</summary>
    /// <param name="inner">The list to watch. The view subscribes to it and does not own it.</param>
    /// <param name="selector">What turns a source item into the item this view holds. Called once per item
    /// that arrives, and once per item the source already held when this was created.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> or <paramref name="selector"/> is null.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="inner"/> is already disposed.</exception>
    /// <remarks>
    /// The contents the source already holds are mapped here, once, in order; the subscription is taken
    /// afterwards, which is what keeps an event from arriving before there is a list to apply it to.
    /// Nothing is raised: there is nobody registered yet, and the mapped items are the starting state
    /// rather than a change to it.
    /// </remarks>
    public ObservableMappedReadOnlyListView(
        IReadOnlyObservableList<TInner> inner,
        Func<TInner, TOuter> selector)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(selector);

        _inner = inner;
        _selector = selector;
        _onInnerChanged = OnInnerChanged;
        _batch = new(this);

        foreach (var item in inner)
        {
            _items.Add(selector(item));
        }

        inner.AddObservableListChangedHandler(_onInnerChanged);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A constant of the view and not a read of anything, so it publishes no dependency and stays
    /// readable once the view is dead.
    /// </remarks>
    public bool IsDisposed => _batch.IsDisposed;

    /// <inheritdoc/>
    /// <remarks>Publishes the count dependency: raised by an add, a removal or a clear of the source,
    /// which are the only changes that alter how many mapped items there are.</remarks>
    public int Count
    {
        get
        {
            ThrowIfDisposed();

            _dependencies.PublishCount();
            return _items.Count;
        }
    }

    /// <summary>The mapped item at <paramref name="index"/>.</summary>
    /// <param name="index">Which item.</param>
    /// <value>The item the source's item at <paramref name="index"/> mapped to when it arrived.</value>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the view.</exception>
    /// <exception cref="ObjectDisposedException">The view is disposed.</exception>
    /// <remarks>
    /// Reading publishes that index's dependency before the range check, so a read that throws is still
    /// a read and is re-run when the index comes into range.
    /// </remarks>
    public TOuter this[int index]
    {
        get
        {
            ThrowIfDisposed();

            _dependencies.PublishIndex(index);
            return _items[index];
        }
    }

    /// <summary>Whether the view holds <paramref name="item"/>.</summary>
    /// <param name="item">What to look for.</param>
    /// <returns>True when some mapped item equals it.</returns>
    /// <exception cref="ObjectDisposedException">The view is disposed.</exception>
    /// <remarks>Publishes the contents dependency, because the answer comes from every item.</remarks>
    public bool Contains(TOuter item)
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        return _items.Contains(item);
    }

    /// <summary>The first index holding <paramref name="item"/>, or -1.</summary>
    /// <param name="item">What to look for.</param>
    /// <returns>Where it is, or -1 when it is not there.</returns>
    /// <exception cref="ObjectDisposedException">The view is disposed.</exception>
    /// <remarks>Publishes the contents dependency, because the answer comes from every item.</remarks>
    public int IndexOf(TOuter item)
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        return _items.IndexOf(item);
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
    /// is the mapped list's own, so it is the version-checked one.
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
    public IDisposable AddObservableListChangedHandler(Action<ObservableListChangedEventArgs<TOuter>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return _batch.AddHandler(handler);
    }

    /// <inheritdoc/>
    public void RemoveObservableListChangedHandler(Action<ObservableListChangedEventArgs<TOuter>> handler)
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
    /// The source list is not disposed and stays usable - the view does not own it, may be one of several
    /// readers of it, and a teardown that took the model's list down with the screen would be a bug. What
    /// ends here is this view's subscription to it.
    /// </para>
    /// <para>
    /// Idempotent, and safe after the source is gone, because the removal is tolerant by
    /// <see cref="IReadOnlyObservableList{T}"/>'s contract.
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
            _inner.RemoveObservableListChangedHandler(_onInnerChanged);
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
    int IObservableChangeTarget<ObservableListChange<TOuter>, ObservableListChangedEventArgs<TOuter>>.Size =>
        _items.Count;

    /// <inheritdoc/>
    ObservableListChange<TOuter> IObservableChangeTarget<ObservableListChange<TOuter>, ObservableListChangedEventArgs<TOuter>>.Untracked =>
        ObservableListChange<TOuter>.Untracked();

    /// <inheritdoc/>
    bool IObservableChangeTarget<ObservableListChange<TOuter>, ObservableListChangedEventArgs<TOuter>>.IsUntracked(
        in ObservableListChange<TOuter> change) =>
        change.ChangeType == ObservableListChangeType.UntrackedChanges;

    /// <inheritdoc/>
    ObservableListChangedEventArgs<TOuter>
        IObservableChangeTarget<ObservableListChange<TOuter>, ObservableListChangedEventArgs<TOuter>>.CreateArgs(
            IReadOnlyList<ObservableListChange<TOuter>> changes) =>
        new(this, changes);

    /// <inheritdoc/>
    void IObservableChangeTarget<ObservableListChange<TOuter>, ObservableListChangedEventArgs<TOuter>>.Mark(
        IReadOnlyList<ObservableListChange<TOuter>> changes,
        int size) =>
        _dependencies.Mark(changes, size);

    /// <inheritdoc/>
    void IObservableChangeTarget<ObservableListChange<TOuter>, ObservableListChangedEventArgs<TOuter>>.RaiseDependencies() =>
        _dependencies.Raise();

    /// <inheritdoc/>
    void IObservableChangeTarget<ObservableListChange<TOuter>, ObservableListChangedEventArgs<TOuter>>.ReleaseDependencies() =>
        _dependencies.Release();

    /// <summary>Translates what the source delivered into the mapped record stream, and delivers it once.</summary>
    /// <remarks>
    /// <para>
    /// Every record is applied to the mapped list before it is recorded, because a handler that reads the
    /// view must already see what the records it is being handed describe - the same order the source
    /// list itself mutates in.
    /// </para>
    /// <para>
    /// The loop runs inside one batch of this view's own. The source's delivery is already the unit an
    /// observer sees, so a batch of three source changes has to leave here as one event with three
    /// translated records rather than three events; and the batch is what makes that true however many
    /// records arrived.
    /// </para>
    /// </remarks>
    private void OnInnerChanged(ObservableListChangedEventArgs<TInner> args)
    {
        if (_batch.IsDisposed)
        {
            // The source hands its handlers out as a snapshot, so one taken before our unsubscribe can
            // still call us. Nothing may touch the cache after the view is dead.
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
            // pending always describes what the cache now holds.
            _batch.EndChangeBatch();
        }
    }

    private void Translate(in ObservableListChange<TInner> change)
    {
        switch (change.ChangeType)
        {
            case ObservableListChangeType.ItemAdded:
            {
                var index = change.NewIndex!.Value;
                var item = _selector(change.NewItem!);

                _items.Insert(index, item);
                _batch.Record(ObservableListChange<TOuter>.Add(index, item));
                break;
            }

            case ObservableListChangeType.ItemRemoved:
            {
                var index = change.OldIndex!.Value;

                // The cached value, not a mapping of OldItem: the mapped item that came out is the one an
                // observer's own copy had in it, and a removal is not a reason to run the function.
                var item = _items[index];

                _items.RemoveAt(index);
                _batch.Record(ObservableListChange<TOuter>.Remove(index, item));
                break;
            }

            case ObservableListChangeType.ItemMoved:
            {
                var oldIndex = change.OldIndex!.Value;
                var newIndex = change.NewIndex!.Value;

                // A move is a reposition: the value it already has goes with it, and the function is not
                // called, because the item that moved is the item that was already mapped.
                var item = _items[oldIndex];

                _items.RemoveAt(oldIndex);
                _items.Insert(newIndex, item);
                _batch.Record(ObservableListChange<TOuter>.Move(oldIndex, newIndex, item));
                break;
            }

            case ObservableListChangeType.ItemChanged:
            {
                var index = change.NewIndex!.Value;
                var oldItem = _items[index];
                var newItem = _selector(change.NewItem!);

                _items[index] = newItem;
                _batch.Record(ObservableListChange<TOuter>.Change(index, oldItem, newItem));
                break;
            }

            case ObservableListChangeType.ListCleared:
                _items.Clear();
                _batch.Record(ObservableListChange<TOuter>.Clear());
                break;

            default:
                // UntrackedChanges, and any kind a later version adds: the change was not described, so the
                // only safe answer is to rebuild from the source and say so - the same conservative answer
                // ObservableListDependencies.Mark gives for an unknown record.
                Resync();
                break;
        }
    }

    /// <summary>Rebuilds the mapped list from the source, and records that the change was not tracked.</summary>
    private void Resync()
    {
        _items.Clear();

        foreach (var item in _inner)
        {
            _items.Add(_selector(item));
        }

        _batch.Record(ObservableListChange<TOuter>.Untracked());
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_batch.IsDisposed, this);
}
