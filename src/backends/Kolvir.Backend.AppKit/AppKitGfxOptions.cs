namespace Kolvir.Backend.AppKit;

/// <summary>
/// The window's drawing preference on macOS: Metal, optionally on one named device, or the CPU path.
/// </summary>
/// <remarks>
/// The shape mirrors the Win32 backend's choice, because the application option that fills it is the
/// same one: a value saved from a settings list, naming one family and optionally one GPU in it.
/// </remarks>
internal readonly struct AppKitGfxChoice
{
    private AppKitGfxChoice(bool metal, bool cpuOnly, string? deviceKey)
    {
        Metal = metal;
        CpuOnly = cpuOnly;
        DeviceKey = deviceKey;
    }

    /// <summary>True when the window should try Metal.</summary>
    internal bool Metal { get; }

    /// <summary>True when the value asked for the CPU path and nothing else.</summary>
    internal bool CpuOnly { get; }

    /// <summary>The device to draw on, or null for whichever the system picks.</summary>
    internal string? DeviceKey { get; }

    /// <summary>Try Metal on the system's default device, which is what a window does by default.</summary>
    internal static AppKitGfxChoice Default { get; } = new(metal: true, cpuOnly: false, deviceKey: null);

    /// <summary>The CPU path, chosen explicitly.</summary>
    internal static AppKitGfxChoice Cpu { get; } = new(metal: false, cpuOnly: true, deviceKey: null);

    /// <summary>Metal only, on <paramref name="deviceKey"/> or on the system default.</summary>
    internal static AppKitGfxChoice Only(string? deviceKey) => new(metal: true, cpuOnly: false, deviceKey);

    /// <summary>
    /// Reads the acceleration an application's user chose, as <see cref="ApplicationOptions.GfxAcceleration"/>
    /// carries it.
    /// </summary>
    /// <param name="value">The saved value, or null when the application has no preference.</param>
    /// <returns>
    /// Null when there is no preference and the backend should decide for itself. Otherwise a choice
    /// naming Metal only, so a device that cannot be drawn on leaves the window on the CPU path rather
    /// than falling through to another GPU.
    /// </returns>
    /// <remarks>
    /// A value this platform cannot honor - <c>d3d</c> saved on a Mac and read here, or something
    /// hand-edited into nonsense - is the CPU path for the same reason: the user asked for a specific
    /// thing, and the honest answer when it is not here is to stop accelerating rather than substitute.
    /// </remarks>
    internal static AppKitGfxChoice? FromSelected(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!GpuAccelerationSyntax.TryParse(value, out var kind, out var deviceKey))
        {
            if (AppKitMetalTrace.IsEnabled)
            {
                AppKitMetalTrace.Write($"kolvir-gfx: ignoring unreadable acceleration '{value}'");
            }

            return Cpu;
        }

        return kind switch
        {
            GpuAccelerationKind.Cpu => Cpu,
            GpuAccelerationKind.Metal => Only(deviceKey),

            // Nothing else exists here: this machine cannot honor what was asked for.
            _ => Cpu,
        };
    }
}

/// <summary>
/// Reads <c>KOLVIR_GL_OPTIONS</c>, the process-wide variable that restricts which tiers a window may
/// use without an application option and without a rebuild.
/// </summary>
/// <remarks>
/// <para>
/// The same variable the Win32 backend honours, with the words this platform has: <c>metal</c> for the
/// accelerated tier and <c>cpu</c> for the CPU path. It exists so an A/B against acceleration needs no
/// setting, no rebuild and no change to the application - <c>KOLVIR_GL_OPTIONS=cpu</c> runs this build
/// without touching a GPU.
/// </para>
/// <para>
/// It takes the place the application option has when the application names none, so an explicit
/// <see cref="ApplicationOptions.GfxAcceleration"/> still wins. A value with nothing recognizable in it
/// - including this platform's sibling words <c>d3d</c>, <c>vulkan</c> and <c>ogl</c> - says nothing at
/// all and leaves the backend's own default in charge, rather than being read as a request for the CPU
/// path: a variable tuned for one platform must not silently disable another.
/// </para>
/// </remarks>
internal static class AppKitGfxOptions
{
    /// <summary>The variable the process is configured with.</summary>
    internal const string VariableName = "KOLVIR_GL_OPTIONS";

    /// <summary>Reads the process's environment once.</summary>
    internal static AppKitGfxChoice? FromEnvironment() =>
        Parse(Environment.GetEnvironmentVariable(VariableName));

    /// <summary>Parses a value of <see cref="VariableName"/>.</summary>
    /// <param name="value">The raw value, or null when the variable is not set.</param>
    /// <returns>
    /// The choice the value asks for, or null when it asks for nothing this platform understands.
    /// </returns>
    internal static AppKitGfxChoice? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sawMetal = false;
        var sawCpu = false;

        foreach (var entry in value.Split(',', StringSplitOptions.TrimEntries))
        {
            switch (entry.ToLowerInvariant())
            {
                case "metal":
                    sawMetal = true;
                    break;

                case "cpu":
                    sawCpu = true;
                    break;

                // A tier this platform does not have, a device identity, or a typo: ignored, so one
                // stray word does not throw the rest away.
                default:
                    break;
            }
        }

        if (sawMetal)
        {
            // `cpu` beside a tier is ignored, exactly as it is on Win32: a list that names a tier is
            // asking for acceleration.
            return AppKitGfxChoice.Only(deviceKey: null);
        }

        return sawCpu ? AppKitGfxChoice.Cpu : null;
    }
}
