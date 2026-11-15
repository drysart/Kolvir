using System.Text;

namespace Kolvir;

/// <summary>
/// Reads the mnemonic marker out of menu text. <c>&amp;</c> marks the character after it as the
/// mnemonic and is not drawn, <c>&amp;&amp;</c> is a literal ampersand, and a trailing <c>&amp;</c>
/// is dropped. This is the Win32 convention, and it is what makes Alt+F reach a File menu and a
/// bare <c>V</c> reach View while a menu is open.
/// </summary>
internal static class MenuText
{
    /// <summary>Removes the marker and reports the character it marked, or <c>'\0'</c> for none.</summary>
    public static string Parse(string? text, out char mnemonic)
    {
        mnemonic = '\0';

        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c != '&')
            {
                builder.Append(c);
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '&')
            {
                builder.Append('&');
                i++;
                continue;
            }

            if (i + 1 < text.Length && mnemonic == '\0')
            {
                mnemonic = char.ToUpperInvariant(text[i + 1]);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// The letter or digit <paramref name="key"/> stands for, or null for a key that cannot be a
    /// mnemonic. <see cref="Key"/> stops at the digits, so the punctuation mnemonics a native
    /// menu accepts are out of reach on every platform alike.
    /// </summary>
    public static char? KeyToMnemonic(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return (char)('A' + (key - Key.A));
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return (char)('0' + (key - Key.D0));
        }

        return null;
    }
}

/// <summary>
/// Drops the separators that would look wrong: leading, trailing, and doubled. Every menu - the
/// context menu, a drop-down, a submenu - is trimmed this way before it is laid out, so a menu never
/// starts or ends on a separator line and never shows two in a row where the middle group was empty.
/// </summary>
internal static class MenuTrimming
{
    public static IReadOnlyList<MenuItem> Trim(IReadOnlyList<MenuItem> items)
    {
        var trimmed = new List<MenuItem>(items.Count);

        foreach (var item in items)
        {
            if (item.IsSeparator && (trimmed.Count == 0 || trimmed[^1].IsSeparator))
            {
                continue;
            }

            trimmed.Add(item);
        }

        while (trimmed.Count > 0 && trimmed[^1].IsSeparator)
        {
            trimmed.RemoveAt(trimmed.Count - 1);
        }

        return trimmed;
    }
}
