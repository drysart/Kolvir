using System.Globalization;

namespace Kolvir;

/// <summary>
/// The machine-readable spelling of an acceleration choice: what <see cref="GpuAccelerationOption.Value"/>
/// holds, what an application saves when the user picks from a settings list, and what it passes back
/// through <see cref="ApplicationOptions.GfxAcceleration"/> at startup.
/// </summary>
/// <remarks>
/// <para>
/// A value is a family word followed by the identity of one device in it, or the family word alone when
/// the family has nothing to choose between: <c>d3d</c>, <c>d3d:10de-2504-1616</c>, <c>vulkan</c>,
/// <c>vulkan:8f4a37c2...</c>, <c>ogl</c>, <c>metal</c> and <c>cpu</c>.
/// </para>
/// <para>
/// The device part is an <b>identity, not a position</b>. An ordinal would be shorter, and it would also
/// be wrong: both graphics APIs enumerate their devices in an order they make no promise about, so a
/// number shifts when an eGPU is plugged in, an adapter is disabled, a laptop switches between its
/// integrated and discrete GPU, or a driver update reorders the list. A setting saved as "device 1"
/// would then quietly name a different card - and would still name <em>something</em>, so nothing would
/// report the mistake.
/// </para>
/// <para>
/// Neither the family word nor the identity is meant to be read by a person; the display form lives in
/// <see cref="GpuAccelerationOption.Name"/>. Each backend decides what its identity is, because what is
/// stable is a property of the platform: the Win32 backend uses the hardware identity its APIs report -
/// the adapter's vendor, device and subsystem ids for Direct3D 12, and the device UUID for Vulkan - each
/// of which survives reboots, driver updates and the GPU being moved, and which differs between two
/// physically identical cards. The identity is opaque above the backend: this type only checks that it
/// is a token that can be written, read and compared.
/// </para>
/// <para>
/// Reading is lenient about spelling - <c>d3d12</c>, <c>direct3d</c>, <c>gl</c>, <c>opengl</c> and
/// <c>mtl</c> are accepted as their families - while writing is strict, so what an application saves
/// stays in one canonical form.
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

    /// <summary>
    /// The longest device identity a value may carry. A Vulkan device UUID is 32 hex digits and a
    /// Direct3D 12 adapter identity is three groups of at most eight, so the limit only exists to reject
    /// a malformed value before it is stored.
    /// </summary>
    private const int MaxKeyLength = 40;

    /// <summary>The family word for <paramref name="kind"/>, without a device.</summary>
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
    /// <param name="deviceKey">
    /// The device's identity, as the family's backend reports it; null for a family with nothing to
    /// choose between.
    /// </param>
    public static string Format(GpuAccelerationKind kind, string? deviceKey = null)
    {
        var word = Word(kind);

        if (string.IsNullOrEmpty(deviceKey))
        {
            return word;
        }

        if (kind == GpuAccelerationKind.Cpu)
        {
            throw new ArgumentException("The CPU path has no devices to choose between.", nameof(deviceKey));
        }

        if (!IsDeviceKey(deviceKey))
        {
            throw new ArgumentException($"'{deviceKey}' is not a device identity.", nameof(deviceKey));
        }

        return string.Create(CultureInfo.InvariantCulture, $"{word}:{deviceKey.ToLowerInvariant()}");
    }

    /// <summary>Reads a value, with or without a device identity and in any of the accepted spellings.</summary>
    /// <param name="value">The value to read, or null when there is none.</param>
    /// <param name="kind">The family the value named.</param>
    /// <param name="deviceKey">
    /// The device identity the value carried, lowercased, or null when it carried none.
    /// </param>
    /// <returns>True when the value named a family; false when it named nothing or was malformed.</returns>
    public static bool TryParse(string? value, out GpuAccelerationKind kind, out string? deviceKey)
    {
        kind = GpuAccelerationKind.Cpu;
        deviceKey = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        var word = colon < 0 ? text : text[..colon];

        if (colon >= 0)
        {
            // The identity is taken verbatim: its characters are checked, not repaired, so that
            // "d3d: 10de" is rejected rather than read as a device nobody wrote.
            var key = text[(colon + 1)..];

            if (!IsDeviceKey(key))
            {
                return false;
            }

            deviceKey = key.ToLowerInvariant();
        }

        switch (word.Trim().ToLowerInvariant())
        {
            case CpuValue:
                if (deviceKey is not null)
                {
                    deviceKey = null;
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
                deviceKey = null;
                return false;
        }
    }

    /// <summary>
    /// True when <paramref name="key"/> can be a device identity: hex digits in groups separated by
    /// single dashes.
    /// </summary>
    /// <param name="key">The candidate identity.</param>
    /// <remarks>
    /// What the identity means is the backend's business, so this checks only what every backend can
    /// promise: one short token, none of the characters that would be read as the family's separator,
    /// and no whitespace - so that a value survives a config file, a command line and a round trip
    /// through a JSON blob.
    /// </remarks>
    public static bool IsDeviceKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength)
        {
            return false;
        }

        var previousWasDash = true;

        foreach (var character in key)
        {
            if (character == '-')
            {
                if (previousWasDash)
                {
                    return false;
                }

                previousWasDash = true;
                continue;
            }

            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }

            previousWasDash = false;
        }

        return !previousWasDash;
    }
}
