namespace Kolvir;

/// <summary>
/// What the machine can draw a window with, and what it is drawing with now. An application resolves
/// this from <see cref="Application.Services"/> to build a settings list.
/// </summary>
/// <remarks>
/// <para>
/// The service is cross-platform: this interface and everything it returns live in the core assembly
/// and say nothing about Direct3D, Vulkan, Metal or GLX. Each backend registers its own implementation
/// from <see cref="IApplicationBackend.RegisterServices"/>, and the one registered last wins, because a
/// backend registers after the core has put its CPU-only default in place.
/// </para>
/// <para>
/// The list is two levels, the same shape a settings UI wants: <see cref="Types"/> is the families
/// that work on this machine, and each family's <see cref="GpuAccelerationType.Devices"/> are the GPUs
/// under it. <see cref="Options"/> is that list flattened, one entry per fully-resolved choice, which
/// is what an application saves and what <see cref="Current"/> returns.
/// </para>
/// <para>
/// Nothing that cannot be used is listed. A family appears only when the backend has actually built
/// something with it, and a GPU appears only when the backend can draw on it, so presenting
/// <see cref="Options"/> as the whole set of choices is always correct. The list is fixed for the life
/// of the process: only <see cref="Current"/> changes.
/// </para>
/// <para>
/// Enumeration can be slow the first time - the Win32 backend opens a Vulkan instance to ask what
/// devices exist - so it happens on the first read and the answer is kept.
/// </para>
/// </remarks>
public interface IGpuAccelerationService
{
    /// <summary>
    /// Every family of acceleration this machine can draw with, in the backend's own order of
    /// preference, with the CPU path last.
    /// </summary>
    IReadOnlyList<GpuAccelerationType> Types { get; }

    /// <summary>
    /// Every fully-resolved choice, flattened: one entry per family that has no devices, and one entry
    /// per device for a family that has them.
    /// </summary>
    IReadOnlyList<GpuAccelerationOption> Options { get; }

    /// <summary>
    /// The choice a window is drawing with now. Never null, and always one of <see cref="Options"/>:
    /// the CPU path stands in until a window has picked something, and it is also what a window falls
    /// back to when the choice it was asked for could not be built.
    /// </summary>
    GpuAccelerationOption Current { get; }

    /// <summary>Raised when <see cref="Current"/> changes, so a settings list can follow it.</summary>
    event EventHandler? CurrentChanged;

    /// <summary>
    /// Switches every window to <paramref name="value"/> while the application runs, so a settings UI can
    /// apply a choice without a restart.
    /// </summary>
    /// <param name="value">A value from <see cref="Options"/>, such as <c>d3d:10de-2504-1616</c>.</param>
    /// <param name="reason">
    /// One sentence saying what happened, whether or not it worked: the step that could not be built, or
    /// that the choice is already in use. Never null.
    /// </param>
    /// <returns>
    /// True when the requested choice is what the windows are drawing with afterwards. False when it could
    /// not be honored - which includes the case where the family was built but a window fell back to the
    /// CPU path, because a request that quietly turns into a different answer is the one thing a settings
    /// UI must not show.
    /// </returns>
    /// <remarks>
    /// Switching means building a new drawing surface, so it is not free: expect the window to miss a
    /// frame or two, and nothing else to change. What is on screen is repainted, the controls and their
    /// state are untouched, and <see cref="Current"/> follows whatever the windows actually built. A
    /// backend that cannot do this while it runs refuses and leaves everything as it was.
    /// </remarks>
    bool TryApply(string? value, out string reason);

    /// <summary>Switches every window to <paramref name="option"/>, which must be one of <see cref="Options"/>.</summary>
    /// <param name="option">The choice, as a settings list holds it.</param>
    /// <param name="reason">One sentence saying what happened. Never null.</param>
    /// <returns>True when the choice is what the windows are drawing with afterwards.</returns>
    bool TryApply(GpuAccelerationOption option, out string reason);
}
