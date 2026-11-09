using System.Runtime.InteropServices;
using SkiaSharp;

namespace XPlatUI.Backend.Win32;

/// <summary>
/// The WGL half of the Win32 backend's drawing seam. One probe creates a context, the context
/// describes the framebuffer, and <see cref="Win32GlSurface"/> owns the Skia render target and
/// surface built on it.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is internal and stays that way. GTK4 and AppKit will grow the same
/// probe/context/target/surface/present sequence for their own GL bindings, and an ANGLE-backed
/// Windows implementation would replace this file rather than add a second public switch. The
/// window class only sees the small surface type below.
/// </para>
/// <para>
/// All of this runs on the UI thread: the context is created on the thread that receives
/// <c>WM_SIZE</c>, and every later make-current, paint and swap happens on the thread that receives
/// <c>WM_PAINT</c>. Nothing schedules GL work on a timer or a worker.
/// </para>
/// </remarks>
internal interface IWin32GlProbe
{
    /// <summary>
    /// Sets a pixel format on the window and creates a WGL context for it, or returns null when the
    /// machine has no usable OpenGL implementation. A failure must leave nothing live behind.
    /// </summary>
    Win32GlContext? TryCreate(IntPtr hwnd);
}

/// <summary>The production probe: WGL on the window's own device context.</summary>
internal sealed class Win32GlProbe : IWin32GlProbe
{
    internal static Win32GlProbe Instance { get; } = new();

    public Win32GlContext? TryCreate(IntPtr hwnd) => Win32GlContext.TryCreate(hwnd);
}

/// <summary>
/// A WGL context plus the pixel format it was created with. This is native state only: Skia's
/// <c>GRContext</c> and surface belong to <see cref="Win32GlSurface"/>.
/// </summary>
internal sealed class Win32GlContext : IDisposable
{
    private readonly IntPtr _hwnd;
    private IntPtr _dc;
    private IntPtr _context;
    private readonly int _samples;
    private readonly int _stencilBits;

    private Win32GlContext(
        IntPtr hwnd,
        IntPtr dc,
        IntPtr context,
        int samples,
        int stencilBits,
        string renderer)
    {
        _hwnd = hwnd;
        _dc = dc;
        _context = context;
        _samples = samples;
        _stencilBits = stencilBits;
        Renderer = renderer;
    }

    /// <summary>The framebuffer's multisample count, queried from GL rather than assumed.</summary>
    internal int Samples => _samples;

    /// <summary>The framebuffer's stencil depth, queried from GL/PFD rather than assumed.</summary>
    internal int StencilBits => _stencilBits;

    /// <summary>Vendor and renderer, so a screenshot says which GPU produced its numbers.</summary>
    internal string Renderer { get; }

    internal static Win32GlContext? TryCreate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        var dc = NativeMethods.GetDC(hwnd);
        if (dc == IntPtr.Zero)
        {
            return null;
        }

        var context = IntPtr.Zero;

        try
        {
            var descriptor = new NativeMethods.PIXELFORMATDESCRIPTOR
            {
                nSize = (ushort)Marshal.SizeOf<NativeMethods.PIXELFORMATDESCRIPTOR>(),
                nVersion = 1,
                dwFlags = NativeMethods.PfdDrawToWindow | NativeMethods.PfdSupportOpenGl | NativeMethods.PfdDoubleBuffer,
                iPixelType = NativeMethods.PfdTypeRgba,
                cColorBits = 32,
                cAlphaBits = 8,
                cDepthBits = 24,
                cStencilBits = 8,
            };

            // wglChoosePixelFormat/wglSetPixelFormat, not gdi32's ChoosePixelFormat/SetPixelFormat:
            // the wgl exports go through the ICD that wglCreateContext will use, while the gdi32
            // ones can hand back a system format number that wglCreateContext rejects.
            int format = NativeMethods.wglChoosePixelFormat(dc, ref descriptor);
            if (format == 0)
            {
                NativeMethods.ReleaseDC(hwnd, dc);
                return null;
            }

            // A window's pixel format can be set once and never changed, which is why the whole GL
            // stack either comes up on this call or the window stays on the DIB path for its life.
            if (!NativeMethods.wglSetPixelFormat(dc, format, ref descriptor))
            {
                NativeMethods.ReleaseDC(hwnd, dc);
                return null;
            }

            var actual = new NativeMethods.PIXELFORMATDESCRIPTOR
            {
                nSize = (ushort)Marshal.SizeOf<NativeMethods.PIXELFORMATDESCRIPTOR>(),
            };

            if (NativeMethods.wglDescribePixelFormat(
                    dc,
                    format,
                    (uint)Marshal.SizeOf<NativeMethods.PIXELFORMATDESCRIPTOR>(),
                    ref actual) == 0
                || (actual.dwFlags & NativeMethods.PfdDoubleBuffer) == 0)
            {
                NativeMethods.ReleaseDC(hwnd, dc);
                return null;
            }

            context = NativeMethods.wglCreateContext(dc);
            if (context == IntPtr.Zero)
            {
                NativeMethods.ReleaseDC(hwnd, dc);
                return null;
            }

            if (!NativeMethods.wglMakeCurrent(dc, context))
            {
                NativeMethods.wglDeleteContext(context);
                NativeMethods.ReleaseDC(hwnd, dc);
                return null;
            }

            // The descriptor has no sample count, so that comes from GL. The stencil depth comes
            // from the *described* format: it is what the driver actually granted, and it is what
            // the render target must be told.
            int samples = 0;
            int stencilBits = actual.cStencilBits;

            try
            {
                NativeMethods.glGetIntegerv(NativeMethods.GlSamples, out samples);
            }
            catch (EntryPointNotFoundException)
            {
                // A minimal opengl32 without the query: no samples is the safe answer, and the
                // described format still supplies the stencil depth.
            }

            var swapInterval = ResolveSwapInterval();
            if (swapInterval is not null)
            {
                try
                {
                    swapInterval(1);
                }
                catch (Exception)
                {
                    // A driver that advertises the extension but rejects the call keeps vsync at its
                    // default; that is preferable to losing the context over it.
                }
            }

            string renderer = DescribeRenderer();

            return new Win32GlContext(
                hwnd,
                dc,
                context,
                Math.Max(samples, 0),
                Math.Max(stencilBits, 0),
                renderer);
        }
        catch (Exception)
        {
            if (context != IntPtr.Zero)
            {
                NativeMethods.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
                NativeMethods.wglDeleteContext(context);
            }

            NativeMethods.ReleaseDC(hwnd, dc);
            return null;
        }
    }

    /// <summary>Binds this context to the calling (UI) thread.</summary>
    internal bool MakeCurrent() =>
        _context != IntPtr.Zero && NativeMethods.wglMakeCurrent(_dc, _context);

    /// <summary>Presents the back buffer. The context must be current on this thread.</summary>
    internal bool Present() => _context != IntPtr.Zero && NativeMethods.wglSwapBuffers(_dc);

    private static string DescribeRenderer()
    {
        string? vendor = ReadGlString(NativeMethods.GlVendor);
        string? renderer = ReadGlString(NativeMethods.GlRenderer);

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

    private static string? ReadGlString(uint name)
    {
        try
        {
            var pointer = NativeMethods.glGetString(name);
            return pointer == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(pointer);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static NativeMethods.WglSwapIntervalProc? ResolveSwapInterval()
    {
        var address = NativeMethods.wglGetProcAddress("wglSwapIntervalEXT");
        return address == IntPtr.Zero
            ? null
            : Marshal.GetDelegateForFunctionPointer<NativeMethods.WglSwapIntervalProc>(address);
    }

    public void Dispose()
    {
        if (_context == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        NativeMethods.wglDeleteContext(_context);
        _context = IntPtr.Zero;

        if (_dc != IntPtr.Zero)
        {
            NativeMethods.ReleaseDC(_hwnd, _dc);
            _dc = IntPtr.Zero;
        }
    }
}

/// <summary>
/// Skia's side of the seam: a <c>GRContext</c>, a render target for the window's default
/// framebuffer, and the surface the control tree paints into. Created by <see cref="Win32Gfx"/>,
/// owned by the window, and presented with <see cref="Present"/>.
/// </summary>
internal sealed class Win32GlSurface : IDisposable
{
    private readonly Win32GlContext _context;
    private GRGlInterface? _interface;
    private GRContext? _contextState;
    private GRBackendRenderTarget? _target;
    private SKSurface? _surface;
    private int _width;
    private int _height;

    private Win32GlSurface(Win32GlContext context) => _context = context;

    internal SKSurface? Surface => _surface;

    /// <summary>The vendor and renderer string, for the demo's one-line diagnostic.</summary>
    internal string Renderer => _context.Renderer;

    internal int Width => _width;

    internal int Height => _height;

    internal bool MakeCurrent() => _context.MakeCurrent();

    /// <summary>
    /// Builds the whole stack or nothing: interface, Skia context, render target and surface each
    /// can fail, and a caller that gets null must be able to stay on the CPU path with no GL state
    /// left behind.
    /// </summary>
    internal static Win32GlSurface? TryCreate(
        IntPtr hwnd,
        IWin32GlProbe probe,
        int width,
        int height,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(probe);

        if (hwnd == IntPtr.Zero)
        {
            reason = "cpu (no window)";
            return null;
        }

        if (width <= 0 || height <= 0)
        {
            reason = "cpu (empty surface)";
            return null;
        }

        Win32GlContext? nativeContext;
        try
        {
            nativeContext = probe.TryCreate(hwnd);
        }
        catch (Exception)
        {
            nativeContext = null;
        }

        if (nativeContext is null)
        {
            reason = "cpu (no gl context)";
            return null;
        }

        var surface = new Win32GlSurface(nativeContext);

        try
        {
            if (!nativeContext.MakeCurrent())
            {
                reason = "cpu (no current context)";
                surface.Dispose();
                return null;
            }

            var glInterface = GRGlInterface.Create();
            if (glInterface is null || !glInterface.Validate())
            {
                glInterface?.Dispose();
                reason = "cpu (no gl interface)";
                surface.Dispose();
                return null;
            }

            var grContext = GRContext.CreateGl(glInterface);
            if (grContext is null)
            {
                glInterface.Dispose();
                reason = "cpu (no skia gl context)";
                surface.Dispose();
                return null;
            }

            surface._interface = glInterface;
            surface._contextState = grContext;

            if (surface.CreateTarget(width, height) is { } failure)
            {
                reason = failure;
                surface.Dispose();
                return null;
            }

            reason = "gl";
            return surface;
        }
        catch (Exception)
        {
            reason = "cpu (gl init failed)";
            surface.Dispose();
            return null;
        }
    }

    /// <summary>Recreates the render target after a resize. False means GL must be abandoned.</summary>
    internal bool Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        if (_surface is not null && _width == width && _height == height)
        {
            return true;
        }

        try
        {
            return CreateTarget(width, height) is null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Creates the target and surface, or names why it could not.</summary>
    private string? CreateTarget(int width, int height)
    {
        if (_contextState is null)
        {
            return "cpu (no skia gl context)";
        }

        if (_contextState.IsAbandoned)
        {
            return "cpu (skia gl context abandoned)";
        }

        if (!_context.MakeCurrent())
        {
            return "cpu (no current context)";
        }

        _surface?.Dispose();
        _surface = null;
        _target?.Dispose();
        _target = null;

        // The default framebuffer is 0, and its real sample and stencil counts come from the context.
        // Passing 0/8 instead would render wrongly or trip a Skia assert on a multisampled format.
        var frameBuffer = new GRGlFramebufferInfo(
            0,
            SKColorType.Rgba8888.ToGlSizedFormat());

        // A driver can report a sample count Skia will not accept. Clamping to what this context
        // can actually render keeps the real value whenever it is usable and still avoids a failed
        // target on a driver that reports something impossible.
        int samples = Math.Min(
            _context.Samples,
            Math.Max(0, _contextState.GetMaxSurfaceSampleCount(SKColorType.Rgba8888)));

        var target = new GRBackendRenderTarget(
            width,
            height,
            samples,
            _context.StencilBits,
            frameBuffer);

        if (!target.IsValid)
        {
            target.Dispose();
            return $"cpu (invalid render target: {width}x{height}, {samples} samples, {_context.StencilBits} stencil)";
        }

        var surface = SKSurface.Create(
            _contextState,
            target,
            GRSurfaceOrigin.BottomLeft,
            SKColorType.Rgba8888);

        if (surface is null)
        {
            target.Dispose();
            return $"cpu (no skia surface: {width}x{height}, {samples} samples, {_context.StencilBits} stencil)";
        }

        _target = target;
        _surface = surface;
        _width = width;
        _height = height;
        return null;
    }

    /// <summary>Submits the frame and swaps the back buffer onto the window.</summary>
    internal bool Present()
    {
        if (_contextState is null || _surface is null || !_context.MakeCurrent())
        {
            return false;
        }

        _contextState.Flush();
        return _context.Present();
    }

    public void Dispose()
    {
        // Releasing Skia's GPU resources wants the context current; if it is already gone, disposal
        // still has to finish without throwing.
        try
        {
            if (_context.MakeCurrent())
            {
                _contextState?.Flush();
            }
        }
        catch (Exception)
        {
        }

        _surface?.Dispose();
        _surface = null;
        _target?.Dispose();
        _target = null;
        _contextState?.Dispose();
        _contextState = null;
        _interface?.Dispose();
        _interface = null;

        _context.Dispose();
    }
}

/// <summary>
/// The window's single decision point for hardware acceleration. Keeping it here - rather than
/// inline in the window class - is what lets a test drive the fallback with a probe that always
/// fails, which is the only shape available on a machine with no OpenGL implementation.
/// </summary>
internal static class Win32Gfx
{
    /// <summary>
    /// Returns a GL surface when the application asked for acceleration and the probe succeeded,
    /// or null with the reason the window must stay on its DIB path.
    /// </summary>
    /// <param name="hardwareAcceleration">The application option, read once when the window exists.</param>
    /// <param name="hwnd">The window's native handle.</param>
    /// <param name="probe">The context probe; the production one is <see cref="Win32GlProbe"/>.</param>
    /// <param name="width">The framebuffer width in physical pixels.</param>
    /// <param name="height">The framebuffer height in physical pixels.</param>
    /// <param name="reason">A one-line diagnostic naming the path taken.</param>
    internal static Win32GlSurface? TryCreate(
        bool hardwareAcceleration,
        IntPtr hwnd,
        IWin32GlProbe probe,
        int width,
        int height,
        out string reason)
    {
        if (!hardwareAcceleration)
        {
            reason = "cpu (disabled)";
            return null;
        }

        return Win32GlSurface.TryCreate(hwnd, probe, width, height, out reason);
    }
}
