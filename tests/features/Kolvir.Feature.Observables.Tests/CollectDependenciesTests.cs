namespace Kolvir.Feature.Observables.Tests;

/// <summary>
/// The collector: one synchronous run, the sources it read in the order it read them, a subscription to
/// each of them taken as it read, and the set that comes back even when the body threw.
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class CollectDependenciesTests
{
    [TestMethod]
    public void CollectingReportsWhatTheBodyRead()
    {
        var first = Observable.CreateValue(1);
        var second = Observable.CreateValue(2);

        using var run = Observable.CollectDependencies(
            () =>
            {
                _ = first.Value;
                _ = second.Value;
            },
            _ => { });

        Assert.AreEqual(2, run.Dependencies.Count, "both reads were collected");
        Assert.AreSame(first, run.Dependencies[0], "in the order the body read them");
        Assert.AreSame(second, run.Dependencies[1], "which is the order a cycle message needs");
        Assert.IsFalse(run.IsFaulted, "and the body finished");
        Assert.IsNull(run.Fault, "so there is nothing to rethrow");
    }

    [TestMethod]
    public void CollectingAnEmptyBodyReportsNothing()
    {
        using var run = Observable.CollectDependencies(() => { }, _ => { });

        Assert.AreEqual(0, run.Dependencies.Count, "a body that read nothing depends on nothing");
        Assert.IsFalse(run.IsFaulted, "and it finished");
    }

    [TestMethod]
    public void DuplicatesCollapseToTheirFirstRead()
    {
        // A body that reads a value in a loop must report one dependency and take one subscription, or
        // every expression over a collection would subscribe to the same source once per item.
        var value = Observable.CreateValue(1);

        using var run = Observable.CollectDependencies(
            () =>
            {
                for (var index = 0; index < 5; index++)
                {
                    _ = value.Value;
                }
            },
            _ => { });

        Assert.AreEqual(1, run.Dependencies.Count, "read five times, collected once");
        Assert.AreSame(value, run.Dependencies[0], "and it is the source itself");
    }

    [TestMethod]
    public void AnUntypedReadIsCollectedToo()
    {
        var value = Observable.CreateValue("hello");
        IObservableValue untyped = value;

        using var run = Observable.CollectDependencies(
            () =>
            {
                _ = untyped.Value;
            },
            _ => { });

        Assert.AreEqual(1, run.Dependencies.Count, "a consumer that reads through the untyped view is "
            + "tracked like one that reads the typed one");
        Assert.AreSame(value, run.Dependencies[0], "and it is the same source underneath the boxing");
    }

    [TestMethod]
    public void NestedScopesStaySeparate()
    {
        // Isolation is the design and not a convenience: an outer body that collects an inner one is
        // depending on what the inner one read, not on the outer's own reads, and each re-runs on its own
        // change (dev-docs/OBSERVABLES.md section 3.3).
        var outerBefore = Observable.CreateValue(1);
        var innerOnly = Observable.CreateValue(2);
        var outerAfter = Observable.CreateValue(3);
        Collected? inner = null;

        using var outer = Observable.CollectDependencies(
            () =>
            {
                _ = outerBefore.Value;
                inner = Observable.CollectDependencies(() =>
                {
                    _ = innerOnly.Value;
                }, _ => { });
                _ = outerAfter.Value;
            },
            _ => { });

        Assert.IsNotNull(inner, "the inner run happened");
        Assert.AreEqual(1, inner!.Dependencies.Count, "the inner run sees only what it read");
        Assert.AreSame(innerOnly, inner.Dependencies[0], "which is its own value");
        Assert.AreEqual(2, outer.Dependencies.Count, "and the outer run is not polluted by it");
        Assert.AreSame(outerBefore, outer.Dependencies[0], "outer read first");
        Assert.AreSame(outerAfter, outer.Dependencies[1], "then the one after the inner run - the inner "
            + "value is absent, which is the isolation");
    }

    [TestMethod]
    public void AReadOutsideARunIsNotCollected()
    {
        var outside = Observable.CreateValue(1);
        var inside = Observable.CreateValue(2);

        _ = outside.Value;

        using var run = Observable.CollectDependencies(
            () =>
            {
                _ = inside.Value;
            },
            _ => { });

        Assert.AreEqual(1, run.Dependencies.Count, "a read with no run to publish to goes nowhere");
        Assert.AreSame(inside, run.Dependencies[0], "and the run collected only its own");
    }

    [TestMethod]
    public void CollectingSubscribesWhileItReadsRatherThanAfterwards()
    {
        // The whole point of the shape: by the time a read returns, the source is already being watched,
        // so there is no window in which the body has the value and is not listening to what produced it.
        var probe = new CountingSource();
        var handlersSeenDuringTheBody = -1;
        var raised = 0;

        using var run = Observable.CollectDependencies(
            () =>
            {
                ((IObservableEventSource)probe).DependUpon();
                handlersSeenDuringTheBody = probe.ActiveHandlers;
            },
            _ => raised++);

        Assert.AreEqual(1, handlersSeenDuringTheBody, "subscribed before the read returned");
        Assert.AreEqual(1, run.Dependencies.Count, "and reported as a dependency");

        probe.RaiseEvent();

        Assert.AreEqual(1, raised, "so a raise reaches the handler the run was given");
    }

    [TestMethod]
    public void DisposingTheRunStopsListening()
    {
        var probe = new CountingSource();
        var raised = 0;
        var run = Observable.CollectDependencies(
            () => ((IObservableEventSource)probe).DependUpon(),
            _ => raised++);

        run.Dispose();

        Assert.AreEqual(0, probe.ActiveHandlers, "the subscriptions went with it");

        probe.RaiseEvent();
        Assert.AreEqual(0, raised, "so a raise afterwards reaches nobody");

        run.Dispose();

        Assert.AreEqual(0, probe.ActiveHandlers, "and disposing twice is not an error");
    }

    [TestMethod]
    public void AWriteFromInsideTheBodyIsDeliveredLikeAnyOther()
    {
        // Collecting forbids nothing. A write the body makes to something it already read reaches the
        // run's own subscription, so the caller's handler is called from inside the body - and a handler
        // that writes back is a loop stopped by the raise depth, not by the collector (section 3.8).
        var value = Observable.CreateValue(1);
        var raised = 0;
        var raisedWhileTheBodyWasStillRunning = false;

        using var run = Observable.CollectDependencies(
            () =>
            {
                _ = value.Value;

                value.Value = 2;

                raisedWhileTheBodyWasStillRunning = raised == 1;
            },
            _ => raised++);

        Assert.IsFalse(run.IsFaulted, "the body ran to completion");
        Assert.IsTrue(raisedWhileTheBodyWasStillRunning, "the handler was called from inside the body");
        Assert.AreEqual(1, raised, "once, for the one write");
        Assert.AreEqual(2, value.Value, "and the write landed");
    }

    [TestMethod]
    public void TheSetComesBackWhenTheBodyThrows()
    {
        // The dependencies a run reached are what its result - success or exception - was computed from,
        // so they come back with the failure, and so does the subscription to them.
        var read = Observable.CreateValue(1);
        var never = Observable.CreateValue(2);
        var thrown = new InvalidOperationException("boom");
        var raised = 0;

        using var run = Observable.CollectDependencies(
            () =>
            {
                _ = read.Value;
                throw thrown;
            },
            _ => raised++);

        Assert.AreEqual(1, run.Dependencies.Count, "what it read before throwing is in the set");
        Assert.AreSame(read, run.Dependencies[0], "and what it never reached is not");
        Assert.IsTrue(run.IsFaulted, "the failure is recorded rather than thrown");
        Assert.AreSame(thrown, run.Fault, "the original instance, for a caller that wants to inspect it");

        read.Value = 5;

        Assert.AreEqual(1, raised, "and the run is still watching what it reached, which is what can "
            + "make the failure go away");

        var rethrown = Assert.ThrowsExactly<InvalidOperationException>(run.Throw, "Throw rethrows it");

        Assert.AreSame(thrown, rethrown, "and the same instance arrives, with its original stack");
    }

    [TestMethod]
    public void ThrowDoesNothingWhenTheBodyFinished()
    {
        using var run = Observable.CollectDependencies(() => { }, _ => { });

        run.Throw();

        Assert.IsFalse(run.IsFaulted, "so an unconditional Throw at the end of a collector is safe");
    }

    [TestMethod]
    public void TheTypedFormReturnsTheBodysResult()
    {
        var first = Observable.CreateValue(1);
        var second = Observable.CreateValue(2);

        using var run = Observable.CollectDependencies(() => first.Value + second.Value, _ => { });

        Assert.AreEqual(3, run.Value, "the result comes back beside the set");
        Assert.AreEqual(2, run.Dependencies.Count, "with both reads");
    }

    [TestMethod]
    public void TheTypedFormThrowsThroughValue()
    {
        var read = Observable.CreateValue(1);
        var thrown = new InvalidOperationException("boom");

        using var run = Observable.CollectDependencies<int>(
            () =>
            {
                _ = read.Value;
                throw thrown;
            },
            _ => { });

        Assert.AreEqual(1, run.Dependencies.Count, "the run is subscribed to what it read first");

        var rethrown = Assert.ThrowsExactly<InvalidOperationException>(
            () => _ = run.Value,
            "and reading the result is where the failure arrives");

        Assert.AreSame(thrown, rethrown, "as the very same exception the body threw");
        Assert.AreSame(thrown, run.Fault, "which is also what Fault holds");
    }

    [TestMethod]
    public void TheScopeIsRestoredWhenTheBodyThrows()
    {
        // The scope is restored in a finally. If it were not, the failed run's scope would still be
        // current, and every later read in the process would join a set that has already been handed out.
        var first = Observable.CreateValue(1);
        var later = Observable.CreateValue(2);

        using var run = Observable.CollectDependencies(
            () =>
            {
                _ = first.Value;
                throw new InvalidOperationException("boom");
            },
            _ => { });

        _ = later.Value;

        Assert.AreEqual(1, run.Dependencies.Count, "the later read did not join the finished run's set");
        Assert.AreSame(first, run.Dependencies[0], "which still holds only what it read");
    }

    [TestMethod]
    public void ALargeReadSetStillDeDuplicatesAndKeepsItsOrder()
    {
        // Past the scope's scan limit it hashes instead of scanning. The two have to agree: no
        // duplicates, no second subscription, and first-read order either way.
        var values = Enumerable.Range(0, 40).Select(index => Observable.CreateValue(index)).ToArray();

        using var run = Observable.CollectDependencies(
            () =>
            {
                foreach (var value in values)
                {
                    _ = value.Value;
                }

                foreach (var value in values)
                {
                    _ = value.Value;
                }
            },
            _ => { });

        Assert.AreEqual(values.Length, run.Dependencies.Count, "read twice, collected once each");

        for (var index = 0; index < values.Length; index++)
        {
            Assert.AreSame(values[index], run.Dependencies[index], "first-read order survives the switch to hashing");
        }
    }
}
