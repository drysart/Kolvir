using System.Collections.Immutable;

namespace Kolvir.Feature.Observables;

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
/// <para>
/// <b>A source goes both ways.</b> The collection is told when the count goes from zero to one as well
/// as from one to zero, because a caller can hold one of these - a run reports its dependencies, and
/// the interface is public - and register on it again later. The first handler therefore puts the entry
/// back in the collection's table; without that, a source that had been dropped would accept handlers
/// and never raise them, since the table is what marking reads. See
/// <see cref="Adopt"/> for the case where something else took the row meanwhile.
/// </para>
/// </remarks>
internal class ObservableDependency : IObservableEventSource
{
    private readonly string? _description;
    private readonly Action? _onWatched;
    private readonly Action? _onReleased;
    private ImmutableArray<Action<IObservableEventSource>> _handlers = [];
    private ObservableDependency? _successor;

    /// <summary>Creates a dependency, telling its owner when it starts and stops being watched.</summary>
    /// <param name="description">How it is named in a diagnostic, or null for a source that names itself
    /// some other way - an entry per address keeps its key and builds the text on demand, because there
    /// is one of those for every address a run watched and the text is only needed to describe a
    /// refusal.</param>
    /// <param name="onWatched">What to call when a handler is added to an entry that had none, which is
    /// where an owner that dropped the entry puts it back. It must not raise.</param>
    /// <param name="onReleased">What to call when the last handler is removed. It must not raise.</param>
    internal ObservableDependency(
        string? description = null,
        Action? onWatched = null,
        Action? onReleased = null)
    {
        _description = description;
        _onWatched = onWatched;
        _onReleased = onReleased;
    }

    /// <inheritdoc/>
    /// <remarks>Always false: the collection owns this and nothing here has a lifetime of its own.</remarks>
    public bool IsDisposed => false;

    /// <summary>Whether anything is watching this, which is what a mark costs when it is empty.</summary>
    internal bool IsWatched => !Target._handlers.IsEmpty;

    /// <summary>Where this entry's handlers actually live: here, or on the entry that adopted it.</summary>
    /// <remarks>
    /// Followed rather than kept flat because an adoption can itself be adopted - a row that changes
    /// hands twice - and the chain is at most as long as the number of adoptions, which is one in
    /// practice.
    /// </remarks>
    private ObservableDependency Target => _successor is { } successor ? successor.Target : this;

    /// <summary>Whether a standing delivery has already marked this, so a batch raises it once.</summary>
    internal bool IsMarked { get; set; }

    /// <inheritdoc/>
    public IDisposable AddObservableEventRaisedHandler(Action<IObservableEventSource> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        // An adopted source forwards everything to the entry that holds the handlers, so the token this
        // hands back unregisters from the entry that is actually raised.
        if (_successor is { } successor)
        {
            return successor.AddObservableEventRaisedHandler(handler);
        }

        var wasEmpty = _handlers.IsEmpty;

        // Copy-on-write, so a raise walks the array it began with and this registration belongs to the
        // next one.
        _handlers = _handlers.Add(handler);

        // Zero to one: the owner dropped this entry when the last handler went, so it has to be told to
        // take it back. Told after the add, so an owner that asks whether it is watched gets yes.
        if (wasEmpty)
        {
            _onWatched?.Invoke();
        }

        return new HandlerRegistration(this, handler);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Tolerant, like every other source, and it tells the owner when it takes the last handler away so
    /// that an entry nobody watches can be dropped. A handler that is not registered is a no-op rather
    /// than a release: a token disposed twice, or one whose entry the owner already emptied, must not
    /// report that this entry lost a watcher it never had.
    /// </remarks>
    public void RemoveObservableEventRaisedHandler(Action<IObservableEventSource> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (_successor is { } successor)
        {
            successor.RemoveObservableEventRaisedHandler(handler);
            return;
        }

        var before = _handlers.Length;
        _handlers = _handlers.Remove(handler);

        if (_handlers.Length == before)
        {
            return;
        }

        if (_handlers.IsEmpty)
        {
            _onReleased?.Invoke();
        }
    }

    /// <inheritdoc/>
    public void DependUpon() => Observable.PublishRead(this);

    /// <inheritdoc/>
    /// <remarks>Public because the collection that owns this raises it; a consumer reads, it does not
    /// raise. An adopted source raises through the entry that adopted it, which is where the handlers
    /// went.</remarks>
    public void RaiseEvent() => Target.RaiseToHandlers();

    /// <summary>Drops every handler without telling the owner, for a collection being disposed.</summary>
    /// <remarks>
    /// Along the chain, because a collection disposes the entries in its table and an adopted one is
    /// still registered on the entry that adopted it - dropping only the table's own array would leave
    /// those handlers on an entry the table no longer holds.
    /// </remarks>
    internal void Clear()
    {
        var target = Target;
        target._handlers = [];

        if (!ReferenceEquals(target, this))
        {
            _handlers = [];
        }
    }

    /// <summary>Takes over everything <paramref name="other"/> is watching, and leaves it forwarding here.</summary>
    /// <param name="other">The entry that was holding the row this one has just claimed.</param>
    /// <remarks>
    /// <para>
    /// A caller can keep a source past the point where the collection dropped it - a run reports its
    /// dependencies, and the interface is public. If that caller registers on it again after a later
    /// read has made a second entry for the same index, both entries have live readers and only one of
    /// them can be the entry the collection marks, so marking either one alone would leave the other's
    /// readers silent forever.
    /// </para>
    /// <para>
    /// So the returning entry adopts the newer one: every handler moves here, which means a change
    /// raises all of them, and the adopted entry is left forwarding - so the tokens its readers still
    /// hold unregister from here rather than from an entry nothing raises.
    /// </para>
    /// </remarks>
    internal void Adopt(ObservableDependency other)
    {
        var target = other.Target;

        _handlers = _handlers.AddRange(target._handlers);
        target._handlers = [];
        target._successor = this;
    }

    private void RaiseToHandlers() => Observable.RaiseToHandlers(this, _handlers);

    /// <summary>What was read, for a diagnostic.</summary>
    /// <remarks>The only caller is the refusal message, so a source with no text of its own still names
    /// something rather than returning null.</remarks>
    public override string ToString() => _description ?? string.Empty;
}
