namespace Kolvir.Observables;

/// <summary>
/// The scope a read publishes into: one static field, so an ordinary read outside any run costs one
/// null check.
/// </summary>
/// <remarks>
/// <para>
/// A single-threaded design, like the rest of the library: there is one scope in flight per thread's
/// worth of work and no locking, which is what lets every property read in a model stay as cheap as a
/// comparison (dev-docs/OBSERVABLES.md section 3.9).
/// </para>
/// <para>
/// The token restores the previous scope in <c>finally</c>, so an exception from a body cannot leave
/// the collector pointing at a scope whose run has already ended - and because the previous scope is
/// captured rather than assumed to be null, nesting works without a counter.
/// </para>
/// </remarks>
internal static class ReadScope
{
    private static CollectionScope? _current;

    /// <summary>Records a read, or does nothing when no run is in progress.</summary>
    internal static void Publish(IObservableEventSource source) => _current?.Add(source);

    /// <summary>Makes <paramref name="scope"/> the innermost scope until the token is disposed.</summary>
    internal static ScopeToken Begin(CollectionScope scope) => Enter(scope);

    /// <summary>Publishes nothing until the token is disposed, whatever scope is current.</summary>
    /// <remarks>
    /// What a cold expression read does: it wants the function's result and throws its dependencies
    /// away, so there is nothing to collect them into - and suspending also keeps them out of whatever
    /// run this expression was read from, which is the isolation in section 3.3.
    /// </remarks>
    internal static ScopeToken Suspend() => Enter(null);

    /// <summary>Swaps the current scope for <paramref name="scope"/> until the token is disposed.</summary>
    private static ScopeToken Enter(CollectionScope? scope)
    {
        var previous = _current;
        _current = scope;
        return new ScopeToken(previous);
    }

    /// <summary>Puts the scope that was current before <see cref="Enter"/> back.</summary>
    /// <remarks>
    /// A struct and not a class, so entering a scope allocates nothing at all - this runs on every read
    /// of every expression.
    /// </remarks>
    internal readonly struct ScopeToken : IDisposable
    {
        private readonly CollectionScope? _previous;

        internal ScopeToken(CollectionScope? previous) => _previous = previous;

        public void Dispose() => _current = _previous;
    }
}
