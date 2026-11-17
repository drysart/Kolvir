using Kolvir;

namespace Kolvir.VirtualDom.Tests;

/// <summary>
/// Composition: a component emitted as a node of another component's render keeps a mount of its own,
/// so its state updates reach its own subtree and nothing else.
/// </summary>
/// <remarks>
/// The parent decides that a child is there and where it sits; the child decides what it looks like and
/// re-renders itself when its own state changes. These tests hold the two halves of that apart: a
/// parent's render leaves the child alone, and a child's render leaves the parent alone.
/// </remarks>
[TestClass]
[TestCategory("VirtualDom")]
public sealed class CompositionTests
{
    [TestMethod]
    public void AChildComponentRendersInsideItsParent()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var child = new Child("one");
        var parent = new Parent(child);

        using var mount = VirtualDomRuntime.Mount(host, parent);

        Assert.AreEqual(1, parent.Renders);
        Assert.AreEqual(1, child.Renders, "the child rendered once, for its first mount");
        Assert.AreEqual("one", Texts(mount).Single());
    }

    [TestMethod]
    public void AChildSetStateRendersOnlyTheChild()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var child = new Child("one");
        var parent = new Parent(child);
        using var mount = VirtualDomRuntime.Mount(host, parent);

        var control = ChildControls(mount).Single();

        child.Change("two");

        Assert.AreEqual(2, child.Renders, "the child rendered its own new state");
        Assert.AreEqual(1, parent.Renders, "and the parent did not render at all");
        Assert.AreEqual("two", Texts(mount).Single(), "the screen followed the child's state");
        Assert.AreSame(control, ChildControls(mount).Single(), "the child's control was patched, not rebuilt");
    }

    [TestMethod]
    public void AParentRenderLeavesTheChildAlone()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var child = new Child("one");
        var parent = new Parent(child);
        using var mount = VirtualDomRuntime.Mount(host, parent);

        var control = ChildControls(mount).Single();

        parent.Bump();

        Assert.AreEqual(2, parent.Renders);
        Assert.AreEqual(1, child.Renders, "emitting the child again does not re-render it");
        Assert.AreSame(control, ChildControls(mount).Single(), "nor rebuild its controls");
    }

    [TestMethod]
    public void RemovingAChildDisposesItsMount()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var child = new Child("one");
        var parent = new Parent(child);
        using var mount = VirtualDomRuntime.Mount(host, parent);

        parent.Hide(child);

        Assert.AreEqual(0, ChildControls(mount).Count, "the child's controls went with it");

        child.Change("two");

        Assert.AreEqual(1, child.Renders, "a removed child has no tree to patch");
    }

    [TestMethod]
    public void ReorderingKeyedChildrenMovesTheirControls()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var first = new Child("a");
        var second = new Child("b");
        var parent = new Parent(first, second);
        using var mount = VirtualDomRuntime.Mount(host, parent);

        var childControls = ChildControls(mount);

        parent.Reverse();

        var after = ChildControls(mount);
        Assert.AreEqual("b,a", string.Join(",", Texts(mount)), "the order followed the parent's state");
        Assert.AreSame(childControls[0], after[1], "the first child's control moved rather than being rebuilt");
        Assert.AreSame(childControls[1], after[0], "and so did the second's");
        Assert.AreEqual(1, first.Renders);
        Assert.AreEqual(1, second.Renders, "moving a child does not re-render it");
    }

    [TestMethod]
    public void ADifferentChildInTheSameSlotIsMountedAfresh()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var first = new Child("a");
        var parent = new Parent(first);
        using var mount = VirtualDomRuntime.Mount(host, parent);

        var second = new Child("b");
        parent.Replace(first, second);

        Assert.AreEqual("b", Texts(mount).Single(), "the slot shows the component the parent now asks for");
        Assert.AreEqual(1, second.Renders);

        first.Change("gone");

        Assert.AreEqual(1, first.Renders, "the replaced child was disposed, so its state goes nowhere");
    }

    [TestMethod]
    public void AComponentCannotBeMountedTwice()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var child = new Child("one");

        // One instance in two places would leave the first mount patching a component that no longer
        // points at it, so the second place fails instead.
        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => VirtualDomRuntime.Mount(host, new Parent(child, child)));

        StringAssert.Contains(exception.Message, "already mounted");
    }

    [TestMethod]
    public void AComponentNodeCannotBeTheRootOfAMount()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        var exception = Assert.ThrowsExactly<ArgumentException>(
            () => VirtualDomRuntime.Mount(host, (VNode)new VComponent(new Child("one"))));

        StringAssert.Contains(exception.Message, nameof(ComponentMount));
    }

    [TestMethod]
    public void AComponentNodeCannotAlsoBeGivenChildren()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => VirtualDomRuntime.Mount(host, new ChildWithChildren(new Child("one"))));

        StringAssert.Contains(exception.Message, "cannot be given Children");
    }

    [TestMethod]
    public void AComponentChildOfAComponentChildRenders()
    {
        // Nesting is the ordinary shape of a screen, so depth has to work without a special case: each
        // level mounts, patches and disposes its own subtree.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var leaf = new Child("deep");
        var middle = new Wrapper(new Wrapper(leaf));
        using var mount = VirtualDomRuntime.Mount(host, middle);

        Assert.AreEqual("deep", ((Label)Descend(mount.Root!, 3)).Text);

        middle.Bump();

        Assert.AreEqual(1, leaf.Renders, "a render three levels up does not reach the leaf");
        Assert.AreEqual("deep", ((Label)Descend(mount.Root!, 3)).Text);
    }

    /// <summary>Walks the single-child chain a wrapper makes, so a depth can be checked by label.</summary>
    private static Control Descend(Control control, int depth)
    {
        for (int i = 0; i < depth; i++)
        {
            control = ((Panel)control).Controls.OfType<Control>().Last();
        }

        return control;
    }

    /// <summary>A component whose whole state is the text in a box it renders.</summary>
    private sealed class Editor : Component
    {
        internal string Text { get; private set; } = string.Empty;

        internal int Renders { get; private set; }

        public override VNode Render()
        {
            Renders++;

            return new VPanel
            {
                Dock = DockStyle.Fill,
                Children =
                [
                    new VTextBox
                    {
                        Dock = DockStyle.Fill,
                        Text = Text,
                        TextChanged = sender => Edit(sender),
                    },
                ],
            };
        }

        private void Edit(object sender)
        {
            if (sender is not TextBox box || string.Equals(box.Text, Text, StringComparison.Ordinal))
            {
                return;
            }

            Text = box.Text;
            SetState();
        }
    }

    /// <summary>A component that changes the kind of its root node, which no patch can follow.</summary>
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

    /// <summary>A component whose only job is to emit another component.</summary>
    private sealed class Wrapper : Component
    {
        private readonly Component _inner;

        internal Wrapper(Component inner) => _inner = inner;

        internal int Renders { get; private set; }

        public override VNode Render()
        {
            Renders++;

            return new VPanel
            {
                Dock = DockStyle.Fill,
                Children = [new VComponent(_inner) { Key = _inner }],
            };
        }

        internal void Bump() => SetState();
    }

    [TestMethod]
    public void ATextBoxChangeRendersOnlyTheComponentThatOwnsIt()
    {
        // The demo's row in miniature: a child component whose state is a text box's text. Typing
        // changes the component, which re-renders and echoes the same text back to the box it came
        // from - and an assignment of the text a box already holds is not a change, so the echo does
        // not re-enter the handler and does not move the caret.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var editor = new Editor();
        var holder = new Wrapper(editor);
        using var mount = VirtualDomRuntime.Mount(host, holder);

        var box = ((Panel)mount.Root!).Controls.OfType<Panel>().Single().Controls.OfType<TextBox>().Single();

        box.Text = "typed";

        Assert.AreEqual("typed", editor.Text, "the component took the box's text");
        Assert.AreEqual(2, editor.Renders, "one render for the keystroke, and the echo is not another");
        Assert.AreEqual(1, holder.Renders, "the component that holds it did not render");
        Assert.AreEqual("typed", box.Text);
        Assert.AreSame(box, ((Panel)mount.Root!).Controls.OfType<Panel>().Single().Controls.OfType<TextBox>().Single(), "the box was patched, not rebuilt");
    }

    [TestMethod]
    public void AChildThatReplacesItsOwnRootKeepsItsPlace()
    {
        // A child is free to return a different kind of root node than it did last time, which swaps the
        // control the slot holds. The parent's record has to follow that, or the next parent render
        // would put the old control back into the tree beside the new one.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var child = new Swapper();
        var holder = new Wrapper(child);
        using var mount = VirtualDomRuntime.Mount(host, holder);

        child.Swap();

        var root = (Panel)mount.Root!;
        Assert.AreEqual(1, root.Controls.Count, "the child's own swap left nothing behind");
        Assert.IsInstanceOfType<Label>(root.Controls.OfType<Control>().Single());

        holder.Bump();

        root = (Panel)mount.Root!;
        Assert.AreEqual(
            1,
            root.Controls.Count,
            $"a parent render did not re-add the old root: [{string.Join(", ", root.Controls.OfType<Control>().Select(control => control.GetType().Name))}]");
        Assert.IsInstanceOfType<Label>(root.Controls.OfType<Control>().Single(), "and the slot still holds the new one");
    }

    /// <summary>The panel the parent docked the children into.</summary>
    private static Panel Stage(ComponentMount mount) =>
        ((Panel)mount.Root!).Controls.OfType<Panel>().Single();

    /// <summary>The controls the child components made, in order.</summary>
    private static List<Control> ChildControls(ComponentMount mount) => [.. Stage(mount).Controls];

    /// <summary>What each child is showing, in order, read back from the controls.</summary>
    private static string[] Texts(ComponentMount mount) =>
        [.. Stage(mount).Controls.Select(control => ((Panel)control).Controls.OfType<Label>().First().Text ?? string.Empty)];

    /// <summary>A component whose whole state is the text it shows.</summary>
    private sealed class Child : Component
    {
        private string _text;

        internal Child(string text) => _text = text;

        internal int Renders { get; private set; }

        public override VNode Render()
        {
            Renders++;

            return new VPanel
            {
                Dock = DockStyle.Top,
                Children = [new VLabel { Text = _text }],
            };
        }

        internal void Change(string text)
        {
            _text = text;
            SetState();
        }
    }

    /// <summary>
    /// A component that puts children on a component node, which is a mistake: the child's own render
    /// is what says what the child contains.
    /// </summary>
    private sealed class ChildWithChildren : Component
    {
        private readonly Child _child;

        internal ChildWithChildren(Child child) => _child = child;

        public override VNode Render() => new VPanel
        {
            Dock = DockStyle.Fill,
            Children =
            [
                new VComponent(_child) { Children = [new VLabel { Text = "and this as well" }] },
            ],
        };
    }

    /// <summary>
    /// A component that renders other components: its state is which are showing and in what order, and
    /// none of it is the children's state.
    /// </summary>
    private sealed class Parent : Component
    {
        private readonly List<Child> _children;
        private readonly List<Child> _hidden = [];
        private bool _reversed;
        private int _bumps;

        internal Parent(params Child[] children) => _children = [.. children];

        internal int Renders { get; private set; }

        public override VNode Render()
        {
            Renders++;

            var showing = _children.Where(child => !_hidden.Contains(child)).ToList();

            if (_reversed)
            {
                showing.Reverse();
            }

            return new VPanel
            {
                Dock = DockStyle.Fill,
                Children =
                [
                    new VLabel { Dock = DockStyle.Top, Text = $"{_bumps} bumps" },
                    new VPanel
                    {
                        Dock = DockStyle.Fill,
                        Children = [.. showing.Select(child => new VComponent(child) { Key = child })],
                    },
                ],
            };
        }

        /// <summary>Changes state that only the parent shows, to prove the children are untouched.</summary>
        internal void Bump()
        {
            _bumps++;
            SetState();
        }

        internal void Reverse()
        {
            _reversed = !_reversed;
            SetState();
        }

        internal void Hide(Child child)
        {
            _hidden.Add(child);
            SetState();
        }

        internal void Replace(Child previous, Child next)
        {
            _children[_children.IndexOf(previous)] = next;
            SetState();
        }
    }
}
