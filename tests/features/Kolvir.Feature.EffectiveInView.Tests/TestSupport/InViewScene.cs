using Kolvir.Backend.Headless;
using SkiaSharp;

namespace Kolvir.Feature.InView.Tests;

/// <summary>
/// A real application, a real window and a real control tree on the headless backend: the starting
/// point for every test that needs layout or a painted frame.
/// </summary>
/// <remarks>
/// The core suite's own scene helper is internal to <c>Kolvir.Tests</c>, which this assembly cannot
/// see, so this is the same idea in miniature: create the application and window, lay the tree out,
/// paint on demand, and dispose both. Nothing here reaches into the library; layout is
/// <see cref="UiHost.PerformLayout"/> and painting is <see cref="UiHost.Paint"/>, exactly what a
/// backend calls.
/// </remarks>
internal sealed class InViewScene : IDisposable
{
    private readonly Application _application;

    /// <summary>Creates the scene and lays the tree out once.</summary>
    /// <param name="root">The tree, or null for an empty window.</param>
    /// <param name="width">Client width in logical pixels.</param>
    /// <param name="height">Client height in logical pixels.</param>
    public InViewScene(Control? root = null, float width = 400f, float height = 300f)
    {
        _application = new Application(new HeadlessApplicationBackend());
        Window = new Window("effective in view", root) { ClientSize = new SKSize(width, height) };
        Host = Window.Host;
        ClientSize = new SKSize(width, height);

        Host.PerformLayout();
    }

    /// <summary>The window the tree is shown in.</summary>
    public Window Window { get; }

    /// <summary>The host that owns the tree.</summary>
    public UiHost Host { get; }

    /// <summary>The client size the tree was laid out at.</summary>
    public SKSize ClientSize { get; }

    /// <summary>Runs another layout pass, as a frame would.</summary>
    public void Layout() => Host.PerformLayout();

    /// <summary>Paints a frame, so the paint path is exercised and nothing there throws.</summary>
    public void Paint()
    {
        var info = new SKImageInfo(
            Math.Max(1, (int)MathF.Round(ClientSize.Width)),
            Math.Max(1, (int)MathF.Round(ClientSize.Height)),
            SKColorType.Bgra8888,
            SKAlphaType.Premul);

        using var surface = SKSurface.Create(info);
        Host.Paint(surface.Canvas);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Window.Close();
        _application.Dispose();
    }
}
