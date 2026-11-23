using SkiaSharp;
using Kolvir;

namespace Kolvir.Feature.VirtualDom.Tests;

/// <summary>
/// Mounting a tree, and updating it: what a control becomes, what a property means, and when a
/// control survives a render.
/// </summary>
[TestClass]
[TestCategory("VirtualDom")]
public sealed class MountTests
{
    [TestMethod]
    public void MountingATreeCreatesItsControls()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VPanel
        {
            Children = [new VLabel { Text = "first" }, new VLabel { Text = "second" }],
        });

        var panel = mount.Root as Panel;
        Assert.IsNotNull(panel, "the root node should have made a panel");
        Assert.AreSame(host, panel.Parent, "and it should be attached to the mount point");
        Assert.AreEqual(2, panel.Controls.Count);
        Assert.AreEqual("first", panel.Controls[0].Text);
        Assert.AreEqual("second", panel.Controls[1].Text);
    }

    [TestMethod]
    public void AMountedTreeIsLaidOutByTheHost()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VPanel
        {
            Dock = DockStyle.Fill,
            Children = [new VButton { Dock = DockStyle.Fill, Text = "go" }],
        });

        scene.Layout();

        var button = ((Panel)mount.Root!).Controls[0];
        Assert.AreSame(host, mount.Root!.Parent, "the root fills the mount point");
        Assert.IsTrue(button.Bounds.Width > 0f, $"a mounted control should be laid out, was {button.Bounds}");
    }

    [TestMethod]
    public void APropertyTheNodeDoesNotSetIsLeftAlone()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VNote());

        Assert.AreEqual(NoteControl.Pristine, ((NoteControl)mount.Root!).Note, "an unset property keeps what the constructor gave it");
    }

    [TestMethod]
    public void UpdatingReusesTheControlForTheSameKindOfNode()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VLabel { Text = "before" });
        var label = mount.Root;

        mount.Update(new VLabel { Text = "after" });

        Assert.AreSame(label, mount.Root, "the same node class should patch the same control");
        Assert.AreEqual("after", mount.Root!.Text);
        Assert.AreEqual(1, host.Controls.Count, "and should not leave a second control behind");
    }

    [TestMethod]
    public void APropertyThatStopsBeingSetGoesBackToItsPristineValue()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VNote { Note = "set" });
        Assert.AreEqual("set", ((NoteControl)mount.Root!).Note);

        mount.Update(new VNote());

        Assert.AreEqual(
            NoteControl.Pristine,
            ((NoteControl)mount.Root!).Note,
            "dropping a property restores what the constructor gave the control, not default(T)");
    }

    [TestMethod]
    public void AnAssignedNullIsAValueAndNotAnAbsence()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VNote { Note = null });
        Assert.IsNull(((NoteControl)mount.Root!).Note, "a nullable property can be set to null");

        mount.Update(new VNote { Note = null });
        Assert.IsNull(((NoteControl)mount.Root!).Note, "and staying null is not the same as being dropped");

        mount.Update(new VNote());
        Assert.AreEqual(NoteControl.Pristine, ((NoteControl)mount.Root!).Note, "dropping it is what restores the pristine value");
    }

    [TestMethod]
    public void AChangedValueTypeIsAssignedAndAnUnchangedOneIsNot()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        var padding = new Thickness(4f, 6f, 4f, 6f);

        using var mount = VirtualDomRuntime.Mount(host, new VLabel { Padding = padding });
        Assert.AreEqual(padding, mount.Root!.Padding);

        mount.Update(new VLabel { Padding = padding });
        Assert.AreEqual(padding, mount.Root!.Padding, "an unchanged value is not re-assigned and is still right");

        mount.Update(new VLabel());
        Assert.AreEqual(new Label().Padding, mount.Root!.Padding, "and dropping it restores the pristine padding");
    }

    [TestMethod]
    public void ADifferentRootKindReplacesTheControlInPlace()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        host.Controls.Add(new Panel());
        using var mount = VirtualDomRuntime.Mount(host, new VLabel { Text = "label" });

        Assert.AreEqual(2, host.Controls.Count, "a sibling plus the mounted control");

        mount.Update(new VNote { Note = "note" });

        Assert.IsInstanceOfType<NoteControl>(mount.Root);
        Assert.AreEqual(2, host.Controls.Count, "the replacement happens in the mounted slot, not beside it");
        Assert.AreSame(mount.Root, host.Controls[1], "and the mounted control is still the host's second child");
        Assert.AreEqual("note", ((NoteControl)mount.Root!).Note);
    }

    [TestMethod]
    public void UnmountingRemovesTheControl()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        var mount = VirtualDomRuntime.Mount(host, new VLabel { Text = "gone" });
        var label = mount.Root;

        mount.Unmount();

        Assert.AreEqual(0, host.Controls.Count);
        Assert.IsNull(label!.Parent);
        Assert.IsFalse(mount.IsMounted);

        mount.Dispose();
    }

    [TestMethod]
    public void AnUnmountedHandleMountsAgainOnTheNextUpdate()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VLabel { Text = "first" });
        mount.Unmount();
        mount.Update(new VLabel { Text = "second" });

        Assert.IsTrue(mount.IsMounted);
        Assert.AreEqual("second", host.Controls[0].Text);
    }

    [TestMethod]
    public void MountingNeedsAParentAndATree()
    {
        using var panel = new Panel();

        Assert.ThrowsExactly<ArgumentNullException>(() => VirtualDomRuntime.Mount(null!, new VPanel()));
        Assert.ThrowsExactly<ArgumentNullException>(() => VirtualDomRuntime.Mount(panel, (VNode)null!));

        using var mount = VirtualDomRuntime.Mount(panel, new VPanel());
        Assert.ThrowsExactly<ArgumentNullException>(() => mount.Update(null!));
    }
}
