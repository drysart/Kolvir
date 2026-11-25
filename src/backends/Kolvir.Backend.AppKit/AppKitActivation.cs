namespace Kolvir.Backend.AppKit;

/// <summary>
/// The one activation loss a shown popup survives: the system's character palette taking the
/// activation to insert a character.
/// </summary>
/// <remarks>
/// <para>
/// A popup that never takes activation is dismissed when the application loses it. That is the one
/// route a non-activating surface has for "the user went somewhere else": a click in our own windows
/// is reported through <see cref="IPopupWindow.OutsidePress"/>, but a click in another process's
/// window is not a press this process ever sees, so all that is left to observe is that someone else
/// became frontmost.
/// </para>
/// <para>
/// The character palette - "Emoji &amp; Symbols", opened with Control-Command-Space - is a process of
/// its own (<c>com.apple.CharacterPaletteIM</c>), and it is the one application that takes the
/// activation in order to give it back. Measured on macOS 27.2 with a text box inside a popup holding
/// the keyboard: clicking a character in the palette deactivates the application, the palette is
/// frontmost for about 0.6 seconds, and then macOS re-activates the application and delivers the
/// commit to the field that had the keyboard - which is the field inside the popup, because a popup
/// borrows the window's keyboard rather than taking its own. A popup that dismissed on that
/// deactivation is gone before the commit arrives, so the character the user chose is dropped and the
/// popup closes as if they had clicked elsewhere.
/// </para>
/// <para>
/// So the palette is not the user leaving: it is the user typing into the focused field through a
/// system surface. Everything else - Finder because the desktop was clicked, an application the user
/// switched to, the Dock - still dismisses, and so does anything this does not recognize, because the
/// cautious answer is the default one.
/// </para>
/// </remarks>
internal static class AppKitActivation
{
    /// <summary>
    /// The system's character palette: the panel Control-Command-Space opens, which inserts the
    /// character it was clicked for into the field that has the keyboard.
    /// </summary>
    internal const string CharacterPalette = "com.apple.CharacterPaletteIM";

    /// <summary>
    /// Whether an application taking the activation is the system's text input surface, whose click
    /// commits a character into the focused field rather than taking the user away.
    /// </summary>
    /// <param name="bundleIdentifier">
    /// The bundle identifier of the application that took the activation, or null when there is none.
    /// </param>
    /// <returns>True for the character palette, false for everything else.</returns>
    public static bool IsTextInputSurface(string? bundleIdentifier) =>
        string.Equals(bundleIdentifier, CharacterPalette, StringComparison.Ordinal);
}
