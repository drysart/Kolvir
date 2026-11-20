namespace Kolvir.Backend.AppKit;

/// <summary>What a switch asks the application to do: rebuild every window's drawing surface.</summary>
/// <remarks>
/// The service knows what the machine offers and which choice is in use; the application backend owns
/// the windows. This is the seam between them, so the service never has to know what a window is.
/// </remarks>
internal interface IAppKitAccelerationTarget
{
    /// <summary>Switches every open window to <paramref name="option"/>.</summary>
    /// <param name="option">The choice, already known to be one this machine offers.</param>
    /// <param name="reason">One sentence saying what happened. Never null.</param>
    /// <returns>True when the windows are drawing with <paramref name="option"/> afterwards.</returns>
    bool Apply(GpuAccelerationOption option, out string reason);
}

/// <summary>
/// The macOS answer to <see cref="IGpuAccelerationService"/>: Metal, with the devices this machine can
/// draw on, and the CPU path underneath.
/// </summary>
/// <remarks>
/// <para>
/// Metal is the only accelerated family macOS has, so the list is shallow here: one family, one device
/// on every Apple silicon Mac, and two on an Intel Mac with a discrete GPU. Which is still the same
/// two-level shape a settings UI reads, so the page needs no special case for this platform.
/// </para>
/// <para>
/// A family is listed only when the machine really reports a device for it, so presenting
/// <see cref="IGpuAccelerationService.Options"/> as the whole set of choices stays correct.
/// </para>
/// </remarks>
internal sealed class AppKitGpuAccelerationService : GpuAccelerationService
{
    /// <summary>The name a settings list shows for the Metal family.</summary>
    private const string MetalName = "Metal";

    private readonly IAppKitAccelerationTarget? _target;

    /// <summary>Creates the service a macOS application resolves.</summary>
    /// <param name="target">
    /// What actually rebuilds the windows, or null when there is nothing to switch - the case a test
    /// reaches.
    /// </param>
    internal AppKitGpuAccelerationService(IAppKitAccelerationTarget? target = null) => _target = target;

    /// <summary>
    /// The value a window created from now on should use, or null when nothing has been switched and the
    /// application's own option decides.
    /// </summary>
    /// <remarks>
    /// This is deliberately not <see cref="IGpuAccelerationService.Current"/>: Current is what a window
    /// actually built, which starts on the CPU path and may be the CPU path because the requested device
    /// failed. What a new window needs is the request, not its outcome.
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
        var devices = AppKitMetalSurface.EnumerateDevices();

        return devices.Count == 0
            ? []
            : [new GpuAccelerationType(GpuAccelerationKind.Metal, MetalName, devices)];
    }

    /// <summary>
    /// Records what a window came up with, which is what <see cref="IGpuAccelerationService.Current"/>
    /// reports. A null view, or one that is not drawing through Metal, is the CPU path.
    /// </summary>
    /// <param name="view">The view the window draws with, or null when it has none.</param>
    internal void Report(AppKitSkiaView? view)
    {
        if (view?.MetalDeviceKey is not { } key)
        {
            ReportCurrent(GpuAccelerationSyntax.CpuValue);
            return;
        }

        var value = GpuAccelerationSyntax.Format(GpuAccelerationKind.Metal, key);

        // The device may not be one the enumeration listed - it appeared since, or its key fell back to a
        // name hash - so the family's first device is reported instead of a value no settings list holds.
        ReportCurrent(TryFindOption(value, out _) ? value : GpuAccelerationSyntax.MetalValue);
    }
}
