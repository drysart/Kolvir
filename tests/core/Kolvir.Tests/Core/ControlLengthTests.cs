using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The width, height, left and top of a control as values that can be a number of pixels, a
/// percentage of the parent, or <c>auto</c> - and the layout that answers them.
/// </summary>
/// <remarks>
/// The parsing is exact and belongs in unit tests. The resolution is a claim about layout, so every
/// integration test here reads the child's <see cref="Control.Bounds"/> - the resolved truth - and
/// never the request, which is what a control asked for rather than what it got.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class ControlLengthTests : HeadlessTestBase
{
    [TestMethod]
    public void ANUmberIsPixelsAndSaysSo()
    {
        ControlLength width = 120;

        Assert.AreEqual(ControlLengthKind.Absolute, width.Kind);
        Assert.AreEqual(120f, width.Value);
        Assert.AreEqual(120f, (float)width, "an absolute value reads back as the number it is");
        Assert.IsTrue(width.IsAbsolute);
        Assert.IsFalse(width.IsPercent);
    }

    [TestMethod]
    public void ParsingReadsPixelsPercentAndAuto()
    {
        Assert.AreEqual(ControlLength.Absolute(10f), ControlLength.Parse("10"));
        Assert.AreEqual(ControlLength.Absolute(10f), ControlLength.Parse("10px"));
        Assert.AreEqual(ControlLength.Absolute(10f), ControlLength.Parse(" 10 px "));
        Assert.AreEqual(ControlLength.Absolute(12.5f), ControlLength.Parse("12.5"));
        Assert.AreEqual(ControlLength.Percent(10f), ControlLength.Parse("10%"));
        Assert.AreEqual(ControlLength.Percent(12.5f), ControlLength.Parse("12.5 %"));
        Assert.AreEqual(ControlLength.Auto, ControlLength.Parse("auto"));
        Assert.AreEqual(ControlLength.Auto, ControlLength.Parse("  AUTO "));
        Assert.AreEqual(ControlLength.Absolute(-40f), ControlLength.Parse("-40"), "a position may be negative");
    }

    [TestMethod]
    public void ParsingRejectsWhatItCannotRead()
    {
        foreach (string? text in new[] { "invalid", "", "  ", null, "10q", "10%%", "NaN", "Infinity", "auto2", "%" })
        {
            var value = ControlLength.Parse(text);

            Assert.AreEqual(ControlLengthKind.Invalid, value.Kind, $"'{text}' is not a length");
            Assert.IsFalse(value.IsValid, "and it says so rather than turning into auto silently");
            Assert.IsTrue(value.IsAuto, "while laying out as auto, which is what the control does best");
        }
    }

    [TestMethod]
    public void AStringAssignedToAPropertyIsParsedByTheConversion()
    {
        Assert.AreEqual(ControlLength.Percent(50f), "50%");
        Assert.AreEqual(ControlLength.Absolute(50f), "50px");
        Assert.AreEqual(ControlLength.Auto, "auto");
        Assert.AreEqual(ControlLengthKind.Invalid, ((ControlLength)"nonsense").Kind);
    }

    [TestMethod]
    public void APercentageDoesNotPassForAPixelCount()
    {
        Assert.IsTrue(float.IsNaN((float)ControlLength.Percent(50f)), "10% is not 10 px, and never reads as one");
        Assert.IsTrue(float.IsNaN((float)ControlLength.Auto), "and auto is not a number either");
    }

    [TestMethod]
    public void ToStringWritesWhatParseReads()
    {
        foreach (var value in new[] { "120px", "12.5%", "auto", "invalid" })
        {
            Assert.AreEqual(value, ControlLength.Parse(value).ToString());
        }
    }

    [TestMethod]
    public void EqualityComparesTheKindAsWellAsTheNumber()
    {
        Assert.AreEqual(ControlLength.Absolute(10f), ControlLength.Absolute(10f));
        Assert.AreNotEqual(ControlLength.Absolute(10f), ControlLength.Percent(10f), "ten of one is not ten of the other");
        Assert.AreNotEqual(ControlLength.Auto, ControlLength.Invalid, "an invalid value is still tellable apart");
    }

    [TestMethod]
    public void ResolveMeasuresAPercentageAgainstTheBasis()
    {
        Assert.AreEqual(100f, ControlLength.Percent(25f).Resolve(400f, auto: 55f), "a quarter of 400");
        Assert.AreEqual(12f, ControlLength.Absolute(12f).Resolve(400f, auto: 55f), "a number ignores the basis");
        Assert.AreEqual(55f, ControlLength.Auto.Resolve(400f, auto: 55f), "auto is what the control measures at");
        Assert.AreEqual(55f, ControlLength.Invalid.Resolve(400f, auto: 55f), "and an invalid value behaves the same");
    }

    [TestMethod]
    public void AnAbsoluteWidthIsAppliedImmediatelyAndWithoutAParent()
    {
        var control = new Panel();

        control.Width = 120;

        Assert.AreEqual(120f, control.Bounds.Width, "no parent, no layout pass, the size is simply set");
        Assert.AreEqual(ControlLength.Absolute(120f), control.Width, "and the request reads back as what it was");
    }

    [TestMethod]
    public void ARequestReadsBackAsTheRequestWhileTheBoundsHoldTheAnswer()
    {
        var child = new Panel { Bounds = new SKRect(0f, 0f, 10f, 10f) };
        var stage = Stage(child, 400f, 200f);
        using var scene = HeadlessScene.Create(stage, 400f, 200f);

        child.Width = "50%";

        Assert.AreEqual(ControlLengthKind.Percent, child.Width.Kind, "the property says what was asked for");
        Assert.IsTrue(float.IsNaN((float)child.Width), "and that is not a pixel count");
        Assert.AreEqual(200f, child.Bounds.Width, 0.5f, "while the bounds hold what the layout made of it");
    }

    [TestMethod]
    public void APercentageWidthFollowsTheParentAsItResizes()
    {
        var child = new Panel { Bounds = new SKRect(0f, 0f, 10f, 10f) };
        var stage = Stage(child, 400f, 200f);
        using var scene = HeadlessScene.Create(stage, 400f, 200f);

        child.Width = "50%";
        Assert.AreEqual(200f, child.Bounds.Width, 0.5f, "half of 400");

        scene.Resize(600f, 300f);
        Assert.AreEqual(300f, child.Bounds.Width, 0.5f, "and half of 600 after the window grew");

        scene.Resize(200f, 300f);
        Assert.AreEqual(100f, child.Bounds.Width, 0.5f, "and half of 200 after it shrank");
    }

    [TestMethod]
    public void APercentageIsMeasuredAgainstTheParentsContentArea()
    {
        var child = new Panel { Bounds = new SKRect(0f, 0f, 10f, 10f) };
        var stage = Stage(child, 400f, 200f);
        stage.Padding = new Thickness(20f);

        using var scene = HeadlessScene.Create(stage, 400f, 200f);

        child.Width = "50%";
        child.Height = "50%";

        Assert.AreEqual(180f, child.Bounds.Width, 0.5f, "half of the 360 the padding leaves");
        Assert.AreEqual(80f, child.Bounds.Height, 0.5f, "and half of the 160");

        child.Left = "auto";

        Assert.AreEqual(20f, child.Bounds.Left, 0.5f, "auto puts a control at the start of the content area");
    }

    [TestMethod]
    public void APercentageLeftAndTopPlaceTheControlInsideTheContent()
    {
        var child = new Panel { Bounds = new SKRect(0f, 0f, 40f, 40f) };
        var stage = Stage(child, 400f, 200f);
        using var scene = HeadlessScene.Create(stage, 400f, 200f);

        child.Left = "25%";
        child.Top = "50%";

        Assert.AreEqual(100f, child.Bounds.Left, 0.5f, "a quarter across");
        Assert.AreEqual(100f, child.Bounds.Top, 0.5f, "half way down");
        Assert.AreEqual(40f, child.Bounds.Width, 0.5f, "and the size it already had");
    }

    [TestMethod]
    public void AutoSizesToWhatTheControlMeasures()
    {
        var label = new Label("Measure me") { Bounds = new SKRect(0f, 0f, 10f, 10f) };
        var stage = Stage(label, 400f, 200f);
        using var scene = HeadlessScene.Create(stage, 400f, 200f);

        label.Width = "auto";

        float measured = label.Measure(new SKSize(400f, 200f)).Width;
        Assert.IsTrue(measured > 10f, "the text is wider than the ten pixels it started with");
        Assert.AreEqual(measured, label.Bounds.Width, 0.5f, "auto is the size the control measures at");
    }

    [TestMethod]
    public void AnInvalidValueLaysOutExactlyAsAuto()
    {
        var stage = Stage(new Panel { Bounds = new SKRect(0f, 0f, 10f, 10f) }, 400f, 200f);
        var child = (Panel)stage.Controls[0];
        using var scene = HeadlessScene.Create(stage, 400f, 200f);

        child.Width = "auto";
        var auto = child.Bounds;

        child.Width = "half of it, please";
        var invalid = child.Bounds;

        Assert.AreEqual(auto, invalid, "an unreadable value is laid out as auto");
        Assert.AreEqual(ControlLengthKind.Invalid, child.Width.Kind, "and is still reported as invalid");
    }

    [TestMethod]
    public void ABoundsAssignmentClearsTheRequestsItAnswers()
    {
        var child = new Panel { Bounds = new SKRect(0f, 0f, 10f, 10f) };
        var stage = Stage(child, 400f, 200f);
        using var scene = HeadlessScene.Create(stage, 400f, 200f);

        child.Width = "50%";
        Assert.AreEqual(200f, child.Bounds.Width, 0.5f);

        child.Bounds = new SKRect(0f, 0f, 90f, 20f);
        scene.Window.Host.PerformLayout();

        Assert.AreEqual(90f, child.Bounds.Width, 0.5f, "a rectangle is an answer, so the request is gone");
        Assert.AreEqual(ControlLength.Absolute(90f), child.Width);
    }

    [TestMethod]
    public void ADockedChildKeepsTheSizeItsContainerGaveIt()
    {
        var child = new Panel { Dock = DockStyle.Fill, Bounds = new SKRect(0f, 0f, 10f, 10f) };
        var stage = Stage(child, 400f, 200f);
        using var scene = HeadlessScene.Create(stage, 400f, 200f);

        child.Width = "50%";

        Assert.AreEqual(400f, child.Bounds.Width, 0.5f, "a docked child is arranged by its container, which wins");
    }

    /// <summary>A plain panel of the given size with one freely positioned child in it.</summary>
    private static Panel Stage(Control child, float width, float height)
    {
        var stage = new Panel
        {
            Bounds = new SKRect(0f, 0f, width, height),
            Padding = Thickness.Empty,
            Border = Border.None,
        };

        stage.Controls.Add(child);
        return stage;
    }
}
