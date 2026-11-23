namespace Kolvir.Feature.Observables.Tests;

/// <summary>
/// What a read of an observable set depends on, and what wakes it: the count, one item, and the contents,
/// each raised by exactly the changes that can change its answer
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 6.2.4).
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class ObservableSetDependencyTests
{
    [TestMethod]
    public void AReadOfCountAndOfAnItemAreTwoDependencies()
    {
        using var set = Observable.CreateHashSet("a");

        using var run = Observable.CollectDependencies(
            () =>
            {
                _ = set.Count;
                _ = set.Contains("a");
            },
            _ => { });

        Assert.AreEqual(2, run.Dependencies.Count, "the size and the item are different questions");
        Assert.AreNotSame(
            run.Dependencies[0],
            run.Dependencies[1],
            "so one change can wake only one of them");
    }

    [TestMethod]
    public void AskingWhetherOneItemIsPresentIsThatItemsDependency()
    {
        // The read a rendered toggle makes: a chip per tag asking about its own tag. A change to one tag
        // must not re-render the others, or the whole point of an address is lost.
        using var set = Observable.CreateHashSet("a");
        var wakes = new List<string>();

        using var count = Observable.CollectDependencies(() => { _ = set.Count; }, _ => wakes.Add("count"));
        using var first = Observable.CollectDependencies(() => { _ = set.Contains("a"); }, _ => wakes.Add("a"));
        using var second = Observable.CollectDependencies(() => { _ = set.Contains("b"); }, _ => wakes.Add("b"));

        set.Add("b");

        CollectionAssert.AreEqual(
            new[] { "count", "b" },
            wakes,
            "the size changed and b arrived; a was not asked about again");
    }

    [TestMethod]
    public void ARemovalWakesTheItemAndTheSize()
    {
        using var set = Observable.CreateHashSet("a", "b");
        var wakes = new List<string>();

        using var count = Observable.CollectDependencies(() => { _ = set.Count; }, _ => wakes.Add("count"));
        using var first = Observable.CollectDependencies(() => { _ = set.Contains("a"); }, _ => wakes.Add("a"));

        set.Remove("a");

        CollectionAssert.AreEqual(new[] { "count", "a" }, wakes, "the count and the item that left");
    }

    [TestMethod]
    public void AddingAnItemTheComparerCallsEqualWakesNobody()
    {
        // A no-op that raises no record cannot wake a dependency: an event means something changed.
        using var set = Observable.CreateHashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wakes = 0;

        set.Add("abc");
        using var run = Observable.CollectDependencies(() => { _ = set.Contains("ABC"); }, _ => wakes++);

        Assert.IsFalse(set.Add("ABC"), "the comparer says it is already there");

        Assert.AreEqual(0, wakes, "no record, so no wake-up");
    }

    [TestMethod]
    public void TheSetsComparerDecidesWhichEntryAnItemIs()
    {
        // The entries are keyed with the set's comparer, not the item's equality: a reader that asked
        // about one spelling has to be woken by a change in another, because they are the same item.
        using var set = Observable.CreateHashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wakes = 0;

        using var run = Observable.CollectDependencies(() => { _ = set.Contains("ABC"); }, _ => wakes++);

        set.Add("abc");

        Assert.AreEqual(1, wakes, "the same entry, whichever spelling the change used");
    }

    [TestMethod]
    public void EnumerationAndTheScansDependOnTheContents()
    {
        using var set = Observable.CreateHashSet("a");
        var fromEnumeration = 0;
        var fromSetEquals = 0;
        var fromOverlaps = 0;
        var fromSubset = 0;

        using var enumeration = Observable.CollectDependencies(
            () =>
            {
                foreach (var item in set)
                {
                    _ = item;
                }
            },
            _ => fromEnumeration++);

        using var setEquals = Observable.CollectDependencies(() => { _ = set.SetEquals(["a"]); }, _ => fromSetEquals++);
        using var overlaps = Observable.CollectDependencies(() => { _ = set.Overlaps(["a"]); }, _ => fromOverlaps++);
        using var subset = Observable.CollectDependencies(() => { _ = set.IsSubsetOf(["a"]); }, _ => fromSubset++);

        set.Add("b");

        Assert.AreEqual(1, fromEnumeration, "an enumeration is stale when the set changes");
        Assert.AreEqual(1, fromSetEquals, "and so is every question that reads every item");
        Assert.AreEqual(1, fromOverlaps);
        Assert.AreEqual(1, fromSubset);
    }

    [TestMethod]
    public void AClearWakesTheCountAndEveryWatchedItem()
    {
        using var set = Observable.CreateHashSet("a", "b");
        var wakes = new List<string>();

        using var count = Observable.CollectDependencies(() => { _ = set.Count; }, _ => wakes.Add("count"));
        using var first = Observable.CollectDependencies(() => { _ = set.Contains("a"); }, _ => wakes.Add("a"));
        using var second = Observable.CollectDependencies(() => { _ = set.Contains("b"); }, _ => wakes.Add("b"));

        set.Clear();

        CollectionAssert.AreEqual(
            new[] { "count", "a", "b" },
            wakes,
            "a clear invalidates the size and every item it emptied");
    }

    [TestMethod]
    public void ABulkOperationWakesEachAffectedItemOnce()
    {
        using var set = Observable.CreateHashSet("keep");
        var wakes = new List<string>();

        using var count = Observable.CollectDependencies(() => { _ = set.Count; }, _ => wakes.Add("count"));
        using var keep = Observable.CollectDependencies(() => { _ = set.Contains("keep"); }, _ => wakes.Add("keep"));
        using var added = Observable.CollectDependencies(() => { _ = set.Contains("added"); }, _ => wakes.Add("added"));

        set.UnionWith(["added", "keep"]);

        CollectionAssert.AreEquivalent(
            new[] { "count", "added" },
            wakes,
            "one event, one raise each: the item that arrived, and the size - not the one that did not");
    }

    [TestMethod]
    public void AnUntrackedEventWakesEveryReader()
    {
        using var set = Observable.CreateHashSet("a");
        var wakes = new List<string>();

        using var count = Observable.CollectDependencies(() => { _ = set.Count; }, _ => wakes.Add("count"));
        using var first = Observable.CollectDependencies(() => { _ = set.Contains("a"); }, _ => wakes.Add("a"));
        using var absent = Observable.CollectDependencies(() => { _ = set.Contains("z"); }, _ => wakes.Add("z"));

        set.BeginChangeBatch();

        // Past the cap, so the event says only that things changed - which could have been any item.
        for (var index = 0; index <= Observable.Default.MaxCollectionBatchSize; index++)
        {
            set.Add($"item {index}");
        }

        set.EndChangeBatch();

        CollectionAssert.AreEquivalent(
            new[] { "count", "a", "z" },
            wakes,
            "a reader is woken whether or not its own answer actually changed");
    }

    [TestMethod]
    public void AReadOutsideARunRegistersNothing()
    {
        using var set = Observable.CreateHashSet("a");

        // No run is active, so these publish nothing and create no entry.
        _ = set.Count;
        _ = set.Contains("a");
        _ = set.SetEquals(["a"]);

        using var run = Observable.CollectDependencies(() => { _ = set.Contains("a"); }, _ => { });

        Assert.AreEqual(1, run.Dependencies.Count, "and the first read inside a run is still one dependency");
    }

    [TestMethod]
    public void ASourceWatchedAgainAfterItWasReleasedIsWokenAgain()
    {
        // The entries are the same machinery the list uses, so the release-and-return case has to hold
        // here too: an item's source is dropped when its last handler goes, and put back when one returns.
        using var set = Observable.CreateHashSet<string>();

        var run = Observable.CollectDependencies(() => { _ = set.Contains("a"); }, _ => { });
        var source = run.Dependencies[0];

        run.Dispose();

        var wakes = 0;
        using var again = source.AddObservableEventRaisedHandler(_ => wakes++);

        set.Add("a");

        Assert.AreEqual(1, wakes, "the set takes the entry back when something watches it again");
    }
}
