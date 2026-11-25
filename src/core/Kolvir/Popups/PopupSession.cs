namespace Kolvir;

/// <summary>
/// The state a control carries for the popup API: the handlers it has subscribed, and the popup it
/// is currently shown in.
/// </summary>
/// <remarks>
/// It exists because an extension method cannot add a field to <see cref="Control"/>. Nothing
/// creates one until a control is used with <see cref="PopupExtensions"/>, so an application that
/// never shows a popup never allocates one.
/// </remarks>
internal sealed class PopupChannel
{
    private List<Action<object, PopupResult>>? _handlers;

    /// <summary>The popup currently showing this control as its content, or null.</summary>
    public PopupSession? Session { get; set; }

    /// <summary>Subscribes a closed handler, which stays until it is removed.</summary>
    public void Add(Action<object, PopupResult> handler) => (_handlers ??= []).Add(handler);

    /// <summary>Unsubscribes a closed handler; one that is not subscribed is ignored.</summary>
    public void Remove(Action<object, PopupResult> handler) => _handlers?.Remove(handler);

    /// <summary>
    /// Runs the closed handlers in subscription order. A copy is walked, so a handler that removes
    /// itself or another one does not disturb the pass - which matters because a handler is
    /// application code running inside the close path.
    /// </summary>
    public void Invoke(Control sender, PopupResult result)
    {
        if (_handlers is not { Count: > 0 } handlers)
        {
            return;
        }

        foreach (var handler in handlers.ToArray())
        {
            handler(sender, result);
        }
    }
}

/// <summary>
/// The popup an owner surface currently has open. A window owns one; a popup owns one, which is
/// what makes a popup opened inside a popup a child of it rather than a sibling.
/// </summary>
internal sealed class PopupSurface
{
    private PopupSurface(PopupSession? parent) => Parent = parent;

    /// <summary>The popup this surface belongs to, or null for a window's surface.</summary>
    public PopupSession? Parent { get; }

    /// <summary>The popup currently open on this surface, or null.</summary>
    public PopupSession? Current { get; set; }

    public static PopupSurface ForWindow() => new(null);

    public static PopupSurface ForSession(PopupSession owner) => new(owner);

    /// <summary>Completes the popup on this surface with <see cref="PopupCloseReason.Cancelled"/>.</summary>
    public void CancelCurrent()
    {
        var current = Current;
        Current = null;
        current?.Cancel();
    }
}

/// <summary>
/// One shown popup: the <see cref="Popup"/>, the result it will report, and the completion the
/// caller awaits.
/// </summary>
/// <remarks>
/// <para>
/// The result is written once and every later close is a no-op. That is what keeps the five
/// dismissal paths - application code closing it, a press outside, Escape, the owner going away,
/// and a replacement - from completing the same task twice.
/// </para>
/// <para>
/// The completion is created with
/// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/>, so application code never runs
/// inside <see cref="Popup.Close"/>: the continuation is queued, and the
/// <see cref="SynchronizationContext"/> the run loop installs puts it back on the UI thread.
/// </para>
/// </remarks>
internal sealed class PopupSession
{
    private readonly Control _content;
    private readonly PopupChannel _channel;
    private readonly PopupSurface _surface;
    private readonly PopupOptions _options;
    private readonly Control? _anchor;
    private readonly Window? _window;
    private readonly PopupPlacement _placement;
    private readonly TaskCompletionSource<PopupResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Popup? _popup;
    private PopupResult? _result;
    private IDisposable? _popupToken;
    private IDisposable? _windowToken;
    private IDisposable? _displayedToken;
    private Action? _ownerClosed;

    public PopupSession(
        Control content,
        PopupChannel channel,
        PopupSurface surface,
        PopupOptions options,
        Control? anchor,
        Window? window,
        PopupPlacement placement)
    {
        _content = content;
        _channel = channel;
        _options = options;
        _anchor = anchor;
        _window = window;
        _placement = placement;
        _surface = surface;

        Surface = PopupSurface.ForSession(this);
    }

    /// <summary>The surface this popup owns, which is where a popup opened inside it belongs.</summary>
    public PopupSurface Surface { get; }

    /// <summary>Completes with the result the popup closed with.</summary>
    public Task<PopupResult> Completion => _completion.Task;

    /// <summary>Raised once this popup has closed, so a popup it owns closes with it.</summary>
    public event Action? Closed;

    /// <summary>Creates the popup, works out where it goes, hooks its dismissal paths and shows it.</summary>
    public void Show()
    {
        var popup = CreatePopup();
        var location = PopupGeometry.Resolve(popup, _options, _placement, _anchor, popup.Host);

        _popup = popup;
        _popupToken = popup.AddClosedHandler(OnPopupClosed);

        // A popup that was itself opened from inside a popup belongs to that popup, not to the
        // window, so the level above closing takes it down as well.
        if (_surface.Parent is { } owner)
        {
            _ownerClosed = () => Complete(PopupResult.Dismissed(PopupCloseReason.OwnerClosed));
            owner.Closed += _ownerClosed;
        }

        if (_window is { } window)
        {
            _windowToken = window.AddClosedHandler(OnWindowClosed);
        }

        if (_anchor is { } anchorControl)
        {
            _displayedToken = anchorControl.AddDisplayedChangedHandler(OnAnchorDisplayedChanged);
        }

        popup.Show(location);
    }

    /// <summary>
    /// Builds the popup and gives it everything the options ask for except the location, which is
    /// worked out once the popup can measure itself.
    /// </summary>
    private Popup CreatePopup()
    {
        var popup = _anchor is { } anchor ? new Popup(anchor) : new Popup(_window!.Host);

        popup.Content = _content;
        popup.DismissOnOutsideClick = _options.DismissOnOutsideClick;

        if (_options.Size is { } size)
        {
            popup.Size = size;
        }

        if (_options.MaximumSize is { } maximum)
        {
            popup.MaximumSize = maximum;
        }

        // The popup is drawn at the zoom of the subtree it was opened from, so it reads as part of
        // that subtree rather than as chrome at the window's scale. Placement then works with the
        // size that results, which is the room the popup really takes on the host.
        popup.ScaleFactor = _anchor?.EffectiveScaleMultiplier ?? 1f;

        return popup;
    }

    /// <summary>Completes with <see cref="PopupCloseReason.Cancelled"/> and closes the popup.</summary>
    public void Cancel() => Complete(PopupResult.Dismissed(PopupCloseReason.Cancelled));

    /// <summary>
    /// Writes the result, detaches every dismissal path, closes the popup and hands the result to
    /// the caller. The first call wins; every later one does nothing.
    /// </summary>
    public void Complete(PopupResult result)
    {
        if (_result is not null)
        {
            return;
        }

        _result = result;

        // The associations are cleared before anything is raised, so a handler or a continuation
        // can show the same control in a new popup from inside this close.
        if (ReferenceEquals(_channel.Session, this))
        {
            _channel.Session = null;
        }

        if (ReferenceEquals(Surface.Current, this))
        {
            Surface.Current = null;
        }

        Detach();

        _completion.TrySetResult(result);

        // Anything opened from inside this popup goes with it, before the application hears about
        // this one, so the levels come down outermost-last.
        Closed?.Invoke();

        _channel.Invoke(_content, result);
    }

    /// <summary>
    /// Undoes a session whose popup failed to show, without reporting a result for a call the caller
    /// never received.
    /// </summary>
    public void Abandon()
    {
        if (_result is not null)
        {
            return;
        }

        if (ReferenceEquals(_channel.Session, this))
        {
            _channel.Session = null;
        }

        if (ReferenceEquals(Surface.Current, this))
        {
            Surface.Current = null;
        }

        Detach();
    }

    /// <summary>Releases every hook the session holds and disposes the popup.</summary>
    private void Detach()
    {
        _popupToken?.Dispose();
        _popupToken = null;

        _windowToken?.Dispose();
        _windowToken = null;

        _displayedToken?.Dispose();
        _displayedToken = null;

        if (_ownerClosed is { } ownerClosed && _surface.Parent is { } owner)
        {
            owner.Closed -= ownerClosed;
        }

        _ownerClosed = null;

        var popup = _popup;
        _popup = null;
        popup?.Dispose();
    }

    /// <summary>
    /// The popup closed itself: a press outside it, or a platform reporting it lost the user's
    /// attention. Every route here ends in <see cref="Popup.Close"/>, which does not say why, so
    /// this is the one classification that cannot come from the caller. A platform that dismisses
    /// the popup because its owner went away reports the same thing as a press, so the owner is
    /// checked first.
    /// </summary>
    private void OnPopupClosed(object sender) =>
        Complete(PopupResult.Dismissed(OwnerIsGone() ? PopupCloseReason.OwnerClosed : PopupCloseReason.OutsideClick));

    private void OnWindowClosed(object sender) =>
        Complete(PopupResult.Dismissed(PopupCloseReason.OwnerClosed));

    private void OnAnchorDisplayedChanged(object sender)
    {
        if (_anchor is { } anchor && !anchor.IsDisplayed)
        {
            Complete(PopupResult.Dismissed(PopupCloseReason.OwnerClosed));
        }
    }

    /// <summary>Whether the window, or the control the popup hangs from, has already gone.</summary>
    private bool OwnerIsGone() =>
        _window?.IsClosed == true || (_anchor is { } anchor && anchor.Host is null);
}
