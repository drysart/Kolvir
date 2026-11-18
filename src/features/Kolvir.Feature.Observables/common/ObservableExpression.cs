using System.Collections.Immutable;
using System.Runtime.ExceptionServices;

namespace Kolvir.Observables;

/// <summary>
/// A function, its cached result, and the run that subscribed it to what the result was computed from.
/// </summary>
/// <typeparam name="T">What the function returns.</typeparam>
/// <remarks>
/// <para>
/// Internal, and handed out as <see cref="IObservableValue{T}"/>, so the class is not part of the
/// contract. It cannot be written at all: the only way to change an expression is to change something it
/// read, which is the property that makes a graph of them trustworthy.
/// </para>
/// <para>
/// Two states, and the handler list is the whole of the difference. <em>Cold</em> - no handlers - is not
/// listening to anything and cannot be, so every read is a run, because a cache is only valid while the
/// subscription that maintains it is live. <em>Hot</em> - at least one handler - holds the
/// <see cref="Collected"/> of its last run, whose subscriptions are what keep the cache honest, so a read
/// is a field. Becoming hot evaluates once and raises nothing, since no existing subscriber could have
/// missed a change; becoming cold releases the run and keeps the result, which stays readable, just no
/// longer trusted.
/// </para>
/// <para>
/// A run that throws caches the exception in place of the value, and is subscribed only to what it
/// reached - see <see cref="Recompute"/> for the ordering that makes all of this work
/// (dev-docs/OBSERVABLES.md sections 3.6 to 3.7).
/// </para>
/// </remarks>
internal sealed class ObservableExpression<T> : IObservableValue<T>
{
    private readonly Func<T> _function;
    private readonly IEqualityComparer<T> _valueComparer;
    private readonly IEqualityComparer<Exception>? _exceptionComparer;
    private readonly string _name;

    private ImmutableArray<Action<IObservableEventSource>> _handlers = [];
    private Collected? _hooked;
    private T _value = default!;
    private Exception? _fault;
    private bool _hasResult;
    private bool _isDisposed;

    internal ObservableExpression(
        Func<T> function,
        IEqualityComparer<T>? valueComparer,
        IEqualityComparer<Exception>? exceptionComparer,
        string name)
    {
        _function = function;
        _valueComparer = valueComparer ?? EqualityComparer<T>.Default;
        _exceptionComparer = exceptionComparer;
        _name = name;
    }

    /// <inheritdoc/>
    public bool IsDisposed => _isDisposed;

    /// <inheritdoc/>
    /// <remarks>
    /// The read publishes <em>this</em> expression, not what it reads, which is what keeps nested runs
    /// isolated: an outer function that reads this one depends on it and hears about any change through
    /// it (section 3.3). And because a scope subscribes as it reads, a cold expression read from inside
    /// somebody else's run is hot by the time this returns - the value handed back is the one it keeps.
    /// </remarks>
    public T Value
    {
        get
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            Observable.PublishRead(this);

            // Cold, which now means "read with no run in progress": nothing is listening, so nothing is
            // maintaining the cache and this read is a run. There is no scope to report to either - if
            // there were, the read above would have made this expression hot - so nothing is collected.
            if (_handlers.IsEmpty)
            {
                return _function();
            }

            return CachedOrThrow();
        }
    }

    /// <inheritdoc/>
    object? IObservableValue.Value => Value;

    /// <inheritdoc/>
    /// <remarks>
    /// The first handler is what makes an expression hot: it evaluates once, subscribes that run to what
    /// it read, and raises nothing - the subscriber count went from zero to one, so no earlier subscriber
    /// could have missed a change and this one can read <see cref="Value"/> for the current result. A run
    /// that throws still registers, with the exception cached and its subscriptions in place, so a
    /// failure puts no lifetime decision on a registration call.
    /// </remarks>
    public IDisposable AddObservableEventRaisedHandler(Action<IObservableEventSource> handler)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(handler);

        var wasCold = _handlers.IsEmpty;
        _handlers = _handlers.Add(handler);

        if (wasCold)
        {
            Recompute(raiseIfChanged: false);
        }

        return new HandlerRegistration(this, handler);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Removing the last handler makes the expression cold again: it releases every subscription and
    /// keeps the result, which stays readable.
    /// </remarks>
    public void RemoveObservableEventRaisedHandler(Action<IObservableEventSource> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var wasHot = !_handlers.IsEmpty;
        _handlers = _handlers.Remove(handler);

        if (wasHot && _handlers.IsEmpty)
        {
            UnhookAll();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The event an expression raises when its own result changes, which it does itself; calling this
    /// announces the cached result to the subscribers without re-running the function.
    /// </remarks>
    public void RaiseEvent()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        Observable.RaiseToHandlers(this, _handlers);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Releases the run first and then tells the subscribers. Stopping listening is not part of the
    /// documented dead-then-raise order, and it has to come first: with the subscriptions still live, a
    /// handler in the final raise could write a model and cascade back into an expression that is in the
    /// middle of dying.
    /// </remarks>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        UnhookAll();

        try
        {
            Observable.RaiseToHandlers(this, _handlers);
        }
        finally
        {
            _handlers = [];
        }
    }

    /// <summary>Named for the diagnostic messages that describe how a chain came back to an expression.</summary>
    public override string ToString() => _name;

    /// <summary>The cached result, or the exception standing in for it.</summary>
    private T CachedOrThrow()
    {
        if (_fault is { } fault)
        {
            ExceptionDispatchInfo.Capture(fault).Throw();
        }

        return _value;
    }

    /// <summary>Runs the function again, re-subscribes, and raises if the result changed.</summary>
    /// <param name="raiseIfChanged">
    /// False only for the run that makes the expression hot, which raises nothing because it has just
    /// gained its first subscriber.
    /// </param>
    /// <remarks>
    /// Three steps, in this order, and the order is load-bearing (section 3.6.1):
    /// <list type="number">
    /// <item>Release the previous run. By the time the function executes, nothing it used to read reaches
    /// this expression, so a value the run is about to stop reading cannot start a second evaluation of it
    /// while the first is still collecting.</item>
    /// <item>Run the function, which subscribes to each source as the run reads it. A run that throws
    /// still ends up subscribed to what it reached and to nothing else - those are exactly the sources
    /// that can make its exception go away.</item>
    /// <item>Keep the new run whether or not the result changed, because a conditional read can move the
    /// set without moving the result - and then raise if the result differs.</item>
    /// </list>
    /// </remarks>
    private void Recompute(bool raiseIfChanged)
    {
        var previousValue = _value;
        var previousFault = _fault;
        var hadResult = _hasResult;

        UnhookAll();

        var run = Observable.CollectDependencies(_function, OnDependencyRaised);

        var changed = !hadResult || ChangedFrom(previousValue, previousFault, run);

        _hasResult = true;
        if (run.IsFaulted)
        {
            _fault = run.Fault;
            _value = default!;
        }
        else
        {
            _fault = null;
            _value = run.Value;
        }

        // Keeping the run is what keeps the subscriptions. An expression that lost its last handler during
        // its own run is cold again, and a cold expression listens to nothing - so the run is released
        // rather than held.
        if (_handlers.IsEmpty)
        {
            run.Dispose();
        }
        else
        {
            _hooked = run;
        }

        if (raiseIfChanged && changed)
        {
            Observable.RaiseToHandlers(this, _handlers);
        }
    }

    /// <summary>Whether <paramref name="run"/> produced a result different from the one before it.</summary>
    /// <remarks>
    /// A value and an exception are never equal, and two exceptions are equal only when the expression was
    /// given a comparer that says so: a reader can branch on an exception's type, message or inner
    /// exception, so "still failing, differently" is a different result and subscribing to it has to say
    /// so. Suppressing the repeat affects the notification only - the new exception still replaces the
    /// cached one, so a read after a suppressed raise throws the newest.
    /// </remarks>
    private bool ChangedFrom(T previousValue, Exception? previousFault, Collected<T> run)
    {
        if (run.IsFaulted)
        {
            return previousFault is null || !(_exceptionComparer?.Equals(previousFault, run.Fault!) ?? false);
        }

        if (previousFault is not null)
        {
            return true;
        }

        return !_valueComparer.Equals(previousValue, run.Value);
    }

    /// <summary>Releases the subscriptions the last run took, if it is still held.</summary>
    private void UnhookAll()
    {
        var hooked = _hooked;
        _hooked = null;
        hooked?.Dispose();
    }

    /// <summary>What a subscribed dependency calls when it changes.</summary>
    /// <remarks>
    /// A change that comes back here while this expression is still running its own function is a loop,
    /// and it is the raise chain - not this method - that stops it: each turn of the loop is another
    /// raise, nested inside the last, until <see cref="Observable.MaxEventStackSize"/> refuses one.
    /// </remarks>
    private void OnDependencyRaised(IObservableEventSource dependency) => Recompute(raiseIfChanged: true);
}