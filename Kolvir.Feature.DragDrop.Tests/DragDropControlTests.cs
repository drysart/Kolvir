using SkiaSharp;
using Kolvir;
using Kolvir.Backend.Headless;
using Kolvir.DragDrop;

namespace Kolvir.DragDrop.Tests;

/// <summary>
/// The two public seams the feature adds to a control and to a window: whether a control accepts a
/// drop, and the window hook that installs and releases the router.
/// </summary>
/// <remarks>
/// <para>
/// The routing itself - finding the control under the pointer, walking up to the nearest ancestor
/// that accepts drops, and clamping the effect to what the source allows - lives in
/// <c>DragDropRouter</c> and <c>DragDropState</c>, which are internal and are driven by the
/// platform hook. These tests therefore cover the surface an application actually touches, and the
/// router's own behaviour is left to the platform runs.
/// </para>
/// <para>
/// A window needs an application, and the library allows one per process, so the window tests build
/// and dispose one around the test.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("DragDrop")]
public sealed class DragDropControlTests
{
    [TestMethod]
    public void AControlDoesNotAcceptDropsUntilItIsToldTo()
    {
        var label = new Label();

        Assert.IsFalse(label.AllowDrop, "accepting drops is opt-in");

        label.AllowDrop = true;
        Assert.IsTrue(label.AllowDrop);
    }

    [TestMethod]
    public void EachControlCarriesItsOwnAnswer()
    {
        var parent = new Panel();
        var child = new Label();
        parent.Controls.Add(child);

        parent.AllowDrop = true;

        Assert.IsTrue(parent.AllowDrop);
        Assert.IsFalse(child.AllowDrop, "a panel that accepts a drop does not make every control in it accept one");

        child.AllowDrop = true;
        parent.AllowDrop = false;

        Assert.IsFalse(parent.AllowDrop);
        Assert.IsTrue(child.AllowDrop);
    }

    [TestMethod]
    public void AddingAHandlerRejectsNullAndDoesNotTurnDropsOn()
    {
        var panel = new Panel();

        Assert.ThrowsExactly<ArgumentNullException>(() => panel.AddDragEnterHandler(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => panel.AddDragOverHandler(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => panel.AddDragLeaveHandler(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => panel.AddDragDropHandler(null!));
        Assert.IsFalse(panel.AllowDrop, "asking to be told about a drag is not the same as accepting one");
    }

    [TestMethod]
    public void TheArgumentsCarryWhatTheDragReported()
    {
        var data = DataObject.FromText("payload");

        var args = new DragEventArgs(
            data,
            new SKPoint(12f, 34f),
            DragDropEffects.Copy | DragDropEffects.Move,
            Modifiers.Shift);

        Assert.AreSame(data, args.Data);
        Assert.AreEqual(new SKPoint(12f, 34f), args.Location);
        Assert.AreEqual(Modifiers.Shift, args.Modifiers);
        Assert.AreEqual(DragDropEffects.Copy | DragDropEffects.Move, args.AllowedEffects);
    }

    [TestMethod]
    public void TheEffectStartsAsTheMostUsefulOneTheSourceAllows()
    {
        var data = new DataObject();

        Assert.AreEqual(
            DragDropEffects.Copy,
            new DragEventArgs(data, default, DragDropEffects.Copy | DragDropEffects.Move).Effect,
            "a copy is offered ahead of a move when both are allowed");

        Assert.AreEqual(
            DragDropEffects.Move,
            new DragEventArgs(data, default, DragDropEffects.Move | DragDropEffects.Link).Effect);

        Assert.AreEqual(
            DragDropEffects.Link,
            new DragEventArgs(data, default, DragDropEffects.Link).Effect);

        Assert.AreEqual(
            DragDropEffects.None,
            new DragEventArgs(data, default, DragDropEffects.None).Effect,
            "a source that allows nothing gives a target nothing to do");
    }

    [TestMethod]
    public void AHandlerCanNarrowTheEffectOrRefuseTheDrop()
    {
        var data = new DataObject();
        var args = new DragEventArgs(data, default, DragDropEffects.Copy | DragDropEffects.Move);

        args.Effect = DragDropEffects.Move;
        Assert.AreEqual(DragDropEffects.Move, args.Effect);

        args.Effect = DragDropEffects.None;
        Assert.AreEqual(DragDropEffects.None, args.Effect, "None is how a target refuses the drop");
    }

    [TestMethod]
    public void TheArgumentsRejectANullPayload()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => _ = new DragEventArgs(null!, default, DragDropEffects.Copy));
    }
}
