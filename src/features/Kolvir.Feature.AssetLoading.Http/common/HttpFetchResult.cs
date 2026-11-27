using System.Runtime.ExceptionServices;

namespace Kolvir.Feature.AssetLoading.Http;

/// <summary>
/// What one fetch produced: the encoded bytes, or the reason there are none.
/// </summary>
/// <remarks>
/// <para>
/// <b>It never holds an <see cref="ImageData"/>.</b> The bytes are the shared thing under the handles
/// and the decoded value is not: each handle calls the application's
/// <see cref="IImageDataFactory"/> with these bytes and owns the image it gets back, which is what
/// makes it legal for the manager to dispose a result nobody is left to receive
/// (dev-docs/ASSET-LOADING.md section 4.1). The type says so by having no field a decoded value could
/// be stored in.
/// </para>
/// <para>
/// <b>The bytes are transport, not state.</b> <see cref="Release"/> drops the array once the last
/// handle over this fetch has decoded it. A future cache is the component that decides to keep bytes,
/// and it must copy what it keeps rather than relying on this object to leave a buffer lying around -
/// which is why dropping them here is unconditional rather than something the loader can be asked to
/// turn off.
/// </para>
/// </remarks>
internal sealed class HttpFetchResult
{
    private readonly ExceptionDispatchInfo? _error;
    private byte[]? _bytes;

    private HttpFetchResult(byte[]? bytes, ExceptionDispatchInfo? error)
    {
        _bytes = bytes;
        _error = error;
    }

    /// <summary>The encoded bytes, while at least one handle still has to decode them.</summary>
    public byte[]? Bytes => _bytes;

    /// <summary>A result that produced bytes.</summary>
    /// <param name="bytes">The encoded image, which this object owns until it is released.</param>
    /// <returns>The result.</returns>
    public static HttpFetchResult Succeeded(byte[] bytes) => new(bytes, null);

    /// <summary>A result that produced a reason instead of bytes.</summary>
    /// <param name="error">What the fetch threw.</param>
    /// <returns>The result.</returns>
    public static HttpFetchResult Failed(Exception error) => new(null, ExceptionDispatchInfo.Capture(error));

    /// <summary>Throws the failure the fetch recorded, with its original stack trace.</summary>
    /// <exception cref="Exception">The recorded failure, when there is one.</exception>
    public void ThrowIfFailed() => _error?.Throw();

    /// <summary>
    /// Drops the encoded bytes, and returns how many were dropped so the factory can stop counting
    /// them.
    /// </summary>
    /// <returns>The number of bytes released, or zero when there were none left.</returns>
    public int Release()
    {
        byte[]? bytes = _bytes;
        _bytes = null;

        return bytes?.Length ?? 0;
    }
}
