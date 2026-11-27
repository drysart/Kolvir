namespace Kolvir.Feature.Observables.Tests;

/// <summary>
/// A dictionary seen as a read-only set of its pairs: the element is the pair, so identity includes the
/// value, the algebra agrees with a <see cref="HashSet{T}"/> built on the same comparer, and the set events
/// mirror the dictionary's (dev-docs/OBSERVABLE-COLLECTIONS.md section 7.7).
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class ObservableDictionarySetViewTests
{
    [TestMethod]
    public void ContainsIsTrueOnlyForTheExactPair()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });

        var set = SetView(dictionary);

        Assert.IsTrue(set.Contains(new("a", 1)), "the key is there with that value");
        Assert.IsFalse(set.Contains(new("a", 2)), "the same key with a different value is a stale pair");
        Assert.IsFalse(set.Contains(new("b", 1)), "an absent key is not in the set at all");
    }

    [TestMethod]
    public void ContainsAsksTheDictionariesComparerWhichKeyItIs()
    {
        // The BCL's ICollection<KeyValuePair>.Contains answers the key half with the type's default
        // equality, which disagrees with a dictionary that was built with any other comparer.
        using var dictionary = Observable.CreateDictionary(
            StringComparer.OrdinalIgnoreCase,
            new Dictionary<string, int> { ["a"] = 1 });

        var set = SetView(dictionary);

        Assert.IsTrue(set.Contains(new("A", 1)), "the dictionary's comparer says it is the same key");
        Assert.IsFalse(set.Contains(new("A", 2)), "and the value still has to match");
    }

    [TestMethod]
    public void ThePublicComparerStaysTheKeyComparerAndTheSetViewHasThePairComparer()
    {
        using var dictionary = Observable.CreateDictionary(
            StringComparer.OrdinalIgnoreCase,
            new Dictionary<string, int> { ["a"] = 1 });

        Assert.AreSame(
            StringComparer.OrdinalIgnoreCase,
            dictionary.Comparer,
            "the member a dictionary caller sees is still the key comparer");

        var pairs = SetView(dictionary).Comparer;

        Assert.IsTrue(pairs.Equals(new("A", 1), new("a", 1)), "the pair's key is compared by the dictionary's comparer");
        Assert.IsFalse(pairs.Equals(new("a", 1), new("a", 2)), "and its value is compared as a value");
        Assert.IsFalse(pairs.Equals(new("a", 1), new("b", 1)), "two keys are two elements");
        Assert.AreEqual(
            pairs.GetHashCode(new("A", 1)),
            pairs.GetHashCode(new("a", 1)),
            "pairs it calls equal hash alike");
    }

    [TestMethod]
    public void TheSameKeysWithADifferentValueAreNotSetEqual()
    {
        using var first = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
        using var second = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1, ["b"] = 3 });
        using var identical = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });

        var set = SetView(first);

        Assert.IsTrue(set.SetEquals(identical), "the same pairs are the same set");
        Assert.IsFalse(set.SetEquals(second), "the same keys with one different value are not");
    }

    [TestMethod]
    public void TheAlgebraAnswersExactlyAsAHashSetOfTheSamePairsWould()
    {
        // The cheapest proof that the six questions mean what IReadOnlySet says they mean: build the
        // reference from the same pairs with the same comparer and compare every answer.
        var scenarios = new[]
        {
            (Left: Pairs(("a", 1), ("b", 2)), Right: Pairs(("a", 1), ("b", 2))),
            (Left: Pairs(("a", 1), ("b", 2)), Right: Pairs(("a", 1), ("b", 3))),
            (Left: Pairs(("a", 1), ("b", 2)), Right: Pairs(("a", 9), ("b", 8))),
            (Left: Pairs(("a", 1)), Right: Pairs(("a", 1), ("b", 2))),
            (Left: Pairs(("a", 1), ("b", 2), ("c", 3)), Right: Pairs(("a", 1))),
            (Left: Pairs(("a", 1), ("b", 2)), Right: Pairs(("b", 2), ("c", 3))),
            (Left: Pairs(("a", 1)), Right: Pairs(("b", 2))),
            (Left: Pairs(), Right: Pairs()),
        };

        for (var index = 0; index < scenarios.Length; index++)
        {
            var (left, right) = scenarios[index];

            using var dictionary = Observable.CreateDictionary(left);

            var set = SetView(dictionary);
            var reference = new HashSet<KeyValuePair<string, int>>(left, set.Comparer);

            Assert.AreEqual(reference.SetEquals(right), set.SetEquals(right), $"SetEquals, scenario {index}");
            Assert.AreEqual(reference.IsSubsetOf(right), set.IsSubsetOf(right), $"IsSubsetOf, scenario {index}");
            Assert.AreEqual(reference.IsSupersetOf(right), set.IsSupersetOf(right), $"IsSupersetOf, scenario {index}");
            Assert.AreEqual(
                reference.IsProperSubsetOf(right),
                set.IsProperSubsetOf(right),
                $"IsProperSubsetOf, scenario {index}");

            Assert.AreEqual(
                reference.IsProperSupersetOf(right),
                set.IsProperSupersetOf(right),
                $"IsProperSupersetOf, scenario {index}");

            Assert.AreEqual(reference.Overlaps(right), set.Overlaps(right), $"Overlaps, scenario {index}");
        }
    }

    [TestMethod]
    public void AProperSubsetByKeyIsAProperSubsetAndAlsoASubset()
    {
        using var smaller = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });
        using var larger = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });

        var set = SetView(smaller);

        Assert.IsTrue(set.IsSubsetOf(larger), "everything here is there");
        Assert.IsTrue(set.IsProperSubsetOf(larger), "and there is more there");
        Assert.IsFalse(set.SetEquals(larger));
        Assert.IsTrue(SetView(larger).IsProperSupersetOf(smaller), "said the other way round");
    }

    [TestMethod]
    public void DisjointDictionariesNeitherOverlapNorSubset()
    {
        using var first = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });
        using var second = Observable.CreateDictionary(new Dictionary<string, int> { ["b"] = 2 });

        var set = SetView(first);

        Assert.IsFalse(set.Overlaps(second), "nothing in common");
        Assert.IsFalse(set.IsSubsetOf(second), "not a subset of a disjoint set");
        Assert.IsFalse(set.IsSupersetOf(second), "nor a superset of one");
    }

    [TestMethod]
    public void PartialOverlapOverlapsAndIsNeitherSubsetNorSuperset()
    {
        using var first = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
        using var second = Observable.CreateDictionary(new Dictionary<string, int> { ["b"] = 2, ["c"] = 3 });

        var set = SetView(first);

        Assert.IsTrue(set.Overlaps(second), "b is in both");
        Assert.IsFalse(set.IsSubsetOf(second), "a is not");
        Assert.IsFalse(set.IsSupersetOf(second), "c is not");
        Assert.IsFalse(set.SetEquals(second), "so they are not equal");
    }

    [TestMethod]
    public void AnAddRaisesOneSetChangeAndTheDictionaryEvent()
    {
        using var dictionary = Observable.CreateDictionary<string, int>();

        var setEvents = new List<IReadOnlyList<ObservableSetChange<KeyValuePair<string, int>>>>();
        var dictionaryEvents = 0;

        dictionary.AddObservableSetChangedHandler(args => setEvents.Add([.. args.Changes]));
        dictionary.AddObservableDictionaryChangedHandler(_ => dictionaryEvents++);

        dictionary.Add("a", 1);

        Assert.AreEqual(1, dictionaryEvents, "the dictionary event still fires as before");
        Assert.AreEqual(1, setEvents.Count, "and one set event with it");
        Assert.AreEqual(1, setEvents[0].Count);
        Assert.AreEqual(ObservableSetChangeType.ItemAdded, setEvents[0][0].ChangeType);
        Assert.AreEqual(new KeyValuePair<string, int>("a", 1), setEvents[0][0].Item);
    }

    [TestMethod]
    public void ARemovalRaisesOneSetChangeCarryingTheOldPair()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });

        var setEvents = new List<IReadOnlyList<ObservableSetChange<KeyValuePair<string, int>>>>();
        var dictionaryEvents = 0;

        dictionary.AddObservableSetChangedHandler(args => setEvents.Add([.. args.Changes]));
        dictionary.AddObservableDictionaryChangedHandler(_ => dictionaryEvents++);

        Assert.IsTrue(dictionary.Remove("a"));

        Assert.AreEqual(1, dictionaryEvents);
        Assert.AreEqual(1, setEvents.Count);
        Assert.AreEqual(ObservableSetChangeType.ItemRemoved, setEvents[0][0].ChangeType);
        Assert.AreEqual(new KeyValuePair<string, int>("a", 1), setEvents[0][0].Item, "the pair that left");
    }

    [TestMethod]
    public void AReplacementIsOneEventCarryingTheOldPairLeavingAndTheNewOneArriving()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });

        var setEvents = new List<ObservableSetChangedEventArgs<KeyValuePair<string, int>>>();
        var dictionaryEvents = 0;

        dictionary.AddObservableSetChangedHandler(setEvents.Add);
        dictionary.AddObservableDictionaryChangedHandler(_ => dictionaryEvents++);

        dictionary["a"] = 2;

        Assert.AreEqual(1, dictionaryEvents, "the dictionary event still fires as before");
        Assert.AreEqual(1, setEvents.Count, "one set event, not two");
        Assert.AreSame(dictionary, setEvents[0].Set, "the set that changed is the dictionary itself");

        var changes = setEvents[0].Changes;

        Assert.AreEqual(2, changes.Count, "a replacement is two changes");
        Assert.AreEqual(ObservableSetChangeType.ItemRemoved, changes[0].ChangeType, "the old pair leaves first");
        Assert.AreEqual(new KeyValuePair<string, int>("a", 1), changes[0].Item);
        Assert.AreEqual(ObservableSetChangeType.ItemAdded, changes[1].ChangeType, "then the new one arrives");
        Assert.AreEqual(new KeyValuePair<string, int>("a", 2), changes[1].Item);
    }

    [TestMethod]
    public void AClearRaisesOneSetClearAndTheDictionaryEvent()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });

        var setEvents = new List<IReadOnlyList<ObservableSetChange<KeyValuePair<string, int>>>>();
        var dictionaryEvents = 0;

        dictionary.AddObservableSetChangedHandler(args => setEvents.Add([.. args.Changes]));
        dictionary.AddObservableDictionaryChangedHandler(_ => dictionaryEvents++);

        dictionary.Clear();

        Assert.AreEqual(1, dictionaryEvents, "the dictionary event still fires as before");
        Assert.AreEqual(1, setEvents.Count, "two entries, one set event");
        Assert.AreEqual(1, setEvents[0].Count, "carrying one record");
        Assert.AreEqual(ObservableSetChangeType.SetCleared, setEvents[0][0].ChangeType);
    }

    [TestMethod]
    public void AnUntrackedDictionaryEventIsAnUntrackedSetEvent()
    {
        using var dictionary = Observable.CreateDictionary<int, int>();
        dictionary.MaxCollectionBatchSize = 1;

        var setEvents = new List<IReadOnlyList<ObservableSetChange<KeyValuePair<int, int>>>>();
        var dictionaryEvents = new List<IReadOnlyList<ObservableDictionaryChange<int, int>>>();

        dictionary.AddObservableSetChangedHandler(args => setEvents.Add([.. args.Changes]));
        dictionary.AddObservableDictionaryChangedHandler(args => dictionaryEvents.Add([.. args.Changes]));

        dictionary.BeginChangeBatch();
        dictionary[0] = 0;
        dictionary[1] = 1;
        dictionary.EndChangeBatch();

        Assert.AreEqual(1, dictionaryEvents.Count, "the dictionary event still fires as before");
        Assert.AreEqual(ObservableDictionaryChangeType.UntrackedChanges, dictionaryEvents[0][0].ChangeType);

        Assert.AreEqual(1, setEvents.Count, "one set event for it");
        Assert.AreEqual(1, setEvents[0].Count, "admitting the same thing rather than describing it");
        Assert.AreEqual(ObservableSetChangeType.UntrackedChanges, setEvents[0][0].ChangeType);
        Assert.AreEqual(2, dictionary.Count, "the dictionary itself is complete");
    }

    [TestMethod]
    public void TheMappedViewRaisesTheSameSetChangesAsTheDictionaryItMaps()
    {
        using var source = Observable.CreateDictionary(new Dictionary<string, string> { ["a"] = "x" });
        using var view = new ObservableMappedReadOnlyDictionaryView<string, string, string>(
            source,
            text => text.ToUpperInvariant());

        var setEvents = new List<ObservableSetChangedEventArgs<KeyValuePair<string, string>>>();

        view.AddObservableSetChangedHandler(setEvents.Add);

        source["b"] = "y";
        source["a"] = "z";
        source.Remove("b");
        source.Clear();

        Assert.AreEqual(4, setEvents.Count, "one set event per dictionary event");

        Assert.AreSame(view, setEvents[0].Set, "the set that changed is the view itself");
        Assert.AreEqual(ObservableSetChangeType.ItemAdded, setEvents[0].Changes[0].ChangeType);
        Assert.AreEqual(new KeyValuePair<string, string>("b", "Y"), setEvents[0].Changes[0].Item, "the mapped value");

        Assert.AreEqual(2, setEvents[1].Changes.Count, "a replacement is two changes, as in the dictionary");
        Assert.AreEqual(ObservableSetChangeType.ItemRemoved, setEvents[1].Changes[0].ChangeType);
        Assert.AreEqual(new KeyValuePair<string, string>("a", "X"), setEvents[1].Changes[0].Item);
        Assert.AreEqual(ObservableSetChangeType.ItemAdded, setEvents[1].Changes[1].ChangeType);
        Assert.AreEqual(new KeyValuePair<string, string>("a", "Z"), setEvents[1].Changes[1].Item);

        Assert.AreEqual(ObservableSetChangeType.ItemRemoved, setEvents[2].Changes[0].ChangeType);
        Assert.AreEqual(new KeyValuePair<string, string>("b", "Y"), setEvents[2].Changes[0].Item, "the mapped pair that left");

        Assert.AreEqual(ObservableSetChangeType.SetCleared, setEvents[3].Changes[0].ChangeType);
    }

    [TestMethod]
    public void ASetHandlerIsNotCalledAfterTheDictionaryIsDisposed()
    {
        var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });
        var events = 0;

        dictionary.AddObservableSetChangedHandler(_ => events++);

        dictionary.Dispose();

        Assert.AreEqual(1, events, "the one final empty event, and nothing after it");
        Assert.IsTrue(dictionary.IsDisposed);
        Assert.ThrowsExactly<ObjectDisposedException>(
            () => dictionary.AddObservableSetChangedHandler(_ => { }),
            "and nothing new can be registered, exactly as for the dictionary handlers");
    }

    [TestMethod]
    public void DisposingASetHandlerTokenStopsTheHandler()
    {
        using var dictionary = Observable.CreateDictionary<string, int>();
        var events = 0;

        var token = dictionary.AddObservableSetChangedHandler(_ => events++);

        dictionary["a"] = 1;
        token.Dispose();
        dictionary["b"] = 2;

        Assert.AreEqual(1, events, "only the change that happened while it was registered");
    }

    [TestMethod]
    public void TheWritableInterfaceResolvesContainsAndAnswersWithPairIdentity()
    {
        // The shape that was ambiguous while the set base arrived un-redeclared: the two inherited
        // Contains(KeyValuePair) declarations collided for a caller of this interface, and the `new`
        // declaration on IObservableDictionary is what resolves them to the one implementation.
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });

        Assert.IsTrue(dictionary.Contains(new KeyValuePair<string, int>("a", 1)), "the key is there with that value");
        Assert.IsFalse(dictionary.Contains(new KeyValuePair<string, int>("a", 2)), "a stale pair is not");
        Assert.IsFalse(dictionary.Contains(new KeyValuePair<string, int>("b", 1)), "nor an absent key");
    }

    [TestMethod]
    public void TheReadOnlyInterfaceResolvesContainsAndAnswersWithPairIdentity()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });

        IReadOnlyObservableDictionary<string, int> readOnly = dictionary;

        Assert.IsTrue(readOnly.Contains(new KeyValuePair<string, int>("a", 1)));
        Assert.IsFalse(readOnly.Contains(new KeyValuePair<string, int>("a", 2)), "a stale pair is not in the set");
    }

    [TestMethod]
    public void TheCollectionInterfaceStillResolvesContains()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });

        ICollection<KeyValuePair<string, int>> collection = dictionary;

        Assert.IsTrue(collection.Contains(new KeyValuePair<string, int>("a", 1)));
        Assert.IsFalse(collection.Contains(new KeyValuePair<string, int>("a", 2)), "one member, one answer");
    }

    [TestMethod]
    public void TheWritableInterfaceIsDeliberatelyNotAnObservableSet()
    {
        using var dictionary = Observable.CreateDictionary<string, int>();

        Assert.IsTrue(
            dictionary is IReadOnlyObservableSet<KeyValuePair<string, int>>,
            "the read-only dictionary is a set of its pairs");
        Assert.IsFalse(
            dictionary is IObservableSet<KeyValuePair<string, int>>,
            "the writable side was left alone: IObservableDictionary does not extend IObservableSet");
    }

    private static IReadOnlyObservableSet<KeyValuePair<string, int>> SetView(
        IReadOnlyObservableDictionary<string, int> dictionary) => dictionary;

    private static KeyValuePair<string, int>[] Pairs(params (string Key, int Value)[] items) =>
        [.. items.Select(item => new KeyValuePair<string, int>(item.Key, item.Value))];
}
