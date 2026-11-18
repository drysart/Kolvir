namespace Kolvir.Observables;

/// <summary>
/// The scope a read publishes into: one static field, so an ordinary read outside any run costs one
/// null check.
/// </summary>
/// <remarks>
/// <para>
/// A single-threaded design, like the rest of the library: there is one run in flight and no locking,
/// which is what lets every property read in a model stay as cheap as a comparison
/// (dev-docs/OBSERVABLES.md section 3.9).
/// </para>
/// <para>
/// Being in a scope says nothing about what a change may do. A body is free to write, and a write it
/// made to something it had already read raises straight back into the run through the subscription
/// that read took - a loop, stopped by the raise depth and not here (see
/// <see cref="Observable.MaxEventStackSize"/>). The scope only collects.
/// </para>
/// <para>
/// The token restores the previous scope in <c>finally</c>, so an exception from a body cannot leave
/// the collector pointing at a run that has already ended - and because the previous scope is captured
/// rather than assumed to be null, nesting works without a counter.
/// </para>
/// </remarks>
internal static class ReadScope
{
    private static CollectionScope? _current;

    /// <summary>Records a read, or does nothing when no run is in progress.</summary>
    internal static void Publish(IObservableEventSource source) => _current?.Add(source);

    /// <summary>Makes <paramref name="scope"/> the innermost scope until the token is disposed.</summary>
    internal static ScopeToken Begin(CollectionScope scope)
    {
        var previous = _current;
        _current = scope;
        return new ScopeToken(previous);
    }

    /// <summary>Puts the scope that was current before <see cref="Begin"/> back.</summary>
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
