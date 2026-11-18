using System.Collections.Immutable;
using System.Runtime.ExceptionServices;

namespace Kolvir.Observables;

/// <summary>
/// A function, its cached result, and the sources that result was computed from: the implementation
/// behind <see cref="Observable.CreateExpression{T}(Func{T}, string?, IEqualityComparer{T}?, IEqualityComparer{Exception}?)"/>.
/// </summary>
/// <typeparam name="T">What the function returns.</typeparam>
/// <remarks>
/// <para>
/// Internal, and handed out as <see cref="IObservableValue{T}"/>, so the class is not part of the
/// contract. It cannot be written at all: the only way to change an expression is to change something it
/// read, which is the property that makes a graph of them trustworthy.
/// </para>
/// <para>
/// Two states, and the handler list is the whole of the difference. <em>Cold</em> - no handlers - hooks
/// nothing and re-runs on every read, because a cache is only valid while the subscription that
/// maintains it is live. <em>Hot</em> - at least one handler - hooks every dependency its last run read,
/// and a read is a field. Becoming hot evaluates once and raises nothing, since no existing subscriber
/// could have missed a change; becoming cold unhooks everything and keeps the result, which is still
/// readable, just no longer trusted.
/// </para>
/// <para>
/// A run that throws caches the exception in place of the value, and only the dependencies it reached
/// are hooked - see <see cref="Recompute"/> for the ordering that makes all of this work
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
    private ImmutableArray<IDisposable> _dependencies = [];
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
    /// it (section 3.3).
    /// </remarks>
    public T Value
    {
        get
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            Observable.PublishRead(this);

            // Cold: nothing is maintaining the cache, so it is not trusted and this read is a run. The
            // dependencies such a run reads are thrown away, so none are collected - the scope is
            // suspended rather than entered, which costs nothing and also keeps these reads out of
            // whatever run this expression was read from.
            if (_handlers.IsEmpty)
            {
                using (ReadScope.Suspend())
                {
                    return _function();
                }
            }

            return CachedOrThrow();
        }
    }

    /// <inheritdoc/>
    object? IObservableValue.Value => Value;

    /// <inheritdoc/>
    /// <remarks>
    /// The first handler is what makes an expression hot: it evaluates once, hooks what the run read and
    /// raises nothing - the subscriber count went from zero to one, so no earlier subscriber could have
    /// missed a change and this one can read <see cref="Value"/> for the current result. A run that
    /// throws still registers, with the exception cached and its dependencies hooked, so the failure puts
    /// no lifetime decision on a registration call.
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
    /// Removing the last handler makes the expression cold again: it unhooks every dependency and keeps
    /// the result, which stays readable.
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

        RaiseToHandlers();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Unhooks first and then tells the subscribers. Stopping listening is not part of the documented
    /// dead-then-raise order, and it has to come first: with the hooks still live, a handler in the final
    /// raise could write a model, cascade back through one of the dependencies this expression had, and
    /// re-run an expression that is in the middle of dying.
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
            RaiseToHandlers();
        }
        finally
        {
            _handlers = [];
        }
    }

    /// <summary>Named for the diagnostic messages that describe a cycle or a write inside a run.</summary>
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

    /// <summary>Runs the function again, re-collects the dependencies it read, and raises if the result changed.</summary>
    /// <param name="raiseIfChanged">
    /// False only for the run that makes the expression hot, which raises nothing because it has just
    /// gained its first subscriber.
    /// </param>
    /// <remarks>
    /// Three steps, in this order, and the order is load-bearing (section 3.6.1):
    /// <list type="number">
    /// <item>Unhook the old set. By the time the function runs, nothing in the old set reaches this
    /// expression, so a value the run is about to stop reading cannot start a second evaluation of it
    /// while the first is still collecting.</item>
    /// <item>Run the function. A run that throws still reports the sources it reached, and those are
    /// exactly the ones that can make its exception go away.</item>
    /// <item>Hook the new set - whether or not the result changed, because a conditional read can move
    /// the set without moving the result - and then raise if the result differs.</item>
    /// </list>
    /// </remarks>
    private void Recompute(bool raiseIfChanged)
    {
        var previousValue = _value;
        var previousFault = _fault;
        var hadResult = _hasResult;

        UnhookAll();

        var run = Observable.CollectDependencies(_function);
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

        // Nothing is hooked if the run lost the last handler: a cold expression hooks nothing, and the
        // removal that made it cold has already unhooked what this run would have replaced.
        if (!_handlers.IsEmpty)
        {
            Hook(run.Dependencies);
        }

        if (raiseIfChanged && changed)
        {
            RaiseToHandlers();
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

    /// <summary>Hooks every source this run read.</summary>
    /// <remarks>
    /// Added one at a time rather than built and assigned, so that a source whose registration throws
    /// cannot leave the ones already taken unreachable - they stay in the field and the next unhook or
    /// disposal releases them.
    /// </remarks>
    private void Hook(IReadOnlyList<IObservableEventSource> dependencies)
    {
        foreach (var dependency in dependencies)
        {
            _dependencies = _dependencies.Add(dependency.AddObservableEventRaisedHandler(OnDependencyRaised));
        }
    }

    /// <summary>Releases every hook, clearing the field before disposing any of them.</summary>
    private void UnhookAll()
    {
        var tokens = _dependencies;
        _dependencies = [];

        foreach (var token in tokens)
        {
            token.Dispose();
        }
    }

    /// <summary>What a hooked dependency calls when it changes.</summary>
    private void OnDependencyRaised(IObservableEventSource dependency) => Recompute(raiseIfChanged: true);

    /// <summary>Calls every handler the array held when this raise began.</summary>
    private void RaiseToHandlers()
    {
        var handlers = _handlers;

        foreach (var handler in handlers)
        {
            handler(this);
        }
    }
}
