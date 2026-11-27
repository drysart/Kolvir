namespace Kolvir.Feature.AssetLoading;

/// <summary>
/// Decides which queued load starts next, and answers the caller when it has.
/// </summary>
/// <remarks>
/// <para>
/// The manager owns order; the loader owns capacity and bytes. It holds one queue for the whole
/// application - across windows, because a window that closes disposes its control tree and its
/// loads cancel through that, so there is nothing per window to keep - and it applies no rule of its
/// own about how many loads may run: it asks each loader <see cref="IAssetLoader.CanLoadNow"/> and
/// takes the answer (dev-docs/ASSET-LOADING.md sections 3.1 and 3.2).
/// </para>
/// <para>
/// It never polls. It walks once on enqueue, once per priority change, once per availability change,
/// and once per completion - because all four change what may start and nothing else does
/// (section 3.7).
/// </para>
/// <para>
/// It is a singleton, resolved from the application's services after <c>AddAssetLoading()</c>, and
/// every member of it is UI-thread only: the queue is UI-thread state, like the control tree.
/// </para>
/// </remarks>
public interface IAssetLoadManager
{
    /// <summary>
    /// Queues one request and answers it when the walk starts it and the load has produced a result.
    /// </summary>
    /// <typeparam name="TResult">What the load produces.</typeparam>
    /// <param name="loader">The request's handle, from
    /// <see cref="IAssetLoaderProvider.TryCreateAssetLoader{TResult}"/>. Its
    /// <see cref="IAssetLoader.Priority"/> decides where it queues, and its notification pairs are
    /// how it moves afterwards.</param>
    /// <param name="onStarted">
    /// Raised on the UI thread at the moment the manager has called the loader's
    /// <see cref="IAssetLoader.LoadAsync"/> and is about to observe its completion - not when this
    /// method was called, which can be many walks and many loads earlier. Pass
    /// <see langword="null"/> when the caller does not care.
    /// </param>
    /// <param name="cancellationToken">Stops this request. See the remarks.</param>
    /// <returns>
    /// Completes with the loader's result, on the UI thread, so a caller can assign what it loaded
    /// on the next line without a dispatch of its own.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Starting.</b> The walk starts what it can as soon as the request is queued - a loader
    /// whose gate is open starts inside this call - and a queued request whose gate is closed starts
    /// when its loader reports that its answer changed, or when the queue changes around it.
    /// <paramref name="onStarted"/> is therefore raised synchronously from inside this call when the
    /// loader starts at once (a cache hit, an embedded resource already in memory), so a caller must
    /// set its own waiting state <em>before</em> calling, not after the call returns
    /// (section 2.4). It is never raised for a request cancelled while queued; it has already been
    /// raised for one cancelled while running.
    /// </para>
    /// <para>
    /// <b>Ordering.</b> Higher priority first, and equal priorities in submission order: the
    /// sequence number is taken here and kept, so changing a request's priority moves that request
    /// without moving it ahead of earlier requests at the same priority (section 3.6). A running
    /// load is never preempted; its priority no longer matters (section 3.5).
    /// </para>
    /// <para>
    /// <b>Cancellation.</b> The loader is handed
    /// <paramref name="cancellationToken"/>, and the manager does not assume it honours it, because
    /// some loaders cannot. What the manager guarantees is its own: the caller is answered cancelled
    /// at the moment it asks rather than when the load happens to end; a result that arrives
    /// afterwards is never delivered to it; a request cancelled while still queued never starts; and
    /// the load keeps counting against the loader's own capacity until its underlying task actually
    /// ends, so cancelling a running request cannot quietly let the application exceed whatever
    /// limit the loader enforces (section 3.9). The token may be cancelled from any thread: a
    /// cancellation on the UI thread - what a control's removal produces - is answered inside the
    /// <c>Cancel()</c> call, and one from another thread is answered on the loop's next turn,
    /// because the manager moves that work onto the UI thread rather than mutating its queue where
    /// the cancellation landed.
    /// </para>
    /// <para>
    /// <b>One request per handle.</b> A loader instance with a request outstanding cannot be
    /// submitted again; an instance may be submitted again once its request has finished, which is
    /// what lets a handle be reused without ever being shared (section 3.11).
    /// </para>
    /// <para>
    /// A load that throws - on the way to its task, or with a faulted task - answers its caller with
    /// that exception and frees everything the request held, exactly as a completed load does.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="loader"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="loader"/> already has a request outstanding, or this was called from a thread
    /// that is not the UI thread.
    /// </exception>
    Task<TResult> LoadAsync<TResult>(
        IAssetLoader<TResult> loader,
        Action? onStarted,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the queue: every request the manager is holding, in the order the walk would consider
    /// them - priority descending, sequence ascending.
    /// </summary>
    /// <returns>A copy of the queue as it stands. Completing or cancelling a request does not change
    /// it; taking it again does.</returns>
    /// <remarks>
    /// <para>
    /// For debugging, and deliberately minimal: it is the smallest thing that answers "why is this
    /// one not starting". An entry at the top of the list with
    /// <see cref="AssetLoadSnapshot.CanLoadNow"/> false is the whole story, because that value is
    /// read when this is called rather than cached, and it is the same answer the next walk gets
    /// (section 2.6).
    /// </para>
    /// <para>
    /// It reports the present, not a log: a finished request is not in the manager any more, and
    /// nothing is maintained for this method - the queue already exists and the result is a copy of
    /// it - so a debugging aid never becomes a cost. A caller that wants history takes this twice and
    /// diffs it.
    /// </para>
    /// <para>
    /// UI thread only, like the rest of the manager's state.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Called from a thread that is not the UI
    /// thread.</exception>
    IReadOnlyList<AssetLoadSnapshot> Snapshot();
}
