using System.Runtime.ExceptionServices;

namespace Kolvir.Observables;

/// <summary>
/// What one synchronous run read, whether it threw, and the subscriptions taken on its behalf.
/// </summary>
/// <remarks>
/// <para>
/// The result of <c>Observable.CollectDependencies</c> over a body with no value. It is both halves of
/// what a run is for: the sources the body read, and a live subscription to each of them - taken at the
/// moment the read happened rather than after the body finished. So a caller holds this and holds the
/// listening; there is no second step where the set is walked and hooked, and no window in which the body
/// has read something and is not yet watching it.
/// </para>
/// <para>
/// That is why this is <see cref="IDisposable"/>, and disposing it is how the listening stops: the next
/// run replaces it, and a caller whose run is over disposes it. Disposing is safe more than once, and
/// safe after the sources it subscribed to are gone.
/// </para>
/// <para>
/// The set comes back even when the body threw, and so does the subscription to whatever it reached
/// before throwing. Those are exactly the sources that can make its exception go away, so a failed run is
/// still watching the things that could revive it - which is what stops a render that threw from being
/// stranded with no way back.
/// </para>
/// <para>
/// Nothing is reported anywhere. This is a synchronous call with somebody waiting, so the exception
/// belongs to the caller and the library has no channel to send it to as well
/// (dev-docs/OBSERVABLES.md sections 4 and 8.1).
/// </para>
/// </remarks>
public class Collected : IDisposable
{
    private readonly Exception? _fault;
    private IReadOnlyList<IDisposable>? _tokens;

    internal Collected(
        IReadOnlyList<IObservableEventSource> dependencies,
        IReadOnlyList<IDisposable> tokens,
        Exception? fault)
    {
        Dependencies = dependencies;
        _tokens = tokens;
        _fault = fault;
    }

    /// <summary>The distinct sources the body read, in the order it first read them.</summary>
    /// <remarks>
    /// Distinct by reference identity, so a body that reads <c>Count</c> in a loop reports one source.
    /// The order is the first read of each, which is the order the body reached them - the useful order
    /// to report a run's inputs in, and the order a test asserts against. Every source here is already
    /// subscribed.
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

    /// <summary>Stops listening: releases every subscription this run took.</summary>
    /// <remarks>
    /// What a caller does when this run's result is no longer what it wants to hear about - the next
    /// render in place of this one, or a detach. Idempotent, and tolerant of a source that has already
    /// been disposed, because a teardown path releases what it holds without checking what state it is
    /// in.
    /// </remarks>
    public void Dispose()
    {
        var tokens = _tokens;
        _tokens = null;

        if (tokens is null)
        {
            return;
        }

        foreach (var token in tokens)
        {
            token.Dispose();
        }
    }
}

/// <summary>
/// What one synchronous run read, what it returned, and the subscriptions taken on its behalf.
/// </summary>
/// <typeparam name="T">What the body returns.</typeparam>
/// <remarks>
/// The same as <see cref="Collected"/> plus <see cref="Value"/>. Reading <see cref="Value"/> is where a
/// failure arrives, and by then the subscriptions are already in place - so a body that throws still
/// leaves its caller listening to what it did reach.
/// </remarks>
public sealed class Collected<T> : Collected
{
    private readonly T _value;

    internal Collected(
        IReadOnlyList<IObservableEventSource> dependencies,
        IReadOnlyList<IDisposable> tokens,
        Exception? fault,
        T value)
        : base(dependencies, tokens, fault)
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
