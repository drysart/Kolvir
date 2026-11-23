namespace Kolvir.Feature.Observables;

/// <summary>One change to an observable dictionary, as a record an observer can replay.</summary>
/// <typeparam name="TKey">The type of the dictionary's keys.</typeparam>
/// <typeparam name="TValue">The type of the dictionary's values.</typeparam>
/// <remarks>
/// <para>
/// Which key, and what the value was and is. The key is the identity here, so a record carries it
/// rather than a position, and the two value fields are filled by the kinds that have them:
/// <see cref="ObservableDictionaryChangeType.ItemAdded"/> fills the new one,
/// <see cref="ObservableDictionaryChangeType.ItemRemoved"/> the old one, and
/// <see cref="ObservableDictionaryChangeType.ItemChanged"/> both
/// (dev-docs/OBSERVABLE-COLLECTIONS.md sections 7.1 and 7.4).
/// </para>
/// <para>
/// <see cref="Key"/> and the value fields are <c>default</c> for a record that does not carry them - a
/// clear, or the untracked admission - which for a value type is that type's default rather than null,
/// so a handler switches on <see cref="ChangeType"/> and reads the fields that type promises. The key is
/// nullable for that reason: a record with no key has no key, and pretending otherwise would make a
/// read of a clear's key look valid.
/// </para>
/// </remarks>
public readonly struct ObservableDictionaryChange<TKey, TValue>
{
    private ObservableDictionaryChange(
        ObservableDictionaryChangeType changeType,
        TKey? key,
        TValue? oldValue,
        TValue? newValue) =>
        (ChangeType, Key, OldValue, NewValue) = (changeType, key, oldValue, newValue);

    /// <summary>What happened.</summary>
    public ObservableDictionaryChangeType ChangeType { get; }

    /// <summary>The key the change is about, or <c>default</c> when the change names none.</summary>
    public TKey? Key { get; }

    /// <summary>What the key's value was, for the kinds that had one before the change.</summary>
    public TValue? OldValue { get; }

    /// <summary>What the key's value is, for the kinds that have one after the change.</summary>
    public TValue? NewValue { get; }

    /// <summary><paramref name="key"/> arrived with <paramref name="value"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value it is now beside.</param>
    /// <returns>A record that carries the key and the new value.</returns>
    public static ObservableDictionaryChange<TKey, TValue> Add(TKey key, TValue value) =>
        new(ObservableDictionaryChangeType.ItemAdded, key, default, value);

    /// <summary><paramref name="key"/> left, and <paramref name="oldValue"/> was beside it.</summary>
    /// <param name="key">The key.</param>
    /// <param name="oldValue">The value the key had when it was removed.</param>
    /// <returns>A record that carries the key and the old value.</returns>
    public static ObservableDictionaryChange<TKey, TValue> Remove(TKey key, TValue oldValue) =>
        new(ObservableDictionaryChangeType.ItemRemoved, key, oldValue, default);

    /// <summary><paramref name="key"/> stayed and its value is different.</summary>
    /// <param name="key">The key.</param>
    /// <param name="oldValue">What the value was.</param>
    /// <param name="newValue">What it is now.</param>
    /// <returns>A record that carries the key and both values.</returns>
    public static ObservableDictionaryChange<TKey, TValue> Change(TKey key, TValue oldValue, TValue newValue) =>
        new(ObservableDictionaryChangeType.ItemChanged, key, oldValue, newValue);

    /// <summary>Every key was removed at once.</summary>
    /// <returns>A record that carries nothing.</returns>
    public static ObservableDictionaryChange<TKey, TValue> Clear() =>
        new(ObservableDictionaryChangeType.DictionaryCleared, default, default, default);

    /// <summary>Changes happened that this event does not describe, so re-read the dictionary.</summary>
    /// <returns>A record that carries nothing.</returns>
    /// <remarks>
    /// The last record of a batch that grew past the dictionary's
    /// <see cref="IObservableDictionary{TKey, TValue}.MaxCollectionBatchSize"/>: the collected records
    /// are dropped and this is delivered in their place, so an observer that was keeping a copy has to
    /// take the dictionary's word for what it holds rather than its own. It is the one record that
    /// cannot be applied, which is why it is a kind of its own rather than an empty batch.
    /// </remarks>
    public static ObservableDictionaryChange<TKey, TValue> Untracked() =>
        new(ObservableDictionaryChangeType.UntrackedChanges, default, default, default);

    /// <summary>What happened, in words, for a diagnostic.</summary>
    /// <returns>One line naming the kind and what the kind carries.</returns>
    public override string ToString() => ChangeType switch
    {
        ObservableDictionaryChangeType.ItemAdded => $"added {Key} with {NewValue}",
        ObservableDictionaryChangeType.ItemRemoved => $"removed {Key}, which was {OldValue}",
        ObservableDictionaryChangeType.ItemChanged => $"changed {Key} from {OldValue} to {NewValue}",
        ObservableDictionaryChangeType.DictionaryCleared => "cleared the dictionary",
        _ => "changes happened that this event does not describe",
    };
}
