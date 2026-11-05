using SkiaSharp;
using XPlatUI;
using XPlatUI.Multiline;

namespace XPlatUI.Tests;

/// <summary>
/// The single-line text box's editing behaviour, and the numeric field built on it.
/// </summary>
/// <remarks>
/// The multi-line document, the annotated spans and the undo history have their own classes; this
/// one covers the editing surface every text field has: the caret, selection, the clipboard,
/// <see cref="XPlatTextBox.MaxLength"/> and <see cref="XPlatTextBox.ReadOnly"/>, and the keyboard
/// a native single-line field would take.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class TextBoxTests : HeadlessTestBase
{
    [TestMethod]
    public void TypingInsertsAtTheCaretAndMovesIt()
    {
        using var scene = Box("abc", out var box);

        scene.Type("de");

        Assert.AreEqual("deabc", box.Text, "typing should insert at the caret, which starts at 0");
        Assert.AreEqual(2, box.CaretIndex);
        Assert.IsFalse(box.HasSelection);
    }

    [TestMethod]
    public void TypingAtTheEndAppends()
    {
        using var scene = Box("ab", out var box);

        box.CaretIndex = 2;
        scene.Type("cd");

        Assert.AreEqual("abcd", box.Text);
        Assert.AreEqual(4, box.CaretIndex);
    }

    [TestMethod]
    public void BackspaceAndDeleteRemoveEitherSideOfTheCaret()
    {
        using var scene = Box("abcd", out var box);

        box.CaretIndex = 2;
        scene.Key(XPlatKey.Backspace);
        Assert.AreEqual("acd", box.Text, "Backspace should remove the character before the caret");
        Assert.AreEqual(1, box.CaretIndex);

        scene.Key(XPlatKey.Delete);
        Assert.AreEqual("ad", box.Text, "Delete should remove the character after the caret");
        Assert.AreEqual(1, box.CaretIndex);
    }

    [TestMethod]
    public void ArrowKeysMoveTheCaretAndStopAtTheEnds()
    {
        using var scene = Box("abc", out var box);

        box.CaretIndex = 1;

        scene.Key(XPlatKey.Right);
        Assert.AreEqual(2, box.CaretIndex);

        scene.Key(XPlatKey.End);
        Assert.AreEqual(3, box.CaretIndex);

        scene.Key(XPlatKey.Right);
        Assert.AreEqual(3, box.CaretIndex, "Right at the end should stay at the end");

        scene.Key(XPlatKey.Home);
        Assert.AreEqual(0, box.CaretIndex);

        scene.Key(XPlatKey.Left);
        Assert.AreEqual(0, box.CaretIndex, "Left at the start should stay at the start");
    }

    [TestMethod]
    public void MaxLengthStopsTypingAndPasting()
    {
        using var scene = Box("abc", out var box);
        box.MaxLength = 4;
        box.CaretIndex = 3;

        scene.Type("de");

        Assert.AreEqual("abcd", box.Text, "only the characters that fit should be inserted");
        Assert.AreEqual(4, box.CaretIndex);

        scene.Type("e");
        Assert.AreEqual("abcd", box.Text, "a full field should ignore further typing");
    }

    [TestMethod]
    public void AReadOnlyFieldIgnoresEditingButStillTakesFocus()
    {
        using var scene = Box("locked", out var box);
        box.ReadOnly = true;
        box.CaretIndex = 0;

        scene.Type("x");
        scene.Key(XPlatKey.Backspace);
        scene.Key(XPlatKey.Delete);
        box.SelectAll();
        scene.Key(XPlatKey.Backspace);

        Assert.AreEqual("locked", box.Text, "a read-only field should not change");
        Assert.IsTrue(box.Focusable, "but it is still focusable, so its text can be selected and copied");
    }

    [TestMethod]
    public void SelectionIsReportedAndCanBeCleared()
    {
        using var scene = Box("hello world", out var box);

        box.Select(6, 5);

        Assert.AreEqual(6, box.SelectionStart);
        Assert.AreEqual(5, box.SelectionLength);
        Assert.AreEqual("world", box.SelectedText);
        Assert.IsTrue(box.HasSelection);
        Assert.AreEqual(11, box.CaretIndex, "Select should leave the caret at the end of the selection");

        box.ClearSelection();
        Assert.IsFalse(box.HasSelection);
        Assert.AreEqual(11, box.CaretIndex);

        box.SelectAll();
        Assert.AreEqual(11, box.SelectionLength);
        Assert.AreEqual("hello world", box.SelectedText);
    }

    [TestMethod]
    public void CopyAndPasteRoundTripThroughTheClipboard()
    {
        using var scene = Box("copy this", out var box);

        box.Select(0, 4);
        Assert.IsTrue(box.Copy());
        Assert.AreEqual("copy", XPlatClipboard.GetText());

        box.CaretIndex = box.Text.Length;
        Assert.IsTrue(box.Paste());

        Assert.AreEqual("copy thiscopy", box.Text);
        Assert.AreEqual(13, box.CaretIndex);
    }

    [TestMethod]
    public void CutRemovesTheSelectionAndPutsItOnTheClipboard()
    {
        using var scene = Box("cut this", out var box);

        box.Select(0, 3);
        Assert.IsTrue(box.Cut());

        Assert.AreEqual(" this", box.Text);
        Assert.AreEqual("cut", XPlatClipboard.GetText());
        Assert.AreEqual(0, box.CaretIndex);
    }

    [TestMethod]
    public void TheCaretIsClampedToTheText()
    {
        using var scene = Box("abc", out var box);

        box.CaretIndex = 99;
        Assert.AreEqual(3, box.CaretIndex, "the caret should stop at the end of the text");

        box.CaretIndex = -5;
        Assert.AreEqual(0, box.CaretIndex, "and at the start");
    }

    [TestMethod]
    public void AssigningShorterTextClampsTheCaret()
    {
        using var scene = Box("abcdef", out var box);

        box.CaretIndex = 6;
        box.Text = "abc";

        Assert.AreEqual(3, box.CaretIndex, "a shorter value should take the caret to its end, not to the start");
        Assert.IsFalse(box.HasSelection);
    }

    [TestMethod]
    public void AssigningShorterTextClampsTheSelection()
    {
        using var scene = Box("abcdef", out var box);

        box.Select(1, 4);
        box.Text = "abc";

        Assert.AreEqual(1, box.SelectionStart, "the selection should keep its start where the new text still has room");
        Assert.AreEqual(2, box.SelectionLength, "and end at the new end of the text");
        Assert.AreEqual(3, box.CaretIndex);
    }

    [TestMethod]
    public void AssigningTheValueTheBoxAlreadyHoldsLeavesTheCaretAlone()
    {
        using var scene = Box("abcdef", out var box);

        box.CaretIndex = 4;
        box.Text = "abcdef";

        Assert.AreEqual(4, box.CaretIndex, "an assignment of the value the box holds is not an edit and moves nothing");
    }

    [TestMethod]
    public void APasswordFieldsCaretIsClampedAgainstTheRealText()
    {
        using var scene = Box("secret", out var box);

        box.IsPassword = true;
        box.CaretIndex = 6;
        box.Text = "ab";

        Assert.AreEqual(2, box.CaretIndex, "the caret indexes the text, not the mask");
        Assert.AreEqual("ab", box.Text);
    }

    [TestMethod]
    public void AssigningShorterTextClampsTheCaretOfAMultiLineField()
    {
        var multiline = new XPlatTextBox("one\ntwo\nthree") { Multiline = true };
        using var scene = Box("one\ntwo\nthree", out var box, multiline);

        box.CaretIndex = box.Text.Length;
        box.Text = "ab\ncd";

        Assert.AreEqual(5, box.CaretIndex, "the clamp is against the whole multi-line value, not its last line");
        Assert.IsFalse(box.HasSelection);
    }

    [TestMethod]
    public void TypingOverASelectionReplacesIt()
    {
        using var scene = Box("one two three", out var box);

        box.Select(4, 3);
        scene.Type("2");

        Assert.AreEqual("one 2 three", box.Text);
        Assert.AreEqual(5, box.CaretIndex);
        Assert.IsFalse(box.HasSelection);
    }

    [TestMethod]
    public void ThePasswordFlagMasksWithoutChangingTheText()
    {
        using var scene = Box("secret", out var box);

        box.IsPassword = true;
        Assert.AreEqual("secret", box.Text, "masking is a paint decision, not a text one");
        Assert.AreEqual('\u2022', box.PasswordChar, "the default mask is a bullet");
    }

    [TestMethod]
    public void ThePlaceholderIsOnlyWhatAnEmptyFieldShows()
    {
        using var scene = Box(string.Empty, out var box);
        box.PlaceholderText = "Search";

        Assert.AreEqual("Search", box.PlaceholderText);
        Assert.IsTrue(string.IsNullOrEmpty(box.Text), "a placeholder is not the field's text");
    }

    [TestMethod]
    public void ATextBoxRaisesTextChangedForEveryEdit()
    {
        using var scene = Box(string.Empty, out var box);
        int changes = 0;
        box.AddTextChangedHandler(_ => changes++);

        box.CaretIndex = 0;
        scene.Type("a");
        scene.Type("b");
        scene.Key(XPlatKey.Backspace);

        Assert.AreEqual(3, changes, "two inserts and a delete are three changes");
        Assert.AreEqual("a", box.Text);
    }

    [TestMethod]
    public void ANumericFieldStartsAtItsMinimumAndIsClampedToItsRange()
    {
        using var field = new XPlatNumericUpDown();

        Assert.AreEqual(0m, field.Minimum);
        Assert.AreEqual(100m, field.Maximum);
        Assert.AreEqual(0m, field.Value);

        field.Value = 500m;
        Assert.AreEqual(100m, field.Value, "a value above the maximum is clamped");

        field.Value = -500m;
        Assert.AreEqual(0m, field.Value, "and so is a value below the minimum");
    }

    [TestMethod]
    public void UpAndDownStepByTheIncrement()
    {
        using var field = new XPlatNumericUpDown { Value = 10m, Increment = 2.5m, DecimalPlaces = 1 };

        field.Up();
        Assert.AreEqual(12.5m, field.Value);

        field.Down();
        field.Down();
        Assert.AreEqual(7.5m, field.Value);
    }

    [TestMethod]
    public void TheFieldShowsTheValueFormattedToItsDecimalPlaces()
    {
        using var field = new XPlatNumericUpDown { DecimalPlaces = 2, Value = 3m };

        Assert.AreEqual("3.00", field.Text, "the text should follow the decimal places");

        field.DecimalPlaces = 0;
        Assert.AreEqual("3", field.Text);
    }

    [TestMethod]
    public void TheArrowKeysStepAFocusedFieldAndAreConsumed()
    {
        using var scene = Box(string.Empty, out var box, new XPlatNumericUpDown { Value = 5m });
        var field = (XPlatNumericUpDown)box;
        box.Focus();

        Assert.IsTrue(scene.Host.ProcessKeyDown(XPlatKey.Up), "the field should consume Up");
        Assert.AreEqual(6m, field.Value);

        scene.Key(XPlatKey.Down);
        scene.Key(XPlatKey.Down);
        Assert.AreEqual(4m, field.Value);
    }

    [TestMethod]
    public void TheWheelStepsTheValueUnderThePointer()
    {
        using var scene = Box(string.Empty, out var box, new XPlatNumericUpDown { Value = 5m });
        var field = (XPlatNumericUpDown)box;

        scene.WindowBackend.SimulateMouseWheel(scene.CenterOf(field), 48f);
        Assert.AreEqual(6m, field.Value, "a turn toward the start should step up");

        scene.WindowBackend.SimulateMouseWheel(scene.CenterOf(field), -48f);
        Assert.AreEqual(5m, field.Value);
    }

    [TestMethod]
    public void AValueChangedHandlerFollowsEveryCommittedChange()
    {
        using var field = new XPlatNumericUpDown { Value = 1m };
        int changes = 0;
        field.AddValueChangedHandler(_ => changes++);

        field.Value = 2m;
        Assert.AreEqual(1, changes);

        field.Value = 2m;
        Assert.AreEqual(1, changes, "assigning the value it already holds should not raise");

        field.Up();
        Assert.AreEqual(2, changes, "a step is a change");
    }

    [TestMethod]
    public void ANonPositiveIncrementIsRejected()
    {
        using var field = new XPlatNumericUpDown();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => field.Increment = 0m);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => field.Increment = -1m);
    }

    [TestMethod]
    public void APointInTheTextMapsBackToAnIndexOfThePinnedFace()
    {
        using (TestFont.Pin())
        {
            using var scene = Box("hello world", out var box);

            // GetSelectionOffset is what turns a pointer position into a caret index, so it is the
            // public way to ask "where would a click at this x land".
            float nearestStart = box.GetSelectionOffset(new SKPoint(1f, box.Height / 2f));
            float nearestEnd = box.GetSelectionOffset(new SKPoint(box.Width - 2f, box.Height / 2f));

            Assert.AreEqual(0f, nearestStart, 0.5f, "a point at the left edge should map to the first character");
            Assert.IsTrue(nearestEnd > nearestStart, $"a point at the right edge should map further in ({nearestStart} -> {nearestEnd})");
        }
    }

    /// <summary>
    /// One focused text field filling a headless window. The generic overload lets the numeric field
    /// reuse the same setup.
    /// </summary>
    private static HeadlessScene Box(string text, out XPlatTextBox box)
    {
        box = new XPlatTextBox(text) { Dock = DockStyle.Fill };
        var scene = HeadlessScene.Create(box, 320f, 60f);
        box.Focus();
        return scene;
    }

    private static HeadlessScene Box(string text, out XPlatTextBox box, XPlatTextBox custom)
    {
        box = custom;
        box.Dock = DockStyle.Fill;
        var scene = HeadlessScene.Create(box, 320f, 60f);
        box.Focus();
        return scene;
    }
}
