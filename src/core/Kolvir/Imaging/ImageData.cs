using SkiaSharp;
using Kolvir.Undo;

namespace Kolvir;

/// <summary>
/// What an <see cref="Image"/> shows: a timeline over an <see cref="IImageDataSource"/>,
/// which a still picture is the degenerate case of.
/// </summary>
/// <remarks>
/// <para>
/// <b>The position is computed, never accumulated.</b> <see cref="Play"/> records an anchor - a
/// position and the timestamp it was taken at - and every read of <see cref="Position"/> is that
/// anchor plus the time that has passed since. Nothing advances on a timer, so a paused application,
/// a hidden control or an animation in an app with no message loop still has the right frame when
/// something finally asks for it: time spent hidden counts, and showing the control again shows the
/// frame the clock says, not the one it left on.
/// </para>
/// <para>
/// <b>Paused means frozen.</b> <see cref="Position"/> moves only while <see cref="IsPlaying"/> is
/// true, and <see cref="Pause"/> stores the position it froze at. <see cref="Seek"/> re-anchors, so
/// seeking during playback does not jump when the clock that was already running catches up.
/// </para>
/// <para>
/// <b>The position is a position within one pass, not a stopwatch.</b> It is always in
/// <c>[0, Duration)</c>: wrapped when <see cref="Loop"/> is true, and stopped at the last frame of
/// the pass when it is false. Playback rate is fixed at 1.0x in this version; the arithmetic lives
/// in <see cref="Advance"/> so that a rate is one multiplication rather than a rewrite.
/// </para>
/// <para>
/// <b>A still image has no timeline.</b> A source with a <see cref="Duration"/> of zero never plays:
/// <see cref="Position"/> stays at zero, <see cref="Play"/>, <see cref="Pause"/> and
/// <see cref="Seek"/> do nothing rather than throwing, and the control that holds it never starts a
/// timer for it.
/// </para>
/// <para>
/// The clock is <see cref="UndoClock.Current"/>, the same seam undo timing and every timer in
/// the library read, so a test moves one clock and no second notion of time exists.
/// </para>
/// </remarks>
public sealed class ImageData : IDisposable
{
    private readonly IImageDataSource _source;
    private TimeSpan _position;
    private TimeSpan _anchorPosition;
    private long _anchorTimestamp;
    private bool _playing;
    private bool _disposed;
    private bool _loop;

    /// <summary>Takes ownership of a source and starts on its first picture.</summary>
    /// <param name="source">The timeline to play. It is disposed with this object.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    public ImageData(IImageDataSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
        _loop = source.Loops;
    }

    /// <summary>
    /// Wraps one picture as a still image, which is what "show this picture" is for a control that
    /// plays a timeline.
    /// </summary>
    /// <param name="picture">The picture to show.</param>
    /// <param name="disposePictureWithData">
    /// True to dispose the picture with this object. False leaves it to whoever made it, which is
    /// what a picture shared between two controls needs.
    /// </param>
    /// <returns>The data to assign to <see cref="Image.Source"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="picture"/> is null.</exception>
    public static ImageData FromPicture(SKPicture picture, bool disposePictureWithData = false) =>
        new(new PictureImageDataSource(picture, disposePictureWithData));

    /// <summary>
    /// Runs when something a viewer has to look at changed: play state, position, or the loop
    /// setting a new frame depends on.
    /// </summary>
    internal event Action<ImageData>? Changed;

    /// <summary>How long one pass through the pictures takes; zero for a still image.</summary>
    public TimeSpan Duration => _source.Duration;

    /// <summary>The size the pictures are laid out at, fixed for the source.</summary>
    public SKSize NaturalSize => _source.NaturalSize;

    /// <summary>
    /// Whether playback wraps at the end of a pass or stops on the last frame.
    /// </summary>
    /// <remarks>
    /// It starts from what the format declared - <see cref="IImageDataSource.Loops"/> - and a
    /// later assignment replaces that default entirely. Setting it takes effect at the next wrap for
    /// a playing animation, and immediately for one that has already stopped.
    /// </remarks>
    public bool Loop
    {
        get => _loop;
        set
        {
            if (_loop == value)
            {
                return;
            }

            _loop = value;
            Changed?.Invoke(this);
        }
    }

    /// <summary>
    /// Whether the position is following the clock.
    /// </summary>
    /// <remarks>
    /// Reading it settles a one-shot animation whose end has already passed, exactly as reading
    /// <see cref="Position"/> does, so a viewer that only ever asks "are we playing?" still finds out
    /// that the clock has run out even with no timer running.
    /// </remarks>
    public bool IsPlaying
    {
        get
        {
            _ = Position;
            return _playing;
        }
    }

    /// <summary>
    /// Where playback is within one pass.
    /// </summary>
    /// <remarks>
    /// Always in <c>[0, Duration)</c>: wrapped when <see cref="Loop"/> is true, and stopped on the
    /// last frame of the pass when it is not, so no position ever answers to a frame that does not
    /// exist. Reading it past the end of a non-looping animation is what stops it, so the property
    /// settles the animation rather than reporting a position the source cannot draw.
    /// </remarks>
    public TimeSpan Position
    {
        get
        {
            if (_disposed || !_playing)
            {
                return _position;
            }

            var advanced = Advance();
            if (advanced < Duration)
            {
                return advanced;
            }

            if (_loop)
            {
                return TimeSpan.FromTicks(advanced.Ticks % Duration.Ticks);
            }

            // Reaching the end of a one-shot animation stops it on its last frame. This is the only
            // state change a read makes, and it belongs in a getter because the animation has to be
            // right with no timer running at all.
            _playing = false;
            _position = LastPosition;
            return _position;
        }
    }

    /// <summary>The picture at <see cref="Position"/>.</summary>
    /// <remarks>
    /// Owned by this object: a viewer draws it and never disposes it. Null once this object is
    /// disposed.
    /// </remarks>
    public SKPicture? CurrentFrame => _disposed ? null : _source.FrameAt(Position);

    /// <summary>Starts, or resumes from where <see cref="Pause"/> stopped.</summary>
    /// <remarks>
    /// Playing at the end of a one-shot animation restarts it from the beginning, because there is
    /// nowhere else for it to go. Calling it while already playing does nothing. A still image never
    /// plays, so for one this is a no-op.
    /// </remarks>
    public void Play()
    {
        if (_disposed || _playing || Duration <= TimeSpan.Zero)
        {
            return;
        }

        if (_position >= LastPosition)
        {
            _position = TimeSpan.Zero;
        }

        Anchor(_position);
        _playing = true;
        Changed?.Invoke(this);
    }

    /// <summary>Stops the clock where it is; <see cref="Play"/> continues from there.</summary>
    /// <remarks>A still image has no clock, so for one this is a no-op.</remarks>
    public void Pause()
    {
        if (_disposed || !_playing)
        {
            return;
        }

        _position = Position;
        _playing = false;
        Changed?.Invoke(this);
    }

    /// <summary>Moves to <paramref name="position"/> and keeps playing from there.</summary>
    /// <param name="position">
    /// The position to move to, clamped into the pass: a negative value seeks to the first frame and
    /// one at or past the end seeks to the last.
    /// </param>
    /// <remarks>
    /// Seeking while playing re-anchors the clock, so playback continues from the new position
    /// instead of jumping back to where the previous anchor would have been by now. A still image
    /// has nowhere to seek to, so for one this is a no-op.
    /// </remarks>
    public void Seek(TimeSpan position)
    {
        if (_disposed || Duration <= TimeSpan.Zero)
        {
            return;
        }

        _position = position < TimeSpan.Zero ? TimeSpan.Zero
            : position > LastPosition ? LastPosition
            : position;

        if (_playing)
        {
            Anchor(_position);
        }

        Changed?.Invoke(this);
    }

    /// <summary>
    /// How long until the picture changes at <paramref name="position"/>; null when nothing will.
    /// </summary>
    /// <param name="position">The position to ask about, normally <see cref="Position"/>.</param>
    /// <remarks>
    /// The control uses this to schedule its repaint timer to the next frame boundary rather than to
    /// a fixed frame rate, so an animation with four frames a second wakes four times a second. It is
    /// public for the same reason: anything else driving an animation - a test, a host with its own
    /// frame loop - needs the same number, and reading it here is one answer instead of two.
    /// </remarks>
    public TimeSpan? TimeUntilNextFrame(TimeSpan position) =>
        _disposed || Duration <= TimeSpan.Zero ? null : _source.TimeUntilNextFrame(position);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _playing = false;
        Changed = null;
        _source.Dispose();
    }

    /// <summary>
    /// The last position inside the pass: one tick short of the duration, which is where a
    /// non-looping animation stops and where a seek past the end lands.
    /// </summary>
    private TimeSpan LastPosition => TimeSpan.FromTicks(Duration.Ticks - 1);

    /// <summary>
    /// The position the clock has reached since the anchor. The only place playback rate would
    /// multiply, which is why it is a method rather than an expression at each use.
    /// </summary>
    private TimeSpan Advance() => _anchorPosition + UndoClock.Current.GetElapsedTime(_anchorTimestamp);

    private void Anchor(TimeSpan position)
    {
        _anchorPosition = position;
        _anchorTimestamp = UndoClock.Current.GetTimestamp();
    }
}
