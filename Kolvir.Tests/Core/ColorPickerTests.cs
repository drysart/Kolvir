using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The spectrum colour picker: its defaults, the two non-RGB spaces, the square's axes and corners,
/// dragging, the keyboard, the disabled and read-only paths, the component and hex fields, tab order
/// and the spectrum's image cache.
/// </summary>
[TestClass]
[TestCategory("Headless")]
public sealed class ColorPickerTests : HeadlessTestBase
{
    private static Control Spectrum(ColorPicker picker) => picker.Controls[0];

    private static Control HueSlider(ColorPicker picker) => picker.Controls[1];

    private static IReadOnlyList<TextBox> Fields(ColorPicker picker) =>
        picker.Controls.OfType<TextBox>().ToArray();

    private static TextBox Component(ColorPicker picker, int index) => Fields(picker)[index];

    private static TextBox Hex(ColorPicker picker) => Fields(picker)[3];

    [TestMethod]
    public void AColorPickerStartsWithRedRgbAndItsWholeSurface()
    {
        using var picker = new ColorPicker();

        Assert.AreEqual(SKColors.Red, picker.Color);
        Assert.AreEqual(ColorMode.Rgb, picker.ColorMode);
        Assert.IsFalse(picker.IsReadOnly);

        Assert.IsFalse(picker.TabStop, "the square and the fields are the tab stops, not the container");
        Assert.IsFalse(picker.Focusable);
        Assert.AreEqual(8f, picker.Padding.Left);
        Assert.AreEqual(1f, picker.BorderWidth);
        Assert.AreEqual(8f, picker.CornerRadius);
        Assert.AreEqual(150f, picker.MinimumSize.Width);
        Assert.AreEqual(270f, picker.MinimumSize.Height);

        Assert.AreEqual(6, picker.Controls.Count, "the square, the hue strip and four fields");
        Assert.AreEqual(4, Fields(picker).Count);

        var measured = picker.Measure(new SKSize(1000f, 1000f));
        Assert.IsTrue(measured.Width >= picker.MinimumSize.Width);
        Assert.IsTrue(measured.Height >= picker.MinimumSize.Height);

        Assert.AreEqual("255", Component(picker, 0).Text);
        Assert.AreEqual("0", Component(picker, 1).Text);
        Assert.AreEqual("0", Component(picker, 2).Text);
        Assert.AreEqual("#FF0000", Hex(picker).Text);
    }

    [TestMethod]
    public void TheModeChangesTheLabelsUnitsAndFieldsAndNotTheColour()
    {
        using var picker = new ColorPicker();
        picker.Color = new SKColor(51, 102, 153);

        int changes = 0;
        using var subscription = picker.AddColorChangedHandler(_ => changes++);
        var before = picker.Color;

        Assert.AreEqual("R", picker.ComponentLabelAt(0));
        Assert.AreEqual("G", picker.ComponentLabelAt(1));
        Assert.AreEqual("B", picker.ComponentLabelAt(2));
        Assert.AreEqual("51", Component(picker, 0).Text);
        Assert.AreEqual("102", Component(picker, 1).Text);
        Assert.AreEqual("153", Component(picker, 2).Text);

        picker.ColorMode = ColorMode.Hsv;

        Assert.AreEqual("H\u00B0", picker.ComponentLabelAt(0));
        Assert.AreEqual("S%", picker.ComponentLabelAt(1));
        Assert.AreEqual("V%", picker.ComponentLabelAt(2));
        Assert.AreEqual("210", Component(picker, 0).Text);
        Assert.AreEqual("67", Component(picker, 1).Text);
        Assert.AreEqual("60", Component(picker, 2).Text);

        picker.ColorMode = ColorMode.Hsl;

        Assert.AreEqual("H\u00B0", picker.ComponentLabelAt(0));
        Assert.AreEqual("S%", picker.ComponentLabelAt(1));
        Assert.AreEqual("L%", picker.ComponentLabelAt(2));
        Assert.AreEqual("210", Component(picker, 0).Text);
        Assert.AreEqual("50", Component(picker, 1).Text);
        Assert.AreEqual("40", Component(picker, 2).Text);

        Assert.AreEqual(before, picker.Color, "switching mode is a display change only");
        Assert.AreEqual(0, changes, "a mode switch is not a colour change");
        Assert.AreEqual("#336699", Hex(picker).Text, "the hex readout is mode-independent");
    }

    [TestMethod]
    public void TheColourSurvivesEveryModeSwitchExactlyIncludingItsAlpha()
    {
        using var picker = new ColorPicker();
        var color = new SKColor(17, 34, 51, 200);
        picker.Color = color;

        int changes = 0;
        picker.AddColorChangedHandler(_ => changes++);

        foreach (var mode in new[] { ColorMode.Hsv, ColorMode.Hsl, ColorMode.Rgb, ColorMode.Hsl, ColorMode.Hsv })
        {
            picker.ColorMode = mode;
            Assert.AreEqual(color, picker.Color, $"the colour survives {mode}");
            Assert.AreEqual(200, picker.Color.Alpha, "alpha is preserved but not edited");
            Assert.AreEqual("#112233", Hex(picker).Text);
        }

        Assert.AreEqual(0, changes);
    }

    [TestMethod]
    public void EachCornerOfTheHsvSquareIsItsExpectedColour()
    {
        var picker = new ColorPicker { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(picker, 320f, 360f);

        ClickCorner(scene, picker, right: false, bottom: false);
        Assert.AreEqual(SKColors.White, picker.Color, "top-left is zero saturation at full value");

        ClickCorner(scene, picker, right: true, bottom: false);
        Assert.AreEqual(SKColors.Red, picker.Color, "top-right is full saturation at the hue");

        ClickCorner(scene, picker, right: false, bottom: true);
        Assert.AreEqual(SKColors.Black, picker.Color, "bottom-left is full black");

        ClickCorner(scene, picker, right: true, bottom: true);
        Assert.AreEqual(SKColors.Black, picker.Color, "bottom-right is full black too");
    }

    [TestMethod]
    public void EachCornerOfTheHslSquareIsItsExpectedColour()
    {
        var picker = new ColorPicker { Dock = DockStyle.Fill, ColorMode = ColorMode.Hsl };
        using var scene = HeadlessScene.Create(picker, 320f, 360f);

        ClickCorner(scene, picker, right: false, bottom: false);
        Assert.AreEqual(SKColors.White, picker.Color, "top-left is full lightness");

        ClickCorner(scene, picker, right: true, bottom: false);
        Assert.AreEqual(SKColors.White, picker.Color, "and full lightness is white across the whole top row");

        ClickCorner(scene, picker, right: false, bottom: true);
        Assert.AreEqual(SKColors.Black, picker.Color, "the bottom row is black across the width");

        ClickCorner(scene, picker, right: true, bottom: true);
        Assert.AreEqual(SKColors.Black, picker.Color, "including its right corner");
    }

    [TestMethod]
    public void DraggingAcrossTheSquareReportsEachDistinctValueOnce()
    {
        var picker = new ColorPicker { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(picker, 320f, 360f);

        var square = Spectrum(picker);
        var origin = square.GetPositionInHost();
        float y = origin.Y + (square.Height * 0.4f);

        var seen = new List<SKColor>();
        picker.AddColorChangedHandler(_ => seen.Add(picker.Color));

        var first = new SKPoint(origin.X + 1f, y);
        scene.Host.ProcessMouseMove(first);
        scene.Host.ProcessMouseDown(first, MouseButtons.Left, Modifiers.None);

        int afterPress = seen.Count;
        int expected = 0;
        var previous = picker.Color;

        for (int step = 1; step <= 20; step++)
        {
            var point = new SKPoint(origin.X + ((square.Width - 2f) * step / 20f), y);
            scene.Host.ProcessMouseMove(point);

            if (picker.Color != previous)
            {
                expected++;
                previous = picker.Color;
            }
        }

        var last = new SKPoint(origin.X + ((square.Width - 2f) * 20f / 20f), y);
        scene.Host.ProcessMouseMove(last);
        int beforeRepaint = seen.Count;
        scene.Host.ProcessMouseMove(last);

        scene.Host.ProcessMouseUp(last, MouseButtons.Left, Modifiers.None);

        Assert.IsTrue(seen.Count - afterPress >= expected, "every move that changed the colour was reported");
        Assert.AreEqual(expected, seen.Count - afterPress, "and nothing else was");
        Assert.AreEqual(beforeRepaint, seen.Count, "a move that lands on the same colour reports nothing");

        for (int index = 1; index < seen.Count; index++)
        {
            Assert.AreNotEqual(seen[index - 1], seen[index], "consecutive notifications always carry a new value");
        }
    }

    [TestMethod]
    public void TheSquareKeysMoveSaturationAndValueAndHomeAndEndTouchTheEdges()
    {
        var picker = new ColorPicker { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(picker, 320f, 360f);
        var square = Spectrum(picker);

        ClickCorner(scene, picker, right: true, bottom: false);
        Assert.AreEqual(SKColors.Red, picker.Color);

        scene.Host.SetFocus(square);
        Assert.IsTrue(square.IsFocused);

        scene.Key(Key.Home);
        Assert.AreEqual(SKColors.White, picker.Color, "Home takes saturation to zero at the top");

        scene.Key(Key.End);
        Assert.AreEqual(SKColors.Red, picker.Color, "End takes saturation to full at the top");

        var before = picker.Color;
        scene.Key(Key.Down);
        Assert.AreNotEqual(before, picker.Color, "Down lowers value");

        before = picker.Color;
        scene.Key(Key.Up);
        Assert.AreNotEqual(before, picker.Color, "Up raises it again");

        before = picker.Color;
        scene.Key(Key.Left);
        Assert.AreNotEqual(before, picker.Color, "Left lowers saturation");

        before = picker.Color;
        scene.Key(Key.Right);
        Assert.AreNotEqual(before, picker.Color, "Right raises it");
    }

    [TestMethod]
    public void TheHueStripKeysMoveByDegreesAndHomeAndEndReachTheEnds()
    {
        var picker = new ColorPicker { Dock = DockStyle.Fill, ColorMode = ColorMode.Hsv };
        using var scene = HeadlessScene.Create(picker, 320f, 360f);
        var hue = HueSlider(picker);

        scene.Host.SetFocus(hue);
        Assert.IsTrue(hue.IsFocused);

        scene.Key(Key.End);
        Assert.AreEqual("359", Component(picker, 0).Text);

        scene.Key(Key.Home);
        Assert.AreEqual("0", Component(picker, 0).Text);

        scene.Key(Key.Right);
        Assert.AreEqual("1", Component(picker, 0).Text);

        scene.Key(Key.PageUp);
        Assert.AreEqual("11", Component(picker, 0).Text);

        scene.Key(Key.Left);
        Assert.AreEqual("10", Component(picker, 0).Text);
    }

    [TestMethod]
    public void TheComponentFieldParsesLiveClampsOnCommitAndSteps()
    {
        var picker = new ColorPicker { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(picker, 320f, 360f);

        var red = Component(picker, 0);
        scene.Host.SetFocus(red);
        scene.Type("300");

        Assert.AreEqual(255, picker.Color.Red, "an out-of-range channel is clamped as it is typed");
        Assert.AreEqual("300", red.Text, "but the text is the user's until the edit is committed");

        scene.Key(Key.Enter);
        Assert.AreEqual("255", red.Text, "Enter rewrites the text from the clamped value");
        Assert.IsFalse(ReferenceEquals(scene.Host.FocusedControl, red), "Enter moves the focus on");

        var before = picker.Color;
        red.Text = "abc";
        scene.Host.SetFocus(red);
        scene.Key(Key.Enter);
        Assert.AreEqual(before, picker.Color, "an unparseable value leaves the colour alone");
        Assert.AreEqual("255", red.Text, "and the field is put back to the current value");

        scene.Host.SetFocus(red);
        scene.Type("100");
        scene.Key(Key.Up);
        Assert.AreEqual("101", red.Text, "the arrow keys step by one");
        scene.Key(Key.PageDown);
        Assert.AreEqual("91", red.Text, "Page Down steps by ten");
    }

    [TestMethod]
    public void TheHueFieldClampsTo359AndIsRelabelledInBothSpaces()
    {
        var picker = new ColorPicker { Dock = DockStyle.Fill, ColorMode = ColorMode.Hsv };
        using var scene = HeadlessScene.Create(picker, 320f, 360f);

        var hue = Component(picker, 0);
        scene.Host.SetFocus(hue);
        scene.Type("400");
        scene.Key(Key.Enter);

        Assert.AreEqual("359", hue.Text, "a hue past the end is clamped to 359");
        Assert.AreEqual("H\u00B0", picker.ComponentLabelAt(0));

        picker.ColorMode = ColorMode.Hsl;
        Assert.AreEqual("359", Component(picker, 0).Text, "the hue survives the mode switch");
        Assert.AreEqual("L%", picker.ComponentLabelAt(2));
    }

    [TestMethod]
    public void TheFieldsAndHexFollowASquareDrag()
    {
        var picker = new ColorPicker { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(picker, 320f, 360f);

        var square = Spectrum(picker);
        var origin = square.GetPositionInHost();
        scene.ClickAt(new SKPoint(origin.X + (square.Width * 0.7f), origin.Y + (square.Height * 0.3f)));

        var color = picker.Color;
        var fields = Fields(picker);

        Assert.AreEqual(color.Red.ToString(), fields[0].Text);
        Assert.AreEqual(color.Green.ToString(), fields[1].Text);
        Assert.AreEqual(color.Blue.ToString(), fields[2].Text);
        Assert.AreEqual($"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}", fields[3].Text);
    }

    [TestMethod]
    public void TheHexFieldTakesBothFormsAndWritesUppercase()
    {
        var picker = new ColorPicker { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(picker, 320f, 360f);
        var hex = Hex(picker);

        scene.Host.SetFocus(hex);
        scene.Type("#0f0");
        Assert.AreEqual(SKColors.Lime, picker.Color, "the shorthand is applied while it is typed");

        scene.Key(Key.Enter);
        Assert.AreEqual("#00FF00", hex.Text, "and committed as the long uppercase form");

        scene.Host.SetFocus(hex);
        scene.Type("aB12Cd");
        scene.Key(Key.Enter);
        Assert.AreEqual(new SKColor(0xAB, 0x12, 0xCD), picker.Color);
        Assert.AreEqual("#AB12CD", hex.Text);

        scene.Host.SetFocus(hex);
        scene.Type("123456");
        scene.Key(Key.Enter);
        Assert.AreEqual(new SKColor(0x12, 0x34, 0x56), picker.Color, "the leading hash is optional on input");
    }

    [TestMethod]
    public void AMalformedHexRevertsAndAnAlphaFormIsNotAccepted()
    {
        var picker = new ColorPicker { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(picker, 320f, 360f);
        var hex = Hex(picker);

        var before = picker.Color;
        scene.Host.SetFocus(hex);
        hex.Text = "#GGGGGG";
        scene.Key(Key.Enter);

        Assert.AreEqual(before, picker.Color, "a malformed value leaves the colour alone");
        Assert.AreEqual($"#{before.Red:X2}{before.Green:X2}{before.Blue:X2}", hex.Text, "and the field falls back");

        picker.Color = new SKColor(255, 0, 0, 128);
        scene.Host.SetFocus(hex);
        scene.Type("#11223344");
        scene.Key(Key.Enter);

        Assert.AreEqual(new SKColor(255, 0, 0, 128), picker.Color, "the alpha form is rejected, not read as a colour");
        Assert.AreEqual("#FF0000", hex.Text);
    }

    [TestMethod]
    public void AHexEditPreservesTheAlphaAndRoundTripsExactly()
    {
        var picker = new ColorPicker { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(picker, 320f, 360f);
        picker.Color = new SKColor(255, 0, 0, 128);

        var hex = Hex(picker);
        scene.Host.SetFocus(hex);
        scene.Type("#0000FF");
        scene.Key(Key.Enter);

        Assert.AreEqual(new SKColor(0, 0, 255, 128), picker.Color);

        scene.Host.SetFocus(hex);
        Assert.AreEqual("#0000FF", hex.Text);
        scene.Key(Key.Enter);
        Assert.AreEqual(new SKColor(0, 0, 255, 128), picker.Color, "the canonical text round-trips exactly");
    }

    [TestMethod]
    public void AReadOnlyPickerDisplaysButRefusesEveryEdit()
    {
        var picker = new ColorPicker { Dock = DockStyle.Fill, Color = new SKColor(10, 20, 30) };
        using var scene = HeadlessScene.Create(picker, 320f, 360f);

        picker.IsReadOnly = true;

        foreach (var field in Fields(picker))
        {
            Assert.IsTrue(field.ReadOnly, "every field is read-only");
        }

        var square = Spectrum(picker);
        var origin = square.GetPositionInHost();
        scene.ClickAt(new SKPoint(origin.X + square.Width - 1f, origin.Y + 1f));
        Assert.AreEqual(new SKColor(10, 20, 30), picker.Color, "the square refuses a press");

        var red = Component(picker, 0);
        scene.Host.SetFocus(red);
        scene.Type("200");
        Assert.AreEqual("10", red.Text, "the field refuses typing");
        Assert.AreEqual(new SKColor(10, 20, 30), picker.Color);

        picker.Color = SKColors.Orange;
        Assert.AreEqual(SKColors.Orange, picker.Color, "programmatic assignment still works");
    }

    [TestMethod]
    public void ADisabledPickerPaintsDisabledAndTakesNoInput()
    {
        var picker = new ColorPicker { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(picker, 320f, 360f);

        var before = picker.Color;
        var square = Spectrum(picker);
        var origin = square.GetPositionInHost();

        picker.Enabled = false;
        scene.ClickAt(new SKPoint(origin.X + square.Width - 1f, origin.Y + 1f));

        Assert.AreEqual(before, picker.Color, "a disabled square takes no press");
        Assert.IsFalse(Component(picker, 0).Enabled, "the fields inherit the disabled state");

        var pixel = scene.PixelAt(
            (int)(origin.X + (square.Width / 2f)),
            (int)(origin.Y + (square.Height / 2f)));
        Assert.AreEqual(Theme.DisabledBackground, pixel, "the square is painted disabled");

        picker.Enabled = true;
        scene.ClickAt(new SKPoint(origin.X + (square.Width * 0.3f), origin.Y + (square.Height * 0.6f)));
        Assert.AreNotEqual(before, picker.Color, "and it takes input again once enabled");
    }

    [TestMethod]
    public void HoverPressAndFocusAreDistinctStates()
    {
        var picker = new ColorPicker { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(picker, 320f, 360f);
        var square = Spectrum(picker);

        scene.MoveTo(square);
        Assert.IsTrue(square.IsMouseOver);
        Assert.IsFalse(square.IsPressed);

        scene.Press(square);
        Assert.IsTrue(square.IsPressed);
        Assert.IsTrue(square.IsFocused);

        scene.Release(square);
        Assert.IsFalse(square.IsPressed);
    }

    [TestMethod]
    public void TheTabOrderRunsThroughTheSquareHueComponentFieldsAndHex()
    {
        var before = new Button("Before") { Dock = DockStyle.Top, Height = 30f, TabIndex = 0 };
        var picker = new ColorPicker { Dock = DockStyle.Top, Height = 300f, TabIndex = 1 };
        var after = new Button("After") { Dock = DockStyle.Top, Height = 30f, TabIndex = 2 };

        var root = new Panel
        {
            Dock = DockStyle.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new Thickness(0f),
        };

        root.Controls.Add(before);
        root.Controls.Add(picker);
        root.Controls.Add(after);

        using var scene = HeadlessScene.Create(root, 420f, 400f);
        scene.Host.SetFocus(before);

        var expected = new Control[]
        {
            Spectrum(picker),
            HueSlider(picker),
            Component(picker, 0),
            Component(picker, 1),
            Component(picker, 2),
            Hex(picker),
            after,
        };

        foreach (var control in expected)
        {
            scene.Key(Key.Tab);
            Assert.AreSame(control, scene.Host.FocusedControl, $"Tab should reach {control.Name}");
        }
    }

    [TestMethod]
    public void EnterInTheHexFieldMovesTheFocusOutOfThePicker()
    {
        var picker = new ColorPicker { Dock = DockStyle.Top, Height = 300f };
        var after = new Button("After") { Dock = DockStyle.Top, Height = 30f, TabIndex = 1 };

        var root = new Panel
        {
            Dock = DockStyle.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new Thickness(0f),
        };

        root.Controls.Add(picker);
        root.Controls.Add(after);

        using var scene = HeadlessScene.Create(root, 420f, 400f);
        var hex = Hex(picker);
        scene.Host.SetFocus(hex);

        scene.Type("0f0");
        scene.Key(Key.Enter);

        Assert.AreEqual(SKColors.Lime, picker.Color);
        Assert.AreSame(after, scene.Host.FocusedControl, "Enter commits and leaves the picker");
    }

    [TestMethod]
    public void TheSquareImageIsRegeneratedForAHueOrSpaceChangeAndNotForARepaint()
    {
        var picker = new ColorPicker { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(picker, 320f, 360f);

        scene.Paint();
        int initial = picker.SpectrumGenerationCount;
        Assert.AreEqual(1, initial, "the first paint generates the square");

        scene.Paint();
        scene.Paint();
        Assert.AreEqual(initial, picker.SpectrumGenerationCount, "a repaint reuses the image");

        var square = Spectrum(picker);
        var origin = square.GetPositionInHost();
        scene.ClickAt(new SKPoint(origin.X + (square.Width * 0.3f), origin.Y + (square.Height * 0.6f)));
        scene.Paint();
        Assert.AreEqual(initial, picker.SpectrumGenerationCount, "moving saturation and value does not recolour the square");

        var hue = HueSlider(picker);
        var hueOrigin = hue.GetPositionInHost();
        scene.ClickAt(new SKPoint(hueOrigin.X + (hue.Width / 2f), hueOrigin.Y + (hue.Height / 2f)));
        scene.Paint();
        Assert.AreEqual(initial + 1, picker.SpectrumGenerationCount, "a new hue regenerates the image");

        scene.Paint();
        Assert.AreEqual(initial + 1, picker.SpectrumGenerationCount);

        picker.ColorMode = ColorMode.Hsv;
        scene.Paint();
        Assert.AreEqual(initial + 1, picker.SpectrumGenerationCount, "Rgb and Hsv share the same square");

        picker.ColorMode = ColorMode.Hsl;
        scene.Paint();
        Assert.AreEqual(initial + 2, picker.SpectrumGenerationCount, "the lightness square is a different image");

        picker.ColorMode = ColorMode.Rgb;
        scene.Paint();
        Assert.AreEqual(initial + 3, picker.SpectrumGenerationCount);

        scene.Resize(360f, 420f);
        scene.Paint();
        Assert.AreEqual(initial + 4, picker.SpectrumGenerationCount, "a different device size regenerates it");
    }

    [TestMethod]
    public void ANarrowPickerStacksItsFieldsAndKeepsThemInsideWhileAWideOneKeepsARow()
    {
        var narrow = new ColorPicker { Dock = DockStyle.Left, Width = 170f };
        var wide = new ColorPicker { Dock = DockStyle.Fill };

        var root = new Panel
        {
            Dock = DockStyle.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new Thickness(0f),
        };

        root.Controls.Add(narrow);
        root.Controls.Add(wide);

        using var scene = HeadlessScene.Create(root, 500f, 400f);
        scene.Paint();

        var square = Spectrum(narrow);

        Assert.IsTrue(square.Width > 0f && square.Height > 0f, "the square still has room");

        Assert.IsTrue(
            Component(narrow, 1).Top >= Component(narrow, 0).Bottom,
            "the component fields stack rather than overlap");
        Assert.IsTrue(
            Component(narrow, 2).Top >= Component(narrow, 1).Bottom);

        foreach (var field in Fields(narrow))
        {
            Assert.IsTrue(field.Left >= 0f, $"{field.Name} starts inside the picker");
            Assert.IsTrue(field.Top >= square.Bottom, $"{field.Name} sits below the square");
            Assert.IsTrue(field.Right <= narrow.Width + 0.01f, $"{field.Name} ends inside the picker");
            Assert.IsTrue(field.Bottom <= narrow.Height + 0.01f, $"{field.Name} fits the height");
        }

        Assert.AreEqual(Component(wide, 0).Top, Component(wide, 1).Top, 0.01f, "a wide picker keeps the fields in one row");
        Assert.IsTrue(wide.Width > narrow.Width);
    }

    private static void ClickCorner(HeadlessScene scene, ColorPicker picker, bool right, bool bottom)
    {
        var square = Spectrum(picker);
        var origin = square.GetPositionInHost();
        float x = origin.X + (right ? MathF.Max(0f, square.Width - 1f) : 0f);
        float y = origin.Y + (bottom ? MathF.Max(0f, square.Height - 1f) : 0f);
        scene.ClickAt(new SKPoint(x, y));
    }
}
