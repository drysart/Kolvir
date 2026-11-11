using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// Whether the platform draws the window's caption buttons, independently of how much of the rest of
/// the window it draws.
/// </summary>
/// <remarks>
/// <para>
/// This is a request, and <see cref="XPlatWindowChromeMetrics.CaptionButtons"/> is the answer: an
/// application must read back what it got rather than assume it got what it asked for.
/// </para>
/// <para>
/// Where the platform has buttons of its own - the macOS traffic lights, GTK's
/// <c>Gtk.WindowControls</c> - they are real controls that take their own clicks and are visible to
/// assistive technology. On Windows there is no caption to draw in a borderless window, so the
/// backend draws and hit-tests them itself, which is deliberately indistinguishable from the API's
/// point of view.
/// </para>
/// </remarks>
public enum XPlatCaptionButtons
{
    /// <summary>
    /// No platform buttons. The application draws and handles whatever it wants in their place.
    /// </summary>
    None,

    /// <summary>
    /// The platform draws the buttons and claims presses inside them.
    /// </summary>
    Platform,
}

/// <summary>
/// One caption button the platform draws.
/// </summary>
/// <remarks>
/// <see cref="Maximize"/> and <see cref="Restore"/> are separate kinds rather than one button in two
/// states, because a platform reports the one it is showing: a maximised window reports
/// <see cref="Restore"/> where a restored one reports <see cref="Maximize"/>.
/// </remarks>
public enum XPlatCaptionButtonKind
{
    /// <summary>Closes the window.</summary>
    Close,

    /// <summary>Minimises the window.</summary>
    Minimize,

    /// <summary>Maximises the window; shown while the window is restored.</summary>
    Maximize,

    /// <summary>Restores a maximised window; shown while the window is maximised.</summary>
    Restore,
}

/// <summary>
/// Which end of the title bar the platform puts its caption buttons at.
/// </summary>
/// <remarks>
/// Fixed per platform - macOS keeps its traffic lights on the left, Windows and GTK put theirs on the
/// right - but reported rather than assumed, because a GTK decoration layout can move them.
/// </remarks>
public enum XPlatCaptionButtonSide
{
    /// <summary>Leading edge; the left in a left-to-right layout.</summary>
    Left,

    /// <summary>Trailing edge; the right in a left-to-right layout.</summary>
    Right,
}

/// <summary>
/// A caption button the platform draws, and where it is.
/// </summary>
/// <remarks>
/// <see cref="Bounds"/> is in logical pixels, in client coordinates, so an application can keep its
/// own content out of the way without knowing anything about the platform. Never compute these
/// numbers: they differ by platform, by theme, by GTK version and by DPI - a Windows button is
/// 92 x 43 at 192 DPI while a macOS traffic light is 14 x 14.
/// </remarks>
/// <param name="Kind">Which button this is.</param>
/// <param name="Bounds">Where the button sits, in client coordinates.</param>
public readonly record struct XPlatCaptionButton(XPlatCaptionButtonKind Kind, SKRect Bounds);
