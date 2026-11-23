using SkiaSharp;
using Kolvir;
using Kolvir.Backend.Headless;
using Kolvir.Feature.VirtualDom;

namespace Kolvir.Demo.VirtualDom;

/// <summary>
/// Runs the sample's real component on the headless backend and drives the open, close and rename
/// flows through the real controls and real frames.
/// </summary>
/// <remarks>
/// This exists because the interesting flows are otherwise only visible to a person with a pointer.
/// It creates the same <see cref="DocumentApplication"/>, finds buttons by their control id, clicks
/// them through <see cref="UiHost"/>, closes a document through the backend's close path, and paints
/// the frames that make the renders happen. The output is the model's own readout beside the actual
/// window titles.
/// </remarks>
internal static class HeadlessCheck
{
    internal static int Run()
    {
        var options = new ApplicationOptions
        {
            ApplicationName = "Kolvir Virtual DOM check",
#if WINDOWS
            AttachConsole = true,
#endif
        };

        using var application = new Application(new HeadlessApplicationBackend(), options);
        using var mount = VirtualDomRuntime.Mount(application, new DocumentApplication());

        var list = Find(application, "Kolvir - Documents");

        Check(application.Windows.Count == 2, "the first render should open the list and Document 1");
        Check(Readout(list).Contains("1 document window(s)", StringComparison.Ordinal), "the readout should start with one document");
        Console.WriteLine($"initial: {Titles(application)}");

        Click(list, "open");
        Paint(list);

        Check(application.Windows.Count == 3, "opening a document should add its window");
        var second = Find(application, "Document 2 - document");
        Check(Readout(list).Contains("2 document window(s)", StringComparison.Ordinal), "the readout should follow the model");
        Console.WriteLine($"after open: {Titles(application)}");

        second.Backend.Close();

        Check(!second.IsClosed, "a native close should be refused until a render omits the window");
        Check(application.Windows.Count == 3, "and the window should still be open");

        Paint(list);

        Check(second.IsClosed, "the render that omitted Document 2 should close its window");
        Check(application.Windows.Count == 2, "and only Document 1 should remain");
        Check(Readout(list).Contains("1 document window(s)", StringComparison.Ordinal), "the readout should follow the removal");
        Console.WriteLine($"after native close: {Titles(application)}");

        var first = Find(application, "Document 1 - document");
        Click(first, "rename");
        Paint(list);

        first = Find(application, "Document 1 renamed 1 - document");

        var title = first.TryGetControlById("title", out var control)
            ? control.Text
            : throw new InvalidOperationException("The document page should have a title label.");

        Check(title == "Document 1 renamed 1", $"the page should show the renamed title, was '{title}'");
        Check(Readout(list).Contains("Document 1 renamed 1", StringComparison.Ordinal), "the readout should show the renamed title");
        Console.WriteLine($"after rename: {Titles(application)}");
        Console.WriteLine($"readout: {Readout(list)}");

        var homeLocation = first.Location;
        var homeSize = first.ClientSize;
        var userLocation = new SKPoint(900f, 900f);

        // A user move goes through the backend, so the window's own notification is what the model
        // sees - exactly the path a drag takes on a desktop.
        first.Backend.Location = userLocation;
        Paint(list);

        first = Find(application, "Document 1 renamed 1 - document");
        Check(first.Location == userLocation, "the render should leave a window where the user moved it");
        Check(Readout(list).Contains("900,900", StringComparison.Ordinal), "the model should see the user's move");
        Console.WriteLine("after move: the model tracks the position and the window stayed put");

        var userSize = new SKSize(620f, 420f);
        first.Backend.ClientSize = userSize;
        Paint(list);

        first = Find(application, "Document 1 renamed 1 - document");
        Check(first.ClientSize == userSize, "the render should leave a window at the size the user gave it");
        Check(first.Backend.ClientSize == userSize, "the resize should reach the backend");
        Check(Readout(list).Contains("620x420", StringComparison.Ordinal), "the model should see the user's resize");
        Check(first.Location == userLocation, "resizing should not move the window");
        Console.WriteLine("after resize: the model tracks the size and the window stayed put");

        first.Backend.State = WindowState.Minimized;
        Paint(list);

        first = Find(application, "Document 1 renamed 1 - document");
        Check(first.Backend.State == WindowState.Minimized, "the user's minimize should survive the render");
        Check(Readout(list).Contains("Minimized", StringComparison.Ordinal), "the model should see the state change");
        Check(first.Location == userLocation && first.ClientSize == userSize, "minimizing should not change the geometry");

        first.Backend.State = WindowState.Normal;
        Paint(list);

        first = Find(application, "Document 1 renamed 1 - document");
        Check(first.Backend.State == WindowState.Normal, "the user's restore should survive the render");
        Check(Readout(list).Contains("Normal", StringComparison.Ordinal), "the model should see the restore");
        Console.WriteLine("after minimize and restore: the model tracked both states");

        Click(list, "tidy");
        Paint(list);

        first = Find(application, "Document 1 renamed 1 - document");
        Check(first.Location == homeLocation, "Tidy should put the document back where the model assigned it");
        Check(first.ClientSize == homeSize, "Tidy should put the document back at the size the model assigned");
        Check(first.Backend.State == WindowState.Normal, "Tidy should restore the model's window state");
        Check(Readout(list).Contains("160,60 480x300 Normal", StringComparison.Ordinal), "the readout should follow the tidy");
        Console.WriteLine("after tidy: the moved document returned to its assigned position and size");

        Click(list, "quit");

        Check(application.Windows.Count == 0, "Quit should close every window");
        Console.WriteLine("after quit: no open windows");
        Console.WriteLine("check: OK");

        return 0;
    }

    private static Window Find(Application application, string title) =>
        application.Windows.FirstOrDefault(window =>
            !window.IsClosed && window.Title.Contains(title, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"No open window contains '{title}'.");

    private static string Readout(Window window) =>
        window.TryGetControlById("readout", out var control)
            ? control.Text
            : throw new InvalidOperationException("The list window should have a readout label.");

    private static string Titles(Application application) =>
        string.Join(", ", application.Windows.Where(window => !window.IsClosed).Select(window => window.Title));

    private static void Click(Window window, string id)
    {
        window.Host.PerformLayout();

        if (!window.TryGetControlById(id, out var control))
        {
            throw new InvalidOperationException($"The window does not contain a control with id '{id}'.");
        }

        var origin = control.GetPositionInHost();
        var point = new SKPoint(origin.X + (control.Width / 2f), origin.Y + (control.Height / 2f));

        window.Host.ProcessMouseMove(point);
        window.Host.ProcessMouseDown(point, MouseButtons.Left, Modifiers.None);
        window.Host.ProcessMouseUp(point, MouseButtons.Left, Modifiers.None);
    }

    private static void Paint(Window window)
    {
        window.Host.PerformLayout();

        using var surface = SKSurface.Create(new SKImageInfo(
            Math.Max(1, (int)MathF.Round(window.ClientSize.Width)),
            Math.Max(1, (int)MathF.Round(window.ClientSize.Height))));

        window.Host.Paint(surface.Canvas);

        // A close the frame asked for is deferred until no paint is in progress. The headless
        // backend has no message loop to turn, so the harness turns it explicitly: this is the
        // turn that would follow the frame on a desktop.
        if (window.Application.Backend is HeadlessApplicationBackend backend)
        {
            backend.DrainPending();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"check failed: {message}");
        }
    }
}
