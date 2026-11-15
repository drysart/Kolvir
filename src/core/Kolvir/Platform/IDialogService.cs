namespace Kolvir;

/// <summary>
/// The platform's message box, file and font dialogs. Every backend, headless included, registers one
/// through <see cref="IApplicationBackend.RegisterServices"/>; an application reaches it
/// through <c>Application.Dialogs</c> or
/// <c>Services.GetService&lt;IDialogService&gt;()</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Implementations must tolerate a nested loop.</b> Every method here is modal: it re-enters the
/// platform loop that <c>Application.Run</c> started and returns only when the modal session
/// ends. A backend therefore cannot assume its loop is entered once and left once, and
/// <see cref="IApplicationBackend.Invoke"/> callbacks must keep dispatching to the same UI
/// thread while a dialog is open. That requirement is part of the backend contract from the first
/// migration step on purpose: it is the first feature that makes "the backend owns the loop"
/// observable, and retrofitting it once three backends exist is how modal bugs get baked in.
/// </para>
/// <para>
/// Call these on the UI thread. <c>owner</c> is null when the application has no window to be
/// modal to; a backend then centres the dialog on the primary screen instead of parenting it.
/// </para>
/// </remarks>
public interface IDialogService
{
    /// <summary>
    /// Shows a message box and returns the button the user chose. Blocks in a nested loop until
    /// the user dismisses it.
    /// </summary>
    DialogResult ShowMessage(MessageBoxOptions options, Window? owner);

    /// <summary>
    /// Asks for a file to open and returns its full path, or null when the user cancelled.
    /// Blocks in a nested loop.
    /// </summary>
    string? OpenFile(FileDialogOptions options, Window? owner);

    /// <summary>
    /// Asks for a file to save and returns its full path, or null when the user cancelled.
    /// Blocks in a nested loop.
    /// </summary>
    string? SaveFile(FileDialogOptions options, Window? owner);

    /// <summary>
    /// Asks for a folder and returns its full path, or null when the user cancelled. Blocks in a
    /// nested loop.
    /// </summary>
    string? PickFolder(FileDialogOptions options, Window? owner);

    /// <summary>
    /// Asks for a font and returns what the user chose, or null when the user cancelled. Blocks in a
    /// nested loop.
    /// </summary>
    /// <remarks>
    /// The result is a value, not an <see cref="Font"/>: the caller owns whatever font it
    /// builds from it. The dialog lists the families the operating system has installed, so a face
    /// loaded from a file is not offered unless the OS knows about it as well.
    /// </remarks>
    FontSelection? PickFont(FontDialogOptions options, Window? owner);
}
