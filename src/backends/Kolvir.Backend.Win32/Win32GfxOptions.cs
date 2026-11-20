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
    private Win32GfxChoice(IReadOnlyList<Win32GfxTier> order, bool cpuOnly)
    {
        Order = order;
        CpuOnly = cpuOnly;
    }

    /// <summary>The order to try, most preferred first. Empty when <see cref="CpuOnly"/> is set.</summary>
    internal IReadOnlyList<Win32GfxTier> Order { get; }

    /// <summary>True when the value asked for the CPU path and nothing else.</summary>
    internal bool CpuOnly { get; }

    /// <summary>The order a window uses when nothing says otherwise.</summary>
    internal static Win32GfxChoice Default { get; } =
        new([Win32GfxTier.Direct3D12, Win32GfxTier.Vulkan, Win32GfxTier.OpenGl], cpuOnly: false);

    /// <summary>The CPU path, chosen explicitly.</summary>
    internal static Win32GfxChoice Cpu { get; } = new([], cpuOnly: true);

    /// <summary>Builds a choice from an explicit order, which is what the tests need.</summary>
    /// <param name="order">The tiers, most preferred first.</param>
    internal static Win32GfxChoice FromOrder(params Win32GfxTier[] order) =>
        order.Length == 0 ? Cpu : new(order, cpuOnly: false);
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