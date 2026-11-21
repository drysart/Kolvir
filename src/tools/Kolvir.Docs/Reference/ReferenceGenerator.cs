using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Kolvir.Docs.Reference;

/// <summary>
/// Writes one Markdown page per public control from the library's XML documentation.
/// </summary>
/// <remarks>
/// One page per control, members grouped, and each page naming the sample page that exercises the
/// control. The pages are generated rather than committed, so a control's reference is always the
/// documentation its assembly ships, and cannot be stale.
/// </remarks>
internal static partial class ReferenceGenerator
{
    /// <summary>Generates every page, and returns how many were written.</summary>
    /// <param name="repository">The repository to write into.</param>
    /// <param name="options">The command line, for the configuration and the missing-docs switch.</param>
    /// <returns>The number of pages written.</returns>
    internal static int Generate(DocsRepository repository, ToolOptions options)
    {
        var model = Build(repository, options);
        var problems = new List<string>();

        if (Directory.Exists(repository.ControlsDirectory))
        {
            foreach (string stale in Directory.EnumerateFiles(repository.ControlsDirectory, "*.md"))
            {
                File.Delete(stale);
            }
        }
        else
        {
            Directory.CreateDirectory(repository.ControlsDirectory);
        }

        foreach (var page in model.Pages)
        {
            var writer = new ControlPageWriter(model, page, repository);
            string markdown = writer.Write();
            File.WriteAllText(Path.Combine(repository.ControlsDirectory, page.Slug + ".md"), markdown, new UTF8Encoding(false));
        }

        problems.AddRange(model.Problems);
        if (problems.Count > 0 && !options.AllowMissingDocs)
        {
            foreach (string problem in problems)
            {
                Console.Error.WriteLine(problem);
            }

            throw new ToolException($"{problems.Count} problem(s) generating the reference");
        }

        foreach (string problem in problems)
        {
            Console.Error.WriteLine(problem);
        }

        return model.Pages.Count;
    }

    /// <summary>Lists the controls the reference would generate, and where each is demonstrated.</summary>
    /// <param name="repository">The repository holding the library and the sample.</param>
    /// <param name="options">The command line, for the configuration.</param>
    /// <returns>One line per control.</returns>
    internal static IEnumerable<string> DescribeControls(DocsRepository repository, ToolOptions options)
    {
        var model = Build(repository, options);
        var lines = new List<string>();

        foreach (var page in model.Pages)
        {
            lines.Add($"{page.Entry.Type,-28} demo: {page.Entry.Demo ?? "(none)",-32} members: {page.Members().Count}");
        }

        foreach (string problem in model.Problems)
        {
            lines.Add(problem);
        }

        return lines;
    }

    private static ReferenceModel Build(DocsRepository repository, ToolOptions options)
    {
        var xml = XmlDocumentation.Load(LibraryBuilder.DocumentationFile(repository, options.Configuration));
        var assembly = typeof(Kolvir.Control).Assembly;
        var exported = assembly.GetExportedTypes();
        var byName = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var type in exported)
        {
            byName.TryAdd(type.Name, type);
        }

        var problems = new List<string>();
        var pages = new List<ControlPage>();
        var contextual = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in ControlMap.Entries)
        {
            if (!byName.TryGetValue(entry.Type, out var type))
            {
                problems.Add($"error: the control map names '{entry.Type}', which is not a public type in {assembly.GetName().Name}.");
                continue;
            }

            var related = new List<Type>();
            foreach (string relatedName in entry.Related ?? [])
            {
                if (!byName.TryGetValue(relatedName, out var relatedType))
                {
                    problems.Add($"error: '{entry.Type}' lists related type '{relatedName}', which is not a public type in {assembly.GetName().Name}.");
                    continue;
                }

                related.Add(relatedType);
                contextual.Add(relatedType.Name);
            }

            pages.Add(new ControlPage(entry, type, Slug(entry.Type), related));
        }

        // A public control with no entry is a control an application can use and cannot look up.
        foreach (var type in exported)
        {
            if (!type.IsClass || !typeof(Kolvir.Control).IsAssignableFrom(type))
            {
                continue;
            }

            if (ControlMap.Excluded.ContainsKey(type.Name) || ControlMap.Entries.Any(entry => entry.Type == type.Name))
            {
                continue;
            }

            problems.Add($"error: '{type.Name}' is a public control with no entry in ControlMap.Entries, so it would have no reference page.");
        }

        // A related type that also has a page would document it twice.
        foreach (var page in pages)
        {
            if (contextual.Contains(page.Entry.Type))
            {
                problems.Add($"error: '{page.Entry.Type}' has a page of its own and is also listed as a related type of another control.");
            }
        }

        var demoPageIds = ReadDemoPageIds(repository);
        foreach (var page in pages)
        {
            if (page.Entry.Demo is { } demo && !demoPageIds.Contains(demo))
            {
                problems.Add($"error: '{page.Entry.Type}' names demo page '{demo}', which is not a page id in {repository.Relative(repository.DemoPageTable)}.");
            }

            if (page.Entry.Guide is { } guide && !File.Exists(Path.Combine(repository.DocsDirectory, guide)))
            {
                problems.Add($"error: '{page.Entry.Type}' names the guide page '{guide}', which does not exist under docs/.");
            }
        }

        // Prose that the reference cannot attach to a member is a page with a hole in it.
        var model = new ReferenceModel(pages, xml, problems, exported);

        if (!options.AllowMissingDocs)
        {
            model.CheckDocumentation();
        }

        return model;
    }

    private static HashSet<string> ReadDemoPageIds(DocsRepository repository)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        if (!File.Exists(repository.DemoPageTable))
        {
            return ids;
        }

        foreach (Match match in DemoRow().Matches(File.ReadAllText(repository.DemoPageTable)))
        {
            ids.Add(match.Groups[1].Value);
        }

        return ids;
    }

    /// <summary>The demo page table's rows: an id in the first column of each tuple.</summary>
    [GeneratedRegex(@"\(""([a-z0-9-]+)"",\s*""")]
    private static partial Regex DemoRow();

    /// <summary>The file name a page is written to.</summary>
    /// <param name="typeName">The type name.</param>
    /// <returns>The kebab-case slug, such as <c>split-container</c>.</returns>
    internal static string Slug(string typeName)
    {
        var builder = new StringBuilder(typeName.Length + 8);

        for (int index = 0; index < typeName.Length; index++)
        {
            char character = typeName[index];
            bool boundary = index > 0
                && char.IsUpper(character)
                && (!char.IsUpper(typeName[index - 1])
                    || (index + 1 < typeName.Length && char.IsLower(typeName[index + 1])));

            if (boundary)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}

/// <summary>A control's page: its map entry, its type, its file name and the types documented on it.</summary>
/// <param name="Entry">The map entry.</param>
/// <param name="Type">The reflected type.</param>
/// <param name="Slug">The file name without its extension.</param>
/// <param name="Related">Types documented as sections of this page.</param>
internal sealed record ControlPage(ControlEntry Entry, Type Type, string Slug, IReadOnlyList<Type> Related)
{
    /// <summary>The page's own members, in the order the sections show them.</summary>
    /// <returns>Constructors, then properties, then methods, then fields.</returns>
    internal List<DocumentedMember> Members() => MembersOf(Type);

    /// <summary>The members of a type, in the order a page shows them.</summary>
    /// <param name="type">The type to list.</param>
    /// <returns>Constructors, then properties, then methods, then fields.</returns>
    internal static List<DocumentedMember> MembersOf(Type type)
    {
        var members = new List<DocumentedMember>();

        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            if (IsBrowsable(constructor))
            {
                members.Add(new("Constructor", DocIds.ForMethod(constructor), Signatures.ForConstructor(constructor), constructor));
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (IsBrowsable(property) && property.GetMethod is not null)
            {
                members.Add(new("Property", DocIds.ForProperty(property), Signatures.ForProperty(property), property));
            }
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (!method.IsSpecialName && IsBrowsable(method))
            {
                members.Add(new("Method", DocIds.ForMethod(method), Signatures.ForMethod(method), method));
            }
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (IsBrowsable(field))
            {
                members.Add(new("Field", DocIds.ForField(field), Signatures.ForField(field), field));
            }
        }

        return members;
    }

    private static bool IsBrowsable(MemberInfo member) =>
        member.GetCustomAttribute<System.ComponentModel.EditorBrowsableAttribute>()?.State
            != System.ComponentModel.EditorBrowsableState.Never;
}

/// <summary>One member as a page shows it: its group, its documentation id and its declaration.</summary>
/// <param name="Kind">Constructor, Property, Method or Field.</param>
/// <param name="DocId">The id its prose is filed under.</param>
/// <param name="Signature">The declaration, as the source writes it.</param>
/// <param name="Info">The reflected member.</param>
internal sealed record DocumentedMember(string Kind, string DocId, string Signature, MemberInfo Info);

/// <summary>Everything the writer needs: the pages, the prose, and the cross-link targets.</summary>
internal sealed class ReferenceModel
{
    private readonly Dictionary<string, string> _pageByDocId = new(StringComparer.Ordinal);
    private readonly List<Type> _exported;

    internal ReferenceModel(List<ControlPage> pages, XmlDocumentation xml, List<string> problems, Type[] exported)
    {
        Pages = pages;
        Xml = xml;
        Problems = problems;
        _exported = [.. exported];

        foreach (var page in pages)
        {
            _pageByDocId[DocIds.ForType(page.Type)] = page.Slug + ".md";

            foreach (var related in page.Related)
            {
                _pageByDocId[DocIds.ForType(related)] = page.Slug + ".md";
            }
        }
    }

    /// <summary>The pages to write, in map order.</summary>
    internal List<ControlPage> Pages { get; }

    /// <summary>The library's documentation file.</summary>
    internal XmlDocumentation Xml { get; }

    /// <summary>What went wrong while building the model.</summary>
    internal List<string> Problems { get; }

    /// <summary>Resolves a cref to a link when the target has a page, and to code text otherwise.</summary>
    /// <param name="cref">The cref a comment carried.</param>
    /// <returns>Markdown.</returns>
    internal string Cref(string cref)
    {
        if (_pageByDocId.TryGetValue(cref, out string? link))
        {
            return $"[{DocIds.DisplayName(cref)}]({link})";
        }

        return "`" + DocIds.DisplayName(cref) + "`";
    }

    /// <summary>Finds a public type by its documented name.</summary>
    /// <param name="name">The type name, without a namespace.</param>
    /// <returns>The type, or null.</returns>
    internal Type? FindType(string name) => _exported.FirstOrDefault(type => type.Name == name);

    /// <summary>Reports every member whose documentation the XML file does not carry.</summary>
    internal void CheckDocumentation()
    {
        foreach (var page in Pages)
        {
            foreach (var member in page.Members())
            {
                if (Xml.Find(member.DocId) is null)
                {
                    Problems.Add($"error: no documentation entry for '{member.DocId}'; the reference page would have a hole where its summary belongs.");
                }
            }
        }

        foreach (var page in Pages)
        {
            foreach (var related in page.Related)
            {
                if (Xml.Element(DocIds.ForType(related), "summary") is null)
                {
                    Problems.Add($"error: no summary for '{related.Name}', which is documented on the '{page.Entry.Type}' page.");
                }
            }
        }
    }
}
