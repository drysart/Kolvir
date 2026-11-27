using Kolvir;

namespace Kolvir.Feature.VirtualDom.Tests;

/// <summary>
/// What happens to the controls a mount made when their nodes leave the tree: they are disposed, and
/// the two things that are not a removal - a reorder and a same-kind patch - leave them alone.
/// </summary>
/// <remarks>
/// Disposal is observed through <see cref="Control.AddDisposingHandler"/> rather than by poking at an
/// internal flag, because firing <c>Disposing</c> is the behaviour a consumer can rely on - and it is
/// what an in-flight asset load cancels from. A borrowed slot is the one control a mount fills but did
/// not make, so it is pinned here as the case that must stay alive.
/// </remarks>
[TestClass]
[TestCategory("VirtualDom")]
public sealed class DisposalTests
{
    [TestMethod]
    public void RemovingAChildDisposesTheControlItMade()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        using var mount = VirtualDomRuntime.Mount(host, new VPanel
        {
            Children = [new VLabel { Text = "a" }, new VLabel { Text = "b" }],
        });

        var container = (Panel)mount.Root!;
        var kept = container.Controls[0];
        var removed = container.Controls[1];
        var keptDisposed = Track(kept);
        var removedDisposed = Track(removed);

        mount.Update(new VPanel { Children = [new VLabel { Text = "a" }] });

        Assert.IsTrue(removedDisposed(), "the control the removed child made is disposed");
        Assert.IsFalse(keptDisposed(), "and the one that stayed is not");
        Assert.IsNull(removed.Parent);
        Assert.AreEqual(1, container.Controls.Count);
    }

    [TestMethod]
    public void ReorderingKeyedChildrenDisposesNothing()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        using var mount = VirtualDomRuntime.Mount(host, new VPanel
        {
            Children =
            [
                new VLabel { Key = "a", Text = "a" },
                new VLabel { Key = "b", Text = "b" },
                new VLabel { Key = "c", Text = "c" },
            ],
        });

        var container = (Panel)mount.Root!;
        var disposed = container.Controls.Cast<Control>().Select(Track).ToList();
        var before = container.Controls.Cast<Control>().ToArray();

        mount.Update(new VPanel
        {
            Children =
            [
                new VLabel { Key = "c", Text = "c" },
                new VLabel { Key = "a", Text = "a" },
                new VLabel { Key = "b", Text = "b" },
            ],
        });

        Assert.AreEqual(0, disposed.Count(wasDisposed => wasDisposed()), "a move is not a removal");

        var after = container.Controls.Cast<Control>().ToArray();

        CollectionAssert.AreEquivalent(before, after, "each keyed control is the same instance after the move");
    }

    [TestMethod]
    public void PatchingTheSameNodeTypeDisposesNothing()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        using var mount = VirtualDomRuntime.Mount(host, new VLabel { Text = "before" });

        var label = mount.Root!;
        var disposed = Track(label);

        mount.Update(new VLabel { Text = "after" });

        Assert.IsFalse(disposed(), "a patch reuses the control, so nothing is disposed");
        Assert.AreSame(label, mount.Root);
        Assert.AreEqual("after", mount.Root!.Text);
    }

    [TestMethod]
    public void ReplacingAChildWithADifferentNodeTypeDisposesTheOldControl()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        using var mount = VirtualDomRuntime.Mount(host, new VPanel
        {
            Children = [new VLabel { Text = "first" }],
        });

        var container = (Panel)mount.Root!;
        var old = container.Controls[0];
        var disposed = Track(old);

        mount.Update(new VPanel { Children = [new VButton { Text = "replacement" }] });

        Assert.IsTrue(disposed(), "the control whose node type changed cannot be patched, so it is disposed");
        Assert.IsNull(old.Parent);
        Assert.IsInstanceOfType<Button>(container.Controls[0]);
        Assert.AreEqual(1, container.Controls.Count);
    }

    [TestMethod]
    public void UnmountingAComponentDisposesTheControlsItsRenderMade()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var mount = VirtualDomRuntime.Mount(host, new Boxed());

        var panel = (Panel)mount.Root!;
        var label = panel.Controls[0];
        var panelDisposed = Track(panel);
        var labelDisposed = Track(label);

        mount.Dispose();

        Assert.IsTrue(panelDisposed(), "the component's root control is disposed with its mount");
        Assert.IsTrue(labelDisposed(), "and so is the control under it");
        Assert.IsFalse(mount.IsMounted);
        Assert.AreEqual(0, host.Controls.Count);
    }

    [TestMethod]
    public void DisposingAMountHandleDisposesTheWholeTree()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var mount = VirtualDomRuntime.Mount(host, new VPanel
        {
            Children = [new VPanel { Children = [new VLabel { Text = "deep" }] }],
        });

        var outer = (Panel)mount.Root!;
        var inner = (Panel)outer.Controls[0];
        var label = inner.Controls[0];
        var disposed = new[] { outer, inner, label }.Select(Track).ToList();

        mount.Dispose();

        Assert.AreEqual(3, disposed.Count(wasDisposed => wasDisposed()), "disposing the handle unwinds every control it made");
    }

    [TestMethod]
    public void ABorrowedSlotsControlIsNotDisposedWhenItsNodeGoesAway()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        using var mount = VirtualDomRuntime.Mount(host, new VSplitContainer
        {
            Panel1 = new VPanel { Children = [new VLabel { Text = "inside" }] },
        });

        var split = (SplitContainer)mount.Root!;
        var panel = split.Panel1;
        var label = panel.Controls[0];
        var panelDisposed = Track(panel);
        var labelDisposed = Track(label);

        mount.Update(new VSplitContainer());

        Assert.IsFalse(
            panelDisposed(),
            "a borrowed slot's panel belongs to the container that made it, so the reconciler does not dispose it");
        Assert.IsTrue(labelDisposed(), "but the children the node put in it are unmounted as usual");
        Assert.AreSame(split, panel.Parent, "and the panel stays where the container put it");
        Assert.AreEqual(0, panel.Controls.Count);
    }

    [TestMethod]
    public void RemovingASpanDisposesTheControlItsNodeMade()
    {
        // The element-collection half of the same rule: a span leaving a collection is a permanent
        // removal for the control its node made, even though the TextSpan itself is application state
        // and is not disposed. A reorder stays a move and is covered by ElementCollectionTests.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        using var mount = VirtualDomRuntime.Mount(host, new VRichLabel
        {
            Spans = [new VTextSpan { Text = "go", Control = new VButton { Text = "go" } }],
        });

        var label = (RichLabel)mount.Root!;
        var button = label.Spans[0].Control!;
        var disposed = Track(button);

        mount.Update(new VRichLabel { Spans = [] });

        Assert.IsTrue(disposed(), "the control the removed span's node made is disposed");
        Assert.IsNull(button.Parent);
        Assert.AreEqual(0, label.Controls.Count);
    }

    [TestMethod]
    public void ReplacingASpansControlNodeTypeDisposesTheOldControl()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        using var mount = VirtualDomRuntime.Mount(host, new VRichLabel
        {
            Spans = [new VTextSpan { Text = "go", Control = new VButton { Text = "go" } }],
        });

        var label = (RichLabel)mount.Root!;
        var button = label.Spans[0].Control!;
        var disposed = Track(button);

        mount.Update(new VRichLabel
        {
            Spans = [new VTextSpan { Text = "go", Control = new VNote { Note = "replacement" } }],
        });

        Assert.IsTrue(disposed(), "the control whose node type changed is disposed");
        Assert.IsNull(button.Parent);
        Assert.IsInstanceOfType<NoteControl>(label.Spans[0].Control);
        Assert.AreEqual(1, label.Controls.Count);
    }

    /// <summary>Watches <paramref name="control"/> for the <c>Disposing</c> notification.</summary>
    private static Func<bool> Track(Control control)
    {
        var disposed = false;
        control.AddDisposingHandler(_ => disposed = true);
        return () => disposed;
    }

    /// <summary>A component whose render is a panel holding one label, for the mount-wide case.</summary>
    private sealed class Boxed : Component
    {
        public override VNode Render() => new VPanel
        {
            Dock = DockStyle.Fill,
            Children = [new VLabel { Text = "inside" }],
        };
    }
}
