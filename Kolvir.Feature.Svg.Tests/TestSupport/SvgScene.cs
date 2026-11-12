using SkiaSharp;
using Kolvir.Backend.Headless;

namespace Kolvir.Svg.Tests;

/// <summary>
/// A real application, window and control tree on the headless backend, so a test can lay a
/// control out and read back the pixels it painted.
/// </summary>
/// <remarks>
/// The SVG feature's interesting behaviour is mostly paint behaviour - how a picture scales, where a
/// document's own bounds put it, what colour a recoloured icon comes out - and none of that is
/// visible without a layout pass and a raster. This is the smallest thing that provides both: the
/// core suite has a larger one, and a test project may not reference it.
/// </remarks>
internal sealed class SvgScene : IDisposable
{
    private readonly Application _application;

    private SvgScene(Application application, Window window, HeadlessWindowBackend backend)
    {
        _application = application;
        Window = window;
        Backend = backend;
    }

    internal Window Window { get; }

    internal HeadlessWindowBackend Backend { get; }

    internal static SvgScene Create(
        Control? root,
        float width,
        float height,
        Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? configureServices = null)
    {
        var application = new Application(
            new HeadlessApplicationBackend(),
            new ApplicationOptions { ConfigureServices = configureServices });
        var window = new Window("Kolvir svg test", root) { ClientSize = new SKSize(width, height) };
        var backend = (HeadlessWindowBackend)window.Backend;
        window.Host.PerformLayout();
        return new SvgScene(application, window, backend);
    }

    internal SKBitmap Render()
    {
        var info = new SKImageInfo(
            Math.Max(1, (int)MathF.Round(Backend.ClientSize.Width)),
            Math.Max(1, (int)MathF.Round(Backend.ClientSize.Height)),
            SKColorType.Bgra8888,
            SKAlphaType.Premul);

        var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            Backend.RenderTo(canvas);
            canvas.Flush();
        }

        return bitmap;
    }

    internal SKColor PixelAt(int x, int y)
    {
        using var bitmap = Render();
        return bitmap.GetPixel(
            Math.Clamp(x, 0, bitmap.Width - 1),
            Math.Clamp(y, 0, bitmap.Height - 1));
    }

    public void Dispose()
    {
        Window.Close();
        _application.Dispose();
    }
}
