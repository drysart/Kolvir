using SkiaSharp;
using Kolvir;
using Kolvir.Backend.Headless;

namespace Kolvir.VirtualDom.Tests;

/// <summary>
/// A real application with an application mount on the headless backend, so a window set can be
/// asserted on with real <see cref="Window"/> instances and real frames.
/// </summary>
/// <remarks>
/// The headless backend already owns a genuine <see cref="UiHost"/> for each window, which is the
/// same seam the control suite uses. Nothing here fakes a window manager: a window is created,
/// shown, painted and closed by the library's own code, and the test reads the results.
/// </remarks>
internal sealed class ApplicationScene : IDisposable
{
    private readonly Application _application;
    private readonly ApplicationMount _mount;

    private ApplicationScene(Application application, ApplicationMount mount)
    {
        _application = application;
        _mount = mount;
    }

    /// <summary>The application whose window list the mount maintains.</summary>
    public Application Application => _application;

    /// <summary>The mounted component's window handle.</summary>
    public ApplicationMount Mount => _mount;

    /// <summary>The application's open windows, in the order they were shown.</summary>
    public IReadOnlyList<Window> Windows => _application.Windows;

    /// <summary>Creates an application and mounts <paramref name="component"/> into it.</summary>
    /// <param name="component">The application component to render.</param>
    public static ApplicationScene Create(IApplicationComponent component)
    {
        var application = new Application(new HeadlessApplicationBackend());
        var mount = VirtualDomRuntime.Mount(application, component);

        return new ApplicationScene(application, mount);
    }

    /// <summary>
    /// Paints one real frame, which is what drains both render queues.
    /// </summary>
    /// <remarks>
    /// The frame is the moment deferred work happens, so a test draws one between changing state and
    /// asserting. One host is enough because the application queue is process-wide; painting every
    /// window would only repeat an empty drain. The headless backend has no message loop, so the
    /// turn that follows the frame - where a close asked for during the paint completes - is drained
    /// explicitly rather than left for a pump that never runs.
    /// </remarks>
    public void Frame()
    {
        var window = Windows.FirstOrDefault(window => !window.IsClosed)
            ?? throw new InvalidOperationException("A frame needs at least one open window.");

        using var surface = SKSurface.Create(new SKImageInfo(
            Math.Max(1, (int)MathF.Round(window.ClientSize.Width)),
            Math.Max(1, (int)MathF.Round(window.ClientSize.Height))));

        window.Host.Paint(surface.Canvas);

        if (_application.Backend is HeadlessApplicationBackend backend)
        {
            backend.DrainPending();
        }
    }

    /// <summary>Finds the open window whose title is <paramref name="title"/>.</summary>
    public Window Window(string title) =>
        Windows.Single(window => !window.IsClosed && string.Equals(window.Title, title, StringComparison.Ordinal));

    /// <summary>The panel that is <paramref name="window"/>'s content.</summary>
    public Panel Content(Window window) =>
        window.Content as Panel
            ?? throw new InvalidOperationException("An application-mounted window should have a host panel as its content.");

    /// <inheritdoc />
    public void Dispose()
    {
        _mount.Dispose();
        _application.Dispose();
    }
}
