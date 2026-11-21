namespace Kolvir.Docs.Reference;

/// <summary>
/// Builds the library so its XML documentation file exists, and finds that file.
/// </summary>
internal static class LibraryBuilder
{
    /// <summary>Builds the library project.</summary>
    /// <param name="repository">The repository to build in.</param>
    /// <param name="configuration">The configuration to build.</param>
    internal static void Build(DocsRepository repository, string configuration)
    {
        Console.WriteLine($"building:   {repository.Relative(repository.LibraryProject)} ({configuration})");

        ProcessRunner.Run(
            repository,
            "dotnet",
            "build",
            repository.LibraryProject,
            "--configuration",
            configuration,
            "--nologo");
    }

    /// <summary>Finds the documentation file the build produced.</summary>
    /// <param name="repository">The repository holding the project.</param>
    /// <param name="configuration">The configuration that was built.</param>
    /// <returns>The absolute path of <c>Kolvir.xml</c>.</returns>
    internal static string DocumentationFile(DocsRepository repository, string configuration)
    {
        // Beside the assembly the tool actually loaded: the same file a package ships, and the same
        // one IntelliSense reads, so the reference cannot be generated from anything else.
        var assembly = typeof(Kolvir.Control).Assembly.Location;
        var beside = Path.ChangeExtension(assembly, ".xml");
        if (File.Exists(beside))
        {
            return beside;
        }

        // A build of this project copies the library's documentation beside it only when the library
        // asks for one, so fall back to the project's own output directory.
        var project = Path.Combine(
            repository.Root,
            "src",
            "core",
            "Kolvir",
            "bin",
            configuration,
            "net10.0",
            "Kolvir.xml");

        if (File.Exists(project))
        {
            return project;
        }

        throw new ToolException($"no Kolvir.xml found beside {assembly}, or at {repository.Relative(project)}; build the library first.");
    }
}
