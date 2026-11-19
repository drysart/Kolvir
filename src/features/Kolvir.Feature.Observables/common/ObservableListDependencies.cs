using System.Collections.Immutable;

namespace Kolvir.Observables;

/// <summary>
/// The dependencies one observable list publishes: its count, its contents, and one per index that
/// anything is watching.
/// </summary>
/// <typeparam name="T">The type of the list's items.</typeparam>
/// <remarks>
/// <para>
/// A read asks one of three questions and gets the source for that question
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 3.6). The count and the contents are fields, created
/// with the list and never dropped. An index entry is created by the first read of that index inside a
/// run and dropped when its last handler goes, so the table holds the rows on screen rather than every
/// index ever read - the same rule the named-dependency table uses, applied to a key space the list
/// owns.
/// </para>
/// <para>
/// A change marks what it invalidated and the delivery raises each marked source once, so a batch of a
/// hundred records touching one row wakes one reader. Marking is a pass over the watched entries, not
/// over the list: an insert marks the entries whose index is at or past it, a move marks the entries it
/// spans, an item write marks one, and a clear marks all of them. Which record marks what is the table
/// in section 3.6.2, and it is the whole of what the tests can disagree with.
/// </para>
/// <para>
/// Single-threaded, like everything else here: marking and raising both run on the thread that changed
/// the list.
/// </para>
/// </remarks>
internal sealed class ObservableListDependencies<T>
{
    private readonly ObservableDependency _count;
    private readonly ObservableDependency _contents;
    private readonly Dictionary<int, ObservableDependency> _indexes = [];
    private readonly List<ObservableDependency> _marked = [];

    private bool _countMarked;
    private bool _contentsMarked;

    internal ObservableListDependencies()
    {
        _count = new ObservableDependency("the count of an observable list");
        _contents = new ObservableDependency("the contents of an observable list");
    }

    /// <summary>Publishes a read of the size.</summary>
    internal void PublishCount() => Observable.PublishRead(_count);

    /// <summary>Publishes a read that looked at every item.</summary>
    internal void PublishContents() => Observable.PublishRead(_contents);

    /// <summary>Publishes a read of one index.</summary>
    /// <param name="index">The index that was read, in range or not.</param>
    /// <remarks>
    /// Called before the read decides whether the index is in range, so a read that throws is still a
    /// read: an expression <c>list[7]</c> over three items has to be re-run when the list grows to
    /// eight, and it can only be subscribed to index 7 if it published on the way to throwing.
    /// <para>
    /// A read outside any run allocates nothing. The entry is only created when a collection scope is
    /// active, which is the same test <see cref="Observable.PublishRead"/> makes before it does
    /// anything - asked here so that a cold read of a model does not grow the table.
    /// </para>
    /// </remarks>
    internal void PublishIndex(int index)
    {
        if (!ReadScope.IsActive)
        {
            return;
        }

        Observable.PublishRead(GetIndexDependency(index));
    }

    /// <summary>Marks what the records of one delivery invalidated.</summary>
    /// <param name="records">The records, in the order they happened.</param>
    /// <param name="count">The list's size once they had all happened, which is the size it has when
    /// this is called - the delivery copies its records before running any handler, so nothing a
    /// handler did is in here.</param>
    /// <remarks>
    /// The size matters to an index read that was already past the end: an insertion at 7 does not make
    /// index 12 answerable, so a reader watching 12 must not be woken for it. Walking the records
    /// backwards from <paramref name="count"/> recovers the size each one happened at, which is what
    /// puts a bound on the indexes a change can reach.
    /// </remarks>
    internal void Mark(ReadOnlySpan<ObservableListChange<T>> records, int count)
    {
        var cleared = false;

        foreach (var record in records)
        {
            cleared |= record.ChangeType == ObservableListChangeType.ListCleared;
        }

        if (cleared)
        {
            // A clear destroys the count the records began with, and everything in range changed, so
            // this is the one case that marks every watched index - including, conservatively, the few
            // that were already past the end.
            MarkShape();
            MarkEveryIndex();
            return;
        }

        // The size before the first record of this event.
        foreach (var record in records)
        {
            count += record.ChangeType switch
            {
                ObservableListChangeType.ItemAdded => -1,
                ObservableListChangeType.ItemRemoved => 1,
                _ => 0,
            };
        }

        foreach (var record in records)
        {
            switch (record.ChangeType)
            {
                case ObservableListChangeType.ItemAdded:
                    count++;
                    MarkShape();
                    MarkIndexesFrom(record.NewIndex!.Value, count);
                    break;

                case ObservableListChangeType.ItemRemoved:
                    MarkShape();
                    MarkIndexesFrom(record.OldIndex!.Value, count);
                    count--;
                    break;

                case ObservableListChangeType.ItemMoved:
                    // The size is unchanged and the items are unchanged; only where they sit is not,
                    // so the contents are stale and the indexes the move spans may answer differently.
                    _contentsMarked = true;
                    MarkIndexesBetween(record.OldIndex!.Value, record.NewIndex!.Value);
                    break;

                case ObservableListChangeType.ItemChanged:
                    _contentsMarked = true;
                    MarkIndex(record.NewIndex!.Value);
                    break;

                default:
                    // Unreachable - a clear was handled above - but written out rather than assumed, so
                    // that adding a change type later fails a test rather than a reader.
                    MarkShape();
                    MarkEveryIndex();
                    break;
            }
        }
    }

    /// <summary>Raises everything marked, once each, and clears the marks.</summary>
    /// <remarks>
    /// Handlers run here and are free to read the list, which publishes dependencies of its own - those
    /// are new subscriptions and are not raised for the change that woke them, exactly as a value that
    /// starts being read in a handler is not re-raised for the write that caused it.
    /// </remarks>
    internal void Raise()
    {
        try
        {
            if (_countMarked)
            {
                _count.RaiseEvent();
            }

            if (_contentsMarked)
            {
                _contents.RaiseEvent();
            }

            foreach (var dependency in _marked)
            {
                dependency.RaiseEvent();
            }
        }
        finally
        {
            _countMarked = false;
            _contentsMarked = false;

            // The flags are what makes one delivery raise an entry once however many of its records
            // touched it, so they are per delivery and have to be cleared with them - a flag left set
            // would silence that entry for every delivery after this one.
            foreach (var dependency in _marked)
            {
                dependency.IsMarked = false;
            }

            _marked.Clear();
        }
    }

    /// <summary>Drops every subscription, for a list that is being disposed.</summary>
    /// <remarks>
    /// Silent rather than raised: the event is the list's liveness signal, and a dependency of a dead
    /// list has nothing useful to say about it - a reader that is re-run from anywhere else will read
    /// the list and get <see cref="ObjectDisposedException"/>. What this is for is the memory: the
    /// closures registered through these sources are dropped with them.
    /// </remarks>
    internal void Release()
    {
        _count.Clear();
        _contents.Clear();
        _indexes.Clear();
        _marked.Clear();

        _countMarked = false;
        _contentsMarked = false;
    }

    private ObservableDependency GetIndexDependency(int index)
    {
        if (_indexes.TryGetValue(index, out var dependency))
        {
            return dependency;
        }

        dependency = new ObservableDependency(
            $"index {index} of an observable list",
            () => _indexes.Remove(index));

        _indexes.Add(index, dependency);
        return dependency;
    }

    private void MarkShape()
    {
        // Adding a removal or a clear changes both answers: the size, and any scan of the contents.
        _countMarked = true;
        _contentsMarked = true;
    }

    private void MarkIndex(int index)
    {
        if (_indexes.TryGetValue(index, out var dependency) && !dependency.IsMarked)
        {
            dependency.IsMarked = true;
            _marked.Add(dependency);
        }
    }

    private void MarkIndexesFrom(int index, int count)
    {
        // Everything at or past an insertion or removal point, and still inside the list, may now
        // answer with a different item; everything before it answers with the same one, and everything
        // past the end answered with a throw before and answers with one after. The dictionary holds
        // the watched indexes only.
        foreach (var (watched, dependency) in _indexes)
        {
            if (watched >= index && watched < count && !dependency.IsMarked)
            {
                dependency.IsMarked = true;
                _marked.Add(dependency);
            }
        }
    }

    private void MarkIndexesBetween(int first, int second)
    {
        var low = Math.Min(first, second);
        var high = Math.Max(first, second);

        foreach (var (watched, dependency) in _indexes)
        {
            if (watched >= low && watched <= high && !dependency.IsMarked)
            {
                dependency.IsMarked = true;
                _marked.Add(dependency);
            }
        }
    }

    private void MarkEveryIndex()
    {
        foreach (var (_, dependency) in _indexes)
        {
            if (!dependency.IsMarked)
            {
                dependency.IsMarked = true;
                _marked.Add(dependency);
            }
        }
    }
}
