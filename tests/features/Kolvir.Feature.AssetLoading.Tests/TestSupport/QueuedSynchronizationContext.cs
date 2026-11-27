using System.Collections.Generic;

namespace Kolvir.Feature.AssetLoading.Tests;

/// <summary>
/// A synchronization context whose <see cref="Post"/> queues the callback instead of running it, and
/// whose turns are taken by hand without the context being current - which is what makes a
/// continuation awaiting through it post again rather than run inline.
/// </summary>
/// <remarks>
/// This is not the step an application takes. There the manager's continuation is posted from the
/// loader's thread but the caller's runs inline inside it, because the loop's context is current for
/// every turn (dev-docs/ASSET-LOADING.md section 3.12). It is how the suite constructs the window the
/// control's stale-completion comparison covers - a result delivered with the turn that applies it
/// still queued - so removing that comparison breaks a test instead of changing a behaviour no test
/// can reach.
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
