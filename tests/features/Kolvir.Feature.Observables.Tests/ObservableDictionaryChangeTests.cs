namespace Kolvir.Feature.Observables.Tests;

/// <summary>
/// A dictionary's records: what each mutation raises, what each kind carries, and the replay that proves
/// a record stream is enough to keep a copy
/// (dev-docs/OBSERVABLE-COLLECTIONS.md sections 7.1, 7.3 and 7.4).
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class ObservableDictionaryChangeTests
{
    [TestMethod]
    public void AddingAKeyRaisesOneRecordCarryingTheKeyAndItsValue()
    {
        using var dictionary = Observable.CreateDictionary<string, int>();
        var events = new List<ObservableDictionaryChange<string, int>>();

        dictionary.AddObservableDictionaryChangedHandler(args => events.AddRange(args.Changes));

        dictionary.Add("a", 1);

        Assert.AreEqual(1, events.Count, "one record");
        Assert.AreEqual(ObservableDictionaryChangeType.ItemAdded, events[0].ChangeType);
        Assert.AreEqual("a", events[0].Key);
        Assert.AreEqual(1, events[0].NewValue, "the value it arrived with");
        Assert.AreEqual(0, events[0].OldValue, "and no old value, which is a value type's default");
    }

    [TestMethod]
    public void AddingAKeyThatIsThereThrowsAndRaisesNothing()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });
        var events = 0;

        dictionary.AddObservableDictionaryChangedHandler(args => events += args.Changes.Count);

        Assert.ThrowsExactly<ArgumentException>(() => dictionary.Add("a", 2));

        Assert.AreEqual(0, events, "the add threw before it could change anything");
        Assert.AreEqual(1, dictionary["a"], "and the value it had is untouched");
    }

    [TestMethod]
    public void TryAddRaisesOnlyWhenItAdds()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });
        var events = new List<ObservableDictionaryChange<string, int>>();

        dictionary.AddObservableDictionaryChangedHandler(args => events.AddRange(args.Changes));

        Assert.IsFalse(dictionary.TryAdd("a", 2), "the key is there, so nothing happened and nothing throws");
        Assert.AreEqual(0, events.Count);

        Assert.IsTrue(dictionary.TryAdd("b", 2));

        Assert.AreEqual(1, events.Count, "the add that worked raised one record");
        Assert.AreEqual("b", events[0].Key);
        Assert.AreEqual(2, events[0].NewValue);
        Assert.AreEqual(1, dictionary["a"], "and the key that was already there kept its value");
    }

    [TestMethod]
    public void WritingAnAbsentKeyRaisesItemAdded()
    {
        using var dictionary = Observable.CreateDictionary<string, int>();
        var events = new List<ObservableDictionaryChange<string, int>>();

        dictionary.AddObservableDictionaryChangedHandler(args => events.AddRange(args.Changes));

        dictionary["a"] = 1;

        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(ObservableDictionaryChangeType.ItemAdded, events[0].ChangeType, "the key arrived");
        Assert.AreEqual("a", events[0].Key);
        Assert.AreEqual(1, events[0].NewValue);
    }

    [TestMethod]
    public void WritingAPresentKeyRaisesItemChangedWithBothValues()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });
        var events = new List<ObservableDictionaryChange<string, int>>();

        dictionary.AddObservableDictionaryChangedHandler(args => events.AddRange(args.Changes));

        dictionary["a"] = 2;

        Assert.AreEqual(1, events.Count, "one record");
        Assert.AreEqual(ObservableDictionaryChangeType.ItemChanged, events[0].ChangeType);
        Assert.AreEqual("a", events[0].Key);
        Assert.AreEqual(1, events[0].OldValue, "what the value was");
        Assert.AreEqual(2, events[0].NewValue, "and what it is");
    }

    [TestMethod]
    public void WritingAPresentKeyWithAnEqualValueRaisesNothing()
    {
        // The rule a value's write and a list's item write already follow: an event means something a
        // listener cares about is different.
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });
        var events = 0;

        dictionary.AddObservableDictionaryChangedHandler(args => events += args.Changes.Count);

        dictionary["a"] = 1;

        Assert.AreEqual(0, events);
    }

    [TestMethod]
    public void RemovingAKeyReportsTheValueThatWasThere()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });
        var events = new List<ObservableDictionaryChange<string, int>>();

        dictionary.AddObservableDictionaryChangedHandler(args => events.AddRange(args.Changes));

        Assert.IsTrue(dictionary.Remove("a"));

        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(ObservableDictionaryChangeType.ItemRemoved, events[0].ChangeType);
        Assert.AreEqual("a", events[0].Key);
        Assert.AreEqual(1, events[0].OldValue, "the value the key had, which is what a copy held");
    }

    [TestMethod]
    public void RemovingAKeyThatIsNotThereRaisesNothing()
    {
        using var dictionary = Observable.CreateDictionary<string, int>();
        var events = 0;

        dictionary.AddObservableDictionaryChangedHandler(args => events += args.Changes.Count);

        Assert.IsFalse(dictionary.Remove("a"));
        Assert.AreEqual(0, events);
    }

    [TestMethod]
    public void RemovingAPairIsConditionalOnTheValue()
    {
        // ICollection<KeyValuePair>.Remove says it removes only when the key and the value both match, so
        // a mismatch changes nothing and raises nothing (section 7.2.3).
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });
        var events = 0;

        dictionary.AddObservableDictionaryChangedHandler(args => events += args.Changes.Count);

        Assert.IsFalse(dictionary.Remove(new KeyValuePair<string, int>("a", 2)), "the value differs");
        Assert.AreEqual(0, events);
        Assert.AreEqual(1, dictionary["a"], "and the entry is untouched");

        Assert.IsTrue(dictionary.Remove(new KeyValuePair<string, int>("a", 1)), "the pair matches");

        Assert.AreEqual(1, events, "one record");
        Assert.AreEqual(0, dictionary.Count);
    }

    [TestMethod]
    public void ClearRaisesOneRecordHoweverManyKeysThereWere()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
        var events = new List<ObservableDictionaryChange<string, int>>();

        dictionary.AddObservableDictionaryChangedHandler(args => events.AddRange(args.Changes));

        dictionary.Clear();

        Assert.AreEqual(1, events.Count, "one record");
        Assert.AreEqual(ObservableDictionaryChangeType.DictionaryCleared, events[0].ChangeType);
        Assert.IsNull(events[0].Key, "and it carries nothing");
        Assert.AreEqual(0, dictionary.Count);
    }

    [TestMethod]
    public void ClearingAnEmptyDictionaryRaisesNothing()
    {
        using var dictionary = Observable.CreateDictionary<string, int>();
        var events = 0;

        dictionary.AddObservableDictionaryChangedHandler(args => events += args.Changes.Count);

        dictionary.Clear();

        Assert.AreEqual(0, events);
    }

    [TestMethod]
    public void EachKindFillsTheFieldsItMeans()
    {
        var added = ObservableDictionaryChange<string, int>.Add("a", 1);
        Assert.AreEqual(ObservableDictionaryChangeType.ItemAdded, added.ChangeType);
        Assert.AreEqual("a", added.Key);
        Assert.AreEqual(1, added.NewValue);

        var removed = ObservableDictionaryChange<string, int>.Remove("b", 2);
        Assert.AreEqual(ObservableDictionaryChangeType.ItemRemoved, removed.ChangeType);
        Assert.AreEqual(2, removed.OldValue);

        var changed = ObservableDictionaryChange<string, int>.Change("c", 3, 4);
        Assert.AreEqual(ObservableDictionaryChangeType.ItemChanged, changed.ChangeType);
        Assert.AreEqual(3, changed.OldValue, "both sides, because both are known");
        Assert.AreEqual(4, changed.NewValue);

        var cleared = ObservableDictionaryChange<string, int>.Clear();
        Assert.IsNull(cleared.Key, "a clear names no key");

        var untracked = ObservableDictionaryChange<string, int>.Untracked();
        Assert.AreEqual(ObservableDictionaryChangeType.UntrackedChanges, untracked.ChangeType);
        Assert.IsNull(untracked.Key);
    }

    [TestMethod]
    public void ARecordSaysWhatHappenedInWords()
    {
        StringAssert.Contains(ObservableDictionaryChange<string, int>.Add("a", 1).ToString(), "added a with 1");
        StringAssert.Contains(ObservableDictionaryChange<string, int>.Remove("a", 1).ToString(), "removed a");
        StringAssert.Contains(ObservableDictionaryChange<string, int>.Change("a", 1, 2).ToString(), "changed a from 1 to 2");
        StringAssert.Contains(ObservableDictionaryChange<string, int>.Clear().ToString(), "cleared");
        StringAssert.Contains(ObservableDictionaryChange<string, int>.Untracked().ToString(), "does not describe");
    }

    [TestMethod]
    public void AReplayOfTheRecordsReproducesTheDictionary()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });
        var copy = new Dictionary<string, int>(StringComparer.Ordinal);

        dictionary.AddObservableDictionaryChangedHandler(args =>
        {
            foreach (var change in args.Changes)
            {
                switch (change.ChangeType)
                {
                    case ObservableDictionaryChangeType.ItemAdded:
                    case ObservableDictionaryChangeType.ItemChanged:
                        copy[change.Key!] = change.NewValue!;
                        break;

                    case ObservableDictionaryChangeType.ItemRemoved:
                        copy.Remove(change.Key!);
                        break;

                    case ObservableDictionaryChangeType.DictionaryCleared:
                        copy.Clear();
                        break;

                    default:
                        // UntrackedChanges: the batch was too big to describe, so rebuild from the source.
                        copy.Clear();

                        foreach (var pair in dictionary)
                        {
                            copy[pair.Key] = pair.Value;
                        }

                        break;
                }
            }
        });

        dictionary["b"] = 2;
        dictionary["a"] = 10;
        dictionary.Remove("b");
        dictionary.TryAdd("c", 3);

        dictionary.BeginChangeBatch();
        dictionary["d"] = 4;
        dictionary.Remove("c");
        dictionary.EndChangeBatch();

        Assert.AreEqual(dictionary.Count, copy.Count, "the same number of keys");

        foreach (var pair in dictionary)
        {
            Assert.IsTrue(copy.TryGetValue(pair.Key, out var value), $"{pair.Key} is missing from the copy");
            Assert.AreEqual(pair.Value, value, $"{pair.Key} has the wrong value in the copy");
        }
    }
}
