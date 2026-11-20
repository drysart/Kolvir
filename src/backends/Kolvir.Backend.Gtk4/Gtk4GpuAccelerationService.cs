namespace Kolvir.Backend.Gtk4;

/// <summary>What a switch asks the application to do: rebuild every window's drawing surface.</summary>
/// <remarks>
/// The service knows what the machine offers and which choice is in use; the application backend owns
/// the windows. This is the seam between them, so the service never has to know what a window is.
/// </remarks>
internal interface IGtk4AccelerationTarget
{
    /// <summary>Switches every open window to <paramref name="option"/>.</summary>
    /// <param name="option">The choice, already known to be one this machine offers.</param>
    /// <param name="reason">One sentence saying what happened. Never null.</param>
    /// <returns>True when the windows are drawing with <paramref name="option"/> afterwards.</returns>
    bool Apply(GpuAccelerationOption option, out string reason);
}

/// <summary>
/// The GTK4 answer to <see cref="IGpuAccelerationService"/>: OpenGL through the window's
/// <see cref="Gtk.GLArea"/>, and the CPU path underneath.
/// </summary>
/// <remarks>
/// <para>
/// GTK4 shows one accelerated family and has nothing to choose between inside it: the GL context
/// belongs to the area GTK created, and GTK does not let an application ask for a particular GPU. So the
/// family carries no devices and the list is two rows - <c>ogl</c> and <c>cpu</c> - which is still the
/// shape a settings UI reads, so the page needs no platform case.
/// </para>
/// <para>
/// The family is offered when there is a display to draw on. Whether a *particular* window can make a
/// GL context is answered by that window, when it first tries, exactly as on the other backends: a
/// machine with no GL driver shows the row and falls back with a sentence saying which step failed.
/// </para>
/// </remarks>
internal sealed class Gtk4GpuAccelerationService : GpuAccelerationService
{
    /// <summary>The name a settings list shows for the OpenGL family.</summary>
    private const string OpenGlName = "OpenGL";

    private readonly IGtk4AccelerationTarget? _target;

    /// <summary>Creates the service a GTK4 application resolves.</summary>
    /// <param name="target">
    /// What actually rebuilds the windows, or null when there is nothing to switch - the case a test
    /// reaches.
    /// </param>
    internal Gtk4GpuAccelerationService(IGtk4AccelerationTarget? target = null) => _target = target;

    /// <summary>
    /// The value a window created from now on should use, or null when nothing has been switched and the
    /// application's own option decides.
    /// </summary>
    /// <remarks>
    /// This is deliberately not <see cref="IGpuAccelerationService.Current"/>: Current is what a window
    /// actually built, which starts on the CPU path and may be the CPU path because the seam failed.
    /// What a new window needs is the request, not its outcome.
    /// </remarks>
    internal string? Requested { get; private set; }

    /// <inheritdoc />
    protected override bool ApplyCurrent(GpuAccelerationOption option, out string reason)
    {
        if (_target is null)
        {
            reason = "This application has no windows that can be switched.";
            return false;
        }

        if (!_target.Apply(option, out reason))
        {
            return false;
        }

        // Only a switch that took becomes what the next window asks for: a request that had to fall back
        // leaves the old value in place, so a second window does not inherit a failure.
        Requested = option.Value;
        return true;
    }

    /// <inheritdoc />
    protected override IReadOnlyList<GpuAccelerationType> Enumerate()
    {
        try
        {
            return Gdk.Display.GetDefault() is null
                ? []
                : [new GpuAccelerationType(GpuAccelerationKind.OpenGl, OpenGlName)];
        }
        catch (Exception exception)
        {
            // Asked before GDK is up - a service resolved before the backend initialized - there is no
            // display to draw on yet, and reporting nothing is the honest answer.
            Gtk4GlTrace.Write($"gfx: enumeration skipped ({exception.GetType().Name})");
            return [];
        }
    }

    /// <summary>
    /// Records what a window came up with, which is what <see cref="IGpuAccelerationService.Current"/>
    /// reports. A null view, or one that is not drawing through GL, is the CPU path.
    /// </summary>
    /// <param name="view">The view the window draws with, or null when it has none.</param>
    internal void Report(Gtk4SkiaView? view) =>
        ReportCurrent(view?.GraphicsPath == "gl" ? GpuAccelerationSyntax.OpenGlValue : GpuAccelerationSyntax.CpuValue);
}
