namespace Kolvir.Feature.Observables;

/// <summary>Which collection an address belongs to, which is all a diagnostic needs to name it.</summary>
/// <remarks>
/// A byte rather than an int: this is stored once per watched address, and an address is the cheapest
/// thing in the graph - see <see cref="ObservableKeyedDependency{TKey}"/>.
/// </remarks>
internal enum ObservableKeyedDependencyKind : byte
{
    /// <summary>An index into an observable list.</summary>
    Index,

    /// <summary>An item of an observable set, which is its own address.</summary>
    Item,

    /// <summary>A key of an observable dictionary.</summary>
    Key,
}
