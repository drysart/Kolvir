namespace Kolvir.Feature.Observables;

/// <summary>
/// The one implementation of an observable set: a <see cref="HashSet{T}"/> in the order it happens to
/// enumerate in, and the batch that carries its records to the handlers.
/// </summary>
/// <typeparam name="T">The type of the items.</typeparam>
/// <remarks>
/// <para>
/// Internal, and handed out as <see cref="IObservableSet{T}"/> and
/// <see cref="IReadOnlyObservableSet{T}"/>, so the class itself is not part of the contract.
/// </para>
/// <para>
/// A set is the simplest of the three collections and that is what its implementation should look like:
/// no index and no value, so a change can only add an item or take one away, and everything about how a
/// change reaches a handler is <see cref="ObservableChangeBatch{TChange, TArgs}"/>. What is left here is
/// the storage, the four bulk operations and the record each call raises
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 6).
/// </para>
/// <para>
/// The storage is a <see cref="HashSet{T}"/>, so its enumerator is the version-checked one: mutating
/// during a <c>foreach</c> throws <see cref="InvalidOperationException"/> rather than yielding a torn
/// sequence.
/// </para>
/// </remarks>
internal sealed class ObservableHashSet<T>
    : IObservableSet<T>, IObservableChangeTarget<ObservableSetChange<T>, ObservableSetChangedEventArgs<T>>
    where T : notnull
{
    private readonly HashSet<T> _items;
    private readonly ObservableSetDependencies<T> _dependencies;
    private readonly ObservableChangeBatch<ObservableSetChange<T>, ObservableSetChangedEventArgs<T>> _batch;

    /// <summary>Creates an empty set.</summary>
    /// <param name="comparer">What decides that two items are the same item, or null for the type's
    /// own.</param>
    internal ObservableHashSet(IEqualityComparer<T>? comparer)
        : this([], comparer)
    {
    }

    /// <summary>Creates a set holding <paramref name="items"/>.</summary>
    /// <param name="items">What it holds. Duplicates by the comparer collapse, as they would in a
    /// <see cref="HashSet{T}"/>.</param>
    /// <param name="comparer">What decides that two items are the same item, or null for the type's
    /// own.</param>
    /// <remarks>Creating a set with contents raises nothing: there is nobody registered yet, and the
    /// items are the starting state rather than a change to it.</remarks>
    internal ObservableHashSet(ReadOnlySpan<T> items, IEqualityComparer<T>? comparer)
    {
        _items = new HashSet<T>(comparer);

        foreach (var item in items)
        {
            _items.Add(item);
        }

        // The set's own comparer, which is the one the storage settled on - a null argument means the
        // type's default, and the dependencies have to key by the same rule.
        _dependencies = new ObservableSetDependencies<T>(_items.Comparer);
        _batch = new(this);
    }

    /// <inheritdoc/>
    public IEqualityComparer<T> Comparer => _items.Comparer;

    /// <inheritdoc/>
    public bool IsDisposed => _batch.IsDisposed;

    /// <inheritdoc/>
    /// <remarks>
    /// A constant of the set and not a read of anything, so it publishes no dependency and stays readable
    /// once the set is dead.
    /// </remarks>
    public bool IsReadOnly => false;

    /// <inheritdoc/>
    public int ChangeBatchDepth => _batch.ChangeBatchDepth;

    /// <inheritdoc/>
    public int MaxCollectionBatchSize
    {
        get => _batch.MaxCollectionBatchSize;
        set => _batch.MaxCollectionBatchSize = value;
    }

    /// <inheritdoc/>
    /// <remarks>Publishes the count dependency: raised by an item arriving, an item leaving, or a clear,
    /// and by nothing else.</remarks>
    public int Count
    {
        get
        {
            ThrowIfDisposed();

            _dependencies.PublishCount();
            return _items.Count;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Publishes the dependency for that item, and raises one
    /// <see cref="ObservableSetChangeType.ItemAdded"/> when the comparer says it was not already
    /// there. Adding an item the comparer calls equal is a no-op that returns false and raises nothing,
    /// so the item already in the set stays the one it had.
    /// </remarks>
    public bool Add(T item)
    {
        ThrowIfDisposed();

        if (!_items.Add(item))
        {
            return false;
        }

        _batch.Record(ObservableSetChange<T>.Add(item));
        return true;
    }

    /// <summary>The <see cref="ICollection{T}"/> spelling of <see cref="Add"/>, which returns nothing.</summary>
    /// <param name="item">The item to add.</param>
    void ICollection<T>.Add(T item) => Add(item);

    /// <inheritdoc/>
    /// <remarks>
    /// Publishes the dependency for that item, and raises one
    /// <see cref="ObservableSetChangeType.ItemRemoved"/> carrying the item as the set stored it - which
    /// matters when the comparer calls the argument equal to a different instance. Removing an item that
    /// is not there raises nothing.
    /// </remarks>
    public bool Remove(T item)
    {
        ThrowIfDisposed();

        if (!_items.TryGetValue(item, out var stored))
        {
            return false;
        }

        _items.Remove(item);
        _batch.Record(ObservableSetChange<T>.Remove(stored));
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>One record however many items there were, and nothing at all for a set already
    /// empty.</remarks>
    public void Clear()
    {
        ThrowIfDisposed();

        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        _batch.Record(ObservableSetChange<T>.Clear());
    }

    /// <inheritdoc/>
    /// <remarks>
    /// One <see cref="ObservableSetChangeType.ItemAdded"/> per item taken, in <paramref name="other"/>'s
    /// order, delivered as one event: the implicit batch is the same mechanism a caller would use by hand,
    /// and nested inside a caller's batch it joins it rather than delivering an event of its own
    /// (dev-docs/OBSERVABLE-COLLECTIONS.md section 6.2.2).
    /// </remarks>
    public void UnionWith(IEnumerable<T> other)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(other);

        _batch.BeginChangeBatch();

        try
        {
            foreach (var item in other)
            {
                if (_items.Add(item))
                {
                    _batch.Record(ObservableSetChange<T>.Add(item));
                }
            }
        }
        finally
        {
            _batch.EndChangeBatch();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// One <see cref="ObservableSetChangeType.ItemRemoved"/> per item that left. A set of ten thousand
    /// items with an empty argument raises nothing at all, and one that is emptied this way is one
    /// <see cref="ObservableSetChangeType.SetCleared"/> when the argument is the set itself.
    /// </remarks>
    public void ExceptWith(IEnumerable<T> other)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(other);

        if (ReferenceEquals(other, this))
        {
            // Removing while enumerating this set would invalidate the enumerator, and the answer is the
            // clear anyway.
            Clear();
            return;
        }

        _batch.BeginChangeBatch();

        try
        {
            foreach (var item in other)
            {
                if (_items.Remove(item))
                {
                    _batch.Record(ObservableSetChange<T>.Remove(item));
                }
            }
        }
        finally
        {
            _batch.EndChangeBatch();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// One <see cref="ObservableSetChangeType.ItemRemoved"/> per item that left, decided in one pass and
    /// removed in a second, because a mutation during the enumeration would throw. No size change means
    /// no event: intersecting with a superset raises nothing.
    /// </remarks>
    public void IntersectWith(IEnumerable<T> other)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(other);

        if (ReferenceEquals(other, this))
        {
            return;
        }

        // Membership is asked of a set when the argument is one, and of a copy when it is not, so a long
        // sequence is not rescanned once per item.
        var keep = other as IReadOnlySet<T> ?? new HashSet<T>(other, _items.Comparer);

        List<T>? leaving = null;

        foreach (var item in _items)
        {
            if (!keep.Contains(item))
            {
                (leaving ??= []).Add(item);
            }
        }

        if (leaving is null)
        {
            return;
        }

        _batch.BeginChangeBatch();

        try
        {
            foreach (var item in leaving)
            {
                if (_items.Remove(item))
                {
                    _batch.Record(ObservableSetChange<T>.Remove(item));
                }
            }
        }
        finally
        {
            _batch.EndChangeBatch();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Every item that leaves is one <see cref="ObservableSetChangeType.ItemRemoved"/> and every item that
    /// arrives is one <see cref="ObservableSetChangeType.ItemAdded"/>, with the removals delivered first -
    /// so an observer applying the records in order never has to add an item that is already there.
    /// </para>
    /// <para>
    /// The argument is read once into the set's own equality, which is what makes both passes possible on
    /// a sequence that can only be enumerated once. A symmetric difference with the set itself empties it,
    /// and is reported as one <see cref="ObservableSetChangeType.SetCleared"/>.
    /// </para>
    /// </remarks>
    public void SymmetricExceptWith(IEnumerable<T> other)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(other);

        if (ReferenceEquals(other, this))
        {
            Clear();
            return;
        }

        var seen = new HashSet<T>(_items.Comparer);
        List<T>? leaving = null;
        List<T>? arriving = null;

        foreach (var item in other)
        {
            if (!seen.Add(item))
            {
                continue;
            }

            (_items.Contains(item) ? leaving ??= [] : arriving ??= []).Add(item);
        }

        if (leaving is null && arriving is null)
        {
            return;
        }

        _batch.BeginChangeBatch();

        try
        {
            if (leaving is not null)
            {
                foreach (var item in leaving)
                {
                    if (_items.Remove(item))
                    {
                        _batch.Record(ObservableSetChange<T>.Remove(item));
                    }
                }
            }

            if (arriving is not null)
            {
                foreach (var item in arriving)
                {
                    if (_items.Add(item))
                    {
                        _batch.Record(ObservableSetChange<T>.Add(item));
                    }
                }
            }
        }
        finally
        {
            _batch.EndChangeBatch();
        }
    }

    /// <inheritdoc/>
    /// <remarks>Publishes the contents dependency, because an enumeration answers from every item.</remarks>
    public IEnumerator<T> GetEnumerator()
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        return _items.GetEnumerator();
    }

    /// <inheritdoc/>
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    /// <remarks>Publishes the dependency for that item, so a toggle that asks about its own item is not
    /// woken by a change to another one.</remarks>
    public bool Contains(T item)
    {
        ThrowIfDisposed();

        _dependencies.PublishItem(item);
        return _items.Contains(item);
    }

    /// <inheritdoc/>
    public void CopyTo(T[] array, int arrayIndex)
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        _items.CopyTo(array, arrayIndex);
    }

    /// <inheritdoc/>
    public bool SetEquals(IEnumerable<T> other) => ReadContents(_items.SetEquals, other);

    /// <inheritdoc/>
    public bool IsSubsetOf(IEnumerable<T> other) => ReadContents(_items.IsSubsetOf, other);

    /// <inheritdoc/>
    public bool IsSupersetOf(IEnumerable<T> other) => ReadContents(_items.IsSupersetOf, other);

    /// <inheritdoc/>
    public bool IsProperSubsetOf(IEnumerable<T> other) => ReadContents(_items.IsProperSubsetOf, other);

    /// <inheritdoc/>
    public bool IsProperSupersetOf(IEnumerable<T> other) => ReadContents(_items.IsProperSupersetOf, other);

    /// <inheritdoc/>
    public bool Overlaps(IEnumerable<T> other) => ReadContents(_items.Overlaps, other);

    /// <inheritdoc/>
    public void BeginChangeBatch()
    {
        ThrowIfDisposed();

        _batch.BeginChangeBatch();
    }

    /// <inheritdoc/>
    public void EndChangeBatch()
    {
        ThrowIfDisposed();

        _batch.EndChangeBatch();
    }

    /// <inheritdoc/>
    public IDisposable AddObservableSetChangedHandler(Action<ObservableSetChangedEventArgs<T>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return _batch.AddHandler(handler);
    }

    /// <inheritdoc/>
    public void RemoveObservableSetChangedHandler(Action<ObservableSetChangedEventArgs<T>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _batch.RemoveHandler(handler);
    }

    /// <summary>
    /// Ends the set's life: pending records are dropped, one last empty event says so, and every
    /// subscription is let go.
    /// </summary>
    public void Dispose() => _batch.Dispose();

    /// <summary>What this set is, for a diagnostic.</summary>
    public override string ToString() => $"an observable set of {typeof(T).Name}";

    /// <inheritdoc/>
    int IObservableChangeTarget<ObservableSetChange<T>, ObservableSetChangedEventArgs<T>>.Size =>
        _items.Count;

    /// <inheritdoc/>
    ObservableSetChange<T> IObservableChangeTarget<ObservableSetChange<T>, ObservableSetChangedEventArgs<T>>.Untracked =>
        ObservableSetChange<T>.Untracked();

    /// <inheritdoc/>
    bool IObservableChangeTarget<ObservableSetChange<T>, ObservableSetChangedEventArgs<T>>.IsUntracked(
        in ObservableSetChange<T> change) =>
        change.ChangeType == ObservableSetChangeType.UntrackedChanges;

    /// <inheritdoc/>
    ObservableSetChangedEventArgs<T>
        IObservableChangeTarget<ObservableSetChange<T>, ObservableSetChangedEventArgs<T>>.CreateArgs(
            IReadOnlyList<ObservableSetChange<T>> changes) =>
        new(this, changes);

    /// <inheritdoc/>
    void IObservableChangeTarget<ObservableSetChange<T>, ObservableSetChangedEventArgs<T>>.Mark(
        IReadOnlyList<ObservableSetChange<T>> changes,
        int size) =>
        _dependencies.Mark(changes);

    /// <inheritdoc/>
    void IObservableChangeTarget<ObservableSetChange<T>, ObservableSetChangedEventArgs<T>>.RaiseDependencies() =>
        _dependencies.Raise();

    /// <inheritdoc/>
    void IObservableChangeTarget<ObservableSetChange<T>, ObservableSetChangedEventArgs<T>>.ReleaseDependencies() =>
        _dependencies.Release();

    /// <summary>Answers one scan of the set, having published the dependency that scan reads.</summary>
    /// <param name="scan">The storage's own implementation of that scan.</param>
    /// <param name="other">What the caller passed.</param>
    /// <remarks>
    /// Every set-algebra question reads every item on at least one side, which is what the contents
    /// dependency is for (dev-docs/OBSERVABLE-COLLECTIONS.md section 6.2.4).
    /// </remarks>
    private bool ReadContents(Func<IEnumerable<T>, bool> scan, IEnumerable<T> other)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(other);

        _dependencies.PublishContents();
        return scan(other);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_batch.IsDisposed, this);
}
