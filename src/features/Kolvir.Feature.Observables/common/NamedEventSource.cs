using System.Collections.Immutable;

namespace Kolvir.Observables;

/// <summary>
/// The handle for one named dependency: a key, a handler list, and the dead flag the table sets when it
/// lets the key go.
/// </summary>
/// <remarks>
/// <para>
/// An event source and nothing more, which is the whole point: a named dependency says "the thing behind
/// this key changed" and carries no value, so eviction has nothing to lose. A value stored in a dictionary
/// entry would vanish with the last listener, and worse, would be reset in the middle of a re-evaluation
/// that unhooks before it re-hooks - because that unhook is momentarily the last listener
/// (dev-docs/OBSERVABLES.md section 3.2).
/// </para>
/// <para>
/// One instance per key for as long as the key is watched. Two handles for one key would be two
/// dependencies to a set that de-duplicates by reference, so an expression that read the key twice would
/// hook it twice and unhook it once; the entry owns its handle and the table returns it, which also removes
/// an allocation from every read.
/// </para>
/// <para>
/// Eviction is what makes a handle stale, so the only safe long-lived holder is something that keeps the
/// entry alive by holding a registration token - an expression, a component. A stale handle throws rather
/// than quietly doing nothing, because a dependency that silently stops working is the one failure this
/// design refuses to allow (section 3.10).
/// </para>
/// </remarks>
internal sealed class NamedEventSource : IObservableEventSource
{
    private ImmutableArray<Action<IObservableEventSource>> _handlers = [];
    private bool _isDisposed;

    internal NamedEventSource(object key) => Key = key;

    /// <summary>What this entry is filed under in the table.</summary>
    internal object Key { get; }

    /// <summary>Whether anything is registered, which is the only thing that keeps the entry alive.</summary>
    internal bool IsWatched => !_handlers.IsEmpty;

    /// <inheritdoc/>
    public bool IsDisposed => _isDisposed;

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The table has let the key go.</exception>
    public IDisposable AddObservableEventRaisedHandler(Action<IObservableEventSource> handler)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(handler);

        _handlers = _handlers.Add(handler);
        return new HandlerRegistration(this, handler);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Tolerant on purpose, and safe on a dead handle: a teardown path disposes tokens without checking
    /// what they point at, and a token whose entry was already evicted has nothing left to do. Removing the
    /// last handler hands the entry to the table, which evicts it once whatever is in progress has finished
    /// - not here, because a handler removing itself during its own dispatch must not take the entry out
    /// from under the walk that is still calling the others (section 3.4).
    /// </remarks>
    public void RemoveObservableEventRaisedHandler(Action<IObservableEventSource> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var wasWatched = !_handlers.IsEmpty;
        _handlers = _handlers.Remove(handler);

        if (wasWatched && _handlers.IsEmpty)
        {
            NamedDependencies.Schedule(this);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Checked, unlike the value's: a run that reached this handle while the entry was alive must not go on
    /// depending on a key the table has let go, because nothing will ever raise it again.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The table has let the key go.</exception>
    public void DependUpon()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        Observable.PublishRead(this);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The table has let the key go.</exception>
    public void RaiseEvent()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        // The same dispatch a value uses, so a named event is counted against the evaluation depth and
        // refused when a chain of them loops, exactly as a write is (section 3.8).
        Observable.RaiseToHandlers(this, _handlers);
    }

    /// <summary>Takes the key out of the table: from here the handle is dead.</summary>
    internal void Evict()
    {
        _isDisposed = true;
        _handlers = [];
    }

    /// <summary>What a stale handle says it was.</summary>
    public override string ToString() => $"the named dependency for {Key}";
}
