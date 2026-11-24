namespace Kolvir.Feature.Observables.Tests;

/// <summary>
/// A mapped read-only view over a dictionary: the projection is eager and cached per key, the source's
/// records are translated one for one into the mapped values, reads are the same three dependencies a
/// dictionary publishes, and disposal ends this view's subscription without touching the source
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 7.5).
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class ObservableMappedReadOnlyDictionaryViewTests
{
    [TestMethod]
    public void ConstructionMapsWhatTheSourceAlreadyHolds()
    {
        using var source = Source(("a", "x"), ("b", "y"));
        using var view = Upper(source);

        Assert.AreEqual(2, view.Count, "one entry per source entry");
        Assert.AreEqual("X", view["a"], "with the value the function produced");
        Assert.AreEqual("Y", view["b"]);
    }

    [TestMethod]
    public void EqualValuesUnderDifferentKeysStayTwoEntries()
    {
        // The key is the identity and the value is not, so a dictionary holding one value under two keys
        // is a normal dictionary and the view keeps both entries. Nothing collapses here, unlike the set.
        using var source = Source(("a", "same"), ("b", "same"));
        using var view = new ObservableMappedReadOnlyDictionaryView<string, string, string>(source, text => text);

        Assert.AreEqual(2, view.Count, "two keys, so two entries");
        CollectionAssert.AreEquivalent(new[] { "a", "b" }, view.Keys.ToArray(), "both keys are there");
        Assert.AreEqual(2, view.Values.Count(), "and both values, which happen to be equal");
    }

    [TestMethod]
    public void NullValuesAreHeldLikeAnyOther()
    {
        // TOuter is deliberately unconstrained, because a dictionary's values may be null and nothing
        // about a keyed projection requires otherwise. There is no outer comparer to consult either.
        using var source = Source(("a", "x"), ("b", "y"));
        using var view = new ObservableMappedReadOnlyDictionaryView<string, string, string?>(source, _ => null);

        Assert.AreEqual(2, view.Count, "one entry per key even when every value maps to null");
        Assert.IsNull(view["a"], "and a null value reads back as null");
        Assert.IsTrue(view.TryGetValue("b", out var value));
        Assert.IsNull(value);
    }

    [TestMethod]
    public void AnAddMapsTheNewValueOnceAndReportsTheKey()
    {
        var calls = 0;

        using var source = Source(("a", "x"));
        using var view = new ObservableMappedReadOnlyDictionaryView<string, string, string>(
            source,
            text =>
            {
                calls++;
                return text.ToUpperInvariant();
            });

        Assert.AreEqual(1, calls, "the source's entry was mapped once on construction");

        var events = new List<IReadOnlyList<ObservableDictionaryChange<string, string>>>();
        view.AddObservableDictionaryChangedHandler(args => events.Add([.. args.Changes]));

        source.Add("b", "yy");

        Assert.AreEqual(2, calls, "an add is mapped exactly once");
        Assert.AreEqual(1, events.Count, "and reported as one record");
        Assert.AreEqual(ObservableDictionaryChangeType.ItemAdded, events[0][0].ChangeType);
        Assert.AreEqual("b", events[0][0].Key);
        Assert.AreEqual("YY", events[0][0].NewValue, "the mapped value, not the source's");
        Assert.IsNull(events[0][0].OldValue, "which the kind does not promise");
        Assert.AreEqual("YY", view["b"]);
    }

    [TestMethod]
    public void AChangeReportsTheCachedOldValueAndMapsOnce()
    {
        var calls = 0;

        using var source = Source(("a", "x"));
        using var view = new ObservableMappedReadOnlyDictionaryView<string, string, string>(
            source,
            text =>
            {
                calls++;
                return text.ToUpperInvariant();
            });

        var events = new List<IReadOnlyList<ObservableDictionaryChange<string, string>>>();
        view.AddObservableDictionaryChangedHandler(args => events.Add([.. args.Changes]));

        source["a"] = "zz";

        Assert.AreEqual(2, calls, "a change maps the new value exactly once");
        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(ObservableDictionaryChangeType.ItemChanged, events[0][0].ChangeType);
        Assert.AreEqual("a", events[0][0].Key);
        Assert.AreEqual("X", events[0][0].OldValue, "the value the view had cached, which is the mapped one");
        Assert.AreEqual("ZZ", events[0][0].NewValue);
        Assert.AreEqual("ZZ", view["a"]);
    }

    [TestMethod]
    public void ARemovalReportsTheCachedValueAndMapsNothing()
    {
        var calls = 0;

        using var source = Source(("a", "x"), ("b", "y"));
        using var view = new ObservableMappedReadOnlyDictionaryView<string, string, string>(
            source,
            text =>
            {
                calls++;
                return text.ToUpperInvariant();
            });

        Assert.AreEqual(2, calls);

        var events = new List<IReadOnlyList<ObservableDictionaryChange<string, string>>>();
        view.AddObservableDictionaryChangedHandler(args => events.Add([.. args.Changes]));

        source.Remove("a");

        Assert.AreEqual(2, calls, "a removal never runs the function: the record names the key and the view is keyed by it");
        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(ObservableDictionaryChangeType.ItemRemoved, events[0][0].ChangeType);
        Assert.AreEqual("a", events[0][0].Key);
        Assert.AreEqual("X", events[0][0].OldValue, "the value the view already held");
        Assert.AreEqual(1, view.Count);
        Assert.IsFalse(view.ContainsKey("a"));
    }

    [TestMethod]
    public void AClearEmptiesTheViewAndLeavesItUsable()
    {
        var calls = 0;

        using var source = Source(("a", "x"), ("b", "y"));
        using var view = new ObservableMappedReadOnlyDictionaryView<string, string, string>(
            source,
            text =>
            {
                calls++;
                return text.ToUpperInvariant();
            });

        var events = new List<IReadOnlyList<ObservableDictionaryChange<string, string>>>();
        view.AddObservableDictionaryChangedHandler(args => events.Add([.. args.Changes]));

        source.Clear();

        Assert.AreEqual(2, calls, "a clear maps nothing");
        Assert.AreEqual(0, view.Count);
        Assert.AreEqual(0, view.Keys.Count(), "nothing of the old contents survived");
        Assert.AreEqual(1, events.Count, "a clear is one record");
        Assert.AreEqual(ObservableDictionaryChangeType.DictionaryCleared, events[0][0].ChangeType);
        Assert.IsNull(events[0][0].Key, "carrying nothing");

        source.Add("c", "z");

        Assert.AreEqual(1, view.Count, "the view still follows the source afterwards");
        Assert.AreEqual("Z", view["c"]);
    }

    [TestMethod]
    public void AnUntrackedEventRebuildsTheViewFromTheSource()
    {
        // A source left at the default cap drops what a batch collected once it grows past that cap and
        // says only that things changed, so the view has to rebuild rather than patch - and it passes the
        // admission on, because it cannot describe the change either.
        using var source = Source();
        using var view = Upper(source);

        var events = new List<IReadOnlyList<ObservableDictionaryChange<string, string>>>();
        view.AddObservableDictionaryChangedHandler(args => events.Add([.. args.Changes]));

        source.BeginChangeBatch();

        for (var index = 0; index <= Observable.Default.MaxCollectionBatchSize; index++)
        {
            source.Add($"x{index}", $"v{index}");
        }

        source.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "one inner event, one delivery");
        Assert.AreEqual(1, events[0].Count, "carrying the record that says it was not described");
        Assert.AreEqual(ObservableDictionaryChangeType.UntrackedChanges, events[0][0].ChangeType);
        Assert.AreEqual(
            Observable.Default.MaxCollectionBatchSize + 1,
            view.Count,
            "and the view was resynced from the source rather than left behind");
        Assert.AreEqual("V0", view["x0"], "with every value mapped");
    }

    [TestMethod]
    public void ASourceEventAtTheViewsCapArrivesWithEveryRecord()
    {
        // The boundary is the cap itself, not one past it: exactly this many view records is still a
        // describable event. Unlike the set's, the dictionary's exact-cap case is intrinsically sound -
        // every source record translates to exactly one view record, so a batch of the cap's worth cannot
        // arrive short and there is no distinct-outer care to take.
        using var source = Source();
        using var view = Upper(source);

        var events = new List<IReadOnlyList<ObservableDictionaryChange<string, string>>>();
        view.AddObservableDictionaryChangedHandler(args => events.Add([.. args.Changes]));

        source.BeginChangeBatch();

        for (var index = 0; index < Observable.Default.MaxCollectionBatchSize; index++)
        {
            source.Add($"x{index}", $"v{index}");
        }

        source.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "one source event, one delivery");
        Assert.AreEqual(Observable.Default.MaxCollectionBatchSize, events[0].Count, "carrying every record");
        Assert.AreEqual(
            ObservableDictionaryChangeType.ItemAdded,
            events[0][0].ChangeType,
            "and no untracked record, because the check is strictly greater");
        Assert.AreEqual(Observable.Default.MaxCollectionBatchSize, view.Count, "while the view holds them all");
    }

    [TestMethod]
    public void ASourceTunedAboveTheViewsCapCollapsesToUntrackedWithTheContentsIntact()
    {
        // The view's cap is the default and the source's cap is its owner's, so a source that describes
        // more records in one event than the view will describe has its event collapsed here. What is
        // lost is the description; every record was applied to the cache before anything was recorded, so
        // the contents are whole.
        using var source = Source();
        using var view = Upper(source);

        source.MaxCollectionBatchSize = Observable.Default.MaxCollectionBatchSize * 4;

        var events = new List<IReadOnlyList<ObservableDictionaryChange<string, string>>>();
        view.AddObservableDictionaryChangedHandler(args => events.Add([.. args.Changes]));

        var added = Observable.Default.MaxCollectionBatchSize * 2;

        source.BeginChangeBatch();

        for (var index = 0; index < added; index++)
        {
            source.Add($"x{index}", $"v{index}");
        }

        source.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "the source described them in one event, as its own cap allowed");
        Assert.AreEqual(1, events[0].Count, "and this view gave up on describing it");
        Assert.AreEqual(ObservableDictionaryChangeType.UntrackedChanges, events[0][0].ChangeType);
        Assert.AreEqual(added, view.Count, "but every entry was applied");
        Assert.AreEqual($"V{added - 1}", view[$"x{added - 1}"], "and every one of them was mapped");
    }

    [TestMethod]
    public void ABatchOfSourceChangesArrivesAsOneDeliveryInOrder()
    {
        using var source = Source(("a", "x"));
        using var view = Upper(source);

        var events = new List<IReadOnlyList<ObservableDictionaryChange<string, string>>>();
        view.AddObservableDictionaryChangedHandler(args => events.Add([.. args.Changes]));

        source.BeginChangeBatch();
        source.Add("b", "y");
        source["a"] = "z";
        source.Add("c", "w");
        source.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "three source records, one delivery from the view");
        Assert.AreEqual(3, events[0].Count, "carrying all three");

        Assert.AreEqual(ObservableDictionaryChangeType.ItemAdded, events[0][0].ChangeType, "in the order they happened");
        Assert.AreEqual("b", events[0][0].Key);
        Assert.AreEqual(ObservableDictionaryChangeType.ItemChanged, events[0][1].ChangeType);
        Assert.AreEqual("a", events[0][1].Key);
        Assert.AreEqual(ObservableDictionaryChangeType.ItemAdded, events[0][2].ChangeType);
        Assert.AreEqual("c", events[0][2].Key);
    }

    [TestMethod]
    public void TheDeliveredRecordsAreTheSourcesRecordsTranslated()
    {
        using var source = Source(("a", "x"));
        using var view = Upper(source);

        ObservableDictionaryChangedEventArgs<string, string>? last = null;
        view.AddObservableDictionaryChangedHandler(args => last = args);

        source.Add("b", "y");

        Assert.IsNotNull(last, "the view delivered the change");
        Assert.AreSame(view, last.Dictionary, "and names itself as the dictionary, not the source");
        Assert.AreEqual(ObservableDictionaryChangeType.ItemAdded, last.Changes[0].ChangeType);
        Assert.AreEqual("b", last.Changes[0].Key);
        Assert.AreEqual("Y", last.Changes[0].NewValue);

        source["a"] = "q";

        Assert.AreEqual(ObservableDictionaryChangeType.ItemChanged, last!.Changes[0].ChangeType);
        Assert.AreEqual("X", last.Changes[0].OldValue, "the mapped value the view had");
        Assert.AreEqual("Q", last.Changes[0].NewValue);

        source.Remove("a");

        Assert.AreEqual(ObservableDictionaryChangeType.ItemRemoved, last.Changes[0].ChangeType);
        Assert.AreEqual("Q", last.Changes[0].OldValue, "the value that was beside the key");

        source.Clear();

        Assert.AreEqual(ObservableDictionaryChangeType.DictionaryCleared, last.Changes[0].ChangeType, "a clear is one record");
        Assert.IsNull(last.Changes[0].Key, "carrying nothing");
    }

    [TestMethod]
    public void TheRecordsReplayIntoAMirrorEqualToTheView()
    {
        // A listener that only ever applies the view's records has to end equal to the view, so the record
        // stream is the whole contract rather than a hint about it.
        using var source = Source(("a", "x"), ("b", "y"));
        using var view = Upper(source);

        var mirror = new Dictionary<string, string>(view.Comparer);

        foreach (var pair in view)
        {
            mirror.Add(pair.Key, pair.Value);
        }

        var records = new List<ObservableDictionaryChange<string, string>>();

        view.AddObservableDictionaryChangedHandler(args =>
        {
            foreach (var change in args.Changes)
            {
                records.Add(change);

                switch (change.ChangeType)
                {
                    case ObservableDictionaryChangeType.ItemAdded:
                        mirror.Add(change.Key!, change.NewValue!);
                        break;
                    case ObservableDictionaryChangeType.ItemChanged:
                        mirror[change.Key!] = change.NewValue!;
                        break;
                    case ObservableDictionaryChangeType.ItemRemoved:
                        mirror.Remove(change.Key!);
                        break;
                    case ObservableDictionaryChangeType.DictionaryCleared:
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
            Assert.AreEqual(view.Count, mirror.Count, $"the mirror disagrees about the size {because}");

            foreach (var pair in view)
            {
                Assert.IsTrue(
                    mirror.TryGetValue(pair.Key, out var value) && value == pair.Value,
                    $"the mirror disagrees about {pair.Key} {because}");
            }
        }

        source.Add("c", "z");
        AssertMirror("after an add");

        source["a"] = "q";
        AssertMirror("after a change");

        source.Remove("b");
        AssertMirror("after a removal");

        source["c"] = "w";
        AssertMirror("after the other key changed");

        source.Remove("a");
        AssertMirror("after the last removal of that key");

        source.Clear();
        AssertMirror("after the clear");

        source.Add("d", "v");
        AssertMirror("after an add following the clear");

        Assert.IsTrue(records.Count > 0, "and the listener was not simply never called");
        Assert.IsFalse(
            records.Any(change => change.ChangeType == ObservableDictionaryChangeType.UntrackedChanges),
            "everything here was describable");
    }

    [TestMethod]
    public void TheComparerIsTheSourcesKeyComparer()
    {
        // There is no outer comparer on purpose: the key is the identity, so the source's key comparer is
        // the view's identity too - which is what a mirror built from this view's records has to use.
        using var source = Observable.CreateDictionary(
            StringComparer.OrdinalIgnoreCase,
            new Dictionary<string, string> { ["a"] = "x" });

        using var view = Upper(source);

        Assert.AreSame(StringComparer.OrdinalIgnoreCase, view.Comparer, "the comparer the source was given");

        source.Remove("A");

        Assert.AreEqual(0, view.Count, "the source's comparer says that is the same entry, whichever spelling the removal used");
    }

    [TestMethod]
    public void ACountReadIsNotWokenByAValueChange()
    {
        using var source = Source(("a", "x"));
        using var view = Upper(source);

        var wakes = 0;
        using var run = Observable.CollectDependencies(() => { _ = view.Count; }, _ => wakes++);

        source["a"] = "y";

        Assert.AreEqual(0, wakes, "the size did not change");

        source.Add("b", "z");

        Assert.AreEqual(1, wakes, "a key arriving does change it");
    }

    [TestMethod]
    public void KeysFollowTheStructureAndNotTheValues()
    {
        // Keys is the structure, exactly as it is on the dictionary: a value changing beside a key that
        // stayed is not a structural change, while a key arriving is - and that wakes a Keys reader
        // without anything having read the contents.
        using var source = Source(("a", "x"));
        using var view = Upper(source);

        var wakes = 0;

        using var run = Observable.CollectDependencies(
            () =>
            {
                foreach (var key in view.Keys)
                {
                    _ = key;
                }
            },
            _ => wakes++);

        source["a"] = "y";

        Assert.AreEqual(0, wakes, "the same keys, so the structure did not move");

        source.Add("b", "z");

        Assert.AreEqual(1, wakes, "a key arrived");
    }

    [TestMethod]
    public void AKeyReadIsNotWokenByAChangeToAnotherKey()
    {
        // The read a rendered row makes - "what is beside my key" - and the reason an address is worth an
        // entry: a change to one key must not re-render the other rows.
        using var source = Source(("a", "x"), ("b", "y"));
        using var view = Upper(source);

        var wakes = new List<string>();

        using var first = Observable.CollectDependencies(() => { _ = view.TryGetValue("a", out _); }, _ => wakes.Add("a"));
        using var second = Observable.CollectDependencies(() => { _ = view.TryGetValue("b", out _); }, _ => wakes.Add("b"));

        source["b"] = "q";

        CollectionAssert.AreEqual(new[] { "b" }, wakes, "only the key that changed");

        source.Add("c", "z");

        CollectionAssert.AreEqual(new[] { "b" }, wakes, "and a key neither reader asked about wakes neither");
    }

    [TestMethod]
    public void TheValuesAndEnumerationDependOnTheContents()
    {
        using var source = Source(("a", "x"));
        using var view = Upper(source);

        var fromValues = 0;
        var fromEnumeration = 0;

        using var values = Observable.CollectDependencies(() => { _ = view.Values.Count(); }, _ => fromValues++);

        using var enumeration = Observable.CollectDependencies(
            () =>
            {
                foreach (var pair in view)
                {
                    _ = pair;
                }
            },
            _ => fromEnumeration++);

        source["a"] = "y";

        Assert.AreEqual(1, fromValues, "the values are stale when any value changes");
        Assert.AreEqual(1, fromEnumeration, "and so is an enumeration");
    }

    [TestMethod]
    public void TheIndexerReadOfAnAbsentKeyPublishesNothing()
    {
        // The dictionary's own departure from "publish before the check", mirrored here: a key that is
        // gone is the wrong shape of address to remember, and TryGetValue and ContainsKey are the shapes
        // that survive it.
        using var source = Source(("a", "x"));
        using var view = Upper(source);

        using var run = Observable.CollectDependencies(() => { _ = view["zzz"]; }, _ => { });

        Assert.IsTrue(run.IsFaulted, "the read threw");
        Assert.AreEqual(0, run.Dependencies.Count, "and remembers nothing about a key that is not there");
    }

    [TestMethod]
    public void ATryGetValueReadOfAnAbsentKeyIsWokenWhenTheKeyAppears()
    {
        using var source = Source(("a", "x"));
        using var view = Upper(source);

        var wakes = 0;

        using var run = Observable.CollectDependencies(
            () =>
            {
                _ = view.TryGetValue("zzz", out var value);
                _ = value;
            },
            _ => wakes++);

        Assert.IsFalse(view.ContainsKey("zzz"));

        source.Add("zzz", "v");

        Assert.AreEqual(1, wakes, "the key appeared");
        Assert.AreEqual("V", view["zzz"]);
    }

    [TestMethod]
    public void AnUntrackedEventWakesEveryReader()
    {
        using var source = Source(("a", "x"));
        using var view = Upper(source);

        var wakes = new List<string>();

        using var count = Observable.CollectDependencies(() => { _ = view.Count; }, _ => wakes.Add("count"));
        using var keys = Observable.CollectDependencies(() => { _ = view.Keys.Count(); }, _ => wakes.Add("keys"));
        using var present = Observable.CollectDependencies(() => { _ = view.TryGetValue("a", out _); }, _ => wakes.Add("a"));
        using var absent = Observable.CollectDependencies(() => { _ = view.TryGetValue("zzz", out _); }, _ => wakes.Add("zzz"));
        using var contents = Observable.CollectDependencies(() => { _ = view.Values.Count(); }, _ => wakes.Add("values"));

        source.BeginChangeBatch();

        // Past the source's cap, so its event says only that things changed - which could have been any
        // key, so the view resyncs and marks everything.
        for (var index = 0; index <= Observable.Default.MaxCollectionBatchSize; index++)
        {
            source.Add($"x{index}", $"v{index}");
        }

        source.EndChangeBatch();

        CollectionAssert.AreEquivalent(
            new[] { "count", "keys", "a", "zzz", "values" },
            wakes,
            "a reader is woken whether or not its own answer actually changed");
    }

    [TestMethod]
    public void ContainsComparesTheValueAndPublishesTheContents()
    {
        using var source = Source(("a", "x"));
        using var view = Upper(source);

        Assert.IsFalse(
            view.Contains(new KeyValuePair<string, string>("a", "x")),
            "the view holds the mapped value, not the source's");

        Assert.IsTrue(view.Contains(new KeyValuePair<string, string>("a", "X")));

        var wakes = 0;

        using var run = Observable.CollectDependencies(
            () => { _ = view.Contains(new KeyValuePair<string, string>("a", "X")); },
            _ => wakes++);

        source["a"] = "y";

        Assert.AreEqual(1, wakes, "a pair contains a value, so the contents dependency is what it reads");
    }

    [TestMethod]
    public void CopyToRoundTripsEveryEntry()
    {
        using var source = Source(("a", "x"), ("b", "y"));
        using var view = Upper(source);

        var array = new KeyValuePair<string, string>[2];
        view.CopyTo(array, 0);

        CollectionAssert.AreEquivalent(
            new[]
            {
                new KeyValuePair<string, string>("a", "X"),
                new KeyValuePair<string, string>("b", "Y"),
            },
            array,
            "every entry, mapped");

        var wakes = 0;

        using var run = Observable.CollectDependencies(() => view.CopyTo(new KeyValuePair<string, string>[2], 0), _ => wakes++);

        source["a"] = "z";

        Assert.AreEqual(1, wakes, "a copy reads every entry, so it is stale when any value changes");
    }

    [TestMethod]
    public void AThrowingSelectorLeavesTheViewBehindAndARemovalHealsIt()
    {
        // A selector that throws is the reachable cause of a view that is behind the source: the source
        // applies and records its own change first, then the exception escapes this view's translation -
        // which is where the view's own application of it would have happened. The source holds the key
        // and the view does not. The next record naming that key finds no entry, so the view rebuilds
        // from the source and says the change was not described, instead of throwing, dropping the
        // record, or inventing an old value from the record's fields.
        using var source = Source();
        var calls = 0;

        using var view = new ObservableMappedReadOnlyDictionaryView<string, string, string>(
            source,
            text =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new InvalidOperationException("the selector is not deterministic");
                }

                return text.ToUpperInvariant();
            });

        Assert.ThrowsExactly<InvalidOperationException>(
            () => source.Add("a", "boom"),
            "the selector's exception propagates out of the source's delivery");

        Assert.AreEqual(1, source.Count, "the source applied and recorded its change before the handler failed");
        Assert.AreEqual(0, view.Count, "and the view never applied it, so the view is behind the source");

        var events = new List<IReadOnlyList<ObservableDictionaryChange<string, string>>>();
        view.AddObservableDictionaryChangedHandler(args => events.Add([.. args.Changes]));

        source.Remove("a");

        Assert.AreEqual(1, events.Count, "one source record, one delivery");
        Assert.AreEqual(1, events[0].Count, "carrying the record that says it was not described");
        Assert.AreEqual(
            ObservableDictionaryChangeType.UntrackedChanges,
            events[0][events[0].Count - 1].ChangeType,
            "because the removal named a key the view never held");
        Assert.AreEqual(0, view.Count, "and the view was rebuilt from the now-empty source");
        Assert.AreEqual(1, calls, "neither the removal nor the resync of an empty source ran the function");
        Assert.AreEqual(0, source.Count);
    }

    [TestMethod]
    public void AThrowingSelectorLeavesTheViewBehindAndAChangeHealsIt()
    {
        // The same disagreement on the change path: a change naming a key the view does not hold rebuilds
        // from the source, which brings the entry in with the mapped value it has now.
        using var source = Source();
        var calls = 0;

        using var view = new ObservableMappedReadOnlyDictionaryView<string, string, string>(
            source,
            text =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new InvalidOperationException("the selector is not deterministic");
                }

                return text.ToUpperInvariant();
            });

        Assert.ThrowsExactly<InvalidOperationException>(() => source.Add("a", "x"));
        Assert.AreEqual(0, view.Count);

        var events = new List<IReadOnlyList<ObservableDictionaryChange<string, string>>>();
        view.AddObservableDictionaryChangedHandler(args => events.Add([.. args.Changes]));

        source["a"] = "y";

        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(1, events[0].Count, "one admission, because the change named a key the view never held");
        Assert.AreEqual(ObservableDictionaryChangeType.UntrackedChanges, events[0][0].ChangeType);
        Assert.AreEqual(1, view.Count, "and the resync brought the entry in");
        Assert.AreEqual("Y", view["a"], "with the value it maps to now");
    }

    [TestMethod]
    public void DisposingUnsubscribesFromTheSourceAndLeavesItUsable()
    {
        using var source = Source(("a", "x"));
        var view = Upper(source);

        var events = new List<ObservableDictionaryChangedEventArgs<string, string>>();
        view.AddObservableDictionaryChangedHandler(events.Add);

        view.Dispose();

        Assert.AreEqual(1, events.Count, "one last event, so a registrant learns to let go");
        Assert.AreEqual(0, events[0].Changes.Count, "which carries no records");
        Assert.IsTrue(events[0].Dictionary.IsDisposed, "and says the view is dead");

        source.Add("b", "y");

        Assert.AreEqual(1, events.Count, "the view unsubscribed, so the source's change reached nobody");
        Assert.AreEqual(2, source.Count, "and the source is untouched");
        Assert.AreEqual("y", source["b"], "and is still readable");
    }

    [TestMethod]
    public void DisposingTwiceAndRemovingAHandlerAfterwardsStaySafe()
    {
        using var source = Source(("a", "x"));
        var view = Upper(source);

        view.Dispose();
        view.Dispose();
        view.RemoveObservableDictionaryChangedHandler(_ => { });

        Assert.IsTrue(view.IsDisposed, "teardown paths release what they hold without knowing what state it is in");
    }

    [TestMethod]
    public void EveryOtherMemberThrowsOnceDisposed()
    {
        using var source = Source(("a", "x"));
        var view = Upper(source);

        view.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = view.Count, "a count read");
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = view.Keys, "a keys read");
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = view.Values, "a values read");
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = view["a"], "a key read");
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = view.ContainsKey("a"), "a presence read");
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = view.TryGetValue("a", out _), "a lookup");
        Assert.ThrowsExactly<ObjectDisposedException>(
            () => _ = view.Contains(new KeyValuePair<string, string>("a", "X")),
            "a pair read");

        Assert.ThrowsExactly<ObjectDisposedException>(
            () => view.CopyTo(new KeyValuePair<string, string>[1], 0),
            "a copy");

        Assert.ThrowsExactly<ObjectDisposedException>(
            () =>
            {
                foreach (var pair in view)
                {
                    _ = pair;
                }
            },
            "an enumeration");

        Assert.ThrowsExactly<ObjectDisposedException>(
            () => view.AddObservableDictionaryChangedHandler(_ => { }),
            "a registration");
    }

    [TestMethod]
    public void ADisposedSourceLeavesTheViewReadableWithItsLastContents()
    {
        // The source's one empty event is its disposal: nothing can change any more, so the view keeps
        // what it last had rather than emptying itself or dying with the source.
        var source = Source(("a", "x"), ("b", "y"));
        var view = Upper(source);
        var events = 0;

        view.AddObservableDictionaryChangedHandler(_ => events++);

        source.Dispose();

        Assert.AreEqual(0, events, "the source's final event is not a change to report");
        Assert.IsFalse(view.IsDisposed, "the view's lifetime is its own");
        Assert.AreEqual(2, view.Count, "and it still holds what it last mapped");
        Assert.AreEqual("X", view["a"]);

        view.Dispose();

        Assert.IsTrue(view.IsDisposed, "disposing a view whose source is gone is safe");
    }

    [TestMethod]
    public void ANullSourceOrSelectorIsRefused()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => _ = new ObservableMappedReadOnlyDictionaryView<string, string, string>(null!, text => text));

        using var source = Source(("a", "x"));

        Assert.ThrowsExactly<ArgumentNullException>(
            () => _ = new ObservableMappedReadOnlyDictionaryView<string, string, string>(source, null!));
    }

    private static IObservableDictionary<string, string> Source(params (string Key, string Value)[] items) =>
        Observable.CreateDictionary(items.Select(item => new KeyValuePair<string, string>(item.Key, item.Value)));

    private static ObservableMappedReadOnlyDictionaryView<string, string, string> Upper(
        IReadOnlyObservableDictionary<string, string> source) =>
        new(source, text => text.ToUpperInvariant());
}
