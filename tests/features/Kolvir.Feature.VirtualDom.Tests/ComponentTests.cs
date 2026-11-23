using Kolvir;

namespace Kolvir.Feature.VirtualDom.Tests;

/// <summary>
/// The component layer: a view model renders a tree, marks itself when its state changes, and renders
/// once on the next frame - or does nothing at all when it has no tree to patch.
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
    public void SetStateRendersOnceOnTheNextFrame()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var counter = new Counter();

        using var mount = VirtualDomRuntime.Mount(host, counter);
        var button = Button(mount);

        counter.Add();

        Assert.AreEqual(1, counter.Renders, "SetState only marks the component: the next frame renders it");

        scene.Frame();

        Assert.AreEqual(2, counter.Renders, "and one frame is one render");
        Assert.AreEqual("1 clicks", Text(mount));
        Assert.AreSame(button, Button(mount), "the button is patched, not rebuilt");
    }

    [TestMethod]
    public void SeveralStateChangesBeforeAFrameRenderOnce()
    {
        // The whole reason for deferring: a handler that changes several things costs one render and
        // one tree update, not one of each per change - which is what keeps layout thrashing out.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var counter = new Counter();
        using var mount = VirtualDomRuntime.Mount(host, counter);

        counter.Add();
        counter.Add();
        counter.Add();

        scene.Frame();

        Assert.AreEqual(2, counter.Renders, "three changes and one render");
        Assert.AreEqual("3 clicks", Text(mount), "the render read the state as it was by then");
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
        Assert.AreEqual(1, counter.Renders, "the click changed state; it did not render");

        scene.Frame();

        Assert.AreEqual(1, counter.HandlerCalls, "the frame does not re-run the handler");
        Assert.AreEqual("1 clicks", Text(mount), "the frame shows what the click changed");
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

        Assert.IsInstanceOfType<Panel>(mount.Root, "the swap waits for a frame like any other state change");

        scene.Frame();

        Assert.IsInstanceOfType<Label>(mount.Root, "a different root node class swaps the control in place");
        Assert.AreEqual(1, host.Controls.Count);
    }

    [TestMethod]
    public void ARenderThatChangesStateIsRefused()
    {
        // A render describes the state it is given, so one that changes the state describes a tree that
        // is already out of date: there is no fixed point to settle on, so this is refused at the call.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var corrector = new Corrector();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => VirtualDomRuntime.Mount(host, corrector));

        StringAssert.Contains(exception.Message, nameof(Corrector));
        StringAssert.Contains(exception.Message, nameof(Component.SetState));
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

        scene.Frame();

        Assert.AreEqual(rendersBefore, counter.Renders, "a disposed mount does not render, even when a frame runs");
        Assert.IsFalse(mount.IsMounted);
    }

    [TestMethod]
    public void StateSetBeforeTheTreeIsOnScreenRendersOnTheFirstFrame()
    {
        // A mount does not need a host: a page builds and mounts its components while the window is
        // still being assembled, so a state change with nothing to draw into has to be queued rather
        // than dropped - and the first frame of any window drains it.
        var host = new Panel { Dock = DockStyle.Fill };
        var counter = new Counter();
        using var mount = VirtualDomRuntime.Mount(host, counter);

        counter.Add();

        Assert.AreEqual(1, counter.Renders, "no frame has been drawn, so nothing rendered");

        using var scene = Scene.Create(host);

        scene.Frame();

        Assert.AreEqual(2, counter.Renders, "the first frame drew the change that was waiting");
        Assert.AreEqual("1 clicks", Text(mount));
    }

    [TestMethod]
    public void AComponentThatDoesNotDeriveFromComponentMountsAndRenders()
    {
        // The seam. The mount needs Render, Attach and Detach and nothing else, so a base class of its
        // own - one that cannot inherit, or that schedules differently - gets the whole layer: the
        // deferred render, the patch and the teardown. SetState is the application's half of the
        // protocol and is not on the interface, so this one spells its own state change instead.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var counter = new PlainCounter();

        using var mount = VirtualDomRuntime.Mount(host, counter);

        Assert.AreEqual(1, counter.Renders, "the mount renders it once");
        Assert.AreSame(mount, counter.Mount, "and told it which mount owns the tree");
        Assert.AreEqual("0 clicks", Text(mount));

        counter.Add();

        Assert.AreEqual(1, counter.Renders, "Invalidate is the same deferral SetState is");
        Assert.AreEqual("0 clicks", Text(mount), "and the screen has not followed yet");

        scene.Frame();

        Assert.AreEqual(2, counter.Renders, "one frame is one render");
        Assert.AreEqual("1 clicks", Text(mount));
        Assert.AreSame(mount, counter.Mount, "and it is still mounted through the same one");

        mount.Dispose();

        Assert.IsNull(counter.Mount, "disposing detached it, because Detach is the interface's half");
    }

    [TestMethod]
    public void MountingNeedsAParentAndAComponent()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => VirtualDomRuntime.Mount(null!, new Counter()));
        Assert.ThrowsExactly<ArgumentNullException>(() => VirtualDomRuntime.Mount(new Panel(), (IComponent)null!));
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

    /// <summary>
    /// An <see cref="IComponent"/> that does not derive from <see cref="Component"/>: the seam, exercised.
    /// </summary>
    /// <remarks>
    /// It deliberately spells out the three members the mount uses, including the single-mount rule that
    /// <see cref="Component"/> keeps for its own subclasses. Its state change is
    /// <see cref="ComponentMount.Invalidate"/> and nothing else, which is the whole point of the
    /// interface not carrying `SetState`.
    /// </remarks>
    private sealed class PlainCounter : IComponent
    {
        private int _count;

        internal int Renders { get; private set; }

        internal ComponentMount? Mount { get; private set; }

        public VNode Render()
        {
            Renders++;

            return new VPanel
            {
                Dock = DockStyle.Fill,
                Children =
                [
                    new VButton { Dock = DockStyle.Fill, Text = $"{_count} clicks", Click = _ => Add() },
                ],
            };
        }

        public void Attach(ComponentMount mount)
        {
            if (Mount is { IsMounted: true } existing && !ReferenceEquals(existing, mount))
            {
                throw new InvalidOperationException($"{nameof(PlainCounter)} is already mounted.");
            }

            Mount = mount;
        }

        public void Detach(ComponentMount mount)
        {
            if (ReferenceEquals(Mount, mount))
            {
                Mount = null;
            }
        }

        internal void Add()
        {
            _count++;
            Mount?.Invalidate();
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

    /// <summary>Changes its own state from inside its first render, which the contract forbids.</summary>
    private sealed class Corrector : Component
    {
        public override VNode Render()
        {
            SetState();

            return new VPanel { Dock = DockStyle.Fill };
        }
    }
}
