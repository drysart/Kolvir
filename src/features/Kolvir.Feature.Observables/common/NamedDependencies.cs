namespace Kolvir.Observables;

/// <summary>
/// The process-wide table of named dependencies, and the eviction that keeps it as small as what is
/// watched.
/// </summary>
/// <remarks>
/// <para>
/// Keys are opaque objects compared by the default comparer, so a tuple key compares structurally with no
/// extra layer and the layer never has to know which part of a name is the scope
/// (dev-docs/OBSERVABLES.md section 3.2).
/// </para>
/// <para>
/// An entry is alive while something is registered on it. When the last handler goes it is only
/// <em>scheduled</em>: the table keeps it until nothing is in progress, because the thing that just emptied
/// it is very often the thing about to fill it again - an expression releases its whole set before it
/// re-runs, and evicting in that window would hand the same run a fresh handle every time it evaluated,
/// changing the entry's identity for no reason and adding an allocation to every recompute.
/// </para>
/// <para>
/// Nothing here is a cache of values, so an entry created and never watched costs one dictionary slot until
/// the next evacuation. Single-threaded like everything else (section 3.9).
/// </para>
/// </remarks>
internal static class NamedDependencies
{
    private static Dictionary<object, NamedEventSource>? _entries;
    private static List<NamedEventSource>? _candidates;

    /// <summary>The entry for <paramref name="key"/>, created if there is none.</summary>
    /// <remarks>
    /// The caller is asking for a handle because it is about to register on it, so there has to be an entry
    /// to register on. A caller that only wants to announce a change uses
    /// <see cref="Raise"/> instead, which never creates one.
    /// </remarks>
    internal static NamedEventSource Get(object key)
    {
        _entries ??= new Dictionary<object, NamedEventSource>();

        if (_entries.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var entry = new NamedEventSource(key);
        _entries.Add(key, entry);

        // Queued but not drained: the registration this handle exists for has not happened yet, and the
        // caller may be several statements away from it. If it never comes, the next evacuation takes the
        // entry back.
        AddCandidate(entry);

        return entry;
    }

    /// <summary>Raises the entry for <paramref name="key"/>, or does nothing when nobody watches it.</summary>
    /// <remarks>
    /// One lookup and nothing else for an unwatched key: the common shape is a model announcing that a key
    /// changed without wanting to register on it, and going through a handle for that would create an entry
    /// and an allocation per mutation.
    /// </remarks>
    internal static void Raise(object key)
    {
        if (_entries is not null && _entries.TryGetValue(key, out var entry))
        {
            entry.RaiseEvent();
        }
    }

    /// <summary>Hands <paramref name="entry"/> to the table to be evicted once it is still unwanted.</summary>
    internal static void Schedule(NamedEventSource entry)
    {
        AddCandidate(entry);
        Drain();
    }

    private static void AddCandidate(NamedEventSource entry)
    {
        _candidates ??= [];
        _candidates.Add(entry);
    }

    /// <summary>Evicts the scheduled entries that nothing has registered on, if nothing is in progress.</summary>
    /// <remarks>
    /// Called when a raise or a cold read ends, and when a run ends, so the queue is emptied at every point
    /// where the application could be about to do something else. Deferring at any other time is the whole
    /// design: a raise is dispatching, and a run is collecting, and in either one the removal that emptied
    /// an entry may be the same code that is about to subscribe again.
    /// </remarks>
    internal static void Drain()
    {
        var candidates = _candidates;

        if (candidates is null || candidates.Count == 0)
        {
            return;
        }

        if (Observable.IsEvaluating || ReadScope.IsInCollection)
        {
            return;
        }

        _candidates = null;

        foreach (var entry in candidates)
        {
            if (entry.IsWatched || entry.IsDisposed)
            {
                continue;
            }

            if (_entries is null
                || !_entries.TryGetValue(entry.Key, out var current)
                || !ReferenceEquals(current, entry))
            {
                continue;
            }

            _entries.Remove(entry.Key);
            entry.Evict();
        }
    }
}
