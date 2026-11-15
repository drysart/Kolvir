namespace Kolvir;

/// <summary>
/// Describes another launch of the application that was handed to this, already running, process.
/// </summary>
/// <remarks>
/// The arguments are the ones <see cref="ApplicationOptions.CommandLine"/> supplies, so a
/// launcher that appends its own switches should strip them before the application sees them.
/// </remarks>
public sealed class SecondInstanceEventArgs
{
    /// <summary>Creates the event data for a forwarded launch.</summary>
    /// <param name="arguments">The second launch's command line arguments, without the executable path.</param>
    /// <exception cref="ArgumentNullException"><paramref name="arguments"/> is null.</exception>
    public SecondInstanceEventArgs(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        Arguments = arguments;
    }

    /// <summary>The second launch's command line arguments, without the executable path.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>
    /// Set to true in a <see cref="Application.AddSecondInstanceHandler"/> handler to stop the
    /// default action, which is to raise activation.
    /// </summary>
    public bool Handled { get; set; }
}
