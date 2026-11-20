namespace Kolvir.Observables;

/// <summary>
/// The dependencies one observable list publishes: its count, its contents, and one per index that
/// anything is watching.
/// </summary>
/// <typeparam name="T">The type of the list's items.</typeparam>
/// <remarks>
/// <para>
/// A read asks one of three questions and gets the source for that question
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 3.6). The count and the contents are
/// <see cref="ObservableShapeDependencies"/>; the indexes are an
/// <see cref="ObservableKeyedDependencies{TKey}"/> keyed by the index, which is what makes them the same
/// machinery the set and the dictionary use.
/// </para>
/// <para>
/// What is left here is the list's own part: which record invalidates which answer. An insert marks
/// every watched index at or past it, a move marks the span it crosses, a write marks one, and a clear
/// marks all of them - and each of those is bounded by the size the list had at that moment, so an
/// insertion at 7 does not wake a reader watching 12 (section 3.6.2).
/// </para>
/// <para>
/// Single-threaded, like everything else here: marking and raising both run on the thread that changed
/// the list.
/// </para>
/// </remarks>
internal sealed class ObservableListDependencies<T>
{
    private readonly ObservableShapeDependencies _shape = new("an observable list");

    private readonly ObservableKeyedDependencies<int> _indexes = new(
        null,
        index => $"index {index} of an observable list");

    /// <summary>Publishes a read of the size.</summary>
    internal void PublishCount() => _shape.PublishCount();

    /// <summary>Publishes a read that looked at every item.</summary>
    internal void PublishContents() => _shape.PublishContents();

    /// <summary>Publishes a read of one index.</summary>
    /// <param name="index">The index that was read, in range or not.</param>
    /// <remarks>
    /// Called before the read decides whether the index is in range, so a read that throws is still a
    /// read: an expression <c>list[7]</c> over three items has to be re-run when the list grows to
    /// eight, and it can only be subscribed to index 7 if it published on the way to throwing.
    /// </remarks>
    internal void PublishIndex(int index) => _indexes.Publish(index);

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
    internal void Mark(IReadOnlyList<ObservableListChange<T>> records, int count)
    {
        var everything = false;

        for (var index = 0; index < records.Count; index++)
        {
            everything |= records[index].ChangeType is ObservableListChangeType.ListCleared
                or ObservableListChangeType.UntrackedChanges;
        }

        if (everything)
        {
            // A clear destroys the count the records began with, and an untracked event does not say what
            // it did at all - so both mark every watched index, including, conservatively, the few that
            // were already past the end.
            _shape.MarkShape();
            _indexes.MarkAll();
            return;
        }

        // The size before the first record of this event.
        for (var index = 0; index < records.Count; index++)
        {
            count += records[index].ChangeType switch
            {
                ObservableListChangeType.ItemAdded => -1,
                ObservableListChangeType.ItemRemoved => 1,
                _ => 0,
            };
        }

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];

            switch (record.ChangeType)
            {
                case ObservableListChangeType.ItemAdded:
                    count++;
                    _shape.MarkShape();
                    MarkIndexesFrom(record.NewIndex!.Value, count);
                    break;

                case ObservableListChangeType.ItemRemoved:
                    _shape.MarkShape();
                    MarkIndexesFrom(record.OldIndex!.Value, count);
                    count--;
                    break;

                case ObservableListChangeType.ItemMoved:
                    // The size is unchanged and the items are unchanged; only where they sit is not,
                    // so the contents are stale and the indexes the move spans may answer differently.
                    _shape.MarkContents();
                    MarkIndexesBetween(record.OldIndex!.Value, record.NewIndex!.Value);
                    break;

                case ObservableListChangeType.ItemChanged:
                    _shape.MarkContents();
                    _indexes.Mark(record.NewIndex!.Value);
                    break;

                default:
                    // Unreachable - a clear was handled above - but written out rather than assumed, so
                    // that adding a change type later fails a test rather than a reader.
                    _shape.MarkShape();
                    _indexes.MarkAll();
                    break;
            }
        }
    }

    /// <summary>Raises everything marked, once each, and clears the marks.</summary>
    internal void Raise()
    {
        _shape.Raise();
        _indexes.Raise();
    }

    /// <summary>Drops every subscription, for a list that is being disposed.</summary>
    internal void Release()
    {
        _shape.Release();
        _indexes.Release();
    }

    private void MarkIndexesFrom(int index, int count)
    {
        // Everything at or past an insertion or removal point, and still inside the list, may now
        // answer with a different item; everything before it answers with the same one, and everything
        // past the end answered with a throw before and answers with one after. The table holds the
        // watched indexes only.
        foreach (var (watched, entry) in _indexes.Entries)
        {
            if (watched >= index && watched < count)
            {
                _indexes.Mark(entry);
            }
        }
    }

    private void MarkIndexesBetween(int first, int second)
    {
        var low = Math.Min(first, second);
        var high = Math.Max(first, second);

        foreach (var (watched, entry) in _indexes.Entries)
        {
            if (watched >= low && watched <= high)
            {
                _indexes.Mark(entry);
            }
        }
    }
}
