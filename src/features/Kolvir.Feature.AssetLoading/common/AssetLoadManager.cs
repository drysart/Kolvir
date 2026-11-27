namespace Kolvir.Feature.AssetLoading;

/// <summary>
/// The queue behind <see cref="IAssetLoadManager"/>.
/// </summary>
/// <remarks>
/// <para>
/// Two structures, each doing one job: a dictionary keyed by handle identity, which is how a second
/// submission of a live handle is rejected and how a notification that arrives with only a sender is
/// resolved back to its request, and an ordered list of the queued requests, which is the queue
/// itself. Running requests live in the dictionary only - they are no longer in the queue, and the
/// snapshot finds them there too.
/// </para>
/// <para>
/// The walk is the whole of the behaviour, and it is written as an order of operations because the
/// interaction between it and the callbacks is where a queue like this goes wrong
/// (dev-docs/ASSET-LOADING.md section 5). It is re-entrancy safe by a flag pair rather than by
/// recursion: a loader can complete synchronously, so a walk's own "start this one" can re-enter it,
/// and a handler raised inside a walk can enqueue. The inner call sets the second flag and returns;
/// the outer walk repeats when it finishes (section 3.8).
/// </para>
/// <para>
/// Nothing here polls: a walk happens on enqueue, on a priority change, on an availability change and
/// on a completion that frees a slot, and those are the only state changes there are
/// (section 3.7).
/// </para>
/// </remarks>
internal sealed class AssetLoadManager : IAssetLoadManager
{
    /// <summary>
    /// Every request the manager is holding, keyed by handle identity rather than by equality: two
    /// handles for one URI are two requests, and a loader type that overrode <c>Equals</c> would
    /// otherwise collapse them into one.
    /// </summary>
    private readonly Dictionary<IAssetLoader, AssetLoadRequest> _live = new(ReferenceEqualityComparer.Instance);

    /// <summary>The queued requests, kept in the order the walk considers them.</summary>
    private readonly List<AssetLoadRequest> _queue = [];

    private long _nextSequence;
    private bool _walking;
    private bool _walkAgain;

    /// <inheritdoc/>
    public Task<TResult> LoadAsync<TResult>(
        IAssetLoader<TResult> loader,
        Action? onStarted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(loader);
        EnsureUiThread();

        if (_live.ContainsKey(loader))
        {
            throw new InvalidOperationException(
                "This loader already has a request outstanding, and a handle is for one request at a " +
                "time. Make a second handle for a second request, or wait for this one to finish: an " +
                "instance may be submitted again once its request has ended.");
        }

        var request = new AssetLoadRequest<TResult>(loader, onStarted, _nextSequence++, cancellationToken);

        // Subscribed before the request is inserted, and released on every exit path. Nothing can fire
        // between the two on one thread, so the ordering only matters if a loader chooses to raise a
        // notification from its own Add method - which would find no request to act on rather than a
        // half-built one.
        request.PrioritySubscription = loader.AddPriorityChangedHandler(OnPriorityChanged);
        request.CanLoadNowSubscription = loader.AddCanLoadNowChangedHandler(OnCanLoadNowChanged);

        _live.Add(loader, request);
        _queue.Insert(IndexFor(request), request);

        // A token that is already cancelled runs this callback inline, on the queued path: nothing
        // was started, so there is nothing to stop, and the caller is answered cancelled before this
        // method returns.
        request.CancellationRegistration = cancellationToken.Register(() => OnCallerCancelled(request));

        if (request.IsFinished)
        {
            // The registration above fired before it could be stored on the request, so the path that
            // answered the caller could not release it. This is the one place that has to.
            request.DetachCancellation();
        }

        Walk();

        return request.Completion;
    }

    /// <inheritdoc/>
    public IReadOnlyList<AssetLoadSnapshot> Snapshot()
    {
        EnsureUiThread();

        var snapshots = new List<AssetLoadSnapshot>(_live.Count);

        foreach (AssetLoadRequest request in _live.Values)
        {
            snapshots.Add(new AssetLoadSnapshot(
                request.Loader,
                request.Loader.Priority,
                request.Sequence,
                request.Phase,
                request.Loader.CanLoadNow));
        }

        snapshots.Sort(static (left, right) =>
        {
            int byPriority = right.Priority.CompareTo(left.Priority);

            return byPriority != 0 ? byPriority : left.Sequence.CompareTo(right.Sequence);
        });

        return snapshots;
    }

    /// <summary>
    /// Walks the queue, or - when this is called from inside a walk - asks the walk in progress to
    /// walk once more before it finishes.
    /// </summary>
    /// <remarks>
    /// Recursing here is not merely unbounded: the inner walk would start loads the outer walk has
    /// already decided against, and would do it over the list the outer one is walking. A second flag
    /// keeps the chain flat, and the loop is what makes a nested request (a synchronous completion, an
    /// enqueue from a handler, a gate that opened mid-walk) start in the same turn.
    /// </remarks>
    private void Walk()
    {
        if (_walking)
        {
            _walkAgain = true;
            return;
        }

        _walking = true;

        try
        {
            do
            {
                _walkAgain = false;
                StartWhatCanStart();
            }
            while (_walkAgain);
        }
        finally
        {
            _walking = false;
        }
    }

    /// <summary>
    /// Starts the highest-priority queued request whose loader says it may start, and repeats until
    /// none can.
    /// </summary>
    /// <remarks>
    /// The scan restarts from the top of the queue after every start rather than continuing past it.
    /// That is not a micro-optimisation missed: a start can change the gates - the count moves at
    /// submission, so the gate read after a start is meant to reflect it (section 4.2) - and a
    /// completion that happened synchronously inside that start has already changed them again. The
    /// queue is tens of entries, so the scan is cheap, and it is the shape that is correct first
    /// (section 3.7 names the index to add if it ever stops being cheap enough).
    /// </remarks>
    private void StartWhatCanStart()
    {
        while (true)
        {
            AssetLoadRequest? next = null;

            foreach (AssetLoadRequest request in _queue)
            {
                // A closed gate says nothing about the next request's gate: with per-domain limits
                // the next entry is often the one that can start (section 3.2).
                if (request.Loader.CanLoadNow)
                {
                    next = request;
                    break;
                }
            }

            if (next is null)
            {
                return;
            }

            _queue.Remove(next);
            Start(next);
        }
    }

    /// <summary>
    /// Asks one request's loader to load, raises its started callback, and starts observing its task.
    /// </summary>
    /// <param name="request">The request, already out of the queue.</param>
    private void Start(AssetLoadRequest request)
    {
        request.Phase = AssetLoadPhase.Running;

        Task<object?> underlying;

        try
        {
            underlying = request.Loader.LoadAsync(request.CancellationToken);

            // Raised here: after the task exists, and before a continuation is attached to it, so a
            // loader that completes synchronously cannot deliver "loaded" before "started" (2.4).
            request.OnStarted?.Invoke();
        }
        catch (Exception error)
        {
            // A loader that throws on the way to a task, or a caller's started callback that throws,
            // fails the request like any other loader failure. There is no continuation to attach, so
            // the request is answered here and the walk continues with the rest of the queue.
            Fail(request, error);
            return;
        }

        Observe(request, underlying);
    }

    /// <summary>
    /// Waits for the loader's task and reports its outcome on the thread that finished it.
    /// </summary>
    /// <param name="request">The request the task belongs to.</param>
    /// <param name="underlying">The loader's task.</param>
    /// <remarks>
    /// <para>
    /// The await resumes on the ambient context rather than a pool thread, which is what
    /// <see cref="UiSynchronizationContext"/> supplies for the duration of <see cref="Application.Run()"/>:
    /// the manager does not post, it needs the context a UI application already has (section 3.12).
    /// </para>
    /// <para>
    /// The returned task is not awaited by anyone - every outcome is handled inside, so it never
    /// faults - and it is deliberately not stored: the request is the thing that outlives this call,
    /// and a cancellation keeps it here until the loader's task really ends (section 3.9).
    /// </para>
    /// </remarks>
    private void Observe(AssetLoadRequest request, Task<object?> underlying) =>
        _ = ObserveAsync(request, underlying);

    private async Task ObserveAsync(AssetLoadRequest request, Task<object?> underlying)
    {
        object? result = null;
        Exception? error = null;

        try
        {
            result = await underlying;
        }
        catch (Exception exception)
        {
            error = exception;
        }

        OnUnderlyingCompleted(request, result, error);
    }

    /// <summary>
    /// Ends one request: releases what it held, answers whoever is still waiting, and walks again
    /// because a slot the loader was counting has just come free.
    /// </summary>
    /// <param name="request">The request whose task ended.</param>
    /// <param name="result">What it produced, when it produced anything.</param>
    /// <param name="error">What it threw, when it threw.</param>
    private void OnUnderlyingCompleted(AssetLoadRequest request, object? result, Exception? error)
    {
        Finish(request);

        if (!request.CallerDone)
        {
            // Released before the caller is answered, so a continuation that looks at this manager
            // cannot see a request the walk no longer holds (3.10).
            request.CallerDone = true;

            if (error is null)
            {
                try
                {
                    request.HandOver(result);
                }
                catch (InvalidCastException mismatch)
                {
                    // The loader's untyped task produced something its own typed member could not: a
                    // factory bug, and one the caller has to hear about rather than wait on forever.
                    request.Fail(mismatch);
                }
            }
            else
            {
                request.Fail(error);
            }
        }

        // Either the caller was answered here, or it was answered when it asked to stop and this
        // result goes nowhere (3.9). Both leave the same thing to do: the request is gone, so the
        // loaders vetoed while it held a slot get their turn.
        Walk();
    }

    /// <summary>Answers a request that never produced a task, and keeps walking.</summary>
    /// <param name="request">The request that failed.</param>
    /// <param name="error">What the loader or the started callback threw.</param>
    private void Fail(AssetLoadRequest request, Exception error)
    {
        Finish(request);
        request.CallerDone = true;
        request.Fail(error);
    }

    /// <summary>Releases everything a request held and takes it out of the manager.</summary>
    /// <param name="request">The request that is leaving.</param>
    private void Finish(AssetLoadRequest request)
    {
        // Set first, so a cancellation that arrives while this runs finds a request it can ignore.
        request.IsFinished = true;

        request.DetachCancellation();
        request.DetachHandlers();
        _queue.Remove(request);
        _live.Remove(request.Loader);
    }

    /// <summary>
    /// Answers a caller that has cancelled, and takes the two paths apart because they are different.
    /// </summary>
    /// <param name="request">The request whose caller cancelled.</param>
    private void OnCallerCancelled(AssetLoadRequest request)
    {
        if (request.IsFinished || request.CallerDone)
        {
            return;
        }

        request.CallerDone = true;

        if (request.Phase == AssetLoadPhase.Queued)
        {
            // Requirement 4's first half: it leaves the queue and is never started, which is also why
            // its started callback is never raised (2.4). No load exists to stop, so nothing has to
            // be watched afterwards - both subscriptions come off and the queue is walked for the
            // entries below it.
            Finish(request);
            request.Cancel();
            Walk();

            return;
        }

        // Running: the started callback was raised when it started, so the caller has already been
        // told. The caller is answered now - the load does not get to finish first - and the priority
        // handler comes off, because the queue order no longer concerns this request. The
        // availability handler stays until the underlying task actually ends, because the slot it
        // occupies is still the loader's until then (3.9).
        request.DetachPriorityHandler();
        request.DetachCancellation();
        request.Cancel();
    }

    /// <summary>Moves a queued request to wherever its new priority puts it, and walks.</summary>
    /// <param name="sender">The loader that raised the notification.</param>
    private void OnPriorityChanged(object sender)
    {
        if (!_live.TryGetValue((IAssetLoader)sender, out AssetLoadRequest? request))
        {
            return;
        }

        if (request.Phase == AssetLoadPhase.Queued)
        {
            // The sequence number is kept, so a re-prioritised request does not jump ahead of entries
            // submitted earlier at the priority it moved to (3.6).
            _queue.Remove(request);
            _queue.Insert(IndexFor(request), request);
        }

        Walk();
    }

    /// <summary>Walks, because a loader said its availability answer had changed.</summary>
    /// <param name="sender">The loader that raised the notification. Its identity is not needed: a
    /// walk asks every queued request's loader anyway, which is what makes a notification for one
    /// gate safe for the rest of the queue.</param>
    private void OnCanLoadNowChanged(object sender) => Walk();

    /// <summary>Finds where a request belongs in the ordered queue.</summary>
    /// <param name="request">The request to place.</param>
    /// <returns>The index it should be inserted at.</returns>
    private int IndexFor(AssetLoadRequest request)
    {
        for (int index = 0; index < _queue.Count; index++)
        {
            if (ComesBefore(request, _queue[index]))
            {
                return index;
            }
        }

        return _queue.Count;
    }

    /// <summary>Orders the queue: priority descending, then submission order.</summary>
    /// <param name="left">The request being placed or compared.</param>
    /// <param name="right">The request already in the queue.</param>
    /// <returns>True when <paramref name="left"/> belongs ahead of <paramref name="right"/>.</returns>
    /// <remarks>
    /// The second half is not decoration: <c>List&lt;T&gt;.Sort</c> is an introsort and is not stable,
    /// so equal priorities would otherwise come back in an order that changed with the array's size.
    /// The tie-break is the sequence number, the same mechanism
    /// <c>ObservableSortedReadOnlySetView</c> uses for the same problem (3.6).
    /// </remarks>
    private static bool ComesBefore(AssetLoadRequest left, AssetLoadRequest right)
    {
        int leftPriority = left.Loader.Priority;
        int rightPriority = right.Loader.Priority;

        return leftPriority != rightPriority ? leftPriority > rightPriority : left.Sequence < right.Sequence;
    }

    /// <summary>
    /// Rejects a call from a thread that is not the UI thread.
    /// </summary>
    /// <remarks>
    /// The queue is UI-thread state, so a call from a pool thread is a bug rather than something to
    /// marshal: a silent marshal would hide the race between the caller and the walk rather than
    /// remove it, and the caller would believe it had ordered something it had not - the same stance
    /// <see cref="UiSynchronizationContext.Send"/> takes, and for the same reason (section 3.12).
    /// With no application in the process there is no UI thread to be off, so the check passes: a
    /// test host and a headless render are both in that state, and neither has a queue to race.
    /// </remarks>
    private static void EnsureUiThread()
    {
        if (Application.Current is { } application && !application.CheckAccess())
        {
            throw new InvalidOperationException(
                "Asset loading is UI-thread state, like the control tree, so IAssetLoadManager must be " +
                "used from the UI thread. A call from another thread would race the walk rather than " +
                "order anything: hand the work to another thread from inside the loader, not from the " +
                "caller.");
        }
    }
}
