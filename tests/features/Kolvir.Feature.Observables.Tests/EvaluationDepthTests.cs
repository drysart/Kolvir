namespace Kolvir.Feature.Observables.Tests;

/// <summary>
/// The one limit on a runaway graph: raises and cold reads nest, both are counted in one depth, and the
/// step past the limit is refused instead of exhausting the stack.
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class EvaluationDepthTests
{
    [TestMethod]
    public void TheLimitIsFiftyByDefaultAndRefusesLessThanOne()
    {
        Assert.AreEqual(50, Observable.MaxEventStackSize, "fifty by default");

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Observable.MaxEventStackSize = 0,
            "a limit that no raise can satisfy is a mistake, not a setting");

        Assert.AreEqual(50, Observable.MaxEventStackSize, "and a refused change leaves it alone");
    }

    [TestMethod]
    public void AnEndlessChainOfRaisesIsRefusedAtTheLimit()
    {
        // A handler that writes the value it is a handler of: the write raises, which reaches the handler,
        // which writes. Nothing in the graph describes the loop - it is one write away - so the raise chain
        // is where it is caught, one raise at a time, until the nesting is too deep to continue.
        var value = Observable.CreateValue(0);
        value.AddObservableEventRaisedHandler(_ => value.Value++);

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(
            () => value.Value = 1,
            "the loop is refused rather than left to exhaust the stack");

        StringAssert.Contains(thrown.Message, "MaxEventStackSize", "and the limit is named");

        Assert.AreEqual(
            Observable.MaxEventStackSize + 1,
            value.Value,
            "every write in the chain up to the refused raise landed, and the one past it did not");
    }

    [TestMethod]
    public void RaisingTheLimitLetsTheSameChainGoDeeper()
    {
        // The limit is a setting because a legitimate chain can be deeper than one number is comfortable
        // guessing at: a change that settles a handful of dependent expressions nests once per link.
        var looping = true;
        var value = Observable.CreateValue(0);
        value.AddObservableEventRaisedHandler(_ => { if (looping) { value.Value++; } });

        try
        {
            Observable.MaxEventStackSize = 3;

            Assert.ThrowsExactly<InvalidOperationException>(
                () => value.Value = 1,
                "a low limit refuses the chain sooner");

            Assert.AreEqual(4, value.Value, "at three levels of nesting");

            Observable.MaxEventStackSize = 6;

            Assert.ThrowsExactly<InvalidOperationException>(
                () => value.Value = 1,
                "and the same chain is refused later");

            Assert.AreEqual(7, value.Value, "at six, which is as far as the new limit allows");
        }
        finally
        {
            Observable.MaxEventStackSize = 50;
        }
    }

    [TestMethod]
    public void TheDepthIsRestoredSoTheNextRaiseStartsOver()
    {
        // A depth left behind by a refused chain would refuse everything after it - the first raise of the
        // next change would look like the fifty-first of the last one.
        var looping = true;
        var value = Observable.CreateValue(0);
        value.AddObservableEventRaisedHandler(_ => { if (looping) { value.Value++; } });

        Assert.ThrowsExactly<InvalidOperationException>(() => value.Value = 1);

        looping = false;

        var raised = 0;
        var other = Observable.CreateValue(0);
        other.AddObservableEventRaisedHandler(_ => raised++);

        other.Value = 1;
        other.Value = 2;

        Assert.AreEqual(2, raised, "ordinary raises work again, counted from zero");
        Assert.AreEqual(2, other.Value, "with their writes landing");
    }

    [TestMethod]
    public void AColdReadOfAnExpressionThatReadsItselfIsRefused()
    {
        // The loop the raise depth cannot see: nothing is subscribed, so a read is a plain run, and a
        // function that reads its own expression runs itself until the stack dies. Counting the run in
        // the same depth as a raise is what turns that crash into a message.
        IObservableValue<int>? self = null;
        self = Observable.CreateExpression(() => self!.Value + 1);

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(
            () => _ = self!.Value,
            "the run is refused instead of recursing");

        StringAssert.Contains(thrown.Message, "cold read", "and names the step that was refused");
        StringAssert.Contains(thrown.Message, "MaxEventStackSize", "and the limit");
    }

    [TestMethod]
    public void AColdReadOfTwoExpressionsThatReadEachOtherIsRefused()
    {
        // The same loop with no expression on it twice in a row, so nothing could notice by name.
        IObservableValue<int>? first = null;
        IObservableValue<int>? second = null;
        first = Observable.CreateExpression(() => second!.Value + 1);
        second = Observable.CreateExpression(() => first!.Value + 1);

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(
            () => _ = first!.Value,
            "two expressions reading each other are refused as well");

        StringAssert.Contains(thrown.Message, "MaxEventStackSize", "with the limit named");
    }

    [TestMethod]
    public void ALegitimateChainOfColdReadsCountsAgainstTheSameLimit()
    {
        // Not a loop, just depth: three derived values read from the top nest three deep, because a cold
        // read of each runs it. The limit covers this chain and a chain of raises with one number, which
        // is what makes a chain that alternates between them bounded too.
        var leaf = Observable.CreateValue(1);
        var first = Observable.CreateExpression(() => leaf.Value + 1);
        var second = Observable.CreateExpression(() => first.Value + 1);
        var third = Observable.CreateExpression(() => second.Value + 1);

        try
        {
            Observable.MaxEventStackSize = 2;

            Assert.ThrowsExactly<InvalidOperationException>(
                () => _ = third.Value,
                "a limit below the chain's depth refuses it, exactly as it would a chain of raises");

            Observable.MaxEventStackSize = 50;

            Assert.AreEqual(4, third.Value, "and the same chain is fine under the default");
        }
        finally
        {
            Observable.MaxEventStackSize = 50;
        }
    }

    [TestMethod]
    public void TheDepthGoesBackToZeroAfterARefusedColdRead()
    {
        // Same requirement as for a raise: a refusal that left the depth or the overrun behind would
        // refuse the next read in the application.
        IObservableValue<int>? self = null;
        self = Observable.CreateExpression(() => self!.Value + 1);

        Assert.ThrowsExactly<InvalidOperationException>(() => _ = self!.Value);

        var leaf = Observable.CreateValue(2);

        Assert.AreEqual(3, Observable.CreateExpression(() => leaf.Value + 1).Value, "an ordinary read works");
    }

    [TestMethod]
    public void ARaiseWithNoHandlersIsNotCountedAtAll()
    {
        // Nothing to call means nothing to recurse into, so a raise nobody is listening to cannot be part
        // of a chain - and the common case of disposing an unwatched value stays free.
        var value = Observable.CreateValue(0);

        try
        {
            Observable.MaxEventStackSize = 1;

            value.Value = 1;
            value.Value = 2;

            Assert.AreEqual(2, value.Value, "a value with no handlers raises and writes as usual");
        }
        finally
        {
            Observable.MaxEventStackSize = 50;
        }
    }
}
