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
    public void ABatchUpToTheLimitKeepsEveryRecord()
    {
        // The records are the payload of one event, so the batch size is what decides whether an
        // observer replays or rescans - not how many notifications it gets.
        using var list = Observable.CreateList<int>();
        var events = new List<IReadOnlyList<ObservableListChange<int>>>();

        list.AddObservableListChangedHandler(args => events.Add([.. args.Changes]));

        list.BeginChangeBatch();

        for (var index = 0; index < Observable.MaxBatchRecords; index++)
        {
            list.Add(index);
        }

        list.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "fifty changes, one event");
        Assert.AreEqual(Observable.MaxBatchRecords, events[0].Count, "carrying all fifty records");
        Assert.AreEqual(Observable.MaxBatchRecords, list.Count, "and every one of them applied");
    }

    [TestMethod]
    public void ABatchPastTheLimitBecomesOneUntrackedRecord()
    {
        // One record too many is where replaying stops being cheaper than re-reading, so the batch drops
        // what it collected and says only that things changed.
        using var list = Observable.CreateList<int>();
        var events = new List<IReadOnlyList<ObservableListChange<int>>>();

        list.AddObservableListChangedHandler(args => events.Add([.. args.Changes]));

        list.BeginChangeBatch();

        for (var index = 0; index < Observable.MaxBatchRecords + 1; index++)
        {
            list.Add(index);
        }

        list.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "still one event");
        Assert.AreEqual(1, events[0].Count, "carrying one record rather than fifty-one");
        Assert.AreEqual(
            ObservableListChangeType.UntrackedChanges,
            events[0][0].ChangeType,
            "and that record says the change was not tracked");
        Assert.AreEqual(Observable.MaxBatchRecords + 1, list.Count, "the list itself is complete");
    }

    [TestMethod]
    public void ABatchThatGaveUpTracksNothingAfterwards()
    {
        // Once the event says "re-read the list", every later change in the same batch is already
        // covered by it, so none of them is queued.
        using var list = Observable.CreateList<int>();
        var events = new List<IReadOnlyList<ObservableListChange<int>>>();

        list.AddObservableListChangedHandler(args => events.Add([.. args.Changes]));

        list.BeginChangeBatch();

        for (var index = 0; index < 500; index++)
        {
            list.Add(index);
        }

        list.RemoveAt(0);
        list.Clear();
        list.Add(1);

        list.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "one event");
        Assert.AreEqual(1, events[0].Count, "with the one record it gave up at, and nothing after it");
        Assert.AreEqual(ObservableListChangeType.UntrackedChanges, events[0][0].ChangeType);
        CollectionAssert.AreEqual(new[] { 1 }, list.ToArray(), "while the list did everything it was told");
    }

    [TestMethod]
    public void ADrainThatWouldCarryTooManyRecordsGivesUpToo()
    {
        // The same cap applies where records accumulate outside a batch: a handler that makes many
        // changes has them delivered as one event after the one it is hearing.
        using var list = Observable.CreateList<int>();
        var events = new List<IReadOnlyList<ObservableListChange<int>>>();
        var acted = false;

        list.AddObservableListChangedHandler(args =>
        {
            events.Add([.. args.Changes]);

            // Once only: a handler that made a batch on every event it heard would be an infinite flat
            // loop, which is the handler's bug and not something the drain bounds (section 3.7).
            if (args.Changes.Count == 0 || acted)
            {
                return;
            }

            acted = true;

            list.BeginChangeBatch();

            for (var index = 0; index < Observable.MaxBatchRecords + 5; index++)
            {
                list.Add(index);
            }

            list.EndChangeBatch();
        });

        list.Add(-1);

        Assert.AreEqual(2, events.Count, "the change, then the batch the handler made");
        Assert.AreEqual(1, events[1].Count, "which gave up on detail");
        Assert.AreEqual(ObservableListChangeType.UntrackedChanges, events[1][0].ChangeType);
    }

    [TestMethod]
    public void AChainOfHandlerCausedDeliveriesIsNotLimited()
    {
        // A handler that changes the list it is hearing about is doing real work every time round, and
        // delivery is a queue, not a recursion - so the chain is flat and there is nothing to bound. Only
        // the handler decides when it stops, and a handler-induced chain deeper than MaxEventStackSize is
        // ordinary: fifty is a stack-depth figure, not a work budget.
        using var list = Observable.CreateList(0);

        list.AddObservableListChangedHandler(args =>
        {
            // The disposal event carries no records and arrives after the list is dead, so a handler
            // that reads the list checks first - the same rule every handler in these tests follows.
            if (args.Changes.Count == 0)
            {
                return;
            }

            var value = list[0];

            if (value < 200)
            {
                list[0] = value + 1;
            }
        });

        list[0] = 1;

        Assert.AreEqual(200, list[0], "two hundred deliveries in one drain, none of them refused");
    }
    [TestMethod]
    public void ANestedDispatchIsRefusedAtMaxEventStackSize()
    {
        // A change to the list being delivered is queued, so it is flat - but a delivery can still be
        // re-entered from a handler that changes a different list, or that disposes one. Those nest on
        // the stack, which is exactly what the guard is for.
        var previous = Observable.MaxEventStackSize;
        Observable.MaxEventStackSize = 5;

        try
        {
            var refused = Assert.ThrowsExactly<InvalidOperationException>(
                () => Nest(),
                "a chain of deliveries that each start another one is refused rather than overflowing");

            StringAssert.Contains(
                refused.Message,
                "MaxEventStackSize",
                "and the message names the limit, which is the thing a caller can change");
        }
        finally
        {
            Observable.MaxEventStackSize = previous;
        }

        // Flat again once the chain unwound: a delivery of this list's own is an ordinary one.
        using var list = Observable.CreateList(1);
        var delivered = 0;
        list.AddObservableListChangedHandler(_ => delivered++);
        list.Add(2);

        Assert.AreEqual(1, delivered, "the depth was restored rather than left poisoned");

        static void Nest()
        {
            var fresh = Observable.CreateList<int>();

            fresh.AddObservableListChangedHandler(args =>
            {
                if (args.Changes.Count == 0)
                {
                    return;
                }

                Nest();
            });

            fresh.Add(1);
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
