namespace Kolvir.Observables;

/// <summary>
/// One change to an observable list, in the form an observer replays.
/// </summary>
/// <typeparam name="T">The type of the list's items.</typeparam>
/// <remarks>
/// <para>
/// A record is what a list says happened, precisely enough that an observer which never reads the list
/// can keep a copy of it in step: apply the records of an event in the order given, each against the
/// state left by the one before it, and the copy equals the list
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 3.2). Indexes are positions as of the moment of the
/// change, not as of the end of a batch.
/// </para>
/// <para>
/// Which members mean something is a property of <see cref="ChangeType"/>, so a handler switches on
/// that and then reads the members the kind promises without testing the others:
/// </para>
/// <list type="table">
/// <item>
/// <term><see cref="ObservableListChangeType.ItemAdded"/></term>
/// <description><see cref="NewIndex"/> and <see cref="NewItem"/>; the old pair is empty.</description>
/// </item>
/// <item>
/// <term><see cref="ObservableListChangeType.ItemRemoved"/></term>
/// <description><see cref="OldIndex"/> and <see cref="OldItem"/>; the new pair is empty.</description>
/// </item>
/// <item>
/// <term><see cref="ObservableListChangeType.ItemMoved"/></term>
/// <description>Both indexes, and the same item on both sides - a move is a reposition, not a removal
/// and an addition.</description>
/// </item>
/// <item>
/// <term><see cref="ObservableListChangeType.ItemChanged"/></term>
/// <description>Both indexes (the same one) and both items.</description>
/// </item>
/// <item>
/// <term><see cref="ObservableListChangeType.ListCleared"/></term>
/// <description>Nothing. The list is empty and the observer that cared about the contents already had
/// them, which is what keeps a clear one record rather than one per item.</description>
/// </item>
/// </list>
/// <para>
/// An index shift caused by a change is implied rather than reported: an insertion at 0 does not also
/// report the items behind it as moved, because that would be O(n) records for one change and the
/// observer is applying records rather than patching items by address.
/// </para>
/// <para>
/// A struct, because an event carries an array of them and a mutation nobody is listening to should not
/// allocate one. The <c>Create*</c> methods exist for a hand-written
/// <see cref="IObservableList{T}"/> in another assembly, which needs a way to build records that its
/// observers cannot tell from these.
/// </para>
/// </remarks>
public readonly struct ObservableListChange<T>
{
    private ObservableListChange(
        ObservableListChangeType changeType,
        int? oldIndex,
        T? oldItem,
        int? newIndex,
        T? newItem)
    {
        ChangeType = changeType;
        OldIndex = oldIndex;
        OldItem = oldItem;
        NewIndex = newIndex;
        NewItem = newItem;
    }

    /// <summary>What happened.</summary>
    public ObservableListChangeType ChangeType { get; }

    /// <summary>Where the item was, for a kind that has an item in the list before the change.</summary>
    public int? OldIndex { get; }

    /// <summary>What the item was, for a kind that has an item in the list before the change.</summary>
    public T? OldItem { get; }

    /// <summary>Where the item is now, for a kind that has an item in the list after the change.</summary>
    public int? NewIndex { get; }

    /// <summary>What the item is now, for a kind that has an item in the list after the change.</summary>
    public T? NewItem { get; }

    /// <summary>An item that appeared at <paramref name="index"/>.</summary>
    /// <param name="index">Where it appeared.</param>
    /// <param name="item">What it is.</param>
    /// <returns>A record with only the new half filled in.</returns>
    public static ObservableListChange<T> Add(int index, T item) =>
        new(ObservableListChangeType.ItemAdded, null, default, index, item);

    /// <summary>An item that was taken out of <paramref name="index"/>.</summary>
    /// <param name="index">Where it was.</param>
    /// <param name="item">What it was.</param>
    /// <returns>A record with only the old half filled in.</returns>
    public static ObservableListChange<T> Remove(int index, T item) =>
        new(ObservableListChangeType.ItemRemoved, index, item, null, default);

    /// <summary>An item that moved, which fills both indexes and the item on both sides.</summary>
    /// <param name="oldIndex">Where it was.</param>
    /// <param name="newIndex">Where it is now.</param>
    /// <param name="item">The item that moved.</param>
    /// <returns>A record with both halves filled in, naming the same item twice.</returns>
    public static ObservableListChange<T> Move(int oldIndex, int newIndex, T item) =>
        new(ObservableListChangeType.ItemMoved, oldIndex, item, newIndex, item);

    /// <summary>The item at <paramref name="index"/> was replaced.</summary>
    /// <param name="index">The index, which is the same before and after.</param>
    /// <param name="oldItem">What was there.</param>
    /// <param name="newItem">What is there now.</param>
    /// <returns>A record with both halves filled in at one index.</returns>
    public static ObservableListChange<T> Change(int index, T oldItem, T newItem) =>
        new(ObservableListChangeType.ItemChanged, index, oldItem, index, newItem);

    /// <summary>Every item was removed at once.</summary>
    /// <returns>A record that carries nothing.</returns>
    public static ObservableListChange<T> Clear() =>
        new(ObservableListChangeType.ListCleared, null, default, null, default);

    /// <summary>Changes happened that this event does not describe, so re-read the list.</summary>
    /// <returns>A record that carries nothing.</returns>
    /// <remarks>
    /// The last record of a batch that grew past the list's
    /// <see cref="IObservableList{T}.MaxCollectionBatchSize"/>: the collected records are dropped and
    /// this is delivered in their place, so an observer that was keeping a copy has to take the list's
    /// word for what it holds rather than its own. It is the one record that cannot be applied, which is
    /// why it is a kind of its own rather than an empty batch.
    /// </remarks>
    public static ObservableListChange<T> Untracked() =>
        new(ObservableListChangeType.UntrackedChanges, null, default, null, default);

    /// <summary>What happened, in words, for a diagnostic.</summary>
    /// <returns>One line naming the kind and what the kind carries.</returns>
    public override string ToString() => ChangeType switch
    {
        ObservableListChangeType.ItemAdded => $"added {NewItem} at {NewIndex}",
        ObservableListChangeType.ItemRemoved => $"removed {OldItem} from {OldIndex}",
        ObservableListChangeType.ItemMoved => $"moved {NewItem} from {OldIndex} to {NewIndex}",
        ObservableListChangeType.ItemChanged => $"changed the item at {NewIndex} from {OldItem} to {NewItem}",
        ObservableListChangeType.ListCleared => "cleared the list",
        _ => "changed in ways this event did not track - re-read the list",
    };
}
