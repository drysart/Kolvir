namespace Kolvir.Feature.Observables.Tests;

/// <summary>
/// A set's records: what each mutation raises, what each kind carries, and the replay that proves a
/// record stream is enough to keep a copy
/// (dev-docs/OBSERVABLE-COLLECTIONS.md sections 6.1, 6.3 and 6.4).
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class ObservableSetChangeTests
{
    [TestMethod]
    public void AddingAnItemRaisesOneRecordCarryingIt()
    {
        using var set = Observable.CreateHashSet<string>();
        var events = new List<ObservableSetChange<string>>();

        set.AddObservableSetChangedHandler(args => events.AddRange(args.Changes));

        Assert.IsTrue(set.Add("a"), "the item was not there");

        Assert.AreEqual(1, events.Count, "one record");
        Assert.AreEqual(ObservableSetChangeType.ItemAdded, events[0].ChangeType);
        Assert.AreEqual("a", events[0].Item, "naming the item, and filling nothing else");
    }

    [TestMethod]
    public void AddingAnItemTheComparerCallsEqualRaisesNothing()
    {
        // A set holds one item per equality class, so this is not a change: the instance it already has
        // stays, and Add says so by returning false.
        using var set = Observable.CreateHashSet<string>(StringComparer.OrdinalIgnoreCase);
        var events = 0;

        set.AddObservableSetChangedHandler(args => events += args.Changes.Count);
        set.Add("abc");

        Assert.IsFalse(set.Add("ABC"), "the comparer says it is already there");
        Assert.AreEqual(1, events, "so only the first add raised anything");
        Assert.IsTrue(set.Contains("AbC"), "and the item it kept answers for all three spellings");
    }

    [TestMethod]
    public void RemovingAnItemRaisesOneRecordCarryingTheItemThatWasStored()
    {
        // The argument and the stored item need not be the same instance when the comparer is not
        // equality: the record says what came out of the set, which is what an observer's copy holds.
        using var set = Observable.CreateHashSet<string>(StringComparer.OrdinalIgnoreCase);
        var events = new List<ObservableSetChange<string>>();

        set.Add("abc");
        set.AddObservableSetChangedHandler(args => events.AddRange(args.Changes));

        Assert.IsTrue(set.Remove("ABC"), "the comparer finds it");

        Assert.AreEqual(1, events.Count, "one record");
        Assert.AreEqual(ObservableSetChangeType.ItemRemoved, events[0].ChangeType);
        Assert.AreEqual("abc", events[0].Item, "carrying what was stored, not the argument");
    }

    [TestMethod]
    public void RemovingAnItemThatIsNotThereRaisesNothing()
    {
        using var set = Observable.CreateHashSet("a", "b");
        var events = 0;

        set.AddObservableSetChangedHandler(args => events += args.Changes.Count);

        Assert.IsFalse(set.Remove("c"), "there is nothing to remove");
        Assert.AreEqual(0, events);
    }

    [TestMethod]
    public void ClearRaisesOneRecordHoweverManyItemsThereWere()
    {
        using var set = Observable.CreateHashSet("a", "b", "c");
        var events = new List<ObservableSetChange<string>>();

        set.AddObservableSetChangedHandler(args => events.AddRange(args.Changes));

        set.Clear();

        Assert.AreEqual(1, events.Count, "one record");
        Assert.AreEqual(ObservableSetChangeType.SetCleared, events[0].ChangeType);
        Assert.IsNull(events[0].Item, "and a clear carries nothing");
        Assert.AreEqual(0, set.Count);
    }

    [TestMethod]
    public void ClearingAnEmptySetRaisesNothing()
    {
        using var set = Observable.CreateHashSet<string>();
        var events = 0;

        set.AddObservableSetChangedHandler(args => events += args.Changes.Count);

        set.Clear();

        Assert.AreEqual(0, events, "an event means something a listener cares about is different");
    }

    [TestMethod]
    public void EachKindFillsTheFieldsItMeans()
    {
        var added = ObservableSetChange<string>.Add("a");
        Assert.AreEqual(ObservableSetChangeType.ItemAdded, added.ChangeType);
        Assert.AreEqual("a", added.Item);

        var removed = ObservableSetChange<string>.Remove("b");
        Assert.AreEqual(ObservableSetChangeType.ItemRemoved, removed.ChangeType);
        Assert.AreEqual("b", removed.Item);

        var cleared = ObservableSetChange<string>.Clear();
        Assert.AreEqual(ObservableSetChangeType.SetCleared, cleared.ChangeType);
        Assert.IsNull(cleared.Item, "a clear names no item");

        var untracked = ObservableSetChange<string>.Untracked();
        Assert.AreEqual(ObservableSetChangeType.UntrackedChanges, untracked.ChangeType);
        Assert.IsNull(untracked.Item, "and neither does the admission");
    }

    [TestMethod]
    public void ARecordSaysWhatHappenedInWords()
    {
        // Diagnostics only: the event line in the sample and an exception message are the two places a
        // record is ever printed.
        StringAssert.Contains(ObservableSetChange<string>.Add("a").ToString(), "added a");
        StringAssert.Contains(ObservableSetChange<string>.Remove("b").ToString(), "removed b");
        StringAssert.Contains(ObservableSetChange<string>.Clear().ToString(), "cleared");
        StringAssert.Contains(ObservableSetChange<string>.Untracked().ToString(), "does not describe");
    }

    [TestMethod]
    public void AReplayOfTheRecordsReproducesTheSet()
    {
        // The contract in one test: a handler that only ever applies records ends up with the set,
        // without ever reading it.
        using var set = Observable.CreateHashSet("a", "b", "c");
        var copy = new HashSet<string>(StringComparer.Ordinal);

        set.AddObservableSetChangedHandler(args =>
        {
            foreach (var change in args.Changes)
            {
                switch (change.ChangeType)
                {
                    case ObservableSetChangeType.ItemAdded:
                        copy.Add(change.Item!);
                        break;

                    case ObservableSetChangeType.ItemRemoved:
                        copy.Remove(change.Item!);
                        break;

                    case ObservableSetChangeType.SetCleared:
                        copy.Clear();
                        break;

                    default:
                        // UntrackedChanges: the batch was too big to describe, so rebuild from the set.
                        copy.Clear();
                        copy.UnionWith(set);
                        break;
                }
            }
        });

        set.Add("d");
        set.Remove("a");
        set.UnionWith(["e", "f"]);
        set.ExceptWith(["b"]);
        set.IntersectWith(["c", "d", "e", "z"]);
        set.SymmetricExceptWith(["c", "e", "g"]);

        set.BeginChangeBatch();
        set.Add("h");
        set.Remove("d");
        set.EndChangeBatch();

        Assert.IsTrue(copy.SetEquals(set), "the copy the records built is the set");
    }
}
