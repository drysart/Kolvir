namespace Kolvir.Feature.Observables;

/// <summary>
/// The factories for observable lists.
/// </summary>
/// <remarks>
/// <para>
/// Where a list comes from, matching <see cref="CreateValue{T}"/>: the writable interface is what the
/// owner of the state keeps, and everything downstream sees
/// <see cref="IReadOnlyObservableList{T}"/>.
/// </para>
/// <code>
/// internal sealed class TaskList
/// {
///     private readonly IObservableList&lt;string&gt; _tasks = Observable.CreateList&lt;string&gt;();
///
///     // The model's surface: read-only, observable, and nothing to dispose for a consumer.
///     public IReadOnlyObservableList&lt;string&gt; Tasks =&gt; _tasks;
///
///     public void Add(string task) =&gt; _tasks.Add(task);
/// }
/// </code>
/// <para>
/// The <c>params</c> overloads are for a model with initial contents, and they exist as spans because
/// the call site usually has a handful of items and copying them into an array first is an allocation
/// nobody needs.
/// </para>
/// </remarks>
public static partial class Observable
{
    /// <summary>Creates an empty observable list.</summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <returns>The writable view of the new list, for the code that owns the state.</returns>
    /// <remarks>
    /// Always a fresh list, unrelated to any other. Writing an equal item over itself raises nothing;
    /// every other change raises one record with what changed
    /// (dev-docs/OBSERVABLE-COLLECTIONS.md section 3.4).
    /// </remarks>
    public static IObservableList<T> CreateList<T>() => new ObservableList<T>(null);

    /// <summary>Creates an empty observable list whose item equality is <paramref name="comparer"/>.</summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="comparer">
    /// What decides that a write changed an item, or null for <see cref="EqualityComparer{T}.Default"/>.
    /// It is used for that decision and nothing else: <c>Contains</c>, <c>IndexOf</c> and <c>Remove</c>
    /// keep <see cref="IList{T}"/>'s own equality, because the list is not a set.
    /// </param>
    /// <returns>The writable view of the new list.</returns>
    public static IObservableList<T> CreateList<T>(IEqualityComparer<T>? comparer) =>
        new ObservableList<T>(comparer);

    /// <summary>Creates an observable list holding <paramref name="items"/>.</summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="items">What the list holds, in order.</param>
    /// <returns>The writable view of the new list.</returns>
    /// <remarks>Creating a list with contents raises nothing: there is nobody registered yet, and the
    /// items are the starting state rather than a change to it.</remarks>
    public static IObservableList<T> CreateList<T>(params ReadOnlySpan<T> items) =>
        new ObservableList<T>(items, null);

    /// <summary>Creates an observable list holding <paramref name="items"/>, with its own item equality.</summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="comparer">
    /// What decides that a write changed an item, or null for <see cref="EqualityComparer{T}.Default"/>.
    /// </param>
    /// <param name="items">What the list holds, in order.</param>
    /// <returns>The writable view of the new list.</returns>
    public static IObservableList<T> CreateList<T>(IEqualityComparer<T>? comparer, params ReadOnlySpan<T> items) =>
        new ObservableList<T>(items, comparer);
}
