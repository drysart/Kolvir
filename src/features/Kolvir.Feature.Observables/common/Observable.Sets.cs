namespace Kolvir.Observables;

/// <summary>
/// The factories for observable sets.
/// </summary>
/// <remarks>
/// <para>
/// Where a set comes from, matching <see cref="CreateList{T}()"/>: the writable interface is what the
/// owner of the state keeps, and everything downstream sees <see cref="IReadOnlyObservableSet{T}"/>.
/// </para>
/// <code>
/// internal sealed class TagFilter
/// {
///     private readonly IObservableSet&lt;string&gt; _tags = Observable.CreateHashSet&lt;string&gt;();
///
///     // The model's surface: read-only, observable, and nothing to dispose for a consumer.
///     public IReadOnlyObservableSet&lt;string&gt; Tags =&gt; _tags;
/// }
/// </code>
/// <para>
/// The comparer is fixed at creation and exposed as <see cref="IReadOnlyObservableSet{T}.Comparer"/>,
/// because a set's identity is what its comparer says it is - a consumer mirroring the set has to build
/// its mirror with the same rule. Without one the type's own equality is used.
/// </para>
/// <para>
/// The <c>params</c> overloads are for a model with initial contents, and they exist as spans for the
/// reason the list's do: the call site usually has a handful of items and copying them into an array
/// first is an allocation nobody needs.
/// </para>
/// </remarks>
public static partial class Observable
{
    /// <summary>Creates an empty observable set.</summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <returns>The writable view of the new set, for the code that owns the state.</returns>
    /// <remarks>
    /// Always a fresh set, unrelated to any other. Adding an item the comparer says is already there
    /// raises nothing; every change that happens raises one record per item
    /// (dev-docs/OBSERVABLE-COLLECTIONS.md section 6.4).
    /// </remarks>
    public static IObservableSet<T> CreateHashSet<T>()
        where T : notnull =>
        new ObservableHashSet<T>(null);

    /// <summary>Creates an empty observable set whose item equality is <paramref name="comparer"/>.</summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="comparer">What decides that two items are the same item, or null for
    /// <see cref="EqualityComparer{T}.Default"/>. It is the set's identity: every operation that asks
    /// whether an item is present asks this.</param>
    /// <returns>The writable view of the new set.</returns>
    public static IObservableSet<T> CreateHashSet<T>(IEqualityComparer<T>? comparer)
        where T : notnull =>
        new ObservableHashSet<T>(comparer);

    /// <summary>Creates an observable set holding <paramref name="items"/>.</summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="items">What the set holds. Duplicates collapse.</param>
    /// <returns>The writable view of the new set.</returns>
    /// <remarks>Creating a set with contents raises nothing: there is nobody registered yet, and the
    /// items are the starting state rather than a change to it.</remarks>
    public static IObservableSet<T> CreateHashSet<T>(params ReadOnlySpan<T> items)
        where T : notnull =>
        new ObservableHashSet<T>(items, null);

    /// <summary>Creates an observable set holding <paramref name="items"/>, with its own item
    /// equality.</summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="comparer">What decides that two items are the same item, or null for
    /// <see cref="EqualityComparer{T}.Default"/>.</param>
    /// <param name="items">What the set holds. Duplicates by <paramref name="comparer"/> collapse.</param>
    /// <returns>The writable view of the new set.</returns>
    public static IObservableSet<T> CreateHashSet<T>(IEqualityComparer<T>? comparer, params ReadOnlySpan<T> items)
        where T : notnull =>
        new ObservableHashSet<T>(items, comparer);
}
