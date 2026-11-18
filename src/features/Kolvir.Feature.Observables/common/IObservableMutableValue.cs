namespace Kolvir.Observables;

/// <summary>
/// A value that can be written, which only the code that owns the state is handed.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <remarks>
/// <para>
/// What <see cref="Observable.CreateValue{T}"/> returns. The owner of a model holds one of these in a
/// private field and exposes it as an ordinary property - <c>public string Text =&gt; _text.Value;</c>
/// or a get and a set - because a consumer that cares the property is observable is writing an
/// expression or collecting dependencies over it, and a consumer that does not care should not have to
/// know. Nothing in the API asks a consumer to hold an observable directly.
/// </para>
/// <para>
/// Writing an equal value raises nothing. An event means something a listener cares about is
/// different, so a model that recomputes 42 over 42 wakes nothing up; a setter that always raised would
/// make every "changed" handler an "assigned" handler. Equality is the type's own,
/// <see cref="EqualityComparer{T}.Default"/>, which is what every other equality decision in the
/// library uses - so a record's value semantics work with no ceremony, and a type that wants identity
/// semantics gets them by not overriding <see cref="object.Equals(object)"/>.
/// </para>
/// <para>
/// Writing once and raising once is the whole of it: there is no batching, no deferred notification
/// and no coalescing here. A component that must render once for five changes gets that from the frame
/// it schedules, not from the value.
/// </para>
/// </remarks>
public interface IObservableMutableValue<T> : IObservableValue<T>
{
    /// <summary>The current value. Assigning raises the event when it actually changes.</summary>
    /// <exception cref="ObjectDisposedException">The value is disposed.</exception>
    /// <remarks>
    /// Reading publishes the read, like every other read. Assigning compares first, stores, and raises
    /// once - so a handler is never told about a write that changed nothing.
    /// </remarks>
    new T Value { get; set; }
}
