using System.Text;
using System.Text.RegularExpressions;
using Kolvir.Docs.Navigation;
using Kolvir.Docs.Screenshots;

namespace Kolvir.Docs.Validation;

/// <summary>
/// The checks the docs tree has to pass before the site is built.
/// </summary>
/// <remarks>
/// The generator reports its own broken links, which is most of what can go wrong here. What it cannot
/// see, and this does, is the rule the repository keeps for its Markdown (7-bit ASCII), the one CI
/// would only catch after a push (a path whose case is wrong), whether every page the navigation names
/// exists, and whether the code a page shows is code that compiles.
/// </remarks>
internal static partial class DocsChecks
{
    /// <summary>Runs every check.</summary>
    /// <param name="repository">The repository to check.</param>
    /// <param name="entries">The navigation, already read.</param>
    /// <returns>One message per problem, prefixed with <c>error:</c>; empty when the tree is clean.</returns>
    internal static List<string> Run(DocsRepository repository, IReadOnlyList<NavEntry> entries)
    {
        var problems = new List<string>();

        problems.AddRange(CheckNavigation(repository, entries));

        foreach (string file in MarkdownFiles(repository))
        {
            problems.AddRange(CheckAscii(repository, file));
            problems.AddRange(CheckLinks(repository, file));
        }

        problems.AddRange(CheckScreenshots(repository));
        problems.AddRange(CheckSnippets(repository));

        return problems;
    }

    private static IEnumerable<string> MarkdownFiles(DocsRepository repository)
    {
        if (!Directory.Exists(repository.DocsDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(repository.DocsDirectory, "*.md", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(repository.DocsDirectory, file).StartsWith("_site", StringComparison.Ordinal))
            .OrderBy(file => file, StringComparer.Ordinal);
    }

    private static List<string> CheckNavigation(DocsRepository repository, IReadOnlyList<NavEntry> entries)
    {
        var problems = new List<string>();

        Visit(entries, entry =>
        {
            if (!entry.IsWellFormed(out string? problem))
            {
                problems.Add(problem!);
                return;
            }

            if (entry.Href is not { } href)
            {
                return;
            }

            string path = Path.Combine(repository.DocsDirectory, href.Replace('/', Path.DirectorySeparatorChar));
            bool exists = File.Exists(path);

            if (entry.Planned && exists)
            {
                problems.Add($"error: docs/nav.yml marks '{href}' as planned, but {repository.Relative(path)} exists; remove the marker.");
            }
            else if (!entry.Planned && !exists)
            {
                problems.Add($"error: docs/nav.yml links '{href}', which does not exist; write the page or mark it planned.");
            }
        });

        return problems;

        static void Visit(IReadOnlyList<NavEntry> items, Action<NavEntry> action)
        {
            foreach (var entry in items)
            {
                action(entry);

                if (entry.Items is { Count: > 0 } children)
                {
                    Visit(children, action);
                }
            }
        }
    }

    private static List<string> CheckAscii(DocsRepository repository, string file)
    {
        var problems = new List<string>();
        byte[] bytes = File.ReadAllBytes(file);

        int line = 1;
        int column = 1;
        for (int index = 0; index < bytes.Length; index++)
        {
            byte value = bytes[index];

            if (value > 127)
            {
                problems.Add($"error: {repository.Relative(file)}({line},{column}): the Markdown is 7-bit ASCII only; byte 0x{value:X2} is not.");
                return problems;
            }

            if (value == (byte)'\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return problems;
    }

    private static List<string> CheckLinks(DocsRepository repository, string file)
    {
        var problems = new List<string>();
        string text = File.ReadAllText(file);
        string directory = Path.GetDirectoryName(file)!;

        foreach (var target in LinkTargets(text))
        {
            if (target.Length == 0 || target.StartsWith('#')
                || target.Contains("://", StringComparison.Ordinal)
                || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int anchor = target.IndexOf('#', StringComparison.Ordinal);
            string path = anchor >= 0 ? target[..anchor] : target;
            if (path.Length == 0)
            {
                continue;
            }

            string combined = Path.GetFullPath(Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar)));

            // CI builds on a system where `Guide.md` and `guide.md` are two files, so a link whose
            // case does not match works on the author's machine and nowhere else.
            if (!ExistsWithExactCase(repository.DocsDirectory, combined))
            {
                problems.Add($"error: {repository.Relative(file)}: '{target}' does not resolve to a file with that exact case.");
            }
        }

        return problems;
    }

    private static bool ExistsWithExactCase(string docsRoot, string absolutePath)
    {
        string relative = Path.GetRelativePath(docsRoot, absolutePath);
        if (relative.StartsWith("..", StringComparison.Ordinal))
        {
            return File.Exists(absolutePath) || Directory.Exists(absolutePath);
        }

        string current = docsRoot;

        foreach (string segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (!Directory.Exists(current))
            {
                return false;
            }

            string? match = Directory.EnumerateFileSystemEntries(current)
                .FirstOrDefault(entry => string.Equals(Path.GetFileName(entry), segment, StringComparison.Ordinal));

            if (match is null)
            {
                return false;
            }

            current = match;
        }

        return true;
    }

    private static List<string> CheckScreenshots(DocsRepository repository)
    {
        var problems = new List<string>();
        var manifest = ScreenshotManifest.Read(repository);
        var named = new HashSet<string>(manifest.Select(entry => entry.Image), StringComparer.Ordinal);

        foreach (string image in ScreenshotReferences(repository))
        {
            if (!named.Contains(image))
            {
                problems.Add($"error: a page shows '{image}' but docs/assets/screenshots/manifest.yml does not say how to render it.");
            }
        }

        foreach (var entry in manifest)
        {
            string path = Path.Combine(repository.AssetsDirectory, entry.Image.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                problems.Add($"error: the screenshot manifest lists '{entry.Image}', which does not exist; run `-- screenshots`.");
            }
        }

        return problems;
    }

    private static IEnumerable<string> ScreenshotReferences(DocsRepository repository)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);

        foreach (string file in MarkdownFiles(repository))
        {
            foreach (var target in LinkTargets(File.ReadAllText(file)))
            {
                int anchor = target.IndexOf('#', StringComparison.Ordinal);
                string path = (anchor >= 0 ? target[..anchor] : target).Replace("\\", "/");

                if (path.StartsWith("assets/screenshots/", StringComparison.Ordinal))
                {
                    references.Add(path["assets/".Length..]);
                }
                else if (path.Contains("screenshots/", StringComparison.Ordinal) && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    int index = path.IndexOf("screenshots/", StringComparison.Ordinal);
                    references.Add(path[index..]);
                }
            }
        }

        return references;
    }

    private static List<string> CheckSnippets(DocsRepository repository)
    {
        var problems = new List<string>();

        if (!Directory.Exists(repository.SnippetsDirectory))
        {
            return problems;
        }

        var snippets = Directory.EnumerateFiles(repository.SnippetsDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(file => Normalise(File.ReadAllText(file)))
            .ToList();

        foreach (string file in MarkdownFiles(repository))
        {
            string relative = Path.GetRelativePath(repository.DocsDirectory, file).Replace('\\', '/');
            bool showsCode = relative.StartsWith("getting-started/", StringComparison.Ordinal)
                || relative.StartsWith("concepts/", StringComparison.Ordinal)
                || (relative.StartsWith("reference/", StringComparison.Ordinal)
                    && !relative.StartsWith("reference/controls/", StringComparison.Ordinal));

            if (!showsCode)
            {
                continue;
            }

            foreach (Match match in CSharpBlock().Matches(File.ReadAllText(file)))
            {
                string block = Normalise(match.Groups[1].Value);
                if (block.Length == 0)
                {
                    continue;
                }

                if (!snippets.Any(snippet => snippet.Contains(block, StringComparison.Ordinal)))
                {
                    problems.Add($"error: {relative} shows a C# block that is not verbatim in {repository.Relative(repository.SnippetsDirectory)}, so it is not compiled: {FirstLine(block)}");
                }
            }
        }

        return problems;
    }

    private static string FirstLine(string block)
    {
        string[] lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        return lines.Length == 0 ? "(empty)" : lines[0].Trim();
    }

    private static string Normalise(string text) =>
        string.Join(
            '\n',
            text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0));

    private static IEnumerable<string> LinkTargets(string text)
    {
        foreach (Match match in MarkdownLink().Matches(text))
        {
            yield return match.Groups[1].Value.Trim();
        }

        foreach (Match match in HtmlSource().Matches(text))
        {
            yield return match.Groups[1].Value.Trim();
        }
    }

    [GeneratedRegex(@"\]\(([^)\s]+)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"<img[^>]*src=""([^""]+)""")]
    private static partial Regex HtmlSource();

    [GeneratedRegex(@"```csharp\r?\n(.*?)```", RegexOptions.Singleline)]
    private static partial Regex CSharpBlock();
}
