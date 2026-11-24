using System.Text;

namespace Kolvir.Backend.Win32;

/// <summary>
/// What the window procedure reads out of the input method while a composition is live.
/// </summary>
/// <remarks>
/// <para>
/// Production is always <see cref="Win32ImeComposition.Instance"/>: the window backend's property is
/// internal, so no application can substitute a reader and the shipped path cannot be a fake. The
/// interface exists because Imm32 keeps the composition string inside the input method's own context,
/// and a machine with no IME installed has no input method to keep it: there,
/// <c>ImmGetProperty(IGP_SETCOMPSTR)</c> is zero, <c>ImmSetCompositionStringW</c> answers TRUE without
/// storing anything, and every <c>ImmGetCompositionStringW</c> read is zero bytes. The test assembly
/// publishes a synthesized composition through this seam so the real window, the real window procedure
/// and the real host still run; it is the same arrangement the backend already uses for
/// <c>IWin32GlProbe</c> and <c>IWin32D3D12Probe</c>.
/// </para>
/// <para>
/// The uncommitted text is <see cref="CompositionString"/>. The text a composition just committed is
/// <see cref="ResultString"/>, which is only non-empty on the message that carries the result.
/// </para>
/// </remarks>
internal interface IWin32ImeComposition
{
    /// <summary>The composition string, or empty when there is none.</summary>
    /// <param name="context">The window's input context.</param>
    string CompositionString(IntPtr context);

    /// <summary>The committed text of a composition, or empty when there is none.</summary>
    /// <param name="context">The window's input context.</param>
    string ResultString(IntPtr context);

    /// <summary>Where the input method's caret sits inside the composition string.</summary>
    /// <param name="context">The window's input context.</param>
    int CaretPosition(IntPtr context);
}

/// <summary>The production reader: <c>ImmGetCompositionStringW</c>, on the window's own context.</summary>
internal sealed class Win32ImeComposition : IWin32ImeComposition
{
    private Win32ImeComposition()
    {
    }

    internal static Win32ImeComposition Instance { get; } = new();

    public string CompositionString(IntPtr context) => Read(context, NativeMethods.GcsCompStr);

    public string ResultString(IntPtr context) => Read(context, NativeMethods.GcsResultStr);

    public int CaretPosition(IntPtr context)
    {
        if (context == IntPtr.Zero)
        {
            return 0;
        }

        // GCS_CURSORPOS answers the position itself rather than a string, so the sizing call is the
        // read. A negative answer is an IMM error, and zero is the only sane caret for one.
        var position = NativeMethods.ImmGetCompositionStringW(context, NativeMethods.GcsCursorPos, null, 0);
        return position < 0 ? 0 : position;
    }

    /// <summary>
    /// Reads a string out of the composition in the two calls IMM documents: the first sizes it with no
    /// buffer, the second fills one.
    /// </summary>
    private static string Read(IntPtr context, uint index)
    {
        if (context == IntPtr.Zero)
        {
            return string.Empty;
        }

        var size = NativeMethods.ImmGetCompositionStringW(context, index, null, 0);
        if (size <= 0)
        {
            return string.Empty;
        }

        var bytes = new byte[size];
        var read = NativeMethods.ImmGetCompositionStringW(context, index, bytes, (uint)size);
        if (read <= 0)
        {
            return string.Empty;
        }

        // The composition is UTF-16, and a return of an odd number of bytes would mean the input method
        // broke its own contract; trimming keeps that from throwing inside a window procedure.
        return Encoding.Unicode.GetString(bytes, 0, Math.Min(read, bytes.Length) & ~1);
    }
}
