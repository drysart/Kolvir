using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Kolvir.Feature.VirtualDom.Generators;

/// <summary>
/// The documentation of the referenced assemblies, indexed by assembly name, so that a node property
/// can carry the documentation of the control property it exposes.
/// </summary>
/// <remarks>
/// <para>
/// A generator sees the library's controls as metadata, and a metadata symbol has no documentation of
/// its own: Roslyn attaches a documentation provider to a reference only when the host supplies one,
/// and the compiler does not. Measured in a real <c>dotnet build</c>: the XML file sitting next to
/// the referenced assembly, and documentation generation turned on in the consuming project, both
/// made no difference - <c>GetDocumentationCommentXml()</c> returned nothing. So the XML files are
/// handed in as additional files by the feature's own buildTransitive target, and this reads them.
/// </para>
/// <para>
/// A symbol declared in the compilation being built does have documentation, and that is used first:
/// it is the same text, already resolved, and needs no index.
/// </para>
/// </remarks>
internal sealed class DocumentationIndex
{
    private readonly ImmutableDictionary<string, DocumentationFile> _files;

    private DocumentationIndex(ImmutableDictionary<string, DocumentationFile> files) => _files = files;

    /// <summary>No documentation is available, which is what a build with no additional files gets.</summary>
    internal static DocumentationIndex Empty { get; } = new(ImmutableDictionary<string, DocumentationFile>.Empty);

    /// <summary>
    /// The documentation of a member, as the body of a doc comment, or null when there is none to be
    /// had or none of it belongs on a property.
    /// </summary>
    internal string? Member(ISymbol symbol)
    {
        if (DocumentationCleaner.Clean(symbol.GetDocumentationCommentXml()) is { } source)
        {
            return source;
        }

        if (symbol.GetDocumentationCommentId() is not { } id
            || symbol.ContainingAssembly?.Name is not { } assembly
            || !_files.TryGetValue(assembly, out var file)
            || file.Member(id) is not { } xml)
        {
            return null;
        }

        return DocumentationCleaner.Clean(xml);
    }

    /// <summary>
    /// The documentation of a member of a referenced assembly, by assembly name and documentation ID,
    /// as the body of a doc comment; null when the assembly's file has nothing for it.
    /// </summary>
    internal string? MemberOf(string assembly, string id) =>
        _files.TryGetValue(assembly, out var file) && file.Member(id) is { } xml
            ? DocumentationCleaner.Clean(xml)
            : null;

    /// <summary>Builds the index from the additional files the build handed in.</summary>
    /// <remarks>
    /// Every XML file is a candidate, tested by name only: reading a file to find out whether it is
    /// documentation would defeat the point of not reading it. Nothing else in the file is touched
    /// until a member is asked for.
    /// </remarks>
    internal static DocumentationIndex Create(ImmutableArray<AdditionalText> texts)
    {
        var files = ImmutableDictionary.CreateBuilder<string, DocumentationFile>(StringComparer.Ordinal);

        foreach (var text in texts)
        {
            if (DocumentationFile.Create(text) is { } file && !files.ContainsKey(file.Assembly))
            {
                files.Add(file.Assembly, file);
            }
        }

        return files.Count == 0 ? Empty : new DocumentationIndex(files.ToImmutable());
    }
}

/// <summary>One assembly's documentation file, parsed the first time any of it is asked for.</summary>
/// <remarks>
/// Deferred because the build hands in the documentation of <em>every</em> reference, and only the
/// few that declare a control or an extension member are ever asked about; the rest cost a filename
/// comparison and nothing else, which is what keeps Roslyn's own several megabytes of documentation
/// from being parsed on every build.
/// </remarks>
internal sealed class DocumentationFile
{
    private const string MemberStart = "<member name=\"";
    private const string MemberEnd = "</member>";

    private readonly AdditionalText _text;
    private readonly Lazy<ImmutableDictionary<string, string>> _members;

    private DocumentationFile(string assembly, AdditionalText text)
    {
        Assembly = assembly;
        _text = text;
        _members = new Lazy<ImmutableDictionary<string, string>>(Read, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The assembly the file documents, which is the file's own name by convention.</summary>
    internal string Assembly { get; }

    /// <summary>The XML inside the <c>&lt;member&gt;</c> element for <paramref name="id"/>, or null.</summary>
    internal string? Member(string id) => _members.Value.TryGetValue(id, out var xml) ? xml : null;

    internal static DocumentationFile? Create(AdditionalText text) =>
        string.Equals(Path.GetExtension(text.Path), ".xml", StringComparison.OrdinalIgnoreCase)
            ? new DocumentationFile(Path.GetFileNameWithoutExtension(text.Path), text)
            : null;

    /// <summary>
    /// Reads the member elements out of the file.
    /// </summary>
    /// <remarks>
    /// A scan rather than an XML parse: the file is one <c>&lt;member&gt;</c> per line with no nesting
    /// between them, and the text is kept verbatim - whatever entities, whitespace and line breaks the
    /// documentation has, they are copied through untouched, which is also what keeps the copy
    /// well-formed.
    /// </remarks>
    private ImmutableDictionary<string, string> Read()
    {
        string? content = _text.GetText()?.ToString();

        if (content is null)
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        var members = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        int position = 0;

        while (position < content.Length)
        {
            int open = content.IndexOf(MemberStart, position, StringComparison.Ordinal);

            if (open < 0)
            {
                break;
            }

            int nameStart = open + MemberStart.Length;
            int nameEnd = content.IndexOf('"', nameStart);

            if (nameEnd < 0)
            {
                break;
            }

            int bodyStart = content.IndexOf('>', nameEnd);

            if (bodyStart < 0)
            {
                break;
            }

            int close = content.IndexOf(MemberEnd, bodyStart, StringComparison.Ordinal);

            if (close < 0)
            {
                break;
            }

            var id = content.Substring(nameStart, nameEnd - nameStart);
            var body = content.Substring(bodyStart + 1, close - bodyStart - 1);

            if (!members.ContainsKey(id))
            {
                members.Add(id, body);
            }

            position = close + MemberEnd.Length;
        }

        return members.ToImmutable();
    }
}

/// <summary>
/// Turns the documentation of one member into something a generated node property can carry: the
/// elements that are valid on a property, with every link reduced to text.
/// </summary>
/// <remarks>
/// <para>
/// This is where the feature could introduce a build warning, so it is deliberately conservative:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// Only <c>summary</c>, <c>remarks</c>, <c>value</c> and <c>example</c> survive. The rest belong to
/// the member the text was written for - <c>param</c>, <c>typeparam</c> and <c>returns</c> do not
/// describe a property, and the compiler reports them there (CS1572, CS1573).
/// </description>
/// </item>
/// <item>
/// <description>
/// Every <c>cref</c> becomes <c>&lt;c&gt;name&lt;/c&gt;</c>, keeping the name and losing the link. A
/// documentation ID (<c>P:Kolvir.Control.Text</c>) is not valid source cref syntax, so it cannot be
/// copied; resolving it back to a symbol and writing a source cref risks CS1574 in the consumer's
/// compilation, which is exactly the failure this must not ship, and a source cref from the control's
/// own file is relative to <em>that</em> file's namespace and can be just as unresolvable here. The
/// text is what a reader needs; the link is not worth a warning in somebody else's build.
/// </description>
/// </item>
/// <item>
/// <description>
/// <c>paramref</c> and <c>typeparamref</c> become <c>&lt;c&gt;</c> for the same reason: the property
/// they would name does not exist (CS1734).
/// </description>
/// </item>
/// </list>
/// <para>
/// Everything kept is copied verbatim, so entities and markup are exactly as documented and no
/// escaping decision is made here at all.
/// </para>
/// </remarks>
internal static class DocumentationCleaner
{
    private static readonly string[] TopLevel =
        ["summary", "remarks", "value", "example"];

    /// <summary>The doc-comment body for <paramref name="xml"/>, or null when nothing belongs on a property.</summary>
    internal static string? Clean(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        var body = Body(xml!);
        var builder = new StringBuilder(body.Length);
        int position = 0;

        while (position < body.Length)
        {
            int open = body.IndexOf('<', position);

            if (open < 0)
            {
                break;
            }

            if (IsComment(body, open, out int commentEnd))
            {
                position = commentEnd;
                continue;
            }

            var tag = Tag(body, open);

            if (tag is not { } found)
            {
                break;
            }

            int close = body.IndexOf('>', open);

            if (close < 0)
            {
                break;
            }

            int end;
            if (found.Closing)
            {
                // A close tag with nothing open here belongs to the wrapper the body was cut out of.
                end = close + 1;
            }
            else if (Array.IndexOf(TopLevel, found.Name) >= 0)
            {
                end = Copy(body, found, open, close, builder);
            }
            else
            {
                end = Skip(body, found, close);
            }

            if (end < 0)
            {
                break;
            }

            position = end;
        }

        return builder.Length == 0 ? null : Tidy(builder.ToString());
    }

    /// <summary>The member element's contents, or the whole text when it is already just the body.</summary>
    private static string Body(string xml)
    {
        int open = xml.IndexOf('>');

        if (open >= 0 && xml.TrimStart().StartsWith("<member", StringComparison.Ordinal))
        {
            int close = xml.LastIndexOf("</member>", StringComparison.Ordinal);

            if (close > open)
            {
                return xml.Substring(open + 1, close - open - 1);
            }
        }

        return xml;
    }

    /// <summary>
    /// Copies one kept element, rewriting the tags inside it that cannot survive, and answers where it
    /// ends.
    /// </summary>
    private static int Copy(string body, TagSpec tag, int open, int close, StringBuilder builder)
    {
        int start = builder.Length;

        if (tag.SelfClosing)
        {
            builder.Append(body, open, close - open + 1);
            return close + 1;
        }

        builder.Append(body, open, close - open + 1);

        int position = close + 1;
        int text = position;
        int depth = 1;

        while (position < body.Length)
        {
            int next = body.IndexOf('<', position);

            if (next < 0)
            {
                break;
            }

            builder.Append(body, text, next - text);

            if (IsComment(body, next, out int commentEnd))
            {
                builder.Append(body, next, commentEnd - next);
                position = text = commentEnd;
                continue;
            }

            var inner = Tag(body, next);
            int innerClose = body.IndexOf('>', next);

            if (inner is not { } found || innerClose < 0)
            {
                break;
            }

            if (found.Name == tag.Name)
            {
                if (found.Closing)
                {
                    depth--;
                }
                else if (!found.SelfClosing)
                {
                    depth++;
                }

                builder.Append(body, next, innerClose - next + 1);
                position = text = innerClose + 1;

                if (depth == 0)
                {
                    return position;
                }

                continue;
            }

            if (Rewrite(body, found, next, innerClose, builder, out int after))
            {
                position = text = after;
                continue;
            }

            builder.Append(body, next, innerClose - next + 1);
            position = text = innerClose + 1;
        }

        // A kept element that never closed cannot be made well-formed by guessing; drop it.
        builder.Length = start;
        return -1;
    }

    /// <summary>
    /// Writes the replacement for a tag that is not kept as it is, and answers whether it handled it
    /// and where the element ends.
    /// </summary>
    private static bool Rewrite(
        string body,
        TagSpec tag,
        int open,
        int close,
        StringBuilder builder,
        out int after)
    {
        switch (tag.Name)
        {
            case "see":
            case "seealso":
            {
                if (Attribute(body, open, close, "cref") is { } cref)
                {
                    Text(builder, DisplayName(cref));
                }
                else if (Attribute(body, open, close, "langword") is { } word)
                {
                    // A langword names a keyword rather than a declaration, so it is valid on its own.
                    builder.Append("<c>").Append(word).Append("</c>");
                }
                else
                {
                    Text(builder, tag.Name);
                }

                after = tag.SelfClosing ? close + 1 : Closing(body, tag.Name, close + 1);
                return after >= 0;
            }

            case "paramref":
            case "typeparamref":
            {
                Text(builder, Attribute(body, open, close, "name") ?? tag.Name);
                after = close + 1;
                return true;
            }

            case "inheritdoc":
            case "include":
                after = tag.SelfClosing ? close + 1 : Closing(body, tag.Name, close + 1);
                return after >= 0;

            default:
                after = -1;
                return false;
        }
    }

    private static void Text(StringBuilder builder, string value) =>
        builder.Append("<c>").Append(value).Append("</c>");

    /// <summary>Answers where an element that is not kept ends.</summary>
    private static int Skip(string body, TagSpec tag, int close)
    {
        if (tag.SelfClosing)
        {
            return close + 1;
        }

        int depth = 1;
        int position = close + 1;

        while (position < body.Length)
        {
            int next = body.IndexOf('<', position);

            if (next < 0)
            {
                return -1;
            }

            if (IsComment(body, next, out int commentEnd))
            {
                position = commentEnd;
                continue;
            }

            var inner = Tag(body, next);
            int innerClose = body.IndexOf('>', next);

            if (inner is not { } found || innerClose < 0)
            {
                return -1;
            }

            if (found.Name == tag.Name)
            {
                if (found.Closing)
                {
                    depth--;

                    if (depth == 0)
                    {
                        return innerClose + 1;
                    }
                }
                else if (!found.SelfClosing)
                {
                    depth++;
                }
            }

            position = innerClose + 1;
        }

        return -1;
    }

    private static int Closing(string body, string name, int from)
    {
        var end = "</" + name + ">";
        int index = body.IndexOf(end, from, StringComparison.Ordinal);
        return index < 0 ? -1 : index + end.Length;
    }

    private static bool IsComment(string body, int open, out int end)
    {
        end = -1;

        if (string.CompareOrdinal(body, open, "<!--", 0, 4) != 0)
        {
            return false;
        }

        int close = body.IndexOf("-->", open, StringComparison.Ordinal);
        end = close < 0 ? body.Length : close + 3;
        return true;
    }

    /// <summary>The tag that starts at <paramref name="open"/>, or null when it is not a tag.</summary>
    private static TagSpec? Tag(string body, int open)
    {
        int position = open + 1;

        if (position >= body.Length)
        {
            return null;
        }

        bool closing = body[position] == '/';

        if (closing)
        {
            position++;
        }
        else if (body[position] is '!' or '?')
        {
            return null;
        }

        int start = position;

        while (position < body.Length && (char.IsLetterOrDigit(body[position]) || body[position] is '_' or '-' or ':' or '.'))
        {
            position++;
        }

        if (position == start)
        {
            return null;
        }

        var name = body.Substring(start, position - start);
        int close = body.IndexOf('>', position);

        if (close < 0)
        {
            return null;
        }

        bool selfClosing = body[close - 1] == '/';
        return new TagSpec(name, closing, selfClosing);
    }

    /// <summary>The value of an attribute in the tag between <paramref name="open"/> and <paramref name="close"/>.</summary>
    private static string? Attribute(string body, int open, int close, string name)
    {
        int position = open;

        while (true)
        {
            int found = body.IndexOf(name + "=", position, StringComparison.Ordinal);

            if (found < 0 || found > close)
            {
                return null;
            }

            // A word that merely ends in the attribute name is not the attribute.
            if (found > open && (char.IsLetterOrDigit(body[found - 1]) || body[found - 1] == '_'))
            {
                position = found + name.Length;
                continue;
            }

            int quote = found + name.Length + 1;

            if (quote >= body.Length || body[quote] is not ('"' or '\''))
            {
                return null;
            }

            int end = body.IndexOf(body[quote], quote + 1);

            return end < 0 ? null : body.Substring(quote + 1, end - quote - 1);
        }
    }

    /// <summary>
    /// The readable name behind a cref: a documentation ID or an unqualified source cref, reduced to
    /// the identifier a reader recognises.
    /// </summary>
    internal static string DisplayName(string cref)
    {
        var name = cref.Trim();

        if (name.Length > 2 && name[1] == ':')
        {
            name = name.Substring(2);
        }

        int parameters = name.IndexOf('(');

        if (parameters >= 0)
        {
            name = name.Substring(0, parameters);
        }

        int type = name.IndexOf('{');

        if (type >= 0)
        {
            name = name.Substring(0, type);
        }

        int segment = name.LastIndexOfAny(['.', '+', ':']);

        if (segment >= 0)
        {
            name = name.Substring(segment + 1);
        }

        int arity = name.IndexOf('`');

        if (arity >= 0)
        {
            name = name.Substring(0, arity);
        }

        return name.Length == 0 ? "type" : name;
    }

    /// <summary>
    /// Trims the indentation a documentation file carries and drops the blank lines, so that the
    /// generated comment is as short as the text it carries. Paragraphs survive as elements.
    /// </summary>
    private static string Tidy(string xml)
    {
        var lines = xml.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var builder = new StringBuilder(xml.Length);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(trimmed);
        }

        return builder.ToString();
    }

    /// <summary>A tag as the scanner meets it: its name, and whether it opens, closes or does both.</summary>
    private readonly record struct TagSpec(string Name, bool Closing, bool SelfClosing);
}
