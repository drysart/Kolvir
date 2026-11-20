namespace Kolvir.Observables.Tests;

/// <summary>
/// What a read of an observable list depends on, and what wakes it: the count, one index, and the
/// contents, each raised by exactly the changes that can change its answer
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 3.6).
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class ObservableListDependencyTests
{
    [TestMethod]
    public void AReadOfCountAndOfAnIndexAreTwoDependencies()
    {
        using var list = Observable.CreateList("a", "b");

        using var run = Observable.CollectDependencies(
            () =>
            {
                _ = list.Count;
                _ = list[1];
            },
            _ => { });

        Assert.AreEqual(2, run.Dependencies.Count, "the size and the index are different questions");
        Assert.IsNotNull(run.Dependencies[0], "the first is the list's count");
        Assert.IsNotNull(run.Dependencies[1], "and the second is index 1");
        Assert.AreNotSame(
            run.Dependencies[0],
            run.Dependencies[1],
            "so they are different sources, which is what lets one change wake only one of them");
    }

    [TestMethod]
    public void OneListHasOneCountDependencyHoweverManyRunsReadIt()
    {
        using var list = Observable.CreateList("a", "b");

        using var first = Observable.CollectDependencies(() => { _ = list.Count; }, _ => { });
        using var second = Observable.CollectDependencies(() => { _ = list.Count; }, _ => { });

        Assert.AreSame(
            first.Dependencies[0],
            second.Dependencies[0],
            "the count is one field of the list, so every reader hears the same source");
    }

    [TestMethod]
    public void ReadingTheSameIndexTwiceInOneRunIsOneDependency()
    {
        using var list = Observable.CreateList("a", "b", "c");

        using var run = Observable.CollectDependencies(
            () =>
            {
                _ = list[1];
                _ = list[1];
                _ = list[0];
            },
            _ => { });

        Assert.AreEqual(2, run.Dependencies.Count, "index 1 once and index 0 once");
    }

    [TestMethod]
    public void AWriteToAnItemWakesItsIndexAndNotTheCount()
    {
        using var list = Observable.CreateList("a", "b");
        var wakes = new List<string>();

        using var count = Observable.CollectDependencies(() => { _ = list.Count; }, _ => wakes.Add("count"));
        using var first = Observable.CollectDependencies(() => { _ = list[0]; }, _ => wakes.Add("0"));
        using var second = Observable.CollectDependencies(() => { _ = list[1]; }, _ => wakes.Add("1"));

        list[1] = "z";

        CollectionAssert.AreEqual(
            new[] { "1" },
            wakes,
            "the size did not change and index 0 still answers with the same item");
    }

    [TestMethod]
    public void AMoveWakesOnlyTheIndexesItSpans()
    {
        using var list = Observable.CreateList("a", "b", "c", "d");
        var wakes = new List<string>();

        using var count = Observable.CollectDependencies(() => { _ = list.Count; }, _ => wakes.Add("count"));
        using var first = Observable.CollectDependencies(() => { _ = list[0]; }, _ => wakes.Add("0"));
        using var second = Observable.CollectDependencies(() => { _ = list[1]; }, _ => wakes.Add("1"));
        using var third = Observable.CollectDependencies(() => { _ = list[2]; }, _ => wakes.Add("2"));
        using var fourth = Observable.CollectDependencies(() => { _ = list[3]; }, _ => wakes.Add("3"));

        list.Move(1, 2);

        CollectionAssert.AreEqual(
            new[] { "1", "2" },
            wakes,
            "moving 1 to 2 shifts what those two indexes answer, and leaves 0, 3 and the size alone");
    }

    [TestMethod]
    public void AnInsertWakesEveryWatchedIndexAtOrPastItAndNoneBefore()
    {
        using var list = Observable.CreateList("a", "b", "c");
        var wakes = new List<string>();

        using var count = Observable.CollectDependencies(() => { _ = list.Count; }, _ => wakes.Add("count"));
        using var first = Observable.CollectDependencies(() => { _ = list[0]; }, _ => wakes.Add("0"));
        using var second = Observable.CollectDependencies(() => { _ = list[1]; }, _ => wakes.Add("1"));
        using var third = Observable.CollectDependencies(() => { _ = list[2]; }, _ => wakes.Add("2"));

        list.Insert(1, "x");

        CollectionAssert.AreEqual(
            new[] { "count", "1", "2" },
            wakes,
            "everything from the insertion point on may answer differently, and index 0 does not");
    }

    [TestMethod]
    public void AClearWakesTheCountAndEveryWatchedIndex()
    {
        using var list = Observable.CreateList("a", "b");
        var wakes = new List<string>();

        using var count = Observable.CollectDependencies(() => { _ = list.Count; }, _ => wakes.Add("count"));
        using var first = Observable.CollectDependencies(() => { _ = list[0]; }, _ => wakes.Add("0"));
        using var second = Observable.CollectDependencies(() => { _ = list[1]; }, _ => wakes.Add("1"));

        list.Clear();

        CollectionAssert.AreEqual(
            new[] { "count", "0", "1" },
            wakes,
            "a clear invalidates the size and every index it emptied");
    }

    [TestMethod]
    public void AReadPastTheEndIsStillARead()
    {
        // The only way an expression that is currently failing can recover: it subscribed to the index
        // on its way to throwing.
        using var list = Observable.CreateList("a", "b", "c");
        var wakes = 0;

        using var run = Observable.CollectDependencies(() => { _ = list[7]; }, _ => wakes++);

        Assert.IsTrue(run.IsFaulted, "the read threw");
        Assert.AreEqual(1, run.Dependencies.Count, "and it still reports what it read");

        list.Add("d");
        list.Add("e");
        list.Add("f");
        list.Add("g");
        list.Add("h");

        Assert.AreEqual(1, wakes, "growth into range wakes the reader that wanted index 7");

        list.Add("i");

        Assert.AreEqual(1, wakes, "and a change past it does not");
    }

    [TestMethod]
    public void EnumerationAndTheScansDependOnTheContents()
    {
        using var list = Observable.CreateList("a", "b");
        var fromEnumeration = 0;
        var fromContains = 0;
        var fromIndexOf = 0;

        using var enumeration = Observable.CollectDependencies(
            () =>
            {
                foreach (var item in list)
                {
                    _ = item;
                }
            },
            _ => fromEnumeration++);

        using var contains = Observable.CollectDependencies(() => { _ = list.Contains("a"); }, _ => fromContains++);
        using var indexOf = Observable.CollectDependencies(() => { _ = list.IndexOf("b"); }, _ => fromIndexOf++);

        list[0] = "z";

        Assert.AreEqual(1, fromEnumeration, "an enumeration is stale when any item changes");
        Assert.AreEqual(1, fromContains, "and so is a scan, because it read every item");
        Assert.AreEqual(1, fromIndexOf, "all three of them");
    }

    [TestMethod]
    public void ABatchRaisesEachDependencyOnce()
    {
        using var list = Observable.CreateList("a", "b");
        var wakes = new List<string>();

        using var count = Observable.CollectDependencies(() => { _ = list.Count; }, _ => wakes.Add("count"));
        using var first = Observable.CollectDependencies(() => { _ = list[0]; }, _ => wakes.Add("0"));
        using var second = Observable.CollectDependencies(() => { _ = list[1]; }, _ => wakes.Add("1"));

        list.BeginChangeBatch();
        list[0] = "x";
        list[0] = "y";
        list[1] = "z";
        list.EndChangeBatch();

        CollectionAssert.AreEqual(
            new[] { "0", "1" },
            wakes,
            "three records, one delivery, one raise each - however many touched the same index");
    }

    [TestMethod]
    public void AnUntrackedEventWakesEveryReader()
    {
        // An event that does not say what changed has to assume everything did - including the size,
        // which may well be where the batch went wrong, and the indexes past the end, which are cheap to
        // wake and impossible to rule out.
        using var list = Observable.CreateList("a", "b");
        var wakes = new List<string>();

        using var count = Observable.CollectDependencies(() => { _ = list.Count; }, _ => wakes.Add("count"));

        using var contents = Observable.CollectDependencies(
            () =>
            {
                foreach (var item in list)
                {
                    _ = item;
                }
            },
            _ => wakes.Add("contents"));

        using var first = Observable.CollectDependencies(() => { _ = list[0]; }, _ => wakes.Add("0"));
        using var second = Observable.CollectDependencies(() => { _ = list[1]; }, _ => wakes.Add("1"));
        using var past = Observable.CollectDependencies(() => { _ = list[5]; }, _ => wakes.Add("5"));

        list.BeginChangeBatch();

        for (var index = 0; index <= Observable.MaxBatchRecords; index++)
        {
            list.Add("x");
        }

        list.EndChangeBatch();

        CollectionAssert.AreEquivalent(
            new[] { "count", "contents", "0", "1", "5" },
            wakes,
            "every reader is woken once, whether or not its own answer actually changed");
    }
    [TestMethod]
    public void AReaderIsWokenByEveryChangeAndNotOnlyTheFirst()
    {
        // The mark that keeps one delivery to one raise per entry is per delivery: a flag left set after
        // a delivery would silence that reader for every change after it.
        using var list = Observable.CreateList("a");
        var wakes = 0;

        using var run = Observable.CollectDependencies(() => { _ = list[0]; }, _ => wakes++);

        list[0] = "b";
        list[0] = "c";
        list.Insert(0, "d");

        Assert.AreEqual(3, wakes, "three changes, three wakes");
    }

    [TestMethod]
    public void AReaderThatStopsReadingStopsHearing()
    {
        using var list = Observable.CreateList("a", "b");
        var wakes = 0;

        var run = Observable.CollectDependencies(() => { _ = list[0]; }, _ => wakes++);
        list[0] = "b";
        run.Dispose();
        list[0] = "c";

        Assert.AreEqual(1, wakes, "disposing the run unsubscribed from the index");
    }

    [TestMethod]
    public void AReadOutsideARunRegistersNothing()
    {
        using var list = Observable.CreateList("a", "b");

        // No run is active, so these publish nothing and create no entry - which is what keeps a model
        // read in a test or a console host as cheap as it was before this feature existed.
        _ = list.Count;
        _ = list[0];
        _ = list[1];
        _ = list.Contains("a");

        using var run = Observable.CollectDependencies(() => { _ = list[1]; }, _ => { });

        Assert.AreEqual(1, run.Dependencies.Count, "and the first read inside a run is still one dependency");
    }
}
