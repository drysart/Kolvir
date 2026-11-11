using SkiaSharp;

namespace XPlatUI.Backend.Gtk4;

/// <summary>
/// The GTK4 host widget for the XPlat UI library. It owns an <see cref="XPlatUiHost"/>, presents
/// the control tree with SkiaSharp either through a <see cref="Gtk.GLArea"/> or through a
/// premultiplied BGRA bitmap shown by a <see cref="Gtk.Picture"/>, and forwards mouse and keyboard
/// input from GTK's event controllers. This is the counterpart of the Win32 host and of the AppKit
/// view: same duties, different native API.
/// </summary>
/// <remarks>
/// <para>
/// The accelerated path is the one described in README.md, "Hardware acceleration (Linux)": a
/// <see cref="Gtk.GLArea"/> whose context GTK already has current when it emits the render signal,
/// Skia's GLES interface assembled over <c>eglGetProcAddress</c>, a retained offscreen surface that
/// the control tree paints its damage into, and one Skia blit onto the area's own framebuffer. The
/// CPU texture below is what a window falls back to, and what a window that did not ask for
/// acceleration uses from the start.
/// </para>
/// The frame is handed to GTK as a <see cref="Gdk.MemoryTexture"/> rather than painted into the
/// Cairo context of a <see cref="Gtk.DrawingArea"/>. GTK4 gives a drawing area a Cairo recording
/// surface, so compositing a bitmap into it means using the bitmap as a Cairo source, and Cairo
/// answers a mutable source with a full size snapshot. GTK keeps the render nodes those snapshots
/// end up in, so every repaint leaked one frame buffer. A texture copies its pixels once, on
/// construction, and owns the copy - which is also what lets the Skia surface be resized without
/// leaving the renderer pointing at freed memory.
/// </para>
/// <para>
/// The picture and a transparent <see cref="Gtk.DrawingArea"/> share one cell of a
/// <see cref="Gtk.Grid"/>. The drawing area is what reports the widget's size and scale factor and
/// what receives input, and its draw function is also what paints the frame; the picture is added
/// first so that the area stays on top of it for input. A grid snapshots its children in the order
/// they were added, so the picture is snapshotted before the area paints, and setting a paintable
/// queues a resize as well - either one alone leaves the picture holding the frame from before
/// when GTK reaches it in that pass. The view therefore asks for one more pass once the frame is
/// painted; see <see cref="SchedulePicturePass"/>. A window paints often enough for that to go
/// unnoticed, but a popover paints only when something asks it to, so without the extra pass a
/// popup kept showing its first, empty frame until the pointer moved over it.
/// </para>
/// <para>
/// The view does not run a timer. Time based behaviour is the business of whichever control needs
/// it: a control that animates owns an <see cref="XPlatTimer"/> and only runs it while it matters.
/// </para>
/// </remarks>
public sealed class Gtk4SkiaView : IDisposable
{
    private Action<object>? _metricsChanged;
    /// <summary>
    /// A control that invalidates while it is being painted would otherwise keep the drawing area
    /// permanently dirty, so repaints requested during a paint are deferred and capped.
    /// </summary>
    private const int MaxDeferredRepaints = 8;

    private const long MemoryReportIntervalMs = 5000;

    /// <summary>Set <c>XPLATUI_DEBUG=1</c> to trace host work to stderr, which helps on odd setups.</summary>
    private static readonly bool DebugTrace =
        Environment.GetEnvironmentVariable("XPLATUI_DEBUG") is "1" or "true";

    internal string DebugTag { get; set; } = "window";

    private readonly Gtk.Grid _root;
    private readonly Gtk.DrawingArea _area;
    private readonly Gtk.Picture _picture;
    private readonly Gtk.GLArea? _glArea;
    private readonly GLib.SourceFunc _redrawIdle;
    private readonly GLib.SourceFunc _pictureIdle;
    private readonly GLib.SourceFunc _notifyMetrics;
    private readonly GLib.SourceFunc _presentationSwap;

    /// <summary>
    /// Whether this view was created for a window that asked for acceleration. The option is a
    /// window-creation decision, so it is read once: a window never switches back on after is has
    /// fallen back, exactly as on Windows and macOS.
    /// </summary>
    private readonly bool _useHardwareAcceleration;

    private Gtk4GlSurface? _gl;
    private bool _glUnavailable;
    private bool _presentationSwapPending;
    private bool _gfxDecided;
    private Action<object>? _graphicsPathChanged;
    private string _gfxReason;

    private SKSurface? _frame;
    private IntPtr _framePixels;
    private int _frameWidth;
    private int _frameHeight;
    private int _frameStride;

    private int _width;
    private int _height;
    private int _scale;

    private int _metricsWidth;
    private int _metricsHeight;
    private int _metricsScale;

    private bool _inDraw;
    private bool _redrawPending;
    private bool _picturePassPending;
    private bool _metricsPending;
    private int _deferredRepaints;
    private bool _disposed;

    /// <summary>
    /// The last pointer position GTK reported. The scroll signal carries a delta but no position,
    /// so the wheel has to be routed to wherever the pointer was last seen.
    /// </summary>
    private SKPoint _pointer;

    /// <summary>
    /// The press currently in flight, which is the only moment a window drag can be started.
    /// </summary>
    /// <remarks>
    /// GDK4 exposes no event serial, so <c>begin_move</c> works from GDK's own implicit grab and does
    /// nothing at all outside a real press. Caching the press lets the backend start a drag from the
    /// application's own mouse-down, where the grab is live, without the application knowing anything
    /// about devices or timestamps.
    /// </remarks>
    internal PressRecord? LastPress { get; private set; }

    private long _frames;
    private long _reportedAt;
    private long _reportedFrames;

    public Gtk4SkiaView(XPlatUiHost host)
        : this(host, false)
    {
    }

    /// <summary>
    /// Creates the host widget, choosing at creation whether the window may present through a
    /// <see cref="Gtk.GLArea"/> instead of the CPU texture.
    /// </summary>
    /// <param name="host">The control tree to paint.</param>
    /// <param name="hardwareAcceleration">
    /// True to try the GL seam. The attempt is made on the first render pass and any failure leaves
    /// the view on the CPU path, so this only decides whether it is tried at all.
    /// </param>
    internal Gtk4SkiaView(XPlatUiHost host, bool hardwareAcceleration)
    {
        Host = host;
        _useHardwareAcceleration = hardwareAcceleration;
        _gfxReason = hardwareAcceleration ? "cpu (not created)" : "cpu (disabled)";

        if (!hardwareAcceleration)
        {
            _glUnavailable = true;
            _gfxDecided = true;
        }

        // The delegates must stay rooted for as long as GLib may call them.
        _redrawIdle = OnRedrawIdle;
        _pictureIdle = OnPictureIdle;
        _notifyMetrics = OnNotifyMetrics;
        _presentationSwap = OnPresentationSwap;

        _area = Gtk.DrawingArea.New();
        _area.SetHexpand(true);
        _area.SetVexpand(true);
        _area.SetFocusable(true);
        _area.SetCanFocus(true);
        _area.SetDrawFunc(OnDraw);

        _picture = Gtk.Picture.New();
        _picture.SetHexpand(true);
        _picture.SetVexpand(true);
        _picture.SetHalign(Gtk.Align.Fill);
        _picture.SetValign(Gtk.Align.Fill);
        _picture.SetContentFit(Gtk.ContentFit.Fill);
        _picture.SetCanShrink(true);
        _picture.SetFocusable(false);
        _picture.SetCanFocus(false);

        _root = Gtk.Grid.New();
        _root.SetHexpand(true);
        _root.SetVexpand(true);

        // The GL area goes in first, so every other child is drawn over it: the picture keeps
        // showing the CPU frame while the GL seam proves itself, and the drawing area stays on top
        // for input exactly as it did before. GTK still snapshots - and so still renders - an area
        // that an opaque sibling covers, which is what lets the seam come up underneath the frame
        // the user is already looking at instead of showing an empty window while it tries.
        if (!_glUnavailable)
        {
            _glArea = Gtk.GLArea.New();
            _glArea.SetHexpand(true);
            _glArea.SetVexpand(true);
            _glArea.SetHasDepthBuffer(false);
            _glArea.SetHasStencilBuffer(true);
            _glArea.OnRender += OnGlRender;
            _root.Attach(_glArea, 0, 0, 1, 1);
        }

        // The picture goes in before the drawing area so that the area, which receives input, stays
        // on top of it.
        _root.Attach(_picture, 0, 0, 1, 1);
        _root.Attach(_area, 0, 0, 1, 1);

        Host.AddInvalidatedHandler(OnHostInvalidated);
        Host.AddCursorChangedHandler(OnHostCursorChanged);
        XPlatTheme.AddChangedHandler(OnThemeChanged);

        AttachInput();
    }

    /// <summary>
    /// What this view is presenting with, for a one-line diagnostic: <c>gl</c> when the GL seam is
    /// live, or <c>cpu (...)</c> naming the step that put it there.
    /// </summary>
    internal string GraphicsPath => _gl is not null ? "gl" : _gfxReason;

    /// <summary>The GL vendor and renderer when <see cref="GraphicsPath"/> is <c>gl</c>, else null.</summary>
    internal string? GraphicsRenderer => _gl?.Renderer;

    /// <summary>
    /// Adds a handler that runs when <see cref="GraphicsPath"/> stops saying "not decided". The GL
    /// seam is chosen on the first render pass rather than when the window is created, so this is
    /// how a caller learns the outcome; a view that already knows it - because acceleration was
    /// never asked for - runs the handler immediately.
    /// </summary>
    internal IDisposable AddGraphicsPathChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (_gfxDecided)
        {
            handler(this);
        }
        else
        {
            _graphicsPathChanged += handler;
        }

        return new XPlatHandlerToken<Action<object>>(RemoveGraphicsPathChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddGraphicsPathChangedHandler"/> added.</summary>
    internal void RemoveGraphicsPathChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _graphicsPathChanged -= handler;
    }

    private void MarkGraphicsPathDecided()
    {
        if (_gfxDecided)
        {
            return;
        }

        _gfxDecided = true;
        _graphicsPathChanged?.Invoke(this);
    }

    /// <summary>Adds a handler that runs after a paint when the widget's size or scale factor changed.</summary>
    public IDisposable AddMetricsChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _metricsChanged += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveMetricsChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddMetricsChangedHandler"/> added.</summary>
    public void RemoveMetricsChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _metricsChanged -= handler;
    }

    /// <summary>The GTK widget that shows the UI tree.</summary>
    public Gtk.Widget Widget => _root;

    /// <summary>The platform independent UI host.</summary>
    public XPlatUiHost Host { get; }

    /// <summary>The drawing area input arrives on, which is where a controller has to be added.</summary>
    public Gtk.DrawingArea InputArea => _area;

    /// <summary>The widget's width in logical pixels.</summary>
    public int Width => _metricsWidth > 0 ? _metricsWidth : _area.GetWidth();

    /// <summary>The widget's height in logical pixels.</summary>
    public int Height => _metricsHeight > 0 ? _metricsHeight : _area.GetHeight();

    /// <summary>The integer device pixel ratio GTK reports for the widget's monitor.</summary>
    public int ScaleFactor
    {
        get
        {
            try
            {
                return Math.Max(_area.ScaleFactor, 1);
            }
            catch (Exception)
            {
                return 1;
            }
        }
    }

    /// <summary>Gives the drawing area keyboard focus, which is how key events reach the host.</summary>
    public void Focus() => _area.GrabFocus();

    /// <summary>
    /// Paints the tree now, at a size the widget is about to be allocated, and hands the frame to
    /// the picture before returning.
    /// </summary>
    /// <param name="width">Width in logical pixels.</param>
    /// <param name="height">Height in logical pixels.</param>
    /// <returns>True when a frame was painted.</returns>
    /// <remarks>
    /// A popup changes size while it is up, and GTK applies that geometry in its next layout pass
    /// while a frame produced by the draw signal only reaches the picture one idle later. The pass
    /// therefore runs with the picture still holding the previous frame, and a picture that fills
    /// its allocation stretches that frame across the new size - which reads as the popup jumping
    /// and stretching when a submenu opens. Painting before the pass means the pass already has the
    /// frame for the new size.
    /// </remarks>
    public bool PaintNow(int width, int height)
    {
        if (_disposed || _inDraw || width <= 0 || height <= 0)
        {
            return false;
        }

        var scale = ScaleFactor;

        _width = width;
        _height = height;
        _scale = scale;

        Host.SetClientSize(width, height);
        PaintFrame(width, height, scale);

        return true;
    }

    /// <summary>Called periodically by the application backend; only reports under XPLATUI_DEBUG.</summary>
    public void ReportStatistics()
    {
        if (!DebugTrace)
        {
            return;
        }

        var now = Environment.TickCount64;
        _frames++;

        if (_reportedAt == 0)
        {
            _reportedAt = now;
            _reportedFrames = _frames;
            return;
        }

        if (now - _reportedAt < MemoryReportIntervalMs)
        {
            return;
        }

        var seconds = (now - _reportedAt) / 1000.0;
        var frames = _frames - _reportedFrames;
        var kib = (long)_width * _height * _scale * _scale * 4 / 1024;

        Console.Error.WriteLine(
            $"[xplat] gtk4 view {_width}x{_height}@{_scale} {frames / seconds:F1} fps, buffer {kib} KiB");

        _reportedAt = now;
        _reportedFrames = _frames;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        XPlatTheme.RemoveChangedHandler(OnThemeChanged);
        Host.RemoveInvalidatedHandler(OnHostInvalidated);
        Host.RemoveCursorChangedHandler(OnHostCursorChanged);

        _picture.SetPaintable(null);
        _frame?.Dispose();
        _frame = null;
        _framePixels = IntPtr.Zero;

        _gl?.Dispose();
        _gl = null;
        _glUnavailable = true;
    }

    private void OnDraw(Gtk.DrawingArea area, Cairo.Context cr, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        _inDraw = true;

        try
        {
            Host.SetClientSize(width, height);

            var scale = Math.Max(area.ScaleFactor, 1);

            _width = width;
            _height = height;
            _scale = scale;

            // A GL area that never renders never reports its own failure through the render signal,
            // so this is where a machine whose compositor refused a context is noticed: the
            // drawing area is snapshotted in the same pass either way.
            if (_gl is null && !_glUnavailable && _glArea is { } candidate && candidate.GetError() is { } error)
            {
                DropToCpu($"cpu (gl area: {error.Message ?? "unknown"})");
            }

            if (_gl is null)
            {
                Trace($"{DebugTag} draw {width}x{height} at {scale}x");
                PaintFrame(width, height, scale);
            }

            ReportMetrics(width, height, scale);
        }
        finally
        {
            _inDraw = false;
            _deferredRepaints = 0;
        }
    }

    /// <summary>
    /// The GL seam's frame: GTK calls this with the area's own framebuffer bound, so this is where
    /// every GL operation for a window happens - creation, paint, blit and flush.
    /// </summary>
    /// <returns>True, so GTK treats the framebuffer as fully painted by the handler.</returns>
    private bool OnGlRender(Gtk.GLArea sender, Gtk.GLArea.RenderSignalArgs args)
    {
        if (_disposed)
        {
            return true;
        }

        // GTK does not re-enter a render, and doing so would paint the same frame twice.
        if (_inDraw)
        {
            return true;
        }

        _inDraw = true;

        try
        {
            var width = _area.GetWidth();
            var height = _area.GetHeight();

            if (width <= 0 || height <= 0)
            {
                return true;
            }

            var scale = Math.Max(_area.ScaleFactor, 1);

            sender.MakeCurrent();

            // Before Skia creates or binds anything: creating a texture surface leaves one of
            // Skia's framebuffers bound, and the target has to name GTK's.
            _gl?.CaptureFrameState();

            if (_gl is null)
            {
                if (_glUnavailable)
                {
                    return true;
                }

                TryCreateGl(sender, width, height, scale);

                if (_gl is null)
                {
                    return true;
                }
            }

            Host.SetClientSize(width, height);

            _width = width;
            _height = height;
            _scale = scale;

            DrawGl(width, height, scale);
            ReportMetrics(width, height, scale);
        }
        catch (Exception exception)
        {
            // A paint that throws must not escape the signal; the next frame is the CPU path.
            DropToCpu($"cpu (gl draw threw {exception.GetType().Name})");
        }
        finally
        {
            _inDraw = false;
            _deferredRepaints = 0;
        }

        return true;
    }

    /// <summary>
    /// Builds the GL stack on the first render pass, when the area finally has a context. A failure
    /// is remembered for the life of the window, exactly as the Win32 probe is, so a window that
    /// fell back does not retry a losing attempt on every frame.
    /// </summary>
    private void TryCreateGl(Gtk.GLArea area, int width, int height, int scale)
    {
        var gl = Gtk4GlSurface.TryCreate(area, width * scale, height * scale, out var reason);

        _gfxReason = reason;

        if (gl is null)
        {
            DropToCpu(reason);
            return;
        }

        _gl = gl;
        _glUnavailable = false;
        Gtk4GlTrace.Write($"gfx: {reason} ({gl.Renderer})");

        // The picture is still the child on top, so the swap that reveals the GL area is the next
        // thing that has to happen - after this pass, because the grid is being snapshotted now.
        SchedulePresentationSwap();
        MarkGraphicsPathDecided();
    }

    /// <summary>
    /// Presents one frame through GL. The retained tier paints only the host's damage into the
    /// persistent offscreen surface and blits the whole buffer; the direct tier paints the area's
    /// framebuffer in full, because GTK does not promise it still holds the previous frame.
    /// </summary>
    private void DrawGl(int width, int height, int scale)
    {
        var gl = _gl!;

        if (!gl.EnsureSize(width * scale, height * scale))
        {
            DropToCpu("cpu (gl surface invalid)");
            return;
        }

        // The retained buffer keeps the pixels between frames, so the host may paint the damaged
        // region alone. The CPU path rasterizes every frame into a reused buffer and clears what it
        // writes, so it must keep asking for complete frames.
        Host.PartialUpdates = gl.HasRetainedBuffer;

        if (gl.HasRetainedBuffer && gl.Surface is { } retained)
        {
            var damaged = Host.IsUpdatePending || gl.NeedsFullRepaint;

            if (Gtk4GlTrace.IsEnabled)
            {
                Gtk4GlTrace.Write(
                    $"frame {width}x{height}@{scale} retained damaged={damaged} full={gl.NeedsFullRepaint}");
            }

            if (damaged)
            {
                // The offscreen contents are undefined when it is created, and a resize or scale
                // change makes them so again; TakeNeedsFull is what forces that first frame full.
                Paint(retained, gl.TakeNeedsFull(), width, height, scale);
            }

            var result = gl.PresentRetained();

            if (result == Gtk4GlPresentResult.RetryDirect)
            {
                // The retained buffer failed mid-frame. Repaint this one frame against the area's
                // framebuffer and present it; the next frame stays on the direct tier.
                var retry = gl.PresentDirect(surface => Paint(surface, full: true, width, height, scale));

                if (retry == Gtk4GlPresentResult.Lost)
                {
                    DropToCpu("cpu (gl context lost)");
                }

                return;
            }

            if (result == Gtk4GlPresentResult.Lost)
            {
                DropToCpu("cpu (gl context lost)");
            }

            return;
        }

        // The direct tier has no retained contents, so every frame it serves is a full frame.
        var direct = gl.PresentDirect(surface => Paint(surface, full: true, width, height, scale));

        if (direct == Gtk4GlPresentResult.Lost)
        {
            DropToCpu("cpu (gl context lost)");
        }
    }

    /// <summary>Paints the control tree into a GL-backed surface at the widget's device resolution.</summary>
    private void Paint(SKSurface surface, bool full, int width, int height, int scale)
    {
        var canvas = surface.Canvas;
        canvas.Save();
        canvas.Scale(scale);
        canvas.ClipRect(SKRect.Create(0, 0, width, height));
        Host.Paint(canvas, full);
        canvas.Restore();
        canvas.Flush();
    }

    /// <summary>
    /// Abandons the GL seam and returns the view to the CPU texture for the rest of its life. The
    /// CPU buffer may never have been allocated, so the next frame builds it and paints in full.
    /// </summary>
    private void DropToCpu(string reason)
    {
        _gfxReason = reason;
        _glUnavailable = true;

        var gl = _gl;
        _gl = null;
        gl?.Dispose();

        Host.PartialUpdates = false;

        SchedulePresentationSwap();

        Trace($"gfx: {reason}");
        MarkGraphicsPathDecided();
    }

    /// <summary>
    /// Swaps which child is on top. The picture always exists, so the two tiers are a visibility
    /// flip: CPU while the GL seam is unproven or has failed, GL once it has painted.
    /// </summary>
    /// <remarks>
    /// It has to happen outside the pass that is running, because GTK is walking the grid's
    /// children while the decision is made, and changing a child's visibility under it is not
    /// safe. The swap also asks for the frame that shows its result, which is what makes the
    /// picture's first CPU frame reach the screen after a fallback.
    /// </remarks>
    private void SchedulePresentationSwap()
    {
        if (_presentationSwapPending || _disposed)
        {
            return;
        }

        _presentationSwapPending = true;
        GLib.Functions.IdleAdd(0, _presentationSwap);
    }

    private bool OnPresentationSwap()
    {
        _presentationSwapPending = false;

        if (_disposed)
        {
            return false;
        }

        var glLive = _gl is not null;

        _picture.SetVisible(!glLive);
        _glArea?.SetVisible(glLive || !_glUnavailable);

        if (glLive)
        {
            _glArea?.QueueRender();
        }
        else
        {
            _area.QueueDraw();
        }

        return false;
    }

    /// <summary>
    /// Rasterizes the control tree at the widget's device resolution and hands the result to GTK.
    /// </summary>
    private unsafe void PaintFrame(int width, int height, int scale)
    {
        var pixelWidth = width * scale;
        var pixelHeight = height * scale;

        if (!EnsureFrame(pixelWidth, pixelHeight))
        {
            return;
        }

        var canvas = _frame!.Canvas;
        canvas.Save();
        canvas.Scale(scale);
        canvas.ClipRect(SKRect.Create(0, 0, width, height));
        Host.Paint(canvas);
        canvas.Restore();
        canvas.Flush();

        // The bytes must own the pixels. NewStatic only aliases them, which makes the texture read
        // memory that EnsureFrame frees on the next size change, and GTK's renderer still holds the
        // previous frame's node while it renders the new one - a resize then faults inside the GL
        // upload. New copies into GLib's own memory, so a texture outlives the surface it came from.
        var pixels = new Span<byte>((void*)_framePixels, _frameStride * pixelHeight);
        var bytes = GLib.Bytes.New(pixels);
        var texture = Gdk.MemoryTexture.New(
            pixelWidth,
            pixelHeight,
            Gdk.MemoryFormat.B8g8r8a8Premultiplied,
            bytes,
            (nuint)_frameStride);

        _picture.SetPaintable(texture);

        SchedulePicturePass();

        texture.Dispose();
        bytes.Dispose();
    }

    /// <summary>
    /// Asks for the render pass that puts the frame just painted on screen.
    /// </summary>
    /// <remarks>
    /// GTK renders a widget from a render node it caches, and rebuilds that node only for a widget
    /// that has been asked to draw. The picture cannot draw this frame in the pass that produced it:
    /// setting a paintable queues a resize, so when GTK reaches the picture later in the same pass
    /// the picture still needs an allocation and GTK keeps the node it had before - which, the
    /// first time, is no node at all. A window paints often enough for the next pass to fix that
    /// unnoticed, but a popover paints only when something asks it to, so a popup stayed blank
    /// until the user happened to move the pointer over it.
    /// <para>
    /// The request has to be made from outside the pass, and it has to name a widget that is not
    /// already waiting to be drawn: queuing a draw on a widget that already is stops GTK from
    /// walking up to the surfaces above it, so no pass would be scheduled at all.
    /// </para>
    /// </remarks>
    private void SchedulePicturePass()
    {
        if (_picturePassPending || _disposed)
        {
            return;
        }

        _picturePassPending = true;
        GLib.Functions.IdleAdd(0, _pictureIdle);
    }

    private bool OnPictureIdle()
    {
        _picturePassPending = false;

        if (_disposed)
        {
            return false;
        }

        // The picture redraws itself, and the grid redraws so that it snapshots the picture again:
        // the node it cached holds the picture's previous node, not the one about to be built.
        _picture.QueueDraw();
        _root.QueueDraw();

        return false;
    }

    /// <summary>Allocates or reallocates the Skia surface that backs the picture, if needed.</summary>
    private bool EnsureFrame(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return false;
        }

        if (_frame is not null && _frameWidth == pixelWidth && _frameHeight == pixelHeight)
        {
            return true;
        }

        _frame?.Dispose();

        var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        _frame = SKSurface.Create(info);

        if (_frame is null)
        {
            Trace($"could not allocate a {pixelWidth}x{pixelHeight} frame");
            _framePixels = IntPtr.Zero;
            _frameWidth = 0;
            _frameHeight = 0;
            return false;
        }

        using var pixmap = _frame.PeekPixels();
        _framePixels = pixmap.GetPixels();
        _frameStride = pixmap.RowBytes;
        _frameWidth = pixelWidth;
        _frameHeight = pixelHeight;

        _frame.Canvas.Clear(SKColors.Transparent);

        return _framePixels != IntPtr.Zero;
    }

    private void ReportMetrics(int width, int height, int scale)
    {
        if (width == _metricsWidth && height == _metricsHeight && scale == _metricsScale)
        {
            return;
        }

        _metricsWidth = width;
        _metricsHeight = height;
        _metricsScale = scale;

        if (_metricsPending)
        {
            return;
        }

        // Raised from an idle rather than from inside the paint: the listeners resize the control
        // tree and raise model events, and neither is safe while Skia is mid-frame.
        _metricsPending = true;
        GLib.Functions.IdleAdd(0, _notifyMetrics);
    }

    private bool OnNotifyMetrics()
    {
        _metricsPending = false;

        if (!_disposed)
        {
            _metricsChanged?.Invoke(this);
        }

        return false;
    }

    private void AttachInput()
    {
        // Button 0 listens for every button, so one gesture covers left, right and middle.
        var click = Gtk.GestureClick.New();
        click.SetButton(0);
        click.OnPressed += OnPressed;
        click.OnReleased += OnReleased;
        _area.AddController(click);

        var motion = Gtk.EventControllerMotion.New();
        motion.OnMotion += OnMotion;
        motion.OnLeave += OnLeave;
        _area.AddController(motion);

        // Both axes, so a tilt wheel and a horizontal touchpad swipe arrive here too.
        var scroll = Gtk.EventControllerScroll.New(Gtk.EventControllerScrollFlags.BothAxes);
        scroll.OnScroll += OnScroll;
        _area.AddController(scroll);

        var key = Gtk.EventControllerKey.New();
        key.OnKeyPressed += OnKeyPressed;
        key.OnKeyReleased += OnKeyReleased;
        _area.AddController(key);
    }

    private void OnPressed(Gtk.GestureClick gesture, Gtk.GestureClick.PressedSignalArgs args)
    {
        var button = ToMouseButton(gesture.GetCurrentButton());
        if (button == XPlatMouseButtons.None)
        {
            return;
        }

        // Clicking anywhere inside the view takes keyboard focus, exactly like a Win32 window.
        _area.GrabFocus();
        _pointer = new SKPoint((float)args.X, (float)args.Y);

        // The press is what a window drag is started from, so everything begin_move needs is
        // captured here, while GDK's implicit grab is still the one in force.
        LastPress = new PressRecord(
            gesture.GetCurrentEventDevice(),
            gesture.GetCurrentButton(),
            args.X,
            args.Y,
            gesture.GetCurrentEventTime());

        Host.ProcessMouseDown(
            _pointer,
            button,
            Gtk4KeyMap.MapModifiers(gesture.GetCurrentEventState()),
            Math.Max(args.NPress, 1));
    }

    private void OnReleased(Gtk.GestureClick gesture, Gtk.GestureClick.ReleasedSignalArgs args)
    {
        // The grab this release ends is the one a drag would have used, so it stops being usable
        // here rather than at the next unrelated event.
        LastPress = null;

        var button = ToMouseButton(gesture.GetCurrentButton());
        if (button == XPlatMouseButtons.None)
        {
            return;
        }

        var point = new SKPoint((float)args.X, (float)args.Y);
        Host.ProcessMouseUp(point, button, Gtk4KeyMap.MapModifiers(gesture.GetCurrentEventState()));

        // GTK has no context menu message of its own for a drawing area, so the release drives it,
        // which is also when Win32 asks for one.
        if (button == XPlatMouseButtons.Right)
        {
            Host.ProcessContextMenu(point);
        }
    }

    private void OnMotion(Gtk.EventControllerMotion sender, Gtk.EventControllerMotion.MotionSignalArgs args)
    {
        _pointer = new SKPoint((float)args.X, (float)args.Y);
        Host.ProcessMouseMove(_pointer);
    }

    private void OnLeave(Gtk.EventControllerMotion sender, EventArgs args) => Host.ProcessMouseLeave();

    /// <summary>
    /// Forwards a wheel turn or a touchpad swipe; returns true when the UI consumed it.
    /// </summary>
    /// <remarks>
    /// GTK reports a wheel in whole detents and a touchpad in pixels, and says which through the
    /// controller's unit. Its positive deltas point down and right, which is the opposite of the
    /// host's convention, so both are negated on the way through.
    /// </remarks>
    private bool OnScroll(Gtk.EventControllerScroll sender, Gtk.EventControllerScroll.ScrollSignalArgs args)
    {
        var scale = sender.GetUnit() == Gdk.ScrollUnit.Wheel ? XPlatUiHost.WheelPixelsPerDetent : 1f;

        var delta = (float)-args.Dy * scale;
        if (MathF.Abs(delta) < 0.01f)
        {
            delta = (float)-args.Dx * scale;
        }

        return Host.ProcessMouseWheel(_pointer, delta, Gtk4KeyMap.MapModifiers(sender.GetCurrentEventState()));
    }

    /// <summary>Forwards a key press; returns true when the UI consumed it.</summary>
    private bool OnKeyPressed(Gtk.EventControllerKey sender, Gtk.EventControllerKey.KeyPressedSignalArgs args)
    {
        var modifiers = ModifiersOf(args.Keyval, args.State);
        var key = Gtk4KeyMap.Map(args.Keyval);

        if (key != XPlatKey.None && Host.ProcessKeyDown(key, modifiers))
        {
            return true;
        }

        var text = Gtk4KeyMap.MapText(args.Keyval, args.State);
        if (text is null)
        {
            return false;
        }

        Host.ProcessTextInput(text);
        return true;
    }

    private void OnKeyReleased(Gtk.EventControllerKey sender, Gtk.EventControllerKey.KeyReleasedSignalArgs args)
    {
        var key = Gtk4KeyMap.Map(args.Keyval);
        if (key != XPlatKey.None)
        {
            Host.ProcessKeyUp(key, ModifiersOf(args.Keyval, args.State));
        }
    }

    /// <summary>
    /// Reads the modifiers of a key event. GTK reports shift+Tab as <c>ISO_Left_Tab</c>, a key
    /// value of its own, so the shift bit is not the only place the modifier comes from.
    /// </summary>
    private static XPlatModifiers ModifiersOf(uint keyval, Gdk.ModifierType state)
    {
        var modifiers = Gtk4KeyMap.MapModifiers(state);
        return Gtk4KeyMap.ImpliesShift(keyval) ? modifiers | XPlatModifiers.Shift : modifiers;
    }

    private void OnHostInvalidated(object sender)
    {
        if (_disposed)
        {
            return;
        }

        if (_inDraw)
        {
            if (_deferredRepaints >= MaxDeferredRepaints || _redrawPending)
            {
                return;
            }

            _deferredRepaints++;
            _redrawPending = true;
            GLib.Functions.IdleAdd(0, _redrawIdle);
            return;
        }

        QueueRepaint();
    }

    private bool OnRedrawIdle()
    {
        _redrawPending = false;

        if (!_disposed)
        {
            QueueRepaint();
        }

        return false;
    }

    /// <summary>
    /// Asks for the frame the tier needs. A GL area renders only when something queues a render on
    /// it, and queueing one from inside the render signal is folded into the pass that is already
    /// running - which is why the deferred path runs on an idle.
    /// </summary>
    private void QueueRepaint()
    {
        if (_gl is not null && _glArea is { } glArea)
        {
            glArea.QueueRender();
            return;
        }

        _area.QueueDraw();
    }

    private void OnHostCursorChanged(object sender) =>
        _area.SetCursorFromName(Host.Cursor switch
        {
            XPlatCursor.Hand => "pointer",
            XPlatCursor.IBeam => "text",
            XPlatCursor.Cross => "crosshair",
            XPlatCursor.SizeAll => "move",
            XPlatCursor.SizeNS => "ns-resize",
            XPlatCursor.SizeWE => "ew-resize",
            _ => "default",
        });

    /// <summary>Repaints whenever the palette is swapped, by the system or by the application.</summary>
    private void OnThemeChanged(object sender) => OnHostInvalidated(sender);

    private static void Trace(string message)
    {
        if (DebugTrace)
        {
            Console.Error.WriteLine($"[xplat] {message}");
        }
    }

    private static XPlatMouseButtons ToMouseButton(uint button) => button switch
    {
        1 => XPlatMouseButtons.Left,
        2 => XPlatMouseButtons.Middle,
        3 => XPlatMouseButtons.Right,
        _ => XPlatMouseButtons.None,
    };
}
