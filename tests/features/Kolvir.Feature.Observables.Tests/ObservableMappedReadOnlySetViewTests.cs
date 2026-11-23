namespace Kolvir.Feature.Observables.Tests;

/// <summary>
/// A mapped read-only view over a set: the projection is eager, cached and counted, so a source record
/// translates into at most one view record and a change that leaves the view alone raises nothing; reads
/// are the same three dependencies a set publishes, and disposal ends this view's subscription without
/// touching the source (dev-docs/OBSERVABLE-COLLECTIONS.md section 6.5).
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class ObservableMappedReadOnlySetViewTests
{
    [TestMethod]
    public void ConstructionMapsWhatTheSourceAlreadyHolds()
    {
        using var source = Observable.CreateHashSet("a", "bb", "ccc");
        using var view = Upper(source);

        Assert.AreEqual(3, view.Count, "one mapped item per source item when nothing collapses");
        CollectionAssert.AreEquivalent(
            new[] { "A", "BB", "CCC" },
            view.ToArray(),
            "with the type the function produced");
    }

    [TestMethod]
    public void ConstructionCollapsesItemsThatShareAMappedItem()
    {
        var calls = 0;

        using var source = Observable.CreateHashSet("a1", "a2", "b1");
        using var view = new ObservableMappedReadOnlySetView<string, string>(
            source,
            text =>
            {
                calls++;
                return text[..1].ToUpperInvariant();
            });

        Assert.AreEqual(3, calls, "every source item was mapped once");
        Assert.AreEqual(2, view.Count, "but two of them are the same mapped item");
        Assert.IsTrue(view.Contains("A"), "which is present once");
        Assert.IsTrue(view.Contains("B"));
    }

    [TestMethod]
    public void AnAddThatIntroducesAMappedItemIsReportedOnce()
    {
        using var source = Observable.CreateHashSet("a1");
        using var view = FirstLetter(source);

        var events = new List<IReadOnlyList<ObservableSetChange<string>>>();
        view.AddObservableSetChangedHandler(args => events.Add([.. args.Changes]));

        source.Add("b2");

        Assert.AreEqual(1, events.Count, "a new mapped item is a change");
        Assert.AreEqual(1, events[0].Count);
        Assert.AreEqual(ObservableSetChangeType.ItemAdded, events[0][0].ChangeType);
        Assert.AreEqual("B", events[0][0].Item);
        Assert.AreEqual(2, view.Count);
    }

    [TestMethod]
    public void AnAddForAMappedItemAlreadyPresentRaisesNothing()
    {
        var calls = 0;

        using var source = Observable.CreateHashSet("a1");
        using var view = new ObservableMappedReadOnlySetView<string, string>(
            source,
            text =>
            {
                calls++;
                return text[..1].ToUpperInvariant();
            });

        var events = 0;
        view.AddObservableSetChangedHandler(args => events += args.Changes.Count);

        source.Add("a2");

        Assert.AreEqual(2, calls, "the arriving item was still mapped");
        Assert.AreEqual(1, view.Count, "but it mapped to an item the view already held");
        Assert.AreEqual(0, events, "so there is nothing an observer could apply");
    }

    [TestMethod]
    public void APartialRemovalRaisesNothing()
    {
        using var source = Observable.CreateHashSet("a1", "a2");
        using var view = FirstLetter(source);

        var events = 0;
        view.AddObservableSetChangedHandler(args => events += args.Changes.Count);

        source.Remove("a2");

        Assert.AreEqual(0, events, "another inner item still maps to this outer item");
        Assert.AreEqual(1, view.Count);
        Assert.IsTrue(view.Contains("A"), "which is still there");
    }

    [TestMethod]
    public void TheLastRemovalReportsTheStoredInstance()
    {
        // The mapper hands back a fresh instance every time, and the comparer calls them equal. The record
        // has to name the instance the view already held, or an observer's own copy cannot remove it.
        using var source = Observable.CreateHashSet("a1", "a2");
        using var view = new ObservableMappedReadOnlySetView<string, Token>(
            source,
            _ => new Token("x"),
            new TokenComparer());

        var stored = view.Single();

        Assert.AreEqual(1, view.Count, "both inner items map to one token");

        var events = new List<ObservableSetChange<Token>>();
        view.AddObservableSetChangedHandler(args => events.AddRange(args.Changes));

        source.Remove("a2");

        Assert.AreEqual(0, events.Count, "the last mapping is still there");

        source.Remove("a1");

        Assert.AreEqual(1, events.Count, "the last mapping left");
        Assert.AreEqual(ObservableSetChangeType.ItemRemoved, events[0].ChangeType);
        Assert.AreSame(stored, events[0].Item, "the instance the view held, not the one the mapper just made");
        Assert.AreEqual(0, view.Count);
    }

    [TestMethod]
    public void AnImpureSelectorResyncsAndSaysTheRemovalWasNotDescribed()
    {
        // The mapping function is supposed to be deterministic. One that is not produces a removal for an
        // outer the view never took in, and the view answers that by rebuilding from the source and saying
        // the change was not described - so the disagreement is visible and the view heals rather than
        // drifting silently.
        using var source = Observable.CreateHashSet("a");
        var calls = 0;

        using var view = new ObservableMappedReadOnlySetView<string, string>(
            source,
            _ => $"v{++calls}");

        Assert.AreEqual(1, view.Count, "the source's item was mapped once on construction");

        var events = new List<IReadOnlyList<ObservableSetChange<string>>>();
        view.AddObservableSetChangedHandler(args => events.Add([.. args.Changes]));

        source.Remove("a");

        Assert.AreEqual(1, events.Count, "one source record, one delivery");
        Assert.AreEqual(1, events[0].Count, "carrying the record that says it was not described");
        Assert.AreEqual(
            ObservableSetChangeType.UntrackedChanges,
            events[0][events[0].Count - 1].ChangeType,
            "because the removal mapped to something the view never held");
        Assert.AreEqual(0, view.Count, "and the view was rebuilt from the now-empty source");
    }

    [TestMethod]
    public void AClearEmptiesTheViewAndLeavesItUsable()
    {
        using var source = Observable.CreateHashSet("a", "b");
        using var view = Upper(source);

        var events = new List<IReadOnlyList<ObservableSetChange<string>>>();
        view.AddObservableSetChangedHandler(args => events.Add([.. args.Changes]));

        source.Clear();

        Assert.AreEqual(0, view.Count);
        Assert.AreEqual(0, view.ToArray().Length, "nothing of the old contents survived");
        Assert.AreEqual(1, events.Count, "a clear is one record");
        Assert.AreEqual(ObservableSetChangeType.SetCleared, events[0][0].ChangeType);
        Assert.IsNull(events[0][0].Item, "carrying nothing");

        source.Add("c");

        Assert.AreEqual(1, view.Count, "the view still follows the source afterwards");
        Assert.IsTrue(view.Contains("C"));
    }

    [TestMethod]
    public void AClearOfAnAlreadyEmptyViewRaisesNothing()
    {
        using var source = Observable.CreateHashSet<string>();
        using var view = Upper(source);

        var events = 0;
        view.AddObservableSetChangedHandler(args => events += args.Changes.Count);

        source.Clear();

        Assert.AreEqual(0, events, "an empty set's clear is not a change");
        Assert.AreEqual(0, view.Count);
    }

    [TestMethod]
    public void AnUntrackedEventRebuildsTheViewFromTheSource()
    {
        // A source left at the default cap drops what a batch collected once it grows past that cap and
        // says only that things changed, so the view has to rebuild rather than patch - and it passes the
        // admission on, because it cannot describe the change either.
        using var source = Observable.CreateHashSet<string>();
        using var view = Upper(source);

        var events = new List<IReadOnlyList<ObservableSetChange<string>>>();
        view.AddObservableSetChangedHandler(args => events.Add([.. args.Changes]));

        source.BeginChangeBatch();

        for (var index = 0; index <= Observable.Default.MaxCollectionBatchSize; index++)
        {
            source.Add($"x{index}");
        }

        source.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "one inner event, one delivery");
        Assert.AreEqual(1, events[0].Count, "carrying the record that says it was not described");
        Assert.AreEqual(ObservableSetChangeType.UntrackedChanges, events[0][0].ChangeType);
        Assert.AreEqual(
            Observable.Default.MaxCollectionBatchSize + 1,
            view.Count,
            "and the view was resynced from the source rather than left behind");
        Assert.IsTrue(view.Contains("X0"), "with every item mapped");
    }

    [TestMethod]
    public void ASourceEventAtTheViewsCapArrivesWithEveryRecord()
    {
        // The boundary is the cap itself, not one past it: exactly this many view records is still a
        // describable event, so they have to leave here as that many translated records.
        using var source = Observable.CreateHashSet<string>();
        using var view = Upper(source);

        var events = new List<IReadOnlyList<ObservableSetChange<string>>>();
        view.AddObservableSetChangedHandler(args => events.Add([.. args.Changes]));

        source.BeginChangeBatch();

        for (var index = 0; index < Observable.Default.MaxCollectionBatchSize; index++)
        {
            source.Add($"x{index}");
        }

        source.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "one source event, one delivery");
        Assert.AreEqual(Observable.Default.MaxCollectionBatchSize, events[0].Count, "carrying every record");
        Assert.AreEqual(
            ObservableSetChangeType.ItemAdded,
            events[0][0].ChangeType,
            "and no untracked record, because the check is strictly greater");
        Assert.AreEqual(Observable.Default.MaxCollectionBatchSize, view.Count, "while the view holds them all");
    }

    [TestMethod]
    public void ASourceTunedAboveTheViewsCapCollapsesToUntrackedWithTheContentsIntact()
    {
        // The view's cap is the default and the source's cap is its owner's, so a source that describes
        // more records in one event than the view will describe has its event collapsed here. What is
        // lost is the description; every record was applied to the storage before anything was recorded,
        // so the contents are whole.
        using var source = Observable.CreateHashSet<string>();
        using var view = Upper(source);

        source.MaxCollectionBatchSize = Observable.Default.MaxCollectionBatchSize * 4;

        var events = new List<IReadOnlyList<ObservableSetChange<string>>>();
        view.AddObservableSetChangedHandler(args => events.Add([.. args.Changes]));

        var added = Observable.Default.MaxCollectionBatchSize * 2;

        source.BeginChangeBatch();

        for (var index = 0; index < added; index++)
        {
            source.Add($"x{index}");
        }

        source.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "the source described them in one event, as its own cap allowed");
        Assert.AreEqual(1, events[0].Count, "and this view gave up on describing it");
        Assert.AreEqual(ObservableSetChangeType.UntrackedChanges, events[0][0].ChangeType);
        Assert.AreEqual(added, view.Count, "but every item was applied");
        Assert.IsTrue(view.Contains($"X{added - 1}"), "and every one of them was mapped");
    }

    [TestMethod]
    public void TheDeliveredRecordsAreTheSourcesRecordsTranslated()
    {
        using var source = Observable.CreateHashSet("a", "b");
        using var view = Upper(source);

        ObservableSetChangedEventArgs<string>? last = null;
        view.AddObservableSetChangedHandler(args => last = args);

        source.Add("c");

        Assert.IsNotNull(last, "the view delivered the change");
        Assert.AreSame(view, last.Set, "and names itself as the set, not the source");
        Assert.AreEqual(ObservableSetChangeType.ItemAdded, last.Changes[0].ChangeType);
        Assert.AreEqual("C", last.Changes[0].Item, "the mapped item, not the source's");

        source.Remove("a");

        Assert.AreEqual(ObservableSetChangeType.ItemRemoved, last!.Changes[0].ChangeType);
        Assert.AreEqual("A", last.Changes[0].Item, "the mapped item that came out");

        source.Clear();

        Assert.AreEqual(ObservableSetChangeType.SetCleared, last.Changes[0].ChangeType, "a clear is one record");
        Assert.IsNull(last.Changes[0].Item, "carrying nothing");
    }

    [TestMethod]
    public void ABatchOfSourceChangesArrivesAsOneDeliveryInOrder()
    {
        using var source = Observable.CreateHashSet("a");
        using var view = Upper(source);

        var events = new List<IReadOnlyList<ObservableSetChange<string>>>();
        view.AddObservableSetChangedHandler(args => events.Add([.. args.Changes]));

        source.BeginChangeBatch();
        source.Add("b");
        source.Remove("a");
        source.Add("c");
        source.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "three source records, one delivery from the view");
        Assert.AreEqual(3, events[0].Count, "carrying all three");

        Assert.AreEqual(ObservableSetChangeType.ItemAdded, events[0][0].ChangeType, "in the order they happened");
        Assert.AreEqual("B", events[0][0].Item);
        Assert.AreEqual(ObservableSetChangeType.ItemRemoved, events[0][1].ChangeType);
        Assert.AreEqual("A", events[0][1].Item);
        Assert.AreEqual(ObservableSetChangeType.ItemAdded, events[0][2].ChangeType);
        Assert.AreEqual("C", events[0][2].Item);
    }

    [TestMethod]
    public void TheRecordsReplayIntoAMirrorEqualToTheView()
    {
        // A listener that only ever applies the view's records has to end equal to the view - including
        // the records that never arrive, because the source changed and the view did not.
        using var source = Observable.CreateHashSet("a1", "a2", "b");
        using var view = FirstLetter(source);

        var mirror = new HashSet<string>(view.Comparer);
        mirror.UnionWith(view);

        var records = new List<ObservableSetChange<string>>();

        view.AddObservableSetChangedHandler(args =>
        {
            foreach (var change in args.Changes)
            {
                records.Add(change);

                switch (change.ChangeType)
                {
                    case ObservableSetChangeType.ItemAdded:
                        mirror.Add(change.Item!);
                        break;
                    case ObservableSetChangeType.ItemRemoved:
                        mirror.Remove(change.Item!);
                        break;
                    case ObservableSetChangeType.SetCleared:
                        mirror.Clear();
                        break;
                    default:
                        Assert.Fail("the records were all supposed to be describable");
                        break;
                }
            }
        });

        void AssertMirror(string because)
        {
            Assert.IsTrue(view.SetEquals(mirror), $"the mirror is missing something {because}");
            Assert.IsTrue(mirror.SetEquals(view), $"the mirror invented something {because}");
        }

        source.Add("a3");                               // collapses into A: no record
        AssertMirror("after a collapsing add");

        source.Add("c");                                // a new mapped item
        AssertMirror("after an add");

        source.Remove("a1");                            // still a second inner item on A
        AssertMirror("after a partial removal");

        source.Remove("a2");                            // still a third one
        AssertMirror("after another partial removal");

        source.Remove("a3");                            // the last one
        AssertMirror("after the last removal");

        source.UnionWith(["d", "e"]);                   // two records, one event
        AssertMirror("after a union");

        source.SymmetricExceptWith(["b", "f"]);         // a removal then an add
        AssertMirror("after a symmetric difference");

        source.Clear();
        AssertMirror("after the clear");

        Assert.IsFalse(
            records.Any(change => change.ChangeType == ObservableSetChangeType.ItemAdded && change.Item == "A"),
            "the collapsing adds never became records");
        Assert.IsFalse(
            records.Any(change => change.ChangeType == ObservableSetChangeType.UntrackedChanges),
            "everything here was describable");
        Assert.IsTrue(records.Count > 0, "and the listener was not simply never called");
    }

    [TestMethod]
    public void ACountReadIsNotWokenByAChangeThatLeavesTheSizeAlone()
    {
        using var source = Observable.CreateHashSet("a1");
        using var view = FirstLetter(source);

        var wakes = 0;
        using var run = Observable.CollectDependencies(() => { _ = view.Count; }, _ => wakes++);

        source.Add("b");

        Assert.AreEqual(1, wakes, "the size changed");

        source.Add("a2");

        Assert.AreEqual(1, wakes, "a collapsing add is not a change the view made");
    }

    [TestMethod]
    public void AContainsReadThroughTheInterfaceIsWokenByExactlyItsOwnItem()
    {
        using var source = Observable.CreateHashSet("a");
        using var view = Upper(source);

        IReadOnlyObservableSet<string> asInterface = view;
        var wakes = new List<string>();

        using var own = Observable.CollectDependencies(
            () => { _ = asInterface.Contains("B"); },
            _ => wakes.Add("B"));

        using var other = Observable.CollectDependencies(
            () => { _ = asInterface.Contains("C"); },
            _ => wakes.Add("C"));

        source.Add("b");

        CollectionAssert.AreEqual(new[] { "B" }, wakes, "the item that arrived, and not the other");

        source.Remove("b");

        CollectionAssert.AreEqual(new[] { "B", "B" }, wakes, "and the same entry when it leaves");
    }

    [TestMethod]
    public void TheScansAndCopyToDependOnTheContents()
    {
        using var source = Observable.CreateHashSet("a");
        using var view = Upper(source);

        var fromEnumeration = 0;
        var fromCopyTo = 0;
        var fromSetEquals = 0;
        var fromSubset = 0;
        var fromSuperset = 0;
        var fromProperSubset = 0;
        var fromProperSuperset = 0;
        var fromOverlaps = 0;

        using var enumeration = Observable.CollectDependencies(
            () =>
            {
                foreach (var item in view)
                {
                    _ = item;
                }
            },
            _ => fromEnumeration++);

        using var copy = Observable.CollectDependencies(() => view.CopyTo(new string[1], 0), _ => fromCopyTo++);
        using var setEquals = Observable.CollectDependencies(() => { _ = view.SetEquals(["A"]); }, _ => fromSetEquals++);
        using var subset = Observable.CollectDependencies(() => { _ = view.IsSubsetOf(["A"]); }, _ => fromSubset++);
        using var superset = Observable.CollectDependencies(() => { _ = view.IsSupersetOf(["A"]); }, _ => fromSuperset++);

        using var properSubset = Observable.CollectDependencies(
            () => { _ = view.IsProperSubsetOf(["A", "B"]); },
            _ => fromProperSubset++);

        using var properSuperset = Observable.CollectDependencies(
            () => { _ = view.IsProperSupersetOf([]); },
            _ => fromProperSuperset++);

        using var overlaps = Observable.CollectDependencies(() => { _ = view.Overlaps(["A"]); }, _ => fromOverlaps++);

        source.Add("b");

        Assert.AreEqual(1, fromEnumeration, "an enumeration is stale when the set changes");
        Assert.AreEqual(1, fromCopyTo, "and so is a copy");
        Assert.AreEqual(1, fromSetEquals, "and every question that reads every item");
        Assert.AreEqual(1, fromSubset);
        Assert.AreEqual(1, fromSuperset);
        Assert.AreEqual(1, fromProperSubset);
        Assert.AreEqual(1, fromProperSuperset);
        Assert.AreEqual(1, fromOverlaps);
    }

    [TestMethod]
    public void AnUntrackedEventWakesEveryReader()
    {
        using var source = Observable.CreateHashSet("a");
        using var view = Upper(source);

        var wakes = new List<string>();

        using var count = Observable.CollectDependencies(() => { _ = view.Count; }, _ => wakes.Add("count"));
        using var present = Observable.CollectDependencies(() => { _ = view.Contains("A"); }, _ => wakes.Add("A"));
        using var absent = Observable.CollectDependencies(() => { _ = view.Contains("Z"); }, _ => wakes.Add("Z"));

        source.BeginChangeBatch();

        // Past the source's cap, so its event says only that things changed - which could have been any
        // item, so the view resyncs and marks everything.
        for (var index = 0; index <= Observable.Default.MaxCollectionBatchSize; index++)
        {
            source.Add($"x{index}");
        }

        source.EndChangeBatch();

        CollectionAssert.AreEquivalent(
            new[] { "count", "A", "Z" },
            wakes,
            "a reader is woken whether or not its own answer actually changed");
    }

    [TestMethod]
    public void TheComparerDecidesWhichItemsAreTheSameAndDefaultsToTheTypesOwn()
    {
        using var source = Observable.CreateHashSet("x");
        using var view = new ObservableMappedReadOnlySetView<string, string>(
            source,
            text => text,
            StringComparer.OrdinalIgnoreCase);

        var events = 0;
        view.AddObservableSetChangedHandler(args => events += args.Changes.Count);

        Assert.AreSame(StringComparer.OrdinalIgnoreCase, view.Comparer, "the comparer the view was given");

        source.Add("X");

        Assert.AreEqual(1, view.Count, "the view's comparer says it is the same item");
        Assert.AreEqual(0, events);
        Assert.IsTrue(view.Contains("x"), "whichever spelling the read uses");

        using var plainSource = Observable.CreateHashSet("x");
        using var plainView = new ObservableMappedReadOnlySetView<string, string>(plainSource, text => text);

        Assert.AreSame(
            EqualityComparer<string>.Default,
            plainView.Comparer,
            "no comparer argument means the type's own equality");

        plainSource.Add("X");

        Assert.AreEqual(2, plainView.Count, "which says those are two items");
    }

    [TestMethod]
    public void DisposingUnsubscribesFromTheSourceAndLeavesItUsable()
    {
        using var source = Observable.CreateHashSet("a");
        var view = Upper(source);

        var events = new List<ObservableSetChangedEventArgs<string>>();
        view.AddObservableSetChangedHandler(events.Add);

        view.Dispose();

        Assert.AreEqual(1, events.Count, "one last event, so a registrant learns to let go");
        Assert.AreEqual(0, events[0].Changes.Count, "which carries no records");
        Assert.IsTrue(events[0].Set.IsDisposed, "and says the view is dead");

        source.Add("b");

        Assert.AreEqual(1, events.Count, "the view unsubscribed, so the source's change reached nobody");
        Assert.AreEqual(2, source.Count, "and the source is untouched");
        Assert.IsTrue(source.Contains("b"), "and is still readable");
    }

    [TestMethod]
    public void DisposingTwiceAndRemovingAHandlerAfterwardsStaySafe()
    {
        using var source = Observable.CreateHashSet("a");
        var view = Upper(source);

        view.Dispose();
        view.Dispose();
        view.RemoveObservableSetChangedHandler(_ => { });

        Assert.IsTrue(view.IsDisposed, "teardown paths release what they hold without knowing what state it is in");
    }

    [TestMethod]
    public void EveryOtherMemberThrowsOnceDisposed()
    {
        using var source = Observable.CreateHashSet("a");
        var view = Upper(source);

        view.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = view.Count, "a count read");
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = view.Contains("A"), "an item read");
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

        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = view.SetEquals(["A"]), "a scan");
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = view.IsSubsetOf(["A"]), "another scan");
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = view.IsSupersetOf(["A"]), "another scan");
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = view.IsProperSubsetOf(["A"]), "another scan");
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = view.IsProperSupersetOf(["A"]), "another scan");
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = view.Overlaps(["A"]), "another scan");

        Assert.ThrowsExactly<ObjectDisposedException>(
            () => view.AddObservableSetChangedHandler(_ => { }),
            "a registration");
    }

    [TestMethod]
    public void ADisposedSourceLeavesTheViewReadableWithItsLastContents()
    {
        // The source's one empty event is its disposal: nothing can change any more, so the view keeps
        // what it last had rather than emptying itself or dying with the source.
        var source = Observable.CreateHashSet("a", "b");
        var view = Upper(source);
        var events = 0;

        view.AddObservableSetChangedHandler(_ => events++);

        source.Dispose();

        Assert.AreEqual(0, events, "the source's final event is not a change to report");
        Assert.IsFalse(view.IsDisposed, "the view's lifetime is its own");
        Assert.AreEqual(2, view.Count, "and it still holds what it last mapped");
        Assert.IsTrue(view.Contains("A"));

        view.Dispose();

        Assert.IsTrue(view.IsDisposed, "disposing a view whose source is gone is safe");
    }

    [TestMethod]
    public void ANullSourceOrSelectorIsRefused()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => _ = new ObservableMappedReadOnlySetView<string, string>(null!, text => text));

        using var source = Observable.CreateHashSet("a");

        Assert.ThrowsExactly<ArgumentNullException>(
            () => _ = new ObservableMappedReadOnlySetView<string, string>(source, null!));
    }

    private static ObservableMappedReadOnlySetView<string, string> Upper(IReadOnlyObservableSet<string> source) =>
        new(source, text => text.ToUpperInvariant());

    private static ObservableMappedReadOnlySetView<string, string> FirstLetter(IReadOnlyObservableSet<string> source) =>
        new(source, text => text[..1].ToUpperInvariant());

    private sealed class Token
    {
        internal Token(string key) => Key = key;

        internal string Key { get; }

        public override string ToString() => Key;
    }

    private sealed class TokenComparer : IEqualityComparer<Token>
    {
        public bool Equals(Token? x, Token? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null && x.Key == y.Key);

        public int GetHashCode(Token obj) => StringComparer.Ordinal.GetHashCode(obj.Key);
    }
}
