using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace Kolvir.Backend.AppKit;

/// <summary>
/// The view an <see cref="AppKitWindowBackend"/> draws into: an AppKit <see cref="NSView"/> that
/// rasterizes the control tree of the <see cref="UiHost"/> it is given with SkiaSharp into a
/// BGRA bitmap and shows it as the contents of its layer. Input arrives as AppKit events and is
/// forwarded to the host in the same form the Win32 and GTK backends use.
/// <para>
/// The bitmap route is deliberate: there is no SkiaSharp view for AppKit in SkiaSharp 4.x. What
/// the bitmap is presented *through* is not a matter of taste, though. Drawing it into the
/// window's own CoreGraphics context with <c>DrawImage</c> makes CoreGraphics keep a full-size
/// backing store per frame while the window is being resized, and a drag to near full screen on a
/// Retina display then peaked at 1.8 GB of physical footprint - the same drag through a layer's
/// <c>contents</c> stays near 150 MB, and a plain view that fills itself with a solid colour stays
/// at 123 MB. So the view is layer backed and overrides <see cref="WantsUpdateLayer"/> rather
/// than <see cref="NSView.DrawRect(CGRect)"/>: AppKit then asks for a layer update and never
/// allocates a backing store of its own.
/// </para>
/// <para>
/// The view is also the process's text input client (<see cref="INSTextInputClient"/>), which is
/// what lets a dead key, an Option composition or an IME syllable reach a text box as marked text
/// and a commit instead of as raw key characters. Read <c>dev-docs/IME.md</c> section 6.3 before
/// changing <see cref="KeyDown"/>: the order of the checks there is load bearing three times over.
/// </para>
/// </summary>
internal sealed class AppKitSkiaView : NSView, INSTextInputClient
{
    /// <summary>Set <c>KOLVIR_DEBUG=1</c> to trace host work to stderr, which helps on odd setups.</summary>
    private static readonly bool DebugTrace =
        Environment.GetEnvironmentVariable("KOLVIR_DEBUG") is "1" or "true";

    /// <summary>How many frames in a row may be queued by an invalidation raised while painting.</summary>
    private const int MaxDeferredRepaints = 8;

    /// <summary>A drawing buffer is only given back once the window needs under half its area.</summary>
    private const int OversizedAreaFactor = 2;

    /// <summary>How long the window has to hold its size before an oversized buffer is given back.</summary>
    private const long ShrinkDelayMs = 750;

    /// <summary>How often the tracing memory report is printed.</summary>
    private const long MemoryReportIntervalMs = 5000;

    private const long Mib = 1024 * 1024;

    /// <summary>The pasteboard type of a dragged file.</summary>
    private const string FileUrlType = "public.file-url";

    /// <summary>The pasteboard type of dragged plain text.</summary>
    private const string PlainTextType = "public.utf8-plain-text";

    /// <summary>The effects a drop offers the drag source.</summary>
    private const AppKitDragEffect DragEffects = AppKitDragEffect.Copy | AppKitDragEffect.Move;

    /// <summary>The pasteboard types this view is registered to receive as a drag destination.</summary>
    private static readonly string[] DragTypes = [FileUrlType, PlainTextType];

    private readonly UiHost _host;
    private readonly AppKitComposition _composition;
    private bool _useHardwareAcceleration;

    private IAppKitTextInterpreter _interpreter = AppKitTextInterpreter.Instance;

    /// <summary>The event the input manager is being handed, for the commands it sends back.</summary>
    private NSEvent? _eventBeingInterpreted;

    /// <summary>How deep the key path currently is, which is what makes a re-entry a refusal.</summary>
    private int _keyPathDepth;

    /// <summary>True while a command the input manager declined is being forwarded to the host.</summary>
    private bool _forwardingCommand;

    private NSTrackingArea? _trackingArea;
    private bool _tracksPointerInBackgroundWindow;
    private AppKitMetalSurface? _metal;
    private string? _deviceKey;
    private string _gfxReason = "cpu (not created)";
    private bool _metalUnavailable;
    private SKBitmap? _bitmap;
    private SKSurface? _skia;
    private CGDataProvider? _provider;
    private CGColorSpace? _colorSpace;
    private CGImage? _presented;
    private string? _diagnostic;
    private string? _bufferError;
    private int _width;
    private int _height;
    private int _scale;
    private int _bufferGeneration;
    private int _paintedGeneration;
    private long _lastSizeChange;
    private bool _disposed;

    private bool _inDraw;
    private bool _invalidationDuringDraw;
    private int _deferredRepaints;
    private AppKitDragPayload? _dragPayload;
    private IAppKitDragSink? _dragSink;

    private long _frames;
    private long _buffersAllocated;
    private long _reportedAt;
    private long _reportedFrames;

    /// <summary>Creates a view that draws and drives <paramref name="host"/>.</summary>
    /// <param name="host">The control tree to paint.</param>
    /// <param name="hardwareAcceleration">
    /// True to try the Metal tiers before the CPU <c>CGImage</c> path. Popup windows leave this
    /// false, which is the equivalent of the Win32 backend keeping its popups on the DIB.
    /// </param>
    /// <param name="deviceKey">
    /// The identity of the Metal device to draw on, or null for whichever the system picks. A key that
    /// names no device here leaves the view on the CPU path rather than quietly drawing on the default:
    /// the application asked for a particular GPU.
    /// </param>
    public AppKitSkiaView(UiHost host, bool hardwareAcceleration = false, string? deviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(host);

        _host = host;
        _useHardwareAcceleration = hardwareAcceleration;
        _deviceKey = deviceKey;

        // macOS reports a composition as marked text and ends it with a commit or a cancel; the
        // state behind that lives outside this view so a test can drive it without an NSView.
        _composition = new AppKitComposition(host, DiscardMarkedText);

        // A hand run can be the only way to see what the input method does once its candidate list
        // is open, so the windows that take and lose the keyboard are traced when a file was asked
        // for. Nothing is registered otherwise.
        if (AppKitImeTrace.Enabled)
        {
            AppKitImeTrace.Line($"view created; app key window {AppKitImeTrace.Describe(NSApplication.SharedApplication.KeyWindow)}");
            NSNotificationCenter.DefaultCenter.AddObserver(
                NSWindow.DidBecomeKeyNotification,
                n => AppKitImeTrace.Line($"WINDOW became key: {AppKitImeTrace.Describe(n.Object as NSWindow)}"));
            NSNotificationCenter.DefaultCenter.AddObserver(
                NSWindow.DidResignKeyNotification,
                n => AppKitImeTrace.Line($"WINDOW resigned key: {AppKitImeTrace.Describe(n.Object as NSWindow)}"));
            NSNotificationCenter.DefaultCenter.AddObserver(
                NSApplication.DidBecomeActiveNotification,
                _ => AppKitImeTrace.Line("APP became active"));
            NSNotificationCenter.DefaultCenter.AddObserver(
                NSApplication.DidResignActiveNotification,
                _ => AppKitImeTrace.Line("APP resigned active"));
        }

        if (!hardwareAcceleration)
        {
            _gfxReason = "cpu (disabled)";
        }

        _host.AddInvalidatedHandler(OnHostInvalidated);
        _host.AddCursorChangedHandler(OnHostCursorChanged);
        Theme.AddChangedHandler(OnThemeChanged);
        _reportedAt = Environment.TickCount64;

        // Frames are painted into a buffer that outlives them and handed to the layer as it stands,
        // so only the part of it that changed has to be repainted.
        _host.PartialUpdates = true;

        // Layer backed from the start, so AppKit never creates a backing store for this view and
        // the first frame already goes through the layer instead of the window's context.
        WantsLayer = true;
    }

    /// <summary>
    /// The sink that receives the drags the view is registered for, or null when there is none. A
    /// view that is never given a sink never registers for a dragged type, so AppKit never sends it
    /// a drag at all.
    /// </summary>
    public IAppKitDragSink? DragSink
    {
        get => _dragSink;
        set
        {
            if (ReferenceEquals(_dragSink, value))
            {
                return;
            }

            _dragSink = value;

            if (value is null)
            {
                _dragPayload = null;
                UnregisterDraggedTypes();
            }
            else
            {
                RegisterForDraggedTypes(DragTypes);
            }
        }
    }

    /// <summary>
    /// A layer backed view is not marked dirty when it is resized: AppKit's default is to leave the
    /// layer showing what it already has and let the compositor stretch it to the new bounds. That
    /// is why the layer path has to ask for a frame itself, or a whole drag shows the frame from
    /// before the drag started, scaled, and the window only paints at its new size once the mouse
    /// is released.
    /// </summary>
    public override void SetFrameSize(CGSize newSize)
    {
        var previous = Frame.Size;
        base.SetFrameSize(newSize);

        if (previous.Width != newSize.Width || previous.Height != newSize.Height)
        {
            NeedsDisplay = true;
        }
    }

    /// <summary>
    /// A move to a display with a different scale changes the device-pixel size without changing
    /// the frame. The CPU path notices in <see cref="EnsureBuffer"/>; the Metal path notices when
    /// <see cref="AppKitMetalSurface.EnsureSize"/> sees the new pixel size, and either way the
    /// frame has to be asked for.
    /// </summary>
    public override void DidChangeBackingProperties()
    {
        base.DidChangeBackingProperties();
        NeedsDisplay = true;
    }

    /// <summary>The platform independent UI host this view paints.</summary>
    public UiHost Host => _host;

    /// <summary>
    /// The seam that hands a key to AppKit's input manager. It exists for the reason
    /// <see cref="IAppKitTextInterpreter"/> gives; production never sets it.
    /// </summary>
    internal IAppKitTextInterpreter TextInterpreter
    {
        get => _interpreter;
        set => _interpreter = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>The marked text state this view mirrors for the input manager.</summary>
    internal AppKitComposition Composition => _composition;

    /// <summary>
    /// How many times a re-entrant call into the key path has been refused. The guard is the
    /// difference between a command the input manager sends back and a ping-pong between the two,
    /// so it is read by a test rather than merely relied on.
    /// </summary>
    internal int ReentrancyRefusals { get; private set; }

    /// <summary>
    /// What this view is presenting with, for a one-line diagnostic: <c>metal</c> when the Metal
    /// tiers are live, or <c>cpu (...)</c> with the step that put it there.
    /// </summary>
    internal string GraphicsPath => _metal is not null ? "metal" : _gfxReason;

    /// <summary>The Metal device name when <see cref="GraphicsPath"/> is <c>metal</c>, else null.</summary>
    internal string? GraphicsRenderer => _metal?.Renderer;

    /// <summary>
    /// The identity of the Metal device this view draws on, or null when it is on the CPU path.
    /// </summary>
    internal string? MetalDeviceKey => _metal?.DeviceKey;

    /// <summary>
    /// Makes this view draw with <paramref name="option"/> from now on, rebuilding only what the
    /// presentation is made of.
    /// </summary>
    /// <param name="option">The choice, already known to be one this machine offers.</param>
    /// <param name="reason">One sentence saying what happened. Never null.</param>
    /// <returns>True when the view is drawing with <paramref name="option"/> afterwards.</returns>
    /// <remarks>
    /// Unlike Windows there is nothing to take back from a swap chain: the Metal surface is a sublayer of
    /// this view's backing layer, so a switch releases one layer and builds another underneath the same
    /// window. The window, the controls and their state are untouched, so a switch here costs a frame
    /// rather than a window - and the window is never shown again, which is what makes it invisible.
    /// </remarks>
    internal bool TrySwitchAcceleration(GpuAccelerationOption option, out string reason)
    {
        ArgumentNullException.ThrowIfNull(option);

        if (option.Kind == GpuAccelerationKind.Cpu)
        {
            DropMetal("cpu (chosen)");

            // Told apart from a failure: this is the answer the user picked, not one to recover from, so
            // the frame path is switched off rather than left free to bring Metal back.
            _useHardwareAcceleration = false;
            _deviceKey = null;
            reason = "Now drawing on the CPU path.";
            return true;
        }

        if (option.Kind != GpuAccelerationKind.Metal)
        {
            reason = $"{option.Name} cannot draw this window on macOS.";
            return false;
        }

        ReleaseMetal();
        _useHardwareAcceleration = true;
        _metalUnavailable = false;
        _deviceKey = option.Device?.Key;

        TryCreateMetal(Bounds, BackingScaleOf());

        if (_metal is null)
        {
            // The window stays on the CPU path: reporting success would leave a settings list naming a
            // card that is drawing nothing.
            reason = $"Fell back to the CPU path: {_gfxReason}.";
            return false;
        }

        reason = $"Now drawing with Metal: {_metal.Renderer}.";
        return true;
    }

    /// <summary>
    /// Builds the Metal stack once the view has its window and backing scale, so the presentation
    /// path is decided before the window reports itself shown. Calling it again does nothing.
    /// </summary>
    internal void PrepareGraphics()
    {
        if (!_useHardwareAcceleration || _metal is not null || _metalUnavailable)
        {
            return;
        }

        TryCreateMetal(Bounds, BackingScaleOf());
    }

    /// <summary>Top-left origin, y down: the coordinate space the UI library draws in.</summary>
    public override bool IsFlipped => true;

    /// <summary>
    /// The view paints its layer's contents itself, so AppKit must call <see cref="UpdateLayer"/>
    /// instead of <see cref="NSView.DrawRect(CGRect)"/>; drawing in <c>DrawRect</c> would give the
    /// view a backing store of its own, which is the allocation this exists to avoid.
    /// </summary>
    public override bool WantsUpdateLayer => true;

    public override void UpdateLayer()
    {
        _frames++;

        // AppKit does not re-enter a draw, and doing so would paint the same buffer twice, so a
        // nested call can only mean something asked for a frame while painting.
        if (_inDraw)
        {
            Trace("draw skipped: already drawing");
            return;
        }

        _inDraw = true;
        try
        {
            DrawFrame();
        }
        finally
        {
            _inDraw = false;
        }

        // A control that invalidates while it paints needs one more frame to show that state, but
        // one that does it every frame would spin the run loop as fast as the machine allows and
        // grow memory with it, so the deferral is bounded and says so when it gives up.
        if (!_invalidationDuringDraw)
        {
            _deferredRepaints = 0;
            return;
        }

        _invalidationDuringDraw = false;
        _deferredRepaints++;

        if (_deferredRepaints > MaxDeferredRepaints)
        {
            if (_deferredRepaints == MaxDeferredRepaints + 1)
            {
                Trace($"repaint loop: the UI asked for a frame from inside {_deferredRepaints} draws in a row; " +
                      "repainting only when something else asks from now on");
            }

            return;
        }

        NeedsDisplay = true;
    }

    private void DrawFrame()
    {
        var bounds = Bounds;

        // This should not happen, and it looks exactly like "the window is empty" from the
        // outside, so it says so when tracing is on.
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            Trace($"draw skipped: empty bounds {bounds.Width}x{bounds.Height}");
            return;
        }

        var scale = BackingScaleOf();
        SyncHostSize();

        // Resizing the host lays its children out, which asks for a repaint - but this frame is
        // about to paint at exactly the size that was just set, so that invalidation is already
        // satisfied. Asking for another frame on its behalf would double the work of every step of
        // a live resize.
        _invalidationDuringDraw = false;

        // The backend creates its surface when the window is really shown, and a window shown
        // before PrepareGraphics has run falls in here on its first draw.
        if (_metal is null && !_metalUnavailable && _useHardwareAcceleration)
        {
            TryCreateMetal(bounds, scale);
        }

        if (_metal is not null)
        {
            try
            {
                DrawMetal(bounds, scale);
            }
            catch (Exception ex)
            {
                // A paint that throws must not escape UpdateLayer; the next frame is the CPU path.
                Trace($"metal draw threw {ex.GetType().Name}: {ex.Message}");
                DropMetal($"cpu (metal draw threw {ex.GetType().Name})");
            }

            return;
        }

        EnsureBuffer((int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height), scale);

        if (_skia is null || _bitmap is null || _provider is null || _colorSpace is null)
        {
            DrawDiagnostic(_bufferError ?? "the SkiaSharp drawing buffer could not be created");
            return;
        }

        if (DebugTrace && (_frames <= 3 || _frames % 100 == 0))
        {
            Trace($"draw #{_frames} {_width}x{_height} at {_scale}x");
        }

        try
        {
            var canvas = _skia.Canvas;
            canvas.Save();
            canvas.Scale(scale);

            // The buffer is larger than the window so that a drag does not reallocate it, and the
            // clear that starts every frame honours the clip, so without this each frame would also
            // wipe all the headroom. This is after the scale because the clip is in the same
            // logical units the UI paints in, not in pixels.
            canvas.ClipRect(new SKRect(0, 0, _width, _height));
            _host.Paint(canvas, full: _paintedGeneration != _bufferGeneration);
            _paintedGeneration = _bufferGeneration;
            canvas.Restore();
            canvas.Flush();
        }
        catch (Exception ex)
        {
            DrawDiagnostic($"painting the UI failed: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        Present();

        // A window that painted is a window whose last complaint is out of date.
        _diagnostic = null;
    }

    private int BackingScaleOf() =>
        Math.Max((int)Math.Round(Window is null ? 1d : (double)Window.BackingScaleFactor), 1);

    /// <summary>
    /// Tries to bring the Metal stack up at the view's current size. A failure is remembered for
    /// the life of the window, exactly as the Win32 probe is, so a window that fell back does not
    /// retry a losing probe on every frame.
    /// </summary>
    private void TryCreateMetal(CGRect bounds, int scale)
    {
        var hostLayer = Layer;

        if (hostLayer is null)
        {
            // AppKit has not made the backing layer yet; this is not a reason to give up, because
            // the next draw asks again.
            _gfxReason = "cpu (no host layer)";
            return;
        }

        var width = Math.Max((int)Math.Round(bounds.Width) * scale, 1);
        var height = Math.Max((int)Math.Round(bounds.Height) * scale, 1);
        var frame = new CGRect(0, 0, bounds.Width, bounds.Height);

        var metal = AppKitMetalSurface.TryCreate(hostLayer, frame, width, height, scale, _deviceKey, out var reason);

        _gfxReason = reason;

        if (metal is null)
        {
            _metalUnavailable = true;
            Trace($"gfx: {reason}");
            return;
        }

        _metal = metal;
        _metalUnavailable = false;
        Trace($"gfx: {reason} ({metal.Renderer})");
    }

    /// <summary>
    /// Presents one frame through Metal. The retained tier paints only the host's damage into the
    /// persistent offscreen surface and blits the whole buffer; the direct tier paints a fresh
    /// drawable in full. A drawable that is not available skips the frame rather than spinning.
    /// </summary>
    private void DrawMetal(CGRect bounds, int scale)
    {
        var metal = _metal!;
        var width = (int)Math.Round(bounds.Width) * scale;
        var height = (int)Math.Round(bounds.Height) * scale;

        if (width <= 0 || height <= 0)
        {
            Trace($"metal draw skipped: empty drawable {width}x{height}");
            return;
        }

        if (!metal.EnsureSize(width, height, scale))
        {
            DropMetal("cpu (invalid drawable size)");
            return;
        }

        // The backing layer can be replaced when the view moves between windows; a sublayer that
        // was attached to the old one would never be composited.
        if (Layer is { } hostLayer && metal.Layer is { } metalLayer &&
            !ReferenceEquals(metalLayer.SuperLayer, hostLayer))
        {
            hostLayer.AddSublayer(metalLayer);
        }

        if (metal.HasRetainedBuffer && metal.Surface is { } retained)
        {
            var damaged = _host.IsUpdatePending
                || metal.NeedsFullRepaint
                || _paintedGeneration != _bufferGeneration;

            if (AppKitMetalTrace.IsEnabled)
            {
                AppKitMetalTrace.Write(
                    $"frame {width}x{height} retained damaged={damaged} full={metal.NeedsFullRepaint}");
            }

            if (damaged)
            {
                // The offscreen contents are undefined when it is created, and a resize or scale
                // change makes them so again; TakeNeedsFull is what forces that first frame full.
                var full = metal.TakeNeedsFull() || _paintedGeneration != _bufferGeneration;
                _paintedGeneration = _bufferGeneration;
                PaintHost(retained, full, scale, bounds);
            }

            var result = metal.PresentRetained();

            if (result == AppKitMetalPresentResult.Skipped)
            {
                return;
            }

            if (result == AppKitMetalPresentResult.RetryDirect)
            {
                // The retained buffer failed mid-frame. Repaint this one frame against a fresh
                // drawable and present it; the next frame stays on the direct tier.
                var retry = metal.PresentDirect(surface => PaintHost(surface, full: true, scale, bounds));

                if (retry == AppKitMetalPresentResult.Lost)
                {
                    DropMetal("cpu (metal context lost)");
                }

                return;
            }

            if (result == AppKitMetalPresentResult.Lost)
            {
                DropMetal("cpu (metal context lost)");
            }

            return;
        }

        // The direct tier has no retained contents, so every frame it serves is a full frame.
        var direct = metal.PresentDirect(surface => PaintHost(surface, full: true, scale, bounds));

        if (direct == AppKitMetalPresentResult.Lost)
        {
            DropMetal("cpu (metal context lost)");
        }
    }

    private SKRectI PaintHost(SKSurface surface, bool full, int scale, CGRect bounds)
    {
        var canvas = surface.Canvas;
        canvas.Save();
        canvas.Scale(scale);
        canvas.ClipRect(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height));
        var area = _host.Paint(canvas, full);
        canvas.Restore();
        canvas.Flush();
        return area;
    }

    /// <summary>
    /// Drops the Metal stack and returns the view to its CPU path for the rest of its life. The
    /// CPU buffer may never have been allocated, so the next frame builds it and paints in full.
    /// </summary>
    private void DropMetal(string reason)
    {
        ReleaseMetal();
        _metalUnavailable = true;
        _gfxReason = reason;
        Trace($"gfx: {reason}");
    }

    /// <summary>
    /// Gives up the Metal surface and asks for a frame, without deciding anything about why - a switch
    /// to another tier wants to build again straight away, and a failure wants to stay on the CPU path.
    /// </summary>
    private void ReleaseMetal()
    {
        _metal?.Dispose();
        _metal = null;
        _bufferGeneration++;
        NeedsDisplay = true;
    }

    /// <summary>
    /// Hands the pixels that were just painted to the layer. CoreGraphics treats a
    /// <see cref="CGImage"/> as immutable and is free to keep the decode it made of the provider's
    /// memory, so one cached image would pin the window to whatever the buffer held when it was
    /// first drawn - and the first frame is drawn before the control tree exists, which is an
    /// empty window. A fresh image over the pixels that were just painted is what makes each frame
    /// show the current UI; the bitmap, its Skia surface and the data provider over its pixels
    /// stay cached, so a frame allocates nothing but the image itself.
    /// </summary>
    private void Present()
    {
        if (_bitmap is null || _provider is null || _colorSpace is null || Layer is not { } layer)
        {
            return;
        }

        CGImage? image = null;
        try
        {
            image = new CGImage(
                _width * _scale,
                _height * _scale,
                8,
                32,
                _bitmap.RowBytes,
                _colorSpace,
                CGBitmapFlags.PremultipliedFirst | CGBitmapFlags.ByteOrder32Little,
                _provider,
                null,
                false,
                CGColorRenderingIntent.Default);

            if (((INativeObject)image).Handle == IntPtr.Zero)
            {
                image.Dispose();
                image = null;
                Trace("present failed: CoreGraphics built no image from the SkiaSharp bitmap");
                return;
            }

            // Resize rather than the default centring, because the image is exactly the size of the
            // window and only the headroom around it has to be cropped off; the scale is what
            // keeps a Retina window from being shown at half size.
            layer.ContentsGravity = CoreAnimation.CALayer.GravityResize;
            layer.ContentsScale = _scale;
            layer.Contents = image;

            // The layer retained the image, so the view's own reference goes; dropping it here
            // rather than at the next frame keeps exactly one frame's image alive.
            _presented?.Dispose();
            _presented = image;
            image = null;
        }
        catch (Exception ex)
        {
            image?.Dispose();
            Trace($"present failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public override bool AcceptsFirstResponder() => true;

    /// <summary>Clicking an inactive window both activates it and clicks, like every other app.</summary>
    public override bool AcceptsFirstMouse(NSEvent? theEvent) => true;

    public override void ViewDidMoveToWindow()
    {
        base.ViewDidMoveToWindow();
        SyncHostSize();
        Window?.MakeFirstResponder(this);
        NeedsDisplay = true;
    }

    public override void UpdateTrackingAreas()
    {
        base.UpdateTrackingAreas();
        SyncTrackingArea();
    }

    /// <summary>
    /// True to deliver pointer movement to this view even when its window is not the key window,
    /// which is what a popup needs: a popup is never key, so a key-window-only tracking area never
    /// becomes active and nothing inside the popup highlights under the pointer.
    /// </summary>
    public bool TracksPointerInBackgroundWindow
    {
        get => _tracksPointerInBackgroundWindow;
        set
        {
            if (_tracksPointerInBackgroundWindow == value)
            {
                return;
            }

            _tracksPointerInBackgroundWindow = value;
            SyncTrackingArea();
        }
    }

    /// <summary>
    /// Rebuilds the tracking area the view reports pointer movement through, so the options always
    /// match <see cref="TracksPointerInBackgroundWindow"/> and the view's current bounds.
    /// </summary>
    private void SyncTrackingArea()
    {
        if (_trackingArea is not null)
        {
            RemoveTrackingArea(_trackingArea);
            _trackingArea.Dispose();
            _trackingArea = null;
        }

        if (_disposed)
        {
            return;
        }

        _trackingArea = new NSTrackingArea(
            Bounds,
            NSTrackingAreaOptions.MouseEnteredAndExited |
            NSTrackingAreaOptions.MouseMoved |
            (_tracksPointerInBackgroundWindow ? NSTrackingAreaOptions.ActiveAlways : NSTrackingAreaOptions.ActiveInKeyWindow) |
            NSTrackingAreaOptions.InVisibleRect |
            NSTrackingAreaOptions.EnabledDuringMouseDrag,
            this,
            null!);

        AddTrackingArea(_trackingArea);
    }

    public override void MouseDown(NSEvent theEvent)
    {
        Window?.MakeFirstResponder(this);
        _host.ProcessMouseDown(PointFrom(theEvent), MouseButtons.Left, ModifiersFrom(theEvent), ClickCountFrom(theEvent));
        InvalidateCaretCoordinates();
    }

    public override void MouseUp(NSEvent theEvent)
    {
        _host.ProcessMouseUp(PointFrom(theEvent), MouseButtons.Left, ModifiersFrom(theEvent));
        InvalidateCaretCoordinates();
    }

    public override void MouseDragged(NSEvent theEvent) => MoveTo(theEvent);

    public override void MouseMoved(NSEvent theEvent) => MoveTo(theEvent);

    public override void MouseEntered(NSEvent theEvent) => MoveTo(theEvent);

    public override void MouseExited(NSEvent theEvent) => _host.ProcessMouseLeave();

    public override void RightMouseDown(NSEvent theEvent) =>
        _host.ProcessMouseDown(PointFrom(theEvent), MouseButtons.Right, ModifiersFrom(theEvent), ClickCountFrom(theEvent));

    public override void RightMouseUp(NSEvent theEvent)
    {
        var point = PointFrom(theEvent);
        _host.ProcessMouseUp(point, MouseButtons.Right, ModifiersFrom(theEvent));

        // AppKit has no context menu message of its own here, so the release drives it, which is
        // also when Win32 asks for one.
        _host.ProcessContextMenu(point);
    }

    public override void OtherMouseDown(NSEvent theEvent) =>
        _host.ProcessMouseDown(PointFrom(theEvent), MouseButtons.Middle, ModifiersFrom(theEvent), ClickCountFrom(theEvent));

    public override void OtherMouseUp(NSEvent theEvent) =>
        _host.ProcessMouseUp(PointFrom(theEvent), MouseButtons.Middle, ModifiersFrom(theEvent));

    public override void OtherMouseDragged(NSEvent theEvent) => MoveTo(theEvent);

    /// <summary>
    /// How far one line of wheel scroll moves, in points. AppKit's own line height is ten points -
    /// an event's <c>DeltaY</c> is its <c>ScrollingDeltaY</c> over ten - but a line is scaled up by
    /// half again here, because at ten points a wheel scrolls noticeably slower than the same wheel
    /// does on Windows and Linux. At fifteen, the three lines a typical wheel reports land on
    /// forty-five points, all but matching the forty-eight pixel detent the other backends use.
    /// </summary>
    private const float WheelLineHeight = 15f;

    /// <summary>
    /// Forwards a wheel turn.
    /// </summary>
    /// <remarks>
    /// A trackpad reports points and a wheel reports whole lines, and AppKit says which through
    /// <see cref="NSEvent.HasPreciseScrollingDeltas"/>. A point is already a logical pixel, so
    /// those pass straight through; a line is converted with <see cref="WheelLineHeight"/>. A
    /// line is not a detent - treating it as one scrolls the wheel nearly five times too fast -
    /// and the delta the system reports already carries the user's scroll speed, so no further
    /// acceleration belongs here. AppKit's sign already matches the host's convention, where a
    /// positive delta scrolls toward the start of the content.
    /// </remarks>
    public override void ScrollWheel(NSEvent theEvent)
    {
        var delta = (float)theEvent.ScrollingDeltaY * (theEvent.HasPreciseScrollingDeltas
            ? 1f
            : WheelLineHeight);

        _host.ProcessMouseWheel(PointFrom(theEvent), delta, ModifiersFrom(theEvent));
        InvalidateCaretCoordinates();
    }

    /// <summary>
    /// Routes one key press: the library first, then the platform's input manager.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is deliberate and must not change. The library sees the key first, so a shortcut,
    /// a menu mnemonic or an editing key it owns never reaches the input method - which is also why
    /// a key it consumes has to reset the input method itself, below. The Command and Control
    /// checks come next: a Command chord belongs to the menu bar, and a Control chord is the
    /// library's own accelerator, so neither may be turned into text. Only what is left goes to
    /// <c>interpretKeyEvents:</c>, which is what drives the input context.
    /// </para>
    /// <para>
    /// There is no raw-text fallback any more. <c>AppKitKeyMap.ToText</c> used to run here and hand
    /// the event's characters straight to the field, which double-inserted as soon as the input
    /// manager also committed them - and made a dead key both compose and insert its accent.
    /// <c>insertText:</c> is the only route a composed character takes.
    /// </para>
    /// </remarks>
    public override void KeyDown(NSEvent theEvent)
    {
        // `interpretKeyEvents:` can call `doCommandBySelector:` synchronously, which forwards the
        // key into the host, which can re-enter this method. Win32 needed the same guard so a
        // cancel could not ping-pong the window procedure; here it also keeps a host callback that
        // synthesizes a key from recursing. The count is a test's observable.
        if (_keyPathDepth > 0)
        {
            ReentrancyRefusals++;
            Trace("KeyDown refused: already handling a key (a command re-entered the key path)");
            return;
        }

        _keyPathDepth++;

        try
        {
            var modifiers = ModifiersFrom(theEvent);
            var key = AppKitKeyMap.ToKey(theEvent.KeyCode);

            if (AppKitImeTrace.Enabled)
            {
                AppKitImeTrace.Line(
                    $"KEY {key} mods={modifiers} marked={_composition.HasMarkedText} " +
                    $"ourWindowKey={Window?.IsKeyWindow} appKey={AppKitImeTrace.Describe(NSApplication.SharedApplication.KeyWindow)} " +
                    $"eventWindow={AppKitImeTrace.Describe(theEvent.Window)} " +
                    $"firstResponder={Window?.FirstResponder?.GetType().Name ?? "none"}");

                if (_composition.HasMarkedText && key is Key.Space or Key.Enter or Key.Escape)
                {
                    var selection = _composition.SelectedLocation + "," + _composition.SelectedLength;
                    AppKitImeTrace.Boundary($"{key} pressed while a mark is live (selection {selection})");

                    if (_composition.SelectedLength > 0)
                    {
                        AppKitImeTrace.Boundary("a commit key arrived with a candidate selected");
                    }
                }

                if (!_composition.HasMarkedText && key == Key.Enter)
                {
                    AppKitImeTrace.Boundary("Enter pressed with no mark live");
                }
            }

            if (key != Key.None && _host.ProcessKeyDown(key, modifiers))
            {
                if (AppKitImeTrace.Enabled)
                {
                    AppKitImeTrace.Line($"key {key} taken by the host; the input method never sees it");
                    AppKitImeTrace.Line($"reset the mark: reason=key taken by the host, key={key}, marked={_composition.HasMarkedText}");
                }

                // The input method never saw this key, so whatever it was composing cannot survive
                // it: the field has already dropped its preedit, and the input manager's own state
                // is reset here. This is GTK's IMContext.Reset and Win32's CPS_CANCEL.
                _composition.Reset();

                // The key may have moved the caret, which is what a candidate window anchors to.
                InvalidateCaretCoordinates();
                return;
            }

            // Command combinations belong to the menu bar. Anything the menu did not claim must not
            // fall through to text input, or Cmd+D would type a "d".
            if ((theEvent.ModifierFlags & NSEventModifierMask.CommandKeyMask) != 0)
            {
                return;
            }

            // Control combinations are the library's own shortcuts; they never produce text.
            if ((modifiers & Modifiers.Control) != Modifiers.None)
            {
                return;
            }

            _eventBeingInterpreted = theEvent;

            try
            {
                _interpreter.Interpret(theEvent, this);
            }
            finally
            {
                _eventBeingInterpreted = null;

                if (AppKitImeTrace.Enabled)
                {
                    AppKitImeTrace.Line($"KEY {key} routed: marked={_composition.HasMarkedText} appKey={AppKitImeTrace.Describe(NSApplication.SharedApplication.KeyWindow)}");
                }

                // Whatever the input manager did, the document may have changed under it.
                InvalidateCaretCoordinates();
            }
        }
        finally
        {
            _keyPathDepth--;
        }
    }

    public override void KeyUp(NSEvent theEvent)
    {
        var key = AppKitKeyMap.ToKey(theEvent.KeyCode);
        if (key != Key.None)
        {
            _host.ProcessKeyUp(key, ModifiersFrom(theEvent));
        }
    }

    /// <summary>
    /// A composition cannot outlive the focus. AppKit asks before the view stops being the first
    /// responder, which is the one moment both the input manager and the control still know about
    /// the composition, so it is ended here rather than on the next key.
    /// </summary>
    /// <remarks>
    /// The host already cancels a composition when the focused control changes
    /// (<see cref="UiHost.SetFocus"/>), so this is about the input manager's own marked text when
    /// the window itself loses the keyboard.
    /// </remarks>
    public override bool ResignFirstResponder()
    {
        if (AppKitImeTrace.Enabled)
        {
            AppKitImeTrace.Line($"reset the mark: reason=first responder resigning, marked={_composition.HasMarkedText}");
        }

        _composition.Reset();
        return base.ResignFirstResponder();
    }

    /// <summary>The range a text input client answers when it has no marked text.</summary>
    private static readonly NSRange NoRange = new(NSRange.NotFound, 0);

    /// <summary>
    /// The input manager's commit: the final text of a composition, or an emoji, or any character
    /// it produced itself. <see cref="UiHost.CommitComposition"/> routes it through the ordinary
    /// text path, so undo, validation, <c>MaxLength</c> and the autocomplete handler all see typing.
    /// </summary>
    /// <param name="text">A string or an attributed string; the library draws its own attributes.</param>
    /// <param name="replacementRange">Ignored: v1 always commits at the caret, as section 3 records.</param>
    /// <remarks>
    /// This is the only commit route for a composed character. Nothing else in this view inserts
    /// text, which is what keeps a dead key from composing and inserting the accent as well.
    /// </remarks>
    public void InsertText(NSObject text, NSRange replacementRange)
    {
        if (AppKitImeTrace.Enabled)
        {
            AppKitImeTrace.Line($"insertText '{TextOf(text)}' replacement={replacementRange.Location},{replacementRange.Length}");
            AppKitImeTrace.Boundary("a commit arrived");
        }

        _composition.InsertText(TextOf(text));

        // The commit moved the caret, which is where the next candidate window or viewer goes.
        InvalidateCaretCoordinates();
    }

    /// <summary>
    /// The input manager's preedit: the text so far and the caret inside it. The first call opens
    /// the composition, and every later one updates it.
    /// </summary>
    /// <param name="text">The marked string.</param>
    /// <param name="selectedRange">
    /// The range the input method has selected inside <paramref name="text"/>, in UTF-16 code
    /// units. Its location is the preedit caret; its length is the run a conversion covers.
    /// </param>
    /// <param name="replacementRange">Ignored, as for <see cref="InsertText"/>.</param>
    public void SetMarkedText(NSObject text, NSRange selectedRange, NSRange replacementRange)
    {
        if (AppKitImeTrace.Enabled)
        {
            if (!_composition.HasMarkedText)
            {
                AppKitImeTrace.Boundary("a composition starts");
            }

            AppKitImeTrace.Line($"setMarkedText '{TextOf(text)}' selected={selectedRange.Location},{selectedRange.Length} replacement={replacementRange.Location},{replacementRange.Length} candidate={selectedRange.Length > 0}");
        }

        _composition.SetMarkedText(
            TextOf(text),
            (int)selectedRange.Location,
            (int)selectedRange.Length);

        // The preedit is drawn in front of the caret, so its width moves the answer too.
        InvalidateCaretCoordinates();
    }

    /// <summary>
    /// The input manager ended its composition without committing. The preedit goes and nothing is
    /// inserted, so no value, caret or undo entry changes.
    /// </summary>
    public void UnmarkText()
    {
        if (AppKitImeTrace.Enabled)
        {
            AppKitImeTrace.Line("unmarkText");
            AppKitImeTrace.Boundary("the input method ended its composition without committing");
        }

        _composition.UnmarkText();
    }

    /// <summary>True while the input manager holds marked text for this view.</summary>
    public bool HasMarkedText
    {
        get
        {
            var marked = _composition.HasMarkedText;
            if (AppKitImeTrace.Enabled)
            {
                AppKitImeTrace.Line($"hasMarkedText -> {marked}");
            }

            return marked;
        }
    }

    /// <summary>The marked range, or <see cref="NSRange.NotFound"/> when there is none.</summary>
    public NSRange MarkedRange
    {
        get
        {
            var range = _composition.HasMarkedText ? new NSRange(0, _composition.MarkedLength) : NoRange;
            if (AppKitImeTrace.Enabled)
            {
                AppKitImeTrace.Line($"markedRange -> {range.Location},{range.Length}");
            }

            return range;
        }
    }

    /// <summary>
    /// The selection inside the marked text, which is the range the input method last reported: its
    /// location is the preedit caret, and its length is the run a conversion covers. The library
    /// holds no editable selection of its own while composing, so there is nothing else to answer
    /// with, and answering a length the input method did not set would contradict it.
    /// </summary>
    public NSRange SelectedRange
    {
        get
        {
            var range = _composition.HasMarkedText
                ? new NSRange(_composition.SelectedLocation, _composition.SelectedLength)
                : NoRange;

            if (AppKitImeTrace.Enabled)
            {
                AppKitImeTrace.Line($"selectedRange -> {range.Location},{range.Length}");
            }

            return range;
        }
    }

    /// <summary>
    /// Where the caret is, in screen points - the one answer macOS uses to place a candidate window
    /// or the character viewer.
    /// </summary>
    /// <param name="characterRange">The range the caller asks about; the answer is the caret.</param>
    /// <param name="actualRange">The range the answer describes, or <see cref="NSRange.NotFound"/>.</param>
    /// <returns>The caret rectangle in screen points, or an empty rectangle with no caret.</returns>
    public CGRect GetFirstRect(NSRange characterRange, out NSRange actualRange)
    {
        if (!_host.TryGetCaretBounds(out var caret) || Window is not { } window)
        {
            actualRange = NoRange;
            return CGRect.Empty;
        }

        // The view is flipped, so a host rectangle is already in its own coordinate system; what is
        // missing is where the view's bottom edge sits on the screen, because screen coordinates
        // count upwards. AppKit does the view-to-window and window-to-screen steps.
        var bottomInView = IsFlipped ? new CGPoint(0d, Bounds.Height) : CGPoint.Empty;
        var origin = window.ConvertPointToScreen(ConvertPointToView(bottomInView, null!));

        if (AppKitImeTrace.Enabled)
        {
            var answer = AppKitImeGeometry.ToScreenRect(caret, Bounds.Height, origin);
            AppKitImeTrace.Line($"firstRect {characterRange.Location},{characterRange.Length} -> {answer.Left},{answer.Top} {answer.Width}x{answer.Height} in screen points");
            actualRange = characterRange;
            return answer;
        }

        actualRange = characterRange;
        return AppKitImeGeometry.ToScreenRect(caret, Bounds.Height, origin);
    }

    /// <summary>
    /// The input manager declined a key and turned it into a command. The library already offered
    /// the key at the top of <see cref="KeyDown"/>, so this forwards the same key once more to
    /// whatever holds the focus now, which is what keeps a command the text box would have handled
    /// from disappearing into a beep.
    /// </summary>
    /// <param name="selector">The command, such as <c>deleteBackward:</c> or <c>moveLeft:</c>.</param>
    [Export("doCommandBySelector:")]
    public void DoCommandBySelector(Selector? selector)
    {
        var name = selector?.Name ?? string.Empty;

        if (AppKitImeTrace.Enabled)
        {
            AppKitImeTrace.Line($"doCommandBySelector '{name}' marked={_composition.HasMarkedText}");
            AppKitImeTrace.Boundary($"AppKit dispatched the command '{name}' to this view");
        }

        // The host callback can re-enter here. Without this, a command that moves the focus to a
        // control which answers by issuing another command never returns.
        if (_forwardingCommand)
        {
            ReentrancyRefusals++;
            Trace($"doCommandBySelector {name} refused: a command is already being forwarded");
            return;
        }

        var key = KeyForCommand(name, _eventBeingInterpreted);

        if (AppKitImeTrace.Enabled)
        {
            AppKitImeTrace.Line($"doCommandBySelector '{name}' maps to {key}");
        }

        if (key == Key.None)
        {
            Trace($"doCommandBySelector {name}: nothing to forward");
            return;
        }

        var modifiers = _eventBeingInterpreted is { } theEvent
            ? AppKitKeyMap.ToModifiers(theEvent.ModifierFlags)
            : Modifiers.None;

        _forwardingCommand = true;

        try
        {
            var markedBefore = _composition.HasMarkedText;
            var taken = _host.ProcessKeyDown(key, modifiers);

            if (AppKitImeTrace.Enabled)
            {
                AppKitImeTrace.Line(
                    $"doCommandBySelector '{name}' forwarded {key}: host took it={taken}, " +
                    $"marked before={markedBefore} after={_composition.HasMarkedText}");
            }

            if (!taken)
            {
                Trace($"doCommandBySelector {name}: the library did not take {key}");
            }
        }
        finally
        {
            _forwardingCommand = false;
        }
    }

    /// <summary>
    /// The text of a mark, whether the input manager handed it over as a string or as an attributed
    /// string. The library draws the preedit with the field's own font and colour, so the
    /// attributes are not used.
    /// </summary>
    private static string TextOf(NSObject? text) => text switch
    {
        null => string.Empty,
        NSAttributedString attributed => attributed.Value ?? string.Empty,
        _ => text.ToString() ?? string.Empty,
    };

    /// <summary>
    /// The key a declined command stands for. The selector name is authoritative where it is known,
    /// and the event the manager was interpreting is the fallback, which is what makes a state the
    /// input method leaves behind answerable at all.
    /// </summary>
    private static Key KeyForCommand(string selector, NSEvent? theEvent)
    {
        switch (selector)
        {
            case "insertNewline:":
            case "insertNewlineIgnoringFieldEditor:":
            case "insertParagraphSeparator:":
                return Key.Enter;

            case "insertTab:":
            case "insertBacktab:":
                return Key.Tab;

            case "deleteBackward:":
            case "deleteBackwardByDecomposingPreviousCharacter:":
                return Key.Backspace;

            case "deleteForward:":
            case "deleteWordBackward:":
            case "deleteWordForward:":
            case "deleteToBeginningOfLine:":
            case "deleteToEndOfLine:":
                return Key.Delete;

            case "moveLeft:":
            case "moveLeftAndModifySelection:":
            case "moveWordLeft:":
            case "moveWordLeftAndModifySelection:":
            case "moveToBeginningOfLine:":
            case "moveToBeginningOfLineAndModifySelection:":
                return Key.Left;

            case "moveRight:":
            case "moveRightAndModifySelection:":
            case "moveWordRight:":
            case "moveWordRightAndModifySelection:":
            case "moveToEndOfLine:":
            case "moveToEndOfLineAndModifySelection:":
                return Key.Right;

            case "moveUp:":
            case "moveUpAndModifySelection:":
            case "moveToBeginningOfParagraph:":
            case "moveToBeginningOfDocument:":
                return Key.Up;

            case "moveDown:":
            case "moveDownAndModifySelection:":
            case "moveToEndOfParagraph:":
            case "moveToEndOfDocument:":
                return Key.Down;

            case "cancelOperation:":
                return Key.Escape;

            // The manager says "I did nothing with this one", which is not a key for the library.
            case "noop:":
            case "complete:":
                return Key.None;

            default:
                return theEvent is { } keyEvent ? AppKitKeyMap.ToKey(keyEvent.KeyCode) : Key.None;
        }
    }

    /// <summary>
    /// Tells AppKit's input context that its marked text is no longer welcome, which is the macOS
    /// answer to a field that refuses a composition.
    /// </summary>
    /// <remarks>
    /// The context may not exist yet - a view with no window has none - and that is not a failure:
    /// there is then nothing on screen to take down.
    /// </remarks>
    private void DiscardMarkedText()
    {
        try
        {
            InputContext?.DiscardMarkedText();
        }
        catch (Exception ex)
        {
            Trace($"discarding marked text failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Tells the input system that the caret may have moved, so the rectangle it asks for next is
    /// not one it cached. <c>NSTextInputContext</c> documents this as the client's job whenever the
    /// document's visual coordinates change, and says that <c>handleEvent:</c> is only an implicit
    /// notification - a click or a key the input manager never sees is neither.
    /// </summary>
    /// <remarks>
    /// This is the macOS counterpart of the Win32 defect where the emoji panel's anchor was written
    /// only during a composition and was stale otherwise: here the anchor is answered on demand,
    /// but the system is still allowed to cache the answer.
    /// </remarks>
    private void InvalidateCaretCoordinates()
    {
        try
        {
            InputContext?.InvalidateCharacterCoordinates();
        }
        catch (Exception ex)
        {
            Trace($"invalidating the caret coordinates failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- NSTextInputClient: the rest of the protocol, answered at its legal minimum ----------

    /// <summary>
    /// The only text this client holds is the marked text, so the answer is the part of it the
    /// proposed range covers.
    /// </summary>
    /// <param name="proposedRange">The document range being asked about.</param>
    /// <param name="actualRange">The part of it the answer describes, or <see cref="NSRange.NotFound"/>.</param>
    /// <remarks>
    /// Answering nothing is legal, and it is what this method did while the composition was being
    /// built. A candidate list asks for the run it is converting, though, and a client that says it
    /// holds no text at all hands the input method no context to convert - so the mark is offered
    /// instead, clamped to the range asked about. The field's own value is still invisible to the
    /// input method, which is the same decision the other backends make for a preedit held outside
    /// <c>Text</c>.
    /// </remarks>
    public NSAttributedString GetAttributedSubstring(NSRange proposedRange, out NSRange actualRange)
    {
        string marked = _composition.MarkedText;

        // A proposed range is a document range: it can name text that does not exist, and it can be
        // NotFound, whose location is all ones. The intersection is taken in wide arithmetic so
        // neither case can overflow.
        ulong markedLength = (ulong)marked.Length;
        ulong from = Math.Min((ulong)proposedRange.Location, markedLength);
        ulong to = from + Math.Min((ulong)proposedRange.Length, markedLength - from);

        if (AppKitImeTrace.Enabled)
        {
            AppKitImeTrace.Line($"attributedSubstring {proposedRange.Location},{proposedRange.Length} requested; mark is {marked.Length} units");
        }

        if (to <= from)
        {
            actualRange = NoRange;
            return new NSAttributedString(string.Empty);
        }

        actualRange = new NSRange((nint)from, (nint)(to - from));
        return new NSAttributedString(marked.Substring((int)from, (int)(to - from)));
    }

    /// <summary>
    /// The attributes the input manager may put inside marked text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty is legal, and it is what this answered while the composition was being built: the library
    /// draws the preedit itself and drops whatever attributes arrive, so declaring a set looked like
    /// needless surface. It is not needless. A reference trace of a stock <c>NSTextView</c> on macOS
    /// 27.2, driven with the same keystrokes through the same Japanese input method, shows the input
    /// method asking for this list five times immediately before it offers the marked text of a
    /// conversion, and never asking a client that answers with an empty array. The list is what tells
    /// it which decorations it may put in a mark - <c>NSMarkedClauseSegment</c> is how Kotoeri marks
    /// the clause under conversion - so an empty answer is a client claiming it can render none of
    /// them. This is now the same list <c>NSTextView</c> returns, and the queries arrive here in the
    /// same pattern they arrive there (section 7.7).
    /// </para>
    /// <para>
    /// Declaring them costs nothing: <c>insertText:</c> and <c>setMarkedText:</c> take the string and
    /// ignore the attributes, exactly as before, because the field draws its own text.
    /// </para>
    /// </remarks>
    public NSString[] ValidAttributesForMarkedText
    {
        get
        {
            if (AppKitImeTrace.Enabled)
            {
                AppKitImeTrace.Line($"validAttributesForMarkedText -> {MarkedTextAttributes.Length} names");
            }

            return MarkedTextAttributes;
        }
    }

    /// <summary>The attribute names <c>NSTextView</c> declares, which is the set a client that works returns.</summary>
    private static readonly NSString[] MarkedTextAttributes =
    [
        new("NSFont"),
        new("NSUnderline"),
        new("NSColor"),
        new("NSBackgroundColor"),
        new("NSUnderlineColor"),
        new("NSMarkedClauseSegment"),
        new("NSLanguage"),
        new("NSTextInputReplacementRangeAttributeName"),
        new("NSGlyphInfo"),
        new("NSTextAlternatives"),
        new("NSTextInsertionUndoable"),
        new("NSAttachment"),
    ];

    /// <summary>No attributed string holds this view's text.</summary>
    public NSAttributedString AttributedString => null!;

    /// <summary>The window's level, so the input manager can attach a panel to it.</summary>
    public NSWindowLevel WindowLevel => Window?.Level ?? NSWindowLevel.Normal;

    /// <summary>No range is visible in a rectangle of its own; the caret answer covers this.</summary>
    public CGRect UnionRectInVisibleSelectedRange => CGRect.Empty;

    /// <summary>The whole view is the document as far as a candidate window is concerned.</summary>
    public CGRect DocumentVisibleRect => Bounds;

    /// <summary>The library draws its own caret, so there is no accessory to place.</summary>
    public NSTextCursorAccessoryPlacement PreferredTextAccessoryPlacement =>
        NSTextCursorAccessoryPlacement.Unspecified;

    /// <summary>Inline image glyphs are not drawn by the library.</summary>
    public bool SupportsAdaptiveImageGlyph => false;

    /// <summary>
    /// An inline image (a Genmoji, say) cannot be drawn by the library, so it is dropped rather
    /// than inserted as the wrong character.
    /// </summary>
    public void InsertAdaptiveImageGlyph(NSAdaptiveImageGlyph adaptiveImageGlyph, NSRange replacementRange) =>
        Trace("an inline image glyph was declined: the library draws text only");

    /// <summary>The library has no character index to answer with, so the first one is the answer.</summary>
    public nuint GetCharacterIndex(CGPoint point) => 0;

    /// <summary>Vertical layout is not something the library implements.</summary>
    public bool DrawsVertically(nuint charIndex) => false;

    /// <summary>No baseline offset applies to a caret the library draws at the text baseline.</summary>
    public NFloat GetBaselineDelta(nuint charIndex) => (NFloat)0;

    /// <summary>Glyph-distance metrics are not used by anything the library draws.</summary>
    public NFloat GetFractionOfDistanceThroughGlyph(CGPoint point) => (NFloat)0;

    /// <summary>Called when a drag enters the view, and says whether the view would take the drop.</summary>
    public override NSDragOperation DraggingEntered(INSDraggingInfo sender)
    {
        var payload = ReadDragPayload(sender);
        if (payload is null || _dragSink is null)
        {
            _dragPayload = null;
            return NSDragOperation.None;
        }

        _dragPayload = payload;
        return ToOperation(_dragSink.DragEntered(PointFrom(sender), payload.Value));
    }

    /// <summary>Called as a drag moves over the view, and says what a drop there would do.</summary>
    public override NSDragOperation DraggingUpdated(INSDraggingInfo sender)
    {
        if (_dragPayload is not { } payload || _dragSink is null)
        {
            return NSDragOperation.None;
        }

        return ToOperation(_dragSink.DragUpdated(PointFrom(sender), payload));
    }

    /// <summary>Called when the drag leaves the view without being dropped.</summary>
    public override void DraggingExited(INSDraggingInfo? sender)
    {
        if (_dragPayload is null)
        {
            return;
        }

        _dragPayload = null;
        _dragSink?.DragExited();
    }

    /// <summary>Called when the drag is released over the view; true says the drop will be taken.</summary>
    public override bool PrepareForDragOperation(INSDraggingInfo sender) => _dragPayload is not null;

    /// <summary>Called when the drag is released over the view, and routes the drop.</summary>
    public override bool PerformDragOperation(INSDraggingInfo sender)
    {
        var payload = ReadDragPayload(sender) ?? _dragPayload;
        _dragPayload = null;

        if (payload is not { } drop || _dragSink is null)
        {
            return false;
        }

        return _dragSink.Dropped(PointFrom(sender), drop) != AppKitDragEffect.None;
    }

    /// <summary>
    /// The parameterless <c>Dispose</c> on <see cref="NSObject"/> is sealed, so the teardown hook
    /// is the protected overload every NSObject subclass overrides.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;

            Theme.RemoveChangedHandler(OnThemeChanged);
            _host.RemoveInvalidatedHandler(OnHostInvalidated);
            _host.RemoveCursorChangedHandler(OnHostCursorChanged);

            if (_trackingArea is not null)
            {
                RemoveTrackingArea(_trackingArea);
                _trackingArea.Dispose();
                _trackingArea = null;
            }

            _metal?.Dispose();
            _metal = null;

            ReleaseBuffer();

            _colorSpace?.Dispose();
            _colorSpace = null;
        }

        base.Dispose(disposing);
    }

    private void MoveTo(NSEvent theEvent)
    {
        _host.ProcessMouseMove(PointFrom(theEvent));

        // AppKit re-applies the arrow cursor on every move unless the view claims otherwise, so
        // the cursor the control asked for has to be re-asserted here.
        ApplyCursor();
    }

    private void OnHostInvalidated(object sender)
    {
        if (_inDraw)
        {
            _invalidationDuringDraw = true;
            return;
        }

        NeedsDisplay = true;
    }

    /// <summary>
    /// Prints frame rate and memory use every few seconds when tracing is on. A UI that grows
    /// without bound is either drawing far more often than it should (watch the frame rate) or
    /// holding on to something for every frame (watch private memory against the frame count).
    /// The application backend calls this from the timer that polls the system appearance.
    /// </summary>
    internal void ReportStatistics()
    {
        if (!DebugTrace)
        {
            return;
        }

        var now = Environment.TickCount64;
        var since = now - _reportedAt;
        if (since < MemoryReportIntervalMs)
        {
            return;
        }

        var frames = _frames - _reportedFrames;
        _reportedAt = now;
        _reportedFrames = _frames;

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        Trace(
            $"stats: {frames * 1000d / since:F1} fps, {_frames} frames, {_buffersAllocated} buffers, " +
            $"managed {GC.GetTotalMemory(false) / Mib} MB, allocated {GC.GetTotalAllocatedBytes() / Mib} MB, " +
            $"resident {process.WorkingSet64 / Mib} MB, " +
            $"buffer {_width}x{_height} at {_scale}x");
    }

    /// <summary>Repaints whenever the palette is swapped, by the system or by the application.</summary>
    private void OnThemeChanged(object sender) => NeedsDisplay = true;

    private void OnHostCursorChanged(object sender) => ApplyCursor();

    private void ApplyCursor()
    {
        var cursor = _host.Cursor switch
        {
            Cursor.Hand => NSCursor.PointingHandCursor,
            Cursor.IBeam => NSCursor.IBeamCursor,
            Cursor.Cross => NSCursor.CrosshairCursor,
            Cursor.SizeAll => NSCursor.OpenHandCursor,
            Cursor.SizeNS => NSCursor.ResizeUpDownCursor,
            Cursor.SizeWE => NSCursor.ResizeLeftRightCursor,
            _ => NSCursor.ArrowCursor,
        };

        cursor.Set();
    }

    private SKPoint PointFrom(NSEvent theEvent)
    {
        var point = ConvertPointFromView(theEvent.LocationInWindow, null!);
        return new SKPoint((float)point.X, (float)point.Y);
    }

    private SKPoint PointFrom(INSDraggingInfo sender)
    {
        var point = ConvertPointFromView(sender.DraggingLocation, null!);
        return new SKPoint((float)point.X, (float)point.Y);
    }

    /// <summary>
    /// Reads whatever the dragged pasteboard offers that the library understands. Files come first
    /// because a file drag also carries its name as text, and the file is the more useful of the
    /// two.
    /// </summary>
    /// <remarks>
    /// Only file URLs count as files. A web URL is also an <see cref="NSUrl"/>, and its
    /// <see cref="NSUrl.Path"/> is the URL's path rather than anything on disk, so an image dragged
    /// out of a browser would otherwise arrive as a "file" called <c>/images/logo.png</c>.
    /// </remarks>
    private static AppKitDragPayload? ReadDragPayload(INSDraggingInfo sender)
    {
        try
        {
            var pasteboard = sender.DraggingPasteboard;
            var objects = pasteboard.ReadObjectsForClasses([new Class(typeof(NSUrl))], null!);
            string[] paths = objects is null
                ? []
                :
                [
                    .. objects
                        .OfType<NSUrl>()
                        .Where(url => url.IsFileUrl)
                        .Select(url => url.Path ?? string.Empty)
                        .Where(path => path.Length > 0),
                ];

            if (paths.Length > 0)
            {
                return new AppKitDragPayload(paths, null, DragEffects, DragModifiers());
            }

            return pasteboard.GetStringForType(PlainTextType) is { Length: > 0 } text
                ? new AppKitDragPayload([], text, DragEffects, DragModifiers())
                : null;
        }
        catch (Exception ex)
        {
            Trace($"reading the dragged data failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Reads the modifier keys that are down right now, which AppKit keeps on the event.</summary>
    private static Modifiers DragModifiers() =>
        NSApplication.SharedApplication.CurrentEvent is { } current
            ? AppKitKeyMap.ToModifiers(current.ModifierFlags)
            : Modifiers.None;

    /// <summary>Translates the effect the UI accepted into the operation AppKit shows for it.</summary>
    private static NSDragOperation ToOperation(AppKitDragEffect effect)
    {
        if ((effect & AppKitDragEffect.Move) != 0)
        {
            return NSDragOperation.Move;
        }

        return (effect & AppKitDragEffect.Copy) != 0 ? NSDragOperation.Copy : NSDragOperation.None;
    }

    private static Modifiers ModifiersFrom(NSEvent theEvent) => AppKitKeyMap.ToModifiers(theEvent.ModifierFlags);

    private static int ClickCountFrom(NSEvent theEvent) => Math.Max((int)theEvent.ClickCount, 1);

    internal void SyncHostSize()
    {
        var bounds = Bounds;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            _host.SetClientSize((float)bounds.Width, (float)bounds.Height);
        }
    }

    private void EnsureBuffer(int width, int height, int scale)
    {
        var now = Environment.TickCount64;
        if (width != _width || height != _height || scale != _scale)
        {
            _lastSizeChange = now;
        }

        var pixelWidth = width * scale;
        var pixelHeight = height * scale;

        // Dragging a window edge asks for a frame at every intermediate size, and a fresh
        // multi-megabyte bitmap per frame is native memory the garbage collector never sees the
        // pressure from: one drag across a Retina screen reallocated twenty times, and the
        // allocator was in no hurry to give any of it back. So the buffer is made with headroom
        // and a whole drag fits in the allocation it already has. Giving an oversized buffer back
        // waits until the window has held its size for a moment, because an in-and-out drag is
        // otherwise one reallocation per step, and every one of them leaves a freed multi-megabyte
        // block in the allocator's hands.
        if (_bitmap is not null && _scale == scale &&
            _bitmap.Width >= pixelWidth && _bitmap.Height >= pixelHeight)
        {
            var oversized = (long)_bitmap.Width * _bitmap.Height >
                            (long)pixelWidth * pixelHeight * OversizedAreaFactor;

            if (!oversized || now - _lastSizeChange < ShrinkDelayMs)
            {
                _width = width;
                _height = height;
                return;
            }
        }

        // A quarter again the size that is actually needed, so the next few sizes of the same drag
        // fit the buffer that was just made. It is measured from the needed size rather than from
        // the buffer being replaced, which is what keeps a shrinking window from growing its buffer
        // and bounds the buffer to 1.25x the window instead of letting it compound.
        var allocWidth = pixelWidth + pixelWidth / 4;
        var allocHeight = pixelHeight + pixelHeight / 4;

        ReleaseBuffer();

        if (DebugTrace && _buffersAllocated > 0)
        {
            Trace($"buffer reallocated for {width}x{height} at {scale}x " +
                  $"(#{_buffersAllocated + 1}, {allocWidth}x{allocHeight} pixels)");
        }

        try
        {
            // Premultiplied BGRA, which is what CoreGraphics wants on a little endian machine with
            // ByteOrder32Little, so the same buffer can be handed to both Skia and CoreGraphics.
            var info = new SKImageInfo(allocWidth, allocHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            _bitmap = new SKBitmap(info);

            var pixels = _bitmap.GetPixels();
            if (pixels == IntPtr.Zero)
            {
                throw new InvalidOperationException($"SkiaSharp allocated no pixels for {allocWidth}x{allocHeight}.");
            }

            _skia = SKSurface.Create(info, pixels, _bitmap.RowBytes)
                ?? throw new InvalidOperationException($"SkiaSharp could not wrap {allocWidth}x{allocHeight} pixels in a surface.");

            // The (memory, size, bool) overload reads that bool as "owns the buffer" and, when it is
            // false, copies the whole block - which would memcpy every pixel of every frame, about
            // 16 MB on a Retina window. The callback overload wraps the pixels where they are; the
            // callback does nothing because SkiaSharp owns them and outlives the provider.
            _provider = new CGDataProvider(pixels, _bitmap.ByteCount, static _ => { });

            _colorSpace ??= CGColorSpace.CreateDeviceRGB()
                ?? throw new InvalidOperationException("CoreGraphics has no device RGB colour space.");

            _width = width;
            _height = height;
            _scale = scale;
            _bufferError = null;
            _buffersAllocated++;

            // The new pixels hold whatever was in that memory, so the frame they start with has
            // to cover all of them.
            _bufferGeneration++;
        }
        catch (Exception ex)
        {
            // Failing here silently is a window that never draws anything, which is the hardest
            // kind of bug to report, so the reason is kept for DrawRect to put on screen.
            _bufferError = $"{ex.GetType().Name}: {ex.Message}";
            Console.Error.WriteLine($"[kolvir] {pixelWidth}x{pixelHeight} drawing buffer failed: {_bufferError}");
            ReleaseBuffer();
        }
    }

    private void ReleaseBuffer()
    {
        // The layer shows an image over these pixels, so it has to let go of that image before the
        // pixels go: a layer holding an image whose provider has been freed draws garbage.
        if (Layer is { } layer)
        {
            layer.Contents = null;
        }

        _presented?.Dispose();
        _presented = null;

        // The surface writes into the bitmap's memory, so it has to go first.
        _skia?.Dispose();
        _skia = null;

        // The provider points at the bitmap's pixels, so it goes before the bitmap does.
        _provider?.Dispose();
        _provider = null;

        _bitmap?.Dispose();
        _bitmap = null;

        // The colour space is not tied to the pixels, so it outlives the buffer and is disposed
        // with the view instead of being rebuilt on every reallocation.
        _width = 0;
        _height = 0;
        _scale = 0;
    }

    /// <summary>
    /// Paints the reason the UI cannot be drawn over the whole view. A window that draws nothing
    /// says nothing, so every failure ends up here rather than on a console nobody is watching.
    /// <para>
    /// The message is drawn with Skia into the same bitmap every other frame uses, because by the
    /// time a failure is being reported there may be no usable buffer, and text drawn with AppKit
    /// would need the drawing context this view no longer takes part in.
    /// </para>
    /// </summary>
    private void DrawDiagnostic(string message)
    {
        if (_diagnostic != message)
        {
            _diagnostic = message;
            Console.Error.WriteLine($"[kolvir] {message}");
        }

        if (Layer is not { } layer)
        {
            return;
        }

        if (_skia is null || _bitmap is null)
        {
            // Nothing to paint into, so the window is at least made a colour that says something
            // went wrong rather than left showing whatever was there before.
            layer.Contents = null;
            layer.BackgroundColor = new CGColor(0.6f, 0f, 0f);
            return;
        }

        try
        {
            var canvas = _skia.Canvas;
            canvas.Save();
            canvas.ResetMatrix();
            canvas.Clear(new SKColor(0x99, 0x00, 0x00));
            canvas.Restore();

            using var font = new Font(13f);
            TextRenderer.Draw(
                canvas,
                message,
                font,
                SKColors.White,
                new SKRect(16, 16, _width - 16, _height - 16),
                ContentAlignment.TopLeft,
                true);

            canvas.Flush();
            Present();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[kolvir] drawing the diagnostic failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Trace(string message)
    {
        if (DebugTrace)
        {
            Console.Error.WriteLine($"[kolvir] {message}");
        }
    }
}
