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

    [TestMethod]
    public void CollapsingASectionMovesTheSectionBelowIt()
    {
        var first = new XPlatExpander("First") { Dock = DockStyle.Top, Height = 120f };
        var second = new XPlatExpander("Second") { Dock = DockStyle.Top, Height = 80f };

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

        root.Controls.Add(first);
        root.Controls.Add(second);

        using var scene = HeadlessScene.Create(root, 420f, 320f);

        float before = second.Top;

        first.IsExpanded = false;

        Assert.IsTrue(second.Top < before, "the section below belongs where the collapsed one ended");
        Assert.AreEqual(first.Bounds.Bottom, second.Top, "which is immediately under its header");
    }

    [TestMethod]
    public void SettingIsExpandedMovesTheProgressOnItsOwn()
    {
        var (expander, _) = Build(out var scene);
        using var __ = scene;

        Assert.AreEqual(1d, expander.ExpandProgress, 0.0001d, "an expander starts fully open");

        expander.IsExpanded = false;

        Assert.AreEqual(0d, expander.ExpandProgress, 0.0001d, "collapsing folds the progress with it, at once");

        expander.IsExpanded = true;

        Assert.AreEqual(1d, expander.ExpandProgress, 0.0001d, "and expanding puts it back the same way");
    }

    [TestMethod]
    public void AnOrdinaryExpanderNeverNeedsItsProgressWrittenToIt()
    {
        var (expander, child) = Build(out var scene);
        using var __ = scene;

        float open = expander.Height;

        scene.ClickAt(HeaderPoint(expander));

        Assert.IsFalse(expander.IsExpanded);
        Assert.AreEqual(0d, expander.ExpandProgress, 0.0001d, "the click alone folded the progress");
        Assert.AreEqual(0d, expander.ArrowRotationDegrees, 0.0001d, "the arrow points right");
        Assert.AreEqual(0f, child.Bounds.Height, "and the content is folded away");
        Assert.IsTrue(expander.Height < open, "the section is only its header");

        scene.ClickAt(HeaderPoint(expander));

        Assert.AreEqual(1d, expander.ExpandProgress, 0.0001d);
        Assert.AreEqual(90d, expander.ArrowRotationDegrees, 0.0001d, "the arrow points down");
        Assert.AreEqual(open, expander.Height, "and the height came back");
        Assert.IsTrue(child.Bounds.Height > 0f);
    }

    [TestMethod]
    public void TheArrowIsOneChevronTurnedByTheProgress()
    {
        var (expander, _) = Build(out var scene);
        using var __ = scene;

        Assert.AreEqual(90d, expander.ArrowRotationDegrees, 0.0001d, "open, the arrow points down");

        expander.ExpandProgress = 0.5d;

        Assert.AreEqual(45d, expander.ArrowRotationDegrees, 0.0001d, "halfway is a quarter of the way round");

        expander.ExpandProgress = 0d;

        Assert.AreEqual(0d, expander.ArrowRotationDegrees, 0.0001d, "folded, it points right");

        expander.ExpandProgress = 1d;

        Assert.AreEqual(90d, expander.ArrowRotationDegrees, 0.0001d, "and a whole turn lies exactly at the open end");
    }

    [TestMethod]
    public void AHalfOpenSectionIsExpandedAndHalfwayThere()
    {
        var (expander, child) = Build(out var scene);
        using var __ = scene;

        float open = expander.Height;

        expander.IsExpanded = false;
        float header = expander.Height;

        expander.IsExpanded = true;
        expander.ExpandProgress = 0.5d;

        Assert.IsTrue(expander.IsExpanded, "half open is still heading for open");
        Assert.AreEqual(0.5d, expander.ExpandProgress, 0.0001d);
        Assert.AreEqual(
            header + ((open - header) * 0.5f),
            expander.Height,
            0.5f,
            "the height is halfway between the header and the whole section");
        Assert.IsTrue(child.Bounds.Height > 0f, "the content is laid out in the room that is left");
        Assert.IsTrue(child.Bounds.Height < open - header, "which is less than all of it");
    }

    [TestMethod]
    public void AProgressOutsideTheRangeIsClamped()
    {
        var (expander, _) = Build(out var scene);
        using var __ = scene;

        float open = expander.Height;

        expander.IsExpanded = false;
        float header = expander.Height;
        expander.IsExpanded = true;

        expander.ExpandProgress = -2d;

        Assert.AreEqual(0d, expander.ExpandProgress, 0.0001d, "a negative progress folds the section");
        Assert.AreEqual(header, expander.Height, 0.5f);

        expander.ExpandProgress = 4d;

        Assert.AreEqual(1d, expander.ExpandProgress, 0.0001d, "and one past the top opens it fully");
        Assert.AreEqual(open, expander.Height, 0.5f);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => expander.ExpandProgress = double.NaN);
    }

    [TestMethod]
    public void TheProgressHandlerRunsOncePerChange()
    {
        var (expander, _) = Build(out var scene);
        using var __ = scene;

        int changes = 0;
        expander.AddExpandProgressChangedHandler(_ => changes++);

        expander.ExpandProgress = 0.5d;
        expander.ExpandProgress = 0.5d;
        expander.ExpandProgress = 0.75d;

        Assert.AreEqual(2, changes, "assigning the value it already has is not a change");

        expander.RemoveExpandProgressChangedHandler(_ => { });
        expander.ExpandProgress = 0d;

        Assert.AreEqual(3, changes, "and removing a handler that was never added does nothing");
    }

    [TestMethod]
    public void AHandlerOfTheExpansionChangeCanTakeTheProgressOver()
    {
        var (expander, _) = Build(out var scene);
        using var __ = scene;

        float open = expander.Height;
        expander.IsExpanded = false;
        float header = expander.Height;
        expander.IsExpanded = true;

        // What an animation of the fold does: on the change, hold the progress where it is and drive
        // it from there. The control must leave it alone rather than folding it to the target in the
        // same breath.
        expander.AddExpandedChangedHandler(sender =>
            ((XPlatExpander)sender).ExpandProgress = 0.25d);

        expander.IsExpanded = false;

        Assert.IsFalse(expander.IsExpanded, "the target is folded");
        Assert.AreEqual(0.25d, expander.ExpandProgress, 0.0001d, "but the progress is where the handler left it");
        Assert.AreEqual(header + ((open - header) * 0.25f), expander.Height, 0.5f, "and so is the height");
    }

    [TestMethod]
    public void APartlyOpenSectionMovesTheSectionBelowIt()
    {
        var first = new XPlatExpander("First") { Dock = DockStyle.Top, Height = 120f };
        var second = new XPlatExpander("Second") { Dock = DockStyle.Top, Height = 80f };

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

        root.Controls.Add(first);
        root.Controls.Add(second);

        using var scene = HeadlessScene.Create(root, 420f, 320f);

        float open = second.Top;

        first.ExpandProgress = 0.5d;
        float half = second.Top;

        first.ExpandProgress = 0d;

        Assert.IsTrue(half < open, "a fold in progress takes the section below with it");
        Assert.IsTrue(second.Top < half, "and so does a fold that finished");
        Assert.AreEqual(first.Bounds.Bottom, second.Top, "which always sits immediately under the fold");
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
