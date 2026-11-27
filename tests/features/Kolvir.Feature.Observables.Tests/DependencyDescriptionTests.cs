namespace Kolvir.Feature.Observables.Tests;

/// <summary>
/// What a dependency says about itself: the same words it always said, and the words for an address are
/// only built when something asks - a run holds one entry per address it read, so text kept per entry is
/// the most expensive thing in the graph.
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class DependencyDescriptionTests
{
    [TestMethod]
    public void TheShapeSourcesNameTheirCollection()
    {
        using var list = Observable.CreateList(1);

        using var count = Observable.CollectDependencies(() => { _ = list.Count; }, _ => { });
        using var contents = Observable.CollectDependencies(
            () =>
            {
                foreach (var item in list)
                {
                    _ = item;
                }
            },
            _ => { });

        Assert.AreEqual(
            "the count of an observable list",
            count.Dependencies[0].ToString(),
            "the size says which collection it is the size of");
        Assert.AreEqual(
            "the contents of an observable list",
            contents.Dependencies[0].ToString(),
            "and so do the contents");
    }

    [TestMethod]
    public void AnAddressIsNamedByItsCollection()
    {
        // One entry per address, and the address is what the reader asked about: the three collections
        // word it differently, which is what the kind on the entry is for.
        using var list = Observable.CreateList("a");
        using var set = Observable.CreateHashSet("a");
        using var dictionary = Observable.CreateDictionary<string, int>();
        dictionary["a"] = 1;

        using var index = Observable.CollectDependencies(() => { _ = list[0]; }, _ => { });
        using var item = Observable.CollectDependencies(() => { _ = set.Contains("a"); }, _ => { });
        using var key = Observable.CollectDependencies(() => { _ = dictionary.TryGetValue("a", out _); }, _ => { });

        Assert.AreEqual("index 0 of an observable list", index.Dependencies[0].ToString());
        Assert.AreEqual("item a of an observable set", item.Dependencies[0].ToString());
        Assert.AreEqual("key a of an observable dictionary", key.Dependencies[0].ToString());
    }

    [TestMethod]
    public void ReadingAnAddressDoesNotBuildItsDescription()
    {
        // The thing this exists for: an entry keeps the key, not the text. A key whose own ToString is
        // expensive - or throws - must not be rendered by a read, because a read is what happens ten
        // thousand times when a screen is filled, and a refusal is what happens once.
        var item = new TracedItem();

        using var set = Observable.CreateHashSet(item);

        using var run = Observable.CollectDependencies(() => { _ = set.Contains(item); }, _ => { });

        Assert.AreEqual(0, item.Renders, "reading an address publishes the address, not its name");
        Assert.AreEqual(1, run.Dependencies.Count, "and it is one dependency");

        var description = run.Dependencies[0].ToString();

        Assert.AreEqual(1, item.Renders, "the name is built by ToString and by nothing else");
        Assert.AreEqual("item traced#1 of an observable set", description, "and it is the name it always was");
    }

    private sealed class TracedItem
    {
        public int Renders { get; private set; }

        public override string ToString()
        {
            Renders++;
            return $"traced#{Renders}";
        }
    }
}
