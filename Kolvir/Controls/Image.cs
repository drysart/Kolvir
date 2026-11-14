using SkiaSharp;

namespace Kolvir;

/// <summary>
/// Shows an image - a still picture or an animation, which are the same thing here: what it holds is
/// an <see cref="ImageData"/>, and a picture is an animation with a duration of zero.
/// </summary>
/// <remarks>
/// <para>
/// <b>The control never decodes anything.</b> There is no <c>Load</c> and no format handling here: an
/// application resolves <see cref="IImageDataFactory"/> from its services and assigns the
/// result, so a failure is an ordinary exception where the developer called the factory rather than
/// an event raised from a control:
/// </para>
/// <code>
/// var factory = services.GetRequiredService&lt;IImageDataFactory&gt;();
/// image.Source = factory.LoadFromStream(stream);
/// </code>
/// <para>
/// <b>The control never advances the animation either.</b> <see cref="ImageData.Position"/> is
/// computed from a clock, so what is drawn is right whether or not a timer ever runs. The control's
/// timer only repaints, and only when there is something to repaint for: it is scheduled to the next
/// frame boundary rather than to a display rate, and it does not exist while the animation is
/// paused, while the image is still, or while the control is not
/// <see cref="Control.IsDisplayed"/>. That last rule is the one the marquee progress bar set: a
/// control nobody can look at holds no clock. Time still passes for the animation while it is
/// hidden, so showing it again shows the frame the clock says rather than the one it left on.
/// </para>
/// <para>
/// <b>Ownership.</b> The control draws the data and never disposes it - the same convention WinForms
/// has for <c>PictureBox.Image</c>, and for the same reason: the same picture is often shown in more
/// than one place, and an image that vanished because one control was closed would be a surprise no
/// amount of documentation fixes. <see cref="ImageData"/> is <see cref="IDisposable"/> and it
/// belongs to whoever created it: assign it, show it, dispose it when the application is done with
/// it. Disposing the control does not dispose the image, and assigning a different image does not
/// dispose the one that was there.
/// </para>
/// </remarks>
public class Image : Control
{
    // A source may ask for the next frame in zero time - a GIF frame with no delay, or a position
    // that lands exactly on a boundary - and a timer with no interval is a busy loop.
    private static readonly TimeSpan MinimumFrameDelay = TimeSpan.FromMilliseconds(1);

    private ImageData? _image;
    private SizeMode _sizeMode = SizeMode.Zoom;
    private Timer? _frameTimer;

    /// <summary>Creates an image control with no source that sizes itself to the picture it is given.</summary>
    public Image()
    {
        AutoSize = true;
    }

    /// <summary>
    /// What the control shows. The control does not take ownership: it draws this and does not
    /// dispose it.
    /// </summary>
    /// <remarks>
    /// Setting it replaces whatever was there without disposing the old data, and the timer follows
    /// the new data: an animation already playing starts ticking, and a still picture stops the timer
    /// at once. The caller disposes what the caller created - see the ownership note on this class.
    /// </remarks>
    public ImageData? Source
    {
        get => _image;
        set
        {
            if (ReferenceEquals(_image, value))
            {
                return;
            }

            ReleaseImage();
            _image = value;

            if (_image is not null)
            {
                _image.Changed += OnImageChanged;
            }

            UpdateFrames();
            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>
    /// How the picture is fitted into the content area. Defaults to <see cref="SizeMode.Zoom"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="SizeMode.Normal"/> and <see cref="SizeMode.CenterImage"/> draw at the picture's
    /// own size, while <see cref="SizeMode.StretchImage"/> and <see cref="SizeMode.Zoom"/> use the
    /// room available. Whatever the mode, the picture is placed in the content area according to
    /// <see cref="ImageAlign"/>.
    /// </remarks>
    public SizeMode SizeMode
    {
        get => _sizeMode;
        set
        {
            if (_sizeMode == value)
            {
                return;
            }

            _sizeMode = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Where the picture sits inside the content area when it is smaller than it. Defaults to
    /// <see cref="ContentAlignment.MiddleCenter"/>. Ignored by <see cref="SizeMode.StretchImage"/>,
    /// which takes the whole area.
    /// </summary>
    public ContentAlignment ImageAlign { get; set; } = ContentAlignment.MiddleCenter;

    /// <summary>Draws a solid color placeholder instead of an image.</summary>
    public SKColor? PlaceholderColor { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// The answer is the source's own size, never the current frame's, so an animation whose frames
    /// differ by a pixel does not re-lay-out the window on every frame.
    /// </remarks>
    protected override SKSize MeasureContent(SKSize availableSize)
    {
        // The source's own size, never the current frame's: an animation whose frames differ by a
        // pixel would otherwise re-lay-out the window on every frame.
        return _image is null ? SKSize.Empty : _image.NaturalSize;
    }

    /// <summary>
    /// Fills the background with <see cref="PlaceholderColor"/> while there is no
    /// <see cref="Source"/>, and leaves the background to the base class once there is one.
    /// </summary>
    /// <param name="canvas">Canvas to paint into, in this control's client space.</param>
    /// <param name="rect">The area to paint.</param>
    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        base.OnPaintBackground(canvas, rect);

        if (PlaceholderColor is { } placeholder && _image is null)
        {
            Painter.Fill(canvas, rect, placeholder, Border.CornerRadius);
        }
    }

    /// <summary>
    /// Draws the source's current frame, fitted into the content area as <see cref="Image.SizeMode"/>
    /// and <see cref="ImageAlign"/> ask. Also re-arms the frame timer, so a frame that is only painted
    /// while the control is on screen keeps its countdown aimed at the next frame boundary.
    /// </summary>
    /// <param name="canvas">Canvas to paint into, in this control's client space.</param>
    /// <param name="rect">The area to paint.</param>
    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        // A host may paint a tree it never showed, which is a frame rather than the user seeing it:
        // the check here is what keeps the timer honest in that case too.
        UpdateFrames();

        if (_image?.CurrentFrame is not { } frame)
        {
            return;
        }

        var content = Geometry.Deflate(rect, Padding);
        if (content.Width <= 0f || content.Height <= 0f)
        {
            return;
        }

        var natural = _image.NaturalSize;
        var destination = ComputeDestination(content, natural.Width, natural.Height, _sizeMode, ImageAlign);
        if (destination.Width <= 0f || destination.Height <= 0f)
        {
            return;
        }

        // Replay the frame at the destination's scale. The bounds are the picture's own, so a frame
        // recorded with an origin still lands where it drew.
        var bounds = frame.CullRect;
        canvas.Save();
        canvas.Translate(destination.Left, destination.Top);
        canvas.Scale(destination.Width / natural.Width, destination.Height / natural.Height);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(frame);
        canvas.Restore();
    }

    /// <summary>
    /// Starts or stops the frame timer for the new displayed state, so a control nobody can look at
    /// holds no clock. A control that becomes displayed again asks for a repaint rather than resuming
    /// where it left off: the position comes from the clock, so the next paint shows the frame time
    /// has reached.
    /// </summary>
    protected override void OnDisplayedChanged()
    {
        base.OnDisplayedChanged();

        // Showing the control again does not resume from where it left: the position is computed
        // from the clock, so the paint after this one shows the frame time has reached.
        UpdateFrames();
        Invalidate();
    }

    private static SKRect ComputeDestination(SKRect content, float pictureWidth, float pictureHeight, SizeMode sizeMode, ContentAlignment alignment)
    {
        if (pictureWidth <= 0f || pictureHeight <= 0f)
        {
            return SKRect.Empty;
        }

        var natural = new SKSize(pictureWidth, pictureHeight);

        return sizeMode switch
        {
            SizeMode.StretchImage => content,
            SizeMode.CenterImage => Geometry.Align(natural, content, alignment),
            SizeMode.Zoom => Geometry.Align(ScaleToFit(natural, content.Size), content, alignment),
            _ => Geometry.Align(natural, content, alignment),
        };
    }

    private static SKSize ScaleToFit(SKSize source, SKSize bounds)
    {
        if (source.Width <= 0f || source.Height <= 0f)
        {
            return SKSize.Empty;
        }

        float scale = MathF.Min(bounds.Width / source.Width, bounds.Height / source.Height);
        return new SKSize(source.Width * scale, source.Height * scale);
    }

    /// <summary>
    /// Runs a repaint timer, or stops it, according to whether there is a frame to wait for.
    /// </summary>
    /// <remarks>
    /// <see cref="Control.IsDisplayed"/> and an <see cref="Application"/> are what make the
    /// decision, the same rule the marquee progress bar follows: a control that is hidden, sits under
    /// a hidden container, has been taken off its host or lives in a window that was never shown or
    /// has closed has nothing to repaint for, and a tick with no application to deliver it would run
    /// on a pool thread. Paused and still animations have no next frame at all.
    /// </remarks>
    private void UpdateFrames()
    {
        if (_image is not { IsPlaying: true } data
            || data.Duration <= TimeSpan.Zero
            || !IsDisplayed
            || Application.Current is null)
        {
            StopFrames();
            return;
        }

        var position = data.Position;
        if (data.TimeUntilNextFrame(position) is not { } wait)
        {
            StopFrames();
            return;
        }

        // The countdown is aimed at the boundary from the position just read, every time this runs -
        // on a tick and on every repaint. Leaving a running countdown alone because it was already
        // pointing at the same boundary was the mistake: the timer's own period is what it was last
        // armed with, which is a fraction of a millisecond short of a frame whenever the arm happened
        // inside the window, so the error accumulates until a tick lands just before a boundary. That
        // tick repaints the frame about to be replaced, the boundary repaint follows a millisecond
        // later, and the frame it belonged to is on screen for a millisecond - which is what a dropped
        // frame looks like. Re-aiming costs a timer restart and cannot drift, because the fire time is
        // always the boundary rather than the boundary plus an error.
        var interval = wait > MinimumFrameDelay ? wait : MinimumFrameDelay;

        if (_frameTimer is null)
        {
            _frameTimer = CreateTimer(interval, OnFrameTick);
        }
        else
        {
            _frameTimer.Interval = interval;
        }

        // Start restarts the countdown from now, which is what makes the fire time the boundary
        // rather than the boundary plus whatever error the last arm left behind.
        _frameTimer.Start();
    }

    private void StopFrames()
    {
        _frameTimer?.Stop();
    }

    private void OnFrameTick(object sender)
    {
        // The control tree belongs to the caller, so this timer can outlive the application. Once
        // that happens Timer ticks inline on a pool thread, which must not touch the UI.
        if (Application.Current is null || !IsDisplayed)
        {
            StopFrames();
            return;
        }

        // The tick is only a repaint: the frame it shows comes from the clock, and the next wait
        // comes from the source.
        UpdateFrames();
        Invalidate();
    }

    private void OnImageChanged(ImageData data)
    {
        UpdateFrames();
        Invalidate();
    }

    private void ReleaseImage()
    {
        // The data belongs to whoever assigned it, so this only stops listening to it.
        _image?.Changed -= OnImageChanged;
        _image = null;
        StopFrames();
    }

    /// <summary>
    /// Stops listening to the source and disposes the frame timer. The source itself is left alone:
    /// it belongs to whoever assigned it.
    /// </summary>
    /// <param name="disposing">True when the control is being disposed rather than finalized.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseImage();
            _frameTimer?.Dispose();
            _frameTimer = null;
        }

        base.Dispose(disposing);
    }
}
