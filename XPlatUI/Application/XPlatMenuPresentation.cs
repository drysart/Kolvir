namespace XPlatUI;

/// <summary>Chooses who draws an application's menus.</summary>
public enum XPlatMenuPresentation
{
    /// <summary>
    /// The library's own menus on Windows and Linux, the platform's on macOS.
    /// </summary>
    /// <remarks>
    /// macOS is the exception because AppKit owns the one menu bar the system shows at the top of
    /// the screen, and the standard Edit menu, the application menu and the shortcuts that go with
    /// them come from being in it. A window-drawn bar there would sit inside the window and leave
    /// the system bar empty.
    /// </remarks>
    Auto,

    /// <summary>
    /// Always the library's own menus, drawn with Skia. They look the same on every platform and
    /// support icons, submenus and controls in a row.
    /// </summary>
    /// <remarks>
    /// A window's bar is still the system's on macOS, because there is only one menu bar there and
    /// it belongs to the application; what changes is the context menus, which are drawn by the
    /// library instead of by AppKit.
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
    public static bool UsesNativeMenus(this XPlatMenuPresentation presentation) => presentation switch
    {
        XPlatMenuPresentation.Native => true,
        XPlatMenuPresentation.XPlatUI => false,
        _ => OperatingSystem.IsMacOS(),
    };
}
