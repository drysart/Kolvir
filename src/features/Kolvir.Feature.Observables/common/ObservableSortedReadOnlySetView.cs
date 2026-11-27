using System.Collections;

namespace Kolvir.Feature.Observables;

/// <summary>
/// A read-only, ordered view over another observable set: the source's items, sorted by an
/// <see cref="IComparer{T}"/>, exposed as an <see cref="IReadOnlyObservableList{T}"/>.
/// </summary>
/// <typeparam name="T">The type of the items of the set being watched, and of this view.</typeparam>
/// <remarks>
/// <para>
/// The ordered view 5.3 of <c>dev-docs/OBSERVABLE-COLLECTIONS.md</c> left open, and the one that is a
/// list rather than a set: it only orders the source, it never decides membership, so it holds exactly
/// one node per source item and every source record becomes one record here. It is a view of a set and
/// not a set: it implements no algebra, because a sorted projection of a set has no set operations of
/// its own to answer (dev-docs/OBSERVABLE-COLLECTIONS.md section 6.7).
/// </para>
/// <para>
/// <b>An order-statistic AVL tree, so insert, remove and the indexer are all <c>O(log n)</c>.</b> Each
/// node carries the size of its subtree, which turns the tree into a rank index: <c>this[index]</c>
/// walks down by sizes, an insertion finds the rank it produced on the way down rather than in a second
/// search, and enumeration is the in-order walk, <c>O(n)</c> with an <c>O(log n)</c> stack. A skip list
/// with per-level widths would have been the other candidate and is easier to get right, but its bounds
/// are expected rather than worst-case; an AVL tree gives the worst case and lets the height bound be
/// asserted directly, which is why it is the one used here.
/// </para>
/// <para>
/// <b>Ordering is by the comparer first and an arrival number last.</b> Two distinct items can compare
/// equal under the ordering - a case-insensitive ordering over a case-sensitive set is the usual way -
/// so comparing by the user's comparer alone would make their keys equal and break both the tree's
/// search and the fact that a list may hold both. Every item is therefore given a monotonically
/// increasing sequence number when it arrives, the tree's key is (comparer, sequence), and the key is
/// unique by construction: equal-comparing items are neighbours ordered by the order they arrived in.
/// </para>
/// <para>
/// <b>A removal arrives as an element, so it needs a way back to its node.</b> A
/// <see cref="Dictionary{TKey, TValue}"/> keyed by the source set's own
/// <see cref="IReadOnlyObservableSet{T}.Comparer"/> (falling back to
/// <see cref="EqualityComparer{T}.Default"/>) holds each element's sequence number, which is what lets
/// a removal pick the right node among tied elements - the ordering alone cannot tell them apart. The
/// number is forgotten when the element leaves, so an element that is removed and added again is the
/// newest among its ties; keeping the number forever would make that rule different and would let the
/// map grow without bound. The numbering starts in the source's enumeration order at construction, so
/// ties the source already held start in whatever order it yields them; a rebuild keeps the number of
/// every item it still holds and numbers only the ones it has not seen before, so a tie that survives
/// an untracked change keeps the order it arrived in.
/// </para>
/// <para>
/// <b>Reads publish dependencies, one kind per question</b>, exactly as the list does
/// (<see cref="IReadOnlyObservableList{T}"/>): <see cref="IReadOnlyCollection{T}.Count"/> publishes
/// the count, <c>this[index]</c> publishes that index, and enumeration publishes the contents. An
/// expression over this view is therefore woken through it and not through the source, and a reader
/// of one row is not woken by a change to another.
/// </para>
/// <para>
/// <b>The source is not owned.</b> The view subscribes to it and reads it; it never disposes it,
/// because the same set may be read directly and by other views. <see cref="IDisposable.Dispose"/>
/// ends this view's life only: it unsubscribes, drops what had not been delivered, and delivers one
/// last event with no records, as <see cref="IReadOnlyObservableList{T}"/> specifies. A source that is
/// disposed first delivers its own final event, which this view ignores - there can never be another
/// change, so the view keeps the contents it last had and stays readable.
/// </para>
/// <para>
/// Single-threaded, like every collection in this package: no locks, no cross-thread delivery. It is
/// not an <see cref="IObservableEventSource"/> either, for the reason
/// <see cref="IReadOnlyObservableList{T}"/> gives about lists.
/// </para>
/// </remarks>
public sealed class ObservableSortedReadOnlySetView<T>
    : IReadOnlyObservableList<T>,
      IObservableChangeTarget<ObservableListChange<T>, ObservableListChangedEventArgs<T>>
    where T : notnull
{
    private readonly IReadOnlyObservableSet<T> _inner;
    private readonly IComparer<T> _order;
    private readonly IEqualityComparer<T> _identity;
    private readonly Action<ObservableSetChangedEventArgs<T>> _onInnerChanged;
    private readonly ObservableListDependencies<T> _dependencies = new();
    private readonly ObservableChangeBatch<ObservableListChange<T>, ObservableListChangedEventArgs<T>> _batch;

    private Dictionary<T, long> _sequences;
    private Node? _root;
    private long _nextSequence;
    private int _version;

    /// <summary>Creates a view over <paramref name="inner"/>, ordered by <paramref name="comparer"/>.</summary>
    /// <param name="inner">The set to watch. The view subscribes to it and does not own it.</param>
    /// <param name="comparer">What decides the order, or null for <see cref="Comparer{T}.Default"/>. It is
    /// the view's order and not its identity: the source set's own comparer still decides what "the same
    /// item" means when one is removed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is null.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="inner"/> is already disposed.</exception>
    /// <remarks>
    /// The contents the source already holds are numbered and built into a balanced tree here, once; the
    /// subscription is taken afterwards, which is what keeps an event from arriving before there is a
    /// list to apply it to. Nothing is raised: there is nobody registered yet, and the ordered items are
    /// the starting state rather than a change to it.
    /// </remarks>
    public ObservableSortedReadOnlySetView(
        IReadOnlyObservableSet<T> inner,
        IComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(inner);

        ObjectDisposedException.ThrowIf(inner.IsDisposed, inner);

        _inner = inner;
        _order = comparer ?? Comparer<T>.Default;

        // The source's comparer is what says two items are the same item. The interface promises a
        // non-null comparer; the fallback is here so a hand-written source cannot make it a null
        // dereference.
        _identity = inner.Comparer ?? EqualityComparer<T>.Default;
        _sequences = new Dictionary<T, long>(_identity);

        _onInnerChanged = OnInnerChanged;
        _batch = new(this);

        Rebuild();

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
    /// Publishes the count dependency, which an add, a removal and a clear all raise. The answer is the
    /// root's stored size, so it is a field read rather than a walk.
    /// </remarks>
    public int Count
    {
        get
        {
            ThrowIfDisposed();

            _dependencies.PublishCount();
            return _root?.Size ?? 0;
        }
    }

    /// <summary>The item at <paramref name="index"/> in the view's order.</summary>
    /// <param name="index">Which item.</param>
    /// <value>The sorted item at that position.</value>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the view.</exception>
    /// <exception cref="ObjectDisposedException">The view is disposed.</exception>
    /// <remarks>
    /// Reading publishes that index's dependency before the range check, so a read that throws is still
    /// a read and is re-run when the index comes into range. The lookup is the tree's rank index,
    /// <c>O(log n)</c>: at each node the left subtree's size says whether the index is in it, at the
    /// node, or in the right subtree with the offset removed.
    /// </remarks>
    public T this[int index]
    {
        get
        {
            ThrowIfDisposed();

            _dependencies.PublishIndex(index);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _root?.Size ?? 0);

            return ItemAt(index);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Publishes the contents dependency, because an enumeration answers from every item. The
    /// enumerator walks the tree in order and is version-checked, so a mutation under it throws
    /// <see cref="InvalidOperationException"/> rather than yielding a torn view.
    /// </remarks>
    public IEnumerator<T> GetEnumerator()
    {
        ThrowIfDisposed();

        _dependencies.PublishContents();
        return new Enumerator(this);
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

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
    public override string ToString() => $"an observable sorted view of {typeof(T).Name}";

    /// <summary>Checks the tree's invariants, for the tests that protect it.</summary>
    /// <exception cref="InvalidOperationException">A size, a height, the in-order sequence or the
    /// identity map does not describe the tree it is supposed to.</exception>
    /// <remarks>
    /// Not part of the surface, and unreachable from an application. It exists because a hand-written
    /// balanced tree is the part of this feature a passing example-based test does not prove: the
    /// randomised test calls this after every operation, so a rotation that forgot a size or a map that
    /// kept a stale number fails at the operation that caused it rather than somewhere later.
    /// </remarks>
    internal void ValidateTree()
    {
        var items = new List<T>();
        var sequences = new List<long>();

        Collect(_root, items, sequences);

        if (items.Count != _sequences.Count)
        {
            throw new InvalidOperationException(
                $"The tree holds {items.Count} items and the identity map holds {_sequences.Count}.");
        }

        for (var index = 0; index < items.Count; index++)
        {
            if (!_sequences.TryGetValue(items[index], out var sequence) || sequence != sequences[index])
            {
                throw new InvalidOperationException($"The identity map does not agree about '{items[index]}'.");
            }

            if (index == 0)
            {
                continue;
            }

            var order = _order.Compare(items[index - 1], items[index]);

            if (order > 0 || (order == 0 && sequences[index - 1] >= sequences[index]))
            {
                throw new InvalidOperationException(
                    $"The in-order walk is not ordered at {index}: '{items[index - 1]}' then '{items[index]}'.");
            }
        }

        var (count, height) = CheckNode(_root);
        var bound = 2 * CeilingLog2(count + 1);

        if (height > bound)
        {
            throw new InvalidOperationException($"The tree's height {height} is outside the bound {bound} for {count}.");
        }
    }

    /// <inheritdoc/>
    int IObservableChangeTarget<ObservableListChange<T>, ObservableListChangedEventArgs<T>>.Size =>
        _root?.Size ?? 0;

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

    /// <summary>Translates what the source delivered into the view's record stream, and delivers it once.</summary>
    /// <remarks>
    /// <para>
    /// Every record is applied to the tree before it is recorded, because a handler that reads the view
    /// must already see what the records it is being handed describe - the same order the source set
    /// itself mutates in. The records are translated one at a time, against the tree as the previous one
    /// left it: an insertion's rank is where the item belongs among the items that are already there,
    /// which a translate-then-apply-in-a-batch would get wrong for every source event carrying more than
    /// one change.
    /// </para>
    /// <para>
    /// The loop runs inside one batch of this view's own. The source's delivery is already the unit an
    /// observer sees, so a batch of three source changes has to leave here as one event with three
    /// translated records rather than three events. That batch carries the package default cap and knows
    /// nothing about a source whose owner raised its own: an event longer than the default collapses here
    /// to one untracked record, while the tree is already complete, which is the same
    /// fidelity-for-boundedness trade a single list makes, one level down.
    /// </para>
    /// </remarks>
    private void OnInnerChanged(ObservableSetChangedEventArgs<T> args)
    {
        if (_batch.IsDisposed)
        {
            // The source hands its handlers out as a snapshot, so one taken before our unsubscribe can
            // still call us. Nothing may touch the tree after the view is dead.
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
            // Delivers the records that were applied, even when something threw part way: what is
            // pending always describes what the tree now holds.
            _batch.EndChangeBatch();
        }
    }

    private void Translate(in ObservableSetChange<T> change)
    {
        switch (change.ChangeType)
        {
            case ObservableSetChangeType.ItemAdded:
            {
                var item = change.Item!;

                // A set cannot hold two items its own comparer calls equal, so an item that already has
                // a number means the source and the view disagree. Rebuild rather than give one item
                // two nodes.
                if (!_sequences.TryAdd(item, _nextSequence))
                {
                    Resync();
                    break;
                }

                var sequence = _nextSequence++;
                var index = 0;

                // The rank falls out of the descent, so the record names the position the tree already
                // computed instead of a second search for it.
                _root = Insert(_root, item, sequence, ref index);
                _version++;

                _batch.Record(ObservableListChange<T>.Add(index, item));
                break;
            }

            case ObservableSetChangeType.ItemRemoved:
            {
                var item = change.Item!;

                // The number is what picks this node out of its tie group; the ordering alone cannot.
                if (!_sequences.Remove(item, out var sequence))
                {
                    Resync();
                    break;
                }

                var index = 0;
                var removed = false;
                var stored = default(T)!;

                _root = Remove(_root, item, sequence, ref index, ref removed, ref stored);
                _version++;

                if (!removed)
                {
                    // The source says an item left that this view does not hold. There is nothing here
                    // that could describe the change, so rebuild and say so, exactly as an unknown
                    // record is answered.
                    Resync();
                    break;
                }

                // The instance the view held, which is the one an observer's own copy has in it.
                _batch.Record(ObservableListChange<T>.Remove(index, stored));
                break;
            }

            case ObservableSetChangeType.SetCleared:
                _root = null;
                _sequences.Clear();
                _version++;

                _batch.Record(ObservableListChange<T>.Clear());
                break;

            default:
                // UntrackedChanges, and any kind a later version adds: the change was not described, so
                // the only safe answer is to rebuild from the source and say so - the same conservative
                // answer ObservableListDependencies.Mark gives for an unknown record.
                Resync();
                break;
        }
    }

    /// <summary>Rebuilds the ordered tree from the source, and records that the change was not tracked.</summary>
    private void Resync()
    {
        Rebuild();
        _batch.Record(ObservableListChange<T>.Untracked());
    }

    /// <summary>
    /// Builds the tree from the source: gives every item it has not numbered before a number in the
    /// source's enumeration order, keeps the number of every item it still holds, sorts by
    /// (comparer, number) and assembles a perfectly balanced tree from the sorted array.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The numbering rule is the one the constructor states: an element's number is fixed the first time
    /// it arrives and is forgotten only when it leaves. So a rebuild keeps the arrival order of the ties
    /// it already knew about - an untracked change must not silently reorder two items the ordering
    /// calls equal - and numbers the items it has not seen before in the source's enumeration order,
    /// which is also how the initial build numbers what the source already held.
    /// </para>
    /// <para>
    /// The sort is by the same composite key the tree uses, so it is total and no stability is needed;
    /// assembling from the middle of the sorted array is <c>O(n)</c> and leaves the tree as balanced as
    /// it can be, so construction does not depend on the source's enumeration order for its height.
    /// </para>
    /// </remarks>
    private void Rebuild()
    {
        var previous = _sequences;
        var current = new Dictionary<T, long>(previous.Count, _identity);
        var entries = new List<(T Item, long Sequence)>(previous.Count);

        _root = null;

        foreach (var item in _inner)
        {
            var sequence = previous.TryGetValue(item, out var known) ? known : _nextSequence++;

            entries.Add((item, sequence));
            current.Add(item, sequence);
        }

        _sequences = current;

        entries.Sort((left, right) => CompareKeys(left.Item, left.Sequence, right.Item, right.Sequence));

        var items = new T[entries.Count];
        var sequences = new long[entries.Count];

        for (var index = 0; index < entries.Count; index++)
        {
            items[index] = entries[index].Item;
            sequences[index] = entries[index].Sequence;
        }

        _root = BuildBalanced(items, sequences, 0, entries.Count);
        _version++;
    }

    /// <summary>The composite key: the user's order first, and the arrival number only to separate ties.</summary>
    private int CompareKeys(T leftItem, long leftSequence, T rightItem, long rightSequence)
    {
        var order = _order.Compare(leftItem, rightItem);

        return order != 0 ? order : leftSequence.CompareTo(rightSequence);
    }

    /// <summary>Inserts into the subtree and reports the item's rank in the whole tree.</summary>
    /// <param name="node">The subtree's root, or null.</param>
    /// <param name="item">The item to insert.</param>
    /// <param name="sequence">Its arrival number, which makes the key unique.</param>
    /// <param name="index">The rank within the subtree, accumulated to the whole tree on the way back up:
    /// every right turn adds the left subtree it passed plus the node it passed.</param>
    /// <returns>The subtree's new root, rebalanced if it needed it.</returns>
    private Node Insert(Node? node, T item, long sequence, ref int index)
    {
        if (node is null)
        {
            index = 0;
            return new Node(item, sequence);
        }

        if (CompareKeys(item, sequence, node.Item, node.Sequence) < 0)
        {
            node.Left = Insert(node.Left, item, sequence, ref index);
        }
        else
        {
            node.Right = Insert(node.Right, item, sequence, ref index);
            index += Size(node.Left) + 1;
        }

        Update(node);
        return Balance(node);
    }

    /// <summary>Removes a node by its composite key and reports the rank it had.</summary>
    /// <param name="node">The subtree's root, or null.</param>
    /// <param name="item">The item to remove.</param>
    /// <param name="sequence">The arrival number the identity map gave back, which picks the right node
    /// out of a tie group.</param>
    /// <param name="index">The rank within the subtree, accumulated as <see cref="Insert"/> does.</param>
    /// <param name="removed">Whether the key was found at all; false means the source and the view
    /// disagree and the caller has to rebuild.</param>
    /// <param name="stored">The item the tree held, which is what the removal record has to name.</param>
    /// <returns>The subtree's new root, rebalanced if it needed it.</returns>
    private Node? Remove(Node? node, T item, long sequence, ref int index, ref bool removed, ref T stored)
    {
        if (node is null)
        {
            return null;
        }

        var order = CompareKeys(item, sequence, node.Item, node.Sequence);

        if (order < 0)
        {
            node.Left = Remove(node.Left, item, sequence, ref index, ref removed, ref stored);
        }
        else if (order > 0)
        {
            node.Right = Remove(node.Right, item, sequence, ref index, ref removed, ref stored);
            index += Size(node.Left) + 1;
        }
        else
        {
            index += Size(node.Left);
            stored = node.Item;
            removed = true;

            if (node.Left is null)
            {
                return node.Right;
            }

            if (node.Right is null)
            {
                return node.Left;
            }

            // The successor's key replaces this one; the in-order sequence is unchanged, so the rank of
            // everything else stays where the sizes say it is.
            node.Right = RemoveMin(node.Right, out var successor);
            node.Item = successor.Item;
            node.Sequence = successor.Sequence;
        }

        Update(node);
        return Balance(node);
    }

    /// <summary>Removes the smallest node of a subtree and hands it back.</summary>
    private static Node? RemoveMin(Node node, out Node removed)
    {
        if (node.Left is null)
        {
            removed = node;
            return node.Right;
        }

        node.Left = RemoveMin(node.Left, out removed);
        Update(node);
        return Balance(node);
    }

    /// <summary>Assembles a balanced subtree from a sorted range, in <c>O(n)</c>.</summary>
    private static Node? BuildBalanced(T[] items, long[] sequences, int start, int count)
    {
        if (count == 0)
        {
            return null;
        }

        var leftCount = count / 2;
        var middle = start + leftCount;
        var node = new Node(items[middle], sequences[middle])
        {
            Left = BuildBalanced(items, sequences, start, leftCount),
            Right = BuildBalanced(items, sequences, middle + 1, count - leftCount - 1),
        };

        Update(node);
        return node;
    }

    /// <summary>The rank lookup: <c>O(log n)</c>, because each node's left size is the index of the node itself.</summary>
    private T ItemAt(int index)
    {
        var node = _root;

        while (node is not null)
        {
            var left = Size(node.Left);

            if (index < left)
            {
                node = node.Left;
            }
            else if (index > left)
            {
                index -= left + 1;
                node = node.Right;
            }
            else
            {
                return node.Item;
            }
        }

        // Unreachable: the caller checked the range against the root's size.
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    private static Node RotateLeft(Node node)
    {
        var pivot = node.Right!;

        node.Right = pivot.Left;
        pivot.Left = node;

        Update(node);
        Update(pivot);

        return pivot;
    }

    private static Node RotateRight(Node node)
    {
        var pivot = node.Left!;

        node.Left = pivot.Right;
        pivot.Right = node;

        Update(node);
        Update(pivot);

        return pivot;
    }

    private static Node Balance(Node node)
    {
        var difference = Height(node.Left) - Height(node.Right);

        if (difference > 1)
        {
            if (Height(node.Left!.Left) < Height(node.Left.Right))
            {
                node.Left = RotateLeft(node.Left);
            }

            return RotateRight(node);
        }

        if (difference < -1)
        {
            if (Height(node.Right!.Right) < Height(node.Right.Left))
            {
                node.Right = RotateRight(node.Right);
            }

            return RotateLeft(node);
        }

        return node;
    }

    private static void Update(Node node)
    {
        node.Size = 1 + Size(node.Left) + Size(node.Right);
        node.Height = 1 + Math.Max(Height(node.Left), Height(node.Right));
    }

    private static int Size(Node? node) => node?.Size ?? 0;

    private static int Height(Node? node) => node?.Height ?? 0;

    private static void Collect(Node? node, List<T> items, List<long> sequences)
    {
        if (node is null)
        {
            return;
        }

        Collect(node.Left, items, sequences);
        items.Add(node.Item);
        sequences.Add(node.Sequence);
        Collect(node.Right, items, sequences);
    }

    private static (int Count, int Height) CheckNode(Node? node)
    {
        if (node is null)
        {
            return (0, 0);
        }

        var left = CheckNode(node.Left);
        var right = CheckNode(node.Right);
        var count = left.Count + right.Count + 1;
        var height = Math.Max(left.Height, right.Height) + 1;

        if (node.Size != count)
        {
            throw new InvalidOperationException($"A node stores a size of {node.Size} for {count} items.");
        }

        if (node.Height != height)
        {
            throw new InvalidOperationException($"A node stores a height of {node.Height} for a height of {height}.");
        }

        return (count, height);
    }

    private static int CeilingLog2(int value)
    {
        var log = 0;

        for (var power = 1; power < value; power <<= 1)
        {
            log++;
        }

        return log;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_batch.IsDisposed, this);

    /// <summary>One node of the order-statistic tree: an item, its arrival number, and its subtree's shape.</summary>
    private sealed class Node
    {
        internal Node(T item, long sequence)
        {
            Item = item;
            Sequence = sequence;
            Size = 1;
            Height = 1;
        }

        internal T Item;
        internal long Sequence;
        internal Node? Left;
        internal Node? Right;
        internal int Size;
        internal int Height;
    }

    /// <summary>The in-order walk, version-checked, with the path on an explicit stack.</summary>
    /// <remarks>
    /// The stack holds one path from the root, so it costs <c>O(log n)</c> and the whole walk costs
    /// <c>O(n)</c>. The version is taken at creation, and a mutation makes the next step throw, which is
    /// the same answer the backing list of the other views gives.
    /// </remarks>
    private sealed class Enumerator : IEnumerator<T>
    {
        private readonly ObservableSortedReadOnlySetView<T> _view;
        private readonly int _version;
        private readonly Stack<Node> _stack = new();

        private Node? _current;

        internal Enumerator(ObservableSortedReadOnlySetView<T> view)
        {
            _view = view;
            _version = view._version;

            for (var node = view._root; node is not null; node = node.Left)
            {
                _stack.Push(node);
            }
        }

        public T Current => _current is null
            ? throw new InvalidOperationException("The enumerator is not positioned on an item.")
            : _current.Item;

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (_version != _view._version)
            {
                throw new InvalidOperationException("The view changed during enumeration.");
            }

            if (_stack.Count == 0)
            {
                _current = null;
                return false;
            }

            _current = _stack.Pop();

            for (var node = _current.Right; node is not null; node = node.Left)
            {
                _stack.Push(node);
            }

            return true;
        }

        public void Reset() => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
