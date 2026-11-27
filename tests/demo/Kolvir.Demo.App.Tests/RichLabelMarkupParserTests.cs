namespace Kolvir.Demo.Tests;

/// <summary>
/// The rich-label page's parser: the small markup grammar a person types into the box, and the runs it
/// becomes.
/// </summary>
/// <remarks>
/// The parser runs on every keystroke over whatever is in the box, so the tests here are about the two
/// properties that matter to a page in front of a person: every rule of the grammar maps onto a run a
/// <c>TextSpan</c> can express, and no input at all - empty, half-typed, hostile or enormous - can make
/// it throw.
/// </remarks>
[TestClass]
public sealed class RichLabelMarkupParserTests
{
    [TestMethod]
    public void PlainTextIsOneRunWithNoStyles()
    {
        var runs = RichLabelMarkup.Parse("hello, world");

        Assert.AreEqual(1, runs.Count);
        AssertRun(runs[0], "hello, world");
    }

    [TestMethod]
    public void EachTagSetsTheStyleItNames()
    {
        var runs = RichLabelMarkup.Parse("<b>bold</b> <i>italic</i> <u>under</u>");

        Assert.AreEqual(5, runs.Count);
        AssertRun(runs[0], "bold", bold: true);
        AssertRun(runs[1], " ");
        AssertRun(runs[2], "italic", italic: true);
        AssertRun(runs[3], " ");
        AssertRun(runs[4], "under", underline: true);
    }

    [TestMethod]
    public void NestedTagsCombineTheirStyles()
    {
        var runs = RichLabelMarkup.Parse("<b>a<i>b</i>c</b>");

        Assert.AreEqual(3, runs.Count);
        AssertRun(runs[0], "a", bold: true);
        AssertRun(runs[1], "b", bold: true, italic: true);
        AssertRun(runs[2], "c", bold: true);
    }

    [TestMethod]
    public void AllThreeStylesCombine()
    {
        var runs = RichLabelMarkup.Parse("<b><i><u>x</u></i></b>");

        Assert.AreEqual(1, runs.Count);
        AssertRun(runs[0], "x", bold: true, italic: true, underline: true);
    }

    [TestMethod]
    public void AnUnclosedTagRunsToTheEndOfTheText()
    {
        var runs = RichLabelMarkup.Parse("plain <b>bold to the end");

        Assert.AreEqual(2, runs.Count);
        AssertRun(runs[0], "plain ");
        AssertRun(runs[1], "bold to the end", bold: true);
    }

    [TestMethod]
    public void AnUnclosedInnerTagLeavesTheOuterOneOnTheRest()
    {
        var runs = RichLabelMarkup.Parse("<b>a<i>b</b>");

        // </b> closes bold while italic is still on, so the run that ends carries both styles and the
        // italic that was never closed would carry on - there is simply nothing after it.
        Assert.AreEqual(2, runs.Count);
        AssertRun(runs[0], "a", bold: true);
        AssertRun(runs[1], "b", bold: true, italic: true);
    }

    [TestMethod]
    public void AStrayClosingTagIsLiteralText()
    {
        var runs = RichLabelMarkup.Parse("x</b>y");

        Assert.AreEqual(1, runs.Count);
        AssertRun(runs[0], "x</b>y");
    }

    [TestMethod]
    public void AStrayClosingTagIsLiteralEvenAfterItsStyleWasClosed()
    {
        var runs = RichLabelMarkup.Parse("<b>a</b></b>");

        Assert.AreEqual(2, runs.Count);
        AssertRun(runs[0], "a", bold: true);
        AssertRun(runs[1], "</b>");
    }

    [TestMethod]
    public void AStrayClosingTagKeepsTheStylesInForceAroundIt()
    {
        var runs = RichLabelMarkup.Parse("<i>a</b>b</i>");

        Assert.AreEqual(1, runs.Count);
        AssertRun(runs[0], "a</b>b", italic: true);
    }

    [TestMethod]
    [DataRow("<blink>hi</blink>")]
    [DataRow("<B>hi</B>")]
    [DataRow("<b >hi</b >")]
    [DataRow("<b")]
    [DataRow("<")]
    [DataRow("<bold>hi")]
    [DataRow("<b attr=1>hi")]
    [DataRow("a <> b")]
    [DataRow("a </> b")]
    public void UnknownAndMalformedTagsAreShownAsTyped(string input)
    {
        var runs = RichLabelMarkup.Parse(input);

        AssertOneRunOf(input, runs);
    }

    [TestMethod]
    public void AnEmptyTagPairProducesNoRuns()
    {
        Assert.AreEqual(0, RichLabelMarkup.Parse("<b></b>").Count);
        Assert.AreEqual(0, RichLabelMarkup.Parse("<b><i><u></u></i></b>").Count);
    }

    [TestMethod]
    public void TheEscapeProducesALiteralAngleBracket()
    {
        var runs = RichLabelMarkup.Parse("a&lt;b");

        Assert.AreEqual(1, runs.Count, "an escape does not split the run around it");
        AssertRun(runs[0], "a<b");
    }

    [TestMethod]
    public void OnlyTheEscapeIsAnEscape()
    {
        // &amp; is not part of the grammar, so it stays as typed - the same rule that keeps an unknown
        // tag visible instead of silently eaten.
        AssertOneRunOf("a&amp;b", RichLabelMarkup.Parse("a&amp;b"));
        AssertOneRunOf("a & b", RichLabelMarkup.Parse("a & b"));
        AssertOneRunOf("&lt", RichLabelMarkup.Parse("&lt"));
        AssertOneRunOf("&LT;", RichLabelMarkup.Parse("&LT;"));
    }

    [TestMethod]
    public void EmptyInputProducesNoRuns()
    {
        Assert.AreEqual(0, RichLabelMarkup.Parse(null).Count);
        Assert.AreEqual(0, RichLabelMarkup.Parse(string.Empty).Count);
    }

    [TestMethod]
    public void WhitespaceAndLineBreaksAreOneRunOfText()
    {
        var runs = RichLabelMarkup.Parse("  \n\t  ");

        Assert.AreEqual(1, runs.Count);
        AssertRun(runs[0], "  \n\t  ");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("<")]
    [DataRow("</")]
    [DataRow("<b")]
    [DataRow("</b")]
    [DataRow("<>")]
    [DataRow("&")]
    [DataRow("&l")]
    [DataRow("&lt;")]
    [DataRow("<b><i><u>")]
    [DataRow("</u></i></b>")]
    [DataRow("<b>></b><<")]
    [DataRow("\0<\r\n>")]
    [DataRow("héllo <b>→</b>")]
    public void ParsingIsTotal(string input)
    {
        // Every one of these is something a person can have typed at this moment. None may throw, and
        // none may consume a character it did not recognise as a tag or an escape.
        var runs = RichLabelMarkup.Parse(input);

        Assert.IsNotNull(runs);
        Assert.IsTrue(
            runs.All(run => run.Text.Length > 0),
            "a run is never empty, however the tags fall");
    }

    [TestMethod]
    public void ALongMalformedStringIsOneRunAndDoesNotAllocatePerCharacter()
    {
        // A hundred thousand characters that all look like the start of a closing tag and never are
        // one: the case where matching with a substring or a regular expression would allocate for
        // every character of the input.
        var input = string.Concat(Enumerable.Repeat("</xx", 25_000));
        Assert.AreEqual(100_000, input.Length);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var runs = RichLabelMarkup.Parse(input);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(1, runs.Count, "nothing in the input is a tag, so all of it is one run");
        AssertRun(runs[0], input);

        // The run builder's growth plus the string it copies out, per run rather than per character.
        // An implementation that allocated as it scanned would spend an order of magnitude more.
        Assert.IsTrue(
            allocated < input.Length * 8L,
            $"parsing {input.Length} characters allocated {allocated} bytes");
    }

    private static void AssertOneRunOf(string input, IReadOnlyList<MarkupSpan> runs)
    {
        Assert.AreEqual(1, runs.Count, $"'{input}' should be one run");
        AssertRun(runs[0], input);
    }

    private static void AssertRun(MarkupSpan run, string text, bool bold = false, bool italic = false, bool underline = false)
    {
        Assert.AreEqual(text, run.Text, "text");
        Assert.AreEqual(bold, run.Bold, $"bold of '{text}'");
        Assert.AreEqual(italic, run.Italic, $"italic of '{text}'");
        Assert.AreEqual(underline, run.Underline, $"underline of '{text}'");
    }
}
