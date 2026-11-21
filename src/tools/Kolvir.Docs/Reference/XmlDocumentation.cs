using System.Xml.Linq;

namespace Kolvir.Docs.Reference;

/// <summary>
/// The XML documentation file, indexed by member id.
/// </summary>
internal sealed class XmlDocumentation
{
    private readonly Dictionary<string, XElement> _members;

    private XmlDocumentation(Dictionary<string, XElement> members)
    {
        _members = members;
    }

    /// <summary>Loads a documentation file.</summary>
    /// <param name="path">The path of the <c>.xml</c> file beside a shipped assembly.</param>
    /// <returns>The loaded comments.</returns>
    internal static XmlDocumentation Load(string path)
    {
        var document = XDocument.Load(path);
        var members = new Dictionary<string, XElement>(StringComparer.Ordinal);

        var root = document.Root?.Element("members");
        if (root is not null)
        {
            foreach (var member in root.Elements("member"))
            {
                if (member.Attribute("name")?.Value is { } name)
                {
                    members[name] = member;
                }
            }
        }

        return new XmlDocumentation(members);
    }

    /// <summary>How many members the file documents.</summary>
    internal int Count => _members.Count;

    /// <summary>Looks a member up by its documentation id.</summary>
    /// <param name="id">The id, such as <c>T:Kolvir.Button</c>.</param>
    /// <returns>The documented member, or null.</returns>
    internal XElement? Find(string id) => _members.TryGetValue(id, out var member) ? member : null;

    /// <summary>Reads one of a member's child elements, such as <c>summary</c>.</summary>
    /// <param name="id">The member id.</param>
    /// <param name="element">The element to read.</param>
    /// <returns>The element, or null when the member or element is absent.</returns>
    internal XElement? Element(string id, string element) => Find(id)?.Element(element);
}
