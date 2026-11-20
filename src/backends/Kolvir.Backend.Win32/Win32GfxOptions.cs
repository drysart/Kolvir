namespace Kolvir.Backend.Win32;

/// <summary>One way the window can draw, in the order <c>KOLVIR_GL_OPTIONS</c> names them.</summary>
internal enum Win32GfxTier
{
    /// <summary>The Direct3D 12 tier, <c>d3d</c> on the environment variable and <c>d3d12</c> in the
    /// diagnostic line.</summary>
    Direct3D12,

    /// <summary>The Vulkan tier, <c>vulkan</c> both places.</summary>
    Vulkan,

    /// <summary>The WGL tier, <c>ogl</c> on the environment variable and <c>gl</c> in the diagnostic
    /// line, where it has always been called that.</summary>
    OpenGl,
}

/// <summary>
/// The window's drawing preference: an ordered list of tiers to try, or a request for the CPU path.
/// </summary>
/// <remarks>
/// The default is every tier, Direct3D 12 first: it is the native API on this platform, it needs no
/// Vulkan runtime of its own, and its resize path releases. The list exists so one machine can be
/// asked for a specific path without rebuilding, which is what makes an A/B between the three tiers
/// possible on a box that can run all of them.
/// </remarks>
internal readonly struct Win32GfxChoice
{
    private Win32GfxChoice(IReadOnlyList<Win32GfxTier> order, bool cpuOnly, string? deviceKey = null)
    {
        Order = order;
        CpuOnly = cpuOnly;
        DeviceKey = deviceKey;
    }

    /// <summary>The order to try, most preferred first. Empty when <see cref="CpuOnly"/> is set.</summary>
    internal IReadOnlyList<Win32GfxTier> Order { get; }

    /// <summary>True when the value asked for the CPU path and nothing else.</summary>
    internal bool CpuOnly { get; }

    /// <summary>
    /// The identity of the GPU the choice named within its family, or null when it named none. Only the
    /// first tier in <see cref="Order"/> sees it: a device identity belongs to the family that carried it.
    /// </summary>
    internal string? DeviceKey { get; }

    /// <summary>The order a window uses when nothing says otherwise.</summary>
    internal static Win32GfxChoice Default { get; } =
        new([Win32GfxTier.Direct3D12, Win32GfxTier.Vulkan, Win32GfxTier.OpenGl], cpuOnly: false);

    /// <summary>The CPU path, chosen explicitly.</summary>
    internal static Win32GfxChoice Cpu { get; } = new([], cpuOnly: true);

    /// <summary>Builds a choice from an explicit order, which is what the tests need.</summary>
    /// <param name="order">The tiers, most preferred first.</param>
    internal static Win32GfxChoice FromOrder(params Win32GfxTier[] order) =>
        order.Length == 0 ? Cpu : new(order, cpuOnly: false);

    /// <summary>Builds a choice that names one family only, with an optional device inside it.</summary>
    /// <param name="tier">The only tier to try.</param>
    /// <param name="deviceKey">The GPU within that tier, or null for whichever the window would pick.</param>
    internal static Win32GfxChoice Only(Win32GfxTier tier, string? deviceKey = null) =>
        new([tier], cpuOnly: false, deviceKey);
}

/// <summary>
/// Parses <c>KOLVIR_GL_OPTIONS</c>, the environment variable that reorders the tiers.
/// </summary>
/// <remarks>
/// <para>
/// The value is a comma-separated list of <c>d3d</c>, <c>vulkan</c> and <c>ogl</c>, tried in the order
/// written; a tier left out is not tried at all. It may also be the single word <c>cpu</c>, which asks
/// for the non-accelerated path.
/// </para>
/// <para>
/// Entries that are not one of those four words are ignored, and so is a second mention of a tier
/// already listed - the point of the variable is to order the real tiers, not to validate a config
/// file, and one stray word should not throw the rest away. <c>cpu</c> beside a tier is ignored the
/// same way, because a list that names a tier is asking for acceleration. A value with nothing
/// recognizable in it leaves the default order in place.
/// </para>
/// </remarks>
internal static class Win32GfxOptions
{
    /// <summary>The variable the process is configured with.</summary>
    internal const string VariableName = "KOLVIR_GL_OPTIONS";

    /// <summary>Reads the process's environment once.</summary>
    internal static Win32GfxChoice FromEnvironment() =>
        Parse(Environment.GetEnvironmentVariable(VariableName));

    /// <summary>
    /// Reads the acceleration an application's user chose, as <see cref="ApplicationOptions.GfxAcceleration"/>
    /// carries it.
    /// </summary>
    /// <param name="value">The saved value, or null when the application has no preference.</param>
    /// <returns>
    /// Null when there is no preference and the backend should pick for itself, which is what leaves
    /// <c>KOLVIR_GL_OPTIONS</c> and the default order in charge. Otherwise a choice naming one family
    /// only, so that a family that cannot be built leaves the window on the CPU path rather than
    /// falling through to a different GPU API.
    /// </returns>
    /// <remarks>
    /// A value this platform cannot honor - <c>metal</c> saved on a Mac and read here, or something
    /// hand-edited into nonsense - is the CPU path for the same reason: the user asked for a specific
    /// thing, and the honest answer when it is not here is to stop accelerating rather than to
    /// substitute another API.
    /// </remarks>
    internal static Win32GfxChoice? FromSelected(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!GpuAccelerationSyntax.TryParse(value, out var kind, out var deviceKey))
        {
            if (Win32GlTrace.IsEnabled)
            {
                Win32GlTrace.Write($"kolvir-gfx: ignoring unreadable acceleration '{value}'");
            }

            return Win32GfxChoice.Cpu;
        }

        return kind switch
        {
            GpuAccelerationKind.Cpu => Win32GfxChoice.Cpu,
            GpuAccelerationKind.Direct3D12 => Win32GfxChoice.Only(Win32GfxTier.Direct3D12, deviceKey),
            GpuAccelerationKind.Vulkan => Win32GfxChoice.Only(Win32GfxTier.Vulkan, deviceKey),
            GpuAccelerationKind.OpenGl => Win32GfxChoice.Only(Win32GfxTier.OpenGl),

            // Metal does not exist here: this machine cannot honor what was asked for.
            _ => Win32GfxChoice.Cpu,
        };
    }

    /// <summary>Parses a value.</summary>
    /// <param name="value">The raw value, or null when the variable is not set.</param>
    internal static Win32GfxChoice Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Win32GfxChoice.Default;
        }

        List<Win32GfxTier> order = [];
        var sawCpu = false;

        foreach (var entry in value.Split(',', StringSplitOptions.TrimEntries))
        {
            switch (entry.ToLowerInvariant())
            {
                case "d3d":
                    Add(Win32GfxTier.Direct3D12, order);
                    break;

                case "vulkan":
                    Add(Win32GfxTier.Vulkan, order);
                    break;

                case "ogl":
                    Add(Win32GfxTier.OpenGl, order);
                    break;

                case "cpu":
                    sawCpu = true;
                    break;

                default:
                    // An empty entry, a typo, or a word from some later version of this variable: it
                    // is ignored, and what is left still describes the order.
                    break;
            }
        }

        if (order.Count > 0)
        {
            return Win32GfxChoice.FromOrder([.. order]);
        }

        return sawCpu ? Win32GfxChoice.Cpu : Win32GfxChoice.Default;
    }

    private static void Add(Win32GfxTier tier, List<Win32GfxTier> order)
    {
        // The first mention fixes a tier's place: a repeat adds nothing to the order, and skipping it
        // keeps the list free of duplicates for the tier loop.
        if (!order.Contains(tier))
        {
            order.Add(tier);
        }
    }
}