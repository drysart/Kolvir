using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using XPlatUI;
using XPlatUI.Backend.Headless;

namespace XPlatUI.Tests;

/// <summary>
/// The rest of the application model: the hook phases, the shutdown modes, and the window
/// properties that only mean something once there is an owner or a state to report.
/// </summary>
/// <remarks>
/// <para>
/// A hook is resolved from the container the application builds, so these tests register hooks
/// through <see cref="XPlatApplicationOptions.ConfigureServices"/> - the same seam a feature uses -
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
    private static XPlatApplicationOptions OptionsFor(
        Action<IServiceCollection> configure,
        XPlatShutdownMode shutdownMode = XPlatShutdownMode.OnLastWindowClose) =>
        new()
        {
            ApplicationName = "tests",
            ShutdownMode = shutdownMode,
            ConfigureServices = services => configure(services),
        };

    private static XPlatApplication NewApplication(
        Action<IServiceCollection> configure,
        XPlatShutdownMode shutdownMode = XPlatShutdownMode.OnLastWindowClose) =>
        new(new HeadlessApplicationBackend(), OptionsFor(configure, shutdownMode));

    private sealed class RecordingHook(List<XPlatApplicationPhase> phases) : IXPlatApplicationHook
    {
        public void OnPhase(XPlatApplicationPhase phase, XPlatApplication application) => phases.Add(phase);
    }

    /// <summary>Runs <paramref name="body"/> and disposes the application whatever the body does.</summary>
    private static void WithApplication(XPlatApplication app, Action<XPlatApplication> body)
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
        var phases = new List<XPlatApplicationPhase>();

        WithApplication(
            NewApplication(services =>
                services.AddSingleton<IXPlatApplicationHook>(new RecordingHook(phases))),
            app =>
            {
                var window = new XPlatWindow("main", new XPlatPanel());

                // No Exit before Run: a request to exit that early skips the Created phase, which
                // is exactly the ordering this test is about. The headless loop returns by itself.
                window.Show();
                app.Run();
            });

        CollectionAssert.AreEqual(
            new[]
            {
                XPlatApplicationPhase.Creating,
                XPlatApplicationPhase.Created,
                XPlatApplicationPhase.Stopping,
                XPlatApplicationPhase.Stopped,
                XPlatApplicationPhase.Disposed,
            },
            phases.ToArray(),
            "the phases are entered in the order the enum documents");
    }

    [TestMethod]
    public void AnApplicationThatNeverRunsALoopStillSeesCreatingAndDisposed()
    {
        var phases = new List<XPlatApplicationPhase>();

        WithApplication(
            NewApplication(services =>
                services.AddSingleton<IXPlatApplicationHook>(new RecordingHook(phases))),
            _ =>
            {
                // A launch forwarded to another instance and a headless render both take this path:
                // the window is shown, no loop ever runs.
                var window = new XPlatWindow("main", new XPlatPanel());
                window.Show();
            });

        CollectionAssert.AreEqual(
            new[] { XPlatApplicationPhase.Creating, XPlatApplicationPhase.Disposed },
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
                services.AddXPlatApplicationStartupHook(_ => order.Add("first"));
                services.AddXPlatApplicationStartupHook(_ => order.Add("second"));
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
        var phases = new List<XPlatWindowPhase>();

        WithApplication(
            NewApplication(services => services.AddXPlatWindowHook(
                onCreated: _ => phases.Add(XPlatWindowPhase.Created),
                onShowing: _ => phases.Add(XPlatWindowPhase.Showing),
                onShown: _ => phases.Add(XPlatWindowPhase.Shown),
                onClosing: _ => phases.Add(XPlatWindowPhase.Closing),
                onClosed: _ => phases.Add(XPlatWindowPhase.Closed),
                onDisposed: _ => phases.Add(XPlatWindowPhase.Disposed))),
            app =>
            {
                var window = new XPlatWindow("main", new XPlatPanel());

                // Windows shown before Run become visible when it starts, which is the only place a
                // window is shown at all: Show outside a loop only queues it.
                window.AddShownHandler(_ => window.Close());
                window.Show();
                app.Run();
            });

        CollectionAssert.AreEqual(
            new[]
            {
                XPlatWindowPhase.Created,
                XPlatWindowPhase.Showing,
                XPlatWindowPhase.Shown,
                XPlatWindowPhase.Closing,
                XPlatWindowPhase.Closed,
                XPlatWindowPhase.Disposed,
            },
            phases.ToArray(),
            "a window comes up and goes down through the documented phases");
    }

    [TestMethod]
    public void EachWindowGetsItsOwnWindowHook()
    {
        var seen = new List<XPlatWindow>();

        WithApplication(
            NewApplication(services => services.AddXPlatWindowHook(onShown: seen.Add)),
            app =>
            {
                var first = new XPlatWindow("first", new XPlatPanel());
                var second = new XPlatWindow("second", new XPlatPanel());
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
            NewApplication(_ => { }, XPlatShutdownMode.OnMainWindowClose),
            app =>
            {
                var window = new XPlatWindow("main", new XPlatPanel());
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
            NewApplication(_ => { }, XPlatShutdownMode.OnExplicitShutdown),
            app =>
            {
                var window = new XPlatWindow("main", new XPlatPanel());
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
            var owner = new XPlatWindow("owner", new XPlatPanel());
            var child = new XPlatWindow("child", new XPlatPanel());

            int stateChanges = 0;
            child.AddStateChangedHandler(_ => stateChanges++);

            child.State = XPlatWindowState.Maximized;

            Assert.AreEqual(XPlatWindowState.Maximized, child.State);
            Assert.AreEqual(
                XPlatWindowState.Maximized,
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
            var window = new XPlatWindow("main", new XPlatPanel());

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
            var modal = new XPlatWindow("modal", new XPlatPanel());

            // ShowDialog resets the result to Cancel as it opens, so the result is what the window
            // carries when it closes: the headless backend closes at once, so set it on Shown.
            modal.AddShownHandler(_ => modal.DialogResult = XPlatDialogResult.Ok);

            var result = modal.ShowDialog();

            Assert.AreEqual(XPlatDialogResult.Ok, result, "the result travels back to the caller");
            Assert.IsTrue(modal.IsClosed, "and the window is closed once the dialog returns");
            Assert.AreEqual(0, app.Windows.Count, "with nothing left registered on the application");
        });
    }

    [TestMethod]
    public void AHookThatThrowsReachesTheUnhandledExceptionHandler()
    {
        Exception? reported = null;

        WithApplication(
            NewApplication(services => services.AddXPlatApplicationHook(
                XPlatApplicationPhase.Created,
                _ => throw new InvalidOperationException("hook failed"))),
            app =>
            {
                app.AddUnhandledExceptionHandler((_, e) =>
                {
                    reported = e.Exception;

                    // Handling it stops Run rethrowing the fault.
                    e.Handled = true;
                });

                var window = new XPlatWindow("main", new XPlatPanel());

                // No Exit here: Exit before Run start would skip the Created phase that raises.
                window.Show();
                app.Run();
            });

        Assert.IsNotNull(reported, "a hook's failure is reported to the unhandled handler");
        Assert.IsInstanceOfType<InvalidOperationException>(reported);
    }
}
