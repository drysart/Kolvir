namespace Kolvir;

/// <summary>
/// The failure a font load reports: the data is not a font this build can read, or the provider that
/// claimed it could not decode it.
/// </summary>
/// <remarks>
/// <para>
/// It derives from <see cref="ArgumentException"/> because that is what
/// <see cref="Font.FromBytes"/> has always thrown for bytes it cannot read, and a caller that
/// catches the old type keeps working. What it adds is the provider's name, which is the one part of
/// the failure a caller cannot work out for themselves.
/// </para>
/// <para>
/// It is thrown from a load, not from a paint: the caller asked for the font, so the caller catches
/// it. Environmental failures are the file system's own exceptions - a missing file is not a
/// format failure.
/// </para>
/// </remarks>
public sealed class FontFormatException : ArgumentException
{
    /// <summary>Creates the failure a load reports.</summary>
    /// <param name="providerName">The provider that claimed the data, or null when none did.</param>
    /// <param name="paramName">The parameter the caller passed the unusable data in.</param>
    /// <param name="inner">What the provider threw, or null when it returned nothing instead.</param>
    /// <remarks>
    /// The message says what went wrong rather than repeating <paramref name="paramName"/>, because
    /// the parameter's name is already on the exception and what a developer needs is the format.
    /// </remarks>
    public FontFormatException(string? providerName, string paramName, Exception? inner = null)
        : base(
            string.IsNullOrEmpty(providerName)
                ? inner is null
                    ? "The data is not a font this build can read."
                    : $"The data is not a font this build can read: {inner.Message}"
                : inner is null
                    ? $"The font format provider '{providerName}' claimed the data but produced nothing readable."
                    : $"The font format provider '{providerName}' claimed the data but could not decode it: {inner.Message}",
            paramName,
            inner)
    {
        ProviderName = providerName ?? string.Empty;
    }

    /// <summary>
    /// The provider that claimed the data, or an empty string when no provider claimed it.
    /// </summary>
    /// <remarks>
    /// An empty name means the built-in loader was the only thing that could have read the data, so
    /// "which format was this supposed to be" is a question for the caller. A name means a provider
    /// recognised the container and then failed, which is either a damaged file or a bug in that
    /// provider - never a format the application has not enabled.
    /// </remarks>
    public string ProviderName { get; } = string.Empty;
}
