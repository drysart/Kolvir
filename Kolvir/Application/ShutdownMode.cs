namespace Kolvir;

/// <summary>What ends an <see cref="Application"/>'s message loop.</summary>
public enum ShutdownMode
{
    /// <summary>The loop ends when the last entry in <see cref="Application.Windows"/> closes.</summary>
    OnLastWindowClose,

    /// <summary>The loop ends when <see cref="Application.MainWindow"/> closes.</summary>
    OnMainWindowClose,

    /// <summary>
    /// The loop ends only when <see cref="Application.Exit()"/> is called. A backend keeps the
    /// platform application alive with no windows open: a message-only window on Windows,
    /// <c>GApplication.Hold()</c> on GTK4, nothing at all on macOS.
    /// </summary>
    OnExplicitShutdown,
}
