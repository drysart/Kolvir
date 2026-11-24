using Kolvir;
using Kolvir.Undo;

namespace Kolvir.Backend.AppKit.Tests;

/// <summary>
/// The macOS half of the composition contract: marked text opens a composition once, updates it
/// while it is uncommitted, and ends it as a commit or a cancel.
/// </summary>
/// <remarks>
/// <para>
/// These tests drive <see cref="AppKitComposition"/> directly, because an NSView cannot be created
/// off the process's main thread and MSTest runs test methods on a pool thread. What is real here
/// is the host, the control tree and the whole core contract behind them; what is substituted is
/// exactly one thing - the callback that would be <c>NSTextInputContext.DiscardMarkedText</c> on
/// the view - so a refusal can be observed.
/// </para>
/// <para>
/// The view-level half (which protocol method reaches which of these calls) is
/// <c>tests/backends/Kolvir.Backend.AppKit.Harness</c>.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Platform.AppKit")]
public sealed class AppKitCompositionTests
{
    [TestMethod]
    public void MarkedTextOpensTheCompositionOnceAndUpdatesItInPlace()
    {
        var box = new RecordingBox { Dock = DockStyle.Fill };
        using var scene = new AppKitImeScene(box);
        box.Focus();

        var discards = 0;
        var composition = new AppKitComposition(scene.Host, () => discards++);

        composition.SetMarkedText("n", 1);
        composition.SetMarkedText("ni", 2);

        Assert.AreEqual(1, box.Started, "the composition begins once, not once per update");
        Assert.AreEqual(2, box.Updated);
        Assert.AreEqual("ni", composition.MarkedText);
        Assert.AreEqual(2, composition.MarkedLength);
        Assert.AreEqual("ni", box.Preedit, "the control holds the preedit");
        Assert.AreEqual(2, box.PreeditCaret);
        Assert.AreEqual(string.Empty, box.Text, "a preedit is never part of the value");
        Assert.AreEqual(0, box.Received, "an update is not an insert");
        Assert.AreEqual(0, discards, "an accepted composition is never discarded");
    }

    [TestMethod]
    public void ThePreeditCaretIsTheSelectedRangeLocation()
    {
        var box = new RecordingBox { Dock = DockStyle.Fill };
        using var scene = new AppKitImeScene(box);
        box.Focus();

        var composition = new AppKitComposition(scene.Host, () => { });

        composition.SetMarkedText("nihon", 3);
        Assert.AreEqual(3, composition.SelectedLocation);
        Assert.AreEqual(3, box.PreeditCaret);

        composition.SetMarkedText("nihon", 20);
        Assert.AreEqual(5, composition.SelectedLocation, "a location past the mark is clamped into it");

        composition.SetMarkedText("nihon", -4);
        Assert.AreEqual(0, composition.SelectedLocation, "a negative location clamps to the start");
    }

    [TestMethod]
    public void ASelectedRangeInsideTheMarkMovesThePreeditCaret()
    {
        var box = new RecordingBox { Dock = DockStyle.Fill };
        using var scene = new AppKitImeScene(box);
        box.Focus();

        var composition = new AppKitComposition(scene.Host, () => { });

        // A UTF-16 offset, which is what NSRange carries and what a C# string index is.
        composition.SetMarkedText("\u65e5\u672c\u8a9e", 2);

        Assert.AreEqual(2, composition.SelectedLocation);
        Assert.AreEqual(2, box.PreeditCaret);
        Assert.AreEqual(3, composition.MarkedLength);
    }

    [TestMethod]
    public void InsertTextCommitsExactlyOnceThroughTheOrdinaryPath()
    {
        var box = new RecordingBox { Dock = DockStyle.Fill };
        using var scene = new AppKitImeScene(box);
        box.Focus();

        var composition = new AppKitComposition(scene.Host, () => { });
        composition.SetMarkedText("nihon", 5);

        composition.InsertText("\u65e5\u672c");

        Assert.AreEqual("\u65e5\u672c", box.Text, "the commit is inserted once, not once per marked step");
        Assert.AreEqual(2, box.CaretIndex);
        Assert.AreEqual(1, box.Received, "one commit is one ordinary text input");
        Assert.IsFalse(composition.HasMarkedText);
        Assert.AreEqual(string.Empty, box.Preedit, "the preedit is gone after the commit");
        Assert.AreEqual(1, box.Cancelled, "the host clears the preedit as part of committing");
    }

    [TestMethod]
    public void TheCommitClampsToMaxLengthAndIsUndoneLikeTyping()
    {
        var box = new RecordingBox { Dock = DockStyle.Fill, MaxLength = 2, UndoEnabled = true };
        using var scene = new AppKitImeScene(box);
        box.Focus();

        var composition = new AppKitComposition(scene.Host, () => { });
        composition.SetMarkedText("nihongo", 7);
        composition.InsertText("nihongo");

        Assert.AreEqual("ni", box.Text, "MaxLength applies to the commit, not to the preedit");
        Assert.IsTrue(box.CanUndo, "a commit is an undoable insert, like typing");

        box.Undo();

        Assert.AreEqual(string.Empty, box.Text);
    }

    [TestMethod]
    public void UnmarkTextLeavesNoTraceAndAddsNoUndoEntry()
    {
        var box = new RecordingBox { Dock = DockStyle.Fill, Text = "kept", UndoEnabled = true };
        using var scene = new AppKitImeScene(box);
        box.Focus();
        box.CaretIndex = 4;

        var discards = 0;
        var composition = new AppKitComposition(scene.Host, () => discards++);
        composition.SetMarkedText("ni", 2);

        composition.UnmarkText();

        Assert.AreEqual("kept", box.Text);
        Assert.AreEqual(4, box.CaretIndex);
        Assert.AreEqual(string.Empty, box.Preedit);
        Assert.IsFalse(box.CanUndo, "a cancel adds no history entry");
        Assert.IsFalse(composition.HasMarkedText);
        Assert.AreEqual(0, discards, "a cancel the input method started is not a refusal");
        Assert.AreEqual(1, box.Cancelled);

        composition.UnmarkText();
        Assert.AreEqual(1, box.Cancelled, "a second cancel changes nothing");
    }

    [TestMethod]
    public void AFieldThatRefusesTheCompositionDiscardsTheMarkedTextOnceAndStillTakesTheCommit()
    {
        var box = new RecordingBox { Dock = DockStyle.Fill, ReadOnly = true };
        using var scene = new AppKitImeScene(box);
        box.Focus();

        var discards = 0;
        var composition = new AppKitComposition(scene.Host, () => discards++);

        composition.SetMarkedText("ni", 2);
        composition.SetMarkedText("nihon", 5);

        Assert.AreEqual(0, box.Started, "a read-only field never opens a composition");
        Assert.AreEqual(0, box.Updated);
        Assert.AreEqual(string.Empty, composition.MarkedText, "nothing is held for a field that refused");
        Assert.IsFalse(composition.HasMarkedText);
        Assert.AreEqual(
            2,
            discards,
            "one answer per offered mark: a refused composition is answered, never amplified");

        composition.InsertText("e");

        Assert.AreEqual(string.Empty, box.Text, "the read-only field refuses the characters too");
        Assert.AreEqual(1, box.Received, "the commit still arrived through the ordinary text path");
    }

    [TestMethod]
    public void AFieldThatStopsComposingMidwayIsTakenDownCleanly()
    {
        var box = new RecordingBox { Dock = DockStyle.Fill };
        using var scene = new AppKitImeScene(box);
        box.Focus();

        var discards = 0;
        var composition = new AppKitComposition(scene.Host, () => discards++);
        composition.SetMarkedText("ni", 2);

        box.ReadOnly = true;
        composition.SetMarkedText("nihon", 5);

        Assert.IsFalse(composition.IsComposing);
        Assert.AreEqual(string.Empty, composition.MarkedText);
        Assert.AreEqual(string.Empty, box.Preedit, "the preedit does not survive a field that stopped taking it");
        Assert.AreEqual(1, discards);
    }

    [TestMethod]
    public void AKeyTheLibraryConsumedEndsTheCompositionAndResetsTheInputMethod()
    {
        var box = new RecordingBox { Dock = DockStyle.Fill, Text = "hello" };
        using var scene = new AppKitImeScene(box);
        box.Focus();
        box.CaretIndex = 5;

        var discards = 0;
        var composition = new AppKitComposition(scene.Host, () => discards++);
        composition.SetMarkedText("ni", 2);

        composition.Reset();

        Assert.IsFalse(composition.IsComposing);
        Assert.AreEqual(string.Empty, box.Preedit);
        Assert.AreEqual(1, box.Cancelled);
        Assert.AreEqual(1, discards, "the input manager is reset, not left holding a composition");
        Assert.AreEqual("hello", box.Text, "resetting inserts nothing");
    }

    [TestMethod]
    public void ResetDoesNothingWhenThereIsNothingToReset()
    {
        var box = new RecordingBox { Dock = DockStyle.Fill };
        using var scene = new AppKitImeScene(box);
        box.Focus();

        var discards = 0;
        var composition = new AppKitComposition(scene.Host, () => discards++);

        composition.Reset();
        composition.Reset();

        Assert.AreEqual(0, discards, "a key with no composition in flight costs the input method nothing");
        Assert.AreEqual(0, box.Cancelled);
    }

    [TestMethod]
    public void ADiscardThatReOffersAMarkDoesNotPingPong()
    {
        var box = new RecordingBox { Dock = DockStyle.Fill, ReadOnly = true };
        using var scene = new AppKitImeScene(box);
        box.Focus();

        AppKitComposition? composition = null;
        var discards = 0;

        // AppKit answers DiscardMarkedText by calling unmarkText: on the client, which must not
        // start the exchange again.
        composition = new AppKitComposition(scene.Host, () =>
        {
            discards++;
            composition!.UnmarkText();
            composition.SetMarkedText("again", 5);
        });

        composition.SetMarkedText("ni", 2);

        Assert.AreEqual(1, discards, "one refusal is one discard, however the input method answers");
        Assert.IsFalse(composition.HasMarkedText);
    }
}
