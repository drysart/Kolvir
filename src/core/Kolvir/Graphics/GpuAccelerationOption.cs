namespace Kolvir;

/// <summary>
/// One choice from the acceleration list, resolved all the way down: a family on its own, or a family
/// and one of its GPUs.
/// </summary>
/// <remarks>
/// <see cref="Value"/> is the whole point of this type. It is the string an application saves when
/// the user picks a row, passes back through <see cref="ApplicationOptions.GfxAcceleration"/> at the
/// next startup, and compares against <see cref="IGpuAccelerationService.Current"/> to see which row
/// to show as selected. <see cref="Name"/> is the other half: what the row says.
/// </remarks>
public sealed class GpuAccelerationOption : IEquatable<GpuAccelerationOption>
{
    internal GpuAccelerationOption(GpuAccelerationType type, GpuAccelerationDevice? device)
    {
        Type = type;
        Device = device;
        Value = GpuAccelerationSyntax.Format(type.Kind, device?.Index ?? 0);
        Name = device is null ? type.Name : $"{type.Name}: {device.Name}";
    }

    /// <summary>The family this choice belongs to.</summary>
    public GpuAccelerationKind Kind => Type.Kind;

    /// <summary>The family this choice belongs to.</summary>
    public GpuAccelerationType Type { get; }

    /// <summary>The GPU this choice names, or null for a family with nothing to choose between.</summary>
    public GpuAccelerationDevice? Device { get; }

    /// <summary>What a settings row says, such as "Direct3D 12: NVIDIA GeForce RTX 5090".</summary>
    public string Name { get; }

    /// <summary>The machine-readable value, such as <c>d3d:1</c>; see <see cref="GpuAccelerationSyntax"/>.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public bool Equals(GpuAccelerationOption? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as GpuAccelerationOption);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    /// <summary>True when both sides name the same choice.</summary>
    /// <param name="left">One option.</param>
    /// <param name="right">The other, possibly null.</param>
    public static bool operator ==(GpuAccelerationOption? left, GpuAccelerationOption? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>True when the sides name different choices.</summary>
    /// <param name="left">One option.</param>
    /// <param name="right">The other, possibly null.</param>
    public static bool operator !=(GpuAccelerationOption? left, GpuAccelerationOption? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => Value;
}
