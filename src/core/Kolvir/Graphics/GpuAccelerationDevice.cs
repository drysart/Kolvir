namespace Kolvir;

/// <summary>
/// One GPU under one acceleration family: the thing a two-level acceleration list shows under
/// "Direct3D 12" or "Vulkan".
/// </summary>
/// <remarks>
/// <see cref="Index"/> is the device's place in its family's enumeration, and it is what the
/// device suffix on a <see cref="GpuAccelerationOption.Value"/> refers to. It is not an adapter
/// handle or a Vulkan device handle: those belong to the backend and mean nothing to an application.
/// </remarks>
public sealed class GpuAccelerationDevice
{
    /// <summary>Describes one GPU.</summary>
    /// <param name="index">The device's index within its family, counting from 0.</param>
    /// <param name="name">The name to show a user, such as the driver's name for the adapter.</param>
    public GpuAccelerationDevice(int index, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Index = index;
        Name = name;
    }

    /// <summary>The device's index within its family, counting from 0.</summary>
    public int Index { get; }

    /// <summary>The name to show a user, such as the driver's name for the adapter.</summary>
    public string Name { get; }

    /// <inheritdoc />
    public override string ToString() => Name;
}
