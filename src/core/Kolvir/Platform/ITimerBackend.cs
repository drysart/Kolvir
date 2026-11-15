namespace Kolvir;

/// <summary>
/// The platform half of an <see cref="Timer"/>: it owns the wait and calls the tick callback
/// it was created with once per interval.
/// </summary>
/// <remarks>
/// <para>
/// Only the wait lives here. Dropping overlapping ticks and moving the callback to the UI thread
/// stay in <see cref="Timer"/>, so a backend that ticks on its own thread and one that ticks
/// on a pool thread behave identically to the application.
/// </para>
/// <para>
/// A backend returns one of these from <see cref="IApplicationBackend.CreateTimer"/>. Return
/// <see cref="ThreadPoolTimerBackend"/> unless the platform's default timer is too coarse for the
/// intervals the UI asks for: on Windows it cannot fire faster than the 15.625 ms system timer
/// period, which is why the Win32 backend answers sub-period intervals with a high-resolution
/// waitable timer instead.
/// </para>
/// <para>
/// Every member may be called from any thread, including a tick callback, and all of them are
/// safe to call after <see cref="IDisposable.Dispose"/>.
/// </para>
/// </remarks>
public interface ITimerBackend : IDisposable
{
    /// <summary>
    /// Starts the timer, or restarts it when it is already running. The first tick arrives one
    /// <paramref name="interval"/> after this call.
    /// </summary>
    void Start(TimeSpan interval);

    /// <summary>Stops the timer. A tick already in flight may still arrive.</summary>
    void Stop();
}
