using System.Runtime.InteropServices;
using SkiaSharp;

namespace Kolvir.Backend.Gtk4;

/// <summary>
/// Writes the GL seam's decisions to standard error when <c>KOLVIR_GL_TRACE=1</c>, the same
/// switch the Win32 backend honours.
/// </summary>
internal static class Gtk4GlTrace
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("KOLVIR_GL_TRACE") is "1" or "true" or "TRUE";

    /// <summary>Lets a hot path skip formatting a message nobody will read.</summary>
    internal static bool IsEnabled => Enabled;

    internal static void Write(string message)
    {
        if (Enabled)
        {
            Console.Error.WriteLine($"[kolvir-gl] {message}");
        }
    }
}

/// <summary>Outcome of a present attempt, so the view can fall back without guessing.</summary>
internal enum Gtk4GlPresentResult
{
    /// <summary>The frame reached the area's framebuffer and Skia flushed it.</summary>
    Presented,

    /// <summary>The retained buffer failed; repaint once against a fresh framebuffer.</summary>
    RetryDirect,

    /// <summary>The context is gone; the view must fall back to the CPU texture path.</summary>
    Lost,
}

/// <summary>
/// Skia's side of the GTK4 seam: a <see cref="GRContext"/> on the GLArea's own
/// <see cref="Gdk.GLContext"/>, a render target wrapping whatever framebuffer the area has bound,
/// a retained offscreen surface the control tree paints its damage into, and the blit that presents
/// it.
/// </summary>
/// <remarks>
/// <para>
/// The shape mirrors the Win32 WGL and macOS Metal seams: every step is named, and a caller that
/// gets null must be able to stay on the CPU <see cref="Gdk.MemoryTexture"/> path with no GL state
/// behind. There is no pixel format to spend here and no native library to add - the GTK4 binding
/// already talks to GDK, which owns the context.
/// </para>
/// <para>
/// The one GTK-specific difference is how Skia is given a function table. On X11 GTK4 asks EGL for
/// a GLES context, so <c>GRGlInterface.Create()</c> - which assembles Skia's GLX interface - returns
/// null. Skia's GLES assembler takes a resolver instead, so this class supplies
/// <c>eglGetProcAddress</c> and lets Skia build the interface itself; that is why libSkiaSharp
/// carrying no <c>libEGL</c> reference does not matter.
/// </para>
/// <para>
/// All of this runs on the GTK thread, from the GLArea's render signal. Nothing is scheduled on a
/// worker and no second context is created.
/// </para>
/// </remarks>
internal sealed unsafe class Gtk4GlSurface : IDisposable
{
    private const uint GlVendor = 0x1F00;
    private const uint GlRenderer = 0x1F01;
    private const uint GlFramebufferBinding = 0x8CA6;
    private const uint GlViewport = 0x0BA2;
    private const uint GlSamples = 0x80A9;
    private const uint GlStencilBits = 0x0D57;

    private delegate IntPtr GetStringProc(uint name);

    private delegate void GetIntegerProc(uint pname, int* value);

    /// <summary>
    /// Kept in a field so the native side always finds a rooted delegate. It must never throw: the
    /// exception would cross back into native Skia code.
    /// </summary>
    private static readonly GRGlGetProcedureAddressDelegate ProcedureResolver = ResolveProcedure;

    private readonly Gtk.GLArea _area;
    private readonly Gdk.GLContext _gdkContext;
    private readonly GRGlInterface _interface;
    private readonly GRContext _context;
    private readonly GetStringProc? _getString;
    private readonly GetIntegerProc? _getInteger;
    private readonly string _renderer;

    private GRBackendRenderTarget? _target;
    private SKSurface? _targetSurface;
    private SKSurface? _offscreen;
    private int _fbo;
    private int _targetWidth;
    private int _targetHeight;
    private int _targetSamples;
    private int _targetStencil;
    private int _width;
    private int _height;
    private bool _needsFull = true;
    private bool _offscreenUnavailable;
    private bool _disposed;


    private Gtk4GlSurface(
        Gtk.GLArea area,
        Gdk.GLContext gdkContext,
        GRGlInterface glInterface,
        GRContext context,
        GetStringProc? getString,
        GetIntegerProc? getInteger,
        int fbo,
        int targetWidth,
        int targetHeight,
        int samples,
        int stencil)
    {
        _area = area;
        _gdkContext = gdkContext;
        _interface = glInterface;
        _context = context;
        _getString = getString;
        _getInteger = getInteger;
        _fbo = fbo;
        _targetWidth = targetWidth;
        _targetHeight = targetHeight;

        try
        {
            // A driver can report a sample count Skia will not accept.
            _targetSamples = Math.Min(samples, Math.Max(0, context.GetMaxSurfaceSampleCount(SKColorType.Rgba8888)));
        }
        catch (Exception)
        {
            _targetSamples = 0;
        }

        _targetStencil = stencil;
        _renderer = DescribeRenderer();
    }

    /// <summary>The client area the retained buffer was last sized to, in device pixels.</summary>
    internal int Width => _width;

    /// <summary>The client area the retained buffer was last sized to, in device pixels.</summary>
    internal int Height => _height;

    /// <summary>The GL vendor and renderer string, for the demo's one-line diagnostic.</summary>
    internal string Renderer => _renderer;

    /// <summary>The retained offscreen surface, or null when the direct tier is live.</summary>
    internal SKSurface? Surface => _offscreen;

    /// <summary>True while the retained offscreen buffer is the paint target.</summary>
    internal bool HasRetainedBuffer => _offscreen is not null;

    /// <summary>True when the retained buffer's contents are undefined and a full paint is owed.</summary>
    internal bool NeedsFullRepaint => _needsFull;

    /// <summary>Consumes the full-repaint flag; the frame that asks is the one that clears it.</summary>
    internal bool TakeNeedsFull()
    {
        bool needed = _needsFull;
        _needsFull = false;
        return needed;
    }

    /// <summary>
    /// Builds the whole stack or nothing: the context, the GL interface and its validation, the
    /// Skia context and the first offscreen surface each can fail, and a caller that gets null must
    /// be able to stay on the CPU path with no GL state left behind.
    /// </summary>
    /// <param name="area">The live GLArea whose context is current on the GTK thread.</param>
    /// <param name="width">Device-pixel width to size the retained buffer for.</param>
    /// <param name="height">Device-pixel height to size the retained buffer for.</param>
    /// <param name="reason">A one-line diagnostic naming the path taken or the failing step.</param>
    internal static Gtk4GlSurface? TryCreate(Gtk.GLArea area, int width, int height, out string reason)
    {
        if (area is null)
        {
            reason = "cpu (no gl area)";
            return null;
        }

        if (width <= 0 || height <= 0)
        {
            reason = "cpu (empty surface)";
            return null;
        }

        Gdk.GLContext? gdkContext;

        try
        {
            gdkContext = area.GetContext();
        }
        catch (Exception exception)
        {
            reason = $"cpu (gl context threw {exception.GetType().Name})";
            return null;
        }

        if (gdkContext is null)
        {
            // A machine with no GL driver, or a compositor that refused to make one, lands here.
            // GTK usually also records the failure as the area's own error.
            var areaError = area.GetError();
            reason = areaError is null
                ? "cpu (no gl context)"
                : $"cpu (gl area: {areaError.Message ?? "unknown"})";
            return null;
        }

        var api = Gdk.GLAPI.Gl;

        try
        {
            api = gdkContext.GetApi();
        }
        catch (Exception exception)
        {
            Gtk4GlTrace.Write($"GetApi threw {exception.GetType().Name}, assuming desktop GL");
        }

        GRGlInterface? glInterface;
        bool assembled = false;

        try
        {
            // The context has to be current before Skia asks it anything.
            area.MakeCurrent();

            var candidate = api == Gdk.GLAPI.Gles
                ? GRGlInterface.CreateGles(ProcedureResolver)
                : GRGlInterface.CreateOpenGl(ProcedureResolver);

            assembled = candidate is not null;

            if (candidate is not null && candidate.Validate())
            {
                glInterface = candidate;
            }
            else
            {
                candidate?.Dispose();

                // A desktop GL context created by a GLX-based compositor is the case the native
                // interface is built for; it returns null on GLES, where the assembled one above
                // is the only option.
                var native = GRGlInterface.Create();
                assembled |= native is not null;
                glInterface = native is not null && native.Validate() ? native : null;
                native?.Dispose();
            }
        }
        catch (Exception exception)
        {
            reason = $"cpu (gl interface threw {exception.GetType().Name})";
            return null;
        }

        if (glInterface is null)
        {
            reason = assembled ? "cpu (interface invalid)" : "cpu (no gl interface)";
            return null;
        }

        var getString = Bind<GetStringProc>("glGetString");
        var getInteger = Bind<GetIntegerProc>("glGetIntegerv");

        // The area's framebuffer has to be read before Skia creates anything: creating a texture
        // surface leaves one of Skia's own framebuffers bound, and a target built from that id
        // would be blitted into Skia's scratch buffer while GTK composites its own - which looks
        // exactly like "the window is empty".
        if (!ReadFrameState(getInteger, out var fbo, out var targetWidth, out var targetHeight, out var sampleCount, out var stencilBits))
        {
            glInterface.Dispose();
            reason = "cpu (no gl framebuffer)";
            return null;
        }

        GRContext? context;

        try
        {
            context = GRContext.CreateGl(glInterface);
        }
        catch (Exception exception)
        {
            glInterface.Dispose();
            reason = $"cpu (gr context threw {exception.GetType().Name})";
            return null;
        }

        if (context is null)
        {
            glInterface.Dispose();
            reason = "cpu (no gr context)";
            return null;
        }

        var surface = new Gtk4GlSurface(
            area,
            gdkContext,
            glInterface,
            context,
            getString,
            getInteger,
            fbo,
            targetWidth,
            targetHeight,
            sampleCount,
            stencilBits);

        if (!surface.EnsureSize(width, height))
        {
            reason = $"cpu (no gl surface: {width}x{height})";
            surface.Dispose();
            return null;
        }

        reason = "gl";
        return surface;
    }

    /// <summary>
    /// Sizes the retained offscreen buffer. False means the context cannot provide a surface at all
    /// and the view must fall back to the CPU path.
    /// </summary>
    internal bool EnsureSize(int width, int height)
    {
        if (_disposed || width <= 0 || height <= 0)
        {
            return false;
        }

        if (_width == width && _height == height)
        {
            return true;
        }

        // A new size invalidates everything the retained buffer held, and so does a newly created
        // one; either way the next paint is full.
        _needsFull = true;
        _width = width;
        _height = height;

        // The render target wraps the area's framebuffer, which GTK may have replaced when it
        // resized the area, so the cached one is no longer trustworthy.
        DropTarget();

        if (_offscreenUnavailable)
        {
            // The direct tier has no buffer to rebuild; the target is recreated on demand.
            return true;
        }

        _offscreen?.Dispose();
        _offscreen = null;

        if (_context.IsAbandoned)
        {
            return true;
        }

        try
        {
            _area.MakeCurrent();

            _offscreen = SKSurface.Create(
                _context,
                false,
                new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul),
                1,
                GRSurfaceOrigin.BottomLeft);
        }
        catch (Exception exception)
        {
            _offscreen = null;
            Gtk4GlTrace.Write($"offscreen surface threw {exception.GetType().Name}, presenting directly");
        }

        if (_offscreen is null)
        {
            // Do not keep trying on every resize: the tier is chosen for the life of the window.
            _offscreenUnavailable = true;
            Gtk4GlTrace.Write("offscreen surface unavailable, presenting directly");
        }

        return true;
    }

    /// <summary>
    /// Blits the retained offscreen buffer onto the area's framebuffer. The tree is not repainted
    /// here - <see cref="Surface"/> already holds the frame.
    /// </summary>
    internal Gtk4GlPresentResult PresentRetained()
    {
        if (_disposed || _context.IsAbandoned)
        {
            return Gtk4GlPresentResult.Lost;
        }

        if (_offscreen is null)
        {
            return Gtk4GlPresentResult.RetryDirect;
        }

        try
        {
            _area.MakeCurrent();

            // GTK, the window system and our own previous frame all touch GL state, and Skia caches
            // what it set last time. Resetting the cache makes Skia re-issue every piece of state it
            // needs, which is what stops a second frame from drawing with a stale binding.
            _context.ResetContext(GRGlBackendState.All);

            var target = EnsureTarget();

            if (target is null)
            {
                DropRetainedBuffer();
                return Gtk4GlPresentResult.RetryDirect;
            }

            using var image = _offscreen.Snapshot();

            if (image is null)
            {
                Gtk4GlTrace.Write("present failed: no snapshot");
                DropRetainedBuffer();
                return Gtk4GlPresentResult.RetryDirect;
            }

            // The target is the framebuffer's own size, which can differ from the retained buffer
            // by a rounding step; drawing into its exact rect keeps the blit 1:1 instead of
            // leaving a strip of undefined framebuffer at the edge.
            target.Canvas.DrawImage(
                image,
                new SKRect(0, 0, _targetWidth, _targetHeight),
                new SKSamplingOptions(SKFilterMode.Nearest),
                null);

            target.Canvas.Flush();
            _context.Flush();

            return Gtk4GlPresentResult.Presented;
        }
        catch (Exception exception)
        {
            Gtk4GlTrace.Write($"offscreen present threw {exception.GetType().Name}, dropping to direct");
            DropRetainedBuffer();
            return Gtk4GlPresentResult.RetryDirect;
        }
    }

    /// <summary>
    /// Paints a complete frame straight into the area's framebuffer. Only the direct tier uses this:
    /// GTK does not promise the framebuffer still holds the previous frame, so a partial paint would
    /// leave the rest of the window garbage.
    /// </summary>
    /// <param name="paint">Paints a full frame into the surface; called at most once.</param>
    internal Gtk4GlPresentResult PresentDirect(Action<SKSurface> paint)
    {
        ArgumentNullException.ThrowIfNull(paint);

        if (_disposed || _context.IsAbandoned)
        {
            return Gtk4GlPresentResult.Lost;
        }

        try
        {
            _area.MakeCurrent();

            // GTK, the window system and our own previous frame all touch GL state, and Skia caches
            // what it set last time. Resetting the cache makes Skia re-issue every piece of state it
            // needs, which is what stops a second frame from drawing with a stale binding.
            _context.ResetContext(GRGlBackendState.All);

            var target = EnsureTarget();

            if (target is null)
            {
                return Gtk4GlPresentResult.Lost;
            }

            paint(target);
            target.Canvas.Flush();
            _context.Flush();

            return Gtk4GlPresentResult.Presented;
        }
        catch (Exception exception)
        {
            Gtk4GlTrace.Write($"direct present threw {exception.GetType().Name}");
            return Gtk4GlPresentResult.Lost;
        }
    }

    /// <summary>
    /// Drops the retained buffer and keeps the direct tier for the rest of the window's life. This
    /// is the one-tier demotion the Win32 and AppKit seams make.
    /// </summary>
    internal void DropRetainedBuffer()
    {
        _offscreen?.Dispose();
        _offscreen = null;
        _offscreenUnavailable = true;
        _needsFull = true;
    }

    /// <summary>
    /// Reads the framebuffer GTK has bound and its viewport. Must run before Skia creates or binds
    /// anything at all - including before the first texture surface exists - because creating one
    /// leaves a Skia framebuffer bound.
    /// </summary>
    private static bool ReadFrameState(
        GetIntegerProc? getInteger,
        out int fbo,
        out int width,
        out int height,
        out int samples,
        out int stencil)
    {
        fbo = 0;
        width = 0;
        height = 0;
        samples = 0;
        stencil = 0;

        if (getInteger is null)
        {
            return false;
        }

        var viewport = new int[4];
        var bound = 0;
        var sampleCount = 0;
        var stencilCount = 0;

        fixed (int* pointer = viewport)
        {
            getInteger(GlFramebufferBinding, &bound);
            getInteger(GlViewport, pointer);
            getInteger(GlSamples, &sampleCount);
            getInteger(GlStencilBits, &stencilCount);
        }

        fbo = bound;
        samples = sampleCount;
        stencil = stencilCount;
        width = viewport[2];
        height = viewport[3];

        return width > 0 && height > 0;
    }

    /// <summary>
    /// Re-reads the framebuffer for the frame about to be presented. The GTK thread calls this at
    /// the top of every render, before Skia is asked to do anything.
    /// </summary>
    internal void CaptureFrameState()
    {
        if (!ReadFrameState(_getInteger, out var fbo, out var width, out var height, out var samples, out var stencil))
        {
            return;
        }

        try
        {
            samples = Math.Min(samples, Math.Max(0, _context.GetMaxSurfaceSampleCount(SKColorType.Rgba8888)));
        }
        catch (Exception)
        {
            samples = 0;
        }

        if (fbo == _fbo && width == _targetWidth && height == _targetHeight &&
            samples == _targetSamples && stencil == _targetStencil)
        {
            return;
        }

        _fbo = fbo;
        _targetWidth = width;
        _targetHeight = height;
        _targetSamples = samples;
        _targetStencil = stencil;
        DropTarget();

        Gtk4GlTrace.Write(
            $"framebuffer: fbo {fbo}, {width}x{height}, {samples} samples, {stencil} stencil");
    }

    /// <summary>
    /// Wraps the framebuffer GTK bound for this frame, rebuilding the target only when the
    /// framebuffer or its format actually changed. The id is queried rather than assumed: a GTK4
    /// window renders into an offscreen framebuffer of its own, never the 0 a swap-chain window
    /// would use.
    /// </summary>
    private SKSurface? EnsureTarget()
    {
        if (_disposed || _context.IsAbandoned)
        {
            return null;
        }

        if (_targetWidth <= 0 || _targetHeight <= 0)
        {
            return null;
        }

        if (_targetSurface is not null)
        {
            return _targetSurface;
        }

        var framebufferInfo = new GRGlFramebufferInfo(
            (uint)_fbo,
            SKColorType.Rgba8888.ToGlSizedFormat());

        var target = new GRBackendRenderTarget(
            _targetWidth,
            _targetHeight,
            _targetSamples,
            _targetStencil,
            framebufferInfo);

        if (!target.IsValid)
        {
            target.Dispose();
            Gtk4GlTrace.Write(
                $"invalid render target: fbo {_fbo}, {_targetWidth}x{_targetHeight}, {_targetSamples} samples, {_targetStencil} stencil");
            return null;
        }

        var surface = SKSurface.Create(
            _context,
            target,
            GRSurfaceOrigin.BottomLeft,
            SKColorType.Rgba8888);

        if (surface is null)
        {
            target.Dispose();
            Gtk4GlTrace.Write(
                $"no framebuffer surface: fbo {_fbo}, {_targetWidth}x{_targetHeight}, {_targetSamples} samples, {_targetStencil} stencil");
            return null;
        }

        Gtk4GlTrace.Write(
            $"target: fbo {_fbo}, {_targetWidth}x{_targetHeight}, {_targetSamples} samples, {_targetStencil} stencil");

        _target = target;
        _targetSurface = surface;

        return _targetSurface;
    }

    private void DropTarget()
    {
        _targetSurface?.Dispose();
        _targetSurface = null;
        _target?.Dispose();
        _target = null;
    }

    /// <summary>
    /// Resolves a GL entry point for the current context, the table Skia's GLES assembler asks for.
    /// libEGL answers for both extension and core functions under GLVND, and the dynamic symbol
    /// table is the fallback for an implementation that does not.
    /// </summary>
    private static IntPtr ResolveProcedure(string name)
    {
        try
        {
            var pointer = eglGetProcAddress(name);

            if (pointer != IntPtr.Zero)
            {
                return pointer;
            }
        }
        catch (Exception)
        {
            // libEGL is absent on a system whose GTK has no GL at all; the symbol lookup below is
            // then the only route, and returning zero tells Skia the function is missing.
        }

        try
        {
            return dlsym(IntPtr.Zero, name);
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }

    private static T? Bind<T>(string name) where T : Delegate
    {
        var pointer = ResolveProcedure(name);

        if (pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.GetDelegateForFunctionPointer<T>(pointer);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string DescribeRenderer()
    {
        if (_getString is null)
        {
            return "unknown";
        }

        string? vendor = ReadString(GlVendor);
        string? renderer = ReadString(GlRenderer);

        if (string.IsNullOrWhiteSpace(vendor) && string.IsNullOrWhiteSpace(renderer))
        {
            return "unknown";
        }

        if (string.IsNullOrWhiteSpace(vendor))
        {
            return renderer!;
        }

        return string.IsNullOrWhiteSpace(renderer) ? vendor : $"{vendor} / {renderer}";
    }

    private string? ReadString(uint name)
    {
        try
        {
            var pointer = _getString!(name);
            return pointer == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(pointer);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Releasing Skia's GPU resources wants the context current; if the area is already gone,
        // disposal still has to finish without throwing.
        try
        {
            _area.MakeCurrent();
            _context.Flush();
        }
        catch (Exception)
        {
        }

        DropTarget();
        _offscreen?.Dispose();
        _offscreen = null;
        _context.Dispose();
        _interface.Dispose();

        // The Gdk.GLContext belongs to the GLArea and is destroyed with the widget.
        _ = _gdkContext;
    }

    [DllImport("libEGL.so.1", EntryPoint = "eglGetProcAddress")]
    private static extern IntPtr eglGetProcAddress([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport("libdl.so.2", EntryPoint = "dlsym")]
    private static extern IntPtr dlsym(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string symbol);
}
