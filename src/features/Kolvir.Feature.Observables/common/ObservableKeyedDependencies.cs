namespace Kolvir.Feature.Observables;

/// <summary>
/// The watched addresses of one collection: an entry per key, dropped when its last handler goes.
/// </summary>
/// <typeparam name="TKey">What addresses a read: a list's index, a set's item, a dictionary's key.</typeparam>
/// <remarks>
/// <para>
/// The address is the dependency that earns the work - it is what lets one row re-render without
/// re-rendering the collection - so it is the one that gets an entry of its own. An entry is created by
/// the first read of that address inside a run and dropped when its last handler goes, so the table
/// holds the rows on screen rather than every address ever read. Dropped is not gone: a source a caller
/// kept is put back by the entry itself as soon as something watches it again, and one that finds the
/// key taken adopts the entry that took it (3.6.1).
/// </para>
/// <para>
/// The comparer belongs to the collection rather than to the key - a set's items are its addresses, and
/// what counts as the same item is the set's own rule - so it is a parameter here and not
/// <see cref="Observable.GetNamed"/>, which compares keys by the key's equality
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 6.2.1). A null address cannot be watched, which is what
/// <c>notnull</c> says: an entry table is a dictionary, and the address is its key.
/// </para>
/// <para>
/// Marking is the caller's rule and lives with the collection; this type only owns the table, the flag
/// that makes a delivery raise an entry once, and the raise itself.
/// </para>
/// </remarks>
internal sealed class ObservableKeyedDependencies<TKey>
    where TKey : notnull
{
    private readonly Dictionary<TKey, ObservableDependency> _entries;
    private readonly ObservableKeyedDependencyKind _kind;
    private readonly List<ObservableDependency> _marked = [];

    /// <summary>Creates an empty table for one collection.</summary>
    /// <param name="comparer">What decides that two keys are the same address.</param>
    /// <param name="kind">Which collection this is, which is how an entry names itself in a diagnostic.
    /// An entry keeps its key rather than a name built from it, because the name is only ever needed to
    /// describe a refusal.</param>
    internal ObservableKeyedDependencies(IEqualityComparer<TKey>? comparer, ObservableKeyedDependencyKind kind)
    {
        _entries = new Dictionary<TKey, ObservableDependency>(comparer ?? EqualityComparer<TKey>.Default);
        _kind = kind;
    }

    /// <summary>The entries that anything is watching, which is what a mark walks.</summary>
    internal IReadOnlyDictionary<TKey, ObservableDependency> Entries => _entries;

    /// <summary>Publishes a read of one address.</summary>
    /// <param name="key">The address that was read, whether or not it currently answers.</param>
    /// <remarks>
    /// A read outside any run allocates nothing: the entry is only created when a collection scope is
    /// active, which is the same test <see cref="Observable.PublishRead"/> makes before it does
    /// anything.
    /// </remarks>
    internal void Publish(TKey key)
    {
        if (!ReadScope.IsActive)
        {
            return;
        }

        Observable.PublishRead(GetEntry(key));
    }

    /// <summary>Marks one address, if anything is watching it.</summary>
    internal void Mark(TKey key)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            Mark(entry);
        }
    }

    /// <summary>Marks an entry the caller found by walking <see cref="Entries"/>.</summary>
    internal void Mark(ObservableDependency entry)
    {
        if (!entry.IsMarked)
        {
            entry.IsMarked = true;
            _marked.Add(entry);
        }
    }

    /// <summary>Marks every watched address, for a change that could have touched any of them.</summary>
    internal void MarkAll()
    {
        foreach (var entry in _entries.Values)
        {
            Mark(entry);
        }
    }

    /// <summary>Raises everything marked, once each, and clears the marks.</summary>
    /// <remarks>
    /// Walked by index rather than by enumerator: a handler is free to dispose the collection or to drop
    /// an entry, and an enumerator over a list that changed under it throws.
    /// </remarks>
    internal void Raise()
    {
        try
        {
            for (var index = 0; index < _marked.Count; index++)
            {
                _marked[index].RaiseEvent();
            }
        }
        finally
        {
            // The flag is what makes one delivery raise an entry once however many of its records
            // touched it, so it is cleared with the delivery - a flag left set would silence that entry
            // for every delivery after this one.
            for (var index = 0; index < _marked.Count; index++)
            {
                _marked[index].IsMarked = false;
            }

            _marked.Clear();
        }
    }

    /// <summary>Drops every entry and subscription, for a collection that is being disposed.</summary>
    internal void Release()
    {
        foreach (var entry in _entries.Values)
        {
            entry.IsMarked = false;
            entry.Clear();
        }

        _entries.Clear();
        _marked.Clear();
    }

    private ObservableDependency GetEntry(TKey key)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            return entry;
        }

        // The entry is made first, because the callbacks need to name it, and it registers itself the
        // moment something watches it - which is what lets a source a caller kept be watched again after
        // this table dropped it.
        ObservableDependency? created = null;

        created = new ObservableKeyedDependency<TKey>(
            key,
            _kind,
            onWatched: () => Acquire(key, created!),
            onReleased: () => ReleaseEntry(key, created!));

        return created;
    }

    private void Acquire(TKey key, ObservableDependency entry)
    {
        if (_entries.TryGetValue(key, out var current) && !ReferenceEquals(current, entry))
        {
            // A read made a second entry for this address while the first was dropped but still held.
            // Both have readers and only one can hold the key, so the one coming back takes it and
            // adopts the other rather than leaving it subscribed to an entry nothing marks.
            entry.Adopt(current);
        }

        _entries[key] = entry;
    }

    private void ReleaseEntry(TKey key, ObservableDependency entry)
    {
        // By identity as well as by key: an entry that was already replaced must not take its
        // successor's address away when its last handler goes.
        if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
        {
            _entries.Remove(key);
        }
    }
}
