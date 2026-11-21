using Kolvir;
using Kolvir.Backend.Headless;
using SkiaSharp;

namespace Kolvir.Docs.Snippets;

/// <summary>The code docs/getting-started/run-on-each-platform.md shows.</summary>
internal static class HeadlessRender
{
    private static void Render()
    {
        var backend = new HeadlessApplicationBackend();
        using var application = new Application(backend);

        var window = new Window("Hello", new Label("Hello, Kolvir."))
        {
            ClientSize = new SKSize(480f, 240f),
        };

        window.Show();
        application.Run();
        backend.LastWindow!.SavePng("hello.png");
    }
}
