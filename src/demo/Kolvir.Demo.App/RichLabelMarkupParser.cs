using System.Text;

namespace Kolvir.Demo;

/// <summary>One run of the markup page's preview: its text, and the styles in force where it appears.</summary>
/// <param name="Text">The characters the run carries, with escapes already resolved.</param>
/// <param name="Bold">Whether the run is inside a <c>&lt;b&gt;</c>.</param>
/// <param name="Italic">Whether the run is inside an <c>&lt;i&gt;</c>.</param>
/// <param name="Underline">Whether the run is inside a <c>&lt;u&gt;</c>.</param>
/// <remarks>
/// A run is what a <see cref="TextSpan"/> can carry: text, and the three styles the label derives from
/// its own font. Colour is the label's, not a span's, so the grammar has no tag for it. This type is
/// deliberately not a <c>VTextSpan</c>: the parser describes what the text says, and the page decides
/// which node class says it.
/// </remarks>
internal readonly record struct MarkupSpan(string Text, bool Bold, bool Italic, bool Underline);

/// <summary>
/// Turns the markup typed into the demo's text box into the runs a rich label is made of.
/// </summary>
/// <remarks>
/// <para>
/// The grammar is deliberately small, and every run of it maps onto something a <see cref="TextSpan"/>
/// can express:
/// </para>
/// <list type="bullet">
/// <item><description><c>&lt;b&gt;</c>, <c>&lt;i&gt;</c> and <c>&lt;u&gt;</c> add a style until their closing tag;</description></item>
/// <item><description>the three nest, and the styles combine, so <c>&lt;b&gt;a&lt;i&gt;b&lt;/i&gt;&lt;/b&gt;</c> is one bold run and one bold, italic run;</description></item>
/// <item><description><c>&amp;lt;</c> is the one escape: it stands for a literal <c>&lt;</c>;</description></item>
/// <item><description>an unclosed tag applies to the end of the text;</description></item>
/// <item><description>a stray closing tag, an unknown tag and a malformed one are all literal text.</description></item>
/// </list>
/// <para>
/// The function is total: it runs on every keystroke, over whatever is in the box, so no input can
/// make it throw. It makes one pass, a list, and - only if there is text - a string per run. Matching a
/// tag is a character comparison rather than a substring or a regular expression, which is what keeps a
/// long malformed input from allocating as it scans.
/// </para>
/// </remarks>
internal static class RichLabelMarkup
{
    /// <summary>Parses <paramref name="markup"/> into runs, in the order they should be laid out.</summary>
    /// <param name="markup">The text to parse; null and empty produce no runs.</param>
    /// <returns>
    /// The runs, in order, or an empty list. A run is never empty, so the list carries no run for a tag
    /// that sits between two others with no text around it.
    /// </returns>
    internal static IReadOnlyList<MarkupSpan> Parse(string? markup)
    {
        if (string.IsNullOrEmpty(markup))
        {
            return [];
        }

        var spans = new List<MarkupSpan>();
        var run = new StringBuilder(markup.Length);
        bool bold = false;
        bool italic = false;
        bool underline = false;

        // A tag that changes the styles ends the run that was in force before it, so the run that
        // ends and the one that begins carry the styles they were written under.
        void Flush()
        {
            if (run.Length == 0)
            {
                return;
            }

            spans.Add(new MarkupSpan(run.ToString(), bold, italic, underline));
            run.Clear();
        }

        bool InForce(char style) => style switch
        {
            'b' => bold,
            'i' => italic,
            _ => underline,
        };

        void Set(char style, bool value)
        {
            switch (style)
            {
                case 'b':
                    bold = value;
                    break;
                case 'i':
                    italic = value;
                    break;
                default:
                    underline = value;
                    break;
            }
        }

        for (int index = 0; index < markup.Length; index++)
        {
            var rest = markup.AsSpan(index);

            if (rest[0] == '<' && TryTag(rest, out char style, out bool closing, out int length))
            {
                // A closing tag with nothing to close is literal text, not an error: the box is being
                // typed into, and a half-typed pair spends a moment in exactly that state.
                if (closing && !InForce(style))
                {
                    run.Append(rest[..length]);
                }
                else
                {
                    Flush();
                    Set(style, !closing);
                }

                index += length - 1;
                continue;
            }

            if (rest[0] == '&' && rest.StartsWith("&lt;", StringComparison.Ordinal))
            {
                run.Append('<');
                index += 3;
                continue;
            }

            // Everything else - including a '<' or '&' that starts nothing recognised - is text.
            run.Append(rest[0]);
        }

        Flush();
        return spans;
    }

    /// <summary>Recognises one of the three tags at the start of <paramref name="rest"/>.</summary>
    /// <param name="rest">The unparsed text, starting at a <c>&lt;</c>.</param>
    /// <param name="style">The style the tag carries, or <c>'\0'</c> when there is no tag.</param>
    /// <param name="closing">Whether the tag closes a style rather than opening one.</param>
    /// <param name="length">How many characters the tag occupies, or zero when there is no tag.</param>
    /// <returns>True when <paramref name="rest"/> begins with a well-formed tag this parser knows.</returns>
    /// <remarks>
    /// Tags are lower case and take no arguments: <c>&lt;B&gt;</c>, <c>&lt;b &gt;</c> and
    /// <c>&lt;bold&gt;</c> are all unknown, and unknown tags are literal by design - the page shows
    /// what the user typed rather than silently eating it.
    /// </remarks>
    private static bool TryTag(ReadOnlySpan<char> rest, out char style, out bool closing, out int length)
    {
        if (rest.Length >= 3 && rest[2] == '>' && rest[1] is 'b' or 'i' or 'u')
        {
            style = rest[1];
            closing = false;
            length = 3;
            return true;
        }

        if (rest.Length >= 4 && rest[1] == '/' && rest[3] == '>' && rest[2] is 'b' or 'i' or 'u')
        {
            style = rest[2];
            closing = true;
            length = 4;
            return true;
        }

        style = '\0';
        closing = false;
        length = 0;
        return false;
    }
}
