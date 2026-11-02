using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// The platform-independent heart of the UI library. A host adapter (WinForms, WPF,
/// Win32, Cocoa, X11, SDL, ...) owns an <see cref="XPlatUiHost"/>, forwards input and
/// calls <see cref="Paint"/> whenever <see cref="AddInvalidatedHandler"/> fires.
/// </summary>
public sealed class XPlatUiHost
{
    /// <summary>
    /// How far one wheel detent scrolls, in logical pixels. A backend converts its native wheel
    /// units with this, so a detent moves the same distance on every platform.
    /// </summary>
    public const float WheelPixelsPerDetent = 48f;

    private Action<object>? _invalidated;
    private Action<object>? _cursorChanged;
    private Action<object, XPlatContextMenuEventArgs>? _contextMenuOpening;
    private readonly XPlatSelection _selection;
    private XPlatControl? _root;
    private SKSize _clientSize;
    private XPlatControl? _hovered;
    private XPlatControl? _captured;
    private XPlatControl? _focused;
    private XPlatControl? _selectionOwner;
    private XPlatCursor _cursor = XPlatCursor.Default;
    private SKPoint _selectionOrigin;
    private bool _selecting;
    private bool _draggingSelection;
    private SKColor? _clearColor;
    private SKRegion _dirty = new();
    private SKRegion _painting = new();
    private bool _dirtyAll = true;
    private bool _debugHoldingContent;

    public XPlatUiHost() => _selection = new XPlatSelection(this);

    /// <summary>Adds a handler that runs when the UI needs to be repainted.</summary>
    public IDisposable AddInvalidatedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _invalidated += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveInvalidatedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddInvalidatedHandler"/> added.</summary>
    public void RemoveInvalidatedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _invalidated -= handler;
    }

    /// <summary>Adds a handler that runs when the control under the pointer requests a different cursor.</summary>
    public IDisposable AddCursorChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _cursorChanged += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveCursorChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddCursorChangedHandler"/> added.</summary>
    public void RemoveCursorChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _cursorChanged -= handler;
    }

    /// <summary>
    /// Adds a handler that runs before a context menu is shown, so an application can supply one
    /// for a control that carries no <see cref="XPlatControl.ContextMenu"/> of its own.
    /// </summary>
    public IDisposable AddContextMenuOpeningHandler(Action<object, XPlatContextMenuEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _contextMenuOpening += handler;
        return new XPlatHandlerToken<Action<object, XPlatContextMenuEventArgs>>(RemoveContextMenuOpeningHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddContextMenuOpeningHandler"/> added.</summary>
    public void RemoveContextMenuOpeningHandler(Action<object, XPlatContextMenuEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _contextMenuOpening -= handler;
    }

    /// <summary>The root of the control tree.</summary>
    public XPlatControl? Root
    {
        get => _root;
        set
        {
            if (ReferenceEquals(_root, value))
            {
                return;
            }

            if (_focused is not null)
            {
                SetFocus(null);
            }

            _hovered?.RaiseMouseLeave();
            _hovered = null;
            _captured = null;
            _selecting = false;
            _draggingSelection = false;
            ClearSelection();

            _root?.AttachHost(null);
            _root = value;
            _root?.AttachHost(this);

            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>
    /// The window-scoped service provider, assigned by the <see cref="XPlatWindow"/> that owns this
    /// host. Null when a host is used standalone, as the harnesses do.
    /// </summary>
    public IServiceProvider? Services { get; set; }

    /// <summary>
    /// The window this host draws, or null when the host is used standalone. Assigned by
    /// <see cref="XPlatWindow"/> so that code inside a control can parent dialogs on its window.
    /// </summary>
    public XPlatWindow? Window { get; set; }

    /// <summary>Resolves <typeparamref name="TService"/> from <see cref="Services"/>, or null when there is none.</summary>
    /// <typeparam name="TService">The service to resolve.</typeparam>
    public TService? GetService<TService>() where TService : class =>
        Services?.GetService(typeof(TService)) as TService;

    /// <summary>Size of the drawable area in logical pixels (96 DPI).</summary>
    public SKSize ClientSize => _clientSize;

    /// <summary>
    /// Color used to clear the surface before painting. While it has never been assigned it
    /// follows <see cref="XPlatTheme.Background"/>, so the window follows the theme on its own.
    /// </summary>
    public SKColor ClearColor
    {
        get => _clearColor ?? XPlatTheme.Background;
        set => _clearColor = value;
    }

    public XPlatControl? FocusedControl => _focused;

    /// <summary>
    /// The control that currently owns a selection of its own, such as a text box with a caret,
    /// or null when the host's own <see cref="Selection"/> is the only selection.
    /// </summary>
    public XPlatControl? SelectionOwner => _selectionOwner;

    public XPlatControl? HoveredControl => _hovered;

    public XPlatControl? CapturedControl => _captured;

    public XPlatCursor Cursor => _cursor;

    /// <summary>
    /// The selection driven by pointer input. It can span several controls, so it lives here
    /// rather than in a control.
    /// </summary>
    public XPlatSelection Selection => _selection;

    /// <summary>
    /// Text inserted between the pieces that different controls contribute when a selection is
    /// copied. Defaults to a newline, so a selection spanning three labels copies as three lines.
    /// </summary>
    public string SelectionTextSeparator { get; set; } = "\n";

    /// <summary>
    /// Distance in logical pixels the pointer must travel after a press before the press turns
    /// into a selection. Keeps plain clicks reaching links, buttons and text boxes.
    /// </summary>
    public float SelectionDragThreshold { get; set; } = 3f;

    /// <summary>Updates the logical client size and re-runs layout when it changed.</summary>
    public void SetClientSize(float width, float height)
    {
        width = MathF.Max(0f, width);
        height = MathF.Max(0f, height);

        if (MathF.Abs(width - _clientSize.Width) < 0.01f && MathF.Abs(height - _clientSize.Height) < 0.01f)
        {
            return;
        }

        _clientSize = new SKSize(width, height);
        PerformLayout();
        Invalidate();
    }

    /// <summary>Arranges the root control to fill the client area.</summary>
    public void PerformLayout()
    {
        if (_root is null)
        {
            return;
        }

        _root.Bounds = new SKRect(0f, 0f, _clientSize.Width, _clientSize.Height);
        _root.PerformLayout();
    }

    /// <summary>
    /// Whether the backend keeps the pixels of the previous frame between paints, so a paint only
    /// has to cover the area that was invalidated. Off by default, because a backend that
    /// rasterizes the whole surface every frame - GTK4 and the headless renderer both do - would
    /// be left with blank patches where the retained content should be. Turn it on only when
    /// <see cref="Paint"/> draws into a surface that outlives the frame.
    /// </summary>
    public bool PartialUpdates { get; set; }

    /// <summary>The area the next paint will cover, in logical pixels.</summary>
    /// <remarks>
    /// A backend that can present part of its surface uses this to ask for a repaint of just that
    /// area. It covers everything when nothing has been invalidated since the last paint, so a
    /// paint always produces a complete frame even when the backend asks for one unprompted.
    /// </remarks>
    public SKRectI PendingUpdateBounds =>
        _dirtyAll || _dirty.IsEmpty ? SurfaceBounds : _dirty.Bounds;

    /// <summary>
    /// Whether anything has been invalidated since the last paint. Lets a backend skip the work of
    /// asking the platform for a repaint when it would only be handed a complete frame back.
    /// </summary>
    public bool IsUpdatePending => _dirtyAll || !_dirty.IsEmpty;

    /// <summary>The whole drawable area, rounded out to whole pixels.</summary>
    private SKRectI SurfaceBounds =>
        new(0, 0, (int)MathF.Ceiling(_clientSize.Width), (int)MathF.Ceiling(_clientSize.Height));

    /// <summary>How long a red debug frame stays on screen before its content is painted over it.</summary>
    private const int DefaultDebugPaintHoldMs = 80;

    /// <summary>
    /// Set <c>XPLATUI_DEBUG_PAINT=1</c> to paint every invalidated area in flat red for one frame
    /// before painting its content, which shows what a partial repaint actually covers. A number
    /// sets how long the red frame is held, in milliseconds.
    /// </summary>
    private static readonly int DebugPaintHoldMs = ReadDebugPaintHold();

    private static int ReadDebugPaintHold()
    {
        var value = Environment.GetEnvironmentVariable("XPLATUI_DEBUG_PAINT");

        if (string.IsNullOrEmpty(value) || value is "0" or "false")
        {
            return 0;
        }

        return int.TryParse(value, out var hold) && hold > 0 ? hold : DefaultDebugPaintHoldMs;
    }

    /// <summary>Paints the part of the control tree that was invalidated.</summary>
    /// <param name="canvas">Canvas whose transform maps logical pixels to device pixels.</param>
    /// <param name="full">Paint everything, whatever has been invalidated.</param>
    /// <returns>The area that was painted, in logical pixels.</returns>
    /// <remarks>
    /// The area is cleared and then repainted, so a backend with a retained surface can present
    /// just that part of it and leave the rest of the frame alone. The tree is still walked from
    /// the root; children that cannot reach the clip are culled as it goes.
    /// </remarks>
    public SKRectI Paint(SKCanvas canvas, bool full = false)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        var whole = full || !PartialUpdates || _dirtyAll || _dirty.IsEmpty;
        var area = whole ? SurfaceBounds : _dirty.Bounds;

        if (DebugPaintHoldMs > 0 && !whole)
        {
            // Two frames per repaint: the area in flat red, then the content that replaces it. The
            // red frame leaves the dirty region alone, so the content frame covers exactly the same
            // area. The hold belongs to the start of the content frame rather than the end of the
            // red one, because the red frame is not on screen until the backend has presented it,
            // which happens after this method returns.
            if (!_debugHoldingContent)
            {
                _debugHoldingContent = true;

                Guard(() =>
                {
                    canvas.Save();
                    ClipToPaintArea(canvas, _dirty, false, area);
                    using var red = new SKPaint { Color = SKColors.Red, BlendMode = SKBlendMode.Src };
                    canvas.DrawPaint(red);
                    canvas.Restore();
                });

                // The region rather than its bounds: a paint that covers two separate controls has
                // bounds that take in everything between them, and asking for that would make the
                // content frame repaint the gap as well - which is the over-painting the debug mode
                // exists to expose.
                using (var pending = new SKRegion(_dirty))
                {
                    Invalidate(pending);
                }

                return area;
            }

            _debugHoldingContent = false;
            Thread.Sleep(DebugPaintHoldMs);
        }
        else
        {
            _debugHoldingContent = false;
        }

        // The paint takes the dirty region with it. Anything invalidated from inside the paint - a
        // control that repaints itself, a layout that moves something - belongs to the next frame,
        // so it must land in an empty region rather than be cleared along with this one.
        var region = _dirty;
        _dirty = _painting;
        _painting = region;
        _dirty.SetEmpty();
        _dirtyAll = false;

        Guard(() =>
        {
            canvas.Save();
            ClipToPaintArea(canvas, region, whole, area);

            // DrawPaint rather than Clear: both fill the clip, but only DrawPaint is documented to
            // fill the clip *region*, and the region the dirty area came from can have holes in it.
            using var clear = new SKPaint { Color = ClearColor, BlendMode = SKBlendMode.Src };
            canvas.DrawPaint(clear);

            _root?.Render(canvas);
            canvas.Restore();
        });

        return area;
    }

    /// <summary>Clips a canvas to the area a paint covers, in logical pixels.</summary>
    private static void ClipToPaintArea(SKCanvas canvas, SKRegion region, bool whole, SKRectI area)
    {
        // A scaled canvas means the region - which is in logical pixels - cannot be handed to
        // ClipRegion, which ignores the transform and expects device space. The boundary path goes
        // through the transform like every other clip.
        if (whole || region.IsRect)
        {
            var bounds = whole ? area : region.Bounds;
            canvas.ClipRect(new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));
        }
        else
        {
            using var boundary = region.GetBoundaryPath();
            canvas.ClipPath(boundary, SKClipOperation.Intersect, false);
        }
    }

    /// <summary>Requests a repaint of the whole surface.</summary>
    public void Invalidate()
    {
        _dirtyAll = true;
        _dirty.SetEmpty();
        _invalidated?.Invoke(this);
    }

    /// <summary>Requests a repaint of an area, in logical pixels.</summary>
    /// <param name="rect">The area to repaint.</param>
    public void Invalidate(SKRect rect)
    {
        if (rect.Width > 0f && rect.Height > 0f && !_dirtyAll)
        {
            _dirty.Op(ToRectI(rect), SKRegionOperation.Union);
        }

        _invalidated?.Invoke(this);
    }

    /// <summary>Requests a repaint of a region, in logical pixels.</summary>
    /// <param name="region">The area to repaint.</param>
    public void Invalidate(SKRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);

        if (!_dirtyAll && !region.IsEmpty)
        {
            _dirty.Op(region, SKRegionOperation.Union);
        }

        _invalidated?.Invoke(this);
    }

    /// <summary>
    /// Adds an area to the next paint without asking for a frame.
    /// </summary>
    /// <param name="rect">The area to add, in logical pixels.</param>
    /// <remarks>
    /// For a backend that has been told to repaint by the platform rather than by the UI: the
    /// area the platform reports stale is not necessarily one the UI knows about, and it has to be
    /// added to what the paint covers without asking for a frame from inside the frame.
    /// </remarks>
    public void IncludeInNextPaint(SKRect rect)
    {
        if (!_dirtyAll && rect.Width > 0f && rect.Height > 0f)
        {
            _dirty.Op(ToRectI(rect), SKRegionOperation.Union);
        }
    }

    /// <summary>Rounds a rectangle out to the whole pixels that cover it.</summary>
    private static SKRectI ToRectI(SKRect rect) => new(
        (int)MathF.Floor(rect.Left),
        (int)MathF.Floor(rect.Top),
        (int)MathF.Ceiling(rect.Right),
        (int)MathF.Ceiling(rect.Bottom));

    /// <summary>
    /// Re-reads the platform's light/dark preference through <see cref="XPlatSystemTheme"/> and,
    /// when it no longer matches the palette, switches the palette and repaints. Backends call this
    /// once at startup and again from their theme-changed notification (<c>WM_SETTINGCHANGE</c> and
    /// <c>WM_THEMECHANGED</c> on Windows).
    /// </summary>
    /// <returns>True when the appearance changed.</returns>
    public bool RefreshTheme()
    {
        if (!XPlatSystemTheme.Refresh())
        {
            return false;
        }

        Invalidate();
        return true;
    }

    public void ProcessMouseMove(SKPoint location) => Guard(() =>
    {
        var hit = HitTest(location);

        if (!ReferenceEquals(hit, _hovered))
        {
            _hovered?.RaiseMouseLeave();
            _hovered = hit;
            _hovered?.RaiseMouseEnter();
        }

        var target = _captured ?? hit;
        target?.RaiseMouseMove(new XPlatMouseEventArgs(ToLocal(target, location)));
        SetCursor(hit is null ? XPlatCursor.Default : hit.GetCursorAt(ToLocal(hit, location)));

        if (_selecting && _selectionOwner is null)
        {
            if (!_draggingSelection)
            {
                var dx = location.X - _selectionOrigin.X;
                var dy = location.Y - _selectionOrigin.Y;
                _draggingSelection = (dx * dx) + (dy * dy) >= SelectionDragThreshold * SelectionDragThreshold;
            }

            if (_draggingSelection)
            {
                ExtendSelection(location);
            }
        }
    });

    public void ProcessMouseDown(
        SKPoint location,
        XPlatMouseButtons button = XPlatMouseButtons.Left,
        XPlatModifiers modifiers = XPlatModifiers.None,
        int clickCount = 1) => Guard(() =>
    {
        var hit = HitTest(location);
        _captured = hit;
        var takesFocus = hit is null || (hit.TabStop && hit.Focusable);

        if (button == XPlatMouseButtons.Left)
        {
            var selectionTarget = FindSelectionTarget(hit);
            if (selectionTarget is null)
            {
                _selecting = false;
                _draggingSelection = false;

                // A press that cannot move the focus - the scrollbar a panel owns for itself, a
                // plain label - leaves the selection and the caret of the focused text box alone.
                // Clearing them here would take the caret away from a control that still has the
                // focus and is still receiving what the user types.
                if (takesFocus)
                {
                    ClearSelection();
                }
            }
            else
            {
                _selectionOrigin = location;
                _selecting = true;
                _draggingSelection = false;
                BeginSelection(selectionTarget, location);
            }
        }

        if (hit is null)
        {
            SetFocus(null);
            return;
        }

        if (takesFocus)
        {
            SetFocus(hit);
        }

        hit.RaiseMouseDown(new XPlatMouseEventArgs(ToLocal(hit, location), button, modifiers, clickCount));
    });

    public void ProcessMouseUp(SKPoint location, XPlatMouseButtons button = XPlatMouseButtons.Left, XPlatModifiers modifiers = XPlatModifiers.None) => Guard(() =>
    {
        if (button == XPlatMouseButtons.Left)
        {
            _selecting = false;
            _draggingSelection = false;
        }

        var target = _captured ?? HitTest(location);
        _captured = null;
        target?.RaiseMouseUp(new XPlatMouseEventArgs(ToLocal(target, location), button, modifiers));
    });

    /// <summary>Clears the hover state when the pointer leaves the surface.</summary>
    public void ProcessMouseLeave() => Guard(() =>
    {
        _hovered?.RaiseMouseLeave();
        _hovered = null;
        SetCursor(XPlatCursor.Default);
    });

    /// <summary>
    /// Routes a wheel turn to the control under the pointer. Returns true when something consumed
    /// it, which tells the adapter the platform has nothing left to do with the event.
    /// </summary>
    /// <param name="location">The pointer position, in logical client pixels.</param>
    /// <param name="delta">
    /// How far to scroll, in logical pixels. Positive scrolls toward the start of the content (up
    /// or left). Fractions are expected: a touchpad reports a fraction of a detent.
    /// </param>
    /// <param name="modifiers">The modifier keys held while the wheel turned.</param>
    /// <remarks>
    /// The turn starts at the control under the pointer and travels up the parent chain until one
    /// consumes it, so a wheel over a label inside a scrolling panel scrolls the panel. A control
    /// that cannot move any further in the direction asked for leaves the event alone, which hands
    /// the rest of the turn to the container around it.
    /// </remarks>
    public bool ProcessMouseWheel(SKPoint location, float delta, XPlatModifiers modifiers = XPlatModifiers.None) => Guard(() =>
    {
        if (delta == 0f)
        {
            return false;
        }

        var target = _captured ?? HitTest(location);

        for (var control = target; control is not null; control = control.Parent)
        {
            if (control.RaiseMouseWheel(new XPlatMouseEventArgs(ToLocal(control, location), modifiers: modifiers, delta: delta)))
            {
                return true;
            }
        }

        return false;
    }, false);

    /// <summary>
    /// Shows the context menu for <paramref name="location"/>. Adapters call this from the
    /// platform's own context menu event (on Windows, <c>WM_CONTEXTMENU</c>), which is why the
    /// host does not raise it from a right button release: the platform message already covers
    /// both the mouse and the keyboard menu key. Returns true when a menu was handed to a
    /// presenter.
    /// </summary>
    /// <remarks>
    /// The menu comes from the hit control's <see cref="XPlatControl.ContextMenu"/> (or the nearest
    /// ancestor that has one), then from <see cref="AddContextMenuOpeningHandler"/>, and finally
    /// from the current selection, which offers Copy. Right-clicking never changes the selection,
    /// so a menu can act on what the user already selected.
    /// </remarks>
    public bool ProcessContextMenu(SKPoint location) => Guard(() =>
    {
        var hit = HitTest(location);
        var menu = hit?.BuildContextMenu();

        if (menu is null || menu.IsEmpty)
        {
            var args = new XPlatContextMenuEventArgs(hit, location);
            _contextMenuOpening?.Invoke(this, args);
            menu = args.Menu;
        }

        if (menu is null || menu.IsEmpty)
        {
            menu = _selection.IsEmpty ? null : XPlatContextMenu.CreateCopyMenu(this);
        }

        return menu is not null && menu.Show(this, hit, location);
    }, false);

    /// <summary>
    /// Routes a key press to the focused control. Returns true when the control consumed the
    /// key, which tells the adapter to swallow the platform key event (and its text input).
    /// </summary>
    /// <remarks>
    /// The window's menu bar sees the key first, the way a native menu's accelerators do, so a
    /// shortcut is not shadowed by whichever control happens to have the focus. macOS dispatches
    /// Command combinations through its own menu bar before a view sees them at all, so the same
    /// lookup there only ever covers the modifiers AppKit does not claim.
    /// </remarks>
    public bool ProcessKeyDown(XPlatKey key, XPlatModifiers modifiers = XPlatModifiers.None) => Guard(() =>
    {
        if (Window is { } window && window.TryInvokeShortcut(key, modifiers))
        {
            return true;
        }

        if (key == XPlatKey.Tab)
        {
            MoveFocus((modifiers & XPlatModifiers.Shift) != 0 ? -1 : 1);
            return true;
        }

        return _focused?.RaiseKeyDown(new XPlatKeyEventArgs(key, modifiers)) ?? false;
    }, false);

    public void ProcessKeyUp(XPlatKey key, XPlatModifiers modifiers = XPlatModifiers.None) =>
        Guard(() => _focused?.RaiseKeyUp(new XPlatKeyEventArgs(key, modifiers)));

    public void ProcessTextInput(string text) =>
        Guard(() => _focused?.RaiseTextInput(new XPlatTextInputEventArgs(text)));

    /// <summary>Moves keyboard focus to the given control (null clears focus).</summary>
    public void SetFocus(XPlatControl? control)
    {
        if (ReferenceEquals(_focused, control))
        {
            return;
        }

        if (control is { Enabled: false } || control is { Focusable: false })
        {
            return;
        }

        var previous = _focused;
        previous?.RaiseLostFocus();

        // A control's internal selection only lives while it holds focus, so leaving the
        // control ends it - that keeps a caret from lingering next to another selection.
        if (previous is not null && ReferenceEquals(_selectionOwner, previous))
        {
            ReleaseSelectionOwner();
        }

        _focused = control;
        _focused?.RaiseGotFocus();
        Invalidate();
    }

    public void CaptureMouse(XPlatControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        _captured = control;
    }

    public void ReleaseMouseCapture(XPlatControl control)
    {
        if (ReferenceEquals(_captured, control))
        {
            _captured = null;
        }
    }

    /// <summary>
    /// Declares that <paramref name="owner"/> is starting a selection of its own - a text box
    /// caret or selection, for example. Only one selection may exist at a time, so the host's
    /// control-level selection is cleared and any other control's internal selection is dropped
    /// before the caller proceeds.
    /// </summary>
    public void ClaimSelection(XPlatControl owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var previous = _selectionOwner;
        _selectionOwner = owner;
        _selecting = false;
        _draggingSelection = false;

        bool changed = !_selection.IsEmpty;
        _selection.Clear();

        if (!ReferenceEquals(previous, owner))
        {
            changed = true;
            previous?.RaiseSelectionCleared();
        }

        if (changed)
        {
            Invalidate();
        }
    }

    /// <summary>Clears every selection: the control-level selection and any control's internal one.</summary>
    public void ClearSelection()
    {
        _selection.Clear();
        ReleaseSelectionOwner();
    }

    /// <summary>
    /// Copies the current selection to the clipboard. Every control the selection touches
    /// contributes its own piece of the text, in visual order, joined with
    /// <see cref="SelectionTextSeparator"/>. Controls that can describe their formatting also
    /// contribute an HTML flavour, so rich targets paste the formatting.
    /// Returns true when text was copied.
    /// </summary>
    public bool CopySelection()
    {
        var text = _selection.GetText(SelectionTextSeparator);
        if (text.Length == 0)
        {
            return false;
        }

        XPlatClipboard.SetHtml(text, _selection.GetHtml(SelectionTextSeparator));
        return true;
    }

    private void ReleaseSelectionOwner()
    {
        var previous = _selectionOwner;
        if (previous is null)
        {
            return;
        }

        _selectionOwner = null;
        previous.RaiseSelectionCleared();
    }

    private XPlatControl? HitTest(SKPoint location) => _root?.HitTest(location);

    /// <summary>
    /// Runs an entry point that a backend calls from a platform callback, so a failure reaches
    /// <see cref="XPlatApplication.AddUnhandledExceptionHandler"/> instead of unwinding into the native frame
    /// that called it. With no application the exception is rethrown to the caller, which is what a
    /// standalone host has always done.
    /// </summary>
    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            XPlatApplication.HandleNativeBoundary(ex);
        }
    }

    /// <summary>Guarded variant of <see cref="Guard(Action)"/> for an entry point that returns a value.</summary>
    private static T Guard<T>(Func<T> func, T fallback)
    {
        try
        {
            return func();
        }
        catch (Exception ex)
        {
            XPlatApplication.HandleNativeBoundary(ex);
            return fallback;
        }
    }

    private void BeginSelection(XPlatControl control, SKPoint location)
    {
        ReleaseSelectionOwner();
        _selection.Begin(new XPlatSelectionPosition(control, control.GetSelectionOffset(ToLocal(control, location))));
    }

    private void ExtendSelection(SKPoint location)
    {
        var position = ResolveSelectionPosition(location);
        if (position.IsEmpty)
        {
            return;
        }

        _selection.SetFocus(position);
    }

    /// <summary>
    /// Resolves a pointer location to a selection position. A location over a selectable control
    /// maps straight into that control; a location outside every selectable control - the gap
    /// between two controls, the padding of a panel, or beyond the surface - clamps to the
    /// nearest one, so dragging off the end of the content selects up to that end instead of
    /// freezing wherever the pointer last happened to be over a control.
    /// </summary>
    private XPlatSelectionPosition ResolveSelectionPosition(SKPoint location)
    {
        var target = FindSelectionTarget(HitTest(location));
        if (target is not null)
        {
            return new XPlatSelectionPosition(target, target.GetSelectionOffset(ToLocal(target, location)));
        }

        var nearest = FindNearestSelectable(location) ?? _selection.Focus.Control ?? _selection.Anchor.Control;
        return nearest is null
            ? default
            : new XPlatSelectionPosition(nearest, nearest.GetSelectionOffset(ToLocal(nearest, location)));
    }

    /// <summary>
    /// The selectable control whose client area is closest to <paramref name="location"/>. Ties
    /// keep the control that currently holds the moving end of the selection, so a drag along an
    /// edge between two neighbours does not flicker between them.
    /// </summary>
    private XPlatControl? FindNearestSelectable(SKPoint location)
    {
        var current = _selection.Focus.Control;
        XPlatControl? best = current is { } control && control.SelectionUnitCount > 0 ? control : null;
        float bestDistance = best is null
            ? float.MaxValue
            : DistanceToRect(location, ToHost(best, best.ClientRect));

        CollectNearestSelectable(_root, location, ref best, ref bestDistance);
        return best;
    }

    private static void CollectNearestSelectable(
        XPlatControl? control,
        SKPoint location,
        ref XPlatControl? best,
        ref float bestDistance)
    {
        if (control is null || !control.Visible || !control.Enabled)
        {
            return;
        }

        if (control.SelectionUnitCount > 0)
        {
            // Matches the selection's own walk: a selectable control hides its children.
            float distance = DistanceToRect(location, ToHost(control, control.ClientRect));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = control;
            }

            return;
        }

        if (!control.HasChildren)
        {
            return;
        }

        foreach (var child in control.Controls)
        {
            CollectNearestSelectable(child, location, ref best, ref bestDistance);
        }
    }

    private static float DistanceToRect(SKPoint point, SKRect rect)
    {
        float dx = point.X < rect.Left ? rect.Left - point.X : point.X > rect.Right ? point.X - rect.Right : 0f;
        float dy = point.Y < rect.Top ? rect.Top - point.Y : point.Y > rect.Bottom ? point.Y - rect.Bottom : 0f;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>Maps a point from a control's coordinate space to the host's.</summary>
    private static SKPoint ToHost(XPlatControl control, SKPoint point)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            point = new SKPoint(point.X + current.Left, point.Y + current.Top);

            if (current.Parent is { } parent)
            {
                var offset = parent.GetChildOffset(current);
                point = new SKPoint(point.X + offset.X, point.Y + offset.Y);
            }
        }

        return point;
    }

    /// <summary>Maps a rectangle from a control's coordinate space to the host's.</summary>
    internal static SKRect ToHost(XPlatControl control, SKRect rect)
    {
        var origin = ToHost(control, SKPoint.Empty);
        return new SKRect(
            rect.Left + origin.X,
            rect.Top + origin.Y,
            rect.Right + origin.X,
            rect.Bottom + origin.Y);
    }

    /// <summary>
    /// Returns the control that should own a selection position for <paramref name="control"/>:
    /// the control itself when it is selectable, otherwise its nearest selectable ancestor (so
    /// clicking a link or a chip inside a selectable label selects the label).
    /// </summary>
    private static XPlatControl? FindSelectionTarget(XPlatControl? control)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            if (current.SelectionUnitCount > 0)
            {
                return current;
            }
        }

        return null;
    }

    private void SetCursor(XPlatCursor cursor)
    {
        if (_cursor == cursor)
        {
            return;
        }

        _cursor = cursor;
        _cursorChanged?.Invoke(this);
    }

    private void MoveFocus(int direction)
    {
        var focusable = new List<XPlatControl>();
        CollectFocusable(_root, focusable);
        if (focusable.Count == 0)
        {
            return;
        }

        int current = _focused is null ? -1 : focusable.IndexOf(_focused);
        int next = current < 0
            ? (direction >= 0 ? 0 : focusable.Count - 1)
            : ((current + direction) % focusable.Count + focusable.Count) % focusable.Count;

        SetFocus(focusable[next]);
    }

    private static void CollectFocusable(XPlatControl? control, List<XPlatControl> output)
    {
        if (control is null || !control.Visible || !control.Enabled)
        {
            return;
        }

        if (control.TabStop && control.Focusable)
        {
            output.Add(control);
        }

        if (control.HasChildren)
        {
            foreach (var child in control.Controls)
            {
                CollectFocusable(child, output);
            }
        }
    }

    private static SKPoint ToLocal(XPlatControl control, SKPoint point)
    {
        var local = point;
        for (var current = control; current is not null; current = current.Parent)
        {
            local = new SKPoint(local.X - current.Left, local.Y - current.Top);

            if (current.Parent is { } parent)
            {
                var offset = parent.GetChildOffset(current);
                local = new SKPoint(local.X - offset.X, local.Y - offset.Y);
            }
        }

        return local;
    }
}
