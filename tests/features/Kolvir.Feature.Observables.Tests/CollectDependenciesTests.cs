namespace Kolvir.Observables.Tests;

/// <summary>
/// The collector: one synchronous run, the distinct sources it read in the order it read them, and the
/// set that comes back even when the body threw.
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

        var run = Observable.CollectDependencies(() =>
        {
            _ = first.Value;
            _ = second.Value;
        });

        Assert.AreEqual(2, run.Dependencies.Count, "both reads were collected");
        Assert.AreSame(first, run.Dependencies[0], "in the order the body read them");
        Assert.AreSame(second, run.Dependencies[1], "which is the order a cycle message needs");
        Assert.IsFalse(run.IsFaulted, "and the body finished");
        Assert.IsNull(run.Fault, "so there is nothing to rethrow");
    }

    [TestMethod]
    public void CollectingAnEmptyBodyReportsNothing()
    {
        var run = Observable.CollectDependencies(() => { });

        Assert.AreEqual(0, run.Dependencies.Count, "a body that read nothing depends on nothing");
        Assert.IsFalse(run.IsFaulted, "and it finished");
    }

    [TestMethod]
    public void DuplicatesCollapseToTheirFirstRead()
    {
        // A body that reads a value in a loop must report one dependency, or every expression over a
        // collection would hook the same source once per item.
        var value = Observable.CreateValue(1);

        var run = Observable.CollectDependencies(() =>
        {
            for (var i = 0; i < 5; i++)
            {
                _ = value.Value;
            }
        });

        Assert.AreEqual(1, run.Dependencies.Count, "read five times, collected once");
        Assert.AreSame(value, run.Dependencies[0], "and it is the source itself");
    }

    [TestMethod]
    public void AnUntypedReadIsCollectedToo()
    {
        var value = Observable.CreateValue("hello");
        IObservableValue untyped = value;

        var run = Observable.CollectDependencies(() =>
        {
            _ = untyped.Value;
        });

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

        var outer = Observable.CollectDependencies(() =>
        {
            _ = outerBefore.Value;
            inner = Observable.CollectDependencies(() => { _ = innerOnly.Value; });
            _ = outerAfter.Value;
        });

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

        var run = Observable.CollectDependencies(() =>
        {
            _ = inside.Value;
        });

        Assert.AreEqual(1, run.Dependencies.Count, "a read with no run to publish to goes nowhere");
        Assert.AreSame(inside, run.Dependencies[0], "and the run collected only its own");
    }

    [TestMethod]
    public void TheSetComesBackWhenTheBodyThrows()
    {
        // The dependencies a run reached are what its result - success or exception - was computed from,
        // so they come back with the failure instead of being replaced by it.
        var read = Observable.CreateValue(1);
        var never = Observable.CreateValue(2);
        var thrown = new InvalidOperationException("boom");

        var run = Observable.CollectDependencies(() =>
        {
            _ = read.Value;
            throw thrown;
        });

        Assert.AreEqual(1, run.Dependencies.Count, "what it read before throwing is in the set");
        Assert.AreSame(read, run.Dependencies[0], "and what it never reached is not");
        Assert.IsTrue(run.IsFaulted, "the failure is recorded rather than thrown");
        Assert.AreSame(thrown, run.Fault, "the original instance, for a caller that wants to inspect it");
        Assert.IsFalse(run.Dependencies.Contains(never), "the unread value is not there");

        var rethrown = Assert.ThrowsExactly<InvalidOperationException>(run.Throw, "Throw rethrows it");

        Assert.AreSame(thrown, rethrown, "and the same instance arrives, with its original stack");
    }

    [TestMethod]
    public void ThrowDoesNothingWhenTheBodyFinished()
    {
        var run = Observable.CollectDependencies(() => { });

        run.Throw();

        Assert.IsFalse(run.IsFaulted, "so an unconditional Throw at the end of a collector is safe");
    }

    [TestMethod]
    public void TheTypedFormReturnsTheBodysResult()
    {
        var first = Observable.CreateValue(1);
        var second = Observable.CreateValue(2);

        var run = Observable.CollectDependencies(() => first.Value + second.Value);

        Assert.AreEqual(3, run.Value, "the result comes back beside the set");
        Assert.AreEqual(2, run.Dependencies.Count, "with both reads");
    }

    [TestMethod]
    public void TheTypedFormThrowsThroughValue()
    {
        // Reading Value is how the two halves are used together: hook the set, then read the result. A
        // render that threw therefore still ends up listening to what it read.
        var read = Observable.CreateValue(1);
        var thrown = new InvalidOperationException("boom");

        var run = Observable.CollectDependencies<int>(() =>
        {
            _ = read.Value;
            throw thrown;
        });

        Assert.AreEqual(1, run.Dependencies.Count, "the set is there to hook first");

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
        // current and every later read in the process would join a set that has already been handed out.
        var first = Observable.CreateValue(1);
        var later = Observable.CreateValue(2);

        var run = Observable.CollectDependencies(() =>
        {
            _ = first.Value;
            throw new InvalidOperationException("boom");
        });

        _ = later.Value;

        Assert.AreEqual(1, run.Dependencies.Count, "the later read did not join the finished run's set");
        Assert.AreSame(first, run.Dependencies[0], "which still holds only what it read");
    }

    [TestMethod]
    public void CollectingDoesNotSubscribe()
    {
        // The collector reports; hooking is the caller's decision and the caller's token. The component
        // seam depends on that split, because it hooks only after it knows the whole set.
        var probe = new ProbeSource();

        var run = Observable.CollectDependencies(() => ((IObservableEventSource)probe).DependUpon());

        Assert.AreEqual(1, run.Dependencies.Count, "the read was collected");
        Assert.AreEqual(0, probe.Registrations, "and the collector registered nothing");
    }

    /// <summary>A hand-written source, which is what an application with its own dependency would write.</summary>
    private sealed class ProbeSource : IObservableEventSource
    {
        public int Registrations { get; private set; }

        public bool IsDisposed => false;

        public IDisposable AddObservableEventRaisedHandler(Action<IObservableEventSource> handler)
        {
            Registrations++;
            return new Release();
        }

        public void RemoveObservableEventRaisedHandler(Action<IObservableEventSource> handler)
        {
        }

        public void RaiseEvent()
        {
        }

        private sealed class Release : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
