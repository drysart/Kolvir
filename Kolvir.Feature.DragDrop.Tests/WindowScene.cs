using SkiaSharp;
using Kolvir;
using Kolvir.Backend.Headless;

namespace Kolvir.DragDrop.Tests;

/// <summary>
/// A real application, window and control tree on the headless backend, laid out at a known client
/// size, so a test can point the router at a control and know where the pointer landed.
/// </summary>
/// <remarks>
/// The window is deliberately not shown: layout, hit-testing and the host all work on an unshown
/// window, and <see cref="Window.Show"/> only does anything while the application is running.
/// A scene disposes the whole application, which disposes its windows with it.
/// </remarks>
internal sealed class WindowScene : IDisposable
{
    private WindowScene(Application application, Window window)
    {
        Application = application;
        Window = window;
    }

    /// <summary>The application, which allows one per process.</summary>
    public Application Application { get; }

    /// <summary>The window whose control tree the drag is routed into.</summary>
    public Window Window { get; }

    /// <summary>The host the router hit-tests through.</summary>
    public UiHost Host => Window.Host;

    /// <summary>Creates the application and a window around <paramref name="root"/>.</summary>
    /// <param name="root">The control tree.</param>
    /// <param name="width">Client width in logical pixels.</param>
    /// <param name="height">Client height in logical pixels.</param>
    public static WindowScene Create(Control? root = null, float width = 300f, float height = 200f)
    {
        var application = new Application(new HeadlessApplicationBackend());
        var window = new Window("drag", root) { ClientSize = new SKSize(width, height) };
        window.Host.PerformLayout();

        return new WindowScene(application, window);
    }

    /// <summary>Where <paramref name="control"/> starts, in host coordinates.</summary>
    public SKPoint OriginOf(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetPositionInHost();
    }

    /// <summary>The centre of <paramref name="control"/>, in host coordinates.</summary>
    public SKPoint CenterOf(Control control)
    {
        var origin = OriginOf(control);
        return new SKPoint(origin.X + (control.Width / 2f), origin.Y + (control.Height / 2f));
    }

    /// <summary>Where <paramref name="point"/>, given in host coordinates, sits inside <paramref name="control"/>.</summary>
    public SKPoint LocalPointIn(Control control, SKPoint point)
    {
        var origin = OriginOf(control);
        return new SKPoint(point.X - origin.X, point.Y - origin.Y);
    }

    /// <inheritdoc />
    public void Dispose() => Application.Dispose();
}
