namespace Kolvir;

/// <summary>
/// One family of GPU acceleration as this machine offers it: the first level of the acceleration
/// list, with the GPUs that can serve it underneath.
/// </summary>
/// <remarks>
/// <para>
/// A family with devices gives one <see cref="GpuAccelerationOption"/> per device, so a machine with
/// two GPUs exposes <c>d3d</c> and <c>d3d:1</c>. A family with no devices - OpenGL and the CPU path
/// have nothing to choose between - gives exactly one option, whose value is the bare family word.
/// </para>
/// <para>
/// <see cref="Name"/> is what a settings list shows above the devices, so it is a sentence fragment
/// rather than an identifier: "Direct3D 12", "No GPU acceleration".
/// </para>
/// </remarks>
public sealed class GpuAccelerationType
{
    private readonly IReadOnlyList<GpuAccelerationOption> _options;

    /// <summary>Describes one family.</summary>
    /// <param name="kind">The family.</param>
    /// <param name="name">The name to show a user, such as "Direct3D 12".</param>
    /// <param name="devices">
    /// The GPUs that can serve this family, in the family's own enumeration order, or null when the
    /// family has nothing to choose between.
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

    /// <summary>The GPUs that can serve this family, in the family's own enumeration order.</summary>
    public IReadOnlyList<GpuAccelerationDevice> Devices { get; }

    /// <summary>True when there is more than nothing to show under this family in a settings list.</summary>
    public bool HasDevices => Devices.Count > 0;

    /// <summary>Every fully-resolved choice this family offers, in device order.</summary>
    public IReadOnlyList<GpuAccelerationOption> Options => _options;

    /// <summary>The CPU path, which every backend offers and nothing can refuse.</summary>
    public static GpuAccelerationType Cpu { get; } = new(GpuAccelerationKind.Cpu, "No GPU acceleration");

    /// <summary>Reads a family back out of the value one of its options carries.</summary>
    /// <param name="value">The value, such as <c>d3d:1</c>.</param>
    /// <param name="option">The matching option.</param>
    /// <returns>False when the value names no family, or names a device this family does not have.</returns>
    public bool TryFindOption(string? value, out GpuAccelerationOption? option)
    {
        option = null;

        if (!GpuAccelerationSyntax.TryParse(value, out var kind, out var deviceIndex) || kind != Kind)
        {
            return false;
        }

        if (Devices.Count == 0)
        {
            option = _options[0];
            return true;
        }

        if (deviceIndex >= Devices.Count)
        {
            return false;
        }

        option = _options[deviceIndex];
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Name;
}
