namespace Kolvir.Feature.Observables;

/// <summary>
/// The dependencies one observable dictionary publishes: its structure, its contents, and one per key
/// that anything is watching.
/// </summary>
/// <typeparam name="TKey">The type of the dictionary's keys.</typeparam>
/// <typeparam name="TValue">The type of the dictionary's values.</typeparam>
/// <remarks>
/// <para>
/// A dictionary's address is its key, so the entry a render depends on is the key it asked about - which
/// is what lets one row re-render without re-rendering the table
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 7.2.4). Its structure is the set of keys that exist, which
/// is what <c>Count</c> and <c>Keys</c> read, and it is the count source: nothing here grows or shrinks
/// without the count changing too.
/// </para>
/// <para>
/// The distinction that matters in marking is that a value changing beside a key that stayed is
/// <em>not</em> a structural change: it marks the contents and that one key, and leaves the count and the
/// keys alone (section 7.4).
/// </para>
/// </remarks>
internal sealed class ObservableDictionaryDependencies<TKey, TValue>
    where TKey : notnull
{
    private readonly ObservableShapeDependencies _shape = new("an observable dictionary");

    private readonly ObservableKeyedDependencies<TKey> _keys;

    /// <summary>Creates the dependencies for one dictionary.</summary>
    /// <param name="comparer">What decides that two keys are the same address.</param>
    internal ObservableDictionaryDependencies(IEqualityComparer<TKey>? comparer) =>
        _keys = new ObservableKeyedDependencies<TKey>(comparer, ObservableKeyedDependencyKind.Key);

    /// <summary>Publishes a read of the structure: the size, or the keys.</summary>
    internal void PublishCount() => _shape.PublishCount();

    /// <summary>Publishes a read that looked at every value.</summary>
    internal void PublishContents() => _shape.PublishContents();

    /// <summary>Publishes a read of one key, whether or not it is there.</summary>
    /// <param name="key">The key that was asked about.</param>
    internal void PublishKey(TKey key) => _keys.Publish(key);

    /// <summary>Marks what the records of one delivery invalidated.</summary>
    /// <param name="changes">The records, in the order they happened.</param>
    /// <remarks>
    /// A key arriving or leaving changes the structure, the contents and that key's answer; a value
    /// changing beside a key that stayed changes the contents and that key, and nothing structural; a
    /// clear and an untracked event could have changed anything.
    /// </remarks>
    internal void Mark(IReadOnlyList<ObservableDictionaryChange<TKey, TValue>> changes)
    {
        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index];

            switch (change.ChangeType)
            {
                case ObservableDictionaryChangeType.ItemAdded:
                case ObservableDictionaryChangeType.ItemRemoved:
                    _shape.MarkShape();
                    _keys.Mark(change.Key!);
                    break;

                case ObservableDictionaryChangeType.ItemChanged:
                    _shape.MarkContents();
                    _keys.Mark(change.Key!);
                    break;

                default:
                    // A clear empties the dictionary and an untracked event does not say what it did, so
                    // both mark every watched key.
                    _shape.MarkShape();
                    _keys.MarkAll();
                    return;
            }
        }
    }

    /// <summary>Raises everything marked, once each, and clears the marks.</summary>
    internal void Raise()
    {
        _shape.Raise();
        _keys.Raise();
    }

    /// <summary>Drops every subscription, for a dictionary that is being disposed.</summary>
    internal void Release()
    {
        _shape.Release();
        _keys.Release();
    }
}
