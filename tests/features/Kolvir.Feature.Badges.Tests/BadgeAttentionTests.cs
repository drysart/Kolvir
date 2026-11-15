using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Kolvir.Backend.Headless;

namespace Kolvir.Badges.Tests;

/// <summary>
/// What an attention request does once the user comes back, which is the only badge state a platform
/// ends by itself.
/// </summary>
/// <remarks>
/// <para>
/// A flash, a bounce and a dock highlight all stop when the application's window becomes the active
/// window, so that is the event these tests drive - through a real application and real windows on
/// the headless backend, rather than by calling into the badge's private plumbing. What is being
/// pinned is the promise the property makes: <see cref="AppBadge.Urgent"/> describes a request
/// that is still in flight, so it reads false once nobody needs to be told.
/// </para>
/// <para>
/// The application allows one per process, so every test here disposes the one it made.
/// </para>
/// </remarks>
[TestClass]
public sealed class BadgeAttentionTests
{
    [TestMethod]
    public void ComingBackToTheWindowEndsAnUrgentRequest()
    {
        using var scene = new Scene();

        scene.Badge.Urgent = true;
        Assert.IsTrue(scene.Badge.Urgent, "the request stands while nobody has looked");
        Assert.IsTrue(scene.Presenter.Last.Urgent, "and the platform has been asked");

        scene.Windows[0].Activate();

        Assert.IsFalse(scene.Badge.Urgent, "the platform stops drawing attention when the window is activated");
        Assert.AreEqual(
            new BadgeState(BadgeKind.None, 0, null, false),
            scene.Presenter.Last,
            "and is told to stop, rather than left showing something the badge no longer holds");
    }

    [TestMethod]
    public void AnyWindowComingBackEndsTheRequest()
    {
        using var scene = new Scene(windows: 2);

        scene.Badge.Urgent = true;
        scene.Windows[1].Activate();

        Assert.IsFalse(scene.Badge.Urgent, "the second window being activated is the user coming back too");
    }

    [TestMethod]
    public void AnUrgentRequestCanBeMadeAgainAfterTheUserHasBeenBack()
    {
        using var scene = new Scene();

        scene.Badge.Urgent = true;
        scene.Windows[0].Activate();
        scene.Badge.Urgent = true;

        CollectionAssert.AreEqual(
            new[] { true, false, true },
            scene.Presenter.Applied.ConvertAll(state => state.Urgent).ToArray(),
            "the second request is a transition, which is what makes the platform draw attention again");
    }

    [TestMethod]
    public void ComingBackWhenNothingIsUrgentPublishesNothing()
    {
        using var scene = new Scene();

        scene.Badge.Count = 3;
        var published = scene.Presenter.Applied.Count;

        scene.Windows[0].Activate();

        Assert.AreEqual(published, scene.Presenter.Applied.Count, "there was no request to end");
        Assert.AreEqual(3, scene.Badge.Count, "and the count is untouched: only the request is a moment");
    }

    [TestMethod]
    public void TheBadgeReportsChangesItMakesItself()
    {
        using var scene = new Scene();

        var reported = new List<bool>();
        scene.Badge.AddChangedHandler(badge => reported.Add(badge.Urgent));

        scene.Badge.Urgent = true;
        scene.Windows[0].Activate();

        CollectionAssert.AreEqual(
            new[] { true, false },
            reported,
            "the request and the platform ending it are both changes an application can hear about");
    }

    /// <summary>An application, its windows and the badge over a presenter that records what it is told.</summary>
    private sealed class Scene : IDisposable
    {
        private readonly Application _application;

        public Scene(int windows = 1)
        {
            Presenter = new RecordingPresenter(BadgeCapabilities.All);
            _application = new Application(
                new HeadlessApplicationBackend(),
                new ApplicationOptions
                {
                    ConfigureServices = services => services.AddSingleton<IAppBadgePresenter>(Presenter),
                });

            for (var index = 0; index < windows; index++)
            {
                var window = new Window($"badge {index}", new Label("nothing to show"));

                // Showing is what registers a window with the application, and the application is
                // where the badge finds them; nothing is drawn, because the application is not running.
                window.Show();
                Windows.Add(window);
            }

            Badge = _application.Badge;
        }

        public RecordingPresenter Presenter { get; }

        public List<Window> Windows { get; } = [];

        public AppBadge Badge { get; }

        public void Dispose() => _application.Dispose();
    }
}
