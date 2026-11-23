namespace Kolvir.Feature.Observables;

/// <summary>
/// A dependency that carries a value, seen without its type.
/// </summary>
/// <remarks>
/// <para>
/// This is the shape a dependency set holds and the shape an expression publishes a read through, so
/// that a set can be homogeneous over values of different types. A caller that knows what it is
/// reading uses <see cref="IObservableValue{T}"/> and gets no boxing.
/// </para>
/// <para>
/// Reading <see cref="Value"/> publishes the read (see
/// <see cref="IObservableEventSource.DependUpon"/>), which is the whole mechanism behind an
/// expression's dependency set: there is no registration list to keep in step with what the function
/// actually touches.
/// </para>
/// <para>
/// Read-only on purpose. The <see cref="IDisposable"/> half is about lifetime, not about writing:
/// disposing a value marks it dead, raises its event once so a registrant learns the dependency is
/// gone, and releases the handlers.
/// </para>
/// </remarks>
public interface IObservableValue : IObservableEventSource, IDisposable
{
    /// <summary>The current value, boxed.</summary>
    /// <exception cref="ObjectDisposedException">The value is disposed.</exception>
    /// <remarks>
    /// Reading publishes the read, exactly as the typed form does, so a consumer that reads through
    /// this interface is tracked like one that reads
    /// <see cref="IObservableValue{T}.Value"/>.
    /// </remarks>
    object? Value { get; }
}

/// <summary>
/// A dependency that carries a value of its own type, and can only be read.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <remarks>
/// <para>
/// What a dependency set holds and what an expression's result is. It cannot be written, which is a
/// compile-time promise rather than a guard: an expression's only way to change is to change something
/// it read, and that is the property that makes the graph trustworthy. A determined caller can still
/// cast to <see cref="IObservableMutableValue{T}"/>, which is the same level of trust the rest of the
/// library extends to its own internals and costs nothing to keep out of an API.
/// </para>
/// <para>
/// Reading publishes the read, so it belongs to whatever collection scope is running, and the value
/// raises its event when it changes - see <see cref="IObservableMutableValue{T}"/> for the writer's
/// half.
/// </para>
/// </remarks>
public interface IObservableValue<T> : IObservableValue
{
    /// <summary>The current value.</summary>
    /// <exception cref="ObjectDisposedException">The value is disposed.</exception>
    /// <remarks>Reading publishes the read, so it belongs to the running collection scope.</remarks>
    new T Value { get; }
}
