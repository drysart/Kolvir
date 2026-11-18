namespace Kolvir.Observables.Tests;

/// <summary>
/// An observable value: it raises only when it actually changes, its raise walks a snapshot so a
/// handler may subscribe and unsubscribe freely, and disposal is ordered.
/// </summary>
[TestClass]
[TestCategory("Observables")]
public sealed class ObservableValueTests
{
    [TestMethod]
    public void CreateValueStartsAtTheInitialValue()
    {
        var value = Observable.CreateValue(42);

        Assert.AreEqual(42, value.Value, "the value is what it was created with");
        Assert.IsFalse(value.IsDisposed, "and a new value is alive");
    }

    [TestMethod]
    public void WritingADifferentValueRaisesOnceAndKeepsIt()
    {
        var value = Observable.CreateValue(1);
        var raised = 0;
        value.AddObservableEventRaisedHandler(_ => raised++);

        value.Value = 2;

        Assert.AreEqual(1, raised, "one write is one event");
        Assert.AreEqual(2, value.Value, "and the write happened");
    }

    [TestMethod]
    public void WritingAnEqualValueRaisesNothing()
    {
        // An event means something a listener cares about is different, so a model that recomputes the
        // same answer wakes nothing up. Equality is the type's own, so this holds for a record too.
        var value = Observable.CreateValue(new Text("hello"));
        var raised = 0;
        value.AddObservableEventRaisedHandler(_ => raised++);

        value.Value = new Text("hello");

        Assert.AreEqual(0, raised, "an equal value is not a change");
    }

    [TestMethod]
    public void TheHandlerIsHandedTheSourceThatRaised()
    {
        var value = Observable.CreateValue(1);
        IObservableEventSource? raisedBy = null;
        value.AddObservableEventRaisedHandler(source => raisedBy = source);

        value.Value = 2;

        Assert.AreSame(value, raisedBy, "so one handler can serve several sources and know which changed");
    }

    [TestMethod]
    public void AHandlerCanUnsubscribeItselfDuringARaise()
    {
        // The snapshot is what makes this safe: the raise is walking the list it began with, so a
        // mutation from inside a handler can only take effect on the next raise.
        var value = Observable.CreateValue(0);
        var calls = 0;
        IDisposable? token = null;
        token = value.AddObservableEventRaisedHandler(_ =>
        {
            calls++;
            token!.Dispose();
        });

        value.Value = 1;
        value.Value = 2;

        Assert.AreEqual(1, calls, "the handler removed itself, so the second write did not reach it");
    }

    [TestMethod]
    public void AHandlerCanUnsubscribeAnotherHandlerDuringARaise()
    {
        var value = Observable.CreateValue(0);
        var second = 0;
        var secondToken = value.AddObservableEventRaisedHandler(_ => second++);
        value.AddObservableEventRaisedHandler(_ => secondToken.Dispose());

        value.Value = 1;

        Assert.AreEqual(1, second, "the handler was already in the array being walked, so it still ran");

        value.Value = 2;

        Assert.AreEqual(1, second, "and from the next raise on, it is gone");
    }

    [TestMethod]
    public void AHandlerAddedDuringARaiseRunsOnTheNextOne()
    {
        var value = Observable.CreateValue(0);
        var added = 0;
        var addedOnce = false;
        value.AddObservableEventRaisedHandler(_ =>
        {
            if (addedOnce)
            {
                return;
            }

            addedOnce = true;
            value.AddObservableEventRaisedHandler(_ => added++);
        });

        value.Value = 1;

        Assert.AreEqual(0, added, "a handler added mid-raise is not part of that raise");

        value.Value = 2;

        Assert.AreEqual(1, added, "but it is part of the next one");
    }

    [TestMethod]
    public void AThrowingHandlerEndsTheNotificationAndPropagates()
    {
        // Nothing is caught: a raise is a synchronous callback, so it behaves like a multicast delegate.
        // The consequence to know about is that the handlers behind the throwing one do not run.
        var value = Observable.CreateValue(0);
        var after = 0;
        value.AddObservableEventRaisedHandler(_ => throw new InvalidOperationException("boom"));
        value.AddObservableEventRaisedHandler(_ => after++);

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(
            () => value.Value = 1,
            "the exception travels out of the write to whoever caused it");

        Assert.AreEqual("boom", thrown.Message, "unwrapped, with the original stack");
        Assert.AreEqual(0, after, "and the handlers behind it did not run for that change");
        Assert.AreEqual(1, value.Value, "the write itself happened - the event is what did not finish");
    }

    [TestMethod]
    public void DependUponOutsideACollectionScopeDoesNothing()
    {
        // The cost of every ordinary property read in an application that never uses an expression: a
        // null check on a static field.
        var value = Observable.CreateValue(1);
        var raised = 0;
        value.AddObservableEventRaisedHandler(_ => raised++);

        IObservableEventSource source = value;
        source.DependUpon();

        Assert.AreEqual(0, raised, "reading a value is not a change to it");
    }

    [TestMethod]
    public void DisposeRaisesOnceToTheHandlersThatAreStillThere()
    {
        var value = Observable.CreateValue(1);
        var calls = 0;
        var disposedWhenCalled = false;
        value.AddObservableEventRaisedHandler(_ =>
        {
            calls++;
            disposedWhenCalled = value.IsDisposed;
        });

        value.Dispose();

        Assert.AreEqual(1, calls, "a registrant is told its dependency is gone");
        Assert.IsTrue(disposedWhenCalled, "and it is told after the value is already dead, not before");
    }

    [TestMethod]
    public void AHandlerCannotReadTheValueDuringTheFinalRaise()
    {
        // dead first, then the raise: handing back a value that will never update again is the kind of
        // "still working" lie a lifetime bug hides behind.
        var value = Observable.CreateValue(1);
        Exception? readFailure = null;
        var readDuringFinalRaise = -1;
        value.AddObservableEventRaisedHandler(_ =>
        {
            try
            {
                readDuringFinalRaise = value.Value;
            }
            catch (Exception exception)
            {
                readFailure = exception;
            }
        });

        value.Dispose();

        Assert.IsInstanceOfType<ObjectDisposedException>(readFailure, "reading a disposed value throws");
        Assert.AreEqual(-1, readDuringFinalRaise, "and no last value came back instead");
    }

    [TestMethod]
    public void DisposeIsIdempotent()
    {
        var value = Observable.CreateValue(1);
        var calls = 0;
        value.AddObservableEventRaisedHandler(_ => calls++);

        value.Dispose();
        value.Dispose();

        Assert.AreEqual(1, calls, "the second dispose is not a second notification");
    }

    [TestMethod]
    public void AHandlerThatDisposesTheValueDoesNotShortenTheRaise()
    {
        // The remaining handlers are already in the snapshot being walked, so the walk finishes - and
        // the dispose raises once itself, which is why the second handler is told twice.
        var value = Observable.CreateValue(0);
        var calls = 0;
        value.AddObservableEventRaisedHandler(_ => value.Dispose());
        value.AddObservableEventRaisedHandler(_ => calls++);

        value.Value = 1;

        Assert.AreEqual(2, calls, "once for the change being raised, and once for the disposal");
        Assert.IsTrue(value.IsDisposed, "and the value is gone");
    }

    [TestMethod]
    public void EveryUseAfterDisposalThrowsExceptUnregistering()
    {
        var value = Observable.CreateValue(1);
        var token = value.AddObservableEventRaisedHandler(_ => { });

        value.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = value.Value, "reading throws");
        Assert.ThrowsExactly<ObjectDisposedException>(() => value.Value = 2, "writing throws");
        Assert.ThrowsExactly<ObjectDisposedException>(() => value.RaiseEvent(), "raising throws");
        Assert.ThrowsExactly<ObjectDisposedException>(
            () => value.AddObservableEventRaisedHandler(_ => { }),
            "registering throws");

        token.Dispose();

        Assert.IsTrue(value.IsDisposed, "a token can be released after the value it points at, which is "
            + "the shape every teardown path has");
    }

    [TestMethod]
    public void RemovingAHandlerThatIsNotRegisteredIsNotAnError()
    {
        var value = Observable.CreateValue(1);

        value.RemoveObservableEventRaisedHandler(_ => { });

        Assert.IsFalse(value.IsDisposed, "an unregistered handler is nothing to complain about");
    }

    [TestMethod]
    public void AHandlerRegisteredTwiceNeedsBothTokensReleased()
    {
        var value = Observable.CreateValue(0);
        var calls = 0;
        Action<IObservableEventSource> handler = _ => calls++;
        var first = value.AddObservableEventRaisedHandler(handler);
        var second = value.AddObservableEventRaisedHandler(handler);

        first.Dispose();
        value.Value = 1;

        Assert.AreEqual(1, calls, "registration is a subscription, so the second one is still live");

        second.Dispose();
        value.Value = 2;

        Assert.AreEqual(1, calls, "and the second token released it");
    }

    /// <summary>A record, so that equality is value equality and the write is a different instance.</summary>
    private sealed record Text(string Value);
}
