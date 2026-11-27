namespace Kolvir.Feature.Observables;

/// <summary>
/// A dictionary's entries seen as a set: what one element is, and the set algebra that identity implies.
/// </summary>
/// <typeparam name="TKey">The type of the dictionary's keys.</typeparam>
/// <typeparam name="TValue">The type of the dictionary's values.</typeparam>
/// <remarks>
/// <para>
/// <see cref="IReadOnlyObservableDictionary{TKey, TValue}"/> is an
/// <see cref="IReadOnlyObservableSet{T}"/> of its pairs, so an element is the exact pair - the key and
/// the value beside it - and not the key alone: <c>{"a" to 1}</c> and <c>{"a" to 2}</c> are two different
/// elements even though they share a key. A pair naming a key the dictionary holds with a different value
/// is therefore not in the set at all.
/// </para>
/// <para>
/// <b>One place, because the two implementations must agree.</b>
/// <see cref="ObservableDictionary{TKey, TValue}"/> and
/// <see cref="ObservableMappedReadOnlyDictionaryView{TKey, TInner, TOuter}"/> answer every set question
/// through this type - the comparer, the seven <see cref="IReadOnlySet{T}"/> members, and the translation
/// of a dictionary record into the set records an observer hears. Writing any of that out again in one
/// of them is how the two would silently drift apart.
/// </para>
/// <para>
/// <b>The comparer hashes what it compares.</b> A pair's key is compared with the dictionary's own
/// <see cref="IEqualityComparer{TKey}"/> and its value with <see cref="EqualityComparer{TValue}.Default"/>;
/// the hash combines exactly those two. A comparer whose equality included the value while its hash
/// ignored it would agree that two pairs are equal and still file them in different buckets, and every
/// set operation would then be wrong without ever throwing.
/// </para>
/// </remarks>
internal static class ObservableDictionaryPairSet<TKey, TValue>
    where TKey : notnull
{
    /// <summary>Creates the comparer the set view of a dictionary with
    /// <paramref name="keyComparer"/> uses.</summary>
    /// <param name="keyComparer">What decides that two keys are the same key, which is the dictionary's
    /// own comparer rather than a second rule invented for the set view.</param>
    /// <returns>What decides that two pairs are the same element.</returns>
    internal static IEqualityComparer<KeyValuePair<TKey, TValue>> CreateComparer(
        IEqualityComparer<TKey> keyComparer) =>
        new PairComparer(keyComparer);

    /// <summary>Whether <paramref name="pair"/> is an element of the set <paramref name="items"/> is.</summary>
    /// <param name="items">The dictionary's storage.</param>
    /// <param name="pair">The pair to look for.</param>
    /// <returns>True only when the key is present <em>and</em> its value equals the pair's value; a
    /// stale pair, whose key still holds a different value, is not an element.</returns>
    internal static bool Contains(Dictionary<TKey, TValue> items, KeyValuePair<TKey, TValue> pair) =>
        items.TryGetValue(pair.Key, out var value) && EqualityComparer<TValue>.Default.Equals(value, pair.Value);

    /// <summary>Whether the set and <paramref name="other"/> hold the same pairs.</summary>
    /// <param name="items">The dictionary's storage.</param>
    /// <param name="comparer">What decides that two pairs are the same element.</param>
    /// <param name="other">What to compare with.</param>
    /// <returns>True when both sides hold the same distinct pairs under
    /// <paramref name="comparer"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    internal static bool SetEquals(
        Dictionary<TKey, TValue> items,
        IEqualityComparer<KeyValuePair<TKey, TValue>> comparer,
        IEnumerable<KeyValuePair<TKey, TValue>> other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var candidates = new HashSet<KeyValuePair<TKey, TValue>>(other, comparer);

        if (candidates.Count != items.Count)
        {
            return false;
        }

        foreach (var pair in items)
        {
            if (!candidates.Contains(pair))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether every pair of the set is in <paramref name="other"/>.</summary>
    /// <param name="items">The dictionary's storage.</param>
    /// <param name="comparer">What decides that two pairs are the same element.</param>
    /// <param name="other">What to compare with.</param>
    /// <returns>True when nothing here is missing from <paramref name="other"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    internal static bool IsSubsetOf(
        Dictionary<TKey, TValue> items,
        IEqualityComparer<KeyValuePair<TKey, TValue>> comparer,
        IEnumerable<KeyValuePair<TKey, TValue>> other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var candidates = new HashSet<KeyValuePair<TKey, TValue>>(other, comparer);

        if (items.Count > candidates.Count)
        {
            return false;
        }

        foreach (var pair in items)
        {
            if (!candidates.Contains(pair))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether every pair of <paramref name="other"/> is in the set.</summary>
    /// <param name="items">The dictionary's storage.</param>
    /// <param name="other">What to compare with.</param>
    /// <returns>True when nothing in <paramref name="other"/> is missing from here.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <remarks>Duplicates in <paramref name="other"/> are harmless: each one is asked about in turn and
    /// the answer does not depend on how often it was asked.</remarks>
    internal static bool IsSupersetOf(
        Dictionary<TKey, TValue> items,
        IEnumerable<KeyValuePair<TKey, TValue>> other)
    {
        ArgumentNullException.ThrowIfNull(other);

        foreach (var pair in other)
        {
            if (!Contains(items, pair))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether the set is a subset of <paramref name="other"/> and is smaller than it.</summary>
    /// <param name="items">The dictionary's storage.</param>
    /// <param name="comparer">What decides that two pairs are the same element.</param>
    /// <param name="other">What to compare with.</param>
    /// <returns>True when every pair here is in <paramref name="other"/> and
    /// <paramref name="other"/> holds a pair this set does not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    internal static bool IsProperSubsetOf(
        Dictionary<TKey, TValue> items,
        IEqualityComparer<KeyValuePair<TKey, TValue>> comparer,
        IEnumerable<KeyValuePair<TKey, TValue>> other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var candidates = new HashSet<KeyValuePair<TKey, TValue>>(other, comparer);

        if (items.Count >= candidates.Count)
        {
            return false;
        }

        foreach (var pair in items)
        {
            if (!candidates.Contains(pair))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether the set is a superset of <paramref name="other"/> and is larger than it.</summary>
    /// <param name="items">The dictionary's storage.</param>
    /// <param name="comparer">What decides that two pairs are the same element.</param>
    /// <param name="other">What to compare with.</param>
    /// <returns>True when every pair in <paramref name="other"/> is here and this set holds a pair
    /// <paramref name="other"/> does not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    internal static bool IsProperSupersetOf(
        Dictionary<TKey, TValue> items,
        IEqualityComparer<KeyValuePair<TKey, TValue>> comparer,
        IEnumerable<KeyValuePair<TKey, TValue>> other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var candidates = new HashSet<KeyValuePair<TKey, TValue>>(other, comparer);

        if (items.Count <= candidates.Count)
        {
            return false;
        }

        foreach (var pair in candidates)
        {
            if (!Contains(items, pair))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether the set and <paramref name="other"/> have a pair in common.</summary>
    /// <param name="items">The dictionary's storage.</param>
    /// <param name="other">What to compare with.</param>
    /// <returns>True when at least one pair of <paramref name="other"/> is here.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    internal static bool Overlaps(
        Dictionary<TKey, TValue> items,
        IEnumerable<KeyValuePair<TKey, TValue>> other)
    {
        ArgumentNullException.ThrowIfNull(other);

        foreach (var pair in other)
        {
            if (Contains(items, pair))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Translates one delivered dictionary event into the set event an observer hears.</summary>
    /// <param name="batch">The set view's own batch, which is what delivers the result.</param>
    /// <param name="changes">The dictionary records one event carried, in the order they happened.</param>
    /// <remarks>
    /// <para>
    /// The translation is total, one dictionary change at a time:
    /// </para>
    /// <list type="table">
    /// <item><term><see cref="ObservableDictionaryChangeType.ItemAdded"/></term>
    /// <description><see cref="ObservableSetChangeType.ItemAdded"/> with the new pair.</description></item>
    /// <item><term><see cref="ObservableDictionaryChangeType.ItemRemoved"/></term>
    /// <description><see cref="ObservableSetChangeType.ItemRemoved"/> with the old pair.</description></item>
    /// <item><term><see cref="ObservableDictionaryChangeType.ItemChanged"/></term>
    /// <description><see cref="ObservableSetChangeType.ItemRemoved"/> with the old pair, then
    /// <see cref="ObservableSetChangeType.ItemAdded"/> with the new one.</description></item>
    /// <item><term><see cref="ObservableDictionaryChangeType.DictionaryCleared"/></term>
    /// <description><see cref="ObservableSetChangeType.SetCleared"/>.</description></item>
    /// <item><term><see cref="ObservableDictionaryChangeType.UntrackedChanges"/></term>
    /// <description><see cref="ObservableSetChangeType.UntrackedChanges"/>.</description></item>
    /// </list>
    /// <para>
    /// <b>A replacement is two changes in one event.</b> The old pair leaving and the new one arriving are
    /// two elements of the set, not one element that moved, and a set observer replaying records has to
    /// see them in that order. They are recorded together and delivered together, because an observer
    /// that saw only half of a replacement would hold a view the dictionary never had - and the records
    /// are opened as one batch here so that a caller already inside a batch only nests rather than
    /// delivers early.
    /// </para>
    /// </remarks>
    internal static void RecordChanges(
        ObservableChangeBatch<
            ObservableSetChange<KeyValuePair<TKey, TValue>>,
            ObservableSetChangedEventArgs<KeyValuePair<TKey, TValue>>> batch,
        IReadOnlyList<ObservableDictionaryChange<TKey, TValue>> changes)
    {
        // The one empty event a disposal delivers: the set side delivers its own, from its own batch.
        if (changes.Count == 0)
        {
            return;
        }

        batch.BeginChangeBatch();

        try
        {
            for (var index = 0; index < changes.Count; index++)
            {
                RecordOne(batch, changes[index]);
            }
        }
        finally
        {
            batch.EndChangeBatch();
        }
    }

    private static void RecordOne(
        ObservableChangeBatch<
            ObservableSetChange<KeyValuePair<TKey, TValue>>,
            ObservableSetChangedEventArgs<KeyValuePair<TKey, TValue>>> batch,
        in ObservableDictionaryChange<TKey, TValue> change)
    {
        switch (change.ChangeType)
        {
            case ObservableDictionaryChangeType.ItemAdded:
                batch.Record(ObservableSetChange<KeyValuePair<TKey, TValue>>.Add(
                    new(change.Key!, change.NewValue!)));
                break;

            case ObservableDictionaryChangeType.ItemRemoved:
                batch.Record(ObservableSetChange<KeyValuePair<TKey, TValue>>.Remove(
                    new(change.Key!, change.OldValue!)));
                break;

            case ObservableDictionaryChangeType.ItemChanged:
                batch.Record(ObservableSetChange<KeyValuePair<TKey, TValue>>.Remove(
                    new(change.Key!, change.OldValue!)));
                batch.Record(ObservableSetChange<KeyValuePair<TKey, TValue>>.Add(
                    new(change.Key!, change.NewValue!)));
                break;

            case ObservableDictionaryChangeType.DictionaryCleared:
                batch.Record(ObservableSetChange<KeyValuePair<TKey, TValue>>.Clear());
                break;

            default:
                // UntrackedChanges, and any kind a later version adds: the record says the change was
                // not described, and the set version of that is the same admission.
                batch.Record(ObservableSetChange<KeyValuePair<TKey, TValue>>.Untracked());
                break;
        }
    }

    /// <summary>The identity of one element: the key under the dictionary's comparer, the value under
    /// equality.</summary>
    private sealed class PairComparer(IEqualityComparer<TKey> keyComparer)
        : IEqualityComparer<KeyValuePair<TKey, TValue>>
    {
        /// <inheritdoc/>
        public bool Equals(KeyValuePair<TKey, TValue> x, KeyValuePair<TKey, TValue> y) =>
            keyComparer.Equals(x.Key, y.Key) && EqualityComparer<TValue>.Default.Equals(x.Value, y.Value);

        /// <inheritdoc/>
        /// <remarks>
        /// Combines the key's hash under the dictionary's comparer with the value's, because equality
        /// compares both. A value type's null check is folded into the same expression rather than
        /// branching on the type: <see cref="EqualityComparer{TValue}.Default"/> answers for null
        /// values too.
        /// </remarks>
        public int GetHashCode(KeyValuePair<TKey, TValue> obj) =>
            HashCode.Combine(
                keyComparer.GetHashCode(obj.Key),
                obj.Value is null ? 0 : EqualityComparer<TValue>.Default.GetHashCode(obj.Value));
    }
}
