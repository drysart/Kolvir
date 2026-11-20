namespace Kolvir;

/// <summary>
/// One family of GPU acceleration as this machine offers it: the first level of the acceleration
/// list, with the GPUs that can serve it underneath.
/// </summary>
/// <remarks>
/// <para>
/// A family with devices gives one <see cref="GpuAccelerationOption"/> per device, so a machine with
/// two GPUs exposes two <c>d3d</c> choices distinguished by hardware identity. A family with no devices
/// - OpenGL and the CPU path have nothing to choose between - gives exactly one option, whose value is
/// the bare family word.
/// </para>
/// <para>
/// <see cref="Name"/> is what a settings list shows above the devices, so it is a name rather than an
/// identifier: "Direct3D 12", "No GPU acceleration".
/// </para>
/// </remarks>
public sealed class GpuAccelerationType
{
    private readonly IReadOnlyList<GpuAccelerationOption> _options;

    /// <summary>Describes one family.</summary>
    /// <param name="kind">The family.</param>
    /// <param name="name">The name to show a user, such as "Direct3D 12".</param>
    /// <param name="devices">
    /// The GPUs that can serve this family, or null when the family has nothing to choose between.
    /// </param>
    public GpuAccelerationType(
        GpuAccelerationKind kind,
        string name,
        IReadOnlyList<GpuAccelerationDevice>? devices = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Kind = kind;
        Name = name;
        Devices = devices is null ? [] : [.. devices];

        if (Devices.Count == 0)
        {
            _options = [new GpuAccelerationOption(this, device: null)];
        }
        else
        {
            GpuAccelerationOption[] options = new GpuAccelerationOption[Devices.Count];

            for (var index = 0; index < Devices.Count; index++)
            {
                options[index] = new GpuAccelerationOption(this, Devices[index]);
            }

            _options = options;
        }
    }

    /// <summary>The family.</summary>
    public GpuAccelerationKind Kind { get; }

    /// <summary>The name to show a user, such as "Direct3D 12".</summary>
    public string Name { get; }

    /// <summary>The GPUs that can serve this family, in the backend's own enumeration order.</summary>
    public IReadOnlyList<GpuAccelerationDevice> Devices { get; }

    /// <summary>True when there is something to show under this family in a settings list.</summary>
    public bool HasDevices => Devices.Count > 0;

    /// <summary>Every fully-resolved choice this family offers, in device order.</summary>
    public IReadOnlyList<GpuAccelerationOption> Options => _options;

    /// <summary>The CPU path, which every backend offers and nothing can refuse.</summary>
    public static GpuAccelerationType Cpu { get; } = new(GpuAccelerationKind.Cpu, "No GPU acceleration");

    /// <summary>Reads a family back out of the value one of its options carries.</summary>
    /// <param name="value">The value, such as <c>d3d:10de-2504-1616</c>.</param>
    /// <param name="option">The matching option.</param>
    /// <returns>False when the value names another family, or a device this one does not list.</returns>
    public bool TryFindOption(string? value, out GpuAccelerationOption? option)
    {
        option = null;

        if (!GpuAccelerationSyntax.TryParse(value, out var kind, out var deviceKey) || kind != Kind)
        {
            return false;
        }

        if (Devices.Count == 0)
        {
            option = _options[0];
            return true;
        }

        if (deviceKey is null)
        {
            // The bare family word names the family's first device, which is the only reading that keeps
            // a hand-written value meaning something.
            option = _options[0];
            return true;
        }

        foreach (var candidate in _options)
        {
            if (candidate.Device?.Key == deviceKey)
            {
                option = candidate;
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public override string ToString() => Name;
}
