namespace Kolvir.Observables;

/// <summary>
/// What one run of a function read, in the order it read it.
/// </summary>
/// <remarks>
/// <para>
/// The set an expression hooks, and the set <c>CollectDependencies</c> hands back. It is a stack of
/// these that makes nested runs work: a scope is entered by whoever is about to run a body, reads
/// publish into the innermost one, and the outer run never sees what the inner run read.
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

    private readonly List<IObservableEventSource> _dependencies = [];
    private HashSet<IObservableEventSource>? _seen;

    internal IReadOnlyList<IObservableEventSource> Dependencies => _dependencies;

    internal void Add(IObservableEventSource source)
    {
        if (_seen is not null)
        {
            if (_seen.Add(source))
            {
                _dependencies.Add(source);
            }

            return;
        }

        foreach (var dependency in _dependencies)
        {
            if (ReferenceEquals(dependency, source))
            {
                return;
            }
        }

        _dependencies.Add(source);

        if (_dependencies.Count > LinearScanLimit)
        {
            _seen = new HashSet<IObservableEventSource>(_dependencies, ReferenceEqualityComparer.Instance);
        }
    }
}
