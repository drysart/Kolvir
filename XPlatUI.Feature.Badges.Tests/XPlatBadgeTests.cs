using Microsoft.VisualStudio.TestTools.UnitTesting;
using XPlatUI.Badges;

namespace XPlatUI.Badges.Tests;

/// <summary>
/// What an application's four badge members resolve to, and what a platform that cannot draw that
/// presentation is told instead.
/// </summary>
/// <remarks>
/// The precedence between the count, the text and the dot is invisible until two features of an
/// application set different ones, and the degradation of an unsupported presentation is invisible
/// until it runs on the platform that lacks it. Both are decided here, once, so they are pinned here
/// rather than left to the three platform halves to agree on.
/// </remarks>
[TestClass]
public sealed class XPlatBadgeTests
{
    /// <summary>The Linux desktop's set: a number and an urgent highlight, no text and no dot.</summary>
    private static readonly XPlatBadgeCapabilities Linux = new() { Count = true, Urgent = true };

    [TestMethod]
    public void ANewBadgeShowsNothing()
    {
        var presenter = new RecordingPresenter(XPlatBadgeCapabilities.All);
        var badge = new XPlatAppBadge(presenter);

        Assert.AreEqual(0, presenter.Applied.Count, "nothing is published until something is set");
        Assert.AreEqual(XPlatBadgeKind.None, badge.Resolve(presenter.Capabilities).Kind);
    }

    [TestMethod]
    public void ACountIsShown()
    {
        var presenter = new RecordingPresenter(XPlatBadgeCapabilities.All);
        var badge = new XPlatAppBadge(presenter);

        badge.Count = 3;

        Assert.AreEqual(new XPlatBadgeState(XPlatBadgeKind.Count, 3, null, false), presenter.Last);
    }

    [TestMethod]
    public void ACountOfZeroIsNoCount()
    {
        var presenter = new RecordingPresenter(XPlatBadgeCapabilities.All);
        var badge = new XPlatAppBadge(presenter);

        badge.Count = 3;
        badge.Count = 0;

        Assert.AreEqual(XPlatBadgeKind.None, presenter.Last.Kind, "no unread items is not a badge");
        Assert.IsTrue(presenter.Last.Urgent == false, "and it is not urgent either");
    }

    [TestMethod]
    public void TextBeatsTheCount()
    {
        var presenter = new RecordingPresenter(XPlatBadgeCapabilities.All);
        var badge = new XPlatAppBadge(presenter);

        badge.Count = 1200;
        badge.Text = "99+";

        Assert.AreEqual(new XPlatBadgeState(XPlatBadgeKind.Text, 0, "99+", false), presenter.Last);
    }

    [TestMethod]
    public void TheDotBeatsTextAndCount()
    {
        var presenter = new RecordingPresenter(XPlatBadgeCapabilities.All);
        var badge = new XPlatAppBadge(presenter);

        badge.Count = 4;
        badge.Text = "New";
        badge.IsDot = true;

        Assert.AreEqual(XPlatBadgeKind.Dot, presenter.Last.Kind);
    }

    [TestMethod]
    public void ClearingTheDotFallsBackToWhatIsStillSet()
    {
        var presenter = new RecordingPresenter(XPlatBadgeCapabilities.All);
        var badge = new XPlatAppBadge(presenter);

        badge.Count = 4;
        badge.IsDot = true;
        badge.IsDot = false;

        Assert.AreEqual(new XPlatBadgeState(XPlatBadgeKind.Count, 4, null, false), presenter.Last);
    }

    [TestMethod]
    public void WhitespaceIsNotText()
    {
        var presenter = new RecordingPresenter(XPlatBadgeCapabilities.All);
        var badge = new XPlatAppBadge(presenter);

        badge.Count = 2;
        badge.Text = "   ";

        Assert.IsNull(badge.Text, "whitespace clears the label");
        Assert.AreEqual(XPlatBadgeKind.Count, presenter.Last.Kind, "the count is what is left");
    }

    [TestMethod]
    public void SettingTheSameValueTwicePublishesOnce()
    {
        var presenter = new RecordingPresenter(XPlatBadgeCapabilities.All);
        var badge = new XPlatAppBadge(presenter);

        badge.Count = 5;
        badge.Count = 5;
        badge.Text = "5";
        badge.Text = "5";

        Assert.AreEqual(2, presenter.Applied.Count, "one per change, not one per assignment");
    }

    [TestMethod]
    public void UrgentIsIndependentOfTheBadge()
    {
        var presenter = new RecordingPresenter(XPlatBadgeCapabilities.All);
        var badge = new XPlatAppBadge(presenter);

        badge.Count = 2;
        badge.Urgent = true;

        Assert.AreEqual(new XPlatBadgeState(XPlatBadgeKind.Count, 2, null, true), presenter.Last);
    }

    [TestMethod]
    public void UrgentIsDroppedWhereThePlatformCannotShowIt()
    {
        var limited = new RecordingPresenter(new XPlatBadgeCapabilities { Count = true });
        var badge = new XPlatAppBadge(limited);

        badge.Count = 2;
        badge.Urgent = true;

        Assert.IsFalse(limited.Last.Urgent, "a platform with no attention presentation is not asked for one");
        Assert.AreEqual(XPlatBadgeKind.Count, limited.Last.Kind);
    }

    [TestMethod]
    public void ClearingPublishesOnceAndShowsNothing()
    {
        var presenter = new RecordingPresenter(XPlatBadgeCapabilities.All);
        var badge = new XPlatAppBadge(presenter);

        badge.Count = 3;
        badge.Text = "New";
        badge.IsDot = true;
        badge.Urgent = true;
        var before = presenter.Applied.Count;

        badge.Clear();

        Assert.AreEqual(before + 1, presenter.Applied.Count, "one change, however many members were set");
        Assert.AreEqual(XPlatBadgeState.Empty, presenter.Last);
        Assert.IsNull(badge.Count);
        Assert.IsFalse(badge.IsDot);
        Assert.IsFalse(badge.Urgent);
    }

    [TestMethod]
    public void ClearingAnEmptyBadgePublishesNothing()
    {
        var presenter = new RecordingPresenter(XPlatBadgeCapabilities.All);
        var badge = new XPlatAppBadge(presenter);

        badge.Clear();

        Assert.AreEqual(0, presenter.Applied.Count);
    }

    [TestMethod]
    public void AnUnsupportedCountBecomesAttention()
    {
        var presenter = new RecordingPresenter(new XPlatBadgeCapabilities { Urgent = true });
        var badge = new XPlatAppBadge(presenter);

        badge.Count = 7;

        Assert.AreEqual(XPlatBadgeKind.None, presenter.Last.Kind, "no number can be drawn");
        Assert.IsTrue(presenter.Last.Urgent, "but the user is still told");
    }

    [TestMethod]
    public void AnUnsupportedTextBecomesAttention()
    {
        var presenter = new RecordingPresenter(Linux);
        var badge = new XPlatAppBadge(presenter);

        badge.Text = "New";

        Assert.AreEqual(XPlatBadgeKind.None, presenter.Last.Kind);
        Assert.IsTrue(presenter.Last.Urgent);
    }

    [TestMethod]
    public void AnUnsupportedDotBecomesAttention()
    {
        var presenter = new RecordingPresenter(Linux);
        var badge = new XPlatAppBadge(presenter);

        badge.IsDot = true;

        Assert.AreEqual(XPlatBadgeKind.None, presenter.Last.Kind);
        Assert.IsTrue(presenter.Last.Urgent);
    }

    [TestMethod]
    public void AnUnsupportedPresentationIsDroppedWhenAttentionIsNoBetter()
    {
        var presenter = new RecordingPresenter(new XPlatBadgeCapabilities { Count = true });
        var badge = new XPlatAppBadge(presenter);

        badge.IsDot = true;

        Assert.AreEqual(XPlatBadgeState.Empty, presenter.Last, "there is nothing to say and no way to say it");
    }

    [TestMethod]
    public void ASupportedCountStaysACountOnTheSamePlatform()
    {
        var presenter = new RecordingPresenter(Linux);
        var badge = new XPlatAppBadge(presenter);

        badge.Count = 9;

        Assert.AreEqual(new XPlatBadgeState(XPlatBadgeKind.Count, 9, null, false), presenter.Last);
    }

    [TestMethod]
    public void WithoutTheOptInNothingThrowsAndNothingIsShown()
    {
        var badge = new XPlatAppBadge(presenter: null);

        badge.Count = 3;
        badge.Text = "New";
        badge.IsDot = true;
        badge.Urgent = true;
        badge.Clear();

        Assert.IsFalse(badge.Capabilities.Any, "an application that forgot AddAppBadges can tell");
        Assert.AreEqual(XPlatBadgeState.Empty, badge.Resolve(badge.Capabilities));
    }

    [TestMethod]
    public void CapabilitiesComeFromThePlatformHalf()
    {
        var badge = new XPlatAppBadge(new RecordingPresenter(Linux));

        Assert.AreEqual(Linux, badge.Capabilities);
    }

    /// <summary>A platform half that keeps everything it was told.</summary>
    private sealed class RecordingPresenter(XPlatBadgeCapabilities capabilities) : IXPlatAppBadgePresenter
    {
        public List<XPlatBadgeState> Applied { get; } = [];

        public XPlatBadgeCapabilities Capabilities { get; } = capabilities;

        public XPlatBadgeState Last => Applied[^1];

        public void Apply(XPlatBadgeState state) => Applied.Add(state);
    }
}
