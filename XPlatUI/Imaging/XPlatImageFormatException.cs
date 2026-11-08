namespace XPlatUI;

/// <summary>
/// Raised through <see cref="XPlatImage.AddLoadFailedHandler"/> when a provider claimed a stream and
/// then could not decode it.
/// </summary>
/// <remarks>
/// This is not raised when nothing recognises the bytes: an unknown stream is a false return with no
/// event, exactly as it was before providers existed. It is raised when the format was recognised,
/// which makes it a real failure - a truncated file, a broken vector document, or a document the
/// provider refused on purpose.
/// </remarks>
public sealed class XPlatImageFormatException : Exception
{
    /// <summary>Creates the failure a provider's load reports.</summary>
    /// <param name="providerName">The provider that claimed the bytes.</param>
    /// <param name="inner">What the provider threw, or null when it declined the stream.</param>
    public XPlatImageFormatException(string providerName, Exception? inner = null)
        : base(inner is null
            ? $"The image format provider '{providerName}' recognised the stream but could not decode it."
            : $"The image format provider '{providerName}' failed to decode the stream: {inner.Message}",
              inner)
    {
        ProviderName = providerName;
    }

    /// <summary>The provider that claimed the bytes.</summary>
    public string ProviderName { get; }
}
