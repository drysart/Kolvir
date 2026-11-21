using System.Diagnostics;

namespace Kolvir.Docs;

/// <summary>
/// Runs a child process in the repository root and echoes what it printed.
/// </summary>
/// <remarks>
/// The tool never hides a child's output: when a build or the site generator fails, the line that
/// explains it is the child's, and a wrapper that swallowed it would make the failure harder to read
/// than running the command by hand.
/// </remarks>
internal static class ProcessRunner
{
    /// <summary>Runs a command and fails the tool when it exits non-zero.</summary>
    /// <param name="repository">The repository whose root is the working directory.</param>
    /// <param name="fileName">The executable to start.</param>
    /// <param name="arguments">Its arguments, one per element.</param>
    /// <returns>Nothing; a non-zero exit is an exception.</returns>
    internal static void Run(DocsRepository repository, string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = repository.Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                Console.WriteLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                Console.Error.WriteLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new ToolException($"`{fileName} {string.Join(' ', arguments)}` exited with {process.ExitCode}.");
        }
    }
}
