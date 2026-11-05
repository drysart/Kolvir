using SkiaSharp;
using XPlatUI;
using XPlatUI.Multiline;
using XPlatUI.Spans;
using XPlatUI.Undo;

namespace XPlatUI.Tests;

/// <summary>
/// The text box's undo history, ported from the demo application's <c>--undo-check</c> harness.
/// </summary>
/// <remarks>
/// <para>
/// Every scenario builds a real text box in a real (headless) window and drives it through the
/// host's input entry points - the same ones the three window backends call - rather than calling
/// private helpers. Time comes from <see cref="TestClock"/>, so "a gap over five seconds" is exact
/// and the whole class runs in milliseconds.
/// </para>
/// <para>
/// The harness printed one PASS/FAIL line per scenario and exited non-zero. Each scenario is now a
/// test, and each half of its conjunction is its own assertion so a failure names the part that
/// broke rather than the whole sentence.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class UndoHistoryTests : HeadlessTestBase
{
    [TestMethod]
    public void ACharacterRunCoalescesIntoOneEntry()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;
            Type(host, clock, "abc");

            Assert.AreEqual(1, box.UndoCount, "three characters inside the window should be one entry");

            host.ProcessKeyDown(XPlatKey.Z, XPlatModifiers.Control);

            Assert.AreEqual(string.Empty, box.Text, "Ctrl+Z should take the whole run back");
            Assert.AreEqual(1, box.RedoCount, "the undone run should be offered as a redo");
        });
    }

    [TestMethod]
    public void AGapOverFiveSecondsSplitsTheRun()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;
            Type(host, clock, "ab");
            clock.Advance(5_000);
            Type(host, clock, "c");

            Assert.AreEqual(2, box.UndoCount, "a gap longer than the window should start a new entry");

            box.Undo();
            Assert.AreEqual("ab", box.Text, "the first undo should take back only the later run");

            box.Undo();
            Assert.AreEqual(string.Empty, box.Text, "the second undo should take back the first run");
        });
    }

    [TestMethod]
    public void AFiveSecondGapIsStillTheSameRun()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;

            // Type already moves the clock 120 ms on, so this makes the gap exactly the window
            // rather than a millisecond past it.
            Type(host, clock, "a");
            clock.Advance(5_000 - 120);
            Type(host, clock, "b");

            Assert.AreEqual(1, box.UndoCount, "the coalescing window is inclusive");
        });
    }

    [TestMethod]
    public void TypingAndDeletingDoNotMerge()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;
            Type(host, clock, "abc");
            clock.Advance(100);
            host.ProcessKeyDown(XPlatKey.Backspace);
            clock.Advance(100);
            host.ProcessKeyDown(XPlatKey.Backspace);

            Assert.AreEqual(2, box.UndoCount, "a run of typing and a run of deleting are two entries");

            box.Undo();
            Assert.AreEqual("abc", box.Text, "undoing the deletes should restore what they removed");
        });
    }

    [TestMethod]
    public void ABurstOfDeletingCoalescesOnItsOwn()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;
            box.Text = "abcdef";
            box.CaretIndex = 6;
            box.ClearUndoStack();

            for (int i = 0; i < 3; i++)
            {
                host.ProcessKeyDown(XPlatKey.Backspace);
                clock.Advance(120);
            }

            Assert.AreEqual(1, box.UndoCount, "three backspaces should be one entry");

            box.Undo();

            Assert.AreEqual(0, box.UndoCount, "one undo should consume the whole burst");
            Assert.AreEqual("abcdef", box.Text, "and restore every character the burst removed");
        });
    }

    [TestMethod]
    public void AReplacedSelectionIsOneEntryThatRestoresTheText()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;
            box.Text = "Hello brave world";
            box.ClearUndoStack();

            // "brave" is 6..11; the caret starts after it and the selection is made right to left,
            // so the anchor stays at 11 and the caret walks to 6.
            box.CaretIndex = 11;

            for (int i = 0; i < 5; i++)
            {
                host.ProcessKeyDown(XPlatKey.Left, XPlatModifiers.Shift);
            }

            Type(host, clock, "X");

            Assert.AreEqual("Hello X world", box.Text, "typing should replace the selection");
            Assert.AreEqual(1, box.UndoCount, "the replacement should be one entry");

            box.Undo();

            Assert.AreEqual("Hello brave world", box.Text, "undo should restore the replaced text");
            Assert.AreEqual(6, box.SelectionStart, "and reselect it");
            Assert.AreEqual(5, box.SelectionLength);
            Assert.AreEqual(6, box.CaretIndex);

            box.Redo();

            Assert.AreEqual("Hello X world", box.Text, "redo should reapply the replacement");
            Assert.AreEqual(7, box.CaretIndex);
            Assert.AreEqual(0, box.SelectionLength);
        });
    }

    [TestMethod]
    public void TheCaretIsRestoredOnUndoAndOnRedo()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;
            box.Text = "hello world";
            box.ClearUndoStack();
            box.CaretIndex = 5;

            Type(host, clock, "XY");
            Assert.AreEqual(7, box.CaretIndex, "typing should leave the caret after the run");

            host.ProcessKeyDown(XPlatKey.Z, XPlatModifiers.Control);
            Assert.AreEqual("hello world", box.Text);
            Assert.AreEqual(5, box.CaretIndex, "undo should put the caret back where the edit began");
            Assert.AreEqual(0, box.SelectionLength);

            host.ProcessKeyDown(XPlatKey.Y, XPlatModifiers.Control);
            Assert.AreEqual("helloXY world", box.Text);
            Assert.AreEqual(7, box.CaretIndex, "redo should put the caret back where the edit ended");

            host.ProcessKeyDown(XPlatKey.Z, XPlatModifiers.Control);
            host.ProcessKeyDown(XPlatKey.Z, XPlatModifiers.Control | XPlatModifiers.Shift);

            Assert.AreEqual("helloXY world", box.Text, "Ctrl+Shift+Z should redo");
            Assert.AreEqual(7, box.CaretIndex);
        });
    }

    [TestMethod]
    public void RedoIsDiscardedByANewEdit()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;
            Type(host, clock, "ab");
            box.Undo();

            Assert.IsTrue(box.CanRedo, "an undo should leave something to redo");

            Type(host, clock, "x");

            Assert.IsFalse(box.CanRedo, "a new edit should drop the redo side");
            Assert.AreEqual("x", box.Text);
        });
    }

    [TestMethod]
    public void ClearUndoStackEmptiesBothSides()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;
            box.Text = "first";
            Type(host, clock, "one");

            // Undoing once leaves an entry on each side, which is the only way both are non-empty at
            // the same time: a new edit clears the redo side by design.
            box.Undo();

            Assert.IsTrue(box.CanUndo && box.CanRedo, "both sides should hold an entry");
            Assert.AreEqual(1, box.UndoCount);
            Assert.AreEqual(1, box.RedoCount);

            box.ClearUndoStack();

            Assert.IsFalse(box.CanUndo, "no undo should remain");
            Assert.IsFalse(box.CanRedo, "no redo should remain");
            Assert.AreEqual(0, box.UndoCount);
            Assert.AreEqual(0, box.RedoCount);
        });
    }

    [TestMethod]
    public void TheDepthCapHolds()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;
            box.Text = string.Empty;
            box.ClearUndoStack();

            // One entry per character: the clock moves past the window between them.
            for (int i = 0; i < 105; i++)
            {
                Type(host, clock, "x", gap: 0);
                clock.Advance(6_000);
            }

            Assert.AreEqual(100, box.UndoCount, "the history should stop growing at its cap");

            while (box.Undo())
            {
            }

            // 105 characters in, 100 entries kept, so the oldest five are past recovery.
            Assert.AreEqual("xxxxx", box.Text, "undoing everything should leave the characters the cap dropped");
        });
    }

    [TestMethod]
    public void AMultiLineDocumentUndoesAcrossLineBreaksAndRestoresTheView()
    {
        WithBox((box, host, clock) =>
        {
            box.Multiline = true;
            box.UndoEnabled = true;

            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < 60; i++)
            {
                builder.Append("line ").Append(i + 1).Append(" of a long document\n");
            }

            box.Text = builder.ToString();
            box.ClearUndoStack();

            int end = box.Text.Length;
            box.CaretIndex = end;
            float scroll = box.VerticalOffset;

            Type(host, clock, "\nlast");

            Assert.AreEqual(end + 5, box.Text.Length, "the edit should be recorded");
            Assert.IsTrue(scroll > 0f, "60 lines should not fit the viewport");

            // Scroll away from the edit, then take it back.
            box.CaretIndex = 0;
            Assert.IsTrue(box.VerticalOffset < scroll, "moving the caret to the top should scroll up");

            host.ProcessKeyDown(XPlatKey.Z, XPlatModifiers.Control);

            Assert.AreEqual(end, box.Text.Length, "undo should take the edit back");
            Assert.AreEqual(end, box.CaretIndex, "and restore the caret");
            Assert.AreEqual(scroll, box.VerticalOffset, 0.5f, "and restore the scroll offset it was made at");
        });
    }

    [TestMethod]
    public void SpansAreRemarkedAfterAnUndo()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;
            box.Text = "The quick brown fox jumpped over the dog";
            box.ClearUndoStack();

            Remark(box);
            box.AddTextChangedHandler(_ => Remark(box));

            var initial = box.Spans;
            Assert.IsNotNull(initial, "the page's re-marking should have produced a span");
            Assert.AreEqual(1, initial!.Count);

            int at = box.Text.IndexOf("jumpped", StringComparison.Ordinal);
            box.Select(at, "jumpped".Length);
            Type(host, clock, "jumped");

            // Typing over the selection is one entry for the replaced run and then a run of its
            // own for the rest of the word, so the history is walked all the way back.
            box.Undo();
            Assert.IsNull(box.Spans, "the fix removed the misspelling, so nothing should be marked");
            Assert.IsFalse(box.Text.Contains("jumpped", StringComparison.Ordinal));

            while (box.Undo())
            {
            }

            var remarked = box.Spans;
            Assert.IsNotNull(remarked, "undoing back to the misspelling should re-mark it");
            Assert.AreEqual(1, remarked!.Count);
            Assert.AreEqual("jumpped", box.Text.Substring(remarked[0].Start, remarked[0].Length));
        });
    }

    [TestMethod]
    public void AStaleSpanPaintsInsteadOfThrowing()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;
            box.Text = "short text";
            box.ClearUndoStack();
            box.Spans = [new XPlatTextBoxSpan(4000, 900), new XPlatTextBoxSpan(3, 0)];

            Type(host, clock, "x");
            box.Undo();

            Paint(box);

            Assert.AreEqual("short text", box.Text);
        });
    }

    [TestMethod]
    public void AReadOnlyFieldHasNoUndoAndRecordsNothing()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;
            box.Text = "locked";
            box.ReadOnly = true;

            Type(host, clock, "abc");

            Assert.AreEqual("locked", box.Text, "a read-only field should ignore typing");
            Assert.IsFalse(box.CanUndo || box.CanRedo, "and report no history");
            Assert.IsFalse(box.Undo(), "Undo should do nothing");
            Assert.IsFalse(box.Redo(), "Redo should do nothing");
            Assert.AreEqual("locked", box.Text);

            box.ReadOnly = false;

            Assert.IsTrue(box.CanUndo, "the history should still be there once the field is editable again");
            Assert.IsTrue(box.Undo());
            Assert.AreEqual(string.Empty, box.Text, "and the original assignment should be undoable");
        });
    }

    [TestMethod]
    public void TurningMultilineOffClearsTheHistory()
    {
        WithBox((box, host, clock) =>
        {
            box.Multiline = true;
            box.UndoEnabled = true;
            box.Text = "one\ntwo";
            box.ClearUndoStack();
            box.CaretIndex = box.Text.Length;
            Type(host, clock, "\nthree");

            Assert.AreEqual(1, box.UndoCount, "the edit should have recorded an entry");

            box.Multiline = false;

            Assert.AreEqual(0, box.UndoCount, "dropping the multi-line document should drop its history");
            Assert.IsFalse(box.CanUndo);
            Assert.IsFalse(box.Undo());
            Assert.IsFalse(box.Text.Contains('\n'), "a single-line field should not keep the line break");
        });
    }

    [TestMethod]
    public void AProgrammaticAssignmentIsUndoableAndAnEchoIsFree()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;
            box.Text = "loaded";

            Assert.AreEqual(1, box.UndoCount, "assigning Text should be one entry");

            // What a two-way binding does on every change: write the value the box already holds
            // straight back. It must not add an entry or drop the run.
            box.AddTextChangedHandler(_ => box.Text = box.Text);
            Type(host, clock, "abc");

            Assert.AreEqual(2, box.UndoCount, "an echo should not add an entry");

            box.Undo();
            Assert.AreEqual("loaded", box.Text, "the typed run should come back off");

            box.Undo();
            Assert.AreEqual(string.Empty, box.Text, "and the assignment with it");
        });
    }

    [TestMethod]
    public void TheStateChangedHandlerFollowsTheHistory()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;
            int raises = 0;
            box.AddUndoStateChangedHandler(_ => raises++);

            // Two characters, one entry: the handler runs once per recorded edit.
            Type(host, clock, "ab");
            Assert.AreEqual(2, raises, "each recorded edit should raise, coalesced or not");

            box.Undo();
            box.Redo();
            box.ClearUndoStack();

            Assert.AreEqual(5, raises, "undo, redo and clear should each raise in turn");
            Assert.IsFalse(box.CanUndo);
            Assert.IsFalse(box.CanRedo);
        });
    }

    [TestMethod]
    public void APasteIsOneEntryAndUndoRestoresWhatItReplaced()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;
            box.Text = "keep this";
            box.ClearUndoStack();
            box.Select(0, 4);

            XPlatClipboard.SetText("paste");
            host.ProcessKeyDown(XPlatKey.V, XPlatModifiers.Control);

            Assert.AreEqual("paste this", box.Text);
            Assert.AreEqual(1, box.UndoCount, "a paste should be one entry");

            host.ProcessKeyDown(XPlatKey.Z, XPlatModifiers.Control);

            Assert.AreEqual("keep this", box.Text);
            Assert.AreEqual(0, box.SelectionStart);
            Assert.AreEqual(4, box.SelectionLength, "undo should restore the selection the paste replaced");
        });
    }

    [TestMethod]
    public void TheEditingBlockLeadsWithUndoAndRedo()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;

            var menu = ContextMenuOf(box, host);

            Assert.AreEqual(
                "Undo, Redo, (sep), Cut, Copy, Paste, (sep), Select All",
                ItemList(menu),
                "the history block should lead the editing menu");
            Assert.IsFalse(EnabledOf(menu, "Undo"), "Undo should start disabled");
            Assert.IsFalse(EnabledOf(menu, "Redo"), "Redo should start disabled");
        });
    }

    [TestMethod]
    public void TheMenuRowsFollowCanUndoAndCanRedo()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;
            Type(host, clock, "ab");

            var typed = ContextMenuOf(box, host);
            Assert.IsTrue(EnabledOf(typed, "Undo"), "Undo should be enabled after an edit");
            Assert.IsFalse(EnabledOf(typed, "Redo"), "Redo should still be disabled");

            // Invoking the row is the same operation as Ctrl+Z: the text goes and the caret with it.
            Assert.IsTrue(Invoke(typed, "Undo"), "the Undo row should invoke");
            Assert.AreEqual(string.Empty, box.Text);
            Assert.AreEqual(0, box.CaretIndex);

            var afterUndo = ContextMenuOf(box, host);
            Assert.IsFalse(EnabledOf(afterUndo, "Undo"));
            Assert.IsTrue(EnabledOf(afterUndo, "Redo"));

            Assert.IsTrue(Invoke(afterUndo, "Redo"), "the Redo row should invoke");
            Assert.AreEqual("ab", box.Text);
            Assert.AreEqual(2, box.CaretIndex);

            var afterRedo = ContextMenuOf(box, host);
            Assert.IsTrue(EnabledOf(afterRedo, "Undo"));
            Assert.IsFalse(EnabledOf(afterRedo, "Redo"));
        });
    }

    [TestMethod]
    public void TheMenusUndoRestoresAReplacedSelectionAndItsCaret()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;
            box.Text = "Hello brave world";
            box.ClearUndoStack();
            box.Select(6, 5);
            int caretBefore = box.CaretIndex;

            Type(host, clock, "X");
            Assert.AreEqual("Hello X world", box.Text);

            var menu = ContextMenuOf(box, host);
            Assert.IsTrue(Invoke(menu, "Undo"));

            Assert.AreEqual("Hello brave world", box.Text);
            Assert.AreEqual(6, box.SelectionStart);
            Assert.AreEqual(5, box.SelectionLength);
            Assert.AreEqual(caretBefore, box.CaretIndex);
        });
    }

    [TestMethod]
    public void AReadOnlyBoxShowsBothRowsDisabled()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;
            Type(host, clock, "ab");
            box.ReadOnly = true;

            var menu = ContextMenuOf(box, host);

            Assert.AreEqual("Undo, Redo, (sep), Cut, Copy, Paste, (sep), Select All", ItemList(menu));
            Assert.IsFalse(EnabledOf(menu, "Undo"));
            Assert.IsFalse(EnabledOf(menu, "Redo"));

            Assert.IsFalse(Invoke(menu, "Undo"), "a disabled row should swallow the click");
            Assert.AreEqual("ab", box.Text);
        });
    }

    [TestMethod]
    public void ABoxWithoutTheHistoryGetsNoUndoOrRedoRows()
    {
        WithBox((box, host, _) =>
        {
            var menu = ContextMenuOf(box, host);

            Assert.AreEqual("Cut, Copy, Paste, (sep), Select All", ItemList(menu));
        });
    }

    [TestMethod]
    public void AnApplicationThatReplacesTheMenuKeepsItsOwnBlock()
    {
        WithBox((box, host, clock) =>
        {
            box.UndoEnabled = true;
            Type(host, clock, "ab");

            XPlatContextMenu? fromBox = null;
            XPlatContextMenu? shown = null;

            using (box.AddContextMenuOpeningHandler(args =>
            {
                fromBox = args.Menu;
                args.Menu = new XPlatContextMenu().Add("App item");
            }))
            using (box.AddContextMenuOpeningHandler(args =>
            {
                shown = args.Menu;
                args.Menu = null;
            }))
            {
                host.ProcessContextMenu(new SKPoint(12f, 12f));
            }

            Assert.IsTrue(
                fromBox?.Items.Any(item => item.Text == "Undo") == true,
                "the box's own block should reach the first handler");
            Assert.AreEqual("App item", ItemList(shown), "the second handler should see the replacement");
        });
    }

    /// <summary>
    /// Builds one focused text box in a real (headless) window and runs <paramref name="body"/>
    /// against it. A fresh clock per scenario keeps the gaps exact.
    /// </summary>
    private static void WithBox(Action<XPlatTextBox, XPlatUiHost, TestClock> body)
    {
        var clock = new TestClock();
        XPlatUndoClock.Current = clock;

        var box = new XPlatTextBox { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(box, 560f, 320f);
        box.Focus();

        body(box, scene.Host, clock);
    }

    /// <summary>Types <paramref name="text"/> one character at a time, moving the clock on between them.</summary>
    private static void Type(XPlatUiHost host, TestClock clock, string text, long gap = 120)
    {
        foreach (char c in text)
        {
            host.ProcessTextInput(c.ToString());
            clock.Advance(gap);
        }
    }

    /// <summary>
    /// Right-clicks <paramref name="box"/> through the host's own entry point and returns the editing
    /// block the box contributed to it.
    /// </summary>
    private static XPlatContextMenu? ContextMenuOf(XPlatTextBox box, XPlatUiHost host)
    {
        XPlatContextMenu? captured = null;

        // The block is what the control hands to its own opening handlers, so a handler is the way
        // to see exactly what a consumer gets. The menu is cleared afterwards: this checks the rows,
        // not the presenter, and a popup left open would own the keyboard for the rest of the test.
        using (box.AddContextMenuOpeningHandler(args =>
        {
            captured = args.Menu;
            args.Menu = null;
        }))
        {
            host.ProcessContextMenu(new SKPoint(12f, 12f));
        }

        return captured;
    }

    /// <summary>The rows of a menu as one line, with separators spelled out.</summary>
    private static string ItemList(XPlatContextMenu? menu) => menu is null
        ? "<none>"
        : string.Join(", ", menu.Items.Select(item => item.IsSeparator ? "(sep)" : item.Text));

    /// <summary>Whether the row called <paramref name="text"/> is enabled; false when there is no such row.</summary>
    private static bool EnabledOf(XPlatContextMenu? menu, string text) =>
        menu?.Items.FirstOrDefault(item => item.Text == text)?.Enabled == true;

    /// <summary>
    /// Clicks the row called <paramref name="text"/>, which is what the presenter does. Returns false
    /// when the row is missing or disabled, the way a disabled row swallows the click.
    /// </summary>
    private static bool Invoke(XPlatContextMenu? menu, string text) =>
        menu?.Items.FirstOrDefault(item => item.Text == text)?.Invoke() == true;

    /// <summary>Paints the box once, so a paint-path bug shows up as a failure rather than a crash later.</summary>
    private static void Paint(XPlatTextBox box)
    {
        using var surface = SKSurface.Create(new SKImageInfo(200, 60));
        surface.Canvas.Clear(SKColors.White);
        box.Host!.Paint(surface.Canvas, full: true);
        surface.Canvas.Flush();
    }

    /// <summary>The page's own miniature checker, so the tests exercise the same re-marking an application does.</summary>
    private static void Remark(XPlatTextBox box)
    {
        var text = box.Text;
        List<XPlatTextBoxSpan> spans = [];

        foreach (string word in new[] { "jumpped", "lazzy" })
        {
            int at = text.IndexOf(word, StringComparison.Ordinal);
            if (at >= 0)
            {
                spans.Add(new XPlatTextBoxSpan(at, word.Length) { Tag = word });
            }
        }

        box.Spans = spans;
    }
}
