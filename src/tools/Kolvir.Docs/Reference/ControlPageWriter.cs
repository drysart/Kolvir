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
        string name = member.Info is ConstructorInfo ? member.Info.DeclaringType!.Name : member.Info.Name;

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
