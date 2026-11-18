namespace Kolvir.Observables;

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
/// Distinct by reference identity rather than by equality, because a source has no value equality to
/// have an opinion about and two named dependencies for one key are the same instance anyway. Identity
/// is enforced rather than assumed: a hand-written source is free to override
/// <see cref="object.Equals(object)"/>, and two such sources that compare equal are still two different
/// dependencies.
/// </para>
/// <para>
/// The order is the first read of each, which is what makes a cycle message read like the path that
/// closed it, so the list is kept whatever else happens. While the set is small its de-duplication is a
/// scan, because a handful of items is cheaper to scan than to hash; past
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

    internal CollectionScope(Action<IObservableEventSource> onChanged, string description)
    {
        _onChanged = onChanged;
        Description = description;
    }

    /// <summary>What this run is, for the message that refuses a change made from inside it.</summary>
    internal string Description { get; }

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
            if (ReferenceEquals(dependency, source))
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
            _seen = new HashSet<IObservableEventSource>(_dependencies, ReferenceEqualityComparer.Instance);
        }
    }
}
