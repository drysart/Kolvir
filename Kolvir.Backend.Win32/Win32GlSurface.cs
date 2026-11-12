using System.Runtime.InteropServices;
using SkiaSharp;

namespace Kolvir.Backend.Win32;

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
internal static class Win32GlTrace
{
    private const int StdErrorHandle = -12;
    private const int MaxTraceBytes = 1024;

    private static readonly bool Enabled = ResolveEnabled();

    /// <summary>Lets a hot path skip formatting a message nobody will read.</summary>
    internal static bool IsEnabled => Enabled;

    internal static unsafe void Write(string message)
    {
        if (!Enabled)
        {
            return;
        }

        // Console.Error costs about 75 KB in a trimmed native image, which this opt-in diagnostic
        // does not justify. Write the UTF-8 bytes to the standard error handle directly instead.
        byte* buffer = stackalloc byte[MaxTraceBytes];
        int length = Encode("kolvir-gl: ", buffer, 0);

        length = Encode(message, buffer, length);

        if (length < MaxTraceBytes)
        {
            buffer[length++] = (byte)'\n';
        }

        IntPtr handle = NativeMethods.GetStdHandle(StdErrorHandle);

        if (handle != IntPtr.Zero && handle != new IntPtr(-1))
        {
            NativeMethods.WriteFile(handle, buffer, (uint)length, out _, IntPtr.Zero);
        }
    }

    private static unsafe int Encode(string text, byte* buffer, int offset)
    {
        for (int i = 0; i < text.Length && offset < MaxTraceBytes - 4; i++)
        {
            char c = text[i];

            if (c < 0x80)
            {
                buffer[offset++] = (byte)c;
            }
            else if (c < 0x800)
            {
                buffer[offset++] = (byte)(0xC0 | (c >> 6));
                buffer[offset++] = (byte)(0x80 | (c & 0x3F));
            }
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                int codePoint = char.ConvertToUtf32(c, text[++i]);
                buffer[offset++] = (byte)(0xF0 | (codePoint >> 18));
                buffer[offset++] = (byte)(0x80 | ((codePoint >> 12) & 0x3F));
                buffer[offset++] = (byte)(0x80 | ((codePoint >> 6) & 0x3F));
                buffer[offset++] = (byte)(0x80 | (codePoint & 0x3F));
            }
            else
            {
                buffer[offset++] = (byte)(0xE0 | (c >> 12));
                buffer[offset++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                buffer[offset++] = (byte)(0x80 | (c & 0x3F));
            }
        }

        return offset;
    }

    private static unsafe bool ResolveEnabled()
    {
        char* buffer = stackalloc char[8];
        uint length = NativeMethods.GetEnvironmentVariableW("KOLVIR_GL_TRACE", buffer, 8);

        if (length is 0 or > 8)
        {
            return false;
        }

        return buffer[0] == '1' || buffer[0] is 't' or 'T';
    }
}

/// <summary>Which family of pixel-format entry points a context is created with.</summary>
/// <remarks>
/// The three pixel-format calls have two faces on Windows. <c>gdi32</c>'s
/// <c>ChoosePixelFormat</c>/<c>SetPixelFormat</c>/<c>DescribePixelFormat</c> are the app-facing ones
/// and route through the display driver to the registered ICD, which is what a real vendor driver
/// expects. <c>opengl32</c> exports its own <c>wgl*</c> versions, which are the ICD-facing side;
/// Mesa implements those as the stand-in stack and a real ICD may not. Neither can be assumed, so
/// each is tried on a throwaway window before the real window spends its one-shot pixel format.
/// </remarks>
internal enum Win32GlFlavor
{
    /// <summary><c>gdi32</c> <c>ChoosePixelFormat</c>/<c>SetPixelFormat</c>, the app-facing pair.</summary>
    Gdi32,

    /// <summary><c>opengl32</c> <c>wglChoosePixelFormat</c>/<c>wglSetPixelFormat</c>, the ICD-facing pair.</summary>
    Opengl32,
}

/// <summary>Tries one flavour on one window and returns a live context or a bare failure detail.</summary>
internal interface IWin32GlFlavorTester
{
    /// <summary>
    /// Sets a pixel format on <paramref name="hwnd"/> with <paramref name="flavor"/> and creates a
    /// WGL context for it. A failure must leave no context live and returns a short detail such as
    /// <c>gdi: no wgl context (error 0)</c>. The returned object is disposed by the probe, which is
    /// all the selection needs; the real window's creation asks for the concrete context.
    /// </summary>
    IDisposable? TryCreate(IntPtr hwnd, Win32GlFlavor flavor, out string failure);
}

/// <summary>Creates and destroys the hidden 1 x 1 windows the flavour probe spends.</summary>
internal interface IWin32ProbeWindowFactory
{
    IntPtr Create();

    void Destroy(IntPtr hwnd);
}

/// <summary>
/// The production probe: chooses a flavour on throwaway windows, then builds the real context on the
/// window the caller handed over.
/// </summary>
internal interface IWin32GlProbe
{
    /// <summary>
    /// Tests each flavour on its own throwaway window and returns the first one that produced a
    /// live context, or null with a combined failure detail when neither did.
    /// </summary>
    Win32GlFlavor? SelectFlavor(out string failure);

    /// <summary>Builds the chosen flavour's context on the real window.</summary>
    Win32GlContext? TryCreate(IntPtr hwnd, Win32GlFlavor flavor, out string failure);
}

/// <summary>The production probe.</summary>
internal sealed class Win32GlProbe : IWin32GlProbe
{
    private static readonly Win32GlFlavor[] Flavors =
    [
        // The app-facing pair first, because that is what a real vendor ICD expects. A stack that
        // is standing in for the whole thing - Mesa - fails here after spending the throwaway
        // window's format, and the opengl32 pair on a fresh window is what rescues it.
        Win32GlFlavor.Gdi32,
        Win32GlFlavor.Opengl32,
    ];

    private readonly IWin32GlFlavorTester _tester;
    private readonly IWin32ProbeWindowFactory _windows;

    internal Win32GlProbe(IWin32GlFlavorTester tester, IWin32ProbeWindowFactory windows)
    {
        ArgumentNullException.ThrowIfNull(tester);
        ArgumentNullException.ThrowIfNull(windows);

        _tester = tester;
        _windows = windows;
    }

    internal static Win32GlProbe Instance { get; } = new(new Win32GlFlavorTester(), new Win32ProbeWindowFactory());

    public Win32GlFlavor? SelectFlavor(out string failure) =>
        Win32GlFlavorSelection.Select(_tester, _windows, out failure);

    public Win32GlContext? TryCreate(IntPtr hwnd, Win32GlFlavor flavor, out string failure) =>
        _tester.TryCreate(hwnd, flavor, out failure) as Win32GlContext;

    /// <summary>The order the flavours are tried in, exposed for the trace and the tests.</summary>
    internal static IReadOnlyList<Win32GlFlavor> Order => Flavors;
}

/// <summary>The flavour decision, split from the native windows so a test can drive every case.</summary>
internal static class Win32GlFlavorSelection
{
    /// <summary>
    /// Tries each flavour on a fresh throwaway window. The real window is never passed to this
    /// method, which is what guarantees a failed probe cannot consume its one-shot pixel format.
    /// </summary>
    internal static Win32GlFlavor? Select(
        IWin32GlFlavorTester tester,
        IWin32ProbeWindowFactory windows,
        out string failure)
    {
        ArgumentNullException.ThrowIfNull(tester);
        ArgumentNullException.ThrowIfNull(windows);

        var failures = new List<string>(2);

        foreach (var flavor in Win32GlProbe.Order)
        {
            var probeWindow = windows.Create();

            if (probeWindow == IntPtr.Zero)
            {
                failures.Add($"{Name(flavor)}: no probe window");
                Win32GlTrace.Write($"{Name(flavor)}: no probe window");
                continue;
            }

            try
            {
                var context = tester.TryCreate(probeWindow, flavor, out var detail);

                if (context is not null)
                {
                    context.Dispose();
                    Win32GlTrace.Write($"{Name(flavor)}: live context on probe window, selected");
                    failure = string.Empty;
                    return flavor;
                }

                failures.Add(detail);
                Win32GlTrace.Write(detail);
            }
            catch (Exception exception)
            {
                failures.Add($"{Name(flavor)}: threw {exception.GetType().Name}");
                Win32GlTrace.Write($"{Name(flavor)}: threw {exception.GetType().Name}");
            }
            finally
            {
                windows.Destroy(probeWindow);
            }
        }

        // Report the flavour that was tried last as the headline reason, in the same vocabulary the
        // individual steps use, and keep the other flavour's reason after it. A screenshot then
        // names the failing step rather than a generic "no context".
        failure = failures.Count switch
        {
            0 => "no gl context",
            1 => failures[0],
            _ => Combine(failures),
        };

        return null;
    }

    private static string Combine(List<string> failures)
    {
        string combined = Detail(failures[^1]);

        for (int i = 0; i < failures.Count - 1; i++)
        {
            combined += "; " + failures[i];
        }

        return combined;
    }

    private static string Detail(string failure)
    {
        int separator = failure.IndexOf(": ", StringComparison.Ordinal);

        return separator < 0 ? failure : failure[(separator + 2)..];
    }

    internal static string Name(Win32GlFlavor flavor) => flavor switch
    {
        Win32GlFlavor.Gdi32 => "gdi",
        Win32GlFlavor.Opengl32 => "wgl",
        _ => flavor.ToString(),
    };
}

/// <summary>Creates the hidden probe window. <c>STATIC</c> is a system class, so nothing registers.</summary>
internal sealed class Win32ProbeWindowFactory : IWin32ProbeWindowFactory
{
    public IntPtr Create() => NativeMethods.CreateWindowExW(
        0,
        "STATIC",
        string.Empty,
        NativeMethods.WsPopup,
        0,
        0,
        1,
        1,
        IntPtr.Zero,
        IntPtr.Zero,
        NativeMethods.GetModuleHandleW(null),
        IntPtr.Zero);

    public void Destroy(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(hwnd);
        }
    }
}

/// <summary>The production flavour tester: the actual WGL calls on one window.</summary>
internal sealed class Win32GlFlavorTester : IWin32GlFlavorTester
{
    public IDisposable? TryCreate(IntPtr hwnd, Win32GlFlavor flavor, out string failure)
    {
        var context = Win32GlContext.TryCreate(hwnd, flavor, out failure);

        if (context is null)
        {
            return null;
        }

        // A live WGL context is not enough: the generic GDI OpenGL 1.1 stack creates one and then
        // has no extensions Skia can use. Validating the Skia side here, before the real window's
        // one-shot pixel format is spent, is what keeps the DIB path untouched on such a machine.
        var skiaFailure = Win32GlSurface.ProbeSkia(context);

        if (skiaFailure is not null)
        {
            context.Dispose();
            failure = $"{Win32GlFlavorSelection.Name(flavor)}: {skiaFailure}";
            return null;
        }

        return context;
    }
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

    /// <summary>The framebuffer's stencil depth, taken from the described pixel format.</summary>
    internal int StencilBits => _stencilBits;

    /// <summary>Vendor and renderer, so a screenshot says which GPU produced its numbers.</summary>
    internal string Renderer { get; }

    /// <summary>
    /// Builds a context with one flavour. Every failure returns a bare detail naming the step and
    /// the Win32 error where there is one; the caller wraps it in <c>cpu (...)</c>.
    /// </summary>
    internal static Win32GlContext? TryCreate(IntPtr hwnd, Win32GlFlavor flavor, out string failure)
    {
        if (hwnd == IntPtr.Zero)
        {
            failure = "no window";
            return null;
        }

        string name = Win32GlFlavorSelection.Name(flavor);
        var dc = NativeMethods.GetDC(hwnd);

        if (dc == IntPtr.Zero)
        {
            failure = $"{name}: no dc (error {NativeMethods.GetLastError()})";
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

            int format = flavor == Win32GlFlavor.Gdi32
                ? NativeMethods.ChoosePixelFormat(dc, ref descriptor)
                : NativeMethods.wglChoosePixelFormat(dc, ref descriptor);

            int chooseError = (int)NativeMethods.GetLastError();

            if (format == 0)
            {
                NativeMethods.ReleaseDC(hwnd, dc);
                failure = $"{name}: no pixel format (error {chooseError})";
                return null;
            }

            bool set = flavor == Win32GlFlavor.Gdi32
                ? NativeMethods.SetPixelFormat(dc, format, ref descriptor)
                : NativeMethods.wglSetPixelFormat(dc, format, ref descriptor);

            int setError = (int)NativeMethods.GetLastError();

            if (!set)
            {
                NativeMethods.ReleaseDC(hwnd, dc);
                failure = $"{name}: set pixel format failed (error {setError})";
                return null;
            }

            // A window's pixel format can be set once and never changed, which is why the whole GL
            // stack either comes up on this call or the window stays on the DIB path for its life.
            // The probe decides the flavour on throwaway windows precisely so this call is only
            // made on the real window with a flavour that already proved itself.
            var actual = new NativeMethods.PIXELFORMATDESCRIPTOR
            {
                nSize = (ushort)Marshal.SizeOf<NativeMethods.PIXELFORMATDESCRIPTOR>(),
            };

            int described = flavor == Win32GlFlavor.Gdi32
                ? NativeMethods.DescribePixelFormat(
                    dc,
                    format,
                    (uint)Marshal.SizeOf<NativeMethods.PIXELFORMATDESCRIPTOR>(),
                    ref actual)
                : NativeMethods.wglDescribePixelFormat(
                    dc,
                    format,
                    (uint)Marshal.SizeOf<NativeMethods.PIXELFORMATDESCRIPTOR>(),
                    ref actual);

            if (described == 0)
            {
                NativeMethods.ReleaseDC(hwnd, dc);
                failure = $"{name}: describe pixel format failed (error {NativeMethods.GetLastError()})";
                return null;
            }

            if ((actual.dwFlags & NativeMethods.PfdDoubleBuffer) == 0)
            {
                NativeMethods.ReleaseDC(hwnd, dc);
                failure = $"{name}: pixel format has no double buffer";
                return null;
            }

            context = NativeMethods.wglCreateContext(dc);

            if (context == IntPtr.Zero)
            {
                NativeMethods.ReleaseDC(hwnd, dc);
                failure = $"{name}: no wgl context (error {NativeMethods.GetLastError()})";
                return null;
            }

            if (!NativeMethods.wglMakeCurrent(dc, context))
            {
                int error = (int)NativeMethods.GetLastError();
                NativeMethods.wglDeleteContext(context);
                NativeMethods.ReleaseDC(hwnd, dc);
                failure = $"{name}: wglMakeCurrent failed (error {error})";
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

            Win32GlTrace.Write(
                $"{name}: format={format} colorBits={actual.cColorBits} " +
                $"stencilBits={Math.Max(stencilBits, 0)} samples={Math.Max(samples, 0)} " +
                $"renderer=\"{renderer}\"");

            failure = string.Empty;
            return new Win32GlContext(
                hwnd,
                dc,
                context,
                Math.Max(samples, 0),
                Math.Max(stencilBits, 0),
                renderer);
        }
        catch (Exception exception)
        {
            if (context != IntPtr.Zero)
            {
                NativeMethods.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
                NativeMethods.wglDeleteContext(context);
            }

            NativeMethods.ReleaseDC(hwnd, dc);
            failure = $"{name}: gl init threw {exception.GetType().Name}";
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

/// <summary>Outcome of a present attempt, so the window can fall back without guessing.</summary>
internal enum Win32GlPresentResult
{
    /// <summary>The frame reached the window and the buffers were swapped.</summary>
    Presented,

    /// <summary>The retained buffer failed; repaint once against the default framebuffer.</summary>
    RetryDirect,

    /// <summary>The context or the swap was lost; the window must fall back to the DIB.</summary>
    Lost,
}

/// <summary>
/// Skia's side of the seam: a <c>GRContext</c>, a render target for the window's default
/// framebuffer, a retained offscreen surface the control tree paints its damage into, and the blit
/// that presents it. Created by <see cref="Win32Gfx"/>, owned by the window, and presented with
/// <see cref="Present"/>.
/// </summary>
internal sealed class Win32GlSurface : IDisposable
{
    private readonly Win32GlContext _context;
    private GRGlInterface? _interface;
    private GRContext? _contextState;
    private GRBackendRenderTarget? _target;
    private SKSurface? _surface;
    private SKSurface? _offscreen;
    private int _width;
    private int _height;
    private bool _needsFull = true;
    private bool _offscreenUnavailable;

    private Win32GlSurface(Win32GlContext context) => _context = context;

    internal SKSurface? Surface => _offscreen ?? _surface;

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

    /// <summary>The vendor and renderer string, for the demo's one-line diagnostic.</summary>
    internal string Renderer => _context.Renderer;

    internal int Width => _width;

    internal int Height => _height;

    internal bool MakeCurrent() => _context.MakeCurrent();

    /// <summary>
    /// Validates that Skia can use this native context, without creating a window-sized surface.
    /// Used by the flavour probe on its throwaway window so a stack that has WGL but no usable GL
    /// interface never spends the real window's pixel format.
    /// </summary>
    internal static string? ProbeSkia(Win32GlContext context)
    {
        if (!context.MakeCurrent())
        {
            return "no current context";
        }

        GRGlInterface? glInterface = null;

        try
        {
            // The interface is the step that fails on a GDI Generic OpenGL 1.1 stack: it has a
            // context but none of the extensions Skia needs. Validating it here is enough to keep
            // the real window's one-shot format untouched on such a machine, and it avoids creating
            // a second GPU context just to throw it away.
            glInterface = GRGlInterface.Create();

            if (glInterface is null)
            {
                return "no gl interface";
            }

            return glInterface.Validate() ? null : "interface invalid";
        }
        catch (Exception exception)
        {
            return $"gl init threw {exception.GetType().Name}";
        }
        finally
        {
            glInterface?.Dispose();
        }
    }

    /// <summary>
    /// Builds the whole stack or nothing: the probe picks a flavour, then the interface, Skia
    /// context, render target and surface each can fail, and a caller that gets null must be able to
    /// stay on the CPU path with no GL state left behind.
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

        Win32GlFlavor? flavor;

        try
        {
            flavor = probe.SelectFlavor(out var flavorFailure);

            if (flavor is null)
            {
                reason = $"cpu ({flavorFailure})";
                return null;
            }

            Win32GlTrace.Write($"using {Win32GlFlavorSelection.Name(flavor.Value)} on the real window");
        }
        catch (Exception exception)
        {
            reason = $"cpu (gl probe threw {exception.GetType().Name})";
            return null;
        }

        Win32GlContext? nativeContext;

        try
        {
            nativeContext = probe.TryCreate(hwnd, flavor.Value, out var contextFailure);

            if (nativeContext is null)
            {
                reason = $"cpu ({contextFailure})";
                return null;
            }
        }
        catch (Exception exception)
        {
            reason = $"cpu (gl context threw {exception.GetType().Name})";
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

            if (glInterface is null)
            {
                reason = "cpu (no gl interface)";
                surface.Dispose();
                return null;
            }

            if (!glInterface.Validate())
            {
                glInterface.Dispose();
                reason = "cpu (interface invalid)";
                surface.Dispose();
                return null;
            }

            var grContext = GRContext.CreateGl(glInterface);

            if (grContext is null)
            {
                glInterface.Dispose();
                reason = "cpu (no gr context)";
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
        catch (Exception exception)
        {
            reason = $"cpu (gl init threw {exception.GetType().Name})";
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
            return "cpu (no gr context)";
        }

        if (_contextState.IsAbandoned)
        {
            return "cpu (gr context abandoned)";
        }

        if (!_context.MakeCurrent())
        {
            return "cpu (no current context)";
        }

        _surface?.Dispose();
        _surface = null;
        _target?.Dispose();
        _target = null;
        _offscreen?.Dispose();
        _offscreen = null;

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
            return $"cpu (no surface: {width}x{height}, {samples} samples, {_context.StencilBits} stencil)";
        }

        _target = target;
        _surface = surface;

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        if (_offscreenUnavailable)
        {
            _offscreen = null;
        }
        else
        {
            try
            {
                _offscreen = SKSurface.Create(
                    _contextState,
                    false,
                    info,
                    samples,
                    GRSurfaceOrigin.BottomLeft);
            }
            catch (Exception exception)
            {
                _offscreen = null;
                Win32GlTrace.Write($"offscreen surface threw {exception.GetType().Name}, presenting directly");
            }

            if (_offscreen is null)
            {
                // Do not keep trying on every resize: the tier is chosen for the life of the window.
                _offscreenUnavailable = true;
                Win32GlTrace.Write("offscreen surface unavailable, presenting directly");
            }
        }

        _width = width;
        _height = height;
        _needsFull = true;

        return null;
    }

    /// <summary>Presents the frame and swaps the back buffer onto the window.</summary>
    internal Win32GlPresentResult Present()
    {
        if (_contextState is null || _surface is null || !_context.MakeCurrent())
        {
            return Win32GlPresentResult.Lost;
        }

        if (_offscreen is not null)
        {
            try
            {
                // The offscreen buffer is the retained frame. Wrapping the window's own default
                // framebuffer in a surface keeps this a Skia blit: no GLSL to maintain per driver,
                // and no partial-present extension to require.
                using var image = _offscreen.Snapshot();

                if (image is null)
                {
                    DropRetainedBuffer();
                    return Win32GlPresentResult.RetryDirect;
                }

                _surface.Canvas.DrawImage(
                    image,
                    0f,
                    0f,
                    new SKSamplingOptions(SKFilterMode.Nearest),
                    null);

                _contextState.Flush();
            }
            catch (Exception exception)
            {
                Win32GlTrace.Write(
                    $"offscreen present threw {exception.GetType().Name}, dropping to direct");
                DropRetainedBuffer();
                return Win32GlPresentResult.RetryDirect;
            }
        }
        else
        {
            _contextState.Flush();
        }

        return _context.Present()
            ? Win32GlPresentResult.Presented
            : Win32GlPresentResult.Lost;
    }

    private void DropRetainedBuffer()
    {
        _offscreen?.Dispose();
        _offscreen = null;
        _offscreenUnavailable = true;
        _needsFull = true;
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
        _offscreen?.Dispose();
        _offscreen = null;
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
