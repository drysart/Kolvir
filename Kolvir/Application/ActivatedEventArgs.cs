namespace Kolvir;

/// <summary>
/// Describes a request from the platform for the application to come to the foreground, such as a
/// click on the macOS Dock tile or a second launch that could not start a competing process.
/// </summary>
public sealed class ActivatedEventArgs
{
    /// <summary>
    /// Set to true in a <see cref="Application.AddActivatedHandler"/> handler to stop the
    /// default action, which is <see cref="Application.Activate"/>.
    /// </summary>
    public bool Handled { get; set; }
}
