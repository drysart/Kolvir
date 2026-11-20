namespace Kolvir.Observables.Tests;

/// <summary>
/// What a read of an observable dictionary depends on, and what wakes it: the structure, one key, and the
/// contents, each raised by exactly the changes that can change its answer
/// (dev-docs/OBSERVABLE-COLLECTIONS.md section 7.2.4).
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class ObservableDictionaryDependencyTests
{
    [TestMethod]
    public void StructureKeyAndContentsAreThreeQuestions()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });

        using var run = Observable.CollectDependencies(
            () =>
            {
                _ = dictionary.Count;
                _ = dictionary.TryGetValue("a", out _);
                _ = dictionary.Values.Count;
            },
            _ => { });

        Assert.AreEqual(3, run.Dependencies.Count, "the size, one key, and the values");
    }

    [TestMethod]
    public void AKeyReaderIsWokenByItsOwnKeyAndNotAnother()
    {
        // The read a rendered row makes - "what is beside my key" - and the reason an address is worth an
        // entry: a change to one key must not re-render the other rows.
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });
        var wakes = new List<string>();

        using var first = Observable.CollectDependencies(() => { _ = dictionary.TryGetValue("a", out _); }, _ => wakes.Add("a"));
        using var second = Observable.CollectDependencies(() => { _ = dictionary.TryGetValue("b", out _); }, _ => wakes.Add("b"));

        dictionary["b"] = 2;

        CollectionAssert.AreEqual(new[] { "b" }, wakes, "only the key that changed");
    }

    [TestMethod]
    public void AValueWriteWakesTheKeyAndTheContentsAndNotTheStructure()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });
        var wakes = new List<string>();

        using var count = Observable.CollectDependencies(() => { _ = dictionary.Count; }, _ => wakes.Add("count"));
        using var keys = Observable.CollectDependencies(() => { _ = dictionary.Keys.Count; }, _ => wakes.Add("keys"));
        using var key = Observable.CollectDependencies(() => { _ = dictionary["a"]; }, _ => wakes.Add("a"));

        using var values = Observable.CollectDependencies(
            () =>
            {
                foreach (var pair in dictionary)
                {
                    _ = pair;
                }
            },
            _ => wakes.Add("contents"));

        dictionary["a"] = 2;

        CollectionAssert.AreEquivalent(
            new[] { "a", "contents" },
            wakes,
            "the value changed: that key and the contents, not the size and not the keys");
    }

    [TestMethod]
    public void AddingAKeyWakesTheStructureTheContentsAndThatKey()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });
        var wakes = new List<string>();

        using var count = Observable.CollectDependencies(() => { _ = dictionary.Count; }, _ => wakes.Add("count"));
        using var key = Observable.CollectDependencies(() => { _ = dictionary.TryGetValue("a", out _); }, _ => wakes.Add("a"));
        using var values = Observable.CollectDependencies(() => { _ = dictionary.Values.Count; }, _ => wakes.Add("values"));

        dictionary["b"] = 2;

        CollectionAssert.AreEquivalent(
            new[] { "count", "values" },
            wakes,
            "a key arrived: the structure and the contents, and a was not asked about again");
    }

    [TestMethod]
    public void AnAbsentKeyReadIsWokenWhenTheKeyAppears()
    {
        // TryGetValue publishes before it answers, so a reader that asked about a key that is not there is
        // woken by the key arriving - the only way an expression that is currently failing can recover.
        using var dictionary = Observable.CreateDictionary<string, int>();
        var wakes = 0;

        using var run = Observable.CollectDependencies(
            () =>
            {
                _ = dictionary.TryGetValue("a", out var value);
                _ = value;
            },
            _ => wakes++);

        Assert.IsFalse(dictionary.ContainsKey("a"));

        dictionary["a"] = 1;

        Assert.AreEqual(1, wakes, "the key appeared");
    }

    [TestMethod]
    public void ContainsKeyIsWokenByAValueChangeToo()
    {
        // Deliberate over-raise: presence and value are one entry to everyone who holds it, so a value
        // change wakes a ContainsKey reader as well. The cost is a re-run, never a wrong answer.
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });
        var wakes = 0;

        using var run = Observable.CollectDependencies(() => { _ = dictionary.ContainsKey("a"); }, _ => wakes++);

        dictionary["a"] = 2;

        Assert.AreEqual(1, wakes, "the entry is one thing, and it moved");
    }

    [TestMethod]
    public void TheIndexerReadOfAnAbsentKeyPublishesNothing()
    {
        // The one place the dictionary departs from "publish before the check": a key that is gone is the
        // wrong shape of address to remember, and the caller has TryGetValue and ContainsKey for that.
        using var dictionary = Observable.CreateDictionary<string, int>();

        using var run = Observable.CollectDependencies(() => { _ = dictionary["a"]; }, _ => { });

        Assert.IsTrue(run.IsFaulted, "the read threw");
        Assert.AreEqual(0, run.Dependencies.Count, "and remembers nothing about a key that is not there");
    }

    [TestMethod]
    public void AClearWakesTheStructureAndEveryWatchedKey()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
        var wakes = new List<string>();

        using var count = Observable.CollectDependencies(() => { _ = dictionary.Count; }, _ => wakes.Add("count"));
        using var first = Observable.CollectDependencies(() => { _ = dictionary.TryGetValue("a", out _); }, _ => wakes.Add("a"));
        using var second = Observable.CollectDependencies(() => { _ = dictionary.TryGetValue("b", out _); }, _ => wakes.Add("b"));
        using var gone = Observable.CollectDependencies(() => { _ = dictionary.TryGetValue("z", out _); }, _ => wakes.Add("z"));

        dictionary.Clear();

        CollectionAssert.AreEquivalent(
            new[] { "count", "a", "b", "z" },
            wakes,
            "a clear invalidates the structure and every key anything asked about");
    }

    [TestMethod]
    public void AnUntrackedEventWakesEveryReader()
    {
        using var dictionary = Observable.CreateDictionary<string, int>(new Dictionary<string, int> { ["a"] = 1 });
        var wakes = new List<string>();

        using var count = Observable.CollectDependencies(() => { _ = dictionary.Count; }, _ => wakes.Add("count"));
        using var key = Observable.CollectDependencies(() => { _ = dictionary.TryGetValue("a", out _); }, _ => wakes.Add("a"));
        using var absent = Observable.CollectDependencies(() => { _ = dictionary.TryGetValue("z", out _); }, _ => wakes.Add("z"));

        dictionary.BeginChangeBatch();

        for (var index = 0; index <= Observable.Default.MaxCollectionBatchSize; index++)
        {
            dictionary[$"key {index}"] = index;
        }

        dictionary.EndChangeBatch();

        CollectionAssert.AreEquivalent(
            new[] { "count", "a", "z" },
            wakes,
            "a reader is woken whether or not its own answer actually changed");
    }

    [TestMethod]
    public void AReadOutsideARunRegistersNothing()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });

        _ = dictionary.Count;
        _ = dictionary.TryGetValue("a", out _);
        _ = dictionary.ContainsKey("a");
        _ = dictionary.Values.Count;

        using var run = Observable.CollectDependencies(() => { _ = dictionary.TryGetValue("a", out _); }, _ => { });

        Assert.AreEqual(1, run.Dependencies.Count, "and the first read inside a run is still one dependency");
    }

    [TestMethod]
    public void TheComparerDecidesWhichEntryAKeyIs()
    {
        using var dictionary = Observable.CreateDictionary(StringComparer.OrdinalIgnoreCase, new Dictionary<string, int> { ["abc"] = 1 });
        var wakes = 0;

        using var run = Observable.CollectDependencies(() => { _ = dictionary.TryGetValue("ABC", out _); }, _ => wakes++);

        dictionary["abc"] = 2;

        Assert.AreEqual(1, wakes, "the same key, whichever spelling the change used");
    }

    [TestMethod]
    public void ASourceWatchedAgainAfterItWasReleasedIsWokenAgain()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });

        var run = Observable.CollectDependencies(() => { _ = dictionary.TryGetValue("a", out _); }, _ => { });
        var source = run.Dependencies[0];

        run.Dispose();

        var wakes = 0;
        using var again = source.AddObservableEventRaisedHandler(_ => wakes++);

        dictionary["a"] = 2;

        Assert.AreEqual(1, wakes, "the dictionary takes the entry back when something watches it again");
    }
}
