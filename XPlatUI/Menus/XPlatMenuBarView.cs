using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// The menu bar drawn inside the window, above the control tree: a strip of top-level menus that
/// drop down into <see cref="XPlatMenuView"/>s. Windows and Linux use it; macOS keeps its own
/// system-wide bar, because AppKit owns that strip and the standard Edit and application menus come
/// with it.
/// </summary>
/// <remarks>
/// <para>
/// The bar belongs to the window rather than to a control, so <see cref="XPlatUiHost"/> lays it out
/// above the root and paints it after it. It is not focusable and takes no part in tab order: a menu
/// bar is reached with the pointer or with a mnemonic, and an open menu takes the keyboard through
/// <see cref="IXPlatInputSink"/> instead.
/// </para>
/// <para>
/// The drop-down is an <see cref="XPlatPopup"/>, so it can hang past the bottom of the window when
/// the bar is near the bottom of the screen. Pressing a title while a menu is open switches menus
/// rather than closing and reopening, which is what the owner-click dismissal in the popup layer
/// leaves to do here.
/// </para>
/// </remarks>
public sealed class XPlatMenuBarView : XPlatControl
{
    /// <summary>Gap between the window edge and the first title, and after the last one.</summary>
    private const float Inset = 2f;

    /// <summary>Space either side of a title's text inside its button.</summary>
    private const float TitlePadding = 10f;

    private readonly List<SKRect> _titles = new();
    private XPlatMenuBar? _menuBar;
    private XPlatPopup? _popup;
    private int _hovered = -1;
    private int _open = -1;

    /// <summary>Creates an empty bar. Assign <see cref="MenuBar"/> to fill it.</summary>
    public XPlatMenuBarView()
    {
        TabStop = false;
        Focusable = false;
        Selectable = false;
        Cursor = XPlatCursor.Default;
    }

    /// <summary>Creates a bar showing <paramref name="menuBar"/>.</summary>
    public XPlatMenuBarView(XPlatMenuBar menuBar)
        : this()
    {
        MenuBar = menuBar;
    }

    /// <summary>The bar this view shows, or null for an empty strip.</summary>
    public XPlatMenuBar? MenuBar
    {
        get => _menuBar;
        set
        {
            if (ReferenceEquals(_menuBar, value))
            {
                return;
            }

            CloseMenu();
            _menuBar = value;
            _hovered = -1;
            _titles.Clear();
            Invalidate();
        }
    }

    /// <summary>Height of the strip, in logical pixels, before the separating line.</summary>
    public float TitleHeight { get; set; } = 28f;

    /// <summary>Label font, or null for <see cref="XPlatTheme.DefaultFont"/>.</summary>
    public XPlatFont? Font { get; set; }

    /// <summary>The top-level menu whose drop-down is open, or -1 when none is.</summary>
    public int OpenIndex => _open;

    /// <summary>True while a drop-down is open.</summary>
    public bool IsMenuOpen => _open >= 0;

    /// <summary>
    /// Handles an Alt+mnemonic press, which is the keyboard's way into a menu bar: the bar is not
    /// focusable, so Alt is what reaches it at all.
    /// </summary>
    /// <param name="key">The key that was pressed.</param>
    /// <param name="modifiers">The modifiers held at the time.</param>
    /// <returns>True when the press opened a menu and must not go any further.</returns>
    public bool ProcessKeyDown(XPlatKey key, XPlatModifiers modifiers)
    {
        if ((modifiers & XPlatModifiers.Alt) == 0 || _menuBar is not { } bar)
        {
            return false;
        }

        if (XPlatMenuText.KeyToMnemonic(key) is not { } letter)
        {
            return false;
        }

        for (var i = 0; i < bar.Menus.Count && i < _titles.Count; i++)
        {
            if (bar.Menus[i].Mnemonic != letter)
            {
                continue;
            }

            OpenMenu(i);
            return IsMenuOpen;
        }

        return false;
    }

    /// <summary>Opens the drop-down of the menu at <paramref name="index"/>.</summary>
    public void OpenMenu(int index)
    {
        if (_menuBar is not { } bar || index < 0 || index >= bar.Menus.Count || index >= _titles.Count)
        {
            return;
        }

        var menu = bar.Menus[index];

        if (menu.Items.Count == 0)
        {
            return;
        }

        CloseMenu();

        if (Host is not { } host)
        {
            return;
        }

        _open = index;
        Invalidate();

        var view = new XPlatMenuView(menu.TrimmedItems);
        var location = new SKPoint(_titles[index].Left, Bounds.Bottom);

        _popup = XPlatMenuPresenter.ShowMenu(host, view, location, OnMenuClosed);
    }

    /// <summary>Closes the open drop-down, if there is one.</summary>
    public void CloseMenu()
    {
        if (_popup is not { } popup)
        {
            return;
        }

        _popup = null;
        _open = -1;
        Invalidate();

        // Closing raises the closed handler, which finds _popup already cleared and only has the
        // paint to refresh.
        popup.Close();
    }

    /// <summary>Measures the strip: as wide as the titles and as tall as <see cref="TitleHeight"/>.</summary>
    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var font = Font ?? XPlatTheme.DefaultFont;
        var x = Inset;

        _titles.Clear();

        if (_menuBar is { } bar)
        {
            foreach (var menu in bar.Menus)
            {
                var width = MathF.Ceiling(font.MeasureWidth(menu.DisplayText) + (TitlePadding * 2f));
                _titles.Add(SKRect.Create(x, 0f, width, TitleHeight));
                x += width;
            }
        }

        return new SKSize(x + Inset, TitleHeight + 1f);
    }

    /// <summary>Paints the strip, the titles and the highlight on the open or hovered one.</summary>
    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        if (_titles.Count == 0 && _menuBar is not null)
        {
            MeasureContent(rect.Size);
        }

        using (var background = new SKPaint { Color = XPlatTheme.Background, IsAntialias = false })
        {
            canvas.DrawRect(rect, background);
        }

        using (var edge = new SKPaint { Color = XPlatTheme.Border, IsAntialias = false })
        {
            canvas.DrawRect(SKRect.Create(rect.Left, rect.Bottom - 1f, rect.Width, 1f), edge);
        }

        if (_menuBar is not { } bar)
        {
            return;
        }

        var font = Font ?? XPlatTheme.DefaultFont;
        using var pill = new SKPaint { IsAntialias = true };
        using var text = new SKPaint { IsAntialias = true, Color = XPlatTheme.Foreground };

        for (var i = 0; i < _titles.Count && i < bar.Menus.Count; i++)
        {
            var title = _titles[i];

            if (i == _open)
            {
                pill.Color = XPlatTheme.Surface;
                canvas.DrawRoundRect(title, 4f, 4f, pill);
            }
            else if (i == _hovered)
            {
                var foreground = XPlatTheme.Foreground;
                pill.Color = new SKColor(foreground.Red, foreground.Green, foreground.Blue, XPlatMenuRenderer.HoverWashAlpha);
                canvas.DrawRoundRect(title, 4f, 4f, pill);
            }

            var baseline = title.MidY + (font.BaselineOffset - (font.LineHeight * 0.5f));
            canvas.DrawText(bar.Menus[i].DisplayText, title.Left + TitlePadding, baseline, SKTextAlign.Left, font.SkFont, text);
        }
    }

    /// <inheritdoc />
    protected override void OnMouseMove(XPlatMouseEventArgs e)
    {
        base.OnMouseMove(e);

        var index = HitTestTitle(e.Location);

        if (index == _hovered)
        {
            return;
        }

        _hovered = index;
        Invalidate();

        // Once a menu is down, sliding along the bar switches menus the way a native one does,
        // without a click and without closing and reopening the popup in between.
        if (_open >= 0 && index >= 0 && index != _open)
        {
            OpenMenu(index);
        }
    }

    /// <inheritdoc />
    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();

        if (_hovered < 0)
        {
            return;
        }

        _hovered = -1;
        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnMouseDown(XPlatMouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != XPlatMouseButtons.Left)
        {
            return;
        }

        var index = HitTestTitle(e.Location);

        if (index < 0)
        {
            return;
        }

        if (index == _open)
        {
            CloseMenu();
        }
        else
        {
            OpenMenu(index);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseMenu();
        }

        base.Dispose(disposing);
    }

    private void OnMenuClosed()
    {
        _popup = null;
        _open = -1;
        Invalidate();
    }

    private int HitTestTitle(SKPoint location)
    {
        for (var i = 0; i < _titles.Count; i++)
        {
            if (_titles[i].Contains(location))
            {
                return i;
            }
        }

        return -1;
    }
}
