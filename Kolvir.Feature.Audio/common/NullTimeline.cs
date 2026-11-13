using Kolvir.Undo;

namespace Kolvir.Audio;

/// <summary>
/// The clock the silent device runs on: one timer for the engine, aimed at the next voice that is due
/// to finish.
/// </summary>
/// <remarks>
/// <para>
/// A voice on the null device produces no samples, so the only thing that has to happen is that it
/// finishes when it would have. This class is that promise: it keeps the deadlines, and when one
/// passes it tells the voice, which is what lets an application chain sounds - "play the next one
/// when this one ends" - behave the same on a build machine as on a machine with speakers.
/// </para>
/// <para>
/// <b>One timer, re-aimed, is why this is a class.</b> A timer per voice would be a timer per sound,
/// and the wait is not repeated: it is re-aimed at the nearest deadline each time it fires, the same
/// way a frame timer is re-aimed at the next frame boundary. A timer whose interval drifted instead
/// would finish voices late by however much it had drifted.
/// </para>
/// <para>
/// Deadlines are timestamps from <see cref="UndoClock.Current"/>, not wall-clock values this class
/// invents, so a test that moves the clock and fires the timer by hand sees exactly what a running
/// application sees.
/// </para>
/// </remarks>
internal sealed class NullTimeline : IDisposable
{
    private readonly List<Entry> _entries = [];
    private readonly object _gate = new();

    private Timer? _timer;
    private bool _disposed;

    /// <summary>
    /// Schedules <paramref name="voice"/> to finish <paramref name="delay"/> from now, and then every
    /// <paramref name="period"/> if it loops.
    /// </summary>
    /// <param name="voice">The voice to tell when its deadline passes.</param>
    /// <param name="delay">How long until it finishes.</param>
    /// <param name="period">
    /// How long one pass of the clip lasts when the voice loops, or <see cref="TimeSpan.Zero"/> for a
    /// voice that finishes once.
    /// </param>
    public void Schedule(Voice voice, TimeSpan delay, TimeSpan period)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var due = UndoClock.Current.GetTimestamp() + ToStamps(Math.Max(0, delay.Ticks));

            CancelLocked(voice);
            _entries.Add(new Entry(voice, due, ToStamps(Math.Max(0, period.Ticks))));

            RearmLocked();
        }
    }

    /// <summary>
    /// Forgets <paramref name="voice"/>'s deadline, which is what stopping or pausing it means: a
    /// paused voice has no deadline until it resumes.
    /// </summary>
    public void Cancel(Voice voice)
    {
        lock (_gate)
        {
            CancelLocked(voice);
            RearmLocked();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Timer? timer;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _entries.Clear();
            timer = _timer;
            _timer = null;
        }

        timer?.Dispose();
    }

    private void CancelLocked(Voice voice) => _entries.RemoveAll(entry => entry.Voice == voice);

    /// <summary>Aims the one timer at the nearest deadline, and stops it when there is none.</summary>
    private void RearmLocked()
    {
        if (_disposed)
        {
            return;
        }

        if (_entries.Count == 0)
        {
            _timer?.Stop();
            return;
        }

        var now = UndoClock.Current.GetTimestamp();
        var nearest = long.MaxValue;

        foreach (var entry in _entries)
        {
            var remaining = entry.DueAt - now;

            if (remaining < nearest)
            {
                nearest = remaining;
            }
        }

        // A deadline already passed fires on the next tick rather than being skipped, which is what a
        // voice resumed after the UI thread was busy expects.
        var interval = FromStamps(Math.Max(nearest, 1));

        if (_timer is null)
        {
            _timer = new Timer(interval);
            _timer.AddTickHandler(OnTick);
        }

        _timer.Interval = interval;
        _timer.Start();
    }

    /// <summary>
    /// A duration in the clock's own timestamp units, which is what makes the deadlines comparable
    /// with <see cref="TimeProvider.GetTimestamp"/>.
    /// </summary>
    /// <remarks>
    /// The clock is a <see cref="TimeProvider"/> rather than a stopwatch, so its timestamps are in
    /// whatever unit it chooses - milliseconds in a test, nanoseconds on a real one. Adding
    /// <see cref="TimeSpan.Ticks"/> to one of them would be a thousandfold error on one clock and no
    /// error at all on another, so every conversion goes through the provider's frequency.
    /// </remarks>
    private static long ToStamps(long ticks) =>
        (long)Math.Round(ticks * (double)UndoClock.Current.TimestampFrequency / TimeSpan.TicksPerSecond);

    private static TimeSpan FromStamps(long stamps) =>
        TimeSpan.FromSeconds(stamps / (double)UndoClock.Current.TimestampFrequency);

    private void OnTick(object? sender)
    {
        List<Voice>? due = null;
        bool rearm = false;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var now = UndoClock.Current.GetTimestamp();

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];

                if (entry.DueAt > now)
                {
                    continue;
                }

                if (entry.Period == 0)
                {
                    _entries.RemoveAt(i);
                    (due ??= []).Add(entry.Voice);
                    continue;
                }

                // A looping voice has no end, so its deadline moves on by whole passes until it is
                // ahead of the clock again - a long stall must not make it fire once per missed pass.
                while (entry.DueAt <= now)
                {
                    entry.DueAt += entry.Period;
                }

                rearm = true;
            }

            if (_entries.Count > 0)
            {
                rearm = true;
            }
        }

        if (rearm)
        {
            lock (_gate)
            {
                RearmLocked();
            }
        }

        if (due is null)
        {
            return;
        }

        foreach (var voice in due)
        {
            voice.Complete();
        }
    }

    /// <summary>One scheduled deadline: when it is due and, for a looping voice, how long one pass is.</summary>
    private sealed class Entry(Voice voice, long dueAt, long period)
    {
        public Voice Voice { get; } = voice;

        public long DueAt { get; set; } = dueAt;

        public long Period { get; } = period;
    }
}
