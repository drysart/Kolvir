using Microsoft.Extensions.DependencyInjection;

namespace XPlatUI;

/// <summary>
/// Configuration every platform needs but that is not specific to one window. Handed to the
/// <see cref="XPlatApplication"/> constructor.
/// </summary>
public sealed class XPlatApplicationOptions
{
    private bool _gfxHardwareAcceleration = true;
    private bool _gfxHardwareAccelerationAssigned;
    private IReadOnlyList<string>? _commandLine;

    /// <summary>Creates options with defaults taken from this process's own command line.</summary>
    public XPlatApplicationOptions()
        : this(null)
    {
    }

    /// <summary>
    /// Creates options with defaults taken from <paramref name="commandLine"/> - the form to use
    /// when a launcher supplies the arguments itself rather than letting the process's own command
    /// line decide.
    /// </summary>
    /// <param name="commandLine">
    /// The arguments the application was started with, without the executable path, or null to read
    /// <see cref="Environment.GetCommandLineArgs"/>.
    /// </param>
    public XPlatApplicationOptions(IReadOnlyList<string>? commandLine)
    {
        CommandLine = commandLine;
        _gfxHardwareAcceleration = !HasNoHardwareAccelerationSwitch(ResolveCommandLine());
    }

    /// <summary>
    /// The application's name: the macOS menu bar's first entry, the taskbar entry, the about box.
    /// Null leaves it to the backend, which usually takes it from the executable.
    /// </summary>
    public string? ApplicationName { get; set; }

    /// <summary>What ends the message loop. Defaults to <see cref="XPlatShutdownMode.OnLastWindowClose"/>.</summary>
    public XPlatShutdownMode ShutdownMode { get; set; } = XPlatShutdownMode.OnLastWindowClose;

    /// <summary>
    /// True to keep one process per user: a second launch hands its command line to the running
    /// instance and exits instead of starting a competing one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The running instance learns about the launch through a handler added with
    /// <see cref="XPlatApplication.AddSecondInstanceHandler"/> and comes to the
    /// foreground. A second launch's <see cref="XPlatApplication.Run"/> returns 0
    /// before any handler added with <see cref="XPlatApplication.AddStartupHandler"/>
    /// runs, so nothing is ever shown by it.
    /// </para>
    /// <para>
    /// Windows and a direct macOS launch get this from <see cref="XPlatInstanceLock"/>; GTK4 uses
    /// <c>Gio.Application</c>'s own uniqueness, which is also the only way to raise a window from
    /// another process under Wayland. A Finder or Dock launch on macOS is single-instance whether or
    /// not this is set, because the platform routes it to the running application.
    /// </para>
    /// </remarks>
    public bool SingleInstance { get; set; }

    /// <summary>
    /// What identifies the application for <see cref="SingleInstance"/>. Null uses
    /// <see cref="ApplicationName"/>, and failing that the entry assembly's name.
    /// </summary>
    /// <remarks>
    /// Set this when two different applications built on this library would otherwise share a name,
    /// or when the name is not a legal platform identifier: each backend sanitises it, and GTK4
    /// additionally turns it into the reverse-DNS application id D-Bus requires.
    /// </remarks>
    public string? InstanceKey { get; set; }

    /// <summary>
    /// The command line arguments this launch was started with, without the executable path. A
    /// second launch's are delivered in <see cref="XPlatSecondInstanceEventArgs.Arguments"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null reads <see cref="Environment.GetCommandLineArgs"/>. A launcher that adds switches of its
    /// own, such as <c>--render</c> in the test harness, should pass only the arguments the
    /// application understands.
    /// </para>
    /// <para>
    /// Assigning this property after construction re-evaluates the command-line default of
    /// <see cref="GfxHardwareAcceleration"/> until that property has been assigned explicitly. A
    /// launcher that assigns this property after the options exist is still advised to assign
    /// <see cref="GfxHardwareAcceleration"/> in the same place: the application may already have
    /// read the options, and an explicit assignment always wins over the switch.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string>? CommandLine
    {
        get => _commandLine;
        set
        {
            _commandLine = value;

            if (!_gfxHardwareAccelerationAssigned)
            {
                _gfxHardwareAcceleration = !HasNoHardwareAccelerationSwitch(ResolveCommandLine());
            }
        }
    }

    /// <summary>
    /// The arguments a forwarded launch carries: <see cref="CommandLine"/>, or this process's own
    /// command line without the executable path when that is null.
    /// </summary>
    /// <returns>The arguments, never null.</returns>
    public IReadOnlyList<string> ResolveCommandLine() =>
        CommandLine ?? Environment.GetCommandLineArgs()[1..];

    /// <summary>
    /// True to let a backend draw with an accelerated surface when the machine has a working GPU
    /// implementation. True by default, and false when <see cref="ResolveCommandLine"/> carries
    /// <c>--no-hardware-acceleration</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On Windows this is a plain opt-out: true (the default) lets the Win32 backend try WGL, and a
    /// failure falls back to the DIB. On macOS the AppKit backend requires an explicit assignment
    /// (see <see cref="GfxHardwareAccelerationAssigned"/>): the physical-Mac measurements are
    /// content-dependent - Metal is much faster on image-heavy pages and slower on light
    /// text-heavy ones - so the default is the CPU path and an application opts into Metal by
    /// assigning this property. The demo does so explicitly.
    /// </para>
    /// <para>
    /// The constructor reads the command line once, from <see cref="CommandLine"/> when that is
    /// assigned or from the process's own arguments otherwise. Because
    /// <see cref="CommandLine"/> is settable after construction, a launcher that assigns it later
    /// cannot retroactively change a value the application has already read; assigning it
    /// re-evaluates the default only until this property itself is assigned. Assign both properties
    /// together when the launcher owns the command line - <c>{ CommandLine = args,
    /// GfxHardwareAcceleration = !args.Contains("--no-hardware-acceleration") }</c> - rather than
    /// relying on the order.
    /// </para>
    /// <para>
    /// An explicit assignment always wins, so setting this to true after a
    /// <c>--no-hardware-acceleration</c> launch keeps acceleration on. The setting is a decision the
    /// window makes when it allocates its drawing surface: the fallback to the CPU path is silent
    /// and the window never fails because acceleration was requested but unavailable. Popups,
    /// menus and headless surfaces ignore it.
    /// </para>
    /// </remarks>
    public bool GfxHardwareAcceleration
    {
        get => _gfxHardwareAcceleration;
        set
        {
            _gfxHardwareAcceleration = value;
            _gfxHardwareAccelerationAssigned = true;
        }
    }

    /// <summary>
    /// True when the application assigned <see cref="GfxHardwareAcceleration"/> rather than leaving
    /// the constructor's default. The AppKit backend requires this on macOS, because Metal is
    /// strictly opt-in there; the Win32 backend does not need it.
    /// </summary>
    public bool GfxHardwareAccelerationAssigned => _gfxHardwareAccelerationAssigned;

    private static bool HasNoHardwareAccelerationSwitch(IReadOnlyList<string> arguments)
    {
        for (int i = 0; i < arguments.Count; i++)
        {
            if (string.Equals(arguments[i], "--no-hardware-acceleration", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True to follow the desktop's light/dark preference for the lifetime of the application.
    /// </summary>
    /// <remarks>
    /// Has no effect unless the backend registers an <see cref="IXPlatSystemTheme"/>: an
    /// application with no platform appearance to follow keeps whatever
    /// <see cref="XPlatTheme.Palette"/> is already in use. That is what keeps a headless render
    /// deterministic regardless of how the host machine is configured.
    /// </remarks>
    public bool FollowSystemAppearance { get; set; } = true;

    /// <summary>
    /// Forces a dark (true) or light (false) palette and ignores the desktop's preference. This is
    /// where a <c>--dark</c> or <c>--light</c> command line switch belongs; null means "do not force".
    /// </summary>
    public bool? ForcedDarkMode { get; set; }

    /// <summary>
    /// The font family the built-in controls draw with. Null uses the operating system's default,
    /// which is what a headless render does.
    /// </summary>
    /// <remarks>
    /// A family name resolves to a different physical face on each platform, and this option cannot
    /// carry a font of your own: an option is read by <see cref="XPlatApplication"/> at start-up,
    /// which is a type every application runs, so a font read there would keep the loaders in every
    /// published application. Call <see cref="XPlatTheme.UseFont"/> from application code before the
    /// windows are created instead - <c>XPlatTheme.UseFont(XPlatFont.FromFile(path))</c> - when the
    /// same metrics everywhere matter.
    /// </remarks>
    public string? FontFamily { get; set; }

    /// <summary>How the process responds to display scaling. Defaults to <see cref="XPlatDpiAwareness.PerMonitor"/>.</summary>
    public XPlatDpiAwareness DpiAwareness { get; set; } = XPlatDpiAwareness.PerMonitor;

    /// <summary>
    /// Windows only: attach to the parent console so diagnostics from a GUI executable are visible
    /// when it is started from a terminal.
    /// </summary>
    public bool AttachConsole { get; set; }

    /// <summary>
    /// The menu bar every window uses unless it sets <see cref="XPlatWindow.MenuBar"/> itself. On
    /// macOS it is also mirrored into the system menu bar, which the platform requires to exist.
    /// </summary>
    public XPlatMenuBar? MenuBar { get; set; }

    /// <summary>
    /// Who draws the menus: the library or the platform. Defaults to
    /// <see cref="XPlatMenuPresentation.Auto"/>, which is the library's own menus on every platform,
    /// macOS included.
    /// </summary>
    /// <remarks>
    /// The library's menus are the same Skia drawing on every platform, so they support icons,
    /// submenus and controls in a menu row; a platform menu supports whatever that platform's menu
    /// API does. Windows keeps <c>--classic-menus</c> as a further step down to <c>TrackPopupMenuEx</c>
    /// for comparison.
    /// </remarks>
    public XPlatMenuPresentation MenuPresentation { get; set; } = XPlatMenuPresentation.Auto;

    /// <summary>
    /// The icon every window starts with, and the icon the desktop shows for the application: the
    /// taskbar and Alt-Tab on Windows, the Dock on macOS. Null keeps the platform's generic icon.
    /// </summary>
    /// <remarks>
    /// Set it before the application is created: the platform applies it during initialization,
    /// which is the only moment macOS lets an application change its Dock icon without a bundle
    /// resource. A window may still override it with <see cref="XPlatWindow.Icon"/>.
    /// </remarks>
    public XPlatIcon? Icon { get; set; }

    /// <summary>
    /// Registers the application's own services. Runs after the backend has registered the platform
    /// services and before the container is built, so registering a service the backend already
    /// provides replaces it.
    /// </summary>
    /// <remarks>
    /// The parameter is MEDI's own <see cref="IServiceCollection"/>, so every <c>Add*</c> extension
    /// from every package works unchanged. Keep to the AOT-safe subset: generic
    /// <c>AddSingleton&lt;TService, TImpl&gt;()</c>, factory overloads, and
    /// <see cref="XPlatServiceCollectionExtensions.AddWindowScoped{TService}"/>. Assembly scanning
    /// is the one common pattern that is not trimming-safe.
    /// </remarks>
    public Action<IServiceCollection>? ConfigureServices { get; set; }
}
