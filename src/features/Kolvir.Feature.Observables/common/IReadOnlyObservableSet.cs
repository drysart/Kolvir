namespace Kolvir.Feature.Observables;

/// <summary>
/// A set that can be watched, which is what everything downstream of the owner is handed.
/// </summary>
/// <typeparam name="T">The type of the items.</typeparam>
/// <remarks>
/// <para>
/// The read-only half of an observable set, and the one a consumer sees:
/// <see cref="Observable.CreateHashSet{T}(IEqualityComparer{T}?)"/> hands back the writable
/// <see cref="IObservableSet{T}"/>, and a model exposes that as this.
/// </para>
/// <para>
/// <b>Registering is the whole of it.</b> A set is not an <see cref="IObservableEventSource"/> for the
/// reason a list is not (dev-docs/OBSERVABLE-COLLECTIONS.md section 3.8): what it publishes instead is a
/// dependency per kind of read - the count, one item, the contents - each reachable only by making the
/// read, so a render depends on exactly what it looked at.
/// </para>
/// <para>
/// <see cref="Comparer"/> is part of the contract rather than a detail, because it decides what "the
/// same item" means: a consumer mirroring this set has to build its mirror with the same comparer or it
/// will disagree about what is in it
/// (<see cref="Observable.CreateHashSet{T}(IEqualityComparer{T}?)"/>).
/// </para>
/// </remarks>
public interface IReadOnlyObservableSet<T> : IReadOnlySet<T>, IDisposable
{
    /// <summary>Whether the set has been disposed.</summary>
    /// <remarks>
    /// True from inside the final event a disposal delivers, which is why a handler that reads the set
    /// checks it first - every other event arrives with it false.
    /// </remarks>
    bool IsDisposed { get; }

    /// <summary>What decides that two items are the same item.</summary>
    /// <remarks>
    /// A constant of the set and not a read of anything, so it publishes no dependency and stays
    /// readable once the set is dead. It is what <c>Contains</c>, <c>Add</c>'s "already there",
    /// <c>Remove</c> and every set-algebra operation mean, so a mirror has to use it too.
    /// </remarks>
    IEqualityComparer<T> Comparer { get; }

    /// <summary>Registers for every event.</summary>
    /// <param name="handler">What one delivery calls. One change outside a batch, a whole batch inside
    /// one, and one empty event when the set is disposed.</param>
    /// <returns>What unregisters it. Disposing twice is safe, and so is disposing it after the set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The set is disposed.</exception>
    IDisposable AddObservableSetChangedHandler(Action<ObservableSetChangedEventArgs<T>> handler);

    /// <summary>Unregisters a handler registered with
    /// <see cref="AddObservableSetChangedHandler"/>.</summary>
    /// <param name="handler">The same delegate that was registered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
    /// <remarks>Tolerant: removing a handler that is not registered does nothing, and stays legal after
    /// the set is disposed.</remarks>
    void RemoveObservableSetChangedHandler(Action<ObservableSetChangedEventArgs<T>> handler);
}
