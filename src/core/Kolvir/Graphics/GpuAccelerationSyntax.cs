using System.Globalization;

namespace Kolvir;

/// <summary>
/// The machine-readable spelling of an acceleration choice: what <see cref="GpuAccelerationOption.Value"/>
/// holds, what an application saves when the user picks from a settings list, and what it passes back
/// through <see cref="ApplicationOptions.GfxAcceleration"/> at startup.
/// </summary>
/// <remarks>
/// <para>
/// A value is a family word, optionally followed by a colon and the index of one device within that
/// family: <c>d3d</c>, <c>d3d:1</c>, <c>vulkan</c>, <c>vulkan:0</c>, <c>ogl</c>, <c>metal</c> and
/// <c>cpu</c>. Index 0 is the family's first device and is also what the bare word means, so
/// <c>d3d</c> and <c>d3d:0</c> are the same choice and <see cref="Format"/> writes the bare word for
/// it. <c>cpu</c> takes no index, because there is nothing under it to choose between.
/// </para>
/// <para>
/// The family words are the ones <c>KOLVIR_GL_OPTIONS</c> already uses on the Win32 backend, so a
/// value an application saved can also be handed to that variable while the choice is being tried
/// out. Reading is deliberately forgiving of spelling - <c>d3d12</c>, <c>direct3d</c>, <c>gl</c> and
/// <c>opengl</c> are all read as their family - while writing is strict, so what an application
/// saves stays in one canonical form.
/// </para>
/// </remarks>
public static class GpuAccelerationSyntax
{
    /// <summary>The family word for <see cref="GpuAccelerationKind.Cpu"/>: <c>cpu</c>.</summary>
    public const string CpuValue = "cpu";

    /// <summary>The family word for <see cref="GpuAccelerationKind.Direct3D12"/>: <c>d3d</c>.</summary>
    public const string Direct3D12Value = "d3d";

    /// <summary>The family word for <see cref="GpuAccelerationKind.Vulkan"/>: <c>vulkan</c>.</summary>
    public const string VulkanValue = "vulkan";

    /// <summary>The family word for <see cref="GpuAccelerationKind.OpenGl"/>: <c>ogl</c>.</summary>
    public const string OpenGlValue = "ogl";

    /// <summary>The family word for <see cref="GpuAccelerationKind.Metal"/>: <c>metal</c>.</summary>
    public const string MetalValue = "metal";

    /// <summary>The family word for <paramref name="kind"/>, without a device index.</summary>
    /// <param name="kind">The family.</param>
    public static string Word(GpuAccelerationKind kind) => kind switch
    {
        GpuAccelerationKind.Cpu => CpuValue,
        GpuAccelerationKind.Direct3D12 => Direct3D12Value,
        GpuAccelerationKind.Vulkan => VulkanValue,
        GpuAccelerationKind.OpenGl => OpenGlValue,
        GpuAccelerationKind.Metal => MetalValue,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// The canonical value for a family and one of its devices.
    /// </summary>
    /// <param name="kind">The family.</param>
    /// <param name="deviceIndex">The device's index within the family; 0 is written without a suffix.</param>
    public static string Format(GpuAccelerationKind kind, int deviceIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deviceIndex);

        if (kind == GpuAccelerationKind.Cpu && deviceIndex != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceIndex), deviceIndex, "The CPU path has no devices to choose between.");
        }

        var word = Word(kind);
        return deviceIndex == 0 ? word : string.Create(CultureInfo.InvariantCulture, $"{word}:{deviceIndex}");
    }

    /// <summary>Reads a value, with or without a device suffix and in any of the accepted spellings.</summary>
    /// <param name="value">The value to read, or null when there is none.</param>
    /// <param name="kind">The family the value named.</param>
    /// <param name="deviceIndex">
    /// The device index the value carried; 0 when it carried none, which is the family's first device.
    /// </param>
    /// <returns>True when the value named a family; false when it named nothing or was malformed.</returns>
    public static bool TryParse(string? value, out GpuAccelerationKind kind, out int deviceIndex)
    {
        kind = GpuAccelerationKind.Cpu;
        deviceIndex = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        var word = colon < 0 ? text : text[..colon];

        if (colon >= 0)
        {
            var digits = text[(colon + 1)..];

            // The suffix is an index, not a number with a sign, a separator or padding: "d3d:+0" and
            // "d3d:1x" name nothing rather than quietly meaning device 0 and device 1.
            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out deviceIndex) || deviceIndex < 0)
            {
                deviceIndex = 0;
                return false;
            }
        }

        switch (word.Trim().ToLowerInvariant())
        {
            case CpuValue:
                if (deviceIndex != 0)
                {
                    deviceIndex = 0;
                    return false;
                }

                kind = GpuAccelerationKind.Cpu;
                return true;

            case Direct3D12Value:
            case "d3d12":
            case "direct3d":
            case "direct3d12":
            case "dx12":
                kind = GpuAccelerationKind.Direct3D12;
                return true;

            case VulkanValue:
            case "vk":
                kind = GpuAccelerationKind.Vulkan;
                return true;

            case OpenGlValue:
            case "gl":
            case "opengl":
            case "wgl":
                kind = GpuAccelerationKind.OpenGl;
                return true;

            case MetalValue:
            case "mtl":
                kind = GpuAccelerationKind.Metal;
                return true;

            default:
                deviceIndex = 0;
                return false;
        }
    }
}
