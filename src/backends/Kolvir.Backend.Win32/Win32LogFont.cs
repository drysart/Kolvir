namespace Kolvir.Backend.Win32;

/// <summary>
/// The <c>LOGFONTW</c> helpers two features share: the font dialog fills and reads the face-name
/// buffer, and the composition path fills it from the focused control's font so the input method's own
/// window matches the field it sits over.
/// </summary>
/// <remarks>
/// The buffer is <c>WCHAR lfFaceName[LF_FACESIZE]</c> stored in place, which is why these take a
/// pointer: it keeps the struct blittable, which matters under NativeAOT.
/// </remarks>
internal static class Win32LogFont
{
    /// <summary>Copies a family name into the fixed face-name buffer, truncating at the native limit.</summary>
    internal static unsafe void SetFaceName(NativeMethods.LOGFONTW* logFont, string? family)
    {
        if (string.IsNullOrEmpty(family))
        {
            return;
        }

        var length = Math.Min(family.Length, 31);
        for (var index = 0; index < length; index++)
        {
            logFont->lfFaceName[index] = family[index];
        }
    }

    /// <summary>Reads the NUL-terminated face name out of the fixed buffer.</summary>
    internal static unsafe string FaceNameOf(NativeMethods.LOGFONTW* logFont)
    {
        var length = 0;
        while (length < 32 && logFont->lfFaceName[length] != '\0')
        {
            length++;
        }

        return length == 0 ? string.Empty : new string(logFont->lfFaceName, 0, length);
    }
}
