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
/// <see cref="Comparer"/> is part of the contract rather than a detail, because it decides what "the
/// same key" means: a consumer mirroring this dictionary has to build its mirror with the same comparer
/// or it will disagree about what is in it (section 7.2.1).
/// </para>
/// </remarks>
public interface IReadOnlyObservableDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>, IDisposable
{
    /// <summary>Whether the dictionary has been disposed.</summary>
    /// <remarks>
    /// True from inside the final event a disposal delivers, which is why a handler that reads the
    /// dictionary checks it first - every other event arrives with it false.
    /// </remarks>
    bool IsDisposed { get; }

    /// <summary>What decides that two keys are the same key.</summary>
    /// <remarks>
    /// A constant of the dictionary and not a read of anything, so it publishes no dependency and stays
    /// readable once it is dead. It is what <c>ContainsKey</c>, <c>TryGetValue</c>, the indexer and
    /// <c>Add</c>'s "already there" mean, so a mirror has to use it too.
    /// </remarks>
    IEqualityComparer<TKey> Comparer { get; }

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
