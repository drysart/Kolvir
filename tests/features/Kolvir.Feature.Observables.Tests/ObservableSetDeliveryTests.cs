namespace Kolvir.Observables.Tests;

/// <summary>
/// A set's delivery: the implicit batch every bulk operation runs in, the cap, the queue that keeps a
/// handler's own changes in order, and disposal
/// (dev-docs/OBSERVABLE-COLLECTIONS.md sections 6.2.2, 3.3, 3.7, 3.9 and 3.11).
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class ObservableSetDeliveryTests
{
    [TestMethod]
    public void ABatchIsOneEventWithAllTheRecordsInOrder()
    {
        using var set = Observable.CreateHashSet("a");
        var events = new List<IReadOnlyList<ObservableSetChange<string>>>();

        set.AddObservableSetChangedHandler(args => events.Add([.. args.Changes]));

        set.BeginChangeBatch();
        set.Add("b");
        set.Remove("a");
        set.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "two mutations, one event");
        Assert.AreEqual(2, events[0].Count, "carrying two records");
        Assert.AreEqual(ObservableSetChangeType.ItemAdded, events[0][0].ChangeType, "in the order they happened");
        Assert.AreEqual(ObservableSetChangeType.ItemRemoved, events[0][1].ChangeType);
    }

    [TestMethod]
    public void ABulkOperationIsOneEventWithOneRecordPerItem()
    {
        using var set = Observable.CreateHashSet<string>();
        var events = new List<IReadOnlyList<ObservableSetChange<string>>>();

        set.AddObservableSetChangedHandler(args => events.Add([.. args.Changes]));

        set.UnionWith(["a", "b", "c"]);

        Assert.AreEqual(1, events.Count, "three items, one event");
        Assert.AreEqual(3, events[0].Count, "with a record each");
        CollectionAssert.AreEqual(
            new[] { "a", "b", "c" },
            events[0].Select(change => change.Item).ToList(),
            "in the argument's order");
    }

    [TestMethod]
    public void ABulkOperationThatChangesNothingRaisesNothing()
    {
        using var set = Observable.CreateHashSet("a", "b");
        var events = 0;

        set.AddObservableSetChangedHandler(args => events += args.Changes.Count);

        set.UnionWith(["a"]);                                   // a subset of what is there
        set.ExceptWith([]);                                     // nothing to take away
        set.IntersectWith(["a", "b", "c"]);                     // a superset, so nothing leaves
        set.SymmetricExceptWith([]);                            // nothing on either side

        Assert.AreEqual(0, events, "four calls, none of them a change");
        Assert.AreEqual(2, set.Count);
    }

    [TestMethod]
    public void ABulkOperationInsideTheCallersBatchJoinsIt()
    {
        // The implicit batch is the same mechanism a caller uses, so nesting it does not deliver: the
        // caller's end does.
        using var set = Observable.CreateHashSet("a");
        var events = new List<int>();

        set.AddObservableSetChangedHandler(args => events.Add(args.Changes.Count));

        set.BeginChangeBatch();
        set.UnionWith(["b", "c"]);
        set.Add("d");

        Assert.AreEqual(0, events.Count, "nothing delivered inside the caller's batch");

        set.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "one event for the whole of it");
        Assert.AreEqual(3, events[0], "carrying what the bulk operation and the add did - the contents the set was created with were never a change");
    }

    [TestMethod]
    public void ASymmetricDifferenceReportsRemovalsBeforeAdditions()
    {
        // So a replay never has to "add an item that is already there": applying the records in order
        // ends with the set it would have had.
        using var set = Observable.CreateHashSet("a", "b");
        var records = new List<ObservableSetChange<string>>();

        set.AddObservableSetChangedHandler(args => records.AddRange(args.Changes));

        set.SymmetricExceptWith(["b", "c"]);

        Assert.AreEqual(2, records.Count, "b left and c arrived");
        Assert.AreEqual(ObservableSetChangeType.ItemRemoved, records[0].ChangeType, "the removal first");
        Assert.AreEqual("b", records[0].Item);
        Assert.AreEqual(ObservableSetChangeType.ItemAdded, records[1].ChangeType);
        Assert.AreEqual("c", records[1].Item);
    }

    [TestMethod]
    public void ASymmetricDifferenceWithTheSetItselfEmptiesIt()
    {
        // A symmetric difference with itself is the empty set, which is what a set means by it - and it is
        // a clear, not seventeen removals, because every item went at once.
        using var set = Observable.CreateHashSet("a", "b");
        var events = new List<ObservableSetChange<string>>();

        set.AddObservableSetChangedHandler(args => events.AddRange(args.Changes));

        set.SymmetricExceptWith(set);

        Assert.AreEqual(0, set.Count, "everything gone");
        Assert.AreEqual(1, events.Count, "one record");
        Assert.AreEqual(ObservableSetChangeType.SetCleared, events[0].ChangeType);
    }

    [TestMethod]
    public void IntertwiningWithItselfRaisesNothingAndKeepsEverything()
    {
        using var set = Observable.CreateHashSet("a", "b");
        var events = 0;

        set.AddObservableSetChangedHandler(args => events += args.Changes.Count);

        set.IntersectWith(set);
        set.UnionWith(set);

        Assert.AreEqual(0, events);
        Assert.AreEqual(2, set.Count);
    }

    [TestMethod]
    public void ABatchUpToTheLimitKeepsEveryRecord()
    {
        using var set = Observable.CreateHashSet<int>();
        var events = new List<int>();

        set.AddObservableSetChangedHandler(args => events.Add(args.Changes.Count));

        set.BeginChangeBatch();

        for (var item = 0; item < Observable.Default.MaxCollectionBatchSize; item++)
        {
            set.Add(item);
        }

        set.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "fifty changes, one event");
        Assert.AreEqual(Observable.Default.MaxCollectionBatchSize, events[0], "carrying all fifty records");
        Assert.AreEqual(Observable.Default.MaxCollectionBatchSize, set.Count, "and every one of them applied");
    }

    [TestMethod]
    public void ABatchPastTheLimitBecomesOneUntrackedRecord()
    {
        using var set = Observable.CreateHashSet<int>();
        var events = new List<IReadOnlyList<ObservableSetChange<int>>>();

        set.AddObservableSetChangedHandler(args => events.Add([.. args.Changes]));

        set.BeginChangeBatch();

        for (var item = 0; item <= Observable.Default.MaxCollectionBatchSize; item++)
        {
            set.Add(item);
        }

        set.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "still one event");
        Assert.AreEqual(1, events[0].Count, "carrying one record rather than fifty-one");
        Assert.AreEqual(ObservableSetChangeType.UntrackedChanges, events[0][0].ChangeType);
        Assert.AreEqual(Observable.Default.MaxCollectionBatchSize + 1, set.Count, "the set itself is complete");
    }

    [TestMethod]
    public void ABulkOperationPastTheLimitIsCappedToo()
    {
        // The cap belongs to the event, not to the caller's batch: a union of ten thousand items is one
        // record and one rescan, which is the whole reason the implicit batch is capped.
        using var set = Observable.CreateHashSet<int>();
        var events = new List<IReadOnlyList<ObservableSetChange<int>>>();

        set.AddObservableSetChangedHandler(args => events.Add([.. args.Changes]));

        set.UnionWith(Enumerable.Range(0, Observable.Default.MaxCollectionBatchSize + 10));

        Assert.AreEqual(1, events.Count, "one event");
        Assert.AreEqual(1, events[0].Count, "described as untracked rather than item by item");
        Assert.AreEqual(ObservableSetChangeType.UntrackedChanges, events[0][0].ChangeType);
        Assert.AreEqual(Observable.Default.MaxCollectionBatchSize + 10, set.Count, "while the set took all of them");
    }

    [TestMethod]
    public void ASetUsesItsOwnCap()
    {
        using var set = Observable.CreateHashSet<int>();
        var events = new List<int>();

        set.MaxCollectionBatchSize = 3;
        set.AddObservableSetChangedHandler(args => events.Add(args.Changes.Count));

        set.BeginChangeBatch();
        set.Add(1);
        set.Add(2);
        set.Add(3);
        set.Add(4);
        set.EndChangeBatch();

        Assert.AreEqual(1, events[0], "four changes over a cap of three gave up on detail");
    }

    [TestMethod]
    public void AChangeFromInsideAHandlerIsDeliveredAfterTheEvent()
    {
        // Delivering is a queue, not a nested raise: an observer applying records in order must never see
        // one out of order.
        using var set = Observable.CreateHashSet("a");
        var first = new List<string>();
        var second = new List<string>();

        set.AddObservableSetChangedHandler(args =>
        {
            if (args.Changes.Count == 0)
            {
                return;
            }

            first.Add(args.Changes[0].Item!);

            if (args.Changes[0].Item == "b")
            {
                set.Add("c");
            }
        });

        set.AddObservableSetChangedHandler(args =>
        {
            foreach (var change in args.Changes)
            {
                second.Add($"{change.ChangeType}:{change.Item}");
            }
        });

        set.Add("b");

        CollectionAssert.AreEqual(
            new[] { "ItemAdded:b", "ItemAdded:c" },
            second,
            "the second handler saw the first change before the one it caused");
    }

    [TestMethod]
    public void ABatchLeftOpenIsDroppedByDisposal()
    {
        using var set = Observable.CreateHashSet("a");
        var events = new List<int>();

        set.AddObservableSetChangedHandler(args => events.Add(args.Changes.Count));

        set.BeginChangeBatch();
        set.Add("b");
        set.Clear();

        set.Dispose();

        Assert.AreEqual(1, events.Count, "only the final event, with nothing in it");
        Assert.AreEqual(0, events[0], "the records a dead set had not delivered are dropped");
    }

    [TestMethod]
    public void DisposalSaysSoOnceAndLeavesNothingToRegisterOn()
    {
        var set = Observable.CreateHashSet("a", "b");
        var events = new List<ObservableSetChangedEventArgs<string>>();
        Action<ObservableSetChangedEventArgs<string>> handler = events.Add;

        using var token = set.AddObservableSetChangedHandler(handler);

        set.Dispose();

        Assert.AreEqual(1, events.Count, "one last event");
        Assert.AreEqual(0, events[0].Changes.Count, "with no changes");
        Assert.IsTrue(events[0].Set.IsDisposed, "and the set already says it is dead");

        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = set.Count);
        Assert.ThrowsExactly<ObjectDisposedException>(() => set.Add("c"));
        Assert.ThrowsExactly<ObjectDisposedException>(() => set.AddObservableSetChangedHandler(_ => { }));

        set.RemoveObservableSetChangedHandler(handler);
        token.Dispose();
        set.Dispose();
    }

    [TestMethod]
    public void AnUnmatchedEndChangeBatchThrowsAndLeavesTheSetUsable()
    {
        using var set = Observable.CreateHashSet("a");

        Assert.ThrowsExactly<InvalidOperationException>(() => set.EndChangeBatch());

        set.Add("b");

        Assert.AreEqual(2, set.Count, "the failed end did not leave a batch open");
    }
}
