namespace Kolvir;

/// <summary>Identifies one window in an <see cref="Application"/> event.</summary>
public sealed class WindowEventArgs
{
    /// <summary>Creates the arguments for <paramref name="window"/>.</summary>
    public WindowEventArgs(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Window = window;
    }

    /// <summary>The window the event is about.</summary>
    public Window Window { get; }
}
