using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Kolvir;
using Kolvir.Backend.Headless;

namespace Kolvir.Tests;

/// <summary>
/// The application and window model: ownership, lifecycle, the container, the dialog seam, theme
/// changes at runtime, activation and second-launch forwarding, and menu shortcuts.
/// </summary>
/// <remarks>
/// The headless backend makes the whole application model a straight line - <c>Run</c> drains queued
/// work and returns rather than looping - so the lifecycle is asserted from a shown handler, which
/// is the moment an application would really do the work.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class ApplicationTests : HeadlessTestBase
{
    [TestMethod]
    public void TheApplicationOwnsEveryWindowItShows()
    {
        using var app = NewApplication();
        var first = new Window("first", new Panel());
        var second = new Window("second", new Panel());

        Assert.AreEqual(0, app.Windows.Count, "a window joins the application when it is shown");

        first.Show();
        second.Show();
        app.Run();

        // Run tears the application down, so what it owned is asked inside the run: the shown
        // handler is the one place the window list is meaningful.
        Assert.IsTrue(first.IsClosed && second.IsClosed);
    }

    [TestMethod]
    public void AShownWindowIsRegisteredAndAClosedOneIsRemoved()
    {
        using var app = NewApplication();
        var window = new Window("window", new Panel());
        var other = new Window("other", new Panel());

        var registration = new List<int>();
        window.AddShownHandler(_ =>
        {
            registration.Add(app.Windows.Count);
            other.Close();
            registration.Add(app.Windows.Count);
        });

        other.Show();
        window.Show();
        app.Run();

        CollectionAssert.AreEqual(new[] { 2, 1 }, registration.ToArray());
    }

    [TestMethod]
    public void AClosingHandlerCanCancelTheClose()
    {
        using var app = NewApplication();
        var window = new Window("window", new Panel());

        void Cancel(object _, System.ComponentModel.CancelEventArgs args) => args.Cancel = true;

        window.AddClosingHandler(Cancel);
        window.Close();

        Assert.IsFalse(window.IsClosed, "a handler that cancels should keep the window open");

        // With the handler removed the same close goes through.
        window.RemoveClosingHandler(Cancel);
        window.Close();
        Assert.IsTrue(window.IsClosed);
    }

    [TestMethod]
    public void RunShowsEveryWindowAndReturnsTheExitCode()
    {
        using var app = NewApplication();
        var first = new Window("first", new Panel());
        var second = new Window("second", new Panel());

        bool firstShown = false, secondShown = false;
        first.AddShownHandler(_ => firstShown = true);
        second.AddShownHandler(_ => secondShown = true);

        // Exit from the last window's shown handler, so both have been shown by then. Exiting from
        // the first would stop the second from ever being created as far as the run is concerned.
        second.AddShownHandler(_ => app.Exit(7));

        first.Show();
        second.Show();
        int code = app.Run();

        Assert.IsTrue(firstShown && secondShown, "both windows should have been shown");
        Assert.AreEqual(7, code, "Exit's code should be the process exit code");
        Assert.IsTrue(first.IsClosed && second.IsClosed, "and every window should be closed");
    }

    [TestMethod]
    public void TheMainWindowIsTheOneTheApplicationWasTold()
    {
        using var app = NewApplication();
        var window = new Window("main", new Panel());

        app.MainWindow = window;

        Assert.AreSame(window, app.MainWindow);
    }

    [TestMethod]
    public void TheContainerResolvesTheBackendsDialogService()
    {
        using var app = NewApplication();

        var dialogs = app.Dialogs;
        Assert.IsInstanceOfType<HeadlessDialogService>(dialogs, "the headless backend should register its scripted service");

        var service = (HeadlessDialogService)dialogs;
        service.MessageHandler = (_, _) => DialogResult.Yes;

        var result = dialogs.ShowMessage(new MessageBoxOptions { Message = "Continue?" }, null);

        Assert.AreEqual(DialogResult.Yes, result, "the scripted answer should come back");
        Assert.AreEqual(1, service.Messages.Count);
        Assert.AreEqual("Continue?", service.Messages[0].Message);
    }

    [TestMethod]
    public void AFileDialogReturnsTheScriptedPath()
    {
        using var app = NewApplication();
        var service = (HeadlessDialogService)app.Dialogs;
        service.OpenFileHandler = (_, _) => "C:/data/input.txt";

        string? path = app.Dialogs.OpenFile(new FileDialogOptions { Title = "Open" }, null);

        Assert.AreEqual("C:/data/input.txt", path);
        Assert.AreEqual(1, service.OpenFileRequests.Count);
        Assert.AreEqual("Open", service.OpenFileRequests[0].Title);
    }

    [TestMethod]
    public void ConfigureServicesMakesAServiceAvailableToAWindowAndItsControls()
    {
        var marker = new Marker();

        using var app = NewApplication(new ApplicationOptions
        {
            ApplicationName = "container",
            ConfigureServices = services => services.AddSingleton(marker),
        });

        var control = new Panel();
        var window = new Window("window", control);

        Assert.AreSame(marker, window.Services.GetService(typeof(Marker)));
        Assert.AreSame(marker, control.GetService<Marker>(), "a control should reach the window's scope through the host");
    }

    [TestMethod]
    public void TheWindowForwardsItsPropertiesToTheBackend()
    {
        using var app = NewApplication();
        var window = new Window("title", new Panel()) { ClientSize = new SKSize(300f, 200f) };

        var backend = (HeadlessWindowBackend)window.Backend;

        Assert.AreEqual("title", backend.Title);
        Assert.AreEqual(new SKSize(300f, 200f), backend.ClientSize);

        window.Title = "renamed";
        window.ClientSize = new SKSize(400f, 250f);
        window.TopMost = true;
        window.Resizable = false;

        Assert.AreEqual("renamed", backend.Title);
        Assert.AreEqual(new SKSize(400f, 250f), backend.ClientSize);
        Assert.IsTrue(backend.TopMost);
        Assert.IsFalse(backend.Resizable);
    }

    [TestMethod]
    public void AThemeChangeAtRuntimeReachesTheApplication()
    {
        using var app = NewApplication();
        int fromApplication = 0, fromTheme = 0;
        app.AddThemeChangedHandler(_ => fromApplication++);
        using var token = Theme.AddChangedHandler(_ => fromTheme++);

        Theme.Palette = Palette.Dark;

        Assert.AreEqual(1, fromTheme, "the theme should tell its subscribers");
        Assert.AreEqual(1, fromApplication, "and the application should pass it on");
        Assert.IsTrue(Theme.IsDarkMode);
    }

    [TestMethod]
    public void AShortcutOnTheMenuBarInvokesItsCommand()
    {
        using var app = NewApplication();
        int invoked = 0;

        var item = new MenuItem("&Dark Mode", () => invoked++)
        {
            Shortcut = new Shortcut(Key.D),
        };
        var window = new Window("shortcuts", new Panel())
        {
            MenuBar = new MenuBar(new Menu("&View", item)),
        };

        var host = window.Host;
        Assert.IsTrue(host.ProcessKeyDown(Key.D, Modifiers.Control), "the bar should claim the key");
        Assert.AreEqual(1, invoked);

        host.ProcessKeyDown(Key.D);
        Assert.AreEqual(1, invoked, "the bare key must not reach the item");
    }

    [TestMethod]
    public void ASecondLaunchIsForwardedWithItsArguments()
    {
        using var app = NewApplication();
        var arguments = new List<string>();
        app.AddSecondInstanceHandler((_, e) => arguments.AddRange(e.Arguments));
        var backend = (HeadlessApplicationBackend)app.Backend;
        var window = new Window("main", new Panel());
        app.MainWindow = window;

        backend.RaiseSecondInstance(["file.txt", "--flag"]);

        CollectionAssert.AreEqual(new[] { "file.txt", "--flag" }, arguments.ToArray());
    }

    [TestMethod]
    public void ActivateBringsTheMainWindowForward()
    {
        using var app = NewApplication();
        var window = new Window("main", new Panel());
        app.MainWindow = window;

        app.Activate();

        int activated = 0;
        window.AddActivatedHandler(_ => activated++);
        ((HeadlessWindowBackend)window.Backend).Activate();

        Assert.AreEqual(1, activated, "the backend's Activate should reach the window's handler");
    }

    /// <summary>The headless application every test in this class starts from.</summary>
    private static Application NewApplication(ApplicationOptions? options = null) =>
        new(new HeadlessApplicationBackend(), options ?? new ApplicationOptions { ApplicationName = "tests" });

    /// <summary>A service with no dependencies, so the container's plumbing is the thing under test.</summary>
    private sealed class Marker;
}
