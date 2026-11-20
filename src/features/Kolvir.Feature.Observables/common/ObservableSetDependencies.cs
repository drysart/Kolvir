namespace Kolvir.Observables;

/// <summary>
/// The dependencies one observable set publishes: its count, its contents, and one per item that
/// anything is watching.
/// </summary>
/// <typeparam name="T">The type of the set's items.</typeparam>
/// <remarks>
/// <para>
/// The address of a set is the item itself: a rendered toggle asks <c>Contains(tag)</c> about its own
/// tag, so the entry for that tag is what has to be woken when the tag arrives or leaves, and a change to
/// one tag must not re-render the others (dev-docs/OBSERVABLE-COLLECTIONS.md section 6.2.4).
/// </para>
/// <para>
/// The entries are an <see cref="ObservableKeyedDependencies{TKey}"/> keyed with the set's own comparer,
/// because what counts as the same item is the set's rule and not the item's - which is also why
/// <see cref="Observable.GetNamed"/> cannot be used for this.
/// </para>
/// <para>
/// There is no address to shift and no order to move in, so the marking rule is the whole of the per-set
/// work: an add or a removal marks the shape and one item, and a clear or an untracked event marks
/// everything.
/// </para>
/// </remarks>
internal sealed class ObservableSetDependencies<T>
    where T : notnull
{
    private readonly ObservableShapeDependencies _shape = new("an observable set");

    private readonly ObservableKeyedDependencies<T> _items;

    /// <summary>Creates the dependencies for one set.</summary>
    /// <param name="comparer">What decides that two items are the same address.</param>
    internal ObservableSetDependencies(IEqualityComparer<T>? comparer) =>
        _items = new ObservableKeyedDependencies<T>(comparer, item => $"item {item} of an observable set");

    /// <summary>Publishes a read of the size.</summary>
    internal void PublishCount() => _shape.PublishCount();

    /// <summary>Publishes a read that looked at every item.</summary>
    internal void PublishContents() => _shape.PublishContents();

    /// <summary>Publishes a read asking whether one item is present.</summary>
    /// <param name="item">The item that was asked about, whether or not it is there.</param>
    internal void PublishItem(T item) => _items.Publish(item);

    /// <summary>Marks what the records of one delivery invalidated.</summary>
    /// <param name="changes">The records, in the order they happened.</param>
    /// <remarks>
    /// An add or a removal changes the size and the contents and the answer for that one item; a clear and
    /// an untracked event could have changed anything.
    /// </remarks>
    internal void Mark(IReadOnlyList<ObservableSetChange<T>> changes)
    {
        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index];

            switch (change.ChangeType)
            {
                case ObservableSetChangeType.ItemAdded:
                case ObservableSetChangeType.ItemRemoved:
                    _shape.MarkShape();
                    _items.Mark(change.Item!);
                    break;

                default:
                    // A clear empties the set and an untracked event does not say what it did, so both
                    // mark every watched item.
                    _shape.MarkShape();
                    _items.MarkAll();
                    return;
            }
        }
    }

    /// <summary>Raises everything marked, once each, and clears the marks.</summary>
    internal void Raise()
    {
        _shape.Raise();
        _items.Raise();
    }

    /// <summary>Drops every subscription, for a set that is being disposed.</summary>
    internal void Release()
    {
        _shape.Release();
        _items.Release();
    }
}
