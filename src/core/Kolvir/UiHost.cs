using System.Diagnostics;
using SkiaSharp;

namespace Kolvir;

/// <summary>
/// The platform-independent heart of the UI library. A host adapter (WinForms, WPF,
/// Win32, Cocoa, X11, SDL, ...) owns an <see cref="UiHost"/>, forwards input and
/// calls <see cref="Paint"/> whenever <see cref="AddInvalidatedHandler"/> fires.
/// </summary>
public sealed class UiHost
{
    /// <summary>
    /// How far one wheel detent scrolls, in logical pixels. A backend converts its native wheel
    /// units with this, so a detent moves the same distance on every platform.
    /// </summary>
    public const float WheelPixelsPerDetent = 48f;

    private Action<object>? _invalidated;
    private Action<object>? _framePainted;
    private Action<object>? _cursorChanged;

    /// <summary>Handlers that run at the start of every frame any host draws.</summary>
    private static Action<UiHost>? _frameStarting;

    /// <summary>Handlers that run once a host has finished arranging its tree.</summary>
    private static Action<UiHost>? _layoutCompleted;

    /// <summary>Ends the pass that is already completing, so a handler cannot start another one.</summary>
    private static bool _raisingLayoutCompleted;

    /// <summary>True while this host is inside <see cref="PerformLayout"/>.</summary>
    private bool _performingLayout;

    private static int _paintDepth;
    private Window? _window;
    private IDisposable? _windowShownToken;
    private IDisposable? _windowClosedToken;
    private Action<object, ContextMenuEventArgs>? _contextMenuOpening;
    private readonly Selection _selection;
    private Control? _root;
    private MenuBarView? _menuBar;
    private List<Control>? _overlays;
    private List<IInputSink>? _inputSinks;
    private SKSize _clientSize;
    private Control? _hovered;
    private readonly List<Control> _hoverChain = [];
    private readonly List<Control> _previousHoverChain = [];
    private Control? _captured;
    private Control? _focused;
    private SKPoint? _mousePosition;
    private Control? _selectionOwner;
    private Cursor _cursor = Cursor.Default;
    private SKPoint _selectionOrigin;
    private bool _selecting;
    private bool _draggingSelection;
    private Union<SKColor, ThemeColor, InheritedValue> _clearColor = PropertyValue.Inherited;
    private SKRegion _dirty = new();
    private SKRegion _painting = new();
    private bool _dirtyAll = true;
    /// <summary>
    /// How many rounds of queued arrangements one frame runs before giving up on a container that
    /// keeps asking for itself.
    /// </summary>
    private const int MaxArrangementPasses = 8;

    private bool _layoutPending;
    private readonly List<Control> _arrangeQueue = new();
    private bool _debugHoldingContent;
    private System.Threading.Timer? _debugFrameTimer;

    // The theme font generation the tree was last laid out for. A control resolves its font when it
    // measures and paints, so a theme font change needs no rebuild - only another layout pass, which
    // this one int compare asks for.
    private int _themeFontVersion = Theme.FontVersion;

    /// <summary>
    /// Creates a host with an empty tree, no window and no services. A backend fills those in as it
    /// attaches the host to a window.
    /// </summary>
    public UiHost() => _selection = new Selection(this);

    /// <summary>Adds a handler that runs when the UI needs to be repainted.</summary>
    public IDisposable AddInvalidatedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _invalidated += handler;
        return new HandlerToken<Action<object>>(RemoveInvalidatedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddInvalidatedHandler"/> added.</summary>
    public void RemoveInvalidatedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _invalidated -= handler;
    }

    /// <summary>Adds a handler that runs once a frame has been painted.</summary>
    /// <remarks>
    /// The handler is told about the frame that has just finished, so it reads
    /// <see cref="LastFrameTime"/>, <see cref="LastFrameArea"/> and <see cref="FrameCount"/> to see
    /// what that frame cost and covered. The paint has already taken the dirty region by then, so
    /// anything the handler invalidates is painted by the next frame rather than lost - which is
    /// what lets a control display the cost of the frame that drew it.
    /// </remarks>
    public IDisposable AddFramePaintedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _framePainted += handler;
        return new HandlerToken<Action<object>>(RemoveFramePaintedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddFramePaintedHandler"/> added.</summary>
    public void RemoveFramePaintedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _framePainted -= handler;
    }

    /// <summary>
    /// Adds a handler that runs at the start of a frame, before that frame is laid out and drawn.
    /// </summary>
    /// <param name="handler">What to run; it is told which host is about to draw.</param>
    /// <returns>A token that removes the handler when disposed.</returns>
    /// <remarks>
    /// <para>
    /// This is the point at which the tree still can change: the layout pass and the dirty region are
    /// both decided after these handlers have run, so a control created here is laid out and drawn by
    /// the frame that is starting rather than the one after it. Work that only wants to know what was
    /// drawn uses <see cref="AddFramePaintedHandler"/> instead.
    /// </para>
    /// <para>
    /// Static because the work that needs it is process-wide rather than per window - a virtual-DOM
    /// mount can exist before any window holds it - and because every handler is told which host is
    /// drawing, so a handler that does care can narrow to one. Handlers run on the UI thread, once per
    /// host per frame; an application with two windows painting runs them twice, which is why they must
    /// be cheap and idempotent.
    /// </para>
    /// </remarks>
    public static IDisposable AddFrameStartingHandler(Action<UiHost> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _frameStarting += handler;
        return new HandlerToken<Action<UiHost>>(RemoveFrameStartingHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddFrameStartingHandler"/> added.</summary>
    /// <param name="handler">The handler to remove.</param>
    public static void RemoveFrameStartingHandler(Action<UiHost> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _frameStarting -= handler;
    }

    /// <summary>
    /// Adds a handler that runs once a host has finished arranging its tree.
    /// </summary>
    /// <param name="handler">What to run; it is told which host completed.</param>
    /// <returns>A token that removes the handler when disposed.</returns>
    /// <remarks>
    /// <para>
    /// It runs after <see cref="PerformLayout"/> has arranged the menu bar, the root and the
    /// overlays, and after the arrangements the last frame queued through
    /// <see cref="RequestLayout"/> have run, so the tree a handler reads is the one the next paint
    /// will draw. A pass that arranged nothing does not raise it, which is what makes a handler
    /// that only cares about geometry free on a frame that changed nothing.
    /// </para>
    /// <para>
    /// It also runs when a scroll container moves its content without a layout pass. Scrolling
    /// slides content under a viewport that does not move, so a scroll deliberately does not
    /// re-arrange the children - but it does change what the last layout put on screen, and a
    /// handler that answers "what can the user see" has to hear about it. The event therefore means
    /// "the geometry the last layout produced is settled", and both cases settle it.
    /// </para>
    /// <para>
    /// Static because the work that needs it is process-wide: a feature can subscribe before any
    /// window exists, and every handler is told which host completed so a handler that cares can
    /// narrow to one. Handlers run on the UI thread once per pass per host, in subscription order; a
    /// handler that throws stops those after it and the exception reaches
    /// <see cref="Application.AddUnhandledExceptionHandler"/> through the caller's guard, as the other
    /// handler pairs do. A handler cannot start another pass of the same notification: a
    /// <see cref="PerformLayout"/> called from one completes normally but does not raise this event
    /// again, so a handler that moves a control cannot recurse. The next pass reports the change.
    /// </para>
    /// </remarks>
    public static IDisposable AddLayoutCompletedHandler(Action<UiHost> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _layoutCompleted += handler;
        return new HandlerToken<Action<UiHost>>(RemoveLayoutCompletedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddLayoutCompletedHandler"/> added.</summary>
    public static void RemoveLayoutCompletedHandler(Action<UiHost> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _layoutCompleted -= handler;
    }

    /// <summary>
    /// Tells the layout handlers that this host has finished arranging. Called by a completed pass
    /// and by a scroll that changed what the last pass put on screen.
    /// </summary>
    /// <remarks>
    /// The flag is what keeps a handler from starting another pass of the same notification - a
    /// handler is allowed to call <see cref="PerformLayout"/>, and the call completes, it just does
    /// not raise the event again - and it is restored rather than cleared because a layout can run
    /// from inside another one. An exception from a handler is deliberately left to the caller's
    /// guard: the pass is already complete, so nothing is half-arranged.
    /// </remarks>
    internal void NotifyLayoutCompleted()
    {
        if (_layoutCompleted is null || _raisingLayoutCompleted)
        {
            return;
        }

        _raisingLayoutCompleted = true;
        try
        {
            _layoutCompleted(this);
        }
        finally
        {
            _raisingLayoutCompleted = false;
        }
    }

    /// <summary>True while any host is inside <see cref="Paint"/>.</summary>
    /// <remarks>
    /// <para>
    /// A backend owns the surface it hands to <see cref="Paint"/> and keeps using it after the call
    /// returns - it presents or restores the canvas there. Destroying a window during that call
    /// disposes the surface the backend is still holding, which is a native use-after-free rather
    /// than a managed exception. A frame-starting handler is the ordinary way to ask for a close, and
    /// it runs inside the paint, so teardown reads this and defers itself to the next loop turn.
    /// </para>
    /// <para>
    /// Static and counted: a paint is a process-wide UI-thread operation, a nested paint still holds
    /// the outer surface, and a handler that closes a different window must not tear down while any
    /// paint is in flight. It is false outside <see cref="Paint"/>, which is where the deferred work
    /// runs.
    /// </para>
    /// </remarks>
    public static bool IsPainting => Volatile.Read(ref _paintDepth) > 0;

    /// <summary>Adds a handler that runs when the control under the pointer requests a different cursor.</summary>
    public IDisposable AddCursorChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _cursorChanged += handler;
        return new HandlerToken<Action<object>>(RemoveCursorChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddCursorChangedHandler"/> added.</summary>
    public void RemoveCursorChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _cursorChanged -= handler;
    }

    /// <summary>
    /// Adds a handler that runs before a context menu is shown, so an application can supply one
    /// for a control that carries no <see cref="Control.ContextMenu"/> of its own.
    /// </summary>
    public IDisposable AddContextMenuOpeningHandler(Action<object, ContextMenuEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _contextMenuOpening += handler;
        return new HandlerToken<Action<object, ContextMenuEventArgs>>(RemoveContextMenuOpeningHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddContextMenuOpeningHandler"/> added.</summary>
    public void RemoveContextMenuOpeningHandler(Action<object, ContextMenuEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _contextMenuOpening -= handler;
    }

    /// <summary>The root of the control tree.</summary>
    public Control? Root
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

            SetHovered(null);
            _captured = null;
            _selecting = false;
            _draggingSelection = false;
            ClearSelection();

            if (_overlays is not null)
            {
                foreach (var overlay in _overlays)
                {
                    overlay.AttachHost(null);
                }

                _overlays.Clear();
            }

            _root?.AttachHost(null);
            _root = value;
            _root?.AttachHost(this);

            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>
    /// The window-scoped service provider, assigned by the <see cref="Window"/> that owns this
    /// host. Null when a host is used standalone, as the harnesses do.
    /// </summary>
    public IServiceProvider? Services { get; set; }

    /// <summary>
    /// The window this host draws, or null when the host is used standalone. Assigned by
    /// <see cref="Window"/> so that code inside a control can parent dialogs on its window.
    /// </summary>
    /// <remarks>
    /// The host follows the window's own lifecycle: the tree is told when the window appears, so a
    /// control that was waiting to be seen (the marquee progress bar, the caret) can start, and
    /// again when it closes, so nothing is left ticking for a window nobody is looking at.
    /// </remarks>
    public Window? Window
    {
        get => _window;
        set
        {
            if (ReferenceEquals(_window, value))
            {
                return;
            }

            DetachWindow();
            _window = value;

            if (value is not null)
            {
                _windowShownToken = value.AddShownHandler(OnWindowDisplayChanged);
                _windowClosedToken = value.AddClosedHandler(OnWindowClosed);
            }

            _root?.UpdateDisplayed();
        }
    }

    private void OnWindowDisplayChanged(object sender) => _root?.UpdateDisplayed();

    private void OnWindowClosed(object sender)
    {
        DetachWindow();
        _root?.UpdateDisplayed();
    }

    private void DetachWindow()
    {
        _windowShownToken?.Dispose();
        _windowClosedToken?.Dispose();
        _windowShownToken = null;
        _windowClosedToken = null;
    }

    /// <summary>Resolves <typeparamref name="TService"/> from <see cref="Services"/>, or null when there is none.</summary>
    /// <typeparam name="TService">The service to resolve.</typeparam>
    public TService? GetService<TService>() where TService : class =>
        Services?.GetService(typeof(TService)) as TService;

    /// <summary>Size of the drawable area in logical pixels (96 DPI).</summary>
    public SKSize ClientSize => _clientSize;

    /// <summary>
    /// The colour the host clears the surface with: a literal <see cref="SKColor"/>, a palette
    /// <see cref="ThemeColor"/> role, or <see cref="PropertyValue.Inherited"/> for the theme's
    /// background.
    /// </summary>
    /// <remarks>
    /// The property reports what was assigned, not what is painted. Read
    /// <see cref="EffectiveClearColor"/> for the colour the surface is cleared with. Nothing is
    /// assigned by default, which follows <see cref="ThemeColor.Background"/>, so the window follows
    /// the theme on its own; a role is resolved when it is read, so a palette swap recolours the
    /// next frame.
    /// </remarks>
    public Union<SKColor, ThemeColor, InheritedValue> ClearColor
    {
        get => _clearColor;
        set => _clearColor = value;
    }

    /// <summary>
    /// The colour the surface is cleared with: the assignment, else
    /// <see cref="ThemeColor.Background"/>.
    /// </summary>
    /// <remarks>
    /// A literal colour is returned as it is and a role is resolved through
    /// <see cref="Theme.ColorOf"/> on every read, so a palette swap is a repaint rather than a
    /// rebuild. A backend that erases the window natively asks the same question through this
    /// property, so the platform and the application agree on what the window is cleared with.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <see cref="ClearColor"/> was assigned <c>default</c>, so it holds no case.
    /// </exception>
    public SKColor EffectiveClearColor
    {
        get
        {
            _clearColor.ThrowIfUnspecified();

            if (_clearColor.TryGetValue<SKColor>(out var literal))
            {
                return literal;
            }

            return _clearColor.TryGetValue<ThemeColor>(out var role)
                ? Theme.ColorOf(role)
                : Theme.ColorOf(ThemeColor.Background);
        }
    }

    /// <summary>The control that holds the keyboard focus, or null when no control does.</summary>
    public Control? FocusedControl => _focused;

    /// <summary>
    /// The control that currently owns a selection of its own, such as a text box with a caret,
    /// or null when the host's own <see cref="Selection"/> is the only selection.
    /// </summary>
    public Control? SelectionOwner => _selectionOwner;

    /// <summary>The control the pointer is over, or null when it is over none.</summary>
    public Control? HoveredControl => _hovered;

    /// <summary>
    /// Where the pointer was the last time it moved over this host, in client coordinates, or null
    /// when it has not moved over it since it last left. A tool tip is placed against this rather
    /// than against the control that owns it, so the tip appears where the user is looking.
    /// </summary>
    public SKPoint? MousePosition => _mousePosition;

    /// <summary>
    /// The control that took the pointer with <see cref="CaptureMouse"/>, or null when nothing has
    /// it. While a control is captured, pointer moves, the button release and wheel turns go to it
    /// wherever the pointer is.
    /// </summary>
    public Control? CapturedControl => _captured;

    /// <summary>The cursor the control under the pointer asked for.</summary>
    /// <remarks>
    /// A control reports the cursor for a point through <see cref="Control.GetCursorAt"/>, and the
    /// backend sets the platform cursor from this when <see cref="AddCursorChangedHandler"/> fires.
    /// </remarks>
    public Cursor Cursor => _cursor;

    /// <summary>
    /// The selection driven by pointer input. It can span several controls, so it lives here
    /// rather than in a control.
    /// </summary>
    public Selection Selection => _selection;

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
    /// <remarks>
    /// The layout pass is deferred to the next frame rather than run here. A resize arrives as a
    /// stream of sizes - a dragged edge sends one every few milliseconds - and each pass lays the
    /// whole tree out again, so running one per size means the tree is laid out dozens of times for
    /// frames nobody ever sees. Marking the frame dirty instead means every size that arrives before
    /// the next paint costs a comparison, and the pass runs once, for the size that gets painted.
    /// </remarks>
    public void SetClientSize(float width, float height)
    {
        width = MathF.Max(0f, width);
        height = MathF.Max(0f, height);

        if (MathF.Abs(width - _clientSize.Width) < 0.01f && MathF.Abs(height - _clientSize.Height) < 0.01f)
        {
            return;
        }

        _clientSize = new SKSize(width, height);
        _layoutPending = true;
        Invalidate();
    }

    /// <summary>
    /// The menu bar drawn inside the window, or null for a window with no in-window bar.
    /// </summary>
    /// <remarks>
    /// The bar belongs to the window rather than to the tree, so the host arranges it across the top
    /// of the client area and gives the root everything below it. A backend that hands its menus to
    /// the platform instead - macOS, which owns the system-wide bar - leaves this null.
    /// </remarks>
    public MenuBarView? MenuBar
    {
        get => _menuBar;
        set
        {
            if (ReferenceEquals(_menuBar, value))
            {
                return;
            }

            if (_menuBar is { } previous)
            {
                _menuBar = null;
                previous.AttachHost(null);
            }

            _menuBar = value;

            if (_menuBar is { } bar)
            {
                bar.AttachHost(this);
            }

            _layoutPending = true;
            Invalidate();
        }
    }

    /// <summary>
    /// Asks for one control's own layout to run again before the next frame is painted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A splitter being dragged asks for this on every pointer move, and a pointer reports far more
    /// often than a screen refreshes. Laying the container out inside the request would arrange the
    /// subtree in the moving panel once per report, for frames nobody ever sees; queueing it folds
    /// every request between two paints into the one pass that is actually drawn.
    /// </para>
    /// <para>
    /// Only the control that asked is laid out, and only if it is still in this window when the
    /// frame comes. Its own bounds did not change - nothing above it has to look at it again - so
    /// this is not the whole-tree pass <see cref="SetClientSize"/> defers, which is why hit testing
    /// does not wait for it either: the pointer is being captured by the control doing the asking.
    /// </para>
    /// </remarks>
    /// <param name="control">The control that has to arrange its own children again.</param>
    public void RequestLayout(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (!control.ArrangeRequested)
        {
            control.ArrangeRequested = true;
            _arrangeQueue.Add(control);
        }

        Invalidate();
    }

    /// <summary>Runs the layouts that were asked for since the last frame.</summary>
    /// <remarks>
    /// Arranging a control can ask for another one - the panels inside a splitter, a list that
    /// refilled itself - so this repeats until nothing is left asking. The bound is there because a
    /// container that asked for itself on every pass would otherwise never let a frame happen.
    /// </remarks>
    private void FlushArrangements()
    {
        var arranged = false;

        for (int pass = 0; pass < MaxArrangementPasses && _arrangeQueue.Count > 0; pass++)
        {
            // A pass remembers the sizes it measured, so it has to know it is a new one, exactly as
            // a whole-tree pass does.
            Control.BeginLayoutEpoch();

            var queued = _arrangeQueue.ToArray();
            _arrangeQueue.Clear();

            foreach (var control in queued)
            {
                control.ArrangeRequested = false;

                // The control may have left this window, or been disposed, since it asked.
                if (ReferenceEquals(control.Host, this))
                {
                    control.PerformLayout();
                    arranged = true;
                }
            }
        }

        foreach (var control in _arrangeQueue)
        {
            control.ArrangeRequested = false;
        }

        _arrangeQueue.Clear();

        // This runs from Paint and from a press as well as from the whole-tree pass, and each is a
        // pass that moved controls without one. The whole-tree pass notifies for itself once it
        // returns, so this only speaks for the runs that stand alone.
        if (arranged && !_performingLayout)
        {
            NotifyLayoutCompleted();
        }
    }

    /// <summary>Arranges the menu bar across the top of the client area and the root below it.</summary>
    public void PerformLayout()
    {
        // A pass can run from inside a handler - hit testing asks for one when a size arrived since
        // the last frame - so the flag is saved and restored rather than cleared.
        bool wasPerformingLayout = _performingLayout;
        _performingLayout = true;

        try
        {
            _layoutPending = false;

            // A pass remembers the sizes it measured, so it needs to know when it is a new one.
            Control.BeginLayoutEpoch();

            // A theme font assigned after the controls exist changes how much room their text needs. The
            // controls resolve the font themselves, so all the tree needs is to be told that caches
            // derived from the old one are stale; the pass below re-measures everything anyway.
            if (_themeFontVersion != Theme.FontVersion)
            {
                _themeFontVersion = Theme.FontVersion;
                _root?.InvalidateAppearanceTree();
                _menuBar?.InvalidateAppearanceTree();
                _root?.PerformLayoutTree();
                _menuBar?.PerformLayoutTree();
            }

            var top = 0f;
            var arranged = false;

            if (_menuBar is { } menuBar)
            {
                var size = menuBar.Measure(new SKSize(_clientSize.Width, _clientSize.Height));
                top = MathF.Min(size.Height, _clientSize.Height);
                menuBar.SetArrangedBounds(new SKRect(0f, 0f, _clientSize.Width, top));
                menuBar.PerformLayout();
                arranged = true;
            }

            if (_root is null)
            {
                // The bar is the only thing this pass can have arranged. A host with neither a bar nor
                // a tree has arranged nothing, and the event promises not to fire for that.
                if (arranged)
                {
                    NotifyLayoutCompleted();
                }

                return;
            }

            _root.SetArrangedBounds(new SKRect(0f, top, _clientSize.Width, _clientSize.Height));
            _root.PerformLayout();

            if (_overlays is not null)
            {
                foreach (var overlay in _overlays)
                {
                    overlay.PerformLayout();
                }
            }

            // Whoever asked for this pass expects the tree to be arranged when it returns, including the
            // controls that asked for their own arrangement since the last frame.
            FlushArrangements();

            NotifyLayoutCompleted();
        }
        finally
        {
            _performingLayout = wasPerformingLayout;
        }
    }

    /// <summary>
    /// Controls painted above the tree and hit tested before it, in the order they were added.
    /// </summary>
    /// <remarks>
    /// An overlay is positioned in host coordinates by whoever owns it rather than by layout, so
    /// this is how a popup gets drawn inside the window when the platform cannot give it a window
    /// of its own. It still belongs to this host, so it resolves services and themes normally.
    /// </remarks>
    public IReadOnlyList<Control> Overlays =>
        _overlays ?? (IReadOnlyList<Control>)Array.Empty<Control>();

    /// <summary>Adds a control to the overlay layer, on top of the ones already there.</summary>
    public void AddOverlay(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (control.Parent is not null)
        {
            throw new ArgumentException("An overlay cannot be a child of another control.", nameof(control));
        }

        _overlays ??= new List<Control>(2);

        if (_overlays.Contains(control))
        {
            return;
        }

        _overlays.Add(control);
        control.AttachHost(this);
        control.PerformLayout();
        Invalidate();
    }

    /// <summary>Removes a control from the overlay layer.</summary>
    public void RemoveOverlay(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (_overlays is null || !_overlays.Remove(control))
        {
            return;
        }

        control.AttachHost(null);
        Invalidate();
    }

    /// <summary>
    /// Whether the backend keeps the pixels of the previous frame between paints, so a paint only
    /// has to cover the area that was invalidated. Off by default, because a backend that
    /// rasterizes the whole surface every frame - GTK4's CPU path and the headless renderer both
    /// do - would be left with blank patches where the retained content should be. Turn it on only
    /// when <see cref="Paint"/> draws into a surface that outlives the frame.
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

    /// <summary>How long the last painted frame took to draw.</summary>
    /// <remarks>
    /// Measured around the library's own work - clipping, clearing and walking the tree - so it
    /// leaves out whatever the backend spends presenting the frame, which the library cannot see.
    /// The debug paint's red frame is not counted; the content frame that replaces it is.
    /// </remarks>
    public TimeSpan LastFrameTime { get; private set; }

    /// <summary>The area the last painted frame covered, in logical pixels.</summary>
    public SKRectI LastFrameArea { get; private set; }

    /// <summary>How many frames have been painted.</summary>
    public long FrameCount { get; private set; }

    /// <summary>How long a red debug frame stays on screen before its content is painted over it.</summary>
    private const int DefaultDebugPaintHoldMs = 80;

    /// <summary>
    /// Set <c>KOLVIR_DEBUG_PAINT=1</c> to paint every invalidated area in flat red for one frame
    /// before painting its content, which shows what a partial repaint actually covers. A number
    /// sets how long the red frame is held, in milliseconds.
    /// </summary>
    private static readonly int DebugPaintHoldMs = ReadDebugPaintHold();

    private static int ReadDebugPaintHold()
    {
        var value = Environment.GetEnvironmentVariable("KOLVIR_DEBUG_PAINT");

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

        // Counted rather than boolean: a backend can paint from inside a paint (a GPU surface that
        // failed and re-enters through the software path), and the outer surface is still held while
        // the inner one runs. IsPainting must stay true for the whole stack.
        Interlocked.Increment(ref _paintDepth);
        try
        {
            return PaintCore(canvas, full);
        }
        finally
        {
            Interlocked.Decrement(ref _paintDepth);
        }
    }

    private SKRectI PaintCore(SKCanvas canvas, bool full)
    {
        // Whatever a frame-starting handler changes is laid out and drawn by this frame: it runs before
        // the layout pass and before the dirty region is taken. That is what makes it the place for
        // work that has been deferred to "the next frame" - a virtual-DOM render, for instance.
        if (_frameStarting is { } frameStarting)
        {
            Guard(frameStarting, this);
        }

        // Sizes and structure that arrived since the last frame are answered here: this is the one
        // point where the tree has to be right, because it is about to be drawn.
        if (_layoutPending)
        {
            PerformLayout();
        }

        // Controls that rearranged their own subtree since the last frame, which the whole-tree
        // pass above does not reach: their bounds are unchanged, so nothing arranged them again.
        FlushArrangements();

        // A font assigned to the theme or to a control after the last frame moved text that is
        // already on screen, so the frame that shows it is laid out first. A repaint covers the
        // whole surface either way: text that grew has taken room from something else.
        if (_themeFontVersion != Theme.FontVersion)
        {
            // The layout pass notices the new generation, re-measures the tree and marks the whole
            // surface dirty, so the frame that shows the new font is painted in one piece.
            PerformLayout();
            Invalidate();
        }

        var whole = full || !PartialUpdates || _dirtyAll || _dirty.IsEmpty;
        var area = whole ? SurfaceBounds : _dirty.Bounds;

        if (DebugPaintHoldMs > 0 && !whole)
        {
            // Two frames per repaint: the area in flat red, then the content that replaces it. The
            // red frame leaves the dirty region alone, so the content frame covers exactly the same
            // area. The wait between them belongs out here rather than inside the content frame: a
            // backend that only shows what it drew when the message loop turns - AppKit does - has
            // the next paint overwrite the red frame before it is ever on screen, so waiting inside
            // the paint is what hid the red frame entirely on macOS.
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
                ScheduleDebugContentFrame(new SKRegion(_dirty));

                return area;
            }

            _debugHoldingContent = false;
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

        var started = Stopwatch.GetTimestamp();

        Guard(() =>
        {
            canvas.Save();
            ClipToPaintArea(canvas, region, whole, area);

            // DrawPaint rather than Clear: both fill the clip, but only DrawPaint is documented to
            // fill the clip *region*, and the region the dirty area came from can have holes in it.
            using var clear = new SKPaint { Color = EffectiveClearColor, BlendMode = SKBlendMode.Src };
            canvas.DrawPaint(clear);

            _menuBar?.Render(canvas);
            _root?.Render(canvas);

            if (_overlays is not null)
            {
                foreach (var overlay in _overlays)
                {
                    overlay.Render(canvas);
                }
            }

            canvas.Restore();
        });

        // Published after the tree has drawn rather than before it, so a control that reads these
        // while painting shows the frame before its own. That is what a readout of the last frame
        // has to say, and it keeps the readout from invalidating itself on every frame.
        LastFrameTime = Stopwatch.GetElapsedTime(started);
        LastFrameArea = area;
        FrameCount++;

        if (_framePainted is { } framePainted)
        {
            Guard(framePainted, this);
        }

        return area;
    }

    /// <summary>Asks for the frame that paints over the red debug frame, once it has been seen.</summary>
    /// <param name="region">The area the red frame covers, in logical pixels. It is disposed here.</param>
    private void ScheduleDebugContentFrame(SKRegion region)
    {
        if (Application.Current is not { } application)
        {
            // A host painted on its own has no loop to turn, so there is nothing to wait for and the
            // content frame runs straight after the red one.
            using (region)
            {
                Invalidate(region);
            }

            return;
        }

        _debugFrameTimer?.Dispose();
        _debugFrameTimer = new System.Threading.Timer(
            _ =>
            {
                try
                {
                    application.BeginInvoke(() =>
                    {
                        using (region)
                        {
                            Invalidate(region);
                        }
                    });
                }
                catch (ObjectDisposedException)
                {
                    // The application shut down while the red frame was held.
                    region.Dispose();
                }
            },
            null,
            DebugPaintHoldMs,
            System.Threading.Timeout.Infinite);
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
    /// Re-reads the platform's light/dark preference through <see cref="SystemTheme"/> and,
    /// when it no longer matches the palette, switches the palette and repaints. Backends call this
    /// once at startup and again from their theme-changed notification (<c>WM_SETTINGCHANGE</c> and
    /// <c>WM_THEMECHANGED</c> on Windows).
    /// </summary>
    /// <returns>True when the appearance changed.</returns>
    public bool RefreshTheme()
    {
        if (!SystemTheme.Refresh())
        {
            return false;
        }

        Invalidate();
        return true;
    }

    /// <summary>
    /// Routes a pointer move: hover enter and leave, the move itself, the cursor the control asks
    /// for, and the extension of a selection being dragged. A captured control receives the move
    /// wherever the pointer is.
    /// </summary>
    /// <param name="location">The pointer position, in logical client pixels.</param>
    /// <remarks>
    /// A press only turns into a selection once the pointer has travelled
    /// <see cref="SelectionDragThreshold"/> from where it was pressed, so a plain click still reaches
    /// the control underneath.
    /// </remarks>
    public void ProcessMouseMove(SKPoint location) => Guard(() =>
    {
        _mousePosition = location;

        var hit = HitTest(location, hover: true);

        if (!ReferenceEquals(hit, _hovered))
        {
            SetHovered(hit);
        }

        var target = _captured ?? hit;
        target?.RaiseMouseMove(new MouseEventArgs(ToLocal(target, location)));
        SetCursor(hit is null ? Cursor.Default : hit.GetCursorAt(ToLocal(hit, location)));

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

    /// <summary>
    /// Routes a press: captures the pointer, moves the focus when the control under it takes focus,
    /// starts or clears a selection, and raises the press on the control.
    /// </summary>
    /// <param name="location">The pointer position, in logical client pixels.</param>
    /// <param name="button">The button that was pressed.</param>
    /// <param name="modifiers">The modifier keys held while the button was pressed.</param>
    /// <param name="clickCount">How many clicks the platform counts this press as; 2 is a double click.</param>
    /// <remarks>
    /// A press that cannot take the focus - the scrollbar a panel owns for itself, a plain label -
    /// leaves the selection and the caret of the focused text box alone, rather than clearing
    /// something the user is still typing into.
    /// </remarks>
    public void ProcessMouseDown(
        SKPoint location,
        MouseButtons button = MouseButtons.Left,
        Modifiers modifiers = Modifiers.None,
        int clickCount = 1) => Guard(() =>
    {
        // A press is aimed at whatever is under it, so any arrangement the last frame's drags asked
        // for runs first: one press per gesture can afford it, where the moves that queue it cannot.
        FlushArrangements();

        var hit = HitTest(location);
        _captured = hit;
        var takesFocus = hit is null || (hit.TabStop && hit.Focusable);

        if (button == MouseButtons.Left)
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

        hit.RaiseMouseDown(new MouseEventArgs(ToLocal(hit, location), button, modifiers, clickCount));
    });

    /// <summary>
    /// Routes a button release, ending any selection drag and raising the release on the captured
    /// control, or on the control under the pointer when nothing was captured.
    /// </summary>
    /// <param name="location">The pointer position, in logical client pixels.</param>
    /// <param name="button">The button that was released.</param>
    /// <param name="modifiers">The modifier keys held while the button was released.</param>
    public void ProcessMouseUp(SKPoint location, MouseButtons button = MouseButtons.Left, Modifiers modifiers = Modifiers.None) => Guard(() =>
    {
        if (button == MouseButtons.Left)
        {
            _selecting = false;
            _draggingSelection = false;
        }

        var target = _captured ?? HitTest(location);
        _captured = null;
        target?.RaiseMouseUp(new MouseEventArgs(ToLocal(target, location), button, modifiers));
    });

    /// <summary>Clears the hover state when the pointer leaves the surface.</summary>
    public void ProcessMouseLeave() => Guard(() =>
    {
        _mousePosition = null;
        SetHovered(null);
        SetCursor(Cursor.Default);
    });

    /// <summary>
    /// Makes <paramref name="hit"/> the hovered control, raising the enter and leave events on every
    /// control whose hover changed.
    /// </summary>
    /// <param name="hit">The control under the pointer, or null when the pointer left the surface.</param>
    /// <remarks>
    /// The pointer is over one control and over each of that control's ancestors that asked to be
    /// told about their descendants (<see cref="Control.HoverIncludesChildren"/>), so that chain is
    /// what the events travel along: a leave goes deepest first, so a container hears about it after
    /// its child, and an enter goes shallowest first, so it hears before it.
    /// </remarks>
    private void SetHovered(Control? hit)
    {
        _previousHoverChain.Clear();
        _previousHoverChain.AddRange(_hoverChain);

        _hoverChain.Clear();
        for (var control = hit; control is not null; control = control.Parent)
        {
            if (ReferenceEquals(control, hit) || control.HoverIncludesChildren)
            {
                _hoverChain.Add(control);
            }
        }

        _hoverChain.Reverse();
        _hovered = hit;

        for (int i = _previousHoverChain.Count - 1; i >= 0; i--)
        {
            if (!_hoverChain.Contains(_previousHoverChain[i]))
            {
                _previousHoverChain[i].RaiseMouseLeave();
            }
        }

        foreach (var control in _hoverChain)
        {
            if (!_previousHoverChain.Contains(control))
            {
                control.RaiseMouseEnter();
            }
        }
    }

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
    public bool ProcessMouseWheel(SKPoint location, float delta, Modifiers modifiers = Modifiers.None) => Guard(() =>
    {
        if (delta == 0f)
        {
            return false;
        }

        var target = _captured ?? HitTest(location);

        for (var control = target; control is not null; control = control.Parent)
        {
            if (control.RaiseMouseWheel(new MouseEventArgs(ToLocal(control, location), modifiers: modifiers, delta: delta)))
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
    /// <para>
    /// The control under the pointer composes the menu: its own <see cref="Control.ContextMenu"/>
    /// (or the nearest ancestor's, or whatever it builds from its own state) is the starting point,
    /// and the handlers it, its ancestors and finally this host added all receive that same
    /// <see cref="ContextMenuEventArgs.Menu"/> instance to add to. A text box contributes Cut,
    /// Copy, Paste and Select All, so an annotation menu sits beside the ordinary editing commands
    /// instead of replacing them.
    /// </para>
    /// <para>
    /// A handler that wants a menu of its own assigns a new menu to
    /// <see cref="ContextMenuEventArgs.Menu"/> (or clears the one it was given), which is how
    /// the control's contribution is suppressed. The handlers on the host are the application-wide
    /// fallback: they run only when the control under the pointer ran no handler and supplied no
    /// menu, so a text box's editing commands are never shadowed by a page menu. The selection's
    /// **Copy** item is offered last, when nothing else has anything to show.
    /// </para>
    /// </remarks>
    public bool ProcessContextMenu(SKPoint location) => Guard(() =>
    {
        var hit = HitTest(location);
        var args = new ContextMenuEventArgs(hit, location)
        {
            Menu = hit?.BuildContextMenu(),
        };

        bool handled = hit?.RaiseContextMenuOpening(args) ?? false;

        var menu = args.Menu;
        if (!handled && (menu is null || menu.IsEmpty))
        {
            _contextMenuOpening?.Invoke(this, args);
            menu = args.Menu;
        }

        if (menu is null || menu.IsEmpty)
        {
            menu = _selection.IsEmpty ? null : ContextMenu.CreateCopyMenu(this);
        }

        return menu is not null && menu.Show(this, hit, location);
    }, false);

    /// <summary>
    /// Routes a key press to the focused control. Returns true when the control consumed the
    /// key, which tells the adapter to swallow the platform key event (and its text input).
    /// </summary>
    /// <remarks>
    /// An input sink sees the key first - an open menu owns the keyboard while it is down - then the
    /// window's menu bar, the way a native menu's accelerators do, so a shortcut is not shadowed by
    /// whichever control happens to have the focus. macOS dispatches Command combinations through
    /// its own menu bar before a view sees them at all, so the same lookup there only ever covers the
    /// modifiers AppKit does not claim.
    /// </remarks>
    public bool ProcessKeyDown(Key key, Modifiers modifiers = Modifiers.None) => Guard(() =>
    {
        if (ProcessKeyInSinks(key, modifiers))
        {
            return true;
        }

        // Alt+mnemonic reaches the bar before anything else, the way a native menu bar is reached
        // even when a control has the focus.
        if (_menuBar is { } bar && bar.ProcessKeyDown(key, modifiers))
        {
            return true;
        }

        if (Window is { } window && window.TryInvokeShortcut(key, modifiers))
        {
            return true;
        }

        if (key == Key.Tab)
        {
            MoveFocus((modifiers & Modifiers.Shift) != 0 ? -1 : 1);
            return true;
        }

        return _focused?.RaiseKeyDown(new KeyEventArgs(key, modifiers)) ?? false;
    }, false);

    /// <summary>Routes a key release to the focused control.</summary>
    /// <param name="key">The key that was released.</param>
    /// <param name="modifiers">The modifier keys held while it was released.</param>
    public void ProcessKeyUp(Key key, Modifiers modifiers = Modifiers.None) =>
        Guard(() => _focused?.RaiseKeyUp(new KeyEventArgs(key, modifiers)));

    /// <summary>
    /// Routes typed text to the focused control, after the input sinks have been offered it.
    /// </summary>
    /// <param name="text">The characters the platform produced for one input event.</param>
    public void ProcessTextInput(string text) =>
        Guard(() =>
        {
            if (ProcessTextInSinks(text))
            {
                return;
            }

            _focused?.RaiseTextInput(new TextInputEventArgs(text));
        });

    /// <summary>
    /// Asks the focused control whether it takes text composition at all, such as a text box that
    /// is not read-only. A platform input method calls this when it begins a composition.
    /// </summary>
    /// <returns>
    /// True when the control took the composition, false when nothing is focused or the control
    /// declined.
    /// </returns>
    /// <remarks>
    /// Until a composition begins nothing is held: a control that declines is never touched by a
    /// preedit, and the commit still arrives through <see cref="ProcessTextInput"/> as ordinary
    /// text, so a field that refuses composition is simply a field where the preedit is not shown.
    /// </remarks>
    public bool BeginComposition() =>
        Guard(() => _focused?.RaiseCompositionStarted() ?? false, false);

    /// <summary>
    /// Offers the focused control the uncommitted text of a composition and the position of the
    /// platform's caret inside it.
    /// </summary>
    /// <param name="text">The preedit string. It is not part of the control's value.</param>
    /// <param name="caretIndex">Where the platform's caret sits inside <paramref name="text"/>.</param>
    /// <returns>True when the control is composing, false when nothing is focused or it declined.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public bool UpdateComposition(string text, int caretIndex)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Guard(() => _focused?.RaiseCompositionUpdated(text, caretIndex) ?? false, false);
    }

    /// <summary>
    /// Ends a composition with <paramref name="text"/>: the focused control drops its preedit and
    /// the text goes through the ordinary text path.
    /// </summary>
    /// <param name="text">The committed text, which may be empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <remarks>
    /// The commit is exactly as if the same characters had been typed: it is offered to the input
    /// sinks first and then inserted by the control, so undo, validation,
    /// <see cref="TextBox.MaxLength"/> and the autocomplete handler all see an ordinary insert.
    /// </remarks>
    public void CommitComposition(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Guard(() =>
        {
            _focused?.RaiseCompositionCancelled();
            ProcessTextInput(text);
        });
    }

    /// <summary>
    /// Ends a composition without committing anything, leaving the focused control's value and
    /// undo history exactly as they were.
    /// </summary>
    public void CancelComposition() => Guard(() => _focused?.RaiseCompositionCancelled());

    /// <summary>
    /// The insertion point of the focused control, in host (logical) coordinates, which is where a
    /// platform input method places its candidate window.
    /// </summary>
    /// <param name="bounds">The caret rect, or an empty rect when there is no answer.</param>
    /// <returns>True when a control answered.</returns>
    /// <remarks>
    /// A control with no caret of its own answers its own bounds, so a backend always has a
    /// sensible rectangle to anchor to. Each backend converts from this host space into the space
    /// its platform wants, rather than the other way round.
    /// </remarks>
    public bool TryGetCaretBounds(out SKRect bounds)
    {
        if (_focused is { } control && control.TryGetCaretBounds(out bounds))
        {
            return true;
        }

        bounds = default;
        return false;
    }

    /// <summary>
    /// Pushes <paramref name="sink"/> onto the window's input sinks, so it sees keys and typed text
    /// before the focused control does. Dispose the result to remove it again.
    /// </summary>
    public IDisposable AddInputSink(IInputSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        _inputSinks ??= new List<IInputSink>();

        if (!_inputSinks.Contains(sink))
        {
            _inputSinks.Add(sink);
        }

        return new InputSinkToken(this, sink);
    }

    /// <summary>Removes a sink that <see cref="AddInputSink"/> added.</summary>
    public void RemoveInputSink(IInputSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _inputSinks?.Remove(sink);
    }

    /// <summary>
    /// Offers a key to the sinks, newest first. A sink may remove itself while it handles the key,
    /// so the walk re-reads the live list rather than a copy of it.
    /// </summary>
    private bool ProcessKeyInSinks(Key key, Modifiers modifiers)
    {
        if (_inputSinks is not { Count: > 0 } sinks)
        {
            return false;
        }

        for (var i = sinks.Count - 1; i >= 0; i--)
        {
            if (i < sinks.Count && sinks[i].ProcessKeyDown(key, modifiers))
            {
                return true;
            }
        }

        return false;
    }

    private bool ProcessTextInSinks(string text)
    {
        if (_inputSinks is not { Count: > 0 } sinks)
        {
            return false;
        }

        for (var i = sinks.Count - 1; i >= 0; i--)
        {
            if (i < sinks.Count && sinks[i].ProcessTextInput(text))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Moves keyboard focus to the given control (null clears focus).</summary>
    /// <remarks>
    /// A control that cannot be reached - disabled, not focusable, or inside a container that has
    /// stopped showing its content, such as the section of a collapsed expander - is not focused.
    /// The Tab order already leaves such a control out; this is what stops anything else, a
    /// programmatic <see cref="Control.Focus"/> included, from putting the focus where the
    /// user cannot see it.
    /// </remarks>
    public void SetFocus(Control? control)
    {
        if (ReferenceEquals(_focused, control))
        {
            return;
        }

        if (control is not null && (!control.Focusable || !control.IsReachable))
        {
            return;
        }

        var previous = _focused;

        // A composition belongs to the control it started on, so leaving the field ends it. The
        // preedit is dropped before the control hears about the lost focus, so nothing it does in
        // response sees a half-finished composition.
        previous?.RaiseCompositionCancelled();
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

    /// <summary>
    /// Sends every later pointer move, button release and wheel turn to <paramref name="control"/>
    /// until it releases the capture, whether or not the pointer is over it. A press captures the
    /// control it hit on its own; this is for a drag a control starts deliberately, so a scrollbar
    /// thumb keeps tracking the pointer outside the bar.
    /// </summary>
    /// <param name="control">The control to capture the pointer for.</param>
    /// <exception cref="ArgumentNullException"><paramref name="control"/> is null.</exception>
    public void CaptureMouse(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        _captured = control;
    }

    /// <summary>Gives up a capture taken by <paramref name="control"/>, if it is the one holding it.</summary>
    /// <param name="control">
    /// The control releasing the pointer. A control that does not hold the capture changes nothing.
    /// </param>
    public void ReleaseMouseCapture(Control control)
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
    public void ClaimSelection(Control owner)
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

        Clipboard.SetHtml(text, _selection.GetHtml(SelectionTextSeparator));
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

    /// <summary>
    /// Finds the control under a point. <paramref name="hover"/> asks for the control the pointer
    /// belongs to, which is the one below an overlay that only exists to catch presses.
    /// </summary>
    private Control? HitTest(SKPoint location, bool hover = false)
    {
        // Hit testing reads the rectangles layout produced, and a size that arrived since the last
        // frame has not been laid out yet. Answering it here aims the pointer at the tree that is
        // about to be on screen rather than the one it was in a frame ago.
        if (_layoutPending)
        {
            PerformLayout();
        }

        // A press on a title of the bar's own open menu belongs to the bar even while the dismiss
        // catcher covers it, so clicking another title switches menus and clicking the open one
        // closes it. Everywhere else, and over any other popup, the catcher keeps the press.
        var titleBar = _menuBar is { } bar && bar.HitTestTitle(new SKPoint(
            location.X - bar.Bounds.Left,
            location.Y - bar.Bounds.Top)) >= 0 ? bar : null;

        if (_overlays is not null)
        {
            for (int i = _overlays.Count - 1; i >= 0; i--)
            {
                var overlay = _overlays[i];

                if (overlay.PassesHoverThrough &&
                    (hover || titleBar?.OwnsDismissCatcher(overlay) == true))
                {
                    continue;
                }

                if (overlay.HitTest(location) is { } hit)
                {
                    return hit;
                }
            }
        }

        if (_menuBar?.HitTest(location) is { } barHit)
        {
            return barHit;
        }

        return _root?.HitTest(location);
    }

    /// <summary>
    /// Runs an entry point that a backend calls from a platform callback, so a failure reaches
    /// <see cref="Application.AddUnhandledExceptionHandler"/> instead of unwinding into the native frame
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
            Application.HandleNativeBoundary(ex);
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
            Application.HandleNativeBoundary(ex);
            return fallback;
        }
    }

    /// <summary>
    /// Guarded invocation of a handler list, without the closure <see cref="Guard(Action)"/> would need.
    /// </summary>
    /// <param name="action">The handlers to run; may be null or multicast.</param>
    /// <param name="state">What to pass them.</param>
    /// <remarks>
    /// A frame runs these on every paint, so the lambda a <c>Guard(() => action(state))</c> would capture
    /// is one allocation per frame per handler list. The lambda is what makes that allocation, not the
    /// call: an existing delegate is passed straight through.
    /// </remarks>
    private static void Guard<T>(Action<T> action, T state)
    {
        try
        {
            action(state);
        }
        catch (Exception ex)
        {
            Application.HandleNativeBoundary(ex);
        }
    }

    private void BeginSelection(Control control, SKPoint location)
    {
        ReleaseSelectionOwner();
        _selection.Begin(new SelectionPosition(control, control.GetSelectionOffset(ToLocal(control, location))));
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
    private SelectionPosition ResolveSelectionPosition(SKPoint location)
    {
        var target = FindSelectionTarget(HitTest(location));
        if (target is not null)
        {
            return new SelectionPosition(target, target.GetSelectionOffset(ToLocal(target, location)));
        }

        var nearest = FindNearestSelectable(location) ?? _selection.Focus.Control ?? _selection.Anchor.Control;
        return nearest is null
            ? default
            : new SelectionPosition(nearest, nearest.GetSelectionOffset(ToLocal(nearest, location)));
    }

    /// <summary>
    /// The selectable control whose client area is closest to <paramref name="location"/>. Ties
    /// keep the control that currently holds the moving end of the selection, so a drag along an
    /// edge between two neighbours does not flicker between them.
    /// </summary>
    private Control? FindNearestSelectable(SKPoint location)
    {
        var current = _selection.Focus.Control;
        Control? best = current is { } control && control.SelectionUnitCount > 0 ? control : null;
        float bestDistance = best is null
            ? float.MaxValue
            : DistanceToRect(location, ToHost(best, best.ClientRect));

        CollectNearestSelectable(_root, location, ref best, ref bestDistance);
        return best;
    }

    private static void CollectNearestSelectable(
        Control? control,
        SKPoint location,
        ref Control? best,
        ref float bestDistance)
    {
        if (control is null || !control.Visible || !control.EffectiveEnabled)
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
    /// <remarks>
    /// A zoomed container scales both the child's own position and the content offset it applies
    /// while painting, so the multiplication happens after both have been added.
    /// </remarks>
    private static SKPoint ToHost(Control control, SKPoint point)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            point = new SKPoint(point.X + current.Left, point.Y + current.Top);

            if (current.Parent is { } parent)
            {
                var offset = parent.GetChildOffset(current);
                var multiplier = parent.GetChildScaleFactor(current);
                point = new SKPoint(
                    (point.X + offset.X) * multiplier,
                    (point.Y + offset.Y) * multiplier);
            }
        }

        return point;
    }

    /// <summary>Maps a rectangle from a control's coordinate space to the host's.</summary>
    internal static SKRect ToHost(Control control, SKRect rect)
    {
        var origin = ToHost(control, SKPoint.Empty);
        var scale = control.EffectiveScaleMultiplier;

        return new SKRect(
            origin.X + (rect.Left * scale),
            origin.Y + (rect.Top * scale),
            origin.X + (rect.Right * scale),
            origin.Y + (rect.Bottom * scale));
    }

    /// <summary>
    /// Returns the control that should own a selection position for <paramref name="control"/>:
    /// the control itself when it is selectable, otherwise its nearest selectable ancestor (so
    /// clicking a link or a chip inside a selectable label selects the label).
    /// </summary>
    private static Control? FindSelectionTarget(Control? control)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            // A scrollbar inside a selectable control is chrome, not content: the press belongs to
            // the bar and must not begin, extend or release a selection of the control around it.
            if (current is ISelectionBarrier)
            {
                return null;
            }

            if (current.SelectionUnitCount > 0)
            {
                return current;
            }
        }

        return null;
    }

    private void SetCursor(Cursor cursor)
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
        var focusable = new List<Control>();
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

    /// <summary>
    /// The controls Tab reaches under <paramref name="root"/>, in the order it reaches them, by the
    /// same rules <see cref="MoveFocus"/> uses for the window's own tree.
    /// </summary>
    /// <remarks>
    /// A popup walks its content with this instead of moving the window's focus: the popup's tree is
    /// not the window's tree, so <see cref="MoveFocus"/> would step onto the controls behind it.
    /// </remarks>
    /// <param name="root">The top of the subtree to walk, or null for nothing.</param>
    /// <returns>The tab stops, empty when there are none.</returns>
    internal static List<Control> CollectTabStops(Control? root)
    {
        var stops = new List<Control>();
        CollectFocusable(root, stops);
        return stops;
    }

    /// <summary>
    /// Collects the controls Tab can reach, in the order it reaches them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tab order is per container, the way WinForms nests it: each container visits its own
    /// children in <see cref="Control.TabIndex"/> order, and a container that is itself a tab
    /// stop is collected before its children, so it is entered as a unit rather than interleaved
    /// with them. A child container's indices are therefore only compared with its siblings' -
    /// a control inside a nested panel does not sort against controls outside it.
    /// </para>
    /// <para>
    /// Equal <see cref="Control.TabIndex"/> values fall back to the order the children were
    /// added in, so the result is deterministic rather than dependent on how a sort happens to
    /// permute equals. With the default index of 0 everywhere that makes the walk plain tree order,
    /// which is what the host did before it ordered by index at all.
    /// </para>
    /// <para>
    /// Only the order changes here. Which controls are eligible is unchanged: a control is a stop
    /// when it is <see cref="Control.TabStop"/> and <see cref="Control.Focusable"/>, and
    /// an invisible or disabled control - or one inside an invisible or disabled container, or one
    /// inside a container that has stopped showing its content - takes no part in the walk at all.
    /// That last case is a collapsed <see cref="Expander"/>: its section stays in the tree but
    /// is showing nothing, so nothing inside it is a stop. The walk asks the control rather than
    /// knowing what kind of container it is looking at, which is what lets any container that
    /// arranges its children to nothing say the same thing.
    /// </para>
    /// </remarks>
    private static void CollectFocusable(Control? control, List<Control> output)
    {
        if (control is null || !control.IsReachable)
        {
            return;
        }

        if (control.TabStop && control.Focusable)
        {
            output.Add(control);
        }

        int count = control.Controls.Count;
        if (count == 0)
        {
            return;
        }

        if (count == 1)
        {
            CollectFocusable(control.Controls[0], output);
            return;
        }

        // Array.Sort is not stable, so the current position is part of the key: that is what makes
        // equal indices keep tree order. The library does not reference System.Linq for an OrderBy.
        var order = new int[count];
        for (int i = 0; i < count; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (left, right) =>
        {
            int byIndex = control.Controls[left].TabIndex.CompareTo(control.Controls[right].TabIndex);
            return byIndex != 0 ? byIndex : left.CompareTo(right);
        });

        foreach (int index in order)
        {
            CollectFocusable(control.Controls[index], output);
        }
    }

    /// <summary>Maps a host point into a control's client coordinates.</summary>
    /// <remarks>
    /// <para>
    /// This is the control's own public inverse, deliberately, rather than a second walk of the
    /// parent chain here: the mapping has to undo a parent's zoom before the level above it, because
    /// a parent's multiplier applies to its child's position and content offset as much as to the
    /// child's own coordinates. A control-first loop that divides and then subtracts applies the
    /// innermost inverse to the host point, so it divides every ancestor's position by the
    /// multiplier as well - a click inside a zoomed panel that sits away from the origin then lands
    /// tens of pixels away from the pointer.
    /// </para>
    /// <para>
    /// With every multiplier at one the order is invisible (subtractions commute and the division is
    /// a no-op), which is why the coordinate path only had to state the rule once zoom arrived.
    /// Sharing <see cref="Control.HostToClient"/> means the input path and the public
    /// control-to-host conversions cannot drift apart again.
    /// </para>
    /// </remarks>
    private static SKPoint ToLocal(Control control, SKPoint point) => control.HostToClient(point);

    /// <summary>
    /// The subscription <see cref="AddInputSink"/> hands back. It is not an
    /// <see cref="HandlerToken{T}"/> because a sink is an object, not a handler delegate.
    /// </summary>
    private sealed class InputSinkToken : IDisposable
    {
        private UiHost? _host;
        private readonly IInputSink _sink;

        /// <summary>Remembers the host and the sink it will remove.</summary>
        /// <param name="host">The host the sink was added to.</param>
        /// <param name="sink">The sink to remove on disposal.</param>
        public InputSinkToken(UiHost host, IInputSink sink)
        {
            _host = host;
            _sink = sink;
        }

        /// <summary>Removes the sink again. Disposing twice is harmless.</summary>
        public void Dispose()
        {
            _host?.RemoveInputSink(_sink);
            _host = null;
        }
    }
}
