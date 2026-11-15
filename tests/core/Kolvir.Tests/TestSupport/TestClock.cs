namespace Kolvir.Tests;

/// <summary>
/// A clock that only moves when a test says so.
/// </summary>
/// <remarks>
/// Every time-dependent behaviour in the library - undo coalescing, tool-tip delays, caret blink -
/// reads <see cref="UndoClock.Current"/> or a timer built from it, so a test drives the time
/// rather than waiting for it. Nothing in this suite sleeps.
/// </remarks>
internal sealed class TestClock : TimeProvider
{
    /// <summary>One tick per millisecond, so <see cref="Advance"/> is readable.</summary>
    private long _ticks;

    public override long TimestampFrequency => 1_000;

    public override long GetTimestamp() => _ticks;

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(_ticks);

    /// <summary>Moves the clock forward by <paramref name="milliseconds"/>.</summary>
    public void Advance(long milliseconds) => _ticks += milliseconds;

    /// <summary>Puts the clock back to zero, which every scenario starts from.</summary>
    public void Reset() => _ticks = 0;
}
