using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Kolvir;
using Kolvir.Feature.Animations;
using Kolvir.Feature.Audio;
using Kolvir.AutoComplete;
using Kolvir.Backend.Headless;
using Kolvir.Feature.Badges;
using Kolvir.Feature.DragDrop;
using Kolvir.Multiline;
using Kolvir.Spans;
using Kolvir.Feature.Svg;
using Kolvir.Undo;
using Kolvir.Feature.WoffFonts;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The sample Kolvir shown by the harnesses. It is kept separate from any host window so the
/// very same control tree is rendered by the Win32 harness, by the GTK4 harness, and headlessly
/// (see <see cref="RenderToPng"/>).
/// </summary>
/// <remarks>
/// The window shell only: the header, the navigation list, the status line and the stage, plus the
/// table that says which page is which. A page is a class of its own under <c>Pages/</c>, derived
/// from <see cref="DemoPage"/>, and it owns everything below the stage's inset.
/// </remarks>
public sealed class DemoUi
{
    private Label _statusLabel = null!;
    private Label? _counterLabel;
    private ListBox _navList = null!;
    private Panel _stage = null!;
    private DemoPage[] _pages = null!;
    private Control? _shownPage;

    /// <summary>The navigation's filter box: what the user types to narrow the list.</summary>
    private TextBox? _navFilter;

    /// <summary>
    /// The page each row of the navigation stands for. The list shows what the filter matched, so a
    /// row is not a page position and the two are only the same while the filter is empty.
    /// </summary>
    private int[] _navRows = [];

    /// <summary>The page on the stage, which survives filtering even when no row stands for it.</summary>
    private int _shownIndex = -1;

    /// <summary>Set while the list is being rebuilt, so replacing its rows does not change the page.</summary>
    private bool _rebuildingNav;

    // The click counter, which the footer's Reset clears, the labels page shows and the buttons page
    // advances: it belongs to the window rather than to any one of them.
    private int _clicks;

    /// <summary>The footer's renderer readout, which the measured-rendering page keeps up to date.</summary>
    private Label? _gfxLabel;

    private Window? _chromeWindow;

    public DemoUi()
    {
        Root = BuildUi();

        // A page that has no menu of its own still has to answer a right-click: the host asks for a
        // menu only after the control under the pointer has declined, and the native menu paths show
        // nothing at all when nobody answers. This one is genuinely application-wide - it belongs to
        // the window, not to any one control - so it is the host's handler, and it runs last. The
        // selection's Copy menu comes after it, so a page menu is only offered while there is
        // nothing selected to copy. A text box keeps its own Cut/Copy/Paste menu, which is why the
        // page menu is not offered over one.
        DemoPage.OnHostAvailable(Root, host => host.AddContextMenuOpeningHandler((_, args) =>
        {
            if (host.Selection.IsEmpty && args.Control is not TextBox)
            {
                args.Menu = BuildPageContextMenu();
            }
        }));
    }

    /// <summary>The root of the sample control tree.</summary>
    public Control Root { get; }

    /// <summary>
    /// Gives the sample the window it is showing in, which the chromeless page drives.
    /// </summary>
    /// <remarks>
    /// A page is built before the window exists, so the page cannot ask for one - and a headless
    /// render never has a real window to give it, which is why the page reports "no window" rather
    /// than failing when this is never called.
    /// </remarks>
    /// <param name="window">The window the sample is displayed in.</param>
    public void AttachWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (ReferenceEquals(_chromeWindow, window))
        {
            return;
        }

        _chromeWindow = window;

        // A page built before there was a window could not subscribe to it or read from it, so every
        // page is given the window here - once - rather than the shell re-reading each page's state
        // on its behalf.
        foreach (var page in _pages)
        {
            page.OnWindowAttached(window);
        }
    }

    public void FocusFirstTextBox()
    {
        for (int index = 0; index < _pages.Length; index++)
        {
            if (FindTextBox(_pages[index].Root) is not { } box)
            {
                continue;
            }

            SelectPage(index);
            box.Focus();
            return;
        }
    }

    private static TextBox? FindTextBox(Control control)
    {
        var pending = new Stack<Control>();
        pending.Push(control);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current is TextBox box)
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
    /// <remarks>
    /// A page the filter is hiding cannot be selected in the list, so the filter is cleared first:
    /// asking for a page is a request to see it rather than to see nothing.
    /// </remarks>
    public void SelectPage(int index)
    {
        if (index < 0 || index >= _pages.Length)
        {
            return;
        }

        if (Array.IndexOf(_navRows, index) < 0)
        {
            _navFilter!.Text = string.Empty;
        }

        _navList.SelectedIndex = Array.IndexOf(_navRows, index);
    }

    /// <summary>
    /// The position of the page <paramref name="page"/> names, or -1 when it names none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A page is named two ways, and <c>--page</c> takes either: by its id - the word in the first
    /// column of <see cref="Demos"/>, such as <c>audio</c> - or by its position in the navigation
    /// list, so <c>--page 59</c> and <c>--page audio</c> are the same page.
    /// </para>
    /// <para>
    /// A number is read as a position, which is what keeps a command line written before ids existed
    /// working unchanged; anything else is matched against an id. Ids are compared ordinally, so case
    /// matters and <c>--page Audio</c> names nothing.
    /// </para>
    /// </remarks>
    /// <param name="page">An id or a position, or null to name no page.</param>
    /// <returns>The page's position, or -1 when nothing matched.</returns>
    public static int PageIndex(string? page)
    {
        if (string.IsNullOrWhiteSpace(page))
        {
            return -1;
        }

        if (int.TryParse(page, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            return index >= 0 && index < Demos.Length ? index : -1;
        }

        return Array.FindIndex(Demos, demo => string.Equals(demo.Id, page, StringComparison.Ordinal));
    }

    /// <summary>
    /// The menu a page offers when the control under the pointer has none of its own: the two moves
    /// a reader of a page wants, and a line about what the sample is. It is built per right-click so
    /// the page moves know which page they are on.
    /// </summary>
    private ContextMenu BuildPageContextMenu()
    {
        int index = _shownIndex;

        return new ContextMenu()
            .Add(new MenuItem("&Previous page", () => SelectPage(index - 1)) { Enabled = index > 0 })
            .Add(new MenuItem("&Next page", () => SelectPage(index + 1))
            {
                Enabled = index >= 0 && index < _pages.Length - 1,
            })
            .AddSeparator()
            .Add(new MenuItem("&About this sample", () => SetStatus(
                "Every control here is drawn by Kolvir itself, so the same tree runs on Windows, macOS and Linux.")));
    }

    /// <summary>
    /// Renders the sample UI to a PNG without opening a window. Useful for visual regression tests
    /// and for verifying the render pipeline on CI.
    /// </summary>
    /// <remarks>
    /// This goes through the real application model - <see cref="Application"/>,
    /// <see cref="Window"/> and the headless backend - so a regression here is a regression in
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
        Func<Window, DemoUi, MenuBar>? menuBar = null,
        Action<Window, DemoUi>? afterShown = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var demo = new DemoUi();
        demo.SelectPage(page);

        var backend = new HeadlessApplicationBackend();

        // The same features the sample's own window declares, because a page is entitled to them and a
        // render that lacked one would show a page the application never shows. The fonts page is the
        // one that depends on it: it loads its web font through the loader.
        using var application = new Application(backend, new ApplicationOptions
        {
            ConfigureServices = services => services.AddWoffFonts(),
        });

        var window = new Window("Kolvir sample", demo.Root)
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

    private Control BuildUi()
    {
        // The window background comes from the host's EffectiveClearColor, which follows the palette.
        var root = new Panel
        {
            Padding = new Thickness(0f),
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

    private Panel BuildHeader()
    {
        var header = new Panel
        {
            Dock = XDock.Top,
            Height = 100f,
            Padding = new Thickness(0f),
        };

        header.Controls.Add(BuildAppearanceButton());
        header.Controls.Add(BuildOwnedWindowButton());

        var title = new Label("Kolvir")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 44f,
            Font = Theme.TitleFont,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Thickness(28f, 16f, 28f, 0f),
        };

        var subtitle = new Label("A lightweight, AOT-friendly, cross-platform control layer rendered with SkiaSharp.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 24f,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(28f, 4f, 28f, 0f),
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
    internal void AttachWindowDrag(Control strip) =>
        strip.AddMouseDownHandler((_, _) => DragWindowFromThisPress());

    /// <summary>
    /// Swaps the palette. The library repaints on its own (the host listens to
    /// <see cref="Theme.AddChangedHandler"/>), so this is the whole of the application's dark mode
    /// story.
    /// </summary>
    private Button BuildAppearanceButton()
    {
        var button = new Button("Switch appearance")
        {
            Dock = XDock.Right,
            Style = ButtonStyle.Secondary,
            AutoSize = true,
            Margin = new Thickness(8f, 20f, 28f, 0f),
        };

        button.AddClickHandler(_ => SetAppearance(Theme.IsDarkMode ? Palette.Light : Palette.Dark));

        return button;
    }

    /// <summary>
    /// Applies <paramref name="palette"/> and reports it in the status line, so the header button
    /// and the View menu describe the change the same way.
    /// </summary>
    public void SetAppearance(Palette palette)
    {
        Theme.Palette = palette;
        SetStatus(ReferenceEquals(palette, Palette.Dark)
            ? "Dark palette applied - every control recoloured on the next frame."
            : "Light palette applied - every control recoloured on the next frame.");
    }

    /// <summary>
    /// Opens a second window from the UI itself. Windows has no menu bar in this sample, so this is
    /// the only way to reach ownership without a command-line flag there.
    /// </summary>
    private Button BuildOwnedWindowButton()
    {
        var button = new Button("Owned window")
        {
            Dock = XDock.Right,
            Style = ButtonStyle.Secondary,
            AutoSize = true,
            Margin = new Thickness(8f, 20f, 8f, 0f),
        };

        button.AddClickHandler(_ => ShowOwnedWindow());

        return button;
    }

    private Panel BuildFooter()
    {
        var footer = new SurfacePanel
        {
            Dock = XDock.Bottom,
            Height = 64f,
            Border = Border.None,
            Padding = new Thickness(0f),
        };

        _statusLabel = new Label("Ready - click a button, or Tab/Enter to drive the UI from the keyboard.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Thickness(28f, 0f, 12f, 0f),
        };

        var resetButton = new Button("Reset")
        {
            Dock = XDock.Right,
            Style = ButtonStyle.Secondary,
            AutoSize = true,
            Margin = new Thickness(8f, 16f, 28f, 16f),
        };
        resetButton.AddClickHandler(_ =>
        {
            ResetClicks();
        });

        // A collect on demand, so what a page is holding can be compared before and after without
        // waiting for the runtime to choose a moment. The second collect reclaims what the finalizers
        // the first one ran had released.
        var gcButton = new Button("Run GC")
        {
            Dock = XDock.Right,
            Style = ButtonStyle.Secondary,
            AutoSize = true,
            Margin = new Thickness(8f, 16f, 0f, 16f),
        };
        gcButton.AddClickHandler(_ =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            SetStatus($"Collected - {GC.GetTotalMemory(false) / 1024:N0} KB of managed heap in use.");
        });

        // Always visible, not a transient status: a screenshot of this window has to say which
        // presentation path produced whatever number the page is showing. The renderer string is
        // filled in from Shown, when the backend has actually created its surface.
        _gfxLabel = new Label("gfx: -")
        {
            Dock = XDock.Right,
            AutoSize = true,
            // Hidden until a backend reports its path. The headless render harness never calls
            // SetGraphicsInfo, so a --render image stays byte-identical to the one before this
            // label existed - an invisible docked child takes no space.
            Visible = false,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Thickness(8f, 0f, 0f, 0f),
        };

        footer.Controls.Add(_statusLabel);
        footer.Controls.Add(_gfxLabel);
        footer.Controls.Add(resetButton);
        footer.Controls.Add(gcButton);
        return footer;
    }

    /// <summary>
    /// The stage pages, in the order the navigation lists them. The id, the row text and the builder
    /// sit together, so a page cannot be added without a row to select it by or an id to name it by.
    /// </summary>
    /// <remarks>
    /// <b>The id is the page's stable name, and what <c>--page</c> accepts.</b> Without one a
    /// screenshot script has to count rows, so inserting a page anywhere but the end renumbers every
    /// script that names a page after it - and a script that names a page with <c>--page 55</c> says
    /// nothing to a reader about which page it meant. With one the script says <c>--page zoom</c> and
    /// keeps working wherever the row ends up. The position is still a selector, so an id is a
    /// convenience rather than a requirement; ids are compared ordinally, so they are lower case with
    /// hyphens between words.
    /// </remarks>
    private static readonly (string Id, string Title, Func<DemoUi, DemoPage> Create)[] Demos =
    {
        ("split-container", "Split container", demo => new SplitContainerPage(demo)),
        ("stack-panel", "Stack panel", demo => new StackPanelPage(demo)),
        ("text-input", "Text input", demo => new TextInputPage(demo)),
        ("dialogs", "Dialogs", demo => new DialogsPage(demo)),
        ("table-layout", "Table layout", demo => new TableLayoutPage(demo)),
        ("labels-and-text", "Labels and text", demo => new LabelsAndTextPage(demo)),
        ("buttons-and-input", "Buttons and input", demo => new ButtonsAndInputPage(demo)),
        ("check-boxes-and-radios", "Check boxes and radios", demo => new CheckBoxesAndRadiosPage(demo)),
        ("progress-bars", "Progress bars", demo => new ProgressBarsPage(demo)),
        ("group-boxes", "Group boxes", demo => new GroupBoxesPage(demo)),
        ("borders", "Borders", demo => new BordersPage(demo)),
        ("filters", "Filters", demo => new FiltersPage(demo)),
        ("rich-text", "Rich text", demo => new RichTextPage(demo)),
        ("images-and-layout", "Images and layout", demo => new ImagesAndLayoutPage(demo)),
        ("relative-sizing", "Relative sizing", demo => new RelativeSizingPage(demo)),
        ("selection-and-clipboard", "Selection and clipboard", demo => new SelectionAndClipboardPage(demo)),
        ("scrolling", "Scrolling", demo => new ScrollingPage(demo)),
        ("lists", "Lists", demo => new ListsPage(demo)),
        ("performance-list-box", "Performance: list box", demo => new PerformanceListBoxPage(demo)),
        ("performance-controls", "Performance: controls", demo => new PerformanceControlsPage(demo)),
        ("combo-boxes", "Combo boxes", demo => new ComboBoxesPage(demo)),
        ("tab-controls", "Tab controls", demo => new TabControlsPage(demo)),
        ("tool-tips", "Tool tips", demo => new ToolTipsPage(demo)),
        ("list-views", "List views", demo => new ListViewsPage(demo)),
        ("tree-view", "Tree view", demo => new TreeViewPage(demo)),
        ("numeric-up-down", "Numeric up-down", demo => new NumericUpDownPage(demo)),
        ("track-bars", "Track bars", demo => new TrackBarsPage(demo)),
        ("link-labels", "Link labels", demo => new LinkLabelsPage(demo)),
        ("status-and-tool-strips", "Status and tool strips", demo => new ToolStripsPage(demo)),
        ("menus", "Menus", demo => new MenusPage(demo)),
        ("date-and-time", "Date and time", demo => new DateTimePickerPage(demo)),
        ("text-box-autocomplete", "Text box autocomplete", demo => new AutoCompletePage(demo)),
        ("multi-line-text", "Multi-line text", demo => new MultilineTextPage(demo)),
        ("text-box-spans", "Text box spans", demo => new SpanTextPage(demo)),
        ("undo-and-redo", "Undo and redo", demo => new UndoRedoPage(demo)),
        ("fonts-from-a-file", "Fonts from a file", demo => new FontsFromFilePage(demo)),
        ("inherited-appearance", "Inherited appearance", demo => new InheritedAppearancePage(demo)),
        ("tab-order", "Tab order", demo => new TabOrderPage(demo)),
        ("animations", "Animations", demo => new AnimationsPage(demo)),
        ("colour-picker", "Colour picker", demo => new ColorPickerPage(demo)),
        ("font-dialog", "Font dialog", demo => new FontDialogPage(demo)),
        ("colour-dialog", "Colour dialog", demo => new ColorDialogPage(demo)),
        ("split-buttons", "Split buttons", demo => new SplitButtonsPage(demo)),
        ("expander", "Expander", demo => new ExpanderPage(demo)),
        ("wrap-panel", "Wrap panel", demo => new WrapPanelPage(demo)),
        ("busy-indicator-and-overlay", "Busy indicator and overlay", demo => new BusyOverlayPage(demo)),
        ("toast", "Toast", demo => new ToastPage(demo)),
        ("toast-animation", "Toast animation", demo => new ToastAnimationPage(demo)),
        ("overlay-fade", "Overlay fade", demo => new OverlayFadePage(demo)),
        ("svg-icons", "SVG icons", demo => new SvgIconsPage(demo)),
        ("svg-recolour", "SVG recolour", demo => new SvgRecolourPage(demo)),
        ("svg-and-a-raster-twin", "SVG and a raster twin", demo => new SvgRasterTwinPage(demo)),
        ("animated-gif", "Animated GIF", demo => new AnimatedGifPage(demo)),
        ("gif-frames", "GIF frames", demo => new GifFramesPage(demo)),
        ("gif-out-of-view", "GIF out of view", demo => new GifScrollPage(demo)),
        ("animated-webp", "Animated WebP", demo => new AnimatedWebpPage(demo)),
        ("webp-frames", "WebP frames", demo => new WebpFramesPage(demo)),
        ("measured-rendering", "Measured rendering", demo => new MeasuredRenderingPage(demo)),
        ("zoom", "Zoom", demo => new ZoomPage(demo)),
        ("drag-drop-lifetime", "Drag/drop lifetime", demo => new DragLifetimePage(demo)),
        ("chromeless-window", "Chromeless window", demo => new ChromelessWindowPage(demo)),
        ("app-badge", "App badge", demo => new AppBadgePage(demo)),
        ("audio", "Audio", demo => new AudioPage(demo)),
        ("virtual-dom", "Virtual DOM", demo => new VirtualDomPage(demo)),
        ("observables", "Observables", demo => new ObservablesPage(demo)),
        ("observable-list", "Observable list", demo => new ObservableListPage(demo)),
("observable-set", "Observable set", demo => new ObservableSetPage(demo)),
        ("observable-dictionary", "Observable dictionary", demo => new ObservableDictionaryPage(demo)),
        ("effective-in-view", "Effective in view", demo => new EffectiveInViewPage(demo)),

        // graphics-acceleration keeps the position every page above it depends on, so a screenshot
        // script that names a page by --page <position> still finds the same page. New pages go
        // after it for the same reason: a page appended here renumbers nothing above it.
        ("graphics-acceleration", "Graphics acceleration", demo => new GraphicsAccelerationPage(demo)),
        ("popups", "Popups", demo => new PopupPage(demo)),
    };

    /// <summary>
    /// A navigation list down the left and one page at a time on the right. The splitter between
    /// them is draggable, so the navigation can be widened when a demo name does not fit.
    /// </summary>
    private Control BuildContent()
    {
        _pages = new DemoPage[Demos.Length];
        for (int index = 0; index < Demos.Length; index++)
        {
            _pages[index] = Demos[index].Create(this);

            // Set here rather than in each builder, so a page cannot be added to the table without
            // carrying the id the table gives it - the two halves of the name stay together.
            _pages[index].Root.Id = Demos[index].Id;
        }

        _stage = new Panel
        {
            Dock = XDock.Fill,
            Padding = new Thickness(0f),
        };

        _navList = new ListBox
        {
            Dock = XDock.Fill,
            SelectionMode = SelectionMode.One,
            VerticalScroll = ScrollStyle.Automatic,
            HorizontalScroll = ScrollStyle.None,
            Border = SimpleBorder.Thin.WithCornerRadius(4f),
            ItemHeight = 32f,
            EmptyText = "No page matches that.",
        };

        _navList.AddSelectedIndexChangedHandler(_ => ShowNavRow(_navList.SelectedIndex));

        // Sixty-one pages is more than the eye can scan, so the list is what the filter leaves of it.
        // The box takes a tab stop of its own - it is the one part of the navigation a keyboard user
        // has to be able to reach - and hands the keyboard back to the list on Down.
        _navFilter = new TextBox
        {
            Dock = XDock.Top,
            PlaceholderText = "Find a page",
            Height = 30f,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        };

        _navFilter.AddTextChangedHandler(_ => ApplyNavFilter());
        _navFilter.AddKeyDownHandler((_, e) =>
        {
            if (e.Handled)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Down when _navRows.Length > 0:
                    _navList.Focus();
                    e.Handled = true;
                    break;

                case Key.Escape when _navFilter.Text.Length > 0:
                    _navFilter.Text = string.Empty;
                    e.Handled = true;
                    break;
            }
        });

        ApplyNavFilter();

        var nav = new Panel
        {
            Dock = XDock.Fill,
            Padding = new Thickness(20f, 16f, 12f, 16f),
        };
        nav.Controls.Add(_navFilter);
        nav.Controls.Add(_navList);

        // The navigation is window chrome rather than a demo, so it keeps no tab stop of its own:
        // Tab goes straight from the header into the list. The splitter still drags with the mouse.
        var split = new SplitContainer
        {
            Dock = XDock.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 210f,
            Panel1MinSize = 150f,
            Panel1MaxSize = 400f,
            Panel2MinSize = 400f,
            TabStop = false,
        };

        foreach (var panel in new[] { split.Panel1, split.Panel2 })
        {
            panel.Padding = new Thickness(0f);
        }

        split.Panel1.Controls.Add(nav);
        split.Panel2.Controls.Add(_stage);

        _navList.SelectedIndex = 0;
        return split;
    }

    /// <summary>
    /// Rebuilds the navigation from what the filter box holds: every page while it is empty, the
    /// pages whose title contains it otherwise.
    /// </summary>
    /// <remarks>
    /// The page on the stage does not change. Filtering is a way of finding a row, not a way of
    /// choosing one, so a filter that hides the page being read leaves it where it is - and keeps its
    /// row selected when the filter is cleared again.
    /// </remarks>
    private void ApplyNavFilter()
    {
        string filter = _navFilter?.Text.Trim() ?? string.Empty;
        var rows = new List<int>(Demos.Length);

        for (int index = 0; index < Demos.Length; index++)
        {
            if (filter.Length == 0 || Demos[index].Title.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(index);
            }
        }

        _navRows = [.. rows];

        // Replacing the rows resets the selection, which would otherwise be read as a page change.
        _rebuildingNav = true;
        try
        {
            _navList.Items.Clear();
            foreach (var row in _navRows)
            {
                _navList.Items.Add(Demos[row].Title);
            }

            _navList.SelectedIndex = Array.IndexOf(_navRows, _shownIndex);
        }
        finally
        {
            _rebuildingNav = false;
        }
    }

    /// <summary>Shows the page the navigation's row <paramref name="row"/> stands for.</summary>
    /// <param name="row">The row, or -1 when the selection was cleared.</param>
    private void ShowNavRow(int row)
    {
        if (_rebuildingNav || row < 0 || row >= _navRows.Length)
        {
            return;
        }

        ShowPage(_navRows[row]);
    }

    /// <summary>Swaps the page on the stage. Every page is built once and kept, so returning to one costs a re-parent.</summary>
    private void ShowPage(int index)
    {
        if (index < 0 || index >= _pages.Length)
        {
            return;
        }

        var page = _pages[index].Root;
        if (ReferenceEquals(page, _shownPage))
        {
            return;
        }

        if (_shownPage is not null)
        {
            _stage.Controls.Remove(_shownPage);
        }

        _shownPage = page;
        _shownIndex = index;
        _stage.Controls.Add(page);
    }

    /// <summary>
    /// The window the demo is shown in, used to parent the dialogs. It is resolved from the root at
    /// call time rather than from a control on one page: only the page being viewed is attached to
    /// the window, so a control on another page has no <see cref="Control.Window"/> and would make a
    /// dialog opened from here silently do nothing.
    /// </summary>
    internal Window? OwnerWindow => Root.Window;

    /// <summary>The audio engine the application registered, which the audio page drives.</summary>
    internal IAudioEngine? AudioEngine => _chromeWindow?.Application?.Services.GetService<IAudioEngine>();

    /// <summary>
    /// Shows a small window owned by the window hosting the sample. Ownership is what makes a window
    /// behave like a child of another: it stays above its owner, minimizes with it, and closes when
    /// the owner closes.
    /// </summary>
    public void ShowOwnedWindow()
    {
        if (OwnerWindow is not { } owner)
        {
            return;
        }

        var panel = new SurfacePanel
        {
            Padding = new Thickness(16f),
        };

        panel.Controls.Add(new Label(
            "This window is owned by the main window: it stays above it, minimizes with it, and "
            + "closes when it closes.")
        {
            Dock = XDock.Fill,
            WrapText = true,
            TextAlign = ContentAlignment.MiddleCenter,
        });

        var window = new Window("Owned Window", panel)
        {
            ClientSize = new SKSize(440f, 170f),
            MinimumClientSize = new SKSize(300f, 130f),
        };

        window.Show(owner);
        SetStatus("Opened a window owned by this one - it stays above it and closes with it.");
    }

    /// <summary>The page the table built for <typeparamref name="TPage"/>.</summary>
    /// <typeparam name="TPage">The page's type.</typeparam>
    /// <returns>The page.</returns>
    /// <remarks>
    /// The window-level entry points are the ones a screenshot script calls on the sample after it is
    /// constructed: the zoom popup, the benchmark, the appearance demo. Each of those drives one page
    /// rather than the window, so they are forwarded here instead of being duplicated - and the table
    /// is built in the constructor, so every page exists by the time one of them can be called.
    /// </remarks>
    private TPage FindPage<TPage>() where TPage : DemoPage => _pages.OfType<TPage>().First();

    /// <summary>
    /// Switches the running window to an acceleration value, which is what the graphics-acceleration page
    /// does when a row is clicked and what <c>--apply-acceleration</c> calls.
    /// </summary>
    /// <param name="value">A value from the service, such as <c>cpu</c> or <c>d3d:10de-2504-1616</c>.</param>
    /// <returns>One sentence saying what happened, for the status line and the console.</returns>
    /// <remarks>
    /// The service is resolved from the window rather than held, because a page is built before the window
    /// exists and this is also reachable from the command line before anything has been drawn.
    /// </remarks>
    public string ApplyAcceleration(string value)
    {
        if (OwnerWindow is not { } window)
        {
            return "No window to switch.";
        }

        if (window.Services.GetService(typeof(IGpuAccelerationService)) is not IGpuAccelerationService service)
        {
            return "This application registered no IGpuAccelerationService.";
        }

        var applied = service.TryApply(value, out var reason);
        var sentence = applied ? reason : $"Not applied - {reason}";
        SetStatus(sentence);
        return sentence;
    }

    /// <summary>Tells the pages which presentation path produced what is on screen.</summary>
    /// <param name="path">The path the window is rendering through.</param>
    /// <param name="renderer">The backend's own name for its renderer, or null when it does not report one.</param>
    /// <remarks>
    /// Two pages want this: the measured-rendering page, whose numbers mean nothing without it, and the
    /// graphics-acceleration page, which shows it beside the choice the service says is in use.
    /// </remarks>
    public void SetGraphicsInfo(string path, string? renderer)
    {
        FindPage<MeasuredRenderingPage>().SetGraphicsInfo(path, renderer);
        FindPage<GraphicsAccelerationPage>().SetGraphicsInfo(path, renderer);
    }

    /// <summary>Gives the zoom page the scale the application is asking the backend for.</summary>
    /// <param name="scale">The scale, as a multiplier over logical pixels.</param>
    public void SetScaleProvider(Func<float> scale) => FindPage<MeasuredRenderingPage>().SetScaleProvider(scale);

    /// <summary>Starts the frame-time benchmark on the measured-rendering page.</summary>
    /// <param name="partialDamage">Whether to repaint only the animating swatch.</param>
    public void StartBenchmark(bool partialDamage = false) =>
        FindPage<MeasuredRenderingPage>().StartBenchmark(partialDamage);

    /// <summary>Opens the menus page's demo menu, for a screenshot of a menu that is showing.</summary>
    /// <param name="window">The window the menu is drawn in.</param>
    public void OpenMenuDemo(Window window) => FindPage<MenusPage>().OpenMenuDemo(window);

    /// <summary>Opens the span page's demo dropdown, for a screenshot of it with the list showing.</summary>
    /// <param name="window">The window the dropdown is drawn in.</param>
    public void OpenSpanDemo(Window window) => FindPage<SpanTextPage>().OpenSpanDemo(window);

    /// <summary>Puts the caret in the undo page's text box, so a script can type into it.</summary>
    public void FocusUndoDemo() => FindPage<UndoRedoPage>().FocusUndoDemo();

    /// <summary>Sets the size the inherited-appearance page shows, or null to leave it unset.</summary>
    /// <param name="size">The size, in logical pixels.</param>
    public void SetAppearanceDemo(float? size) => FindPage<InheritedAppearancePage>().SetAppearanceDemo(size);

    /// <summary>Chooses which face the inherited-appearance page compares.</summary>
    /// <param name="bundled">True for the bundled face, false for the system one.</param>
    public void SetAppearanceDemoFace(bool bundled) =>
        FindPage<InheritedAppearancePage>().SetAppearanceDemoFace(bundled);

    /// <summary>Sets the zoom page's slider, which is what a zoomed screenshot is taken through.</summary>
    /// <param name="multiplier">The zoom multiplier.</param>
    public void SetPanelZoom(float multiplier) => FindPage<ZoomPage>().SetPanelZoom(multiplier);

    /// <summary>
    /// Opens one of the zoom page's popups at a zoom multiplier, so a screenshot can show a dropdown,
    /// a menu or a tooltip at the scale the zoomed images are taken at.
    /// </summary>
    /// <param name="window">The window the popup is drawn in.</param>
    /// <param name="kind">Which popup: dropdown, menu or tooltip.</param>
    /// <param name="multiplier">The zoom multiplier to set first.</param>
    public void ShowZoomPopup(Window window, string kind, float multiplier = 2f) =>
        FindPage<ZoomPage>().ShowZoomPopup(window, kind, multiplier);

    /// <summary>The footer's renderer readout, which only the measured-rendering page has anything to say about.</summary>
    internal Label? GraphicsReadout => _gfxLabel;

    /// <summary>
    /// Asks the platform to move the window from the press being handled, which is only effective
    /// while the press is still in flight - which is why this runs from the mouse-down itself.
    /// </summary>
    internal void DragWindowFromThisPress()
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

    /// <summary>Shortens dropped text to one status-line's worth.</summary>
    /// <param name="text">The dropped text.</param>
    /// <returns>The text, cut down to sixty characters.</returns>
    internal string Preview(string text)
    {
        var single = text.ReplaceLineEndings(" ");
        return single.Length <= 60 ? single : string.Concat(single.AsSpan(0, 60), "...");
    }

    /// <summary>
    /// Publishes the label that shows the click count. The count belongs to the window rather than to
    /// the buttons page: the footer's Reset clears it while the labels page is the one showing it.
    /// </summary>
    /// <param name="label">The label to keep current.</param>
    internal void ShowClickCount(Label label)
    {
        _counterLabel = label;
        UpdateClickCount();
    }

    /// <summary>Counts one click of the primary action.</summary>
    /// <returns>The count after this click.</returns>
    internal int CountAClick()
    {
        _clicks++;
        UpdateClickCount();
        return _clicks;
    }

    /// <summary>Clears the click count.</summary>
    internal void ResetClicks()
    {
        ResetClicks();
        UpdateClickCount();
    }

    private void UpdateClickCount()
    {
        if (_counterLabel is not null)
        {
            _counterLabel.Text = $"Button clicks: {_clicks}";
        }
    }

    /// <summary>Reports in the window's status line.</summary>
    /// <param name="message">What to say.</param>
    public void SetStatus(string message) => _statusLabel.Text = message;

    /// <summary>
    /// Reports a drop on the window itself: the files that were dragged in from the desktop, or the
    /// text that was dragged in from another application.
    /// </summary>
    private void OnRootDrop(DragEventArgs e)
    {
        var files = e.Data.GetFiles();
        if (files.Length > 0)
        {
            var names = string.Join(", ", files.Select(Path.GetFileName));
            SetStatus($"Dropped {files.Length} file(s): {names}");
            e.Effect = DragDropEffects.Copy;
            return;
        }

        var text = e.Data.GetText();
        if (!string.IsNullOrEmpty(text))
        {
            SetStatus($"Dropped text: {Preview(text)}");
            e.Effect = DragDropEffects.Copy;
            return;
        }

        SetStatus("Dropped something this sample cannot read.");
        e.Effect = DragDropEffects.None;
    }
}
