namespace Kolvir.Backend.Gtk4;

/// <summary>
/// The window's drawing preference on Linux: the GL seam through the <see cref="Gtk.GLArea"/>, or the
/// CPU path.
/// </summary>
/// <remarks>
/// The shape mirrors the other backends' choices, because the application option that fills it is the
/// same one. GTK4 has one accelerated family and nothing to choose between inside it - the GL context
/// belongs to the area GTK made, and GTK does not offer a way to ask for a particular device - so the
/// family has no devices and a value is the bare family word.
/// </remarks>
internal readonly struct Gtk4GfxChoice
{
    private Gtk4GfxChoice(bool openGl, bool cpuOnly)
    {
        OpenGl = openGl;
        CpuOnly = cpuOnly;
    }

    /// <summary>True when the window should try the GL seam.</summary>
    internal bool OpenGl { get; }

    /// <summary>True when the value asked for the CPU path and nothing else.</summary>
    internal bool CpuOnly { get; }

    /// <summary>Try the GL seam, which is what a window that may accelerate does.</summary>
    internal static Gtk4GfxChoice Gl { get; } = new(openGl: true, cpuOnly: false);

    /// <summary>The CPU path, chosen explicitly.</summary>
    internal static Gtk4GfxChoice Cpu { get; } = new(openGl: false, cpuOnly: true);

    /// <summary>
    /// Reads the acceleration an application's user chose, as <see cref="ApplicationOptions.GfxAcceleration"/>
    /// carries it.
    /// </summary>
    /// <param name="value">The saved value, or null when the application has no preference.</param>
    /// <returns>
    /// Null when there is no preference and the backend should decide for itself, otherwise the family
    /// the value named.
    /// </returns>
    /// <remarks>
    /// A value this platform cannot honor - <c>d3d</c> or <c>metal</c> saved on another machine and read
    /// here, or something hand-edited into nonsense - is the CPU path: the user asked for a specific
    /// thing, and the honest answer when it is not here is to stop accelerating rather than substitute.
    /// A device suffixed onto <c>ogl</c> is accepted and ignored, because GTK has no device to pick.
    /// </remarks>
    internal static Gtk4GfxChoice? FromSelected(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!GpuAccelerationSyntax.TryParse(value, out var kind, out _))
        {
            if (Gtk4GlTrace.IsEnabled)
            {
                Gtk4GlTrace.Write($"kolvir-gfx: ignoring unreadable acceleration '{value}'");
            }

            return Cpu;
        }

        return kind == GpuAccelerationKind.OpenGl ? Gl : Cpu;
    }
}

/// <summary>
/// Reads <c>KOLVIR_GL_OPTIONS</c>, the process-wide variable that restricts which tiers a window may
/// use without an application option and without a rebuild.
/// </summary>
/// <remarks>
/// <para>
/// The same variable the Win32 backend honours, with the words this platform has: <c>ogl</c> for the GL
/// seam and <c>cpu</c> for the CPU path. It exists so an A/B against acceleration needs no setting, no
/// rebuild and no change to the application - <c>KOLVIR_GL_OPTIONS=cpu</c> runs this build without
/// touching a GPU.
/// </para>
/// <para>
/// It takes the place the application option has when the application names none, so an explicit
/// <see cref="ApplicationOptions.GfxAcceleration"/> still wins. A value with nothing recognizable in it
/// - including this platform's sibling words <c>d3d</c> and <c>metal</c> - says nothing at all and
/// leaves the backend's own default in charge, rather than being read as a request for the CPU path: a
/// variable tuned for one platform must not silently disable another.
/// </para>
/// </remarks>
internal static class Gtk4GfxOptions
{
    /// <summary>The variable the process is configured with.</summary>
    internal const string VariableName = "KOLVIR_GL_OPTIONS";

    /// <summary>Reads the process's environment once.</summary>
    internal static Gtk4GfxChoice? FromEnvironment() =>
        Parse(Environment.GetEnvironmentVariable(VariableName));

    /// <summary>Parses a value of <see cref="VariableName"/>.</summary>
    /// <param name="value">The raw value, or null when the variable is not set.</param>
    /// <returns>
    /// The choice the value asks for, or null when it asks for nothing this platform understands.
    /// </returns>
    internal static Gtk4GfxChoice? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sawOpenGl = false;
        var sawCpu = false;

        foreach (var entry in value.Split(',', StringSplitOptions.TrimEntries))
        {
            switch (entry.ToLowerInvariant())
            {
                case "ogl":
                    sawOpenGl = true;
                    break;

                case "cpu":
                    sawCpu = true;
                    break;

                // A tier this platform does not have, or a typo: ignored, so one stray word does not
                // throw the rest away.
                default:
                    break;
            }
        }

        if (sawOpenGl)
        {
            // `cpu` beside a tier is ignored, exactly as it is on Win32: a list that names a tier is
            // asking for acceleration.
            return Gtk4GfxChoice.Gl;
        }

        return sawCpu ? Gtk4GfxChoice.Cpu : null;
    }
}
