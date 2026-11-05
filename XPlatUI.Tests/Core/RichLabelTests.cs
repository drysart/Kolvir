using SkiaSharp;
using XPlatUI;

namespace XPlatUI.Tests;

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
        using var label = new XPlatRichLabel();
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

        var label = new XPlatRichLabel { Dock = DockStyle.Fill };
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
        var label = new XPlatRichLabel { Dock = DockStyle.Fill };
        using (TestFont.Pin())
        {
            label.Font = TestFont.Create(13f);
            var link = label.AddLink("open", _ => clicked++);

            using var scene = HeadlessScene.Create(label, 200f, 40f);

            XPlatSpanEventArgs? seen = null;
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
        var button = new XPlatButton("Inline") { Width = 60f, Height = 24f };
        using var label = new XPlatRichLabel { Dock = DockStyle.Fill, Font = TestFont.Create(13f) };
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
        var button = new XPlatButton("Inline");
        using var label = new XPlatRichLabel { Font = TestFont.Create(13f) };
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
            var label = new XPlatRichLabel { WrapText = true, Font = TestFont.Create(13f) };
            label.AddText("a sentence long enough to need more than one line at this width");

            float wide = label.Measure(new SKSize(600f, float.PositiveInfinity)).Height;
            float narrow = label.Measure(new SKSize(80f, float.PositiveInfinity)).Height;

            Assert.IsTrue(narrow > wide, $"wrapping at 80 px should need more lines ({wide} -> {narrow})");
        }
    }

    [TestMethod]
    public void TheTextPropertyIsTheConcatenationOfItsRuns()
    {
        using var label = new XPlatRichLabel { Text = "plain text" };

        Assert.AreEqual("plain text", label.Text);
        Assert.AreEqual(1, label.Spans.Count, "assigning Text builds one run");

        label.Text = "other";
        Assert.AreEqual("other", label.Text);
        Assert.AreEqual(1, label.Spans.Count, "and assigning it again replaces the run rather than appending");
    }

    /// <summary>
    /// The first point inside the label whose glyphs belong to <paramref name="span"/>. Searching
    /// for it keeps the assertion about hit-testing rather than about where a font put a glyph.
    /// </summary>
    private static SKPoint PointOver(XPlatRichLabel label, XPlatTextSpan span)
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
