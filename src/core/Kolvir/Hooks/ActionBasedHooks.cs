namespace Kolvir;

/// <summary>
/// Adapts a single callback to <see cref="IApplicationHook"/>, so a feature whose setup is one
/// line does not need a class of its own.
/// </summary>
internal sealed class ActionBasedAppHook : IApplicationHook
{
    private readonly ApplicationPhase _phase;
    private readonly Action<Application> _hookFunc;

    public ActionBasedAppHook(ApplicationPhase phase, Action<Application> hookFunc)
    {
        _phase = phase;
        _hookFunc = hookFunc;
    }

    public void OnPhase(ApplicationPhase phase, Application application)
    {
        if (phase == _phase)
        {
            _hookFunc(application);
        }
    }
}

/// <summary>
/// Adapts one callback per window phase to <see cref="IWindowHook"/>, so a feature that only
/// cares about one or two points writes a lambda per point instead of a class.
/// </summary>
internal sealed class ActionBasedWindowHook : IWindowHook
{
    private readonly Action<Window>? _created;
    private readonly Action<Window>? _showing;
    private readonly Action<Window>? _shown;
    private readonly Action<Window>? _hiding;
    private readonly Action<Window>? _hidden;
    private readonly Action<Window>? _closing;
    private readonly Action<Window>? _closed;
    private readonly Action<Window>? _disposed;

    public ActionBasedWindowHook(
        Action<Window>? created,
        Action<Window>? showing,
        Action<Window>? shown,
        Action<Window>? hiding,
        Action<Window>? hidden,
        Action<Window>? closing,
        Action<Window>? closed,
        Action<Window>? disposed)
    {
        _created = created;
        _showing = showing;
        _shown = shown;
        _hiding = hiding;
        _hidden = hidden;
        _closing = closing;
        _closed = closed;
        _disposed = disposed;
    }

    public void OnPhase(WindowPhase phase, Window window)
    {
        var callback = phase switch
        {
            WindowPhase.Created => _created,
            WindowPhase.Showing => _showing,
            WindowPhase.Shown => _shown,
            WindowPhase.Hiding => _hiding,
            WindowPhase.Hidden => _hidden,
            WindowPhase.Closing => _closing,
            WindowPhase.Closed => _closed,
            WindowPhase.Disposed => _disposed,
            _ => null,
        };

        callback?.Invoke(window);
    }
}
