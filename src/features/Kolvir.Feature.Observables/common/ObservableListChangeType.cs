namespace Kolvir.Observables;

/// <summary>
/// What an <see cref="ObservableListChange{T}"/> says happened.
/// </summary>
/// <remarks>
/// The five kinds are the whole of what can change about a list: an item appears, disappears, moves,
/// is replaced, or everything goes. Which members of the record mean something is a property of the
/// kind, and <see cref="ObservableListChange{T}"/> has the table
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 3.1).
/// </remarks>
public enum ObservableListChangeType
{
    /// <summary>An item appeared at an index, which filled <see cref="ObservableListChange{T}.NewIndex"/>.</summary>
    ItemAdded,

    /// <summary>An item was taken out of an index, which filled <see cref="ObservableListChange{T}.OldIndex"/>.</summary>
    ItemRemoved,

    /// <summary>An item moved, filling both indexes and the item on both sides.</summary>
    ItemMoved,

    /// <summary>The item at an index was replaced, filling that index and both items.</summary>
    ItemChanged,

    /// <summary>Every item was removed at once. The record carries nothing.</summary>
    ListCleared,
}
