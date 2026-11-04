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
/// A submenu is another view inside this one, positioned against the row that opened it, so the
/// whole open menu is one control tree and one surface. Everything the chain needs - hover, keys,
/// dismissal - is answered by the deepest view that is open.
/// </para>
/// </remarks>
public sealed class XPlatMenuView : XPlatControl, IXPlatInputSink
{
    /// <summary>
    /// How far a submenu overlaps the menu that opened it, so the two rounded corners meet without
    /// leaving a notch between them.
    /// </summary>
    private const float SubMenuOverlap = 4f;

    private readonly List<XPlatControl> _hostedControls = new();
    private IReadOnlyList<XPlatMenuItem> _items = Array.Empty<XPlatMenuItem>();
    private XPlatMenuLayout? _layout;
    private XPlatMenuView? _parent;
    private XPlatMenuView? _subMenu;
    private XPlatUiHost? _ownerHost;
    private IDisposable? _sink;
    private Action<object, XPlatMenuItem>? _invoked;
    private Action<object>? _dismissed;
    private Action<object>? _sizeChanged;
    private int _subMenuIndex = -1;
    private int _hovered = -1;
    private int _pressed = -1;
    private bool _laying;

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

    /// <summary>Label font, or null for <see cref="XPlatTheme.DefaultFont"/>.</summary>
    public XPlatFont? Font { get; set; }

    /// <summary>
    /// Opacity of the surface, 0 to 1. Below 1 the menu is translucent so a compositor-supplied
    /// backdrop - the Windows 11 acrylic blur - shows through it.
    /// </summary>
    public float SurfaceAlpha { get; set; } = 1f;

    /// <summary>
    /// True to reserve room for the drop shadow. A submenu keeps one too, so it reads as a surface
    /// of its own, but it is not allowed to cast it over the menu it is joined to - see
    /// <see cref="ShadowExclusion"/>.
    /// </summary>
    public bool ShowShadow { get; set; } = true;

    /// <summary>
    /// How far this view's own surface is inset inside its bounds: the margin it keeps for the drop
    /// shadow, or zero when it has none. A presenter offsets the popup by this much so that the
    /// surface - not the transparent shadow margin - lands where the caller asked for it.
    /// </summary>
    public float SurfaceInset => ShowShadow ? (Metrics ?? XPlatMenuMetrics.Default).ShadowExtent : 0f;

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
    /// Raised when opening or closing a submenu changed the size the whole menu needs, which is what
    /// tells the presenter to grow or shrink the surface it is drawn on.
    /// </summary>
    public IDisposable AddSizeChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _sizeChanged += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveSizeChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddSizeChangedHandler"/> added.</summary>
    public void RemoveSizeChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _sizeChanged -= handler;
    }

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

        Controls.Add(child);
        Invalidate();
        RaiseSizeChanged();
        return true;
    }

    /// <summary>Closes the open submenu, if there is one.</summary>
    public void CloseSubMenu()
    {
        if (_subMenu is not { } child)
        {
            return;
        }

        _subMenu = null;
        _subMenuIndex = -1;

        Controls.Remove(child);
        child.Dispose();

        Invalidate();
        RaiseSizeChanged();
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
        // Escape closes the menu from wherever the keyboard focus is, including from a control
        // hosted in a row.
        if (key == XPlatKey.Escape)
        {
            if (_subMenu is not null)
            {
                CloseSubMenu();
            }
            else
            {
                Dismiss();
            }

            return true;
        }

        // A control hosted in a row owns the keyboard while it has the focus. The popup window is
        // deliberately not focusable, so the window that has the keyboard forwards to it through here.
        if (ForwardToHostedControl(key, modifiers))
        {
            return true;
        }

        var deepest = DeepestMenu;
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

    /// <summary>Measures the menu, and the submenu chain hanging off it.</summary>
    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var metrics = Metrics ?? XPlatMenuMetrics.Default;
        var extent = ShowShadow ? metrics.ShadowExtent : 0f;

        _layout = XPlatMenuRenderer.Measure(
            _items,
            Metrics,
            Font,
            ShowShadow,
            MathF.Max(0f, availableSize.Width - (extent * 2f)));

        var size = _layout.WindowSize;

        if (_subMenu is { } child && RowForSubMenu() is { } row)
        {
            var origin = SubMenuOrigin(row, metrics);
            var childSize = child.Measure(new SKSize(availableSize.Width, availableSize.Height));

            size = new SKSize(
                MathF.Max(size.Width, origin.X + childSize.Width),
                MathF.Max(size.Height, origin.Y + childSize.Height));
        }

        return size;
    }

    /// <summary>Places the submenu against the row that opened it and hosts any in-menu controls.</summary>
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
                    HostControl(control, row.ControlBounds);
                }
            }

            if (_subMenu is { } child && RowForSubMenu() is { } subMenuRow)
            {
                var origin = SubMenuOrigin(subMenuRow, Metrics ?? XPlatMenuMetrics.Default);
                var size = child.DesiredSize;

                if (size.Width <= 0f || size.Height <= 0f)
                {
                    size = child.Measure(new SKSize(float.PositiveInfinity, float.PositiveInfinity));
                }

                var bounds = SKRect.Create(origin.X, origin.Y, size.Width, size.Height);

                if (!child.Bounds.Equals(bounds))
                {
                    child.Bounds = bounds;
                }

                child.PerformLayout();
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

        if (_layout is { } layout)
        {
            XPlatMenuRenderer.Paint(canvas, layout, _hovered, SurfaceAlpha, Metrics, Font, ShadowExclusion());
        }
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

        _pressed = HitTestRow(e.Location);
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
        return _layout?.HitTest(location) ?? -1;
    }

    private XPlatMenuRow? RowForSubMenu() =>
        _layout is { } layout && _subMenuIndex >= 0 && _subMenuIndex < layout.Rows.Count
            ? layout.Rows[_subMenuIndex]
            : null;

    /// <summary>
    /// Where a submenu's top left corner goes: flush against the right edge of the surface that
    /// opened it, overlapping by <see cref="SubMenuOverlap"/>, with its first row level with the row
    /// that opened it.
    /// </summary>
    private SKPoint SubMenuOrigin(XPlatMenuRow row, XPlatMenuMetrics metrics)
    {
        var extent = ShowShadow ? metrics.ShadowExtent : 0f;
        var surfaceWidth = _layout?.SurfaceSize.Width ?? Bounds.Width;

        // The child is offset by its own shadow margin: its bounds start one margin before its
        // surface, so the two surfaces still meet at the same place they did without one.
        var inset = _subMenu?.SurfaceInset ?? 0f;

        return new SKPoint(
            extent + surfaceWidth - SubMenuOverlap - inset,
            MathF.Max(0f, row.Bounds.Top - metrics.Padding - inset));
    }

    /// <summary>
    /// The rectangle this view's shadow must not reach into: the surface of the menu that opened
    /// it, in this view's coordinates. A submenu is painted after its parent, so a shadow left
    /// unclipped would darken the edge of the menu it is joined to instead of falling past it.
    /// </summary>
    private SKRect? ShadowExclusion()
    {
        if (_parent is not { } parent || parent._layout is not { } parentLayout)
        {
            return null;
        }

        var surface = parentLayout.SurfaceBounds;
        surface.Offset(-Bounds.Left, -Bounds.Top);

        return surface;
    }

    private void EnsureLayout()
    {
        if (_layout is null)
        {
            MeasureContent(new SKSize(float.PositiveInfinity, float.PositiveInfinity));
        }
    }

    private void RaiseInvoked(XPlatMenuItem item) => _invoked?.Invoke(this, item);

    private void RaiseDismissed() => _dismissed?.Invoke(this);

    private void RaiseSizeChanged() => RootMenu._sizeChanged?.Invoke(RootMenu);
}
