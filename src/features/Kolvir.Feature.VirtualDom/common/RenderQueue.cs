namespace Kolvir.VirtualDom;

/// <summary>
/// The components that have asked to be rendered, drained once per frame.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="Component.SetState"/> only marks its component and asks for a frame. The renders
/// themselves happen at the start of the next frame any host draws - see
/// <see cref="UiHost.AddFrameStartingHandler"/> - so a handler that changes a list five times renders
/// once, and the tree is touched once, instead of five times.
/// </para>
/// <para>
/// The queue is process-wide because a mount can exist before any window holds it: a page builds its
/// components and mounts them while the window is still being assembled. Rendering is UI-thread work,
/// like everything else here, and a second window painting drains the same queue, which is why a mount
/// that has already been drained finds nothing to do.
/// </para>
/// </remarks>
internal static class RenderQueue
{
    private static readonly List<ComponentMount> Pending = [];
    private static bool _subscribed;
    private static bool _draining;

    /// <summary>Adds a mount to the next frame's batch.</summary>
    /// <param name="mount">The mount to render.</param>
    internal static void Enqueue(ComponentMount mount)
    {
        Subscribe();
        Pending.Add(mount);
    }

    /// <summary>Drops a mount that was disposed before its turn came.</summary>
    /// <param name="mount">The mount to forget.</param>
    internal static void Forget(ComponentMount mount) => Pending.Remove(mount);

    /// <summary>Renders everything queued, once each, in the order the state changed.</summary>
    /// <remarks>
    /// The batch is taken before any render runs, because a render can dispose a mount that is still
    /// waiting its turn - a list that removes a row while both are queued. A disposed mount renders
    /// nothing, so the batch stays simple.
    /// </remarks>
    internal static void Drain()
    {
        if (_draining || Pending.Count == 0)
        {
            return;
        }

        _draining = true;

        try
        {
            var batch = Pending.ToArray();
            Pending.Clear();

            foreach (var mount in batch)
            {
                mount.RenderQueued();
            }
        }
        finally
        {
            _draining = false;
        }
    }

    /// <summary>
    /// Subscribes to the frame the first time something is queued.
    /// </summary>
    /// <remarks>
    /// The subscription is never removed. It is one static call, it is what keeps the queue alive for
    /// the life of the process, and unsubscribing on an empty queue would only mean subscribing again
    /// the next time a component changed - both costs on the hot path for nothing.
    /// </remarks>
    private static void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        UiHost.AddFrameStartingHandler(OnFrameStarting);
    }

    private static void OnFrameStarting(UiHost host) => Drain();
}
