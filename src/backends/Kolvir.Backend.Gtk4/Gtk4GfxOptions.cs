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
