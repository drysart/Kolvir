namespace Kolvir.Feature.Observables.Tests;

/// <summary>
/// An observable list's records: one per actual change, each filling in the members its kind promises,
/// and a replay of them reproducing the list
/// (dev-docs/OBSERVABLE-COLLECTIONS.md sections 3.1, 3.2 and 3.4).
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class ObservableListChangeTests
{
    [TestMethod]
    public void CreateListStartsEmpty()
    {
        using var list = Observable.CreateList<int>();

        Assert.AreEqual(0, list.Count, "a new list holds nothing");
        Assert.IsFalse(list.IsDisposed, "and it is alive");
        Assert.IsFalse(list.IsReadOnly, "and writable");
    }

    [TestMethod]
    public void CreateListWithItemsKeepsThemInOrder()
    {
        using var list = Observable.CreateList("a", "b", "c");

        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, list.ToArray(), "the arguments are the contents");
    }

    [TestMethod]
    public void AddRaisesOneItemAddedAtTheEnd()
    {
        using var list = Observable.CreateList("a");
        var events = Record(list);

        list.Add("b");

        Assert.AreEqual(1, events.Count, "one call is one event");
        Assert.AreEqual(1, events[0].Count, "carrying one record");
        Assert.AreEqual(ObservableListChangeType.ItemAdded, events[0][0].ChangeType, "an addition");
        Assert.AreEqual(1, events[0][0].NewIndex!.Value, "at the end");
        Assert.AreEqual("b", events[0][0].NewItem, "carrying the item");
        Assert.IsNull(events[0][0].OldIndex, "and no old index, which its kind does not promise");
        Assert.IsNull(events[0][0].OldItem, "nor an old item");
    }

    [TestMethod]
    public void InsertRaisesOneItemAddedAtTheIndex()
    {
        using var list = Observable.CreateList("a", "b");
        var events = Record(list);

        list.Insert(1, "x");

        Assert.AreEqual(1, events[0][0].NewIndex!.Value, "an insert reports where it went");
        Assert.AreEqual("x", events[0][0].NewItem, "and what it was");
        CollectionAssert.AreEqual(new[] { "a", "x", "b" }, list.ToArray(), "which is where it is");
    }

    [TestMethod]
    public void RemoveAtRaisesOneItemRemovedWithTheItem()
    {
        using var list = Observable.CreateList("a", "b", "c");
        var events = Record(list);

        list.RemoveAt(1);

        Assert.AreEqual(ObservableListChangeType.ItemRemoved, events[0][0].ChangeType, "a removal");
        Assert.AreEqual(1, events[0][0].OldIndex!.Value, "from where it was");
        Assert.AreEqual("b", events[0][0].OldItem, "carrying the item");
        Assert.IsNull(events[0][0].NewIndex, "and nothing on the new side");
    }

    [TestMethod]
    public void RemoveRaisesWithTheIndexItWasFoundAt()
    {
        using var list = Observable.CreateList("a", "b", "c");
        var events = Record(list);

        var removed = list.Remove("c");

        Assert.IsTrue(removed, "the item was there");
        Assert.AreEqual(2, events[0][0].OldIndex!.Value, "so the record names the index it was found at");
    }

    [TestMethod]
    public void RemoveOfAnItemThatIsNotThereRaisesNothing()
    {
        using var list = Observable.CreateList("a");
        var events = Record(list);

        var removed = list.Remove("zzz");

        Assert.IsFalse(removed, "it returns false");
        Assert.AreEqual(0, events.Count, "and a removal that removed nothing is not a change");
        Assert.AreEqual(1, list.Count, "and the list is untouched");
    }

    [TestMethod]
    public void WritingAnItemRaisesOneItemChangedWithBothSides()
    {
        using var list = Observable.CreateList("a", "b");
        var events = Record(list);

        list[1] = "z";

        Assert.AreEqual(ObservableListChangeType.ItemChanged, events[0][0].ChangeType, "a change");
        Assert.AreEqual(1, events[0][0].OldIndex!.Value, "at the index written");
        Assert.AreEqual(1, events[0][0].NewIndex!.Value, "which is the same index before and after");
        Assert.AreEqual("b", events[0][0].OldItem, "what was there");
        Assert.AreEqual("z", events[0][0].NewItem, "and what is there now");
    }

    [TestMethod]
    public void WritingAnEqualItemRaisesNothing()
    {
        // An event means something a listener cares about is different, which is the rule a value's
        // write already follows (section 3.5).
        using var list = Observable.CreateList(new Text("hello"));
        var events = Record(list);

        list[0] = new Text("hello");

        Assert.AreEqual(0, events.Count, "an equal item is not a change");
    }

    [TestMethod]
    public void AListsOwnComparerDecidesWhatCountsAsEqual()
    {
        using var list = Observable.CreateList<string>(StringComparer.OrdinalIgnoreCase, "hello");
        var events = Record(list);

        list[0] = "HELLO";

        Assert.AreEqual(0, events.Count, "the list's comparer says this changed nothing");
    }

    [TestMethod]
    public void MoveRaisesOneItemMovedWithBothIndexesAndTheItem()
    {
        using var list = Observable.CreateList("a", "b", "c");
        var events = Record(list);

        list.Move(0, 2);

        Assert.AreEqual(1, events.Count, "one call, one event - a move is not a removal and an addition");
        Assert.AreEqual(1, events[0].Count, "carrying one record");
        Assert.AreEqual(ObservableListChangeType.ItemMoved, events[0][0].ChangeType, "a move");
        Assert.AreEqual(0, events[0][0].OldIndex!.Value, "from where");
        Assert.AreEqual(2, events[0][0].NewIndex!.Value, "to where");
        Assert.AreEqual("a", events[0][0].OldItem, "and the item on the old side");
        Assert.AreEqual("a", events[0][0].NewItem, "and on the new, because it is the same item");
        CollectionAssert.AreEqual(
            new[] { "b", "c", "a" },
            list.ToArray(),
            "which is ObservableCollection.Move's rule");
    }

    [TestMethod]
    public void MovingAnItemToWhereItAlreadyIsRaisesNothing()
    {
        using var list = Observable.CreateList("a", "b", "c");
        var events = Record(list);

        list.Move(1, 1);

        Assert.AreEqual(0, events.Count, "nothing moved");
    }

    [TestMethod]
    public void ClearRaisesOneRecordHoweverManyItemsThereWere()
    {
        using var list = Observable.CreateList("a", "b", "c", "d", "e");
        var events = Record(list);

        list.Clear();

        Assert.AreEqual(1, events.Count, "one event");
        Assert.AreEqual(1, events[0].Count, "one record, not one per item");
        Assert.AreEqual(ObservableListChangeType.ListCleared, events[0][0].ChangeType, "a clear");
        Assert.IsNull(events[0][0].OldIndex, "carrying no index");
        Assert.IsNull(events[0][0].OldItem, "and no item");
        Assert.AreEqual(0, list.Count, "and the list is empty");
    }

    [TestMethod]
    public void ClearingAnEmptyListRaisesNothing()
    {
        using var list = Observable.CreateList<int>();
        var events = Record(list);

        list.Clear();

        Assert.AreEqual(0, events.Count, "an empty list cleared is still an empty list");
    }

    [TestMethod]
    public void AnIndexOutOfRangeThrowsAndRaisesNothing()
    {
        using var list = Observable.CreateList("a");
        var events = Record(list);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => list.RemoveAt(5), "remove past the end");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => list.Insert(5, "x"), "insert past the end");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => list.Move(0, 5), "move past the end");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = list[5], "read past the end");

        Assert.AreEqual(0, events.Count, "a mutation that failed is not a change");
        CollectionAssert.AreEqual(new[] { "a" }, list.ToArray(), "and the list is unchanged");
    }

    [TestMethod]
    public void ReplayingTheRecordsReproducesTheList()
    {
        // The fidelity claim: a listener that only ever applies records, and never reads the list, ends
        // with a copy that equals it (section 3.2).
        using var list = Observable.CreateList("a", "b");
        var mirror = new List<string> { "a", "b" };

        list.AddObservableListChangedHandler(args => Replay(mirror, args.Changes));

        list.Add("c");
        list.Insert(0, "start");
        list.RemoveAt(2);
        list.Remove("c");
        list[1] = "changed";
        list.Move(0, 1);
        list.Move(1, 0);
        list.Clear();
        list.Add("after");
        list.Add("the");
        list.Add("clear");

        CollectionAssert.AreEqual(list.ToArray(), mirror, "the copy is the list, element for element");
    }

    [TestMethod]
    public void ReplayingABatchReproducesTheList()
    {
        using var list = Observable.CreateList("a");
        var mirror = new List<string> { "a" };

        list.AddObservableListChangedHandler(args => Replay(mirror, args.Changes));

        list.BeginChangeBatch();
        list.Add("b");
        list.Insert(0, "c");
        list.Remove("a");
        list[0] = "d";
        list.EndChangeBatch();

        CollectionAssert.AreEqual(list.ToArray(), mirror, "a batch is the same records in one event");
    }

    [TestMethod]
    public void ARecordSaysWhatHappened()
    {
        // Not a specification - a diagnostic line that names the kind and the item, so a failing test
        // that prints one is readable.
        Assert.AreEqual("added b at 1", ObservableListChange<string>.Add(1, "b").ToString(), "an add");
        Assert.AreEqual("removed b from 1", ObservableListChange<string>.Remove(1, "b").ToString(), "a removal");
        Assert.AreEqual("moved a from 0 to 2", ObservableListChange<string>.Move(0, 2, "a").ToString(), "a move");
        Assert.AreEqual(
            "changed the item at 1 from a to b",
            ObservableListChange<string>.Change(1, "a", "b").ToString(),
            "a change");
        Assert.AreEqual("cleared the list", ObservableListChange<string>.Clear().ToString(), "a clear");
    }

    [TestMethod]
    public void EnumerationThrowsWhenTheListIsMutatedUnderIt()
    {
        using var list = Observable.CreateList("a", "b");

        Assert.ThrowsExactly<InvalidOperationException>(
            () =>
            {
                foreach (var item in list)
                {
                    _ = item;
                    list.Add("c");
                }
            },
            "a mutation during enumeration is a bug worth failing on, exactly as it is for List<T>");
    }

    private static List<List<ObservableListChange<T>>> Record<T>(IObservableList<T> list)
    {
        var events = new List<List<ObservableListChange<T>>>();
        list.AddObservableListChangedHandler(args => events.Add([.. args.Changes]));
        return events;
    }

    private static void Replay(List<string> mirror, IReadOnlyList<ObservableListChange<string>> changes)
    {
        foreach (var change in changes)
        {
            switch (change.ChangeType)
            {
                case ObservableListChangeType.ItemAdded:
                    mirror.Insert(change.NewIndex!.Value, change.NewItem!);
                    break;

                case ObservableListChangeType.ItemRemoved:
                    mirror.RemoveAt(change.OldIndex!.Value);
                    break;

                case ObservableListChangeType.ItemMoved:
                    mirror.RemoveAt(change.OldIndex!.Value);
                    mirror.Insert(change.NewIndex!.Value, change.NewItem!);
                    break;

                case ObservableListChangeType.ItemChanged:
                    mirror[change.NewIndex!.Value] = change.NewItem!;
                    break;

                default:
                    mirror.Clear();
                    break;
            }
        }
    }

    /// <summary>A value whose equality is its text, so an equal write is testable.</summary>
    private readonly record struct Text(string Value);
}
