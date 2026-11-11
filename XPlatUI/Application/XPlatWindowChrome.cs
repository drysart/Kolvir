namespace XPlatUI;

/// <summary>
/// How much of a window the platform draws, versus how much the application draws.
/// </summary>
/// <remarks>
/// <para>
/// There is no third value. A window is either decorated by the platform in the ordinary way, or the
/// application owns every visible pixel edge to edge; "custom chrome with a platform frame" would
/// only be a rename of <see cref="Borderless"/> with extra words.
/// </para>
/// <para>
/// <see cref="Borderless"/> does not turn the window into a popup. The platform keeps its resize
/// band, snapping, tiling, animations, shadow and minimise behaviour, and the band sits outside the
/// apparent window rather than being carved out of the client area. What the platform still owns in
/// that mode is described by <see cref="XPlatWindowChromeMetrics"/>.
/// </para>
/// </remarks>
public enum XPlatWindowChrome
{
    /// <summary>
    /// The platform draws the title bar and frame. This is the ordinary window, and the default.
    /// </summary>
    System,

    /// <summary>
    /// The application owns every visible pixel; the platform draws no title bar but keeps the
    /// window's frame behaviour.
    /// </summary>
    Borderless,
}
