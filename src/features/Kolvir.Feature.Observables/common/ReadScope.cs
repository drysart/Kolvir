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
/// Being in a scope is also what makes a change to an observable illegal. A function has to be a pure
/// function of what it read, and a change raised into a run that is still collecting would re-enter it
/// with a set that no longer describes what happened. That is checked here, once, for every write and
/// every raise.
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

    /// <summary>Refuses a change made from inside a run.</summary>
    /// <param name="operation">What was attempted, for the message.</param>
    /// <exception cref="InvalidOperationException">A run is in progress.</exception>
    /// <remarks>
    /// A write or a raise from inside a function is not a subtlety to be tolerated: the run would
    /// re-enter itself, and its two collections would interleave their sets. Refused rather than
    /// deferred, because a function that changes something has already computed a result from a state
    /// that is out of date. A change from inside a handler is ordinary and allowed - handlers run with
    /// no run in progress.
    /// </remarks>
    internal static void EnsureNotCollecting(string operation)
    {
        if (_current is { } scope)
        {
            throw new InvalidOperationException(
                $"{operation} while {scope.Description} is collecting its dependencies. A function must be "
                + "a pure function of what it read, so a change made from inside one is refused rather "
                + "than recorded into a set that no longer describes the run.");
        }
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
