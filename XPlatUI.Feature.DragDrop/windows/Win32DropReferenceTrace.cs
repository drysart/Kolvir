using System.Runtime.InteropServices;

namespace XPlatUI.DragDrop;

/// <summary>
/// One live reference count read out of the data object the shell handed a drop target.
/// </summary>
/// <param name="Event">Which OLE entry point took the sample: <c>DragEnter</c>, <c>DragOver</c> or <c>Drop</c>.</param>
/// <param name="Count">The object's reference count at that moment.</param>
/// <param name="DataObject">The borrowed <c>IDataObject</c> pointer the count belongs to.</param>
internal readonly record struct Win32DropReferenceSample(string Event, int Count, IntPtr DataObject);

/// <summary>
/// Samples the live reference count of the borrowed <c>IDataObject</c> the shell passes to
/// <c>IDropTarget</c>, so a lifetime defect can be seen rather than inferred.
/// </summary>
/// <remarks>
/// <para>
/// The count is the only in-process signal that can distinguish "the wrapper released a reference it
/// took" from "the wrapper released a reference it borrowed". It is read with a net-zero pair:
/// <see cref="Marshal.AddRef(IntPtr)"/> returns the count after incrementing, and
/// <see cref="Marshal.Release(IntPtr)"/> immediately takes it back, so the sampler leaves the object
/// exactly as it found it and cannot perturb what it measures. Both are raw <c>IUnknown</c> vtable
/// calls rather than RCW bookkeeping, which is why they work on a pointer the runtime does not own
/// and why they survive NativeAOT.
/// </para>
/// <para>
/// This is a diagnostic, not an API: the type is internal, no application can name it, and the demo
/// reaches it through <c>InternalsVisibleTo</c>. Writing to standard error is gated on
/// <c>XPLATUI_DRAG_TRACE=1</c>, the same switch shape as the GL seam's <c>XPLATUI_GL_TRACE</c>.
/// </para>
/// </remarks>
internal static class Win32DropReferenceTrace
{
    /// <summary>The name of the OLE entry point a sample was taken in.</summary>
    internal const string EnterEvent = "DragEnter";

    /// <summary>The name of the OLE entry point a sample was taken in.</summary>
    internal const string OverEvent = "DragOver";

    /// <summary>The name of the OLE entry point a sample was taken in.</summary>
    internal const string DropEvent = "Drop";

    private static readonly bool TraceEnabled =
        Environment.GetEnvironmentVariable("XPLATUI_DRAG_TRACE") is "1" or "true" or "TRUE";

    /// <summary>Raised for every sample, on the thread making the OLE call.</summary>
    internal static event Action<Win32DropReferenceSample>? Sampled;

    /// <summary>The most recent sample, or <see langword="null"/> before the first drag.</summary>
    internal static Win32DropReferenceSample? Last { get; private set; }

    /// <summary>
    /// Samples <paramref name="dataObject"/> and publishes the result.
    /// </summary>
    /// <param name="eventName">The OLE entry point taking the sample.</param>
    /// <param name="dataObject">The borrowed pointer, or <see cref="IntPtr.Zero"/> when there is none.</param>
    internal static void Record(string eventName, IntPtr dataObject)
    {
        if (dataObject == IntPtr.Zero)
        {
            return;
        }

        var sample = new Win32DropReferenceSample(eventName, Sample(dataObject), dataObject);
        Last = sample;

        if (TraceEnabled)
        {
            Console.Error.WriteLine(
                $"[drag] {eventName,-9} {dataObject:X} refcount {sample.Count}");
        }

        Sampled?.Invoke(sample);
    }

    /// <summary>
    /// Reads the reference count without changing it: <c>AddRef</c> reports the count it has just
    /// produced, and the matching <c>Release</c> puts the object back where it was.
    /// </summary>
    /// <param name="dataObject">The COM interface pointer to sample.</param>
    /// <returns>The count the object had before the pair ran.</returns>
    internal static int Sample(IntPtr dataObject)
    {
        var count = Marshal.AddRef(dataObject);
        Marshal.Release(dataObject);
        return count;
    }
}
