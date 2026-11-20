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
