namespace Kolvir.Animations.Tests;

/// <summary>
/// A clock that only moves when a test says so, at one tick per millisecond so an advance reads as
/// the time it is.
/// </summary>
/// <remarks>
/// The scheduler measures its timeline against a <see cref="TimeProvider"/> rather than counting
/// frames, so a test asserts an exact elapsed time - half a second in is half a second in - rather
/// than however many ticks happened to arrive.
/// </remarks>
internal sealed class TestClock : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => 1_000;

    public override long GetTimestamp() => _ticks;

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(_ticks);

    /// <summary>Moves the clock forward by <paramref name="milliseconds"/>.</summary>
    public void Advance(long milliseconds) => _ticks += milliseconds;
}
