namespace Kolvir.Docs;

using Kolvir.Docs.Navigation;
using Kolvir.Docs.Reference;
using Kolvir.Docs.Screenshots;
using Kolvir.Docs.Site;
using Kolvir.Docs.Validation;

/// <summary>
/// The documentation tool's command line.
/// </summary>
/// <remarks>
/// <para>
/// One verb is the documented command; the rest exist so a single part can be re-run while working on
/// it. <c>build</c> is the whole pipeline: build the library for its XML documentation, generate the
/// control reference, translate <c>docs/nav.yml</c> into the site generator's navigation, check the
/// tree, and run the generator.
/// </para>
/// <para>
/// This is a desktop tool. It reads a built assembly by reflection, starts <c>dotnet</c> as a child
/// process and writes files into the working tree, which is what a documentation tool is for and what
/// a shipped library may not do. It targets <c>net10.0</c> and has no platform conditionals.
/// </para>
/// </remarks>
internal static class Program
{
    private const string Usage = """
        Kolvir documentation tool.

          dotnet run --project src/tools/Kolvir.Docs -- <verb> [options]

        Verbs:
          build        everything below, in order (the default)
          reference    generate docs/reference/controls from the library's XML documentation
          site         translate nav.yml, check the tree, and build the HTML with the pinned generator
          screenshots  render every image listed in docs/assets/screenshots/manifest.yml
          list         print the public controls the reference would generate, and their map entries
          check        check the docs tree only

        Options:
          --repo <path>            the repository root (default: the nearest directory holding Kolvir.slnx)
          --configuration <name>   the build configuration to read (default: Debug)
          --no-build               do not run `dotnet build` for the library first
          --allow-missing-docs     pass when a public member has no XML documentation entry
        """;

    /// <summary>Runs the tool.</summary>
    /// <param name="args">The command line, without the executable path.</param>
    /// <returns>Zero on success, non-zero on failure.</returns>
    internal static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (ToolException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length > 0 && (args[0] is "--help" or "-h" or "help"))
        {
            Console.WriteLine(Usage);
            return 0;
        }

        var options = ToolOptions.Parse(args);
        if (options.ShowHelp)
        {
            Console.WriteLine(Usage);
            return 0;
        }

        var repository = DocsRepository.Locate(options.RepositoryRoot);
        Console.WriteLine($"repository: {repository.Root}");
        Console.WriteLine($"docs:       {repository.DocsDirectory}");

        return options.Verb switch
        {
            "build" => Build(repository, options),
            "reference" => Reference(repository, options),
            "site" => Site(repository, options),
            "screenshots" => Screenshots(repository, options),
            "list" => List(repository, options),
            "check" => Check(repository, options),
            _ => throw new ToolException($"unknown verb '{options.Verb}'. Run with --help for the list."),
        };
    }

    private static int Build(DocsRepository repository, ToolOptions options)
    {
        // The XML documentation file is what the reference is generated from, so the library has to be
        // built before anything reads it. `--no-build` exists for the second run of a working session.
        if (!options.NoBuild)
        {
            LibraryBuilder.Build(repository, options.Configuration);
        }

        var generated = ReferenceGenerator.Generate(repository, options);
        Console.WriteLine($"reference:  {generated} page(s) written to {repository.ControlsDirectory}");

        return Site(repository, options);
    }

    private static int Reference(DocsRepository repository, ToolOptions options)
    {
        if (!options.NoBuild)
        {
            LibraryBuilder.Build(repository, options.Configuration);
        }

        var generated = ReferenceGenerator.Generate(repository, options);
        Console.WriteLine($"reference:  {generated} page(s) written to {repository.ControlsDirectory}");
        return 0;
    }

    private static int Site(DocsRepository repository, ToolOptions options)
    {
        var entries = NavigationFile.Read(repository);
        var written = NavigationFile.WriteGeneratorNavigation(repository, entries);
        Console.WriteLine($"navigation: {written} entr(ies) written to {repository.TocFile}");

        var problems = DocsChecks.Run(repository, entries);
        if (problems.Count > 0)
        {
            foreach (var problem in problems)
            {
                Console.Error.WriteLine(problem);
            }

            throw new ToolException($"{problems.Count} problem(s) in the docs tree");
        }

        SiteBuilder.Build(repository);
        return 0;
    }

    private static int Screenshots(DocsRepository repository, ToolOptions options)
    {
        var manifest = ScreenshotManifest.Read(repository);
        DemoRenderer.Render(repository, options.Configuration, manifest);
        return 0;
    }

    private static int List(DocsRepository repository, ToolOptions options)
    {
        if (!options.NoBuild)
        {
            LibraryBuilder.Build(repository, options.Configuration);
        }

        foreach (var line in ReferenceGenerator.DescribeControls(repository, options))
        {
            Console.WriteLine(line);
        }

        return 0;
    }

    private static int Check(DocsRepository repository, ToolOptions options)
    {
        var entries = NavigationFile.Read(repository);
        var problems = DocsChecks.Run(repository, entries);
        foreach (var problem in problems)
        {
            Console.Error.WriteLine(problem);
        }

        if (problems.Count > 0)
        {
            throw new ToolException($"{problems.Count} problem(s) in the docs tree");
        }

        Console.WriteLine("check:      the docs tree is clean");
        return 0;
    }
}

/// <summary>
/// A failure the tool reports as one line and an exit code, rather than as a stack trace.
/// </summary>
internal sealed class ToolException(string message) : Exception(message);
