namespace Kolvir.Feature.Observables.Tests;

/// <summary>
/// A mapped read-only view: the projection is eager and cached, the source's records are translated one
/// for one into the mapped items, reads are the same three dependencies a list publishes, and disposal
/// ends this view's subscription without touching the source
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 5.4).
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class ObservableMappedReadOnlyListViewTests
{
    [TestMethod]
    public void ConstructionMapsWhatTheSourceAlreadyHolds()
    {
        using var source = Observable.CreateList("a", "bb", "ccc");
        using var view = Upper(source);

        Assert.AreEqual(3, view.Count, "one mapped item per source item");
        Assert.AreEqual("A", view[0], "in the source's order");
        Assert.AreEqual("CCC", view[2], "with the type the function produced");
    }

    [TestMethod]
    public void AnAddMapsTheNewItemAndAppendsIt()
    {
        using var source = Observable.CreateList("a");
        using var view = Upper(source);

        source.Add("bbb");

        Assert.AreEqual(2, view.Count);
        Assert.AreEqual("BBB", view[1], "the view answers with the mapped item, not the one the source holds");
    }

    [TestMethod]
    public void AnInsertMapsIntoThePositionTheSourcePutIt()
    {
        using var source = Observable.CreateList("a", "b");
        using var view = Upper(source);

        source.Insert(1, "c");

        CollectionAssert.AreEqual(new[] { "A", "C", "B" }, view.ToArray(), "the survivors keep their order");
    }

    [TestMethod]
    public void AChangeRemapsThatOneIndexAndNothingElse()
    {
        var calls = 0;

        using var source = Observable.CreateList("a", "b");
        using var view = new ObservableMappedReadOnlyListView<string, string>(
            source,
            text =>
            {
                calls++;
                return text.ToUpperInvariant();
            });

        Assert.AreEqual(2, calls, "the contents the source already held were mapped once each");

        source[1] = "z";

        Assert.AreEqual(3, calls, "a replacement is mapped once, for the new item");
        CollectionAssert.AreEqual(new[] { "A", "Z" }, view.ToArray(), "and only that row moved");
    }

    [TestMethod]
    public void AMoveRepositionsTheMappedItemWithoutMappingItAgain()
    {
        var calls = 0;

        using var source = Observable.CreateList("a", "b", "c");
        using var view = new ObservableMappedReadOnlyListView<string, string>(
            source,
            text =>
            {
                calls++;
                return text.ToUpperInvariant();
            });

        source.Move(0, 2);

        Assert.AreEqual(3, calls, "a move is a reposition, not a removal and an addition");
        CollectionAssert.AreEqual(new[] { "B", "C", "A" }, view.ToArray(), "and the mapped value moved with it");
    }

    [TestMethod]
    public void ARemovalDropsTheMappedItemAndKeepsTheSurvivorsAligned()
    {
        var calls = 0;

        using var source = Observable.CreateList("a", "b", "c", "d");
        using var view = new ObservableMappedReadOnlyListView<string, string>(
            source,
            text =>
            {
                calls++;
                return text.ToUpperInvariant();
            });

        source.RemoveAt(1);

        CollectionAssert.AreEqual(new[] { "A", "C", "D" }, view.ToArray(), "the rest closed the gap");

        source.Remove("d");

        CollectionAssert.AreEqual(new[] { "A", "C" }, view.ToArray(), "and a removal by item does the same");
        Assert.AreEqual(4, calls, "removing a mapped item never runs the function");
    }

    [TestMethod]
    public void AClearEmptiesTheViewAndLeavesItUsable()
    {
        using var source = Observable.CreateList("a", "b");
        using var view = Upper(source);

        source.Clear();

        Assert.AreEqual(0, view.Count);
        Assert.AreEqual(0, view.ToArray().Length, "nothing of the old contents survived");

        source.Add("c");

        Assert.AreEqual(1, view.Count, "the view still follows the source afterwards");
        Assert.AreEqual("C", view[0]);
    }

    [TestMethod]
    public void AnUntrackedEventRebuildsTheViewFromTheSource()
    {
        // The source's cap is what makes this record possible: a batch past it drops what it collected
        // and says only that things changed, so the view's cache has to be rebuilt rather than patched.
        using var source = Observable.CreateList<string>();
        using var view = Upper(source);

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
        Assert.AreEqual(
            Observable.Default.MaxCollectionBatchSize + 1,
            view.Count,
            "and the view was resynced from the source rather than left behind");
        Assert.AreEqual("X0", view[0], "with every item mapped");
    }

    [TestMethod]
    public void TheDeliveredRecordsAreTheSourcesRecordsTranslated()
    {
        using var source = Observable.CreateList("a", "b");
        using var view = Upper(source);

        ObservableListChangedEventArgs<string>? last = null;
        view.AddObservableListChangedHandler(args => last = args);

        source.Insert(1, "c");

        Assert.IsNotNull(last, "the view delivered the change");
        Assert.AreSame(view, last.List, "and names itself as the list, not the source");

        var added = last.Changes[0];

        Assert.AreEqual(ObservableListChangeType.ItemAdded, added.ChangeType);
        Assert.AreEqual(1, added.NewIndex!.Value, "the index is the source's");
        Assert.AreEqual("C", added.NewItem, "and the item is the mapped one");
        Assert.IsNull(added.OldIndex, "an add has no old half");

        source[0] = "z";

        var changed = last.Changes[0];

        Assert.AreEqual(ObservableListChangeType.ItemChanged, changed.ChangeType);
        Assert.AreEqual(0, changed.OldIndex!.Value);
        Assert.AreEqual(0, changed.NewIndex!.Value);
        Assert.AreEqual("A", changed.OldItem, "the old half is the value the view had");
        Assert.AreEqual("Z", changed.NewItem, "and the new half is the value it has now");
    }

    [TestMethod]
    public void ARemovalAndAMoveAndAClearCarryTheMappedItems()
    {
        using var source = Observable.CreateList("a", "b", "c");
        using var view = Upper(source);

        ObservableListChangedEventArgs<string>? last = null;
        view.AddObservableListChangedHandler(args => last = args);

        source.RemoveAt(0);

        Assert.AreEqual(ObservableListChangeType.ItemRemoved, last!.Changes[0].ChangeType);
        Assert.AreEqual(0, last.Changes[0].OldIndex!.Value);
        Assert.AreEqual("A", last.Changes[0].OldItem, "the mapped item that came out, as an observer's copy had it");
        Assert.IsNull(last.Changes[0].NewIndex);

        source.Move(0, 1);

        Assert.AreEqual(ObservableListChangeType.ItemMoved, last.Changes[0].ChangeType);
        Assert.AreEqual(0, last.Changes[0].OldIndex!.Value);
        Assert.AreEqual(1, last.Changes[0].NewIndex!.Value);
        Assert.AreEqual("B", last.Changes[0].OldItem, "a move names the same mapped item on both sides");
        Assert.AreEqual("B", last.Changes[0].NewItem);

        source.Clear();

        Assert.AreEqual(ObservableListChangeType.ListCleared, last.Changes[0].ChangeType, "a clear is one record");
        Assert.IsNull(last.Changes[0].OldIndex, "carrying nothing");
        Assert.IsNull(last.Changes[0].NewItem);
    }

    [TestMethod]
    public void ABatchOfSourceChangesArrivesAsOneDeliveryInOrder()
    {
        using var source = Observable.CreateList("a");
        using var view = Upper(source);

        var events = new List<IReadOnlyList<ObservableListChange<string>>>();
        view.AddObservableListChangedHandler(args => events.Add([.. args.Changes]));

        source.BeginChangeBatch();
        source.Add("b");
        source.Insert(0, "c");
        source.Remove("a");
        source.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "three source records, one delivery from the view");
        Assert.AreEqual(3, events[0].Count, "carrying all three");

        Assert.AreEqual(ObservableListChangeType.ItemAdded, events[0][0].ChangeType, "in the order they happened");
        Assert.AreEqual(1, events[0][0].NewIndex!.Value, "the add went to the end");
        Assert.AreEqual(ObservableListChangeType.ItemAdded, events[0][1].ChangeType);
        Assert.AreEqual(0, events[0][1].NewIndex!.Value, "then the insert went to the front");
        Assert.AreEqual(ObservableListChangeType.ItemRemoved, events[0][2].ChangeType);
        Assert.AreEqual(1, events[0][2].OldIndex!.Value, "and the removal found a at its shifted index");
    }

    [TestMethod]
    public void AChangeMadeFromInsideAViewHandlerIsDeliveredAfterTheEventBeingHeard()
    {
        using var source = Observable.CreateList("a");
        using var view = Upper(source);
        var seen = new List<string>();

        view.AddObservableListChangedHandler(args =>
        {
            // The final event of a disposal carries no records, so a handler checks first.
            if (args.Changes.Count == 0)
            {
                return;
            }

            seen.Add($"{args.Changes[0].ChangeType}:{args.Changes[0].NewItem}");

            if (args.Changes[0].NewItem == "B")
            {
                source.Add("c");
            }
        });

        source.Add("b");

        CollectionAssert.AreEqual(
            new[] { "ItemAdded:B", "ItemAdded:C" },
            seen,
            "the change made from the handler is queued behind the event it was hearing");
    }

    [TestMethod]
    public void AReadOfAnIndexIsNotWokenByAChangeToAnotherIndex()
    {
        using var source = Observable.CreateList("a", "b", "c");
        using var view = Upper(source);
        var wakes = new List<string>();

        using var count = Observable.CollectDependencies(() => { _ = view.Count; }, _ => wakes.Add("count"));
        using var third = Observable.CollectDependencies(() => { _ = view[2]; }, _ => wakes.Add("2"));

        source[0] = "z";

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            wakes,
            "the size did not change and index 2 still answers with the same item");
    }

    [TestMethod]
    public void AnInsertWakesTheIndexReadersAtOrPastItAndTheCount()
    {
        using var source = Observable.CreateList("a", "b", "c");
        using var view = Upper(source);
        var wakes = new List<string>();

        using var count = Observable.CollectDependencies(() => { _ = view.Count; }, _ => wakes.Add("count"));
        using var first = Observable.CollectDependencies(() => { _ = view[0]; }, _ => wakes.Add("0"));
        using var second = Observable.CollectDependencies(() => { _ = view[1]; }, _ => wakes.Add("1"));
        using var third = Observable.CollectDependencies(() => { _ = view[2]; }, _ => wakes.Add("2"));

        source.Insert(1, "x");

        CollectionAssert.AreEqual(
            new[] { "count", "1", "2" },
            wakes,
            "everything from the insertion point on may answer differently, and index 0 does not");
    }

    [TestMethod]
    public void TheScansDependOnTheContents()
    {
        using var source = Observable.CreateList("a", "b");
        using var view = Upper(source);
        var fromEnumeration = 0;
        var fromContains = 0;
        var fromIndexOf = 0;

        using var enumeration = Observable.CollectDependencies(
            () =>
            {
                foreach (var item in view)
                {
                    _ = item;
                }
            },
            _ => fromEnumeration++);

        using var contains = Observable.CollectDependencies(() => { _ = view.Contains("A"); }, _ => fromContains++);
        using var indexOf = Observable.CollectDependencies(() => { _ = view.IndexOf("B"); }, _ => fromIndexOf++);

        source[0] = "z";

        Assert.AreEqual(1, fromEnumeration, "an enumeration is stale when any item changes");
        Assert.AreEqual(1, fromContains, "and so is a scan, because it read every item");
        Assert.AreEqual(1, fromIndexOf, "all three of them");
    }

    [TestMethod]
    public void DisposingUnsubscribesFromTheSourceAndLeavesItUsable()
    {
        using var source = Observable.CreateList("a");
        var view = Upper(source);

        var events = new List<ObservableListChangedEventArgs<string>>();
        view.AddObservableListChangedHandler(events.Add);

        view.Dispose();

        Assert.AreEqual(1, events.Count, "one last event, so a registrant learns to let go");
        Assert.AreEqual(0, events[0].Changes.Count, "which carries no records");
        Assert.IsTrue(events[0].List.IsDisposed, "and says the view is dead");

        source.Add("b");

        Assert.AreEqual(1, events.Count, "the view unsubscribed, so the source's change reached nobody");
        Assert.AreEqual(2, source.Count, "and the source is untouched");
        Assert.AreEqual("b", source[1], "and is still readable");

        source.Dispose();
    }

    [TestMethod]
    public void DisposingTwiceAndRemovingAHandlerAfterwardsStaySafe()
    {
        using var source = Observable.CreateList("a");
        var view = Upper(source);

        view.Dispose();
        view.Dispose();
        view.RemoveObservableListChangedHandler(_ => { });

        Assert.IsTrue(view.IsDisposed, "teardown paths release what they hold without knowing what state it is in");
    }

    [TestMethod]
    public void EveryOtherMemberThrowsOnceDisposed()
    {
        using var source = Observable.CreateList("a");
        var view = Upper(source);

        view.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = view.Count, "a count read");
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = view[0], "an index read");
        Assert.ThrowsExactly<ObjectDisposedException>(() => view.Contains("A"), "a scan");
        Assert.ThrowsExactly<ObjectDisposedException>(() => view.IndexOf("A"), "another scan");
        Assert.ThrowsExactly<ObjectDisposedException>(() => view.CopyTo(new string[1], 0), "a copy");

        Assert.ThrowsExactly<ObjectDisposedException>(
            () =>
            {
                foreach (var item in view)
                {
                    _ = item;
                }
            },
            "an enumeration");

        Assert.ThrowsExactly<ObjectDisposedException>(
            () => view.AddObservableListChangedHandler(_ => { }),
            "a registration");
    }

    [TestMethod]
    public void ANullSourceOrSelectorIsRefused()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => _ = new ObservableMappedReadOnlyListView<string, string>(null!, text => text));

        using var source = Observable.CreateList("a");

        Assert.ThrowsExactly<ArgumentNullException>(
            () => _ = new ObservableMappedReadOnlyListView<string, string>(source, null!));
    }

    [TestMethod]
    public void ADisposedSourceLeavesTheViewReadableWithItsLastContents()
    {
        // The source's one empty event is its disposal: nothing can change any more, so the view keeps
        // what it last had rather than emptying itself or dying with the source.
        var source = Observable.CreateList("a", "b");
        var view = Upper(source);
        var events = 0;

        view.AddObservableListChangedHandler(_ => events++);

        source.Dispose();

        Assert.AreEqual(0, events, "the source's final event is not a change to report");
        Assert.IsFalse(view.IsDisposed, "the view's lifetime is its own");
        Assert.AreEqual(2, view.Count, "and it still holds what it last mapped");
        Assert.AreEqual("A", view[0]);

        view.Dispose();

        Assert.IsTrue(view.IsDisposed, "disposing a view whose source is gone is safe");
    }

    private static ObservableMappedReadOnlyListView<string, string> Upper(IReadOnlyObservableList<string> source) =>
        new(source, text => text.ToUpperInvariant());
}
