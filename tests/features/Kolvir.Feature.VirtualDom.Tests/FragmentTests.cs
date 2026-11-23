using Kolvir;

namespace Kolvir.Feature.VirtualDom.Tests;

/// <summary>
/// The fragment node: a group of nodes with no control of its own, inlined into its parent where it sits.
/// </summary>
/// <remarks>
/// Every test here is about the same property from a different angle - that a fragment is not a sibling,
/// so what it holds is a sibling of everything around it - and about the two things that only work
/// because of it: a render with several top-level nodes, and a render with none.
/// </remarks>
[TestClass]
[TestCategory("VirtualDom")]
public sealed class FragmentTests
{
    [TestMethod]
    public void AnEmptyFragmentMakesNoControl()
    {
        using var session = new Session(new VFragment());

        Assert.AreEqual(0, session.Count, "nothing to show is nothing to add");
    }

    [TestMethod]
    public void AFragmentsChildrenTakeItsPlaceAmongTheSiblings()
    {
        using var session = new Session(
            new VLabel { Text = "a" },
            new VFragment
            {
                Children = [new VLabel { Text = "b" }, new VLabel { Text = "c" }],
            },
            new VLabel { Text = "d" });

        CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, session.Texts());
    }

    [TestMethod]
    public void ANestedFragmentFlattensToItsChildren()
    {
        using var session = new Session(new VFragment
        {
            Children =
            [
                new VLabel { Text = "a" },
                new VFragment { Children = [new VLabel { Text = "b" }] },
                new VLabel { Text = "c" },
            ],
        });

        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, session.Texts());
    }

    [TestMethod]
    public void AFragmentAddedLaterKeepsItsSiblingsControls()
    {
        // The children of a fragment are matched against the rest of the parent's children, which is what
        // makes keys the identity across the boundary: a key has to be unique among all of them.
        using var session = new Session(
            new VLabel { Key = "a", Text = "a" },
            new VLabel { Key = "d", Text = "d" });

        var a = session.Child(0);
        var d = session.Child(1);

        session.Update(
            new VLabel { Key = "a", Text = "a" },
            new VFragment
            {
                Children =
                [
                    new VLabel { Key = "b", Text = "b" },
                    new VLabel { Key = "c", Text = "c" },
                ],
            },
            new VLabel { Key = "d", Text = "d" });

        CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, session.Texts());
        Assert.AreSame(a, session.Child(0), "the sibling before the fragment keeps its control");
        Assert.AreSame(d, session.Child(3), "and so does the one after it");
    }

    [TestMethod]
    public void RemovingAFragmentDetachesItsChildren()
    {
        using var session = new Session(
            new VLabel { Key = "a", Text = "a" },
            new VFragment
            {
                Children =
                [
                    new VLabel { Key = "b", Text = "b" },
                    new VLabel { Key = "c", Text = "c" },
                ],
            });

        var b = session.Child(1);

        session.Update(new VLabel { Key = "a", Text = "a" });

        Assert.AreEqual(1, session.Count);
        Assert.IsNull(b.Parent, "a node that is no longer asked for takes its control with it");
    }

    [TestMethod]
    public void AFragmentCanBeTheRootOfAMount()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VFragment
        {
            Children = [new VLabel { Text = "a" }, new VLabel { Text = "b" }],
        });

        Assert.AreEqual(2, host.Controls.Count, "each child of the root is one of the parent's children");
        Assert.AreSame(host.Controls[0], mount.Root, "the root is the first control the fragment made");

        var first = host.Controls[0];

        mount.Update(new VFragment { Children = [new VLabel { Text = "only" }] });

        Assert.AreEqual(1, host.Controls.Count, "the second child is no longer asked for");
        Assert.AreSame(first, host.Controls[0], "and the first is patched rather than rebuilt");
        Assert.AreEqual("only", host.Controls[0].Text);
    }

    [TestMethod]
    public void AnEmptyRootMakesNoControlsAndTakesTheOnesItIsGiven()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VFragment());

        Assert.AreEqual(0, host.Controls.Count);
        Assert.IsNull(mount.Root, "there is no control to name");
        Assert.IsTrue(mount.IsMounted, "but the tree is mounted: it is the place its controls will go");

        mount.Update(new VLabel { Text = "now" });

        Assert.AreEqual(1, host.Controls.Count);
        Assert.AreSame(host.Controls[0], mount.Root);
        Assert.AreEqual("now", host.Controls[0].Text);
    }

    [TestMethod]
    public void AKeyOnAFragmentIsRefusedByName()
    {
        // Inlining leaves nothing for a key to identify, so a key on the fragment is a mistake the caller
        // cannot see - and it is refused rather than ignored, because ignoring it silently renames the
        // identity it was meant to hold.
        using var session = new Session(new VLabel { Text = "a" });

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(
            () => session.Update(new VFragment { Key = "k", Children = [new VLabel { Text = "b" }] }));

        StringAssert.Contains(thrown.Message, nameof(VFragment), "the message names the node");
        StringAssert.Contains(thrown.Message, "Key", "and the thing it refuses");
    }

    [TestMethod]
    public void AFragmentThatContainsItselfIsRefusedByName()
    {
        // The one way to build a tree that would never finish inlining: a fragment whose descendant is
        // itself. Refused by name, which is the difference between a message and a stack overflow.
        var children = new List<VNode>();
        var fragment = new VFragment { Children = children };
        children.Add(fragment);

        using var session = new Session(new VLabel { Text = "a" });

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => session.Update(fragment));

        StringAssert.Contains(thrown.Message, nameof(VFragment));
    }

    [TestMethod]
    public void AComponentThatRendersAFragmentOwnsEveryControlItMakes()
    {
        // A component's render may put several nodes at the top level, which the parent has to count and
        // place as a span - otherwise the parent's own siblings would be laid out over the top of them.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var child = new TwoLabelComponent();

        using var mount = VirtualDomRuntime.Mount(host, new VPanel
        {
            Children =
            [
                new VLabel { Key = "a", Text = "a" },
                new VComponent(child) { Key = "c" },
                new VLabel { Key = "d", Text = "d" },
            ],
        });

        var panel = (Panel)mount.Root!;

        Assert.AreEqual(4, panel.Controls.Count, "the component's two nodes are the parent's children");
        CollectionAssert.AreEqual(new[] { "a", "x", "y", "d" }, Texts(panel));

        var last = panel.Controls[3];

        child.Collapse();
        scene.Frame();

        Assert.AreEqual(3, panel.Controls.Count, "the node the render stopped returning is gone");
        CollectionAssert.AreEqual(new[] { "a", "x", "d" }, Texts(panel));
        Assert.AreSame(last, panel.Controls[2], "and the parent's own child moved up behind it");
    }

    [TestMethod]
    public void AChildComponentWithNothingToShowOccupiesNoSlot()
    {
        // The case the null model needs: a child that is mounted and listening, and in the parent's list
        // takes up no room - so when it does have something to show, it appears where the parent said the
        // child is rather than at the end.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var child = new LateComponent();

        using var mount = VirtualDomRuntime.Mount(host, new VPanel
        {
            Children =
            [
                new VLabel { Key = "a", Text = "a" },
                new VComponent(child) { Key = "c" },
                new VLabel { Key = "d", Text = "d" },
            ],
        });

        var panel = (Panel)mount.Root!;

        CollectionAssert.AreEqual(new[] { "a", "d" }, Texts(panel));

        var last = panel.Controls[1];

        child.Show();
        scene.Frame();

        CollectionAssert.AreEqual(new[] { "a", "shown", "d" }, Texts(panel), "the control appears in the child's own place");
        Assert.AreSame(last, panel.Controls[2]);
    }

    [TestMethod]
    public void AFragmentCanHoldAChildComponent()
    {
        // A component inside a fragment inside a component's render. The counts have to add up through
        // every level, or the parent places the next sibling over the top of what the child rendered.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var inner = new LateComponent();
        var outer = new WrappingComponent(inner);

        using var mount = VirtualDomRuntime.Mount(host, new VPanel
        {
            Children =
            [
                new VLabel { Key = "a", Text = "a" },
                new VComponent(outer) { Key = "o" },
                new VLabel { Key = "d", Text = "d" },
            ],
        });

        var panel = (Panel)mount.Root!;

        CollectionAssert.AreEqual(new[] { "a", "x", "d" }, Texts(panel), "an empty grandchild takes no room");

        inner.Show();
        scene.Frame();

        CollectionAssert.AreEqual(
            new[] { "a", "x", "shown", "d" },
            Texts(panel),
            "and it appears after its own sibling, not at the end");
    }

    private static string[] Texts(Panel panel) => [.. panel.Controls.Select(control => control.Text ?? string.Empty)];

    /// <summary>Renders two top-level nodes, then one, so a span in the parent can be seen shrinking.</summary>
    private sealed class TwoLabelComponent : Component
    {
        private bool _collapsed;

        public override VNode Render() => _collapsed
            ? new VFragment { Children = [new VLabel { Key = "x", Text = "x" }] }
            : new VFragment
            {
                Children =
                [
                    new VLabel { Key = "x", Text = "x" },
                    new VLabel { Key = "y", Text = "y" },
                ],
            };

        internal void Collapse()
        {
            _collapsed = true;
            SetState();
        }
    }

    /// <summary>Renders nothing at all until it is told to, which is the empty-fragment case.</summary>
    private sealed class LateComponent : Component
    {
        private bool _shown;

        public override VNode Render() => _shown
            ? new VLabel { Text = "shown" }
            : new VFragment();

        internal void Show()
        {
            _shown = true;
            SetState();
        }
    }

    /// <summary>Puts a child component in a fragment of its own, below another node of its own.</summary>
    private sealed class WrappingComponent : Component
    {
        private readonly IComponent _inner;

        internal WrappingComponent(IComponent inner) => _inner = inner;

        public override VNode Render() => new VFragment
        {
            Children = [new VLabel { Key = "x", Text = "x" }, new VComponent(_inner)],
        };
    }

    /// <summary>A panel on the host with the nodes to reconcile as its children.</summary>
    private sealed class Session : IDisposable
    {
        private readonly Panel _host = new() { Dock = DockStyle.Fill };
        private readonly Scene _scene;
        private readonly MountHandle _mount;

        public Session(params VNode[] children)
        {
            _scene = Scene.Create(_host);
            _mount = VirtualDomRuntime.Mount(_host, new VPanel { Children = children });
        }

        public Panel Container => (Panel)_mount.Root!;

        public int Count => Container.Controls.Count;

        public Control Child(int index) => Container.Controls[index];

        public string[] Texts() => FragmentTests.Texts(Container);

        public void Update(params VNode[] children) => _mount.Update(new VPanel { Children = children });

        public void Dispose()
        {
            _mount.Dispose();
            _scene.Dispose();
        }
    }
}
