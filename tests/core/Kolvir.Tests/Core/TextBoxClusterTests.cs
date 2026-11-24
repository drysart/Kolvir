using System.Globalization;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace Kolvir.Tests;

/// <summary>
/// Where the caret may rest: one text element at a time, never between the halves of a character.
/// </summary>
/// <remarks>
/// <para>
/// The defect these pin is old: Left and Right stepped by one UTF-16 code unit, so a caret could land
/// inside the surrogate pair that spells an emoji, and Backspace removed half of one, leaving a lone
/// surrogate in the value. What made it visible was font fallback - a prefix that ends mid-pair became
/// a prefix whose width is not monotonic, so the caret jumped back and forth as it moved - but the
/// stepping was wrong before fallback existed.
/// </para>
/// <para>
/// The last test compares the boundaries against <see cref="StringInfo"/>, which implements the
/// Unicode text segmentation algorithm, so these rules are held to the real answer rather than to what
/// the cases below happen to need.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class TextBoxClusterTests : HeadlessTestBase
{
    /// <summary>The text from the report: a composed word, two spaces, then an emoji.</summary>
    private const string Reported = "かんじ  \U0001F44C";

    /// <summary>The same, with text after the emoji, which is where a caret has somewhere to go next.</summary>
    private const string InTheMiddle = "かんじ  \U0001F44C end";

    [TestMethod]
    public void PressingRightAndLeftWalksEveryCharacterOnceWithoutGoingBackwards()
    {
        WalkTheField(Reported);
        WalkTheField(InTheMiddle);
    }

    [TestMethod]
    public void SettingTheCaretInsideAnEmojiLandsBeforeIt()
    {
        using var scene = Box("a\U0001F44Cb", out var box);

        // Index 2 is between the two halves of the emoji. The field takes the boundary at or before it,
        // which is the only place a caret can honestly be.
        box.CaretIndex = 2;

        Assert.AreEqual(1, box.CaretIndex, "the caret cannot rest between a high and a low surrogate");
    }

    [TestMethod]
    public void TheCaretXNeverGoesBackwardsAcrossACombiningMark()
    {
        WalkTheField("cafe\u0301 latte");
        WalkTheField("a\u0301\u0328b");
    }

    [TestMethod]
    public void RightFromBeforeTheEmojiLandsAfterItInOnePress()
    {
        using var scene = Box("a\U0001F44Cb", out var box);

        box.CaretIndex = 1;
        scene.Key(Key.Right);

        Assert.AreEqual(3, box.CaretIndex, "Right steps over the whole emoji, not into it");

        scene.Key(Key.Left);

        Assert.AreEqual(1, box.CaretIndex, "and Left steps back over the whole of it");
    }

    [TestMethod]
    public void BackspaceRemovesTheWholeEmojiAndLeavesNoLoneSurrogate()
    {
        using var scene = Box("a\U0001F44C", out var box);

        box.CaretIndex = box.Text.Length;
        scene.Key(Key.Backspace);

        Assert.AreEqual("a", box.Text, "the whole emoji goes, not one code unit of it");
        AssertNoSurrogates(box.Text);
    }

    [TestMethod]
    public void DeleteRemovesTheWholeEmojiAndLeavesNoLoneSurrogate()
    {
        using var scene = Box("a\U0001F44Cb", out var box);

        box.CaretIndex = 1;
        scene.Key(Key.Delete);

        Assert.AreEqual("ab", box.Text, "Delete removes the whole emoji ahead of the caret");
        AssertNoSurrogates(box.Text);
    }

    [TestMethod]
    public void BackspaceRemovesACombiningSequenceAsOneElement()
    {
        using var scene = Box("xe\u0301", out var box);

        box.CaretIndex = box.Text.Length;
        scene.Key(Key.Backspace);

        Assert.AreEqual("x", box.Text, "the base and its accent go together");
    }

    [TestMethod]
    public void ShiftRightThenShiftLeftReturnsToWhereItStarted()
    {
        using var scene = Box(InTheMiddle, out var box);

        box.CaretIndex = 5;
        int start = box.CaretIndex;

        scene.Key(Key.Right, Modifiers.Shift);
        Assert.AreEqual(7, box.CaretIndex, "Shift+Right selects the emoji as one element");
        Assert.AreEqual(2, box.SelectionLength, "which is two code units of selection");

        scene.Key(Key.Left, Modifiers.Shift);

        Assert.AreEqual(start, box.CaretIndex, "Shift+Left returns the caret exactly where it started");
        Assert.AreEqual(0, box.SelectionLength, "and the selection is empty again");
    }

    [TestMethod]
    public void TheBoundariesAgreeWithTheUnicodeSegmentationTheFrameworkImplements()
    {
        string[] samples =
        {
            Reported,
            InTheMiddle,
            "a\U0001F44Cb",
            "cafe\u0301",
            "a\u0301\u0328b",
            // A family emoji is four people joined by zero-width joiners.
            "\U0001F468\u200D\U0001F469\u200D\U0001F467",
            // A flag emoji is a pair of regional indicators.
            "\U0001F1EF\U0001F1F5",
            "ab\r\ncd",
            string.Empty,
            "plain ascii",
        };

        foreach (var text in samples)
        {
            var expected = new List<int>(StringInfo.ParseCombiningCharacters(text));
            expected.Add(text.Length);

            var actual = new List<int> { 0 };
            for (int i = 0; i < text.Length && actual[^1] < text.Length;)
            {
                i = TextCluster.Next(text, i);
                actual.Add(i);
            }

            Assert.AreEqual(
                string.Join(",", expected),
                string.Join(",", actual),
                $"forward boundaries for '{Escape(text)}'");

            var backwards = new List<int> { text.Length };
            for (int i = text.Length; i > 0;)
            {
                i = TextCluster.Previous(text, i);
                backwards.Add(i);
            }

            backwards.Reverse();
            Assert.AreEqual(
                string.Join(",", expected),
                string.Join(",", backwards),
                $"backward boundaries for '{Escape(text)}'");
        }
    }

    /// <summary>
    /// Walks the field with the arrow keys the way a person did, recording where the caret stopped and
    /// what x it was drawn at: every stop must be a character boundary, and x must never go backwards
    /// on the way out or forwards on the way back. That is the report: "the caret jumps back and forth,
    /// it doesn't move cleanly from left to right".
    /// </summary>
    private static void WalkTheField(string text)
    {
        // A field wide enough that the caret never scrolls: a scroll offset would make the absolute x
        // of the end of the walk a different number without meaning anything is wrong.
        using var scene = Box(text, out var box, 2000f);

        box.CaretIndex = 0;
        int steps = 0;
        float previous = float.MinValue;
        var stops = new List<float>();

        while (true)
        {
            var caret = box.CaretBounds;
            Assert.IsFalse(caret.IsEmpty, "the field is laid out, so the caret has a rectangle");
            Assert.IsTrue(
                TextCluster.IsBoundary(text, box.CaretIndex),
                $"the caret stopped at {box.CaretIndex}, which is inside a character of '{Escape(text)}'");
            Assert.IsTrue(
                caret.Left >= previous - 0.001f,
                $"the caret at {box.CaretIndex} is drawn at x={caret.Left}, before x={previous}");

            previous = caret.Left;
            stops.Add(caret.Left);

            if (box.CaretIndex >= text.Length)
            {
                break;
            }

            int before = box.CaretIndex;
            scene.Key(Key.Right);
            Assert.IsTrue(box.CaretIndex > before, "Right always moves forward");

            steps++;
            Assert.IsTrue(steps <= text.Length + 1, "Right reaches the end instead of looping");
        }

        // The same walk backwards must come back through the same places, monotonic the other way.
        float forwards = previous;
        while (true)
        {
            scene.Key(Key.Left);
            var caret = box.CaretBounds;
            Assert.IsTrue(
                caret.Left <= forwards + 0.001f,
                $"the caret at {box.CaretIndex} is drawn at x={caret.Left}, after x={forwards}");
            forwards = caret.Left;
            if (box.CaretIndex == 0)
            {
                break;
            }
        }

        Assert.AreEqual(stops[0], forwards, 0.001f, "the walk back ends exactly where the walk out began");
    }

    private static void AssertNoSurrogates(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            Assert.IsFalse(
                char.IsSurrogate(text[i]),
                $"'{Escape(text)}' contains a lone surrogate at {i}, which is half a character");
        }
    }

    private static string Escape(string text) =>
        new StringBuilder().Append(text.Select(c => char.IsSurrogate(c) ? '?' : c).ToArray()).ToString();

    private static HeadlessScene Box(string text, out TextBox box) => Box(text, out box, 320f);

    private static HeadlessScene Box(string text, out TextBox box, float width)
    {
        box = new TextBox(text) { Dock = DockStyle.Fill };
        var scene = HeadlessScene.Create(box, width, 60f);
        box.Focus();
        return scene;
    }
}
