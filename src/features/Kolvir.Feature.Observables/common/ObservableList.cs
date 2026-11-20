using System.Collections;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Kolvir.Observables;

/// <summary>
/// The one implementation of an observable list: storage, a handler list, the records in flight and a
/// dead flag.
/// </summary>
/// <typeparam name="T">The type of the items.</typeparam>
/// <remarks>
/// <para>
/// Internal, and handed out as <see cref="IObservableList{T}"/> and
/// <see cref="IReadOnlyObservableList{T}"/>, so the class itself is not part of the contract.
/// </para>
/// <para>
/// Two pieces of state beyond the items. <see cref="_pending"/> holds records that have not been
/// delivered: the open batch's, or a handler-caused change waiting for the delivery that is running to
/// finish. <see cref="_isDelivering"/> says a delivery is in progress, which is what makes a change
/// from inside a handler queue rather than nest - and that queue is what keeps the chain flat, which is
/// why nothing here needs a depth limit (dev-docs/OBSERVABLE-COLLECTIONS.md section 3.7).
/// </para>
/// <para>
/// The storage is a <see cref="List{T}"/>, so its enumerator is the version-checked one: mutating during
/// a <c>foreach</c> throws <see cref="InvalidOperationException"/> without this class having to keep a
/// version of its own.
/// </para>
/// </remarks>
internal sealed class ObservableList<T> : IObservableList<T>
{
    private readonly List<T> _items;
    private readonly IEqualityComparer<T> _comparer;
    private readonly ObservableListDependencies<T> _dependencies = new();
    private readonly List<ObservableListChange<T>> _pending = [];

    private ImmutableArray<Action<ObservableListChangedEventArgs<T>>> _handlers = [];
    private bool _isDisposed;
    private bool _isDelivering;
    private int _batchDepth;

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
    }

    /// <inheritdoc/>
    public bool IsDisposed => _isDisposed;

    /// <inheritdoc/>
    /// <remarks>
    /// A constant of the list and not a read of anything, so it publishes no dependency and stays
    /// readable once the list is dead.
    /// </remarks>
    public bool IsReadOnly => false;

    /// <inheritdoc/>
    public int ChangeBatchDepth => _batchDepth;

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
            Record(ObservableListChange<T>.Change(index, oldItem, value));
        }
    }

    /// <inheritdoc/>
    public void Add(T item)
    {
        ThrowIfDisposed();

        _items.Add(item);
        Record(ObservableListChange<T>.Add(_items.Count - 1, item));
    }

    /// <inheritdoc/>
    public void Insert(int index, T item)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _items.Count);

        _items.Insert(index, item);
        Record(ObservableListChange<T>.Add(index, item));
    }

    /// <inheritdoc/>
    public void RemoveAt(int index)
    {
        ThrowIfDisposed();

        // Read before mutating, so an out-of-range index throws without having recorded anything.
        var item = _items[index];

        _items.RemoveAt(index);
        Record(ObservableListChange<T>.Remove(index, item));
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
        Record(ObservableListChange<T>.Remove(index, stored));
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
        Record(ObservableListChange<T>.Clear());
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

        Record(ObservableListChange<T>.Move(oldIndex, newIndex, item));
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

        _batchDepth++;
    }

    /// <inheritdoc/>
    public void EndChangeBatch()
    {
        ThrowIfDisposed();

        if (_batchDepth == 0)
        {
            throw new InvalidOperationException(
                "EndChangeBatch was called with no batch open. A batch is begun and ended by the same "
                + "caller, in that order, and nesting is counted rather than ignored.");
        }

        _batchDepth--;

        if (_batchDepth == 0 && _pending.Count > 0)
        {
            Drain();
        }
    }

    /// <inheritdoc/>
    public IDisposable AddObservableListChangedHandler(Action<ObservableListChangedEventArgs<T>> handler)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(handler);

        // Copy-on-write, which is what makes a delivery a snapshot without copying it.
        _handlers = _handlers.Add(handler);
        return new HandlerRegistration<T>(this, handler);
    }

    /// <inheritdoc/>
    public void RemoveObservableListChangedHandler(Action<ObservableListChangedEventArgs<T>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handlers = _handlers.Remove(handler);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Dead first, so a handler that reads the list here gets <see cref="ObjectDisposedException"/>
    /// rather than a last state that will never change again; then the changes that had not been
    /// delivered are dropped, because they describe a list nobody may read; then one last event with no
    /// changes, which is how a registrant learns to let go; then the handler lists and the dependency
    /// subscriptions are dropped, in a <c>finally</c>, because a leak is the one outcome this method
    /// exists to prevent.
    /// </remarks>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _pending.Clear();
        _batchDepth = 0;

        try
        {
            var handlers = _handlers;

            if (!handlers.IsEmpty)
            {
                var final = new ObservableListChangedEventArgs<T>(this, []);

                // A dispatch like any other, and counted like one: it is reached from a handler often
                // enough that it is one of the two ways a delivery nests.
                using (Observable.EnterDelivery(this))
                {
                    foreach (var handler in handlers)
                    {
                        handler(final);
                    }
                }
            }
        }
        finally
        {
            _handlers = [];
            _dependencies.Release();
        }
    }

    /// <summary>What this list is, for a diagnostic.</summary>
    public override string ToString() => $"an observable list of {typeof(T).Name}";

    private void Record(in ObservableListChange<T> change)
    {
        // A pending event that has already given up on detail needs nothing appended to it: one
        // UntrackedChanges record tells the observer to re-read the list, and it says that whatever came
        // before it in this batch. That is what keeps a bulk load from growing this list without bound.
        if (_pending.Count != 1 || _pending[0].ChangeType != ObservableListChangeType.UntrackedChanges)
        {
            _pending.Add(change);

            if (_pending.Count > Observable.MaxBatchRecords)
            {
                _pending.Clear();
                _pending.Add(ObservableListChange<T>.Untracked());
            }
        }

        // Inside a batch the records wait for the outermost end; inside a delivery they wait for the
        // event being delivered to finish, which is what keeps the observer's history in order.
        if (_batchDepth == 0)
        {
            Drain();
        }
    }

    /// <summary>Delivers what is pending, one event at a time, until nothing is left.</summary>
    /// <remarks>
    /// <para>
    /// A loop rather than a recursion, so a handler that changes the list costs a queue entry and not a
    /// stack frame. A drain started from inside a drain returns immediately and leaves the work to the
    /// one already running, which is what preserves the order.
    /// </para>
    /// <para>
    /// The loop itself is unbounded, and it should be: a handler that changes the list it is hearing
    /// about costs a queue entry, not a frame, so a chain of them cannot exhaust the stack -
    /// <see cref="Observable.MaxEventStackSize"/> has nothing to say about a loop that never descends.
    /// Such a handler is doing real work every time round and ends when it stops changing things; one
    /// that never stops is an ordinary infinite loop in application code, the kind <c>while (true)</c>
    /// is, and a work limit here would only turn it into an exception that discards changes an observer
    /// was promised.
    /// </para>
    /// <para>
    /// The dispatch each iteration makes is a different matter, and it is counted - see
    /// <see cref="Deliver"/>, because a delivery can be re-entered even when a change to this list
    /// cannot.
    /// </para>
    /// </remarks>
    private void Drain()
    {
        if (_isDelivering || _pending.Count == 0)
        {
            return;
        }

        _isDelivering = true;

        try
        {
            while (_pending.Count > 0)
            {
                Deliver();
            }
        }
        finally
        {
            _isDelivering = false;
        }
    }

    private void Deliver()
    {
        var handlers = _handlers;

        // Taken before any handler runs: it is the size the records being delivered left behind, and a
        // handler's own change belongs to the next event, so counting it here would put the wrong bound
        // on an index read.
        var count = _items.Count;

        if (handlers.IsEmpty)
        {
            // Nobody to tell, but the dependencies still have to know: a render that reads this list is
            // watching one of them and not the event.
            _dependencies.Mark(CollectionsMarshal.AsSpan(_pending), count);
            _pending.Clear();
            _dependencies.Raise();
            return;
        }

        var records = _pending.ToArray();
        _pending.Clear();

        var args = new ObservableListChangedEventArgs<T>(this, records);

        // The handlers first, then the reads they invalidated: a handler is the listener this change is
        // being reported to, and a dependency raise is the consequence of it.
        //
        // Counted against the evaluation depth, because this dispatch can be re-entered even though a
        // change to *this* list is queued: a handler that disposes a list raises that list's final
        // event inside this frame, and one that changes a different list starts that list's drain
        // here. Those nest, so they are stack, and a chain of them is what the guard is for.
        using (Observable.EnterDelivery(this))
        {
            foreach (var handler in handlers)
            {
                handler(args);
            }
        }

        _dependencies.Mark(records, count);
        _dependencies.Raise();
    }


    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);

    /// <summary>The token that unregisters one typed handler, and nothing else.</summary>
    /// <typeparam name="TItem">The type of the list's items.</typeparam>
    /// <remarks>
    /// A second token type because <see cref="HandlerRegistration"/> is for an
    /// <see cref="IObservableEventSource"/>, which a list is not (section 3.8). It behaves the same
    /// way: it releases both references on the first dispose, so a token that outlives its list holds
    /// neither, and it stays safe to dispose after the list.
    /// </remarks>
    private sealed class HandlerRegistration<TItem> : IDisposable
    {
        private IReadOnlyObservableList<TItem>? _list;
        private Action<ObservableListChangedEventArgs<TItem>>? _handler;

        internal HandlerRegistration(
            IReadOnlyObservableList<TItem> list,
            Action<ObservableListChangedEventArgs<TItem>> handler)
        {
            _list = list;
            _handler = handler;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            var list = _list;
            var handler = _handler;

            if (list is null || handler is null)
            {
                return;
            }

            _list = null;
            _handler = null;

            list.RemoveObservableListChangedHandler(handler);
        }
    }
}
