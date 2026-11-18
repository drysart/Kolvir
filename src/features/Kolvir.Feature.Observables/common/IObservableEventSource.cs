namespace Kolvir.Observables;

/// <summary>
/// Something a value can depend on: raises an event when a particular thing changes, and carries no
/// value of its own.
/// </summary>
/// <remarks>
/// <para>
/// This is the dependency primitive. An <see cref="IObservableValue{T}"/> is one of these plus a
/// value, and so is a named dependency - "the thing behind this key changed" - which is why nothing
/// here has a value to hold: a source that only says "this changed" cannot lose or stale a value it
/// never had, and a per-key dependency can be evicted from a table without the eviction having a
/// consequence to get wrong.
/// </para>
/// <para>
/// It is not <see cref="IDisposable"/>. A named dependency is a name that any number of callers may hold
/// at once, so there is nothing for one of them to dispose; only the value types are disposable. What a
/// registrant gets is <see cref="AddObservableEventRaisedHandler"/>, and disposing that unregisters that
/// one handler.
/// </para>
/// <para>
/// A raise is a synchronous callback and behaves like one. The handler list is snapshotted, so a
/// handler may subscribe and unsubscribe freely - including itself - and none of that disturbs the
/// raise that is running: mutations apply to the next one. Nothing is caught, so the first handler
/// that throws ends the notification and the exception travels out of <see cref="RaiseEvent"/> to
/// whoever caused the change; the handlers behind it do not run for that change. This library has no
/// report channel and no failure policy, because the right response differs for a render, a test and
/// a background service, and the caller of the change is the outermost frame that can do anything
/// useful about it (dev-docs/OBSERVABLES.md section 8.1).
/// </para>
/// </remarks>
public interface IObservableEventSource
{
    /// <summary>Whether this source is dead, which is to say disposed.</summary>
    /// <remarks>
    /// True from the moment disposal starts, and it stays true. Every other member of the interface
    /// throws <see cref="ObjectDisposedException"/> once it is, except
    /// <see cref="RemoveObservableEventRaisedHandler"/> and a repeated <c>Dispose</c>, which stay safe
    /// because teardown paths release what they hold without knowing what state it is in.
    /// </remarks>
    bool IsDisposed { get; }

    /// <summary>Subscribes a handler and returns the token that unsubscribes it.</summary>
    /// <param name="handler">
    /// What to call when this source raises its event. The argument is the source, so one handler can
    /// serve several.
    /// </param>
    /// <returns>
    /// A token whose <see cref="IDisposable.Dispose"/> unregisters <paramref name="handler"/>. It is
    /// safe to dispose more than once, and safe to dispose after the source is gone.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The source is disposed.</exception>
    /// <remarks>
    /// Registering is a subscription and not a claim: the same handler may be registered more than
    /// once, and each registration unregisters separately. Nothing here keeps anything alive that
    /// nothing else references - the handler list is the only thing a source holds, so a source with
    /// no handlers roots nothing and is collected with its last reference.
    /// </remarks>
    IDisposable AddObservableEventRaisedHandler(Action<IObservableEventSource> handler);

    /// <summary>Unsubscribes a handler registered with <see cref="AddObservableEventRaisedHandler"/>.</summary>
    /// <param name="handler">The handler to remove, comparing by the delegate's own equality.</param>
    /// <remarks>
    /// Unregistering is the onus of whoever registers, and the token returned by registration is the
    /// usual way. This method is public because a hand-written source and a token have nothing else to
    /// call, and it is deliberately tolerant: it does nothing when the handler is not registered, and
    /// it does nothing at all once the source is disposed, so a token can be disposed after the source
    /// rather than before it.
    /// </remarks>
    void RemoveObservableEventRaisedHandler(Action<IObservableEventSource> handler);

    /// <summary>Records that the reader now depends on this source.</summary>
    /// <remarks>
    /// <para>
    /// Every read of a value calls this, and it appends the source to the innermost active collection
    /// scope - the one that belongs to the expression or the
    /// <c>CollectDependencies</c> call that is running. It does nothing outside such a scope, which is
    /// the cost of every ordinary property read in an application that never uses an expression: a
    /// null check on a static field.
    /// </para>
    /// <para>
    /// There is a default implementation rather than an abstract one so that an implementation cannot
    /// forget to publish a read, and there is one name for the action rather than two.
    /// </para>
    /// </remarks>
    void DependUpon() => Observable.PublishRead(this);

    /// <summary>Announces that this source changed.</summary>
    /// <exception cref="ObjectDisposedException">The source is disposed.</exception>
    /// <remarks>
    /// <para>
    /// Public and on the interface because a source that owns a change raises it: a collection raising
    /// a key's dependency, a hand-written source raising on a timer tick, an expression raising because
    /// what it depends on changed. It is not something a consumer calls - a consumer changes the model
    /// and the model raises.
    /// </para>
    /// <para>
    /// A disposed source throws rather than ignoring the call. A source that is dead is a lifetime bug
    /// in whoever still holds it, and a silent no-op makes that bug invisible by making the call look
    /// like it announced something. Raising for something nobody is listening to is not that case and
    /// is not an error: a source with no handlers does nothing.
    /// </para>
    /// </remarks>
    void RaiseEvent();
}
