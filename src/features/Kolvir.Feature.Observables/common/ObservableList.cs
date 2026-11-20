using System.Collections;

namespace Kolvir.Observables;

/// <summary>
/// The one implementation of an observable list: storage, and the batch that carries its records to the
/// handlers.
/// </summary>
/// <typeparam name="T">The type of the items.</typeparam>
/// <remarks>
/// <para>
/// Internal, and handed out as <see cref="IObservableList{T}"/> and
/// <see cref="IReadOnlyObservableList{T}"/>, so the class itself is not part of the contract.
/// </para>
/// <para>
/// Everything about how a change reaches a handler - the queue, the batches, the cap, disposal - is
/// <see cref="ObservableChangeBatch{TChange, TArgs}"/>, and everything about what a read depends on is
/// <see cref="ObservableListDependencies{T}"/>. What is left here is what a list is: the items, the
/// comparer that decides whether a write changed one, and the record each mutation raises
/// (dev-docs/OBSERVABLE-COLLECTIONS.md sections 3.4 and 5).
/// </para>
/// <para>
/// The storage is a <see cref="List{T}"/>, so its enumerator is the version-checked one: mutating during
/// a <c>foreach</c> throws <see cref="InvalidOperationException"/> without this class having to keep a
/// version of its own.
/// </para>
/// </remarks>
internal sealed class ObservableList<T>
    : IObservableList<T>, IObservableChangeTarget<ObservableListChange<T>, ObservableListChangedEventArgs<T>>
{
    private readonly List<T> _items;
    private readonly IEqualityComparer<T> _comparer;
    private readonly ObservableListDependencies<T> _dependencies = new();
    private readonly ObservableChangeBatch<ObservableListChange<T>, ObservableListChangedEventArgs<T>> _batch;

    /// <summary>Creates an empty list.</summary>
    /// <param name="comparer">What decides that a write changed an item, or null for the type's own.</param>
    internal ObservableList(IEqualityComparer<T>? comparer)
        : this([], comparer)
    {
    }

    /// <summary>Creates a list with contents.</summary>
    /// <param name="items">What it holds, in order.</param>
    /// <param name="comparer">What decides that a write changed an item, or null for the type's own.</param>
    internal ObservableList(ReadOnlySpan<T> items, IEqualityComparer<T>? comparer)
    {
        _items = [.. items];
        _comparer = comparer ?? EqualityComparer<T>.Default;
        _batch = new(this);
    }

    /// <inheritdoc/>
    public bool IsDisposed => _batch.IsDisposed;

    /// <inheritdoc/>
    /// <remarks>
    /// A constant of the list and not a read of anything, so it publishes no dependency and stays
    /// readable once the list is dead.
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
    /// <remarks>Publishes the count dependency: raised by an add, a removal or a clear, and by nothing
    /// else - a write to an item and a move both leave the size alone.</remarks>
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
    /// Reading publishes the index dependency before the range check, so a read that throws is still a
    /// read and is re-run when the index comes into range. Writing publishes nothing - a write is a
    /// change, not a read - and raises one <see cref="ObservableListChangeType.ItemChanged"/> when the
    /// new item differs from the old one by this list's comparer.
    /// </remarks>
    public T this[int index]
    {
        get
        {
            ThrowIfDisposed();

            _dependencies.PublishIndex(index);
            return _items[index];
        }

        set
        {
            ThrowIfDisposed();

            var oldItem = _items[index];

            if (_comparer.Equals(oldItem, value))
            {
                return;
            }

            _items[index] = value;
            _batch.Record(ObservableListChange<T>.Change(index, oldItem, value));
        }
    }

    /// <inheritdoc/>
    public void Add(T item)
    {
        ThrowIfDisposed();

        _items.Add(item);
        _batch.Record(ObservableListChange<T>.Add(_items.Count - 1, item));
    }

    /// <inheritdoc/>
    public void Insert(int index, T item)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _items.Count);

        _items.Insert(index, item);
        _batch.Record(ObservableListChange<T>.Add(index, item));
    }

    /// <inheritdoc/>
    public void RemoveAt(int index)
    {
        ThrowIfDisposed();

        // Read before mutating, so an out-of-range index throws without having recorded anything.
        var item = _items[index];

        _items.RemoveAt(index);
        _batch.Record(ObservableListChange<T>.Remove(index, item));
    }

    /// <inheritdoc/>
    /// <remarks>A removal by item reports the index it was found at, and one that finds nothing raises
    /// nothing at all.</remarks>
    public bool Remove(T item)
    {
        ThrowIfDisposed();

        var index = _items.IndexOf(item);

        if (index < 0)
        {
            return false;
        }

        // The stored item, not the argument: the record says what came out of the list, which is what an
        // observer's copy had in it.
        var stored = _items[index];

        _items.RemoveAt(index);
        _batch.Record(ObservableListChange<T>.Remove(index, stored));
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>One record however many items there were, and nothing at all for a list already
    /// empty.</remarks>
    public void Clear()
    {
        ThrowIfDisposed();

        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        _batch.Record(ObservableListChange<T>.Clear());
    }

    /// <inheritdoc/>
    public void Move(int oldIndex, int newIndex)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(oldIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(oldIndex, _items.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(newIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(newIndex, _items.Count);

        if (oldIndex == newIndex)
        {
            return;
        }

        var item = _items[oldIndex];

        _items.RemoveAt(oldIndex);
        _items.Insert(newIndex, item);

        _batch.Record(ObservableListChange<T>.Move(oldIndex, newIndex, item));
    }

    /// <inheritdoc/>
    public bool Contains(T item)
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        return _items.Contains(item);
    }

    /// <inheritdoc/>
    public int IndexOf(T item)
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        return _items.IndexOf(item);
    }

    /// <inheritdoc/>
    public void CopyTo(T[] array, int arrayIndex)
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        _items.CopyTo(array, arrayIndex);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Publishes the contents dependency, because an enumeration answers from every item. The
    /// enumerator is the storage's own, so mutating during the enumeration throws
    /// <see cref="InvalidOperationException"/> rather than yielding a torn sequence.
    /// </remarks>
    public IEnumerator<T> GetEnumerator()
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        return _items.GetEnumerator();
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

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
    public IDisposable AddObservableListChangedHandler(Action<ObservableListChangedEventArgs<T>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return _batch.AddHandler(handler);
    }

    /// <inheritdoc/>
    public void RemoveObservableListChangedHandler(Action<ObservableListChangedEventArgs<T>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _batch.RemoveHandler(handler);
    }

    /// <summary>
    /// Ends the list's life: pending records are dropped, one last empty event says so, and every
    /// subscription is let go.
    /// </summary>
    public void Dispose() => _batch.Dispose();

    /// <summary>What this list is, for a diagnostic.</summary>
    public override string ToString() => $"an observable list of {typeof(T).Name}";

    /// <inheritdoc/>
    int IObservableChangeTarget<ObservableListChange<T>, ObservableListChangedEventArgs<T>>.Size =>
        _items.Count;

    /// <inheritdoc/>
    ObservableListChange<T> IObservableChangeTarget<ObservableListChange<T>, ObservableListChangedEventArgs<T>>.Untracked =>
        ObservableListChange<T>.Untracked();

    /// <inheritdoc/>
    bool IObservableChangeTarget<ObservableListChange<T>, ObservableListChangedEventArgs<T>>.IsUntracked(
        in ObservableListChange<T> change) =>
        change.ChangeType == ObservableListChangeType.UntrackedChanges;

    /// <inheritdoc/>
    ObservableListChangedEventArgs<T>
        IObservableChangeTarget<ObservableListChange<T>, ObservableListChangedEventArgs<T>>.CreateArgs(
            IReadOnlyList<ObservableListChange<T>> changes) =>
        new(this, changes);

    /// <inheritdoc/>
    void IObservableChangeTarget<ObservableListChange<T>, ObservableListChangedEventArgs<T>>.Mark(
        IReadOnlyList<ObservableListChange<T>> changes,
        int size) =>
        _dependencies.Mark(changes, size);

    /// <inheritdoc/>
    void IObservableChangeTarget<ObservableListChange<T>, ObservableListChangedEventArgs<T>>.RaiseDependencies() =>
        _dependencies.Raise();

    /// <inheritdoc/>
    void IObservableChangeTarget<ObservableListChange<T>, ObservableListChangedEventArgs<T>>.ReleaseDependencies() =>
        _dependencies.Release();

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_batch.IsDisposed, this);
}
