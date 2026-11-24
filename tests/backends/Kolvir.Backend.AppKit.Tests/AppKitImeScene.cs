using SkiaSharp;
using Kolvir;
using Kolvir.Backend.Headless;

namespace Kolvir.Backend.AppKit.Tests;

/// <summary>
/// A real application, window and control tree on the headless backend, so the composition state
/// can be driven through a real <see cref="UiHost"/> without an NSView.
/// </summary>
internal sealed class AppKitImeScene : IDisposable
{
    private readonly Application _application;

    public AppKitImeScene(Control? root = null, float width = 320f, float height = 80f)
    {
        _application = new Application(new HeadlessApplicationBackend());
        Window = new Window("ime", root) { ClientSize = new SKSize(width, height) };
        Window.Host.PerformLayout();
    }

    public Window Window { get; }

    public UiHost Host => Window.Host;

    public void Dispose()
    {
        Window.Close();
        _application.Dispose();
    }
}

/// <summary>
/// A text box that records what the composition hooks were told, the way an application control
/// would, plus the two switches the refusal tests need.
/// </summary>
internal class RecordingBox : TextBox
{
    public bool Accept { get; init; } = true;

    public int Started { get; private set; }

    public int Updated { get; private set; }

    public int Cancelled { get; private set; }

    public int Inputs { get; private set; }

    /// <summary>Every text input the control was offered, handled or not.</summary>
    public int Received { get; private set; }

    public string Preedit { get; private set; } = string.Empty;

    public int PreeditCaret { get; private set; }

    protected override bool OnCompositionStarted()
    {
        var accepted = Accept && base.OnCompositionStarted();

        if (accepted)
        {
            Started++;
        }

        return accepted;
    }

    protected override bool OnCompositionUpdated(string text, int caretIndex)
    {
        var accepted = Accept && base.OnCompositionUpdated(text, caretIndex);

        if (accepted)
        {
            Updated++;
            Preedit = text;
            PreeditCaret = caretIndex;
        }

        return accepted;
    }

    protected override void OnCompositionCancelled()
    {
        Cancelled++;
        Preedit = string.Empty;
        base.OnCompositionCancelled();
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        Received++;
        base.OnTextInput(e);

        if (e.Handled)
        {
            Inputs++;
        }
    }
}
