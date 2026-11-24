using AppKit;
using Foundation;

namespace Kolvir.Backend.AppKit;

/// <summary>
/// A file trace of the input-method half of the text path: every <c>NSTextInputClient</c> call with
/// its answer, every key the view routes, and every window that takes or loses the keyboard.
/// </summary>
/// <remarks>
/// <para>
/// Off unless <c>KOLVIR_IME_TRACE</c> names a file, and off means nothing is written, nothing is
/// formatted and nothing is allocated: every call site asks <see cref="Enabled"/> first, and the
/// sink is a null field when the variable is unset. A run with the variable set prints the path it
/// is writing to on stderr before the window appears, so a report can name the file without guessing.
/// </para>
/// <para>
/// This exists because one state cannot be reached from a test or from posted events: what the input
/// method does once its candidate list is open. Section 7.7 of <c>dev-docs/IME.md</c> is the case;
/// the trace is what a hand run over a remote console can produce, and it is why the format is one
/// call per line with the answers written out rather than a diff.
/// </para>
/// </remarks>
internal static class AppKitImeTrace
{
    /// <summary>Where the trace goes, or empty when tracing is off.</summary>
    public static readonly string PathToFile = Environment.GetEnvironmentVariable("KOLVIR_IME_TRACE") ?? string.Empty;

    private static readonly StreamWriter? Sink = Open();

    /// <summary>True when a trace file was asked for and could be opened.</summary>
    public static bool Enabled => Sink is not null;

    private static StreamWriter? Open()
    {
        if (PathToFile.Length == 0)
        {
            return null;
        }

        try
        {
            var stream = new FileStream(PathToFile, FileMode.Create, FileAccess.Write, FileShare.Read);
            var writer = new StreamWriter(stream) { AutoFlush = true };

            writer.WriteLine($"# Kolvir input-method trace, started {DateTimeOffset.Now:O}");
            Console.Error.WriteLine($"ime trace: {System.IO.Path.GetFullPath(PathToFile)}");

            return writer;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ime trace off: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>One call and its answer.</summary>
    public static void Line(string message) => Sink?.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} {message}");

    /// <summary>A boundary a reader can grep for, so a key's lines are not guessed at.</summary>
    public static void Boundary(string message) => Sink?.WriteLine($"---- {message} ----");

    /// <summary>A window as class, title and frame - enough to tell a candidate panel from the app's own window.</summary>
    public static string Describe(NSWindow? window) =>
        window is null
            ? "none"
            : $"{window.GetType().Name}\"{window.Title}\"{(window.IsKeyWindow ? " (key)" : string.Empty)}";
}
