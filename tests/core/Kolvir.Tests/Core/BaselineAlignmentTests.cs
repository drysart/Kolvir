using Kolvir;
using SkiaSharp;

namespace Kolvir.Tests;

/// <summary>
/// <see cref="StackPanel.AlignBaselines"/>: which controls declare a baseline, where the row puts
/// them, and the measure/arrange agreement an auto-sized row needs so that nothing is clipped.
/// </summary>
/// <remarks>
/// The face is pinned for every measurement case, because the numbers a baseline assertion is about
/// are the font's own ascent. The numbers asserted are absolute positions in the row rather than
/// only equalities, so a change that moves the whole row is caught too.
/// </remarks>
[TestClass]
[TestCategory("Core")]
public sealed class BaselineAlignmentTests : HeadlessTestBase
{
    [TestMethod]
    public void TwoLabelsAtDifferentSizesShareOneBaseline()
    {
        using (TestFont.Pin())
        {
            var stack = Row(alignBaselines: true);
            var small = Sized("small", 12f);
            var large = Sized("large", 20f);
            stack.Controls.Add(small);
            stack.Controls.Add(large);

            using var scene = HeadlessScene.Create(stack, 300f, 120f);

            // The 20 pt label is the tallest and sits at the top; the 12 pt one moves down by 8 so
            // that both first lines are drawn on y = 20.5, measured from the row's top edge. The
            // boxes stay different sizes - 16 and 26 tall - which is the whole point.
            Assert.AreEqual(0f, large.Top, 0.01f, "the tallest child keeps the position its alignment gives it");
            Assert.AreEqual(8f, small.Top, 0.01f, "the smaller label moves down to meet it");
            Assert.AreEqual(16f, small.Height, 0.01f);
            Assert.AreEqual(26f, large.Height, 0.01f);

            Assert.AreEqual(20.5f, BaselineOf(small), 0.01f, "the 12 pt first line's baseline");
            Assert.AreEqual(20.5f, BaselineOf(large), 0.01f, "the 20 pt first line's baseline");
        }
    }

    [TestMethod]
    public void AnAutoSizedRowMeasuresTallEnoughForTheShiftedContent()
    {
        using (TestFont.Pin())
        {
            var host = new Panel { Padding = Thickness.Empty };
            var stack = Row(alignBaselines: true);
            stack.Dock = DockStyle.Top;
            stack.AutoSize = true;

            var small = Sized("small", 12f);
            small.Margin = new Thickness(0f, 10f, 0f, 0f);
            var large = Sized("large", 20f);
            stack.Controls.Add(small);
            stack.Controls.Add(large);

            host.Controls.Add(stack);

            using var scene = HeadlessScene.Create(host, 400f, 300f);

            Assert.AreEqual(stack.DesiredSize.Height, stack.Height, 0.01f, "the stack is arranged at the height it measured");

            // The margin pushes the 12 pt baseline below the 20 pt one, so the 20 pt label is moved
            // down to meet it and the row has to grow: 26 would have been enough for the tallest
            // child alone, and 28 is what the arrangement actually covers.
            Assert.AreEqual(28f, stack.Height, 0.01f, "the row grew to hold the shifted 20 pt label");
            Assert.AreEqual(22.5f, BaselineOf(small), 0.01f);
            Assert.AreEqual(22.5f, BaselineOf(large), 0.01f);

            // The measure half is only right if it accounts for the shift the arrange half applies,
            // so the check is that the arranged children fit inside what was reported.
            Assert.IsTrue(small.Bottom <= stack.Height + 0.01f, "the shifted label fits in the measured height");
            Assert.AreEqual(stack.Height, large.Bottom, 0.01f, "the moved label reaches exactly the measured height");
        }
    }

    [TestMethod]
    public void AChildWithNoBaselineKeepsItsOwnAlignmentInTheGrownBox()
    {
        using (TestFont.Pin())
        {
            var host = new Panel { Padding = Thickness.Empty };
            var stack = Row(alignBaselines: true);
            stack.Dock = DockStyle.Top;
            stack.AutoSize = true;

            var small = Sized("small", 12f);
            small.Margin = new Thickness(0f, 10f, 0f, 0f);
            var large = Sized("large", 20f);
            var plain = new Panel
            {
                AutoSize = false,
                Width = 10f,
                Height = 12f,
                VerticalAlignment = Alignment.Far,
                Border = Border.None,
                Padding = Thickness.Empty,
            };

            stack.Controls.Add(small);
            stack.Controls.Add(large);
            stack.Controls.Add(plain);

            host.Controls.Add(stack);

            using var scene = HeadlessScene.Create(host, 400f, 300f);

            // The margin makes the 12 pt baseline the lowest one, so the 20 pt label is moved down
            // and the row grows from the 26 its tallest child needs to the 28 the group covers. The
            // child with no text takes no part: it keeps its Far alignment within the grown box.
            Assert.AreEqual(28f, stack.Height, 0.01f);
            Assert.AreEqual(stack.Height, plain.Bottom, 0.01f, "the non-participant keeps its Far alignment");
            Assert.AreEqual(BaselineOf(small), BaselineOf(large), 0.01f, "and the participants still share their baseline");
        }
    }

    [TestMethod]
    public void ATopMarginMovesTheBaselineByExactlyThatMargin()
    {
        using (TestFont.Pin())
        {
            float without = SharedBaselineWithMargin(0f);

            // The margin insets the child from the row's top, so the child that carries it is the
            // lowest one, and the row's shared baseline is that much further down. It is not counted
            // twice, which would move the whole row by twice the margin.
            float with = SharedBaselineWithMargin(7f);

            Assert.AreEqual(7f, with - without, 0.01f);
        }
    }

    [TestMethod]
    public void AWrappedLabelAlignsOnItsFirstLineAndAnEmptyLabelDoesNotTakePart()
    {
        using (TestFont.Pin())
        {
            var font = new Font(TestFont.Family, 13f);
            var stack = Row(alignBaselines: true);
            var wrapped = new Label("wrapped text that breaks over several lines")
            {
                AutoSize = false,
                Width = 70f,
                Height = 4f * font.LineHeight,
                TextAlign = ContentAlignment.TopLeft,
                Font = font,
                VerticalAlignment = Alignment.Near,
            };

            var empty = new Label(string.Empty)
            {
                AutoSize = false,
                Width = 20f,
                Height = 18f,
                Font = font,
                VerticalAlignment = Alignment.Near,
            };

            stack.Controls.Add(empty);
            stack.Controls.Add(wrapped);

            using var scene = HeadlessScene.Create(stack, 300f, 140f);

            var content = wrapped.ContentRect;
            var lines = TextRenderer.BuildTextLines(wrapped.Text, wrapped.EffectiveFont, content.Width, wrap: true);
            Assert.IsTrue(lines.Count > 1, "the label really did wrap");

            float expected = wrapped.Top + TextRenderer.GetBlockBaseline(content, wrapped.EffectiveFont, lines.Count, wrapped.TextAlign);
            Assert.AreEqual(expected, BaselineOf(wrapped), 0.01f, "the first line's baseline, not the block's middle");

            Assert.IsFalse(empty.TryGetBaseline(new SKSize(empty.Width, empty.Height), out float emptyBaseline));
            Assert.AreEqual(0f, emptyBaseline);
        }
    }

    [TestMethod]
    public void AWrappedRichLabelAlignsOnItsFirstLinesAscentAcrossItsFonts()
    {
        using (TestFont.Pin())
        {
            var stack = Row(alignBaselines: true);
            var big = new Font(TestFont.Family, 22f);
            var rich = new RichLabel
            {
                AutoSize = false,
                WrapText = true,
                Width = 90f,
                Font = new Font(TestFont.Family, 13f),
                VerticalAlignment = Alignment.Near,
            };

            rich.AddSpan(new TextSpan("small ") { FontSize = 12f });
            rich.AddSpan(new TextSpan("BIG") { FontSize = 22f });
            rich.AddText(" then a lot more text so that the line has to break");

            float wrappedHeight = rich.Measure(new SKSize(90f, 1000f)).Height;
            Assert.IsTrue(wrappedHeight > big.LineHeight, "the flow wrapped onto more than one line");
            rich.Height = wrappedHeight;

            stack.Controls.Add(rich);

            using var scene = HeadlessScene.Create(stack, 300f, 200f);

            var content = rich.ContentRect;

            // The first line mixes a 12 pt and a 22 pt run, so the line's ascent is the larger of
            // the two fonts' baseline offsets. The label is TopLeft, so the block starts at the top
            // of its content.
            float expected = rich.Top + content.Top + big.BaselineOffset;
            Assert.AreEqual(expected, BaselineOf(rich), 0.01f);
        }
    }

    [TestMethod]
    public void AlignBaselinesOnAVerticalStackChangesNothing()
    {
        using (TestFont.Pin())
        {
            var off = VerticalRow(alignBaselines: false);
            var on = VerticalRow(alignBaselines: true);
            List<SKRect> offBounds;
            List<SKRect> onBounds;

            using (var offScene = HeadlessScene.Create(off.Stack, 300f, 200f))
            {
                offBounds = off.Children.Select(child => child.Bounds).ToList();
            }

            using (var onScene = HeadlessScene.Create(on.Stack, 300f, 200f))
            {
                onBounds = on.Children.Select(child => child.Bounds).ToList();
            }

            CollectionAssert.AreEqual(offBounds, onBounds, "a vertical stack ignores the property");
        }
    }

    [TestMethod]
    public void WithThePropertyOffTheRowIsArrangedExactlyAsBefore()
    {
        using (TestFont.Pin())
        {
            var stack = Row(alignBaselines: false);
            var small = new Label("small") { Font = new Font(TestFont.Family, 12f) };
            var large = new Label("large") { Font = new Font(TestFont.Family, 20f) };
            stack.Controls.Add(small);
            stack.Controls.Add(large);

            using var scene = HeadlessScene.Create(stack, 300f, 120f);

            // The documented default: an auto-sized child is stretched across the stack and its
            // text is centred in the stretched box, which is what the property is there to avoid.
            Assert.AreEqual(new SKRect(small.Left, 0f, small.Right, 120f), small.Bounds);
            Assert.AreEqual(new SKRect(large.Left, 0f, large.Right, 120f), large.Bounds);
        }
    }

    [TestMethod]
    public void ACheckBoxAFieldAndAButtonLineUpWithTheLabels()
    {
        using (TestFont.Pin())
        {
            var stack = Row(alignBaselines: true);
            var label = Sized("Label", 13f);
            var field = new TextBox
            {
                Text = "Field",
                Font = new Font(TestFont.Family, 13f),
                Width = 90f,
                Height = 30f,
                VerticalAlignment = Alignment.Near,
            };
            var check = new CheckBox("Check")
            {
                Font = new Font(TestFont.Family, 13f),
                VerticalAlignment = Alignment.Near,
            };
            var button = new Button("Go")
            {
                AutoSize = true,
                Font = new Font(TestFont.Family, 13f),
                VerticalAlignment = Alignment.Near,
            };

            stack.Controls.Add(label);
            stack.Controls.Add(field);
            stack.Controls.Add(check);
            stack.Controls.Add(button);

            using var scene = HeadlessScene.Create(stack, 400f, 120f);

            float expected = BaselineOf(label);
            Assert.AreEqual(expected, BaselineOf(field), 0.01f, "the field's text line");
            Assert.AreEqual(expected, BaselineOf(check), 0.01f, "the check box's label");
            Assert.AreEqual(expected, BaselineOf(button), 0.01f, "the button's label");

            Assert.AreEqual(30f, field.Height, 0.01f, "a baseline child keeps its own height");
            Assert.IsTrue(check.Height < 120f, "the check box is not stretched to the row");
            Assert.IsTrue(button.Height < 120f, "the button is not stretched to the row");
        }
    }

    /// <summary>The first-line baseline of a control's text, in its parent's coordinates.</summary>
    private static float BaselineOf(Control control)
    {
        Assert.IsTrue(
            control.TryGetBaseline(control.Size, out float baseline),
            $"{control.GetType().Name} did not report a baseline at {control.Size}");

        return control.Top + baseline;
    }

    /// <summary>A horizontal stack with the sample's framing removed, so bounds are bare numbers.</summary>
    private static StackPanel Row(bool alignBaselines) => new()
    {
        Orientation = StackOrientation.Horizontal,
        AlignBaselines = alignBaselines,
        Border = Border.None,
        Padding = Thickness.Empty,
        Spacing = 4f,
    };

    /// <summary>A label in the pinned face, at the top of its own box.</summary>
    private static Label Sized(string text, float size) => new(text)
    {
        AutoSize = true,
        Font = new Font(TestFont.Family, size),
        VerticalAlignment = Alignment.Near,
    };

    /// <summary>The shared baseline of a two-label row where the first label carries a top margin.</summary>
    private static float SharedBaselineWithMargin(float topMargin)
    {
        var stack = Row(alignBaselines: true);
        var first = Sized("first", 13f);
        first.Margin = new Thickness(0f, topMargin, 0f, 0f);
        var second = Sized("second", 13f);

        stack.Controls.Add(first);
        stack.Controls.Add(second);

        using var scene = HeadlessScene.Create(stack, 300f, 120f);
        return BaselineOf(second);
    }

    /// <summary>A two-child vertical stack, for the no-op comparison.</summary>
    private static (StackPanel Stack, List<Control> Children) VerticalRow(bool alignBaselines)
    {
        var stack = Row(alignBaselines);
        stack.Orientation = StackOrientation.Vertical;

        var first = Sized("first", 12f);
        var second = Sized("second", 20f);

        stack.Controls.Add(first);
        stack.Controls.Add(second);
        return (stack, [first, second]);
    }
}
