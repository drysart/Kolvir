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
/// </remarks>
public interface IInputSink
{
    /// <summary>
    /// Handles a key press. Returns true when the sink consumed it, which stops the key reaching the
    /// menu bar's shortcuts or the focused control.
    /// </summary>
    bool ProcessKeyDown(Key key, Modifiers modifiers);

    /// <summary>
    /// Handles typed text. Returns true when the sink consumed it, which stops it reaching the
    /// focused control - a menu that answers to mnemonics swallows the letters it is given.
    /// </summary>
    bool ProcessTextInput(string text);
}
