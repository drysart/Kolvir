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
/// <b>Sizing.</b> The control does not size itself to its source: <see cref="Control.AutoSize"/> is
/// false by default, the same default WinForms' <c>PictureBox</c> has, so the picture is fitted into
/// the bounds the control was given. Give it a size - <see cref="Control.Bounds"/>, <see cref="Control.Width"/>
/// and <see cref="Control.Height"/>, or a dock that fills - and <see cref="SizeMode"/> decides how the
/// picture meets it. An explicit <c>AutoSize = true</c> opts into the other behaviour, where the
/// control takes the source's <see cref="ImageData.NaturalSize"/> and <see cref="SizeMode"/> has no
/// room to act.
/// </para>
/// <para>
/// <b>A control with no size renders nothing.</b> Bounds start at zero, as they do on every control
/// in this library, so a bare <c>new Image { Source = picture }</c> placed free in a panel is a 0x0
/// control. That is deliberate rather than a missing default size: a default here would change panel
/// extents and scroll areas everywhere. Size the control, or ask it to auto-size.
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
    private Action<object>? _sourceChanged;

    /// <summary>
    /// Creates an image control with no source and no size. Give it bounds - <see cref="Control.Bounds"/>,
    /// <see cref="Control.Width"/> and <see cref="Control.Height"/>, or a dock - or set
    /// <see cref="Control.AutoSize"/> to have it take the size of the picture it is given.
    /// </summary>
    public Image()
    {
    }

    /// <summary>
    /// Adds a handler that runs when <see cref="Source"/> is assigned a different value.
    /// </summary>
    /// <param name="handler">The handler, which receives the image control.</param>
    /// <returns>A subscription that removes the handler when it is disposed.</returns>
    /// <remarks>
    /// <para>
    /// Raised at the end of the assignment, after the frame timer, the layout and the repaint have
    /// been updated, so a handler that reads <see cref="Source"/> or the control's layout sees the
    /// image the notification is about.
    /// </para>
    /// <para>
    /// Assigning the value already held raises nothing, because the setter returns early when the
    /// reference has not changed. Assigning null raises it, like any other change.
    /// </para>
    /// <para>
    /// The handler runs inside the assignment and may assign <see cref="Source"/> itself, which
    /// re-enters the setter and raises this notification again, so the control must not be assumed
    /// quiescent. An exception it throws propagates to whatever assigned the source, exactly as every
    /// other handler pair on <see cref="Control"/> does.
    /// </para>
    /// <para>
    /// This is an assignment notification, not a frame notification: an animation advancing raises
    /// <see cref="ImageData"/>'s own change notification, which the control consumes internally to
    /// drive its timer. The contents of an <see cref="ImageData"/> changing does not raise this.
    /// </para>
    /// </remarks>
    public IDisposable AddSourceChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _sourceChanged += handler;
        return new HandlerToken<Action<object>>(RemoveSourceChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddSourceChangedHandler"/> added.</summary>
    public void RemoveSourceChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _sourceChanged -= handler;
    }

    /// <summary>
    /// What the control shows. The control does not take ownership: it draws this and does not
    /// dispose it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Setting it replaces whatever was there without disposing the old data, and the timer follows
    /// the new data: an animation already playing starts ticking, and a still picture stops the timer
    /// at once. The caller disposes what the caller created - see the ownership note on this class.
    /// </para>
    /// <para>
    /// An assignment that changes the value raises <see cref="AddSourceChangedHandler"/> at the end,
    /// which is what lets an application that owns the source tell its own assignment from the
    /// control's internal use of the same property.
    /// </para>
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

            _sourceChanged?.Invoke(this);
        }
    }

    /// <summary>
    /// How the picture is fitted into the content area. Defaults to <see cref="SizeMode.Zoom"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SizeMode.Normal"/> and <see cref="SizeMode.CenterImage"/> draw at the picture's
    /// own size, while <see cref="SizeMode.StretchImage"/> and <see cref="SizeMode.Zoom"/> use the
    /// room available. Whatever the mode, the picture is placed in the content area according to
    /// <see cref="ImageAlign"/>.
    /// </para>
    /// <para>
    /// The mode only has room to act while the control's size comes from somewhere other than the
    /// picture. On a control sized to its source - <see cref="Control.AutoSize"/> with no explicit
    /// bounds - the content area is exactly the picture's size, so <see cref="SizeMode.Zoom"/> and
    /// <see cref="SizeMode.StretchImage"/> are indistinguishable, and a stretched picture is only
    /// stretched in the literal sense of filling a box it already fits.
    /// </para>
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

    /// <summary>
    /// The solid colour an image with no <see cref="Source"/> shows: a literal
    /// <see cref="SKColor"/>, a palette <see cref="ThemeColor"/> role, or
    /// <see cref="PropertyValue.Inherited"/> for no placeholder at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The property reports what was assigned, not what is painted. Read
    /// <see cref="EffectivePlaceholderColor"/> for the colour the surface is filled with. Nothing is
    /// assigned by default, and that means no placeholder: the background the image sits on shows
    /// through, which is what a nullable property meant before this became a union.
    /// </para>
    /// <para>
    /// A role is resolved when it is read, so a palette swap recolours a live image.
    /// </para>
    /// </remarks>
    public Union<SKColor, ThemeColor, InheritedValue> PlaceholderColor { get; set; } = PropertyValue.Inherited;

    /// <summary>
    /// The colour an image with no source is filled with: the assignment, else
    /// <see cref="SKColors.Transparent"/> for nothing.
    /// </summary>
    /// <remarks>
    /// A literal colour is returned as it is and a role is resolved through
    /// <see cref="Theme.ColorOf"/> on every read, so a palette swap is a repaint rather than a
    /// rebuild. Transparent means there is no placeholder, and nothing is filled.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <see cref="PlaceholderColor"/> was assigned <c>default</c>, so it holds no case.
    /// </exception>
    public SKColor EffectivePlaceholderColor
    {
        get
        {
            PlaceholderColor.ThrowIfUnspecified();

            if (PlaceholderColor.TryGetValue<SKColor>(out var literal))
            {
                return literal;
            }

            return PlaceholderColor.TryGetValue<ThemeColor>(out var role)
                ? Theme.ColorOf(role)
                : SKColors.Transparent;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The picture's natural size is reported only while <see cref="Control.AutoSize"/> is on, which is
    /// the only case where the picture can decide the control's size. With it off the control already
    /// has a size, and <see cref="SizeMode"/> resolves the picture into that size - <see cref="SizeMode.Zoom"/>
    /// and <see cref="SizeMode.StretchImage"/> scale into it, <see cref="SizeMode.Normal"/> and
    /// <see cref="SizeMode.CenterImage"/> draw at natural size and clip. There is therefore no mode in
    /// which the picture is a reason for the control to be larger, and reporting one would leak the
    /// source's dimensions out of a control that cannot act on them: a container that mixes measurement
    /// with bounds - an AutoSize table track takes <c>max(measurement, bounds)</c> - would inflate
    /// around a picture the control is visibly not showing at that size.
    /// </para>
    /// <para>
    /// Nothing varies with the picture while <see cref="Control.AutoSize"/> is off, so
    /// <see cref="Control.Measure"/> answers the same for a thumbnail-sized source and a huge one.
    /// <see cref="Control.MinimumSize"/> and <see cref="Control.MaximumSize"/> still apply, because
    /// <see cref="Control.Measure"/> clamps whatever this returns.
    /// </para>
    /// <para>
    /// The answer is the source's own size, never the current frame's, so an animation whose frames
    /// differ by a pixel does not re-lay-out the window on every frame.
    /// </para>
    /// </remarks>
    protected override SKSize MeasureContent(SKSize availableSize)
    {
        // A source's own size, never the current frame's: an animation whose frames differ by a pixel
        // would otherwise re-lay-out the window on every frame. It is only a reason to be that size
        // while the control is allowed to size itself.
        return !AutoSize || _image is null ? SKSize.Empty : _image.NaturalSize;
    }

    /// <summary>
    /// Fills the background with <see cref="EffectivePlaceholderColor"/> while there is no
    /// <see cref="Source"/>, and leaves the background to the base class once there is one.
    /// </summary>
    /// <param name="canvas">Canvas to paint into, in this control's client space.</param>
    /// <param name="rect">The area to paint.</param>
    protected override void OnPaintBackground(SKCanvas canvas, SKRect rect)
    {
        base.OnPaintBackground(canvas, rect);

        if (_image is null)
        {
            var placeholder = EffectivePlaceholderColor;
            if (placeholder.Alpha != 0)
            {
                Painter.Fill(canvas, rect, placeholder, Border.Corners);
            }
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
