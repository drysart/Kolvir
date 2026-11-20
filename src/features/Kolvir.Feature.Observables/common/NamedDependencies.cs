using System.Collections.Immutable;

namespace Kolvir.Observables;

/// <summary>
/// The process-wide table of named dependencies: who is listening to each key, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Keys are opaque objects compared by the default comparer, so a tuple key compares structurally with no
/// extra layer and the table never has to know which part of a name is the scope
/// (dev-docs/OBSERVABLES.md section 3.2).
/// </para>
/// <para>
/// An entry exists exactly as long as something is registered on it, and is dropped the moment the last
/// handler is removed. No deferral is needed because no caller holds an entry: a handle is a name that
/// resolves here on every operation, so the code that empties an entry and the code that fills it again are
/// not sharing an object that could go stale. A raise walks the array it started with, which is a snapshot
/// because the array is immutable - a handler that unregisters itself mid-dispatch removes its own key's
/// entry from the table without disturbing the walk, and a handler that registers again recreates it.
/// </para>
/// <para>
/// Nothing here is a cache of values, so an entry costs one dictionary slot while it is watched and nothing
/// afterwards. Single-threaded like everything else (section 3.9).
/// </para>
/// </remarks>
internal static class NamedDependencies
{
    private static Dictionary<object, Entry>? _entries;

    /// <summary>Adds a handler for <paramref name="key"/>, creating the entry if there is none.</summary>
    internal static void Subscribe(object key, Action<IObservableEventSource> handler)
    {
        _entries ??= new Dictionary<object, Entry>();

        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new Entry(new NamedEventSource(key));
            _entries.Add(key, entry);
        }

        entry.Handlers = entry.Handlers.Add(handler);
    }

    /// <summary>Removes a handler, and the entry with it when it was the last one.</summary>
    internal static void Unsubscribe(object key, Action<IObservableEventSource> handler)
    {
        if (_entries is null || !_entries.TryGetValue(key, out var entry))
        {
            return;
        }

        entry.Handlers = entry.Handlers.Remove(handler);

        if (entry.Handlers.IsEmpty)
        {
            _entries.Remove(key);
        }
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
            Observable.RaiseToHandlers(entry.Source, entry.Handlers);
        }
    }

    /// <summary>One key's handlers, and the source a dispatch reports itself as.</summary>
    /// <remarks>
    /// The source is the handle the entry was created for. Handles for one key are interchangeable - they
    /// compare equal and every operation resolves through the table - so passing the one that happens to be
    /// first is passing the key's handle, whichever instance a subscriber registered with.
    /// </remarks>
    private sealed class Entry
    {
        internal Entry(NamedEventSource source) => Source = source;

        internal NamedEventSource Source { get; }

        internal ImmutableArray<Action<IObservableEventSource>> Handlers { get; set; } = [];
    }
}
