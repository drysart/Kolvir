using Kolvir;
using Kolvir.Backend;
using SkiaSharp;

namespace Kolvir.Docs.Snippets;

/// <summary>The code docs/getting-started/first-window.md shows.</summary>
internal static class FirstWindow
{
    [STAThread]
    private static int Main()
    {
        using var application = new Application(KolvirBackend.CreateDefault());

        var window = new Window("Hello", new Label("Hello, Kolvir."))
        {
            ClientSize = new SKSize(480f, 240f),
        };

        application.MainWindow = window;
        window.Show();
        return application.Run();
    }
}
