namespace Kolvir.Feature.Observables.Tests;

/// <summary>
/// Named dependencies: a handle is a name rather than an entry, handles for one key are interchangeable,
/// and the table holds the handlers for exactly as long as something is subscribed.
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class NamedDependencyTests
{
    [TestMethod]
    public void HandlesForOneKeyAreEqualButNotTheSameInstance()
    {
        var first = Observable.GetNamed("row-7");
        var second = Observable.GetNamed("row-7");

        Assert.AreEqual(first, second, "two handles for one key are the same dependency");
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode(), "and hash the same, so a set agrees");
        Assert.AreNotSame(first, second, "but they are different objects: a handle is a name, not an entry");
        Assert.AreNotEqual(first, Observable.GetNamed("row-8"), "and a different key is a different dependency");
    }

    [TestMethod]
    public void KeysCompareByTheirOwnEquality()
    {
        // Tuple keys are the reason the key is an object and not a string: the layer compares them the way
        // the type does, so a multi-part name needs no formatting and no separator escaping.
        Assert.AreEqual(Observable.GetNamed((7, "text")), Observable.GetNamed((7, "text")), "one dependency");
        Assert.AreNotEqual(Observable.GetNamed((7, "text")), Observable.GetNamed((7, "done")), "and one part differs");
    }

    [TestMethod]
    public void EveryHandleForOneKeyHearsTheRaise()
    {
        // The handlers belong to the key, not to the object a caller happened to get, so it does not matter
        // which handle a subscriber registered through or which handle the raise came from.
        var first = 0;
        var second = 0;

        Observable.GetNamed("both").AddObservableEventRaisedHandler(_ => first++);
        Observable.GetNamed("both").AddObservableEventRaisedHandler(_ => second++);

        Observable.RaiseNamedEvent("both");

        Assert.AreEqual(1, first, "one subscriber heard it");
        Assert.AreEqual(1, second, "and so did the other");
    }

    [TestMethod]
    public void AHandleIsNeverDead()
    {
        // A name has no lifetime, so nothing a caller does with a handle can make it fail: it is not holding
        // anything that could be taken away.
        var handle = Observable.GetNamed("never-dies");

        var token = handle.AddObservableEventRaisedHandler(_ => { });
        token.Dispose();

        Assert.IsFalse(handle.IsDisposed, "releasing the last handler does not kill the name");

        handle.DependUpon();
        handle.RaiseEvent();
        handle.RemoveObservableEventRaisedHandler(_ => { });

        handle.AddObservableEventRaisedHandler(_ => { }).Dispose();

        Assert.IsFalse(handle.IsDisposed, "and it is still usable afterwards");
    }

    [TestMethod]
    public void RaisingReachesAWatchedKeyAndIsHarmlessForAnUnwatchedOne()
    {
        var raised = 0;
        Observable.GetNamed("watched").AddObservableEventRaisedHandler(_ => raised++);

        Observable.RaiseNamedEvent("watched");

        Assert.AreEqual(1, raised, "a watched key notifies");

        Observable.RaiseNamedEvent("nobody-asked");

        Assert.AreEqual(1, raised, "and an unwatched key notifies nothing and throws nothing");
    }

    [TestMethod]
    public void AnExpressionReadingTheSameKeyTwiceHooksItOnce()
    {
        // Two subscriptions would mean two calls per raise, and one unhook per release. Two measurements: the
        // set the run reports, and how many re-runs one raise causes.
        using var run = Observable.CollectDependencies(
            () =>
            {
                Observable.GetNamed("counted").DependUpon();
                Observable.GetNamed("counted").DependUpon();
            },
            _ => { });

        Assert.AreEqual(1, run.Dependencies.Count, "one key read twice is one dependency, through two handles");

        var runs = 0;
        var expression = Observable.CreateExpression(() =>
        {
            runs++;

            var source = Observable.GetNamed("counted-expression");
            source.DependUpon();
            source.DependUpon();

            return runs;
        });

        expression.AddObservableEventRaisedHandler(_ => { });
        Assert.AreEqual(1, runs, "hooking runs it once");

        Observable.RaiseNamedEvent("counted-expression");

        Assert.AreEqual(2, runs, "and one raise is one re-run, not two");
    }

    [TestMethod]
    public void AHandlerThatRemovesItselfDoesNotDisturbTheRestOfTheRaise()
    {
        // Removing the last handler drops the entry from the table. The dispatch is walking the array it
        // started with, which is a snapshot, so the handler behind it still runs - and a handler that
        // registers again recreates the entry for the next raise.
        var handle = Observable.GetNamed("self-removing");
        IDisposable? token = null;
        var selfRemoving = 0;
        var after = 0;

        token = handle.AddObservableEventRaisedHandler(_ =>
        {
            selfRemoving++;
            token!.Dispose();
        });
        handle.AddObservableEventRaisedHandler(_ => after++);

        Observable.RaiseNamedEvent("self-removing");

        Assert.AreEqual(1, after, "the handler behind the one that unregistered still ran");

        Observable.RaiseNamedEvent("self-removing");

        Assert.AreEqual(1, selfRemoving, "and the one that unregistered itself is not called again");
        Assert.AreEqual(2, after, "while the one still registered is");
    }

    [TestMethod]
    public void AHandleKeptAcrossARecomputeStillWorks()
    {
        // The point of a name: an expression releases its whole set before it re-runs, so a handle it keeps
        // in the function is the one thing a lifetime rule would have broken. Here it is ordinary.
        var handle = Observable.GetNamed("retained");
        var runs = 0;

        var expression = Observable.CreateExpression(() =>
        {
            runs++;
            handle.DependUpon();
            return runs;
        });

        expression.AddObservableEventRaisedHandler(_ => { });
        Assert.AreEqual(1, runs, "the first run subscribed");

        Observable.RaiseNamedEvent("retained");

        Assert.AreEqual(2, runs, "the raise re-ran it");

        Observable.RaiseNamedEvent("retained");

        Assert.AreEqual(3, runs, "and the same handle worked the second time");
    }

    [TestMethod]
    public void AKeyReleasedBetweenRunsIsWatchedAgainWhenItIsReadAgain()
    {
        var first = Observable.CollectDependencies(
            () => Observable.GetNamed("alternating").DependUpon(),
            _ => { });

        first.Dispose();

        var raised = 0;

        using var second = Observable.CollectDependencies(
            () => Observable.GetNamed("alternating").DependUpon(),
            _ => raised++);

        Observable.RaiseNamedEvent("alternating");

        Assert.AreEqual(1, raised, "the key was picked up again by the run that re-read it");
    }

    [TestMethod]
    public void ARaisedKeyCountsAgainstTheEvaluationDepth()
    {
        // A named raise goes through the same dispatch a write does, so a key whose handler raises it again
        // is a loop like any other rather than a recursion the depth cannot see.
        Observable.GetNamed("looping").AddObservableEventRaisedHandler(_ => Observable.RaiseNamedEvent("looping"));

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(
            () => Observable.RaiseNamedEvent("looping"),
            "the loop is refused at the limit");

        StringAssert.Contains(thrown.Message, "MaxEventStackSize", "with the limit named");
    }

    [TestMethod]
    public void NullKeysAreRefused()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Observable.GetNamed(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => Observable.RaiseNamedEvent(null!));
    }
}
