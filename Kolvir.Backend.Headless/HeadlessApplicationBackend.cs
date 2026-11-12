using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Kolvir.Backend.Headless;

/// <summary>
/// The platform half for a machine with no display: renders, tests and CI. It registers a scripted
/// dialog service instead of a native one, keeps no clipboard, and has no window chrome, so a
/// render is byte-for-byte reproducible regardless of how the host machine is configured.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Run"/> does not enter a loop. It drains queued work and returns the exit code, which
/// is what makes a headless run a straight line a test can step through.
/// </para>
/// <para>
/// No <see cref="ISystemTheme"/> is registered on purpose. The application model only follows
/// the desktop's appearance when a backend supplies one, so leaving it out is what keeps a render
/// independent of the host's light/dark setting.
/// </para>
/// </remarks>
public sealed class HeadlessApplicationBackend : IApplicationBackend
{
    private Action<object>? _activated;
    private Action<object, SecondInstanceEventArgs>? _secondInstance;

    // Copied out and swapped rather than drained in place. The drain runs handlers outside the
    // lock, so a handler that posts more work appends to the queue the next pass takes, and a
    // timer thread is never blocked behind a handler that takes a while.
    private readonly object _pendingGate = new();
    private Queue<Action> _pending = new();
    private Application? _application;
    private int _exitCode;

    /// <summary>The size every window reports before it is given one, and the size of the synthetic screen.</summary>
    public static SKSize DefaultScreenSize { get; } = new(1920f, 1080f);

    /// <summary>The most recently created window backend, for a harness that needs to paint it.</summary>
    public HeadlessWindowBackend? LastWindow { get; private set; }

    /// <inheritdoc />
    public void Initialize(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        _application = application;
    }

    /// <inheritdoc />
    /// <remarks>Drains queued work once and returns; there is no loop to enter.</remarks>
    public int Run()
    {
        DrainPending();
        return _exitCode;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Always succeeds: a render or a test run has no desktop to be a second instance of, and
    /// taking a process-wide claim would make two concurrent renders of the same application
    /// collide.
    /// </remarks>
    public bool TryClaimSingleInstance() => true;

    /// <inheritdoc />
    public IDisposable AddActivatedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _activated += handler;
        return new HandlerToken<Action<object>>(RemoveActivatedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddActivatedHandler"/> added.</summary>
    public void RemoveActivatedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _activated -= handler;
    }

    /// <inheritdoc />
    public IDisposable AddSecondInstanceHandler(Action<object, SecondInstanceEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _secondInstance += handler;
        return new HandlerToken<Action<object, SecondInstanceEventArgs>>(RemoveSecondInstanceHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddSecondInstanceHandler"/> added.</summary>
    public void RemoveSecondInstanceHandler(Action<object, SecondInstanceEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _secondInstance -= handler;
    }

    /// <inheritdoc />
    public void Quit(int exitCode) => _exitCode = exitCode;

    /// <inheritdoc />
    public void Invoke(Action action, bool synchronous)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (synchronous)
        {
            action();
            return;
        }

        lock (_pendingGate)
        {
            _pending.Enqueue(action);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A headless render paints once and exits, so the portable wait is all a timer can need here.
    /// </remarks>
    public ITimerBackend CreateTimer(Action tick) => new ThreadPoolTimerBackend(tick);

    /// <inheritdoc />
    public IWindowBackend CreateWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        LastWindow = new HeadlessWindowBackend(window, GetScreens()[0]);

        // There is no native menu on a headless backend, so menus are drawn by the portable
        // presenter - which also means a render can show one, and a test can drive it.
        ContextMenuPresenter.Current = new MenuPresenter();
        return LastWindow;
    }

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<HeadlessDialogService>();
        services.AddSingleton<IDialogService>(provider => provider.GetRequiredService<HeadlessDialogService>());
    }

    /// <inheritdoc />
    public IReadOnlyList<Screen> GetScreens() =>
        [new Screen(new SKRect(0f, 0f, DefaultScreenSize.Width, DefaultScreenSize.Height),
            new SKRect(0f, 0f, DefaultScreenSize.Width, DefaultScreenSize.Height), 1f, isPrimary: true)];

    /// <inheritdoc />
    public void RunApplicationHooks(ApplicationPhase phase, Application application)
    {
        // Empty on purpose: this backend declares no typed application hooks yet.
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        lock (_pendingGate)
        {
            _pending.Clear();
        }
    }

    /// <summary>Runs the work queued by <see cref="Invoke"/> with <c>synchronous: false</c>.</summary>
    /// <remarks>
    /// <para>
    /// The queue has one consumer - this method, on the render thread - and several producers:
    /// every timer tick posts from a pool thread, and a handler that posts more work posts from this
    /// one. A bare <see cref="Queue{T}"/> cannot take that, and its failure is not a clean
    /// exception: two concurrent enqueues can leave the count ahead of the slots, so a later
    /// <c>TryDequeue</c> returns <c>true</c> with a null item and the drain calls it - which is the
    /// <see cref="NullReferenceException"/> that used to abort a headless render of an animated
    /// page.
    /// </para>
    /// <para>
    /// One batch is taken under the lock and run outside it. Holding the lock for the whole drain
    /// would work, but it would block every timer thread behind the slowest handler, and a handler
    /// that waits for a timer would deadlock. The swap instead puts work posted during a batch at
    /// the end of the next one. Order is kept both ways: a batch runs in the order it was enqueued,
    /// and work posted during it runs after that batch, never before. A <c>ConcurrentQueue</c> would
    /// also be safe, but it buys nothing for one consumer and hides the batch boundary this method
    /// actually relies on.
    /// </para>
    /// </remarks>
    public void DrainPending()
    {
        while (true)
        {
            Queue<Action> batch;

            lock (_pendingGate)
            {
                if (_pending.Count == 0)
                {
                    return;
                }

                batch = _pending;
                _pending = new Queue<Action>();
            }

            foreach (var action in batch)
            {
                action();
            }
        }
    }

    /// <summary>
    /// Runs the handlers <see cref="AddActivatedHandler"/> added, the way a desktop does, so a test
    /// can drive the re-activation path without a display.
    /// </summary>
    public void RaiseActivated() => _activated?.Invoke(this);

    /// <summary>
    /// Runs the handlers <see cref="AddSecondInstanceHandler"/> added, the way a desktop does, so a
    /// test can drive the second-launch path without a display.
    /// </summary>
    /// <param name="arguments">The arguments the forwarded launch would have carried.</param>
    public void RaiseSecondInstance(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        _secondInstance?.Invoke(this, new SecondInstanceEventArgs(arguments));
    }

    /// <summary>The application this backend drives, or null before <see cref="Initialize"/>.</summary>
    public Application? Application => _application;
}
