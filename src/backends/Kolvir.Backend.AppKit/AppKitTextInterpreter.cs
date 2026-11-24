using AppKit;

namespace Kolvir.Backend.AppKit;

/// <summary>
/// Hands one key to AppKit's input manager, which is what turns a dead key, an Option composition
/// or an IME syllable into marked text or a commit.
/// </summary>
/// <remarks>
/// This is a seam for the same reason <c>IWin32ImeComposition</c> is one: a test cannot manufacture
/// a composition, and a view with no key window has no input context for
/// <c>InterpretKeyEvents</c> to drive. It is held to the same conditions - internal, defaulting to
/// the production implementation, and pinned by a test that fails if that default is replaced with
/// a fake.
/// </remarks>
internal interface IAppKitTextInterpreter
{
    /// <summary>Interprets <paramref name="theEvent"/>, which answers through <paramref name="responder"/>.</summary>
    /// <param name="theEvent">The key event, exactly as AppKit delivered it.</param>
    /// <param name="responder">The view, which is also the text input client the manager answers through.</param>
    void Interpret(NSEvent theEvent, NSResponder responder);
}

/// <summary>The production interpreter: AppKit's own <c>interpretKeyEvents:</c>.</summary>
internal sealed class AppKitTextInterpreter : IAppKitTextInterpreter
{
    private AppKitTextInterpreter()
    {
    }

    /// <summary>The one instance the view uses unless a test says otherwise.</summary>
    public static AppKitTextInterpreter Instance { get; } = new();

    /// <inheritdoc />
    public void Interpret(NSEvent theEvent, NSResponder responder)
    {
        ArgumentNullException.ThrowIfNull(theEvent);
        ArgumentNullException.ThrowIfNull(responder);

        responder.InterpretKeyEvents([theEvent]);
    }
}
