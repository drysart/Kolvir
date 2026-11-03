using System.Diagnostics;
using SkiaSharp;
using XPlatUI;
using XPlatUI.Backend.Headless;
using XPlatUI.DragDrop;
using XDock = XPlatUI.DockStyle;

namespace XPlatUI.Demo;

/// <summary>
/// The sample XPlat UI shown by the harnesses. It is kept separate from any host window so the
/// very same control tree is rendered by the Win32 harness, by the GTK4 harness, and headlessly
/// (see <see cref="RenderToPng"/>).
/// </summary>
public sealed class DemoUi
{
    private XPlatLabel _statusLabel = null!;
    private XPlatLabel _counterLabel = null!;
    private XPlatButton _primaryButton = null!;
    private XPlatButton _dialogAnchor = null!;
    private XPlatListBox _navList = null!;
    private XPlatPanel _stage = null!;
    private XPlatControl[] _pages = null!;
    private XPlatControl? _shownPage;
    private int _clicks;

    public DemoUi()
    {
        Root = BuildUi();
    }

    /// <summary>The root of the sample control tree.</summary>
    public XPlatControl Root { get; }

    /// <summary>Shows the page that holds the first text field and puts the caret in it, which then blinks.</summary>
    public void FocusFirstTextBox()
    {
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
    public static void RenderToPng(string path, int width, int height, int page = -1)
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

        window.AddShownHandler(_ => backend.LastWindow!.SavePng(path));
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

        header.Controls.Add(new XPlatLabel("XPlat UI")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 44f,
            Font = XPlatTheme.TitleFont,
            TextAlign = XPlatContentAlignment.BottomLeft,
            Margin = new XPlatThickness(28f, 16f, 28f, 0f),
        });

        header.Controls.Add(new XPlatLabel("A lightweight, AOT-friendly, cross-platform control layer rendered with SkiaSharp.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 22f,
            Font = XPlatTheme.CaptionFont,
            ForeColorRole = XPlatThemeColor.MutedForeground,
            TextAlign = XPlatContentAlignment.TopLeft,
            Margin = new XPlatThickness(28f, 4f, 28f, 0f),
        });

        return header;
    }

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

        footer.Controls.Add(_statusLabel);
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

        _dialogAnchor = messageBox;

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

    /// <summary>The window the demo is shown in, used to parent the dialogs.</summary>
    private XPlatWindow? OwnerWindow => _dialogAnchor.Window;

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

        card.Controls.Add(ScrollCaption("Marquee"));
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

        // A disabled group box greys its caption and its frame. Enabled does not cascade to the
        // children yet, so they are disabled here as well rather than left looking live.
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
                Enabled = false,
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
            DisposeImageWithControl = true,
        };
        image.Image = CreateGradientImage(320, 200);
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

    /// <summary>Shows <paramref name="message"/> in the footer's status line.</summary>
    /// <param name="message">The text to show.</param>
    /// <remarks>
    /// Public so a host can report something the sample UI did not cause itself, such as a second
    /// launch being handed to the running window.
    /// </remarks>
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

    private static SKImage CreateGradientImage(int width, int height)
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
        return surface.Snapshot();
    }
}
