namespace Kolvir.Docs;

/// <summary>
/// Where everything the tool reads and writes lives in the working tree.
/// </summary>
/// <remarks>
/// Every path is derived from the repository root, so the tool works wherever the repository is
/// checked out and never writes outside it.
/// </remarks>
internal sealed class DocsRepository
{
    private DocsRepository(string root)
    {
        Root = root;
    }

    /// <summary>The repository root - the directory holding <c>Kolvir.slnx</c>.</summary>
    internal string Root { get; }

    /// <summary>The directory the guide is published from.</summary>
    internal string DocsDirectory => Path.Combine(Root, "docs");

    /// <summary>The generated control pages; not committed.</summary>
    internal string ControlsDirectory => Path.Combine(DocsDirectory, "reference", "controls");

    /// <summary>The single hand-written navigation file.</summary>
    internal string NavFile => Path.Combine(DocsDirectory, "nav.yml");

    /// <summary>The site generator's navigation file, written from <see cref="NavFile"/>.</summary>
    internal string TocFile => Path.Combine(DocsDirectory, "toc.yml");

    /// <summary>The site generator's own configuration.</summary>
    internal string DocfxFile => Path.Combine(DocsDirectory, "docfx.json");

    /// <summary>Where images and everything else a page may point at live.</summary>
    internal string AssetsDirectory => Path.Combine(DocsDirectory, "assets");

    /// <summary>The rendered screenshots.</summary>
    internal string ScreenshotsDirectory => Path.Combine(AssetsDirectory, "screenshots");

    /// <summary>The manifest naming the demo page and arguments behind every screenshot.</summary>
    internal string ScreenshotManifest => Path.Combine(ScreenshotsDirectory, "manifest.yml");

    /// <summary>The built HTML; not committed.</summary>
    internal string SiteDirectory => Path.Combine(DocsDirectory, "_site");

    /// <summary>The project whose code the getting-started pages show.</summary>
    internal string SnippetsDirectory => Path.Combine(Root, "src", "tools", "Kolvir.Docs.Snippets");

    /// <summary>The library project, which is what produces the XML documentation file.</summary>
    internal string LibraryProject => Path.Combine(Root, "src", "core", "Kolvir", "Kolvir.csproj");

    /// <summary>The sample application, which renders the screenshots.</summary>
    internal string DemoProject => Path.Combine(Root, "src", "demo", "Kolvir.Demo.App", "Kolvir.Demo.App.csproj");

    /// <summary>The sample's page table, read only to validate the demo page ids the map names.</summary>
    internal string DemoPageTable => Path.Combine(Root, "src", "demo", "Kolvir.Demo.App", "DemoUi.cs");

    /// <summary>Finds the repository root, or uses the one the command line named.</summary>
    /// <param name="root">The repository root from the command line, or null.</param>
    /// <returns>The located repository.</returns>
    internal static DocsRepository Locate(string? root)
    {
        if (!string.IsNullOrWhiteSpace(root))
        {
            var full = Path.GetFullPath(root);
            return new DocsRepository(full);
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kolvir.slnx")))
            {
                return new DocsRepository(directory.FullName);
            }
        }

        throw new ToolException($"no Kolvir.slnx above {AppContext.BaseDirectory}; pass --repo <path>.");
    }

    /// <summary>Makes a path relative to the repository root, for readable diagnostics.</summary>
    /// <param name="path">An absolute path inside the repository.</param>
    /// <returns>The path as it would be written from the root.</returns>
    internal string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');
}
