using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// Shows <see cref="XPlatContextMenu"/> values with <see cref="XPlatMenuView"/>, so every platform
/// draws the same menu. It is the portable counterpart of the native presenters: install it as
/// <see cref="XPlatContextMenuPresenter.Current"/> and the menu looks the same on Windows, macOS and
/// GTK4, because it is the same Skia drawing on all three.
/// </summary>
/// <remarks>
/// <para>
/// Every menu is a surface of its own. The menu the caller asked for gets an
/// <see cref="XPlatPopup"/>, and so does each submenu that opens from it: one popup per level. A
/// popup never grows to hold a submenu, so nothing about the menu the user is pointing at changes
/// when a submenu opens beside it.
/// </para>
/// <para>
/// That is also why the placement is per level. A level is placed from the geometry of the level
/// that opened it - the row that opened it, and the surface that row is drawn on - and its height is
/// capped by the room below that row, so a menu that does not fit scrolls instead of pushing
/// anything else out of the way. Only the root view is pushed onto the owning window as an
/// <see cref="IXPlatInputSink"/>: its key handling already forwards to the deepest menu, so the
/// arrow keys and mnemonics reach the menu the user is in even though no popup window ever takes the
/// keyboard.
/// </para>
/// </remarks>
public sealed class XPlatMenuPresenter : IXPlatContextMenuPresenter
{
    /// <summary>Shows <paramref name="request"/> with the portable renderer.</summary>
    public void Show(XPlatContextMenuRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Menu.IsEmpty)
        {
            return;
        }

        ShowMenu(
            request.Host,
            new XPlatMenuView(request.Menu),
            request.Location,
            scale: request.Control?.EffectiveScaleMultiplier ?? 1f);
    }

    /// <summary>
    /// Shows <paramref name="view"/> at <paramref name="location"/>, in logical pixels in
    /// <paramref name="host"/>'s coordinate space, and returns the popup the menu itself is drawn
    /// in. Closing that popup closes every submenu below it.
    /// </summary>
    /// <param name="host">The window the menu belongs to: the one that owns the keyboard.</param>
    /// <param name="view">The menu to show. It must not already be in a control tree.</param>
    /// <param name="location">Where the menu's top left corner goes.</param>
    /// <param name="closed">
    /// Run once the menu has closed, whether something was picked, the user dismissed it or the
    /// platform took it away. What a menu bar uses to un-press the button that opened it.
    /// </param>
    /// <param name="scale">
    /// The zoom the menu is drawn at, normally the owner's
    /// <see cref="XPlatControl.EffectiveScaleMultiplier"/>. The view is laid out in its own units
    /// and the whole placement calculation runs in them; only the location handed to the popup and
    /// the size it occupies on screen are in host space.
    /// </param>
    public static XPlatPopup ShowMenu(
        XPlatUiHost host,
        XPlatMenuView view,
        SKPoint location,
        Action? closed = null,
        float scale = 1f)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(view);

        return new Chain(host, closed, scale).Open(view, location);
    }

    /// <summary>
    /// The popups of one open menu, one per level, and the bookkeeping that keeps them in step with
    /// the views that own them.
    /// </summary>
    /// <remarks>
    /// The views already know which submenu is open, because
    /// <see cref="XPlatMenuView.OpenSubMenu"/> and <see cref="XPlatMenuView.CloseSubMenu"/> are the
    /// only things that change that, so this only follows them: a submenu opening is a popup to
    /// create and place, and a submenu closing is a popup to dispose. Keyboard and mouse handling
    /// stay entirely in the views.
    /// </remarks>
    private sealed class Chain
    {
        private readonly XPlatUiHost _host;
        private readonly Dictionary<XPlatMenuView, Level> _levels = new();
        private readonly float _scale;

        private Action? _closed;
        private XPlatMenuView? _root;
        private bool _closing;
        private bool _notified;

        internal Chain(XPlatUiHost host, Action? closed, float scale)
        {
            _host = host;
            _closed = closed;
            _scale = scale;
        }

        internal XPlatPopup Open(XPlatMenuView view, SKPoint location)
        {
            _root = view;

            var level = Watch(view, isRoot: true);

            view.Attach(_host);

            var client = ToContentSpace(ClientOf(_host));
            var area = ToContentSpace(PlacementArea(_host));
            var contentLocation = ToContentSpace(location);

            var popup = level.Popup;
            popup.Show(ToHostSpace(PlaceRoot(view, contentLocation, area)));

            if (popup.ActualPresentation == XPlatPopupPresentation.Overlay && !area.Equals(client))
            {
                // Painted inside the window, so the window is as far as the menu can go after all.
                popup.MaximumSize = client.Size;
                popup.MoveTo(ToHostSpace(PlaceRoot(view, contentLocation, client)));
            }

            return popup;
        }

        private Level Watch(XPlatMenuView view, bool isRoot)
        {
            var popup = new XPlatPopup(_host)
            {
                Content = view,
                Presentation = XPlatPopupPresentation.Auto,

                // The menu the user opened is the surface an outside press reaches, so it is the one
                // that dismisses the whole chain. A submenu is not: its closing only asks its own
                // menu to drop it, which is what keeps the menu being pointed at open while a
                // submenu comes and goes.
                DismissOnOutsideClick = isRoot,

                // A submenu is still a menu the user clicks in, so it takes the pointer even though
                // it is not what dismisses the chain.
                TakesPresses = true,
                MaximumSize = ToContentSpace(ClientOf(_host)).Size,
                ScaleFactor = _scale,
            };

            var level = new Level(view, popup);

            level.Add(view.AddInvokedHandler((_, item) =>
            {
                // Closed before the command runs: the command may open a dialog or another menu, and
                // this one has to be gone before it does.
                Close();
                item.Invoke();
            }));

            level.Add(view.AddDismissedHandler(_ => Close()));
            level.Add(popup.AddClosedHandler(_ => OnLevelClosed(level)));

            level.SubMenuOpened = OnSubMenuOpened;
            level.SubMenuClosed = (_, child) => OnSubMenuClosed(child);

            view.SubMenuOpened += level.SubMenuOpened;
            view.SubMenuClosed += level.SubMenuClosed;

            _levels[view] = level;
            return level;
        }

        private void OnSubMenuOpened(XPlatMenuView parent, XPlatMenuView child, int index)
        {
            if (_closing || !_levels.TryGetValue(parent, out var level) || _levels.ContainsKey(child))
            {
                return;
            }

            var popup = Watch(child, isRoot: false).Popup;

            var client = ToContentSpace(ClientOf(_host));
            var area = ToContentSpace(PlacementArea(_host));

            popup.Show(ToHostSpace(PlaceSubMenu(parent, level, child, index, area, _scale)));

            if (popup.ActualPresentation == XPlatPopupPresentation.Overlay && !area.Equals(client))
            {
                popup.MaximumSize = client.Size;
                popup.MoveTo(ToHostSpace(PlaceSubMenu(parent, level, child, index, client, _scale)));
            }
        }

        private void OnSubMenuClosed(XPlatMenuView child)
        {
            if (!_levels.TryGetValue(child, out var level))
            {
                return;
            }

            // Removed first: disposing the popup closes it, and the close handler uses this lookup
            // to tell its own teardown from the platform taking the popup away.
            _levels.Remove(child);
            level.Dispose();
        }

        private void OnLevelClosed(Level level)
        {
            if (_closing || !_levels.ContainsKey(level.View))
            {
                return;
            }

            if (ReferenceEquals(level.View, _root))
            {
                Close();
                return;
            }

            // The platform took a submenu away on the user's behalf - an outside press, or the window
            // losing activation. The menu that opened it stays, and drops the submenu.
            level.View.ParentMenu?.CloseSubMenu();
        }

        private void Close()
        {
            if (_closing)
            {
                return;
            }

            _closing = true;

            try
            {
                // Closing the root's submenu unwinds the chain from the deepest menu outwards, each
                // level dropping its own popup as the menu that opened it lets go of it.
                _root?.CloseSubMenu();

                foreach (var level in _levels.Values.ToArray())
                {
                    level.Dispose();
                }

                _levels.Clear();

                if (_root is { } root)
                {
                    root.Detach();
                    root.Dispose();
                }
            }
            finally
            {
                _closing = false;

                if (!_notified)
                {
                    _notified = true;
                    var closed = _closed;
                    _closed = null;
                    closed?.Invoke();
                }
            }
        }

        /// <summary>
        /// Places the menu the caller asked for, so that its surface lands on
        /// <paramref name="location" /> and stays inside <paramref name="area" />.
        /// </summary>
        /// <remarks>
        /// A menu measures with room around it for its drop shadow, and that margin is transparent.
        /// The surface - the part the user sees as the menu - sits one margin inside the popup, so the
        /// popup is placed one margin up and to the left of the point that was asked for.
        /// </remarks>
        private static SKPoint PlaceRoot(XPlatMenuView view, SKPoint location, SKRect area)
        {
            var inset = view.SurfaceInset;

            // A menu taller than the space it has scrolls instead of being cut off, so it is measured
            // against the room the area leaves once the transparent shadow margin is taken off.
            view.MaximumSurfaceHeight = MathF.Max(0f, area.Height - (inset * 2f));

            var surface = view.MeasureSurface(area.Width);

            var left = area.Left + inset;
            var right = area.Right - inset;
            var x = Axis(location.X, surface.Width, left, right);

            // The popup's top edge follows the point that was asked for and nothing else, so it stays
            // put while submenus open and close beside it.
            var top = Axis(location.Y, surface.Height + (inset * 2f), area.Top, area.Bottom) - inset;

            // Everything the level draws has to fit between the popup's top edge and the bottom of the
            // area, which is what keeps the popup from having to move once it is open.
            var room = MathF.Max(0f, area.Bottom - inset - top);
            view.MaximumSurfaceHeight = MathF.Max(0f, room - (inset * 2f));
            view.Measure(new SKSize(area.Width, room));

            return new SKPoint(x - inset, top);
        }

        /// <summary>
        /// Places a submenu beside the surface of the menu that opened it, level with the row that
        /// opened it, on whichever side of that menu has room for it on the display.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The parent's own popup is the reference, not the window: a submenu hangs off the surface
        /// the user is looking at, wherever on the display that has ended up.
        /// </para>
        /// <para>
        /// Only the room below the row caps the submenu's height, and it scrolls rather than growing
        /// upwards, because a submenu that moved its own menu would move the row the pointer is on -
        /// which is what turns a tall submenu into a menu that opens and closes itself forever.
        /// </para>
        /// </remarks>
        private static SKPoint PlaceSubMenu(
            XPlatMenuView parent,
            Level parentLevel,
            XPlatMenuView child,
            int index,
            SKRect area,
            float scale)
        {
            var metrics = child.Metrics ?? XPlatMenuMetrics.Default;
            var inset = child.SurfaceInset;
            var surface = parent.Layout?.SurfaceBounds ?? SKRect.Empty;

            // The parent's popup location is in host space; everything else here is in the menus'
            // own units, so the reference is brought into them on the way in.
            var origin = new SKPoint(parentLevel.Popup.Location.X / scale, parentLevel.Popup.Location.Y / scale);

            // The row the submenu opens from, in the parent popup's coordinates. The scroll offset is
            // taken off because rows are laid out in content space, which a scrolled menu shows a
            // window into.
            var rowTop = parent.Layout is { } layout && index >= 0 && index < layout.Rows.Count
                ? layout.Rows[index].Bounds.Top - parent.ScrollOffset
                : surface.Top;

            // The submenu's surface starts level with the row, less the padding the menu keeps above
            // its first row, so the two surfaces line up the way a menu chain is expected to.
            var top = origin.Y + rowTop - metrics.Padding;
            var roomBelow = MathF.Max(0f, area.Bottom - inset - top);

            // A submenu given less than a few rows of room would be a sliver. It is given the display
            // instead and slides up, but never past the top of it, and the menu it hangs off still
            // does not move.
            var minimum = MathF.Min(area.Height, metrics.ItemHeight * 3f);
            var rise = roomBelow < minimum;

            child.MaximumSurfaceHeight = rise
                ? MathF.Max(0f, area.Height - (inset * 2f))
                : roomBelow;

            var size = child.MeasureSurface(area.Width);

            if (rise)
            {
                top = MathF.Max(area.Top, area.Bottom - (size.Height + (inset * 2f)));
            }

            var toRight = origin.X + surface.Right - XPlatMenuView.SubMenuOverlap;
            var toLeft = origin.X + surface.Left + XPlatMenuView.SubMenuOverlap - size.Width;

            // A chain that has already turned keeps going the same way, so a submenu of a submenu does
            // not zigzag back across its parent. Otherwise the display decides: the submenu opens to
            // the right unless that would take it off the display.
            var left = parent.OpenedToLeft || toRight + size.Width > area.Right;

            if (left && !parent.OpenedToLeft && toLeft < area.Left && toRight + size.Width <= area.Right)
            {
                // Neither side has room; staying on the right keeps the chain readable, and the
                // display clamps whatever is left over.
                left = false;
            }

            var x = left
                ? MathF.Max(toLeft, area.Left)
                : MathF.Min(toRight, MathF.Max(area.Left, area.Right - size.Width));

            child.OpenedToLeft = left;

            return ToHostSpace(new SKPoint(x - inset, top - inset), scale);
        }

        /// <summary>Maps a host-space rectangle into the menus' own units.</summary>
        private SKRect ToContentSpace(SKRect rect) =>
            _scale == 1f
                ? rect
                : new SKRect(
                    rect.Left / _scale,
                    rect.Top / _scale,
                    rect.Right / _scale,
                    rect.Bottom / _scale);

        /// <summary>Maps a host-space point into the menus' own units.</summary>
        private SKPoint ToContentSpace(SKPoint point) =>
            _scale == 1f
                ? point
                : new SKPoint(point.X / _scale, point.Y / _scale);

        /// <summary>Maps a point in the menus' units back to host space.</summary>
        private SKPoint ToHostSpace(SKPoint point) => ToHostSpace(point, _scale);

        private static SKPoint ToHostSpace(SKPoint point, float scale) =>
            scale == 1f
                ? point
                : new SKPoint(point.X * scale, point.Y * scale);

        /// <summary>The area a popup may use: the display, or the window when it is painted inside it.</summary>
        private static SKRect PlacementArea(XPlatUiHost host)
        {
            var service = host.GetService<IXPlatPopupService>() ?? XPlatPopupService.Current;
            return service?.GetPlacementArea(host) ?? ClientOf(host);
        }

        private static SKRect ClientOf(XPlatUiHost host) => new(
            0f,
            0f,
            MathF.Max(1f, host.ClientSize.Width),
            MathF.Max(1f, host.ClientSize.Height));

        private static float Axis(float offset, float size, float minimum, float maximum) =>
            size >= maximum - minimum
                ? minimum
                : Math.Clamp(offset, minimum, maximum - size);
    }

    /// <summary>One menu, the popup it is drawn in, and the handlers that tie the two together.</summary>
    private sealed class Level
    {
        private readonly List<IDisposable> _tokens = new();

        internal Level(XPlatMenuView view, XPlatPopup popup)
        {
            View = view;
            Popup = popup;
        }

        internal XPlatMenuView View { get; }

        internal XPlatPopup Popup { get; }

        internal Action<XPlatMenuView, XPlatMenuView, int>? SubMenuOpened { get; set; }

        internal Action<XPlatMenuView, XPlatMenuView>? SubMenuClosed { get; set; }

        internal void Add(IDisposable token) => _tokens.Add(token);

        internal void Dispose()
        {
            foreach (var token in _tokens)
            {
                token.Dispose();
            }

            _tokens.Clear();

            if (SubMenuOpened is { } opened)
            {
                View.SubMenuOpened -= opened;
                SubMenuOpened = null;
            }

            if (SubMenuClosed is { } closed)
            {
                View.SubMenuClosed -= closed;
                SubMenuClosed = null;
            }

            Popup.Dispose();
        }
    }
}
