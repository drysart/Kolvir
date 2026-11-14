using Kolvir;
using SkiaSharp;

namespace Kolvir.Tests;

/// <summary>
/// The border contract: what a frame takes, what it clips, and which colour it takes for which
/// state.
/// </summary>
/// <remarks>
/// The tests drive borders through a control rather than calling them in isolation, because the parts
/// that matter - <see cref="Control.ContentRect"/>, <see cref="Control.Measure"/> and the paint pass -
/// are the control's. <see cref="StatefulControl"/> stands in for the built-in controls that add a
/// state of their own.
/// </remarks>
[TestClass]
public sealed class BorderTests : HeadlessTestBase
{
    [TestMethod]
    public void AControlStartsWithoutAFrameAndAFrameThatTakesNothing()
    {
        using var control = new Control { Padding = new Thickness(4f) };

        Assert.AreSame(Border.None, control.Border, "the initial border is the shared no-op");

        var insets = control.Border.Insets(control);
        Assert.AreEqual(Thickness.Empty, insets, "no border takes no room");
        Assert.AreEqual(new SKRect(4f, 4f, 196f, 46f), ContentRectOf(control, 200f, 50f), "the padding is the whole of the gutter");
        Assert.IsTrue(control.Border.IsRectangular);
        Assert.IsFalse(control.Border.NarrowsClip);
        Assert.AreEqual(0f, control.Border.CornerRadius);
    }

    [TestMethod]
    public void AFrameComesOutOfTheContentAndBackIntoTheMeasurement()
    {
        using var control = new Control { Padding = new Thickness(4f), Border = SimpleBorder.Thin };
        control.Measure(SKSize.Empty);
        control.Bounds = new SKRect(0f, 0f, 200f, 50f);

        Assert.AreEqual(new Thickness(1f), control.Border.Insets(control), "one pixel on every side");
        Assert.AreEqual(
            new SKRect(5f, 5f, 195f, 45f),
            ContentRectOf(control, 200f, 50f),
            "the frame is the outer gutter and the padding is inside it");

        var measured = control.Measure(new SKSize(1000f, 1000f));
        Assert.AreEqual(10f, measured.Width, "the frame's two pixels are added to the padding's eight");
        Assert.AreEqual(10f, measured.Height);
    }

    [TestMethod]
    public void AFrameThatOnlyRoundsTakesNoRoomAndNarrowsTheClip()
    {
        using var control = new Control { Padding = new Thickness(4f), Border = SimpleBorder.Rounded(6f) };

        Assert.AreEqual(Thickness.Empty, control.Border.Insets(control), "a stroke-less shape reserves nothing");
        Assert.AreEqual(6f, control.Border.CornerRadius);
        Assert.IsTrue(control.Border.NarrowsClip, "the corners let what is behind show through");
        Assert.IsFalse(control.Border.IsRectangular, "so the control is not opaque");
        Assert.IsFalse(control.Opaque);
    }

    [TestMethod]
    public void TheStateColoursAreTakenInOrder()
    {
        var border = SimpleBorder.Thin
            .WithThemeColor(ThemeColor.Border)
            .WithHoverColor(ThemeColor.SecondaryHover)
            .WithFocusColor(ThemeColor.Accent)
            .WithActiveColor(ThemeColor.AccentPressed)
            .WithPressedColor(ThemeColor.SecondaryPressed)
            .WithDisabledColor(ThemeColor.DisabledBackground);

        Assert.AreEqual(Theme.Border, border.ColorFor(ControlState.None), "nothing happening is the resting colour");
        Assert.AreEqual(Theme.SecondaryHover, border.ColorFor(ControlState.Hovered));
        Assert.AreEqual(Theme.Accent, border.ColorFor(ControlState.Focused | ControlState.Hovered), "focus is stronger than hover");
        Assert.AreEqual(Theme.AccentPressed, border.ColorFor(ControlState.Active | ControlState.Focused));
        Assert.AreEqual(Theme.SecondaryPressed, border.ColorFor(ControlState.Pressed | ControlState.Active | ControlState.Focused));
        Assert.AreEqual(Theme.DisabledBackground, border.ColorFor(ControlState.Disabled | ControlState.Pressed), "disabled wins outright");
    }

    [TestMethod]
    public void AStateColourIsNotTakenWhenItWasNeverGiven()
    {
        var border = SimpleBorder.Thin.WithFocusColor(ThemeColor.Accent);

        Assert.AreEqual(Theme.Border, border.ColorFor(ControlState.Hovered), "the resting colour stands in for a missing one");
        Assert.AreEqual(Theme.Accent, border.ColorFor(ControlState.Focused));
    }

    [TestMethod]
    public void AControlStateFollowsItsOwnFlags()
    {
        using var control = new Control();
        Assert.AreEqual(ControlState.None, control.State);

        control.Enabled = false;
        Assert.AreEqual(ControlState.Disabled, control.State);

        control.Enabled = true;
        control.IsPressed = true;
        Assert.AreEqual(ControlState.Pressed, control.State, "pressed is a state the control reports");
    }

    [TestMethod]
    public void AControlThatHasStatesOfItsOwnAddsThemToTheBase()
    {
        using var overlay = new StatefulControl();
        Assert.AreEqual(ControlState.None, overlay.State);

        overlay.OverlayShown = true;
        Assert.AreEqual(ControlState.Active, overlay.State);

        overlay.Enabled = false;
        Assert.AreEqual(
            ControlState.Active | ControlState.Disabled,
            overlay.State,
            "an override keeps the bits the base reports");
    }

    [TestMethod]
    public void AFramePaintsInsideTheControl()
    {
        var control = new Control { Dock = DockStyle.Fill, Border = SimpleBorder.Thin };
        using var scene = HeadlessScene.Create(control, 60f, 40f);

        var edge = scene.PixelAt(0, (int)control.Height / 2);

        Assert.AreEqual(Theme.Border, edge, "the frame's own pixel is the border role");
    }

    [TestMethod]
    public void AFramePaintsOverWhatItsChildrenDrewLast()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = Thickness.Empty };
        var child = new Control { Dock = DockStyle.Fill, BackColor = SKColors.Red, Border = Border.None };
        panel.Controls.Add(child);
        panel.Border = SimpleBorder.Thin;
        using var scene = HeadlessScene.Create(panel, 60f, 40f);

        Assert.AreEqual(Theme.Border, scene.PixelAt(0, 20), "the border is painted after the children");
        Assert.AreEqual(SKColors.Red, scene.PixelAt(30, 20), "and the child still fills the middle");
    }

    [TestMethod]
    public void AFrameThatTakesMoreRoomLaysOutAgain()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Thickness(2f) };
        var child = new Control { Dock = DockStyle.Top, Height = 10f, Border = Border.None };
        panel.Controls.Add(child);
        using var scene = HeadlessScene.Create(panel, 100f, 60f);

        float before = child.Left;
        panel.Border = new SimpleBorder(4f);

        Assert.AreEqual(before + 4f, child.Left, "a thicker frame moves the content in");
    }

    private static SKRect ContentRectOf(Control control, float width, float height)
    {
        control.Measure(SKSize.Empty);
        control.Bounds = new SKRect(0f, 0f, width, height);
        return control.ContentRect;
    }

    /// <summary>A control with a state of its own, the way a combo box has its open list.</summary>
    private sealed class StatefulControl : Control
    {
        public bool OverlayShown { get; set; }

        public override ControlState State => base.State | (OverlayShown ? ControlState.Active : ControlState.None);
    }
}
