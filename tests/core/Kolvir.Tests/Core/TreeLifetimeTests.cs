using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The tree-lifetime notifications on <see cref="Control"/>: joining a tree, leaving one, and being
/// disposed. These are the only signals a control gives about its own lifetime without being a
/// container, and they are what work started for a control - an asset load, for one - cancels on.
/// </summary>
[TestClass]
[TestCategory("Core")]
public sealed class TreeLifetimeTests
{
    [TestMethod]
    public void AddingAChildRaisesAddedToTreeOnceWithTheControlAsSender()
    {
        using var parent = new Panel();
        using var child = new Control();
        var senders = new List<object>();
        Control? parentSeenByHandler = null;

        child.AddAddedToTreeHandler(sender =>
        {
            senders.Add(sender);
            parentSeenByHandler = ((Control)sender).Parent;
        });

        parent.Controls.Add(child);

        Assert.AreEqual(1, senders.Count, "adding a child is one notification");
        Assert.AreSame(child, senders[0], "the sender is the control that joined the tree");
        Assert.AreSame(parent, parentSeenByHandler, "the handler sees the new parent");
    }

    [TestMethod]
    public void RemovingAChildRaisesRemovedFromTreeOnceWithTheControlAsSender()
    {
        using var parent = new Panel();
        using var child = new Control();
        parent.Controls.Add(child);
        var senders = new List<object>();
        Control? parentSeenByHandler = child;

        child.AddRemovedFromTreeHandler(sender =>
        {
            senders.Add(sender);
            parentSeenByHandler = ((Control)sender).Parent;
        });

        parent.Controls.Remove(child);

        Assert.AreEqual(1, senders.Count, "removing a child is one notification");
        Assert.AreSame(child, senders[0], "the sender is the control that left the tree");
        Assert.IsNull(parentSeenByHandler, "the handler sees that the control has no parent");
    }

    [TestMethod]
    public void AssigningTheSameParentAgainRaisesNothing()
    {
        using var parent = new Panel();
        using var child = new Control();
        parent.Controls.Add(child);
        int added = 0;
        int removed = 0;

        child.AddAddedToTreeHandler(_ => added++);
        child.AddRemovedFromTreeHandler(_ => removed++);

        child.Parent = parent;

        Assert.AreEqual(0, added, "the reference did not change");
        Assert.AreEqual(0, removed, "the reference did not change");
    }

    [TestMethod]
    public void TheIndexerSetterRaisesRemovedThenAdded()
    {
        using var parent = new Panel();
        using var removed = new Button("removed");
        using var added = new Label();
        parent.Controls.Add(removed);
        var events = new List<(string Event, Control Sender)>();

        removed.AddRemovedFromTreeHandler(sender => events.Add(("removed", (Control)sender)));
        added.AddAddedToTreeHandler(sender => events.Add(("added", (Control)sender)));

        parent.Controls[0] = added;

        Assert.AreEqual(2, events.Count);
        Assert.AreEqual("removed", events[0].Event);
        Assert.AreSame(removed, events[0].Sender);
        Assert.AreEqual("added", events[1].Event);
        Assert.AreSame(added, events[1].Sender);
    }

    [TestMethod]
    public void ReparentingRaisesRemovedThenAdded()
    {
        using var first = new Panel();
        using var second = new Panel();
        using var child = new Control();
        first.Controls.Add(child);
        var events = new List<string>();

        child.AddRemovedFromTreeHandler(_ => events.Add("removed"));
        child.AddAddedToTreeHandler(_ => events.Add("added"));

        first.Controls.Remove(child);
        second.Controls.Add(child);

        CollectionAssert.AreEqual(new[] { "removed", "added" }, events);
    }

    [TestMethod]
    public void ClearRaisesRemovedFromTreeForEveryChild()
    {
        using var parent = new Panel();
        using var first = new Control();
        using var second = new Control();
        parent.Controls.Add(first);
        parent.Controls.Add(second);
        int firstRemoved = 0;
        int secondRemoved = 0;

        first.AddRemovedFromTreeHandler(_ => firstRemoved++);
        second.AddRemovedFromTreeHandler(_ => secondRemoved++);

        parent.Controls.Clear();

        Assert.AreEqual(1, firstRemoved);
        Assert.AreEqual(1, secondRemoved);
    }

    [TestMethod]
    public void DisposingRaisesDisposingOnceEvenWhenCalledTwice()
    {
        using var control = new Control();
        int raised = 0;

        control.AddDisposingHandler(_ => raised++);

        control.Dispose();
        control.Dispose();

        Assert.AreEqual(1, raised, "the disposal guard makes the notification once-only");
    }

    [TestMethod]
    public void DisposingAParentRemovesEachChildBeforeDisposingIt()
    {
        using var parent = new Panel();
        using var first = new Control();
        using var second = new Control();
        parent.Controls.Add(first);
        parent.Controls.Add(second);
        var firstEvents = new List<string>();
        var secondEvents = new List<string>();

        first.AddRemovedFromTreeHandler(_ => firstEvents.Add("removed"));
        first.AddDisposingHandler(_ => firstEvents.Add("disposing"));
        second.AddRemovedFromTreeHandler(_ => secondEvents.Add("removed"));
        second.AddDisposingHandler(_ => secondEvents.Add("disposing"));

        parent.Dispose();

        CollectionAssert.AreEqual(new[] { "removed", "disposing" }, firstEvents);
        CollectionAssert.AreEqual(new[] { "removed", "disposing" }, secondEvents);
    }

    [TestMethod]
    public void DisposingTheTokenUnsubscribes()
    {
        using var parent = new Panel();
        using var child = new Control();
        int raised = 0;

        var token = child.AddAddedToTreeHandler(_ => raised++);
        token.Dispose();

        parent.Controls.Add(child);

        Assert.AreEqual(0, raised);
    }

    [TestMethod]
    public void AHandlerThatAddsAChildFromInsideAddedToTreeDoesNotCorruptTheTree()
    {
        using var parent = new Panel();
        using var child = new Control();
        using var grandchild = new Control();
        int raised = 0;

        child.AddAddedToTreeHandler(sender =>
        {
            if (ReferenceEquals(sender, child))
            {
                raised++;
                ((Control)sender).Controls.Add(grandchild);
            }
        });

        parent.Controls.Add(child);

        Assert.AreEqual(1, raised);
        Assert.AreSame(parent, child.Parent);
        Assert.AreSame(child, grandchild.Parent);
        Assert.AreEqual(1, child.Controls.Count);
    }

    [TestMethod]
    public void AnAddedToTreeHandlerSeesTheInheritedStateAlreadyUpdated()
    {
        using var parent = new Panel { Enabled = false };
        using var child = new Control();
        bool? enabledSeenByHandler = null;

        child.AddAddedToTreeHandler(sender => enabledSeenByHandler = ((Control)sender).EffectiveEnabled);

        parent.Controls.Add(child);

        Assert.IsFalse(
            enabledSeenByHandler,
            "the notification is raised after the inherited state is re-read");
    }

    [TestMethod]
    public void AnExceptionFromAHandlerPropagates()
    {
        using var parent = new Panel();
        using var child = new Control();
        child.AddAddedToTreeHandler(_ => throw new InvalidOperationException("boom"));

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => parent.Controls.Add(child));

        Assert.AreEqual("boom", thrown.Message);
        Assert.AreSame(parent, child.Parent, "the tree had already changed when the handler ran");
    }
}
