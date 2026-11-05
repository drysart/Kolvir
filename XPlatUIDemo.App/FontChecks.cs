using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using XPlatUI;

namespace XPlatUI.Demo;

/// <summary>
/// The headless check for a font loaded from font data: it prints the metrics in a form that can be
/// compared between Windows, macOS and Linux, checks that they come from the bundled face rather than
/// the machine's own, and exercises every failure path the loaders have.
/// </summary>
/// <remarks>
/// Run it with <c>XPlatUIDemoApp --font-check [report.txt]</c>. It runs before any toolkit is
/// touched, so it needs no window and no display, and it exits non-zero when a check fails.
/// </remarks>
internal static class FontChecks
{
    private const string SampleText = "The quick brown fox jumps over the lazy dog";
    private const float SampleSize = 13f;

    /// <summary>Runs every check and returns a process exit code.</summary>
    /// <param name="reportPath">
    /// Where to write the same lines that go to the console, or null not to. The sample is a Windows
    /// GUI executable with no console of its own, so a run started from a script has to read the
    /// report from a file there.
    /// </param>
    public static int Run(string? reportPath = null)
    {
        var checks = new List<(bool Passed, string Name, string Detail)>();
        var report = new StringBuilder();

        report.AppendLine($"Font check on {RuntimeInformation.RuntimeIdentifier}");

        using var loaded = XPlatFont.FromBytes(DemoFont.Data);
        using var system = new XPlatFont(SampleSize);

        // The numbers here are the point of the feature: the same lines from all three platforms
        // have to be identical.
        report.AppendLine($"face: {loaded.FamilyName}");
        report.AppendLine($"system face: {system.FamilyName}");
        report.AppendLine($"measure width: {N(loaded.MeasureWidth(SampleText))}");
        report.AppendLine($"system measure width: {N(system.MeasureWidth(SampleText))}");
        report.AppendLine($"line height: {N(loaded.LineHeight)}");
        report.AppendLine($"system line height: {N(system.LineHeight)}");
        report.AppendLine($"baseline offset: {N(loaded.BaselineOffset)}");
        report.AppendLine($"system baseline offset: {N(system.BaselineOffset)}");

        Check(checks, "the bundled face is not the system face", () =>
        {
            bool differentFace = !string.Equals(loaded.FamilyName, system.FamilyName, StringComparison.Ordinal);
            bool differentWidth = loaded.MeasureWidth(SampleText) != system.MeasureWidth(SampleText);
            return (differentFace && differentWidth,
                $"{loaded.FamilyName} vs {system.FamilyName}, {N(loaded.MeasureWidth(SampleText))} vs {N(system.MeasureWidth(SampleText))}");
        });

        Check(checks, "two fonts from the same bytes measure identically", () =>
        {
            using var again = XPlatFont.FromBytes(DemoFont.Data);
            bool same = again.MeasureWidth(SampleText) == loaded.MeasureWidth(SampleText)
                && again.LineHeight == loaded.LineHeight
                && again.BaselineOffset == loaded.BaselineOffset;
            return (same, $"{N(again.MeasureWidth(SampleText))}, {N(again.LineHeight)}, {N(again.BaselineOffset)}");
        });

        Check(checks, "WithSize keeps the loaded face", () =>
        {
            using var bigger = loaded.WithSize(26f);
            float small = loaded.MeasureWidth(SampleText);
            float large = bigger.MeasureWidth(SampleText);
            float ratio = small == 0f ? 0f : large / small;
            bool kept = string.Equals(bigger.FamilyName, loaded.FamilyName, StringComparison.Ordinal);
            bool scaled = ratio > 1.9f && ratio < 2.1f;
            return (kept && scaled, $"{bigger.FamilyName}, ratio {N(ratio)}");
        });

        Check(checks, "a derived font outlives the font it came from", () =>
        {
            var original = XPlatFont.FromBytes(DemoFont.Data);
            var derived = original.WithSize(20f);
            original.Dispose();

            float width = derived.MeasureWidth(SampleText);
            derived.Dispose();
            return (width > 0f, $"measured {N(width)} after the original was disposed");
        });

        Check(checks, "disposing a font twice does nothing", () =>
        {
            var twice = XPlatFont.FromBytes(DemoFont.Data);
            twice.Dispose();
            twice.Dispose();
            return (true, "no exception");
        });

        Check(checks, "a font keeps no tie to the file it came from", () =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"xplatui-font-{Guid.NewGuid():N}.ttf");
            File.WriteAllBytes(path, DemoFont.Data);

            using var fromFile = XPlatFont.FromFile(path);
            File.Delete(path);

            bool same = fromFile.MeasureWidth(SampleText) == loaded.MeasureWidth(SampleText);
            return (same, $"measured {N(fromFile.MeasureWidth(SampleText))} after the file was deleted");
        });

        Check(checks, "a null or empty path is rejected", () =>
        {
            try
            {
                XPlatFont.FromFile(string.Empty);
                return (false, "no exception");
            }
            catch (ArgumentException)
            {
                return (true, "ArgumentException");
            }
        });

        Expect<FileNotFoundException>(checks, "a missing file reports FileNotFoundException", () =>
        {
            using var font = XPlatFont.FromFile(Path.Combine(Path.GetTempPath(), $"xplatui-missing-{Guid.NewGuid():N}.ttf"));
        });

        Expect<ArgumentException>(checks, "a stream that is not a font is rejected", () =>
        {
            using var font = XPlatFont.FromStream(new MemoryStream(Encoding.UTF8.GetBytes("This is not a font, it is a sentence.")));
        });

        Expect<ArgumentException>(checks, "an empty stream is rejected", () =>
        {
            using var font = XPlatFont.FromStream(new MemoryStream());
        });

        Expect<ArgumentException>(checks, "a truncated font is rejected", () =>
        {
            using var font = XPlatFont.FromBytes(DemoFont.Data.AsSpan(0, 1024));
        });

        Check(checks, "a loaded face makes the theme fonts that face", () =>
        {
            XPlatTheme.UseFont(XPlatFont.FromBytes(DemoFont.Data));

            bool sameFace = string.Equals(XPlatTheme.DefaultFont.FamilyName, loaded.FamilyName, StringComparison.Ordinal)
                && string.Equals(XPlatTheme.EmphasizedFont.FamilyName, loaded.FamilyName, StringComparison.Ordinal)
                && string.Equals(XPlatTheme.TitleFont.FamilyName, loaded.FamilyName, StringComparison.Ordinal);
            bool sizesKept = XPlatTheme.CaptionFont.Size == 11.5f && XPlatTheme.TitleFont.Size == 24f;
            bool noFakeWeight = XPlatTheme.EmphasizedFont.Weight == loaded.Weight
                && XPlatTheme.TitleFont.Weight == loaded.Weight;

            string detail =
                $"default {XPlatTheme.DefaultFont.FamilyName} {N(XPlatTheme.DefaultFont.Size)}, " +
                $"emphasized weight {XPlatTheme.EmphasizedFont.Weight}, title {N(XPlatTheme.TitleFont.Size)}";

            XPlatTheme.FontFamily = null;
            return (sameFace && sizesKept && noFakeWeight, detail);
        });

        int failed = checks.Count(check => !check.Passed);
        foreach (var check in checks)
        {
            report.AppendLine($"{(check.Passed ? "PASS" : "FAIL")}  {check.Name}  [{check.Detail}]");
        }

        report.AppendLine(failed == 0
            ? $"All {checks.Count} font checks passed."
            : $"{failed} of {checks.Count} font checks FAILED.");

        Console.Write(report);
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            File.WriteAllText(reportPath, report.ToString());
        }

        return failed == 0 ? 0 : 1;
    }

    /// <summary>Runs one check, treating an exception it did not expect as a failure rather than a crash.</summary>
    private static void Check(List<(bool Passed, string Name, string Detail)> checks, string name, Func<(bool Passed, string Detail)> body)
    {
        try
        {
            var (passed, detail) = body();
            checks.Add((passed, name, detail));
        }
        catch (Exception exception)
        {
            checks.Add((false, name, $"threw {exception.GetType().Name}: {exception.Message}"));
        }
    }

    /// <summary>Runs <paramref name="body"/> and passes when it throws exactly <typeparamref name="TException"/>.</summary>
    private static void Expect<TException>(List<(bool Passed, string Name, string Detail)> checks, string name, Action body)
        where TException : Exception
    {
        try
        {
            body();
            checks.Add((false, name, $"no {typeof(TException).Name}"));
        }
        catch (TException exception)
        {
            checks.Add((true, name, $"{typeof(TException).Name}: {exception.Message}"));
        }
        catch (Exception exception)
        {
            checks.Add((false, name, $"threw {exception.GetType().Name}: {exception.Message}"));
        }
    }

    /// <summary>Formats a metric so the same number prints the same way on every machine.</summary>
    private static string N(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
