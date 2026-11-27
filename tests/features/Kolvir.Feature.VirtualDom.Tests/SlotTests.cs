using Kolvir;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Feature.VirtualDom.Tests;

/// <summary>
/// Property slots: a node filling a control a property already holds, and a node making the control a
/// settable property should hold.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="SplitContainer"/> is the control this exists for. Its two panels are made by its own
/// constructor and added to its own <see cref="Control.Controls"/>, so a node cannot assign them - and
/// the mechanism must fill them without parent­ing them a second time, which every test here that touches
/// panelage asserts directly rather than inferring from "the contents are there".
/// </para>
/// <para>
/// The creating half needs a control with a settable control-typed property, which this library does not
/// have, so it is exercised through <see cref="HostControl"/>.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("VirtualDom")]
public sealed class SlotTests
{
    private static readonly Thickness PanelPadding = new(6f);

    private static readonly Thickness PristinePanelPadding = new Panel().Padding;

    private static readonly string PristinePanelName = new Panel().Name;

    [TestMethod]
    public void BothPanelsGetTheirContentsAndTheContainerStillHoldsOnlyItsOwnPanels()
    {
        using var split = new SplitSession(new VSplitContainer
        {
            Panel1 = new VPanel
            {
                Padding = PanelPadding,
                Children = [new VLabel { Text = "left" }],
            },
            Panel2 = new VPanel
            {
                Name = "right-panel",
                Children = [new VLabel { Text = "right" }],
            },
        });

        Assert.AreEqual(
            2,
            split.Container.Controls.Count,
            "the container should hold exactly the two panels its constructor added - a slot control is not a child");

        Assert.AreSame(split.Container.Panel1, split.Container.Controls[0], "the first of them is Panel1");
        Assert.AreSame(split.Container.Panel2, split.Container.Controls[1], "and the second is Panel2");
        Assert.AreSame(split.Container, split.Container.Panel1.Parent, "the panel belongs to the container");

        Assert.AreEqual(1, split.Panel1.Controls.Count, "the node's children belong inside the panel");
        Assert.AreSame(split.Panel1, split.Panel1.Controls[0].Parent, "and are parented to it");
        Assert.AreEqual("left", split.Panel1.Controls[0].Text);
        Assert.AreEqual(
            PanelPadding,
            split.Panel1.Padding,
            "a borrowed slot's node describes the panel itself, not a control inside it");

        Assert.AreEqual("right-panel", split.Panel2.Name, "and the second slot's properties went to its own panel");
        Assert.AreEqual("right", split.Panel2.Controls[0].Text);
        Assert.AreEqual(PristinePanelPadding, split.Panel2.Padding, "a property the node did not set is left alone");
    }

    [TestMethod]
    public void APatchInsideASlotPatchesInsteadOfRebuilding()
    {
        using var split = new SplitSession(new VSplitContainer
        {
            Panel1 = new VPanel { Children = [new VLabel { Text = "before" }] },
        });

        var panel = split.Panel1;
        var label = split.Panel1.Controls[0];

        split.Update(new VSplitContainer
        {
            Panel1 = new VPanel { Children = [new VLabel { Text = "after" }] },
        });

        Assert.AreSame(panel, split.Panel1, "the panel the container made is the slot");
        Assert.AreSame(label, split.Panel1.Controls[0], "and the child inside it was patched, not rebuilt");
        Assert.AreEqual("after", split.Panel1.Controls[0].Text);
        Assert.AreEqual(2, split.Container.Controls.Count, "with nothing added to the container");
    }

    [TestMethod]
    public void ABorrowedSlotIsNeverReplacedByThePanelItSitsIn()
    {
        using var split = new SplitSession(new VSplitContainer
        {
            Panel1 = new VPanel { Padding = new Thickness(3f) },
        });

        var panel = split.Panel1;

        split.Update(new VSplitContainer
        {
            Panel1 = new VPanel { Padding = new Thickness(9f) },
        });

        Assert.AreSame(panel, split.Panel1, "a borrowed slot is never replaced, because the container owns it");
        Assert.AreEqual(new Thickness(9f), split.Panel1.Padding, "the new node was applied to the same panel");
    }

    [TestMethod]
    public void ASlotThatBecomesNullEmptiesThePanelAndRestoresItsProperties()
    {
        using var split = new SplitSession(new VSplitContainer
        {
            Panel1 = new VPanel
            {
                Padding = PanelPadding,
                Children = [new VLabel { Text = "gone" }],
            },
        });

        var panel = split.Panel1;
        Assert.AreEqual(PanelPadding, panel.Padding);

        split.Update(new VSplitContainer());

        Assert.AreEqual(0, panel.Controls.Count, "the node's contents come off the panel");
        Assert.AreSame(split.Container, panel.Parent, "and the panel stays where the container put it");
        Assert.AreEqual(
            PristinePanelPadding,
            panel.Padding,
            "the panel goes back to what the container's constructor gave it rather than keeping the removed node's value");
        Assert.AreEqual(2, split.Container.Controls.Count);
    }

    [TestMethod]
    public void ASlotInsideASlotWorksThroughACreatedControl()
    {
        using var host = new HostSession(new VHost
        {
            // A creating slot whose node has slots of its own: the split container the reconciler makes
            // here has two borrowed panels, and each of them is filled in turn.
            Split = new VSplitContainer
            {
                Panel1 = new VPanel { Children = [new VLabel { Text = "deep-left" }] },
                Panel2 = new VPanel { Children = [new VLabel { Text = "deep-right" }] },
            },
        });

        var split = host.Control.Split;
        Assert.IsNotNull(split, "the creating slot should have made the split container");
        Assert.AreEqual(2, split!.Controls.Count, "the inner container holds its own two panels and nothing else");
        Assert.AreSame(split, split.Panel1.Parent, "and belongs to them");
        Assert.AreEqual("deep-left", split.Panel1.Controls[0].Text);
        Assert.AreEqual("deep-right", split.Panel2.Controls[0].Text);

        var innerLabel = split.Panel1.Controls[0];

        host.Update(new VHost
        {
            Split = new VSplitContainer
            {
                Panel1 = new VPanel { Children = [new VLabel { Text = "changed" }] },
            },
        });

        Assert.AreSame(split, host.Control.Split, "the outer slot patched the control it made");
        Assert.AreSame(innerLabel, split.Panel1.Controls[0], "and the inner slot patched the label inside it");
        Assert.AreEqual("changed", split.Panel1.Controls[0].Text);
        Assert.AreEqual(0, split.Panel2.Controls.Count, "the second inner slot emptied");

        host.Update(new VHost());

        Assert.AreSame(split, host.Control.Split, "the created control stays in the property, emptied");
        Assert.AreEqual(0, split.Panel1.Controls.Count, "and the whole subtree came off with it");
    }

    [TestMethod]
    public void ACovariantNodeFillsACreatingSlot()
    {
        using var host = new HostSession(new VHost
        {
            // IVNodeFor<TableLayoutPanel> assigning to an IVNodeFor<Panel> slot: the variance conversion.
            Header = new VTableLayoutPanel
            {
                ColumnStyles = [new ColumnStyle(SizeType.Percent, 100f)],
                Children = [new VLabel { Text = "grid" }],
            },
        });

        Assert.IsInstanceOfType<TableLayoutPanel>(
            host.Control.Header,
            "the slot creates the node's own control, which is what makes covariance sound here");

        Assert.AreEqual("grid", host.Control.Header.Controls[0].Text);
    }

    [TestMethod]
    public void ACreatingSlotPatchesInPlaceAndReplacesOnATypeChange()
    {
        using var host = new HostSession(new VHost
        {
            Header = new VPanel { Children = [new VLabel { Text = "one" }] },
        });

        var header = host.Control.Header;
        var label = header.Controls[0];

        host.Update(new VHost
        {
            Header = new VPanel { Children = [new VLabel { Text = "two" }] },
        });

        Assert.AreSame(header, host.Control.Header, "an unchanged node class patches the control the slot made");
        Assert.AreSame(label, host.Control.Header.Controls[0], "and its children with it");
        Assert.AreEqual("two", host.Control.Header.Controls[0].Text);

        host.Update(new VHost { Header = new VTableLayoutPanel() });

        Assert.AreNotSame(header, host.Control.Header, "a different node class replaces it");
        Assert.IsInstanceOfType<TableLayoutPanel>(host.Control.Header);
    }

    [TestMethod]
    public void ACreatingSlotLeavesTheControlItMadeWhenTheNodeGoesAway()
    {
        using var host = new HostSession(new VHost
        {
            Header = new VPanel { Children = [new VLabel { Text = "kept" }] },
            Body = new VPanel { Children = [new VLabel { Text = "body" }] },
        });

        var header = host.Control.Header;
        var body = host.Control.Body;

        host.Update(new VHost());

        Assert.AreSame(header, host.Control.Header, "the property keeps the control the slot made");
        Assert.AreEqual(0, header.Controls.Count, "with its subtree unmounted");
        Assert.AreSame(body, host.Control.Body, "and a nullable one behaves the same way");
        Assert.AreEqual(0, body!.Controls.Count);
    }

    [TestMethod]
    public void TheSameNodeInstanceInTwoSlotsMountsTwoIndependentSubtrees()
    {
        var shared = new VPanel { Children = [new VLabel { Text = "shared" }] };

        using var split = new SplitSession(new VSplitContainer { Panel1 = shared, Panel2 = shared });

        Assert.AreEqual("shared", split.Panel1.Controls[0].Text);
        Assert.AreEqual("shared", split.Panel2.Controls[0].Text);
        Assert.AreNotSame(
            split.Panel1.Controls[0],
            split.Panel2.Controls[0],
            "a node is a description, so each slot mounts its own control from it");

        var left = split.Panel1.Controls[0];

        split.Update(new VSplitContainer
        {
            Panel1 = new VPanel { Children = [new VLabel { Text = "changed" }] },
            Panel2 = shared,
        });

        Assert.AreSame(left, split.Panel1.Controls[0], "patching one slot leaves the other's control alone");
        Assert.AreEqual("changed", split.Panel1.Controls[0].Text);
        Assert.AreEqual("shared", split.Panel2.Controls[0].Text);
    }

    [TestMethod]
    public void ASharedNodeCarryingAComponentFailsByNameAtTheSecondMount()
    {
        var caption = new Caption();
        var shared = new VPanel { Children = [new VComponent(caption)] };

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            VirtualDomRuntime.Mount(new Panel(), new VSplitContainer { Panel1 = shared, Panel2 = shared }));

        StringAssert.Contains(
            exception.Message,
            "already mounted",
            "what a node instance cannot carry into two slots is a component, and it says so by name");
    }

    [TestMethod]
    public void TheSharedAllUnassignedInstanceCarriesNothingBetweenRemovals()
    {
        // Two mounts in one scene, so a state left on the static instance by one removal would be
        // visible in the other. Each round removes and re-fills, twice, which is where a carried value
        // would show as a panel that kept the previous round's padding instead of the one its own node
        // asked for.
        var root = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(root);

        var leftHost = new Panel { Dock = DockStyle.Left };
        var rightHost = new Panel { Dock = DockStyle.Right };
        root.Controls.Add(leftHost);
        root.Controls.Add(rightHost);

        using var left = VirtualDomRuntime.Mount(leftHost, new VSplitContainer { Panel1 = new VPanel { Padding = new Thickness(7f) } });
        using var right = VirtualDomRuntime.Mount(rightHost, new VSplitContainer { Panel1 = new VPanel { Padding = new Thickness(11f) } });

        var leftSplit = (SplitContainer)left.Root!;
        var rightSplit = (SplitContainer)right.Root!;

        for (int round = 0; round < 2; round++)
        {
            var leftPadding = new Thickness(5f + round);
            var rightPadding = new Thickness(13f + round);

            left.Update(new VSplitContainer());
            right.Update(new VSplitContainer());

            Assert.AreEqual(PristinePanelPadding, leftSplit.Panel1.Padding, $"round {round}: the first panel is pristine again");
            Assert.AreEqual(PristinePanelPadding, rightSplit.Panel1.Padding, $"round {round}: and so is the second");

            left.Update(new VSplitContainer { Panel1 = new VPanel { Padding = leftPadding } });
            right.Update(new VSplitContainer { Panel1 = new VPanel { Padding = rightPadding } });

            Assert.AreEqual(leftPadding, leftSplit.Panel1.Padding, $"round {round}: the first panel takes its own node's padding");
            Assert.AreEqual(rightPadding, rightSplit.Panel1.Padding, $"round {round}: and the second its own");
        }
    }

    [TestMethod]
    public void SlotsArePairedByDeclarationOrderNotByDiscovery()
    {
        // Both slots take the same node class, because a borrowed slot is typed with the one node class
        // for the control it holds - so a mispairing is only visible through something that lands on the
        // control itself. Name does: whichever panel ends up called "right" is the one slot 2 was paired
        // with, and a run that paired slot 1 with the wrong panel would put it on the left one.
        using var split = new SplitSession(new VSplitContainer
        {
            Panel1 = new VPanel { Name = "left" },
            Panel2 = new VPanel { Name = "right" },
        });

        Assert.AreEqual("left", split.Panel1.Name, "the first slot's node went to the first panel");
        Assert.AreEqual("right", split.Panel2.Name, "the second slot's node went to the second panel");

        var panel2 = split.Panel2;

        // The first slot is empty now and the second is not, which is where a list that indexed by "the
        // slots that happened to be filled" rather than by slot would pair them wrongly.
        split.Update(new VSplitContainer
        {
            Panel2 = new VPanel { Name = "right-again" },
        });

        Assert.AreEqual(PristinePanelName, split.Panel1.Name, "the first slot emptied its own panel");
        Assert.AreSame(panel2, split.Panel2, "and the second slot kept the panel it was paired with");
        Assert.AreEqual("right-again", split.Panel2.Name);

        split.Update(new VSplitContainer
        {
            Panel1 = new VPanel { Name = "left" },
            Panel2 = new VPanel { Name = "right" },
        });

        Assert.AreEqual("left", split.Panel1.Name, "and a slot that refills goes back to its own panel");
        Assert.AreEqual("right", split.Panel2.Name);
    }

    private sealed class SplitSession : IDisposable
    {
        private readonly Panel _host = new() { Dock = DockStyle.Fill };
        private readonly Scene _scene;
        private readonly MountHandle _mount;

        internal SplitSession(VNode root)
        {
            _scene = Scene.Create(_host);
            _mount = VirtualDomRuntime.Mount(_host, root);
            Container = (SplitContainer)_mount.Root!;
        }

        internal SplitContainer Container { get; }

        internal Panel Panel1 => Container.Panel1;

        internal Panel Panel2 => Container.Panel2;

        internal void Update(VNode root) => _mount.Update(root);

        public void Dispose()
        {
            _mount.Dispose();
            _scene.Dispose();
        }
    }

    private sealed class HostSession : IDisposable
    {
        private readonly Panel _host = new() { Dock = DockStyle.Fill };
        private readonly Scene _scene;
        private readonly MountHandle _mount;

        internal HostSession(VNode root)
        {
            _scene = Scene.Create(_host);
            _mount = VirtualDomRuntime.Mount(_host, root);
            Control = (HostControl)_mount.Root!;
        }

        internal HostControl Control { get; }

        internal void Update(VNode root) => _mount.Update(root);

        public void Dispose()
        {
            _mount.Dispose();
            _scene.Dispose();
        }
    }

    /// <summary>A component with nothing to say, for the case where the same node instance names it twice.</summary>
    private sealed class Caption : Component
    {
        public override VNode Render() => new VLabel { Text = "painted" };
    }
}

