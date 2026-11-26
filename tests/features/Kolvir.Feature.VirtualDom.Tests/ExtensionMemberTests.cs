using Kolvir;

namespace Kolvir.Feature.VirtualDom.Tests;

/// <summary>
/// The extension members a node exposes, end to end: set on the node, applied to a real control, read
/// back, and patched like a native member.
/// </summary>
/// <remarks>
/// These are the assertions that a source assertion cannot make. The generated code assigns through
/// the lowered static accessors rather than through C# 14 extension syntax, so what is worth proving is
/// that the control really ends up with the value, that dropping it restores the declared pristine
/// value, and that a dropped handler is really detached.
/// </remarks>
[TestClass]
[TestCategory("VirtualDom")]
public sealed class ExtensionMemberTests
{
    [TestMethod]
    public void AnExtensionPropertyOnANodeReachesItsControl()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VSlot { Slot = 3 });

        Assert.AreEqual(3, ((SlotControl)mount.Root!).SlotValue);
    }

    [TestMethod]
    public void AnExtensionPropertyDroppedByARenderRestoresItsDeclaredValue()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VSlot { Slot = 3 });
        var slot = (SlotControl)mount.Root!;

        Assert.AreEqual(3, slot.SlotValue);

        mount.Update(new VSlot());

        // 5, the value the property declared, rather than 9, which is what a control's constructor
        // would have said.
        Assert.AreEqual(5, slot.SlotValue);
    }

    [TestMethod]
    public void TheTablesCellExtensionsReachARealTableLayoutPanel()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VTableLayoutPanel
        {
            TableColumn = 1,
            TableRow = 2,
            TableColumnSpan = 3,
            TableRowSpan = 4,
        });

        var table = (TableLayoutPanel)mount.Root!;

        Assert.AreEqual(1, table.TableColumn);
        Assert.AreEqual(2, table.TableRow);
        Assert.AreEqual(3, table.TableColumnSpan);
        Assert.AreEqual(4, table.TableRowSpan);
    }

    [TestMethod]
    public void AnExtensionHandlerPairIsAttachedAndDetachedThroughAPatch()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        var raised = 0;
        Action<object> handler = _ => raised++;

        using var mount = VirtualDomRuntime.Mount(host, new VSlot { Pinged = handler });
        var slot = (SlotControl)mount.Root!;

        slot.Raise();
        Assert.AreEqual(1, raised, "the handler the node set should be attached");

        mount.Update(new VSlot());
        slot.Raise();
        Assert.AreEqual(1, raised, "a render that drops the handler should detach it");

        mount.Update(new VSlot { Pinged = handler });
        slot.Raise();
        Assert.AreEqual(2, raised, "and putting it back should attach it again");

        mount.Update(new VSlot { Pinged = _ => raised += 100 });
        slot.Raise();
        Assert.AreEqual(102, raised, "replacing the handler should detach the old one and attach the new one");
    }

    [TestMethod]
    public void ARenderThatRepeatsTheSameHandlerDoesNotAttachItTwice()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        var raised = 0;
        Action<object> handler = _ => raised++;

        using var mount = VirtualDomRuntime.Mount(host, new VSlot { Pinged = handler });
        var slot = (SlotControl)mount.Root!;

        // The same delegate, so nothing changed and nothing should be detached or attached again.
        mount.Update(new VSlot { Pinged = handler });
        slot.Raise();

        Assert.AreEqual(1, raised, "a patch that leaves the handler alone should not attach it a second time");
    }

    [TestMethod]
    public void AValueStoredWithoutInstallingItsMemberIsReportedRatherThanSkipped()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VSlot());

        // Written the way nothing in this repository writes it - a deserialiser or reflection would -
        // so the apply path finds a value for a member that was never installed.
        var next = new VSlot();
        next.Extensions[new object()] = (Action<object>)(_ => { });

        var exception = Assert.ThrowsException<InvalidOperationException>(() => mount.Update(next));

        StringAssert.Contains(exception.Message, "never installed");
    }
}
