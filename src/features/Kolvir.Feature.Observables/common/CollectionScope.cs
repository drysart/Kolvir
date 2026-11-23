namespace Kolvir.Feature.Observables;

/// <summary>
/// One run of a function: what it read, and the subscriptions taken on its behalf as it read it.
/// </summary>
/// <remarks>
/// <para>
/// A scope subscribes <em>when the read happens</em>, not when the run finishes. That is the difference
/// between "recorded here and hooked later" and "already listening": by the time a read returns, the
/// source behind it is subscribed, so there is no window in which the reader has the value and is not
/// yet watching the thing that produced it.
/// </para>
/// <para>
/// It also means a read of a cold expression makes that expression hot <em>during</em> the read, so the
/// value handed back is the one the expression keeps, rather than the result of an unmonitored run that
/// is thrown away and immediately recomputed. One run, one result, one set.
/// </para>
/// <para>
/// Distinct by the source's own equality. For a value or an expression that is reference identity, which
/// is what a type with no opinion about equality gives and what the layer wants: every value is one
/// dependency. A named dependency is the other case - a handle is a name rather than an entry, so two
/// handles for one key are the same dependency and say so through <c>Equals</c>, which is what makes a
/// run that reads one key twice subscribe once (section 3.2).
/// </para>
/// <para>
/// The order is the first read of each, which is the order the body reached them - the useful report of
/// a run's inputs, so the list is kept whatever else happens. While the set is small its de-duplication
/// is a scan, because a handful of items is cheaper to scan than to hash; past
/// <see cref="LinearScanLimit"/> a hash set is built from what the list already holds and every later
/// read is a hash lookup. A scope only ever grows, so the two never drift apart.
/// </para>
/// </remarks>
internal sealed class CollectionScope
{
    /// <summary>Where a scan stops being cheaper than hashing.</summary>
    private const int LinearScanLimit = 16;

    private readonly Action<IObservableEventSource> _onChanged;
    private readonly List<IObservableEventSource> _dependencies = [];
    private readonly List<IDisposable> _tokens = [];
    private HashSet<IObservableEventSource>? _seen;

    internal CollectionScope(Action<IObservableEventSource> onChanged) => _onChanged = onChanged;

    /// <summary>The distinct sources this run read, in first-read order.</summary>
    internal IReadOnlyList<IObservableEventSource> Dependencies => _dependencies;

    /// <summary>The subscription taken on each of them, in the same order.</summary>
    internal IReadOnlyList<IDisposable> Tokens => _tokens;

    /// <summary>Records a read and subscribes to it, unless it is already in this set.</summary>
    internal void Add(IObservableEventSource source)
    {
        if (Contains(source))
        {
            return;
        }

        // Subscribe before recording, so a source whose registration throws leaves neither a
        // subscription to leak nor an entry claiming one.
        _tokens.Add(source.AddObservableEventRaisedHandler(_onChanged));
        Record(source);
    }

    private bool Contains(IObservableEventSource source)
    {
        if (_seen is not null)
        {
            return _seen.Contains(source);
        }

        foreach (var dependency in _dependencies)
        {
            if (dependency.Equals(source))
            {
                return true;
            }
        }

        return false;
    }

    private void Record(IObservableEventSource source)
    {
        _dependencies.Add(source);

        if (_seen is not null)
        {
            _seen.Add(source);
        }
        else if (_dependencies.Count > LinearScanLimit)
        {
            _seen = new HashSet<IObservableEventSource>(_dependencies);
        }
    }
}
