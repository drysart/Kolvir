using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// A menu drawn with Skia: the portable half of every menu in the library. It lays its items out
/// with <see cref="XPlatMenuRenderer"/>, tracks the row under the pointer, opens submenus, answers
/// to the keyboard and raises <see cref="AddInvokedHandler"/> when the user picks something.
/// </summary>
/// <remarks>
/// <para>
/// A view is not a window. A presenter puts it in an <see cref="XPlatPopup"/> - which gives it a
/// surface that can hang past the edges of the owning window - and pushes it onto the owning window
/// as an <see cref="IXPlatInputSink"/> so the arrow keys, Enter, Escape and the mnemonics reach it.
/// That division is what lets the same menu be a context menu, a drop-down under a menu bar, or a
/// submenu, with no platform code anywhere.
/// </para>
/// <para>
/// A submenu is a view of its own too, joined to the row that opened it and given its own surface.
/// It is not drawn inside this one: a menu is placed once, from the row it hangs off, so a submenu
/// with more rows than the room below that row scrolls instead of pushing the menu the user is
/// pointing at out from under the pointer. <see cref="XPlatMenuPresenter"/> owns that chain - this
/// view only says which submenu is open, through <see cref="SubMenuOpened"/>.
/// </para>
/// <para>
/// Keys and dismissal are still answered by the deepest view that is open, which is why the
/// outermost view is the one pushed onto the owning window as an <see cref="IXPlatInputSink"/>.
/// </para>
/// </remarks>
public sealed class XPlatMenuView : XPlatControl, IXPlatInputSink
{
    /// <summary>
    /// How far a submenu overlaps the menu that opened it, so the two rounded corners meet without
    /// leaving a notch between them. <see cref="XPlatMenuPresenter"/> places the submenu's popup
    /// with it.
    /// </summary>
    internal const float SubMenuOverlap = 4f;

    private readonly List<XPlatControl> _hostedControls = new();
    private IReadOnlyList<XPlatMenuItem> _items = Array.Empty<XPlatMenuItem>();
    private XPlatMenuLayout? _layout;
    private XPlatMenuView? _parent;
    private XPlatMenuView? _subMenu;
    private XPlatUiHost? _ownerHost;
    private IDisposable? _sink;
    private Action<object, XPlatMenuItem>? _invoked;
    private Action<object>? _dismissed;
    private int _subMenuIndex = -1;
    private int _hovered = -1;
    private int _pressed = -1;
    private bool _laying;
    private float? _maximumSurfaceHeight;
    private float _scroll;

    /// <summary>Creates an empty menu view. Fill <see cref="Items"/> with <see cref="SetItems"/>.</summary>
    public XPlatMenuView()
    {
        Initialize();
    }

    /// <summary>Creates a menu view showing <paramref name="items"/>.</summary>
    public XPlatMenuView(IEnumerable<XPlatMenuItem> items)
    {
        Initialize();
        SetItems(items);
    }

    /// <summary>Creates a menu view showing the items of <paramref name="menu"/>.</summary>
    public XPlatMenuView(XPlatContextMenu menu)
        : this((menu ?? throw new ArgumentNullException(nameof(menu))).Items)
    {
    }

    private void Initialize()
    {
        TabStop = false;
        Focusable = false;
        Selectable = false;
        Cursor = XPlatCursor.Default;
        BackColor = null;
    }

    /// <summary>The items shown, in order, with the separators that would look wrong already dropped.</summary>
    public IReadOnlyList<XPlatMenuItem> Items => _items;

    /// <summary>Row metrics, or null for <see cref="XPlatMenuMetrics.Default"/>.</summary>
    public XPlatMenuMetrics? Metrics { get; set; }

    /// <summary>
    /// Opacity of the surface, 0 to 1. Below 1 the menu is translucent so a compositor-supplied
    /// backdrop - the Windows 11 acrylic blur - shows through it.
    /// </summary>
    public float SurfaceAlpha { get; set; } = 1f;

    /// <summary>
    /// True to reserve room for the drop shadow. A submenu keeps one too, so it reads as a surface
    /// of its own rather than a patch of the menu that opened it.
    /// </summary>
    public bool ShowShadow { get; set; } = true;

    /// <summary>
    /// How far this view's own surface is inset inside its bounds: the margin it keeps for the drop
    /// shadow, or zero when it has none. A presenter offsets the popup by this much so that the
    /// surface - not the transparent shadow margin - lands where the caller asked for it.
    /// </summary>
    public float SurfaceInset => ShowShadow ? (Metrics ?? XPlatMenuMetrics.Default).ShadowExtent : 0f;

    /// <summary>
    /// The tallest this menu's surface may be, shadow margin excluded, or null for no limit. A menu
    /// whose rows do not fit is capped to this height and scrolls instead of growing past it: the
    /// caller sets it from the space the display has, and a parent menu sets it on its submenus from
    /// the room they have below the row that opened them.
    /// </summary>
    public float? MaximumSurfaceHeight
    {
        get => _maximumSurfaceHeight;
        set
        {
            if (Nullable.Equals(_maximumSurfaceHeight, value))
            {
                return;
            }

            _maximumSurfaceHeight = value;
            _layout = null;
            Invalidate();
        }
    }

    /// <summary>
    /// How far the rows of a scrolling menu are shifted up, from zero to
    /// <see cref="XPlatMenuLayout.MaxScroll"/>. Always zero for a menu that fits the space it was
    /// given, which is most of them.
    /// </summary>
    public float ScrollOffset => _scroll;

    /// <summary>
    /// Scrolls the rows so that <paramref name="offset"/> is the first pixel of content shown,
    /// clamped to what the menu has room for. Does nothing to a menu that fits its space.
    /// </summary>
    public void ScrollTo(float offset)
    {
        EnsureLayout();

        var clamped = Math.Clamp(offset, 0f, _layout?.MaxScroll ?? 0f);

        if (MathF.Abs(clamped - _scroll) < 0.01f)
        {
            return;
        }

        _scroll = clamped;


        Invalidate();
        PerformLayout();
    }

    /// <summary>Scrolls the rows by <paramref name="delta"/> pixels, positive scrolling towards the last row.</summary>
    public void ScrollBy(float delta) => ScrollTo(_scroll + delta);

    /// <summary>Scrolls the rows just far enough that the row at <paramref name="index"/> is fully shown.</summary>
    public void ScrollRowIntoView(int index)
    {
        EnsureLayout();

        if (_layout is not { IsScrollable: true } layout || index < 0 || index >= layout.Rows.Count)
        {
            return;
        }

        var row = layout.Rows[index].Bounds;
        var viewport = layout.ContentViewport;

        if (row.Top - _scroll < viewport.Top)
        {
            ScrollTo(row.Top - viewport.Top);
        }
        else if (row.Bottom - _scroll > viewport.Bottom)
        {
            ScrollTo(row.Bottom - viewport.Bottom);
        }
    }

    /// <summary>
    /// True when this menu was opened to the left of the menu that opened it, because the display
    /// had no room to its right. Its own submenus then open to the left as well, so a chain of them
    /// does not zigzag back over the menus it came from.
    /// </summary>
    public bool OpenedToLeft { get; internal set; }

    /// <summary>The menu that opened this one, or null for a root menu.</summary>
    public XPlatMenuView? ParentMenu => _parent;

    /// <summary>The outermost menu of the chain this view belongs to.</summary>
    public XPlatMenuView RootMenu => _parent?.RootMenu ?? this;

    /// <summary>The innermost open menu of the chain, which is the one that answers the keyboard.</summary>
    public XPlatMenuView DeepestMenu => _subMenu?.DeepestMenu ?? this;

    /// <summary>The open menus from the root down to this one.</summary>
    public IReadOnlyList<XPlatMenuView> MenuChain
    {
        get
        {
            var chain = new List<XPlatMenuView>();
            for (var view = this; view is not null; view = view._parent)
            {
                chain.Insert(0, view);
            }

            return chain;
        }
    }

    /// <summary>The item under the highlight, or null when no row is highlighted.</summary>
    public XPlatMenuItem? HighlightedItem =>
        _layout is { } layout && _hovered >= 0 && _hovered < layout.Rows.Count
            ? layout.Rows[_hovered].Item
            : null;

    /// <summary>The layout of the last measure, or null before the first one.</summary>
    public XPlatMenuLayout? Layout => _layout;

    /// <summary>Raised when the user picks an item, after the menu has closed itself.</summary>
    public IDisposable AddInvokedHandler(Action<object, XPlatMenuItem> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _invoked += handler;
        return new XPlatHandlerToken<Action<object, XPlatMenuItem>>(RemoveInvokedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddInvokedHandler"/> added.</summary>
    public void RemoveInvokedHandler(Action<object, XPlatMenuItem> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _invoked -= handler;
    }

    /// <summary>Raised when the user dismisses the menu without picking anything.</summary>
    public IDisposable AddDismissedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _dismissed += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveDismissedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddDismissedHandler"/> added.</summary>
    public void RemoveDismissedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _dismissed -= handler;
    }

    /// <summary>
    /// Raised once a submenu has been opened, with the menu that opened it, the submenu itself and
    /// the row it hangs off. <see cref="XPlatMenuPresenter"/> answers by giving the submenu a popup
    /// of its own; a menu shown without a presenter simply has no submenus.
    /// </summary>
    internal event Action<XPlatMenuView, XPlatMenuView, int>? SubMenuOpened;

    /// <summary>Raised when a submenu is closing, before it is let go of.</summary>
    internal event Action<XPlatMenuView, XPlatMenuView>? SubMenuClosed;

    /// <summary>Replaces the items, dropping any submenu that is open.</summary>
    public void SetItems(IEnumerable<XPlatMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        CloseSubMenu();
        _items = XPlatMenuTrimming.Trim(items as IReadOnlyList<XPlatMenuItem> ?? items.ToList());
        _layout = null;
        _hovered = -1;
        _pressed = -1;
        Invalidate();
    }

    /// <summary>
    /// Registers this menu as an input sink on <paramref name="ownerHost"/> - the window that owns
    /// the menu, not the popup surface it is drawn on - so keys reach it while it is open.
    /// </summary>
    public void Attach(XPlatUiHost ownerHost)
    {
        ArgumentNullException.ThrowIfNull(ownerHost);

        if (ReferenceEquals(_ownerHost, ownerHost))
        {
            return;
        }

        Detach();
        _ownerHost = ownerHost;
        _sink = ownerHost.AddInputSink(this);
    }

    /// <summary>Stops listening for keys.</summary>
    public void Detach()
    {
        _sink?.Dispose();
        _sink = null;
        _ownerHost = null;
    }

    /// <summary>Highlights the first item that can be picked.</summary>
    public void HighlightFirst()
    {
        EnsureLayout();

        if (_layout is { FirstPickable: >= 0 } layout)
        {
            Highlight(layout.FirstPickable);
        }
    }

    /// <summary>Highlights the last item that can be picked.</summary>
    public void HighlightLast()
    {
        EnsureLayout();

        if (_layout is not { } layout || layout.FirstPickable < 0)
        {
            return;
        }

        var index = layout.FirstPickable;

        for (var i = layout.Rows.Count - 1; i >= 0; i--)
        {
            if (layout.Rows[i].IsPickable)
            {
                index = i;
                break;
            }
        }

        Highlight(index);
    }

    /// <summary>Moves the highlight to the next pickable row, wrapping the way a native menu does.</summary>
    public void HighlightNext()
    {
        EnsureLayout();

        if (_layout is not { } layout || layout.FirstPickable < 0)
        {
            return;
        }

        Highlight(layout.NextPickable(_hovered, 1));
    }

    /// <summary>Moves the highlight to the previous pickable row, wrapping the way a native menu does.</summary>
    public void HighlightPrevious()
    {
        EnsureLayout();

        if (_layout is not { } layout || layout.FirstPickable < 0)
        {
            return;
        }

        var from = _hovered < 0 ? 0 : _hovered;
        Highlight(layout.NextPickable(from, -1));
    }

    /// <summary>Highlights the row at <paramref name="index"/> and opens or closes submenus to match.</summary>
    public void Highlight(int index)
    {
        if (_layout is not { } layout || index < 0 || index >= layout.Rows.Count)
        {
            return;
        }

        if (_hovered == index)
        {
            return;
        }

        _hovered = index;
        Invalidate();

        // Reached from the keyboard as well as the pointer, and a menu that scrolls has to follow the
        // highlight or the arrow keys would walk into rows that are not on screen.
        ScrollRowIntoView(index);

        if (layout.Rows[index].IsSubMenu)
        {
            OpenSubMenu(index);
        }
        else
        {
            CloseSubMenu();
        }
    }

    /// <summary>Opens the submenu of the item at <paramref name="index"/>.</summary>
    public bool OpenSubMenu(int index)
    {
        EnsureLayout();

        if (_layout is not { } layout || index < 0 || index >= layout.Rows.Count)
        {
            return false;
        }

        var item = layout.Rows[index].Item;

        if (!item.HasSubMenu)
        {
            return false;
        }

        if (_subMenu is not null && _subMenuIndex == index)
        {
            return true;
        }

        CloseSubMenu();

        var child = new XPlatMenuView(item.SubItems)
        {
            Metrics = Metrics,
            Font = Font,
            SurfaceAlpha = SurfaceAlpha,
            ShowShadow = ShowShadow,
            _parent = this,
        };

        _subMenu = child;
        _subMenuIndex = index;

        Invalidate();
        SubMenuOpened?.Invoke(this, child, index);
        return true;
    }

    /// <summary>
    /// Closes the open submenu and everything opened from it, if there is one. The view that
    /// answered <see cref="SubMenuOpened"/> takes the submenu's surface away; the submenu is then
    /// disposed here, once nothing is drawing it any more.
    /// </summary>
    public void CloseSubMenu()
    {
        if (_subMenu is not { } child)
        {
            return;
        }

        _subMenu = null;
        _subMenuIndex = -1;

        Invalidate();

        child.CloseSubMenu();
        SubMenuClosed?.Invoke(this, child);
        child.Dispose();
    }

    /// <summary>Picks the item at <paramref name="index"/>, opening its submenu or invoking it.</summary>
    public bool Activate(int index)
    {
        EnsureLayout();

        if (_layout is not { } layout || index < 0 || index >= layout.Rows.Count)
        {
            return false;
        }

        var row = layout.Rows[index];

        if (row.HasControl || row.Item.IsSeparator || !row.Item.Enabled)
        {
            return false;
        }

        if (row.IsSubMenu)
        {
            Highlight(index);
            return true;
        }

        RootMenu.RaiseInvoked(row.Item);
        return true;
    }

    /// <summary>Picks the item under the highlight.</summary>
    public bool ActivateHighlighted() => _hovered >= 0 && Activate(_hovered);

    /// <summary>Asks the outermost menu to close, as Escape does.</summary>
    public void Dismiss() => RootMenu.RaiseDismissed();

    /// <inheritdoc />
    public bool ProcessKeyDown(XPlatKey key, XPlatModifiers modifiers)
    {
        var deepest = DeepestMenu;

        // Escape closes the menu from wherever the keyboard focus is, including from a control
        // hosted in a row. It takes one level away per press, starting at the innermost submenu, so
        // a chain three deep unwinds the way the Left arrow does and only the outermost dismisses.
        if (key == XPlatKey.Escape)
        {
            if (deepest._subMenu is not null)
            {
                deepest.CloseSubMenu();
            }
            else if (deepest.ParentMenu is { } parent)
            {
                parent.CloseSubMenu();
            }
            else
            {
                Dismiss();
            }

            return true;
        }

        // A control hosted in a row owns the keyboard while it has the focus. The popup window is
        // deliberately not focusable, so the window that has the keyboard forwards to it through here.
        if (deepest.ForwardToHostedControl(key, modifiers))
        {
            return true;
        }

        return deepest.HandleNavigationKey(key, modifiers) || deepest.HandleMnemonic(key);
    }

    /// <inheritdoc />
    public bool ProcessTextInput(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        if (Host is { FocusedControl: { } focused } && !ReferenceEquals(focused, this))
        {
            focused.RaiseTextInput(new XPlatTextInputEventArgs(text));
            return true;
        }

        // Mnemonics are answered from the key press, so typed text is only swallowed here: it must
        // not reach the control the menu was opened over.
        return true;
    }

    /// <summary>
    /// Measures this menu's own surface. A submenu is a surface of its own, so nothing of it is
    /// measured here.
    /// </summary>
    protected override SKSize MeasureContent(SKSize availableSize) => MeasureOwn(availableSize.Width);

    /// <summary>
    /// Measures this menu's own surface - the menu itself, without the margin it keeps for its drop
    /// shadow and without any submenu - as it would be laid out in <paramref name="availableWidth"/>.
    /// </summary>
    public SKSize MeasureSurface(float availableWidth)
    {
        MeasureOwn(MathF.Max(0f, availableWidth - Padding.Horizontal));
        return _layout?.SurfaceSize ?? SKSize.Empty;
    }

    /// <summary>Hosts any in-menu controls in the rows that carry one.</summary>
    protected override void OnLayout()
    {
        if (_laying)
        {
            return;
        }

        _laying = true;

        try
        {
            if (_layout is not { } layout)
            {
                return;
            }

            foreach (var row in layout.Rows)
            {
                if (row.Item.Control is { } control)
                {
                    HostControl(control, Offset(row.ControlBounds));
                }
            }
        }
        finally
        {
            _laying = false;
        }
    }

    /// <summary>Paints the surface, the rows and the highlight.</summary>
    protected override void OnPaint(SKCanvas canvas, SKRect rect)
    {
        EnsureLayout();

        if (_layout is not { } layout)
        {
            return;
        }

        XPlatMenuRenderer.Paint(canvas, layout, _hovered, SurfaceAlpha, Metrics, Font, null, _scroll);
    }

    /// <inheritdoc />
    protected override void OnMouseMove(XPlatMouseEventArgs e)
    {
        base.OnMouseMove(e);

        var index = HitTestRow(e.Location);

        if (index == _hovered)
        {
            return;
        }

        Highlight(index);
    }

    /// <inheritdoc />
    protected override void OnMouseDown(XPlatMouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != XPlatMouseButtons.Left)
        {
            return;
        }

        if (ScrollFromIndicator(e.Location))
        {
            return;
        }

        _pressed = HitTestRow(e.Location);
    }

    /// <inheritdoc />
    protected override void OnMouseWheel(XPlatMouseEventArgs e)
    {
        base.OnMouseWheel(e);

        EnsureLayout();

        if (_layout is not { IsScrollable: true } || e.Delta == 0f)
        {
            return;
        }

        var before = _scroll;
        ScrollBy(-e.Delta);
        e.Handled |= _scroll != before;
    }

    /// <inheritdoc />
    protected override void OnMouseUp(XPlatMouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != XPlatMouseButtons.Left)
        {
            return;
        }

        var index = HitTestRow(e.Location);
        var pressed = _pressed;
        _pressed = -1;

        if (index >= 0 && index == pressed)
        {
            Activate(index);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Detach();
            ReleaseHostedControls();
            SubMenuOpened = null;
            SubMenuClosed = null;
            _subMenu = null;
            _subMenuIndex = -1;
        }

        base.Dispose(disposing);
    }

    private bool HandleNavigationKey(XPlatKey key, XPlatModifiers modifiers)
    {
        switch (key)
        {
            case XPlatKey.Down:
                HighlightNext();
                return true;

            case XPlatKey.Up:
                HighlightPrevious();
                return true;

            case XPlatKey.Home:
                HighlightFirst();
                return true;

            case XPlatKey.End:
                HighlightLast();
                return true;

            case XPlatKey.Right:
                if (_layout is { } layout && _hovered >= 0 && _hovered < layout.Rows.Count && layout.Rows[_hovered].IsSubMenu)
                {
                    OpenSubMenu(_hovered);
                    _subMenu?.HighlightFirst();
                }

                return true;

            case XPlatKey.Left:
                if (_parent is { } parent)
                {
                    parent.CloseSubMenu();
                }

                return true;

            case XPlatKey.Enter:
            case XPlatKey.Space:
                ActivateHighlighted();
                return true;

            case XPlatKey.Tab:
                // A menu holds the focus it took: Tab moves between its own items, it does not
                // hand the keyboard back to the window behind it.
                HighlightNext();
                return true;

            default:
                return false;
        }
    }

    private bool HandleMnemonic(XPlatKey key)
    {
        if (XPlatMenuText.KeyToMnemonic(key) is not { } letter)
        {
            return false;
        }

        EnsureLayout();

        if (_layout is not { } layout)
        {
            return false;
        }

        for (var i = 0; i < layout.Rows.Count; i++)
        {
            var item = layout.Rows[i].Item;

            if (item.IsSeparator || !item.Enabled || item.Mnemonic != letter)
            {
                continue;
            }

            Activate(i);
            return true;
        }

        // A letter that matches nothing is still swallowed: the menu is open and the key belongs to
        // it, not to the control it was opened over.
        return true;
    }

    private bool ForwardToHostedControl(XPlatKey key, XPlatModifiers modifiers)
    {
        if (Host is not { FocusedControl: { } focused } || ReferenceEquals(focused, this))
        {
            return false;
        }

        return focused.RaiseKeyDown(new XPlatKeyEventArgs(key, modifiers));
    }

    private void HostControl(XPlatControl control, SKRect bounds)
    {
        if (control.Parent is null)
        {
            _hostedControls.Add(control);
            Controls.Add(control);
        }
        else if (!ReferenceEquals(control.Parent, this))
        {
            // Another menu is showing the same control right now. A control has one parent, so this
            // row stays empty rather than taking it away from the menu that is already using it.
            return;
        }

        if (!control.Bounds.Equals(bounds))
        {
            control.Bounds = bounds;
        }

        control.PerformLayout();
    }

    private void ReleaseHostedControls()
    {
        for (var i = _hostedControls.Count - 1; i >= 0; i--)
        {
            var control = _hostedControls[i];

            // Removed rather than disposed: the control belongs to whoever built the menu item and
            // is shown again the next time the menu opens.
            Controls.Remove(control);
        }

        _hostedControls.Clear();
    }

    private int HitTestRow(SKPoint location)
    {
        EnsureLayout();

        if (_layout is not { } layout)
        {
            return -1;
        }

        // A row scrolled out of the viewport is not there as far as the pointer is concerned, even
        // though its bounds still lie above or below it.
        if (layout.IsScrollable && !layout.ContentViewport.Contains(location))
        {
            return -1;
        }

        return layout.HitTest(new SKPoint(location.X, location.Y + _scroll));
    }

    /// <summary>
    /// Scrolls one row when <paramref name="location"/> is on a scroll arrow, and reports whether it
    /// was. An arrow is not a row, so pressing one never picks an item.
    /// </summary>
    private bool ScrollFromIndicator(SKPoint location)
    {
        EnsureLayout();

        if (_layout is not { IsScrollable: true } layout)
        {
            return false;
        }

        var step = (Metrics ?? XPlatMenuMetrics.Default).ItemHeight;

        if (layout.ScrollStartBounds.Contains(location))
        {
            ScrollBy(-step);
            return true;
        }

        if (layout.ScrollEndBounds.Contains(location))
        {
            ScrollBy(step);
            return true;
        }

        return false;
    }

    /// <summary>Translates a rectangle of this menu's own layout into its bounds.</summary>
    private SKRect Offset(SKRect rect) =>
        SKRect.Create(rect.Left, rect.Top - _scroll, rect.Width, rect.Height);

    /// <summary>Lays the items out and returns the size of the drawing surface, shadow margin included.</summary>
    private SKSize MeasureOwn(float availableWidth)
    {
        var extent = ShowShadow ? (Metrics ?? XPlatMenuMetrics.Default).ShadowExtent : 0f;

        _layout = XPlatMenuRenderer.Measure(
            _items,
            Metrics,
            Font,
            ShowShadow,
            MathF.Max(0f, availableWidth - (extent * 2f)),
            _maximumSurfaceHeight ?? float.PositiveInfinity);

        // A menu that came back with fewer rows than it had - it was reset, or its submenu closed -
        // cannot stay scrolled past its new end.
        _scroll = Math.Clamp(_scroll, 0f, _layout.MaxScroll);

        return _layout.WindowSize;
    }

    private void EnsureLayout()
    {
        if (_layout is null)
        {
            MeasureOwn(float.PositiveInfinity);
        }
    }

    private void RaiseInvoked(XPlatMenuItem item) => _invoked?.Invoke(this, item);

    private void RaiseDismissed() => _dismissed?.Invoke(this);
}
