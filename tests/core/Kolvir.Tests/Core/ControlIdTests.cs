using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The optional identifier a control can carry, and the lookup that finds a control by it.
/// </summary>
/// <remarks>
/// The property is the least interesting part: it is a string that nothing in the library reads. What
/// is worth pinning is what the lookup promises - this control and everything under it, nothing
/// outside it, controls that are in the tree but not on screen, and a definite answer when a tree
/// breaks the uniqueness the property asks for but does not enforce.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class ControlIdTests : HeadlessTestBase
{
    [TestMethod]
    public void AControlHasNoIdUntilOneIsSet()
    {
        using var control = new Label("Text");

        Assert.IsNull(control.Id);

        control.Id = "greeting";
        Assert.AreEqual("greeting", control.Id);

        control.Id = null;
        Assert.IsNull(control.Id, "clearing it is allowed, because nothing keeps a registry to withdraw from");
    }

    [TestMethod]
    public void AControlFindsItself()
    {
        using var control = new Label("Text") { Id = "greeting" };

        Assert.IsTrue(control.TryGetControlById("greeting", out var found));
        Assert.AreSame(control, found);
    }

    [TestMethod]
    public void AControlFindsAControlUnderIt()
    {
        using var label = new Label("Text") { Id = "greeting" };
        using var middle = new Panel();
        using var root = new Panel();

        middle.Controls.Add(label);
        root.Controls.Add(middle);

        Assert.IsTrue(root.TryGetControlById("greeting", out var found));
        Assert.AreSame(label, found, "the walk goes all the way down, not one level");
    }

    [TestMethod]
    public void ALookupThatFindsNothingReportsIt()
    {
        using var label = new Label("Text") { Id = "greeting" };
        using var root = new Panel();

        root.Controls.Add(label);

        Assert.IsFalse(root.TryGetControlById("goodbye", out var found));
        Assert.IsNull(found);
    }

    [TestMethod]
    public void AControlIsNotFoundFromBesideItsSubtree()
    {
        // The sibling is outside the subtree the lookup was asked about, which is the boundary the
        // method name describes; a window is how an application searches the whole tree.
        using var label = new Label("Text") { Id = "greeting" };
        using var first = new Panel();
        using var second = new Panel();
        using var root = new Panel();

        first.Controls.Add(label);
        root.Controls.Add(first);
        root.Controls.Add(second);

        Assert.IsFalse(second.TryGetControlById("greeting", out _));
        Assert.IsTrue(root.TryGetControlById("greeting", out var found));
        Assert.AreSame(label, found);
    }

    [TestMethod]
    public void ACaseDifferenceIsADifferentId()
    {
        using var label = new Label("Text") { Id = "greeting" };

        Assert.IsFalse(label.TryGetControlById("Greeting", out _), "ids are compared ordinally");
        Assert.IsTrue(label.TryGetControlById("greeting", out _));
    }

    [TestMethod]
    public void AControlThatIsNotOnScreenIsStillFound()
    {
        // "Is it in the tree" is the question an id answers; a collapsed panel, an unselected tab and
        // a scrolled-out row all still hold controls that code needs to reach.
        using var label = new Label("Text") { Id = "greeting" };
        using var hidden = new Panel { Visible = false };
        using var root = new Panel();

        hidden.Controls.Add(label);
        root.Controls.Add(hidden);

        using var scene = HeadlessScene.Create(root, 200f, 200f);

        Assert.IsFalse(label.IsDisplayed, "the control is not on screen");
        Assert.IsTrue(root.TryGetControlById("greeting", out var found));
        Assert.AreSame(label, found);
    }

    [TestMethod]
    public void OneOfTheMatchingControlsIsFoundWhenIdsAreNotUnique()
    {
        // Uniqueness is expected but not enforced, so the promise is only that a control with that id
        // comes back - which one is a detail of the walk and not something to depend on.
        using var first = new Label("First") { Id = "duplicate" };
        using var second = new Label("Second") { Id = "duplicate" };
        using var root = new Panel();

        root.Controls.Add(first);
        root.Controls.Add(second);

        Assert.IsTrue(root.TryGetControlById("duplicate", out var found));
        Assert.AreEqual("duplicate", found!.Id);
        Assert.IsTrue(ReferenceEquals(found, first) || ReferenceEquals(found, second));
    }

    [TestMethod]
    public void ALookupRejectsANullId()
    {
        using var control = new Label("Text");
        using var scene = HeadlessScene.Create(control, 200f, 200f);

        Assert.ThrowsExactly<ArgumentNullException>(() => control.TryGetControlById(null!, out _));
        Assert.ThrowsExactly<ArgumentNullException>(() => scene.Window.TryGetControlById(null!, out _));
    }

    [TestMethod]
    public void AWindowFindsAControlAnywhereInItsTree()
    {
        using var label = new Label("Text") { Id = "greeting" };
        using var inner = new Panel();
        using var root = new Panel();

        inner.Controls.Add(label);
        root.Controls.Add(inner);

        using var scene = HeadlessScene.Create(root, 320f, 200f);

        Assert.IsTrue(scene.Window.TryGetControlById("greeting", out var found));
        Assert.AreSame(label, found);
    }

    [TestMethod]
    public void AWindowWithNoContentFindsNothing()
    {
        // A window is constructed before its content in the usual pattern, so the lookup has to answer
        // rather than throw in that moment.
        using var scene = HeadlessScene.Create(null, 200f, 200f);

        Assert.IsFalse(scene.Window.TryGetControlById("anything", out var found));
        Assert.IsNull(found);
    }

    [TestMethod]
    public void AWindowFindsTheRootItself()
    {
        using var root = new Panel { Id = "root" };
        using var scene = HeadlessScene.Create(root, 200f, 200f);

        Assert.IsTrue(scene.Window.TryGetControlById("root", out var found));
        Assert.AreSame(root, found);
    }
}
