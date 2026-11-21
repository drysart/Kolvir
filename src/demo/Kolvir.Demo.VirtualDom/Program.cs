using Kolvir;
using Kolvir.Backend;
using Kolvir.VirtualDom;

namespace Kolvir.Demo.VirtualDom;

/// <summary>
/// A small application-scope sample for the virtual DOM: the application component renders the
/// window set, and each window still renders an ordinary control tree.
/// </summary>
/// <remarks>
/// <c>--check</c> runs the same component on the headless backend, drives the real controls through
/// the host, and prints what the model and the windows say after opening, closing and renaming a
/// document. It is the manual flow without a display.
/// </remarks>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--check") >= 0)
        {
            return HeadlessCheck.Run();
        }

        var options = new ApplicationOptions
        {
            ApplicationName = "Kolvir Virtual DOM",
#if WINDOWS
            // A WinExe has no console of its own; borrow the parent's so diagnostics are visible.
            AttachConsole = true,
#endif
        };

        using var application = new Application(KolvirBackend.CreateDefault(), options);
        using var windows = VirtualDomRuntime.Mount(application, new DocumentApplication());

        return application.Run();
    }
}
