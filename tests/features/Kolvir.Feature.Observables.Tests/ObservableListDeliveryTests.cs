namespace Kolvir.Observables.Tests;

/// <summary>
/// An observable list's delivery: batches, the queue that keeps a handler's own changes in order, the
/// loop guard, and disposal (dev-docs/OBSERVABLE-COLLECTIONS.md sections 3.3, 3.7 and 3.9).
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class ObservableListDeliveryTests
{
    [TestMethod]
    public void ABatchIsOneEventWithAllTheRecordsInOrder()
    {
        using var list = Observable.CreateList("a");
        var events = new List<IReadOnlyList<ObservableListChange<string>>>();

        list.AddObservableListChangedHandler(args => events.Add([.. args.Changes]));

        list.BeginChangeBatch();
        list.Add("b");
        list.Insert(0, "c");
        list.Remove("a");
        list.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "three mutations, one event");
        Assert.AreEqual(3, events[0].Count, "carrying three records");
        Assert.AreEqual(ObservableListChangeType.ItemAdded, events[0][0].ChangeType, "in the order they happened");
        Assert.AreEqual(1, events[0][0].NewIndex!.Value, "the add went to the end");
        Assert.AreEqual(0, events[0][1].NewIndex!.Value, "then the insert went to the front");
        Assert.AreEqual(ObservableListChangeType.ItemRemoved, events[0][2].ChangeType, "then the removal");
        Assert.AreEqual(1, events[0][2].OldIndex!.Value, "which found a at its shifted index");
    }

    [TestMethod]
    public void ABatchThatChangedNothingDeliversNothing()
    {
        using var list = Observable.CreateList("a");
        var events = 0;

        list.AddObservableListChangedHandler(_ => events++);

        list.BeginChangeBatch();
        list.EndChangeBatch();

        Assert.AreEqual(0, events, "a batch around no work is no notification");
    }

    [TestMethod]
    public void NestedBatchesDeliverOnceAtTheOutermostEnd()
    {
        using var list = Observable.CreateList<int>();
        var events = new List<IReadOnlyList<ObservableListChange<int>>>();

        list.AddObservableListChangedHandler(args => events.Add([.. args.Changes]));

        list.BeginChangeBatch();
        list.Add(1);
        list.BeginChangeBatch();
        list.Add(2);
        list.EndChangeBatch();

        Assert.AreEqual(0, events.Count, "the inner end delivers nothing while the outer batch is open");
        Assert.AreEqual(1, list.ChangeBatchDepth, "one batch is still open");

        list.Add(3);
        list.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "the outermost end delivers once");
        Assert.AreEqual(3, events[0].Count, "with everything both batches collected");
        Assert.AreEqual(0, list.ChangeBatchDepth, "and nothing is open afterwards");
    }

    [TestMethod]
    public void AnUnmatchedEndChangeBatchThrowsAndLeavesTheListUsable()
    {
        using var list = Observable.CreateList<int>();
        var events = 0;

        list.AddObservableListChangedHandler(_ => events++);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => list.EndChangeBatch(),
            "ending a batch nobody began is a bug, and a silent no-op would hide it");

        list.Add(1);

        Assert.AreEqual(1, events, "and the list still works");
        Assert.AreEqual(0, list.ChangeBatchDepth, "with no batch open");
    }

    [TestMethod]
    public void AChangeMadeFromInsideAHandlerIsDeliveredAfterTheEventBeingDelivered()
    {
        // The ordering guarantee: an observer applies records in sequence, so a nested raise would hand
        // the second handler a record that happens after a change it has not been told about yet.
        using var list = Observable.CreateList("a");
        var second = new List<string>();

        list.AddObservableListChangedHandler(args =>
        {
            // A disposal delivers one final event with no records, so a handler checks first.
            if (args.Changes.Count == 0)
            {
                return;
            }

            if (args.Changes[0].ChangeType == ObservableListChangeType.ItemAdded
                && args.Changes[0].NewItem == "b")
            {
                list.Add("c");
            }
        });

        list.AddObservableListChangedHandler(args =>
        {
            foreach (var change in args.Changes)
            {
                second.Add($"{change.ChangeType}:{change.NewItem}");
            }
        });

        list.Add("b");

        CollectionAssert.AreEqual(
            new[] { "ItemAdded:b", "ItemAdded:c" },
            second,
            "the second handler saw the first change before the one it caused");
    }

    [TestMethod]
    public void ALoopIsRefusedAtMaxEventStackSizeGenerations()
    {
        var previous = Observable.MaxEventStackSize;
        Observable.MaxEventStackSize = 5;

        try
        {
            using var list = Observable.CreateList<int>();

            // One item for every item: each delivery causes the next, so the chain never ends.
            list.AddObservableListChangedHandler(args =>
            {
                if (args.Changes.Count > 0
                    && args.Changes[0].ChangeType == ObservableListChangeType.ItemAdded
                    && args.Changes[0].NewItem < 100)
                {
                    list.Add(args.Changes[0].NewItem + 1);
                }
            });

            var refused = Assert.ThrowsExactly<InvalidOperationException>(
                () => list.Add(0),
                "a loop is refused rather than delivered until the process runs out of something");

            StringAssert.Contains(
                refused.Message,
                "MaxEventStackSize",
                "and the message names the limit, which is the thing a caller can change");

            Observable.MaxEventStackSize = 50;
            var delivered = 0;
            list.AddObservableListChangedHandler(_ => delivered++);
            list.Add(101);

            Assert.IsTrue(delivered > 0, "an ordinary change afterwards still delivers, so nothing was poisoned");
        }
        finally
        {
            Observable.MaxEventStackSize = previous;
        }
    }

    [TestMethod]
    public void DisposalDeliversOneEmptyEventAndThenThrows()
    {
        var list = Observable.CreateList("a");
        var events = new List<ObservableListChangedEventArgs<string>>();

        list.AddObservableListChangedHandler(events.Add);
        list.Dispose();

        Assert.AreEqual(1, events.Count, "one last event, so a registrant learns to let go");
        Assert.AreEqual(0, events[0].Changes.Count, "which carries no records");
        Assert.IsTrue(events[0].List.IsDisposed, "and says the list is dead");

        Assert.IsTrue(list.IsDisposed, "the flag");
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = list.Count, "a read");
        Assert.ThrowsExactly<ObjectDisposedException>(() => list.Add("b"), "a write");
        Assert.ThrowsExactly<ObjectDisposedException>(
            () => list.AddObservableListChangedHandler(_ => { }),
            "a registration");

        list.RemoveObservableListChangedHandler(events.Add);
        list.Dispose();

        Assert.IsTrue(list.IsDisposed, "removing a handler and disposing again stay safe");
    }

    [TestMethod]
    public void DisposalDropsWhatABatchHadNotDelivered()
    {
        var list = Observable.CreateList<int>();
        var events = new List<IReadOnlyList<ObservableListChange<int>>>();

        list.AddObservableListChangedHandler(args => events.Add([.. args.Changes]));

        list.BeginChangeBatch();
        list.Add(1);
        list.Dispose();

        Assert.AreEqual(1, events.Count, "only the final empty event");
        Assert.AreEqual(0, events[0].Count, "the batch's records were dropped with the list");
    }

    [TestMethod]
    public void AHandlerCanUnsubscribeItselfDuringADelivery()
    {
        using var list = Observable.CreateList<int>();
        var calls = 0;
        IDisposable? token = null;

        token = list.AddObservableListChangedHandler(_ =>
        {
            calls++;
            token!.Dispose();
        });

        list.Add(1);
        list.Add(2);

        Assert.AreEqual(1, calls, "the token it disposed belongs to the next event");
    }

    [TestMethod]
    public void AHandlerRegisteredDuringADeliveryHearsTheNextOne()
    {
        using var list = Observable.CreateList<int>();
        var late = 0;
        var registered = false;

        list.AddObservableListChangedHandler(_ =>
        {
            if (registered)
            {
                return;
            }

            registered = true;
            list.AddObservableListChangedHandler(args =>
            {
                if (args.Changes.Count > 0 && args.Changes[0].NewItem == 2)
                {
                    late++;
                }
            });
        });

        list.Add(1);

        Assert.AreEqual(0, late, "the registration is for the next delivery");

        list.Add(2);

        Assert.AreEqual(1, late, "which is the one after it");
    }
}
