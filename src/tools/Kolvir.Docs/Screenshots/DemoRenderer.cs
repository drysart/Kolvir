namespace Kolvir.Docs.Screenshots;

/// <summary>
/// Renders the screenshots through the sample's headless renderer.
/// </summary>
/// <remarks>
/// The images are not captured from a desktop: the sample paints the same page through its headless
/// backend, so the result does not depend on the window system, the display size or the person taking
/// it, and CI could take them if it needed to.
/// </remarks>
internal static class DemoRenderer
{
    /// <summary>Renders every image the manifest lists.</summary>
    /// <param name="repository">The repository holding the sample.</param>
    /// <param name="configuration">The configuration to run the sample from.</param>
    /// <param name="entries">The manifest entries.</param>
    internal static void Render(DocsRepository repository, string configuration, IReadOnlyList<ScreenshotEntry> entries)
    {
        if (entries.Count == 0)
        {
            Console.WriteLine("screenshots: nothing in the manifest to render");
            return;
        }

        foreach (var entry in entries)
        {
            string path = Path.Combine(repository.AssetsDirectory, entry.Image.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var arguments = new List<string>
            {
                "run",
                "--project",
                repository.DemoProject,
                "--configuration",
                configuration,
                "--",
                "--render",
                path,
                entry.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
                entry.Height.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--page",
                entry.Page,
                entry.Appearance == "dark" ? "--dark" : "--light",
            };

            Console.WriteLine($"render:     {entry.Image} ({entry.Page}, {entry.Width}x{entry.Height}, {entry.Appearance})");
            ProcessRunner.Run(repository, "dotnet", [.. arguments]);
        }
    }
}
