using SkiaSharp;
using XPlatUI;
using XPlatUI.DragDrop;

namespace XPlatUI.DragDrop.Tests;

/// <summary>
/// The router: which control a drag reaches, what it is told and when, and what the target settles
/// on.
/// </summary>
/// <remarks>
/// <para>
/// These tests drive <see cref="XPlatDragDropRouter"/> directly. It is internal, and deliberately
/// so - it is feature code rather than shipped public surface - but the behaviour it carries is real
/// behaviour that regresses silently if nothing watches it: the control under the pointer, the walk
/// up to the nearest ancestor that accepts drops, the sequencing of enter, over and leave, and the
/// clamp of the effect to what the source allows.
/// </para>
/// <para>
/// Every test builds a real tree on the headless backend, because hit-testing is geometry: the
/// pointer is aimed with host coordinates taken from the laid-out controls, not with numbers that
/// assume a layout.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("DragDrop")]
[TestCategory("Headless")]
public sealed class DragDropRoutingTests
{
    private static readonly XPlatDragDropEffects Both =
        XPlatDragDropEffects.Copy | XPlatDragDropEffects.Move;

    /// <summary>Records what a control was told, so ordering can be asserted.</summary>
    private sealed class Recorder
    {
        public List<string> Events { get; } = [];

        public List<(XPlatControl Control, XPlatDragEventArgs Args)> Enters { get; } = [];

        public Recorder Track(string name, XPlatControl control)
        {
            control.AddDragEnterHandler((sender, e) =>
            {
                Events.Add($"{name}:enter");
                Enters.Add(((XPlatControl)sender, e));
            });

            control.AddDragOverHandler((_, _) => Events.Add($"{name}:over"));
            control.AddDragLeaveHandler(_ => Events.Add($"{name}:leave"));

            return this;
        }
    }

    private static XPlatPanel Panel(float x, float y, float width, float height, bool allowDrop = true) =>
        new()
        {
            Location = new SKPoint(x, y),
            Size = new SKSize(width, height),
            AllowDrop = allowDrop,
        };

    [TestMethod]
    public void AWindowHandsOutOneRouterHoweverOftenItIsAsked()
    {
        using var scene = WindowScene.Create(Panel(0f, 0f, 300f, 200f));

        // A second window in the same application: the library allows one application per process.
        var other = new XPlatWindow("other", Panel(0f, 0f, 300f, 200f));

        var router = XPlatDragDropRouter.For(scene.Window);

        Assert.AreSame(router, XPlatDragDropRouter.For(scene.Window), "the router belongs to the window");
        Assert.AreNotSame(router, XPlatDragDropRouter.For(other), "and to nothing else");
    }

    [TestMethod]
    public void TheHookInstallsTheRouterThePlatformHalfThenFinds()
    {
        using var scene = WindowScene.Create(Panel(0f, 0f, 300f, 200f));
        var hook = new XPlatDragDropWindowHook();

        hook.OnPhase(XPlatWindowPhase.Created, scene.Window);
        var installed = XPlatDragDropRouter.For(scene.Window);

        Assert.AreSame(installed, XPlatDragDropRouter.For(scene.Window));

        hook.OnPhase(XPlatWindowPhase.Closing, scene.Window);

        Assert.AreNotSame(
            installed,
            XPlatDragDropRouter.For(scene.Window),
            "releasing a window drops its router, so a later drag starts from nothing");
    }

    [TestMethod]
    public void AWindowWithNoDragInProgressDeliversNoLeave()
    {
        using var scene = WindowScene.Create(Panel(0f, 0f, 300f, 200f));
        var root = (XPlatPanel)scene.Window.Content!;
        var recorder = new Recorder().Track("root", root);

        var router = XPlatDragDropRouter.For(scene.Window);
        router.DragLeave();

        Assert.AreEqual(0, recorder.Events.Count, "nothing was routed, so nothing is told it left");
    }

    [TestMethod]
    public void TheControlUnderThePointerGetsTheDragInItsOwnCoordinates()
    {
        using var scene = WindowScene.Create(Panel(0f, 0f, 300f, 200f));
        var root = (XPlatPanel)scene.Window.Content!;
        var target = Panel(40f, 30f, 100f, 60f);
        root.Controls.Add(target);

        var recorder = new Recorder().Track("target", target);
        var router = XPlatDragDropRouter.For(scene.Window);
        var pointer = scene.CenterOf(target);

        var effect = router.DragEnter(pointer, XPlatDataObject.FromText("text"), XPlatDragDropEffects.Copy);

        Assert.AreEqual(XPlatDragDropEffects.Copy, effect, "the only effect the source allows is taken");
        Assert.AreEqual(1, recorder.Enters.Count);
        Assert.AreSame(target, recorder.Enters[0].Control, "the sender is the control the drag is over");
        Assert.AreEqual(
            scene.LocalPointIn(target, pointer),
            recorder.Enters[0].Args.Location,
            "the event carries the pointer in the control's own coordinates");
    }

    [TestMethod]
    public void StayingOnTheSameControlReportsMovementsRatherThanArrivals()
    {
        using var scene = WindowScene.Create(Panel(0f, 0f, 300f, 200f));
        var root = (XPlatPanel)scene.Window.Content!;
        var target = Panel(40f, 30f, 100f, 60f);
        root.Controls.Add(target);

        var recorder = new Recorder().Track("target", target);
        var router = XPlatDragDropRouter.For(scene.Window);
        var pointer = scene.CenterOf(target);
        var data = XPlatDataObject.FromText("text");

        Assert.AreEqual(
            XPlatDragDropEffects.Copy,
            router.DragEnter(pointer, data, XPlatDragDropEffects.Copy),
            "the first report arrives");

        Assert.AreEqual(
            XPlatDragDropEffects.Copy,
            router.DragOver(pointer, data, XPlatDragDropEffects.Copy),
            "a move that stays put keeps the effect it had");

        Assert.AreEqual(
            XPlatDragDropEffects.Copy,
            router.DragOver(pointer, data, XPlatDragDropEffects.Copy));

        Assert.AreEqual(1, recorder.Events.Count(e => e == "target:enter"), "the control is entered once");
        Assert.AreEqual(2, recorder.Events.Count(e => e == "target:over"), "and told about each movement after");
        Assert.AreEqual(
            0,
            recorder.Events.Count(e => e == "target:leave"),
            "a drag that stays over a control has not left it, so no leave is raised");
    }

    [TestMethod]
    public void MovingBetweenControlsEntersTheNewOneBeforeLeavingTheOld()
    {
        using var scene = WindowScene.Create(Panel(0f, 0f, 300f, 200f));
        var root = (XPlatPanel)scene.Window.Content!;
        var left = Panel(0f, 0f, 140f, 100f);
        var right = Panel(160f, 0f, 140f, 100f);
        root.Controls.Add(left);
        root.Controls.Add(right);

        var recorder = new Recorder().Track("left", left).Track("right", right);
        var router = XPlatDragDropRouter.For(scene.Window);
        var data = XPlatDataObject.FromText("text");

        router.DragEnter(scene.CenterOf(left), data, XPlatDragDropEffects.Copy);
        router.DragOver(scene.CenterOf(right), data, XPlatDragDropEffects.Copy);

        CollectionAssert.AreEqual(
            new[] { "left:enter", "right:enter", "left:leave" },
            recorder.Events.ToArray(),
            "the new target is told first, so it can claim the drag before the old one cleans up");
    }

    [TestMethod]
    public void NestedChildrenAreHitThroughTheirParents()
    {
        using var scene = WindowScene.Create(Panel(0f, 0f, 300f, 200f));
        var root = (XPlatPanel)scene.Window.Content!;
        var outer = Panel(20f, 20f, 200f, 140f);
        var inner = Panel(10f, 10f, 160f, 100f, allowDrop: false);
        var label = new XPlatLabel { Location = new SKPoint(5f, 5f), Size = new SKSize(60f, 20f), Text = "x" };
        inner.Controls.Add(label);
        outer.Controls.Add(inner);
        root.Controls.Add(outer);

        var recorder = new Recorder().Track("outer", outer);
        var router = XPlatDragDropRouter.For(scene.Window);
        var pointer = scene.CenterOf(label);

        var effect = router.DragEnter(pointer, XPlatDataObject.FromText("text"), XPlatDragDropEffects.Copy);

        Assert.AreEqual(XPlatDragDropEffects.Copy, effect);
        Assert.AreEqual(1, recorder.Enters.Count, "the nearest ancestor that accepts drops takes the drag");
        Assert.AreSame(outer, recorder.Enters[0].Control);
        Assert.AreEqual(
            scene.LocalPointIn(outer, pointer),
            recorder.Enters[0].Args.Location,
            "and it is told where the pointer is relative to itself, not to the label it covered");
    }

    [TestMethod]
    public void AChildThatAcceptsDropsTakesTheDragFromItsParent()
    {
        using var scene = WindowScene.Create(Panel(0f, 0f, 300f, 200f));
        var root = (XPlatPanel)scene.Window.Content!;
        var parent = Panel(0f, 0f, 200f, 140f);
        var child = Panel(20f, 20f, 100f, 80f);
        parent.Controls.Add(child);
        root.Controls.Add(parent);

        var recorder = new Recorder().Track("parent", parent).Track("child", child);
        var router = XPlatDragDropRouter.For(scene.Window);

        router.DragEnter(scene.CenterOf(child), XPlatDataObject.FromText("text"), XPlatDragDropEffects.Copy);

        CollectionAssert.AreEqual(
            new[] { "child:enter" },
            recorder.Events.ToArray(),
            "the parent is not told about a drag its child took");
    }

    [TestMethod]
    public void AControlThatAcceptsDropsWithNoHandlerTakesTheDragAnyway()
    {
        using var scene = WindowScene.Create(Panel(0f, 0f, 300f, 200f));
        var root = (XPlatPanel)scene.Window.Content!;

        Assert.IsTrue(root.AllowDrop);

        var router = XPlatDragDropRouter.For(scene.Window);
        var pointer = scene.CenterOf(root);

        Assert.AreEqual(
            XPlatDragDropEffects.Copy,
            router.DragEnter(pointer, XPlatDataObject.FromText("text"), XPlatDragDropEffects.Copy),
            "opting in is enough: the drag is accepted at the effect the source offers");
        Assert.AreEqual(
            XPlatDragDropEffects.None,
            router.DragEnter(pointer, XPlatDataObject.FromText("text"), XPlatDragDropEffects.None),
            "a source that allows nothing gets nothing");
    }

    [TestMethod]
    public void AChildThatRefusesThePayloadPassesTheDragToItsParent()
    {
        using var scene = WindowScene.Create(Panel(0f, 0f, 300f, 200f));
        var root = (XPlatPanel)scene.Window.Content!;
        var box = new XPlatTextBox
        {
            Location = new SKPoint(20f, 20f),
            Size = new SKSize(120f, 40f),
            AllowDrop = true,
        };
        root.Controls.Add(box);

        var recorder = new Recorder().Track("root", root);
        var router = XPlatDragDropRouter.For(scene.Window);
        var pointer = scene.CenterOf(box);

        var effect = router.DragEnter(pointer, XPlatDataObject.FromFiles(["C:/one.txt"]), XPlatDragDropEffects.Copy);

        Assert.AreEqual(XPlatDragDropEffects.Copy, effect, "the panel behind the field takes the file");
        Assert.AreEqual(1, recorder.Enters.Count);
        Assert.AreSame(root, recorder.Enters[0].Control);
        Assert.AreEqual(
            0,
            recorder.Events.Count(e => e == "root:leave"),
            "the field it fell through was never the target, so it is not told the drag left");
    }

    [TestMethod]
    public void TheEffectIsClampedToWhatTheSourceAllows()
    {
        using var scene = WindowScene.Create(Panel(0f, 0f, 300f, 200f, allowDrop: false));
        var root = (XPlatPanel)scene.Window.Content!;
        var target = Panel(20f, 20f, 120f, 80f);
        root.Controls.Add(target);

        target.AddDragEnterHandler((_, e) => e.Effect = XPlatDragDropEffects.Move);

        var router = XPlatDragDropRouter.For(scene.Window);
        var pointer = scene.CenterOf(target);
        var data = XPlatDataObject.FromText("text");

        Assert.AreEqual(
            XPlatDragDropEffects.Move,
            router.DragEnter(pointer, data, Both),
            "what the target wants, when the source allows it");

        router.DragLeave();

        Assert.AreEqual(
            XPlatDragDropEffects.None,
            router.DragEnter(pointer, data, XPlatDragDropEffects.Copy),
            "a target that only takes a move refuses a source that only allows a copy");
    }

    [TestMethod]
    public void ADisabledControlIsSkippedAndItsParentTakesTheDrag()
    {
        using var scene = WindowScene.Create(Panel(0f, 0f, 300f, 200f));
        var root = (XPlatPanel)scene.Window.Content!;
        var disabled = Panel(20f, 20f, 120f, 80f);
        root.Controls.Add(disabled);

        var recorder = new Recorder().Track("root", root).Track("disabled", disabled);
        disabled.Enabled = false;

        var router = XPlatDragDropRouter.For(scene.Window);

        Assert.AreEqual(
            XPlatDragDropEffects.Copy,
            router.DragEnter(scene.CenterOf(disabled), XPlatDataObject.FromText("text"), XPlatDragDropEffects.Copy));

        CollectionAssert.AreEqual(
            new[] { "root:enter" },
            recorder.Events.ToArray(),
            "a control that cannot be interacted with never sees the drag");
    }

    [TestMethod]
    public void AnInvisibleControlIsSkippedAndItsParentTakesTheDrag()
    {
        using var scene = WindowScene.Create(Panel(0f, 0f, 300f, 200f));
        var root = (XPlatPanel)scene.Window.Content!;
        var hidden = Panel(20f, 20f, 120f, 80f);
        root.Controls.Add(hidden);

        var recorder = new Recorder().Track("root", root).Track("hidden", hidden);
        hidden.Visible = false;

        var router = XPlatDragDropRouter.For(scene.Window);

        router.DragEnter(scene.CenterOf(hidden), XPlatDataObject.FromText("text"), XPlatDragDropEffects.Copy);

        CollectionAssert.AreEqual(new[] { "root:enter" }, recorder.Events.ToArray());
    }

    [TestMethod]
    public void ADropDeliversThePayloadAndClearsTheTargetWithoutALeave()
    {
        using var scene = WindowScene.Create(Panel(0f, 0f, 300f, 200f));
        var root = (XPlatPanel)scene.Window.Content!;
        var target = Panel(20f, 20f, 120f, 80f);
        root.Controls.Add(target);

        var recorder = new Recorder().Track("target", target);
        XPlatDragEventArgs? dropped = null;
        target.AddDragDropHandler((_, e) => dropped = e);

        var router = XPlatDragDropRouter.For(scene.Window);
        var pointer = scene.CenterOf(target);
        var data = XPlatDataObject.FromText("payload");

        router.DragEnter(pointer, data, Both);

        var effect = router.DragDrop(pointer, data, Both);

        Assert.IsNotNull(dropped, "the drop handler runs");
        Assert.AreSame(data, dropped.Data, "with the payload");
        Assert.AreEqual(XPlatDragDropEffects.Copy, effect);
        CollectionAssert.AreEqual(
            new[] { "target:enter", "target:over" },
            recorder.Events.ToArray(),
            "a drop is not a leave either: the target is entered, told the drag moved, and then told it landed");

        router.DragOver(pointer, data, Both);

        Assert.AreEqual(
            2,
            recorder.Events.Count(e => e == "target:enter"),
            "the drop cleared the target, so the next drag arrives again rather than moving");
        Assert.AreEqual(
            0,
            recorder.Events.Count(e => e == "target:leave"),
            "and in none of that was the control told the drag left it");
    }

    [TestMethod]
    public void ACancelRaisesLeaveOnceForTheControlTheDragWasOver()
    {
        using var scene = WindowScene.Create(Panel(0f, 0f, 300f, 200f));
        var root = (XPlatPanel)scene.Window.Content!;
        var target = Panel(20f, 20f, 120f, 80f);
        root.Controls.Add(target);

        var recorder = new Recorder().Track("target", target);
        var router = XPlatDragDropRouter.For(scene.Window);

        router.DragEnter(scene.CenterOf(target), XPlatDataObject.FromText("text"), XPlatDragDropEffects.Copy);
        router.DragLeave();
        router.DragLeave();

        CollectionAssert.AreEqual(
            new[] { "target:enter", "target:leave" },
            recorder.Events.ToArray(),
            "a cancelled drag is announced once, however often the platform reports it");
    }

    [TestMethod]
    public void DroppingOverNothingDoesNothing()
    {
        using var scene = WindowScene.Create(Panel(0f, 0f, 300f, 200f));
        var root = (XPlatPanel)scene.Window.Content!;
        root.AllowDrop = false;

        var router = XPlatDragDropRouter.For(scene.Window);

        Assert.AreEqual(
            XPlatDragDropEffects.None,
            router.DragDrop(scene.CenterOf(root), XPlatDataObject.FromText("text"), XPlatDragDropEffects.Copy),
            "no control accepted the drag, so the drop does nothing");
    }

    [TestMethod]
    public void DroppingTextOnAFieldInsertsItWhereItWasAimed()
    {
        using var scene = WindowScene.Create(Panel(0f, 0f, 300f, 200f));
        var root = (XPlatPanel)scene.Window.Content!;
        var box = new XPlatTextBox
        {
            Location = new SKPoint(20f, 20f),
            Size = new SKSize(160f, 40f),
            AllowDrop = true,
        };
        root.Controls.Add(box);

        var router = XPlatDragDropRouter.For(scene.Window);
        var effect = router.DragDrop(scene.CenterOf(box), XPlatDataObject.FromText("dropped"), Both);

        Assert.AreEqual(XPlatDragDropEffects.Copy, effect);
        Assert.AreEqual("dropped", box.Text, "the field takes the text it was aimed at");
    }

    [TestMethod]
    public void AFileDroppedOnAFieldLandsOnThePanelBehindIt()
    {
        using var scene = WindowScene.Create(Panel(0f, 0f, 300f, 200f));
        var root = (XPlatPanel)scene.Window.Content!;
        var box = new XPlatTextBox
        {
            Location = new SKPoint(20f, 20f),
            Size = new SKSize(160f, 40f),
            AllowDrop = true,
        };
        root.Controls.Add(box);

        string[]? files = null;
        root.AddDragDropHandler((_, e) => files = e.Data.GetFiles());

        var router = XPlatDragDropRouter.For(scene.Window);
        var effect = router.DragDrop(scene.CenterOf(box), XPlatDataObject.FromFiles(["C:/one.txt"]), Both);

        Assert.AreEqual(XPlatDragDropEffects.Copy, effect);
        CollectionAssert.AreEqual(new[] { "C:/one.txt" }, files, "the panel behind the field took the file");
    }

    [TestMethod]
    public void AHandlerThatThrowsIsReportedAndDoesNotUnwind()
    {
        using var scene = WindowScene.Create(Panel(0f, 0f, 300f, 200f));
        var root = (XPlatPanel)scene.Window.Content!;

        Exception? reported = null;
        scene.Application.AddUnhandledExceptionHandler((_, e) =>
        {
            reported = e.Exception;
            e.Handled = true;
        });

        root.AddDragEnterHandler((_, _) => throw new InvalidOperationException("handler failed"));

        var router = XPlatDragDropRouter.For(scene.Window);
        var effect = router.DragEnter(
            scene.CenterOf(root),
            XPlatDataObject.FromText("text"),
            XPlatDragDropEffects.Copy);

        Assert.IsNotNull(reported, "a handler's failure is reported rather than unwinding into the platform");
        Assert.IsInstanceOfType<InvalidOperationException>(reported);
        Assert.AreEqual(XPlatDragDropEffects.None, effect, "and the drag is refused");
    }
}
