namespace Kolvir.Docs.Site;

/// <summary>
/// Builds the HTML with the pinned site generator.
/// </summary>
/// <remarks>
/// The generator is restored from the local tool manifest first, so a clean clone with only the .NET
/// SDK is enough, and its warnings are errors: a broken relative link or a missing image fails the
/// build here rather than shipping as a dead link.
/// </remarks>
internal static class SiteBuilder
{
    /// <summary>Builds the site.</summary>
    /// <param name="repository">The repository holding <c>docs/docfx.json</c>.</param>
    internal static void Build(DocsRepository repository)
    {
        if (Directory.Exists(repository.SiteDirectory))
        {
            Directory.Delete(repository.SiteDirectory, recursive: true);
        }

        Console.WriteLine("tools:      restoring the pinned site generator");
        ProcessRunner.Run(repository, "dotnet", "tool", "restore");

        Console.WriteLine($"site:       building {repository.Relative(repository.DocfxFile)}");
        ProcessRunner.Run(repository, "dotnet", "docfx", "build", repository.DocfxFile, "--warningsAsErrors");

        string index = Path.Combine(repository.SiteDirectory, "index.html");
        if (!File.Exists(index))
        {
            throw new ToolException($"the site build produced no {repository.Relative(index)}.");
        }

        Console.WriteLine($"site:       {repository.Relative(index)}");
    }
}
