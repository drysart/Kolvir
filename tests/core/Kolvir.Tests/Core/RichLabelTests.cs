using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// The rich label: runs of styled text that can be clicked, and inline controls in the middle of a
/// sentence.
/// </summary>
/// <remarks>
/// Fonts are pinned, because every assertion here is about where a piece of text landed and what a
/// point over it means.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class RichLabelTests : HeadlessTestBase
{
    [TestMethod]
    public void ATextRunAndALinkBothContributeToTheText()
    {
        using var label = new RichLabel();
        var lead = label.AddText("Read the ");
        var link = label.AddLink("documentation", _ => { });
        label.AddText(" first.");

        Assert.AreEqual("Read the documentation first.", label.Text);
        Assert.AreEqual(3, label.Spans.Count);
        Assert.IsTrue(link.IsInteractive, "the middle run is a link, so it reacts to the pointer");
        Assert.IsFalse(lead.IsInteractive, "the prose around it is not");
    }

    [TestMethod]
    public void SpanAtNamesTheRunUnderAPoint()
    {
        using var culture = CultureScope.English();

        var label = new RichLabel { Dock = DockStyle.Fill };
        using (TestFont.Pin())
        {
            label.Font = TestFont.Create(13f);
            var lead = label.AddText("ab");
            var link = label.AddLink("cd", _ => { });

            using var scene = HeadlessScene.Create(label, 200f, 40f);

            Assert.AreSame(link, label.SpanAt(PointOver(label, link)), "a point over the link's glyphs names the link");
            Assert.AreSame(lead, label.SpanAt(PointOver(label, lead)), "and a point over the prose names the prose");
        }
    }

    [TestMethod]
    public void ClickingALinkRunsItsAction()
    {
        using var culture = CultureScope.English();

        var clicked = 0;
        var label = new RichLabel { Dock = DockStyle.Fill };
        using (TestFont.Pin())
        {
            label.Font = TestFont.Create(13f);
            var link = label.AddLink("open", _ => clicked++);

            using var scene = HeadlessScene.Create(label, 200f, 40f);

            SpanEventArgs? seen = null;
            label.AddSpanClickHandler((_, e) => seen = e);

            var point = PointOver(label, link);
            scene.Host.ProcessMouseMove(point);
            scene.ClickAt(point);

            Assert.AreEqual(1, clicked, "the span's own action should run");
            Assert.IsNotNull(seen, "and the label's span-click handler should see it");
            Assert.AreSame(link, seen!.Span);
        }
    }

    [TestMethod]
    public void AnInlineControlIsPartOfTheRun()
    {
        var button = new Button("Inline") { Width = 60f, Height = 24f };
        using var label = new RichLabel { Dock = DockStyle.Fill, Font = TestFont.Create(13f) };
        label.AddText("before ");
        label.AddInlineControl(button);
        label.AddText(" after");

        using var scene = HeadlessScene.Create(label, 300f, 60f);

        Assert.AreSame(label, button.Parent, "an inline control is a child of the label");
        Assert.AreEqual(3, label.Spans.Count);
        Assert.IsTrue(button.Width > 0f, "and it is laid out inside the text");
        Assert.IsTrue(label.Measure(new SKSize(300f, 200f)).Width > button.Width, "the text and the control share a line");
    }

    [TestMethod]
    public void LosingAnInlineControlsSpanRemovesItFromTheRun()
    {
        var button = new Button("Inline");
        using var label = new RichLabel { Font = TestFont.Create(13f) };
        label.AddText("a ");
        label.AddInlineControl(button);

        Assert.AreSame(label, button.Parent);

        label.ClearSpans();

        Assert.AreEqual(0, label.Spans.Count);
        Assert.IsNull(button.Parent, "clearing the runs should take the inline control back out");
    }

    [TestMethod]
    public void ARichLabelWrapsAtTheWidthItIsGiven()
    {
        using (TestFont.Pin())
        {
            var label = new RichLabel { WrapText = true, Font = TestFont.Create(13f) };
            label.AddText("a sentence long enough to need more than one line at this width");

            float wide = label.Measure(new SKSize(600f, float.PositiveInfinity)).Height;
            float narrow = label.Measure(new SKSize(80f, float.PositiveInfinity)).Height;

            Assert.IsTrue(narrow > wide, $"wrapping at 80 px should need more lines ({wide} -> {narrow})");
        }
    }

    [TestMethod]
    public void AWrappingChildInATableIsDrawnAtTheWidthItWasMeasuredAt()
    {
        using (TestFont.Pin())
        {
            var label = new RichLabel
            {
                Text = "a sentence long enough to need more than one line at this width",
                WrapText = true,
                TableColumn = 0,
                TableRow = 0,
            };

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = Thickness.Empty,
                CellPadding = Thickness.Empty,
                ColumnStyles = { ColumnStyle.Percent(100f) },
                RowStyles = { RowStyle.AutoSize() },
                Controls = { label },
            };

            using var scene = HeadlessScene.Create(table, 300f, 400f);

            Assert.AreEqual(
                300f,
                label.Bounds.Width,
                0.5f,
                "a wrapping child draws in the cell width it was measured against, not the width of its longest line");

            Assert.AreEqual(
                label.Measure(new SKSize(label.Bounds.Width, 10_000f)).Height,
                label.Bounds.Height,
                0.5f,
                "and the height it takes is the height its own text needs at that width, so no line is cut off");
        }
    }

    [TestMethod]
    public void AChildThatDoesNotWrapStillHugsItsContentInsideATableCell()
    {
        using (TestFont.Pin())
        {
            var label = new Label { Text = "abc", TableColumn = 0, TableRow = 0, AutoSize = true };

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = Thickness.Empty,
                CellPadding = Thickness.Empty,
                ColumnStyles = { ColumnStyle.Percent(100f) },
                RowStyles = { RowStyle.AutoSize() },
                Controls = { label },
            };

            using var scene = HeadlessScene.Create(table, 300f, 400f);

            Assert.IsTrue(
                label.Bounds.Width < 100f,
                $"a label that does not wrap keeps its own size rather than the cell's, got {label.Bounds.Width}");
        }
    }

    [TestMethod]
    public void AWrappingChildInAStackIsDrawnAtTheWidthItWasMeasuredAt()
    {
        using (TestFont.Pin())
        {
            var label = new RichLabel
            {
                Text = "a sentence long enough to need more than one line at this width",
                WrapText = true,
                HorizontalAlignment = Alignment.Near,
            };

            var stack = new StackPanel
            {
                Dock = DockStyle.Fill,
                Padding = Thickness.Empty,
                Orientation = StackOrientation.Vertical,
                Controls = { label },
            };

            using var scene = HeadlessScene.Create(stack, 300f, 400f);

            Assert.AreEqual(300f, label.Bounds.Width, 0.5f, "the stack width is what it wrapped against and what it is drawn at");
            Assert.AreEqual(
                label.Measure(new SKSize(label.Bounds.Width, 10_000f)).Height,
                label.Bounds.Height,
                0.5f,
                "and its height comes from that same width");
        }
    }

    [TestMethod]
    public void WrappingAtANarrowerWidthAndBackGivesTheHeightItHad()
    {
        using (TestFont.Pin())
        {
            var label = new RichLabel
            {
                Text = "a sentence long enough to need several lines once the box it is in is narrow",
                WrapText = true,
                HorizontalAlignment = Alignment.Near,
            };

            using var scene = HeadlessScene.Create(InStack(label), 320f, 600f);

            float wide = label.Bounds.Height;

            scene.Resize(160f, 600f);
            float narrow = label.Bounds.Height;

            scene.Resize(320f, 600f);

            Assert.IsTrue(narrow > wide, $"a narrower box needs more lines ({wide} -> {narrow})");
            Assert.AreEqual(wide, label.Bounds.Height, 0.5f, "and going back to the wide box gives back the height it had");
        }
    }

    [TestMethod]
    public void ChangingTheTextLaysTheLabelOutAgain()
    {
        using (TestFont.Pin())
        {
            var label = new RichLabel { Text = "short", WrapText = true };

            using var scene = HeadlessScene.Create(InStack(label), 240f, 400f);

            float before = label.Bounds.Height;

            label.Text = LongText;
            scene.Host.PerformLayout();

            Assert.IsTrue(
                label.Bounds.Height > before,
                $"the longer text needs more room, stayed at {label.Bounds.Height} from {before}");
        }
    }

    [TestMethod]
    public void ChangingTheFontLaysTheLabelOutAgain()
    {
        using (TestFont.Pin())
        {
            var label = new RichLabel
            {
                Text = LongText,
                WrapText = true,
                Font = TestFont.Create(13f),
            };

            using var scene = HeadlessScene.Create(InStack(label), 300f, 600f);

            float before = label.Bounds.Height;

            label.Font = TestFont.Create(40f);
            scene.Host.PerformLayout();

            Assert.IsTrue(
                label.Bounds.Height > before,
                $"a bigger face takes more room, stayed at {label.Bounds.Height} from {before}");
        }
    }

    [TestMethod]
    public void AnInlineControlIsMeasuredAgainWhenItChangesSize()
    {
        using (TestFont.Pin())
        {
            var inline = new Panel { Padding = Thickness.Empty, AutoSize = false };
            inline.Bounds = new SKRect(0f, 0f, 40f, 20f);

            var label = new RichLabel { WrapText = true };
            label.AddText("before ");
            label.AddInlineControl(inline);
            label.AddText(" after");

            using var scene = HeadlessScene.Create(InStack(label), 300f, 400f);

            float before = label.Bounds.Height;

            inline.Height = 80f;
            scene.Host.PerformLayout();

            Assert.IsTrue(
                label.Bounds.Height > before,
                $"the taller control makes the line taller, stayed at {label.Bounds.Height} from {before}");
        }
    }

    /// <summary>A vertical stack holding one control across its width, so its height is its own.</summary>
    private static StackPanel InStack(Control child) => new()
    {
        Dock = DockStyle.Fill,
        Padding = Thickness.Empty,
        Orientation = StackOrientation.Vertical,
        Controls = { child },
    };

    /// <summary>Text that needs more than one line in any of the boxes these tests use.</summary>
    private const string LongText =
        "a sentence long enough to need more than one line at this width, with more words after it " +
        "to be sure of that, and still more words so that a wider face needs more lines again";

    [TestMethod]
    public void TheTextPropertyIsTheConcatenationOfItsRuns()
    {
        using var label = new RichLabel { Text = "plain text" };

        Assert.AreEqual("plain text", label.Text);
        Assert.AreEqual(1, label.Spans.Count, "assigning Text builds one run");

        label.Text = "other";
        Assert.AreEqual("other", label.Text);
        Assert.AreEqual(1, label.Spans.Count, "and assigning it again replaces the run rather than appending");
    }

    [TestMethod]
    public void AnUnassignedSpanTakesTheLabelsForeColor()
    {
        using var label = new RichLabel();
        var color = new SKColor(0x11, 0x22, 0x33);
        label.ForeColor = color;
        var span = label.AddText("inherited");

        Assert.IsTrue(span.ForeColor.TryGetValue<InheritedValue>(out _), "a span with no colour reads back as inherit");
        Assert.AreEqual(color, span.EffectiveForeColor, "it resolves to the label's effective colour");

        span.ForeColor = InheritedValue.Instance;
        Assert.AreEqual(color, span.EffectiveForeColor, "and assigning inherit back restores the same answer");
    }

    [TestMethod]
    public void ASpanColorCanBeALiteralOrARole()
    {
        using var label = new RichLabel();
        var span = label.AddText("styled");

        var literal = new SKColor(1, 2, 3);
        span.ForeColor = literal;
        Assert.AreEqual(literal, span.EffectiveForeColor, "a literal is used as it is");

        span.ForeColor = ThemeColor.Accent;
        Assert.IsTrue(span.ForeColor.TryGetValue<ThemeColor>(out var role), "the property reports the role it was given");
        Assert.AreEqual(ThemeColor.Accent, role);
        Assert.AreEqual(Theme.ColorOf(ThemeColor.Accent), span.EffectiveForeColor, "a role is resolved when it is read");
    }

    [TestMethod]
    public void ASpanRoleIsResolvedWhenItIsReadSoAPaletteSwapRecolours()
    {
        using var label = new RichLabel();
        var span = label.AddText("themed");
        span.ForeColor = ThemeColor.MutedForeground;

        var light = span.EffectiveForeColor;
        Assert.AreEqual(Theme.MutedForeground, light, "the role is read from the current palette");

        Theme.Palette = Palette.Dark;

        Assert.AreEqual(Theme.MutedForeground, span.EffectiveForeColor, "the same span follows the new palette");
        Assert.AreNotEqual(light, span.EffectiveForeColor, "the two palettes should differ");
    }

    [TestMethod]
    public void ASpanHoverRoleIsResolvedWhenItIsReadSoAPaletteSwapRecolours()
    {
        using var label = new RichLabel();
        var span = label.AddText("themed");
        span.HoverBackColor = ThemeColor.MutedForeground;

        var light = span.EffectiveHoverBackColor;
        var lightMuted = Theme.ColorOf(ThemeColor.MutedForeground);
        Assert.AreEqual(lightMuted.Red, light.Red, "the role is read from the current palette");
        Assert.AreEqual(lightMuted.Green, light.Green);
        Assert.AreEqual(lightMuted.Blue, light.Blue);
        Assert.AreEqual(0x1A, light.Alpha, "and tinted while it is read");

        Theme.Palette = Palette.Dark;

        var dark = span.EffectiveHoverBackColor;
        var darkMuted = Theme.ColorOf(ThemeColor.MutedForeground);
        Assert.AreEqual(darkMuted.Red, dark.Red, "the same span follows the new palette");
        Assert.AreEqual(darkMuted.Green, dark.Green);
        Assert.AreEqual(darkMuted.Blue, dark.Blue);
        Assert.AreEqual(0x1A, dark.Alpha, "the tint follows the new palette too");
        Assert.AreNotEqual(light, dark, "the two palettes should differ");
    }

    [TestMethod]
    public void AnUnownedSpanTakesTheBaseForeground()
    {
        var span = new TextSpan("loose");

        Assert.IsNull(span.Owner);
        Assert.AreEqual(Theme.ColorOf(ThemeColor.Foreground), span.EffectiveForeColor);
    }

    [TestMethod]
    public void AnUnspecifiedSpanColorIsRefused()
    {
        var span = new TextSpan("bad");
        span.ForeColor = default;

        Assert.ThrowsExactly<InvalidOperationException>(
            () => _ = span.EffectiveForeColor,
            "a default-initialised union holds no case and must not be read as inherit");

        span.HoverBackColor = default;

        Assert.ThrowsExactly<InvalidOperationException>(
            () => _ = span.EffectiveHoverBackColor,
            "the hover fill refuses an unspecified union the same way");
    }

    [TestMethod]
    public void ALinkNamesTheAccentRoleForTextAndHoverBack()
    {
        using var label = new RichLabel();
        var link = label.AddLink("documentation");

        Assert.IsTrue(link.ForeColor.TryGetValue<ThemeColor>(out var textRole), "a link's colour is a role");
        Assert.AreEqual(ThemeColor.Accent, textRole);
        Assert.IsTrue(link.HoverBackColor.TryGetValue<ThemeColor>(out var hoverRole), "so is its hover fill");
        Assert.AreEqual(ThemeColor.Accent, hoverRole);
    }

    [TestMethod]
    public void ASpansHoverFillIsTransparentUntilOneIsGiven()
    {
        using var label = new RichLabel();
        var span = label.AddText("hover");

        Assert.AreEqual(SKColors.Transparent, span.EffectiveHoverBackColor, "nothing assigned is no fill");

        var literal = new SKColor(10, 20, 30);
        span.HoverBackColor = literal;
        Assert.AreEqual(literal, span.EffectiveHoverBackColor, "a literal keeps its own alpha");

        var accent = Theme.ColorOf(ThemeColor.Accent);
        span.HoverBackColor = ThemeColor.Accent;
        var tinted = span.EffectiveHoverBackColor;
        Assert.AreEqual(accent.Red, tinted.Red);
        Assert.AreEqual(accent.Green, tinted.Green);
        Assert.AreEqual(accent.Blue, tinted.Blue);
        Assert.AreEqual(0x1A, tinted.Alpha, "a role is tinted so the text stays legible");
    }

    /// <summary>
    /// The first point inside the label whose glyphs belong to <paramref name="span"/>. Searching
    /// for it keeps the assertion about hit-testing rather than about where a font put a glyph.
    /// </summary>
    private static SKPoint PointOver(RichLabel label, TextSpan span)
    {
        var rect = label.ContentRect;

        for (float y = rect.Top + 1f; y < MathF.Min(rect.Bottom, rect.Top + 60f); y += 1f)
        {
            for (float x = rect.Left + 1f; x < rect.Right; x += 1f)
            {
                var point = new SKPoint(x, y);
                if (ReferenceEquals(label.SpanAt(point), span))
                {
                    return point;
                }
            }
        }

        Assert.Fail("the label has no point over the requested run");
        return default;
    }
}
