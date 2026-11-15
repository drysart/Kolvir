using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// A tab strip whose tabs are wider than the control has to scroll. The strip's scrolling is
/// internal, so what is asserted here is the behaviour a user sees of it: every tab is still
/// reachable, exactly one page is laid out at a time, and stepping through a strip that does not fit
/// still reaches every page in order.
/// </summary>
[TestClass]
[TestCategory("Headless")]
public sealed class TabOverflowTests : HeadlessTestBase
{
    private static TabControl CreateOverflowingTabs(int count, out TabPage[] pages)
    {
        var tabs = new TabControl { Location = new SKPoint(0f, 0f), Size = new SKSize(180f, 160f) };
        pages = new TabPage[count];

        for (int i = 0; i < count; i++)
        {
            var page = new TabPage { Text = $"Page number {i}" };
            pages[i] = page;
            tabs.TabPages.Add(page);
        }

        return tabs;
    }

    [TestMethod]
    public void ATabStripThatDoesNotFitStillHasEveryPage()
    {
        using var tabs = CreateOverflowingTabs(12, out var pages);
        using var scene = HeadlessScene.Create(tabs, 180f, 160f);

        Assert.AreEqual(12, tabs.TabPages.Count);
        Assert.AreEqual(0, tabs.SelectedIndex, "the first page is selected when the pages are added");
        Assert.IsNotNull(tabs.SelectedTab);
        Assert.AreSame(pages[0], tabs.SelectedTab);
    }

    [TestMethod]
    public void OnlyTheSelectedPageIsLaidOutWhenTheStripOverflows()
    {
        using var tabs = CreateOverflowingTabs(12, out var pages);
        using var scene = HeadlessScene.Create(tabs, 180f, 160f);

        pages[0].Controls.Add(new Label { Text = "body", Location = new SKPoint(0f, 0f) });
        pages[1].Controls.Add(new Label { Text = "other", Location = new SKPoint(0f, 0f) });
        scene.Render();

        Assert.IsTrue(pages[0].Visible, "the selected page is visible");
        Assert.IsFalse(pages[1].Visible, "and every other page is hidden, however far down the strip it is");
    }

    [TestMethod]
    public void SteppingThroughAnOverflowingStripReachesEveryPageInOrder()
    {
        using var tabs = CreateOverflowingTabs(12, out var pages);
        using var scene = HeadlessScene.Create(tabs, 180f, 160f);

        var seen = new List<int>();
        tabs.AddSelectedIndexChangedHandler(_ => seen.Add(tabs.SelectedIndex));

        for (int i = 1; i < pages.Length; i++)
        {
            tabs.SelectedIndex = i;
        }

        CollectionAssert.AreEqual(
            Enumerable.Range(1, 11).ToArray(),
            seen.ToArray(),
            "each step selects the next page, however far the strip has to scroll");

        Assert.AreSame(pages[^1], tabs.SelectedTab);

        tabs.SelectedIndex = 0;
        Assert.AreSame(pages[0], tabs.SelectedTab, "and the strip comes back to the start");
    }

    [TestMethod]
    public void SelectingTheSamePageAgainDoesNotRaiseTheEvent()
    {
        using var tabs = CreateOverflowingTabs(12, out _);
        using var scene = HeadlessScene.Create(tabs, 180f, 160f);

        int changes = 0;
        tabs.AddSelectedIndexChangedHandler(_ => changes++);

        tabs.SelectedIndex = 0;
        Assert.AreEqual(0, changes, "the first page was already selected");

        tabs.SelectedIndex = 7;
        tabs.SelectedIndex = 7;
        Assert.AreEqual(1, changes);
    }

    [TestMethod]
    public void SelectingPastTheEndClampsToTheLastPage()
    {
        using var tabs = CreateOverflowingTabs(12, out var pages);
        using var scene = HeadlessScene.Create(tabs, 180f, 160f);

        tabs.SelectedIndex = 99;
        Assert.AreSame(pages[^1], tabs.SelectedTab);

        tabs.SelectedIndex = -5;
        Assert.AreSame(pages[0], tabs.SelectedTab);
    }
}
