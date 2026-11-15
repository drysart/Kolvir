using CoreAnimation;
using CoreGraphics;
using Metal;
using SkiaSharp;

namespace Kolvir.Backend.AppKit;

/// <summary>
/// The Metal half of the AppKit backend's drawing seam. A <see cref="CAMetalLayer"/> owns the
/// drawable, <see cref="GRContext"/> is the Skia context created on the same device and queue, and
/// <see cref="AppKitMetalSurface"/> is the retained offscreen surface plus the blit that presents
/// it.
/// </summary>
/// <remarks>
/// <para>
/// The shape mirrors the Win32 WGL seam: the backend decides once per window whether the
/// accelerated path can come up, every step of that decision is named, and any failure leaves the
/// window on its existing CPU path with no Metal state behind. Unlike Windows there is no pixel
/// format to spend and no second native library: SkiaSharp's macOS <c>libSkiaSharp.dylib</c>
/// already contains the Metal backend, and <see cref="GRMtlBackendContext"/> is the whole bridge.
/// </para>
/// <para>
/// All of this runs on the UI thread. The layer, the drawable, the paint and the commit all happen
/// from <c>UpdateLayer</c>, which AppKit calls on the main thread; nothing is scheduled on a timer
/// or a worker.
/// </para>
/// </remarks>
internal static class AppKitMetalTrace
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("KOLVIR_METAL_TRACE") is "1" or "true" or "TRUE";

    /// <summary>Lets a hot path skip formatting a message nobody will read.</summary>
    internal static bool IsEnabled => Enabled;

    /// <summary>
    /// Writes one probe line to standard error. The AppKit backend already writes its failures
    /// through <see cref="Console.Error"/>, so this adds nothing to an AOT image.
    /// </summary>
    internal static void Write(string message)
    {
        if (Enabled)
        {
            Console.Error.WriteLine($"[kolvir-metal] {message}");
        }
    }
}

/// <summary>Outcome of a present attempt, so the view can fall back without guessing.</summary>
internal enum AppKitMetalPresentResult
{
    /// <summary>The frame reached the drawable and was submitted for presentation.</summary>
    Presented,

    /// <summary>The retained buffer failed; repaint once against a fresh drawable.</summary>
    RetryDirect,

    /// <summary>The context or the drawable is gone; the view must fall back to CPU.</summary>
    Lost,

    /// <summary>No drawable was available; the frame is skipped without spinning or throwing.</summary>
    Skipped,
}

/// <summary>
/// Skia's side of the Metal seam: a <c>GRContext</c>, a <see cref="CAMetalLayer"/> whose drawable
/// is the window, a retained offscreen surface the control tree paints its damage into, and the
/// blit that presents it.
/// </summary>
internal sealed class AppKitMetalSurface : IDisposable
{
    private readonly IMTLDevice _device;
    private readonly IMTLCommandQueue _queue;
    private readonly GRMtlBackendContext _backendContext;
    private readonly CGColorSpace? _colorSpace;
    private GRContext? _context;
    private CAMetalLayer? _layer;
    private SKSurface? _offscreen;
    private int _width;
    private int _height;
    private bool _needsFull = true;
    private bool _offscreenUnavailable;
    private bool _disposed;

    private AppKitMetalSurface(
        IMTLDevice device,
        IMTLCommandQueue queue,
        GRMtlBackendContext backendContext,
        GRContext context,
        CAMetalLayer layer,
        CGColorSpace? colorSpace)
    {
        _device = device;
        _queue = queue;
        _backendContext = backendContext;
        _context = context;
        _layer = layer;
        _colorSpace = colorSpace;

        // The layer is above the view's own backing layer, so the CPU path can keep using
        // layer.Contents untouched and this path simply hides when it is not live. It stays hidden
        // until the first drawable has been committed, so the window never shows an empty opaque
        // layer where the CPU path would have shown its first frame.
        layer.Hidden = true;
    }

    /// <summary>The layer that presents the drawables. Owned by the surface.</summary>
    internal CAMetalLayer? Layer => _layer;

    /// <summary>The Metal device's name, for the demo's one-line diagnostic.</summary>
    internal string Renderer => _device.Name ?? "metal";

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
    /// Builds the whole stack or nothing: the device, the command queue, the layer, the Skia
    /// context and the first offscreen surface each can fail, and a caller that gets null must be
    /// able to stay on the CPU path with no Metal state left behind.
    /// </summary>
    /// <param name="hostLayer">The view's backing layer, which receives the Metal sublayer.</param>
    /// <param name="frame">The view's bounds in logical points, which sizes the sublayer.</param>
    /// <param name="width">The drawable width in physical pixels.</param>
    /// <param name="height">The drawable height in physical pixels.</param>
    /// <param name="scale">The backing scale, so the sublayer covers the same logical area.</param>
    /// <param name="reason">A one-line diagnostic naming the path taken or the failing step.</param>
    internal static AppKitMetalSurface? TryCreate(
        CALayer? hostLayer,
        CGRect frame,
        int width,
        int height,
        int scale,
        out string reason)
    {
        if (hostLayer is null)
        {
            reason = "cpu (no host layer)";
            return null;
        }

        IMTLDevice? device;

        try
        {
            device = MTLDevice.SystemDefault;
        }
        catch (Exception exception)
        {
            reason = $"cpu (metal device threw {exception.GetType().Name})";
            return null;
        }

        if (device is null)
        {
            reason = "cpu (no metal device)";
            return null;
        }

        AppKitMetalTrace.Write($"device: {device.Name ?? "unknown"}");

        IMTLCommandQueue? queue;

        try
        {
            queue = device.CreateCommandQueue();
        }
        catch (Exception exception)
        {
            reason = $"cpu (command queue threw {exception.GetType().Name})";
            return null;
        }

        if (queue is null)
        {
            reason = "cpu (no command queue)";
            return null;
        }

        AppKitMetalTrace.Write("command queue: created");

        CGColorSpace? colorSpace = null;
        CAMetalLayer? layer = null;
        GRContext? context = null;

        try
        {
            // The CPU path presents a device-RGB CGImage, so the layer asks for the same space. A
            // mismatch here is the colour shift the acceptance run compares for.
            colorSpace = CGColorSpace.CreateDeviceRGB();

            layer = new CAMetalLayer
            {
                Device = device,
                PixelFormat = MTLPixelFormat.BGRA8Unorm,
                FramebufferOnly = false,
                Opaque = true,
                MasksToBounds = true,
                Frame = frame,
                ContentsScale = scale,
                ColorSpace = colorSpace,
            };

            layer.AutoresizingMask = CAAutoresizingMask.WidthSizable | CAAutoresizingMask.HeightSizable;
            hostLayer.AddSublayer(layer);

            var backendContext = new GRMtlBackendContext
            {
                Device = device,
                Queue = queue,
            };

            context = GRContext.CreateMetal(backendContext);

            if (context is null)
            {
                reason = "cpu (no gr context)";
                layer.RemoveFromSuperLayer();
                layer.Dispose();
                colorSpace?.Dispose();
                return null;
            }

            AppKitMetalTrace.Write("gr context: created");

            var surface = new AppKitMetalSurface(device, queue, backendContext, context, layer, colorSpace);
            context = null;
            layer = null;
            colorSpace = null;

            if (!surface.EnsureSize(width, height, scale))
            {
                reason = $"cpu (no metal surface: {width}x{height})";
                surface.Dispose();
                return null;
            }

            reason = "metal";
            return surface;
        }
        catch (Exception exception)
        {
            reason = $"cpu (metal init threw {exception.GetType().Name})";
            context?.Dispose();
            layer?.RemoveFromSuperLayer();
            layer?.Dispose();
            colorSpace?.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Recreates the drawable size and, on the retained tier, the offscreen surface. False means
    /// the drawable reports an impossible size and the view must fall back to CPU.
    /// </summary>
    internal bool EnsureSize(int width, int height, int scale)
    {
        if (_disposed || width <= 0 || height <= 0)
        {
            return false;
        }

        if (_layer is null)
        {
            return false;
        }

        if (_offscreen is not null && _width == width && _height == height)
        {
            return true;
        }

        _layer.DrawableSize = new CGSize(width, height);
        _layer.ContentsScale = scale;

        AppKitMetalTrace.Write(
            $"drawable {width}x{height} at {scale}x, retained={(!_offscreenUnavailable ? "yes" : "no")}");

        // A new drawable size invalidates everything the retained buffer held, and so does a newly
        // created one; either way the next paint is full.
        _needsFull = true;
        _width = width;
        _height = height;

        if (_offscreenUnavailable)
        {
            return true;
        }

        _offscreen?.Dispose();
        _offscreen = null;

        if (_context is null || _context.IsAbandoned)
        {
            return true;
        }

        try
        {
            _offscreen = SKSurface.Create(
                _context,
                false,
                new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul),
                1,
                GRSurfaceOrigin.TopLeft);
        }
        catch (Exception exception)
        {
            _offscreen = null;
            AppKitMetalTrace.Write($"offscreen surface threw {exception.GetType().Name}, presenting directly");
        }

        if (_offscreen is null)
        {
            // Do not keep trying on every resize: the tier is chosen for the life of the window.
            _offscreenUnavailable = true;
            AppKitMetalTrace.Write("offscreen surface unavailable, presenting directly");
        }

        return true;
    }

    /// <summary>
    /// Blits the retained offscreen buffer onto a fresh drawable and presents it. The tree is not
    /// repainted here - <see cref="Surface"/> already holds the frame.
    /// </summary>
    internal AppKitMetalPresentResult PresentRetained()
    {
        if (_context is null || _context.IsAbandoned || _layer is null)
        {
            return AppKitMetalPresentResult.Lost;
        }

        if (_offscreen is null)
        {
            return AppKitMetalPresentResult.RetryDirect;
        }

        var drawable = _layer.NextDrawable();

        if (drawable is null)
        {
            // No drawable right now. The retained buffer keeps the damage, so the next frame
            // presents it; spinning here would only burn the UI thread.
            AppKitMetalTrace.Write("present skipped: no drawable");
            return AppKitMetalPresentResult.Skipped;
        }

        try
        {
            var info = new GRMtlTextureInfo(drawable.Texture);
            using var target = new GRBackendRenderTarget(_width, _height, info);

            if (!target.IsValid)
            {
                AppKitMetalTrace.Write("present failed: invalid drawable target");
                DropRetainedBuffer();
                return AppKitMetalPresentResult.RetryDirect;
            }

            using var surface = SKSurface.Create(
                _context,
                target,
                GRSurfaceOrigin.TopLeft,
                SKColorType.Bgra8888);

            if (surface is null)
            {
                AppKitMetalTrace.Write("present failed: no drawable surface");
                DropRetainedBuffer();
                return AppKitMetalPresentResult.RetryDirect;
            }

            using var image = _offscreen.Snapshot();

            if (image is null)
            {
                AppKitMetalTrace.Write("present failed: no snapshot");
                DropRetainedBuffer();
                return AppKitMetalPresentResult.RetryDirect;
            }

            surface.Canvas.DrawImage(image, 0f, 0f, new SKSamplingOptions(SKFilterMode.Nearest), null);
            surface.Canvas.Flush();
            surface.Flush();
            _context.Flush();
        }
        catch (Exception exception)
        {
            AppKitMetalTrace.Write($"offscreen present threw {exception.GetType().Name}, dropping to direct");
            DropRetainedBuffer();
            return AppKitMetalPresentResult.RetryDirect;
        }

        return Present(drawable);
    }

    /// <summary>
    /// Paints a complete frame straight into a freshly acquired drawable. Only the direct tier uses
    /// this: a drawable's contents are undefined after present, so a partial paint would leave the
    /// rest of the window garbage.
    /// </summary>
    /// <param name="paint">Paints a full frame into the surface; called at most once.</param>
    internal AppKitMetalPresentResult PresentDirect(Action<SKSurface> paint)
    {
        ArgumentNullException.ThrowIfNull(paint);

        if (_context is null || _context.IsAbandoned || _layer is null)
        {
            return AppKitMetalPresentResult.Lost;
        }

        var drawable = _layer.NextDrawable();

        if (drawable is null)
        {
            AppKitMetalTrace.Write("direct present skipped: no drawable");
            return AppKitMetalPresentResult.Skipped;
        }

        try
        {
            var info = new GRMtlTextureInfo(drawable.Texture);
            using var target = new GRBackendRenderTarget(_width, _height, info);

            if (!target.IsValid)
            {
                AppKitMetalTrace.Write("direct present failed: invalid drawable target");
                return AppKitMetalPresentResult.Lost;
            }

            using var surface = SKSurface.Create(
                _context,
                target,
                GRSurfaceOrigin.TopLeft,
                SKColorType.Bgra8888);

            if (surface is null)
            {
                AppKitMetalTrace.Write("direct present failed: no drawable surface");
                return AppKitMetalPresentResult.Lost;
            }

            paint(surface);
            surface.Canvas.Flush();
            surface.Flush();
            _context.Flush();
        }
        catch (Exception exception)
        {
            AppKitMetalTrace.Write($"direct present threw {exception.GetType().Name}");
            return AppKitMetalPresentResult.Lost;
        }

        return Present(drawable);
    }

    /// <summary>Submits the drawable on the queue. The commit is what vsyncs the frame.</summary>
    private AppKitMetalPresentResult Present(ICAMetalDrawable drawable)
    {
        try
        {
            using var commandBuffer = _queue.CommandBuffer();

            if (commandBuffer is null)
            {
                AppKitMetalTrace.Write("present failed: no command buffer");
                return AppKitMetalPresentResult.Lost;
            }

            commandBuffer.PresentDrawable(drawable);
            commandBuffer.Commit();

            // The first committed drawable is what makes the layer worth showing; until then the
            // view's own (CPU) layer is what the user sees.
            if (_layer is not null)
            {
                _layer.Hidden = false;
            }

            return AppKitMetalPresentResult.Presented;
        }
        catch (Exception exception)
        {
            AppKitMetalTrace.Write($"present threw {exception.GetType().Name}");
            return AppKitMetalPresentResult.Lost;
        }
    }

    /// <summary>
    /// Drops the retained buffer and keeps the direct tier for the rest of the window's life. This
    /// is the one-tier demotion the Win32 backend calls <c>RetryDirect</c>.
    /// </summary>
    internal void DropRetainedBuffer()
    {
        _offscreen?.Dispose();
        _offscreen = null;
        _offscreenUnavailable = true;
        _needsFull = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _context?.Flush();
        }
        catch (Exception)
        {
        }

        _offscreen?.Dispose();
        _offscreen = null;
        _context?.Dispose();
        _context = null;

        if (_layer is not null)
        {
            _layer.Hidden = true;
            _layer.RemoveFromSuperLayer();
            _layer.Dispose();
            _layer = null;
        }

        _colorSpace?.Dispose();
        _queue.Dispose();
        _backendContext.Dispose();
    }
}
