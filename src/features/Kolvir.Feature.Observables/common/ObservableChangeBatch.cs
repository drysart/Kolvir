using System.Collections.Immutable;

namespace Kolvir.Feature.Observables;

/// <summary>
/// One collection's changes in flight: the records that have not been delivered, how deep the open
/// batches are, and the handlers they are delivered to.
/// </summary>
/// <typeparam name="TChange">The collection's record type.</typeparam>
/// <typeparam name="TArgs">The collection's event arguments.</typeparam>
/// <remarks>
/// <para>
/// The half of a collection that has nothing to do with what it holds: every rule about how a change
/// reaches a handler lives here, and a collection supplies only its records and its marking
/// (<see cref="IObservableChangeTarget{TChange, TArgs}"/>, dev-docs/OBSERVABLE-COLLECTIONS.md
/// section 5).
/// </para>
/// <para>
/// Two pieces of state. <see cref="_pending"/> holds records that have not been delivered: the open
/// batch's, or a handler-caused change waiting for the delivery that is running to finish.
/// <see cref="_isDelivering"/> says a delivery is in progress, which is what makes a change from inside
/// a handler queue rather than nest - and that queue is what keeps the chain flat, which is why nothing
/// here needs a depth limit (section 3.7).
/// </para>
/// </remarks>
internal sealed class ObservableChangeBatch<TChange, TArgs> : IDisposable
    where TChange : struct
    where TArgs : EventArgs
{
    private readonly IObservableChangeTarget<TChange, TArgs> _target;
    private readonly List<TChange> _pending = [];

    private ImmutableArray<Action<TArgs>> _handlers = [];
    private bool _isDisposed;
    private bool _isDelivering;
    private int _batchDepth;
    private int _maxCollectionBatchSize = Observable.Default.MaxCollectionBatchSize;

    internal ObservableChangeBatch(IObservableChangeTarget<TChange, TArgs> target) => _target = target;

    /// <summary>Whether the collection has been disposed.</summary>
    /// <remarks>
    /// Set before the final event, so a handler that reads the collection from there gets
    /// <see cref="ObjectDisposedException"/> rather than a last state that will never change again.
    /// </remarks>
    internal bool IsDisposed => _isDisposed;

    /// <summary>How many batches are open. Zero outside any batch.</summary>
    internal int ChangeBatchDepth => _batchDepth;

    /// <summary>How many records one event may carry before it stops describing them.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than one.</exception>
    internal int MaxCollectionBatchSize
    {
        get => _maxCollectionBatchSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxCollectionBatchSize = value;
        }
    }

    /// <summary>Registers a handler for the collection's events.</summary>
    /// <param name="handler">What one delivery calls.</param>
    /// <returns>What unregisters it.</returns>
    /// <exception cref="ObjectDisposedException">The collection is disposed.</exception>
    internal IDisposable AddHandler(Action<TArgs> handler)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, _target);

        // Copy-on-write, which is what makes a delivery a snapshot without copying it.
        _handlers = _handlers.Add(handler);
        return new HandlerToken(this, handler);
    }

    /// <summary>Unregisters a handler. Tolerant, like every other removal here.</summary>
    internal void RemoveHandler(Action<TArgs> handler) => _handlers = _handlers.Remove(handler);

    /// <summary>Records one change, and delivers it unless a batch or a delivery is holding it.</summary>
    /// <param name="change">What happened.</param>
    /// <remarks>
    /// <para>
    /// A pending event that has already given up on detail needs nothing appended to it: one
    /// untracked record tells the observer to re-read the collection, and it says that whatever came
    /// before it in this batch. That is what keeps a bulk load from growing this list without bound
    /// (section 3.11).
    /// </para>
    /// <para>
    /// Inside a batch the records wait for the outermost end; inside a delivery they wait for the event
    /// being delivered to finish, which is what keeps the observer's history in order.
    /// </para>
    /// </remarks>
    internal void Record(in TChange change)
    {
        var alreadyUntracked = false;

        if (_pending.Count == 1)
        {
            var first = _pending[0];
            alreadyUntracked = _target.IsUntracked(in first);
        }

        if (!alreadyUntracked)
        {
            _pending.Add(change);

            if (_pending.Count > _maxCollectionBatchSize)
            {
                _pending.Clear();
                _pending.Add(_target.Untracked);
            }
        }

        if (_batchDepth == 0)
        {
            Drain();
        }
    }

    /// <summary>Stops delivering until the matching <see cref="EndChangeBatch"/>.</summary>
    internal void BeginChangeBatch() => _batchDepth++;

    /// <summary>Ends a batch, delivering what it collected when it was the outermost one.</summary>
    /// <exception cref="InvalidOperationException">No batch is open.</exception>
    internal void EndChangeBatch()
    {
        if (_batchDepth == 0)
        {
            throw new InvalidOperationException(
                "EndChangeBatch was called with no batch open. A batch is begun and ended by the same "
                + "caller, in that order, and nesting is counted rather than ignored.");
        }

        _batchDepth--;

        if (_batchDepth == 0 && _pending.Count > 0)
        {
            Drain();
        }
    }

    /// <summary>Drops the pending records, delivers one last empty event, and lets the handlers go.</summary>
    /// <remarks>
    /// In this order because each step is what the next one is allowed to assume: the records that had
    /// not been delivered are dropped, because they describe a collection nobody may read; then one
    /// last event with no changes, which is how a registrant learns to let go; then the handler list and
    /// the dependency subscriptions, in a <c>finally</c>, because a leak is the one outcome this method
    /// exists to prevent.
    /// </remarks>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _pending.Clear();
        _batchDepth = 0;

        try
        {
            var handlers = _handlers;

            if (!handlers.IsEmpty)
            {
                var final = _target.CreateArgs(Array.Empty<TChange>());

                // A dispatch like any other, and counted like one: it is reached from a handler often
                // enough that it is one of the two ways a delivery nests.
                using (Observable.EnterDelivery(_target))
                {
                    foreach (var handler in handlers)
                    {
                        handler(final);
                    }
                }
            }
        }
        finally
        {
            _handlers = [];
            _target.ReleaseDependencies();
        }
    }

    /// <summary>Delivers what is pending, one event at a time, until nothing is left.</summary>
    /// <remarks>
    /// <para>
    /// A loop rather than a recursion, so a handler that changes the collection costs a queue entry and
    /// not a stack frame. A drain started from inside a drain returns immediately and leaves the work to
    /// the one already running, which is what preserves the order.
    /// </para>
    /// <para>
    /// The loop itself is unbounded, and it should be: a handler that changes the collection it is
    /// hearing about costs a queue entry, not a frame, so a chain of them cannot exhaust the stack -
    /// <see cref="Observable.MaxEventStackSize"/> has nothing to say about a loop that never descends.
    /// Such a handler is doing real work every time round and ends when it stops changing things; one
    /// that never stops is an ordinary infinite loop in application code, the kind <c>while (true)</c>
    /// is, and a work limit here would only turn it into an exception that discards changes an observer
    /// was promised.
    /// </para>
    /// <para>
    /// The dispatch each iteration makes is a different matter, and it is counted - see
    /// <see cref="Deliver"/>, because a delivery can be re-entered even when a change to this collection
    /// cannot.
    /// </para>
    /// </remarks>
    private void Drain()
    {
        if (_isDelivering || _pending.Count == 0)
        {
            return;
        }

        _isDelivering = true;

        try
        {
            while (_pending.Count > 0)
            {
                Deliver();
            }
        }
        finally
        {
            _isDelivering = false;
        }
    }

    private void Deliver()
    {
        var handlers = _handlers;

        // Taken before any handler runs: it is the size the records being delivered left behind, and a
        // handler's own change belongs to the next event, so counting it here would put the wrong bound
        // on an index-shaped mark.
        var size = _target.Size;

        if (handlers.IsEmpty)
        {
            // Nobody to tell, but the dependencies still have to know: a render that reads this
            // collection is watching one of them and not the event.
            _target.Mark(_pending, size);
            _pending.Clear();
            _target.RaiseDependencies();
            return;
        }

        var records = _pending.ToArray();
        _pending.Clear();

        var args = _target.CreateArgs(records);

        // The handlers first, then the reads they invalidated: a handler is the listener this change is
        // being reported to, and a dependency raise is the consequence of it.
        //
        // Counted against the evaluation depth, because this dispatch can be re-entered even though a
        // change to *this* collection is queued: a handler that disposes a collection raises that
        // collection's final event inside this frame, and one that changes a different collection starts
        // that one's drain here. Those nest, so they are stack, and a chain of them is what the guard is
        // for.
        using (Observable.EnterDelivery(_target))
        {
            foreach (var handler in handlers)
            {
                handler(args);
            }
        }

        _target.Mark(records, size);
        _target.RaiseDependencies();
    }

    /// <summary>
    /// What unregisters one handler, once. A collection's registrations are subscriptions rather than
    /// claims, and a token that outlives its collection holds neither the collection nor the closure.
    /// </summary>
    private sealed class HandlerToken : IDisposable
    {
        private ObservableChangeBatch<TChange, TArgs>? _batch;
        private Action<TArgs>? _handler;

        internal HandlerToken(ObservableChangeBatch<TChange, TArgs> batch, Action<TArgs> handler)
        {
            _batch = batch;
            _handler = handler;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            var batch = _batch;
            var handler = _handler;

            if (batch is null || handler is null)
            {
                return;
            }

            _batch = null;
            _handler = null;

            batch.RemoveHandler(handler);
        }
    }
}
