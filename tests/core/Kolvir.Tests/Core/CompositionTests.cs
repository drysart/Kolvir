using SkiaSharp;
using Kolvir;
using Kolvir.AutoComplete;
using Kolvir.Undo;

namespace Kolvir.Tests;

/// <summary>
/// Text composition: the host methods a platform input method drives, the preedit a text box holds
/// outside its value, and the caret seam a candidate window hangs from.
/// </summary>
/// <remarks>
/// The backends cannot be driven from a test, so this is where the composition contract is pinned:
/// the four host methods are called directly, the way a GTK or Win32 adapter would call them.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class CompositionTests : HeadlessTestBase
{
    [TestMethod]
    public void TheHostMethodsRouteToTheFocusedControlAndToNobodyWithoutOne()
    {
        var box = new RecordingBox();
        using var scene = HeadlessScene.Create(box, 320f, 80f);

        Assert.IsFalse(scene.Host.BeginComposition(), "nothing is focused, so nothing takes a composition");
        Assert.IsFalse(scene.Host.UpdateComposition("a", 1));
        scene.Host.CommitComposition("a");
        scene.Host.CancelComposition();

        Assert.AreEqual(0, box.Started, "no control saw the start");
        Assert.AreEqual(0, box.Updated, "no control saw the update");
        Assert.AreEqual(0, box.Inputs, "no control received the commit");
        Assert.AreEqual(string.Empty, box.Text);

        box.Focus();
        Assert.IsTrue(scene.Host.BeginComposition(), "the focused box takes composition");
        Assert.IsTrue(scene.Host.UpdateComposition("ab", 1));
        scene.Host.CommitComposition("X");
        scene.Host.CancelComposition();

        Assert.AreEqual(1, box.Started);
        Assert.AreEqual(1, box.Updated);
        Assert.AreEqual(1, box.Inputs, "the commit arrived through the ordinary text path");
        Assert.AreEqual("X", box.Text);
    }

    [TestMethod]
    public void AControlThatRefusesCompositionStillReceivesTheCommitAsText()
    {
        var box = new RecordingBox { Accept = false };
        using var scene = HeadlessScene.Create(box, 320f, 80f);
        box.Focus();

        Assert.IsFalse(scene.Host.BeginComposition(), "the box declined");
        Assert.IsFalse(scene.Host.UpdateComposition("a", 1));
        Assert.AreEqual(0, box.PreeditText.Length, "a declined composition holds nothing");

        scene.Host.CommitComposition("e");

        Assert.AreEqual("e", box.Text, "a commit is text whether or not the field composes");
        Assert.AreEqual(1, box.CaretIndex);
    }

    [TestMethod]
    public void APreeditNeverReachesTheValueOrTheSelection()
    {
        var box = new TextBox("hello") { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(box, 320f, 60f);
        box.Focus();
        box.CaretIndex = 5;

        Assert.IsTrue(scene.Host.BeginComposition());
        Assert.IsTrue(scene.Host.UpdateComposition("nihongo", 4));

        Assert.AreEqual("hello", box.Text, "the preedit is not part of the value");
        Assert.AreEqual(5, box.CaretIndex, "the caret does not move for a preedit");
        Assert.IsFalse(box.HasSelection);
        Assert.AreEqual("nihongo", box.PreeditText);
        Assert.AreEqual(4, box.PreeditCaretIndex);
    }

    [TestMethod]
    public void ACommitIsExactlyAsIfTheTextHadBeenTyped()
    {
        var typed = new TextBox { UndoEnabled = true, Dock = DockStyle.Top, Height = 40f };
        var committed = new TextBox { UndoEnabled = true, Dock = DockStyle.Top, Height = 40f };
        int typedChanges = 0;
        int committedChanges = 0;
        typed.AddTextChangedHandler(_ => typedChanges++);
        committed.AddTextChangedHandler(_ => committedChanges++);

        var root = new Panel { Dock = DockStyle.Fill, Padding = new Thickness(0f) };
        root.Controls.Add(typed);
        root.Controls.Add(committed);

        using var scene = HeadlessScene.Create(root, 320f, 120f);

        typed.Focus();
        scene.Type("abc");

        committed.Focus();
        Assert.IsTrue(scene.Host.BeginComposition());
        Assert.IsTrue(scene.Host.UpdateComposition("zz", 2));
        scene.Host.CommitComposition("abc");

        Assert.AreEqual(typed.Text, committed.Text, "the same text");
        Assert.AreEqual(typed.CaretIndex, committed.CaretIndex, "the same caret");
        Assert.AreEqual(typedChanges, committedChanges, "the same number of text-changed events");

        typed.Focus();
        scene.TypeKey(Key.Z, Modifiers.Control);
        committed.Focus();
        scene.TypeKey(Key.Z, Modifiers.Control);

        Assert.AreEqual(string.Empty, typed.Text, "typing is one undo entry");
        Assert.AreEqual(string.Empty, committed.Text, "a commit is one undo entry too");
    }

    [TestMethod]
    public void MaxLengthAppliesToTheCommit()
    {
        var box = new TextBox { MaxLength = 3, Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(box, 320f, 60f);
        box.Focus();

        scene.Host.BeginComposition();
        scene.Host.UpdateComposition("abcdef", 6);
        scene.Host.CommitComposition("abcdef");

        Assert.AreEqual("abc", box.Text, "the limit clamps the committed text exactly as it clamps typing");
    }

    [TestMethod]
    public void CancelRestoresExactlyAndLeavesNoUndoEntry()
    {
        var box = new TextBox("hello") { UndoEnabled = true, Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(box, 320f, 60f);
        box.Focus();
        box.Select(1, 2);
        int changes = 0;
        box.AddTextChangedHandler(_ => changes++);

        scene.Host.BeginComposition();
        scene.Host.UpdateComposition("xyz", 2);
        scene.Host.CancelComposition();

        Assert.AreEqual("hello", box.Text, "the value is untouched");
        Assert.AreEqual(1, box.SelectionStart, "the selection survives");
        Assert.AreEqual(2, box.SelectionLength);
        Assert.AreEqual(3, box.CaretIndex);
        Assert.AreEqual(0, changes, "a cancel raises no text-changed event");
        Assert.IsFalse(box.IsComposing);

        scene.TypeKey(Key.Z, Modifiers.Control);
        Assert.AreEqual("hello", box.Text, "there was nothing to undo, because a preedit is not an edit");
    }

    [TestMethod]
    public void AnUpdateDoesNotDisturbTheSelectionOrTheCaret()
    {
        var box = new TextBox("hello") { Dock = DockStyle.Fill };
        using var scene = HeadlessScene.Create(box, 320f, 60f);
        box.Focus();
        box.Select(1, 2);

        int start = box.SelectionStart;
        int length = box.SelectionLength;
        int caret = box.CaretIndex;

        scene.Host.BeginComposition();
        scene.Host.UpdateComposition("n", 1);
        scene.Host.UpdateComposition("ni", 2);

        Assert.AreEqual(start, box.SelectionStart);
        Assert.AreEqual(length, box.SelectionLength);
        Assert.AreEqual(caret, box.CaretIndex);
    }

    [TestMethod]
    public void AutocompleteIsDrivenByCommitsAndNeverByUpdates()
    {
        var box = new TextBox
        {
            Dock = DockStyle.Fill,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.CustomSource,
        };

        box.AutoCompleteCustomSource.Add("Amsterdam");

        using var scene = HeadlessScene.Create(box, 320f, 60f);
        box.Focus();

        scene.Host.BeginComposition();
        scene.Host.UpdateComposition("Ams", 3);

        Assert.AreEqual(string.Empty, box.Text, "an update is not typing, so the source is not consulted");
        Assert.AreEqual(string.Empty, box.SelectedText);

        scene.Host.CommitComposition("Ams");

        Assert.AreEqual("Amsterdam", box.Text, "the commit drives the suggestion, as typing would");
        Assert.AreEqual("terdam", box.SelectedText);
    }

    [TestMethod]
    public void MovingTheFocusCancelsAComposition()
    {
        var first = new RecordingBox { Dock = DockStyle.Top, Height = 40f };
        var second = new RecordingBox { Dock = DockStyle.Top, Height = 40f };
        var root = new Panel { Dock = DockStyle.Fill, Padding = new Thickness(0f) };
        root.Controls.Add(first);
        root.Controls.Add(second);

        using var scene = HeadlessScene.Create(root, 320f, 120f);

        first.Focus();
        scene.Host.BeginComposition();
        scene.Host.UpdateComposition("ab", 1);

        second.Focus();

        Assert.AreEqual(1, first.Cancelled, "leaving the field ends its composition");
        Assert.IsFalse(first.IsComposing);
        Assert.AreEqual(string.Empty, first.PreeditText);
        Assert.AreEqual(string.Empty, first.Text, "and nothing was committed");
    }

    [TestMethod]
    public void TheCaretSeamAnswersTheCaretInHostCoordinates()
    {
        var box = new TextBox("hello")
        {
            Dock = DockStyle.Top,
            Height = 34f,
        };

        var root = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Thickness(12f, 10f, 12f, 10f),
        };

        root.Controls.Add(box);

        using var scene = HeadlessScene.Create(root, 360f, 120f);
        box.Focus();
        box.CaretIndex = 3;

        var origin = box.GetPositionInHost();
        var caret = box.CaretBounds;

        Assert.IsTrue(scene.Host.TryGetCaretBounds(out var bounds));

        Assert.AreEqual(origin.X + caret.Left, bounds.Left, 0.5f, "the caret's left edge");
        Assert.AreEqual(origin.Y + caret.Top, bounds.Top, 0.5f, "the caret's top edge");
        Assert.AreEqual(origin.X + caret.Right, bounds.Right, 0.5f, "the caret's right edge");
        Assert.AreEqual(origin.Y + caret.Bottom, bounds.Bottom, 0.5f, "the caret's bottom edge");
    }

    [TestMethod]
    public void TheCaretSeamAnswersAControlsBoundsWhenItHasNoCaret()
    {
        var button = new Button("Ok")
        {
            Dock = DockStyle.Top,
            Height = 30f,
        };

        var root = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Thickness(8f, 6f, 8f, 6f),
        };

        root.Controls.Add(button);

        using var scene = HeadlessScene.Create(root, 320f, 100f);
        button.Focus();

        Assert.AreSame(button, scene.Host.FocusedControl, "the button has to hold the focus");

        var origin = button.GetPositionInHost();
        Assert.IsTrue(scene.Host.TryGetCaretBounds(out var bounds));

        Assert.AreEqual(origin.X, bounds.Left, 0.5f);
        Assert.AreEqual(origin.Y, bounds.Top, 0.5f);
        Assert.AreEqual(button.Width, bounds.Width, 0.5f);
        Assert.AreEqual(button.Height, bounds.Height, 0.5f);
    }

    /// <summary>
    /// A text box that records what the composition hooks were asked, and can decline composition
    /// the way a non-text control does, without giving up the ordinary text path.
    /// </summary>
    private sealed class RecordingBox : TextBox
    {
        public bool Accept { get; init; } = true;

        public int Started { get; private set; }

        public int Updated { get; private set; }

        public int Cancelled { get; private set; }

        public int Inputs { get; private set; }

        protected override bool OnCompositionStarted()
        {
            Started++;
            return Accept && base.OnCompositionStarted();
        }

        protected override bool OnCompositionUpdated(string text, int caretIndex)
        {
            Updated++;
            return Accept && base.OnCompositionUpdated(text, caretIndex);
        }

        protected override void OnCompositionCancelled()
        {
            Cancelled++;
            base.OnCompositionCancelled();
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);

            if (e.Handled)
            {
                Inputs++;
            }
        }
    }
}
