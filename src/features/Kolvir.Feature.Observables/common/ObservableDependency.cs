using System.Collections.Immutable;

namespace Kolvir.Observables;

/// <summary>
/// One dependency a collection publishes: an event source that carries nothing and is owned by the
/// collection rather than by a process-wide table.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as a named dependency, and for the same reason - it says "this particular question
/// has a different answer now" and holds no value, so nothing can go stale - but resolved against a
/// dictionary the collection owns. That is what lets an entry be keyed by an index or by an item with
/// the collection's own comparer, which <see cref="Observable.GetNamed"/> cannot do: a named key
/// compares by the key's own equality, and a set's or a dictionary's comparer belongs to the
/// collection.
/// </para>
/// <para>
/// Registration is a subscription, so the source is not disposable and never reports itself dead: what
/// releases it is the collection dropping the entry when the last handler goes, and a token that
/// outlives that stays safe because removal is tolerant.
/// </para>
/// </remarks>
internal sealed class ObservableDependency : IObservableEventSource
{
    private readonly string _description;
    private readonly Action? _onReleased;
    private ImmutableArray<Action<IObservableEventSource>> _handlers = [];

    /// <summary>Creates a dependency with no owner to tell about its last handler going.</summary>
    /// <param name="description">How it is named in a diagnostic.</param>
    internal ObservableDependency(string description)
        : this(description, null)
    {
    }

    /// <summary>Creates a dependency whose owner wants to know when it stops being watched.</summary>
    /// <param name="description">How it is named in a diagnostic.</param>
    /// <param name="onReleased">What to call when the last handler is removed. It must not raise.</param>
    internal ObservableDependency(string description, Action? onReleased)
    {
        _description = description;
        _onReleased = onReleased;
    }

    /// <inheritdoc/>
    /// <remarks>Always false: the collection owns this and nothing here has a lifetime of its own.</remarks>
    public bool IsDisposed => false;

    /// <summary>Whether anything is watching this, which is what a mark costs when it is empty.</summary>
    internal bool IsWatched => !_handlers.IsEmpty;

    /// <summary>Whether a standing delivery has already marked this, so a batch raises it once.</summary>
    internal bool IsMarked { get; set; }

    /// <inheritdoc/>
    public IDisposable AddObservableEventRaisedHandler(Action<IObservableEventSource> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        // Copy-on-write, so a raise walks the array it began with and this registration belongs to the
        // next one.
        _handlers = _handlers.Add(handler);
        return new HandlerRegistration(this, handler);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Tolerant, like every other source, and it tells the owner when it takes the last handler away so
    /// that an entry nobody watches can be dropped.
    /// </remarks>
    public void RemoveObservableEventRaisedHandler(Action<IObservableEventSource> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handlers = _handlers.Remove(handler);

        if (_handlers.IsEmpty)
        {
            _onReleased?.Invoke();
        }
    }

    /// <inheritdoc/>
    public void DependUpon() => Observable.PublishRead(this);

    /// <inheritdoc/>
    /// <remarks>Public because the collection that owns this raises it; a consumer reads, it does not
    /// raise.</remarks>
    public void RaiseEvent() => Observable.RaiseToHandlers(this, _handlers);

    /// <summary>Drops every handler without telling the owner, for a collection being disposed.</summary>
    internal void Clear() => _handlers = [];

    /// <summary>What was read, for a diagnostic.</summary>
    public override string ToString() => _description;
}
