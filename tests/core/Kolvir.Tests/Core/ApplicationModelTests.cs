using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Kolvir;
using Kolvir.Backend.Headless;

namespace Kolvir.Tests;

/// <summary>
/// The rest of the application model: the hook phases, the shutdown modes, and the window
/// properties that only mean something once there is an owner or a state to report.
/// </summary>
/// <remarks>
/// <para>
/// A hook is resolved from the container the application builds, so these tests register hooks
/// through <see cref="ApplicationOptions.ConfigureServices"/> - the same seam a feature uses -
/// rather than reaching into the application.
/// </para>
/// <para>
/// The library supports one application per process, and a second one throws. Every test here
/// disposes its application in a <c>finally</c>, so a failing assertion cannot leave one behind and
/// turn the next test into a different failure.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class ApplicationModelTests : HeadlessTestBase
{
    private static ApplicationOptions OptionsFor(
        Action<IServiceCollection> configure,
        ShutdownMode shutdownMode = ShutdownMode.OnLastWindowClose) =>
        new()
        {
            ApplicationName = "tests",
            ShutdownMode = shutdownMode,
            ConfigureServices = services => configure(services),
        };

    private static Application NewApplication(
        Action<IServiceCollection> configure,
        ShutdownMode shutdownMode = ShutdownMode.OnLastWindowClose) =>
        new(new HeadlessApplicationBackend(), OptionsFor(configure, shutdownMode));

    private sealed class RecordingHook(List<ApplicationPhase> phases) : IApplicationHook
    {
        public void OnPhase(ApplicationPhase phase, Application application) => phases.Add(phase);
    }

    /// <summary>Runs <paramref name="body"/> and disposes the application whatever the body does.</summary>
    private static void WithApplication(Application app, Action<Application> body)
    {
        try
        {
            body(app);
        }
        finally
        {
            app.Dispose();
        }
    }

    [TestMethod]
    public void TheApplicationHookSeesEveryPhaseInOrder()
    {
        var phases = new List<ApplicationPhase>();

        WithApplication(
            NewApplication(services =>
                services.AddSingleton<IApplicationHook>(new RecordingHook(phases))),
            app =>
            {
                var window = new Window("main", new Panel());

                // No Exit before Run: a request to exit that early skips the Created phase, which
                // is exactly the ordering this test is about. The headless loop returns by itself.
                window.Show();
                app.Run();
            });

        CollectionAssert.AreEqual(
            new[]
            {
                ApplicationPhase.Creating,
                ApplicationPhase.Created,
                ApplicationPhase.Stopping,
                ApplicationPhase.Stopped,
                ApplicationPhase.Disposed,
            },
            phases.ToArray(),
            "the phases are entered in the order the enum documents");
    }

    [TestMethod]
    public void AnApplicationThatNeverRunsALoopStillSeesCreatingAndDisposed()
    {
        var phases = new List<ApplicationPhase>();

        WithApplication(
            NewApplication(services =>
                services.AddSingleton<IApplicationHook>(new RecordingHook(phases))),
            _ =>
            {
                // A launch forwarded to another instance and a headless render both take this path:
                // the window is shown, no loop ever runs.
                var window = new Window("main", new Panel());
                window.Show();
            });

        CollectionAssert.AreEqual(
            new[] { ApplicationPhase.Creating, ApplicationPhase.Disposed },
            phases.ToArray(),
            "Creating pairs with Disposed because neither loop-only phase can happen");
    }

    [TestMethod]
    public void HooksRunInTheOrderTheyWereRegistered()
    {
        var order = new List<string>();

        WithApplication(
            NewApplication(services =>
            {
                services.AddApplicationStartupHook(_ => order.Add("first"));
                services.AddApplicationStartupHook(_ => order.Add("second"));
            }),
            _ => Assert.AreEqual(2, order.Count, "both startup hooks run while the application is built"));

        CollectionAssert.AreEqual(
            new[] { "first", "second" },
            order.ToArray(),
            "registration order is run order");
    }

    [TestMethod]
    public void AWindowHookSeesTheShowAndClosePhases()
    {
        var phases = new List<WindowPhase>();

        WithApplication(
            NewApplication(services => services.AddWindowHook(
                onCreated: _ => phases.Add(WindowPhase.Created),
                onShowing: _ => phases.Add(WindowPhase.Showing),
                onShown: _ => phases.Add(WindowPhase.Shown),
                onClosing: _ => phases.Add(WindowPhase.Closing),
                onClosed: _ => phases.Add(WindowPhase.Closed),
                onDisposed: _ => phases.Add(WindowPhase.Disposed))),
            app =>
            {
                var window = new Window("main", new Panel());

                // Windows shown before Run become visible when it starts, which is the only place a
                // window is shown at all: Show outside a loop only queues it.
                window.AddShownHandler(_ => window.Close());
                window.Show();
                app.Run();
            });

        CollectionAssert.AreEqual(
            new[]
            {
                WindowPhase.Created,
                WindowPhase.Showing,
                WindowPhase.Shown,
                WindowPhase.Closing,
                WindowPhase.Closed,
                WindowPhase.Disposed,
            },
            phases.ToArray(),
            "a window comes up and goes down through the documented phases");
    }

    [TestMethod]
    public void EachWindowGetsItsOwnWindowHook()
    {
        var seen = new List<Window>();

        WithApplication(
            NewApplication(services => services.AddWindowHook(onShown: seen.Add)),
            app =>
            {
                var first = new Window("first", new Panel());
                var second = new Window("second", new Panel());
                first.Show();
                second.Show();
                app.Run();

                Assert.AreEqual(2, seen.Count, "the hook runs once per window, not once per application");
                CollectionAssert.AreEquivalent(new[] { first, second }, seen.ToArray());
            });
    }

    [TestMethod]
    public void ClosingTheMainWindowEndsTheLoopWhenThatIsTheShutdownMode()
    {
        WithApplication(
            NewApplication(_ => { }, ShutdownMode.OnMainWindowClose),
            app =>
            {
                var window = new Window("main", new Panel());
                app.MainWindow = window;

                // Nothing calls Exit: closing the main window has to end the loop by itself.
                window.AddShownHandler(_ => window.Close());
                window.Show();

                Assert.AreEqual(0, app.Run(), "the loop returns when the main window closes");
            });
    }

    [TestMethod]
    public void ClosingAWindowDoesNotEndTheLoopWhenTheShutdownIsExplicit()
    {
        WithApplication(
            NewApplication(_ => { }, ShutdownMode.OnExplicitShutdown),
            app =>
            {
                var window = new Window("main", new Panel());
                app.MainWindow = window;

                window.AddShownHandler(_ =>
                {
                    window.Close();

                    // If closing the window had ended the loop, this exit code would never be
                    // recorded and Run would have returned 0 instead.
                    app.BeginInvoke(() => app.Exit(3));
                });
                window.Show();

                Assert.AreEqual(3, app.Run(), "only Exit ends the loop in OnExplicitShutdown");
            });
    }

    [TestMethod]
    public void TheWindowStateAndOwnerReachTheBackend()
    {
        WithApplication(NewApplication(_ => { }), _ =>
        {
            var owner = new Window("owner", new Panel());
            var child = new Window("child", new Panel());

            int stateChanges = 0;
            child.AddStateChangedHandler(_ => stateChanges++);

            child.State = WindowState.Maximized;

            Assert.AreEqual(WindowState.Maximized, child.State);
            Assert.AreEqual(
                WindowState.Maximized,
                ((HeadlessWindowBackend)child.Backend).State,
                "the state is the backend's, not a wrapper's guess");
            Assert.AreEqual(1, stateChanges, "and changing it is announced");

            child.Owner = owner;

            Assert.AreSame(owner, child.Owner);
            Assert.AreSame(owner.Backend, ((HeadlessWindowBackend)child.Backend).Owner);
        });
    }

    [TestMethod]
    public void AWindowMovesWhereItIsTold()
    {
        WithApplication(NewApplication(_ => { }), _ =>
        {
            var window = new Window("main", new Panel());

            window.Location = new SKPoint(120f, 64f);

            Assert.AreEqual(new SKPoint(120f, 64f), window.Location);
            Assert.AreEqual(
                new SKPoint(120f, 64f),
                ((HeadlessWindowBackend)window.Backend).Location);
        });
    }

    [TestMethod]
    public void ShowDialogReportsTheDialogResultTheWindowCarries()
    {
        WithApplication(NewApplication(_ => { }), app =>
        {
            var modal = new Window("modal", new Panel());

            // ShowDialog resets the result to Cancel as it opens, so the result is what the window
            // carries when it closes: the headless backend closes at once, so set it on Shown.
            modal.AddShownHandler(_ => modal.DialogResult = DialogResult.Ok);

            var result = modal.ShowDialog();

            Assert.AreEqual(DialogResult.Ok, result, "the result travels back to the caller");
            Assert.IsTrue(modal.IsClosed, "and the window is closed once the dialog returns");
            Assert.AreEqual(0, app.Windows.Count, "with nothing left registered on the application");
        });
    }

    [TestMethod]
    public void AHookThatThrowsReachesTheUnhandledExceptionHandler()
    {
        Exception? reported = null;

        WithApplication(
            NewApplication(services => services.AddApplicationHook(
                ApplicationPhase.Created,
                _ => throw new InvalidOperationException("hook failed"))),
            app =>
            {
                app.AddUnhandledExceptionHandler((_, e) =>
                {
                    reported = e.Exception;

                    // Handling it stops Run rethrowing the fault.
                    e.Handled = true;
                });

                var window = new Window("main", new Panel());

                // No Exit here: Exit before Run start would skip the Created phase that raises.
                window.Show();
                app.Run();
            });

        Assert.IsNotNull(reported, "a hook's failure is reported to the unhandled handler");
        Assert.IsInstanceOfType<InvalidOperationException>(reported);
    }
}
