using System.Diagnostics;
using System.Globalization;
using SkiaSharp;
using XPlatUI;
using XPlatUI.Animations;
using XPlatUI.AutoComplete;
using XPlatUI.Backend.Headless;
using XPlatUI.Badges;
using XPlatUI.DragDrop;
using XPlatUI.Multiline;
using XPlatUI.Spans;
using XPlatUI.Svg;
using XPlatUI.Undo;
using XDock = XPlatUI.DockStyle;

namespace XPlatUI.Demo;

/// <summary>
/// The sample XPlat UI shown by the harnesses. It is kept separate from any host window so the
/// very same control tree is rendered by the Win32 harness, by the GTK4 harness, and headlessly
/// (see <see cref="RenderToPng"/>).
/// </summary>
public sealed class DemoUi
{
    /// <summary>
    /// The formats the sample enables for its images.
    /// </summary>
    /// <remarks>
    /// The application's <c>ConfigureServices</c> does the same thing through the container - see
    /// <c>Program.cs</c> - and this factory exists because the sample is also rendered headlessly,
    /// where no container is set up. One list, composed once, and every image on every page comes
    /// from it: <c>Images.LoadFromStream(...)</c> reads a PNG, an SVG, an animated GIF or an animated
    /// WebP without the call site knowing which.
    /// </remarks>
    private static readonly XPlatImageDataFactory Images =
        new([
            XPlatSvgImageFormatProvider.Shared,
            XPlatGifImageFormatProvider.Shared,
            XPlatWebpImageFormatProvider.Shared,
        ]);

    private XPlatLabel _statusLabel = null!;
    private XPlatLabel _counterLabel = null!;
    private XPlatButton _primaryButton = null!;
    private XPlatListBox _navList = null!;
    private XPlatPanel _stage = null!;
    private XPlatControl[] _pages = null!;
    private XPlatControl? _shownPage;
    private XPlatControl? _menuDemoTarget;
    private XPlatTextBox? _undoFocusTarget;
    private XPlatTrackBar? _zoomSlider;
    private XPlatPanel? _zoomPanel;
    private XPlatComboBox? _zoomCombo;
    private XPlatControl? _zoomMenuTarget;
    private XPlatControl? _zoomButtonRow;
    private XPlatButton? _zoomTipTarget;
    private XPlatToolTip? _zoomToolTip;

    // The drag/drop lifetime page. The counts come from the feature's own diagnostic
    // (Win32DropReferenceTrace, reached through InternalsVisibleTo) and the collection is forced
    // here, in the demo, because that is the only way to pull a wrapper's finalizer into the middle
    // of a drag: the library must not garbage collect on an application's behalf.
    private XPlatCheckBox? _dragLifetimeCollect;
    private XPlatLabel? _dragLifetimeCurrent;
    private XPlatLabel? _dragLifetimeSequence;
    private XPlatLabel? _dragLifetimeCounts;
    private XPlatLabel? _dragLifetimeVerdict;
    private XPlatLabel? _dragLifetimePayload;
    private XPlatTextBox? _dragLifetimeLog;
    private readonly List<int> _dragLifetimeSamples = [];
    private readonly List<(string Time, string Text)> _dragLifetimeEvents = [];
#if WINDOWS
    // The sampler that writes these is Win32-only while the page that reads them is shared, so the
    // fields are conditional: without this guard a --no-incremental build on Linux and macOS sees
    // reads with no writes and reports CS0649 for each of them, which looks like seven dead fields.
    private int _dragLifetimeMinimum;
    private bool _dragLifetimeFell;
    private int _dragLifetimeFallFrom;
    private int _dragLifetimeFallTo;
    private string? _dragLifetimeFallEvent;
    private IntPtr _dragLifetimePointer;
    private string? _dragLifetimeLastEvent;
#endif
    private int _dragLifetimeDrops;
    private string? _dragLifetimeFirstPayload;
    private string? _dragLifetimeSecondPayload;
    private int _clicks;

    // The measured-rendering page. The timer exists only while the page is displayed, and the
    // benchmark drives the same timer and the same invalidation as the rolling readout, so CPU and
    // GL runs are comparable rather than merely similar.
    private const int BenchmarkWarmupFrames = 30;
    private const int BenchmarkMeasuredFrames = 600;
    private const int RollingFrameWindow = 120;

    // A run that stops being painted used to look like a hang: the only output is the BENCH line at
    // the end, so a window that is covered, minimised or on another workspace stalls the count and
    // says nothing, on every backend and for both damage modes. The watchdog says so instead.
    private const int BenchmarkStallCheckSeconds = 5;
    private const int BenchmarkStallSeconds = 10;
    private const int BenchmarkStallReportSeconds = 30;

    // The badge page's delayed nudge: long enough to switch to another window, short enough that the
    // click is still the last thing the tester did.
    private const int BadgeUrgentCountdownSeconds = 3;
    private XPlatTimer? _benchmarkWatchdog;
    private int _watchdogFrames;
    private long _watchdogStartedAt;
    private long _watchdogAdvancedAt;
    private long _watchdogReportedAt;
    private XPlatTimer? _metricsTimer;
    private XPlatTimer? _pageBenchmarkTimer;
    private bool _pageBenchmarkHandlerAdded;
    private XPlatUiHost? _pageBenchmarkHost;
    private XPlatControl? _metricsPage;
    private XPlatUiHost? _metricsHost;
    private XPlatLabel? _metricsReadout;
    private XPlatPanel? _metricsSwatch;
    private XPlatButton? _measureButton;
    private XPlatButton? _damageButton;
    private bool _metricsFullDamage = true;
    private bool _measuring;
    private int _measureFrame;
    private long _measureFirst;
    private long _measureLast;
    private SKRectI _measureLastArea;
    private readonly List<double> _measureSamples = [];
    private readonly Queue<(double Milliseconds, long Timestamp)> _rollingFrames = new();
    private long _rollingUpdatedAt;
    private long _lastRecordedFrame = -1;
    private XPlatLabel? _gfxLabel;
    private string? _graphicsPath;
    private string? _graphicsRenderer;
    private Func<float>? _scaleProvider;

    // The inherited-appearance page. The fonts are the page's for the life of the window: controls
    // never dispose a font they were handed, and the theme keeps the face it is given.
    private XPlatPanel? _appearanceStage;
    private XPlatLabel? _appearanceInherited;
    private XPlatPanel? _appearanceNested;
    private XPlatLabel? _appearanceLabel;
    private XPlatLabel? _appearanceOverrides;
    private XPlatLabel? _appearanceReadout;
    private XPlatFont? _appearanceSubtreeFont;
    private XPlatFont? _appearanceBigFont;
    private XPlatFont? _appearanceFace;

    // The font dialog page. The fonts are the page's for the life of the window, like the
    // appearance page's: a control never disposes a font it was handed, and a paint may still be
    // using the previous one when the next choice arrives.
    private XPlatPanel? _fontDialogStage;
    private XPlatLabel? _fontDialogReadout;
    private XPlatFontSelection? _fontDialogSelection;
    private readonly List<XPlatFont> _fontDialogFonts = [];

    // The colour dialog page. The swatch is the current colour and the readout is the same value in
    // text, so a cancelled dialog can be told apart from a chosen one at a glance.
    private SKColor _colorDialogColor = new(0x33, 0x66, 0x99);
    private XPlatPanel? _colorDialogSwatch;
    private XPlatLabel? _colorDialogReadout;

    // The chromeless page. The window is attached after it is created, because a page is built
    // before there is a window to ask; the readout then reports what the backend said rather than
    // what the page asked for, which is the whole point of the page.
    private XPlatWindow? _chromeWindow;
    private XPlatLabel? _chromeReadout;

    /// <summary>The app badge page's readout: what the backend can draw and what is showing.</summary>
    private XPlatLabel? _badgeReadout;

    /// <summary>
    /// The countdown behind the badge page's "Urgent in 3 s" button, so the nudge can be watched from
    /// outside the window: there is time to switch away, which is the only way to see a flash or a
    /// bounce on the two platforms that skip it while the application is frontmost.
    /// </summary>
    private XPlatTimer? _badgeUrgentTimer;

    /// <summary>Seconds left on that countdown, or zero when nothing is pending.</summary>
    private int _badgeUrgentSeconds;

    private XPlatControl? _chromeDragHandle;

    public DemoUi()
    {
        Root = BuildUi();

        // A page that has no menu of its own still has to answer a right-click: the host asks for a        // menu only after the control under the pointer has declined, and the native menu paths show
        // nothing at all when nobody answers. This one is genuinely application-wide - it belongs to
        // the window, not to any one control - so it is the host's handler, and it runs last. The
        // selection's Copy menu comes after it, so a page menu is only offered while there is
        // nothing selected to copy. A text box keeps its own Cut/Copy/Paste menu, which is why the
        // page menu is not offered over one.
        OnHostAvailable(Root, host => host.AddContextMenuOpeningHandler((_, args) =>
        {
            if (host.Selection.IsEmpty && args.Control is not XPlatTextBox)
            {
                args.Menu = BuildPageContextMenu();
            }
        }));
    }

    /// <summary>The root of the sample control tree.</summary>
    public XPlatControl Root { get; }

    /// <summary>
    /// Gives the sample the window it is showing in, which the chromeless page drives.
    /// </summary>
    /// <remarks>
    /// A page is built before the window exists, so the page cannot ask for one - and a headless
    /// render never has a real window to give it, which is why the page reports "no window" rather
    /// than failing when this is never called.
    /// </remarks>
    /// <param name="window">The window the sample is displayed in.</param>
    public void AttachWindow(XPlatWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (ReferenceEquals(_chromeWindow, window))
        {
            return;
        }

        _chromeWindow = window;

        // The snapshot moves when the window changes: after a resize, a maximise, a change of
        // explicit chrome or of the buttons requested. The page re-reads it rather than assuming.
        //
        // Showing counts as a change even though no backend reports it as one: a borderless window's
        // measured frame, and therefore its insets and button rects, is only known once the native
        // window exists, and the page is attached before that.
        window.AddChromeMetricsChangedHandler(_ => RefreshChromeReadout());
        window.AddStateChangedHandler(_ => RefreshChromeReadout());
        window.AddClientSizeChangedHandler(_ => RefreshChromeReadout());
        window.AddShownHandler(_ => RefreshChromeReadout());

        RefreshChromeReadout();

        // The badge can change without the page: an urgent request is a moment rather than a state, and
        // the platform half ends it when the user comes back, so the readout follows the badge rather
        // than only the clicks that set it.
        window.Application?.Badge.AddChangedHandler(_ => RefreshBadgeReadout());

        RefreshBadgeReadout();
    }
    /// <summary>Shows the page that holds the first text field and puts the caret in it, which then blinks.</summary>
    public void FocusFirstTextBox()    {
        for (int index = 0; index < _pages.Length; index++)
        {
            if (FindTextBox(_pages[index]) is not { } box)
            {
                continue;
            }

            _navList.SelectedIndex = index;
            box.Focus();
            return;
        }
    }

    /// <summary>
    /// Puts the caret in the undo page's single-line field, which is what <c>--undo-focus</c> is for:
    /// a screenshot or a script needs the field ready to type without hunting for it with the
    /// pointer.
    /// </summary>
    public void FocusUndoDemo() => _undoFocusTarget?.Focus();

    private static XPlatTextBox? FindTextBox(XPlatControl control)
    {
        var pending = new Stack<XPlatControl>();
        pending.Push(control);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current is XPlatTextBox box)
            {
                return box;
            }

            foreach (var child in current.Controls)
            {
                pending.Push(child);
            }
        }

        return null;
    }

    /// <summary>Switches the stage to the page at <paramref name="index"/>; an index outside the pages is ignored.</summary>
    /// <param name="index">The page to show, in the order the navigation lists them.</param>
    public void SelectPage(int index)
    {
        if (index >= 0 && index < _pages.Length)
        {
            _navList.SelectedIndex = index;
        }
    }

    /// <summary>
    /// The menu a page offers when the control under the pointer has none of its own: the two moves
    /// a reader of a page wants, and a line about what the sample is. It is built per right-click so
    /// the page moves know which page they are on.
    /// </summary>
    private XPlatContextMenu BuildPageContextMenu()
    {
        int index = _navList.SelectedIndex;

        return new XPlatContextMenu()
            .Add(new XPlatMenuItem("&Previous page", () => SelectPage(index - 1)) { Enabled = index > 0 })
            .Add(new XPlatMenuItem("&Next page", () => SelectPage(index + 1))
            {
                Enabled = index < _pages.Length - 1,
            })
            .AddSeparator()
            .Add(new XPlatMenuItem("&About this sample", () => SetStatus(
                "Every control here is drawn by XPlatUI itself, so the same tree runs on Windows, macOS and Linux.")));
    }

    /// <summary>
    /// Renders the sample UI to a PNG without opening a window. Useful for visual regression tests
    /// and for verifying the render pipeline on CI.
    /// </summary>
    /// <remarks>
    /// This goes through the real application model - <see cref="XPlatApplication"/>,
    /// <see cref="XPlatWindow"/> and the headless backend - so a regression here is a regression in
    /// the model, not just in the demo. The window's client size is set explicitly, so the model's
    /// default sizing never runs and the output matches what the harnesses paint.
    /// </remarks>
    /// <param name="path">The file to write.</param>
    /// <param name="width">Client width in logical pixels.</param>
    /// <param name="height">Client height in logical pixels.</param>
    /// <param name="page">The page to show, or -1 for the first one.</param>
    /// <param name="menuBar">
    /// Builds the window's menu bar from the window and the sample it drives, or null to render
    /// without one. A screenshot should show the same strip the window does, so the harness passes
    /// its own bar in.
    /// </param>
    /// <param name="afterShown">
    /// Runs once the window is up and before the frame is written, which is the only moment a popup
    /// can be opened: a popup needs a host, and a page has none until it is attached. The menus page
    /// uses it to render with its context menu open.
    /// </param>
    public static void RenderToPng(
        string path,
        int width,
        int height,
        int page = -1,
        Func<XPlatWindow, DemoUi, XPlatMenuBar>? menuBar = null,
        Action<XPlatWindow, DemoUi>? afterShown = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var demo = new DemoUi();
        demo.SelectPage(page);

        var backend = new HeadlessApplicationBackend();
        using var application = new XPlatApplication(backend);
        var window = new XPlatWindow("XPlatUI sample", demo.Root)
        {
            ClientSize = new SKSize(width, height),
        };

        if (menuBar is not null)
        {
            window.MenuBar = menuBar(window, demo);
        }

        // The chromeless page drives the window it is shown in, and a headless render has a window
        // even though it has no platform behind it - so the page reports that backend's "no" rather
        // than nothing at all.
        demo.AttachWindow(window);

        window.AddShownHandler(_ =>
        {
            afterShown?.Invoke(window, demo);
            backend.LastWindow!.SavePng(path);
        });
        window.Show();
        application.Run();
    }

    private XPlatControl BuildUi()
    {
        // The window background comes from the host's ClearColor, which follows the palette.
        var root = new XPlatPanel
        {
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };

        root.Controls.Add(BuildHeader());
        root.Controls.Add(BuildFooter());
        root.Controls.Add(BuildContent());

        // The whole window takes drops, so files dragged in from the desktop and text dragged from
        // any application land here unless the control under the pointer handles drops itself.
        root.AllowDrop = true;
        root.AddDragDropHandler((_, e) => OnRootDrop(e));

        return root;
    }

    private XPlatPanel BuildHeader()
    {
        var header = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 100f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };

        header.Controls.Add(BuildAppearanceButton());
        header.Controls.Add(BuildOwnedWindowButton());

        var title = new XPlatLabel("XPlat UI")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 44f,
            Font = XPlatTheme.TitleFont,
            TextAlign = XPlatContentAlignment.BottomLeft,
            Margin = new XPlatThickness(28f, 16f, 28f, 0f),
        };

        var subtitle = new XPlatLabel("A lightweight, AOT-friendly, cross-platform control layer rendered with SkiaSharp.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 24f,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(28f, 4f, 28f, 0f),
        };

        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        // The header is this sample's title bar, so a press on it that a button does not claim is
        // what a borderless window has to turn into a window drag. The labels are not selection
        // targets, so the title text drags with the strip rather than highlighting.
        AttachWindowDrag(header);
        AttachWindowDrag(title);
        AttachWindowDrag(subtitle);

        return header;
    }

    /// <summary>
    /// Makes a strip act as the window's title bar: a press the platform hands to the strip is the
    /// application's chance to ask for a window drag.
    /// </summary>
    /// <remarks>
    /// Only presses the strip itself receives count - a button inside it takes its own click first,
    /// which is why the header's two buttons are not wired to this and still work.
    /// </remarks>
    private void AttachWindowDrag(XPlatControl strip) =>
        strip.AddMouseDownHandler((_, _) => DragWindowFromThisPress());

    /// <summary>
    /// Swaps the palette. The library repaints on its own (the host listens to
    /// <see cref="XPlatTheme.AddChangedHandler"/>), so this is the whole of the application's dark mode
    /// story.
    /// </summary>
    private XPlatButton BuildAppearanceButton()
    {
        var button = new XPlatButton("Switch appearance")
        {
            Dock = XDock.Right,
            Style = XPlatButtonStyle.Secondary,
            AutoSize = true,
            Margin = new XPlatThickness(8f, 20f, 28f, 0f),
        };

        button.AddClickHandler(_ => SetAppearance(XPlatTheme.IsDarkMode ? XPlatPalette.Light : XPlatPalette.Dark));

        return button;
    }

    /// <summary>
    /// Applies <paramref name="palette"/> and reports it in the status line, so the header button
    /// and the View menu describe the change the same way.
    /// </summary>
    public void SetAppearance(XPlatPalette palette)
    {
        XPlatTheme.Palette = palette;
        SetStatus(ReferenceEquals(palette, XPlatPalette.Dark)
            ? "Dark palette applied - every control recoloured on the next frame."
            : "Light palette applied - every control recoloured on the next frame.");
    }

    /// <summary>
    /// Opens a second window from the UI itself. Windows has no menu bar in this sample, so this is
    /// the only way to reach ownership without a command-line flag there.
    /// </summary>
    private XPlatButton BuildOwnedWindowButton()
    {
        var button = new XPlatButton("Owned window")
        {
            Dock = XDock.Right,
            Style = XPlatButtonStyle.Secondary,
            AutoSize = true,
            Margin = new XPlatThickness(8f, 20f, 8f, 0f),
        };

        button.AddClickHandler(_ => ShowOwnedWindow());

        return button;
    }

    private XPlatPanel BuildFooter()
    {
        var footer = new XPlatPanel
        {
            Dock = XDock.Bottom,
            Height = 64f,
            ShowFrame = true,
            IsCard = false,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };

        _statusLabel = new XPlatLabel("Ready - click a button, or Tab/Enter to drive the UI from the keyboard.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.MiddleLeft,
            Margin = new XPlatThickness(28f, 0f, 12f, 0f),
        };

        var resetButton = new XPlatButton("Reset")
        {
            Dock = XDock.Right,
            Style = XPlatButtonStyle.Secondary,
            AutoSize = true,
            Margin = new XPlatThickness(8f, 16f, 28f, 16f),
        };
        resetButton.AddClickHandler(_ =>
        {
            _clicks = 0;
            UpdateCounter();
            SetStatus("Counter reset.");
        });

        var toggleButton = new XPlatButton("Disable primary")
        {
            Dock = XDock.Right,
            Style = XPlatButtonStyle.Secondary,
            AutoSize = true,
            Margin = new XPlatThickness(8f, 16f, 0f, 16f),
        };
        toggleButton.AddClickHandler(_ =>
        {
            _primaryButton.Enabled = !_primaryButton.Enabled;
            toggleButton.Text = _primaryButton.Enabled ? "Disable primary" : "Enable primary";
            SetStatus(_primaryButton.Enabled ? "Primary action enabled." : "Primary action disabled.");
        });

        // Always visible, not a transient status: a screenshot of this window has to say which
        // presentation path produced whatever number the page is showing. The renderer string is
        // filled in from Shown, when the backend has actually created its surface.
        _gfxLabel = new XPlatLabel("gfx: -")
        {
            Dock = XDock.Right,
            AutoSize = true,
            // Hidden until a backend reports its path. The headless render harness never calls
            // SetGraphicsInfo, so a --render image stays byte-identical to the one before this
            // label existed - an invisible docked child takes no space.
            Visible = false,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.MiddleRight,
            Margin = new XPlatThickness(8f, 0f, 0f, 0f),
        };

        footer.Controls.Add(_statusLabel);
        footer.Controls.Add(_gfxLabel);
        footer.Controls.Add(resetButton);
        footer.Controls.Add(toggleButton);
        return footer;
    }

    /// <summary>
    /// The stage pages, in the order the navigation lists them. The row text and the builder sit
    /// together, so a page cannot be added without a row to select it by.
    /// </summary>
    private static readonly (string Title, Func<DemoUi, XPlatControl> Build)[] Demos =
    {
        ("Split container", demo => Page(demo.BuildSplitDemo())),
        ("Stack panel", demo => Page(demo.BuildStackPanelDemo())),
        ("Text input", demo => Page(demo.BuildTextInputDemo())),
        ("Dialogs", demo => Page(demo.BuildDialogDemo())),
        ("Table layout", demo => Page(demo.BuildTableLayoutDemo())),
        ("Labels and text", demo => Page(demo.BuildLabelsCard())),
        ("Buttons and input", demo => Page(demo.BuildButtonsCard())),
        ("Check boxes and radios", demo => Page(demo.BuildToggleDemo())),
        ("Progress bars", demo => Page(demo.BuildProgressDemo())),
        ("Group boxes", demo => Page(demo.BuildGroupBoxDemo())),
        ("Rich text", demo => Page(demo.BuildRichTextCard())),
        ("Images and layout", demo => Page(BuildImagesCard())),
        ("Selection and clipboard", demo => Page(demo.BuildSelectionCard())),
        ("Scrolling", demo => Page(demo.BuildScrollingCard())),
        ("Lists", demo => Page(demo.BuildListBoxCard())),
        ("Performance: list box", demo => Page(demo.BuildPerformancePage())),
        ("Performance: controls", demo => Page(demo.BuildControlStackPage())),
        ("Combo boxes", demo => Page(demo.BuildComboBoxDemo())),
        ("Tab controls", demo => Page(demo.BuildTabControlDemo())),
        ("Tool tips", demo => Page(demo.BuildToolTipDemo())),
        ("List views", demo => Page(demo.BuildListViewDemo())),
        ("Tree view", demo => Page(demo.BuildTreeViewDemo())),
        ("Numeric up-down", demo => Page(demo.BuildNumericUpDownDemo())),
        ("Track bars", demo => Page(demo.BuildTrackBarDemo())),
        ("Link labels", demo => Page(demo.BuildLinkLabelDemo())),
        ("Status and tool strips", demo => Page(demo.BuildToolStripDemo())),
        ("Menus", demo => Page(demo.BuildMenuDemo())),
        ("Date and time", demo => Page(demo.BuildDateTimePickerDemo())),
        ("Text box autocomplete", demo => Page(demo.BuildAutoCompleteDemo())),
        ("Multi-line text", demo => Page(demo.BuildMultilineDemo())),
        ("Text box spans", demo => Page(demo.BuildSpanDemo())),
        ("Undo and redo", demo => Page(demo.BuildUndoDemo())),
        ("Fonts from a file", demo => Page(demo.BuildFontDemo())),
        ("Inherited appearance", demo => Page(demo.BuildAppearanceDemo())),
        ("Tab order", demo => Page(demo.BuildTabOrderDemo())),
        ("Animations", demo => Page(demo.BuildAnimationDemo())),
        ("Colour picker", demo => Page(demo.BuildColorPickerDemo())),
        ("Font dialog", demo => Page(demo.BuildFontDialogDemo())),
        ("Colour dialog", demo => Page(demo.BuildColorDialogDemo())),
        ("Split buttons", demo => Page(demo.BuildSplitButtonDemo())),
        ("Expander", demo => Page(demo.BuildExpanderDemo())),
        ("Wrap panel", demo => Page(demo.BuildWrapPanelDemo())),
        ("Busy indicator and overlay", demo => Page(demo.BuildBusyOverlayDemo())),
        ("Toast", demo => Page(demo.BuildToastDemo())),
        ("Toast animation", demo => Page(demo.BuildToastAnimationDemo())),
        ("Overlay fade", demo => Page(demo.BuildOverlayFadeDemo())),
        ("SVG icons", demo => Page(BuildSvgIconGallery())),
        ("SVG recolour", demo => Page(BuildSvgRecolourDemo())),
        ("SVG and a raster twin", demo => Page(BuildSvgRasterTwinDemo())),
        ("Animated GIF", demo => Page(demo.BuildAnimatedGifDemo())),
        ("GIF frames", demo => Page(demo.BuildGifFrameDemo())),
        ("GIF out of view", demo => Page(demo.BuildGifScrollDemo())),
        ("Animated WebP", demo => Page(demo.BuildAnimatedWebpDemo())),
        ("WebP frames", demo => Page(demo.BuildWebpFrameDemo())),
        ("Measured rendering", demo => Page(demo.BuildMetricsPage())),
        ("Zoom", demo => Page(demo.BuildZoomPage())),
        ("Drag/drop lifetime", demo => Page(demo.BuildDragLifetimePage())),
        ("Chromeless window", demo => Page(demo.BuildChromelessPage())),
        ("App badge", demo => Page(demo.BuildBadgePage())),
    };

    /// <summary>
    /// A navigation list down the left and one page at a time on the right. The splitter between
    /// them is draggable, so the navigation can be widened when a demo name does not fit.
    /// </summary>
    private XPlatControl BuildContent()
    {
        _pages = new XPlatControl[Demos.Length];
        for (int index = 0; index < Demos.Length; index++)
        {
            _pages[index] = Demos[index].Build(this);
        }

        _stage = new XPlatPanel
        {
            Dock = XDock.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };

        _navList = new XPlatListBox
        {
            Dock = XDock.Fill,
            SelectionMode = XPlatSelectionMode.One,
            VerticalScroll = XPlatScrollStyle.Automatic,
            HorizontalScroll = XPlatScrollStyle.None,
            BorderWidth = 0f,
            ItemHeight = 32f,
        };

        foreach (var demo in Demos)
        {
            _navList.Items.Add(demo.Title);
        }

        _navList.AddSelectedIndexChangedHandler(_ => ShowPage(_navList.SelectedIndex));

        var nav = new XPlatPanel
        {
            Dock = XDock.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(20f, 16f, 12f, 16f),
        };
        nav.Controls.Add(_navList);

        // The navigation is window chrome rather than a demo, so it keeps no tab stop of its own:
        // Tab goes straight from the header into the list. The splitter still drags with the mouse.
        var split = new XPlatSplitContainer
        {
            Dock = XDock.Fill,
            Orientation = XPlatOrientation.Vertical,
            SplitterDistance = 210f,
            Panel1MinSize = 150f,
            Panel1MaxSize = 400f,
            Panel2MinSize = 400f,
            TabStop = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
        };

        foreach (var panel in new[] { split.Panel1, split.Panel2 })
        {
            panel.ShowFrame = false;
            panel.IsCard = false;
            panel.BackColor = null;
            panel.BorderWidth = 0f;
            panel.CornerRadius = 0f;
            panel.Padding = new XPlatThickness(0f);
        }

        split.Panel1.Controls.Add(nav);
        split.Panel2.Controls.Add(_stage);

        _navList.SelectedIndex = 0;
        return split;
    }

    /// <summary>
    /// Wraps a page body in the inset every page shares, so a page is only ever its content and
    /// the space around it is decided in one place.
    /// </summary>
    private static XPlatPanel Page(XPlatControl body)
    {
        var page = new XPlatPanel
        {
            Dock = XDock.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(20f, 16f, 20f, 16f),
        };
        page.Controls.Add(body);
        return page;
    }

    /// <summary>Swaps the page on the stage. Every page is built once and kept, so returning to one costs a re-parent.</summary>
    private void ShowPage(int index)
    {
        if (index < 0 || index >= _pages.Length)
        {
            return;
        }

        var page = _pages[index];
        if (ReferenceEquals(page, _shownPage))
        {
            return;
        }

        if (_shownPage is not null)
        {
            _stage.Controls.Remove(_shownPage);
        }

        _shownPage = page;
        _stage.Controls.Add(page);
    }

    private XPlatPanel BuildTextInputDemo()
    {
        var section = new XPlatPanel
        {
            // A row of fields is a caption plus a field tall, so the stage below it stays empty
            // rather than stretching the inputs to the height of the window.
            Dock = XDock.Top,
            Height = 54f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };

        var nameBox = new XPlatTextBox
        {
            PlaceholderText = "Type your name",
            MaxLength = 24,
            AllowDrop = true,
        };
        nameBox.AddTextChangedHandler(_ => SetStatus(nameBox.Text.Length == 0
            ? "Name cleared."
            : $"Name: {nameBox.Text} ({nameBox.Text.Length}/{nameBox.MaxLength})"));

        var passwordBox = new XPlatTextBox
        {
            IsPassword = true,
            PlaceholderText = "Password",
        };
        passwordBox.AddTextChangedHandler(_ => SetStatus($"Password length: {passwordBox.Text.Length}"));

        var readOnlyBox = new XPlatTextBox
        {
            Text = "Read-only - select, then Ctrl+C",
            ReadOnly = true,
        };

        var row = new XPlatPanel
        {
            Dock = XDock.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };
        row.Controls.Add(Field("Name", nameBox, 320f));
        row.Controls.Add(Field("Password", passwordBox, 280f));
        row.Controls.Add(Field("Read only", readOnlyBox, 0f));
        section.Controls.Add(row);

        return section;
    }

    /// <summary>A captioned input: a small label above the control. A width of 0 docks the field to fill.</summary>
    private static XPlatPanel Field(string caption, XPlatTextBox input, float width)
    {
        bool fill = width <= 0f;
        var field = new XPlatPanel
        {
            Dock = fill ? XDock.Fill : XDock.Left,
            Width = fill ? 0f : width,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f, 0f, fill ? 0f : 16f, 0f),
        };

        field.Controls.Add(new XPlatLabel(caption)
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 20f,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        input.Dock = XDock.Fill;
        field.Controls.Add(input);

        return field;
    }

    private XPlatPanel BuildDialogDemo()
    {
        var section = new XPlatPanel
        {
            // The buttons are as tall as a button is, so the row keeps a height of its own rather
            // than stretching to the height of the stage.
            Dock = XDock.Top,
            Height = 44f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };

        var row = new XPlatPanel
        {
            Dock = XDock.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };

        var messageBox = DialogButton("Message box", () => ShowMessageBox());
        row.Controls.Add(messageBox);
        row.Controls.Add(DialogButton("Open file", () => ShowFileDialog(DialogKind.Open)));
        row.Controls.Add(DialogButton("Save file", () => ShowFileDialog(DialogKind.Save)));
        row.Controls.Add(DialogButton("Pick folder", () => ShowFileDialog(DialogKind.Folder)));

        section.Controls.Add(row);
        return section;
    }

    private static XPlatButton DialogButton(string text, Action onClick)
    {
        var button = new XPlatButton(text)
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        button.AddClickHandler(_ => onClick());
        return button;
    }

    private void ShowMessageBox()
    {
        if (OwnerWindow is not { } owner)
        {
            return;
        }

        var result = owner.Application.Dialogs.ShowMessage(
            new XPlatMessageBoxOptions
            {
                Message = "This box is drawn by the platform, not by XPlatUI.\n\n" +
                          "It is modal to the window and the sample keeps painting behind it.",
                Title = "XPlatUI sample",
                Buttons = XPlatMessageBoxButtons.YesNoCancel,
                Icon = XPlatMessageBoxIcon.Information,
                DefaultResult = XPlatDialogResult.Yes,
            },
            owner);

        SetStatus($"Message box returned {result}.");
    }

    private void ShowFileDialog(DialogKind kind)
    {
        if (OwnerWindow is not { } owner)
        {
            return;
        }

        var dialogs = owner.Application.Dialogs;

        var options = new XPlatFileDialogOptions
        {
            Title = kind switch
            {
                DialogKind.Open => "Open a file",
                DialogKind.Save => "Save a file",
                _ => "Pick a folder",
            },
            FileName = kind == DialogKind.Save ? "untitled.txt" : null,
            OverwritePrompt = true,
        };

        if (kind != DialogKind.Folder)
        {
            options.Filters.Add(new XPlatFileFilter("Text files", "*.txt", "*.md"));
            options.Filters.Add(new XPlatFileFilter("All files", "*.*"));
        }

        var path = kind switch
        {
            DialogKind.Open => dialogs.OpenFile(options, owner),
            DialogKind.Save => dialogs.SaveFile(options, owner),
            _ => dialogs.PickFolder(options, owner),
        };

        SetStatus(path is null ? "Dialog cancelled." : $"Chose {path}");
    }

    private enum DialogKind
    {
        Open,
        Save,
        Folder,
    }

    /// <summary>
    /// The window the demo is shown in, used to parent the dialogs. It is resolved from the root at
    /// call time rather than from a control on one page: only the page being viewed is attached to
    /// the window, so a control on another page has no <see cref="XPlatControl.Window"/> and would
    /// make a dialog opened from here silently do nothing.
    /// </summary>
    private XPlatWindow? OwnerWindow => Root.Window;

    /// <summary>
    /// Shows a small window owned by the window hosting the sample. Ownership is what makes a
    /// window behave like a child of another: it stays above its owner, minimizes with it, and
    /// closes when the owner closes.
    /// </summary>
    public void ShowOwnedWindow()
    {
        if (OwnerWindow is not { } owner)
        {
            return;
        }

        var panel = new XPlatPanel
        {
            Padding = new XPlatThickness(16f),
        };

        panel.Controls.Add(new XPlatLabel(
            "This window is owned by the main window: it stays above it, minimizes with it, and "
            + "closes when it closes.")
        {
            Dock = XDock.Fill,
            WrapText = true,
            TextAlign = XPlatContentAlignment.MiddleCenter,
        });

        var window = new XPlatWindow("Owned Window", panel)
        {
            ClientSize = new SKSize(440f, 170f),
            MinimumClientSize = new SKSize(300f, 130f),
        };

        window.Show(owner);
        SetStatus("Opened a window owned by this one - it stays above it and closes with it.");
    }

    private XPlatPanel BuildSplitDemo()
    {
        var section = new XPlatPanel
        {
            Dock = XDock.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };

        var split = new XPlatSplitContainer
        {
            Dock = XDock.Fill,
            Orientation = XPlatOrientation.Vertical,
            SplitterDistance = 260f,
            Panel1MinSize = 140f,
            Panel1MaxSize = 420f,
            Panel2MinSize = 180f,
        };
        split.AddSplitterMovedHandler(_ => SetStatus($"Splitter moved: panel 1 is {split.SplitterDistance:0} px wide."));

        split.Panel1.Controls.Add(Heading("Navigation"));
        split.Panel1.Controls.Add(DemoButton("Dashboard", () => SetStatus("Dashboard selected.")));
        split.Panel1.Controls.Add(DemoButton("Reports", () => SetStatus("Reports selected.")));
        split.Panel1.Controls.Add(DemoButton("Settings", () => SetStatus("Settings selected.")));

        var nested = new XPlatSplitContainer
        {
            Dock = XDock.Fill,
            Orientation = XPlatOrientation.Horizontal,
            SplitterDistance = 64f,
            Panel1MinSize = 40f,
            Panel2MinSize = 40f,
        };
        nested.Panel1.Controls.Add(Heading("Panel 1 of a horizontal split container"));
        nested.Panel2.Controls.Add(new XPlatLabel("Drag either splitter - the dashed band - to resize. The second panel always fills the remaining space.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        split.Panel2.Controls.Add(nested);
        section.Controls.Add(split);

        return section;
    }

    private static XPlatLabel Heading(string text) => new(text)
    {
        Dock = XDock.Top,
        AutoSize = false,
        Height = 26f,
        Font = XPlatTheme.EmphasizedFont,
        TextAlign = XPlatContentAlignment.BottomLeft,
        Margin = new XPlatThickness(0f, 0f, 0f, 8f),
    };

    private static XPlatButton DemoButton(string text, Action onClick)
    {
        var button = new XPlatButton(text)
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        };
        button.AddClickHandler(_ => onClick());
        return button;
    }

    /// <summary>
    /// One of each sizing style, so the difference between them is visible rather than described:
    /// column 0 is a fixed width, column 1 takes a percentage and column 2 sizes to its content,
    /// and the three rows do the same. The cells are outlined so the tracks can be read off.
    /// </summary>
    private XPlatPanel BuildTableLayoutDemo()
    {
        var table = new XPlatTableLayoutPanel
        {
            Dock = XDock.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
            CellPadding = new XPlatThickness(8f),
        };

        table.ColumnStyles.Add(new XPlatColumnStyle(XPlatSizeType.Absolute, 160f));
        table.ColumnStyles.Add(new XPlatColumnStyle(XPlatSizeType.Percent, 100f));
        table.ColumnStyles.Add(new XPlatColumnStyle(XPlatSizeType.AutoSize, 0f));

        table.RowStyles.Add(new XPlatRowStyle(XPlatSizeType.AutoSize, 0f));
        table.RowStyles.Add(new XPlatRowStyle(XPlatSizeType.Percent, 100f));
        table.RowStyles.Add(new XPlatRowStyle(XPlatSizeType.Absolute, 90f));

        table.Add(Cell("Absolute column\n160 px"), 0, 0);
        table.Add(Cell("Percent column\nall the space the other two leave"), 1, 0);
        table.Add(Cell("AutoSize column\nfollows its content"), 2, 0);

        table.Add(Cell("AutoSize row\nfollows its content"), 0, 1);
        table.Add(Cell("The percent row and the percent column absorb whatever the fixed and AutoSize tracks leave over, which is why this cell is the one that grows and shrinks with the window."), 1, 1);
        table.Add(Cell("Drag the splitter, or resize the window, and only this column keeps its width."), 2, 1);

        table.Add(Cell("Absolute row\n90 px"), 0, 2);
        table.Add(Cell("Percent row, percent column"), 1, 2);
        table.Add(Cell("AutoSize\nboth ways"), 2, 2);

        return table;
    }

    private static XPlatPanel Cell(string caption)
    {
        var cell = new XPlatPanel
        {
            Dock = XDock.Fill,
            BorderWidth = 1f,
            CornerRadius = 8f,
            Padding = new XPlatThickness(10f),
        };

        cell.Controls.Add(new XPlatLabel(caption)
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        return cell;
    }

    /// <summary>
    /// One control along one axis: children stretched across the stack by default, the three
    /// alignments that opt out of it, the same control turned sideways, and a stack that scrolls
    /// its own run with a docked header that stays put.
    /// </summary>
    private XPlatPanel BuildStackPanelDemo()
    {
        var card = Card();

        card.Controls.Add(ScrollCaption("Vertical - each child takes its own height and the width of the stack"));
        card.Controls.Add(BuildVerticalStack());

        card.Controls.Add(ScrollCaption("Across the stack - HorizontalAlignment on each child"));
        card.Controls.Add(BuildAlignedStack());

        card.Controls.Add(ScrollCaption("Horizontal - the same control with Orientation = Horizontal"));
        card.Controls.Add(BuildHorizontalStack());

        card.Controls.Add(ScrollCaption("VerticalScroll = Automatic - the run of children is the scrollable content"));
        card.Controls.Add(BuildScrollingStack());

        return card;
    }

    /// <summary>
    /// The framing the stacks on this page share: a bordered box, so the bounds the stack arranges
    /// its children in are visible.
    /// </summary>
    private static XPlatStackPanel StackBox() => new()
    {
        Dock = XDock.Top,
        ShowFrame = true,
        IsCard = true,
        CornerRadius = 8f,
        Padding = new XPlatThickness(8f),
        Spacing = 6f,
        Margin = new XPlatThickness(0f, 0f, 0f, 10f),
    };

    private XPlatStackPanel BuildVerticalStack()
    {
        var stack = StackBox();
        stack.Height = 130f;

        foreach (var text in new[] { "First", "Second", "Third" })
        {
            stack.Controls.Add(StackButton(text, XPlatAlignment.Stretch));
        }

        return stack;
    }

    private XPlatStackPanel BuildAlignedStack()
    {
        var stack = StackBox();
        stack.Height = 170f;

        stack.Controls.Add(StackButton("Near", XPlatAlignment.Near));
        stack.Controls.Add(StackButton("Center", XPlatAlignment.Center));
        stack.Controls.Add(StackButton("Far", XPlatAlignment.Far));
        stack.Controls.Add(StackButton("Stretch", XPlatAlignment.Stretch));

        return stack;
    }

    private XPlatStackPanel BuildHorizontalStack()
    {
        var stack = StackBox();
        stack.Orientation = XPlatStackOrientation.Horizontal;
        stack.Height = 60f;

        // A horizontal stack stretches its children down the stack, so the interesting property is
        // VerticalAlignment.
        foreach (var (text, alignment) in new[]
                 {
                     ("Top", XPlatAlignment.Near),
                     ("Middle", XPlatAlignment.Center),
                     ("Bottom", XPlatAlignment.Far),
                     ("Fill", XPlatAlignment.Stretch),
                 })
        {
            stack.Controls.Add(StackButton(text, alignment));
        }

        return stack;
    }

    private XPlatStackPanel BuildScrollingStack()
    {
        var stack = StackBox();
        stack.Dock = XDock.Fill;
        stack.VerticalScroll = XPlatScrollStyle.Automatic;
        stack.Spacing = 4f;

        // A docked child is not part of the run: it keeps the top of the panel while the rows
        // scroll underneath it.
        stack.Controls.Add(new XPlatLabel("Docked header - it stays put while the rows scroll")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 20f,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.MiddleLeft,
        });

        for (int index = 0; index < 40; index++)
        {
            var row = StackButton($"Row {index + 1}", XPlatAlignment.Stretch);
            row.AutoSize = false;
            row.Height = 28f;
            stack.Controls.Add(row);
        }

        return stack;
    }

    private XPlatButton StackButton(string text, XPlatAlignment alignment)
    {
        var button = new XPlatButton(text)
        {
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            HorizontalAlignment = alignment,
            VerticalAlignment = alignment,
        };
        button.AddClickHandler(_ => SetStatus($"{text} clicked - placed {alignment} across the stack."));
        return button;
    }

    private static XPlatPanel Card() => new()
    {
        Dock = XDock.Fill,
        BorderWidth = 1f,
        CornerRadius = 10f,
        Padding = new XPlatThickness(16f),
    };

    private XPlatPanel BuildLabelsCard()
    {
        var card = Card();

        card.Controls.Add(new XPlatLabel("Label")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 26f,
            Font = XPlatTheme.EmphasizedFont,
            TextAlign = XPlatContentAlignment.BottomLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
        });

        card.Controls.Add(new XPlatLabel("Labels wrap long text automatically, honour nine-way alignment and share the theme fonts.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 72f,
            WrapText = true,
            Font = XPlatTheme.DefaultFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 10f),
            Selectable = true,
        });

        _counterLabel = new XPlatLabel("Button clicks: 0")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 22f,
            Font = XPlatTheme.EmphasizedFont,
            ForeColorRole = XPlatThemeColor.Accent,
            TextAlign = XPlatContentAlignment.MiddleLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
            Selectable = true,
        };
        card.Controls.Add(_counterLabel);

        card.Controls.Add(new XPlatLabel("Multi-line text\nis supported too.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 40f,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Selectable = true,
        });

        return card;
    }

    private XPlatPanel BuildRichTextCard()
    {
        var card = Card();

        card.Controls.Add(new XPlatLabel("Rich label")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 26f,
            Font = XPlatTheme.EmphasizedFont,
            TextAlign = XPlatContentAlignment.BottomLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
        });

        var rich = new XPlatRichLabel
        {
            Dock = XDock.Top,
            AutoSize = true,
            WrapText = true,
            Font = XPlatTheme.DefaultFont,
            Selectable = true,
        };

        rich.AddText("One flow of ");
        rich.AddSpan(new XPlatTextSpan("bold") { Bold = true });
        rich.AddText(", ");
        rich.AddSpan(new XPlatTextSpan("italic") { Italic = true });
        rich.AddText(", ");
        rich.AddSpan(new XPlatTextSpan("underline test") { Underline = true });
        rich.AddText(", ");
        rich.AddSpan(new XPlatTextSpan("colour") { ThemeColor = XPlatThemeColor.Accent });
        rich.AddText(" and ");
        rich.AddSpan(new XPlatTextSpan("struck") { Strikethrough = true });
        rich.AddText(" text, plus a ");

        var link = rich.AddLink("clickable link", span => SetStatus($"Link span clicked: \"{span.Text}\" (tag: {span.Tag ?? "none"})."));
        link.Tag = "demo";
        link.AddMouseEnterHandler(_ => SetStatus("Pointer entered a link span - spans receive mouse events."));
        link.AddMouseLeaveHandler(_ => SetStatus("Pointer left the link span."));

        rich.AddText(". Only the glyphs of a span are hit-tested.");
        rich.AddText("\nLive chips in the flow, aligned per line: ");
        rich.AddInlineControl(Chip("top"), XPlatInlineAlignment.Top);
        rich.AddText(" ");
        rich.AddInlineControl(Chip("middle"), XPlatInlineAlignment.Center);
        rich.AddText(" ");
        rich.AddInlineControl(Chip("bottom"), XPlatInlineAlignment.Bottom);

        // Shown on right-click while nothing is selected; once a range is selected the host swaps
        // in the Copy menu instead, so both paths are reachable from this one control.
        rich.ContextMenu = BuildRichTextMenu();

        card.Controls.Add(rich);
        return card;
    }

    private XPlatContextMenu BuildRichTextMenu()
    {
        var formatting = new XPlatMenuItem("Show formatting", () =>
            SetStatus("'Show formatting' picked - a checked item shows a tick."))
        {
            Checked = true,
            Shortcut = new XPlatShortcut(XPlatKey.F, XPlatModifiers.Control | XPlatModifiers.Shift),
        };

        var menu = new XPlatContextMenu();

        menu.Add(new XPlatMenuItem("Copy rich text", () => SetStatus("'Copy rich text' picked."))
        {
            IsDefault = true,
            Shortcut = new XPlatShortcut(XPlatKey.C),
        });

        menu.Add(new XPlatMenuItem("Copy as plain text", () => SetStatus("'Copy as plain text' picked."))
        {
            Shortcut = new XPlatShortcut(XPlatKey.C, XPlatModifiers.Control | XPlatModifiers.Shift),
        });

        menu.AddSeparator();
        menu.Add(formatting);
        menu.Add(new XPlatMenuItem("Undo formatting", null) { Enabled = false, Shortcut = new XPlatShortcut(XPlatKey.Z) });
        return menu;
    }

    private XPlatButton Chip(string text)
    {
        var chip = new XPlatButton(text)
        {
            Style = XPlatButtonStyle.Secondary,
            AutoSize = true,
            Font = XPlatTheme.CaptionFont,
            Padding = new XPlatThickness(8f, 4f),
            MinimumSize = new SKSize(0f, 26f),
            CornerRadius = 13f,
        };
        chip.AddClickHandler(_ => SetStatus($"Inline chip '{text}' clicked."));
        return chip;
    }

    private XPlatPanel BuildSelectionCard()
    {
        var card = Card();

        card.Controls.Add(new XPlatLabel("Selection")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 24f,
            Font = XPlatTheme.EmphasizedFont,
            TextAlign = XPlatContentAlignment.BottomLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 4f),
        });

        // A drag starts on the label, passes over the whole-control button and ends in the text
        // box, so one gesture produces a partial, a whole and another partial range.
        card.Controls.Add(new XPlatLabel("Drag down from this label to start a selection.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 40f,
            WrapText = true,
            Font = XPlatTheme.DefaultFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
            Selectable = true,
        });

        var whole = new XPlatButton("Whole control")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
            Selectable = true,
        };
        whole.AddClickHandler(_ => SetStatus("Whole-control button clicked - a drag over it selects the button as one unit."));
        card.Controls.Add(whole);

        card.Controls.Add(new XPlatTextBox
        {
            Dock = XDock.Top,
            Height = 32f,
            Text = "...and finish in this box.",
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
        });

        card.Controls.Add(new XPlatLabel("Right-click the selection for the native Copy menu.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>
    /// Check boxes on the left, radio buttons on the right: the checks cover the three states and
    /// the disabled look, and the radios show two groups on one card separated only by
    /// <see cref="XPlatRadioButton.GroupName"/> rather than by a container each.
    /// </summary>
    private XPlatPanel BuildToggleDemo()
    {
        var section = new XPlatPanel
        {
            Dock = XDock.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };

        var checks = Card();
        checks.Dock = XDock.Left;
        checks.Width = 320f;
        checks.Margin = new XPlatThickness(0f, 0f, 16f, 0f);
        checks.Controls.Add(Heading("Check boxes"));

        foreach (string caption in new[] { "Auto-save", "Show line numbers", "Wrap long lines" })
        {
            var box = new XPlatCheckBox(caption)
            {
                Dock = XDock.Top,
                Margin = new XPlatThickness(0f, 0f, 0f, 6f),
            };
            box.AddCheckedChangedHandler(_ => SetStatus($"{box.Text} is {Describe(box.CheckState)}."));
            checks.Controls.Add(box);
        }

        // ThreeState is set before CheckState because the initializer runs in order: a check box
        // that cannot hold the indeterminate state would drop it on the way in.
        var threeState = new XPlatCheckBox("Three state")
        {
            Dock = XDock.Top,
            ThreeState = true,
            CheckState = XPlatCheckState.Indeterminate,
            Margin = new XPlatThickness(0f, 12f, 0f, 2f),
        };
        checks.Controls.Add(threeState);

        var stateReadout = ScrollCaption("Indeterminate");
        checks.Controls.Add(stateReadout);
        threeState.AddCheckedChangedHandler(_ => stateReadout.Text = Describe(threeState.CheckState));

        checks.Controls.Add(new XPlatCheckBox("Disabled, checked")
        {
            Dock = XDock.Top,
            Checked = true,
            Enabled = false,
            Margin = new XPlatThickness(0f, 12f, 0f, 0f),
        });

        var radios = Card();
        radios.Controls.Add(Heading("Radio buttons"));
        radios.Controls.Add(new XPlatLabel("Both groups are on this card: GroupName decides which radio buttons answer to each other.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 34f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
        });

        AddRadioGroup(radios, "Size", "size", ["Small", "Medium", "Large"], "Medium");
        AddRadioGroup(radios, "Alignment", "align", ["Left", "Centre", "Right"], "Left");

        radios.Controls.Add(new XPlatRadioButton("Disabled")
        {
            Dock = XDock.Top,
            Enabled = false,
            Margin = new XPlatThickness(0f, 12f, 0f, 0f),
        });

        section.Controls.Add(checks);
        section.Controls.Add(radios);
        return section;
    }

    /// <summary>Adds a captioned run of radio buttons that form one group by name.</summary>
    private void AddRadioGroup(XPlatPanel card, string caption, string groupName, string[] options, string selected)
    {
        var heading = ScrollCaption(caption);
        heading.Margin = new XPlatThickness(0f, 10f, 0f, 2f);
        card.Controls.Add(heading);

        foreach (string option in options)
        {
            var radio = new XPlatRadioButton(option)
            {
                Dock = XDock.Top,
                GroupName = groupName,
                Checked = option == selected,
                Margin = new XPlatThickness(0f, 0f, 0f, 4f),
            };
            radio.AddCheckedChangedHandler(_ =>
            {
                if (radio.Checked)
                {
                    SetStatus($"{caption} is {option}.");
                }
            });
            card.Controls.Add(radio);
        }
    }

    private static string Describe(XPlatCheckState state) => state switch
    {
        XPlatCheckState.Checked => "checked",
        XPlatCheckState.Indeterminate => "indeterminate",
        _ => "unchecked",
    };

    /// <summary>
    /// The three <see cref="XPlatProgressBarStyle"/>s, a disabled bar, and a Step button, so the
    /// fill is watched rather than described. Only the marquee bar animates, and only while the
    /// window is running a frame loop to tick it.
    /// </summary>
    private XPlatPanel BuildProgressDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Progress bars"));

        var readout = ScrollCaption("35%");
        card.Controls.Add(readout);

        var continuous = new XPlatProgressBar
        {
            Dock = XDock.Top,
            Height = 18f,
            Value = 35f,
            Margin = new XPlatThickness(0f, 0f, 0f, 16f),
        };
        card.Controls.Add(continuous);

        card.Controls.Add(ScrollCaption("Blocks"));
        card.Controls.Add(new XPlatProgressBar
        {
            Dock = XDock.Top,
            Height = 18f,
            Value = 60f,
            Style = XPlatProgressBarStyle.Blocks,
            Margin = new XPlatThickness(0f, 0f, 0f, 16f),
        });

        card.Controls.Add(ScrollCaption("Marquee (no speed set, so it follows the display)"));
        card.Controls.Add(new XPlatProgressBar
        {
            Dock = XDock.Top,
            Height = 18f,
            Style = XPlatProgressBarStyle.Marquee,
            Margin = new XPlatThickness(0f, 0f, 0f, 16f),
        });

        card.Controls.Add(ScrollCaption("Disabled"));
        card.Controls.Add(new XPlatProgressBar
        {
            Dock = XDock.Top,
            Height = 18f,
            Value = 70f,
            Enabled = false,
            Margin = new XPlatThickness(0f, 0f, 0f, 16f),
        });

        var buttons = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 32f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        };

        var step = new XPlatButton("Step")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        step.AddClickHandler(_ =>
        {
            if (continuous.Value >= continuous.Maximum)
            {
                continuous.Value = continuous.Minimum;
            }

            continuous.PerformStep();
            readout.Text = $"{continuous.Value:0}%";
            SetStatus($"Continuous bar is at {continuous.Value:0} of {continuous.Maximum:0}.");
        });
        buttons.Controls.Add(step);

        var reset = new XPlatButton("Reset")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
        };
        reset.AddClickHandler(_ =>
        {
            continuous.Value = 0f;
            readout.Text = "0%";
            SetStatus("Continuous bar reset.");
        });
        buttons.Controls.Add(reset);

        card.Controls.Add(buttons);
        card.Controls.Add(new XPlatLabel("Value is clamped to Minimum..Maximum, so PerformStep wraps only because this page resets it at the top.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 34f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>
    /// <see cref="XPlatNumericUpDown"/>: the spinner is the whole point, so the fields on this page
    /// cover a plain integer, a money amount with two decimals and a coarse step, a percentage
    /// whose increment is larger than one, and a disabled field for comparison.
    /// </summary>
    private XPlatPanel BuildNumericUpDownDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Numeric up-down"));

        var readout = ScrollCaption("Quantity 3 x 12.50 = 37.50");
        card.Controls.Add(readout);

        var quantity = new XPlatNumericUpDown
        {
            Minimum = 1m,
            Maximum = 99m,
            Value = 3m,
        };

        var price = new XPlatNumericUpDown
        {
            Minimum = 0m,
            Maximum = 9999.99m,
            DecimalPlaces = 2,
            Increment = 0.05m,
            Value = 12.5m,
        };

        quantity.AddValueChangedHandler(_ => UpdateTotal());
        price.AddValueChangedHandler(_ => UpdateTotal());

        void UpdateTotal() => readout.Text =
            $"Quantity {quantity.Value:0} x {price.Value:0.00} = {quantity.Value * price.Value:0.00}";

        card.Controls.Add(NumericRow("Quantity (1-99, step 1)", quantity));
        card.Controls.Add(NumericRow("Unit price (0-9999.99, step 0.05)", price));

        card.Controls.Add(NumericRow("Percentage (step 5, typed value snaps on Enter)", new XPlatNumericUpDown
        {
            Minimum = 0m,
            Maximum = 100m,
            Increment = 5m,
            Value = 40m,
        }));

        card.Controls.Add(NumericRow("Large value (two decimals, step 0.01)", new XPlatNumericUpDown
        {
            Minimum = 0m,
            Maximum = 999999.99m,
            DecimalPlaces = 2,
            Increment = 0.01m,
            Value = 987654.32m,
        }));

        card.Controls.Add(NumericRow("Disabled", new XPlatNumericUpDown
        {
            Minimum = 0m,
            Maximum = 10m,
            Value = 5m,
            Enabled = false,
        }));

        card.Controls.Add(new XPlatLabel(
            "Arrow keys step by Increment, Page Up/Down by ten times that, the wheel steps one detent per notch, " +
            "and a held spinner button repeats after a short delay. Typing is free-form: the value follows the text, " +
            "and the text is rewritten from the value on Enter or when the focus leaves.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 62f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 6f, 0f, 0f),
        });

        return card;
    }

    /// <summary>A caption with a fixed-width field on the right, so the fields line up.</summary>
    private static XPlatPanel NumericRow(string caption, XPlatControl field)
    {
        var row = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 34f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
            Margin = new XPlatThickness(0f, 0f, 0f, 10f),
        };

        row.Controls.Add(new XPlatLabel(caption)
        {
            Dock = XDock.Fill,
            AutoSize = false,
            Font = XPlatTheme.DefaultFont,
            TextAlign = XPlatContentAlignment.MiddleLeft,
            Margin = new XPlatThickness(0f, 0f, 12f, 0f),
        });

        field.Dock = XDock.Right;
        field.Width = 170f;
        row.Controls.Add(field);

        return row;
    }

    /// <summary>
    /// <see cref="XPlatTrackBar"/>: a horizontal bar whose value drives a vertical one, so the two
    /// orientations can be compared against the same number, plus a bar with no ticks and a
    /// disabled one.
    /// </summary>
    private XPlatPanel BuildTrackBarDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Track bars"));

        var readout = ScrollCaption("Value 30 of 100");
        card.Controls.Add(readout);

        var horizontal = new XPlatTrackBar
        {
            Dock = XDock.Top,
            Height = 28f,
            Maximum = 100f,
            Value = 30f,
            SmallChange = 1f,
            LargeChange = 10f,
            TickFrequency = 10f,
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        };

        var vertical = new XPlatTrackBar
        {
            Dock = XDock.Left,
            Width = 44f,
            Orientation = XPlatOrientation.Vertical,
            Maximum = 100f,
            Value = 30f,
            SmallChange = 1f,
            LargeChange = 10f,
            TickFrequency = 10f,
            Margin = new XPlatThickness(0f, 0f, 16f, 0f),
        };

        horizontal.AddValueChangedHandler(_ =>
        {
            vertical.Value = horizontal.Value;
            readout.Text = $"Value {horizontal.Value:0} of 100";
        });

        card.Controls.Add(horizontal);

        var row = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 150f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        };

        row.Controls.Add(new XPlatLabel("The same value, drawn vertically: the knob sits at the bottom for Minimum, so dragging up raises the value.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });
        row.Controls.Add(vertical);
        card.Controls.Add(row);

        card.Controls.Add(ScrollCaption("No tick marks (TickFrequency 0), fine steps"));

        var fine = new XPlatTrackBar
        {
            Dock = XDock.Top,
            Height = 28f,
            Minimum = -50f,
            Maximum = 50f,
            Value = 0f,
            SmallChange = 0.5f,
            LargeChange = 5f,
            TickFrequency = 0f,
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        };

        var fineReadout = ScrollCaption("0.0");
        fine.AddValueChangedHandler(_ => fineReadout.Text = $"{fine.Value:0.0}");
        card.Controls.Add(fine);
        card.Controls.Add(fineReadout);

        card.Controls.Add(ScrollCaption("Disabled"));

        card.Controls.Add(new XPlatTrackBar
        {
            Dock = XDock.Top,
            Height = 28f,
            Maximum = 100f,
            Value = 65f,
            TickFrequency = 25f,
            Enabled = false,
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        });

        card.Controls.Add(new XPlatLabel(
            "Pressing the knob starts a drag and the value follows the pointer; pressing the track pages by " +
            "LargeChange toward the press. Arrow keys step by SmallChange, Page Up/Down by LargeChange, " +
            "Home and End jump to the ends, and the wheel steps one SmallChange per detent.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 62f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>
    /// The in-library menus. The bar across the top of the window is drawn by the library on
    /// Windows and Linux and reachable with Alt plus a mnemonic; this page adds a context menu that
    /// uses every row shape the renderer knows - icon, check mark, submenu, shortcut hint, disabled
    /// entry and a hosted control.
    /// </summary>
    private XPlatPanel BuildMenuDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Menus"));

        var readout = ScrollCaption(
            "Alt+V and Alt+W open the window's own bar. Right-click the box for a context menu.");
        readout.Margin = new XPlatThickness(0f, 0f, 0f, 12f);
        card.Controls.Add(readout);

        var target = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 96f,
            BorderWidth = 1f,
            CornerRadius = 8f,
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        };

        target.Controls.Add(new XPlatLabel("Right-click anywhere in this box")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            TextAlign = XPlatContentAlignment.MiddleCenter,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            Font = XPlatTheme.CaptionFont,
        });

        target.ContextMenu = BuildMenuDemoContextMenu(readout);
        _menuDemoTarget = target;
        card.Controls.Add(target);

        // The bar belongs to the window rather than to a control, so opening one of its menus is a
        // host-level call - which is also how Alt plus a mnemonic gets there.
        XPlatButton? openView = null;
        openView = DemoButton("Open the View menu", () =>
        {
            var view = openView?.Host?.MenuBar;
            if (view?.MenuBar is not { } bar || bar.Menus.Count == 0)
            {
                readout.Text = "This backend has no in-window menu bar - it uses the native one.";
                return;
            }

            view.OpenMenu(0);
        });

        card.Controls.Add(openView);

        card.Controls.Add(new XPlatLabel(
            "The bar is an ordinary control in the window's own tree, so it scrolls, resizes and repaints " +
            "with everything else; a drop-down is a popup surface that holds an XPlatMenuView. Mnemonics come " +
            "from an & in the text, submenus open to the side of their parent row and nest as deeply as the " +
            "application likes - Recent > Program.cs is two of them - and a menu taller than the room it has " +
            "scrolls its rows behind arrows at either end. A row may host a control instead of a label: the " +
            "zoom slider is an XPlatTrackBar living inside the menu.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 92f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>
    /// Opens the menus page's context menu as if the box had been right-clicked, which is what a
    /// screenshot of the page needs in order to show a menu at all.
    /// </summary>
    /// <param name="window">The window the sample is showing in.</param>
    public void OpenMenuDemo(XPlatWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (_menuDemoTarget?.ContextMenu is not { } menu)
        {
            return;
        }

        // Nothing has painted yet, so the page has no bounds until something lays it out.
        window.Host.PerformLayout();

        var location = SKPoint.Empty;
        for (var control = _menuDemoTarget; control is not null; control = control.Parent)
        {
            location.Offset(control.Bounds.Left, control.Bounds.Top);
        }

        // Shown through the presenter rather than through the menu itself, because a screenshot
        // wants the row under the pointer and its submenu too, and only the view can open those.
        var view = new XPlatMenuView(menu);
        XPlatMenuPresenter.ShowMenu(window.Host, view, new SKPoint(location.X + 48f, location.Y + 24f));

        // Follow the first row that opens a submenu all the way down, so the picture shows the
        // whole chain rather than just the level the presenter opened.
        for (int depth = 0, limit = 4; depth < limit; depth++)
        {
            var level = view.DeepestMenu;
            int index = -1;
            for (int i = 0; i < level.Items.Count && index < 0; i++)
            {
                index = level.Items[i].HasSubMenu ? i : -1;
            }

            if (index < 0)
            {
                break;
            }

            level.Highlight(index);
            if (!level.OpenSubMenu(index))
            {
                break;
            }
        }
    }

    /// <summary>
    /// The context menu the menus page shows: one row of every shape the renderer supports, so a
    /// screenshot of the open menu covers the whole of it, plus a submenu inside a submenu and one
    /// menu with more rows than any display can show at once.
    /// </summary>
    private XPlatContextMenu BuildMenuDemoContextMenu(XPlatLabel readout)
    {
        var grid = new XPlatMenuItem("Show &grid", () =>
            readout.Text = "'Show grid' picked - the tick is drawn in the check column.")
        {
            Checked = true,
        };

        var recent = new XPlatMenuItem("&Recent");

        // A submenu inside a submenu, so the chain is exercised at a depth a single level cannot
        // reach: Recent > Program.cs > a place in the file.
        var program = new XPlatMenuItem("&Program.cs");
        foreach (var place in new[] { "The top of the file", "Main", "RenderSample" })
        {
            var spot = place;
            program.SubItems.Add(new XPlatMenuItem(
                spot, () => readout.Text = $"'Recent > Program.cs > {spot}' picked."));
        }

        recent.SubItems.Add(program);

        foreach (var name in new[] { "DemoUi.cs", "Program.cs (older)" })
        {
            var file = name;
            recent.SubItems.Add(new XPlatMenuItem(file, () => readout.Text = $"'Recent > {file}' picked."));
        }

        // More rows than fit on any display, so the menu keeps the height it is given and scrolls
        // its rows instead of growing past the edge - which is what the arrows at either end, the
        // wheel and the keyboard keeping the highlight in view are for.
        var scrolling = new XPlatMenuItem("&Scroll test");
        for (int number = 1; number <= 320; number++)
        {
            int picked = number;
            scrolling.SubItems.Add(new XPlatMenuItem(
                $"Item {picked:000}",
                () => readout.Text = $"'Scroll test > Item {picked:000}' picked."));
        }

        var zoom = new XPlatTrackBar
        {
            Minimum = 50f,
            Maximum = 200f,
            Value = 100f,
            TickFrequency = 25f,
            Margin = new XPlatThickness(0f),
        };

        zoom.AddValueChangedHandler(_ =>
            readout.Text = $"Zoom is {zoom.Value:0}% - a control hosted in a menu row.");

        return new XPlatContextMenu()
            .Add(new XPlatMenuItem("&New file", () => readout.Text = "'New file' picked.")
            {
                Image = MenuGlyph((canvas, paint) => canvas.DrawRoundRect(SKRect.Create(7f, 4f, 18f, 24f), 3f, 3f, paint)),
                Shortcut = new XPlatShortcut(XPlatKey.N, XPlatModifiers.Control),
            })
            .Add(new XPlatMenuItem("&Open...", () => readout.Text = "'Open' picked.")
            {
                Image = MenuGlyph((canvas, paint) =>
                {
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 2.5f;
                    canvas.DrawRect(SKRect.Create(5f, 10f, 22f, 16f), paint);
                    canvas.DrawLine(5f, 10f, 11f, 5f, paint);
                    canvas.DrawLine(11f, 5f, 16f, 10f, paint);
                }),
                Shortcut = new XPlatShortcut(XPlatKey.O, XPlatModifiers.Control),
            })
            .AddSeparator()
            .Add(recent)
            .Add(scrolling)
            .Add(grid)
            .AddSeparator()
            .Add(new XPlatMenuItem("Zoom") { Control = zoom })
            .AddSeparator()
            .Add(new XPlatMenuItem("&Delete", null)
            {
                Enabled = false,
                Shortcut = new XPlatShortcut(XPlatKey.Delete),
            });
    }

    /// <summary>
    /// A monochrome glyph for a menu row, drawn in code so the sample needs no image files. The
    /// renderer fits whatever it is given into the icon column, so the source only has to be large
    /// enough to stay crisp.
    /// </summary>
    private static SKImage MenuGlyph(Action<SKCanvas, SKPaint> draw)
    {
        using var surface = SKSurface.Create(new SKImageInfo(32, 32, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var paint = new SKPaint { IsAntialias = true, Color = XPlatTheme.Foreground };
        draw(surface.Canvas, paint);
        surface.Canvas.Flush();
        return surface.Snapshot();
    }

    /// <summary>
    /// <see cref="XPlatLinkLabel"/>: two links that hand their URI to the desktop, one that
    /// navigates in the application instead, a sentence with a link inside it, and a disabled
    /// link - which greys out rather than staying the accent colour.
    /// </summary>
    private XPlatPanel BuildLinkLabelDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Link labels"));

        var readout = ScrollCaption("Click a link, or Tab to one and press Enter.");
        card.Controls.Add(readout);

        var home = new XPlatLinkLabel("Project home", "https://github.com/drysart/XPlatUI")
        {
            Dock = XDock.Top,
            Margin = new XPlatThickness(0f, 0f, 0f, 10f),
        };

        home.AddLinkClickedHandler((_, e) =>
            readout.Text = e.Opened ? $"Handed {e.Url} to the desktop" : $"Could not open {e.Url}");

        var issue = new XPlatLinkLabel("Report an issue", "https://github.com/drysart/XPlatUI/issues")
        {
            Dock = XDock.Top,
            Margin = new XPlatThickness(0f, 0f, 0f, 10f),
        };

        issue.AddLinkClickedHandler((_, e) =>
            readout.Text = e.Opened ? $"Handed {e.Url} to the desktop" : $"Could not open {e.Url}");

        // No Url at all: the label is only a click target, and the handler decides what happens.
        var inApp = new XPlatLinkLabel("What's new (navigates inside the app)")
        {
            Dock = XDock.Top,
            AutoLaunch = false,
            Margin = new XPlatThickness(0f, 0f, 0f, 16f),
        };

        inApp.AddLinkClickedHandler((_, e) => readout.Text = $"In-app navigation from \"{e.Span?.Text}\"");

        // A sentence with a link inside it: the label is a rich label, so the link is just one span
        // among the plain ones around it.
        var sentence = new XPlatLinkLabel
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 44f,
            WrapText = true,
            AutoLaunch = false,
            Margin = new XPlatThickness(0f, 0f, 0f, 16f),
        };

        sentence.AddText("Every control in this library draws itself with Skia and owns no native handle, so ");
        sentence.AddLink("the whole set", span => readout.Text = $"Span \"{span.Text}\" clicked");
        sentence.AddText(" works the same way on Windows, macOS and Linux.");

        card.Controls.Add(home);
        card.Controls.Add(issue);
        card.Controls.Add(inApp);
        card.Controls.Add(sentence);

        card.Controls.Add(new XPlatLinkLabel("A disabled link", "https://example.com")
        {
            Dock = XDock.Top,
            Enabled = false,
            Margin = new XPlatThickness(0f, 0f, 0f, 16f),
        });

        card.Controls.Add(new XPlatLabel(
            "A link is an XPlatRichLabel whose text is a link span, so AddText and AddLink build a sentence around it. " +
            "Clicking one, or pressing Enter or Space while it has the focus, raises LinkClicked; with AutoLaunch on " +
            "and a Url set, the label also hands the URI to the desktop through XPlatLauncher.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 62f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>
    /// <see cref="XPlatToolStrip"/> and <see cref="XPlatStatusStrip"/>: docked bars that hold items
    /// rather than child controls. The first card is a mock editor whose tool bar and status bar
    /// are strips; the second drives a status strip's progress item from a row of buttons.
    /// </summary>
    private XPlatPanel BuildToolStripDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Tool strip and status strip"));

        var readout = ScrollCaption("Click an item, or Tab to a strip and use the arrow keys.");
        readout.Margin = new XPlatThickness(0f, 0f, 0f, 12f);
        card.Controls.Add(readout);

        var revert = new XPlatToolStripButton("Revert") { Enabled = false };
        revert.AddClickHandler(_ => readout.Text = "Tool strip: Revert");

        XPlatToolStripButton ToolItem(string text, string message)
        {
            var item = new XPlatToolStripButton(text);
            item.AddClickHandler(_ =>
            {
                readout.Text = message;
                revert.Enabled = true;
            });

            return item;
        }

        var fit = ToolItem("Fit", "Tool strip: Fit");
        fit.Alignment = XPlatToolStripItemAlignment.Far;

        var farSeparator = new XPlatToolStripSeparator { Alignment = XPlatToolStripItemAlignment.Far };

        var toolbar = new XPlatToolStrip();
        toolbar.Items.Add(ToolItem("New", "Tool strip: New"));
        toolbar.Items.Add(ToolItem("Open", "Tool strip: Open"));
        toolbar.Items.Add(new XPlatToolStripSeparator());
        toolbar.Items.Add(ToolItem("Save", "Tool strip: Save"));
        toolbar.Items.Add(revert);
        toolbar.Items.Add(new XPlatToolStripSpring());
        toolbar.Items.Add(new XPlatToolStripLabel("Zoom: 100%"));
        toolbar.Items.Add(farSeparator);
        toolbar.Items.Add(fit);

        var editorStatus = new XPlatStatusStrip();
        editorStatus.Items.Add(new XPlatToolStripLabel("Ready")
        {
            ForeColor = XPlatThemeColor.MutedForeground,
        });
        editorStatus.Items.Add(new XPlatToolStripSpring());
        editorStatus.Items.Add(new XPlatToolStripLabel("UTF-8")
        {
            ForeColor = XPlatThemeColor.MutedForeground,
        });
        editorStatus.Items.Add(new XPlatToolStripSeparator());
        editorStatus.Items.Add(new XPlatToolStripProgressBar { Width = 90f, Value = 65f });

        var editor = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 156f,
            CornerRadius = 8f,
            BorderWidth = 1f,
            Padding = new XPlatThickness(0f),
            Margin = new XPlatThickness(0f, 0f, 0f, 20f),
        };

        editor.Controls.Add(toolbar);
        editor.Controls.Add(editorStatus);
        editor.Controls.Add(new XPlatLabel(
            "The tool bar and the status bar are one control each, and every item in them is a plain object " +
            "rather than a child control: nine items on the top bar and five on the bottom cost one paint pass " +
            "between them. The spring takes the slack, which is what pins Zoom and Fit to the right-hand end, " +
            "and Revert stays disabled until one of the other items is used.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Padding = new XPlatThickness(14f, 10f),
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        card.Controls.Add(editor);
        card.Controls.Add(Heading("Progress in a status strip"));

        var percentLabel = new XPlatToolStripLabel("0%")
        {
            ForeColor = XPlatThemeColor.MutedForeground,
        };

        var progress = new XPlatToolStripProgressBar { Width = 220f };

        var copyStatus = new XPlatStatusStrip();
        copyStatus.Items.Add(new XPlatToolStripLabel("Copying files..."));
        copyStatus.Items.Add(new XPlatToolStripSeparator());
        copyStatus.Items.Add(progress);
        copyStatus.Items.Add(percentLabel);
        copyStatus.Items.Add(new XPlatToolStripSpring());

        var clear = new XPlatToolStripButton("Clear") { Alignment = XPlatToolStripItemAlignment.Far };
        clear.AddClickHandler(_ =>
        {
            progress.Value = 0f;
            SyncPercent();
            readout.Text = "Status strip: Clear";
        });

        copyStatus.Items.Add(clear);

        void SyncPercent() => percentLabel.Text = $"{(int)MathF.Round(progress.Percent * 100f)}%";

        var statusBox = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 92f,
            CornerRadius = 8f,
            BorderWidth = 1f,
            Padding = new XPlatThickness(0f),
            Margin = new XPlatThickness(0f, 0f, 0f, 16f),
        };

        statusBox.Controls.Add(copyStatus);
        statusBox.Controls.Add(new XPlatLabel(
            "The bar, the percentage and the Clear button are items of one status strip, so the strip is a " +
            "single tab stop: the arrow keys move between the items and Enter activates the one under them.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Padding = new XPlatThickness(14f, 10f),
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        var row = ButtonRow();

        var start = new XPlatButton("Start")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 6f, 0f),
        };
        start.AddClickHandler(_ =>
        {
            progress.Value = 0f;
            SyncPercent();
            readout.Text = "Copy started.";
        });

        var step = new XPlatButton("Step 10")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 6f, 0f),
        };
        step.AddClickHandler(_ =>
        {
            progress.Value = MathF.Min(progress.Maximum, progress.Value + 10f);
            SyncPercent();
            readout.Text = $"Copy at {percentLabel.Text}";
        });

        var finish = new XPlatButton("Finish")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 6f, 0f),
        };
        finish.AddClickHandler(_ =>
        {
            progress.Value = progress.Maximum;
            SyncPercent();
            readout.Text = "Copy finished.";
        });

        row.Controls.Add(start);
        row.Controls.Add(step);
        row.Controls.Add(finish);

        card.Controls.Add(row);
        card.Controls.Add(statusBox);
        return card;
    }

    /// <summary>
    /// <see cref="XPlatGroupBox"/>: the caption sits in a break in the top edge and the content is
    /// laid out below it. The right-hand box scrolls, which is where the caption band earns its
    /// keep - the rows are clipped below the caption instead of sliding under it.
    /// </summary>
    private XPlatPanel BuildGroupBoxDemo()
    {
        var section = new XPlatPanel
        {
            Dock = XDock.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };

        var column = new XPlatPanel
        {
            Dock = XDock.Left,
            Width = 320f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
            Margin = new XPlatThickness(0f, 0f, 16f, 0f),
        };

        var appearance = new XPlatGroupBox("Appearance")
        {
            Dock = XDock.Top,
            Height = 168f,
            Margin = new XPlatThickness(0f, 0f, 0f, 16f),
        };
        foreach (string caption in new[] { "Show grid", "Snap to grid", "Show rulers" })
        {
            var box = new XPlatCheckBox(caption)
            {
                Dock = XDock.Top,
                Checked = caption != "Show rulers",
                Margin = new XPlatThickness(0f, 0f, 0f, 4f),
            };
            box.AddCheckedChangedHandler(_ => SetStatus($"{box.Text} is {Describe(box.CheckState)}."));
            appearance.Controls.Add(box);
        }

        column.Controls.Add(appearance);

        // Disabling the group box disables everything inside it, so the caption, the frame and the
        // check boxes all grey out together.
        var advanced = new XPlatGroupBox("Advanced (disabled)")
        {
            Dock = XDock.Top,
            Height = 116f,
            Enabled = false,
        };

        foreach (string caption in new[] { "Trace layout", "Cache layers" })
        {
            advanced.Controls.Add(new XPlatCheckBox(caption)
            {
                Dock = XDock.Top,
                Checked = caption == "Trace layout",
            });
        }

        column.Controls.Add(advanced);

        section.Controls.Add(column);

        var rows = new XPlatGroupBox("Rows")
        {
            Dock = XDock.Fill,
        };

        // A group box scrolls its own content below the caption; the rows go in a panel of their
        // own so they can start at its top edge and scroll as one block.
        var list = new XPlatPanel
        {
            Dock = XDock.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
            VerticalScroll = XPlatScrollStyle.Automatic,
            HorizontalScroll = XPlatScrollStyle.None,
        };

        for (int i = 0; i < 24; i++)
        {
            var row = new XPlatButton($"Row {i + 1}")
            {
                AutoSize = false,
                Style = XPlatButtonStyle.Secondary,
                Bounds = new SKRect(0f, i * 30f, 160f, (i * 30f) + 26f),
            };
            row.AddClickHandler(_ => SetStatus($"Row {i + 1} clicked inside a group box."));
            list.Controls.Add(row);
        }

        rows.Controls.Add(list);
        section.Controls.Add(rows);
        return section;
    }

    /// <summary>
    /// An <see cref="XPlatListBox"/> in <see cref="XPlatSelectionMode.MultiExtended"/>: click to
    /// select, Ctrl+click to toggle, Shift+click to extend. The button cycles the mode so all four
    /// behave the same way as the WinForms control.
    /// </summary>
    private XPlatPanel BuildListBoxCard()
    {
        var card = Card();

        var modeCaption = ScrollCaption("MultiExtended");
        card.Controls.Add(modeCaption);

        var list = new XPlatListBox
        {
            Dock = XDock.Top,
            Height = 108f,
            SelectionMode = XPlatSelectionMode.MultiExtended,
            VerticalScroll = XPlatScrollStyle.Automatic,
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
        };

        list.Items.AddRange(new object[]
        {
            "Overview", "Labels", "Buttons", "Text input", "Rich text", "Images",
            "Scrolling", "Lists", "Dialogs", "Split container", "Selection", "Drag and drop",
            "Menus", "Owned windows",
        });

        list.SelectedIndex = 0;
        card.Controls.Add(list);

        var selectionLabel = ScrollCaption("1 selected");
        selectionLabel.Margin = new XPlatThickness(0f, 0f, 0f, 6f);
        card.Controls.Add(selectionLabel);

        list.AddSelectedIndexChangedHandler(_ =>
        {
            int count = list.SelectedIndices.Count;
            selectionLabel.Text = count switch
            {
                0 => "no selection",
                1 => $"\"{list.GetItemText(list.SelectedIndex)}\" selected",
                _ => $"{count} rows",
            };
        });

        list.AddItemActivatedHandler(_ =>
            SetStatus($"Activated \"{list.GetItemText(list.SelectedIndex)}\"."));

        var modeButton = new XPlatButton("Next mode")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
        };

        modeButton.AddClickHandler(_ =>
        {
            var mode = list.SelectionMode switch
            {
                XPlatSelectionMode.None => XPlatSelectionMode.One,
                XPlatSelectionMode.One => XPlatSelectionMode.MultiSimple,
                XPlatSelectionMode.MultiSimple => XPlatSelectionMode.MultiExtended,
                _ => XPlatSelectionMode.None,
            };

            list.SelectionMode = mode;
            modeCaption.Text = mode.ToString();
            SetStatus($"List selection mode is {mode}.");
        });

        card.Controls.Add(modeButton);

        return card;
    }

    /// <summary>
    /// Two list views over the same shape of data: one in <see cref="XPlatView.Details"/> with
    /// sortable columns and grid lines, one in <see cref="XPlatView.List"/>. Both only paint the
    /// rows on screen, so the row count is there to make that claim checkable.
    /// </summary>
    private XPlatPanel BuildListViewDemo()
    {
        var card = Card();

        var sortCaption = ScrollCaption("Click a column header to sort; drag a header edge to resize");
        card.Controls.Add(sortCaption);

        var details = new XPlatListView
        {
            Dock = XDock.Top,
            Height = 200f,
            View = XPlatView.Details,
            GridLines = true,
            SelectionMode = XPlatSelectionMode.MultiExtended,
            VerticalScroll = XPlatScrollStyle.Automatic,
            HorizontalScroll = XPlatScrollStyle.Automatic,
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
        };

        // The Size cells read "812 KB" and "4 KB", which is the wrong way round as text, so the column
        // compares the byte count the rows keep in their Tag instead.
        var sizeColumn = new XPlatListViewColumn("Size", 76f, XPlatContentAlignment.MiddleRight)
        {
            Comparer = (left, right) => BytesOf(left).CompareTo(BytesOf(right)),
        };

        details.Columns.Add(new XPlatListViewColumn("Name", 168f));
        details.Columns.Add(new XPlatListViewColumn("Kind", 104f));
        details.Columns.Add(sizeColumn);
        details.Columns.Add(new XPlatListViewColumn("Modified", 118f));

        foreach (var row in SampleFiles)
        {
            details.Items.Add(new XPlatListViewItem(row.Name, row.Kind, FormatSize(row.Bytes), row.Modified)
            {
                Tag = row.Bytes,
            });
        }

        details.SelectedIndex = 0;
        card.Controls.Add(details);

        var selectionCaption = ScrollCaption("1 selected");
        card.Controls.Add(selectionCaption);

        details.AddSelectedIndexChangedHandler(_ =>
        {
            int count = details.SelectedIndices.Count;
            selectionCaption.Text = count switch
            {
                0 => "no selection",
                1 => $"{details.Items[details.SelectedIndex].Text} selected",
                _ => $"{count} rows selected",
            };
        });

        details.AddItemActivatedHandler(_ =>
            SetStatus($"Opened {details.Items[details.SelectedIndex].Text}."));

        details.AddColumnClickHandler((_, e) =>
            sortCaption.Text = $"Sorted by {e.Column.Text}, {details.SortOrder}");

        var icons = new XPlatListView
        {
            Dock = XDock.Top,
            Height = 148f,
            View = XPlatView.List,
            SelectionMode = XPlatSelectionMode.One,
            VerticalScroll = XPlatScrollStyle.Automatic,
            HorizontalScroll = XPlatScrollStyle.Automatic,
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
        };

        foreach (var row in SampleFiles)
        {
            icons.Items.Add(new XPlatListViewItem(row.Name));
        }

        icons.SelectedIndex = 2;
        card.Controls.Add(icons);

        var iconsCaption = ScrollCaption("List view");
        iconsCaption.Margin = new XPlatThickness(0f, 0f, 0f, 6f);
        card.Controls.Add(iconsCaption);

        var buttons = ButtonRow();

        var clearSort = new XPlatButton("Clear sort")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 6f, 0f),
        };

        clearSort.AddClickHandler(_ =>
        {
            details.SortOrder = XPlatSortOrder.None;
            sortCaption.Text = "Click a column header to sort";
            SetStatus("Sorting cleared.");
        });

        var grow = new XPlatButton("Add rows")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
        };

        grow.AddClickHandler(_ =>
        {
            details.BeginUpdate();
            try
            {
                for (int index = 0; index < 500; index++)
                {
                    long bytes = (index + 1) * 1_024L;
                    details.Items.Add(new XPlatListViewItem(
                        $"generated-{details.Items.Count:D4}.bin",
                        "File",
                        FormatSize(bytes),
                        "today")
                    {
                        Tag = bytes,
                    });
                }
            }
            finally
            {
                details.EndUpdate();
            }

            SetStatus($"{details.Items.Count} rows; only the visible ones are drawn.");
        });

        buttons.Controls.Add(clearSort);
        buttons.Controls.Add(grow);

        var lockWidths = new XPlatButton("Lock widths")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(6f, 0f, 0f, 0f),
        };

        lockWidths.AddClickHandler(_ =>
        {
            details.AllowColumnResize = !details.AllowColumnResize;
            lockWidths.Text = details.AllowColumnResize ? "Lock widths" : "Unlock widths";
            SetStatus(details.AllowColumnResize
                ? "Header edges can be dragged again."
                : "Header edges are locked; the columns cannot be resized.");
        });

        buttons.Controls.Add(lockWidths);
        card.Controls.Add(buttons);

        var countCaption = ScrollCaption($"{details.Items.Count} rows");
        card.Controls.Add(countCaption);

        return card;
    }

    private static long BytesOf(XPlatListViewItem item) => item.Tag is long bytes ? bytes : 0L;

    private static string FormatSize(long bytes) => bytes >= 1024 ? $"{bytes / 1024} KB" : $"{bytes} B";

    private static readonly (string Name, string Kind, long Bytes, string Modified)[] SampleFiles =
    {
        ("readme.txt", "Text", 4_096, "2024-05-02 09:14"),
        ("XPlatControl.cs", "Source", 62_464, "2024-05-11 18:02"),
        ("XPlatListView.cs", "Source", 31_744, "2024-05-12 07:45"),
        ("theme-light.json", "Data", 2_048, "2024-04-28 12:30"),
        ("theme-dark.json", "Data", 2_048, "2024-04-28 12:30"),
        ("render-target.png", "Image", 151_552, "2024-05-09 22:11"),
        ("harness.exe", "Binary", 98_304, "2024-05-10 08:00"),
        ("LICENSE", "Text", 1_024, "2024-01-04 00:00"),
        ("build.log", "Log", 831_488, "2024-05-12 06:59"),
        ("a-very-long-file-name-that-should-be-trimmed-with-an-ellipsis.txt", "Text", 7_168, "2024-05-01 11:20"),
    };

    /// <summary>
    /// A tree with check boxes over a small source-tree shape. The rows are virtualized the same way
    /// the lists are - the control only knows the nodes whose ancestors are open - so the branch the
    /// "Add leaves" button grows costs nothing until it is opened.
    /// </summary>
    private XPlatPanel BuildTreeViewDemo()
    {
        var card = Card();

        var selectionCaption = ScrollCaption("Select a node");
        card.Controls.Add(selectionCaption);

        var tree = new XPlatTreeView
        {
            Dock = XDock.Top,
            Height = 260f,
            CheckBoxes = true,
            SelectionMode = XPlatSelectionMode.One,
            VerticalScroll = XPlatScrollStyle.Automatic,
            HorizontalScroll = XPlatScrollStyle.Automatic,
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
        };

        var src = new XPlatTreeNode("src");

        var core = new XPlatTreeNode("Core");
        core.Nodes.Add(new XPlatTreeNode("XPlatControl.cs"));
        core.Nodes.Add(new XPlatTreeNode("XPlatPainter.cs"));
        core.Nodes.Add(new XPlatTreeNode("XPlatTheme.cs"));
        src.Nodes.Add(core);

        var controls = new XPlatTreeNode("Controls");
        controls.Nodes.Add(new XPlatTreeNode("XPlatButton.cs"));
        controls.Nodes.Add(new XPlatTreeNode("XPlatListView.cs"));
        controls.Nodes.Add(new XPlatTreeNode("XPlatTreeView.cs"));
        src.Nodes.Add(controls);

        var platforms = new XPlatTreeNode("Platform");
        platforms.Nodes.Add(new XPlatTreeNode("Win32"));
        platforms.Nodes.Add(new XPlatTreeNode("AppKit"));
        platforms.Nodes.Add(new XPlatTreeNode("Gtk4"));
        src.Nodes.Add(platforms);

        tree.Nodes.Add(src);
        tree.Nodes.Add(new XPlatTreeNode("README.md"));
        tree.Nodes.Add(new XPlatTreeNode("LICENSE"));

        src.Expand();
        core.Expand();
        tree.SelectedNode = core.Nodes[0];

        // Two of the three files under Controls are checked, so that branch reads as indeterminate.
        controls.Nodes[0].Checked = true;
        controls.Nodes[1].Checked = true;

        card.Controls.Add(tree);

        tree.AddSelectedIndexChangedHandler(_ =>
            selectionCaption.Text = tree.SelectedNode is { } node
                ? $"{node.Text}, level {node.Level}"
                : "no selection");

        var buttons = ButtonRow();

        var expand = new XPlatButton("Expand all")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 6f, 0f),
        };

        expand.AddClickHandler(_ =>
        {
            tree.ExpandAll();
            SetStatus($"{tree.ItemCount} rows visible.");
        });

        var collapse = new XPlatButton("Collapse all")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 6f, 0f),
        };

        collapse.AddClickHandler(_ =>
        {
            tree.CollapseAll();
            SetStatus($"{tree.ItemCount} rows visible.");
        });

        var grow = new XPlatButton("Add 5,000 leaves")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
        };

        grow.AddClickHandler(_ =>
        {
            // The branch is built while it is still detached, so filling it costs nothing; adding it
            // to the tree is the one rebuild.
            var generated = new XPlatTreeNode("generated");
            for (int index = 0; index < 5000; index++)
            {
                generated.Nodes.Add(new XPlatTreeNode($"leaf-{index:D4}.cs"));
            }

            tree.Nodes.Add(generated);
            generated.Expand();

            SetStatus($"{tree.Nodes.Count} roots, {tree.ItemCount} rows visible; only those are drawn.");
        });

        buttons.Controls.Add(expand);
        buttons.Controls.Add(collapse);
        buttons.Controls.Add(grow);
        card.Controls.Add(buttons);

        var checkCaption = ScrollCaption(
            $"{CountChecked(tree.Nodes)} of {CountNodes(tree.Nodes)} nodes checked");
        card.Controls.Add(checkCaption);

        tree.AddAfterCheckHandler(_ => checkCaption.Text =
            $"{CountChecked(tree.Nodes)} of {CountNodes(tree.Nodes)} nodes checked");

        return card;
    }

    private static int CountNodes(XPlatTreeView.XPlatTreeNodeCollection nodes)
    {
        int count = 0;
        foreach (var node in nodes)
        {
            count += 1 + CountNodes(node.Nodes);
        }

        return count;
    }

    private static int CountChecked(XPlatTreeView.XPlatTreeNodeCollection nodes)
    {
        int count = 0;
        foreach (var node in nodes)
        {
            if (node.Checked)
            {
                count++;
            }

            count += CountChecked(node.Nodes);
        }

        return count;
    }

    /// <summary>
    /// The measured-rendering page: the live presentation path, a rolling frame-time readout and a
    /// fixed benchmark, all driven by one 15 ms timer that runs only while the page is displayed.
    /// </summary>
    /// <remarks>
    /// The benchmark invalidates the window (or a small swatch, in partial mode) for the same
    /// number of frames under either backend, so a CPU run and a GL run are comparable. The frame
    /// time is <see cref="XPlatUiHost.LastFrameTime"/>, which is the host's paint only; a GL run's
    /// <c>SwapBuffers</c> happens after that and is not in the number, which the README states.
    /// </remarks>
    private XPlatPanel BuildMetricsPage()
    {
        var card = Card();
        _metricsPage = card;

        _metricsReadout = new XPlatLabel("frame: waiting for the first paint")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 22f,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.MiddleLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        };

        _measureButton = new XPlatButton($"Measure {BenchmarkMeasuredFrames} frames")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        _measureButton.AddClickHandler(_ => StartMeasurement());

        _damageButton = new XPlatButton("Damage: full")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        _damageButton.AddClickHandler(_ => ToggleDamage());

        var buttons = Row(32f);
        buttons.Controls.Add(_measureButton);
        buttons.Controls.Add(_damageButton);

        // The partial-damage target is deliberately separate from the readout: repainting this
        // swatch every frame costs about nothing, and it is the area the partial numbers describe.
        _metricsSwatch = new XPlatPanel
        {
            Dock = XDock.Left,
            Width = 180f,
            Height = 180f,
            BackColor = new SKColor(0x2F, 0x6F, 0x8F),
            CornerRadius = 8f,
        };

        var swatchRow = Row(180f);
        swatchRow.Controls.Add(_metricsSwatch);

        var caveat = new XPlatLabel(
            "One 15 ms timer (about 66 Hz) drives both the rolling readout and the benchmark; it runs only while this page is " +
            "displayed. The damage toggle changes what is repainted: partial damage repaints only the swatch. Under GL the " +
            "damaged region goes into a retained buffer and is presented with one full-buffer blit, so a GL partial frame " +
            "reports the swatch area while the blit is outside the frame number (the BENCH line is paint only). The readout's " +
            "own label repaints about four times a second and is part of the frame it reports.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            WrapText = true,
            Height = 84f,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 12f, 0f, 0f),
        };

        card.Controls.Add(_metricsReadout);
        card.Controls.Add(buttons);
        card.Controls.Add(swatchRow);
        card.Controls.Add(caveat);

        // 15 ms is below the Windows 15.625 ms system-timer period, so the backend really uses its
        // high-resolution timer: the loop runs at about 66 Hz and 630 frames take about ten
        // seconds. A 16.67 ms interval would be rounded up to 31.25 ms on this backend.
        _metricsTimer = new XPlatTimer(TimeSpan.FromMilliseconds(15.0));
        _metricsTimer.AddTickHandler(_ => OnMetricsTick());

        // The marquee rule: the timer exists only while this page is on screen. Leaving the page
        // stops it, and returning starts it and paints once immediately.
        card.AddDisplayedChangedHandler(_ =>
        {
            if (card.IsDisplayed)
            {
                _metricsTimer.Start();
                OnMetricsTick();
            }
            else
            {
                _metricsTimer.Stop();
            }
        });

        OnHostAvailable(card, host =>
        {
            _metricsHost = host;
            host.AddFramePaintedHandler(_ => OnMetricsFramePainted(host));
        });

        return card;
    }

    /// <summary>
    /// Opens one of the popup surfaces anchored inside the zoomed panel, at <paramref name="multiplier"/>,
    /// so a headless render can show that it is scaled with its owner.
    /// </summary>
    /// <param name="window">The window the page is shown in; the context menu and pointer use its host.</param>
    /// <param name="kind">"dropdown", "menu" or "tooltip".</param>
    /// <param name="multiplier">The panel's multiplier when the surface is opened.</param>
    public void ShowZoomPopup(XPlatWindow window, string kind, float multiplier = 2f)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(kind);

        SetPanelZoom(multiplier);

        switch (kind)
        {
            case "dropdown":
                _zoomPanel?.ScrollTo(0f, 0f);
                _zoomCombo?.OpenDropDown();
                break;

            case "menu":
                if (_zoomMenuTarget is not null)
                {
                    _zoomPanel?.ScrollControlIntoView(_zoomMenuTarget);
                    window.Host.ProcessContextMenu(_zoomMenuTarget.ClientToHost(new SKPoint(24f, 15f)));
                }

                break;

            case "tooltip":
                if (_zoomTipTarget is not null && _zoomToolTip is not null)
                {
                    _zoomPanel?.ScrollControlIntoView(_zoomButtonRow ?? _zoomTipTarget);
                    var tipPoint = _zoomTipTarget.ClientToHost(new SKPoint(20f, 12f));
                    window.Host.ProcessMouseMove(tipPoint);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Expected dropdown, menu or tooltip.");
        }
    }

    /// <summary>
    /// Page 58: the window's own chrome. The buttons switch this window between the platform's
    /// decoration and a borderless one and ask for the platform's caption buttons, the strip starts a
    /// drag from the application's own press, and the readout reports what the backend answered
    /// rather than what was asked for.
    /// </summary>
    private XPlatPanel BuildChromelessPage()
    {
        var card = Card();
        card.Controls.Add(Heading("Chromeless window"));

        var buttons = Row(34f);
        buttons.Controls.Add(ChromeButton("Borderless", MakeBorderless));
        buttons.Controls.Add(ChromeButton("System chrome", MakeDecorated));
        buttons.Controls.Add(ChromeButton("Platform buttons", AskForPlatformButtons));
        buttons.Controls.Add(ChromeButton("Our own buttons", AskForNoButtons));
        card.Controls.Add(buttons);

        // The application's own title bar: a press here asks the platform to move the window, which is
        // the one thing a borderless window cannot do for itself once the platform's caption is gone.
        var handle = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 36f,
            BorderWidth = 1f,
            CornerRadius = 6f,
            Margin = new XPlatThickness(0f, 0f, 0f, 10f),
        };
        handle.Controls.Add(new XPlatLabel("Press and drag here to move the window")
        {
            Dock = XDock.Fill,
            TextAlign = XPlatContentAlignment.MiddleCenter,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
        });
        AttachWindowDrag(handle);
        _chromeDragHandle = handle;
        card.Controls.Add(handle);

        var readout = new XPlatLabel("No window attached, so there is nothing to report.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 150f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        };
        _chromeReadout = readout;
        card.Controls.Add(readout);

        card.Controls.Add(new XPlatLabel(
            "A borderless window is still a window: the platform keeps its shadow, its rounded " +
            "corners, its resize band and its behaviours. Ask ChromeCapabilities first - the setters " +
            "throw when the answer is no - and read ChromeMetrics back for what was actually " +
            "delivered. On Windows the caption buttons are the backend's own emulation and are " +
            "invisible to assistive technology, so an application that needs them reachable should " +
            "draw its own and keep Alt+F4 and Alt+Space working.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 62f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
        });

        return card;
    }

    /// <summary>
    /// Page 59: the application's own badge - the count or dot on the taskbar button or dock icon.
    /// The buttons set a count, a dot, a label and an urgent nudge, and the readout reports what the
    /// backend said it can draw rather than what was asked for, because on Linux the two differ.
    /// </summary>
    private XPlatPanel BuildBadgePage()
    {
        var card = Card();
        card.Controls.Add(Heading("App badge"));

        var buttons = Row(34f);
        buttons.Controls.Add(BadgeButton("Count 3", badge => badge.Count = 3));
        buttons.Controls.Add(BadgeButton("Count 150", badge => badge.Count = 150));
        buttons.Controls.Add(BadgeButton("Dot", badge =>
        {
            badge.Count = null;
            badge.IsDot = true;
        }));
        buttons.Controls.Add(BadgeButton("Text: 99+", badge =>
        {
            badge.IsDot = false;
            badge.Text = "99+";
        }));
        buttons.Controls.Add(BadgeButton("Urgent", badge => badge.Urgent = true));

        // Delayed on purpose: Windows and macOS skip a flash or a bounce while the window is active,
        // so the nudge is only observable from outside, and three seconds is enough time to get there.
        buttons.Controls.Add(BadgeButton("Urgent in 3 s", StartUrgentCountdown));
        buttons.Controls.Add(BadgeButton("Clear", badge => badge.Clear()));
        card.Controls.Add(buttons);

        var readout = new XPlatLabel("No application attached, so there is no badge to show.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            // Room for the countdown line as well as the two that are always there.
            Height = 74f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            Margin = new XPlatThickness(0f, 0f, 0f, 10f),
        };
        _badgeReadout = readout;
        card.Controls.Add(readout);

        card.Controls.Add(new XPlatLabel(
            "Windows draws the badge itself and lays it over the taskbar button, macOS asks the Dock " +
            "for its own red bubble, and Linux is a convention rather than an API: the dock badges " +
            "only an application whose desktop entry is installed and matches the application id, so " +
            "running the sample out of its build directory can leave the dock unchanged. Ask " +
            "Capabilities first - asking for something a platform cannot draw becomes an urgent " +
            "nudge rather than nothing - and remember that a count of zero is no badge at all.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 74f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
        });

        return card;
    }

    /// <summary>A button that applies one change to the badge and reports what came of it.</summary>
    private XPlatButton BadgeButton(string text, Action<XPlatAppBadge> change)
    {
        var button = new XPlatButton(text)
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        button.AddClickHandler(_ => ApplyBadge(change));
        return button;
    }

    /// <summary>A button that does something other than one change to the badge.</summary>
    private XPlatButton BadgeButton(string text, Action action)
    {
        var button = new XPlatButton(text)
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        button.AddClickHandler(_ => action());
        return button;
    }

    /// <summary>
    /// Starts the page's three-second countdown, after which the urgent nudge is applied.
    /// </summary>
    /// <remarks>
    /// The urgent flag is cleared first, because it is a transition on Windows and macOS rather than a
    /// state: setting it again while it is already set neither flashes nor bounces, so a second click
    /// would otherwise look like nothing happened. Clearing it is also what makes the countdown
    /// repeatable, which is the whole point of a button used to try the thing by hand.
    /// </remarks>
    private void StartUrgentCountdown()
    {
        if (OwnerWindow?.Application is not { } application)
        {
            return;
        }

        application.Badge.Urgent = false;

        _badgeUrgentTimer ??= CreateBadgeUrgentTimer();
        _badgeUrgentSeconds = BadgeUrgentCountdownSeconds;
        _badgeUrgentTimer.Stop();
        _badgeUrgentTimer.Start();
        RefreshBadgeReadout();
    }

    /// <summary>Cancels a countdown that has not fired yet, so an explicit change wins over a pending nudge.</summary>
    private void CancelUrgentCountdown()
    {
        if (_badgeUrgentSeconds == 0)
        {
            return;
        }

        _badgeUrgentSeconds = 0;
        _badgeUrgentTimer?.Stop();
    }

    /// <summary>Builds the timer the countdown runs on: one second a tick, stopping at zero.</summary>
    private XPlatTimer CreateBadgeUrgentTimer()
    {
        var timer = new XPlatTimer(TimeSpan.FromSeconds(1.0));
        timer.AddTickHandler(_ =>
        {
            if (--_badgeUrgentSeconds > 0)
            {
                RefreshBadgeReadout();
                return;
            }

            // Stop before applying: a tick that arrives after the last one has nothing left to do and
            // would push the count below zero.
            timer.Stop();
            ApplyBadge(badge => badge.Urgent = true);
        });
        return timer;
    }

    /// <summary>Applies <paramref name="change"/> to the attached application's badge.</summary>
    private void ApplyBadge(Action<XPlatAppBadge> change)
    {
        if (OwnerWindow?.Application is not { } application)
        {
            return;
        }

        // An explicit change is an answer to the page, so it takes the pending prompt with it.
        CancelUrgentCountdown();

        change(application.Badge);
        RefreshBadgeReadout();
    }

    /// <summary>Reports what is showing and what this backend can draw.</summary>
    private void RefreshBadgeReadout()
    {
        if (_badgeReadout is null)
        {
            return;
        }

        if (OwnerWindow?.Application is not { } application)
        {
            _badgeReadout.Text = "No application attached, so there is no badge to show.";
            return;
        }

        var badge = application.Badge;
        var capabilities = badge.Capabilities;

        var shown = badge.IsDot
            ? "a dot"
            : badge.Text is { Length: > 0 } label
                ? $"the label \u201c{label}\u201d"
                : badge.Count is > 0
                    ? $"a count of {badge.Count.Value.ToString(CultureInfo.CurrentCulture)}"
                    : "nothing";

        var pending = _badgeUrgentSeconds > 0
            ? $"The urgent nudge arrives in {_badgeUrgentSeconds.ToString(CultureInfo.CurrentCulture)} s - " +
              "switch to another window to watch for it.\n"
            : string.Empty;

        _badgeReadout.Text =
            $"Showing {shown}. Urgent: {(badge.Urgent ? "yes" : "no")}.\n" +
            pending +
            "This backend can draw: " +
            $"count {(capabilities.Count ? "yes" : "no")}, " +
            $"text {(capabilities.Text ? "yes" : "no")}, " +
            $"dot {(capabilities.Dot ? "yes" : "no")}, " +
            $"urgent {(capabilities.Urgent ? "yes" : "no")}.";
    }

    /// <summary>A button that sits beside the one before it rather than under it.</summary>
    private static XPlatButton ChromeButton(string text, Action onClick)
    {
        var button = new XPlatButton(text)
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        button.AddClickHandler(_ => onClick());
        return button;
    }

    /// <summary>Switches the sample's window to the application-owned chrome, if the backend has it.</summary>
    private void MakeBorderless() => ChangeChrome(window =>
    {
        if (!window.ChromeCapabilities.Borderless)
        {
            return "This backend cannot make a window borderless.";
        }

        window.Chrome = XPlatWindowChrome.Borderless;
        return "Borderless: the application owns every visible pixel.";
    });

    /// <summary>Hands the window back to the platform's own decoration.</summary>
    private void MakeDecorated() => ChangeChrome(window =>
    {
        window.Chrome = XPlatWindowChrome.System;
        return "System chrome: the platform draws and drives the window again.";
    });

    /// <summary>Asks for the platform's own caption buttons where the platform has any.</summary>
    private void AskForPlatformButtons() => ChangeChrome(window =>
    {
        if (!window.ChromeCapabilities.CaptionButtons)
        {
            return "This backend cannot draw caption buttons of its own.";
        }

        window.CaptionButtons = XPlatCaptionButtons.Platform;
        return window.ChromeMetrics.CaptionButtons == XPlatCaptionButtons.Platform
            ? "Platform buttons: the platform draws them and claims their clicks."
            : "Platform buttons were requested, but none are on screen.";
    });

    /// <summary>Goes back to a window with no buttons of the platform's, so the application can draw its own.</summary>
    private void AskForNoButtons() => ChangeChrome(window =>
    {
        window.CaptionButtons = XPlatCaptionButtons.None;
        return "No platform buttons: the application draws and handles its own.";
    });

    /// <summary>
    /// Runs a chrome change against the attached window and reports what came of it, including the
    /// refusal a backend without the feature answers with.
    /// </summary>
    private void ChangeChrome(Func<XPlatWindow, string> change)
    {
        if (_chromeWindow is not { } window)
        {
            SetStatus("No window attached to change.");
            return;
        }

        try
        {
            SetStatus(change(window));
        }
        catch (NotSupportedException ex)
        {
            // The seam throws where an application would have to guess, and the capabilities are how
            // it is meant to find out first; showing the message is the honest version of that.
            SetStatus(ex.Message);
        }

        RefreshChromeReadout();
    }

    /// <summary>
    /// Asks the platform to move the window from the press being handled, which is only effective
    /// while the press is still in flight - which is why this runs from the mouse-down itself.
    /// </summary>
    private void DragWindowFromThisPress()
    {
        if (_chromeWindow is not { } window)
        {
            return;
        }

        bool started = window.TryBeginDrag();
        SetStatus(started
            ? "The platform took the press and is moving the window."
            : "No window drag started: the backend refused, or there is no press to drag with.");
    }

    /// <summary>Re-reads the window's chrome state into the page's readout.</summary>
    private void RefreshChromeReadout()
    {
        if (_chromeReadout is not { } readout)
        {
            return;
        }

        if (_chromeWindow is not { } window)
        {
            readout.Text = "No window attached, so there is nothing to report.";
            return;
        }

        var caps = window.ChromeCapabilities;
        var metrics = window.ChromeMetrics;

        var buttons = metrics.CaptionButtonBounds.Count == 0
            ? "none"
            : string.Join(", ", metrics.CaptionButtonBounds.Select(b => $"{b.Kind} {Describe(b.Bounds)}"));

        readout.Text =
            $"Window: {window.Title}, client {window.ClientSize.Width:0}x{window.ClientSize.Height:0}, " +
            $"state {window.State}\n" +
            $"Chrome: {window.Chrome}; buttons requested: {window.CaptionButtons}\n" +
            $"Supported: borderless {Yes(caps.Borderless)}, caption buttons {Yes(caps.CaptionButtons)}, " +
            $"window colours {Yes(caps.WindowColors)}\n" +
            $"Surface inset {Describe(metrics.SurfaceInset)}, resize inset {Describe(metrics.ResizeInset)}\n" +
            $"Corner radius {metrics.CornerRadius:0.#}, corner resize {metrics.CornerResizeExtent:0.#}\n" +
            $"Buttons: {metrics.CaptionButtons} on the {metrics.CaptionButtonSide} - {buttons}\n" +
            $"Strip: {Describe(metrics.CaptionButtonStrip)}";
    }

    private static string Yes(bool value) => value ? "yes" : "no";

    private static string Describe(XPlatThickness thickness) =>
        $"{thickness.Left:0.#},{thickness.Top:0.#},{thickness.Right:0.#},{thickness.Bottom:0.#}";

    private static string Describe(SKRect rect) =>
        rect.Width <= 0f || rect.Height <= 0f
            ? "none"
            : $"{rect.Left:0.#},{rect.Top:0.#} {rect.Width:0.#}x{rect.Height:0.#}";

    /// <summary>
    /// Drives the Zoom page's slider, so a headless render can show a fixed multiplier without a
    /// click. The value is clamped by the track bar to its own range.
    /// </summary>
    public void SetPanelZoom(float multiplier) => _zoomSlider?.Value = multiplier;

    /// <summary>
    /// Page 55: a panel whose <see cref="XPlatPanel.ScaleFactor"/> zooms its children while its own
    /// chrome stays put, with a nested panel at the reciprocal multiplier proving that they compose.
    /// </summary>
    private XPlatPanel BuildZoomPage()
    {
        var card = Card();
        card.Controls.Add(Heading("Panel zoom"));

        var readout = ScrollCaption("Panel 1.00x - nested 1.00x - effective 1.00x");
        readout.Height = 18f;
        card.Controls.Add(readout);

        var zoomed = new XPlatPanel
        {
            Dock = XDock.Fill,
            ShowFrame = true,
            IsCard = true,
            Padding = new XPlatThickness(0f),
            HorizontalScroll = XPlatScrollStyle.Automatic,
            VerticalScroll = XPlatScrollStyle.Automatic,
            Margin = new XPlatThickness(0f, 8f, 0f, 0f),
        };
        _zoomPanel = zoomed;

        // The nested panel carries the reciprocal multiplier, so its children see the same logical
        // space at every position of the slider: the two multipliers cancel.
        var nested = new XPlatPanel
        {
            Bounds = new SKRect(8f, 8f, 348f, 96f),
        };

        // The nested panel's box is in its parent's content units, so it draws bigger as the
        // slider goes up; its one child is a fixed-height row, so the button inside it is exactly
        // the same size at every multiplier. That is what "nested 0.50x" cancelling "panel 2.00x"
        // means, and it is why the row is here instead of the button being docked straight in: a
        // docked button would stretch along the axis the panel hands it more space on.
        var nestedRow = Row(34f);
        nestedRow.Margin = new XPlatThickness(0f);
        var nestedButton = new XPlatButton("Nested button")
        {
            Dock = XDock.Left,
            AutoSize = true,
        };
        nestedRow.Controls.Add(nestedButton);
        nested.Controls.Add(nestedRow);

        var combo = new XPlatComboBox
        {
            Bounds = new SKRect(8f, 104f, 208f, 134f),
        };
        combo.Items.Add("First row");
        combo.Items.Add("Second row");
        combo.Items.Add("A longer third row");
        combo.Items.Add("Fourth row");
        combo.SelectedIndex = 0;
        _zoomCombo = combo;

        var caption = new XPlatLabel(
            "A 14 px label, a 20 px button and a 30 px text box all follow the subtree multiplier. " +
            "The panel's border, padding, corner radius and scrollbar do not zoom. The boxed panel " +
            "above carries the reciprocal multiplier, so its button never changes size.")
        {
            Bounds = new SKRect(8f, 142f, 348f, 208f),
            AutoSize = false,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
        };

        var buttonRow = Row(34f);
        buttonRow.Dock = XDock.None;
        buttonRow.Margin = new XPlatThickness(0f);
        buttonRow.Bounds = new SKRect(8f, 214f, 348f, 248f);
        _zoomButtonRow = buttonRow;
        var buttonA = new XPlatButton("A button")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        buttonRow.Controls.Add(buttonA);
        buttonRow.Controls.Add(new XPlatButton("Another")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
        });

        var edit = new XPlatTextBox
        {
            Bounds = new SKRect(8f, 256f, 308f, 286f),
            PlaceholderText = "Type at any zoom",
        };

        var image = new XPlatImage
        {
            Bounds = new SKRect(320f, 256f, 480f, 356f),
            Dock = XDock.None,
            AutoSize = false,
            SizeMode = XPlatSizeMode.StretchImage,
            // The source has the same 8:5 shape as the box and a caption drawn well inside its
            // edges, so the picture is whole and legible at every multiplier.
            Image = Images.LoadFromStream(CreateGradientPng(320, 200)),
        };

        // Controls that leave the panel's canvas for a top-level window of their own. The combo
        // carries a context menu and the first button a tooltip, so both surfaces can be opened
        // from inside the zoomed panel and shown to inherit its multiplier.
        combo.ContextMenu = new XPlatContextMenu().Add("Zoomed row").Add("Another row");
        _zoomMenuTarget = combo;

        zoomed.Controls.Add(nested);
        zoomed.Controls.Add(combo);
        zoomed.Controls.Add(caption);
        zoomed.Controls.Add(buttonRow);
        zoomed.Controls.Add(edit);
        zoomed.Controls.Add(image);

        var toolTip = new XPlatToolTip { InitialDelay = 0, AutoPopDelay = 0 };
        toolTip.SetToolTip(buttonA, "This tip is painted at the panel's multiplier.");
        _zoomToolTip = toolTip;
        _zoomTipTarget = buttonA;

        var sliderRow = Row(42f);
        var slider = new XPlatTrackBar
        {
            Dock = XDock.Fill,
            Minimum = 0.5f,
            Maximum = 3f,
            Value = 1f,
            SmallChange = 0.05f,
            LargeChange = 0.25f,
            TickFrequency = 0.5f,
        };
        sliderRow.Controls.Add(slider);
        _zoomSlider = slider;

        card.Controls.Add(sliderRow);
        card.Controls.Add(zoomed);

        slider.AddValueChangedHandler(_ =>
        {
            float multiplier = slider.Value;
            float reciprocal = 1f / multiplier;

            zoomed.ScaleFactor = multiplier;
            nested.ScaleFactor = reciprocal;

            readout.Text =
                $"Panel {multiplier:0.00}x - nested {reciprocal:0.00}x - effective {multiplier * reciprocal:0.00}x";

            SetStatus(
                "The panel's children zoom with the slider; its chrome and scrollbar do not. " +
                "The nested panel cancels the multiplier, so its own content is unchanged.");
        });

        return card;
    }

    /// <summary>
    /// Page 56: the Win32 drag/drop reference-count defect, measured rather than described.
    /// </summary>
    /// <remarks>
    /// The shell owns the dragged data object and lends the drop target an <c>IDataObject*</c> for
    /// the duration of a drag. A wrapper created over that borrowed pointer believes it owns the
    /// reference and releases it later, so the live count falls - during a drag if a collection
    /// pulls the finalizer forward, or on the second conversion of the same pointer, which is what
    /// the <c>Drop</c> sample shows. This page samples the count at every OLE entry point and states
    /// the criterion: it must never fall.
    /// </remarks>
    private XPlatPanel BuildDragLifetimePage()
    {
        var card = Card();
        card.Controls.Add(Heading("Drag/drop lifetime"));

        // Says, on every platform, whether the numbers below mean anything here.
#if WINDOWS
        const string platformNote =
            "Win32 only. macOS and GTK4 hand a drag to the feature by a different mechanism, so this " +
            "page cannot measure them and shows no samples there.";
#else
        const string platformNote =
            "Informational on this platform. The defect is Win32 specific - the drag/drop feature uses " +
            "a different mechanism here - so no reference count is sampled and no verdict is reached.";
#endif

        card.Controls.Add(new XPlatLabel(platformNote)
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 32f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
        });

        card.Controls.Add(new XPlatLabel(
            "Drag files from Explorer into the box below, then drag a text selection (select some text " +
            "in a browser or Notepad and drag it): the two sources hand over data objects with " +
            "different reference counts. Drop something different the second time - the second drop is " +
            "what shows the over-release's other symptom, a payload that is stale or gone.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 48f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        });

        _dragLifetimeCollect = new XPlatCheckBox("Collect during drag")
        {
            Dock = XDock.Top,
            Checked = true,
            AutoSize = true,
            Margin = new XPlatThickness(0f, 0f, 0f, 4f),
        };
        card.Controls.Add(_dragLifetimeCollect);

        card.Controls.Add(new XPlatLabel(
            "With this on, every drag-over forces a blocking gen-2 collection, which is what pulls the " +
            "wrapper's finalizer into the middle of the drag instead of leaving it to chance. Turn it " +
            "off to measure the other failure: the second conversion of the same pointer, at Drop. A " +
            "drop that never arrives at all is the same defect further along - the object was already gone. " +
            "With collection off the count can still look flat, because the shell takes its own reference " +
            "for the drop at the moment the wrapper cache gives the borrowed one back: the two cancel, and " +
            "the dip hides in the arithmetic instead of disappearing.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 64f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        });

        var zone = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 76f,
            ShowFrame = true,
            IsCard = true,
            CornerRadius = 8f,
            Padding = new XPlatThickness(8f),
            Margin = new XPlatThickness(0f, 0f, 0f, 10f),
        };
        zone.Controls.Add(new XPlatLabel("Drop files or text here")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            TextAlign = XPlatContentAlignment.MiddleCenter,
            ForeColorRole = XPlatThemeColor.MutedForeground,
        });

        // The zone is the measured area; the window itself already takes drops, so a drag that
        // lands on the navigation list is sampled too, but only this control forces a collection.
        zone.AllowDrop = true;
        zone.AddDragEnterHandler((_, e) => OnDragLifetimeEnter(e));
        zone.AddDragOverHandler((_, e) => OnDragLifetimeOver(e));
        zone.AddDragDropHandler((_, e) => OnDragLifetimeDrop(e));
        card.Controls.Add(zone);

        _dragLifetimeCurrent = Readout("Current sample: none yet");
        _dragLifetimeSequence = Readout("This drag: -");
        _dragLifetimeCounts = Readout("Minimum this drag: -   Drops this session: 0");
        _dragLifetimeVerdict = new XPlatLabel(
            "Waiting: the reference count must never fall during a drag.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 34f,
            WrapText = true,
            Font = XPlatTheme.EmphasizedFont,
            Margin = new XPlatThickness(0f, 4f, 0f, 4f),
        };
        _dragLifetimePayload = new XPlatLabel("Drop 1: -   Drop 2: -")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 34f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            Margin = new XPlatThickness(0f, 0f, 0f, 4f),
        };
        card.Controls.Add(_dragLifetimeCurrent);
        card.Controls.Add(_dragLifetimeSequence);
        card.Controls.Add(_dragLifetimeCounts);
        card.Controls.Add(_dragLifetimeVerdict);
        card.Controls.Add(_dragLifetimePayload);

        _dragLifetimeLog = new XPlatTextBox
        {
            Dock = XDock.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            Font = XPlatTheme.CaptionFont,
            Text = "Drag something over the window: the last 12 samples appear here.",
        };
        card.Controls.Add(_dragLifetimeLog);

#if WINDOWS
        // One subscription for the life of the window: the trace belongs to the feature, not to the
        // page, and the samples keep arriving while another page is shown.
        Win32DropReferenceTrace.Sampled += OnDragReferenceSampled;
#endif

        return card;
    }

    /// <summary>Builds one line of the lifetime page's readouts.</summary>
    private static XPlatLabel Readout(string text) => new(text)
    {
        Dock = XDock.Top,
        AutoSize = false,
        Height = 18f,
        Font = XPlatTheme.CaptionFont,
        TextAlign = XPlatContentAlignment.BottomLeft,
    };

    /// <summary>Records a sample the feature took, and rebuilds what the page shows.</summary>
#if WINDOWS
    private void OnDragReferenceSampled(Win32DropReferenceSample sample)
    {
        // A new drag starts with the count the shell handed over, so the baseline is per drag: two
        // different data objects need not have the same count, and comparing across drags would
        // report a fall that never happened.
        if (sample.Event == Win32DropReferenceTrace.EnterEvent || _dragLifetimeSamples.Count == 0)
        {
            _dragLifetimeSamples.Clear();
            _dragLifetimeMinimum = sample.Count;
            _dragLifetimeFell = false;
            _dragLifetimeFallFrom = 0;
            _dragLifetimeFallTo = 0;
            _dragLifetimeFallEvent = null;
            _dragLifetimePointer = sample.DataObject;
        }
        else if (sample.Count < _dragLifetimeSamples[^1] && !_dragLifetimeFell)
        {
            // The criterion is "never falls", so the comparison is against the sample before it
            // rather than against the first one: the count may legitimately be acquired by the
            // shell mid-drag, but it must never be given up.
            _dragLifetimeFell = true;
            _dragLifetimeFallFrom = _dragLifetimeSamples[^1];
            _dragLifetimeFallTo = sample.Count;
            _dragLifetimeFallEvent = sample.Event;
        }

        _dragLifetimeSamples.Add(sample.Count);
        _dragLifetimeMinimum = Math.Min(_dragLifetimeMinimum, sample.Count);
        _dragLifetimeLastEvent = sample.Event;

        // The Drop sample is a second conversion of the pointer DragEnter already converted, and it
        // is where the wrapper cache releases the incoming reference synchronously. Marking it keeps
        // that decrement attributable without a screenshot of the debugger.
        var secondConversion =
            sample.Event == Win32DropReferenceTrace.DropEvent &&
            sample.DataObject == _dragLifetimePointer;

        var line =
            $"{Timestamp()}  {sample.Event,-9} ref {sample.Count,-2}  0x{sample.DataObject.ToInt64():X}" +
            (secondConversion ? "  second conversion of this pointer" : string.Empty);

        _dragLifetimeEvents.Add((Timestamp(), line));
        TrimDragLifetimeLog();
        RefreshDragLifetimePage();
    }
#endif

    /// <summary>Appends the payload the drop zone just read to the sample line it belongs to.</summary>
    private void AppendDragLifetimePayload(string payload)
    {
        if (_dragLifetimeEvents.Count == 0)
        {
            return;
        }

#if WINDOWS
        if (_dragLifetimeLastEvent is not ("DragEnter" or "Drop"))
        {
            return;
        }
#endif

        var last = _dragLifetimeEvents[^1];
        _dragLifetimeEvents[^1] = (last.Time, $"{last.Text}  {payload}");
        TrimDragLifetimeLog();
        RefreshDragLifetimePage();
    }

    private void TrimDragLifetimeLog()
    {
        while (_dragLifetimeEvents.Count > 12)
        {
            _dragLifetimeEvents.RemoveAt(0);
        }
    }

    /// <summary>Runs on every drag-over the zone receives, so the collection lands mid-drag.</summary>
    private void OnDragLifetimeOver(XPlatDragEventArgs e)
    {
        e.Effect = XPlatDragDropEffects.Copy;

        if (_dragLifetimeCollect?.Checked == true)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
        }

        RefreshDragLifetimePage();
    }

    private void OnDragLifetimeEnter(XPlatDragEventArgs e)
    {
        e.Effect = XPlatDragDropEffects.Copy;
        AppendDragLifetimePayload(DescribeDragPayload(e.Data));
    }

    private void OnDragLifetimeDrop(XPlatDragEventArgs e)
    {
        e.Effect = XPlatDragDropEffects.Copy;

        var payload = DescribeDragPayload(e.Data);
        _dragLifetimeDrops++;

        if (_dragLifetimeDrops == 1)
        {
            _dragLifetimeFirstPayload = payload;
        }
        else
        {
            _dragLifetimeSecondPayload = payload;
        }

        SetStatus($"Dropped {payload} on the lifetime page.");
        AppendDragLifetimePayload(payload);
    }

    /// <summary>Says what a dropped payload holds, in one line, for the log and the readouts.</summary>
    private static string DescribeDragPayload(XPlatDataObject data)
    {
        var files = data.GetFiles();
        if (files.Length > 0)
        {
            return $"files: {string.Join(", ", files.Select(Path.GetFileName))}";
        }

        var text = data.GetText();
        return string.IsNullOrEmpty(text) ? "nothing this sample can read" : $"text: \"{Preview(text)}\"";
    }

    /// <summary>
    /// Rebuilds the readout, the verdict and the log. The verdict states the criterion in words
    /// because the numbers alone are easy to read as "fine".
    /// </summary>
    private void RefreshDragLifetimePage()
    {
        if (_dragLifetimeCurrent is not { } current ||
            _dragLifetimeSequence is not { } sequenceLabel ||
            _dragLifetimeCounts is not { } countsLabel ||
            _dragLifetimeVerdict is not { } verdict ||
            _dragLifetimePayload is not { } payloadLabel ||
            _dragLifetimeLog is not { } log)
        {
            return;
        }

        var sequence = string.Join(' ', _dragLifetimeSamples);
        current.Text = _dragLifetimeSamples.Count == 0
            ? "Current sample: none yet"
            : CurrentSampleText();
        sequenceLabel.Text = _dragLifetimeSamples.Count == 0
            ? "This drag: -"
            : $"This drag: {sequence}";
        countsLabel.Text = DragLifetimeCountsText();

        if (_dragLifetimeSamples.Count == 0)
        {
            verdict.Text = "Waiting: the reference count must never fall during a drag.";
            verdict.ClearForeground();
            verdict.ForeColorRole = XPlatThemeColor.MutedForeground;
        }
        else
        {
            ApplyDragLifetimeVerdict(verdict, sequence);
        }

        payloadLabel.Text =
            $"Drop 1: {_dragLifetimeFirstPayload ?? "-"}   " +
            $"Drop 2: {_dragLifetimeSecondPayload ?? "-"}";

        if (_dragLifetimeSecondPayload is not null)
        {
            var secondDrop = _dragLifetimeSecondPayload == "nothing this sample can read"
                ? "FAIL - the second drop carried nothing; the object was gone by then."
                : _dragLifetimeSecondPayload == _dragLifetimeFirstPayload
                    ? "Second drop: same payload as the first - fine if you dragged the same thing, FAIL if you did not."
                    : "Second drop: PASS - it carried its own payload.";

            payloadLabel.Text += $"   {secondDrop}";
        }

        log.Text = _dragLifetimeEvents.Count == 0
            ? "Drag something over the window: the last 12 samples appear here."
            : string.Join(Environment.NewLine, _dragLifetimeEvents.Select(entry => entry.Text));
    }

    /// <summary>Builds the counts line, whose minimum is the one number Win32 has to measure.</summary>
    private string DragLifetimeCountsText()
    {
#if WINDOWS
        return
            $"Minimum this drag: {(_dragLifetimeSamples.Count == 0 ? "-" : _dragLifetimeMinimum.ToString(CultureInfo.InvariantCulture))}" +
            $"   Drops this session: {_dragLifetimeDrops}";
#else
        return $"Minimum this drag: -   Drops this session: {_dragLifetimeDrops}";
#endif
    }

    /// <summary>States the criterion against the samples the drag produced.</summary>
    private void ApplyDragLifetimeVerdict(XPlatLabel verdict, string sequence)
    {
#if WINDOWS
        if (_dragLifetimeFell)
        {
            verdict.Text =
                $"FAIL - the reference count fell from {_dragLifetimeFallFrom} to {_dragLifetimeFallTo} " +
                $"at {_dragLifetimeFallEvent ?? "a drag event"}: {sequence}. The count must never fall " +
                $"while the shell is still using the object, and it did.";
            verdict.Foreground = new SKColor(0xFFD32F2F);
        }
        else
        {
            verdict.Text =
                $"PASS - the count held at {_dragLifetimeSamples[0]} for the whole drag: {sequence}.";
            verdict.Foreground = new SKColor(0xFF2E7D32);
        }
#else
        // No sample can arrive where the sampler does not exist, so the page never reaches a verdict
        // here; this repeats the resting line rather than inventing a second one to read.
        verdict.Text = "Waiting: the reference count must never fall during a drag.";
        verdict.ClearForeground();
        verdict.ForeColorRole = XPlatThemeColor.MutedForeground;
#endif
    }

    private static string Timestamp() =>
        DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>The last sample in words, which is the only line a reader has to trust.</summary>
    private string CurrentSampleText()
    {
#if WINDOWS
        return $"Current sample: {_dragLifetimeSamples[^1]} at {Win32DropReferenceTrace.Last?.Event}";
#else
        return "Current sample: not sampled on this platform";
#endif
    }

    private void OnMetricsTick()
    {
        if (_metricsHost is null || _metricsPage is not { IsDisplayed: true })
        {
            return;
        }

        if (_metricsFullDamage)
        {
            _metricsHost.Invalidate();
        }
        else
        {
            _metricsSwatch?.Invalidate();
        }
    }

    private void StartMeasurement()
    {
        if (_measuring || _metricsPage is not { IsDisplayed: true })
        {
            return;
        }

        _measuring = true;
        _measureFrame = 0;
        _measureFirst = 0;
        _measureLast = 0;
        _measureLastArea = SKRectI.Empty;
        _measureSamples.Clear();

        if (_measureButton is not null)
        {
            _measureButton.Enabled = false;
            _measureButton.Text = "Measuring...";
        }

        if (_metricsReadout is not null)
        {
            _metricsReadout.Text = $"measuring {BenchmarkMeasuredFrames} frames after {BenchmarkWarmupFrames} warm-up...";
        }

        SetStatus("Benchmark running - the page is invalidated on every tick; nothing else changes.");

        StartBenchmarkWatchdog();
    }

    /// <summary>
    /// Starts the stall watchdog: while a measurement is in progress it checks every five seconds
    /// whether the completed-frame count has advanced, and reports to stderr when it has not for ten.
    /// A normal run never prints anything from here, and a run that finishes stops it.
    /// </summary>
    private void StartBenchmarkWatchdog()
    {
        long now = Environment.TickCount64;
        _watchdogStartedAt = now;
        _watchdogAdvancedAt = now;
        _watchdogReportedAt = long.MinValue / 2;
        _watchdogFrames = _measureFrame;

        if (_benchmarkWatchdog is null)
        {
            _benchmarkWatchdog = new XPlatTimer(TimeSpan.FromSeconds(BenchmarkStallCheckSeconds));
            _benchmarkWatchdog.AddTickHandler(_ => OnBenchmarkWatchdogTick());
        }

        _benchmarkWatchdog.Start();
    }

    private void StopBenchmarkWatchdog() => _benchmarkWatchdog?.Stop();

    private void OnBenchmarkWatchdogTick()
    {
        if (!_measuring)
        {
            StopBenchmarkWatchdog();
            return;
        }

        long now = Environment.TickCount64;
        if (_measureFrame != _watchdogFrames)
        {
            _watchdogFrames = _measureFrame;
            _watchdogAdvancedAt = now;
            return;
        }

        long stalled = now - _watchdogAdvancedAt;

        // Repeat at most every 30 s, so a window that stays covered for minutes does not spew.
        if (stalled < BenchmarkStallSeconds * 1000L ||
            now - _watchdogReportedAt < BenchmarkStallReportSeconds * 1000L)
        {
            return;
        }

        _watchdogReportedAt = now;
        int total = BenchmarkWarmupFrames + BenchmarkMeasuredFrames;
        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"BENCH stalled: {_measureFrame}/{total} frames after {(now - _watchdogStartedAt) / 1000}s " +
            $"(no frames for {stalled / 1000}s)"));
    }

    private void ToggleDamage()
    {
        _metricsFullDamage = !_metricsFullDamage;

        if (_damageButton is not null)
        {
            _damageButton.Text = _metricsFullDamage ? "Damage: full" : "Damage: partial";
        }

        SetStatus(_metricsFullDamage
            ? "Full damage: every tick invalidates the whole window."
            : "Partial damage: every tick invalidates only the 180 x 180 swatch.");
    }

    private void OnMetricsFramePainted(XPlatUiHost host)
    {
        // A page benchmark can add its own handler while the measured-rendering page's handler is
        // still registered; both fire for the same frame, so count each frame once.
        if (host.FrameCount == _lastRecordedFrame)
        {
            return;
        }

        _lastRecordedFrame = host.FrameCount;

        double milliseconds = host.LastFrameTime.TotalMilliseconds;
        long timestamp = Stopwatch.GetTimestamp();

        _rollingFrames.Enqueue((milliseconds, timestamp));
        while (_rollingFrames.Count > RollingFrameWindow)
        {
            _rollingFrames.Dequeue();
        }

        if (_measuring)
        {
            _measureFrame++;

            // The glyph atlas and shader programs are uploaded lazily on the first frames, so the
            // warm-up is discarded rather than averaged into a number it would poison.
            if (_measureFrame > BenchmarkWarmupFrames)
            {
                if (_measureSamples.Count == 0)
                {
                    _measureFirst = timestamp;
                }

                _measureSamples.Add(milliseconds);
                _measureLast = timestamp;
                _measureLastArea = host.LastFrameArea;

                if (_measureSamples.Count >= BenchmarkMeasuredFrames)
                {
                    FinishMeasurement(host);
                }
            }

            return;
        }

        long now = Environment.TickCount64;
        if (now - _rollingUpdatedAt < 250)
        {
            return;
        }

        _rollingUpdatedAt = now;
        UpdateMetricsReadout(host);
    }

    private void UpdateMetricsReadout(XPlatUiHost host)
    {
        if (_metricsReadout is null || _rollingFrames.Count == 0)
        {
            return;
        }

        var frames = _rollingFrames.ToArray();
        var values = frames.Select(frame => frame.Milliseconds).OrderBy(value => value).ToArray();
        double mean = values.Average();
        double p95 = Percentile(values, 0.95);
        double worst = values[^1];
        double last = frames[^1].Milliseconds;

        double seconds = frames.Length > 1
            ? Stopwatch.GetElapsedTime(frames[0].Timestamp, frames[^1].Timestamp).TotalSeconds
            : 0;
        double framesPerSecond = seconds > 0 ? (frames.Length - 1) / seconds : 0;

        double scale = _scaleProvider?.Invoke() ?? 1f;
        var client = host.ClientSize;
        var area = host.LastFrameArea;

        _metricsReadout.Text =
            $"last {last:0.0} | mean {mean:0.0} | p95 {p95:0.0} | worst {worst:0.0} ms | " +
            $"{framesPerSecond:0} fps | area {area.Width} x {area.Height} px | " +
            $"client {client.Width:0} x {client.Height:0} @ {scale:0.00}x | " +
            $"damage {(_metricsFullDamage ? "full" : "partial")}";
    }

    private void FinishMeasurement(XPlatUiHost host)
    {
        _measuring = false;
        _pageBenchmarkTimer?.Stop();
        StopBenchmarkWatchdog();

        var values = _measureSamples.OrderBy(value => value).ToArray();
        double mean = values.Average();
        double p50 = Percentile(values, 0.50);
        double p95 = Percentile(values, 0.95);
        double max = values[^1];

        double seconds = _measureLast > _measureFirst
            ? Stopwatch.GetElapsedTime(_measureFirst, _measureLast).TotalSeconds
            : 0;
        double framesPerSecond = seconds > 0 ? values.Length / seconds : 0;

        double scale = _scaleProvider?.Invoke() ?? 1f;
        var client = host.ClientSize;
        string mode = _graphicsPath ?? "cpu (unknown)";
        string renderer = _graphicsRenderer ?? "none";
        string damage = _metricsFullDamage ? "full" : "partial";

        // One machine-readable line per run. Numbers use the invariant culture so a comma decimal
        // separator on the desktop that produced them cannot change what a script reads.
        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"BENCH mode=\"{mode}\" renderer=\"{renderer}\" size={client.Width:0}x{client.Height:0} " +
            $"scale={scale:0.00} damage={damage} frames={values.Length} warmup={BenchmarkWarmupFrames} " +
            $"p50={p50:0.000} p95={p95:0.000} max={max:0.000} mean={mean:0.000} fps={framesPerSecond:0.0} " +
            $"area={_measureLastArea.Width}x{_measureLastArea.Height}");

        Console.Error.WriteLine(line);

        if (_metricsReadout is not null)
        {
            _metricsReadout.Text =
                $"benchmark {damage}: p50 {p50:0.0} | p95 {p95:0.0} | max {max:0.0} | mean {mean:0.0} ms | " +
                $"{framesPerSecond:0.0} fps | area {_measureLastArea.Width} x {_measureLastArea.Height} px | " +
                $"client {client.Width:0} x {client.Height:0} @ {scale:0.00}x";
        }

        if (_measureButton is not null)
        {
            _measureButton.Enabled = true;
            _measureButton.Text = $"Measure {BenchmarkMeasuredFrames} frames";
        }

        SetStatus(
            $"Benchmark {damage}: p50 {p50:0.0} ms, p95 {p95:0.0} ms, max {max:0.0} ms, " +
            $"{framesPerSecond:0.0} fps. The BENCH line is on stderr.");
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 0)
        {
            return 0;
        }

        int rank = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }

    /// <summary>
    /// A list of a chosen size, with what the last frame cost to draw it. The list only paints the
    /// rows that are on screen, so scrolling a hundred thousand items should cost about what
    /// scrolling ten does; the readout is there to show that rather than claim it.
    /// </summary>
    private XPlatPanel BuildPerformancePage()
    {
        var list = new XPlatListBox
        {
            Dock = XDock.Fill,
            SelectionMode = XPlatSelectionMode.One,
            VerticalScroll = XPlatScrollStyle.Automatic,
            HorizontalScroll = XPlatScrollStyle.None,
        };

        return PerformanceCard(list, "items", count =>
        {
            list.BeginUpdate();
            try
            {
                list.Items.Clear();

                if (count > 0)
                {
                    var items = new object[count];
                    for (int index = 0; index < count; index++)
                    {
                        items[index] = $"Item {index + 1:N0}";
                    }

                    list.Items.AddRange(items);
                }
            }
            finally
            {
                list.EndUpdate();
            }

            // A smaller list cannot stay scrolled to where a larger one was.
            list.ScrollTo(0f, 0f);
        });
    }

    /// <summary>
    /// The same measurement over real controls rather than rows: a stack panel holding one button
    /// per entry, stacked down it and scrolled by the panel itself. Nothing here is virtualised, so
    /// the panel arranges and walks every button it holds on every frame - which is the cost the
    /// list page avoids, and the reason to have both.
    /// </summary>
    private XPlatPanel BuildControlStackPage()
    {
        const float ButtonHeight = 32f;
        const float ButtonPitch = 36f;

        var panel = new XPlatStackPanel
        {
            Dock = XDock.Fill,
            VerticalScroll = XPlatScrollStyle.Automatic,
            HorizontalScroll = XPlatScrollStyle.None,
            Spacing = ButtonPitch - ButtonHeight,
            ShowFrame = true,
            IsCard = true,
        };

        return PerformanceCard(panel, "buttons", count =>
        {
            // Every child added lays the panel out again, so filling it one button at a time would
            // cost a pass over a growing child list per button. Suspending layout turns that into
            // the single pass ResumeLayout runs at the end.
            panel.SuspendLayout();
            try
            {
                var previous = panel.Controls.ToArray();
                panel.Controls.Clear();
                foreach (var child in previous)
                {
                    child.Dispose();
                }

                for (int index = 0; index < count; index++)
                {
                    // A fixed height with the stack's stretch keeps the button out of the measure
                    // pass altogether: the stack only has to place it.
                    panel.Controls.Add(new XPlatButton($"Button {index + 1:N0}")
                    {
                        AutoSize = false,
                        Height = ButtonHeight,
                    });
                }
            }
            finally
            {
                panel.ResumeLayout();
            }

            panel.ScrollTo(0f, 0f);
        });
    }

    /// <summary>
    /// The chrome both performance pages share: a row of buttons that load a chosen number of
    /// entries into <paramref name="content"/>, and a readout of what the last frame cost.
    /// </summary>
    /// <param name="content">The control the buttons fill, and the one the readout reports on.</param>
    /// <param name="noun">What one entry is called, for the count and the status line.</param>
    /// <param name="fill">Replaces everything in <paramref name="content"/> with the given count.</param>
    private XPlatPanel PerformanceCard(XPlatControl content, string noun, Action<int> fill)
    {
        var card = Card();

        var countLabel = new XPlatLabel($"no {noun}")
        {
            Dock = XDock.Right,
            AutoSize = false,
            Width = 110f,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.BottomRight,
            Margin = new XPlatThickness(0f),
        };

        var frameLabel = new XPlatLabel("Last frame -")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.BottomLeft,
            Margin = new XPlatThickness(0f),
        };

        // Docking is applied in declaration order, so the count takes the right edge before the
        // frame time fills what is left of the row.
        var readout = Row(20f);
        readout.Controls.Add(countLabel);
        readout.Controls.Add(frameLabel);

        var buttons = Row(32f);
        buttons.Controls.Add(LoadButton("Clear", () => Load(0)));

        foreach (int size in new[] { 1, 10, 100, 1000, 10000, 100000 })
        {
            int count = size;
            buttons.Controls.Add(LoadButton(size.ToString("N0"), () => Load(count)));
        }

        card.Controls.Add(buttons);
        card.Controls.Add(readout);
        card.Controls.Add(content);

        // The readout is the one thing on the page that is not the content, and it is painted into
        // the frame it reports on, so a naive update would measure itself: the text changes, the
        // label repaints, that repaint is the next frame, and the page never goes idle. Only
        // assigning a changed string and only reading the host every so often lets the loop die
        // once the label is all that is left repainting.
        const long ReadoutIntervalMs = 100;
        long lastReadout = long.MinValue / 2;
        string shownFrame = string.Empty;

        OnHostAvailable(content, host => host.AddFramePaintedHandler(_ =>
        {
            long now = Environment.TickCount64;
            if (now - lastReadout < ReadoutIntervalMs)
            {
                return;
            }

            lastReadout = now;

            var area = host.LastFrameArea;
            string text = $"Last frame {host.LastFrameTime.TotalMilliseconds:0.0} ms " +
                          $"over {area.Width} x {area.Height} px";

            if (text != shownFrame)
            {
                shownFrame = text;
                frameLabel.Text = text;
            }
        }));

        return card;

        XPlatButton LoadButton(string text, Action onClick)
        {
            var button = new XPlatButton(text)
            {
                Dock = XDock.Left,
                AutoSize = true,
                Style = XPlatButtonStyle.Secondary,
                Margin = new XPlatThickness(0f, 0f, 6f, 0f),
            };
            button.AddClickHandler(_ => onClick());
            return button;
        }

        void Load(int count)
        {
            long started = Stopwatch.GetTimestamp();

            fill(count);

            countLabel.Text = count == 0 ? $"no {noun}" : $"{count:N0} {noun}";
            SetStatus($"Loaded {count:N0} {noun} in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.0} ms.");
        }
    }

    /// <summary>An invisible full-width strip a page uses to lay a row of controls out in.</summary>
    private static XPlatPanel Row(float height) => new()
    {
        Dock = XDock.Top,
        Height = height,
        ShowFrame = false,
        IsCard = false,
        BackColor = null,
        BorderWidth = 0f,
        CornerRadius = 0f,
        Padding = new XPlatThickness(0f),
        Margin = new XPlatThickness(0f, 0f, 0f, 8f),
    };

    /// <summary>
    /// Runs <paramref name="action"/> the first time <paramref name="control"/> has a host, which
    /// is the first moment there is one to subscribe to. Pages are built before they are attached,
    /// and attaching lays the tree out straight away, so the first bounds change is the earliest
    /// point at which <see cref="XPlatControl.Host"/> resolves. The handler stays registered and
    /// does nothing after that rather than disposing itself from inside its own invocation.
    /// </summary>
    private static void OnHostAvailable(XPlatControl control, Action<XPlatUiHost> action)
    {
        bool done = false;

        control.AddBoundsChangedHandler(_ =>
        {
            if (done || control.Host is not { } host)
            {
                return;
            }

            done = true;
            action(host);
        });
    }

    private XPlatPanel BuildScrollingCard()
    {
        var card = Card();

        card.Controls.Add(ScrollCaption("VerticalScroll = Automatic"));
        card.Controls.Add(BuildAutoScrollList());
        card.Controls.Add(ScrollCaption("HorizontalScroll = Always"));
        card.Controls.Add(BuildAlwaysScrollStrip());

        return card;
    }

    private static XPlatLabel ScrollCaption(string text) => new(text)
    {
        Dock = XDock.Top,
        AutoSize = false,
        Height = 16f,
        Font = XPlatTheme.CaptionFont,
        ForeColorRole = XPlatThemeColor.MutedForeground,
        TextAlign = XPlatContentAlignment.BottomLeft,
        Margin = new XPlatThickness(0f, 0f, 0f, 2f),
    };

    /// <summary>An invisible strip that a row of buttons docks into, left to right.</summary>
    private static XPlatPanel ButtonRow() => new()
    {
        Dock = XDock.Top,
        Height = 30f,
        ShowFrame = false,
        IsCard = false,
        BackColor = null,
        BorderWidth = 0f,
        CornerRadius = 0f,
        Padding = new XPlatThickness(0f),
        Margin = new XPlatThickness(0f, 0f, 0f, 6f),
    };

    /// <summary>
    /// An <see cref="XPlatScrollStyle.Automatic"/> panel: the vertical bar appears only because the
    /// rows overflow. Only children with <see cref="DockStyle.None"/> scroll, so each row is placed
    /// explicitly instead of being docked.
    /// </summary>
    private XPlatPanel BuildAutoScrollList()
    {
        var list = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 90f,
            VerticalScroll = XPlatScrollStyle.Automatic,
            Padding = new XPlatThickness(4f),
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
        };

        for (int i = 0; i < 10; i++)
        {
            var row = new XPlatButton($"Row {i + 1}")
            {
                AutoSize = false,
                Style = XPlatButtonStyle.Secondary,
                Bounds = new SKRect(4f, 4f + (i * 24f), 128f, 26f + (i * 24f)),
            };
            row.AddClickHandler(_ => SetStatus($"Row {i + 1} clicked - hit testing follows the scroll offset."));
            list.Controls.Add(row);
        }

        return list;
    }

    /// <summary>An <see cref="XPlatScrollStyle.Always"/> panel: the bar is shown even when it fits.</summary>
    private XPlatPanel BuildAlwaysScrollStrip()
    {
        var strip = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 36f,
            HorizontalScroll = XPlatScrollStyle.Always,
            Padding = new XPlatThickness(4f),
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
        };

        for (int i = 0; i < 10; i++)
        {
            strip.Controls.Add(new XPlatLabel($"Chip {i + 1}")
            {
                AutoSize = false,
                BackColor = XPlatTheme.Secondary,
                ForeColorRole = XPlatThemeColor.MutedForeground,
                TextAlign = XPlatContentAlignment.MiddleCenter,
                Bounds = new SKRect(4f + (i * 92f), 4f, 88f + (i * 92f), 16f),
            });
        }

        return strip;
    }

    private XPlatPanel BuildButtonsCard()
    {
        var card = Card();

        _primaryButton = new XPlatButton("Primary action")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = XPlatButtonStyle.Primary,
            Margin = new XPlatThickness(0f, 0f, 0f, 10f),
        };
        _primaryButton.AddClickHandler(_ =>
        {
            _clicks++;
            UpdateCounter();
            SetStatus($"Primary action clicked {_clicks} time(s).");
        });
        card.Controls.Add(_primaryButton);

        var secondary = new XPlatButton("Secondary action")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 0f, 10f),
        };
        secondary.AddClickHandler(_ => SetStatus("Secondary action clicked."));
        card.Controls.Add(secondary);

        var ghost = new XPlatButton("Ghost action")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = XPlatButtonStyle.Ghost,
            Margin = new XPlatThickness(0f, 0f, 0f, 10f),
        };
        ghost.AddClickHandler(_ => SetStatus("Ghost action clicked."));
        card.Controls.Add(ghost);

        card.Controls.Add(new XPlatLabel("Tab moves focus between controls; Enter or Space activates the focused button.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 40f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        return card;
    }

    private static XPlatPanel BuildImagesCard()
    {
        var card = Card();

        // Opacity fades a control and everything inside it as a single group, so the gradient and
        // the panel it sits in are composited once over the card instead of each blending on its
        // own. A faded control is not opaque, which is what tells the card that repainting it has
        // to bring back what shows through.
        var faded = new XPlatPanel
        {
            Dock = XDock.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
            Opacity = 0.6f,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        };

        var image = new XPlatImage
        {
            Dock = XDock.Fill,
            SizeMode = XPlatSizeMode.Zoom,
            ImageAlign = XPlatContentAlignment.MiddleCenter,
            CornerRadius = 8f,
        };
        image.Image = Images.LoadFromStream(CreateGradientPng(320, 200));
        faded.Controls.Add(image);
        card.Controls.Add(faded);

        card.Controls.Add(new XPlatLabel("A gradient drawn with SkiaSharp, shown with XPlatImage (Zoom) at Opacity 0.6.")
        {
            Dock = XDock.Bottom,
            AutoSize = false,
            Height = 34f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        return card;
    }

    private void UpdateCounter() => _counterLabel.Text = $"Button clicks: {_clicks}";

    /// <summary>The sizes the icon gallery draws each document at.</summary>
    private static readonly float[] SvgIconSizes = [16f, 24f, 32f, 48f, 64f];

    /// <summary>
    /// A gallery: five documents, each drawn at five sizes.
    /// </summary>
    /// <remarks>
    /// Every one of them is loaded through the same factory call a PNG goes through, so what the
    /// page shows is the path an application gets for free once the image format provider is
    /// registered.
    /// </remarks>
    private static XPlatPanel BuildSvgIconGallery()
    {
        var card = Card();
        card.Controls.Add(Heading("SVG icons"));

        card.Controls.Add(new XPlatLabel(
            "Five documents at 16, 24, 32, 48 and 64 pixels. Each is parsed once and the same "
            + "picture is re-recorded for each control, at whatever size the control asks for.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 34f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        });

        foreach (var (name, source) in new[]
                 {
                     ("Star", DemoIcons.Star),
                     ("Gear", DemoIcons.Gear),
                     ("Heart", DemoIcons.Heart),
                     ("Tick", DemoIcons.Tick),
                     ("Disc", DemoIcons.Disc),
                 })
        {
            var row = ThumbnailRow();
            row.Controls.Add(new XPlatLabel(name)
            {
                Dock = XDock.Left,
                Width = 64f,
                AutoSize = false,
                Font = XPlatTheme.CaptionFont,
                ForeColorRole = XPlatThemeColor.MutedForeground,
                TextAlign = XPlatContentAlignment.MiddleLeft,
            });

            foreach (float size in SvgIconSizes)
            {
                row.Controls.Add(SvgCell(source, size));
            }

            card.Controls.Add(row);
        }

        return card;
    }

    /// <summary>
    /// <c>currentColor</c> following the palette, which is the one part of an SVG a theme can change.
    /// </summary>
    /// <remarks>
    /// The colours are resolved while each document is parsed, so a palette change reloads the
    /// documents rather than repainting them. Two colours of the same icon are two cache entries and
    /// a third switch back to the first parses nothing, which is why following the palette this way
    /// does not cost a parse per frame or per theme toggle.
    /// </remarks>
    private static XPlatPanel BuildSvgRecolourDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("SVG recolour"));

        card.Controls.Add(new XPlatLabel(
            "The same document resolved to four fixed colours, then to three theme roles. The bottom "
            + "row follows the palette - switch the appearance from the header and it recolours with "
            + "everything else, which is what a fixed fill above cannot do.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 40f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        });

        var fixedRow = ThumbnailRow();
        foreach (var (label, colour) in new (string, SKColor)[]
                 {
                     ("Foreground", new SKColor(0x22, 0x22, 0x22)),
                     ("Accent", new SKColor(0x2f, 0x6f, 0xd0)),
                     ("Warning", new SKColor(0xc0, 0x6a, 0x00)),
                     ("Error", new SKColor(0xc4, 0x2b, 0x2b)),
                 })
        {
            fixedRow.Controls.Add(RecolourCell(DemoIcons.CurrentColorBadge, 40f, colour, label));
        }

        card.Controls.Add(fixedRow);

        var themeRow = ThumbnailRow();
        var themed = new List<(XPlatImage Image, string Role)>();

        foreach (string role in new[] { "Foreground", "Accent", "Muted" })
        {
            var cell = new XPlatPanel
            {
                Dock = XDock.Left,
                Width = 120f,
                ShowFrame = false,
                IsCard = false,
                BackColor = null,
                BorderWidth = 0f,
                CornerRadius = 0f,
                Padding = new XPlatThickness(4f),
            };

            cell.Controls.Add(new XPlatLabel("Theme: " + role)
            {
                Dock = XDock.Bottom,
                Height = 18f,
                AutoSize = false,
                Font = XPlatTheme.CaptionFont,
                ForeColorRole = XPlatThemeColor.MutedForeground,
                TextAlign = XPlatContentAlignment.TopLeft,
            });

            var image = new XPlatImage
            {
                Dock = XDock.Fill,
                AutoSize = false,
                SizeMode = XPlatSizeMode.Zoom,
                ImageAlign = XPlatContentAlignment.MiddleCenter,
            };

            Reload(image, DemoIcons.CurrentColorBadge, ThemeColour(role));
            themed.Add((image, role));
            cell.Controls.Add(image);
            themeRow.Controls.Add(cell);
        }

        card.Controls.Add(themeRow);

        card.Controls.Add(new XPlatLabel("The badge is drawn with currentColor, so one document is every colour.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 22f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        // Reloading on a palette change is what makes the row follow the theme. It is one call per
        // image and no parse for a colour already seen, because the cache is keyed on the colour.
        // The subscription is never disposed: the handler lives as long as the process, which is
        // what a demo wants and an application would tie to its window instead.
        XPlatTheme.AddChangedHandler(_ =>
        {
            foreach (var (image, role) in themed)
            {
                Reload(image, DemoIcons.CurrentColorBadge, ThemeColour(role));
            }
        });

        return card;
    }

    /// <summary>
    /// The same artwork as vector and as a thumbnail blown up, which is the argument for the feature
    /// rather than a feature of it.
    /// </summary>
    private static XPlatPanel BuildSvgRasterTwinDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("SVG and a raster twin"));

        card.Controls.Add(new XPlatLabel(
            "The same document drawn at 96 pixels, and the same document rendered once to a 16 pixel "
            + "PNG and then drawn at 96 pixels. The vector stays sharp because it is painted at the "
            + "size it is shown; the raster cannot.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 46f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        });

        var row = ThumbnailRow(height: 128f);
        row.Controls.Add(RasterTwinCell("SVG, drawn at 96", vector: true));
        row.Controls.Add(RasterTwinCell("A 16 pixel PNG at 96", vector: false));
        card.Controls.Add(row);

        return card;
    }

    private static SKColor ThemeColour(string role) => role switch
    {
        "Accent" => XPlatTheme.Accent,
        "Muted" => XPlatTheme.MutedForeground,
        _ => XPlatTheme.Foreground,
    };

    private static XPlatPanel ThumbnailRow(float height = 72f) => new()
    {
        Dock = XDock.Top,
        Height = height,
        ShowFrame = false,
        IsCard = false,
        BackColor = null,
        BorderWidth = 0f,
        CornerRadius = 0f,
        Padding = new XPlatThickness(0f),
        Margin = new XPlatThickness(0f, 0f, 0f, 4f),
    };

    /// <summary>One icon at one size, in a cell the gallery can line up.</summary>
    private static XPlatPanel SvgCell(string source, float size)
    {
        var cell = new XPlatPanel
        {
            Dock = XDock.Left,
            Width = size + 20f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(6f),
        };

        var image = new XPlatImage
        {
            Dock = XDock.Fill,
            AutoSize = false,
            SizeMode = XPlatSizeMode.Zoom,
            ImageAlign = XPlatContentAlignment.MiddleCenter,
        };

        using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(source)))
        {
            image.Image = Images.LoadFromStream(stream);
        }

        cell.Controls.Add(image);
        return cell;
    }

    /// <summary>One recoloured icon with the colour named under it.</summary>
    private static XPlatPanel RecolourCell(string source, float size, SKColor colour, string label)
    {
        var cell = new XPlatPanel
        {
            Dock = XDock.Left,
            Width = 120f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(4f),
        };

        cell.Controls.Add(new XPlatLabel(label)
        {
            Dock = XDock.Bottom,
            Height = 18f,
            AutoSize = false,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        var image = new XPlatImage
        {
            Dock = XDock.Fill,
            AutoSize = false,
            SizeMode = XPlatSizeMode.Zoom,
            ImageAlign = XPlatContentAlignment.MiddleCenter,
        };

        Reload(image, source, colour);
        cell.Controls.Add(image);
        return cell;
    }

    private static XPlatPanel RasterTwinCell(string caption, bool vector)
    {
        var cell = new XPlatPanel
        {
            Dock = XDock.Left,
            Width = 200f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(4f),
        };

        cell.Controls.Add(new XPlatLabel(caption)
        {
            Dock = XDock.Bottom,
            Height = 18f,
            AutoSize = false,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        // A fixed 96x96 box, so both cells are drawn at exactly the same size and the only
        // difference between them is the source the 96 pixels came from.
        var image = new XPlatImage
        {
            Dock = XDock.Left,
            AutoSize = false,
            Width = 96f,
            Height = 96f,
            SizeMode = XPlatSizeMode.StretchImage,
            ImageAlign = XPlatContentAlignment.MiddleCenter,
        };

        if (vector)
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(DemoIcons.Disc));
            image.Image = Images.LoadFromStream(stream);
        }
        else
        {
            image.Image = Images.LoadFromBytes(RasteriseSvg(DemoIcons.Disc, 16));
        }

        cell.Controls.Add(image);
        return cell;
    }

    /// <summary>Renders a document once to a PNG of the requested size, the way a build step would.</summary>
    private static byte[] RasteriseSvg(string source, int size)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(source));
        using var data = Images.LoadFromStream(stream);
        var picture = data.CurrentFrame!;

        using var surface = SKSurface.Create(new SKImageInfo(size, size));
        surface.Canvas.Clear(SKColors.Transparent);

        var bounds = picture.CullRect;
        surface.Canvas.Scale(size / bounds.Width, size / bounds.Height);
        surface.Canvas.DrawPicture(picture);

        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static void Reload(XPlatImage image, string source, SKColor colour)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(source));

        // The control does not own what it shows, so replacing an image here releases the one it
        // was showing by hand rather than relying on the control to do it.
        image.Image?.Dispose();
        image.Image = Images.LoadFromStream(stream, new XPlatSvgOptions { CurrentColor = colour });
    }

    /// <summary>
    /// The animated GIF page: the picture itself, play and pause, a seek bar, and a readout that says
    /// where in the pass it is.
    /// </summary>
    /// <remarks>
    /// The readout is a timer of the demo's own, not the player's. The player runs one only to
    /// repaint when a frame boundary arrives - about twelve times a second for this file - and this
    /// text wants a number every fifty milliseconds. The demo timer follows the same rule the player
    /// follows: it exists only while the page is displayed, and the position it prints comes from the
    /// clock rather than from the ticks.
    /// </remarks>
    private XPlatPanel BuildAnimatedGifDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Animated GIF"));

        var data = Images.LoadFromBytes(Convert.FromBase64String(DemoGif.Spinner));

        var row = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 206f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };

        var image = new XPlatImage
        {
            AutoSize = false,
            SizeMode = XPlatSizeMode.Zoom,
            ImageAlign = XPlatContentAlignment.MiddleCenter,
            CornerRadius = 6f,
            Bounds = new SKRect(0f, 4f, 192f, 196f),
            Image = data,
        };
        row.Controls.Add(image);

        var playPause = new XPlatButton("Pause")
        {
            AutoSize = false,
            Width = 110f,
            Bounds = new SKRect(208f, 12f, 318f, 42f),
        };
        row.Controls.Add(playPause);

        var seek = new XPlatTrackBar
        {
            Minimum = 0f,
            Maximum = 1000f,
            Value = 0f,
            SmallChange = 10f,
            LargeChange = 100f,
            TickFrequency = 100f,
            Bounds = new SKRect(208f, 50f, 468f, 78f),
        };
        row.Controls.Add(seek);

        var readout = new XPlatLabel("0.00 s of 0.96 s - playing")
        {
            AutoSize = false,
            Bounds = new SKRect(208f, 84f, 468f, 104f),
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
        };
        row.Controls.Add(readout);

        row.Controls.Add(new XPlatLabel(
            "48x48, twelve frames, 80 ms each, and the file declares an infinite loop. The frames are "
            + "deltas, so the decoder composites them: the second frame onwards carries only the "
            + "eleven-pixel-wide strip the square moved through.")
        {
            AutoSize = false,
            Bounds = new SKRect(208f, 110f, 468f, 196f),
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
        });

        card.Controls.Add(row);

        bool seeking = false;

        void Update()
        {
            var position = data.Position;
            var duration = data.Duration.TotalMilliseconds;

            readout.Text = duration <= 0
                ? "still image"
                : $"{position.TotalSeconds:0.00} s of {data.Duration.TotalSeconds:0.00} s - "
                  + (data.IsPlaying ? "playing" : data.Loop ? "paused" : "stopped");

            seeking = true;
            seek.Value = duration <= 0 ? 0f : (float)(position.TotalMilliseconds / duration * 1000.0);
            seeking = false;
        }

        playPause.AddClickHandler(_ =>
        {
            if (data.IsPlaying)
            {
                data.Pause();
            }
            else
            {
                data.Play();
            }

            playPause.Text = data.IsPlaying ? "Pause" : "Play";
            Update();
        });

        seek.AddValueChangedHandler(_ =>
        {
            if (seeking || data.Duration <= TimeSpan.Zero)
            {
                return;
            }

            data.Seek(TimeSpan.FromMilliseconds(data.Duration.TotalMilliseconds * seek.Value / 1000.0));
            Update();
        });

        var timer = new XPlatTimer(TimeSpan.FromMilliseconds(50));
        timer.AddTickHandler(_ => Update());
        image.AddDisplayedChangedHandler(_ =>
        {
            if (image.IsDisplayed)
            {
                Update();
                timer.Start();
            }
            else
            {
                timer.Stop();
            }
        });

        data.Play();
        Update();

        card.Controls.Add(ScrollCaption(
            "Play and Pause are the timeline's; the seek bar re-anchors it, so a seek during playback "
            + "does not jump when the clock catches up."));

        return card;
    }

    /// <summary>
    /// The frame-stepping page: one frame at a time, backwards or forwards, on a paused animation.
    /// </summary>
    /// <remarks>
    /// Stepping is a seek to a frame's start, and the frame starts are read back from the timeline
    /// itself rather than from a table: <see cref="XPlatImageData.TimeUntilNextFrame"/> walked from
    /// zero is the list of boundaries, which is the same number the control schedules its repaints
    /// by. Because every frame was decoded up front, stepping backwards costs what stepping forwards
    /// does.
    /// </remarks>
    private XPlatPanel BuildGifFrameDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("GIF frames"));

        var data = Images.LoadFromBytes(Convert.FromBase64String(DemoGif.Spinner));
        var starts = FrameStarts(data);
        int index = 0;

        var row = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 206f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };

        var image = new XPlatImage
        {
            AutoSize = false,
            SizeMode = XPlatSizeMode.Zoom,
            ImageAlign = XPlatContentAlignment.MiddleCenter,
            CornerRadius = 6f,
            Bounds = new SKRect(0f, 4f, 192f, 196f),
            Image = data,
        };
        row.Controls.Add(image);

        var previous = new XPlatButton("Previous")
        {
            AutoSize = false,
            Width = 100f,
            Bounds = new SKRect(208f, 12f, 308f, 42f),
        };
        row.Controls.Add(previous);

        var next = new XPlatButton("Next")
        {
            AutoSize = false,
            Width = 100f,
            Bounds = new SKRect(318f, 12f, 418f, 42f),
        };
        row.Controls.Add(next);

        var readout = new XPlatLabel(string.Empty)
        {
            AutoSize = false,
            Bounds = new SKRect(208f, 54f, 468f, 76f),
        };
        row.Controls.Add(readout);

        row.Controls.Add(new XPlatLabel(
            "The animation is paused and the buttons seek to a frame boundary. The readout's second "
            + "number is the frame's start on the timeline, which is what the player schedules its "
            + "repaints by.")
        {
            AutoSize = false,
            Bounds = new SKRect(208f, 84f, 468f, 196f),
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
        });

        card.Controls.Add(row);

        void Show(int wanted)
        {
            index = Math.Clamp(wanted, 0, starts.Count - 1);
            data.Seek(starts[index]);
            readout.Text = $"Frame {index + 1} of {starts.Count} - starts at {starts[index].TotalMilliseconds:0} ms";
        }

        previous.AddClickHandler(_ => Show(index - 1));
        next.AddClickHandler(_ => Show(index + 1));
        Show(0);

        return card;
    }

    /// <summary>
    /// The page that shows time passing while nobody is looking: scroll the picture out of the
    /// scroll panel, wait, scroll back, and the frame is the one the clock says rather than the one
    /// it left on.
    /// </summary>
    /// <remarks>
    /// Nothing special makes this happen - the position is computed from a clock rather than
    /// accumulated by a timer - so the page exists to make the claim visible: the readout keeps
    /// counting while the picture is scrolled away, and the frame that comes back is the one the
    /// readout describes.
    /// </remarks>
    private XPlatPanel BuildGifScrollDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("GIF out of view"));

        var data = Images.LoadFromBytes(Convert.FromBase64String(DemoGif.Spinner));

        // A stack inside a fixed frame, which is the scrolling container the Scrolling page uses: the
        // run of children is what makes the picture scrollable out of the frame.
        var viewportFrame = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 150f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
        };

        var viewport = new XPlatStackPanel
        {
            Dock = XDock.Fill,
            VerticalScroll = XPlatScrollStyle.Automatic,
            Padding = new XPlatThickness(4f),
            Spacing = 4f,
        };
        viewportFrame.Controls.Add(viewport);

        var image = new XPlatImage
        {
            AutoSize = true,
            MinimumSize = new SKSize(120f, 120f),
            HorizontalAlignment = XPlatAlignment.Near,
            SizeMode = XPlatSizeMode.Zoom,
            CornerRadius = 6f,
            Image = data,
        };
        viewport.Controls.Add(image);

        for (int row = 0; row < 8; row++)
        {
            viewport.Controls.Add(new XPlatLabel($"Filler row {row + 1} - scroll past the picture")
            {
                AutoSize = false,
                Height = 22f,
                Font = XPlatTheme.CaptionFont,
                ForeColorRole = XPlatThemeColor.MutedForeground,
            });
        }

        card.Controls.Add(viewportFrame);

        // The page runs its own cycle so the claim can be watched without touching anything, but a
        // page that pulls the viewport out from under the pointer is worse than one that needs a
        // button: every leg first checks that the offset is still the one the cycle itself wrote,
        // and the first scroll somebody else does stops the cycle for good. What the page is showing
        // - that time passes while the picture is out of view - is still automatic.
        float home = viewport.VerticalOffset;
        float owned = home;
        bool tookOver = false;

        var awayTimer = new XPlatTimer(TimeSpan.FromMilliseconds(2300));
        var backTimer = new XPlatTimer(TimeSpan.FromMilliseconds(700));

        void StopCycle()
        {
            awayTimer.Stop();
            backTimer.Stop();
        }

        bool StillOurs()
        {
            if (tookOver)
            {
                return false;
            }

            if (MathF.Abs(viewport.VerticalOffset - owned) < 0.5f)
            {
                return true;
            }

            // Somebody scrolled: the page gives the viewport up rather than dragging it back to
            // where the demonstration wanted it.
            tookOver = true;
            StopCycle();
            SetStatus("You scrolled it, so the page has handed the scrolling back to you.");
            return false;
        }

        void CycleAway()
        {
            if (!StillOurs())
            {
                return;
            }

            owned = 140f;
            viewport.ScrollTo(0f, owned);
            backTimer.Start();
        }

        void CycleBack()
        {
            if (!StillOurs())
            {
                return;
            }

            owned = home;
            viewport.ScrollTo(0f, home);
            awayTimer.Start();
        }

        awayTimer.AddTickHandler(_ =>
        {
            awayTimer.Stop();
            CycleAway();
        });

        backTimer.AddTickHandler(_ =>
        {
            backTimer.Stop();
            CycleBack();
        });

        var buttons = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 34f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
        };

        var away = new XPlatButton("Scroll the picture away")
        {
            AutoSize = false,
            Width = 200f,
            Bounds = new SKRect(0f, 2f, 200f, 32f),
        };

        var back = new XPlatButton("Scroll it back")
        {
            AutoSize = false,
            Width = 150f,
            Bounds = new SKRect(210f, 2f, 360f, 32f),
        };

        away.AddClickHandler(_ =>
        {
            SetStatus("The picture is scrolled out of view; the timeline is not.");

            // A click is the user asking the page to move it, so the cycle is re-armed from wherever
            // the panel is now.
            tookOver = false;
            owned = viewport.VerticalOffset;
            CycleAway();
        });
        back.AddClickHandler(_ =>
        {
            SetStatus("The picture is back; the frame is the one the clock reached.");

            tookOver = false;
            owned = viewport.VerticalOffset;
            CycleBack();
        });

        bool travelled = false;

        buttons.Controls.Add(away);
        buttons.Controls.Add(back);
        card.Controls.Add(buttons);

        var readout = ScrollCaption("Position 0.00 s of 0.96 s");

        var timer = new XPlatTimer(TimeSpan.FromMilliseconds(50));
        timer.AddTickHandler(_ =>
            readout.Text = $"Position {data.Position.TotalSeconds:0.00} s of {data.Duration.TotalSeconds:0.00} s");

        image.AddDisplayedChangedHandler(_ =>
        {
            if (image.IsDisplayed)
            {
                timer.Start();

                // The cycle starts the first time the page is looked at, so the demonstration happens
                // without anyone clicking anything.
                if (!travelled)
                {
                    travelled = true;
                    awayTimer.Start();
                }
            }
            else
            {
                timer.Stop();
                StopCycle();
            }
        });

        data.Play();
        card.Controls.Add(readout);

        card.Controls.Add(Note(
            "Scroll the picture away, wait, then scroll it back: the frame you get is the one the clock "
            + "says, not the one it left on. Nothing in the player arranges that - the position is "
            + "computed from a clock rather than accumulated by a timer - so the readout keeps counting "
            + "while the picture is out of the frame and the frame that comes back agrees with it. The "
            + "page runs the cycle itself so it can be watched without touching anything, and it stops "
            + "the moment you scroll the panel yourself: a demonstration that fights the pointer is "
            + "worse than one that needs a button."));

        return card;
    }

    /// <summary>Where each frame of an animation starts, read back from the timeline itself.</summary>
    private static List<TimeSpan> FrameStarts(XPlatImageData data)
    {
        var starts = new List<TimeSpan> { TimeSpan.Zero };
        var position = TimeSpan.Zero;

        while (true)
        {
            if (data.TimeUntilNextFrame(position) is not { } wait)
            {
                return starts;
            }

            var boundary = position + wait;
            if (boundary <= position || boundary >= data.Duration)
            {
                return starts;
            }

            starts.Add(boundary);
            position = boundary;
        }
    }

    /// <summary>
    /// The animated WebP page: the picture itself, play and pause, a seek bar, and a readout that says
    /// where in the pass it is.
    /// </summary>
    /// <remarks>
    /// The readout is a timer of the demo's own, not the player's, exactly as the GIF page does it:
    /// the player runs one only to repaint when a frame boundary arrives - about twelve times a
    /// second for this file - and this text wants a number every fifty milliseconds. The demo timer
    /// follows the same rule the player follows: it exists only while the page is displayed, and the
    /// position it prints comes from the clock rather than from the ticks.
    /// </remarks>
    private XPlatPanel BuildAnimatedWebpDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Animated WebP"));

        var data = Images.LoadFromBytes(Convert.FromBase64String(DemoWebp.Spinner));

        var row = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 206f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };

        var image = new XPlatImage
        {
            AutoSize = false,
            SizeMode = XPlatSizeMode.Zoom,
            ImageAlign = XPlatContentAlignment.MiddleCenter,
            CornerRadius = 6f,
            Bounds = new SKRect(0f, 4f, 192f, 196f),
            Image = data,
        };
        row.Controls.Add(image);

        var playPause = new XPlatButton("Pause")
        {
            AutoSize = false,
            Width = 110f,
            Bounds = new SKRect(208f, 12f, 318f, 42f),
        };
        row.Controls.Add(playPause);

        var seek = new XPlatTrackBar
        {
            Minimum = 0f,
            Maximum = 1000f,
            Value = 0f,
            SmallChange = 10f,
            LargeChange = 100f,
            TickFrequency = 100f,
            Bounds = new SKRect(208f, 50f, 468f, 78f),
        };
        row.Controls.Add(seek);

        var readout = new XPlatLabel("0.00 s of 0.96 s - playing")
        {
            AutoSize = false,
            Bounds = new SKRect(208f, 84f, 468f, 104f),
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
        };
        row.Controls.Add(readout);

        row.Controls.Add(new XPlatLabel(
            "48x48, twelve frames, 80 ms each, and the RIFF ANIM chunk declares an infinite loop. The "
            + "first frame is the whole canvas and the eleven after it are 8x8 ANMF sub-rectangles, so "
            + "the compositor draws each one at its own offset rather than replacing the canvas.")
        {
            AutoSize = false,
            Bounds = new SKRect(208f, 110f, 468f, 196f),
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
        });

        card.Controls.Add(row);

        bool seeking = false;

        void Update()
        {
            var position = data.Position;
            var duration = data.Duration.TotalMilliseconds;

            readout.Text = duration <= 0
                ? "still image"
                : $"{position.TotalSeconds:0.00} s of {data.Duration.TotalSeconds:0.00} s - "
                  + (data.IsPlaying ? "playing" : data.Loop ? "paused" : "stopped");

            seeking = true;
            seek.Value = duration <= 0 ? 0f : (float)(position.TotalMilliseconds / duration * 1000.0);
            seeking = false;
        }

        playPause.AddClickHandler(_ =>
        {
            if (data.IsPlaying)
            {
                data.Pause();
            }
            else
            {
                data.Play();
            }

            playPause.Text = data.IsPlaying ? "Pause" : "Play";
            Update();
        });

        seek.AddValueChangedHandler(_ =>
        {
            if (seeking || data.Duration <= TimeSpan.Zero)
            {
                return;
            }

            data.Seek(TimeSpan.FromMilliseconds(data.Duration.TotalMilliseconds * seek.Value / 1000.0));
            Update();
        });

        var timer = new XPlatTimer(TimeSpan.FromMilliseconds(50));
        timer.AddTickHandler(_ => Update());
        image.AddDisplayedChangedHandler(_ =>
        {
            if (image.IsDisplayed)
            {
                Update();
                timer.Start();
            }
            else
            {
                timer.Stop();
            }
        });

        data.Play();
        Update();

        card.Controls.Add(ScrollCaption(
            "Play and Pause are the timeline's; the seek bar re-anchors it, so a seek during playback "
            + "does not jump when the clock catches up. The timeline is the GIF feature's, unchanged."));

        return card;
    }

    /// <summary>
    /// The WebP frame-stepping page: one frame at a time, backwards or forwards, on a paused
    /// animation whose boundaries come from the timeline rather than from a table.
    /// </summary>
    /// <remarks>
    /// Stepping is a seek to a frame's start, and the starts are walked from
    /// <see cref="XPlatImageData.TimeUntilNextFrame"/>, which is the same number the control
    /// schedules its repaints by. Because every frame was decoded up front, stepping backwards costs
    /// what stepping forwards does, and the sub-rectangle frames make the offset visible when the
    /// readout names the frame.
    /// </remarks>
    private XPlatPanel BuildWebpFrameDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("WebP frames"));

        var data = Images.LoadFromBytes(Convert.FromBase64String(DemoWebp.Spinner));
        var starts = FrameStarts(data);
        int index = 0;

        var row = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 206f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };

        var image = new XPlatImage
        {
            AutoSize = false,
            SizeMode = XPlatSizeMode.Zoom,
            ImageAlign = XPlatContentAlignment.MiddleCenter,
            CornerRadius = 6f,
            Bounds = new SKRect(0f, 4f, 192f, 196f),
            Image = data,
        };
        row.Controls.Add(image);

        var previous = new XPlatButton("Previous")
        {
            AutoSize = false,
            Width = 100f,
            Bounds = new SKRect(208f, 12f, 308f, 42f),
        };
        row.Controls.Add(previous);

        var next = new XPlatButton("Next")
        {
            AutoSize = false,
            Width = 100f,
            Bounds = new SKRect(318f, 12f, 418f, 42f),
        };
        row.Controls.Add(next);

        var readout = new XPlatLabel(string.Empty)
        {
            AutoSize = false,
            Bounds = new SKRect(208f, 54f, 468f, 76f),
        };
        row.Controls.Add(readout);

        row.Controls.Add(new XPlatLabel(
            "The animation is paused and the buttons seek to a frame boundary. The readout's second "
            + "number is the frame's start on the timeline, which is what the player schedules its "
            + "repaints by.")
        {
            AutoSize = false,
            Bounds = new SKRect(208f, 84f, 468f, 196f),
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
        });

        card.Controls.Add(row);

        void Show(int wanted)
        {
            index = Math.Clamp(wanted, 0, starts.Count - 1);
            data.Seek(starts[index]);
            readout.Text = $"Frame {index + 1} of {starts.Count} - starts at {starts[index].TotalMilliseconds:0} ms";
        }

        previous.AddClickHandler(_ => Show(index - 1));
        next.AddClickHandler(_ => Show(index + 1));
        Show(0);

        return card;
    }

    /// <summary>
    /// Drop-down lists, including the two cases the popup layer exists for: a list longer than its
    /// owner and one near the bottom of the window, which hangs past the edge of the window instead
    /// of being clipped by it.
    /// </summary>
    private XPlatPanel BuildComboBoxDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Combo boxes"));

        card.Controls.Add(ScrollCaption("A few items"));

        var sizes = new XPlatComboBox
        {
            Dock = XDock.Top,
            Height = 32f,
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        };

        foreach (string size in new[] { "Small", "Medium", "Large", "Extra large" })
        {
            sizes.Items.Add(size);
        }

        sizes.SelectedIndex = 1;
        sizes.AddSelectedIndexChangedHandler(_ => SetStatus($"Size is {sizes.SelectedText}."));
        card.Controls.Add(sizes);

        card.Controls.Add(ScrollCaption("No selection, so the empty text shows"));

        var optional = new XPlatComboBox
        {
            Dock = XDock.Top,
            Height = 32f,
            EmptyText = "Nothing selected",
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        };

        optional.Items.Add("Alpha");
        optional.Items.Add("Beta");
        optional.Items.Add("Gamma");
        card.Controls.Add(optional);

        card.Controls.Add(ScrollCaption("Longer than its owner, so the list scrolls"));

        var languages = new XPlatComboBox
        {
            Dock = XDock.Top,
            Height = 32f,
            MaxDropDownHeight = 160f,
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        };

        foreach (string language in new[]
                 {
                     "C#", "F#", "Rust", "Go", "Swift", "Kotlin", "TypeScript", "Python",
                     "Ruby", "Elixir", "Haskell", "OCaml", "Zig", "Nim", "D", "Julia",
                 })
        {
            languages.Items.Add(language);
        }

        languages.SelectedIndex = 0;
        card.Controls.Add(languages);

        card.Controls.Add(ScrollCaption("Disabled"));

        var disabled = new XPlatComboBox
        {
            Dock = XDock.Top,
            Height = 32f,
            Enabled = false,
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        };

        disabled.Items.Add("Cannot be opened");
        disabled.SelectedIndex = 0;
        card.Controls.Add(disabled);

        card.Controls.Add(new XPlatLabel(
            "A drop-down is drawn in a window of its own when the platform layer provides one, so it can " +
            "hang past the edges of the window. Without one it is painted inside the window instead.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 54f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>
    /// <see cref="XPlatDateTimePicker"/>: the four formats, a bounded one, an empty one and a
    /// disabled one, plus a readout so committing typed text can be watched.
    /// </summary>
    private XPlatPanel BuildDateTimePickerDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Date and time"));

        var readout = ScrollCaption("Value: 2026-11-04 09:30");
        card.Controls.Add(readout);

        var shortPicker = new XPlatDateTimePicker(new DateTime(2026, 11, 4, 9, 30, 0));
        shortPicker.AddValueChangedHandler(_ =>
            readout.Text = $"Value: {shortPicker.Value:yyyy-MM-dd HH:mm}");
        card.Controls.Add(CaptionRow("Short (typed or picked)", shortPicker));

        card.Controls.Add(CaptionRow("Long", new XPlatDateTimePicker(new DateTime(2026, 11, 4))
        {
            Format = XPlatDateTimePickerFormat.Long,
        }));

        card.Controls.Add(CaptionRow("Time", new XPlatDateTimePicker(new DateTime(2026, 11, 4, 9, 30, 0))
        {
            Format = XPlatDateTimePickerFormat.Time,
        }));

        card.Controls.Add(CaptionRow(
            "Custom (yyyy-MM-dd HH:mm)",
            new XPlatDateTimePicker(new DateTime(2026, 11, 4, 9, 30, 0))
            {
                Format = XPlatDateTimePickerFormat.CustomFormat,
                CustomFormat = "yyyy-MM-dd HH:mm",
            }));

        DateTime today = DateTime.Today;
        card.Controls.Add(CaptionRow("Bounded (today +/- 7 days)", new XPlatDateTimePicker(today)
        {
            MinDate = today.AddDays(-7),
            MaxDate = today.AddDays(7),
        }));

        card.Controls.Add(CaptionRow("Empty (placeholder)", new XPlatDateTimePicker
        {
            PlaceholderText = "Pick a date",
        }));

        card.Controls.Add(CaptionRow("Disabled", new XPlatDateTimePicker(new DateTime(2026, 11, 4))
        {
            Enabled = false,
        }));

        card.Controls.Add(new XPlatLabel(
            "Click the calendar button, or press Alt+Down, to pick a date. Typing is parsed against the " +
            "current culture when the edit is committed - on Enter, when the focus leaves, or when the " +
            "calendar opens - and an entry that cannot be parsed reverts to the last good value. " +
            "Left/Right move between the segments, Up/Down step the segment under the caret, and " +
            "Page Up/Down step the month.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 78f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 6f, 0f, 0f),
        });

        return card;
    }

    /// <summary>
    /// <see cref="XPlatTextBox"/> autocomplete: the three modes over a custom source, the history
    /// source, and a disabled field. The readout follows whichever field was typed in last, so the
    /// completed text can be watched while the suffix is selected.
    /// </summary>
    private XPlatPanel BuildAutoCompleteDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Text box autocomplete"));

        var readout = ScrollCaption("SuggestAppend: type \"ap\" to see \"Apple\" completed and listed");
        card.Controls.Add(readout);

        string[] fruits =
        [
            "Apple", "Apricot", "Banana", "Blackberry", "Blueberry", "Cherry", "Cranberry", "Grape",
            "Lemon", "Lime", "Mango", "Orange", "Peach", "Pear", "Pineapple", "Plum", "Raspberry",
            "Strawberry",
        ];

        var suggest = AutoCompleteBox(XPlatAutoCompleteMode.Suggest, fruits);
        suggest.PlaceholderText = "Type a letter";
        suggest.AddTextChangedHandler(_ => readout.Text = $"Suggest: \"{suggest.Text}\"");
        card.Controls.Add(CaptionRow("Suggest (CustomSource)", suggest));

        var suggestAppend = AutoCompleteBox(XPlatAutoCompleteMode.SuggestAppend, fruits);
        suggestAppend.PlaceholderText = "Type a letter";
        suggestAppend.AddTextChangedHandler(_ => readout.Text =
            $"SuggestAppend: \"{suggestAppend.Text}\" ({suggestAppend.SelectionLength} selected)");
        card.Controls.Add(CaptionRow("SuggestAppend (CustomSource)", suggestAppend));

        var append = AutoCompleteBox(XPlatAutoCompleteMode.Append, fruits);
        append.PlaceholderText = "Type a letter";
        append.AddTextChangedHandler(_ => readout.Text =
            $"Append: \"{append.Text}\" ({append.SelectionLength} selected)");
        card.Controls.Add(CaptionRow("Append (CustomSource)", append));

        var history = new XPlatTextBox
        {
            AutoCompleteMode = XPlatAutoCompleteMode.Suggest,
            AutoCompleteSource = XPlatAutoCompleteSource.HistoryList,
            PlaceholderText = "Enter a value, then type it again",
        };
        history.AddTextChangedHandler(_ => readout.Text = $"HistoryList: \"{history.Text}\"");
        card.Controls.Add(CaptionRow("HistoryList (Suggest)", history));

        var disabled = AutoCompleteBox(XPlatAutoCompleteMode.Suggest, fruits);
        disabled.Enabled = false;
        disabled.Text = "Cannot be suggested";
        card.Controls.Add(CaptionRow("Disabled", disabled));

        card.Controls.Add(new XPlatLabel(
            "Typing opens the list under the caret. Up and Down move the highlight, Enter takes it, " +
            "Escape closes the list without clearing the text, and the box keeps the caret the whole " +
            "time. In the two append modes the rest of the first match is appended and selected, so " +
            "the next keystroke replaces it instead of adding to it.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 66f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 6f, 0f, 0f),
        });

        return card;
    }

    /// <summary>
    /// <see cref="XPlatTextBox"/> with <c>Multiline</c> on: a wrapped field holding more text than
    /// fits, an unwrapped one that scrolls sideways, and a wrapped one with autocomplete. The
    /// readout follows the caret's line and column, so the keyboard can be watched working.
    /// </summary>
    private XPlatPanel BuildMultilineDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Multi-line text"));

        var readout = ScrollCaption("Line 1, column 1 - type, press Enter, then move with the arrows");
        card.Controls.Add(readout);

        var wrapped = new XPlatTextBox
        {
            Dock = XDock.Top,
            Height = 96f,
            Multiline = true,
            PlaceholderText = "Write something",
            Text =
                "A multi-line text box wraps at word boundaries and lays its caret out on the visual " +
                "line it is drawn on rather than on a single logical line.\n\n" +
                "Enter inserts a line break and Ctrl+Enter is left for the form around it, because a " +
                "field that spans lines still needs an accept gesture. Tab still moves the focus. Up " +
                "and Down keep the column they started in, even across a short line, and Home and End " +
                "work on the visual line rather than on the whole value.\n\n" +
                "Drag the pointer across two lines to select them, then Ctrl+C and Ctrl+V: the line " +
                "breaks survive the round trip. The bar on the right appears because this text is " +
                "taller than the field, and the wheel scrolls it.",
        };

        wrapped.AddTextChangedHandler(_ => readout.Text = DescribeCaret(wrapped));
        wrapped.AddKeyUpHandler((_, _) => readout.Text = DescribeCaret(wrapped));
        wrapped.AddMouseUpHandler((_, _) => readout.Text = DescribeCaret(wrapped));
        card.Controls.Add(Caption("Wrapped (WordWrap = true)"));
        card.Controls.Add(wrapped);

        var unwrapped = new XPlatTextBox
        {
            Dock = XDock.Top,
            Height = 62f,
            Multiline = true,
            WordWrap = false,
            Text =
                "WordWrap = false keeps every logical line on one visual line and scrolls the field " +
                "sideways instead, the way a code editor does: this sentence is deliberately long so " +
                "that the horizontal bar at the bottom of the field has somewhere to go when the " +
                "caret reaches the end of it.",
        };

        unwrapped.AddTextChangedHandler(_ => readout.Text = DescribeCaret(unwrapped));
        unwrapped.AddKeyUpHandler((_, _) => readout.Text = DescribeCaret(unwrapped));
        card.Controls.Add(Caption("Unwrapped (WordWrap = false)"));
        card.Controls.Add(unwrapped);

        var completing = new XPlatTextBox
        {
            Dock = XDock.Top,
            Height = 78f,
            Multiline = true,
            AutoCompleteMode = XPlatAutoCompleteMode.SuggestAppend,
            AutoCompleteSource = XPlatAutoCompleteSource.CustomSource,
            PlaceholderText = "Type \"ap\" on any line",
        };

        foreach (string fruit in new[] { "Apple", "Apricot", "Banana", "Blueberry", "Cherry", "Mango", "Peach" })
        {
            completing.AutoCompleteCustomSource.Add(fruit);
        }

        card.Controls.Add(Caption("Autocomplete on a wrapped field"));
        card.Controls.Add(completing);

        card.Controls.Add(new XPlatLabel(
            "The suggestion list hangs from the caret's own line, so it opens where the user is typing " +
            "rather than under the bottom of a tall field. Taking a suggestion replaces the text on that " +
            "line and leaves every other line alone, and the append modes complete nothing while the " +
            "field spans lines, because a suffix cannot cross a line break.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 52f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 8f, 0f, 0f),
        });

        return card;
    }

    /// <summary>A small muted caption sitting above a field.</summary>
    private static XPlatLabel Caption(string text) => new(text)
    {
        Dock = XDock.Top,
        AutoSize = false,
        Height = 20f,
        Font = XPlatTheme.CaptionFont,
        ForeColorRole = XPlatThemeColor.MutedForeground,
        TextAlign = XPlatContentAlignment.BottomLeft,
    };

    /// <summary>The caret's logical line and column, and how much is selected, for the readout.</summary>
    private static string DescribeCaret(XPlatTextBox box)
    {
        var text = box.Text;
        int caret = Math.Clamp(box.CaretIndex, 0, text.Length);
        int line = 0;
        int lineStart = 0;

        for (int i = 0; i < caret; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        int column = caret - lineStart + 1;
        int lines = 1;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                lines++;
            }
        }

        return $"Line {line + 1} of {lines}, column {column} - {text.Length} characters, " +
            $"{box.SelectionLength} selected";
    }

    /// <summary>
    /// <see cref="XPlatTextBox"/> with annotation spans: a single-line field with three marked
    /// words, and a wrapped multi-line field whose longest span crosses a soft wrap so it is drawn
    /// on two visual lines. Right-clicking a squiggle reports the span that was hit, both in the
    /// context menu and in the readout above the fields.
    /// </summary>
    private XPlatPanel BuildSpanDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Text box spans"));

        var readout = ScrollCaption("Right-click a squiggle - the menu and this line name the span that was hit");
        card.Controls.Add(readout);

        const string sentence =
            "The quick brown fox jumpped over the lazzy dog, and a grammer checker noticed.";
        var single = new XPlatTextBox
        {
            Dock = XDock.Top,
            Height = 32f,
            Text = sentence,
            Spans =
            [
                Spelling(sentence, "jumpped"),
                Spelling(sentence, "lazzy"),
                Grammar(sentence, "grammer"),
            ],
        };
        AttachSpanMenu(single, readout);
        card.Controls.Add(Caption("Single line - two spelling spans and one grammar span"));
        card.Controls.Add(single);

        const string paragraph =
            "Spans are caller-owned: the box keeps the list by reference, never edits it, and clamps " +
            "a stale span where it is drawn instead of dropping it. This whole sentence is one " +
            "annotation, so its squiggly underline has to break at the end of the first visual line " +
            "and pick up again at the start of the next one - that is what a wrapped multi-line " +
            "field does, and it is why the marker is drawn once per visual line rather than once per " +
            "annotation.\n" +
            "Teh line below holds a short span of its own.";

        var wrapped = new XPlatTextBox
        {
            Dock = XDock.Top,
            Height = 122f,
            Multiline = true,
            Text = paragraph,
            Spans =
            [
                FromTo(paragraph, "This whole sentence", "a wrapped multi-line"),
                Grammar(paragraph, "Teh"),
            ],
        };
        card.Controls.Add(Caption("Multi-line and wrapped - the long span crosses a soft wrap"));
        card.Controls.Add(wrapped);
        AttachSpanMenu(wrapped, readout);

        card.Controls.Add(new XPlatLabel(
            "A span declares a start index, a length and how its squiggle is drawn - colour, stroke " +
            "weight, wave height and wavelength - and is meant to be subclassed, so a checker can hang " +
            "its suggestion list or rule id on it. Assigning Spans replaces the whole set by reference, " +
            "which is what a checker wants on every keystroke. The box never rewrites a span when the " +
            "text changes: an out-of-range one is clamped where it is drawn and an empty one is " +
            "skipped. The context menu event reports the span the right-click landed in through its " +
            "Span property, correctly after scrolling and after re-wrapping.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 78f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 8f, 0f, 0f),
        });

        return card;
    }

    /// <summary>A span over one word, marked the way a spelling checker marks it.</summary>
    private static XPlatTextBoxSpan Spelling(string text, string word) =>
        new(text.IndexOf(word, StringComparison.Ordinal), word.Length) { Tag = word };

    /// <summary>A span over one word in the accent colour, the way a grammar checker marks it.</summary>
    private static XPlatTextBoxSpan Grammar(string text, string word) =>
        new(text.IndexOf(word, StringComparison.Ordinal), word.Length)
        {
            Tag = word,
            ThemeColor = XPlatThemeColor.Accent,
        };

    /// <summary>A span from the start of one word to the end of another, for a run of text.</summary>
    private static XPlatTextBoxSpan FromTo(string text, string from, string to)
    {
        int start = text.IndexOf(from, StringComparison.Ordinal);
        int end = text.IndexOf(to, StringComparison.Ordinal) + to.Length;

        return new(start, Math.Max(0, end - start)) { Tag = text[start..end] };
    }

    /// <summary>
    /// <see cref="XPlatTextBox"/> with the undo history on. The page is built so the three things
    /// the feature promises can be seen: a burst of typing undoes as one step while a pause splits
    /// it, a replaced selection comes back with the selection it replaced, and the caret and the
    /// multi-line view return to where the edit was made. The readout follows
    /// <c>CanUndo</c>/<c>CanRedo</c> and the two counts, so a keystroke that did or did not join the
    /// run before it is visible.
    /// </summary>
    private XPlatPanel BuildUndoDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Undo and redo"));

        var readout = ScrollCaption("Type, pause for more than five seconds, type again - then Ctrl+Z twice, Ctrl+Y once (Cmd on macOS)");
        card.Controls.Add(readout);

        // Text first, then the history, so the page opens with an empty one and the readout starts
        // at "Undo 0" - the assignment itself would otherwise be the first entry.
        var single = new XPlatTextBox
        {
            Dock = XDock.Top,
            Height = 32f,
            Text = "The quick brown fox",
            UndoEnabled = true,
        };

        _undoFocusTarget = single;

        void Sync()
        {
            readout.Text =
                $"Undo {single.UndoCount} - Redo {single.RedoCount} - caret {single.CaretIndex}, " +
                $"{single.SelectionLength} selected - CanUndo {(single.CanUndo ? "yes" : "no")}, " +
                $"CanRedo {(single.CanRedo ? "yes" : "no")}";
        }

        single.AddTextChangedHandler(_ => Sync());
        single.AddKeyUpHandler((_, _) => Sync());
        single.AddMouseUpHandler((_, _) => Sync());

        // The history tells the page when it changed, which is what a real application would use to
        // enable its Undo and Redo commands - including when the change came from Ctrl+Z rather
        // than from a keystroke the page can see.
        single.AddUndoStateChangedHandler(_ => Sync());
        Sync();

        card.Controls.Add(Caption("Single line - a typing run, a pause, then a replaced selection"));
        card.Controls.Add(single);

        var row = ButtonRow();
        row.Controls.Add(CommandButton("Undo", () =>
        {
            SetStatus(single.Undo()
                ? "Undo applied - the caret and the selection came back with the text."
                : "Nothing to undo.");
            Sync();
        }));
        row.Controls.Add(CommandButton("Redo", () =>
        {
            SetStatus(single.Redo()
                ? "Redo applied - the caret went back to where the change left it."
                : "Nothing to redo.");
            Sync();
        }));
        row.Controls.Add(CommandButton("Select \"brown\"", () =>
        {
            single.Select(single.Text.IndexOf("brown", StringComparison.Ordinal), 5);
            SetStatus("Selected \"brown\" - type over it and undo puts the word, and the selection, back.");
            Sync();
        }));
        row.Controls.Add(CommandButton("Clear history", () =>
        {
            single.ClearUndoStack();
            SetStatus("ClearUndoStack() dropped both sides; the text is untouched.");
            Sync();
        }));
        row.Controls.Add(CommandButton("Lock field", () =>
        {
            single.ReadOnly = !single.ReadOnly;
            SetStatus(single.ReadOnly
                ? "Read-only: the history is kept, but CanUndo and CanRedo report no."
                : "Editable again: the history that was kept is available.");
            Sync();
        }));
        card.Controls.Add(row);

        // The rows are part of the control's own editing block, so a right-click shows them next to
        // Cut, Copy and Paste, and re-opening the menu after an undo is how the page shows Redo
        // lighting up.
        card.Controls.Add(Caption(
            "Right-click the field: Undo and Redo lead the editing block, disabled while the stack " +
            "is empty. Undo from the menu, then open it again - Redo is offered."));

        // A multi-line document: the history spans line breaks like any other character, and an
        // entry carries the view's scroll offsets with it, so undo comes back to the line it left.
        var document = new XPlatTextBox
        {
            Dock = XDock.Top,
            Height = 104f,
            Multiline = true,
            Text =
                "A multi-line field records an entry the same way, with the view's two scroll " +
                "offsets on either side of the change.\n\n" +
                "Type at the end of this line, scroll away with the wheel, then press Ctrl+Z: the " +
                "text loses the characters and the view returns to where they were typed.\n\n" +
                "Undo and redo arrive as ordinary text changes, so anything watching the value - a " +
                "checker, a binding, the autocomplete list - sees them the way it sees typing.\n\n" +
                "- A run survives a pause shorter than five seconds.\n" +
                "- A longer pause starts a new entry.\n" +
                "- Backspace coalesces on a run of its own.\n" +
                "- Typing over a selection is always one entry.\n" +
                "- The caret and the selection travel with the entry.\n" +
                "- Turning Multiline off drops the history.\n\n" +
                "The bar on the right is there because this document is taller than the field, which " +
                "is what makes the restored scroll offset visible.",
            UndoEnabled = true,
        };

        var documentReadout = Caption(DescribeCaret(document) + " - scroll 0");
        card.Controls.Add(documentReadout);
        document.AddTextChangedHandler(_ =>
            documentReadout.Text = $"{DescribeCaret(document)} - scroll {document.VerticalOffset:0}");
        document.AddKeyUpHandler((_, _) =>
            documentReadout.Text = $"{DescribeCaret(document)} - scroll {document.VerticalOffset:0}");
        card.Controls.Add(document);

        // The span contract in one line of code: the box never rewrites the caller's spans, so a
        // checker re-marks on the change - and an undo is a change like any other.
        const string marked = "The quick brown fox jumpped over the lazzy dog.";
        var spelled = new XPlatTextBox
        {
            Dock = XDock.Top,
            Height = 32f,
            Text = marked,
            UndoEnabled = true,
        };
        Remark(spelled);
        spelled.AddTextChangedHandler(_ => Remark(spelled));
        card.Controls.Add(Caption("Spans are re-marked after an undo - the checker runs on the change"));
        card.Controls.Add(spelled);

        var locked = new XPlatTextBox
        {
            Dock = XDock.Top,
            Height = 32f,
            Text = "Read-only: the history is kept, but CanUndo reports no",
            ReadOnly = true,
            UndoEnabled = true,
        };
        card.Controls.Add(Caption("Read-only"));
        card.Controls.Add(locked);

        card.Controls.Add(new XPlatLabel(
            "Each entry holds the caret and the selection on both sides of the change, so undo and " +
            "redo put the insertion point back where the user left it instead of at position 0 or " +
            "at the end. Characters typed within five seconds of the previous one join that " +
            "character's entry; a longer pause starts a new one; Backspace coalesces on a run of its " +
            "own, so typing and deleting never merge; and a change over a non-zero selection is " +
            "always one entry, so undo restores the replaced text rather than un-replacing one " +
            "character at a time. The history keeps the newest 100 entries and a bounded amount of " +
            "text, and it runs no timer: the window is measured from a clock, not from a tick.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 78f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 8f, 0f, 0f),
        });

        return card;
    }

    /// <summary>A button that docks into a <see cref="ButtonRow"/>.</summary>
    private static XPlatButton CommandButton(string text, Action onClick)
    {
        var button = new XPlatButton(text)
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 6f, 0f),
        };

        button.AddClickHandler(_ => onClick());
        return button;
    }

    /// <summary>The words this page's miniature checker marks, standing in for a real one.</summary>
    private static readonly string[] Misspellings = ["jumpped", "lazzy", "grammer", "teh"];

    /// <summary>
    /// Re-computes the squiggles from the current text and hands the list over. This is the whole
    /// of the application's side of the span contract, and it is what makes an undo look right:
    /// the box never edits the list, so the checker assigns a new one whenever the text changes.
    /// </summary>
    private static void Remark(XPlatTextBox box)
    {
        var text = box.Text;
        List<XPlatTextBoxSpan> spans = [];

        foreach (string word in Misspellings)
        {
            int at = text.IndexOf(word, StringComparison.Ordinal);
            if (at >= 0)
            {
                spans.Add(new XPlatTextBoxSpan(at, word.Length) { Tag = word });
            }
        }

        // An empty list removes the feature again, so a corrected field costs nothing.
        box.Spans = spans;
    }

    /// <summary>
    /// Gives one field the right-click behaviour of a text box that carries annotations, the way an
    /// application would write it: the handler is attached to the control, so nothing outside the
    /// control has to know which field was pressed, and it only <em>adds</em> to the menu the box
    /// contributed. The result reads like an editor: the suggestions for the marked run, a
    /// separator, then the ordinary Cut/Copy/Paste/Select All. The page-level menu stays a host
    /// concern because it belongs to the window rather than to any one control.
    /// </summary>
    private void AttachSpanMenu(XPlatTextBox box, XPlatLabel readout)
    {
        box.AddContextMenuOpeningHandler(args =>
        {
            if (args.Span is not { } span || span.Tag is not string marked)
            {
                readout.Text = "The press missed every span - Span was null, so only the editing commands are offered";
                return;
            }

            string word = marked.Length <= 24 ? marked : marked[..21] + "...";
            readout.Text = $"Right-click hit the span on \"{word}\" - start {span.Start}, length {span.Length}";

            var menu = args.Menu ??= new XPlatContextMenu();

            // In front of the editing block, in reading order, then one separator between the two.
            menu.Insert(0, new XPlatMenuItem("&Add to dictionary", () => SetStatus($"\"{word}\" added to the dictionary.")));
            menu.Insert(0, new XPlatMenuItem($"&Ignore \"{word}\"", () => SetStatus($"\"{word}\" added to the ignore list.")));
            menu.Insert(0, new XPlatMenuItem($"&Replace \"{word}\"", () => SetStatus($"Replace picked for \"{word}\".")));
            menu.Insert(3, XPlatMenuItem.Separator);
        });
    }

    /// <summary>
    /// Right-clicks a squiggle on the spans page as if the pointer had gone there, which is what a
    /// screenshot needs in order to show the context menu - and the readout - naming the span. It
    /// goes through the same host entry point the platform uses, so it proves the dispatch, not a
    /// shortcut around it.
    /// </summary>
    /// <param name="window">The window the sample is showing in.</param>
    public void OpenSpanDemo(XPlatWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        // Nothing has painted yet, so the page has no bounds until something lays it out.
        window.Host.PerformLayout();

        if (FindSpannedBox(Root) is not { Spans: { Count: > 0 } spans } box)
        {
            return;
        }

        // The last visual-line segment rather than the first, so the picture proves that a span
        // which crosses a soft wrap still resolves from the line it wrapped onto.
        var rects = new List<SKRect>();
        box.GetSelectionRects(spans[0].Start, spans[0].Length, rects);
        if (rects.Count == 0)
        {
            return;
        }

        var segment = rects[^1];
        var origin = box.GetPositionInHost();
        window.Host.ProcessContextMenu(new SKPoint(origin.X + segment.MidX, origin.Y + segment.MidY));
    }

    /// <summary>
    /// The field the screenshot path presses in: the wrapped one when the page has one, so a span
    /// that crosses a soft wrap is the one that gets resolved, and any annotated field otherwise.
    /// </summary>
    private static XPlatTextBox? FindSpannedBox(XPlatControl control)
    {
        var found = Search(control, multilineOnly: true) ?? Search(control, multilineOnly: false);
        return found;

        static XPlatTextBox? Search(XPlatControl node, bool multilineOnly)
        {
            if (node is XPlatTextBox { Spans: { Count: > 0 } } box && (!multilineOnly || box.Multiline))
            {
                return box;
            }

            foreach (var child in node.Controls)
            {
                if (Search(child, multilineOnly) is { } match)
                {
                    return match;
                }
            }

            return null;
        }
    }

    /// <summary>A text box over a custom source, with the values the suggestion list filters.</summary>
    private static XPlatTextBox AutoCompleteBox(XPlatAutoCompleteMode mode, IEnumerable<string> values)
    {
        var box = new XPlatTextBox
        {
            AutoCompleteMode = mode,
            AutoCompleteSource = XPlatAutoCompleteSource.CustomSource,
        };

        foreach (string value in values)
        {
            box.AutoCompleteCustomSource.Add(value);
        }

        return box;
    }

    /// <summary>A caption with a wide field on the right, so a column of fields lines up.</summary>
    private static XPlatPanel CaptionRow(string caption, XPlatControl field)
    {
        var row = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 34f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
            Margin = new XPlatThickness(0f, 0f, 0f, 10f),
        };

        row.Controls.Add(new XPlatLabel(caption)
        {
            Dock = XDock.Fill,
            AutoSize = false,
            Font = XPlatTheme.DefaultFont,
            TextAlign = XPlatContentAlignment.MiddleLeft,
            Margin = new XPlatThickness(0f, 0f, 12f, 0f),
        });

        field.Dock = XDock.Right;
        field.Width = 240f;
        row.Controls.Add(field);

        return row;
    }

    /// <summary>
    /// A tab control whose pages hold real controls, plus a second one with more tabs than fit so
    /// the strip has to scroll.
    /// </summary>
    private XPlatPanel BuildTabControlDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Tab controls"));

        var readout = ScrollCaption("Selected: General");
        card.Controls.Add(readout);

        var tabs = new XPlatTabControl
        {
            Dock = XDock.Top,
            Height = 220f,
            Margin = new XPlatThickness(0f, 0f, 0f, 16f),
        };

        var general = new XPlatTabPage("General");
        general.Controls.Add(new XPlatCheckBox("Show the welcome screen")
        {
            Dock = XDock.Top,
            Checked = true,
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
        });
        general.Controls.Add(new XPlatCheckBox("Reopen the last page")
        {
            Dock = XDock.Top,
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
        });
        general.Controls.Add(new XPlatLabel("Pages are ordinary panels: they dock, scroll and lay out like any other.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        var appearance = new XPlatTabPage("Appearance");
        appearance.Controls.Add(new XPlatComboBox
        {
            Dock = XDock.Top,
            Height = 32f,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        });
        appearance.Controls.Add(new XPlatProgressBar
        {
            Dock = XDock.Top,
            Height = 18f,
            Value = 45f,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        });
        appearance.Controls.Add(DemoButton("A button on a page", () => SetStatus("A button on the Appearance page.")));

        var advanced = new XPlatTabPage("Advanced");
        advanced.Controls.Add(new XPlatTextBox { Dock = XDock.Top, Height = 32f, Margin = new XPlatThickness(0f, 0f, 0f, 8f) });
        advanced.Controls.Add(new XPlatLabel("Tab and the arrow keys move between pages.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        tabs.TabPages.Add(general);
        tabs.TabPages.Add(appearance);
        tabs.TabPages.Add(advanced);
        tabs.SelectedIndex = 0;
        tabs.AddSelectedIndexChangedHandler(_ => readout.Text = $"Selected: {tabs.SelectedTab?.Text ?? "none"}");
        card.Controls.Add(tabs);

        card.Controls.Add(ScrollCaption("More tabs than fit, so the strip scrolls with the wheel"));

        var crowded = new XPlatTabControl
        {
            Dock = XDock.Fill,
            Margin = new XPlatThickness(0f, 0f, 0f, 0f),
        };

        foreach (string name in new[]
                 {
                     "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight",
                     "Nine", "Ten", "Eleven", "Twelve",
                 })
        {
            var page = new XPlatTabPage(name);
            page.Controls.Add(new XPlatLabel($"Page {name}")
            {
                Dock = XDock.Fill,
                AutoSize = false,
                TextAlign = XPlatContentAlignment.MiddleCenter,
            });
            crowded.TabPages.Add(page);
        }

        card.Controls.Add(crowded);
        return card;
    }

    /// <summary>
    /// One <see cref="XPlatToolTip"/> shared by every control on the card, which is the shape the
    /// Windows Forms component has: the tooltip is not a child of anything, it follows the pointer.
    /// </summary>
    private XPlatPanel BuildToolTipDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Tool tips"));

        var toolTip = new XPlatToolTip { InitialDelay = 400, AutoPopDelay = 5000 };

        card.Controls.Add(ScrollCaption("Hover a control to see its tip"));

        var primary = new XPlatButton("Save")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = XPlatButtonStyle.Primary,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        };
        toolTip.SetToolTip(primary, "Write the document to disk.");
        card.Controls.Add(primary);

        var secondary = new XPlatButton("Save as")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        };
        toolTip.SetToolTip(secondary, "Write the document to a new file.");
        card.Controls.Add(secondary);

        var longTip = new XPlatButton("A long tip")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = XPlatButtonStyle.Ghost,
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        };

        // The tooltip measures itself and wraps at MaximumWidth, so a long string stays readable
        // instead of becoming one very wide line.
        toolTip.SetToolTip(longTip, "This tip is long enough that it has to wrap onto more than one line, which is what MaximumWidth is for.");
        card.Controls.Add(longTip);

        var field = new XPlatTextBox
        {
            Dock = XDock.Top,
            Height = 32f,
            Text = "Hover me",
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        };
        toolTip.SetToolTip(field, "Any control can carry a tip, not only buttons.");
        card.Controls.Add(field);

        var toggled = new XPlatCheckBox("Disable the tips")
        {
            Dock = XDock.Top,
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        };
        toggled.AddCheckedChangedHandler(_ => toolTip.Active = !toggled.Checked);
        card.Controls.Add(toggled);

        card.Controls.Add(new XPlatLabel(
            "A tip is shown in a popup that never takes the pointer, so the control under it keeps the hover.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>Shows <paramref name="message"/> in the footer's status line.</summary>
    /// <param name="message">The text to show.</param>
    /// <remarks>
    /// Public so a host can report something the sample UI did not cause itself, such as a second
    /// launch being handed to the running window.
    /// </remarks>
    /// <summary>
    /// Publishes the live presentation path for the footer and the measured-rendering page. The
    /// backend only knows it once the window really exists, so the harness calls this from
    /// <see cref="XPlatWindow.AddShownHandler"/> rather than before <c>Show</c>.
    /// </summary>
    /// <param name="path"><c>gl</c> or <c>cpu (...)</c>, from the Win32 backend.</param>
    /// <param name="renderer">The vendor and renderer string, or null on the CPU path.</param>
    public void SetGraphicsInfo(string path, string? renderer)
    {
        _graphicsPath = path;
        _graphicsRenderer = renderer;

        if (_gfxLabel is not null)
        {
            _gfxLabel.Text = string.IsNullOrWhiteSpace(renderer)
                ? $"gfx: {path}"
                : $"gfx: {path} - {renderer}";
            _gfxLabel.Visible = true;

            // Toggling visibility does not re-run layout - the control explains why in
            // XPlatControl.OnChildVisibleChanged - so the footer has to be laid out explicitly or
            // the newly visible label keeps the zero-width slot it had while hidden.
            _gfxLabel.Parent?.PerformLayout();
        }
    }

    /// <summary>The live scale factor, for the page's readout; the harness supplies it from the window.</summary>
    public void SetScaleProvider(Func<float> scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        _scaleProvider = scale;
    }

    /// <summary>
    /// Starts the measured-rendering page's benchmark once that page is displayed. This is what
    /// <c>--benchmark</c> calls, so a script can capture the BENCH line without a pointer; the
    /// button on the page is the same entry point.
    /// </summary>
    /// <param name="partialDamage">
    /// True to measure the partial-damage row. The <c>--partial-damage</c> switch passes this so a
    /// script can capture all four rows without a pointer, exactly as the on-page toggle does.
    /// </param>
    public void StartBenchmark(bool partialDamage = false)
    {
        if (_metricsPage is { IsDisplayed: true })
        {
            if (partialDamage && _metricsFullDamage)
            {
                ToggleDamage();
            }

            StartMeasurement();
            return;
        }

        StartPageBenchmark();
    }

    /// <summary>
    /// The same 630-frame measurement on whatever page is showing, for pages whose content is
    /// heavier than the measured-rendering page's own swatch. The page is invalidated in full on
    /// every tick, and the frame time is still <see cref="XPlatUiHost.LastFrameTime"/>.
    /// </summary>
    private void StartPageBenchmark()
    {
        if (_measuring)
        {
            return;
        }

        var host = Root.Host ?? _metricsHost;
        if (host is null)
        {
            // Not attached yet; the Shown handler that calls this runs before the first layout on
            // some backends, so say so rather than silently measuring nothing.
            Console.Error.WriteLine("BENCH skipped: the window is not attached to a host yet");
            return;
        }

        _measuring = true;
        _metricsFullDamage = true;
        _measureFrame = 0;
        _measureFirst = 0;
        _measureLast = 0;
        _measureLastArea = SKRectI.Empty;
        _measureSamples.Clear();
        _pageBenchmarkHost = host;

        if (!_pageBenchmarkHandlerAdded)
        {
            host.AddFramePaintedHandler(_ => OnMetricsFramePainted(host));
            _pageBenchmarkHandlerAdded = true;
        }

        if (_pageBenchmarkTimer is null)
        {
            _pageBenchmarkTimer = new XPlatTimer(TimeSpan.FromMilliseconds(15.0));
            _pageBenchmarkTimer.AddTickHandler(_ => OnPageBenchmarkTick());
        }

        _pageBenchmarkTimer.Start();

        if (_metricsReadout is not null)
        {
            _metricsReadout.Text = $"measuring {BenchmarkMeasuredFrames} frames after {BenchmarkWarmupFrames} warm-up...";
        }

        SetStatus("Benchmark running - the page is invalidated in full on every tick.");

        StartBenchmarkWatchdog();
    }

    private void OnPageBenchmarkTick()
    {
        if (!_measuring)
        {
            _pageBenchmarkTimer?.Stop();
            return;
        }

        (_pageBenchmarkHost ?? Root.Host)?.Invalidate();
    }

    public void SetStatus(string message) => _statusLabel.Text = message;

    /// <summary>
    /// Reports a drop on the window itself: the files that were dragged in from the desktop, or the
    /// text that was dragged in from another application.
    /// </summary>
    private void OnRootDrop(XPlatDragEventArgs e)
    {
        var files = e.Data.GetFiles();
        if (files.Length > 0)
        {
            var names = string.Join(", ", files.Select(Path.GetFileName));
            SetStatus($"Dropped {files.Length} file(s): {names}");
            e.Effect = XPlatDragDropEffects.Copy;
            return;
        }

        var text = e.Data.GetText();
        if (!string.IsNullOrEmpty(text))
        {
            SetStatus($"Dropped text: {Preview(text)}");
            e.Effect = XPlatDragDropEffects.Copy;
            return;
        }

        SetStatus("Dropped something this sample cannot read.");
        e.Effect = XPlatDragDropEffects.None;
    }

    /// <summary>Shortens dropped text to one status-line's worth.</summary>
    private static string Preview(string text)
    {
        var single = text.ReplaceLineEndings(" ");
        return single.Length <= 60 ? single : string.Concat(single.AsSpan(0, 60), "...");
    }

    /// <summary>
    /// Encodes the gradient the images page shows. A PNG rather than a picture: the page is meant to
    /// exercise the bitmap path an application takes, and Phase 1 has to leave that page looking
    /// exactly as it did.
    /// </summary>
    private static MemoryStream CreateGradientPng(int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(0f, 0f),
                   new SKPoint(width, height),
                   new[] { new SKColor(0xFF2F6FED), new SKColor(0xFF7C3AED), new SKColor(0xFFEC4899) },
                   SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader, IsAntialias = true })
        {
            canvas.DrawRect(new SKRect(0f, 0f, width, height), paint);
        }

        using (var paint = new SKPaint { Color = new SKColor(0x33FFFFFF), IsAntialias = true })
        {
            canvas.DrawCircle(width * 0.22f, height * 0.3f, height * 0.28f, paint);
            canvas.DrawCircle(width * 0.78f, height * 0.72f, height * 0.36f, paint);
        }

        using (var font = new SKFont(SKTypeface.Default, 30f) { Edging = SKFontEdging.Antialias })
        using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.DrawText("SkiaSharp", 22f, height - 26f, SKTextAlign.Left, font, paint);
        }

        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// A face that travels with the application instead of coming from the machine: every control on
    /// this page draws with IBM Plex Sans Regular read out of the assembly's resources, and the
    /// readout compares its measurements with the machine's own face.
    /// </summary>
    private XPlatPanel BuildFontDemo()
    {
        const string sample = "XPlatUI";

        // The page owns these fonts for the life of the window: a control never disposes a font it was
        // handed, and the theme fonts are held the same way.
        var loaded = XPlatFont.FromBytes(DemoFont.Data);
        var system = new XPlatFont(loaded.Size);

        var card = Card();
        card.Controls.Add(Heading("Fonts from a file"));
        card.Controls.Add(ScrollCaption(
            $"{loaded.FamilyName}, {F(loaded.Size)} pt, read from this app's embedded resources - not a family this machine has"));

        card.Controls.Add(Caption("A label"));
        card.Controls.Add(new XPlatLabel(
            "This label draws with the face in the file. Its glyph advances, line height and baseline " +
            "are the same on Windows, macOS and Linux, which a family name cannot promise: the same name " +
            "resolves to a different physical face on each desktop.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 62f,
            WrapText = true,
            Font = loaded,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        card.Controls.Add(Caption("A multi-line text box"));
        card.Controls.Add(new XPlatTextBox
        {
            Dock = XDock.Top,
            Height = 108f,
            Multiline = true,
            Font = loaded,
            Text =
                "The caret, the wrap points and the selection geometry all come from the same face, so a " +
                "test that pins this file sees the same layout on every platform.\n\n" +
                "Nothing is looked up by name here: the bytes are the face. That is what makes a golden " +
                "image possible, and what lets a trimmed application carry its own brand.",
        });

        card.Controls.Add(Caption("A rich label: spans change size and colour and stay on this face"));
        var rich = new XPlatRichLabel
        {
            Dock = XDock.Top,
            AutoSize = true,
            WrapText = true,
            Font = loaded,
        };

        rich.AddText("The same face in ");
        rich.AddSpan(new XPlatTextSpan("a larger size") { FontSize = 17f });
        rich.AddText(", in ");
        rich.AddSpan(new XPlatTextSpan("the accent colour") { ThemeColor = XPlatThemeColor.Accent });
        rich.AddText(", and in ");
        rich.AddSpan(new XPlatTextSpan("a bold span") { Bold = true });
        rich.AddText(
            " - which stays this face rather than falling back to a system font, because one file holds " +
            "one face and no weight is invented.");
        card.Controls.Add(rich);

        card.Controls.Add(new XPlatLabel(
            $"Measured here: \"{sample}\" is {F(loaded.MeasureWidth(sample))} px in the loaded face and " +
            $"{F(system.MeasureWidth(sample))} px in the system face ({system.FamilyName}); line height " +
            $"{F(loaded.LineHeight)} against {F(system.LineHeight)}.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 10f, 0f, 0f),
        });

        return card;

        static string F(float value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The inherited appearance page: one panel that assigns a font and a foreground, what its
    /// descendants make of it, one control that overrides both inside the subtree, and a label that
    /// paints nothing on top of an ancestor that does. The buttons change the appearance of a live
    /// tree - the panel's font, or the whole theme's face - and the text re-lays itself out.
    /// </summary>
    private XPlatPanel BuildAppearanceDemo()
    {
        // The page owns these for the life of the window: a control never disposes a font it was
        // handed, and the theme keeps the face it is given.
        _appearanceSubtreeFont ??= XPlatFont.FromBytes(DemoFont.Data, 14f);
        _appearanceBigFont ??= XPlatFont.FromBytes(DemoFont.Data, 21f);
        _appearanceFace ??= XPlatFont.FromBytes(DemoFont.Data);

        var card = Card();
        card.Controls.Add(Heading("Inherited appearance"));
        card.Controls.Add(new XPlatLabel(
            "Font, foreground and background are inherited down the tree, the way they are in WinForms. " +
            "A control that assigns one answers for itself and for everything inside it; one that " +
            "assigns none asks its parent and then falls back to its class default. Nothing is captured " +
            "when a control is built, so changing the theme - or one panel's font - moves a live tree.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 56f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 10f),
        });

        var buttons = ButtonRow();
        buttons.Height = 32f;

        var grow = new XPlatButton("Grow the panel's font")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        grow.AddClickHandler(_ => SetAppearanceDemo(size: 21f));

        var themeFace = new XPlatButton("Use the bundled face everywhere")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        themeFace.AddClickHandler(_ =>
        {
            XPlatTheme.UseFont(_appearanceFace);
            UpdateAppearanceReadout();
        });

        var reset = new XPlatButton("Reset")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
        };
        reset.AddClickHandler(_ =>
        {
            _appearanceStage?.ClearFont();
            XPlatTheme.FontFamily = null;
            UpdateAppearanceReadout();
        });

        buttons.Controls.Add(grow);
        buttons.Controls.Add(themeFace);
        buttons.Controls.Add(reset);
        card.Controls.Add(buttons);

        _appearanceReadout = new XPlatLabel
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 62f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        };
        card.Controls.Add(_appearanceReadout);

        // The subtree: a panel that assigns a font, a foreground colour and a background role. Every
        // control added below inherits all three unless it says otherwise.
        var stage = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 214f,
            Padding = new XPlatThickness(12f, 8f),
            CornerRadius = 8f,
            BorderWidth = 1f,
            Font = _appearanceSubtreeFont,
            Foreground = XPlatTheme.Accent,
            Background = XPlatBackground.FromThemeColor(XPlatThemeColor.Secondary),
        };
        _appearanceStage = stage;

        var inherited = new XPlatLabel("Inherits the panel's font, colour and background role.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 34f,
        };
        _appearanceInherited = inherited;

        // A panel that paints a background of its own and assigns nothing: it takes the colour its
        // parent assigned instead of its own surface, which is the inheritance doing its work.
        var nested = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 76f,
            Padding = new XPlatThickness(8f, 6f),
            CornerRadius = 6f,
            BorderWidth = 1f,
        };
        _appearanceNested = nested;

        var noBackground = new XPlatLabel(
            "This label paints nothing at all, so the colour behind it - the panel's, inherited one " +
            "level up - is what shows. Its background resolves to None, not to a colour of its own.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            TextAlign = XPlatContentAlignment.TopLeft,
        };
        _appearanceLabel = noBackground;
        nested.Controls.Add(noBackground);

        // The one control that overrides the subtree it sits in.
        var overrides = new XPlatLabel("This one assigns its own font and colour.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 24f,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
        };
        _appearanceOverrides = overrides;

        var button = new XPlatButton("A button under the same panel")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = XPlatButtonStyle.Ghost,
            Margin = new XPlatThickness(0f, 6f, 0f, 0f),
        };

        stage.Controls.Add(inherited);
        stage.Controls.Add(nested);
        stage.Controls.Add(overrides);
        stage.Controls.Add(button);
        card.Controls.Add(stage);

        card.Controls.Add(new XPlatLabel(
            "Three background states, because two would be wrong: unset means \"ask my parent\", a " +
            "colour answers for this control and everything under it, and \"paint nothing\" is a value " +
            "of its own - a label keeps painting nothing wherever it sits, so it never gains a box it " +
            "did not ask for. Only a colour is inherited: a layout container that paints nothing must " +
            "not take the card inside it away with it.\n\n" +
            "Nothing here is captured at construction. The effective font is read when text is " +
            "measured and painted; what the parent chain contributes is walked once and cached, and " +
            "dropped when an ancestor assigns a value or a control changes parent. Palette roles are " +
            "resolved at paint time, so a palette swap is still just a repaint - but a font is the " +
            "size of the text, so a font change lays the tree out again before the next frame.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 12f, 0f, 0f),
        });

        UpdateAppearanceReadout();
        return card;
    }

    /// <summary>
    /// Changes the font of the panel the demo subtree hangs off, which is the runtime half of the
    /// page: the controls already exist, and their text is measured again at the new size.
    /// </summary>
    /// <param name="size">The size to give the subtree, or null to let it inherit again.</param>
    public void SetAppearanceDemo(float? size)
    {
        if (!size.HasValue)
        {
            _appearanceStage?.ClearFont();
        }
        else if (_appearanceStage is not null && _appearanceBigFont is not null)
        {
            _appearanceStage.Font = size.Value >= 21f ? _appearanceBigFont : _appearanceSubtreeFont!;
        }

        UpdateAppearanceReadout();
    }

    /// <summary>Switches the application's font to the bundled face, or back to the system's.</summary>
    /// <param name="bundled">True for the face the application carries, false for the platform's.</param>
    public void SetAppearanceDemoFace(bool bundled)
    {
        if (bundled && _appearanceFace is not null)
        {
            XPlatTheme.UseFont(_appearanceFace);
        }
        else
        {
            XPlatTheme.FontFamily = null;
        }

        UpdateAppearanceReadout();
    }

    /// <summary>
    /// Prints what the tree resolves to right now: the background state of each level, the font size
    /// the labels ended up with, and the size of the text - which is what changes when a font does.
    /// </summary>
    private void UpdateAppearanceReadout()
    {
        if (_appearanceReadout is null)
        {
            return;
        }

        _appearanceReadout.Text =
            $"panel: {Describe(_appearanceStage)}   |   " +
            $"inherited label: {Describe(_appearanceInherited)}   |   " +
            $"nested panel: {Describe(_appearanceNested)}\n" +
            $"label that paints nothing: {Describe(_appearanceLabel)}\n" +
            $"override inside the subtree: {Describe(_appearanceOverrides)}   |   " +
            $"inherited label text: {S(_appearanceInherited is { } label ? label.Font.MeasureWidth(label.Text) : null)} px wide, " +
            $"{S(_appearanceInherited?.Font.LineHeight)} px line height";

        static string Describe(XPlatControl? control)
        {
            if (control is null)
            {
                return "-";
            }

            string background = control.Background switch
            {
                { IsNone: true } => "none",
                { ThemeColor: { } role } => role.ToString(),
                { Color: { } color } => $"#{color.Alpha:X2}{color.Red:X2}{color.Green:X2}{color.Blue:X2}",
                _ => "inherit",
            };

            return $"{control.Font.Size:0.#} pt, {background}";
        }

        static string S(float? value) =>
            value is { } v ? v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "-";
    }

    /// <summary>
    /// A page whose only job is to be checked by hand. Creation order and <c>TabIndex</c> order
    /// disagree on purpose, a nested container orders its own children, a focusable container is
    /// entered as a unit, two controls are interleaved by index but meant to be skipped, two share
    /// an index, and the readout names whatever has the focus so a person can Tab and read the
    /// order rather than guess it.
    /// </summary>
    private XPlatPanel BuildTabOrderDemo()
    {
        var section = new XPlatPanel
        {
            Dock = XDock.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };

        // The stops in the order Tab should reach them, filled in after the form is built. The
        // readout reads this list, so if the walk ever disagrees with it the page says "it is meant
        // to be skipped" instead of quietly describing whatever the library did.
        var expected = new List<(XPlatControl Control, string Name)>();
        var skipped = new List<(XPlatControl Control, string Name)>();

        var readout = new XPlatLabel("Focus: nothing yet - press Tab to land on 1 Country.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 20f,
            TextAlign = XPlatContentAlignment.MiddleLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
        };

        void Refresh()
        {
            foreach (var (control, name) in skipped)
            {
                if (control.IsFocused)
                {
                    readout.Text = $"Focus: {name} - it is meant to be skipped!";
                    return;
                }
            }

            for (int i = 0; i < expected.Count; i++)
            {
                if (expected[i].Control.IsFocused)
                {
                    readout.Text =
                        $"Focus: {expected[i].Name} - TabIndex {expected[i].Control.TabIndex} - " +
                        $"tab stop {i + 1} of {expected.Count}";
                    return;
                }
            }

            readout.Text = "Focus: nothing on this page - press Tab to land on 1 Country.";
        }

        void Watch(XPlatControl control, string name, bool isSkipped = false)
        {
            (isSkipped ? skipped : expected).Add((control, name));
            control.AddGotFocusHandler(_ => Refresh());
            control.AddLostFocusHandler(_ => Refresh());
        }

        // A label and its control on one line, both placed by hand. A docked row panel would be a
        // container of its own, and its index - not the control's - would decide the order, which is
        // exactly the thing this page exists to show correctly.
        static void Place(XPlatPanel parent, string caption, XPlatControl control, float y)
        {
            parent.Controls.Add(new XPlatLabel(caption)
            {
                AutoSize = false,
                Bounds = new SKRect(0f, y, 330f, y + 28f),
                Font = XPlatTheme.CaptionFont,
                ForeColorRole = XPlatThemeColor.MutedForeground,
                TextAlign = XPlatContentAlignment.MiddleLeft,
            });

            control.Bounds = new SKRect(338f, y, 628f, y + 28f);
            parent.Controls.Add(control);
        }

        var form = Card();
        form.Dock = XDock.Left;
        form.Width = 660f;
        form.Margin = new XPlatThickness(0f, 0f, 16f, 0f);
        form.Controls.Add(Heading("Tab order"));
        form.Controls.Add(new XPlatLabel(
            "Press Tab repeatedly and watch the readout - and Shift+Tab to walk the same order " +
            "backwards. The order is Country, Street, Check address, Email, Standard shipping, Full " +
            "name, the scrollable container, Inside the container, Save. That is not the order these " +
            "controls were created in: they were created top to bottom, and Tab follows TabIndex. " +
            "After Save the walk leaves the page for the window's own controls and wraps back to Country.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 60f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
        });
        form.Controls.Add(readout);

        // Every stop is a direct child of this panel, placed by hand: layout must not invent
        // containers that the Tab walk would then have to order.
        var rows = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 436f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };
        form.Controls.Add(rows);

        // Created first, visited sixth.
        var fullName = new XPlatTextBox { TabIndex = 30 };
        Place(rows, "Created 1st - tab 6 - Full name (TabIndex 30)", fullName, 0f);

        // Created second, visited first.
        var country = new XPlatComboBox { TabIndex = 10 };
        country.Items.Add("United Kingdom");
        country.Items.Add("United States");
        country.Items.Add("Germany");
        Place(rows, "Created 2nd - tab 1 - Country (TabIndex 10)", country, 34f);

        // Equal indices: this one is created before the radio button, so the tie-break puts it first.
        var email = new XPlatCheckBox("Email me a receipt") { AutoSize = false, TabIndex = 20 };
        Place(rows, "Created 3rd - tab 4 - Email (TabIndex 20, tie)", email, 68f);

        var standard = new XPlatRadioButton("Standard shipping") { AutoSize = false, TabIndex = 20 };
        Place(rows, "Created 4th - tab 5 - Standard (TabIndex 20, tie)", standard, 102f);

        // A nested container: not a stop itself, so it takes the place its own index gives it in the
        // outer order (15) and its children order among themselves.
        var addressPanel = new XPlatPanel
        {
            TabIndex = 15,
            Bounds = new SKRect(0f, 136f, 628f, 232f),
            IsCard = true,
            ShowFrame = true,
            BorderWidth = 1f,
            CornerRadius = 8f,
            Padding = new XPlatThickness(8f),
        };
        rows.Controls.Add(addressPanel);
        addressPanel.Controls.Add(new XPlatLabel(
            "Created 5th - TabIndex 15 - a nested container: its children order among themselves, 10 then 20.")
        {
            AutoSize = false,
            Bounds = new SKRect(0f, 0f, 612f, 18f),
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        // Created in the reverse of the order they are visited, so the container's own indices - not
        // creation order - explain the result.
        var checkAddress = new XPlatButton("Check address") { AutoSize = false, TabIndex = 20 };
        var street = new XPlatTextBox { TabIndex = 10 };

        addressPanel.Controls.Add(new XPlatLabel("Created 5a - tab 3 - Check address (TabIndex 20)")
        {
            AutoSize = false,
            Bounds = new SKRect(0f, 22f, 300f, 50f),
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.MiddleLeft,
        });
        checkAddress.Bounds = new SKRect(308f, 22f, 540f, 50f);
        addressPanel.Controls.Add(checkAddress);

        addressPanel.Controls.Add(new XPlatLabel("Created 5b - tab 2 - Street (TabIndex 10)")
        {
            AutoSize = false,
            Bounds = new SKRect(0f, 56f, 300f, 84f),
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.MiddleLeft,
        });
        street.Bounds = new SKRect(308f, 56f, 540f, 84f);
        addressPanel.Controls.Add(street);

        // Created here, but their indices put them between real stops - and they are skipped, which
        // is the point. One line so the page still fits above the fold.
        var notATabStop = new XPlatButton("Not a tab stop") { AutoSize = false, TabStop = false, TabIndex = 25 };
        var disabled = new XPlatButton("Disabled") { AutoSize = false, Enabled = false, TabIndex = 45 };
        rows.Controls.Add(new XPlatLabel("Created 6th and 7th - never - skipped (TabIndex 25, 45)")
        {
            AutoSize = false,
            Bounds = new SKRect(0f, 236f, 330f, 264f),
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.MiddleLeft,
        });
        notATabStop.Bounds = new SKRect(338f, 236f, 470f, 264f);
        disabled.Bounds = new SKRect(478f, 236f, 610f, 264f);
        rows.Controls.Add(notATabStop);
        rows.Controls.Add(disabled);

        // A focusable container: a tab stop of its own, and its children are walked straight after it.
        rows.Controls.Add(new XPlatLabel(
            "Created 8th - tabs 7 and 8 - a scrollable panel that is itself a tab stop (TabIndex 40): " +
            "Tab lands on the container, and its child comes straight after it with TabIndex 0.")
        {
            AutoSize = false,
            Bounds = new SKRect(0f, 268f, 628f, 298f),
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        var scroller = new FocusRingPanel
        {
            TabIndex = 40,
            TabStop = true,
            Bounds = new SKRect(0f, 300f, 628f, 396f),
            VerticalScroll = XPlatScrollStyle.Automatic,
            IsCard = true,
            ShowFrame = true,
            BorderWidth = 1f,
            CornerRadius = 8f,
            Padding = new XPlatThickness(8f),
        };
        rows.Controls.Add(scroller);

        for (int i = 0; i < 4; i++)
        {
            scroller.Controls.Add(new XPlatLabel($"Filler row {i + 1} - the wheel scrolls this container")
            {
                AutoSize = false,
                Bounds = new SKRect(4f, 38f + (i * 22f), 420f, 58f + (i * 22f)),
                Font = XPlatTheme.CaptionFont,
                ForeColorRole = XPlatThemeColor.MutedForeground,
            });
        }

        var inside = new XPlatButton("Inside the container") { AutoSize = false, TabIndex = 0 };
        inside.Bounds = new SKRect(4f, 4f, 260f, 30f);

        // The rows below the fold are what make the panel scroll, and focusing one is the page's
        // demonstration of what an application does with a scrolling panel.
        inside.AddGotFocusHandler(_ => scroller.ScrollControlIntoView(inside));
        scroller.Controls.Add(inside);

        // Created last, visited last: the one row whose creation and tab position agree.
        var save = new XPlatButton("Save") { AutoSize = false, Style = XPlatButtonStyle.Primary, TabIndex = 50 };
        Place(rows, "Created 9th - tab 9 - Save (TabIndex 50)", save, 408f);

        // The order this page expects, written in the order the walk should produce it. Nothing
        // else on the page derives from this list.
        Watch(country, "Country");
        Watch(street, "Street");
        Watch(checkAddress, "Check address");
        Watch(email, "Email me a receipt");
        Watch(standard, "Standard shipping");
        Watch(fullName, "Full name");
        Watch(scroller, "the scrollable container");
        Watch(inside, "Inside the container");
        Watch(save, "Save");
        Watch(notATabStop, "Not a tab stop", isSkipped: true);
        Watch(disabled, "Disabled", isSkipped: true);

        var notes = Card();
        notes.Controls.Add(Heading("What the order shows"));
        notes.Controls.Add(Caption("Click any control and press Tab: the walk continues from there."));
        notes.Controls.Add(Note(
            "Creation order is top to bottom; Tab follows TabIndex, so it starts at Country, the " +
            "lowest index, not at the first control created."));
        notes.Controls.Add(Note(
            "Per container: the address panel's children order among themselves (10, then 20), " +
            "where the panel's own index, 15, puts them."));
        notes.Controls.Add(Note(
            "Equal indices keep tree order: the check box and the radio button are both 20, and the " +
            "check box was created first."));
        notes.Controls.Add(Note(
            "A focused container is one step: Tab stops on the scrollable panel (40), then enters " +
            "it - the child at index 0 - then continues to Save."));
        notes.Controls.Add(Note(
            "Not a tab stop (25) and Disabled (45) sit between real stops by index and never take " +
            "focus; the readout says so if one does."));

        section.Controls.Add(form);
        section.Controls.Add(notes);
        return section;
    }

    /// <summary>A caption block on the Tab order page, one paragraph of explanation.</summary>
    private static XPlatLabel Note(string text) => new(text)
    {
        Dock = XDock.Top,
        AutoSize = false,
        Height = 66f,
        WrapText = true,
        Font = XPlatTheme.CaptionFont,
        ForeColorRole = XPlatThemeColor.MutedForeground,
        TextAlign = XPlatContentAlignment.TopLeft,
        Margin = new XPlatThickness(0f, 0f, 0f, 4f),
    };

    /// <summary>
    /// The animation feature: one timeline with five animations on it, driven by the application's
    /// single frame clock. A block eases across its track, a bar blends two colours while it eases,
    /// and three tiles arrive in a stagger because each one's start time is later than the one
    /// before. Start, Pause, Resume, Restart and End all act on that one set.
    /// </summary>
    /// <remarks>
    /// The set is built with the page and started by a button, so a headless render - which builds
    /// every page and starts nothing - draws it in its rest position and costs no timer at all. The
    /// readout is itself an animation: a linear 0-to-1 value whose callback writes a percentage, so
    /// the timeline's progress can be watched and the "callback assigns the current value" contract
    /// is visible in something other than a control's bounds.
    /// </remarks>
    private XPlatPanel BuildAnimationDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Animations"));

        card.Controls.Add(new XPlatLabel(
            "One AnimationSet, one frame clock. The block eases across its track, the bar blends " +
            "blue into pink while it eases in, and the three tiles start 150 ms apart so they " +
            "arrive in a stagger. Start, Pause, Resume, Restart and End all act on the same set, " +
            "and the clock stops as soon as it is not running.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 58f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        });

        var readout = new XPlatLabel("Not started - press Start")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 20f,
            TextAlign = XPlatContentAlignment.MiddleLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 6f),
        };

        var stage = new XPlatPanel
        {
            Dock = XDock.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };

        var eastward = new SKColor(0xFF2F6FED);
        var westward = new SKColor(0xFFEC4899);

        var runner = new XPlatPanel
        {
            Bounds = new SKRect(8f, 14f, 52f, 58f),
            BackColor = eastward,
            CornerRadius = 8f,
        };
        stage.Controls.Add(runner);

        stage.Controls.Add(new XPlatPanel
        {
            Bounds = new SKRect(8f, 62f, 348f, 64f),
            BackColor = XPlatTheme.Border,
            CornerRadius = 1f,
        });

        var bar = new XPlatPanel
        {
            Bounds = new SKRect(8f, 82f, 348f, 102f),
            BackColor = eastward,
            CornerRadius = 4f,
        };
        stage.Controls.Add(bar);

        var tiles = new List<XPlatPanel>();
        SKColor[] tileColors =
        [
            new(0xFF2F6FED),
            new(0xFF7C3AED),
            new(0xFFEC4899),
        ];

        // The tiles begin below their resting place and fully transparent, so the first frame of a
        // run is what brings them in rather than a value the page was built at.
        for (int index = 0; index < tileColors.Length; index++)
        {
            var tile = new XPlatPanel
            {
                Bounds = new SKRect(8f + (index * 76f), 186f, 72f + (index * 76f), 250f),
                BackColor = tileColors[index],
                CornerRadius = 10f,
                Opacity = 0f,
            };
            tiles.Add(tile);
            stage.Controls.Add(tile);
        }

        TimeSpan timeline = TimeSpan.FromMilliseconds(900);
        int finishes = 0;

        var set = new XPlatAnimationSet();

        void Report(string state) =>
            readout.Text =
                $"{state}: {set.Progress * 100d:0} per cent of {timeline.TotalMilliseconds:0} ms elapsed, " +
                $"{finishes} finish handler run(s)";

        // Something that moves, with an ease that starts and ends at rest: a linear position reads
        // as mechanical over this distance, and the curve is the difference between the two.
        set.Add(XPlatValueAnimation.Create(
            8f,
            312f,
            timeline,
            value => runner.Left = value,
            easing: XPlatEasing.EaseInOutCubic));

        // Something that changes colour, with a curve of its own, so two animations on one timeline
        // can be seen easing differently from each other.
        set.Add(XPlatValueAnimation.Create(
            eastward,
            westward,
            timeline,
            value => bar.BackColor = value,
            easing: XPlatEasing.EaseInQuad));

        // A staggered set: same animation, three start times, so they arrive in sequence.
        for (int index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];
            var stagger = TimeSpan.FromMilliseconds(150 * index);
            var length = TimeSpan.FromMilliseconds(320);

            set.Add(XPlatValueAnimation.Create(
                tile.Top,
                158f,
                length,
                value => tile.Top = value,
                startTime: stagger,
                easing: XPlatEasing.EaseOutCubic));

            set.Add(XPlatValueAnimation.Create(
                0f,
                1f,
                length,
                value => tile.Opacity = value,
                startTime: stagger,
                easing: XPlatEasing.EaseOutQuad));
        }

        // The readout is a destination like any other, which is the point of the callback: it is a
        // value the animation assigns, not a control.
        set.Add(XPlatValueAnimation.Create(0f, 1f, timeline, _ => Report("Running")));

        set.AddFinishedHandler(_ =>
        {
            finishes++;
            Report("Finished");
        });

        var buttons = ButtonRow();

        static XPlatButton RowButton(string text, Action onClick)
        {
            var button = new XPlatButton(text)
            {
                Dock = XDock.Left,
                AutoSize = true,
                Style = XPlatButtonStyle.Secondary,
                Margin = new XPlatThickness(0f, 0f, 6f, 0f),
            };
            button.AddClickHandler(_ => onClick());
            return button;
        }

        buttons.Controls.Add(RowButton("Start", () =>
        {
            set.Start();
            Report("Running");
        }));

        buttons.Controls.Add(RowButton("Pause", () =>
        {
            set.Pause();
            Report("Paused");
        }));

        buttons.Controls.Add(RowButton("Resume", () =>
        {
            set.Resume();
            Report("Running");
        }));

        buttons.Controls.Add(RowButton("Restart", () =>
        {
            set.Restart();
            Report("Running");
        }));

        buttons.Controls.Add(RowButton("End", () =>
        {
            set.End();
            Report("Ended");
        }));

        card.Controls.Add(buttons);
        card.Controls.Add(readout);
        card.Controls.Add(stage);

        return card;
    }

    /// <summary>
    /// The spectrum colour picker, one instance per colour space plus a narrow one. Each column
    /// carries a live readout of the colour it is producing, so dragging one square shows what the
    /// same value looks like in the other two modes, and the narrow instance shows the component
    /// fields stacking instead of sharing a row.
    /// </summary>
    private XPlatPanel BuildColorPickerDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Colour picker"));

        card.Controls.Add(new XPlatLabel(
            "The square runs saturation left to right and value (RGB, HSV) or lightness (HSL) top to " +
            "bottom; the strip on the right is hue, red at the top. HSB is the same space as HSV.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 42f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        });

        var row = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 318f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
            Margin = new XPlatThickness(0f),
        };

        row.Controls.Add(BuildColorPickerColumn("RGB", XPlatColorMode.Rgb, 250f));
        row.Controls.Add(BuildColorPickerColumn("HSV (HSB)", XPlatColorMode.Hsv, 250f));
        row.Controls.Add(BuildColorPickerColumn("HSL", XPlatColorMode.Hsl, 250f));
        row.Controls.Add(BuildColorPickerColumn("Narrow: fields stack", XPlatColorMode.Hsl, 170f));

        card.Controls.Add(row);
        return card;
    }

    private XPlatPanel BuildColorPickerColumn(string title, XPlatColorMode mode, float width)
    {
        var column = new XPlatPanel
        {
            Dock = XDock.Left,
            Width = width,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
            Margin = new XPlatThickness(0f, 0f, 10f, 0f),
        };

        var picker = new XPlatColorPicker
        {
            Dock = XDock.Top,
            Height = 280f,
            ColorMode = mode,
            Color = new SKColor(0x33, 0x66, 0x99),
        };

        var readout = new XPlatLabel(DescribeColorPicker(picker))
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 20f,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.Accent,
            TextAlign = XPlatContentAlignment.MiddleLeft,
            Selectable = true,
        };

        picker.AddColorChangedHandler(_ => readout.Text = DescribeColorPicker(picker));

        column.Controls.Add(new XPlatLabel(title)
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 18f,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.BottomLeft,
        });

        column.Controls.Add(picker);
        column.Controls.Add(readout);
        return column;
    }

    private static string DescribeColorPicker(XPlatColorPicker picker) =>
        $"{picker.ColorMode}  #{picker.Color.Red:X2}{picker.Color.Green:X2}{picker.Color.Blue:X2}";

    /// <summary>
    /// The font dialog page: the platform's own chooser, the choice it returned, and a subtree that
    /// switches to that font so the inherited-appearance path is visible at the same time.
    /// </summary>
    private XPlatPanel BuildFontDialogDemo()
    {
        var initial = XPlatTheme.DefaultFont;
        _fontDialogSelection = SelectionOf(initial);

        // The page owns this font for the life of the window. Controls never dispose a font they
        // were handed, so replacing it means keeping it rather than letting the next paint race a
        // Dispose.
        var font = _fontDialogSelection.CreateFont();
        _fontDialogFonts.Add(font);

        var card = Card();
        card.Controls.Add(Heading("Font dialog"));
        card.Controls.Add(new XPlatLabel(
            "PickFont shows the platform's own font chooser - ChooseFont on Windows, NSFontPanel on " +
            "macOS and GtkFontChooserDialog on GTK - and returns a value describing the choice, or " +
            "null when the dialog was cancelled. The value is not a font: the caller builds and owns " +
            "the XPlatFont.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 58f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 10f),
        });

        _fontDialogReadout = new XPlatLabel
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 44f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        };
        card.Controls.Add(_fontDialogReadout);

        var buttons = ButtonRow();

        var choose = new XPlatButton("Choose font...")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        choose.AddClickHandler(_ => ShowFontDialog());

        var reset = new XPlatButton("Reset")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
        };
        reset.AddClickHandler(_ => ResetFontDialog());

        buttons.Controls.Add(choose);
        buttons.Controls.Add(reset);
        card.Controls.Add(buttons);

        // The subtree: the panel assigns the chosen font and everything under it inherits it,
        // including the button, whose class default is the theme's emphasized font.
        var stage = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 236f,
            Padding = new XPlatThickness(14f, 10f),
            CornerRadius = 8f,
            BorderWidth = 1f,
            Font = font,
            Background = XPlatBackground.FromThemeColor(XPlatThemeColor.Secondary),
        };
        _fontDialogStage = stage;

        stage.Controls.Add(new XPlatLabel(
            "The quick brown fox jumps over the lazy dog. This label inherits the panel's font, so " +
            "the whole subtree re-lays itself out when the choice changes.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 74f,
            WrapText = true,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        var nested = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 72f,
            Padding = new XPlatThickness(10f, 6f),
            CornerRadius = 6f,
            BorderWidth = 1f,
        };

        nested.Controls.Add(new XPlatLabel(
            "This label sits in a nested panel that assigns nothing, so it takes the font from one " +
            "level up - the same font, at the same size.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        stage.Controls.Add(nested);
        stage.Controls.Add(new XPlatButton("A button under the same panel")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = XPlatButtonStyle.Ghost,
            Margin = new XPlatThickness(0f, 8f, 0f, 0f),
        });
        card.Controls.Add(stage);

        card.Controls.Add(new XPlatLabel(
            "The dialog lists the families the operating system has installed. A face loaded with " +
            "XPlatFont.FromFile is not a system font unless the OS knows it too, so it does not " +
            "appear here; that is documented rather than half-supported.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 10f, 0f, 0f),
        });

        UpdateFontDialogReadout();
        return card;
    }

    private void ShowFontDialog()
    {
        if (OwnerWindow is not { } owner || _fontDialogSelection is not { } current)
        {
            return;
        }

        var chosen = owner.Application.Dialogs.PickFont(
            new XPlatFontDialogOptions
            {
                Title = "Choose a font",
                InitialFamilyName = current.FamilyName,
                InitialSize = current.Size,
                InitialWeight = current.Weight,
                InitialItalic = current.Italic,
                MinimumSize = 6f,
                MaximumSize = 48f,
            },
            owner);

        if (chosen is null)
        {
            SetStatus("Font dialog cancelled; the preview is unchanged.");
            return;
        }

        ApplyFontSelection(chosen);
        SetStatus($"Chose {DescribeSelection(chosen)}.");
    }

    private void ResetFontDialog()
    {
        _fontDialogStage?.ClearFont();

        if (_fontDialogStage is { } stage)
        {
            _fontDialogSelection = SelectionOf(stage.Font);
        }

        UpdateFontDialogReadout();
        SetStatus("The preview inherits the theme's font again.");
    }

    private void ApplyFontSelection(XPlatFontSelection selection)
    {
        _fontDialogSelection = selection;

        // The page owns every font it makes. Keeping the previous one alive is deliberate: a paint
        // may still be holding it when the assignment lands, and a handful of fonts is nothing.
        var font = selection.CreateFont();
        _fontDialogFonts.Add(font);

        if (_fontDialogStage is not null)
        {
            _fontDialogStage.Font = font;
        }

        UpdateFontDialogReadout();
    }

    private void UpdateFontDialogReadout()
    {
        if (_fontDialogReadout is null || _fontDialogSelection is not { } selection)
        {
            return;
        }

        var effective = _fontDialogStage?.Font;
        _fontDialogReadout.Text =
            $"Selection: {DescribeSelection(selection)}." +
            (effective is null
                ? string.Empty
                : $"\nThe subtree reports {effective.FamilyName}, {effective.Size:0.#} logical px, " +
                  $"weight {effective.Weight}, {(effective.Italic ? "italic" : "upright")}.");
    }

    private static XPlatFontSelection SelectionOf(XPlatFont font) =>
        new(string.IsNullOrWhiteSpace(font.FamilyName) ? "sans-serif" : font.FamilyName,
            font.Size,
            font.Weight,
            font.Italic);

    private static string DescribeSelection(XPlatFontSelection selection) =>
        $"{selection.FamilyName}, {selection.Size:0.#} logical px, weight {selection.Weight}, " +
        (selection.Italic ? "italic" : "upright");

    /// <summary>
    /// The colour dialog page: the swatch the colour dialog returns, a readout of the same value,
    /// and a button that opens the dialog modally on this window. The owner comes from
    /// <see cref="OwnerWindow"/>, which is the root control's window, so the button works no matter
    /// which page is showing.
    /// </summary>
    private XPlatPanel BuildColorDialogDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Colour dialog"));

        card.Controls.Add(new XPlatLabel(
            "XPlatColorDialog is drawn by the library, not by the platform: it is an ordinary " +
            "XPlatWindow holding XPlatColorPicker, with OK and Cancel, shown through the same modal " +
            "path an application uses for its own dialogs. Enter accepts, Escape cancels, and Cancel " +
            "returns null.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 58f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 10f),
        });

        var swatch = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 110f,
            BorderWidth = 1f,
            CornerRadius = 8f,
            BackColor = _colorDialogColor,
            Margin = new XPlatThickness(0f, 0f, 0f, 10f),
        };
        _colorDialogSwatch = swatch;
        card.Controls.Add(swatch);

        _colorDialogReadout = new XPlatLabel
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 40f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.Accent,
            TextAlign = XPlatContentAlignment.TopLeft,
            Selectable = true,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        };
        card.Controls.Add(_colorDialogReadout);

        var buttons = ButtonRow();

        var choose = new XPlatButton("Choose colour...")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Primary,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        choose.AddClickHandler(_ => ShowColorDialog());

        var reset = new XPlatButton("Reset")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
        };
        reset.AddClickHandler(_ => ResetColorDialog());

        buttons.Controls.Add(choose);
        buttons.Controls.Add(reset);
        card.Controls.Add(buttons);

        card.Controls.Add(new XPlatLabel(
            "The dialog is library-drawn on every platform, which is the difference from the font " +
            "dialog next door: the picker already exists as a portable control, so a native colour " +
            "chooser would replace tested code with three different answers. A font panel has no " +
            "portable substitute, so that one is still the platform's own.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 10f, 0f, 0f),
        });

        UpdateColorDialogReadout();
        return card;
    }

    private void ShowColorDialog()
    {
        if (OwnerWindow is not { } owner)
        {
            return;
        }

        var dialog = new XPlatColorDialog(new XPlatColorDialogOptions
        {
            Title = "Choose a colour",
            InitialColor = _colorDialogColor,
            ColorMode = XPlatColorMode.Rgb,
        });

        var chosen = dialog.Show(owner);
        if (chosen is null)
        {
            SetStatus("Colour dialog cancelled; the swatch is unchanged.");
            return;
        }

        _colorDialogColor = chosen.Value;
        UpdateColorDialogReadout();
        SetStatus($"Chose {DescribeColor(_colorDialogColor)}.");
    }

    private void ResetColorDialog()
    {
        _colorDialogColor = new SKColor(0x33, 0x66, 0x99);
        UpdateColorDialogReadout();
        SetStatus("The swatch is back to #336699.");
    }

    private void UpdateColorDialogReadout()
    {
        if (_colorDialogSwatch is not null)
        {
            _colorDialogSwatch.BackColor = _colorDialogColor;
        }

        if (_colorDialogReadout is not null)
        {
            _colorDialogReadout.Text =
                $"Current colour: {DescribeColor(_colorDialogColor)}. Opening the dialog again " +
                "starts from this value.";
        }
    }

    private static string DescribeColor(SKColor color) =>
        $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2} (R {color.Red}, G {color.Green}, B {color.Blue})";

    /// <summary>
    /// <see cref="XPlatToast"/>: the four severities, each dismissed by its own countdown or by the
    /// close glyph, with the message docked at the bottom of the page like a real one.
    /// </summary>
    private XPlatPanel BuildToastDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Toast"));

        card.Controls.Add(new XPlatLabel(
            "A transient message bar. The accent bar is coloured for the severity, the message " +
            "sits beside it, and the toast dismisses itself after the duration it was given - or " +
            "the user closes it. It is an ordinary control, so a page makes room for it.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 58f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        });

        var toast = new XPlatToast(
            "Project saved to disk - the toast dismisses itself after four seconds.")
        {
            Dock = XDock.Bottom,
            Severity = XPlatToastSeverity.Success,
            Margin = new XPlatThickness(0f, 12f, 0f, 0f),
        };
        toast.AddDismissedHandler(_ => SetStatus("The toast went away - by its countdown or by the close glyph."));

        var row = ButtonRow();
        row.Height = 34f;
        row.Margin = new XPlatThickness(0f, 0f, 0f, 8f);

        row.Controls.Add(ToastButton("Success", XPlatToastSeverity.Success,
            "Project saved to disk - the toast dismisses itself after four seconds.", 4, toast));

        row.Controls.Add(ToastButton("Info", XPlatToastSeverity.Info,
            "Three files were re-indexed in the background.", 4, toast));

        row.Controls.Add(ToastButton("Warning", XPlatToastSeverity.Warning,
            "The export finished, but two images could not be embedded.", 5, toast));

        row.Controls.Add(ToastButton("Error", XPlatToastSeverity.Error,
            "Could not reach the server. This one stays until it is dismissed.", null, toast));

        card.Controls.Add(row);

        card.Controls.Add(new XPlatLabel(
            "A toast with no duration stays up: an error usually waits for the user to acknowledge " +
            "it, while a success can go away on its own. The countdown pauses while the toast is " +
            "not on screen, so a message cannot expire behind a page the user switched away from.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        card.Controls.Add(toast);

        // The page opens with a message up, so a screenshot of it shows the control.
        toast.Show();

        return card;
    }

    /// <summary>One of the toast page's buttons: sets the severity and the message, and shows it.</summary>
    private XPlatButton ToastButton(
        string text,
        XPlatToastSeverity severity,
        string message,
        int? seconds,
        XPlatToast toast)
    {
        var button = new XPlatButton(text)
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };

        button.AddClickHandler(_ =>
        {
            toast.Text = message;
            toast.Severity = severity;
            toast.Duration = seconds is { } value ? TimeSpan.FromSeconds(value) : null;
            toast.Show();
            SetStatus(severity is XPlatToastSeverity.Error or XPlatToastSeverity.Info
                ? $"Showing an {severity} toast."
                : $"Showing a {severity} toast.");
        });

        return button;
    }

    /// <summary>
    /// <see cref="XPlatBusyIndicator"/> and <see cref="XPlatOverlay"/>: a spinner on its own, and the
    /// same spinner inside a layer that stops the panel underneath from being driven.
    /// </summary>
    private XPlatPanel BuildBusyOverlayDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Busy indicator and overlay"));

        card.Controls.Add(new XPlatLabel(
            "Two controls, because they answer two questions. The spinner says the work is running; " +
            "the overlay stops the user from driving the window while it does. Putting the spinner " +
            "inside the overlay is the usual combination.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 42f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        });

        var row = ButtonRow();
        row.Height = 40f;

        var spinner = new XPlatBusyIndicator { Dock = XDock.Left, Margin = new XPlatThickness(0f, 4f, 12f, 0f) };
        row.Controls.Add(spinner);

        var toggle = new XPlatButton("Stop the spinner")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        toggle.AddClickHandler(_ =>
        {
            spinner.IsBusy = !spinner.IsBusy;
            toggle.Text = spinner.IsBusy ? "Stop the spinner" : "Start the spinner";
            SetStatus(spinner.IsBusy
                ? "The spinner is going round again - and it has stopped its timer while it was still."
                : "The spinner is still, and its timer is stopped: an idle spinner costs nothing.");
        });
        row.Controls.Add(toggle);

        var heavy = new XPlatButton("A heavier one")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Ghost,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        heavy.AddClickHandler(_ => SetStatus("A spinner's ring thickness is a property, not a theme value."));

        row.Controls.Add(new XPlatBusyIndicator
        {
            Dock = XDock.Left,
            Thickness = 5f,
            Margin = new XPlatThickness(0f, 4f, 8f, 0f),
        });
        row.Controls.Add(heavy);

        // A spinner in a box that is deliberately nothing like square - wide and short, with the box
        // drawn round it so the control's shape is visible. The circle comes from the shorter side
        // and is centred, so it reads as a spinner rather than as a stretched oval.
        var wideBox = new XPlatPanel
        {
            Dock = XDock.Left,
            Width = 240f,
            BorderWidth = 1f,
            CornerRadius = 8f,
            Padding = new XPlatThickness(0f),
            Margin = new XPlatThickness(0f, 2f, 4f, 0f),
        };
        wideBox.Controls.Add(new XPlatBusyIndicator { Dock = XDock.Fill, Thickness = 5f });
        row.Controls.Add(wideBox);

        card.Controls.Add(row);

        // The shell that gets blocked: its content and the layer are both Fill children, so they
        // cover the same area and the layer is above it.
        var shell = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 170f,
            BorderWidth = 1f,
            CornerRadius = 8f,
            Padding = new XPlatThickness(14f),
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        };

        var content = new XPlatPanel
        {
            Dock = XDock.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };

        var readout = new XPlatLabel("Nothing has been done yet.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 24f,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        };

        var action = new XPlatButton("Do something in the blocked panel")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        };
        int actions = 0;
        action.AddClickHandler(_ =>
        {
            actions++;
            readout.Text = $"The panel's button has been pressed {actions} time(s).";
            SetStatus("The panel's button works while the layer is hidden.");
        });

        var box = new XPlatCheckBox("A control under the layer") { Dock = XDock.Top, AutoSize = true };

        content.Controls.Add(action);
        content.Controls.Add(box);
        content.Controls.Add(readout);

        var busyCard = new XPlatPanel
        {
            AutoSize = false,
            Size = new SKSize(260f, 76f),
            BorderWidth = 1f,
            CornerRadius = 8f,
            Padding = new XPlatThickness(12f),
        };
        busyCard.Controls.Add(new XPlatBusyIndicator
        {
            Dock = XDock.Left,
            Margin = new XPlatThickness(0f, 0f, 14f, 0f),
        });
        busyCard.Controls.Add(new XPlatLabel("Loading the project...")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            TextAlign = XPlatContentAlignment.MiddleLeft,
            ForeColorRole = XPlatThemeColor.Foreground,
        });

        // The page opens with the layer up, so the blocked state is what a screenshot of the page
        // shows; "Unblock" releases it and "Block the panel" puts it back.
        var overlay = new XPlatOverlay();
        overlay.Controls.Add(busyCard);

        shell.Controls.Add(content);
        shell.Controls.Add(overlay);
        card.Controls.Add(shell);

        var layerRow = ButtonRow();
        layerRow.Height = 32f;

        var block = new XPlatButton("Block the panel")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Enabled = false,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        block.AddClickHandler(_ =>
        {
            overlay.Visible = true;
            block.Enabled = false;
            SetStatus("The panel is blocked: its button takes no press and the keyboard goes nowhere.");
        });

        var unblock = new XPlatButton("Unblock")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
        };
        unblock.AddClickHandler(_ =>
        {
            overlay.Visible = false;
            block.Enabled = true;
            SetStatus("The panel is free again, and the keyboard is back on whatever had the focus.");
        });

        layerRow.Controls.Add(block);
        layerRow.Controls.Add(unblock);
        card.Controls.Add(layerRow);

        card.Controls.Add(new XPlatLabel(
            "The layer takes the presses by being the topmost control over the panel, and the keys " +
            "by joining the window as an input sink while it is on screen - the same seam an open " +
            "menu uses. Its dimming is the palette's background, so it follows a theme change. The " +
            "wide box in the row above is a spinner in a very un-square control, and it is still " +
            "a circle in the middle of it.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>
    /// <see cref="XPlatWrapPanel"/>: a tag row that wraps at the page's width, a column flow of
    /// swatches, and a row whose items are of different heights so the stretch is visible.
    /// </summary>
    private XPlatPanel BuildWrapPanelDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Wrap panel"));

        card.Controls.Add(new XPlatLabel(
            "Items flow along one axis and start a new line when the next one does not fit. Rows " +
            "that wrap downwards are the default; columns that wrap to the right are the other " +
            "orientation. A line is as broad as its broadest item, so Stretch - the default across " +
            "a line - makes the rest of the line match it.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 58f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 10f),
        });

        card.Controls.Add(new XPlatLabel("Tags")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 22f,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        var tags = new XPlatWrapPanel
        {
            Dock = XDock.Top,
            Height = 96f,
            Spacing = 6f,
            Margin = new XPlatThickness(0f, 0f, 0f, 14f),
        };

        foreach (string tag in new[]
        {
            "design", "layout", "skia", "cross-platform", "aot", "trimming",
            "popups", "menus", "text", "accessibility", "windows", "macos",
            "linux", "tests",
        })
        {
            var chip = new XPlatButton(tag)
            {
                AutoSize = true,
                Style = XPlatButtonStyle.Secondary,
            };
            chip.AddClickHandler(_ => SetStatus($"Tag \"{tag}\" clicked - the flow wrapped around it."));
            tags.Controls.Add(chip);
        }

        card.Controls.Add(tags);

        card.Controls.Add(new XPlatLabel("Heights across one line")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 22f,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        var heights = new XPlatWrapPanel
        {
            Dock = XDock.Top,
            Height = 56f,
            Spacing = 8f,
            Margin = new XPlatThickness(0f, 0f, 0f, 14f),
        };

        var stretched = new XPlatPanel
        {
            AutoSize = false,
            Size = new SKSize(120f, 24f),
            CornerRadius = 4f,
            BackColor = new SKColor(0x33, 0x66, 0x99),
        };
        heights.Controls.Add(stretched);

        var centred = new XPlatPanel
        {
            AutoSize = false,
            Size = new SKSize(120f, 24f),
            CornerRadius = 4f,
            BackColor = new SKColor(0x2A, 0x9D, 0x8F),
            VerticalAlignment = XPlatAlignment.Center,
        };
        heights.Controls.Add(centred);

        // The tallest item sets the line, and the Stretched one beside it fills that height.
        heights.Controls.Add(new XPlatPanel
        {
            AutoSize = false,
            Size = new SKSize(120f, 44f),
            CornerRadius = 4f,
            BackColor = new SKColor(0xD9, 0x82, 0x2B),
        });

        card.Controls.Add(heights);

        card.Controls.Add(new XPlatLabel("Columns, wrapping to the right")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 22f,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        var swatches = new XPlatWrapPanel
        {
            Dock = XDock.Top,
            Height = 120f,
            Orientation = XPlatStackOrientation.Vertical,
            Spacing = 4f,
            Margin = new XPlatThickness(0f, 0f, 0f, 14f),
        };

        var palette = new[]
        {
            new SKColor(0x33, 0x66, 0x99), new SKColor(0x2A, 0x9D, 0x8F),
            new SKColor(0xD9, 0x82, 0x2B), new SKColor(0xB0, 0x3A, 0x3A),
            new SKColor(0x6A, 0x4C, 0x93), new SKColor(0x4C, 0x7A, 0x34),
            new SKColor(0x88, 0x88, 0x88),
        };

        for (int index = 0; index < palette.Length; index++)
        {
            var swatch = new XPlatPanel
            {
                AutoSize = false,
                Size = new SKSize(44f, 26f),
                CornerRadius = 4f,
                BorderWidth = 1f,
                BackColor = palette[index],
            };
            swatch.AddClickHandler(_ => SetStatus(
                $"Swatch #{index + 1} is {DescribeColor(palette[index])}; the column wrapped where it ran out of height."));
            swatches.Controls.Add(swatch);
        }

        card.Controls.Add(swatches);

        card.Controls.Add(new XPlatLabel(
            "The swatches run down the 120-pixel column and start a second column when the height " +
            "runs out, which is the same layout with the axes swapped.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>
    /// <see cref="XPlatExpander"/>: the settings-page shape, one section open and one folded, with a
    /// disabled one to show that the cascade reaches the header as well as the content.
    /// </summary>
    private XPlatPanel BuildExpanderDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Expander"));

        card.Controls.Add(new XPlatLabel(
            "A header with a chevron and content that folds away under it. Clicking the header, " +
            "or Enter, Space, Left and Right while it has the focus, expands and collapses the " +
            "section. The first one below animates the fold, the second jumps - the same control, " +
            "with and without a duration from the animations package.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 56f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        });

        var column = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 340f,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };

        var animated = new XPlatExpander("Animated - 180 ms")
        {
            Dock = XDock.Top,
            Height = 128f,
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        };
        animated.ExpandDuration = TimeSpan.FromMilliseconds(180);
        foreach (string caption in new[] { "Show grid", "Snap to grid", "Show rulers" })
        {
            var box = new XPlatCheckBox(caption)
            {
                Dock = XDock.Top,
                AutoSize = true,
                Checked = caption != "Show rulers",
                Margin = new XPlatThickness(0f, 0f, 0f, 4f),
            };
            box.AddCheckedChangedHandler(_ => SetStatus($"{box.Text} is {Describe(box.CheckState)}."));
            animated.Controls.Add(box);
        }

        // Tab is the point of this page as much as the fold is, so every stop says its name when
        // it takes the focus: Tab skips the boxes of a folded section and comes back to them when
        // it opens, and folding a section under one of its own boxes brings the focus up to the
        // header here.
        WatchFocus(animated, "the animated section's header");
        foreach (var box in animated.Controls)
        {
            WatchFocus(box, $"the animated section's \u201c{box.Text}\u201d box");
        }

        animated.AddExpandedChangedHandler(sender =>
        {
            var section = (XPlatExpander)sender;
            SetStatus(section.IsExpanded
                ? "The animated section is opening - the arrow turns as the content unfolds."
                : "The animated section is closing - progress and arrow come down together.");
        });

        column.Controls.Add(animated);

        var instant = new XPlatExpander("Instant - no duration")
        {
            Dock = XDock.Top,
            Height = 104f,
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        };
        instant.Controls.Add(new XPlatCheckBox("Trace layout") { Dock = XDock.Top, AutoSize = true });
        instant.Controls.Add(new XPlatCheckBox("Cache layers") { Dock = XDock.Top, AutoSize = true });
        WatchFocus(instant, "the instant section's header");
        foreach (var box in instant.Controls)
        {
            WatchFocus(box, $"the instant section's \u201c{box.Text}\u201d box");
        }
        instant.AddExpandedChangedHandler(sender =>
            SetStatus(((XPlatExpander)sender).IsExpanded
                ? "The instant section opened, all at once."
                : "The instant section jumped shut to its header."));
        column.Controls.Add(instant);

        var disabled = new XPlatExpander("Network (disabled)")
        {
            Dock = XDock.Top,
            Height = 84f,
            Enabled = false,
        };
        disabled.Controls.Add(new XPlatCheckBox("Use a proxy") { Dock = XDock.Top, AutoSize = true });
        column.Controls.Add(disabled);

        card.Controls.Add(column);

        card.Controls.Add(new XPlatLabel(
            "A folded section keeps its content in the tree but arranges it into nothing, so the " +
            "children come back exactly as they were. ExpandProgress is what the fold is drawn " +
            "from - height and arrow together - and it follows IsExpanded on its own, so the " +
            "section without a duration behaves as it always did. The duration is an extension " +
            "member from XPlatUI.Feature.Animations, which this control does not reference. " +
            "Tab is the other half of the fold: it steps over the boxes of a folded section and " +
            "goes back into them when it opens, and folding a section under one of its own boxes " +
            "brings the focus up to the header - the box it was on is no longer reachable.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>
    /// Names a control in the status line when it takes the focus, so the demo says out loud what
    /// Tab is doing - which is the only way to see a skipped stop by hand.
    /// </summary>
    private void WatchFocus(XPlatControl control, string name) =>
        control.AddGotFocusHandler(_ => SetStatus($"Focus is on {name}."));

    /// <summary>
    /// <see cref="XPlatSplitButton"/> and <see cref="XPlatDropDownButton"/>: the main area runs one
    /// command and the arrow opens the menu, with a disabled one beside them to show the greying.
    /// </summary>
    private XPlatPanel BuildSplitButtonDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Split buttons"));

        card.Controls.Add(new XPlatLabel(
            "A split button is a command with a menu: the label runs the default action and the " +
            "arrow opens the drop-down. The drop-down button has no second half - pressing it " +
            "anywhere opens the menu.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 42f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        });

        var row = ButtonRow();
        row.Height = 40f;

        var save = new XPlatSplitButton("Save")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        save.AddClickHandler(_ => SetStatus("Split button: the main area ran Save."));
        save.Menu.Add("Save as...", () => SetStatus("Split button: Save as... chosen from the menu."));
        save.Menu.Add("Save a copy", () => SetStatus("Split button: Save a copy chosen from the menu."));
        save.Menu.AddSeparator();
        save.Menu.Add("Export as PDF", () => SetStatus("Split button: Export as PDF chosen from the menu."));
        row.Controls.Add(save);

        var more = new XPlatDropDownButton("More actions")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        more.AddClickHandler(_ => SetStatus("Drop-down button: the menu opened from the whole face."));
        more.Menu.Add("Rename", () => SetStatus("Drop-down button: Rename chosen from the menu."));
        more.Menu.Add("Archive", () => SetStatus("Drop-down button: Archive chosen from the menu."));
        row.Controls.Add(more);

        var disabled = new XPlatSplitButton("Disabled")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Enabled = false,
            Style = XPlatButtonStyle.Secondary,
        };
        disabled.Menu.Add("Never shown");
        row.Controls.Add(disabled);

        card.Controls.Add(row);

        card.Controls.Add(new XPlatLabel(
            "The arrow is part of the measured width, Enter and Space run the default action, and " +
            "Down opens the menu. The menu is the library's own, so it opens submenus and leaves " +
            "the window the same way every other menu does.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>
    /// The toast's fade and slide, from <c>XPlatUI.Feature.Animations</c>. One message with a
    /// duration for each direction, and a switch that takes both durations away so the difference
    /// is a press away rather than a sentence.
    /// </summary>
    private XPlatPanel BuildToastAnimationDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Toast animation"));

        card.Controls.Add(new XPlatLabel(
            "The same control with a duration from the animations package. Press Show and it fades " +
            "in while sliding out of the edge it is docked against; press Dismiss and it reverses, " +
            "and only leaves the layout when the transition is over. Untick Instant and press them " +
            "again to watch it without the animation - the message appears and goes in one frame.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 58f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        });

        var toast = new XPlatToast("Project saved to disk - watch it slide in from the bottom edge.")
        {
            Dock = XDock.Bottom,
            Severity = XPlatToastSeverity.Success,
            Duration = null,
            Margin = new XPlatThickness(0f, 12f, 0f, 0f),
        };
        toast.ShowDuration = TimeSpan.FromMilliseconds(240);
        toast.DismissDuration = TimeSpan.FromMilliseconds(180);
        toast.AddDismissedHandler(_ => SetStatus("The toast has gone - the layout closed up behind it."));

        var row = ButtonRow();
        row.Height = 34f;
        row.Margin = new XPlatThickness(0f, 0f, 0f, 8f);

        var show = new XPlatButton("Show")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        show.AddClickHandler(_ =>
        {
            toast.Show();
            SetStatus($"Showing over {toast.ShowDuration?.TotalMilliseconds} ms - it is already taking its room.");
        });

        var dismiss = new XPlatButton("Dismiss")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        dismiss.AddClickHandler(_ =>
        {
            toast.Dismiss();
            SetStatus(
                $"Dismissing over {toast.DismissDuration?.TotalMilliseconds} ms - it is out of the Tab order " +
                "before the transition starts.");
        });

        var instant = new XPlatCheckBox("Instant - no duration") { Dock = XDock.Left, AutoSize = true };
        instant.AddCheckedChangedHandler(_ =>
        {
            var duration = instant.Checked ? (TimeSpan?)null : TimeSpan.FromMilliseconds(240);
            toast.ShowDuration = duration;
            toast.DismissDuration = instant.Checked ? (TimeSpan?)null : TimeSpan.FromMilliseconds(180);
            SetStatus(instant.Checked
                ? "No durations: the toast is the control it always was."
                : "Durations are back: Show and Dismiss animate again.");
        });

        row.Controls.Add(show);
        row.Controls.Add(dismiss);
        row.Controls.Add(instant);
        card.Controls.Add(row);

        card.Controls.Add(new XPlatLabel(
            "ShowProgress is the one number the message is drawn from: its opacity, and how far it " +
            "has come in from its docked edge. The two durations animate that number, so the fade " +
            "and the slide cannot disagree, and the message is taken off the screen when the number " +
            "reaches 0 rather than when Dismiss is called - which is why a cancelled dismissal still " +
            "ends with the toast gone. The close glyph and Escape both work while it is up.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        card.Controls.Add(toast);

        // The page opens with the message up, so a screenshot of it shows the control.
        toast.Show();

        return card;
    }

    /// <summary>
    /// The overlay's fade-in, from <c>XPlatUI.Feature.Animations</c>: the layer dims in over the
    /// panel instead of appearing at full strength in one frame.
    /// </summary>
    private XPlatPanel BuildOverlayFadeDemo()
    {
        var card = Card();
        card.Controls.Add(Heading("Overlay fade"));

        card.Controls.Add(new XPlatLabel(
            "The same blocker as the busy-and-overlay page, with a duration: it dims in over the " +
            "panel it covers, and it is holding the keyboard from the first frame rather than from " +
            "the end of the fade. Block and unblock it to watch it come in again.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 42f,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        });

        var shell = new XPlatPanel
        {
            Dock = XDock.Top,
            Height = 170f,
            BorderWidth = 1f,
            CornerRadius = 8f,
            Padding = new XPlatThickness(14f),
            Margin = new XPlatThickness(0f, 0f, 0f, 12f),
        };

        var content = new XPlatPanel
        {
            Dock = XDock.Fill,
            ShowFrame = false,
            IsCard = false,
            BackColor = null,
            BorderWidth = 0f,
            CornerRadius = 0f,
            Padding = new XPlatThickness(0f),
        };

        var readout = new XPlatLabel("Nothing has been done yet.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 24f,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        };

        var action = new XPlatButton("Do something in the blocked panel")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
            Margin = new XPlatThickness(0f, 0f, 0f, 8f),
        };
        int actions = 0;
        action.AddClickHandler(_ =>
        {
            actions++;
            readout.Text = $"The panel's button has been pressed {actions} time(s).";
            SetStatus("The panel's button works while the layer is hidden.");
        });

        content.Controls.Add(action);
        content.Controls.Add(new XPlatCheckBox("A control under the layer") { Dock = XDock.Top, AutoSize = true });
        content.Controls.Add(readout);

        var busyCard = new XPlatPanel
        {
            AutoSize = false,
            Size = new SKSize(260f, 76f),
            BorderWidth = 1f,
            CornerRadius = 8f,
            Padding = new XPlatThickness(12f),
        };
        busyCard.Controls.Add(new XPlatBusyIndicator
        {
            Dock = XDock.Left,
            Margin = new XPlatThickness(0f, 0f, 14f, 0f),
        });
        busyCard.Controls.Add(new XPlatLabel("Loading the project...")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            TextAlign = XPlatContentAlignment.MiddleLeft,
            ForeColorRole = XPlatThemeColor.Foreground,
        });

        // The layer starts up, so the page opens with the blocked state on screen and the fade is
        // the first thing it does when the window appears.
        var overlay = new XPlatOverlay();
        overlay.FadeDuration = TimeSpan.FromMilliseconds(160);
        overlay.Controls.Add(busyCard);

        shell.Controls.Add(content);
        shell.Controls.Add(overlay);
        card.Controls.Add(shell);

        var layerRow = ButtonRow();
        layerRow.Height = 32f;

        var block = new XPlatButton("Block the panel")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Enabled = false,
            Margin = new XPlatThickness(0f, 0f, 8f, 0f),
        };
        block.AddClickHandler(_ =>
        {
            overlay.Visible = true;
            block.Enabled = false;
            SetStatus("The panel is blocked again, and the layer dims in over it.");
        });

        var unblock = new XPlatButton("Unblock")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = XPlatButtonStyle.Secondary,
        };
        unblock.AddClickHandler(_ =>
        {
            overlay.Visible = false;
            block.Enabled = true;
            SetStatus("The panel is free again. There is no fade out: hiding the layer hides it.");
        });

        layerRow.Controls.Add(block);
        layerRow.Controls.Add(unblock);
        card.Controls.Add(layerRow);

        card.Controls.Add(new XPlatLabel(
            "The fade is the layer's Opacity, so the dimming and the card inside it composite as one " +
            "picture rather than each blending with what is under it. Only coming in animates: a " +
            "fade out would mean the control holding on to a hidden layer, so assigning Visible " +
            "still takes it away at once - the application's word for what is on screen stays true.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>
    /// A panel that draws the focus ring when it has the focus. A plain panel has no chrome of its
    /// own, and the readout alone is a weak way to show that a container - not a control inside it -
    /// is the thing Tab landed on.
    /// </summary>
    private sealed class FocusRingPanel : XPlatPanel
    {
        protected override void OnPaintBorder(SKCanvas canvas, SKRect rect)
        {
            base.OnPaintBorder(canvas, rect);

            if (IsFocused)
            {
                XPlatPainter.DrawFocusRing(canvas, rect, XPlatTheme.FocusRing, 2f, CornerRadius);
            }
        }
    }
}
