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
/// have an opinion about and two named dependencies for one key are the same instance anyway. So a
/// function that reads <c>Count</c> in a loop hooks the count source once, and the order is the first
/// read of each, which is what makes a cycle message read like the path that closed it.
/// </para>
/// </remarks>
internal sealed class CollectionScope
{
    private readonly List<IObservableEventSource> _dependencies = [];

    internal IReadOnlyList<IObservableEventSource> Dependencies => _dependencies;

    internal void Add(IObservableEventSource source)
    {
        foreach (var dependency in _dependencies)
        {
            if (ReferenceEquals(dependency, source))
            {
                return;
            }
        }

        _dependencies.Add(source);
    }
}
