using System.Runtime.ExceptionServices;

namespace Kolvir.Observables;

/// <summary>
/// What one synchronous run of a body read, and whether it threw.
/// </summary>
/// <remarks>
/// <para>
/// The result of <c>Observable.CollectDependencies</c> over a body with no value. It is a record of what
/// happened rather than a subscription: nothing here has been hooked, and registering on
/// <see cref="Dependencies"/> is the caller's next step. That split is the point of the collector - the
/// component seam wants "the dependencies of this run" and "call me when one of them changes" as two
/// separate decisions, because it hooks only after the set is known.
/// </para>
/// <para>
/// The set comes back even when the body threw. The dependencies a run *reached* are what its result -
/// success or exception - was computed from, and therefore what can change it, so throwing straight
/// through would hand the caller an exception and no set: for a render, a component that never renders
/// again. The failure is in <see cref="Fault"/> instead, for a caller to rethrow with
/// <see cref="Throw"/> or to inspect and handle.
/// </para>
/// <para>
/// Nothing is reported anywhere. This is a synchronous call with somebody waiting, so the exception
/// belongs to the caller and the library has no channel to send it to as well
/// (dev-docs/OBSERVABLES.md sections 4 and 8.1).
/// </para>
/// </remarks>
public class Collected
{
    private readonly Exception? _fault;

    internal Collected(IReadOnlyList<IObservableEventSource> dependencies, Exception? fault)
    {
        Dependencies = dependencies;
        _fault = fault;
    }

    /// <summary>The distinct sources the body read, in the order it first read them.</summary>
    /// <remarks>
    /// Distinct by reference identity, so a body that reads <c>Count</c> in a loop reports one source.
    /// The order is the first read of each, which is what makes a dependency cycle report the path that
    /// closed it rather than an arbitrary pair.
    /// </remarks>
    public IReadOnlyList<IObservableEventSource> Dependencies { get; }

    /// <summary>Whether the body threw.</summary>
    public bool IsFaulted => _fault is not null;

    /// <summary>The exception the body threw, or null when it finished.</summary>
    /// <remarks>
    /// The original instance, with its original stack, and returned rather than thrown so that a caller
    /// that only wants the dependencies never has to catch anything.
    /// </remarks>
    public Exception? Fault => _fault;

    /// <summary>Rethrows <see cref="Fault"/>, if there is one.</summary>
    /// <remarks>
    /// For the form that has no result to throw through. It does nothing when the body finished, so a
    /// caller can call it unconditionally before using whatever it collected.
    /// </remarks>
    public void Throw()
    {
        if (_fault is { } fault)
        {
            ExceptionDispatchInfo.Capture(fault).Throw();
        }
    }
}

/// <summary>
/// What one synchronous run of a body read, what it returned, and whether it threw.
/// </summary>
/// <typeparam name="T">What the body returns.</typeparam>
/// <remarks>
/// The same as <see cref="Collected"/> plus <see cref="Value"/>. Reading <see cref="Value"/> is how the
/// two halves are used together: hook <see cref="Collected.Dependencies"/> first, then read it, so that
/// a body which throws still leaves the caller listening to what it did reach.
/// </remarks>
public sealed class Collected<T> : Collected
{
    private readonly T _value;

    internal Collected(IReadOnlyList<IObservableEventSource> dependencies, Exception? fault, T value)
        : base(dependencies, fault)
    {
        _value = value;
    }

    /// <summary>What the body returned.</summary>
    /// <exception cref="Exception">The body threw, in which case this throws that exception.</exception>
    /// <remarks>
    /// Throws rather than returning a default, so that a caller cannot mistake "the body failed" for "the
    /// body returned null", and has to deal with the failure at the point where it uses the result. The
    /// exception thrown is the instance in <see cref="Collected.Fault"/>, rethrown with its original
    /// stack.
    /// </remarks>
    public T Value
    {
        get
        {
            if (Fault is { } fault)
            {
                ExceptionDispatchInfo.Capture(fault).Throw();
            }

            return _value;
        }
    }
}
