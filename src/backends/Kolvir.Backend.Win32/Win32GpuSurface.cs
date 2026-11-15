using SkiaSharp;

namespace Kolvir.Backend.Win32;

/// <summary>How a present ended, for whichever GPU tier served it.</summary>
internal enum Win32PresentResult
{
    /// <summary>The frame reached the window.</summary>
    Presented,

    /// <summary>The retained buffer failed; repaint once against the frame target and present again.</summary>
    RetryDirect,

    /// <summary>The context or the swap chain was lost; the window must fall back to its DIB.</summary>
    Lost,
}

/// <summary>
/// The window's drawing seam: whichever GPU tier is in use, it hands the host a Skia surface to paint
/// into and turns what was painted into a frame on screen.
/// </summary>
/// <remarks>
/// The two implementations answer the same questions differently. The GL tier wraps the window's
/// default framebuffer and keeps a retained offscreen buffer that it blits in; the Vulkan tier has no
/// default framebuffer to wrap, so it renders into a retained GPU texture and draws that into an
/// image the swap chain handed it. The host does not know which is in front of it, which is what lets
/// a window fall from one to the other without changing anything above this line.
/// </remarks>
internal interface IWin32GpuSurface : IDisposable
{
    /// <summary>What the host paints into: the retained buffer when there is one.</summary>
    SKSurface? Surface { get; }

    /// <summary>True while the retained buffer is the paint target.</summary>
    bool HasRetainedBuffer { get; }

    /// <summary>True when the retained contents are undefined and a full paint is owed.</summary>
    bool NeedsFullRepaint { get; }

    /// <summary>Consumes the full-repaint flag; the frame that asks is the one that clears it.</summary>
    bool TakeNeedsFull();

    /// <summary>The tier's name, which is also the word its diagnostic line starts with.</summary>
    string Tier { get; }

    /// <summary>The adapter this tier is drawing on, for the window's diagnostic line.</summary>
    string Renderer { get; }

    /// <summary>Binds the drawing context to the calling (UI) thread, where the tier needs that.</summary>
    bool MakeCurrent();

    /// <summary>Resizes the drawing surface; false means the tier must be dropped.</summary>
    bool Resize(int width, int height);

    /// <summary>Puts what was painted on screen.</summary>
    Win32PresentResult Present();

    /// <summary>Drops the retained buffer so the next frame paints against the frame target.</summary>
    void DropRetainedBuffer();
}
