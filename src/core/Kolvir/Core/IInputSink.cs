namespace Kolvir;

/// <summary>
/// Something that wants the window's keys before the focused control sees them: an open menu, a
/// keyboard capture, a modal gesture.
/// </summary>
/// <remarks>
/// <para>
/// A sink is pushed onto the window with <see cref="UiHost.AddInputSink"/> for as long as it
/// needs the keyboard and removed when it is done. It is asked before the menu bar's shortcuts,
/// because a menu that is open owns the keyboard: typing <c>D</c> while the View menu is down must
/// move to the Dark Mode item, not run the Dark Mode accelerator.
/// </para>
/// <para>
/// This is how a menu gets its keys without taking the focus. A popup window is deliberately
/// non-activating on every backend - that is what lets the user carry on typing into a combo box
/// while its list is open - so the popup cannot be given the keyboard; the window that already has
/// it forwards the keys here instead.
/// </para>
/// <para>
/// Both halves of a keystroke are offered, in the order they happen: the press to
/// <see cref="ProcessKeyDown"/>, then the release to <see cref="ProcessKeyUp"/>. A control that
/// acts on the release - a <see cref="Button"/> raises its click on key-up, so that the press has
/// somewhere to show before the click lands - only works inside a sink that carries both. A sink
/// that consumes a press is not asked again for its release when it has gone by then, which is what
/// keeps a popup that Escape closed on the press from acting twice on the same keystroke.
/// </para>
/// </remarks>
public interface IInputSink
{
    /// <summary>
    /// Handles a key press. Returns true when the sink consumed it, which stops the key reaching the
    /// menu bar's shortcuts or the focused control.
    /// </summary>
    bool ProcessKeyDown(Key key, Modifiers modifiers);

    /// <summary>
    /// Handles a key release. Returns true when the sink consumed it, which stops the key reaching
    /// the focused control - the release of a key the sink took the press of belongs to the sink,
    /// not to whatever control is behind it.
    /// </summary>
    /// <remarks>
    /// It defaults to false so that an existing implementation keeps working unchanged: a sink that
    /// only wants presses - a menu, an overlay that blocks input - is not obliged to grow a member.
    /// The window still keeps the release of a key whose press a sink took, when that sink has gone
    /// by the time the key comes up, so a sink that never sees a release cannot leave half a
    /// keystroke behind.
    /// </remarks>
    bool ProcessKeyUp(Key key, Modifiers modifiers) => false;

    /// <summary>
    /// Handles typed text. Returns true when the sink consumed it, which stops it reaching the
    /// focused control - a menu that answers to mnemonics swallows the letters it is given.
    /// </summary>
    bool ProcessTextInput(string text);
}
