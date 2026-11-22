using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The menus page: the menu bar, a context menu and the items that report themselves.
/// </summary>
internal sealed class MenusPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public MenusPage(DemoUi shell) : base(shell) => Initialize(Build());

    private Control? _menuDemoTarget;

    /// <summary>
    /// The in-library menus. The bar across the top of the window is drawn by the library on
    /// Windows and Linux and reachable with Alt plus a mnemonic; this page adds a context menu that
    /// uses every row shape the renderer knows - icon, check mark, submenu, shortcut hint, disabled
    /// entry and a hosted control.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Menus"));

        var readout = ScrollCaption(
            "Alt+V and Alt+W open the window's own bar. Right-click the box for a context menu.");
        readout.Margin = new Thickness(0f, 0f, 0f, 12f);
        card.Controls.Add(readout);

        var target = new SurfacePanel
        {
            Dock = XDock.Top,
            Height = 96f,
            Border = Framed(8f),
            Margin = new Thickness(0f, 0f, 0f, 12f),
        };

        target.Controls.Add(new Label("Right-click anywhere in this box")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = ThemeColor.MutedForeground,
            Font = Theme.CaptionFont,
        });

        target.ContextMenu = BuildMenuDemoContextMenu(readout);
        _menuDemoTarget = target;
        card.Controls.Add(target);

        // The bar belongs to the window rather than to a control, so opening one of its menus is a
        // host-level call - which is also how Alt plus a mnemonic gets there.
        Button? openView = null;
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

        card.Controls.Add(new Label(
            "The bar is an ordinary control in the window's own tree, so it scrolls, resizes and repaints " +
            "with everything else; a drop-down is a popup surface that holds an MenuView. Mnemonics come " +
            "from an & in the text, submenus open to the side of their parent row and nest as deeply as the " +
            "application likes - Recent > Program.cs is two of them - and a menu taller than the room it has " +
            "scrolls its rows behind arrows at either end. A row may host a control instead of a label: the " +
            "zoom slider is an TrackBar living inside the menu.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 92f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>
    /// The context menu the menus page shows: one row of every shape the renderer supports, so a
    /// screenshot of the open menu covers the whole of it, plus a submenu inside a submenu and one
    /// menu with more rows than any display can show at once.
    /// </summary>
    private ContextMenu BuildMenuDemoContextMenu(Label readout)
    {
        var grid = new MenuItem("Show &grid", () =>
            readout.Text = "'Show grid' picked - the tick is drawn in the check column.")
        {
            Checked = true,
        };

        var recent = new MenuItem("&Recent");

        // A submenu inside a submenu, so the chain is exercised at a depth a single level cannot
        // reach: Recent > Program.cs > a place in the file.
        var program = new MenuItem("&Program.cs");
        foreach (var place in new[] { "The top of the file", "Main", "RenderSample" })
        {
            var spot = place;
            program.SubItems.Add(new MenuItem(
                spot, () => readout.Text = $"'Recent > Program.cs > {spot}' picked."));
        }

        recent.SubItems.Add(program);

        foreach (var name in new[] { "DemoUi.cs", "Program.cs (older)" })
        {
            var file = name;
            recent.SubItems.Add(new MenuItem(file, () => readout.Text = $"'Recent > {file}' picked."));
        }

        // More rows than fit on any display, so the menu keeps the height it is given and scrolls
        // its rows instead of growing past the edge - which is what the arrows at either end, the
        // wheel and the keyboard keeping the highlight in view are for.
        var scrolling = new MenuItem("&Scroll test");
        for (int number = 1; number <= 320; number++)
        {
            int picked = number;
            scrolling.SubItems.Add(new MenuItem(
                $"Item {picked:000}",
                () => readout.Text = $"'Scroll test > Item {picked:000}' picked."));
        }

        var zoom = new TrackBar
        {
            Minimum = 50f,
            Maximum = 200f,
            Value = 100f,
            TickFrequency = 25f,
            Margin = new Thickness(0f),
        };

        zoom.AddValueChangedHandler(_ =>
            readout.Text = $"Zoom is {zoom.Value:0}% - a control hosted in a menu row.");

        return new ContextMenu()
            .Add(new MenuItem("&New file", () => readout.Text = "'New file' picked.")
            {
                Image = MenuGlyph((canvas, paint) => canvas.DrawRoundRect(SKRect.Create(7f, 4f, 18f, 24f), 3f, 3f, paint)),
                Shortcut = new Shortcut(Key.N, Modifiers.Control),
            })
            .Add(new MenuItem("&Open...", () => readout.Text = "'Open' picked.")
            {
                Image = MenuGlyph((canvas, paint) =>
                {
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 2.5f;
                    canvas.DrawRect(SKRect.Create(5f, 10f, 22f, 16f), paint);
                    canvas.DrawLine(5f, 10f, 11f, 5f, paint);
                    canvas.DrawLine(11f, 5f, 16f, 10f, paint);
                }),
                Shortcut = new Shortcut(Key.O, Modifiers.Control),
            })
            .AddSeparator()
            .Add(recent)
            .Add(scrolling)
            .Add(grid)
            .AddSeparator()
            .Add(new MenuItem("Zoom") { Control = zoom })
            .AddSeparator()
            .Add(new MenuItem("&Delete", null)
            {
                Enabled = false,
                Shortcut = new Shortcut(Key.Delete),
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
        using var paint = new SKPaint { IsAntialias = true, Color = Theme.Foreground };
        draw(surface.Canvas, paint);
        surface.Canvas.Flush();
        return surface.Snapshot();
    }

    /// <summary>
    /// Opens the menus page's context menu as if the box had been right-clicked, which is what a
    /// screenshot of the page needs in order to show a menu at all.
    /// </summary>
    /// <param name="window">The window the sample is showing in.</param>
    public void OpenMenuDemo(Window window)
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
        var view = new MenuView(menu);
        MenuPresenter.ShowMenu(window.Host, view, new SKPoint(location.X + 48f, location.Y + 24f));

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
}
