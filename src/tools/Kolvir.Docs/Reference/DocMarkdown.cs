using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Kolvir.Docs.Reference;

/// <summary>
/// Turns a documentation comment into the Markdown a page shows.
/// </summary>
/// <remarks>
/// This converts one paragraph of prose into one paragraph of Markdown; it is not a Markdown
/// processor, and the site generator still does that work. Only the elements the library's comments
/// actually use are understood, and anything else contributes its text.
/// </remarks>
internal static partial class DocMarkdown
{
    /// <summary>Converts an element's contents to Markdown.</summary>
    /// <param name="element">The element, such as a <c>summary</c>, or null.</param>
    /// <param name="cref">Resolves a <c>cref</c> attribute into display text or a link.</param>
    /// <returns>One or more paragraphs of Markdown, or an empty string.</returns>
    internal static string ToMarkdown(XElement? element, Func<string, string> cref)
    {
        if (element is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        WriteNodes(builder, element.Nodes(), cref);

        return Tidy(builder.ToString());
    }

    private static void WriteNodes(StringBuilder builder, IEnumerable<XNode> nodes, Func<string, string> cref)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case XText text:
                    builder.Append(Collapse(text.Value));
                    break;

                case XElement element:
                    WriteElement(builder, element, cref);
                    break;
            }
        }
    }

    private static void WriteElement(StringBuilder builder, XElement element, Func<string, string> cref)
    {
        switch (element.Name.LocalName)
        {
            case "para":
                builder.Append("\n\n");
                WriteNodes(builder, element.Nodes(), cref);
                builder.Append("\n\n");
                break;

            case "c":
                builder.Append('`').Append(element.Value.Trim()).Append('`');
                break;

            case "code":
                builder.Append("\n\n```\n").Append(element.Value.Trim('\r', '\n')).Append("\n```\n\n");
                break;

            case "see":
            case "seealso":
                if (element.Attribute("cref")?.Value is { } reference)
                {
                    builder.Append(cref(reference));
                }
                else if (element.Attribute("langword")?.Value is { } word)
                {
                    builder.Append('`').Append(word).Append('`');
                }
                else if (element.Attribute("href")?.Value is { } href)
                {
                    builder.Append(Display(element.Value, href));
                }
                else
                {
                    WriteNodes(builder, element.Nodes(), cref);
                }

                break;

            case "paramref":
            case "typeparamref":
                builder.Append('`').Append(element.Attribute("name")?.Value ?? element.Value).Append('`');
                break;

            case "b":
            case "strong":
                builder.Append("**");
                WriteNodes(builder, element.Nodes(), cref);
                builder.Append("**");
                break;

            case "i":
            case "em":
                builder.Append('*');
                WriteNodes(builder, element.Nodes(), cref);
                builder.Append('*');
                break;

            case "br":
                builder.Append('\n');
                break;

            case "list":
                WriteNodes(builder, element.Nodes(), cref);
                builder.Append('\n');
                break;

            case "item":
                builder.Append("- ");
                WriteNodes(builder, element.Nodes(), cref);
                builder.Append('\n');
                break;

            case "term":
                WriteNodes(builder, element.Nodes(), cref);
                builder.Append(": ");
                break;

            case "inheritdoc":
            case "param":
            case "returns":
            case "exception":
            case "summary":
            case "remarks":
                break;

            default:
                WriteNodes(builder, element.Nodes(), cref);
                break;
        }
    }

    private static string Display(string value, string href) =>
        string.IsNullOrWhiteSpace(value) ? href : $"[{value.Trim()}]({href})";

    private static string Collapse(string text) => Whitespace().Replace(text, " ");

    private static string Tidy(string text)
    {
        string collapsed = ManyNewlines().Replace(text, "\n\n");
        string[] lines = collapsed.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            lines[index] = lines[index].TrimEnd();
        }

        string joined = string.Join('\n', lines);
        while (joined.Contains("\n\n\n", StringComparison.Ordinal))
        {
            joined = joined.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        }

        return joined.Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\n\s*\n\s*\n+")]
    private static partial Regex ManyNewlines();
}
