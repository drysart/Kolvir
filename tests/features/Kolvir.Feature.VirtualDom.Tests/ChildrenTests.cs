using Kolvir;

namespace Kolvir.VirtualDom.Tests;

/// <summary>
/// Reconciling a container's children: append, remove, replace, insert and reorder, and which of them
/// keep the controls that survive a render.
/// </summary>
[TestClass]
[TestCategory("VirtualDom")]
public sealed class ChildrenTests
{
    [TestMethod]
    public void AppendingAChildKeepsTheOnesBeforeIt()
    {
        using var session = new Session(new VLabel { Text = "a" });
        var first = session.Child(0);

        session.Update(new VLabel { Text = "a" }, new VLabel { Text = "b" });

        Assert.AreEqual(2, session.Children);
        Assert.AreSame(first, session.Child(0), "the child that did not change keeps its control");
        Assert.AreEqual("b", session.Child(1).Text);
    }

    [TestMethod]
    public void RemovingAChildDetachesItsControl()
    {
        using var session = new Session(new VLabel { Text = "a" }, new VLabel { Text = "b" });
        var second = session.Child(1);

        session.Update(new VLabel { Text = "a" });

        Assert.AreEqual(1, session.Children);
        Assert.IsNull(second.Parent, "the removed child is detached");
    }

    [TestMethod]
    public void ReplacingAChildsKindRebuildsOnlyThatChild()
    {
        using var session = new Session(new VLabel { Text = "a" }, new VLabel { Text = "b" });
        var first = session.Child(0);

        session.Update(new VLabel { Text = "a" }, new VButton { Text = "now a button" });

        Assert.AreSame(first, session.Child(0), "the sibling keeps its control");
        Assert.IsInstanceOfType<Button>(session.Child(1));
        Assert.AreEqual("now a button", session.Child(1).Text);
    }

    [TestMethod]
    public void ReversingAKeyedListMovesControlsInsteadOfRebuildingThem()
    {
        using var session = new Session(
            new VLabel { Key = "a", Text = "a" },
            new VLabel { Key = "b", Text = "b" },
            new VLabel { Key = "c", Text = "c" });

        var a = session.Child(0);
        var b = session.Child(1);
        var c = session.Child(2);

        session.Update(
            new VLabel { Key = "c", Text = "c" },
            new VLabel { Key = "b", Text = "b" },
            new VLabel { Key = "a", Text = "a" });

        Assert.AreSame(c, session.Child(0), "each keyed child keeps its control");
        Assert.AreSame(b, session.Child(1));
        Assert.AreSame(a, session.Child(2));
    }

    [TestMethod]
    public void InsertingAKeyedChildInTheMiddleKeepsItsSiblings()
    {
        using var session = new Session(
            new VLabel { Key = 1, Text = "one" },
            new VLabel { Key = 3, Text = "three" });

        var one = session.Child(0);
        var three = session.Child(1);

        session.Update(
            new VLabel { Key = 1, Text = "one" },
            new VLabel { Key = 2, Text = "two" },
            new VLabel { Key = 3, Text = "three" });

        Assert.AreEqual(3, session.Children);
        Assert.AreSame(one, session.Child(0));
        Assert.AreEqual("two", session.Child(1).Text);
        Assert.AreSame(three, session.Child(2));
    }

    [TestMethod]
    public void AKeyedChildRemovedAndAnotherAddedLeavesOneOfTheTwo()
    {
        using var session = new Session(
            new VLabel { Key = "keep", Text = "keep" },
            new VLabel { Key = "drop", Text = "drop" });

        var kept = session.Child(0);
        var dropped = session.Child(1);

        session.Update(
            new VLabel { Key = "keep", Text = "keep" },
            new VLabel { Key = "new", Text = "new" });

        Assert.AreSame(kept, session.Child(0));
        Assert.IsNull(dropped.Parent);
        Assert.AreEqual("new", session.Child(1).Text);
    }

    [TestMethod]
    public void UnkeyedChildrenArePairedByPosition()
    {
        using var session = new Session(new VLabel { Text = "a" }, new VLabel { Text = "b" });
        var first = session.Child(0);
        var second = session.Child(1);

        session.Update(new VLabel { Text = "a" }, new VLabel { Text = "changed" });

        Assert.AreSame(first, session.Child(0));
        Assert.AreSame(second, session.Child(1), "an unkeyed child is its position, so the second stays the second");
        Assert.AreEqual("changed", second.Text);
    }

    [TestMethod]
    public void KeyedAndUnkeyedChildrenMix()
    {
        using var session = new Session(
            new VLabel { Key = "k", Text = "keyed" },
            new VLabel { Text = "unkeyed" });

        var keyed = session.Child(0);
        var unkeyed = session.Child(1);

        session.Update(
            new VLabel { Text = "unkeyed" },
            new VLabel { Key = "k", Text = "keyed" });

        Assert.AreSame(unkeyed, session.Child(0), "the unkeyed child fills the first slot");
        Assert.AreSame(keyed, session.Child(1), "and the keyed one moves to where its key is asked for");
    }

    [TestMethod]
    public void GrandchildrenAreReconciledToo()
    {
        using var session = new Session(
            new VPanel { Children = [new VLabel { Text = "deep" }] });

        var inner = (Panel)session.Child(0);
        var label = inner.Controls[0];

        session.Update(
            new VPanel { Children = [new VLabel { Text = "deeper" }] });

        Assert.AreSame(
            inner,
            session.Child(0),
            "the container is patched rather than rebuilt");
        Assert.AreSame(label, inner.Controls[0], "and so is the control inside it");
        Assert.AreEqual("deeper", label.Text);
    }

    [TestMethod]
    public void RemovingEveryChildEmptiesTheContainer()
    {
        using var session = new Session(new VLabel { Text = "a" }, new VLabel { Text = "b" });

        session.Update();

        Assert.AreEqual(0, session.Children);
    }

    [TestMethod]
    public void AChildListUpdateBalancesItsLayoutSuspension()
    {
        // SuspendLayout/ResumeLayout are a counter, and ResumeLayout throws when it is unbalanced, so
        // this asserts that reconciling children does not leak a suspension - which would silently
        // stop the container ever laying out again.
        using var session = new Session(new VLabel { Text = "a" });

        session.Update(new VLabel { Text = "a" }, new VLabel { Text = "b" });
        session.Update();

        session.Container.SuspendLayout();
        session.Container.ResumeLayout();
    }

    /// <summary>A mounted panel of label-sized children, with the narration a child test needs.</summary>
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

        public int Children => Container.Controls.Count;

        public Control Child(int index) => Container.Controls[index];

        public void Update(params VNode[] children) => _mount.Update(new VPanel { Children = children });

        public void Dispose()
        {
            _mount.Dispose();
            _scene.Dispose();
        }
    }
}
