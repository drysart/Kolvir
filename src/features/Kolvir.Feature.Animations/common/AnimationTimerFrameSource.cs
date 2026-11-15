namespace Kolvir.Animations;

/// <summary>
/// The default <see cref="IAnimationFrameSource"/>: an <see cref="Timer"/> asking the
/// running application's backend for the wait.
/// </summary>
/// <remarks>
/// <para>
/// It is a timer rather than a loop of its own because <see cref="Timer"/> already does the two
/// things a frame clock needs and neither of them is portable: it asks the backend for the wait, so
/// Windows uses its high-resolution waitable timer below the 15.625 ms system period while macOS and
/// Linux use their own fine-grained timers, and it moves the callback to the UI thread through
/// <see cref="Application.BeginInvoke"/>, so a frame may touch controls without marshalling.
/// </para>
/// <para>
/// Nothing is created until the first <see cref="Start"/>, because <see cref="Timer"/> asks the
/// application for a backend then - which also means an application that never starts an animation
/// never asks for a timer at all.
/// </para>
/// </remarks>
internal sealed class AnimationTimerFrameSource : IAnimationFrameSource
{
    private readonly Timer _timer = new();
    private Action? _tick;
    private bool _disposed;

    public AnimationTimerFrameSource() => _timer.AddTickHandler(_ => _tick?.Invoke());

    /// <inheritdoc />
    public void Start(TimeSpan interval, Action tick)
    {
        ArgumentNullException.ThrowIfNull(tick);

        if (_disposed)
        {
            return;
        }

        _tick = tick;

        // The interval is stored, so setting it while stopped only records it; setting it while
        // running restarts the wait, which is what a changed frame rate has to do anyway.
        _timer.Interval = interval;
        _timer.Start();
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (!_disposed)
        {
            _timer.Stop();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tick = null;
        _timer.Dispose();
    }
}
