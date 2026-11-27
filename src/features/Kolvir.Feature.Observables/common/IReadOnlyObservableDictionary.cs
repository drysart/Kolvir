namespace Kolvir.Feature.Observables;

/// <summary>
/// A dictionary that can be watched, which is what everything downstream of the owner is handed.
/// </summary>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TValue">The type of the values.</typeparam>
/// <remarks>
/// <para>
/// The read-only half of an observable dictionary, and the one a consumer sees:
/// <see cref="Observable.CreateDictionary{TKey, TValue}()"/> hands back the writable
/// <see cref="IObservableDictionary{TKey, TValue}"/>, and a model exposes that as this.
/// </para>
/// <para>
/// <b>Registering is the whole of it.</b> A dictionary is not an <see cref="IObservableEventSource"/> for
/// the reason a list is not (dev-docs/OBSERVABLE-COLLECTIONS.md section 3.8): what it publishes instead
/// is a dependency per kind of read - the structure, one key, the contents - each reachable only by
/// making the read, so a render depends on exactly what it looked at.
/// </para>
/// <para>
/// <b>It is also a set of its pairs.</b> A dictionary is an
/// <see cref="IReadOnlyObservableSet{T}"/> whose element is the pair itself, so <see cref="IReadOnlySet{T}"/>
/// is on its surface and an <see cref="IReadOnlyObservableSet{T}.AddObservableSetChangedHandler"/> hears
/// the same changes as the dictionary handlers, expressed as pairs. Identity includes the value:
/// <c>{"a" to 1}</c> and
/// <c>{"a" to 2}</c> are two different elements, a pair naming a key the dictionary holds with a
/// different value is not in the set, and two dictionaries with the same keys and different values are
/// not set-equal. A replacement is therefore two set changes - the old pair leaving, then the new one
/// arriving - in one event, because half of a replacement would show an observer a state the dictionary
/// never had (section 7.7).
/// </para>
/// <para>
/// <b>Only the read-only side is a set.</b> <see cref="IObservableDictionary{TKey, TValue}"/> does not
/// extend <see cref="IObservableSet{T}"/>, deliberately: the writable set surface would have to be spelled
/// in pairs, which is a different way of changing a dictionary rather than the way its owner already has,
/// and <c>Add(TKey, TValue)</c>, <c>TryAdd</c> and the indexer are the operations the owner asked for.
/// </para>
/// <para>
/// <b>Two comparers, and this is the dictionary's.</b> <see cref="Comparer"/> stays what decides that two
/// keys are the same key, and it is the member a caller of this interface sees. The set view's comparer -
/// what decides that two <em>pairs</em> are the same element - is
/// <see cref="IReadOnlyObservableSet{T}.Comparer"/>, hidden here by the <c>new</c> declaration below and
/// reachable by casting to the set interface. Implementations return the dictionary's key comparer from
/// the first and the pair comparer from the second; they are not the same object, and the two answer
/// different questions.
/// </para>
/// <para>
/// <b>An implementer supplies seven more members and two handlers.</b> The set view is part of this
/// contract, so a hand-written read-only observable dictionary now also implements
/// <see cref="IReadOnlySet{T}"/>'s six algebra members (plus <c>Contains</c>, which a dictionary usually
/// already has), the explicit pair comparer, and
/// <see cref="IReadOnlyObservableSet{T}.AddObservableSetChangedHandler"/> with
/// <see cref="IReadOnlyObservableSet{T}.RemoveObservableSetChangedHandler"/>. That cost is taken now
/// because a dictionary compared pair-wise through the BCL's default key equality - which is what
/// <see cref="ICollection{T}.Contains"/> uses - cannot answer these questions at all when its keys have
/// a comparer that is not equality.
/// </para>
/// <para>
/// <see cref="IReadOnlyObservableSet{T}.IsDisposed"/> and <see cref="IDisposable"/> are the set base's
/// declarations; this interface does not redeclare them, and their meaning here is the one the set base
/// gives them.
/// </para>
/// </remarks>
public interface IReadOnlyObservableDictionary<TKey, TValue>
    : IReadOnlyDictionary<TKey, TValue>, IReadOnlyObservableSet<KeyValuePair<TKey, TValue>>
{
    /// <summary>What decides that two keys are the same key.</summary>
    /// <remarks>
    /// <para>
    /// A constant of the dictionary and not a read of anything, so it publishes no dependency and stays
    /// readable once it is dead. It is what <c>ContainsKey</c>, <c>TryGetValue</c>, the indexer and
    /// <c>Add</c>'s "already there" mean, so a mirror has to use it too (section 7.2.1).
    /// </para>
    /// <para>
    /// Declared <c>new</c> because the set base brings a <see cref="Comparer"/> too, of the pair type. This
    /// one is the dictionary's, of the key type, and stays the member a caller of this interface sees; the
    /// pair comparer is the set base's, implemented explicitly.
    /// </para>
    /// </remarks>
    new IEqualityComparer<TKey> Comparer { get; }

    /// <summary>Registers for every event.</summary>
    /// <param name="handler">What one delivery calls. One change outside a batch, a whole batch inside
    /// one, and one empty event when the dictionary is disposed.</param>
    /// <returns>What unregisters it. Disposing twice is safe, and so is disposing it after the
    /// dictionary.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The dictionary is disposed.</exception>
    IDisposable AddObservableDictionaryChangedHandler(
        Action<ObservableDictionaryChangedEventArgs<TKey, TValue>> handler);

    /// <summary>Unregisters a handler registered with <see cref="AddObservableDictionaryChangedHandler"/>.</summary>
    /// <param name="handler">The same delegate that was registered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
    /// <remarks>Tolerant: removing a handler that is not registered does nothing, and stays legal after
    /// the dictionary is disposed.</remarks>
    void RemoveObservableDictionaryChangedHandler(
        Action<ObservableDictionaryChangedEventArgs<TKey, TValue>> handler);
}
