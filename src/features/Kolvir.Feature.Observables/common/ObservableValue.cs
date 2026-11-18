namespace Kolvir.Observables;

/// <summary>
/// The one implementation of a standalone observable value: a box, a handler list and a dead flag.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <remarks>
/// <para>
/// Internal, and handed out as <see cref="IObservableMutableValue{T}"/> and
/// <see cref="IObservableValue{T}"/>, so the class itself is not part of the contract and can change.
/// </para>
/// <para>
/// Nothing here needs disposing for memory. A value with no handlers roots nothing - the handler list
/// is all it holds - so dropping the last reference to an unwatched value collects it with no
/// bookkeeping anywhere, which is what makes an observable safe to use as plain model state.
/// <see cref="Dispose"/> exists for the other problem: a long-lived value that a dead screen's handler
/// is still registered on would keep that screen's state alive for the life of the process.
/// </para>
/// </remarks>
internal sealed class ObservableValue<T> : IObservableMutableValue<T>
{
    private T _value;
    private List<Action<IObservableEventSource>>? _handlers;
    private bool _isDisposed;

    internal ObservableValue(T value) => _value = value;

    /// <inheritdoc/>
    public bool IsDisposed => _isDisposed;

    /// <inheritdoc/>
    public T Value
    {
        get
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            Observable.PublishRead(this);
            return _value;
        }

        set
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            // An event means something a listener cares about is different, so a write that changes
            // nothing is not one.
            if (EqualityComparer<T>.Default.Equals(_value, value))
            {
                return;
            }

            _value = value;
            RaiseEvent();
        }
    }

    /// <inheritdoc/>
    object? IObservableValue.Value => Value;

    /// <inheritdoc/>
    public IDisposable AddObservableEventRaisedHandler(Action<IObservableEventSource> handler)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(handler);

        (_handlers ??= []).Add(handler);
        return new HandlerRegistration(this, handler);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Tolerant on purpose: an unregistered handler is not an error, and neither is any call at all
    /// once the value is disposed. That is what lets a token be disposed after the value rather than
    /// before it, which is the common shape of a teardown path.
    /// </remarks>
    public void RemoveObservableEventRaisedHandler(Action<IObservableEventSource> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handlers?.Remove(handler);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Raising is what a write does; this is the same notification for a source that has something else
    /// to announce - a collection raising a key's dependency, a hand-written source on a timer tick.
    /// </remarks>
    public void RaiseEvent()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_handlers is { Count: > 0 } handlers)
        {
            RaiseToSnapshot(handlers);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Ordered, and the order is the behaviour: dead first, so a handler that reads
    /// <see cref="Value"/> here gets <see cref="ObjectDisposedException"/> rather than a last value that
    /// will never update again; then one raise, to the handlers still registered, so a registrant learns
    /// its dependency is gone instead of quietly ceasing to hear from it; then the list is cleared,
    /// which is what drops the registrant's closure and the reason any of this exists.
    /// <para>
    /// The list is released even when a handler throws, because a leak is the one outcome this method
    /// exists to prevent. Nothing is caught: the first handler that throws still ends the notification
    /// and its exception still travels out of <see cref="Dispose"/>, exactly as it would out of
    /// <see cref="RaiseEvent"/>.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        try
        {
            if (_handlers is { Count: > 0 } handlers)
            {
                RaiseToSnapshot(handlers);
            }
        }
        finally
        {
            _handlers = null;
        }
    }

    /// <summary>Calls every handler the list held when the raise began.</summary>
    /// <remarks>
    /// A snapshot and not the live list: a handler may subscribe and unsubscribe freely, including
    /// itself, and none of that may disturb the raise that is running - mutations apply to the next one.
    /// The snapshot also means a handler that disposes the source mid-dispatch does not shorten the
    /// walk; the remaining handlers are already in the list being read.
    /// </remarks>
    private void RaiseToSnapshot(List<Action<IObservableEventSource>> handlers)
    {
        foreach (var handler in handlers.ToArray())
        {
            handler(this);
        }
    }
}
