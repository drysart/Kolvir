namespace Kolvir;

/// <summary>
/// The service an application gets when its backend registers none: a machine whose only sure way to
/// draw is the CPU, which is what a headless or not-yet-supported backend can honestly report.
/// </summary>
/// <remarks>
/// The core registers this before the backend runs its own registrations, so a backend that can
/// enumerate more hands in its own implementation and the last registration wins.
/// </remarks>
internal sealed class CpuGpuAccelerationService : GpuAccelerationService
{
    /// <inheritdoc />
    protected override IReadOnlyList<GpuAccelerationType> Enumerate() => [];
}
