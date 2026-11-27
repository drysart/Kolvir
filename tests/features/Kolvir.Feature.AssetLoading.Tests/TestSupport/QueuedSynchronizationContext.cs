using System.Collections.Generic;

namespace Kolvir.Feature.AssetLoading.Tests;

/// <summary>
/// A synchronization context whose <see cref="Post"/> queues the callback instead of running it, the
/// way <see cref="UiSynchronizationContext"/> does on the UI thread, with the turn taken by hand.
/// </summary>
/// <remarks>
/// This is what lets a test see the step an application really takes: the manager completes the
/// caller's task on the UI thread, and the continuation that applies the result is posted rather than
/// run inline. Without it the scene resumes every await inline, so the window between "the load
/// finished" and "the control writes it" does not exist and a rule about that window cannot be tested.
/// </remarks>
internal sealed class QueuedSynchronizationContext : SynchronizationContext, IDisposable
{
    private readonly SynchronizationContext? _previous = Current;
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

    /// <summary>Installs itself as the current context for the calling thread.</summary>
    public QueuedSynchronizationContext() => SetSynchronizationContext(this);

    /// <summary>How many continuations are waiting for a turn.</summary>
    public int Pending => _queue.Count;

    /// <inheritdoc/>
    public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

    /// <inheritdoc/>
    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("The scene runs every turn on the calling thread; use RunNext.");

    /// <summary>Runs exactly one queued continuation, as one turn of the loop would.</summary>
    public void RunNext()
    {
        if (_queue.Count == 0)
        {
            throw new InvalidOperationException("No continuation is waiting for a turn.");
        }

        (SendOrPostCallback callback, object? state) = _queue.Dequeue();
        callback(state);
    }

    /// <summary>Runs every queued continuation, including the ones they queue.</summary>
    public void Drain()
    {
        while (_queue.Count > 0)
        {
            RunNext();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => SetSynchronizationContext(_previous);
}
