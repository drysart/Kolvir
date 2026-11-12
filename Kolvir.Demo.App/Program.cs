using SkiaSharp;
using Kolvir;
using Kolvir.Animations;
using Kolvir.Badges;
using Kolvir.Demo;
using Kolvir.DragDrop;
using Kolvir.Svg;

#if WINDOWS
using Kolvir.Backend.Win32;
#elif MACOS
using Kolvir.Backend.AppKit;
#else
using Kolvir.Backend.Gtk4;
#endif

namespace Kolvir.Demo.App;

/// <summary>
/// The one application, built for whichever platform is compiling it. Everything platform specific
/// lives behind <see cref="Kolvir.IApplicationBackend"/>, so the only difference between the
/// three builds is which backend this file names and how it reads the desktop's font and
/// appearance.
/// </summary>
/// <remarks>
/// Each OS builds exactly one target framework (<c>net10.0-windows</c>, <c>net10.0-macos</c> or
/// <c>net10.0</c>), which is what makes the conditional compilation below honest: a portable
/// <c>net10.0</c> build on a Mac defines neither <c>WINDOWS</c> nor <c>MACOS</c>, so the TFM has to
/// be right rather than the symbols.
/// </remarks>
internal static class Program
{
#if WINDOWS || MACOS
    /// <summary>The sample UI is designed against this logical (96 DPI) client size.</summary>
    private static readonly SKSize DesignClientSize = new(1280f, 820f);

    /// <summary>Below this the sample layout has nothing left to squeeze.</summary>
    private static readonly SKSize DesignMinimumClientSize = new(820f, 716f);
#else
    /// <summary>
    /// The same 1280 x 760 drawing area, expressed as a client size: GTK has no global menu bar, so
    /// the window packs its own inside the client area and it eats the difference.
    /// </summary>
    private static readonly SKSize DesignClientSize = new(1280f, 760f);

    /// <summary>Below this the sample layout has nothing left to squeeze.</summary>
    private static readonly SKSize DesignMinimumClientSize = new(820f, 660f);
#endif

    /// <summary>
    /// Entry point. The program deliberately avoids Windows Forms and System.Drawing: the window is
    /// a native window and everything inside it is drawn with SkiaSharp, so the whole application
    /// stays trimmable and can be published with NativeAOT.
    /// </summary>
    /// <remarks>
    /// The attribute is not decoration: the runtime reads it before <c>Main</c> runs and
    /// initializes the thread as a single-threaded COM apartment, which is the only thing that can
    /// do so. Windows needs it for the shell's file dialogs, whose <c>IFileDialog::Show</c> never
    /// returns on a multi-threaded apartment. It is ignored on macOS and Linux.
    /// </remarks>
    [STAThread]
    private static int Main(string[] args)
    {
        // Must happen before anything touches SkiaSharp: an AOT publish carries the native Skia
        // library inside the executable instead of next to it. On macOS the library stays in the
        // bundle, so the loader finds it where it expects it and does nothing. The resource is
        // embedded in this assembly by Kolvir's build assets, which is why it is named here.
        SkiaNativeLoader.EnsureLoaded(typeof(Program).Assembly);

        // Headless render of the sample UI, handy for visual checks and CI. It paints through the
        // headless backend rather than the window one, so it needs no window, no message loop and no
        // display, and it works over ssh:
        //   KolvirDemoApp --render output.png [width height] [--dark|--light]
        //   KolvirDemoApp --render output.png 1200 800 --page 55 [--zoom 2.0 | --zoom-popup dropdown]
        //
        // --page picks a page by index, --zoom drives the Zoom page's slider and --zoom-popup opens
        // one of that page's popup surfaces (dropdown, menu or tooltip), so a screenshot of
        // something that normally needs a click can be reproduced from any machine.
        //
        // This has to come before anything touches a toolkit. GDK's initialisation prints
        // "Failed to open display" and terminates the process with exit code 1 when there is no
        // display, and it does so from Gtk.Module.Initialize() - too early for InitCheck to report
        // it - so a render path that loads GTK cannot run on a headless machine. Nothing in the
        // renderer needs GTK: DemoUi.RenderToPng is pure Kolvir plus SkiaSharp.
        if (args.Length > 0 && args[0] == "--render")
        {
            return RenderSample(args);
        }

        // The undo history, the font loaders and the inherited appearance model used to have their
        // own headless harnesses here. They are Kolvir.Tests now: the same scenarios as real tests,
        // so they run in CI on all three platforms instead of only where someone remembered to pass
        // a flag.

#if !WINDOWS && !MACOS
        // Registers the native library resolver for the GTK/GDK/GLib bindings and opens the display,
        // so without one it ends the process with exit code 1. The backend does this too, but the
        // desktop font has to be read from GTK before the options are built.
        Gtk.Module.Initialize();
#endif

        var options = new ApplicationOptions
        {
            ApplicationName = "Kolvir",
            FontFamily = PreferredFontFamily(),
            ForcedDarkMode = ForcedDarkMode(args),
            MenuPresentation = MenuPresentationArgument(args),

            // Assign the switch explicitly so the demo is the showcase on every platform. Windows
            // and Linux already default it on, but macOS defaults it off (Metal is slower on light
            // content), so this is what turns the accelerated path on there.
            // --no-hardware-acceleration is the off switch everywhere.
            GfxHardwareAcceleration = Array.IndexOf(args, "--no-hardware-acceleration") < 0,

            // The application-wide icon: the Dock tile on macOS, the taskbar and Alt-Tab on Windows,
            // and the default icon name for every GTK window. Windows also shows it in the title bar
            // because Window inherits this when it is created.
            Icon = DemoIcon.Create(),

            // Launching the sample twice re-uses the window that is already open instead of opening
            // a second one, which is what makes "open a file with this application" work on all
            // three platforms.
            SingleInstance = true,

            // The sample opts into six features, each for a page that uses it: drag and drop for
            // its drops, animations for the easing pages, SVG for the icon pages, the animated
            // GIF and WebP providers for their picture pages, and app badging for the badge page.
            // Each Add call is the whole opt-in; leaving one out keeps that feature's code out of
            // an application that does not want it.
            ConfigureServices = services => services
                .AddDragDrop()
                .AddAnimations()
                .AddSvgImages()
                .AddAnimatedGifImages()
                .AddAnimatedWebpImages()
                .AddAppBadges(),
#if WINDOWS
            // A WinExe has no console of its own; borrow the parent's so diagnostics are visible.
            AttachConsole = true,
#endif
        };

        using var application = new Application(CreateBackend(args), options);

        // --embedded-font makes the face the sample carries its own application-wide font. It is
        // called here, in application code, rather than exposed as an application option: an option
        // is read by the library at start-up, and that would keep the font loaders in every
        // published application whether or not it loads a font.
        if (Array.IndexOf(args, "--embedded-font") >= 0)
        {
            Theme.UseFont(Font.FromBytes(DemoFont.Data));
        }

        var demo = new DemoUi();
        var window = new Window(WindowTitle, demo.Root)
        {
            ClientSize = DesignClientSize,
            MinimumClientSize = DesignMinimumClientSize,
        };

        // Attached on every platform, and drawn by the library on every platform. macOS keeps the
        // system bar in step with it as well; the shortcuts it carries work on all three, because
        // the window checks the bar before the focused control sees a key.
        window.MenuBar = CreateMenuBar(window, demo);


        application.MainWindow = window;

        // The window comes forward on its own; the status line says why it did.
        application.AddSecondInstanceHandler((_, e) => demo.SetStatus(
            e.Arguments.Count == 0
                ? "A second launch was handed to this window."
                : $"A second launch was handed to this window: {string.Join(' ', e.Arguments)}"));

        // Activation is the other half of the multi-window story: the status line reports it, which
        // is also the easiest way to see that an owned window taking the focus deactivates this one.
        window.AddActivatedHandler(_ => demo.SetStatus("Window activated - it has the keyboard focus."));
        window.AddDeactivatedHandler(_ => demo.SetStatus("Window deactivated - another window has the focus."));

        // Windows has no menu bar to hang these off yet, so the same two window features are also
        // reachable as flags: --topmost starts above everything, --owned opens a window owned by
        // this one.
        window.TopMost = Array.IndexOf(args, "--topmost") >= 0;

        // The chromeless page's two switches, for the same reason: a screenshot of a borderless
        // window needs no pointer to take, and a window with no caption has nowhere to hang a menu.
        if (Array.IndexOf(args, "--chromeless") >= 0)
        {
            if (window.ChromeCapabilities.Borderless)
            {
                window.Chrome = WindowChrome.Borderless;
            }
            else
            {
                demo.SetStatus("This backend cannot make a window borderless.");
            }
        }

        if (ArgumentValue(args, "--caption-buttons") is { } captionButtons)
        {
            window.CaptionButtons = string.Equals(captionButtons, "platform", StringComparison.OrdinalIgnoreCase)
                ? CaptionButtons.Platform
                : CaptionButtons.None;
        }

        // The chromeless page drives the window the sample is shown in, so it needs one - and a page is
        // built before there is a window to ask. Attached after the flags above so the readout starts
        // out describing the window it is actually in rather than the one it would have been.
        demo.AttachWindow(window);

        demo.SelectPage(PageArgument(args));

        if (Array.IndexOf(args, "--benchmark") >= 0)
        {
            // The same entry point as the Measure button, so a script can capture the BENCH line
            // without a pointer. It no-ops unless the measured-rendering page is the one showing.
            // --partial-damage selects the row the on-page toggle would, so all four rows can be
            // captured headlessly.
            var partialDamage = Array.IndexOf(args, "--partial-damage") >= 0;
            window.AddShownHandler(_ => demo.StartBenchmark(partialDamage));
        }

        // Opens the menus page's context menu as if the box had been right-clicked, which is what a
        // screenshot or a script needs in order to see a menu without hunting for the box. A popup
        // needs a host, and a page has none until the window is up, so this waits for that moment -
        // which means the page has to be selected before the window is shown.
        if (Array.IndexOf(args, "--menu") >= 0)
        {
            window.AddShownHandler(_ => demo.OpenMenuDemo(window));
        }

        // The same idea for the spans page: right-click a squiggle as if the pointer had gone there,
        // so a screenshot shows the menu - and the readout - naming the span that was hit.
        if (Array.IndexOf(args, "--span-menu") >= 0)
        {
            window.AddShownHandler(_ => demo.OpenSpanDemo(window));
        }

        // And for the undo page: put the caret in its field, so a script can drive the history
        // without hunting for the box with the pointer.
        if (Array.IndexOf(args, "--undo-focus") >= 0)
        {
            window.AddShownHandler(_ => demo.FocusUndoDemo());
        }

        // The measured-rendering page reports the live scale alongside the client size; the window
        // is the only place that knows it.
        demo.SetScaleProvider(() => window.ScaleFactor);

#if WINDOWS
        // One line that says which presentation path the window took. The backend creates its
        // surface when the window is really shown - Show() before Run() only queues it - so this
        // hangs off Shown rather than following the call. On a machine with no OpenGL ICD it also
        // says why the fallback happened, which is the only way to tell "acceleration worked" from
        // "acceleration quietly did not" when watching a real desktop.
        window.AddShownHandler(_ =>
        {
            if (window.Backend is Win32WindowBackend win32)
            {
                demo.SetGraphicsInfo(win32.GraphicsPath, win32.GraphicsRenderer);
                Console.WriteLine(
                    win32.GraphicsRenderer is { } renderer
                        ? $"gfx: {win32.GraphicsPath}; renderer: {renderer}"
                        : $"gfx: {win32.GraphicsPath}");
            }
        });
#elif MACOS
        // The AppKit backend makes the same decision the Win32 one does: Metal when the option is
        // on and every step of the stack came up, otherwise the CPU CGImage path with the reason.
        // The footer and the measured-rendering page then describe macOS exactly as they do Windows.
        window.AddShownHandler(_ =>
        {
            if (window.Backend is AppKitWindowBackend appKit)
            {
                demo.SetGraphicsInfo(appKit.GraphicsPath, appKit.GraphicsRenderer);
                Console.WriteLine(
                    appKit.GraphicsRenderer is { } renderer
                        ? $"gfx: {appKit.GraphicsPath}; renderer: {renderer}"
                        : $"gfx: {appKit.GraphicsPath}");
            }
        });
#else
        // The GTK4 backend makes the same decision the other two do: GL when the option is on and
        // every step of the seam came up, otherwise the CPU texture with the reason. Unlike Win32
        // and AppKit it cannot make that decision while showing the window - a GL context only
        // exists once the area has been allocated and drawn - so the line is printed when the
        // decision is made, which is the first render pass.
        window.AddShownHandler(_ =>
        {
            if (window.Backend is Gtk4WindowBackend gtk4)
            {
                void Report()
                {
                    demo.SetGraphicsInfo(gtk4.GraphicsPath, gtk4.GraphicsRenderer);
                    Console.WriteLine(
                        gtk4.GraphicsRenderer is { } renderer
                            ? $"gfx: {gtk4.GraphicsPath}; renderer: {renderer}"
                            : $"gfx: {gtk4.GraphicsPath}");
                }

                gtk4.AddGraphicsPathChangedHandler(_ => Report());
            }
        });
#endif

        window.Show();

        if (Array.IndexOf(args, "--focus") >= 0)
        {
            demo.FocusFirstTextBox();
        }

        if (Array.IndexOf(args, "--owned") >= 0)
        {
            demo.ShowOwnedWindow();
        }

        return application.Run();
    }

    /// <summary>
    /// The window's own menu bar, on the platforms that show one. Each item carries a shortcut so
    /// the same commands are reachable from the keyboard on all three platforms, including the one
    /// that does not draw the bar yet.
    /// </summary>
    /// <remarks>
    /// Both menus put their commands inside a submenu, and <c>View &gt; Theme &gt; Appearance</c> goes
    /// one level deeper still, so the sample covers the case that only the system bar can prove:
    /// macOS hands the whole chain to AppKit, which draws and drives the nested menus itself. The
    /// shortcuts sit inside those submenus on purpose - a shortcut nested two levels down still has
    /// to reach the command on every platform.
    /// </remarks>
    private static MenuBar CreateMenuBar(Window window, DemoUi demo)
    {
        var appearance = new MenuItem("&Appearance");
        appearance.SubItems.Add(new MenuItem("&Dark Mode", () => demo.SetAppearance(Palette.Dark))
        {
            Shortcut = new Shortcut(Key.D),
        });
        appearance.SubItems.Add(new MenuItem("&Light Mode", () => demo.SetAppearance(Palette.Light))
        {
            Shortcut = new Shortcut(Key.L),
        });

        var theme = new MenuItem("&Theme");
        theme.SubItems.Add(appearance);
        theme.SubItems.Add(MenuItem.Separator);
        theme.SubItems.Add(new MenuItem("&Reset colours", () => demo.SetAppearance(Palette.Light)));

        var state = new MenuItem("&State");
        foreach (var (text, value) in new[]
        {
            ("&Normal", WindowState.Normal),
            ("&Maximized", WindowState.Maximized),
            ("&Minimized", WindowState.Minimized),
        })
        {
            state.SubItems.Add(new MenuItem(text, () => window.State = value));
        }

        return new(
            new Menu("&View", theme),
            new Menu(
                "&Window",
                new MenuItem("&Topmost", () =>
                {
                    window.TopMost = !window.TopMost;
                    demo.SetStatus(window.TopMost
                        ? "The window is now above every other window."
                        : "The window is back in the normal stacking order.");
                })
                {
                    Shortcut = new Shortcut(Key.T),
                },
                new MenuItem("&Owned Window", demo.ShowOwnedWindow)
                {
                    Shortcut = new Shortcut(Key.N),
                },
                MenuItem.Separator,
                state));
    }

    /// <summary>
    /// The title bar text. It names the backend on purpose: three builds of the same program are
    /// the point, and a screenshot should say which one it came from.
    /// </summary>
    private static string WindowTitle =>
#if WINDOWS
        "Kolvir - Win32";
#elif MACOS
        "Kolvir - AppKit";
#else
        "Kolvir - GTK4";
#endif

    /// <summary>Creates the backend for the platform this build is for.</summary>
    private static IApplicationBackend CreateBackend(string[] args) =>
#if WINDOWS
        new Win32ApplicationBackend
        {
            ForceClassicMenus = Array.IndexOf(args, "--classic-menus") >= 0,
        };
#elif MACOS
        new AppKitApplicationBackend();
#else
        new Gtk4ApplicationBackend();
#endif

    /// <summary>
    /// The desktop's own UI font, so the controls match the rest of the system, or null to keep
    /// SkiaSharp's default.
    /// </summary>
    private static string? PreferredFontFamily() =>
#if MACOS
        AppKitSystemTheme.PreferredFontFamily();
#elif WINDOWS
        // The Win32 backend installs its own ISystemTheme and the library reads the font from
        // there, so there is nothing to pass.
        null;
#else
        // Reading this goes through Gtk.Settings, so the display has to be open already.
        Gtk4SystemTheme.PreferredFontFamily();
#endif

    private static int RenderSample(string[] args)
    {
        var path = args.Length > 1 ? args[1] : "kolvir.png";
        var width = args.Length > 2 && int.TryParse(args[2], out var w) ? w : (int)DesignClientSize.Width;
        var height = args.Length > 3 && int.TryParse(args[3], out var h) ? h : (int)DesignClientSize.Height;

        // Deliberately not PreferredFontFamily() on GTK: that goes through Gtk.Settings, which needs a
        // display. With no backend installed SystemTheme falls back to light, and --dark and
        // --light still force a palette, so the output stays deterministic - which is what a CI
        // screenshot wants anyway.
#if MACOS
        Theme.FontFamily = AppKitSystemTheme.PreferredFontFamily();
#endif

        // The same application-wide switch as the window path's --embedded-font: the theme is rebuilt
        // before the sample builds its controls, so every page renders in the bundled face.
        if (Array.IndexOf(args, "--embedded-font") >= 0)
        {
            Theme.UseFont(Font.FromBytes(DemoFont.Data));
        }

        ApplyAppearanceOverride(args);

        DemoUi.RenderToPng(
            path,
            width,
            height,
            PageArgument(args),
            CreateMenuBar,
            AfterShownArgument(args));
        Console.WriteLine($"Rendered {width}x{height} sample UI to {path}");
        // Printing what the render actually used makes it possible to tell a real visual difference
        // from a changed font or appearance when comparing renders from two builds.
        Console.WriteLine(
            $"Font: {Theme.FontFamily ?? Theme.DefaultFont.FamilyName}; " +
            $"appearance: {(Theme.IsDarkMode ? "dark" : "light")}");

        // The render path never touches a window or a GL context; saying so makes that a claim a
        // script can check rather than one the reader has to infer.
        Console.WriteLine("gfx: cpu (headless render)");
        return 0;
    }

    /// <summary>
    /// The value of <c>--menus kolvir|native</c>, which overrides who draws the menus. The library
    /// draws them by default on every platform, so <c>native</c> is the interesting one: it hands
    /// the menus back to the desktop. A menu bar is the exception on macOS, where the system's bar
    /// is the only one the platform shows and this switch changes nothing about it.
    /// </summary>
    private static MenuPresentation MenuPresentationArgument(string[] args)
    {
        int index = Array.IndexOf(args, "--menus");
        if (index < 0 || index + 1 >= args.Length)
        {
            return MenuPresentation.Auto;
        }

        return args[index + 1].ToLowerInvariant() switch
        {
            "kolvir" => MenuPresentation.Kolvir,
            "native" => MenuPresentation.Native,
            _ => MenuPresentation.Auto,
        };
    }

    /// <summary>
    /// What a headless render should do after the window is up and before the frame is written:
    /// open the menus page's context menu, right-click a span on the spans page, drive the Zoom
    /// page's slider, or nothing. They exist so a screenshot can show something that normally needs
    /// a click.
    /// </summary>
    private static Action<Window, DemoUi>? AfterShownArgument(string[] args)
    {
        bool menu = Array.IndexOf(args, "--menu") >= 0;
        bool spanMenu = Array.IndexOf(args, "--span-menu") >= 0;

        // The zoom page's slider is what a user would drag, so a screenshot of 2.0 against 1.0 is
        // otherwise not reproducible from a headless render. --zoom-popup opens one of the popup
        // surfaces anchored inside that panel, which is the only way to see the scaled wrapper.
        float? zoom = null;
        int zoomIndex = Array.IndexOf(args, "--zoom");
        if (zoomIndex >= 0 &&
            zoomIndex + 1 < args.Length &&
            float.TryParse(
                args[zoomIndex + 1],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsedZoom))
        {
            zoom = parsedZoom;
        }

        string? zoomPopup = null;
        int popupIndex = Array.IndexOf(args, "--zoom-popup");
        if (popupIndex >= 0 && popupIndex + 1 < args.Length)
        {
            zoomPopup = args[popupIndex + 1];
        }

        if (zoom is not null || zoomPopup is not null)
        {
            return (window, demo) =>
            {
                if (zoomPopup is not null)
                {
                    demo.ShowZoomPopup(window, zoomPopup, zoom ?? 2f);
                }
                else
                {
                    demo.SetPanelZoom(zoom!.Value);
                }
            };
        }

        if (menu)
        {
            return (window, demo) => demo.OpenMenuDemo(window);
        }

        // The inherited-appearance page changes a live tree, so a screenshot of it after the change
        // is a second picture of the same page: --page 33 --appearance-change shows the panel's font
        // and the theme's face both swapped with every control already built.
        if (Array.IndexOf(args, "--appearance-change") >= 0)
        {
            return (_, demo) =>
            {
                demo.SetAppearanceDemo(size: 21f);
                demo.SetAppearanceDemoFace(bundled: true);
            };
        }

        return spanMenu ? (window, demo) => demo.OpenSpanDemo(window) : null;
    }

    /// <summary>The value of <c>--page</c>, or -1 when the sample should show its first page.</summary>
    private static int PageArgument(string[] args)
    {
        int index = Array.IndexOf(args, "--page");
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var page) ? page : -1;
    }

    /// <summary>
    /// The value of a <c>--name value</c> switch, or null when it was not given. A flag that takes a
    /// value is read this way rather than by <see cref="Array.IndexOf(string[], string)"/> so a
    /// missing value is "not given" instead of reading whatever follows.
    /// </summary>
    private static string? ArgumentValue(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[index + 1]
            : null;
    }

    /// <summary>
    /// Honours <c>--dark</c> / <c>--light</c>, which force a palette instead of following the
    /// desktop. Handy for screenshots and for checking both appearances on one machine.
    /// </summary>
    private static bool? ForcedDarkMode(string[] args)
    {
        if (Array.IndexOf(args, "--dark") >= 0)
        {
            return true;
        }

        return Array.IndexOf(args, "--light") >= 0 ? false : null;
    }

    /// <summary>
    /// Applies <c>--dark</c> / <c>--light</c> if given, and otherwise the machine's own preference.
    /// </summary>
    private static void ApplyAppearanceOverride(string[] args)
    {
        if (ForcedDarkMode(args) is { } dark)
        {
            Theme.Palette = dark ? Palette.Dark : Palette.Light;
            return;
        }

#if WINDOWS
        // Only for --render: the normal path lets Application install the backend's
        // ISystemTheme and pick the palette up itself, but a headless render has no
        // application to do that.
        SystemTheme.Current = new Win32ApplicationBackend().SystemTheme;
        SystemTheme.Refresh();
#endif
    }
}
