using SkiaSharp;
using XPlatUI;
using XPlatUI.Backend.Headless;
using XPlatUI.Multiline;
using XPlatUI.Spans;
using XPlatUI.Undo;
using XDock = XPlatUI.DockStyle;

namespace XPlatUI.Demo;

/// <summary>
/// The headless input harness for the text box's undo history: it builds a real control tree on the
/// headless backend, drives it through the host's own input entry points - the same ones the three
/// window backends call - and checks what the history made of it.
/// </summary>
/// <remarks>
/// <para>
/// Time is the one thing a "wait five seconds" check cannot do by waiting, so the harness installs
/// its own <see cref="TimeProvider"/> into <see cref="XPlatUndoClock.Current"/> and moves it by
/// hand. Every gap the history measures is therefore exact and the whole run takes milliseconds.
/// </para>
/// <para>
/// Run it with <c>XPlatUIDemoApp --undo-check</c>. It prints one line per check and exits non-zero
/// when any of them fails, so it is usable as a gate on every platform.
/// </para>
/// </remarks>
internal static class UndoChecks
{
    /// <summary>Runs every check and returns a process exit code.</summary>
    /// <param name="reportPath">
    /// Where to write the same lines that go to the console, or null not to. The sample is a Windows
    /// GUI executable with no console of its own, so a run started from a script has to read the
    /// report from a file there.
    /// </param>
    public static int Run(string? reportPath = null)
    {
        var clock = new FakeClock();
        XPlatUndoClock.Current = clock;

        try
        {
            var results = new List<Outcome>
            {
                RunScenario("a character run coalesces into one entry", clock, (box, host) =>
                {
                    box.UndoEnabled = true;
                    Type(host, clock, "abc");

                    bool one = box.UndoCount == 1;
                    host.ProcessKeyDown(XPlatKey.Z, XPlatModifiers.Control);
                    return (one && box.Text.Length == 0 && box.RedoCount == 1,
                        $"entries {(one ? 1 : 0)}, text after Ctrl+Z \"{box.Text}\"");
                }),

                RunScenario("a gap over five seconds splits the run", clock, (box, host) =>
                {
                    box.UndoEnabled = true;
                    Type(host, clock, "ab");
                    clock.Advance(5_000);
                    Type(host, clock, "c");

                    bool two = box.UndoCount == 2;
                    box.Undo();
                    bool afterFirst = box.Text == "ab";
                    box.Undo();
                    return (two && afterFirst && box.Text.Length == 0,
                        $"entries {box.UndoCount + 1}, first undo \"{box.Text}\"");
                }),

                RunScenario("a five second gap is still the same run", clock, (box, host) =>
                {
                    box.UndoEnabled = true;

                    // Type already moves the clock 120 ms on, so this makes the gap exactly the
                    // window rather than a millisecond past it.
                    Type(host, clock, "a");
                    clock.Advance(5_000 - 120);
                    Type(host, clock, "b");

                    return (box.UndoCount == 1, $"entries {box.UndoCount}");
                }),

                RunScenario("typing and deleting do not merge", clock, (box, host) =>
                {
                    box.UndoEnabled = true;
                    Type(host, clock, "abc");
                    clock.Advance(100);
                    host.ProcessKeyDown(XPlatKey.Backspace);
                    clock.Advance(100);
                    host.ProcessKeyDown(XPlatKey.Backspace);

                    bool two = box.UndoCount == 2;
                    box.Undo();
                    bool typingBack = box.Text == "abc";
                    return (two && typingBack, $"entries {(two ? 2 : -1)}, undo left \"{box.Text}\"");
                }),

                RunScenario("a burst of deleting coalesces on its own", clock, (box, host) =>
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

                    box.Undo();
                    return (box.UndoCount == 0 && box.Text == "abcdef", $"restored \"{box.Text}\"");
                }),

                RunScenario("a replaced selection is one entry that restores the text", clock, (box, host) =>
                {
                    box.UndoEnabled = true;
                    box.Text = "Hello brave world";
                    box.ClearUndoStack();

                    // "brave" is 6..11; the caret starts after it and the selection is made right to
                    // left, so the anchor stays at 11 and the caret walks to 6.
                    box.CaretIndex = 11;

                    for (int i = 0; i < 5; i++)
                    {
                        host.ProcessKeyDown(XPlatKey.Left, XPlatModifiers.Shift);
                    }

                    Type(host, clock, "X");

                    bool replaced = box.Text == "Hello X world" && box.UndoCount == 1;
                    box.Undo();
                    bool restored =
                        box.Text == "Hello brave world" &&
                        box.SelectionStart == 6 &&
                        box.SelectionLength == 5 &&
                        box.CaretIndex == 6;

                    box.Redo();
                    bool reapplied = box.Text == "Hello X world" && box.CaretIndex == 7 && box.SelectionLength == 0;

                    return (replaced && restored && reapplied,
                        $"after undo \"{box.Text}\" sel {box.SelectionStart}+{box.SelectionLength}, redo \"{box.Text}\"");
                }),

                RunScenario("the caret is restored on undo and on redo", clock, (box, host) =>
                {
                    box.UndoEnabled = true;
                    box.Text = "hello world";
                    box.ClearUndoStack();
                    box.CaretIndex = 5;

                    Type(host, clock, "XY");
                    bool typed = box.CaretIndex == 7;

                    host.ProcessKeyDown(XPlatKey.Z, XPlatModifiers.Control);
                    bool undone = box.Text == "hello world" && box.CaretIndex == 5 && box.SelectionLength == 0;

                    host.ProcessKeyDown(XPlatKey.Y, XPlatModifiers.Control);
                    bool redone = box.Text == "helloXY world" && box.CaretIndex == 7;

                    host.ProcessKeyDown(XPlatKey.Z, XPlatModifiers.Control);
                    host.ProcessKeyDown(XPlatKey.Z, XPlatModifiers.Control | XPlatModifiers.Shift);
                    bool shiftRedo = box.Text == "helloXY world" && box.CaretIndex == 7;

                    return (typed && undone && redone && shiftRedo,
                        $"caret {box.CaretIndex}, text \"{box.Text}\"");
                }),

                RunScenario("redo is discarded by a new edit", clock, (box, host) =>
                {
                    box.UndoEnabled = true;
                    Type(host, clock, "ab");
                    box.Undo();
                    bool hadRedo = box.CanRedo;
                    Type(host, clock, "x");

                    return (hadRedo && !box.CanRedo && box.Text == "x", $"text \"{box.Text}\"");
                }),

                RunScenario("ClearUndoStack empties both sides", clock, (box, host) =>
                {
                    box.UndoEnabled = true;
                    box.Text = "first";
                    Type(host, clock, "one");

                    // Undoing once leaves an entry on each side, which is the only way both are
                    // non-empty at the same time: a new edit clears the redo side by design.
                    box.Undo();
                    bool before = box.CanUndo && box.CanRedo && box.UndoCount == 1 && box.RedoCount == 1;

                    box.ClearUndoStack();
                    bool after = !box.CanUndo && !box.CanRedo && box.UndoCount == 0 && box.RedoCount == 0;

                    return (before && after, $"undo {box.UndoCount}, redo {box.RedoCount}");
                }),

                RunScenario("the depth cap holds", clock, (box, host) =>
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

                    int capped = box.UndoCount;
                    while (box.Undo())
                    {
                    }

                    return (capped == 100 && box.Text == "xxxxx",
                        $"entries {capped}, text after undoing all of them \"{box.Text}\"");
                }),

                RunScenario("a multi-line document undoes across line breaks and restores the view", clock, (box, host) =>
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
                    bool atEnd = box.Text.Length == end + 5;

                    // Scroll away from the edit, then take it back.
                    box.CaretIndex = 0;
                    bool scrolledAway = box.VerticalOffset < scroll;
                    host.ProcessKeyDown(XPlatKey.Z, XPlatModifiers.Control);

                    return (atEnd && scrolledAway && scroll > 0f &&
                        box.Text.Length == end &&
                        box.CaretIndex == end &&
                        MathF.Abs(box.VerticalOffset - scroll) < 0.5f,
                        $"scroll {scroll:0.#} -> {box.VerticalOffset:0.#}, caret {box.CaretIndex}, length {box.Text.Length}");
                }),

                RunScenario("spans are re-marked after an undo", clock, (box, host) =>
                {
                    box.UndoEnabled = true;
                    box.Text = "The quick brown fox jumpped over the dog";
                    box.ClearUndoStack();

                    Remark(box);
                    box.AddTextChangedHandler(_ => Remark(box));
                    bool initiallyMarked = box.Spans is { Count: 1 };

                    int at = box.Text.IndexOf("jumpped", StringComparison.Ordinal);
                    box.Select(at, "jumpped".Length);
                    Type(host, clock, "jumped");

                    // Typing over the selection is one entry for the replaced run and then a run of
                    // its own for the rest of the word, so the history is walked all the way back.
                    box.Undo();
                    bool marked = box.Spans is null && !box.Text.Contains("jumpped", StringComparison.Ordinal);

                    while (box.Undo())
                    {
                    }

                    bool remarked = false;
                    if (box.Spans is { } spans)
                    {
                        remarked =
                            spans.Count == 1 &&
                            spans[0].End <= box.Text.Length &&
                            box.Text.Substring(spans[0].Start, spans[0].Length) == "jumpped";
                    }

                    return (initiallyMarked && marked && remarked &&
                        box.Text.Contains("jumpped", StringComparison.Ordinal),
                        $"spans {box.Spans?.Count ?? 0}, text \"{box.Text}\"");
                }),

                RunScenario("a stale span paints instead of throwing", clock, (box, host) =>
                {
                    box.UndoEnabled = true;
                    box.Text = "short text";
                    box.ClearUndoStack();
                    box.Spans = [new XPlatTextBoxSpan(4000, 900), new XPlatTextBoxSpan(3, 0)];
                    Type(host, clock, "x");
                    box.Undo();
                    Paint(box);
                    return (box.Text == "short text", $"text \"{box.Text}\"");
                }),

                RunScenario("a read-only field has no undo and records nothing", clock, (box, host) =>
                {
                    box.UndoEnabled = true;
                    box.Text = "locked";
                    box.ReadOnly = true;

                    Type(host, clock, "abc");
                    bool ignored = box.Text == "locked";
                    bool reports = box.CanUndo || box.CanRedo || box.Undo() || box.Redo();
                    bool afterUndo = box.Text == "locked";

                    box.ReadOnly = false;

                    return (ignored && !reports && afterUndo && box.CanUndo && box.Undo() && box.Text.Length == 0,
                        $"text \"{box.Text}\"");
                }),

                RunScenario("turning multi-line off clears the history", clock, (box, host) =>
                {
                    box.Multiline = true;
                    box.UndoEnabled = true;
                    box.Text = "one\ntwo";
                    box.ClearUndoStack();
                    box.CaretIndex = box.Text.Length;
                    Type(host, clock, "\nthree");
                    bool entries = box.UndoCount == 1;

                    box.Multiline = false;
                    bool cleared = box.UndoCount == 0 && !box.CanUndo && !box.Undo();

                    return (entries && cleared && !box.Text.Contains('\n'),
                        $"entries after toggle {box.UndoCount}, text \"{box.Text}\"");
                }),

                RunScenario("a programmatic assignment is undoable and an echo is free", clock, (box, host) =>
                {
                    box.UndoEnabled = true;
                    box.Text = "loaded";
                    bool recorded = box.UndoCount == 1;

                    // What a two-way binding does on every change: write the value the box already
                    // holds straight back. It must not add an entry or drop the run.
                    box.AddTextChangedHandler(_ => box.Text = box.Text);
                    Type(host, clock, "abc");

                    bool oneRun = box.UndoCount == 2;
                    box.Undo();
                    bool typedGone = box.Text == "loaded";
                    box.Undo();
                    bool assignmentGone = box.Text.Length == 0;

                    return (recorded && oneRun && typedGone && assignmentGone, $"entries {box.UndoCount + 1}");
                }),

                RunScenario("the state-changed handler follows the history", clock, (box, host) =>
                {
                    box.UndoEnabled = true;
                    int raises = 0;
                    box.AddUndoStateChangedHandler(_ => raises++);

                    // Two characters, one entry: the handler runs once per recorded edit.
                    Type(host, clock, "ab");
                    int afterTyping = raises;

                    box.Undo();
                    box.Redo();
                    box.ClearUndoStack();

                    return (afterTyping == 2 && raises == 5 && !box.CanUndo && !box.CanRedo,
                        $"raises {afterTyping} then {raises}");
                }),

                RunScenario("a paste is one entry and undo restores what it replaced", clock, (box, host) =>
                {
                    box.UndoEnabled = true;
                    box.Text = "keep this";
                    box.ClearUndoStack();
                    box.Select(0, 4);

                    XPlatClipboard.SetText("paste");
                    host.ProcessKeyDown(XPlatKey.V, XPlatModifiers.Control);
                    bool pasted = box.Text == "paste this" && box.UndoCount == 1;

                    host.ProcessKeyDown(XPlatKey.Z, XPlatModifiers.Control);
                    return (pasted && box.Text == "keep this" && box.SelectionStart == 0 && box.SelectionLength == 4,
                        $"\"{box.Text}\" sel {box.SelectionStart}+{box.SelectionLength}");
                }),

                RunScenario("the editing block leads with Undo and Redo", clock, (box, host) =>
                {
                    box.UndoEnabled = true;

                    var menu = ContextMenuOf(box, host);
                    bool order = ItemList(menu) == "Undo, Redo, (sep), Cut, Copy, Paste, (sep), Select All";
                    bool bothOff = EnabledOf(menu, "Undo") == false && EnabledOf(menu, "Redo") == false;

                    return (order && bothOff, $"items [{ItemList(menu)}], undo {EnabledOf(menu, "Undo")}, redo {EnabledOf(menu, "Redo")}");
                }),

                RunScenario("the menu rows follow CanUndo and CanRedo", clock, (box, host) =>
                {
                    box.UndoEnabled = true;
                    Type(host, clock, "ab");

                    var typed = ContextMenuOf(box, host);
                    bool afterEdit = EnabledOf(typed, "Undo") && !EnabledOf(typed, "Redo");

                    // Invoking the row is the same operation as Ctrl+Z: the text goes and the caret
                    // with it.
                    bool invokedUndo = Invoke(typed, "Undo");
                    bool undone = invokedUndo && box.Text.Length == 0 && box.CaretIndex == 0;

                    var afterUndo = ContextMenuOf(box, host);
                    bool redoOffered = !EnabledOf(afterUndo, "Undo") && EnabledOf(afterUndo, "Redo");

                    bool invokedRedo = Invoke(afterUndo, "Redo");
                    bool redone = invokedRedo && box.Text == "ab" && box.CaretIndex == 2;

                    var afterRedo = ContextMenuOf(box, host);
                    bool undoOffered = EnabledOf(afterRedo, "Undo") && !EnabledOf(afterRedo, "Redo");

                    return (afterEdit && undone && redoOffered && redone && undoOffered,
                        $"after edit u/r {EnabledOf(typed, "Undo")}/{EnabledOf(typed, "Redo")}, " +
                        $"after undo u/r {EnabledOf(afterUndo, "Undo")}/{EnabledOf(afterUndo, "Redo")}, " +
                        $"caret {box.CaretIndex}, text \"{box.Text}\"");
                }),

                RunScenario("the menu's Undo restores a replaced selection and its caret", clock, (box, host) =>
                {
                    box.UndoEnabled = true;
                    box.Text = "Hello brave world";
                    box.ClearUndoStack();
                    box.Select(6, 5);
                    int caretBefore = box.CaretIndex;

                    Type(host, clock, "X");
                    bool replaced = box.Text == "Hello X world";

                    var menu = ContextMenuOf(box, host);
                    bool invoked = Invoke(menu, "Undo");

                    return (replaced && invoked &&
                        box.Text == "Hello brave world" &&
                        box.SelectionStart == 6 &&
                        box.SelectionLength == 5 &&
                        box.CaretIndex == caretBefore,
                        $"\"{box.Text}\" sel {box.SelectionStart}+{box.SelectionLength} caret {box.CaretIndex} (was {caretBefore})");
                }),

                RunScenario("a read-only box shows both rows disabled", clock, (box, host) =>
                {
                    box.UndoEnabled = true;
                    Type(host, clock, "ab");
                    box.ReadOnly = true;

                    var menu = ContextMenuOf(box, host);
                    bool present = ItemList(menu) == "Undo, Redo, (sep), Cut, Copy, Paste, (sep), Select All";
                    bool bothOff = EnabledOf(menu, "Undo") == false && EnabledOf(menu, "Redo") == false;

                    bool invoked = Invoke(menu, "Undo");
                    bool untouched = !invoked && box.Text == "ab";

                    return (present && bothOff && untouched,
                        $"items [{ItemList(menu)}], undo {EnabledOf(menu, "Undo")}, redo {EnabledOf(menu, "Redo")}, text \"{box.Text}\"");
                }),

                RunScenario("a box without the history gets no Undo or Redo rows", clock, (box, host) =>
                {
                    var menu = ContextMenuOf(box, host);

                    return (ItemList(menu) == "Cut, Copy, Paste, (sep), Select All",
                        $"items [{ItemList(menu)}]");
                }),

                RunScenario("an application that replaces the menu keeps its own block", clock, (box, host) =>
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

                    bool blockHadRows = fromBox?.Items.Any(item => item.Text == "Undo") == true;
                    bool replaced = ItemList(shown) == "App item";

                    return (blockHadRows && replaced,
                        $"box [{ItemList(fromBox)}], shown [{ItemList(shown)}]");
                }),
            };

            var report = new System.Text.StringBuilder();
            foreach (var outcome in results)
            {
                report.AppendLine(
                    $"{(outcome.Passed ? "PASS" : "FAIL")}  {outcome.Name}" +
                    $"{(outcome.Detail.Length == 0 ? string.Empty : $"  [{outcome.Detail}]")}");
            }

            int failed = results.Count(outcome => !outcome.Passed);
            report.AppendLine(failed == 0
                ? $"All {results.Count} undo checks passed."
                : $"{failed} of {results.Count} undo checks FAILED.");

            Console.Write(report);
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                File.WriteAllText(reportPath, report.ToString());
            }

            return failed == 0 ? 0 : 1;
        }
        finally
        {
            XPlatUndoClock.Current = TimeProvider.System;
            XPlatClipboard.SetText(null);
        }
    }

    /// <summary>
    /// Builds one text box in a real (headless) window, focuses it, and runs <paramref name="body"/>
    /// once the window is up - which is when a text box has a host, a size and can take the
    /// keyboard.
    /// </summary>
    private static Outcome RunScenario(string name, FakeClock clock, Func<XPlatTextBox, XPlatUiHost, (bool Passed, string Detail)> body)
    {
        clock.Reset();

        var box = new XPlatTextBox { Dock = XDock.Fill };
        var backend = new HeadlessApplicationBackend();
        using var application = new XPlatApplication(backend);
        var window = new XPlatWindow("undo checks", box) { ClientSize = new SKSize(560f, 320f) };

        var outcome = new Outcome(name, false, "the window never came up");
        window.AddShownHandler(_ =>
        {
            box.Focus();
            var (passed, detail) = body(box, window.Host);
            outcome = new Outcome(name, passed, detail);
        });

        window.Show();
        application.Run();
        return outcome;
    }

    /// <summary>Types <paramref name="text"/> one character at a time, moving the clock on between them.</summary>
    private static void Type(XPlatUiHost host, FakeClock clock, string text, long gap = 120)
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

        // The block is what the control hands to its own opening handlers, so a handler is the way to
        // see exactly what a consumer gets. The menu is cleared afterwards: this checks the rows, not
        // the presenter, and a popup left open would own the keyboard for the rest of the check.
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

    /// <summary>The page's own miniature checker, so the harness exercises the same re-marking an application does.</summary>
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

    /// <summary>A clock that only moves when the harness says so.</summary>
    private sealed class FakeClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => 1_000;

        public override long GetTimestamp() => _ticks;

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(_ticks);

        public void Advance(long milliseconds) => _ticks += milliseconds;

        public void Reset() => _ticks = 0;
    }

    private readonly record struct Outcome(string Name, bool Passed, string Detail);
}
