namespace XPlatUI;

/// <summary>Chooses who draws an application's menus.</summary>
public enum XPlatMenuPresentation
{
    /// <summary>
    /// The library's own menus, drawn with Skia, on every platform.
    /// </summary>
    /// <remarks>
    /// The same as <see cref="XPlatUI"/>; the platform's menus are never picked implicitly, so a
    /// window looks the same wherever it runs unless the application asks otherwise with
    /// <see cref="Native"/>.
    /// </remarks>
    Auto,

    /// <summary>
    /// Always the library's own menus, drawn with Skia. They look the same on every platform and
    /// support icons, submenus and controls in a row.
    /// </summary>
    /// <remarks>
    /// On macOS the window's bar is drawn inside the window as well, and the system's own menu bar
    /// is left with nothing but the application menu, which is what the platform requires to be
    /// present at all.
    /// </remarks>
    XPlatUI,

    /// <summary>
    /// Always the platform's own menus: the system menu bar on macOS, the native popup menus on
    /// Windows and GTK4.
    /// </summary>
    Native,
}

/// <summary>Resolves <see cref="XPlatMenuPresentation"/> for a platform.</summary>
public static class XPlatMenuPresentationExtensions
{
    /// <summary>
    /// True when <paramref name="presentation"/> means the platform's own menus on the platform the
    /// process is running on.
    /// </summary>
    /// <remarks>
    /// Only <see cref="XPlatMenuPresentation.Native"/> asks for the platform's menus. A window that
    /// is drawn with Skia is drawn with Skia's menus too, on macOS as much as anywhere else, so a
    /// context menu or a bar does not change shape between platforms.
    /// </remarks>
    public static bool UsesNativeMenus(this XPlatMenuPresentation presentation) =>
        presentation == XPlatMenuPresentation.Native;
}
