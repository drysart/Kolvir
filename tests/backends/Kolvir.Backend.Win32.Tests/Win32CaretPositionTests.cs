using System.Runtime.InteropServices;
using Kolvir;
using SkiaSharp;

namespace Kolvir.Backend.Win32.Tests;

/// <summary>
/// Where Windows is told the insertion point is. The emoji panel and the touch keyboard ask before they
/// appear, so a library that draws its own caret has to answer both ways Windows asks: the thread's
/// caret, and <c>WM_IME_REQUEST</c> with <c>IMR_QUERYCHARPOSITION</c>.
/// </summary>
/// <remarks>
/// The diagnosis and the fix are measured the same way, with <c>GetGUIThreadInfo</c> against a real
/// focused text box in a real window. Before the caret existed it reported <c>hwndCaret == 0</c> and an
/// empty rectangle, which is exactly why the emoji panel anchored to the window instead of the caret.
/// </remarks>
[TestClass]
[TestCategory("Platform.Win32")]
public sealed class Win32CaretPositionTests
{
    private const int MKLButton = 0x0001;

    /// <summary>The other request a real IME sends, which this backend deliberately does not answer.</summary>
    private const uint ImrCandidateWindow = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public uint cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public NativeMethods.RECT rcCaret;
    }

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    /// <summary>The scale the backend works at, from the same place it reads it: the window's DPI.</summary>
    private static float ScaleOf(IntPtr hwnd) => NativeMethods.GetDpiForWindow(hwnd) / 96f;

    private static NativeMethods.POINT ToScreenPoint(IntPtr hwnd, SKRect caret)
    {
        var scale = ScaleOf(hwnd);
        var point = new NativeMethods.POINT
        {
            X = (int)MathF.Round(caret.Left * scale),
            Y = (int)MathF.Round(caret.Top * scale),
        };

        NativeMethods.ClientToScreen(hwnd, ref point);
        return point;
    }

    private static IntPtr PointParam(int x, int y) => new((y << 16) | (x & 0xFFFF));

    /// <summary>Asks the window what an input method asks it, and hands back what it filled in.</summary>
    private static (int Returned, NativeMethods.IMECHARPOSITION Answer) Ask(
        IntPtr hwnd,
        uint request,
        uint charPos = 0,
        uint? dwSize = null)
    {
        var size = Marshal.SizeOf<NativeMethods.IMECHARPOSITION>();
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(
                new NativeMethods.IMECHARPOSITION
                {
                    dwSize = dwSize ?? (uint)size,
                    dwCharPos = charPos,
                },
                buffer,
                false);

            var returned = (int)NativeMethods.SendMessageW(hwnd, NativeMethods.WmImeRequest, new IntPtr(request), buffer);

            return (returned, Marshal.PtrToStructure<NativeMethods.IMECHARPOSITION>(buffer));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Dispatches one message, which is what makes the caret sync run.</summary>
    private static void Settle(IntPtr hwnd) =>
        NativeMethods.SendMessageW(hwnd, NativeMethods.WmNull, IntPtr.Zero, IntPtr.Zero);

    private static (IntPtr CaretWindow, GUITHREADINFO Info) ThreadCaret()
    {
        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        var ok = GetGUIThreadInfo(0, ref info);

        Assert.IsTrue(ok, "GetGUIThreadInfo should answer for this thread");

        return (info.hwndCaret, info);
    }

    /// <summary>
    /// Gives the window the keyboard focus. A background process may not take the foreground on its
    /// own, and without it the window never sees <c>WM_SETFOCUS</c>, which is what creates the caret, so
    /// the test attaches to the foreground thread the way a shell does.
    /// </summary>
    private static void TakeFocus(IntPtr hwnd)
    {
        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out uint _);
        var ourThread = GetWindowThreadProcessId(hwnd, out uint _);

        if (foregroundThread != ourThread)
        {
            AttachThreadInput(ourThread, foregroundThread, true);
            SetForegroundWindow(hwnd);
            SetFocus(hwnd);
            AttachThreadInput(ourThread, foregroundThread, false);
        }

        Assert.AreEqual(hwnd, NativeMethods.GetFocus(), "the window holds the keyboard focus, which a caret requires");
    }

    /// <summary>A real window with a real text box in it, and the body run inside its shown handler.</summary>
    private static void Run(string title, Action<IntPtr, Window, TextBox> body)
    {
        var application = new Application(new Win32ApplicationBackend());

        try
        {
            var box = new TextBox { Dock = DockStyle.Fill, Text = "hello world" };
            var panel = new Panel { Dock = DockStyle.Fill };
            panel.Controls.Add(box);

            var window = new Window(title, panel) { ClientSize = new SKSize(320f, 160f) };

            window.AddShownHandler(_ =>
            {
                try
                {
                    var backend = (Win32WindowBackend)window.Backend;

                    box.Focus();
                    TakeFocus(backend.Handle);

                    body(backend.Handle, window, box);
                }
                finally
                {
                    application.Exit();
                }
            });

            window.Show();
            application.Run();
        }
        finally
        {
            application.Dispose();
        }
    }

    [TestMethod]
    [Timeout(30000)]
    public void TheRequestAnswersTheCaretInScreenCoordinates() => Run("ime request", (hwnd, window, box) =>
    {
        box.CaretIndex = 5;

        Assert.IsTrue(window.Host.TryGetCaretBounds(out var caret), "the seam answers for a focused text box");

        var (returned, answer) = Ask(hwnd, NativeMethods.ImrQueryCharPosition, charPos: 3);
        var size = Marshal.SizeOf<NativeMethods.IMECHARPOSITION>();

        Assert.AreEqual(size, returned, "the structure's size is how an app says it handled the request");
        Assert.AreEqual((uint)size, answer.dwSize, "and the structure is filled in as the contract says");
        Assert.AreEqual(3u, answer.dwCharPos, "the character the caller asked about is left as it was");

        var expected = ToScreenPoint(hwnd, caret);
        Assert.AreEqual(expected.X, answer.pt.X, "the caret is answered in screen coordinates");
        Assert.AreEqual(expected.Y, answer.pt.Y);

        Assert.IsTrue(NativeMethods.GetClientRect(hwnd, out var client));
        var origin = new NativeMethods.POINT();
        NativeMethods.ClientToScreen(hwnd, ref origin);

        Assert.AreEqual(origin.X, answer.rcDocument.Left, "the document rectangle is the client area on screen");
        Assert.AreEqual(origin.Y, answer.rcDocument.Top);
        Assert.AreEqual(origin.X + client.Right, answer.rcDocument.Right);
        Assert.AreEqual(origin.Y + client.Bottom, answer.rcDocument.Bottom);

        var lineHeight = (uint)Math.Max(1, MathF.Round(box.EffectiveFont.LineHeight * ScaleOf(hwnd)));
        Assert.AreEqual(lineHeight, answer.cLineHeight, "a line height comes from the field's own font");
    });

    [TestMethod]
    [Timeout(30000)]
    public void ClickingElsewhereInTheFieldMovesBothAnswers() => Run("ime click", (hwnd, window, box) =>
    {
        box.CaretIndex = 0;

        var before = Ask(hwnd, NativeMethods.ImrQueryCharPosition).Answer;

        Assert.IsTrue(NativeMethods.GetClientRect(hwnd, out var client));

        var x = client.Right * 3 / 4;
        var y = client.Bottom / 2;
        NativeMethods.SendMessageW(hwnd, NativeMethods.WmLButtonDown, new IntPtr(MKLButton), PointParam(x, y));
        NativeMethods.SendMessageW(hwnd, NativeMethods.WmLButtonUp, IntPtr.Zero, PointParam(x, y));

        Assert.AreEqual(box.Text.Length, box.CaretIndex, "the click put the caret at the end of the text");

        var after = Ask(hwnd, NativeMethods.ImrQueryCharPosition).Answer;

        Assert.IsTrue(
            after.pt.X > before.pt.X,
            "the answer follows the caret, not the window, so a panel anchored to it moves with a click");

        var (caretWindow, info) = ThreadCaret();

        Assert.AreEqual(hwnd, caretWindow, "the thread's caret is the one this window made");
        Assert.AreEqual(after.pt.X - after.rcDocument.Left, info.rcCaret.Left, "and it moved with the caret too");
    });

    [TestMethod]
    [Timeout(30000)]
    public void TheThreadCaretReportsTheSamePointAsTheRequest() => Run("thread caret", (hwnd, window, box) =>
    {
        box.CaretIndex = 5;

        // The caret is a push, so it reaches the thread on the next message.
        Settle(hwnd);

        var (caretWindow, info) = ThreadCaret();

        Assert.AreEqual(hwnd, caretWindow, "the focused window owns the thread's caret");
        Assert.AreEqual(hwnd, info.hwndFocus, "and it owns the keyboard focus while it does");

        var answer = Ask(hwnd, NativeMethods.ImrQueryCharPosition).Answer;

        // GetGUIThreadInfo answers in client coordinates and the request answers in screen coordinates,
        // so the two agree once the document origin is taken off.
        Assert.AreEqual(answer.pt.X - answer.rcDocument.Left, info.rcCaret.Left);
        Assert.AreEqual(answer.pt.Y - answer.rcDocument.Top, info.rcCaret.Top);
        Assert.AreEqual(info.rcCaret.Left + 1, info.rcCaret.Right, "the caret is one pixel, because it is never shown");
        Assert.AreEqual(info.rcCaret.Top + 1, info.rcCaret.Bottom);
    });

    [TestMethod]
    [Timeout(30000)]
    public void ARequestTheBackendDoesNotImplementIsNotSwallowed() => Run("ime other request", (hwnd, window, box) =>
    {
        box.CaretIndex = 5;

        var (returned, answer) = Ask(hwnd, ImrCandidateWindow);

        // The size is this backend's answer, so a different value can only be the default handler's.
        Assert.AreEqual(
            0,
            returned, "an unimplemented request reaches DefWindowProc instead of being answered here");
        Assert.AreEqual(0u, answer.dwCharPos, "and nothing this backend fills in is written to it");
    });

    [TestMethod]
    [Timeout(30000)]
    public void TheDiagnosticTraceWritesWhatItSeesAndStopsWhenUnset()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"kolvir-ime-trace-{Guid.NewGuid():N}.log");

        var previous = Environment.GetEnvironmentVariable(Win32ImeLog.PathVariable);

        try
        {
            Environment.SetEnvironmentVariable(Win32ImeLog.PathVariable, path);

            Run("ime trace", (hwnd, window, box) =>
            {
                box.CaretIndex = 5;
                Ask(hwnd, NativeMethods.ImrQueryCharPosition);
            });

            Assert.IsTrue(System.IO.File.Exists(path), "the trace goes to the file the variable names");

            var trace = System.IO.File.ReadAllText(path);

            Assert.IsTrue(trace.Contains("WM_IME_REQUEST", StringComparison.Ordinal), "the message is logged");
            Assert.IsTrue(
                trace.Contains("IMR_QUERYCHARPOSITION answered", StringComparison.Ordinal),
                "with what was answered, which is what tells 'never asked' from 'answered and ignored'");
            Assert.IsTrue(trace.Contains("focus=0x", StringComparison.Ordinal), "and the focus it was asked with");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Win32ImeLog.PathVariable, previous);

            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
    }

    [TestMethod]
    public void TheDiagnosticTraceAndItsExperimentsAreOffByDefault()
    {
        var previous = Environment.GetEnvironmentVariable(Win32ImeLog.PathVariable);
        var previousShow = Environment.GetEnvironmentVariable(Win32ImeLog.ShowCaretVariable);
        var previousRefresh = Environment.GetEnvironmentVariable(Win32ImeLog.RefreshCompositionFormVariable);

        try
        {
            Environment.SetEnvironmentVariable(Win32ImeLog.PathVariable, null);
            Environment.SetEnvironmentVariable(Win32ImeLog.ShowCaretVariable, null);
            Environment.SetEnvironmentVariable(Win32ImeLog.RefreshCompositionFormVariable, null);

            Assert.IsFalse(Win32ImeLog.Enabled, "nothing is written unless a log was asked for");
            Assert.IsFalse(Win32ImeLog.ShowCaret, "and neither experiment runs unless asked for");
            Assert.IsFalse(Win32ImeLog.RefreshCompositionForm);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Win32ImeLog.PathVariable, previous);
            Environment.SetEnvironmentVariable(Win32ImeLog.ShowCaretVariable, previousShow);
            Environment.SetEnvironmentVariable(Win32ImeLog.RefreshCompositionFormVariable, previousRefresh);
        }
    }

    [TestMethod]
    [Timeout(30000)]
    public void ARequestSizedSmallerThanTheStructureIsRefused() => Run(
        "ime small request",
        (hwnd, window, box) =>
        {
            box.CaretIndex = 5;

            // Gecko and Chromium both discard an answer whose caller expected a smaller structure, and
            // writing ours would run past what it allocated. Real callers send the full size, which is
            // what the trace shows, so this only guards the contract.
            var (returned, answer) = Ask(hwnd, NativeMethods.ImrQueryCharPosition, dwSize: 8);

            Assert.AreEqual(0, returned, "a structure too small to fill is refused rather than written");
            Assert.AreEqual(8u, answer.dwSize, "and the caller's own size is left exactly as it was");
            Assert.AreEqual(0, answer.pt.X, "with nothing written into it");
            Assert.AreEqual(0, answer.pt.Y);
        });
}
