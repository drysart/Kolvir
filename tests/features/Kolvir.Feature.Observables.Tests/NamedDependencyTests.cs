namespace Kolvir.Observables.Tests;

/// <summary>
/// Named dependencies: one handle per key, a raise that needs no handle, and eviction that waits for
/// whatever is in progress to finish.
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class NamedDependencyTests
{
    [TestMethod]
    public void OneKeyReturnsOneHandle()
    {
        var key = "row-7";

        var first = Observable.GetNamed(key);
        var second = Observable.GetNamed(key);

        Assert.AreSame(first, second, "the entry owns its handle and hands out that instance");
        Assert.AreNotSame(first, Observable.GetNamed("row-8"), "and a different key is a different entry");
    }

    [TestMethod]
    public void KeysCompareByTheirOwnEquality()
    {
        // Tuple keys are the reason the key is an object and not a string: the layer compares them the way
        // the type does, so a multi-part name needs no formatting and no separator escaping.
        var first = Observable.GetNamed((7, "text"));
        var second = Observable.GetNamed((7, "text"));

        Assert.AreSame(first, second, "two structurally equal keys are one dependency");
        Assert.AreNotSame(first, Observable.GetNamed((7, "done")), "and one part differing is another");
    }

    [TestMethod]
    public void AnEntryLivesWhileItIsWatchedAndIsEvictedWhenTheLastHandlerGoes()
    {
        var key = "watched";
        var handle = Observable.GetNamed(key);

        var first = handle.AddObservableEventRaisedHandler(_ => { });
        var second = handle.AddObservableEventRaisedHandler(_ => { });

        Assert.IsFalse(handle.IsDisposed, "a watched entry is alive");

        first.Dispose();

        Assert.IsFalse(handle.IsDisposed, "and stays alive while anything is registered");

        second.Dispose();

        Assert.IsTrue(handle.IsDisposed, "removing the last handler lets the key go");
        Assert.AreNotSame(handle, Observable.GetNamed(key), "a later ask gets a fresh handle");
        Assert.IsFalse(Observable.GetNamed(key).IsDisposed, "which is alive");
    }

    [TestMethod]
    public void AStaleHandleIsDeadForEveryUse()
    {
        var handle = Observable.GetNamed("stale");
        var token = handle.AddObservableEventRaisedHandler(_ => { });
        token.Dispose();

        Assert.IsTrue(handle.IsDisposed, "the table let the key go");
        Assert.ThrowsExactly<ObjectDisposedException>(() => handle.AddObservableEventRaisedHandler(_ => { }));
        Assert.ThrowsExactly<ObjectDisposedException>(() => handle.DependUpon());
        Assert.ThrowsExactly<ObjectDisposedException>(() => handle.RaiseEvent());

        handle.RemoveObservableEventRaisedHandler(_ => { });
        token.Dispose();

        Assert.IsTrue(handle.IsDisposed, "while unregistering and disposing again stay safe");
    }

    [TestMethod]
    public void RaisingReachesAWatchedKeyAndIsHarmlessForAnUnwatchedOne()
    {
        var raised = 0;
        var watched = Observable.GetNamed("watched");
        watched.AddObservableEventRaisedHandler(_ => raised++);

        Observable.RaiseNamedEvent("watched");

        Assert.AreEqual(1, raised, "a watched key notifies");

        Observable.RaiseNamedEvent("nobody-asked");

        Assert.AreEqual(1, raised, "and an unwatched key notifies nothing and throws nothing");
        Assert.IsFalse(Observable.GetNamed("nobody-asked").IsDisposed, "the raise created no dead entry behind it");
    }

    [TestMethod]
    public void AnExpressionReadingTheSameKeyTwiceHooksItOnce()
    {
        // Two registrations would mean two calls per raise, and one unhook per release. The raise is the
        // measurement: one raise, one re-run.
        var runs = 0;
        var expression = Observable.CreateExpression(() =>
        {
            runs++;

            var source = Observable.GetNamed("counted");
            source.DependUpon();
            source.DependUpon();

            return runs;
        });

        expression.AddObservableEventRaisedHandler(_ => { });
        Assert.AreEqual(1, runs, "hooking runs it once");

        Observable.RaiseNamedEvent("counted");

        Assert.AreEqual(2, runs, "and one raise is one re-run, not two");
    }

    [TestMethod]
    public void EvictionWaitsForTheRaiseThatEmptiedTheEntryToFinish()
    {
        // A handler is allowed to unregister itself during its own dispatch (section 3.4). If that evicted
        // the entry on the spot, the walk that is still calling the other handlers would be holding a dead
        // source - and a handler after this one could not register on it.
        var handle = Observable.GetNamed("self-removing");
        IDisposable? token = null;
        var aliveDuringTheRaise = false;

        token = handle.AddObservableEventRaisedHandler(_ =>
        {
            token!.Dispose();

            aliveDuringTheRaise = !handle.IsDisposed;
        });

        Observable.RaiseNamedEvent("self-removing");

        Assert.IsTrue(aliveDuringTheRaise, "the entry outlives the dispatch it was emptied in");
        Assert.IsTrue(handle.IsDisposed, "and is evicted once the raise is over");
    }

    [TestMethod]
    public void AnExpressionReCollectingKeepsTheEntryItWasWatching()
    {
        // An expression releases its whole set before it re-runs (section 3.6.1), so the entry it depends on
        // is momentarily empty on every recompute. Evicting there would give it a fresh handle every time -
        // and kill the one this function is holding while it re-reads it.
        var handle = Observable.GetNamed("re-collected");
        var runs = 0;
        var expression = Observable.CreateExpression(() =>
        {
            runs++;
            handle.DependUpon();
            return runs;
        });

        expression.AddObservableEventRaisedHandler(_ => { });
        Assert.AreEqual(1, runs, "the first run hooked the key");

        Observable.RaiseNamedEvent("re-collected");

        Assert.AreEqual(2, runs, "the raise re-ran it");
        Assert.IsFalse(expression.IsDisposed, "against a live entry rather than a dead handle");
        Assert.IsFalse(handle.IsDisposed, "which was never evicted in between");
        Assert.AreSame(handle, Observable.GetNamed("re-collected"), "so its identity survived the recompute");
    }

    [TestMethod]
    public void AnEntryIsEvictedOnceTheRunThatReleasedItIsOver()
    {
        // The other half of the deferral: a key a run stopped reading is not watched any more, and the table
        // takes it back as soon as the run is finished rather than at some later change.
        var handle = Observable.GetNamed("dropped");
        IObservableValue<bool>? flag = null;

        using var run = Observable.CollectDependencies(
            () =>
            {
                Observable.GetNamed("dropped").DependUpon();
                _ = flag!.Value;
            },
            _ => { });

        Assert.IsFalse(handle.IsDisposed, "the run is watching it");

        run.Dispose();

        Assert.IsTrue(handle.IsDisposed, "and releasing the run lets the key go");
    }

    [TestMethod]
    public void AHandleNobodyRegistersOnDoesNotOutliveTheNextChange()
    {
        // The table is as large as what is watched, so a key somebody asked about and never subscribed to is
        // taken back at the first idle point rather than sitting there for the life of the process.
        var abandoned = Observable.GetNamed("asked-and-forgotten");
        var value = Observable.CreateValue(0);
        value.AddObservableEventRaisedHandler(_ => { });

        value.Value = 1;

        Assert.IsTrue(abandoned.IsDisposed, "the entry nobody registered on was let go");
        Assert.AreNotSame(abandoned, Observable.GetNamed("asked-and-forgotten"), "and a later ask creates a new one");
    }

    [TestMethod]
    public void NullKeysAreRefused()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Observable.GetNamed(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => Observable.RaiseNamedEvent(null!));
    }

    [TestMethod]
    public void ARaisedKeyCountsAgainstTheEvaluationDepth()
    {
        // A named raise goes through the same dispatch a write does, so a key whose handler raises it again
        // is a loop like any other rather than a recursion the depth cannot see.
        var handle = Observable.GetNamed("looping");
        handle.AddObservableEventRaisedHandler(_ => Observable.RaiseNamedEvent("looping"));

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(
            () => Observable.RaiseNamedEvent("looping"),
            "the loop is refused at the limit");

        StringAssert.Contains(thrown.Message, "MaxEventStackSize", "with the limit named");
    }
}
