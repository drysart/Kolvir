using AppKit;
using CoreGraphics;
using Foundation;
using Kolvir;
using Kolvir.Backend.AppKit;
using Kolvir.Backend.Headless;
using ObjCRuntime;
using SkiaSharp;

namespace Kolvir.Backend.AppKit.Harness;

/// <summary>
/// The view-level half of the macOS text-composition suite: the checks that need a real
/// <see cref="AppKitSkiaView"/>, a real <see cref="NSEvent"/> and, for the caret answer, a real
/// <see cref="NSWindow"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is a console program because AppKit refuses to build any of those off the process's main
/// thread, and a test framework runs test methods on a pool thread. Everything here is asserted
/// against the shipped types; only the step that would hand a key to AppKit's input manager is
/// scripted, through <see cref="AppKitSkiaView.TextInterpreter"/>, because a view with no key
/// window has no input context for <c>interpretKeyEvents:</c> to drive. That step is scripted the
/// way the Win32 suite scripts the Imm32 read, and for the same reason.
/// </para>
/// <para>
/// What it proves, in order: the raw-text fallback is gone, so a dead key inserts nothing; a commit
/// reported by the input manager inserts exactly once; the key path refuses a re-entry instead of
/// recursing; a declined command cannot ping-pong either; the protocol methods reach the marked
/// text and the commit; and the caret answer matches <c>ConvertRectToScreen</c> in screen points on
/// the display this runs on.
/// </para>
/// </remarks>
internal static class Program
{
    private static readonly List<string> Failures = [];

    private static int Main()
    {
        try
        {
            NSApplication.Init();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"note: NSApplication.Init failed ({ex.GetType().Name}: {ex.Message})");
        }

        Check("the default interpreter is AppKit's own interpretKeyEvents", TheDefaultInterpreterIsThePlatformOne);
        Check("a dead key with a silent input manager inserts nothing", ADeadKeyInsertsNothing);
        Check("a commit the input manager reports inserts exactly once", ACommitInsertsOnce);
        Check("a key that re-enters the key path is refused", AKeyPressedAgainIsRefused);
        Check("a command that re-enters the key path is refused", ACommandTheHostReissuesIsRefused);
        Check("the protocol methods reach the marked text and the commit", TheProtocolReachesTheComposition);
        Check("a refused composition is discarded and holds nothing", ARefusedCompositionHoldsNothing);
        Check("the caret answer matches ConvertRectToScreen", TheCaretAnswerMatchesThePlatform);

        Console.WriteLine();

        if (Failures.Count == 0)
        {
            Console.WriteLine("all checks passed");
            return 0;
        }

        Console.WriteLine($"{Failures.Count} check(s) failed:");
        foreach (var failure in Failures)
        {
            Console.WriteLine("  " + failure);
        }

        return 1;
    }

    private static void TheDefaultInterpreterIsThePlatformOne()
    {
        using var scene = new Scene();

        if (!ReferenceEquals(AppKitTextInterpreter.Instance, scene.View.TextInterpreter))
        {
            throw new InvalidOperationException(
                "a real view interprets keys with AppKit's own input manager until a test says otherwise");
        }
    }

    private static void ADeadKeyInsertsNothing()
    {
        using var scene = new Scene();
        scene.View.TextInterpreter = new ScriptedInterpreter();

        // Option+E on a US layout: the key is E and the characters are the acute accent, which is
        // exactly the shape that used to insert the accent through AppKitKeyMap.ToText.
        scene.View.KeyDown(KeyEvent(0x0E, "\u00b4", NSEventModifierMask.AlternateKeyMask));

        if (scene.Box.Text.Length != 0)
        {
            throw new InvalidOperationException(
                $"a dead key inserted \"{scene.Box.Text}\" instead of leaving the field alone");
        }

        if (scene.Box.Received != 0)
        {
            throw new InvalidOperationException($"{scene.Box.Received} text input(s) reached the field");
        }
    }

    private static void ACommitInsertsOnce()
    {
        using var scene = new Scene();

        scene.View.TextInterpreter = new ScriptedInterpreter(responder =>
            ((INSTextInputClient)responder).InsertText(new NSString("\u00e9"), new NSRange(0, 0)));

        scene.View.KeyDown(KeyEvent(0x0E, "\u00b4", NSEventModifierMask.AlternateKeyMask));

        if (scene.Box.Text != "\u00e9")
        {
            throw new InvalidOperationException(
                $"expected one composed character, got \"{scene.Box.Text}\" ({scene.Box.Text.Length} code unit(s))");
        }

        if (scene.Box.Received != 1)
        {
            throw new InvalidOperationException($"{scene.Box.Received} text input(s) for one commit");
        }
    }

    private static void AKeyPressedAgainIsRefused()
    {
        using var scene = new Scene();
        var box = new ReenteringKeyBox();
        scene.Replace(box);

        scene.View.TextInterpreter = new ScriptedInterpreter();
        box.View = scene.View;
        box.PendingEvent = KeyEvent(0x00, "a", (NSEventModifierMask)0);

        scene.View.KeyDown(box.PendingEvent);

        if (scene.View.ReentrancyRefusals != 1)
        {
            throw new InvalidOperationException(
                $"expected one refusal, saw {scene.View.ReentrancyRefusals}");
        }

        if (box.Reentries != 1)
        {
            throw new InvalidOperationException($"the box re-entered {box.Reentries} time(s)");
        }
    }

    private static void ACommandTheHostReissuesIsRefused()
    {
        using var scene = new Scene();
        var box = new ReenteringCommandBox();
        scene.Replace(box);

        box.View = scene.View;

        // The input manager declines a key and hands back a command; the host callback re-issues
        // the same command. Without the guard the two call each other until the stack ends.
        scene.View.TextInterpreter = new ScriptedInterpreter(responder =>
        {
            box.Reissue = true;
            ((AppKitSkiaView)responder).DoCommandBySelector(new Selector("moveLeft:"));
        });

        scene.View.KeyDown(KeyEvent(0x00, "a", (NSEventModifierMask)0));

        if (scene.View.ReentrancyRefusals != 1)
        {
            throw new InvalidOperationException(
                $"expected one refusal, saw {scene.View.ReentrancyRefusals}");
        }

        if (box.Commands != 1)
        {
            throw new InvalidOperationException($"the box handled {box.Commands} command(s)");
        }
    }

    private static void TheProtocolReachesTheComposition()
    {
        using var scene = new Scene();

        scene.View.SetMarkedText(new NSString("\u306b"), new NSRange(1, 0), new NSRange(0, 0));

        if (scene.Box.Preedit != "\u306b" || scene.Box.PreeditCaret != 1)
        {
            throw new InvalidOperationException(
                $"the marked text did not reach the field: \"{scene.Box.Preedit}\" at {scene.Box.PreeditCaret}");
        }

        if (scene.Box.Text.Length != 0)
        {
            throw new InvalidOperationException("marked text reached the value");
        }

        if (!scene.View.HasMarkedText || scene.View.MarkedRange.Length != 1 || scene.View.SelectedRange.Location != 1)
        {
            throw new InvalidOperationException(
                $"the client answers hasMarkedText={scene.View.HasMarkedText} " +
                $"markedRange={scene.View.MarkedRange.Length} selectedRange={scene.View.SelectedRange.Location}");
        }

        scene.View.SetMarkedText(new NSString("\u306b\u307b\u3093"), new NSRange(3, 0), new NSRange(0, 0));
        scene.View.InsertText(new NSString("\u65e5\u672c"), new NSRange(0, 0));

        if (scene.Box.Text != "\u65e5\u672c" || scene.Box.Text.Length != 2)
        {
            throw new InvalidOperationException($"the commit inserted \"{scene.Box.Text}\"");
        }

        if (scene.View.HasMarkedText)
        {
            throw new InvalidOperationException("the composition survived its own commit");
        }

        scene.View.SetMarkedText(new NSString("\u304b"), new NSRange(1, 0), new NSRange(0, 0));
        scene.View.UnmarkText();

        if (scene.Box.Preedit.Length != 0 || scene.Box.Text != "\u65e5\u672c")
        {
            throw new InvalidOperationException("a cancel did not leave the field exactly as it was");
        }
    }

    private static void ARefusedCompositionHoldsNothing()
    {
        using var scene = new Scene();
        var box = new ReadOnlyBox();
        scene.Replace(box);

        scene.View.SetMarkedText(new NSString("\u306b"), new NSRange(1, 0), new NSRange(0, 0));

        if (scene.View.HasMarkedText)
        {
            throw new InvalidOperationException("a read-only field kept the marked text");
        }

        if (box.Started != 0)
        {
            throw new InvalidOperationException("a read-only field opened a composition");
        }
    }

    private static void TheCaretAnswerMatchesThePlatform()
    {
        using var scene = new Scene();

        using var window = new NSWindow(
            new CGRect(100, 50, 420, 320),
            NSWindowStyle.Titled,
            NSBackingStore.Buffered,
            false);

        scene.View.Frame = new CGRect(0, 0, 400, 272);
        window.ContentView = scene.View;

        if (scene.View.Window != window)
        {
            throw new InvalidOperationException("the view is not in the window, so there is nothing to convert against");
        }

        if (!scene.Host.TryGetCaretBounds(out var caret))
        {
            throw new InvalidOperationException("the focused field has no caret to anchor to");
        }

        var answer = scene.View.GetFirstRect(new NSRange(0, 0), out _);
        var bottomInView = new CGPoint(0d, scene.View.Bounds.Height);
        var viewOrigin = window.ConvertPointToScreen(scene.View.ConvertPointToView(bottomInView, null!));

        // The platform's own conversion: the caret as a rectangle in the view's coordinates (which
        // are top-down, like the host's), taken through AppKit to window base coordinates and then
        // to screen coordinates. This is what would disagree if the answer were multiplied by the
        // backing scale or flipped twice.
        var viewRect = new CGRect(caret.Left, caret.Top, caret.Width, caret.Height);
        var expected = window.ConvertRectToScreen(scene.View.ConvertRectToView(viewRect, null!));

        Console.WriteLine(
            $"      caret L={caret.Left:0.###} T={caret.Top:0.###} R={caret.Right:0.###} B={caret.Bottom:0.###} " +
            $"w={caret.Width:0.###} h={caret.Height:0.###}");
        Console.WriteLine(
            $"      bounds h={(double)scene.View.Bounds.Height:0.###} flipped={scene.View.IsFlipped} " +
            $"bottom-left on screen={(double)viewOrigin.X:0.###},{(double)viewOrigin.Y:0.###}");
        Console.WriteLine(
            $"      answer L={(double)answer.Left:0.###} T={(double)answer.Top:0.###} " +
            $"R={(double)answer.Right:0.###} B={(double)answer.Bottom:0.###}");

        if (!SameRect(expected, answer))
        {
            throw new InvalidOperationException(
                $"answer {Format(answer)} but ConvertRectToScreen says {Format(expected)} " +
                $"(backing scale {(double)window.BackingScaleFactor}); bounds {Format(scene.View.Bounds)}, " +
                $"frame {Format(scene.View.Frame)}, view origin on screen {Format(new CGRect(viewOrigin.X, viewOrigin.Y, 0, 0))}, " +
                $"caret host {Format(new CGRect(caret.Left, caret.Top, caret.Right, caret.Bottom))}");
        }

        Console.WriteLine(
            $"      caret host {caret.Left:0.#},{caret.Top:0.#},{caret.Right:0.#},{caret.Bottom:0.#} " +
            $"-> screen {Format(answer)} at backing scale {(double)window.BackingScaleFactor}");
    }

    private static bool SameRect(CGRect left, CGRect right) =>
        Math.Abs((double)left.Left - (double)right.Left) < 0.01d &&
        Math.Abs((double)left.Top - (double)right.Top) < 0.01d &&
        Math.Abs((double)left.Right - (double)right.Right) < 0.01d &&
        Math.Abs((double)left.Bottom - (double)right.Bottom) < 0.01d;

    private static string Format(CGRect rect) =>
        $"({(double)rect.Left:0.#}, {(double)rect.Top:0.#}, {(double)rect.Right:0.#}, {(double)rect.Bottom:0.#})";

    private static NSEvent KeyEvent(ushort keyCode, string characters, NSEventModifierMask flags) =>
        NSEvent.KeyEvent(
            NSEventType.KeyDown,
            CGPoint.Empty,
            flags,
            0d,
            IntPtr.Zero,
            null!,
            characters,
            characters,
            false,
            keyCode)
        ?? throw new InvalidOperationException($"AppKit built no key event for 0x{keyCode:X}");

    private static void Check(string name, Action body)
    {
        try
        {
            body();
            Console.WriteLine($"pass  {name}");
        }
        catch (Exception ex)
        {
            Failures.Add($"{name}: {ex.Message}");
            Console.WriteLine($"FAIL  {name}: {ex.Message}");
        }
    }

    /// <summary>A real application, a real host and a real view, with a focused field.</summary>
    private sealed class Scene : IDisposable
    {
        private readonly Application _application;

        public Scene()
        {
            _application = new Application(new HeadlessApplicationBackend());
            Box = new RecordingBox { Dock = DockStyle.Fill };
            Window = new Window("harness", Box) { ClientSize = new SKSize(320f, 80f) };
            Window.Host.PerformLayout();
            View = new AppKitSkiaView(Window.Host);

            Box.Focus();

            if (!Box.IsFocused)
            {
                throw new InvalidOperationException("the field did not take the focus, so nothing has a caret");
            }
        }

        public Window Window { get; }

        public UiHost Host => Window.Host;

        public AppKitSkiaView View { get; private set; }

        public RecordingBox Box { get; private set; }

        /// <summary>Swaps the field for a specialized one, keeping the host and the view.</summary>
        public void Replace(RecordingBox box)
        {
            Window.Content = box;
            Window.Host.PerformLayout();
            box.Focus();
            Box = box;
        }

        public void Dispose()
        {
            View.Dispose();
            Window.Close();
            _application.Dispose();
        }
    }

    private class RecordingBox : TextBox
    {
        public int Started { get; private set; }

        public string Preedit { get; private set; } = string.Empty;

        public int PreeditCaret { get; private set; }

        public int Received { get; private set; }

        protected override bool OnCompositionStarted()
        {
            var accepted = base.OnCompositionStarted();

            if (accepted)
            {
                Started++;
            }

            return accepted;
        }

        protected override bool OnCompositionUpdated(string text, int caretIndex)
        {
            var accepted = base.OnCompositionUpdated(text, caretIndex);

            if (accepted)
            {
                Preedit = text;
                PreeditCaret = caretIndex;
            }

            return accepted;
        }

        protected override void OnCompositionCancelled()
        {
            Preedit = string.Empty;
            base.OnCompositionCancelled();
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            Received++;
            base.OnTextInput(e);
        }
    }

    /// <summary>A box that presses the key again from inside its own key handler.</summary>
    private sealed class ReenteringKeyBox : RecordingBox
    {
        public AppKitSkiaView? View { get; set; }

        public NSEvent? PendingEvent { get; set; }

        public int Reentries { get; private set; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Handled)
            {
                return;
            }

            Reentries++;
            View!.KeyDown(PendingEvent!);
        }
    }

    /// <summary>A box that re-issues the command the input manager declined.</summary>
    private sealed class ReenteringCommandBox : RecordingBox
    {
        public AppKitSkiaView? View { get; set; }

        public bool Reissue { get; set; }

        public int Commands { get; private set; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (!Reissue || Commands > 0)
            {
                return;
            }

            Commands++;
            View!.DoCommandBySelector(new Selector("moveLeft:"));
        }
    }

    private sealed class ReadOnlyBox : RecordingBox
    {
        public ReadOnlyBox() => ReadOnly = true;
    }

    /// <summary>What an input manager would report, without one.</summary>
    private sealed class ScriptedInterpreter : IAppKitTextInterpreter
    {
        private readonly Action<NSResponder>? _answer;

        public ScriptedInterpreter(Action<NSResponder>? answer = null) => _answer = answer;

        public void Interpret(NSEvent theEvent, NSResponder responder) => _answer?.Invoke(responder);
    }
}
