using Kolvir;

namespace Kolvir.VirtualDom.Tests;

/// <summary>
/// The component layer: a view model renders a tree, patches it when its state changes, and does
/// nothing - while losing nothing - when it has no tree to patch.
/// </summary>
[TestClass]
[TestCategory("VirtualDom")]
public sealed class ComponentTests
{
    [TestMethod]
    public void MountingRendersOnce()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var counter = new Counter();

        using var mount = VirtualDomRuntime.Mount(host, counter);

        Assert.AreEqual(1, counter.Renders, "mounting renders the component once");
        Assert.AreEqual("0 clicks", Text(mount));
    }

    [TestMethod]
    public void SetStateRendersOnceAndReusesTheControls()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var counter = new Counter();

        using var mount = VirtualDomRuntime.Mount(host, counter);
        var button = Button(mount);

        counter.Add();

        Assert.AreEqual(2, counter.Renders, "one SetState is one render");
        Assert.AreEqual("1 clicks", Text(mount));
        Assert.AreSame(button, Button(mount), "the button is patched, not rebuilt");
    }

    [TestMethod]
    public void AClickThroughTheRealControlUpdatesTheScreen()
    {
        // The whole point, end to end: a real hit test on a real control, a handler the mounted tree
        // installed, state changed, and the label on screen following it.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var counter = new Counter();

        using var mount = VirtualDomRuntime.Mount(host, counter);
        scene.Layout();

        Assert.IsTrue(Button(mount).Bounds.Width > 0f, $"the button has to be laid out to be clicked, was {Button(mount).Bounds}");

        scene.Click(Button(mount));

        Assert.AreEqual(1, counter.HandlerCalls, "the click should have reached the mounted button");
        Assert.AreEqual("1 clicks", Text(mount));
    }

    [TestMethod]
    public void ARenderThatChangesTheRootKindReplacesTheControl()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var swapper = new Swapper();

        using var mount = VirtualDomRuntime.Mount(host, swapper);
        Assert.IsInstanceOfType<Panel>(mount.Root);

        swapper.Swap();

        Assert.IsInstanceOfType<Label>(mount.Root, "a different root node class swaps the control in place");
        Assert.AreEqual(1, host.Controls.Count);
    }

    [TestMethod]
    public void StateChangedDuringARenderIsFoldedIntoTheSamePass()
    {
        // A component correcting its own state while rendering must not recurse into the reconciler
        // from inside a diff. It records the request and the pass repeats, so the screen ends correct.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var corrector = new Corrector();

        using var mount = VirtualDomRuntime.Mount(host, corrector);

        Assert.AreEqual(2, corrector.Renders, "the render that corrected itself ran again, once");
        Assert.AreEqual("fixed", LabelText(mount));
    }

    [TestMethod]
    public void AComponentThatNeverSettlesFailsInsteadOfHanging()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => VirtualDomRuntime.Mount(host, new NeverSettles()));

        StringAssert.Contains(exception.Message, nameof(NeverSettles));
    }

    [TestMethod]
    public void StateSetBeforeTheMountIsPickedUpByTheFirstRender()
    {
        // A component with no tree does nothing, and loses nothing: the first render reads the state as
        // it is by then. A component that is never mounted shows no controls at all, which is what
        // tells its author that the mount call went missing.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var counter = new Counter();

        counter.Add();
        counter.Add();

        Assert.AreEqual(0, counter.Renders, "a component with no tree renders nothing");

        using var mount = VirtualDomRuntime.Mount(host, counter);

        Assert.AreEqual(1, counter.Renders, "the mount's own render is the only one");
        Assert.AreEqual("2 clicks", Text(mount), "the state set against no tree was not lost");
    }

    [TestMethod]
    public void SetStateAfterDisposeIsANoOp()
    {
        // An event that arrives after its window closed is ordinary, so teardown order cannot matter.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var counter = new Counter();

        var mount = VirtualDomRuntime.Mount(host, counter);
        var rendersBefore = counter.Renders;
        mount.Dispose();

        counter.Add();

        Assert.AreEqual(rendersBefore, counter.Renders, "a disposed mount does not render again");
        Assert.IsFalse(mount.IsMounted);
    }

    [TestMethod]
    public void MountingNeedsAParentAndAComponent()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => VirtualDomRuntime.Mount(null!, new Counter()));
        Assert.ThrowsExactly<ArgumentNullException>(() => VirtualDomRuntime.Mount(new Panel(), (Component)null!));
    }

    [TestMethod]
    public void TheMountReportsTheComponentItRenders()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var counter = new Counter();

        using var mount = VirtualDomRuntime.Mount(host, counter);

        Assert.AreSame(counter, mount.Component);
        Assert.IsTrue(mount.IsMounted);
        Assert.AreSame(host, mount.Root!.Parent);
    }

    private static Button Button(ComponentMount mount) =>
        ((Panel)mount.Root!).Controls.OfType<Button>().First();

    private static string Text(ComponentMount mount) => Button(mount).Text ?? string.Empty;

    private static string LabelText(ComponentMount mount) =>
        ((Panel)mount.Root!).Controls.OfType<Label>().First().Text ?? string.Empty;

    /// <summary>A counter whose state changes from an event handler, which is the ordinary shape.</summary>
    private sealed class Counter : Component
    {
        private int _count;

        internal int Renders { get; private set; }

        /// <summary>How many times the button's handler ran, so a broken click is distinguishable from a broken render.</summary>
        internal int HandlerCalls { get; private set; }

        public override VNode Render()
        {
            Renders++;

            return new VPanel
            {
                Dock = DockStyle.Fill,
                // One Fill child, which is the shape the reconciler's own tests drive: the click has a
                // laid-out button to land on, rather than whatever a second docked child left over.
                Children =
                [
                    new VButton { Dock = DockStyle.Fill, Text = $"{_count} clicks", Click = _ => Add() },
                ],
            };
        }

        internal void Add()
        {
            HandlerCalls++;
            _count++;
            SetState();
        }
    }

    /// <summary>Changes the kind of its root node, which no patch can follow.</summary>
    private sealed class Swapper : Component
    {
        private bool _swapped;

        public override VNode Render() => _swapped
            ? new VLabel { Text = "now a label" }
            : new VPanel { Dock = DockStyle.Fill };

        internal void Swap()
        {
            _swapped = true;
            SetState();
        }
    }

    /// <summary>Corrects its own state during its first render.</summary>
    private sealed class Corrector : Component
    {
        private bool _fixed;

        internal int Renders { get; private set; }

        public override VNode Render()
        {
            Renders++;

            if (!_fixed)
            {
                _fixed = true;
                SetState();
            }

            return new VPanel
            {
                Dock = DockStyle.Fill,
                Children = [new VLabel { Text = _fixed ? "fixed" : "broken" }],
            };
        }
    }

    /// <summary>Sets state on every render, which can never settle.</summary>
    private sealed class NeverSettles : Component
    {
        public override VNode Render()
        {
            SetState();

            return new VPanel { Dock = DockStyle.Fill };
        }
    }
}
