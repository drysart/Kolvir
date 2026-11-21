namespace Kolvir.Docs;

/// <summary>
/// What the command line asked for.
/// </summary>
internal sealed class ToolOptions
{
    /// <summary>The verb to run; <c>build</c> when the command line named none.</summary>
    internal string Verb { get; private set; } = "build";

    /// <summary>The repository root, or null to find it by walking up from the tool's own directory.</summary>
    internal string? RepositoryRoot { get; private set; }

    /// <summary>The build configuration the library's XML documentation file comes from.</summary>
    internal string Configuration { get; private set; } = "Debug";

    /// <summary>True to read the library's existing build output instead of building it first.</summary>
    internal bool NoBuild { get; private set; }

    /// <summary>True to pass when a public member has no XML documentation entry.</summary>
    internal bool AllowMissingDocs { get; private set; }

    /// <summary>True when the command line asked for the usage text.</summary>
    internal bool ShowHelp { get; private set; }

    internal static ToolOptions Parse(string[] args)
    {
        var options = new ToolOptions();

        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];

            switch (arg)
            {
                case "--help" or "-h":
                    options.ShowHelp = true;
                    break;
                case "--repo":
                    options.RepositoryRoot = Value(args, ref index, arg);
                    break;
                case "--configuration" or "-c":
                    options.Configuration = Value(args, ref index, arg);
                    break;
                case "--no-build":
                    options.NoBuild = true;
                    break;
                case "--allow-missing-docs":
                    options.AllowMissingDocs = true;
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        throw new ToolException($"unknown option '{arg}'. Run with --help for the list.");
                    }

                    options.Verb = arg;
                    break;
            }
        }

        return options;
    }

    private static string Value(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ToolException($"{option} needs a value.");
        }

        return args[++index];
    }
}
