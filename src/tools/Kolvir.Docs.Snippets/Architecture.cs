using Kolvir;
using Kolvir.Feature.Animations;
using Kolvir.Backend;
using Kolvir.Feature.DragDrop;
using SkiaSharp;

namespace Kolvir.Docs.Snippets;

/// <summary>The code docs/concepts/architecture.md shows.</summary>
internal static class Architecture
{
    private static void Start()
    {
        using var application = new Application(KolvirBackend.CreateDefault(), new ApplicationOptions
        {
            ApplicationName = "Notes",
            ConfigureServices = services => services.AddAnimations().AddDragDrop(),
        });
    }

    private static Window Build()
    {
        var save = new Button("Save") { Dock = DockStyle.Bottom, Height = 40f };

        var window = new Window("Notes", new StackPanel
        {
            Padding = new Thickness(16f),
            Spacing = 8f,
            Controls =
            {
                new Label("Nothing to save yet.") { WrapText = true },
                save,
            },
        })
        {
            ClientSize = new SKSize(640f, 420f),
        };

        save.AddClickHandler(_ => Console.WriteLine("saved"));
        return window;
    }

    private static void UseAService(Window window)
    {
        var clock = window.Services.GetService(typeof(IClock)) as IClock;
        Console.WriteLine(clock is null ? "no clock registered" : "clock ready");
    }

    private static void Unsubscribe(Button save)
    {
        IDisposable subscription = save.AddClickHandler(_ => Console.WriteLine("saved again"));
        subscription.Dispose();
    }

    private static void AskWhatIsSupported(Window window)
    {
        if (window.ChromeCapabilities.Borderless)
        {
            window.Chrome = WindowChrome.Borderless;
            window.CaptionButtons = CaptionButtons.Platform;
        }
    }

    private interface IClock
    {
    }
}
