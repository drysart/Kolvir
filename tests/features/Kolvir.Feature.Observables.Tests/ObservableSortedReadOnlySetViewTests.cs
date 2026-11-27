namespace Kolvir.Feature.Observables.Tests;

/// <summary>
/// A sorted read-only view over a set: an order-statistic AVL tree that serves the indexer, insert and
/// remove at <c>O(log n)</c>, with ties broken by an arrival number, a per-element number map so a
/// removal finds its own node, and one source event translated sequentially into one list event
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 6.7).
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class ObservableSortedReadOnlySetViewTests
{
    [TestMethod]
    public void ConstructionOrdersTheSourceWithTheDefaultComparer()
    {
        using var source = Observable.CreateHashSet("c", "a", "b");
        using var view = new ObservableSortedReadOnlySetView<string>(source);

        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, view.ToArray(), "the type's own order");
        Assert.AreEqual(3, view.Count);
        view.ValidateTree();
    }

    [TestMethod]
    public void ConstructionOrdersTheSourceWithTheSuppliedComparer()
    {
        using var source = Observable.CreateHashSet("b", "a", "c");
        using var view = new ObservableSortedReadOnlySetView<string>(source, new ReverseComparer());

        CollectionAssert.AreEqual(new[] { "c", "b", "a" }, view.ToArray(), "the order the comparer decides");
        view.ValidateTree();
    }

    [TestMethod]
    public void AnAddIsInsertedWhereTheOrderPutsItAndReportsThatIndex()
    {
        using var source = Observable.CreateHashSet<string>();
        using var view = new ObservableSortedReadOnlySetView<string>(source);

        var events = new List<IReadOnlyList<ObservableListChange<string>>>();
        view.AddObservableListChangedHandler(args => events.Add([.. args.Changes]));

        source.Add("m");

        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(ObservableListChangeType.ItemAdded, events[0][0].ChangeType);
        Assert.AreEqual(0, events[0][0].NewIndex, "the only item, so at the start");
        CollectionAssert.AreEqual(new[] { "m" }, view.ToArray());

        source.Add("a");

        Assert.AreEqual(2, events.Count);
        Assert.AreEqual(0, events[1][0].NewIndex, "before everything already there");
        CollectionAssert.AreEqual(new[] { "a", "m" }, view.ToArray());

        source.Add("z");

        Assert.AreEqual(2, events[2][0].NewIndex, "at the end");
        CollectionAssert.AreEqual(new[] { "a", "m", "z" }, view.ToArray());

        source.Add("n");

        Assert.AreEqual(2, events[3][0].NewIndex, "between the two it sorts between");
        CollectionAssert.AreEqual(new[] { "a", "m", "n", "z" }, view.ToArray());

        view.ValidateTree();
    }

    [TestMethod]
    public void TwoItemsThatCompareEqualAreBothHeldInArrivalOrder()
    {
        using var source = Observable.CreateHashSet<string>();
        using var view = new ObservableSortedReadOnlySetView<string>(source, StringComparer.OrdinalIgnoreCase);

        source.Add("b");
        source.Add("a");
        source.Add("A");

        Assert.AreEqual(3, view.Count, "both tied items are held, so the count follows the source");
        CollectionAssert.AreEqual(
            new[] { "a", "A", "b" },
            view.ToArray(),
            "the arrival number separates them, so ties keep arrival order");
        view.ValidateTree();
    }

    [TestMethod]
    public void RemovingOneOfTwoTiedItemsTakesTheOneTheSourceNames()
    {
        // The ordering says "a" and "A" are the same position; the source's identity says they are two
        // items, and the number map is what picks the right node out of the tie group.
        using var source = Observable.CreateHashSet<string>();
        using var view = new ObservableSortedReadOnlySetView<string>(source, StringComparer.OrdinalIgnoreCase);

        var events = new List<IReadOnlyList<ObservableListChange<string>>>();
        view.AddObservableListChangedHandler(args => events.Add([.. args.Changes]));

        source.Add("a");
        source.Add("A");

        CollectionAssert.AreEqual(new[] { "a", "A" }, view.ToArray());

        var delivered = events.Count;

        source.Remove("A");

        Assert.AreEqual(delivered + 1, events.Count, "one source record, one delivery");
        Assert.AreEqual(1, events[^1][0].OldIndex, "the second of the tied pair, which is the one named");
        Assert.AreEqual("A", events[^1][0].OldItem);
        CollectionAssert.AreEqual(new[] { "a" }, view.ToArray(), "the other tied item remains");

        source.Add("A");
        source.Remove("a");

        Assert.AreEqual(0, events[^1][0].OldIndex, "and now the first one, which is now the one named");
        CollectionAssert.AreEqual(new[] { "A" }, view.ToArray(), "leaving the other instance");
        view.ValidateTree();
    }

    [TestMethod]
    public void ARemovedAndReAddedItemIsTheNewestAmongItsTies()
    {
        // The number is forgotten on removal, so an item that comes back sorts after the ties that
        // stayed. This is the documented rule, and it is the visible consequence of not keeping the
        // number forever.
        using var source = Observable.CreateHashSet<string>();
        using var view = new ObservableSortedReadOnlySetView<string>(source, StringComparer.OrdinalIgnoreCase);

        source.Add("a");
        source.Add("A");
        source.Add("a2");

        CollectionAssert.AreEqual(new[] { "a", "A", "a2" }, view.ToArray(), "two ties, then the next key");

        source.Remove("a");
        source.Add("a");

        CollectionAssert.AreEqual(
            new[] { "A", "a", "a2" },
            view.ToArray(),
            "the re-added item is now the newest of the tied pair");
        view.ValidateTree();
    }

    [TestMethod]
    public void AClearEmptiesTheViewAndLeavesItUsable()
    {
        using var source = Observable.CreateHashSet("b", "a");
        using var view = Sorted(source);

        var events = new List<IReadOnlyList<ObservableListChange<string>>>();
        view.AddObservableListChangedHandler(args => events.Add([.. args.Changes]));

        source.Clear();

        Assert.AreEqual(0, view.Count);
        Assert.AreEqual(1, events.Count, "a clear is one record");
        Assert.AreEqual(ObservableListChangeType.ListCleared, events[0][0].ChangeType, "carrying nothing");
        view.ValidateTree();

        source.Add("c");

        CollectionAssert.AreEqual(new[] { "c" }, view.ToArray(), "the view still follows the source afterwards");
        view.ValidateTree();
    }

    [TestMethod]
    public void AnUntrackedEventRebuildsTheOrderedContentsFromTheSource()
    {
        // Past the source's default cap its event drops what it collected and says only that things
        // changed, so the view rebuilds - in order - rather than patching what it cannot describe.
        using var source = Observable.CreateHashSet<string>();
        using var view = Sorted(source);

        var events = new List<IReadOnlyList<ObservableListChange<string>>>();
        view.AddObservableListChangedHandler(args => events.Add([.. args.Changes]));

        source.BeginChangeBatch();

        for (var index = 0; index <= Observable.Default.MaxCollectionBatchSize; index++)
        {
            source.Add($"x{index}");
        }

        source.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "one inner event, one delivery");
        Assert.AreEqual(1, events[0].Count, "carrying the record that says it was not described");
        Assert.AreEqual(ObservableListChangeType.UntrackedChanges, events[0][0].ChangeType);
        Assert.AreEqual(Observable.Default.MaxCollectionBatchSize + 1, view.Count, "and every item was rebuilt");
        CollectionAssert.AreEqual(
            source.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
            view.ToArray(),
            "in the view's order");
        view.ValidateTree();
    }

    [TestMethod]
    public void ASourceEventWithSeveralChangesIsTranslatedSequentiallyIntoOneDelivery()
    {
        // Each record has to be applied to the tree as the previous one left it. The ranks below are the
        // ones sequential translation produces; computing them against the contents the event began with
        // would give 0, 2 and 1.
        using var source = Observable.CreateHashSet("m", "t");
        using var view = Sorted(source);

        var events = new List<IReadOnlyList<ObservableListChange<string>>>();
        view.AddObservableListChangedHandler(args => events.Add([.. args.Changes]));

        source.BeginChangeBatch();
        source.Add("a");
        source.Add("z");
        source.Remove("t");
        source.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "three source records, one delivery from the view");
        Assert.AreEqual(3, events[0].Count, "carrying all three");

        Assert.AreEqual(ObservableListChangeType.ItemAdded, events[0][0].ChangeType);
        Assert.AreEqual(0, events[0][0].NewIndex);
        Assert.AreEqual("a", events[0][0].NewItem);

        Assert.AreEqual(ObservableListChangeType.ItemAdded, events[0][1].ChangeType);
        Assert.AreEqual(3, events[0][1].NewIndex, "against the tree the previous change left");
        Assert.AreEqual("z", events[0][1].NewItem);

        Assert.AreEqual(ObservableListChangeType.ItemRemoved, events[0][2].ChangeType);
        Assert.AreEqual(2, events[0][2].OldIndex, "against the tree the previous change left");
        Assert.AreEqual("t", events[0][2].OldItem);

        CollectionAssert.AreEqual(new[] { "a", "m", "z" }, view.ToArray());
        view.ValidateTree();
    }

    [TestMethod]
    public void TheDeliveredRecordsNameTheViewAndReplayIntoAMirror()
    {
        using var source = Observable.CreateHashSet("b", "d");
        using var view = Sorted(source);

        var mirror = view.ToList();
        ObservableListChangedEventArgs<string>? last = null;

        view.AddObservableListChangedHandler(args =>
        {
            last = args;

            foreach (var change in args.Changes)
            {
                switch (change.ChangeType)
                {
                    case ObservableListChangeType.ItemAdded:
                        mirror.Insert(change.NewIndex!.Value, change.NewItem!);
                        break;
                    case ObservableListChangeType.ItemRemoved:
                        mirror.RemoveAt(change.OldIndex!.Value);
                        break;
                    case ObservableListChangeType.ListCleared:
                        mirror.Clear();
                        break;
                    default:
                        mirror.Clear();
                        mirror.AddRange(view);
                        break;
                }
            }
        });

        source.Add("a");

        Assert.AreSame(view, last!.List, "the view names itself as the list, not the source");
        CollectionAssert.AreEqual(view.ToArray(), mirror, "the record replays into a mirror");

        source.Remove("b");
        CollectionAssert.AreEqual(view.ToArray(), mirror, "and so does a removal");

        source.Clear();
        CollectionAssert.AreEqual(view.ToArray(), mirror, "and a clear");
    }

    [TestMethod]
    public void AReadOfOneIndexIsWokenOnlyByAChangeThatCanMoveIt()
    {
        using var source = Observable.CreateHashSet("a");
        using var view = Sorted(source);

        var first = 0;
        var third = 0;

        using var firstRun = Observable.CollectDependencies(() => { _ = view[0]; }, _ => first++);
        using var thirdRun = Observable.CollectDependencies(() => { _ = view[2]; }, _ => third++);

        source.Add("b");

        Assert.AreEqual(0, first, "the item at 0 did not move");
        Assert.AreEqual(0, third, "and index 2 is still past the end");

        source.Add("c");

        Assert.AreEqual(0, first);
        Assert.AreEqual(1, third, "the read that threw is re-run when the index exists");
    }

    [TestMethod]
    public void EnumeratingAMutatingViewThrows()
    {
        using var source = Observable.CreateHashSet("b", "a");
        using var view = Sorted(source);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            foreach (var item in view)
            {
                _ = item;
                source.Add("c");
            }
        });
    }

    [TestMethod]
    public void AGrowingAndShrinkingTreeCostsLogarithmicallyManyComparisons()
    {
        // Deterministic, unlike a timing test: the user comparer is wrapped and its calls are counted.
        // An insert or a removal that walked the items would count one comparison per item; the tree
        // counts one per level, and the bound is a generous multiple of log2(n).
        var comparer = new CountingComparer(StringComparer.Ordinal);

        using var source = Observable.CreateHashSet<string>();

        for (var index = 0; index < 1023; index++)
        {
            source.Add($"i{index:D4}");
        }

        using var view = new ObservableSortedReadOnlySetView<string>(source, comparer);

        Assert.AreEqual(1023, view.Count);

        comparer.Count = 0;
        source.Add("i0511x");

        var inserted = comparer.Count;

        comparer.Count = 0;
        source.Remove("i0511x");

        var removed = comparer.Count;

        var budget = (2 * (int)Math.Ceiling(Math.Log2(view.Count + 1))) + 4;

        Assert.IsTrue(
            inserted <= budget,
            $"an insert compared {inserted} times, which is more than the {budget} a logarithmic descent allows");

        Assert.IsTrue(
            removed <= budget,
            $"a removal compared {removed} times, which is more than the {budget} a logarithmic descent allows");
    }

    [TestMethod]
    [DataRow(20261126)]
    [DataRow(7)]
    [DataRow(424242)]
    public void RandomOperationsMatchASortedReferenceAndKeepEveryInvariant(int seed)
    {
        // The tree is the part of this view a handful of examples cannot prove. A reference model that
        // sorts the arrival list on every step is independent of the tree, and ValidateTree checks the
        // sizes, the heights, the in-order (comparer, number) sequence and the identity map after each
        // operation, so a bad rotation or a stale number fails where it happened.
        var random = new Random(seed);

        using var source = Observable.CreateHashSet<string>();
        using var view = new ObservableSortedReadOnlySetView<string>(source, StringComparer.OrdinalIgnoreCase);

        var arrival = new List<string>();
        var pool = new List<string>();

        for (var index = 0; index < 150; index++)
        {
            pool.Add($"k{index}");
            pool.Add($"K{index}");
        }

        for (var step = 0; step < 2500; step++)
        {
            var choice = random.Next(100);

            if (arrival.Count == 0 || choice < 55)
            {
                var candidate = pool[random.Next(pool.Count)];

                if (source.Add(candidate))
                {
                    arrival.Add(candidate);
                }
            }
            else if (choice < 98)
            {
                var index = random.Next(arrival.Count);
                var item = arrival[index];

                arrival.RemoveAt(index);
                source.Remove(item);
            }
            else
            {
                source.Clear();
                arrival.Clear();
            }

            AssertViewMatches(view, source, arrival, step);
        }

        Assert.IsTrue(arrival.Count > 0, "the run did not simply empty the set and stop");
    }

    [TestMethod]
    public void DisposingUnsubscribesFromTheSourceAndLeavesItUsable()
    {
        using var source = Observable.CreateHashSet("b", "a");
        var view = Sorted(source);

        var viewEvents = new List<ObservableListChangedEventArgs<string>>();
        view.AddObservableListChangedHandler(viewEvents.Add);

        var sourceEvents = 0;
        using var sourceToken = source.AddObservableSetChangedHandler(_ => sourceEvents++);

        view.Dispose();

        Assert.AreEqual(1, viewEvents.Count, "one last event, so a registrant learns to let go");
        Assert.AreEqual(0, viewEvents[0].Changes.Count, "which carries no records");
        Assert.IsTrue(viewEvents[0].List.IsDisposed, "and says the view is dead");

        source.Add("c");

        Assert.AreEqual(1, viewEvents.Count, "the view unsubscribed, so the source's change reached nobody");
        Assert.AreEqual(1, sourceEvents, "the source still calls its own handlers");
        Assert.IsFalse(source.IsDisposed, "the view did not dispose it");
        Assert.AreEqual(3, source.Count, "and it is untouched and still readable");
        Assert.IsTrue(source.Contains("c"));
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = view.Count, "while the view itself is dead");
    }

    [TestMethod]
    public void DisposingTwiceAndRemovingAHandlerAfterwardsStaySafe()
    {
        using var source = Observable.CreateHashSet("a");
        var view = Sorted(source);

        view.Dispose();
        view.Dispose();
        view.RemoveObservableListChangedHandler(_ => { });

        Assert.IsTrue(view.IsDisposed, "teardown paths release what they hold without knowing what state it is in");
    }

    [TestMethod]
    public void ADisposedSourceLeavesTheViewReadableWithItsLastContents()
    {
        var source = Observable.CreateHashSet("b", "a");
        var view = Sorted(source);

        var events = 0;
        view.AddObservableListChangedHandler(_ => events++);

        source.Dispose();

        Assert.AreEqual(0, events, "the source's final event is not a change to report");
        Assert.IsFalse(view.IsDisposed, "the view's lifetime is its own");
        CollectionAssert.AreEqual(new[] { "a", "b" }, view.ToArray(), "and it still holds what it last had");

        view.Dispose();

        Assert.IsTrue(view.IsDisposed, "disposing a view whose source is gone is safe");
    }

    [TestMethod]
    public void ADisposedSourceIsRefusedByTheConstructor()
    {
        var source = Observable.CreateHashSet("a");
        source.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(
            () => _ = new ObservableSortedReadOnlySetView<string>(source));
    }

    [TestMethod]
    public void ANullSourceIsRefused()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => _ = new ObservableSortedReadOnlySetView<string>(null!));
    }

    private static ObservableSortedReadOnlySetView<string> Sorted(IReadOnlyObservableSet<string> source) =>
        new(source);

    private static void AssertViewMatches(
        ObservableSortedReadOnlySetView<string> view,
        IReadOnlyObservableSet<string> source,
        List<string> arrival,
        int step)
    {
        var expected = arrival.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();

        Assert.AreEqual(source.Count, view.Count, $"count after step {step}");
        Assert.AreEqual(expected.Length, view.Count, $"reference count after step {step}");

        for (var index = 0; index < expected.Length; index++)
        {
            if (!StringComparer.Ordinal.Equals(expected[index], view[index]))
            {
                Assert.Fail(
                    $"step {step}: index {index} is '{view[index]}' but the reference has '{expected[index]}'");
            }
        }

        CollectionAssert.AreEqual(expected, view.ToArray(), $"contents after step {step}");
        view.ValidateTree();
    }

    private sealed class ReverseComparer : IComparer<string>
    {
        public int Compare(string? x, string? y) => StringComparer.Ordinal.Compare(y, x);
    }

    private sealed class CountingComparer : IComparer<string>
    {
        private readonly IComparer<string> _inner;

        internal CountingComparer(IComparer<string> inner) => _inner = inner;

        internal int Count { get; set; }

        public int Compare(string? x, string? y)
        {
            Count++;
            return _inner.Compare(x, y);
        }
    }
}
