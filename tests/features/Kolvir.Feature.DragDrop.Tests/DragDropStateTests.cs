using SkiaSharp;
using Kolvir;
using Kolvir.DragDrop;

namespace Kolvir.DragDrop.Tests;

/// <summary>
/// The per-control state the feature keeps beside a control rather than in it, and the rule that a
/// disabled control is never told about a drag.
/// </summary>
/// <remarks>
/// The table is weak-keyed and created on first use, which is what makes the feature cost nothing for
/// a control that never touches it; and every raise is guarded on <see cref="Control.Enabled"/>,
/// which is the last line of defence for a control that became disabled mid-drag.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("DragDrop")]
public sealed class DragDropStateTests
{
    [TestMethod]
    public void StateIsCreatedOnceAndOnlyOnDemand()
    {
        var control = new Label();

        Assert.IsNull(DragDropState.Find(control), "a control that never touched the feature carries nothing");

        var state = DragDropState.Get(control);

        Assert.IsNotNull(state);
        Assert.AreSame(state, DragDropState.Get(control), "the state belongs to the control");
        Assert.AreNotSame(state, DragDropState.Get(new Label()), "and not to any other control");
        Assert.AreSame(state, DragDropState.Find(control));
    }

    [TestMethod]
    public void ADisabledControlIsNeverToldAboutADrag()
    {
        var control = new Label { Enabled = false };
        var state = DragDropState.Get(control);
        var data = new DataObject();
        var args = new DragEventArgs(data, default, DragDropEffects.Copy);

        var recorded = new List<string>();
        state.AddDragEnter((_, _) => recorded.Add("enter"));
        state.AddDragOver((_, _) => recorded.Add("over"));
        state.AddDragLeave(_ => recorded.Add("leave"));
        state.AddDragDrop((_, _) => recorded.Add("drop"));

        state.RaiseDragEnter(control, args);
        state.RaiseDragOver(control, args);
        state.RaiseDragLeave(control);
        state.RaiseDragDrop(control, args);

        Assert.AreEqual(0, recorded.Count, "a control that cannot be interacted with hears nothing");

        control.Enabled = true;

        state.RaiseDragEnter(control, args);
        state.RaiseDragOver(control, args);
        state.RaiseDragLeave(control);
        state.RaiseDragDrop(control, args);

        CollectionAssert.AreEqual(new[] { "enter", "over", "leave", "drop" }, recorded.ToArray());
    }

    [TestMethod]
    public void RemovingAHandlerStopsItBeingTold()
    {
        var control = new Label();
        var state = DragDropState.Get(control);
        var args = new DragEventArgs(new DataObject(), default, DragDropEffects.Copy);

        int enters = 0;
        Action<object, DragEventArgs> handler = (_, _) => enters++;

        state.AddDragEnter(handler);
        state.RaiseDragEnter(control, args);
        Assert.AreEqual(1, enters);

        state.RemoveDragEnter(handler);
        state.RaiseDragEnter(control, args);
        Assert.AreEqual(1, enters, "a removed handler is not called");

        state.RemoveDragEnter(handler);
        Assert.AreEqual(1, enters, "removing it twice is harmless");
    }

    [TestMethod]
    public void HandlersSeeTheControlAndTheArgumentsTheRouterBuilt()
    {
        var control = new Label();
        var state = DragDropState.Get(control);
        var data = new DataObject();
        var args = new DragEventArgs(data, new SKPoint(3f, 4f), DragDropEffects.Move);

        object? sender = null;
        DragEventArgs? seen = null;
        state.AddDragOver((s, e) =>
        {
            sender = s;
            seen = e;
        });

        state.RaiseDragOver(control, args);

        Assert.AreSame(control, sender);
        Assert.AreSame(args, seen, "the state passes the arguments straight through");
        Assert.AreEqual(new SKPoint(3f, 4f), seen!.Location);
    }
}
