namespace Kolvir.Backend.Win32;

/// <summary>
/// Per-window work that needs the Win32 backend itself rather than the portable window surface.
/// </summary>
/// <remarks>
/// <para>
/// This is the backend half of the platform hook seam. The portable
/// <see cref="IWindowHook"/> surface sees only <see cref="Window"/>, which is all most
/// hooks need; a feature that has to talk to the native window needs the concrete backend, and
/// this interface hands it over without every backend having to be named in the core assembly.
/// </para>
/// <para>
/// Hooks are resolved from the window's own service scope, so a backend with no such hook
/// registered resolves nothing and the loop costs an empty array. Register one with
/// <see cref="ServiceCollectionExtensions.AddWin32WindowHook"/>.
/// </para>
/// <para>
/// The phases run in the same order as the portable ones, and after them. The native window does
/// not exist until the backend is shown, so a hook that needs a handle works from
/// <see cref="WindowPhase.Shown"/> onwards and does its teardown at
/// <see cref="WindowPhase.Closing"/>.
/// </para>
/// </remarks>
public interface IWin32WindowHook
{
    /// <summary>Called once for each phase the window enters.</summary>
    /// <param name="phase">The phase being entered.</param>
    /// <param name="window">The window that entered it.</param>
    /// <param name="backend">The Win32 backend for that window.</param>
    void OnPhase(WindowPhase phase, Window window, Win32WindowBackend backend);
}
