using SkiaSharp;
using Kolvir;
using Kolvir.Backend.Headless;

namespace Kolvir.Tests;

/// <summary>
/// A real application, a real window and a real control tree on the headless backend: the thing
/// every test that needs layout, painting or input starts from.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately thin. <see cref="HeadlessWindowBackend"/> already owns a genuine
/// <see cref="UiHost"/>, so the scene only has to create the application and window, show it,
/// and let queued work run. Input goes through the host's own entry points - the same ones the Win32,
/// AppKit and GTK4 backends call - which is what makes these tests describe the library rather than
/// the test harness.
/// </para>
/// <para>
/// A scene is short-lived: create it, assert, dispose it. Disposing the application closes and
/// disposes every window it owns, so a test that needs two windows makes two scenes or opens a
/// second window through <see cref="Application"/>.
/// </para>
/// </remarks>
internal sealed class HeadlessScene : IDisposable
{
    private readonly Application _application;

    private HeadlessScene(
        Application application,
        IApplicationBackend backend,
        Window window,
        HeadlessWindowBackend windowBackend)
    {
        _application = application;
        Backend = backend;
        Window = window;
        WindowBackend = windowBackend;
    }

    /// <summary>
    /// The application backend. Usually <see cref="HeadlessApplicationBackend"/>; a test that needs
    /// to own a timer passes its own wrapper (see <see cref="TestApplicationBackend"/>).
    /// </summary>
    public IApplicationBackend Backend { get; }

    /// <summary>The window model.</summary>
    public Window Window { get; }

    /// <summary>The window backend, which renders and simulates client-size and scale changes.</summary>
    public HeadlessWindowBackend WindowBackend { get; }

    /// <summary>The host the window paints and routes input through.</summary>
    public UiHost Host => WindowBackend.Host;

    /// <summary>The window's client size.</summary>
    public SKSize ClientSize => WindowBackend.ClientSize;

    /// <summary>The application, for tests that open a second window or raise application events.</summary>
    public Application Application => _application;

    /// <summary>
    /// Creates the application and one window around <paramref name="root"/> and lays the tree out at
    /// the requested client size.
    /// </summary>
    /// <remarks>
    /// The window is deliberately not shown. <see cref="Window.Show"/> only does anything while
    /// the application is running, and <see cref="Application.Run"/> tears the application down
    /// when it returns, so a test that needs the shown lifecycle uses the raw pattern the demo
    /// harnesses used: add a shown handler, call <see cref="Window.Show"/>, then call
    /// <see cref="Application.Run"/> and assert from inside that handler. Everything else -
    /// layout, painting, input, focus - works on an unshown window, because those are host
    /// responsibilities rather than window-manager ones.
    /// </remarks>
    /// <param name="root">The control tree, or null for an empty window.</param>
    /// <param name="width">Client width in logical pixels.</param>
    /// <param name="height">Client height in logical pixels.</param>
    /// <param name="title">The window title.</param>
    public static HeadlessScene Create(
        Control? root = null,
        float width = 480f,
        float height = 320f,
        string title = "Kolvir test")
        => Create(new HeadlessApplicationBackend(), root, width, height, title);

    /// <summary>The same scene over an application backend the test supplies.</summary>
    /// <param name="backend">
    /// The backend to drive. It has to create <see cref="HeadlessWindowBackend"/> windows, which is
    /// what the headless backend and a wrapper that delegates to it both do.
    /// </param>
    /// <param name="root">The control tree, or null for an empty window.</param>
    /// <param name="width">Client width in logical pixels.</param>
    /// <param name="height">Client height in logical pixels.</param>
    /// <param name="title">The window title.</param>
    public static HeadlessScene Create(
        IApplicationBackend backend,
        Control? root = null,
        float width = 480f,
        float height = 320f,
        string title = "Kolvir test")
    {
        ArgumentNullException.ThrowIfNull(backend);

        var application = new Application(backend);
        var window = new Window(title, root) { ClientSize = new SKSize(width, height) };

        if (window.Backend is not HeadlessWindowBackend windowBackend)
        {
            throw new InvalidOperationException(
                $"The backend created {window.Backend.GetType().Name}; a scene needs a {nameof(HeadlessWindowBackend)}.");
        }

        // The setter above already drove layout through UiHost.SetClientSize; a second pass
        // costs nothing and makes the scene independent of that detail.
        window.Host.PerformLayout();

        return new HeadlessScene(application, backend, window, windowBackend);
    }

    /// <summary>
    /// Shows the scene's window and runs <paramref name="body"/> once it is on screen, which is the
    /// only moment a window is really visible: <see cref="Window.Show"/> outside
    /// <see cref="Application.Run"/> only registers the window for the run that shows it.
    /// </summary>
    /// <remarks>
    /// <see cref="Application.Run"/> tears the application down when it returns, so a test
    /// asserts inside the handler rather than afterwards.
    /// </remarks>
    /// <param name="body">What to assert while the window is up.</param>
    public void RunWhileShown(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);

        Window.AddShownHandler(_ => body());
        Window.Show();
        _application.Run();
    }

    /// <summary>Paints the whole tree and returns the bitmap the test can sample.</summary>
    public SKBitmap Render()
    {
        var info = new SKImageInfo(
            Math.Max(1, (int)MathF.Round(ClientSize.Width)),
            Math.Max(1, (int)MathF.Round(ClientSize.Height)),
            SKColorType.Bgra8888,
            SKAlphaType.Premul);

        var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            WindowBackend.RenderTo(canvas);
            canvas.Flush();
        }

        return bitmap;
    }

    /// <summary>Paints the tree once and returns the colour at one logical pixel.</summary>
    public SKColor PixelAt(int x, int y)
    {
        using var bitmap = Render();
        return bitmap.GetPixel(
            Math.Clamp(x, 0, bitmap.Width - 1),
            Math.Clamp(y, 0, bitmap.Height - 1));
    }

    /// <summary>Paints the tree once, so a paint-path exception surfaces as a failure.</summary>
    public void Paint()
    {
        using var bitmap = Render();
    }

    /// <summary>The centre of <paramref name="control"/> in host coordinates.</summary>
    public SKPoint CenterOf(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var origin = control.GetPositionInHost();
        return new SKPoint(origin.X + control.Width / 2f, origin.Y + control.Height / 2f);
    }

    /// <summary>Moves the pointer to the centre of <paramref name="control"/>.</summary>
    public void MoveTo(Control control) => Host.ProcessMouseMove(CenterOf(control));

    /// <summary>Moves the pointer to a point in host coordinates.</summary>
    public void MoveTo(SKPoint point) => Host.ProcessMouseMove(point);

    /// <summary>Presses the left button at the centre of <paramref name="control"/>, without releasing.</summary>
    public void Press(Control control, Modifiers modifiers = Modifiers.None) =>
        Host.ProcessMouseDown(CenterOf(control), MouseButtons.Left, modifiers);

    /// <summary>Releases the left button at the centre of <paramref name="control"/>.</summary>
    public void Release(Control control, Modifiers modifiers = Modifiers.None) =>
        Host.ProcessMouseUp(CenterOf(control), MouseButtons.Left, modifiers);

    /// <summary>Moves to, presses and releases at the centre of <paramref name="control"/>.</summary>
    public void Click(Control control, Modifiers modifiers = Modifiers.None)
    {
        MoveTo(control);
        Press(control, modifiers);
        Release(control, modifiers);
    }

    /// <summary>Moves to, presses and releases at a point in host coordinates.</summary>
    public void ClickAt(SKPoint point, Modifiers modifiers = Modifiers.None, int clickCount = 1)
    {
        Host.ProcessMouseMove(point);
        Host.ProcessMouseDown(point, MouseButtons.Left, modifiers, clickCount);
        Host.ProcessMouseUp(point, MouseButtons.Left, modifiers);
    }

    /// <summary>Presses and holds <paramref name="key"/>.</summary>
    public void Key(Key key, Modifiers modifiers = Modifiers.None) =>
        Host.ProcessKeyDown(key, modifiers);

    /// <summary>Releases <paramref name="key"/>.</summary>
    public void KeyUp(Key key, Modifiers modifiers = Modifiers.None) =>
        Host.ProcessKeyUp(key, modifiers);

    /// <summary>Presses and releases <paramref name="key"/>, the way a real keystroke arrives.</summary>
    public void TypeKey(Key key, Modifiers modifiers = Modifiers.None)
    {
        Host.ProcessKeyDown(key, modifiers);
        Host.ProcessKeyUp(key, modifiers);
    }

    /// <summary>Types <paramref name="text"/> as if the keyboard had produced it.</summary>
    public void Type(string text) => Host.ProcessTextInput(text);

    /// <summary>Changes the client size, which is what a window manager does on a resize.</summary>
    public void Resize(float width, float height) => Window.ClientSize = new SKSize(width, height);

    /// <summary>Closes the window and disposes the application, in that order.</summary>
    public void Dispose()
    {
        Window.Close();
        _application.Dispose();
    }
}
