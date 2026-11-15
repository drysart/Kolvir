using SkiaSharp;
using Kolvir;
using Kolvir.Backend.Headless;

namespace Kolvir.VirtualDom.Tests;

/// <summary>
/// A real application, window and control tree on the headless backend, laid out at a known client
/// size, so a mounted tree can be asserted on with layout having actually run.
/// </summary>
/// <remarks>
/// The window is deliberately not shown: layout, hit testing and the host all work on an unshown
/// window. A scene disposes the whole application, which disposes its window with it.
/// </remarks>
internal sealed class Scene : IDisposable
{
    private Scene(Application application, Window window)
    {
        Application = application;
        Window = window;
    }

    /// <summary>The application, which allows one per process.</summary>
    public Application Application { get; }

    /// <summary>The window whose control tree the mounted nodes live in.</summary>
    public Window Window { get; }

    /// <summary>The host the layout runs through.</summary>
    public UiHost Host => Window.Host;

    /// <summary>Creates an application and a window around <paramref name="root"/>.</summary>
    /// <param name="root">The control tree to lay out.</param>
    /// <param name="width">Client width in logical pixels.</param>
    /// <param name="height">Client height in logical pixels.</param>
    public static Scene Create(Control root, float width = 300f, float height = 200f)
    {
        var application = new Application(new HeadlessApplicationBackend());
        var window = new Window("virtualdom", root) { ClientSize = new SKSize(width, height) };
        window.Host.PerformLayout();
        return new Scene(application, window);
    }

    /// <summary>Moves to, presses and releases at the centre of <paramref name="control"/>.</summary>
    /// <param name="control">The control to click.</param>
    public void Click(Control control)
    {
        var origin = control.GetPositionInHost();
        var point = new SKPoint(origin.X + (control.Width / 2f), origin.Y + (control.Height / 2f));

        Host.ProcessMouseMove(point);
        Host.ProcessMouseDown(point, MouseButtons.Left, Modifiers.None);
        Host.ProcessMouseUp(point, MouseButtons.Left, Modifiers.None);
    }

    /// <summary>Runs the layout pass again, which is what a test does after changing a tree.</summary>
    public void Layout() => Host.PerformLayout();

    /// <inheritdoc />
    public void Dispose() => Application.Dispose();
}
