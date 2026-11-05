using SkiaSharp;
using XPlatUI;
using XPlatUI.DragDrop;

namespace XPlatUI.DragDrop.Tests;

/// <summary>
/// The per-control state the feature keeps beside a control rather than in it, and the rule that a
/// disabled control is never told about a drag.
/// </summary>
/// <remarks>
/// The table is weak-keyed and created on first use, which is what makes the feature cost nothing for
/// a control that never touches it; and every raise is guarded on <see cref="XPlatControl.Enabled"/>,
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
        var control = new XPlatLabel();

        Assert.IsNull(XPlatDragDropState.Find(control), "a control that never touched the feature carries nothing");

        var state = XPlatDragDropState.Get(control);

        Assert.IsNotNull(state);
        Assert.AreSame(state, XPlatDragDropState.Get(control), "the state belongs to the control");
        Assert.AreNotSame(state, XPlatDragDropState.Get(new XPlatLabel()), "and not to any other control");
        Assert.AreSame(state, XPlatDragDropState.Find(control));
    }

    [TestMethod]
    public void ADisabledControlIsNeverToldAboutADrag()
    {
        var control = new XPlatLabel { Enabled = false };
        var state = XPlatDragDropState.Get(control);
        var data = new XPlatDataObject();
        var args = new XPlatDragEventArgs(data, default, XPlatDragDropEffects.Copy);

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
        var control = new XPlatLabel();
        var state = XPlatDragDropState.Get(control);
        var args = new XPlatDragEventArgs(new XPlatDataObject(), default, XPlatDragDropEffects.Copy);

        int enters = 0;
        Action<object, XPlatDragEventArgs> handler = (_, _) => enters++;

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
        var control = new XPlatLabel();
        var state = XPlatDragDropState.Get(control);
        var data = new XPlatDataObject();
        var args = new XPlatDragEventArgs(data, new SKPoint(3f, 4f), XPlatDragDropEffects.Move);

        object? sender = null;
        XPlatDragEventArgs? seen = null;
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
