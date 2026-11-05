using SkiaSharp;
using XPlatUI;
using XPlatUI.Backend.Headless;
using XPlatUI.DragDrop;

namespace XPlatUI.DragDrop.Tests;

/// <summary>
/// The two public seams the feature adds to a control and to a window: whether a control accepts a
/// drop, and the window hook that installs and releases the router.
/// </summary>
/// <remarks>
/// <para>
/// The routing itself - finding the control under the pointer, walking up to the nearest ancestor
/// that accepts drops, and clamping the effect to what the source allows - lives in
/// <c>XPlatDragDropRouter</c> and <c>XPlatDragDropState</c>, which are internal and are driven by the
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
        var label = new XPlatLabel();

        Assert.IsFalse(label.AllowDrop, "accepting drops is opt-in");

        label.AllowDrop = true;
        Assert.IsTrue(label.AllowDrop);
    }

    [TestMethod]
    public void EachControlCarriesItsOwnAnswer()
    {
        var parent = new XPlatPanel();
        var child = new XPlatLabel();
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
        var panel = new XPlatPanel();

        Assert.ThrowsExactly<ArgumentNullException>(() => panel.AddDragEnterHandler(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => panel.AddDragOverHandler(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => panel.AddDragLeaveHandler(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => panel.AddDragDropHandler(null!));
        Assert.IsFalse(panel.AllowDrop, "asking to be told about a drag is not the same as accepting one");
    }

    [TestMethod]
    public void TheArgumentsCarryWhatTheDragReported()
    {
        var data = XPlatDataObject.FromText("payload");

        var args = new XPlatDragEventArgs(
            data,
            new SKPoint(12f, 34f),
            XPlatDragDropEffects.Copy | XPlatDragDropEffects.Move,
            XPlatModifiers.Shift);

        Assert.AreSame(data, args.Data);
        Assert.AreEqual(new SKPoint(12f, 34f), args.Location);
        Assert.AreEqual(XPlatModifiers.Shift, args.Modifiers);
        Assert.AreEqual(XPlatDragDropEffects.Copy | XPlatDragDropEffects.Move, args.AllowedEffects);
    }

    [TestMethod]
    public void TheEffectStartsAsTheMostUsefulOneTheSourceAllows()
    {
        var data = new XPlatDataObject();

        Assert.AreEqual(
            XPlatDragDropEffects.Copy,
            new XPlatDragEventArgs(data, default, XPlatDragDropEffects.Copy | XPlatDragDropEffects.Move).Effect,
            "a copy is offered ahead of a move when both are allowed");

        Assert.AreEqual(
            XPlatDragDropEffects.Move,
            new XPlatDragEventArgs(data, default, XPlatDragDropEffects.Move | XPlatDragDropEffects.Link).Effect);

        Assert.AreEqual(
            XPlatDragDropEffects.Link,
            new XPlatDragEventArgs(data, default, XPlatDragDropEffects.Link).Effect);

        Assert.AreEqual(
            XPlatDragDropEffects.None,
            new XPlatDragEventArgs(data, default, XPlatDragDropEffects.None).Effect,
            "a source that allows nothing gives a target nothing to do");
    }

    [TestMethod]
    public void AHandlerCanNarrowTheEffectOrRefuseTheDrop()
    {
        var data = new XPlatDataObject();
        var args = new XPlatDragEventArgs(data, default, XPlatDragDropEffects.Copy | XPlatDragDropEffects.Move);

        args.Effect = XPlatDragDropEffects.Move;
        Assert.AreEqual(XPlatDragDropEffects.Move, args.Effect);

        args.Effect = XPlatDragDropEffects.None;
        Assert.AreEqual(XPlatDragDropEffects.None, args.Effect, "None is how a target refuses the drop");
    }

    [TestMethod]
    public void TheArgumentsRejectANullPayload()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => _ = new XPlatDragEventArgs(null!, default, XPlatDragDropEffects.Copy));
    }
}
