using Kolvir;
using Kolvir.Undo;
using SkiaSharp;

namespace Kolvir.Backend.Win32.Tests;

/// <summary>
/// The native half of text composition: the <c>WM_IME_*</c> messages, driven through a real window and
/// its real message loop, must reach the preedit and the ordinary text path the model defines.
/// </summary>
/// <remarks>
/// <para>
/// The composition string is published through <see cref="IWin32ImeComposition"/> rather than set on the
/// input context. That is measured, not convenience: on a machine with no input method installed,
/// <c>ImmGetProperty(IGP_SETCOMPSTR)</c> is zero and <c>ImmSetCompositionStringW</c> answers TRUE while
/// storing nothing, so every <c>ImmGetCompositionStringW</c> read comes back zero bytes even when both
/// the composition and the reading buffer are supplied. What is real here is the window, the message,
/// the window procedure, the host and the text box; only the read of an Imm32 string that nothing on
/// this machine can hold is substituted.
/// </para>
/// <para>
/// What that leaves untested is the input method's own UI and clause data, which needs a real IME.
/// <c>dev-docs/IME.md</c> records that as a limitation.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Platform.Win32")]
public sealed class Win32ImeCompositionTests
{
    /// <summary>A text box that records what the composition hooks were told, the way an application would.</summary>
    private sealed class RecordingBox : TextBox
    {
        public int Started { get; private set; }

        public int Updated { get; private set; }

        public int Cancelled { get; private set; }

        public string Preedit { get; private set; } = string.Empty;

        public int PreeditCaret { get; private set; }

        protected override bool OnCompositionStarted()
        {
            Started++;
            return true;
        }

        protected override bool OnCompositionUpdated(string text, int caretIndex)
        {
            Updated++;
            Preedit = text;
            PreeditCaret = caretIndex;
            return true;
        }

        protected override void OnCompositionCancelled()
        {
            Cancelled++;
            Preedit = string.Empty;
        }
    }

    /// <summary>The synthesized composition: what the input method would report.</summary>
    private sealed class FakeImeComposition : IWin32ImeComposition
    {
        public string Composition { get; set; } = string.Empty;

        public string Result { get; set; } = string.Empty;

        public int Caret { get; set; }

        public string CompositionString(IntPtr context) => Composition;

        public string ResultString(IntPtr context) => Result;

        public int CaretPosition(IntPtr context) => Caret;
    }

    private static IntPtr Send(IntPtr hwnd, uint message, int wParam = 0, uint lParam = 0) =>
        NativeMethods.SendMessageW(hwnd, message, new IntPtr(wParam), new IntPtr(lParam));

    [TestMethod]
    [Timeout(30000)]
    public void ASynthesizedCompositionShowsAPreeditAndCommitsExactlyOnce()
    {
        var application = new Application(new Win32ApplicationBackend());

        try
        {
            var box = new RecordingBox { Dock = DockStyle.Fill };
            var panel = new Panel { Dock = DockStyle.Fill };
            panel.Controls.Add(box);

            var window = new Window("ime", panel) { ClientSize = new SKSize(320f, 160f) };

            const string committed = "ni\u3093";

            window.AddShownHandler(_ =>
            {
                var backend = (Win32WindowBackend)window.Backend;

                Assert.AreSame(
                    Win32ImeComposition.Instance,
                    backend.ImeComposition,
                    "a real window reads the input method through Imm32 until a test says otherwise");

                var ime = new FakeImeComposition();
                backend.ImeComposition = ime;

                box.Focus();
                Assert.IsTrue(box.IsFocused, "the box holds the focus, so a composition has somewhere to go");

                var hwnd = backend.Handle;

                ime.Composition = "n";
                ime.Caret = 1;
                Send(hwnd, NativeMethods.WmImeStartComposition);
                Send(hwnd, NativeMethods.WmImeComposition, 0, NativeMethods.GcsCompStr);

                Assert.AreEqual(1, box.Started, "WM_IME_STARTCOMPOSITION asked the focused box for a composition");
                Assert.AreEqual("n", box.Preedit, "the composition string is the preedit");
                Assert.AreEqual(1, box.PreeditCaret, "with the input method's caret inside it");
                Assert.AreEqual(string.Empty, box.Text, "a preedit never reaches the value");

                ime.Composition = committed;
                ime.Caret = committed.Length;
                Send(hwnd, NativeMethods.WmImeComposition, 0, NativeMethods.GcsCompStr);

                Assert.AreEqual(2, box.Updated, "a growing preedit reaches the control on every update");
                Assert.AreEqual(committed, box.Preedit);
                Assert.AreEqual(string.Empty, box.Text, "and still never reaches the value");

                ime.Result = committed;
                Send(hwnd, NativeMethods.WmImeComposition, 0, NativeMethods.GcsResultStr);

                Assert.AreEqual(committed, box.Text, "the commit arrived through the ordinary text path");
                Assert.AreEqual(committed.Length, box.CaretIndex, "at the caret, exactly as typing would");
                Assert.AreEqual(string.Empty, box.Preedit, "the commit ended the composition");

                foreach (var character in committed)
                {
                    Send(hwnd, NativeMethods.WmChar, character);
                }

                Assert.AreEqual(
                    committed,
                    box.Text,
                    "the WM_CHAR copies of a commit do not insert the text a second time");

                Send(hwnd, NativeMethods.WmChar, 'x');

                Assert.AreEqual(
                    committed + "x",
                    box.Text,
                    "a WM_CHAR that is not the commit's own text is delivered as usual");

                application.Exit();
            });

            window.Show();
            application.Run();

            Assert.AreEqual(committed + "x", box.Text, "the whole run inserted the text once");
        }
        finally
        {
            application.Dispose();
        }
    }

    [TestMethod]
    [Timeout(30000)]
    public void AnEndedCompositionLeavesNothingBehind()
    {
        var application = new Application(new Win32ApplicationBackend());

        try
        {
            var box = new RecordingBox { Dock = DockStyle.Fill, UndoEnabled = true };
            var panel = new Panel { Dock = DockStyle.Fill };
            panel.Controls.Add(box);

            var window = new Window("ime cancel", panel) { ClientSize = new SKSize(320f, 160f) };

            window.AddShownHandler(_ =>
            {
                var backend = (Win32WindowBackend)window.Backend;
                var ime = new FakeImeComposition { Composition = "k", Caret = 1 };
                backend.ImeComposition = ime;

                box.Focus();
                var hwnd = backend.Handle;

                Send(hwnd, NativeMethods.WmImeStartComposition);
                Send(hwnd, NativeMethods.WmImeComposition, 0, NativeMethods.GcsCompStr);
                Assert.AreEqual("k", box.Preedit, "the preedit is being shown");

                Send(hwnd, NativeMethods.WmImeEndComposition);

                Assert.AreEqual(string.Empty, box.Text, "a composition ended without a commit inserts nothing");
                Assert.AreEqual(string.Empty, box.Preedit, "and leaves no preedit behind");
                Assert.IsFalse(box.CanUndo, "and adds no undo entry");

                // A second end is what a commit followed by its own end looks like: idempotent.
                Send(hwnd, NativeMethods.WmImeEndComposition);

                Assert.AreEqual(string.Empty, box.Text, "and the repeat changes nothing either");

                application.Exit();
            });

            window.Show();
            application.Run();

            Assert.AreEqual(string.Empty, box.Text);
        }
        finally
        {
            application.Dispose();
        }
    }

    [TestMethod]
    [Timeout(30000)]
    public void AnAstralCharacterArrivesAsOneWholeCharacter()
    {
        var application = new Application(new Win32ApplicationBackend());

        try
        {
            var box = new RecordingBox { Dock = DockStyle.Fill };
            var panel = new Panel { Dock = DockStyle.Fill };
            panel.Controls.Add(box);

            var window = new Window("ime astral", panel) { ClientSize = new SKSize(320f, 160f) };
            var expected = char.ConvertFromUtf32(0x1F600);
            var high = expected[0];
            var low = expected[1];

            window.AddShownHandler(_ =>
            {
                var backend = (Win32WindowBackend)window.Backend;
                box.Focus();
                var hwnd = backend.Handle;

                // Text past the BMP arrives as two WM_CHAR messages, one per UTF-16 code unit.
                Send(hwnd, NativeMethods.WmChar, high);

                Assert.AreEqual(string.Empty, box.Text, "half a surrogate pair is not text");

                Send(hwnd, NativeMethods.WmChar, low);

                Assert.AreEqual(expected, box.Text, "the two halves arrive as one character");
                Assert.AreEqual(2, box.Text.Length, "which is one character in two code units");

                application.Exit();
            });

            window.Show();
            application.Run();

            Assert.AreEqual(expected, box.Text);
        }
        finally
        {
            application.Dispose();
        }
    }

    [TestMethod]
    public void TheProductionReaderAnswersNothingWithoutAContext()
    {
        var reader = Win32ImeComposition.Instance;

        // A window with no input context is the first thing this path meets on a machine with no IME.
        Assert.AreEqual(string.Empty, reader.CompositionString(IntPtr.Zero));
        Assert.AreEqual(string.Empty, reader.ResultString(IntPtr.Zero));
        Assert.AreEqual(0, reader.CaretPosition(IntPtr.Zero));
    }
}
