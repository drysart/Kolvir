namespace Kolvir.Feature.Observables.Tests;

/// <summary>
/// A dictionary's delivery: batches, the cap, the queue that keeps a handler's own changes in order, and
/// disposal (dev-docs/OBSERVABLE-COLLECTIONS.md sections 3.3, 3.7, 3.9 and 3.11).
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class ObservableDictionaryDeliveryTests
{
    [TestMethod]
    public void ABatchIsOneEventWithAllTheRecordsInOrder()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });
        var events = new List<IReadOnlyList<ObservableDictionaryChange<string, int>>>();

        dictionary.AddObservableDictionaryChangedHandler(args => events.Add([.. args.Changes]));

        dictionary.BeginChangeBatch();
        dictionary["b"] = 2;
        dictionary["a"] = 10;
        dictionary.Remove("b");
        dictionary.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "three mutations, one event");
        Assert.AreEqual(3, events[0].Count, "carrying three records");
        Assert.AreEqual(ObservableDictionaryChangeType.ItemAdded, events[0][0].ChangeType, "in the order they happened");
        Assert.AreEqual(ObservableDictionaryChangeType.ItemChanged, events[0][1].ChangeType);
        Assert.AreEqual(ObservableDictionaryChangeType.ItemRemoved, events[0][2].ChangeType);
    }

    [TestMethod]
    public void ABatchUpToTheLimitKeepsEveryRecord()
    {
        using var dictionary = Observable.CreateDictionary<int, int>();
        var events = new List<int>();

        dictionary.AddObservableDictionaryChangedHandler(args => events.Add(args.Changes.Count));

        dictionary.BeginChangeBatch();

        for (var key = 0; key < Observable.Default.MaxCollectionBatchSize; key++)
        {
            dictionary[key] = key;
        }

        dictionary.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "fifty changes, one event");
        Assert.AreEqual(Observable.Default.MaxCollectionBatchSize, events[0], "carrying all fifty records");
        Assert.AreEqual(Observable.Default.MaxCollectionBatchSize, dictionary.Count);
    }

    [TestMethod]
    public void ABatchPastTheLimitBecomesOneUntrackedRecord()
    {
        using var dictionary = Observable.CreateDictionary<int, int>();
        var events = new List<IReadOnlyList<ObservableDictionaryChange<int, int>>>();

        dictionary.AddObservableDictionaryChangedHandler(args => events.Add([.. args.Changes]));

        dictionary.BeginChangeBatch();

        for (var key = 0; key <= Observable.Default.MaxCollectionBatchSize; key++)
        {
            dictionary[key] = key;
        }

        dictionary.EndChangeBatch();

        Assert.AreEqual(1, events.Count, "still one event");
        Assert.AreEqual(1, events[0].Count, "carrying one record rather than fifty-one");
        Assert.AreEqual(ObservableDictionaryChangeType.UntrackedChanges, events[0][0].ChangeType);
        Assert.AreEqual(Observable.Default.MaxCollectionBatchSize + 1, dictionary.Count, "the dictionary itself is complete");
    }

    [TestMethod]
    public void AChangeFromInsideAHandlerIsDeliveredAfterTheEvent()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });
        var second = new List<string>();

        dictionary.AddObservableDictionaryChangedHandler(args =>
        {
            if (args.Changes.Count > 0 && args.Changes[0].Key == "b")
            {
                dictionary["c"] = 3;
            }
        });

        dictionary.AddObservableDictionaryChangedHandler(args =>
        {
            foreach (var change in args.Changes)
            {
                second.Add($"{change.ChangeType}:{change.Key}");
            }
        });

        dictionary["b"] = 2;

        CollectionAssert.AreEqual(
            new[] { "ItemAdded:b", "ItemAdded:c" },
            second,
            "the second handler saw the first change before the one it caused");
    }

    [TestMethod]
    public void ABatchLeftOpenIsDroppedByDisposal()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });
        var events = new List<int>();

        dictionary.AddObservableDictionaryChangedHandler(args => events.Add(args.Changes.Count));

        dictionary.BeginChangeBatch();
        dictionary["b"] = 2;
        dictionary.Clear();

        dictionary.Dispose();

        Assert.AreEqual(1, events.Count, "only the final event");
        Assert.AreEqual(0, events[0], "the records a dead dictionary had not delivered are dropped");
    }

    [TestMethod]
    public void DisposalSaysSoOnceAndLeavesNothingToRegisterOn()
    {
        var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });
        var events = new List<ObservableDictionaryChangedEventArgs<string, int>>();
        Action<ObservableDictionaryChangedEventArgs<string, int>> handler = events.Add;

        using var token = dictionary.AddObservableDictionaryChangedHandler(handler);

        dictionary.Dispose();

        Assert.AreEqual(1, events.Count, "one last event");
        Assert.AreEqual(0, events[0].Changes.Count, "with no changes");
        Assert.IsTrue(events[0].Dictionary.IsDisposed, "and the dictionary already says it is dead");

        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = dictionary.Count);
        Assert.ThrowsExactly<ObjectDisposedException>(() => dictionary["a"] = 2);
        Assert.ThrowsExactly<ObjectDisposedException>(() => dictionary.AddObservableDictionaryChangedHandler(_ => { }));

        dictionary.RemoveObservableDictionaryChangedHandler(handler);
        token.Dispose();
        dictionary.Dispose();
    }

    [TestMethod]
    public void AnUnmatchedEndChangeBatchThrowsAndLeavesTheDictionaryUsable()
    {
        using var dictionary = Observable.CreateDictionary(new Dictionary<string, int> { ["a"] = 1 });

        Assert.ThrowsExactly<InvalidOperationException>(() => dictionary.EndChangeBatch());

        dictionary["b"] = 2;

        Assert.AreEqual(2, dictionary.Count, "the failed end did not leave a batch open");
    }

    [TestMethod]
    public void TheDictionaryKeepsItsOwnCap()
    {
        using var dictionary = Observable.CreateDictionary<int, int>();
        var events = new List<int>();

        dictionary.MaxCollectionBatchSize = 3;
        dictionary.AddObservableDictionaryChangedHandler(args => events.Add(args.Changes.Count));

        dictionary.BeginChangeBatch();

        for (var key = 0; key < 4; key++)
        {
            dictionary[key] = key;
        }

        dictionary.EndChangeBatch();

        Assert.AreEqual(1, events[0], "four changes over a cap of three gave up on detail");
    }
}
