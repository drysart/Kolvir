namespace Kolvir;

/// <summary>Chooses who draws an application's menus.</summary>
public enum MenuPresentation
{
    /// <summary>
    /// The library's own menus, drawn with Skia, on every platform.
    /// </summary>
    /// <remarks>
    /// The same as <see cref="Kolvir"/>; the platform's menus are never picked implicitly, so a
    /// window looks the same wherever it runs unless the application asks otherwise with
    /// <see cref="Native"/>.
    /// </remarks>
    Auto,

    /// <summary>
    /// Always the library's own menus, drawn with Skia. They look the same on every platform and
    /// support icons, submenus and controls in a row.
    /// </summary>
    /// <remarks>
    /// A window's menu bar is the one exception: macOS has a single menu bar for the whole process,
    /// owned by AppKit and drawn at the top of the screen, so the bar is the system's there whichever
    /// presentation is asked for. The rest of the library's menus - context menus, the drop-downs
    /// under a bar - are still drawn with Skia on macOS.
    /// </remarks>
    Kolvir,

    /// <summary>
    /// Always the platform's own menus: the system menu bar on macOS, the native popup menus on
    /// Windows and GTK4.
    /// </summary>
    Native,
}

/// <summary>Resolves <see cref="MenuPresentation"/> for a platform.</summary>
public static class MenuPresentationExtensions
{
    /// <summary>
    /// True when <paramref name="presentation"/> means the platform's own menus on the platform the
    /// process is running on.
    /// </summary>
    /// <remarks>
    /// Only <see cref="MenuPresentation.Native"/> asks for the platform's menus. A window that
    /// is drawn with Skia is drawn with Skia's menus too, on macOS as much as anywhere else, so a
    /// context menu or a bar does not change shape between platforms. The bar is the exception on
    /// macOS, where the system's menu bar is the only one the platform shows: it is used whatever
    /// this reports, and what is lost with it is documented on <see cref="MenuBar"/>.
    /// </remarks>
    public static bool UsesNativeMenus(this MenuPresentation presentation) =>
        presentation == MenuPresentation.Native;
}
