namespace Kolvir.Feature.Observables;

/// <summary>
/// The handle for one named dependency: a key, and an <c>Equals</c> that says so.
/// </summary>
/// <remarks>
/// <para>
/// A name, not an entry. Every call to <see cref="Observable.GetNamed"/> makes a fresh one of these, it
/// holds nothing but the key, and two handles for equal keys compare equal - so a dependency set
/// de-duplicates them the way it de-duplicates a value by identity, and a run that reads one key twice
/// subscribes once (section 3.2).
/// </para>
/// <para>
/// That is what keeps the handle free of lifetime rules. It is not the thing that holds handlers, so it
/// cannot go stale: every operation resolves the key in the table, which creates the entry on demand and
/// lets it go the moment nothing is registered on it. A caller may keep a handle for as long as it likes,
/// which is exactly what a model does when it wants to raise a key it also reads.
/// </para>
/// <para>
/// An event source and nothing more, because a named dependency says "the thing behind this key changed"
/// and carries no value: a value stored in the entry would vanish with the last listener, and would be
/// reset in the middle of a re-evaluation that unhooks before it re-hooks - because that unhook is
/// momentarily the last listener.
/// </para>
/// </remarks>
internal sealed class NamedEventSource : IObservableEventSource
{
    private readonly object _key;

    internal NamedEventSource(object key) => _key = key;

    /// <summary>What this handle names.</summary>
    internal object Key => _key;

    /// <inheritdoc/>
    /// <remarks>Always false: a name is not a resource, and nothing the table does can kill one.</remarks>
    public bool IsDisposed => false;

    /// <inheritdoc/>
    public IDisposable AddObservableEventRaisedHandler(Action<IObservableEventSource> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        NamedDependencies.Subscribe(_key, handler);

        return new HandlerRegistration(this, handler);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Tolerant, like every other source: removing a handler that is not registered does nothing, and
    /// removing the last one lets the key go, which is the whole of the memory story.
    /// </remarks>
    public void RemoveObservableEventRaisedHandler(Action<IObservableEventSource> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        NamedDependencies.Unsubscribe(_key, handler);
    }

    /// <inheritdoc/>
    public void DependUpon() => Observable.PublishRead(this);

    /// <inheritdoc/>
    /// <remarks>
    /// The same dispatch a value uses, so a named event is counted against the evaluation depth and refused
    /// when a chain of them loops, exactly as a write is (section 3.8).
    /// </remarks>
    public void RaiseEvent() => NamedDependencies.Raise(_key);

    /// <summary>Whether <paramref name="obj"/> names the same key.</summary>
    /// <remarks>
    /// The key's own equality, so a tuple key compares structurally and needs no formatting or separator
    /// escaping to become a name.
    /// </remarks>
    public override bool Equals(object? obj) =>
        obj is NamedEventSource other && object.Equals(_key, other._key);

    /// <inheritdoc/>
    public override int GetHashCode() => _key.GetHashCode();

    /// <summary>What the handle names, for a diagnostic.</summary>
    public override string ToString() => $"the named dependency for {_key}";
}
