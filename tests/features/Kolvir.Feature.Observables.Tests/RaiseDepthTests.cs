namespace Kolvir.Observables.Tests;

/// <summary>
/// The one limit on a runaway graph: raises nest, a chain of them is counted, and the raise past the
/// limit is refused instead of exhausting the stack.
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class RaiseDepthTests
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
