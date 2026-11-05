using SkiaSharp;
using XPlatUI;
using XPlatUI.Backend.Headless;
using XDock = XPlatUI.DockStyle;

namespace XPlatUI.Demo;

/// <summary>
/// The headless harness for the inherited appearance model: it builds real control trees in a
/// headless window, paints them through the host, and checks that a font, a foreground colour and a
/// background colour come from the right place - the control itself, then the nearest ancestor that
/// assigns one, then the class default that comes from <see cref="XPlatTheme"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every check that changes the appearance at runtime deliberately does so after the controls are
/// built and attached, because that is the behaviour being replaced: nothing may be captured when a
/// control is constructed. A theme font change is followed by a paint rather than by an explicit
/// layout call, so what the check measures is the frame an application would really get.
/// </para>
/// <para>
/// Fonts come from <see cref="XPlatFont.FromBytes"/> so the numbers do not depend on which faces the
/// machine has, and every check leaves <see cref="XPlatTheme"/> the way it found it.
/// </para>
/// <para>
/// Run it with <c>XPlatUIDemoApp --appearance-check [report.txt]</c>. It prints one line per check
/// and exits non-zero when any of them fails, so it is usable as a gate on every platform.
/// </para>
/// </remarks>
internal static class AppearanceChecks
{
    private const string Sample = "Inherited appearance";

    private const float Width = 480f;
    private const float Height = 360f;

    private static readonly SKColor AncestorColour = new(0xFFE8EEF9);
    private static readonly SKColor OverrideColour = new(0xFF2F6F4F);

    /// <summary>Runs every check and returns a process exit code.</summary>
    /// <param name="reportPath">
    /// Where to write the same lines that go to the console, or null not to. The sample is a Windows
    /// GUI executable with no console of its own, so a run started from a script has to read the
    /// report from a file there.
    /// </param>
    public static int Run(string? reportPath = null)
    {
        var checks = new List<(bool Passed, string Name, string Detail)>
        {
            Check("font and colour resolve through a chain of nested panels", Scenario((root, host) =>
            {
                using var outerFont = XPlatFont.FromBytes(DemoFont.Data, 17f);

                var outer = new XPlatPanel { Dock = XDock.Fill, Font = outerFont, Foreground = AncestorColour };
                var middle = NoChrome();
                var label = new XPlatLabel(Sample) { Dock = XDock.Top, AutoSize = true };
                var outsider = new XPlatLabel(Sample) { Dock = XDock.Top, AutoSize = true };

                middle.Controls.Add(label);
                outer.Controls.Add(middle);
                root.Controls.Add(outer);
                root.Controls.Add(outsider);

                bool inheritedFont = ReferenceEquals(label.Font, outerFont) && ReferenceEquals(middle.Font, outerFont);
                bool inheritedColour = label.Foreground == AncestorColour && middle.Foreground == AncestorColour;
                bool outsiderUsesDefault = ReferenceEquals(outsider.Font, XPlatTheme.DefaultFont)
                    && outsider.Foreground == XPlatTheme.ColorOf(XPlatThemeColor.Foreground);

                return (inheritedFont && inheritedColour && outsiderUsesDefault,
                    $"{label.Font.Size:0.#} pt at the leaf, {middle.Font.Size:0.#} pt in the middle, " +
                    $"{outsider.Font.Size:0.#} pt outside the chain");
            })),

            Check("an override answers for every control under it", Scenario((root, host) =>
            {
                using var outerFont = XPlatFont.FromBytes(DemoFont.Data, 17f);
                using var innerFont = XPlatFont.FromBytes(DemoFont.Data, 11f);

                var outer = new XPlatPanel { Dock = XDock.Fill, Font = outerFont, Foreground = AncestorColour };
                var middle = NoChrome();
                var deep = new XPlatLabel(Sample) { Dock = XDock.Top, AutoSize = true };
                var stayed = new XPlatLabel(Sample) { Dock = XDock.Top, AutoSize = true };

                middle.Controls.Add(deep);
                outer.Controls.Add(middle);
                outer.Controls.Add(stayed);
                root.Controls.Add(outer);

                float before = deep.DesiredSize.Width;
                middle.Font = innerFont;
                middle.Foreground = OverrideColour;
                float after = deep.DesiredSize.Width;

                bool overridden = ReferenceEquals(deep.Font, innerFont)
                    && deep.Foreground == OverrideColour
                    && ReferenceEquals(stayed.Font, outerFont)
                    && stayed.Foreground == AncestorColour;

                middle.ClearFont();
                bool restored = ReferenceEquals(deep.Font, outerFont) && ReferenceEquals(deep.Font, stayed.Font);

                return (overridden && restored && after != before,
                    $"deep {before:0.#} -> {after:0.#} px at {innerFont.Size:0.#} pt, back to {deep.Font.Size:0.#} pt");
            })),

            Check("a control that paints no background draws nothing over a coloured ancestor", Scenario((root, host) =>
            {
                var stage = new XPlatPanel
                {
                    Dock = XDock.Fill,
                    Background = XPlatBackground.FromColor(AncestorColour),
                    Padding = new XPlatThickness(0f),
                };

                var label = new XPlatLabel("no box") { Dock = XDock.Top, AutoSize = false, Height = 60f };
                var card = new XPlatPanel { Dock = XDock.Top, Height = 80f };
                card.Controls.Add(new XPlatLabel("a panel that paints"));

                stage.Controls.Add(label);
                stage.Controls.Add(card);
                root.Controls.Add(stage);

                // A label paints nothing at all; the panel inside the same stage does paint, and
                // takes the colour the stage assigned instead of the surface it would use alone.
                bool labelPaintsNothing = label.Background.IsNone && label.BackColor is null;
                bool cardInherits = card.Background.Color == AncestorColour;

                // What is painted where the label is: the ancestor's colour, because the label put
                // nothing of its own in front of it.
                var labelArea = Pixel(host, (int)label.Bounds.Left + 300, (int)label.Bounds.MidY);
                var cardArea = Pixel(host, (int)card.Bounds.Left + 300, (int)card.Bounds.MidY);

                return (labelPaintsNothing && cardInherits && labelArea == AncestorColour && cardArea == AncestorColour,
                    $"label {label.Background} over {labelArea}, card {card.Background.Color} at {cardArea}");
            })),

            Check("a theme font changed after construction re-measures and re-lays out", Scenario((root, host) =>
            {
                var stack = new XPlatStackPanel { Dock = XDock.Fill, ShowFrame = false };
                var label = new XPlatLabel(Sample) { AutoSize = true };
                stack.Controls.Add(label);
                root.Controls.Add(stack);

                float before = label.DesiredSize.Width;
                string beforeFace = label.Font.FamilyName;

                // The same call an application makes at any point in its life, long after its
                // controls exist. Nothing here lays the tree out: the next frame is what notices.
                XPlatTheme.UseFont(XPlatFont.FromBytes(DemoFont.Data));
                try
                {
                    string afterFace = label.Font.FamilyName;
                    Paint(host);
                    float after = label.DesiredSize.Width;

                    bool faceChanged = !string.Equals(beforeFace, afterFace, StringComparison.Ordinal);
                    bool remeasured = MathF.Abs(after - before) > 0.01f;

                    return (faceChanged && remeasured,
                        $"\"{beforeFace}\" {before:0.#} px -> \"{afterFace}\" {after:0.#} px, " +
                        $"bounds {label.Bounds.Width:0.#} x {label.Bounds.Height:0.#}");
                }
                finally
                {
                    XPlatTheme.FontFamily = null;
                }
            })),

            Check("a font assigned to a live subtree re-measures it at once", Scenario((root, host) =>
            {
                using var small = XPlatFont.FromBytes(DemoFont.Data, 9f);
                using var large = XPlatFont.FromBytes(DemoFont.Data, 26f);

                var stack = new XPlatStackPanel { Dock = XDock.Fill, ShowFrame = false, Font = small };
                var label = new XPlatLabel(Sample) { AutoSize = true };
                stack.Controls.Add(label);
                root.Controls.Add(stack);

                float before = label.DesiredSize.Width;
                float heightBefore = label.Bounds.Height;

                // No paint in between: assigning a font has to invalidate the measure itself.
                stack.Font = large;
                float after = label.DesiredSize.Width;

                bool inherits = ReferenceEquals(label.Font, large);
                bool remeasured = after > before;
                bool relaidOut = label.Bounds.Height > heightBefore;

                return (inherits && remeasured && relaidOut,
                    $"{before:0.#} -> {after:0.#} px wide, {heightBefore:0.#} -> {label.Bounds.Height:0.#} px tall");
            })),

            Check("a control built before the theme was configured adopts it", Scenario((root, host) =>
            {
                // Built and measured while the theme is still the platform default.
                var label = new XPlatLabel(Sample) { AutoSize = true };
                var size = new SKSize(float.PositiveInfinity, float.PositiveInfinity);
                label.Measure(size);
                string beforeFace = label.Font.FamilyName;
                float before = label.DesiredSize.Width;

                XPlatTheme.UseFont(XPlatFont.FromBytes(DemoFont.Data));
                try
                {
                    string pinned = XPlatTheme.DefaultFont.FamilyName;
                    label.Measure(size);
                    float after = label.DesiredSize.Width;

                    bool adopted = string.Equals(label.Font.FamilyName, pinned, StringComparison.Ordinal)
                        && !string.Equals(beforeFace, pinned, StringComparison.Ordinal);

                    return (adopted && after != before,
                        $"\"{beforeFace}\" {before:0.#} px -> \"{label.Font.FamilyName}\" {after:0.#} px");
                }
                finally
                {
                    XPlatTheme.FontFamily = null;
                }
            })),

            Check("a palette swap recolours a live control with no rebuild", Scenario((root, host) =>
            {
                var panel = new XPlatPanel { Dock = XDock.Top, Height = Height };
                var label = new XPlatLabel(Sample) { Dock = XDock.Top, AutoSize = true };
                panel.Controls.Add(label);
                root.Controls.Add(panel);

                var lightSurface = panel.Background.Color;
                var lightText = label.Foreground;
                bool followsLight = lightSurface == XPlatTheme.Surface && lightText == XPlatTheme.Foreground;

                XPlatTheme.Palette = XPlatPalette.Dark;
                try
                {
                    var darkSurface = panel.Background.Color;
                    var darkText = label.Foreground;
                    bool followsDark = darkSurface == XPlatTheme.Surface
                        && darkText == XPlatTheme.Foreground
                        && darkSurface != lightSurface
                        && darkText != lightText;

                    // The same object, repainted: the role it was given is resolved while painting.
                    var painted = Pixel(host, 20, (int)panel.Bounds.MidY);
                    bool repainted = painted == XPlatTheme.Surface;

                    return (followsLight && followsDark && repainted,
                        $"{lightSurface} -> {darkSurface}, painted {painted}");
                }
                finally
                {
                    XPlatTheme.Palette = XPlatPalette.Light;
                }
            })),
        };

        int failed = checks.Count(check => !check.Passed);
        var report = new System.Text.StringBuilder();
        report.AppendLine($"Inherited appearance checks on {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}");
        foreach (var check in checks)
        {
            report.AppendLine($"{(check.Passed ? "PASS" : "FAIL")}  {check.Name}  [{check.Detail}]");
        }

        report.AppendLine(failed == 0
            ? $"All {checks.Count} appearance checks passed."
            : $"{failed} of {checks.Count} appearance checks FAILED.");

        Console.Write(report);
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            File.WriteAllText(reportPath, report.ToString());
        }

        return failed == 0 ? 0 : 1;
    }

    /// <summary>An invisible container: the ordinary parent of the controls a check is about.</summary>
    private static XPlatPanel NoChrome() => new()
    {
        Dock = XDock.Fill,
        ShowFrame = false,
        IsCard = false,
        BorderWidth = 0f,
        CornerRadius = 0f,
        Padding = new XPlatThickness(0f),
    };

    /// <summary>
    /// Runs one check inside a real (headless) window, so layout, painting and the host's frame
    /// bookkeeping are the ones an application uses.
    /// </summary>
    private static (bool Passed, string Detail) Scenario(Func<XPlatPanel, XPlatUiHost, (bool Passed, string Detail)> body)
    {
        var root = NoChrome();
        var backend = new HeadlessApplicationBackend();
        using var application = new XPlatApplication(backend);
        var window = new XPlatWindow("appearance checks", root) { ClientSize = new SKSize(Width, Height) };

        (bool Passed, string Detail) outcome = (false, "the window never came up");
        window.AddShownHandler(_ =>
        {
            try
            {
                outcome = body(root, window.Host);
            }
            catch (Exception exception)
            {
                outcome = (false, $"threw {exception.GetType().Name}: {exception.Message}");
            }
        });

        window.Show();
        application.Run();

        // Any check that changed the theme puts it back; this is the belt to that braces.
        XPlatTheme.FontFamily = null;
        XPlatTheme.Palette = XPlatPalette.Light;
        return outcome;
    }

    /// <summary>Names a scenario's outcome. The scenario itself reports an exception as a failure.</summary>
    private static (bool Passed, string Name, string Detail) Check(string name, (bool Passed, string Detail) outcome) =>
        (outcome.Passed, name, outcome.Detail);

    /// <summary>Paints the whole tree and returns the colour at one logical pixel.</summary>
    private static SKColor Pixel(XPlatUiHost host, int x, int y)
    {
        using var bitmap = new SKBitmap((int)Width, (int)Height);
        using var canvas = new SKCanvas(bitmap);
        host.Paint(canvas, full: true);
        canvas.Flush();
        return bitmap.GetPixel(Math.Clamp(x, 0, (int)Width - 1), Math.Clamp(y, 0, (int)Height - 1));
    }

    /// <summary>Paints the whole tree without looking at a pixel: the frame a change is measured on.</summary>
    private static void Paint(XPlatUiHost host)
    {
        using var bitmap = new SKBitmap((int)Width, (int)Height);
        using var canvas = new SKCanvas(bitmap);
        host.Paint(canvas, full: true);
        canvas.Flush();
    }
}
