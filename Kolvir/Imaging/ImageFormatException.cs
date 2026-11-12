namespace Kolvir;

/// <summary>
/// The failure an image load reports: no provider recognised the bytes, the provider that claimed
/// them could not decode them, or an unseekable stream was too large to buffer.
/// </summary>
/// <remarks>
/// <para>
/// It is thrown from a factory call, not from a paint: the caller asked for the image, so the caller
/// catches it. There is no control-side reporting path any more, because the control does not decode
/// anything.
/// </para>
/// <para>
/// This is not the failure for bytes that are not an image at all: that one still ends a load
/// without a provider named, exactly as it did before providers existed.
/// </para>
/// </remarks>
public sealed class ImageFormatException : Exception
{
    /// <summary>Creates the failure a provider's load reports.</summary>
    /// <param name="providerName">The provider that claimed the bytes, or null or empty when none did.</param>
    /// <param name="inner">What the provider threw, or null when it returned nothing instead.</param>
    public ImageFormatException(string providerName, Exception? inner = null)
        : base(BuildMessage(providerName, inner), inner)
    {
        ProviderName = providerName ?? string.Empty;
    }

    private ImageFormatException(string message)
        : base(message)
    {
    }

    private static string BuildMessage(string? providerName, Exception? inner) =>
        string.IsNullOrEmpty(providerName)
            ? inner is null
                ? "No image format provider recognised the stream."
                : $"No image format provider could read the stream: {inner.Message}"
            : inner is null
                ? $"The image format provider '{providerName}' recognised the stream but could not decode it."
                : $"The image format provider '{providerName}' failed to decode the stream: {inner.Message}";

    /// <summary>
    /// The provider that claimed the bytes, or an empty string when no provider claimed them.
    /// </summary>
    public string ProviderName { get; } = string.Empty;

    /// <summary>The failure for bytes that no registered provider recognised.</summary>
    public static ImageFormatException Unrecognised() =>
        new("No image format provider recognised the stream.");

    /// <summary>
    /// The failure for options that arrived with bytes nobody claimed, so nothing was configured.
    /// </summary>
    /// <param name="optionsType">The type the caller passed.</param>
    public static ImageFormatException OptionsNotUnderstood(Type optionsType) =>
        new(string.Empty, new ArgumentException(
            $"No image format provider claimed these bytes, so {optionsType.Name} could not be applied.",
            "options"));

    /// <summary>The failure for an unseekable stream larger than the factory will buffer.</summary>
    /// <param name="limit">The limit that was exceeded, in bytes.</param>
    public static ImageFormatException TooLargeToBuffer(long limit) =>
        new($"The stream cannot seek and is larger than the {limit} byte limit for buffering it. Copy it to a file or a MemoryStream first.");
}
