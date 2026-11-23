namespace Kolvir.Feature.Observables.Tests;

/// <summary>
/// An expression: a function whose result is cached and kept up to date by what it read. Cold it hooks
/// nothing and re-runs on every read; hot it trusts its cache, re-collects on a change, and raises only
/// when the result differs.
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class ExpressionTests
{
    [TestMethod]
    public void CreatingAnExpressionRunsNothing()
    {
        // A derived value nobody reads costs a delegate and nothing else - not a run, not a set, not an
        // exception it might throw before anyone is listening.
        var runs = 0;
        var value = Observable.CreateValue(1);

        _ = Observable.CreateExpression(() =>
        {
            runs++;
            return value.Value * 2;
        });

        Assert.AreEqual(0, runs, "creation records the function and evaluates nothing");
    }

    [TestMethod]
    public void AColdExpressionRunsOnEveryRead()
    {
        var runs = 0;
        var value = Observable.CreateValue(1);
        var expression = Observable.CreateExpression(() =>
        {
            runs++;
            return value.Value * 2;
        });

        Assert.AreEqual(2, expression.Value, "the value comes from the function");
        _ = expression.Value;
        _ = expression.Value;

        Assert.AreEqual(3, runs, "nothing is maintaining a cache, so no read trusts one");
    }

    [TestMethod]
    public void AColdExpressionHooksNothing()
    {
        // An unused derived value must not keep its whole dependency tree alive.
        var probe = new CountingSource();
        var expression = Observable.CreateExpression(() =>
        {
            ((IObservableEventSource)probe).DependUpon();
            return 1;
        });

        _ = expression.Value;
        _ = expression.Value;

        Assert.AreEqual(0, probe.Registrations, "a cold expression subscribes to nothing");
        Assert.AreEqual(0, probe.ActiveHandlers, "and holds nothing open");
    }

    [TestMethod]
    public void AColdExpressionInsideACollectionIsTheDependencyRatherThanItsReads()
    {
        // Isolation: the outer run hears about the inner expression changing through the expression, so
        // it re-evaluates once per inner change rather than once per deep dependency.
        var value = Observable.CreateValue(1);
        var runs = 0;
        var expression = Observable.CreateExpression(() =>
        {
            runs++;
            return value.Value * 2;
        });

        using var run = Observable.CollectDependencies(
            () =>
            {
                _ = expression.Value;
            },
            _ => { });

        Assert.AreEqual(1, run.Dependencies.Count, "one dependency, not two");
        Assert.AreSame(expression, run.Dependencies[0], "and it is the expression, not what it reads");
        Assert.AreEqual(1, runs, "and the read ran it once, not once for the read and again for the "
            + "subscription the run took on it");
    }

    [TestMethod]
    public void BecomingHotEvaluatesOnceHooksAndRaisesNothing()
    {
        var probe = new CountingSource();
        var runs = 0;
        var expression = Observable.CreateExpression(() =>
        {
            runs++;
            ((IObservableEventSource)probe).DependUpon();
            return 1;
        });
        var raised = 0;

        expression.AddObservableEventRaisedHandler(_ => raised++);

        Assert.AreEqual(1, runs, "the first handler is what evaluates it, and it evaluates once");
        Assert.AreEqual(1, probe.ActiveHandlers, "the run's dependencies are hooked");
        Assert.AreEqual(0, raised, "and nothing is raised: the subscriber count only went up");
    }

    [TestMethod]
    public void AHotReadIsAField()
    {
        // A render that reads the same expression twice must not cost two runs.
        var runs = 0;
        var value = Observable.CreateValue(1);
        var expression = Observable.CreateExpression(() =>
        {
            runs++;
            return value.Value * 2;
        });

        expression.AddObservableEventRaisedHandler(_ => { });

        Assert.AreEqual(2, expression.Value, "the cached result");
        _ = expression.Value;

        Assert.AreEqual(1, runs, "the hooks are what keeps the cache honest, so a read is a field");
    }

    [TestMethod]
    public void ADependencyChangeRecomputesAndRaises()
    {
        var value = Observable.CreateValue(1);
        var expression = Observable.CreateExpression(() => value.Value * 2);
        var raised = 0;
        expression.AddObservableEventRaisedHandler(_ => raised++);

        value.Value = 5;

        Assert.AreEqual(1, raised, "one change is one notification");
        Assert.AreEqual(10, expression.Value, "with the recomputed result already cached");
    }

    [TestMethod]
    public void AnUnchangedResultRaisesNothing()
    {
        var value = Observable.CreateValue(1);
        var runs = 0;
        var expression = Observable.CreateExpression(() =>
        {
            runs++;
            return value.Value % 2;
        });
        var raised = 0;
        expression.AddObservableEventRaisedHandler(_ => raised++);

        value.Value = 3;

        Assert.AreEqual(2, runs, "the dependency changed, so the function ran again");
        Assert.AreEqual(0, raised, "but the result is the same, so subscribers hear nothing");
    }

    [TestMethod]
    public void AChangedDependencySetIsRehookedEvenWhenTheResultIsUnchanged()
    {
        // The set describes the current run. Conditional reads are why it has to move independently of
        // the result: keep the old set and the expression stays hooked to what it stopped reading and
        // deaf to what it started.
        var flag = Observable.CreateValue(true);
        var first = Observable.CreateValue(1);
        var second = Observable.CreateValue(1);
        var expression = Observable.CreateExpression(() => flag.Value ? first.Value : second.Value);
        var raised = 0;
        expression.AddObservableEventRaisedHandler(_ => raised++);

        flag.Value = false;

        Assert.AreEqual(0, raised, "the other branch returns the same value, so nothing changed");

        first.Value = 9;

        Assert.AreEqual(0, raised, "the expression no longer reads the first value, so it does not react");

        second.Value = 9;

        Assert.AreEqual(1, raised, "the second value became a dependency without the result ever changing");
    }

    [TestMethod]
    public void TheLastUnsubscribeMakesItColdAgain()
    {
        var value = Observable.CreateValue(1);
        var runs = 0;
        var expression = Observable.CreateExpression(() =>
        {
            runs++;
            return value.Value;
        });
        var token = expression.AddObservableEventRaisedHandler(_ => { });

        token.Dispose();
        value.Value = 2;

        Assert.AreEqual(1, runs, "a cold expression is not listening");

        Assert.AreEqual(2, expression.Value, "but it is still readable, and the read is a run");
        Assert.AreEqual(2, runs, "which is what keeps a stale cache from being served");
    }

    [TestMethod]
    public void AThrowingFunctionCachesItsExceptionAndKeepsWhatItReached()
    {
        var probe = new CountingSource();
        var expression = Observable.CreateExpression<int>(() =>
        {
            ((IObservableEventSource)probe).DependUpon();
            throw new InvalidOperationException("boom");
        });

        var coldRead = Assert.ThrowsExactly<InvalidOperationException>(
            () => _ = expression.Value,
            "a cold read throws what the function threw");

        expression.AddObservableEventRaisedHandler(_ => { });

        Assert.AreEqual(1, probe.ActiveHandlers, "the run that threw still hooked what it reached, so a "
            + "change to it can revive the expression");

        var firstRead = Assert.ThrowsExactly<InvalidOperationException>(() => _ = expression.Value, "a hot read throws");
        var secondRead = Assert.ThrowsExactly<InvalidOperationException>(() => _ = expression.Value, "again");

        Assert.AreSame(firstRead, secondRead, "and it is the cached exception, not a fresh run");
        Assert.AreNotSame(coldRead, firstRead, "the hot run built its own, as every run does");
    }

    [TestMethod]
    public void AFailureReplacesAValueAndRaises()
    {
        var value = Observable.CreateValue(0);
        var failing = false;
        var expression = Observable.CreateExpression(
            () => failing ? throw new InvalidOperationException("boom") : value.Value * 2);
        var raised = 0;
        expression.AddObservableEventRaisedHandler(_ => raised++);

        failing = true;
        value.Value = 1;

        Assert.AreEqual(1, raised, "success to failure is a change, so subscribers are told");
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = expression.Value, "and the result is now the exception");
    }

    [TestMethod]
    public void ARepeatedFailureRaisesEveryTimeByDefault()
    {
        // A reader can branch on an exception's type, message or inner exception, so "still failing,
        // differently" is a different result. With no comparer, two exceptions are never equal.
        var value = Observable.CreateValue(0);
        var expression = Observable.CreateExpression<string>(() =>
        {
            _ = value.Value;
            throw new InvalidOperationException("boom");
        });
        var raised = 0;
        expression.AddObservableEventRaisedHandler(_ => raised++);

        value.Value = 1;
        value.Value = 2;

        Assert.AreEqual(2, raised, "each failing run is a new exception, and a new exception is a change");
    }

    [TestMethod]
    public void AnExceptionComparerSuppressesTheRepeatButAReadSeesTheNewest()
    {
        // The scenario the comparer exists for: a service that has already reported "backend
        // unavailable" does not want the next wait to look like progress, while a reader that switches on
        // the message still gets the exception it just threw.
        var value = Observable.CreateValue(0);
        var expression = Observable.CreateExpression<string>(
            () =>
            {
                var current = value.Value;

                return current == 0 ? "ok" : throw new InvalidOperationException("backend unavailable");
            },
            exceptionComparer: EqualityComparer<Exception>.Create(
                (left, right) => left is not null
                    && right is not null
                    && left.GetType() == right.GetType()
                    && left.Message == right.Message));
        var raised = 0;
        expression.AddObservableEventRaisedHandler(_ => raised++);

        value.Value = 1;

        Assert.AreEqual(1, raised, "success to failure is always a change");

        var firstFailure = Assert.ThrowsExactly<InvalidOperationException>(() => _ = expression.Value, "the first failure");

        value.Value = 2;

        Assert.AreEqual(1, raised, "the second failure compared equal, so nobody was told again");

        var secondFailure = Assert.ThrowsExactly<InvalidOperationException>(() => _ = expression.Value, "the second failure");

        Assert.AreNotSame(firstFailure, secondFailure, "but the cache still took the newest exception - a "
            + "suppressed notification is not a suppressed update");
    }

    [TestMethod]
    public void DisposingStopsListeningBeforeItRaisesForTheLastTime()
    {
        var probe = new CountingSource();
        var expression = Observable.CreateExpression(() =>
        {
            ((IObservableEventSource)probe).DependUpon();
            return 1;
        });
        var raised = 0;
        expression.AddObservableEventRaisedHandler(_ => raised++);

        expression.Dispose();

        Assert.AreEqual(0, probe.ActiveHandlers, "it stopped listening");
        Assert.AreEqual(1, raised, "and told its subscriber once that it is gone");

        probe.RaiseEvent();

        Assert.AreEqual(1, raised, "so a dependency change afterwards reaches nothing");
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = expression.Value, "and the value is gone");
        Assert.IsTrue(expression.IsDisposed, "which IsDisposed says too");
    }

    [TestMethod]
    public void AHookedExpressionMakesTheOneItReadsHot()
    {
        // Composition falls out of the rules rather than being arranged: hooking the outer expression
        // makes it a handler on the inner one, which is what makes the inner one hot.
        var value = Observable.CreateValue(1);
        var inner = Observable.CreateExpression(() => value.Value * 2);
        var outer = Observable.CreateExpression(() => inner.Value + 1);
        var raised = 0;
        outer.AddObservableEventRaisedHandler(_ => raised++);

        value.Value = 5;

        Assert.AreEqual(1, raised, "one change reached the outer expression through the inner one");
        Assert.AreEqual(11, outer.Value, "and the cache is up to date at both levels");
    }

    [TestMethod]
    public void AReadFromInsideARunLeavesTheExpressionSubscribed()
    {
        // Reads subscribe as they happen, so the expression is hot by the time the read returns: the value
        // handed back is the one it keeps, and a second read is a field rather than another run. Before
        // that, the read was an unmonitored run whose result was thrown away and recomputed the moment the
        // outer run subscribed.
        var runs = 0;
        var value = Observable.CreateValue(1);
        var expression = Observable.CreateExpression(() =>
        {
            runs++;
            return value.Value * 2;
        });

        using var run = Observable.CollectDependencies(
            () =>
            {
                _ = expression.Value;
            },
            _ => { });

        Assert.AreEqual(1, run.Dependencies.Count, "the outer run depends on the expression");
        Assert.AreEqual(1, runs, "and the read ran it exactly once");

        Assert.AreEqual(2, expression.Value, "the value it kept");
        Assert.AreEqual(1, runs, "so the next read is a field, because the subscription is already there");
    }

    [TestMethod]
    public void AColdExpressionReadByAColdExpressionStillReachesTheTop()
    {
        // The notification path has to survive two cold links, which is the case a suspended collection
        // could plausibly break: the outer run records the middle expression as its dependency, hooking
        // that expression is what makes the middle hot, and the middle hooking the innermost is what
        // completes the chain. Nothing has to be collected during the cold reads for any of that.
        var leaf = Observable.CreateValue(1);
        var innermost = Observable.CreateExpression(() => leaf.Value * 2);
        var middle = Observable.CreateExpression(() => innermost.Value + 10);
        var outer = Observable.CreateExpression(() => middle.Value + 100);
        var raised = 0;

        outer.AddObservableEventRaisedHandler(_ => raised++);

        Assert.AreEqual(112, outer.Value, "1 * 2 + 10 + 100, with all three cold until that line");

        leaf.Value = 5;

        Assert.AreEqual(1, raised, "a change at the leaf reached the top through two cold links");
        Assert.AreEqual(120, outer.Value, "5 * 2 + 10 + 100");
    }

    [TestMethod]
    public void AnExpressionBuiltInsideARunStillReachesIt()
    {
        // The strongest version of "it is not being monitored": the inner expression exists only for the
        // duration of one outer run, and nothing holds it except the hook the outer took on it.
        var leaf = Observable.CreateValue(1);
        var outer = Observable.CreateExpression(() =>
        {
            var inner = Observable.CreateExpression(() => leaf.Value * 2);
            return inner.Value + 1;
        });
        var raised = 0;

        outer.AddObservableEventRaisedHandler(_ => raised++);

        Assert.AreEqual(3, outer.Value, "1 * 2 + 1");

        leaf.Value = 5;

        Assert.AreEqual(1, raised, "the transient inner expression still carried the change up");
        Assert.AreEqual(11, outer.Value, "and the outer rebuilt it around the new value");
    }

    [TestMethod]
    public void AWriteToSomethingTheRunReadEndsAtTheDepthLimit()
    {
        // The run is subscribed to the value it read, so writing it raises straight back into the
        // expression that is still running - a loop. It is the raise chain that ends it: each turn is
        // another raise nested inside the last, until one is refused. Every run that completed along the
        // way caches its result, so what is left is the outermost run's answer, not a wedged expression.
        var limit = Observable.MaxEventStackSize;
        var value = Observable.CreateValue(1);
        var expression = Observable.CreateExpression(() =>
        {
            var current = value.Value;
            value.Value = current + 1;
            return current;
        });

        try
        {
            Observable.MaxEventStackSize = 4;

            expression.AddObservableEventRaisedHandler(_ => { });

            var thrown = Assert.ThrowsExactly<InvalidOperationException>(
                () => _ = expression.Value,
                "the loop's failure is the cached result, thrown by the read like any other");

            StringAssert.Contains(thrown.Message, "MaxEventStackSize", "and it names the limit");
            Assert.AreEqual(6, value.Value, "the chain stopped at the limit");
        }
        finally
        {
            Observable.MaxEventStackSize = limit;
        }
    }
    [TestMethod]
    public void AWriteToSomethingTheRunDoesNotReadIsAllowed()
    {
        // A blanket ban would be wrong. A function may keep its own scratch state - a last-evaluated
        // stamp, a cached intermediate, a diagnostic counter - and none of that is a dependency.
        var scratch = Observable.CreateValue(0);
        var source = Observable.CreateValue(1);
        var expression = Observable.CreateExpression(() =>
        {
            scratch.Value = 99;
            return source.Value * 2;
        });

        expression.AddObservableEventRaisedHandler(_ => { });

        Assert.AreEqual(2, expression.Value, "the run finished");
        Assert.AreEqual(99, scratch.Value, "and the write landed");
    }

    [TestMethod]
    public void ChangingAValueAnotherExpressionReadsFromInsideARunIsAllowed()
    {
        // A cascade that cannot come back to this expression is ordinary, exactly as it would be from a
        // handler: the other expression re-evaluates nested, inside this run, and this run is unaffected.
        var flag = Observable.CreateValue(0);
        var other = Observable.CreateExpression(() => flag.Value * 10);
        var otherRaised = 0;
        other.AddObservableEventRaisedHandler(_ => otherRaised++);

        var mine = Observable.CreateExpression(() =>
        {
            flag.Value = 3;
            return 1;
        });

        mine.AddObservableEventRaisedHandler(_ => { });

        Assert.AreEqual(1, mine.Value, "the run finished");
        Assert.AreEqual(1, otherRaised, "and the expression the value feeds was told");
        Assert.AreEqual(30, other.Value, "with its cache up to date");
    }

    [TestMethod]
    public void AnExpressionNamedAtItsCallSiteReportsThatName()
    {
        var named = Observable.CreateExpression(() => 1, "total");
        var located = Observable.CreateExpression(() => 1);

        Assert.AreEqual("total", named.ToString(), "a name is used as it was given");
        StringAssert.Contains(located.ToString(), "ExpressionTests.cs:", "an unnamed one reports its creation site");
    }

    [TestMethod]
    public void AValueComparerAloneStillBindsToTheLocatedOverload()
    {
        // The two overloads are told apart by the call site, not guessed: a name picks the named form,
        // and everything else - including a comparer on its own - picks the located one, which is the
        // form that carries no caller attributes.
        IEqualityComparer<int> comparer = EqualityComparer<int>.Default;

        var withComparerOnly = Observable.CreateExpression(() => 1, comparer);
        var withNameAndComparer = Observable.CreateExpression(() => 1, "total", comparer);

        StringAssert.Contains(withComparerOnly.ToString(), "ExpressionTests.cs:", "a comparer is not a name");
        Assert.AreEqual("total", withNameAndComparer.ToString(), "and a name is one");
    }

    [TestMethod]
    public void CreatingAnExpressionWithoutAFunctionIsRefused()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => Observable.CreateExpression<int>(null!, "name"),
            "a name does not make a missing function usable");
    }
}
