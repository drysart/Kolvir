namespace Kolvir.Observables;

/// <summary>One change to an observable set, as a record an observer can replay.</summary>
/// <typeparam name="T">The type of the set's items.</typeparam>
/// <remarks>
/// <para>
/// A set's record needs one field and no more: which item, and whether it arrived or left. There is no
/// index, because a set has no order; no old and new item, because an item is the whole of its identity
/// here; and no value, because a set's item is its own value
/// (dev-docs/OBSERVABLE-COLLECTIONS.md sections 6.2.3 and 6.3).
/// </para>
/// <para>
/// <see cref="Item"/> is <c>default</c> for a record that carries nothing - a clear, or the untracked
/// admission - which for a value type is that type's default rather than null, so a handler switches on
/// <see cref="ChangeType"/> rather than testing the item.
/// </para>
/// </remarks>
public readonly struct ObservableSetChange<T>
{
    private ObservableSetChange(ObservableSetChangeType changeType, T? item) =>
        (ChangeType, Item) = (changeType, item);

    /// <summary>What happened.</summary>
    public ObservableSetChangeType ChangeType { get; }

    /// <summary>The item the change is about, or <c>default</c> when the change names none.</summary>
    public T? Item { get; }

    /// <summary><paramref name="item"/> was added to the set.</summary>
    /// <param name="item">The item that is now in the set.</param>
    /// <returns>A record that carries <paramref name="item"/>.</returns>
    public static ObservableSetChange<T> Add(T item) => new(ObservableSetChangeType.ItemAdded, item);

    /// <summary><paramref name="item"/> was removed from the set.</summary>
    /// <param name="item">The item that was in the set, as it was stored.</param>
    /// <returns>A record that carries <paramref name="item"/>.</returns>
    public static ObservableSetChange<T> Remove(T item) => new(ObservableSetChangeType.ItemRemoved, item);

    /// <summary>Every item was removed at once.</summary>
    /// <returns>A record that carries nothing.</returns>
    public static ObservableSetChange<T> Clear() => new(ObservableSetChangeType.SetCleared, default);

    /// <summary>Changes happened that this event does not describe, so re-read the set.</summary>
    /// <returns>A record that carries nothing.</returns>
    /// <remarks>
    /// The last record of a batch that grew past the set's
    /// <see cref="IObservableSet{T}.MaxCollectionBatchSize"/>: the collected records are dropped and
    /// this is delivered in their place, so an observer that was keeping a copy has to take the set's
    /// word for what it holds rather than its own. It is the one record that cannot be applied, which is
    /// why it is a kind of its own rather than an empty batch.
    /// </remarks>
    public static ObservableSetChange<T> Untracked() => new(ObservableSetChangeType.UntrackedChanges, default);

    /// <summary>What happened, in words, for a diagnostic.</summary>
    /// <returns>One line naming the kind and what the kind carries.</returns>
    public override string ToString() => ChangeType switch
    {
        ObservableSetChangeType.ItemAdded => $"added {Item}",
        ObservableSetChangeType.ItemRemoved => $"removed {Item}",
        ObservableSetChangeType.SetCleared => "cleared the set",
        _ => "changes happened that this event does not describe",
    };
}
