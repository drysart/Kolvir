using Kolvir;

namespace Kolvir.Backend.Win32;

/// <summary>
/// Opens a URI through the shell, which is what makes the registered handler for the scheme -
/// the default browser, the mail client, the file manager - come up.
/// </summary>
internal sealed class Win32Launcher : ILauncher
{
    public bool OpenUrl(string url)
    {
        // ShellExecuteW returns a value greater than 32 on success; the small values are the
        // legacy error codes (no association, out of memory, and so on).
        var result = NativeMethods.ShellExecuteW(
            IntPtr.Zero,
            "open",
            url,
            null,
            null,
            NativeMethods.SwShowNormal);

        return result.ToInt64() > NativeMethods.ShellExecuteSuccess;
    }
}
