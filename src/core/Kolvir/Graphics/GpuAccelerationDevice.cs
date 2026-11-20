namespace Kolvir;

/// <summary>
/// One GPU under one acceleration family: the thing a two-level acceleration list shows under
/// "Direct3D 12" or "Vulkan".
/// </summary>
/// <remarks>
/// <see cref="Key"/> is what makes a saved setting mean the same card next time. It is an identity the
/// backend's own API reports - the adapter's vendor, device and subsystem ids on Direct3D 12, the device
/// UUID on Vulkan - rather than a position, because both APIs enumerate in an order they make no promise
/// about; see <see cref="GpuAccelerationSyntax"/> for why that matters.
/// </remarks>
public sealed class GpuAccelerationDevice
{
    /// <summary>Describes one GPU.</summary>
    /// <param name="key">The device identity, as the family's backend reports it.</param>
    /// <param name="name">The name to show a user, such as the driver's name for the adapter.</param>
    public GpuAccelerationDevice(string key, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!GpuAccelerationSyntax.IsDeviceKey(key))
        {
            throw new ArgumentException($"'{key}' is not a device identity.", nameof(key));
        }

        Key = key.ToLowerInvariant();
        Name = name;
    }

    /// <summary>
    /// The device identity: the same across reboots, driver updates and the GPU being moved, and
    /// different between two physically identical cards.
    /// </summary>
    public string Key { get; }

    /// <summary>The name to show a user, such as the driver's name for the adapter.</summary>
    public string Name { get; }

    /// <inheritdoc />
    public override string ToString() => Name;
}
