namespace Kolvir.Observables.Tests;

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
        var expression = Observable.CreateExpression(() => value.Value * 2);

        var run = Observable.CollectDependencies(() =>
        {
            _ = expression.Value;
        });

        Assert.AreEqual(1, run.Dependencies.Count, "one dependency, not two");
        Assert.AreSame(expression, run.Dependencies[0], "and it is the expression, not what it reads");
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
