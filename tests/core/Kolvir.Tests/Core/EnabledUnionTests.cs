using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kolvir.Tests;

/// <summary>
/// <see cref="Control.Enabled"/> as a union: what the property reports, what
/// <see cref="Control.EffectiveEnabled"/> resolves through the parent chain, and when the enabled
/// handlers run.
/// </summary>
/// <remarks>
/// The resolution keeps the documented AND: a child under a disabled ancestor is effectively
/// disabled however its own value is assigned, so an explicit <c>true</c> is not an override.
/// <see cref="PropertyValue.Inherited"/> and <c>true</c> therefore resolve identically; the arm
/// exists so the property can report that nothing was assigned.
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class EnabledUnionTests : HeadlessTestBase
{
    [TestMethod]
    public void ANewControlInheritsAndResolvesEnabled()
    {
        using var control = new Control();

        Assert.IsTrue(
            control.Enabled.TryGetValue<InheritedValue>(out _),
            "an unassigned Enabled reads back as inherit");
        Assert.IsFalse(control.Enabled.TryGetValue<bool>(out _));
        Assert.IsTrue(control.EffectiveEnabled, "inheriting resolves true at the root");
    }

    [TestMethod]
    public void ThePropertyReportsTheAssignment()
    {
        using var control = new Control();

        control.Enabled = false;
        Assert.IsTrue(control.Enabled.TryGetValue<bool>(out var disabled));
        Assert.IsFalse(disabled, "the property reports the false that was assigned");
        Assert.IsFalse(control.EffectiveEnabled);

        control.Enabled = true;
        Assert.IsTrue(control.Enabled.TryGetValue<bool>(out var enabled));
        Assert.IsTrue(enabled, "and the true that was assigned");
        Assert.IsTrue(control.EffectiveEnabled);

        control.Enabled = PropertyValue.Inherited;
        Assert.IsTrue(control.Enabled.TryGetValue<InheritedValue>(out _));
        Assert.IsTrue(control.EffectiveEnabled);
    }

    [TestMethod]
    public void ADisabledContainerDisablesItsSubtree()
    {
        using var parent = new Panel();
        using var child = new Panel();
        using var grandchild = new Button("go");
        parent.Controls.Add(child);
        child.Controls.Add(grandchild);

        Assert.IsTrue(grandchild.EffectiveEnabled);

        parent.Enabled = false;

        Assert.IsFalse(child.EffectiveEnabled);
        Assert.IsFalse(grandchild.EffectiveEnabled);
        Assert.IsTrue(
            grandchild.Enabled.TryGetValue<InheritedValue>(out _),
            "the child's own value is untouched");

        parent.Enabled = PropertyValue.Inherited;

        Assert.IsTrue(grandchild.EffectiveEnabled, "and the subtree comes back");
    }

    [TestMethod]
    public void AnExplicitTrueDoesNotOverrideADisabledAncestor()
    {
        using var parent = new Panel();
        using var child = new Button("go");
        parent.Controls.Add(child);

        parent.Enabled = false;
        child.Enabled = true;

        Assert.IsTrue(child.Enabled.TryGetValue<bool>(out var assigned));
        Assert.IsTrue(assigned, "the property reports the explicit true");
        Assert.IsFalse(
            child.EffectiveEnabled,
            "the AND with the disabled ancestor still decides the answer");
    }

    [TestMethod]
    public void InheritingAgainRestoresTheEffectiveAnswer()
    {
        using var parent = new Panel();
        using var child = new Button("go");
        parent.Controls.Add(child);

        child.Enabled = false;
        Assert.IsFalse(child.EffectiveEnabled);

        child.Enabled = PropertyValue.Inherited;

        Assert.IsTrue(child.Enabled.TryGetValue<InheritedValue>(out _));
        Assert.IsTrue(child.EffectiveEnabled, "the enabled parent answers for the child again");
    }

    [TestMethod]
    public void ReparentingChangesTheEffectiveAnswer()
    {
        using var enabledParent = new Panel();
        using var disabledParent = new Panel { Enabled = false };
        using var child = new Button("go");
        enabledParent.Controls.Add(child);

        int changes = 0;
        child.AddEnabledChangedHandler(_ => changes++);
        Assert.IsTrue(child.EffectiveEnabled);

        enabledParent.Controls.Remove(child);
        disabledParent.Controls.Add(child);

        Assert.IsFalse(
            child.EffectiveEnabled,
            "the new parent disables it without touching its own value");
        Assert.AreEqual(1, changes);

        disabledParent.Controls.Remove(child);
        enabledParent.Controls.Add(child);

        Assert.IsTrue(child.EffectiveEnabled);
        Assert.AreEqual(2, changes);
    }

    [TestMethod]
    public void HandlersFireOnlyWhenTheEffectiveAnswerChanges()
    {
        using var parent = new Panel();
        using var child = new Button("go");
        parent.Controls.Add(child);

        int changes = 0;
        child.AddEnabledChangedHandler(_ => changes++);

        child.Enabled = true;
        Assert.AreEqual(0, changes, "inherit to explicit true resolves the same, so nothing fires");

        child.Enabled = PropertyValue.Inherited;
        Assert.AreEqual(0, changes, "and back again resolves the same, so nothing fires");

        parent.Enabled = false;
        Assert.AreEqual(1, changes, "disabling the parent changes the child's answer once");

        child.Enabled = true;
        Assert.AreEqual(1, changes, "an explicit true under a disabled ancestor resolves false");

        child.Enabled = PropertyValue.Inherited;
        Assert.AreEqual(1, changes, "so going back to inherit resolves false as well");

        parent.Enabled = PropertyValue.Inherited;
        Assert.AreEqual(2, changes, "re-enabling the parent resolves the inherited answer");

        child.Enabled = false;
        Assert.AreEqual(3, changes, "a local false is a real change");

        child.Enabled = false;
        Assert.AreEqual(3, changes, "assigning the same value again is not");
    }

    [TestMethod]
    public void ADefaultUnionIsRefusedWhenTheAnswerIsRead()
    {
        using var control = new Control();
        control.Enabled = default;

        Assert.ThrowsExactly<InvalidOperationException>(
            () => _ = control.EffectiveEnabled,
            "a default-initialised union holds no case and must not be read as inherit");
    }
}
