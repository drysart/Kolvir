using SkiaSharp;

namespace Kolvir.Backend.Win32;

/// <summary>
/// A one-line-per-IME-message trace, off unless asked for, for finding out why an input method puts its
/// own window where it does. It answers the one question a bug report cannot: whether the application
/// is asked at all, and what it answers when it is.
/// </summary>
/// <remarks>
/// <para>
/// It writes to a file rather than to a debug channel because the application that hits this is a
/// windowed one with no console attached. Set <c>KOLVIR_IME_LOG=1</c> for <c>kolvir-ime.log</c> beside
/// the executable, or <c>KOLVIR_IME_LOG</c> to a full path for a specific file.
/// </para>
/// <para>
/// Two switches select the experiments that a log may exonerate, so a run reports which one was on:
/// <c>KOLVIR_IME_SHOW_CARET=1</c> shows the one-pixel system caret once it is positioned, which is the
/// only visibility signal the thread exposes, and <c>KOLVIR_IME_REFRESH_COMPOSITIONFORM=1</c> keeps the
/// input context's composition form at the caret outside a composition, in case the input stack reads
/// it as an anchor.
/// </para>
/// </remarks>
internal static class Win32ImeLog
{
    internal const string PathVariable = "KOLVIR_IME_LOG";
    internal const string ShowCaretVariable = "KOLVIR_IME_SHOW_CARET";
    internal const string RefreshCompositionFormVariable = "KOLVIR_IME_REFRESH_COMPOSITIONFORM";
    internal const string TestPointVariable = "KOLVIR_IME_TEST_POINT";

    private static readonly object Gate = new();

    /// <summary>The window timer that writes the heartbeat, which is why the id must not collide.</summary>
    internal const nuint HeartbeatTimerId = 0x4B56;

    /// <summary>Half a second: often enough to correlate a run, rare enough not to dominate the file.</summary>
    internal const uint HeartbeatMilliseconds = 500;

    /// <summary>True when a log file was asked for. The variable is read per call, not cached.</summary>
    internal static bool Enabled => ResolvePath() is not null;

    /// <summary>True when <c>KOLVIR_IME_SHOW_CARET</c> asked for the system caret to be visible.</summary>
    internal static bool ShowCaret => IsOn(Environment.GetEnvironmentVariable(ShowCaretVariable));

    /// <summary>True when <c>KOLVIR_IME_REFRESH_COMPOSITIONFORM</c> asked for the extra positioning.</summary>
    internal static bool RefreshCompositionForm =>
        IsOn(Environment.GetEnvironmentVariable(RefreshCompositionFormVariable));

    /// <summary>
    /// A screen point to answer <c>IMR_QUERYCHARPOSITION</c> with instead of the caret, or null.
    /// </summary>
    /// <remarks>
    /// <c>KOLVIR_IME_TEST_POINT=x,y</c> exists to decide one question: whether the shell uses the point at
    /// all. A run at a point nowhere near the field either moves the panel there - the point is honoured
    /// and the defect is an offset to measure - or leaves the panel where it was - the point is ignored,
    /// which is what a panel that pins itself to the pointer would look like.
    /// </remarks>
    internal static SKPoint? TestPoint
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(TestPointVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var parts = value.Split(',');
            if (parts.Length != 2
                || !int.TryParse(parts[0].Trim(), out var x)
                || !int.TryParse(parts[1].Trim(), out var y))
            {
                return null;
            }

            return new SKPoint(x, y);
        }
    }

    /// <summary>
    /// The first line of a traced run, written when the window exists: it proves three things at once -
    /// the instrumented build is running, the variable reached this process, and where the file is. A
    /// trace that only writes once a message arrives cannot tell "never asked" from "logger off".
    /// </summary>
    internal static void WriteStartup(IntPtr hwnd, string windowClass) =>
        Write(
            $"trace enabled pid={Environment.ProcessId} hwnd=0x{hwnd.ToInt64():X} class={windowClass} "
            + $"exe={AppContext.BaseDirectory} log={ResolvePath()} heartbeat={HeartbeatMilliseconds}ms");

    internal static void Write(string line)
    {
        if (ResolvePath() is not { } path)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // A diagnostic must never break the message path it is watching.
        }
    }

    private static string? ResolvePath()
    {
        var value = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(value) || value == "0")
        {
            return null;
        }

        return IsOn(value) ? System.IO.Path.Combine(AppContext.BaseDirectory, "kolvir-ime.log") : value;
    }

    private static bool IsOn(string? value) => value is "1" or "true" or "TRUE" or "True";
}
