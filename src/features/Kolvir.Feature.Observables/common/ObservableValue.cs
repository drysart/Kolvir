using System.Collections.Immutable;

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
/// <para>
/// The handlers are an <see cref="ImmutableArray{T}"/>, so a raise is a snapshot for free: subscribing
/// or unsubscribing replaces the array rather than editing it, and the raise that is running keeps
/// walking the array it started with. Subscribing copies, which happens once per registration;
/// raising copies nothing, which happens on every change - the right way round, and iteration over an
/// immutable array is a struct enumerator over contiguous memory.
/// </para>
/// </remarks>
internal sealed class ObservableValue<T> : IObservableMutableValue<T>
{
    private T _value;
    private ImmutableArray<Action<IObservableEventSource>> _handlers = [];
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

            // A write from inside a function is refused rather than raised: it would re-enter the run
            // that is collecting, and the set that run is halfway through would no longer describe what
            // happened.
            ReadScope.EnsureNotCollecting("An observable value was assigned");

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

        // Copy-on-write, which is what makes the raise a snapshot without copying it: the array a raise
        // is walking is the one it began with, and this registration belongs to the next raise.
        _handlers = _handlers.Add(handler);
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

        // Once per registration: the same handler subscribed twice needs both tokens released, and this
        // removes one of the two. Replaces the array rather than editing it, for the reason above.
        _handlers = _handlers.Remove(handler);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Raising is what a write does; this is the same notification for a source that has something else
    /// to announce - a collection raising a key's dependency, a hand-written source on a timer tick.
    /// </remarks>
    public void RaiseEvent()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ReadScope.EnsureNotCollecting("An observable event was raised");

        RaiseToHandlers();
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
            RaiseToHandlers();
        }
        finally
        {
            _handlers = [];
        }
    }

    /// <summary>Calls every handler the array held when this raise began.</summary>
    /// <remarks>
    /// The local is the snapshot, and reading it is all the snapshot costs: because the array is
    /// immutable, a handler that subscribes or unsubscribes - itself, another handler, or everything by
    /// disposing the value - replaces the field rather than changing what this walk is reading. So
    /// mutations apply to the next raise, and a handler that disposes the value mid-dispatch does not
    /// shorten this one; the remaining handlers are already in the array being walked.
    /// </remarks>
    private void RaiseToHandlers()
    {
        var handlers = _handlers;

        foreach (var handler in handlers)
        {
            handler(this);
        }
    }
}
