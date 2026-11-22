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
            .WithColor(ThemeColor.Border)
            .WithHoverColor(ThemeColor.SecondaryHover)
            .WithFocusColor(ThemeColor.Accent)
            .WithActiveColor(ThemeColor.AccentPressed)
            .WithPressedColor(ThemeColor.SecondaryPressed)
            .WithDisabledColor(ThemeColor.DisabledBackground);

        Assert.AreEqual(Theme.Border, border.ColorFor(BorderSide.Top, ControlState.None), "nothing happening is the resting colour");
        Assert.AreEqual(Theme.SecondaryHover, border.ColorFor(BorderSide.Top, ControlState.Hovered));
        Assert.AreEqual(Theme.Accent, border.ColorFor(BorderSide.Top, ControlState.Focused | ControlState.Hovered), "focus is stronger than hover");
        Assert.AreEqual(Theme.AccentPressed, border.ColorFor(BorderSide.Top, ControlState.Active | ControlState.Focused));
        Assert.AreEqual(Theme.SecondaryPressed, border.ColorFor(BorderSide.Top, ControlState.Pressed | ControlState.Active | ControlState.Focused));
        Assert.AreEqual(Theme.DisabledBackground, border.ColorFor(BorderSide.Top, ControlState.Disabled | ControlState.Pressed), "disabled wins outright");
    }

    [TestMethod]
    public void AStateColourIsNotTakenWhenItWasNeverGiven()
    {
        var border = SimpleBorder.Thin.WithFocusColor(ThemeColor.Accent);

        Assert.AreEqual(Theme.Border, border.ColorFor(BorderSide.Top, ControlState.Hovered), "the resting colour stands in for a missing one");
        Assert.AreEqual(Theme.Accent, border.ColorFor(BorderSide.Top, ControlState.Focused));
    }

    [TestMethod]
    public void AStateColourCoversEveryEdge()
    {
        var border = SimpleBorder.Thin
            .WithColor(BorderSide.Top, ThemeColor.Accent)
            .WithFocusColor(ThemeColor.AccentPressed);

        Assert.AreEqual(Theme.Accent, border.ColorFor(BorderSide.Top, ControlState.None));
        Assert.AreEqual(
            Theme.AccentPressed,
            border.ColorFor(BorderSide.Bottom, ControlState.Focused),
            "a state paints the whole frame, whatever the edges rest as");
    }

    [TestMethod]
    public void AnEdgeTakesItsOwnRestingColour()
    {
        var accent = new SKColor(1, 2, 3);
        var muted = new SKColor(4, 5, 6);
        var border = SimpleBorder.Thin
            .WithColor(BorderSide.Left, accent)
            .WithColor(BorderSide.Bottom, muted);

        Assert.AreEqual(accent, border.ColorFor(BorderSide.Left, ControlState.None));
        Assert.AreEqual(muted, border.ColorFor(BorderSide.Bottom, ControlState.None));
        Assert.AreEqual(Theme.Border, border.ColorFor(BorderSide.Right, ControlState.None), "an edge left alone keeps the palette role");
        Assert.AreEqual(Theme.Border, border.ColorFor(BorderSide.Top, ControlState.None));
    }

    [TestMethod]
    public void AnEdgeCanRestInAPaletteRole()
    {
        var border = SimpleBorder.Thin.WithColor(BorderSide.Left, ThemeColor.Accent);

        Assert.AreEqual(
            Theme.ColorOf(ThemeColor.Accent),
            border.ColorFor(BorderSide.Left, ControlState.None),
            "the role is resolved when the edge is read");
        Assert.AreEqual(Theme.Border, border.ColorFor(BorderSide.Right, ControlState.None), "the other edges keep the border role");
    }

    [TestMethod]
    public void AWholeFrameCanRestInAPaletteRole()
    {
        var border = SimpleBorder.Rounded(4f).WithColor(ThemeColor.Accent);
        var accent = Theme.ColorOf(ThemeColor.Accent);

        Assert.AreEqual(accent, border.ColorFor(BorderSide.Top, ControlState.None));
        Assert.AreEqual(accent, border.ColorFor(BorderSide.Right, ControlState.None));
        Assert.AreEqual(accent, border.ColorFor(BorderSide.Bottom, ControlState.None));
        Assert.AreEqual(accent, border.ColorFor(BorderSide.Left, ControlState.None));
    }

    [TestMethod]
    public void ALiteralAndARoleCanShareOneFrame()
    {
        var accent = new SKColor(1, 2, 3);
        var border = SimpleBorder.Thin
            .WithColor(BorderSide.Left, accent)
            .WithColor(BorderSide.Top, ThemeColor.MutedForeground);

        Assert.AreEqual(accent, border.ColorFor(BorderSide.Left, ControlState.None), "a literal is still used as it is");
        Assert.AreEqual(Theme.ColorOf(ThemeColor.MutedForeground), border.ColorFor(BorderSide.Top, ControlState.None));
    }

    [TestMethod]
    public void AnUnspecifiedEdgeColourIsRefused()
    {
        var border = SimpleBorder.Thin.WithColor(BorderSide.Top, default(Union<SKColor, ThemeColor>));

        Assert.ThrowsExactly<InvalidOperationException>(
            () => _ = border.ColorFor(BorderSide.Top, ControlState.None),
            "a default-initialised union holds no case and must not be read as the border role");
    }

    [TestMethod]
    public void AWidthPerSideIsWhatTheContentGivesUp()
    {
        var border = new SimpleBorder(new Thickness(4f, 1f, 2f, 8f), 0f);
        using var control = new Control { Padding = new Thickness(3f), Border = border };

        Assert.AreEqual(new Thickness(4f, 1f, 2f, 8f), border.Insets(control), "the frame takes what each edge weighs");
        Assert.AreEqual(
            new SKRect(7f, 4f, 195f, 39f),
            ContentRectOf(control, 200f, 50f),
            "the padding sits inside the asymmetric frame");

        var measured = control.Measure(new SKSize(1000f, 1000f));
        Assert.AreEqual(12f, measured.Width, "left 8 and right 5 from the frame, six from the padding");
        Assert.AreEqual(15f, measured.Height, "top 1 and bottom 8 from the frame, six from the padding");
    }

    [TestMethod]
    public void AnAsymmetricFramePaintsThePixelsItReserved()
    {
        var control = new Control
        {
            Dock = DockStyle.Fill,
            Border = new SimpleBorder(new Thickness(6f, 2f, 1f, 1f), 0f),
        };
        using var scene = HeadlessScene.Create(control, 60f, 40f);

        Assert.AreEqual(Theme.Border, scene.PixelAt(0, 20), "the left edge's first pixel");
        Assert.AreEqual(Theme.Border, scene.PixelAt(5, 20), "and its last");
        Assert.AreNotEqual(Theme.Border, scene.PixelAt(6, 20), "the content starts at the inset");
        Assert.AreEqual(Theme.Border, scene.PixelAt(30, 0), "the top edge's first pixel");
        Assert.AreNotEqual(Theme.Border, scene.PixelAt(30, 2), "the top edge is only two pixels");
        Assert.AreEqual(Theme.Border, scene.PixelAt(30, 39), "the bottom edge is the last pixel");
    }

    [TestMethod]
    public void APerSideColourPaintsOnlyThatEdge()
    {
        var accent = new SKColor(200, 40, 90);
        var control = new Control
        {
            Dock = DockStyle.Fill,
            Border = SimpleBorder.Thin.WithColor(BorderSide.Top, accent),
        };
        using var scene = HeadlessScene.Create(control, 60f, 40f);

        Assert.AreEqual(accent, scene.PixelAt(30, 0), "the top edge takes its own colour");
        Assert.AreEqual(Theme.Border, scene.PixelAt(0, 20), "the others keep the palette role");
    }

    [TestMethod]
    public void CornersAreHeldOneByOne()
    {
        var corners = new CornerRadii(1f, 2f, 3f, 4f);
        var border = SimpleBorder.Thin.WithCorners(corners);

        Assert.AreEqual(corners, border.Corners);
        Assert.AreEqual(4f, border.CornerRadius, "the most rounded corner is the one a shorthand caller sees");
        Assert.AreEqual(4f, border.Corners.Maximum);
        Assert.IsFalse(border.Corners.IsUniform, "so the shape is not a plain rounded rect");
        Assert.IsFalse(border.Corners.IsNone);

        Assert.IsTrue(CornerRadii.None.IsNone);
        Assert.IsTrue(new CornerRadii(3f).IsUniform);
        Assert.AreEqual(new CornerRadii(3f), new CornerRadii(3f, 3f, 3f, 3f));
        Assert.AreEqual(CornerRadii.None, default(CornerRadii));
    }

    [TestMethod]
    public void OneSquareCornerIsStillACorner()
    {
        var control = new Control
        {
            Dock = DockStyle.Fill,
            BackColor = SKColors.Red,
            Border = SimpleBorder.Rounded(10f).WithCorners(new CornerRadii(0f, 10f, 0f, 10f)),
        };
        using var scene = HeadlessScene.Create(control, 60f, 40f);

        Assert.IsTrue(control.Border.NarrowsClip, "the rounded corners clip");
        Assert.IsFalse(control.Border.IsRectangular);
        Assert.AreEqual(SKColors.Red, scene.PixelAt(0, 0), "the square corner is filled right into the corner");
        Assert.AreNotEqual(SKColors.Red, scene.PixelAt(59, 0), "the rounded one is not");
    }

    [TestMethod]
    public void AFrameTooSmallToRoundGivesUpItsRadius()
    {
        var control = new Control
        {
            Dock = DockStyle.Fill,
            BackColor = SKColors.Red,
            Border = SimpleBorder.Rounded(40f),
        };
        using var scene = HeadlessScene.Create(control, 30f, 20f);

        Assert.AreEqual(SKColors.Red, scene.PixelAt(15, 10), "the middle is still filled");
        Assert.AreNotEqual(
            SKColors.Red,
            scene.PixelAt(0, 0),
            "the radius is clamped to what the control can hold, not wrapped past it");
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

        Assert.AreEqual(before + 4f, (float)child.Left, "a thicker frame moves the content in");
    }

    [TestMethod]
    public void AMitredCornerLeavesNoGap()
    {
        var control = new Control
        {
            Dock = DockStyle.Fill,
            BackColor = SKColors.Red,
            Border = new SimpleBorder(new Thickness(6f, 2f, 1f, 1f), 8f),
        };
        using var scene = HeadlessScene.Create(control, 60f, 40f);

        Assert.AreEqual(
            Theme.Border,
            scene.PixelAt(3, 3),
            "the corner is mitred through the diagonal, so the two widths meet without a gap");
        Assert.AreEqual(Theme.Border, scene.PixelAt(4, 4), "and the mitre carries on to the inner outline");
        Assert.AreEqual(SKColors.Red, scene.PixelAt(20, 20), "the content is still inside it");
    }

    [TestMethod]
    public void EveryVariationIsANewBorder()
    {
        var widths = new Thickness(1f, 2f, 3f, 4f);
        var corners = new CornerRadii(1f, 2f, 3f, 4f);
        var color = new SKColor(9, 8, 7);
        var origin = SimpleBorder.Thin;

        var wider = origin.WithWidths(widths);
        var rounded = origin.WithCorners(corners);
        var recoloured = origin.WithColor(BorderSide.Left, color);
        var thickened = origin.WithWidth(3f);

        Assert.AreNotSame(origin, wider);
        Assert.AreNotSame(origin, rounded);
        Assert.AreNotSame(origin, recoloured);
        Assert.AreNotSame(origin, thickened);

        Assert.AreEqual(new Thickness(1f), origin.Widths, "the shared default is untouched");
        Assert.AreEqual(CornerRadii.None, origin.Corners);
        Assert.AreEqual(Theme.Border, origin.ColorFor(BorderSide.Left, ControlState.None));

        Assert.AreEqual(widths, wider.Widths);
        Assert.AreEqual(corners, rounded.Corners);
        Assert.AreEqual(color, recoloured.ColorFor(BorderSide.Left, ControlState.None));
        Assert.AreEqual(new Thickness(3f), thickened.Widths);
        Assert.AreEqual(corners, wider.WithCorners(corners).Corners, "variations compose");
    }

    [TestMethod]
    public void AFrameThatFollowsTheStateRepaintsWhenThePointerArrives()
    {
        var control = new Control
        {
            Dock = DockStyle.Fill,
            Border = SimpleBorder.Thin.WithHoverColor(ThemeColor.Accent),
        };
        using var scene = HeadlessScene.Create(control, 60f, 40f);
        scene.Paint();
        Assert.IsFalse(scene.Host.IsUpdatePending, "nothing is pending after a full frame");

        scene.MoveTo(control);

        Assert.IsTrue(control.Border.VariesByState);
        Assert.IsTrue(scene.Host.IsUpdatePending, "a hover colour is worth a repaint, and the border says so");
        Assert.AreEqual(Theme.Accent, scene.PixelAt(0, 20), "the frame takes the hover colour");
    }

    [TestMethod]
    public void AFrameThatDoesNotFollowTheStateLeavesTheHoverAlone()
    {
        var control = new Control { Dock = DockStyle.Fill, Border = SimpleBorder.Thin };
        using var scene = HeadlessScene.Create(control, 60f, 40f);
        scene.Paint();

        scene.MoveTo(control);

        Assert.IsFalse(control.Border.VariesByState);
        Assert.IsFalse(scene.Host.IsUpdatePending, "a plain frame looks the same hovered and not");
    }

    [TestMethod]
    public void AFrameWithAHoverColourAnswersAPointerOverItsChildren()
    {
        var label = new Label("inside") { Dock = DockStyle.Fill };
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Thickness(20f),
            Border = SimpleBorder.Thin.WithHoverColor(ThemeColor.Accent),
            HoverIncludesChildren = true,
        };
        card.Controls.Add(label);
        using var scene = HeadlessScene.Create(card, 100f, 80f);
        Assert.AreEqual(Theme.Border, scene.PixelAt(0, 40), "resting to start with");

        scene.MoveTo(label);

        Assert.AreEqual(Theme.Accent, scene.PixelAt(0, 40), "pointing at the caption lights the card's frame");
    }

    [TestMethod]
    public void AFrameThatShowsTheControlTextReservesItsCaptionBand()
    {
        var control = new Control { Padding = Thickness.Empty, Text = "Options" };
        control.Border = SimpleBorder.Thin.WithControlText();
        float band = MathF.Ceiling(control.EffectiveFont.LineHeight) + 2f;

        Assert.IsTrue(control.Border.ShowsControlText);
        Assert.AreEqual(band, control.Border.Insets(control).Top, "the caption band comes out of the top");
        Assert.AreEqual(1f, control.Border.Insets(control).Left, "and the frame's own pixel from the others");
        Assert.AreEqual(
            band,
            ContentRectOf(control, 200f, 100f).Top,
            "so the content starts below the caption rather than under it");
    }

    [TestMethod]
    public void AFrameThatShowsTheControlTextDrawsItInABreakInTheTopEdge()
    {
        var control = new Control
        {
            Dock = DockStyle.Fill,
            Text = "Options",
            Border = SimpleBorder.Thin.WithControlText(),
        };
        using var scene = HeadlessScene.Create(control, 200f, 100f);
        var edge = (int)MathF.Round((MathF.Ceiling(control.EffectiveFont.LineHeight) + 2f) / 2f);

        Assert.AreNotEqual(Theme.Border, scene.PixelAt(20, edge), "the edge is cut away behind the caption");
        Assert.AreEqual(Theme.Border, scene.PixelAt(180, edge), "and painted where the caption does not reach");
        Assert.AreEqual(
            Theme.Border,
            scene.PixelAt(0, 60),
            "the rest of the frame is unmoved, so only the top edge drops");
    }

    [TestMethod]
    public void AFrameThatShowsNoTextIsAPlainFrame()
    {
        var control = new Control { Padding = Thickness.Empty, Border = SimpleBorder.Thin.WithControlText() };

        Assert.AreEqual(new Thickness(1f), control.Border.Insets(control), "with no text there is no band");
        Assert.AreEqual(new SKRect(1f, 1f, 199f, 49f), ContentRectOf(control, 200f, 50f));

        control.Text = "Now it has one";
        Assert.AreEqual(
            MathF.Ceiling(control.EffectiveFont.LineHeight) + 2f,
            control.Border.Insets(control).Top,
            "and the band appears with the text");
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
