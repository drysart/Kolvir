namespace Kolvir.Observables;

/// <summary>
/// The factories for observable dictionaries.
/// </summary>
/// <remarks>
/// <para>
/// Where a dictionary comes from, matching <see cref="CreateList{T}()"/> and
/// <see cref="CreateHashSet{T}()"/>: the writable interface is what the owner of the state keeps, and
/// everything downstream sees <see cref="IReadOnlyObservableDictionary{TKey, TValue}"/>.
/// </para>
/// <code>
/// internal sealed class Settings
/// {
///     private readonly IObservableDictionary&lt;string, string&gt; _values =
///         Observable.CreateDictionary&lt;string, string&gt;();
///
///     // The model's surface: read-only, observable, and nothing to dispose for a consumer.
///     public IReadOnlyObservableDictionary&lt;string, string&gt; Values =&gt; _values;
///
///     public void Rename(string from, string to) =&gt; _values[to] = _values[from];
/// }
/// </code>
/// <para>
/// The key comparer is fixed at creation and exposed as
/// <see cref="IReadOnlyObservableDictionary{TKey, TValue}.Comparer"/>, because a dictionary's identity is
/// what its comparer says it is. <c>TKey : notnull</c> is
/// <see cref="IDictionary{TKey, TValue}"/>'s own constraint and <see cref="Dictionary{TKey, TValue}"/>'s,
/// so the factories carry it too.
/// </para>
/// </remarks>
public static partial class Observable
{
    /// <summary>Creates an empty observable dictionary.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TValue">The type of the values.</typeparam>
    /// <returns>The writable view of the new dictionary, for the code that owns the state.</returns>
    /// <remarks>
    /// Always a fresh dictionary, unrelated to any other. Writing an equal value over a key raises
    /// nothing; every other change raises one record
    /// (dev-docs/OBSERVABLE-COLLECTIONS.md section 7.4).
    /// </remarks>
    public static IObservableDictionary<TKey, TValue> CreateDictionary<TKey, TValue>()
        where TKey : notnull =>
        new ObservableDictionary<TKey, TValue>(null);

    /// <summary>Creates an empty observable dictionary whose key equality is
    /// <paramref name="comparer"/>.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TValue">The type of the values.</typeparam>
    /// <param name="comparer">What decides that two keys are the same key, or null for
    /// <see cref="EqualityComparer{T}.Default"/>.</param>
    /// <returns>The writable view of the new dictionary.</returns>
    public static IObservableDictionary<TKey, TValue> CreateDictionary<TKey, TValue>(IEqualityComparer<TKey>? comparer)
        where TKey : notnull =>
        new ObservableDictionary<TKey, TValue>(comparer);

    /// <summary>Creates an observable dictionary holding <paramref name="items"/>.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TValue">The type of the values.</typeparam>
    /// <param name="items">What it holds.</param>
    /// <returns>The writable view of the new dictionary.</returns>
    /// <exception cref="ArgumentException">Two items have keys the comparer calls equal.</exception>
    /// <remarks>Creating a dictionary with contents raises nothing: there is nobody registered yet, and
    /// the items are the starting state rather than a change to it.</remarks>
    public static IObservableDictionary<TKey, TValue> CreateDictionary<TKey, TValue>(
        IEnumerable<KeyValuePair<TKey, TValue>> items)
        where TKey : notnull =>
        new ObservableDictionary<TKey, TValue>(items, null);

    /// <summary>Creates an observable dictionary holding <paramref name="items"/>, with its own key
    /// equality.</summary>
    /// <typeparam name="TKey">The type of the keys.</typeparam>
    /// <typeparam name="TValue">The type of the values.</typeparam>
    /// <param name="comparer">What decides that two keys are the same key, or null for
    /// <see cref="EqualityComparer{T}.Default"/>.</param>
    /// <param name="items">What it holds.</param>
    /// <returns>The writable view of the new dictionary.</returns>
    /// <exception cref="ArgumentException">Two items have keys the comparer calls equal.</exception>
    /// <remarks>The comparer comes first, as it does in <see cref="CreateList{T}(IEqualityComparer{T}?, ReadOnlySpan{T})"/>
    /// and <see cref="CreateHashSet{T}(IEqualityComparer{T}?, ReadOnlySpan{T})"/>.</remarks>
    public static IObservableDictionary<TKey, TValue> CreateDictionary<TKey, TValue>(
        IEqualityComparer<TKey>? comparer,
        IEnumerable<KeyValuePair<TKey, TValue>> items)
        where TKey : notnull =>
        new ObservableDictionary<TKey, TValue>(items, comparer);
}
