using Kolvir.Undo;

namespace Kolvir.Audio;

/// <summary>
/// One clip playing on one device, with a position, a volume, and a notification when it finishes.
/// </summary>
/// <remarks>
/// <para>
/// What <see cref="AudioClip.Start"/> returns, and the object that answers "how far into the sound
/// are we" - so a progress bar or a synchronised animation can follow it. Reach it through the clip;
/// there is no way to create one.
/// </para>
/// <para>
/// <b>The position is computed, never accumulated.</b> A voice records the position it was started
/// from and the timestamp it was started at, and <see cref="Position"/> is that anchor plus however
/// long the clock has run since - the same rule <see cref="ImageData"/> uses, for the same reason: a
/// counter advanced by a callback is wrong the moment a callback is late, and wrong again when the
/// clock is a test's. It also means <see cref="Position"/> is right for a looping voice, where the
/// position wraps inside the clip.
/// </para>
/// <para>
/// <b>Nothing calls the application on the audio thread.</b> The platform half reports the end of a
/// clip from whatever thread it processes on; <see cref="Finished"/> is posted through
/// <see cref="Application.BeginInvoke"/>, so a handler may touch controls. Before an application
/// exists - in a test, or before the loop starts - handlers run inline on the thread that noticed.
/// </para>
/// <para>
/// A voice belongs to the device it was started on. If that device disappears the voice stops and
/// reports <see cref="IsLost"/>; it does not migrate to the new default, because the device is part
/// of what the voice is.
/// </para>
/// </remarks>
public sealed class Voice : IDisposable
{
    private readonly object _gate = new();
    private readonly AudioClip _clip;
    private readonly AudioDeviceInfo _device;
    private readonly IPlatformVoice? _platform;
    private readonly NullTimeline? _timeline;
    private readonly bool _loops;

    private Action<Voice>? _finished;
    private long _anchorTimestamp;
    private TimeSpan _anchorPosition;
    private float _volume;
    private State _state;
    private bool _detached;
    private bool _disposed;

    /// <summary>
    /// Creates the voice and starts it, which is why a voice is never observed in a stopped state: it
    /// begins playing when the application asks for it and not a moment later.
    /// </summary>
    internal Voice(
        AudioClip clip,
        AudioDeviceInfo device,
        IPlatformVoice? platformVoice,
        NullTimeline? timeline,
        bool loop,
        float volume)
    {
        _clip = clip;
        _device = device;
        _platform = platformVoice;
        _timeline = timeline;
        _loops = loop;
        _volume = Clamp(volume);
        _anchorTimestamp = UndoClock.Current.GetTimestamp();

        if (_platform is not null)
        {
            _platform.Ended += OnPlatformEnded;
            _platform.SetVolume(_volume);
        }

        _state = State.Playing;

        if (_platform is not null)
        {
            _platform.Start();
        }
        else
        {
            _timeline?.Schedule(this, _clip.Duration, _loops ? _clip.Duration : TimeSpan.Zero);
        }
    }

    /// <summary>Whether the voice repeats until it is stopped.</summary>
    public bool Loops => _loops;

    /// <summary>The clip this voice plays.</summary>
    public AudioClip Clip => _clip;

    /// <summary>The device this voice was started on.</summary>
    public AudioDeviceInfo Device => _device;

    /// <summary>How long one pass of the clip lasts.</summary>
    public TimeSpan Duration => _clip.Duration;

    /// <summary>
    /// How far into the clip the voice is, which for a looping voice wraps back to the start.
    /// </summary>
    /// <remarks>
    /// Frozen while the voice is paused, and the full duration once it has finished, so a progress
    /// bar ends where it should rather than at whatever the clock said afterwards.
    /// </remarks>
    public TimeSpan Position
    {
        get
        {
            lock (_gate)
            {
                return PositionLocked();
            }
        }
    }

    /// <summary>The volume of this voice, between 0 and 1. Values outside that range are clamped.</summary>
    public float Volume
    {
        get => _volume;
        set
        {
            var volume = Clamp(value);

            lock (_gate)
            {
                if (_volume == volume)
                {
                    return;
                }

                _volume = volume;
            }

            _platform?.SetVolume(volume);
        }
    }

    /// <summary>Whether the voice is paused, in which case its position is frozen.</summary>
    public bool IsPaused
    {
        get
        {
            lock (_gate)
            {
                return _state == State.Paused;
            }
        }
    }

    /// <summary>Whether the clip played to its end, as opposed to being stopped or lost.</summary>
    public bool IsFinished
    {
        get
        {
            lock (_gate)
            {
                return _state == State.Finished;
            }
        }
    }

    /// <summary>
    /// Whether the device the voice was playing on disappeared, which is why it stopped.
    /// </summary>
    /// <remarks>
    /// A lost voice does not follow the new default device: the sound the user was hearing has
    /// already been interrupted, and restarting it somewhere else would be worse than stopping.
    /// </remarks>
    public bool IsLost
    {
        get
        {
            lock (_gate)
            {
                return _state == State.Lost;
            }
        }
    }

    /// <summary>
    /// Adds a handler that runs when the clip finishes, on the UI thread.
    /// </summary>
    /// <param name="handler">The handler, which is given the voice.</param>
    /// <returns>A token that removes the handler.</returns>
    /// <remarks>
    /// Runs once, for a clip that reached its end or was lost - not for a voice the application
    /// stopped, which the application already knows about. This is how a sequence of sounds is
    /// chained, and how a looping voice that was stopped is told apart from one whose device went
    /// away.
    /// </remarks>
    public IDisposable AddFinishedHandler(Action<Voice> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _finished += handler;
        return new HandlerToken<Action<Voice>>(RemoveFinishedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddFinishedHandler"/> added.</summary>
    /// <param name="handler">The handler to remove.</param>
    public void RemoveFinishedHandler(Action<Voice> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _finished -= handler;
    }

    /// <summary>Stops the sound without ending the voice: <see cref="Resume"/> continues from here.</summary>
    public void Pause()
    {
        lock (_gate)
        {
            if (_state != State.Playing)
            {
                return;
            }

            _anchorPosition = PositionLocked();
            _anchorTimestamp = UndoClock.Current.GetTimestamp();
            _state = State.Paused;
        }

        // The device keeps its position in the clip across a pause, so the platform half only has to
        // stop consuming; the null device has no deadline while it is paused.
        _platform?.Pause();
        _timeline?.Cancel(this);
    }

    /// <summary>Continues a paused voice from where <see cref="Pause"/> stopped it.</summary>
    public void Resume()
    {
        TimeSpan remaining;
        bool loops;

        lock (_gate)
        {
            if (_state != State.Paused)
            {
                return;
            }

            _anchorTimestamp = UndoClock.Current.GetTimestamp();
            _state = State.Playing;
            remaining = _clip.Duration - _anchorPosition;
            loops = _loops;
        }

        _platform?.Resume();

        if (_platform is null)
        {
            _timeline?.Schedule(this, remaining, loops ? _clip.Duration : TimeSpan.Zero);
        }
    }

    /// <summary>Stops the voice for good, which is what disposing it does too.</summary>
    public void Stop()
    {
        bool autoDispose;

        lock (_gate)
        {
            if (_state is State.Stopped or State.Finished or State.Lost or State.Disposed)
            {
                return;
            }

            _anchorPosition = PositionLocked();
            _state = State.Stopped;
            autoDispose = _detached;
        }

        _platform?.Stop();
        _timeline?.Cancel(this);

        if (autoDispose)
        {
            Dispose();
        }
    }

    /// <summary>Stops the voice and releases the stream it holds on the device.</summary>
    public void Dispose()
    {
        IPlatformVoice? platform;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _state = State.Disposed;
            platform = _platform;
        }

        _timeline?.Cancel(this);

        if (platform is not null)
        {
            platform.Ended -= OnPlatformEnded;
            platform.Stop();
            platform.Dispose();
        }
    }

    /// <summary>
    /// Tells the voice its clip ran out, which for a real device is the platform half's report and for
    /// the null device is the timeline's deadline.
    /// </summary>
    internal void Complete() => End(lost: false);

    /// <summary>Delivers the end of the clip to the application, on the UI thread.</summary>
    private void End(bool lost)
    {
        Action<Voice>? handler;
        bool autoDispose;

        lock (_gate)
        {
            if (_state is not (State.Playing or State.Paused))
            {
                return;
            }

            _anchorPosition = PositionLocked();
            _state = lost ? State.Lost : State.Finished;
            handler = _finished;
            autoDispose = _detached;
        }

        var application = Application.Current;

        if (handler is not null)
        {
            if (application is null)
            {
                handler(this);
            }
            else
            {
                application.BeginInvoke(() => handler(this));
            }
        }

        if (autoDispose)
        {
            Dispose();
        }
    }

    /// <summary>The platform half reports the end, from whatever thread it processes on.</summary>
    private void OnPlatformEnded(bool lost) => End(lost);

    /// <summary>
    /// The position, for callers that already hold the gate: the anchor plus the clock, wrapped or
    /// clamped to the clip.
    /// </summary>
    private TimeSpan PositionLocked()
    {
        if (_state is State.Paused or State.Stopped or State.Disposed or State.Lost)
        {
            return ClampPosition(_anchorPosition);
        }

        if (_state == State.Finished)
        {
            return _clip.Duration;
        }

        var advance = UndoClock.Current.GetElapsedTime(_anchorTimestamp);

        return ClampPosition(_anchorPosition + advance);
    }

    /// <summary>
    /// Keeps a position inside the clip: the end for a voice that plays once, and a wrap for one that
    /// loops, which is where the loop is heard.
    /// </summary>
    private TimeSpan ClampPosition(TimeSpan position)
    {
        var duration = _clip.Duration;

        if (duration <= TimeSpan.Zero || position <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (position < duration)
        {
            return position;
        }

        return _loops ? TimeSpan.FromTicks(position.Ticks % duration.Ticks) : duration;
    }

    private static float Clamp(float volume) => float.IsNaN(volume) ? 0f : Math.Clamp(volume, 0f, 1f);

    /// <summary>Marks the voice as owned by nobody: it releases itself when it finishes.</summary>
    internal void Detach() => _detached = true;

    /// <summary>Where a voice is in its life, which is more states than the public surface exposes.</summary>
    private enum State
    {
        Playing,
        Paused,
        Finished,
        Stopped,
        Lost,
        Disposed,
    }
}
