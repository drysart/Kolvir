using SkiaSharp;
using XPlatUI;

namespace XPlatUI.Tests;

/// <summary>
/// The collapsible section: the header toggles it, the content folds away, and the control shrinks
/// to its header and comes back at the height it had.
/// </summary>
[TestClass]
[TestCategory("Headless")]
public sealed class ExpanderTests : HeadlessTestBase
{
    [TestMethod]
    public void AnExpanderStartsWithItsContentLaidOut()
    {
        var (expander, child) = Build(out var scene);
        using var _ = scene;

        Assert.IsTrue(expander.IsExpanded);
        Assert.IsTrue(child.Bounds.Height > 0f, "the content is arranged below the header");
        Assert.IsTrue(child.GetPositionInHost().Y > expander.GetPositionInHost().Y, "and not under it");
    }

    [TestMethod]
    public void ClickingTheHeaderCollapsesItAndExpandingRestoresTheHeight()
    {
        var (expander, child) = Build(out var scene);
        using var _ = scene;

        float expanded = expander.Height;

        scene.ClickAt(HeaderPoint(expander));

        Assert.IsFalse(expander.IsExpanded, "the header toggles the section");
        Assert.IsTrue(expander.Height < expanded, "a collapsed expander is only its header");
        Assert.AreEqual(0f, child.Bounds.Height, "and the content is not arranged at all");

        scene.ClickAt(HeaderPoint(expander));

        Assert.IsTrue(expander.IsExpanded);
        Assert.AreEqual(expanded, expander.Height, "expanding puts back the height the section had");
        Assert.IsTrue(child.Bounds.Height > 0f, "and the content is laid out again");
    }

    [TestMethod]
    public void AClickInTheContentDoesNotToggleTheSection()
    {
        var (expander, child) = Build(out var scene);
        using var _ = scene;

        var box = (XPlatCheckBox)child;
        scene.Click(child);

        Assert.IsTrue(expander.IsExpanded, "a press that lands on the content belongs to the content");
        Assert.AreEqual(XPlatCheckState.Checked, box.CheckState, "and reaches it");
    }

    [TestMethod]
    public void TheKeyboardTogglesTheSection()
    {
        var (expander, _) = Build(out var scene);
        using var __ = scene;

        expander.Focus();
        scene.TypeKey(XPlatKey.Space);

        Assert.IsFalse(expander.IsExpanded, "Space collapses a focused section");

        scene.TypeKey(XPlatKey.Enter);

        Assert.IsTrue(expander.IsExpanded, "and Enter expands it again");
    }

    [TestMethod]
    public void TheArrowKeysFoldAndUnfoldIt()
    {
        var (expander, _) = Build(out var scene);
        using var __ = scene;

        expander.Focus();
        scene.TypeKey(XPlatKey.Left);

        Assert.IsFalse(expander.IsExpanded, "Left folds the section the way it folds a tree node");

        scene.TypeKey(XPlatKey.Right);

        Assert.IsTrue(expander.IsExpanded);
    }

    [TestMethod]
    public void TheExpandedChangedHandlerRunsOncePerChange()
    {
        var (expander, _) = Build(out var scene);
        using var __ = scene;

        int changes = 0;
        expander.AddExpandedChangedHandler(_ => changes++);

        expander.IsExpanded = false;
        expander.IsExpanded = false;
        expander.IsExpanded = true;

        Assert.AreEqual(2, changes, "assigning the value it already has is not a change");
    }

    [TestMethod]
    public void ADisabledExpanderTakesNoInput()
    {
        var (expander, _) = Build(out var scene);
        using var __ = scene;

        expander.Enabled = false;
        scene.ClickAt(HeaderPoint(expander));
        expander.Focus();
        scene.TypeKey(XPlatKey.Space);

        Assert.IsTrue(expander.IsExpanded, "nor the pointer nor the keyboard reaches a disabled section");
    }

    [TestMethod]
    public void MeasuringACollapsedExpanderIsJustItsHeader()
    {
        var (expander, _) = Build(out var scene);
        using var __ = scene;

        var room = new SKSize(400f, 400f);
        float expanded = expander.Measure(room).Height;

        expander.IsExpanded = false;
        float collapsed = expander.Measure(room).Height;

        Assert.IsTrue(collapsed > 0f, "a collapsed section still has its header");
        Assert.IsTrue(collapsed < expanded, "and nothing else");
    }

    [TestMethod]
    public void ACollapsedExpanderPaintsItsHeaderOnly()
    {
        var (expander, child) = Build(out var scene);
        using var __ = scene;

        expander.IsExpanded = false;
        scene.Paint();

        var surface = expander.Background.Color ?? XPlatTheme.Surface;
        var below = new SKPoint(
            expander.GetPositionInHost().X + 20f,
            expander.GetPositionInHost().Y + expander.Height + 12f);

        Assert.AreEqual(
            XPlatTheme.Background,
            scene.PixelAt((int)below.X, (int)below.Y),
            "the space the content used to occupy is no longer part of the section");
        Assert.AreEqual(0f, child.Bounds.Height);
        Assert.AreNotEqual(default, surface);
    }

    private static (XPlatExpander Expander, XPlatControl Child) Build(out HeadlessScene scene)
    {
        var expander = new XPlatExpander("Appearance")
        {
            Dock = DockStyle.Top,
            Height = 160f,
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        };

        var child = new XPlatCheckBox("Show grid") { Dock = DockStyle.Top, AutoSize = true };
        expander.Controls.Add(child);

        var root = new XPlatPanel
        {
            Dock = DockStyle.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = XPlatThickness.Empty,
        };

        root.Controls.Add(expander);
        scene = HeadlessScene.Create(root, 420f, 320f);
        return (expander, child);
    }

    private static SKPoint HeaderPoint(XPlatControl expander)
    {
        var origin = expander.GetPositionInHost();
        return new SKPoint(origin.X + 60f, origin.Y + 10f);
    }
}
