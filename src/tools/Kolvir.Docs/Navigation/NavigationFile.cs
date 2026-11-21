using Kolvir.Docs.Reference;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Kolvir.Docs.Navigation;

/// <summary>One entry of <c>docs/nav.yml</c>.</summary>
internal sealed class NavEntry
{
    /// <summary>The text the navigation shows.</summary>
    [YamlMember(Alias = "title")]
    public string? Title { get; set; }

    /// <summary>The page, relative to <c>docs/</c>; null for a group.</summary>
    [YamlMember(Alias = "href")]
    public string? Href { get; set; }

    /// <summary>True while the page named by <see cref="Href"/> has not been written yet.</summary>
    [YamlMember(Alias = "planned")]
    public bool Planned { get; set; }

    /// <summary>A generated set to expand in place of items, such as <c>controls</c>.</summary>
    [YamlMember(Alias = "include")]
    public string? Include { get; set; }

    /// <summary>The entries under this group.</summary>
    [YamlMember(Alias = "items")]
    public List<NavEntry>? Items { get; set; }

    /// <summary>Answers what this entry names, one of: a page, a planned page, a group, or a set.</summary>
    /// <param name="problem">Why the entry is malformed, or null.</param>
    /// <returns>True when the entry is well formed.</returns>
    internal bool IsWellFormed(out string? problem)
    {
        problem = null;

        if (string.IsNullOrWhiteSpace(Title))
        {
            problem = "error: a navigation entry has no title.";
            return false;
        }

        bool hasPage = !string.IsNullOrWhiteSpace(Href);
        bool hasItems = Items is { Count: > 0 };
        bool hasInclude = !string.IsNullOrWhiteSpace(Include);

        if ((hasPage ? 1 : 0) + (hasItems ? 1 : 0) + (hasInclude ? 1 : 0) != 1)
        {
            problem = $"error: the navigation entry '{Title}' needs exactly one of href, items or include.";
            return false;
        }

        if (hasInclude && Include != "controls")
        {
            problem = $"error: the navigation entry '{Title}' includes '{Include}'; the only set there is is 'controls'.";
            return false;
        }

        if (hasPage && (Href!.StartsWith('/') || Href.Contains('\\') || Href.Contains("://", StringComparison.Ordinal)))
        {
            problem = $"error: the navigation entry '{Title}' has the href '{Href}'; navigation hrefs are relative paths inside docs/.";
            return false;
        }

        return true;
    }
}

/// <summary>
/// Reads <c>docs/nav.yml</c>, writes the site generator's navigation, and reports entries whose page
/// is missing or whose marker is stale.
/// </summary>
internal static class NavigationFile
{
    /// <summary>Reads the navigation file.</summary>
    /// <param name="repository">The repository holding it.</param>
    /// <returns>Its top-level entries, in order.</returns>
    internal static IReadOnlyList<NavEntry> Read(DocsRepository repository)
    {
        if (!File.Exists(repository.NavFile))
        {
            throw new ToolException($"no navigation file at {repository.Relative(repository.NavFile)}.");
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();

        List<NavEntry> entries;
        try
        {
            entries = deserializer.Deserialize<List<NavEntry>>(File.ReadAllText(repository.NavFile)) ?? [];
        }
        catch (YamlException ex)
        {
            throw new ToolException($"{repository.Relative(repository.NavFile)} is not readable YAML: {ex.Message}");
        }

        return entries;
    }

    /// <summary>Writes the navigation in the form the site generator reads.</summary>
    /// <param name="repository">The repository to write into.</param>
    /// <param name="entries">The entries from <see cref="Read"/>.</param>
    /// <returns>The number of pages the navigation links.</returns>
    internal static int WriteGeneratorNavigation(DocsRepository repository, IReadOnlyList<NavEntry> entries)
    {
        var items = Build(entries, repository, out int pages);

        var serializer = new SerializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

        File.WriteAllText(repository.TocFile, serializer.Serialize(items));
        return pages;
    }

    private static List<TocItem> Build(IReadOnlyList<NavEntry> entries, DocsRepository repository, out int pages)
    {
        pages = 0;
        var items = new List<TocItem>();

        foreach (var entry in entries)
        {
            if (!entry.IsWellFormed(out string? problem))
            {
                throw new ToolException(problem ?? $"the navigation entry '{entry.Title}' is malformed.");
            }

            // A planned page is real in nav.yml and absent from the tree: it is left out of the
            // generated navigation rather than linked to nothing.
            if (entry.Planned)
            {
                continue;
            }

            if (entry.Href is { } href)
            {
                pages++;
                items.Add(new TocItem { Name = entry.Title, Href = href });
                continue;
            }

            if (entry.Include == "controls")
            {
                var controls = ExpandControls(repository);
                pages += controls.Count;
                items.Add(new TocItem { Name = entry.Title, Items = controls });
                continue;
            }

            var children = Build(entry.Items!, repository, out int childPages);
            pages += childPages;
            items.Add(new TocItem { Name = entry.Title, Items = children });
        }

        return items;
    }

    /// <summary>Expands the generated control reference into navigation entries.</summary>
    /// <param name="repository">The repository holding the generated pages.</param>
    /// <returns>One entry per control, in alphabetical order.</returns>
    private static List<TocItem> ExpandControls(DocsRepository repository)
    {
        var items = new List<TocItem>();

        foreach (var entry in ControlMap.Entries.OrderBy(entry => entry.Type, StringComparer.Ordinal))
        {
            string slug = ReferenceGenerator.Slug(entry.Type);
            string file = Path.Combine(repository.ControlsDirectory, slug + ".md");

            if (!File.Exists(file))
            {
                throw new ToolException(
                    $"the navigation includes the control reference, but {repository.Relative(file)} does not exist. "
                    + "Run the build verb, which generates the reference before the site.");
            }

            items.Add(new TocItem { Name = entry.Type, Href = "reference/controls/" + slug + ".md" });
        }

        return items;
    }

    /// <summary>The site generator's own navigation shape.</summary>
    private sealed class TocItem
    {
        [YamlMember(Alias = "name")]
        public string? Name { get; set; }

        [YamlMember(Alias = "href")]
        public string? Href { get; set; }

        [YamlMember(Alias = "items")]
        public List<TocItem>? Items { get; set; }
    }
}