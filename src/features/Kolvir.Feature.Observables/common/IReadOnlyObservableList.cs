namespace Kolvir.Observables;

/// <summary>
/// A list whose changes are reported, and which can be read without being changed.
/// </summary>
/// <typeparam name="T">The type of the items.</typeparam>
/// <remarks>
/// <para>
/// The read surface plus the event: what a model exposes, and what a view holds. It is what
/// <see cref="Observable.CreateList{T}()"/> returns through
/// <see cref="IObservableList{T}"/>, and the narrower of the two is the type to hand out
/// (dev-docs/OBSERVABLE-COLLECTIONS.md sections 2 and 4).
/// </para>
/// <para>
/// <b>Reads publish dependencies, one kind per question.</b> <see cref="IReadOnlyCollection{T}.Count"/>
/// publishes a dependency that is raised only when an add, a removal or a clear changes the size;
/// <c>this[index]</c> publishes one for that index, raised when the item that index would return may
/// differ; and enumeration, <c>Contains</c>, <c>IndexOf</c> and <c>CopyTo</c> publish one that is
/// raised by any change at all, because they read every item. So an expression over
/// <c>Count</c> is not re-run by an item write, and an expression over one row is not re-run by a
/// change to another row (section 3.6).
/// </para>
/// <para>
/// <b>The event is not a dependency.</b> The list is deliberately not an
/// <see cref="IObservableEventSource"/>: an untyped notification could not carry the records, and a
/// single "the list changed" source would be raised by everything and wake every reader for every
/// change - the coarse notification this whole design exists to avoid (section 3.8).
/// </para>
/// <para>
/// <b>Disposable for lifetime, not for memory.</b> A list with no handlers roots nothing beyond its
/// items, so dropping the last reference collects it. <see cref="IDisposable.Dispose"/> exists for the
/// other problem: a long-lived list that a dead screen's handler is still registered on would keep that
/// screen's state alive for the life of the process. Disposal drops any changes that had not been
/// delivered, delivers one last event with no changes, and makes every other member throw
/// (section 3.9).
/// </para>
/// </remarks>
public interface IReadOnlyObservableList<T> : IReadOnlyList<T>, IDisposable
{
    /// <summary>Whether this list is dead, which is to say disposed.</summary>
    /// <remarks>
    /// True from the moment disposal starts and it stays true. Every other member throws
    /// <see cref="ObjectDisposedException"/> once it is, except
    /// <see cref="RemoveObservableListChangedHandler"/> and a repeated <c>Dispose</c>, which stay safe
    /// because teardown paths release what they hold without knowing what state it is in.
    /// </remarks>
    bool IsDisposed { get; }

    /// <summary>Subscribes a handler and returns the token that unsubscribes it.</summary>
    /// <param name="handler">What to call when the list delivers a change.</param>
    /// <returns>
    /// A token whose <see cref="IDisposable.Dispose"/> unregisters <paramref name="handler"/>. It is
    /// safe to dispose more than once, and safe to dispose after the list is gone.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The list is disposed.</exception>
    /// <remarks>
    /// Registering is a subscription and not a claim: the same handler may be registered more than
    /// once, and each registration unregisters separately. The handlers are walked as a snapshot, so a
    /// handler may subscribe and unsubscribe freely - including itself - and a change applies to the
    /// next delivery rather than disturbing the one running.
    /// </remarks>
    IDisposable AddObservableListChangedHandler(Action<ObservableListChangedEventArgs<T>> handler);

    /// <summary>Unsubscribes a handler registered with <see cref="AddObservableListChangedHandler"/>.</summary>
    /// <param name="handler">The handler to remove, comparing by the delegate's own equality.</param>
    /// <remarks>
    /// Deliberately tolerant: removing a handler that is not registered does nothing, and so does any
    /// call after the list is disposed - which is what lets a token be disposed after the list rather
    /// than before it, the common shape of a teardown path.
    /// </remarks>
    void RemoveObservableListChangedHandler(Action<ObservableListChangedEventArgs<T>> handler);
}
