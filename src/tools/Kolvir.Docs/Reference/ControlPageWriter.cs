using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace Kolvir.Docs.Reference;

/// <summary>
/// Writes one control's page.
/// </summary>
/// <remarks>
/// The page is lookup-shaped: what the control is, the sample page that runs it, and every member the
/// control declares with its declaration and its documented description. Members it inherits are on
/// the base control's page, and nothing is repeated.
/// </remarks>
internal sealed class ControlPageWriter(ReferenceModel model, ControlPage page, DocsRepository repository)
{
    private static readonly string[] MemberGroups = ["Constructor", "Property", "Method", "Field"];

    private readonly StringBuilder _builder = new();
    private Dictionary<string, string> _headings = [];

    /// <summary>Writes the page.</summary>
    /// <returns>The Markdown.</returns>
    internal string Write()
    {
        string typeId = DocIds.ForType(page.Type);

        _builder.Append("# ").Append(page.Entry.Type).Append("\n\n");

        if (page.Type.IsAbstract)
        {
            _builder.Append("*This type is abstract: applications use a type that derives from it.*\n\n");
        }

        AppendProse(model.Xml.Element(typeId, "summary"));

        if (page.Entry.IncludeRemarks)
        {
            AppendProse(model.Xml.Element(typeId, "remarks"));
        }

        AppendWhere();

        var members = page.Members();
        _headings = Headings(page, members);

        foreach (string group in MemberGroups)
        {
            var inGroup = members.Where(member => member.Kind == group).ToList();
            if (inGroup.Count == 0)
            {
                continue;
            }

            _builder.Append("## ").Append(group switch
            {
                "Constructor" => "Constructors",
                "Property" => "Properties",
                "Method" => "Methods",
                _ => "Fields",
            }).Append("\n\n");

            foreach (var member in inGroup)
            {
                AppendMember(member);
            }
        }

        AppendInheritance();

        foreach (var related in page.Related)
        {
            AppendRelated(related);
        }

        return _builder.ToString().TrimEnd() + "\n";
    }

    private void AppendWhere()
    {
        if (page.Entry.Demo is { } demo)
        {
            _builder.Append("**Demo:** run the sample with `--page ").Append(demo).Append('`');

            string samples = Path.Combine(repository.DocsDirectory, "samples.md");
            if (File.Exists(samples))
            {
                _builder.Append(" - [what that page shows](../../samples.md)");
            }

            _builder.Append(".\n\n");
        }

        if (page.Entry.Guide is { } guide)
        {
            string title = Path.GetFileNameWithoutExtension(guide).Replace('-', ' ');
            _builder.Append("**How-to:** [").Append(title).Append("](../../").Append(guide).Append(").\n\n");
        }
    }

    private void AppendMember(DocumentedMember member)
    {
        string name = _headings.TryGetValue(member.DocId, out string? heading) ? heading : PlainName(member);

        _builder.Append("### ").Append(name).Append("\n\n");
        _builder.Append("```csharp\n").Append(member.Signature).Append("\n```\n\n");

        AppendProse(model.Xml.Element(member.DocId, "summary"));

        if (page.Entry.IncludeRemarks)
        {
            AppendProse(model.Xml.Element(member.DocId, "remarks"));
        }

        AppendParameters(member);
        AppendReturns(member);
        AppendExceptions(member);
    }

    /// <summary>
    /// The heading each member is written under.
    /// </summary>
    /// <remarks>
    /// A page carries more than one type - the control, and the types documented as sections of it -
    /// so a name on its own is not unique: <c>Text</c> can be a property of two of them,
    /// <c>Invalidate</c> can be six overloads, and a member can share its name with a section. Two
    /// headings with one name are two links with one anchor, so a repeated name says which one it is:
    /// its type when the types differ, and its parameters when one type still contributes it more
    /// than once. A name used once on the page is left alone.
    /// </remarks>
    private static Dictionary<string, string> Headings(ControlPage page, IReadOnlyList<DocumentedMember> members)
    {
        var written = new List<(DocumentedMember? Member, string Name)>();

        // The page's own title competes for an anchor too, which a lone constructor loses to.
        written.Add((null, page.Entry.Type));

        foreach (var member in members)
        {
            written.Add((member, PlainName(member)));
        }

        foreach (var related in page.Related)
        {
            written.Add((null, related.Name));

            if (related.IsEnum)
            {
                continue;
            }

            foreach (var member in ControlPage.MembersOf(related).Where(member => member.Kind != "Constructor"))
            {
                written.Add((member, PlainName(member)));
            }
        }

        var headings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var repeated in written.GroupBy(entry => entry.Name, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            var documented = repeated.Where(entry => entry.Member is not null).ToList();

            // A constructor is named after its type, so only its parameters can tell it apart - from
            // an overload, and from the title the page already uses. Any other repeated name says
            // which type it belongs to, because a property has no parameters to show.
            bool constructors = documented.All(entry => entry.Member!.Info is ConstructorInfo);
            bool oneType = documented.Select(entry => entry.Member!.Info.DeclaringType).Distinct().Count() == 1;
            bool sharesAHeading = documented.Count != repeated.Count();

            foreach (var entry in documented)
            {
                headings[entry.Member!.DocId] = constructors || (oneType && !sharesAHeading)
                    ? entry.Name
                    : entry.Member.Info.DeclaringType!.Name + "." + entry.Name;
            }
        }

        // Overloads of one type still share a name, so whatever is still shared takes the parameters
        // as well - including a lone constructor and the page title it repeats, or a section.
        var byId = new Dictionary<string, DocumentedMember>(StringComparer.Ordinal);
        var reserved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in written)
        {
            if (entry.Member is { } member)
            {
                byId[member.DocId] = member;
            }
            else
            {
                reserved.Add(entry.Name);
            }
        }

        foreach (var shared in headings
            .GroupBy(entry => entry.Value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1 || reserved.Contains(group.Key))
            .ToList())
        {
            foreach (var entry in shared)
            {
                headings[entry.Key] = shared.Key + ParameterTypes(byId[entry.Key].Info);
            }
        }

        return headings;
    }

    private static string PlainName(DocumentedMember member) =>
        member.Info is ConstructorInfo ? member.Info.DeclaringType!.Name : member.Info.Name;

    private static string ParameterTypes(MemberInfo member)
    {
        var parameters = member switch
        {
            MethodBase method => method.GetParameters(),
            PropertyInfo property => property.GetIndexParameters(),
            _ => Array.Empty<ParameterInfo>(),
        };

        return "(" + string.Join(", ", parameters.Select(parameter => Escape(Signatures.Short(parameter.ParameterType)))) + ")";
    }

    private static string Escape(string text) =>
        text.Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

    private void AppendParameters(DocumentedMember member)
    {
        ParameterInfo[] parameters = member.Info switch
        {
            MethodBase method => method.GetParameters(),
            PropertyInfo property => property.GetIndexParameters(),
            _ => [],
        };

        if (parameters.Length == 0)
        {
            return;
        }

        var rows = new List<(string Name, string Type, string Description)>();
        foreach (var parameter in parameters)
        {
            string name = parameter.Name ?? "value";
            string description = DocMarkdown.ToMarkdown(
                model.Xml.Find(member.DocId)?.Elements("param").FirstOrDefault(element => element.Attribute("name")?.Value == name),
                model.Cref);

            rows.Add((name, Signatures.Short(parameter.ParameterType), description));
        }

        // The declaration already names every parameter, so a table with nothing to say about any of
        // them is noise. It appears when the library documents its parameters.
        if (rows.All(row => row.Description.Length == 0))
        {
            return;
        }

        _builder.Append("**Parameters**\n\n");
        _builder.Append("| Name | Type | Description |\n| --- | --- | --- |\n");

        foreach (var (name, type, description) in rows)
        {
            _builder.Append("| `").Append(name).Append("` | `")
                .Append(type.Replace("|", "\\|"))
                .Append("` | ").Append(Cell(description)).Append(" |\n");
        }

        _builder.Append('\n');
    }

    private void AppendReturns(DocumentedMember member)
    {
        if (member.Info is not MethodInfo method || method.ReturnType == typeof(void))
        {
            return;
        }

        var returns = model.Xml.Element(member.DocId, "returns");
        if (returns is null)
        {
            return;
        }

        string description = DocMarkdown.ToMarkdown(returns, model.Cref);
        if (description.Length == 0)
        {
            return;
        }

        _builder.Append("**Returns** `").Append(Signatures.Short(method.ReturnType)).Append("` - ")
            .Append(description).Append("\n\n");
    }

    private void AppendExceptions(DocumentedMember member)
    {
        var exceptions = model.Xml.Find(member.DocId)?.Elements("exception").ToList();
        if (exceptions is null || exceptions.Count == 0)
        {
            return;
        }

        _builder.Append("| Throws | When |\n| --- | --- |\n");

        foreach (var exception in exceptions)
        {
            string type = exception.Attribute("cref")?.Value is { } cref
                ? DocIds.DisplayName(cref)
                : "Exception";

            _builder.Append("| `").Append(type).Append("` | ")
                .Append(Cell(DocMarkdown.ToMarkdown(exception, model.Cref))).Append(" |\n");
        }

        _builder.Append('\n');
    }

    private void AppendInheritance()
    {
        var bases = new List<Type>();
        for (var baseType = page.Type.BaseType; baseType is not null && baseType != typeof(object); baseType = baseType.BaseType)
        {
            if (baseType == typeof(Kolvir.Control) || typeof(Kolvir.Control).IsAssignableFrom(baseType))
            {
                bases.Add(baseType);
            }
        }

        if (bases.Count == 0)
        {
            return;
        }

        var pageable = bases
            .Where(baseType => model.Pages.Any(page => page.Slug == ReferenceGenerator.Slug(baseType.Name)))
            .ToList();

        if (pageable.Count == 0)
        {
            return;
        }

        _builder.Append("## Inherited members\n\n");
        _builder.Append("Members this page does not list come from the base control: ");

        _builder.Append(string.Join(", ", pageable.Select(baseType =>
            $"[{baseType.Name}]({ReferenceGenerator.Slug(baseType.Name)}.md)")));

        _builder.Append(".\n\n");
    }

    private void AppendRelated(Type related)
    {
        string typeId = DocIds.ForType(related);

        _builder.Append("## ").Append(related.Name).Append("\n\n");

        if (related.IsEnum)
        {
            AppendProse(model.Xml.Element(typeId, "summary"));
            AppendEnumValues(related);
            return;
        }

        AppendProse(model.Xml.Element(typeId, "summary"));

        foreach (var member in ControlPage.MembersOf(related).Where(member => member.Kind != "Constructor"))
        {
            AppendMember(member);
        }
    }

    private void AppendEnumValues(Type enumeration)
    {
        var documented = model.Xml.Find(DocIds.ForType(enumeration));
        if (documented is null)
        {
            return;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in documented.Elements("member"))
        {
            string? name = field.Attribute("name")?.Value;
            if (name is not null && name.StartsWith("F:", StringComparison.Ordinal))
            {
                fields[name[(name.LastIndexOf('.') + 1)..]] = DocMarkdown.ToMarkdown(field.Element("summary"), model.Cref);
            }
        }

        if (fields.Count == 0)
        {
            return;
        }

        _builder.Append("| Value | Meaning |\n| --- | --- |\n");
        foreach (var (name, description) in fields)
        {
            _builder.Append("| `").Append(name).Append("` | ").Append(Cell(description)).Append(" |\n");
        }

        _builder.Append('\n');
    }

    private void AppendProse(XElement? element)
    {
        string markdown = DocMarkdown.ToMarkdown(element, model.Cref);
        if (markdown.Length == 0)
        {
            return;
        }

        _builder.Append(markdown).Append("\n\n");
    }

    private static string Cell(string markdown) =>
        markdown.Replace("|", "\\|").Replace("\r", string.Empty).Replace("\n", " ");
}
