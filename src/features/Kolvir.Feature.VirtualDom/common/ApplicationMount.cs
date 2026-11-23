using System.ComponentModel;

namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// One application component's rendered window set, kept in step with the real windows it owns.
/// </summary>
/// <remarks>
/// <para>
/// The application-level counterpart of <see cref="ComponentMount"/>. It owns the windows the
/// component's render describes: disposing it closes them, and every later invalidate is a no-op.
/// The first render happens here, synchronously, because an application with no window would have no
/// host drawing a frame - and therefore no frame in which the first windows could be created.
/// </para>
/// <para>
/// Later renders happen at the start of the next frame, through
/// <see cref="ApplicationRenderQueue"/>, so a model change that arrives several times before that
/// frame still creates, patches and closes windows once. Windows are matched by key first and then by
/// position, which is what lets a reorder move the OS windows instead of replacing them.
/// </para>
/// <para>
/// A native close is always refused by the mount and re-raised as
/// <see cref="VWindow.CloseRequested"/>; the window disappears when a later render omits it, or when
/// the application itself asks to exit. Everything here is UI-thread work, like the rest of the
/// library.
/// </para>
/// </remarks>
public sealed class ApplicationMount : IDisposable
{
    private readonly Application _application;
    private readonly IApplicationComponent _component;
    private readonly List<MountedWindow> _windows = [];
    private bool _pending;
    private bool _rendering;
    private bool _disposed;

    /// <summary>Mounts <paramref name="component"/>'s first window set.</summary>
    /// <param name="application">The application the windows belong to.</param>
    /// <param name="component">The component to render and keep in step.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="application"/> or <paramref name="component"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="application"/> is not the current application, or the component's first render
    /// is not a list of <see cref="VWindow"/> nodes.
    /// </exception>
    internal ApplicationMount(Application application, IApplicationComponent component)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(component);

        if (!ReferenceEquals(Application.Current, application))
        {
            throw new InvalidOperationException(
                "The application passed to Mount is not the current Application. Windows are created for Application.Current, so mount the component on the application that owns this process.");
        }

        _application = application;
        _component = component;
        component.Attach(this);

        try
        {
            Reconcile(RenderComponent());
        }
        catch
        {
            // A mount that never happened must not leave the component pointing at a half-built
            // window set: every later Invalidate, and any attempt to mount it again, would reach a
            // mount that no longer exists. The attach has to precede the render so a render that
            // changes state is caught by name, so it is undone here rather than moved.
            component.Detach(this);
            CloseAllWindows();
            throw;
        }
    }

    /// <summary>Asks for this component's windows to be rendered on the next frame.</summary>
    /// <remarks>
    /// <para>
    /// The primitive <see cref="ApplicationComponent"/> is built on. An
    /// <see cref="IApplicationComponent"/> of one's own calls it from its own state setter, which is
    /// why it is public: an interface whose only way to be re-rendered is a method the mount never
    /// calls would be a seam nothing can use.
    /// </para>
    /// <para>
    /// The whole of it is cheap on purpose: a flag, a queue entry and a frame request. Calling it
    /// twice for one frame queues the mount once, which is what turns a handler that changes a list
    /// five times into one render and one window update.
    /// </para>
    /// <para>
    /// Calling it from inside a render is refused rather than folded into the pass: a render
    /// describes what the state is, so a render that changes the state describes a window set that is
    /// already out of date, and there is no fixed point to iterate to. The message names the
    /// component.
    /// </para>
    /// <para>
    /// With no windows there is no host to request a frame from. A component whose first render
    /// yields no windows therefore cannot schedule the render that would create one, which is why the
    /// first render has to be the one that supplies any window the application needs.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The component changed state from inside its own <see cref="IApplicationComponent.Render"/>.
    /// </exception>
    public void Invalidate()
    {
        if (_disposed)
        {
            return;
        }

        if (_rendering)
        {
            throw new InvalidOperationException(
                $"{_component.GetType().Name} changed state from inside Render. A render describes the state it is given, so it must not call Invalidate; do it from the event handler that changed the state instead.");
        }

        if (_pending)
        {
            // Already queued for the next frame, and the frame has not run yet: the render that is
            // coming will read the state as it is by then.
            return;
        }

        _pending = true;
        ApplicationRenderQueue.Enqueue(this);

        // Ask for the frame the render is waiting for. Every window's host is asked because any of
        // them may be the one that paints first; with no windows there is nothing to ask.
        foreach (var record in _windows)
        {
            if (!record.Window.IsClosed)
            {
                record.Window.Host.Invalidate();
            }
        }
    }

    /// <summary>Detaches the component and closes every window this mount owns. Idempotent.</summary>
    /// <remarks>
    /// Closing is the framework's own close, so no <see cref="VWindow.CloseRequested"/> handler runs.
    /// A queued render that has not happened yet is forgotten rather than left to touch a mount that
    /// no longer owns anything.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ApplicationRenderQueue.Forget(this);
        _component.Detach(this);
        CloseAllWindows();
    }

    /// <summary>Renders the component now, because the frame it was queued for has started.</summary>
    internal void RenderQueued()
    {
        if (_disposed || !_pending)
        {
            return;
        }

        _pending = false;
        Reconcile(RenderComponent());
    }

    private IReadOnlyList<ApplicationVNode> RenderComponent()
    {
        _rendering = true;

        try
        {
            return _component.Render();
        }
        finally
        {
            _rendering = false;
        }
    }

    private void Reconcile(IReadOnlyList<ApplicationVNode> nodes)
    {
        var windows = Validate(nodes);
        var previous = _windows.ToArray();
        var (matches, used) = Match(previous, windows);

        for (int i = 0; i < previous.Length; i++)
        {
            if (!used[i])
            {
                Close(previous[i]);
            }
        }

        if (_disposed || _application.IsExiting)
        {
            // Closing an omitted window can ask the application to exit - the main window, or the
            // last one - and an exit closes the rest. There is nothing left to create in that case.
            _windows.Clear();
            return;
        }

        var updated = new List<MountedWindow>(windows.Count);
        var created = new List<MountedWindow>();

        try
        {
            for (int i = 0; i < windows.Count; i++)
            {
                var node = windows[i];
                var match = matches[i];

                if (match is not null && !match.Window.IsClosed)
                {
                    BeginGeometrySuppression(match);

                    try
                    {
                        node.PatchFrom(match.Node, match.Window, match.Pristine);
                        match.Node = node;
                        match.UpdateContent(node.Content);
                    }
                    finally
                    {
                        EndGeometrySuppression(match);
                    }

                    updated.Add(match);
                    continue;
                }

                if (match is not null)
                {
                    // The window was closed outside the render - by the application tearing down, or
                    // by its own close path - so its content is already released and a new window
                    // replaces the record in the same slot.
                    match.DisposeContent();
                    match.DisposeNotifications();
                }

                var record = Create(node);
                created.Add(record);
                updated.Add(record);
            }
        }
        catch
        {
            // A window made earlier in this render has not reached the window list yet, so the
            // mount's ordinary cleanup would not see it. Close what this pass created without
            // touching the windows that were already on screen.
            foreach (var record in created)
            {
                record.DisposeContent();
                record.DisposeNotifications();
                record.ClosingFromFramework = true;

                try
                {
                    record.Window.Close();
                }
                finally
                {
                    record.ClosingFromFramework = false;
                }
            }

            throw;
        }

        _windows.Clear();
        _windows.AddRange(updated);
    }

    private MountedWindow Create(VWindow node)
    {
        var window = new Window();

        // The pristine values have to be read before any node property is applied, and they have to
        // come from this window rather than a shared instance: a window's icon and menu bar start
        // from the application's options, and its size and location come from the backend.
        var pristine = new WindowPristine(
            window.Title,
            window.ClientSize,
            window.Location,
            window.State,
            window.Resizable,
            window.TopMost,
            window.Icon,
            window.MenuBar,
            window.MinimumClientSize,
            window.MaximumClientSize);

        var host = new Panel { Dock = DockStyle.Fill };
        window.Content = host;

        var record = new MountedWindow(node, window, host, pristine);

        window.AddClosingHandler((_, args) => OnWindowClosing(record, args));
        window.AddClosedHandler(_ => OnWindowClosed(record));
        AttachNotifications(record);

        BeginGeometrySuppression(record);

        try
        {
            record.UpdateContent(node.Content);
            node.ApplyTo(window);
            window.Show();
        }
        catch
        {
            // The record has not reached the window list yet, so a failure while its content or
            // properties are being applied has to release the window it already created.
            record.DisposeContent();
            record.DisposeNotifications();
            record.ClosingFromFramework = true;

            try
            {
                window.Close();
            }
            finally
            {
                record.ClosingFromFramework = false;
            }

            throw;
        }
        finally
        {
            EndGeometrySuppression(record);
        }

        return record;
    }

    private void Close(MountedWindow record)
    {
        record.DisposeContent();
        record.DisposeNotifications();
        record.ClosingFromFramework = true;

        try
        {
            record.Window.Close();
        }
        finally
        {
            record.ClosingFromFramework = false;
        }

        // The closed handler normally removes it first; this is what keeps the list honest when the
        // window was already closed and the handler did not run.
        _windows.Remove(record);
    }

    private void CloseAllWindows()
    {
        var windows = _windows.ToArray();
        _windows.Clear();

        foreach (var record in windows)
        {
            record.DisposeContent();
            record.DisposeNotifications();
            record.ClosingFromFramework = true;

            try
            {
                record.Window.Close();
            }
            finally
            {
                record.ClosingFromFramework = false;
            }
        }
    }

    private void OnWindowClosing(MountedWindow record, CancelEventArgs args)
    {
        if (_disposed || record.ClosingFromFramework || _application.IsExiting)
        {
            return;
        }

        // A native close is never allowed to finish on its own: the model decides whether the window
        // is gone, and it does that by omitting the node from a later render. No handler means the
        // window stays exactly where it is.
        args.Cancel = true;
        record.Node.CloseRequested?.Invoke(record.Node);
    }

    private void OnWindowClosed(MountedWindow record)
    {
        record.DisposeContent();
        record.DisposeNotifications();
        _windows.Remove(record);
    }

    /// <summary>
    /// Routes the window's own notifications to the handlers on the most recently rendered node.
    /// Subscribing happens before the first property is applied so a window's creation and its
    /// show-time state are covered by the suppression below.
    /// </summary>
    private void AttachNotifications(MountedWindow record)
    {
        var window = record.Window;

        record.AddNotificationToken(window.AddLocationChangedHandler(_ => OnWindowMoved(record)));
        record.AddNotificationToken(window.AddClientSizeChangedHandler(_ => OnWindowResized(record)));
        record.AddNotificationToken(window.AddStateChangedHandler(_ => OnWindowStateChanged(record)));
        record.AddNotificationToken(window.AddActivatedHandler(_ => OnWindowActivated(record)));
        record.AddNotificationToken(window.AddDeactivatedHandler(_ => OnWindowDeactivated(record)));
    }

    /// <summary>
    /// Starts dropping the geometry notifications the mount's own property application provokes.
    /// The result of that application is an implementation detail of the render - reading it back
    /// into the model would echo the render into itself - so it must not surface through the node's
    /// events. Activation is not geometry and is left alone.
    /// </summary>
    private static void BeginGeometrySuppression(MountedWindow record) =>
        record.SuppressGeometryNotifications = true;

    /// <summary>
    /// Ends the suppression started by <see cref="BeginGeometrySuppression"/>. A size assigned to a
    /// shown window during a paint is applied on a later loop turn, so when the turn that ends the
    /// suppression is still inside a paint the suppression stays until a turn lands outside one.
    /// </summary>
    private void EndGeometrySuppression(MountedWindow record)
    {
        if (UiHost.IsPainting)
        {
            _application.BeginInvoke(() => ClearGeometrySuppression(record));
        }
        else
        {
            record.SuppressGeometryNotifications = false;
        }
    }

    private void ClearGeometrySuppression(MountedWindow record)
    {
        if (UiHost.IsPainting)
        {
            _application.BeginInvoke(() => ClearGeometrySuppression(record));
            return;
        }

        record.SuppressGeometryNotifications = false;
    }

    private void OnWindowMoved(MountedWindow record)
    {
        if (_disposed || record.Window.IsClosed || record.SuppressGeometryNotifications)
        {
            return;
        }

        var node = record.Node;
        node.Moved?.Invoke(node, record.Window.Location);
    }

    private void OnWindowResized(MountedWindow record)
    {
        if (_disposed || record.Window.IsClosed || record.SuppressGeometryNotifications)
        {
            return;
        }

        var node = record.Node;
        node.Resized?.Invoke(node, record.Window.ClientSize);
    }

    private void OnWindowStateChanged(MountedWindow record)
    {
        if (_disposed || record.Window.IsClosed || record.SuppressGeometryNotifications)
        {
            return;
        }

        var node = record.Node;
        node.StateChanged?.Invoke(node, record.Window.State);
    }

    private void OnWindowActivated(MountedWindow record)
    {
        if (_disposed || record.Window.IsClosed)
        {
            return;
        }

        var node = record.Node;
        node.Activated?.Invoke(node);
    }

    private void OnWindowDeactivated(MountedWindow record)
    {
        if (_disposed || record.Window.IsClosed)
        {
            return;
        }

        var node = record.Node;
        node.Deactivated?.Invoke(node);
    }

    private IReadOnlyList<VWindow> Validate(IReadOnlyList<ApplicationVNode> nodes)
    {
        if (nodes is null)
        {
            throw new InvalidOperationException(
                $"{_component.GetType().Name} returned null from Render. Return an empty list to describe an application with no windows.");
        }

        var windows = new List<VWindow>(nodes.Count);
        Dictionary<object, int>? keys = null;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is not VWindow window)
            {
                var type = nodes[i]?.GetType().Name ?? "null";

                throw new InvalidOperationException(
                    $"{_component.GetType().Name} returned a {type} at the application level, and only VWindow is supported there. Application-level nodes other than windows are not supported yet; return VWindow nodes from Render.");
            }

            if (window.Key is { } key)
            {
                keys ??= new Dictionary<object, int>();

                if (!keys.TryAdd(key, i))
                {
                    throw new InvalidOperationException(
                        $"{_component.GetType().Name} returned two windows with the same Key ({key}). A key identifies a window, so it has to be unique among the windows one render returns.");
                }
            }

            windows.Add(window);
        }

        return windows;
    }

    /// <summary>
    /// Pairs each new window with the previous one it should reuse, keyed first and then by position.
    /// </summary>
    /// <remarks>
    /// The same rule the control reconciler applies to children: keyed windows are matched by key,
    /// which is what makes a reorder move windows instead of rebuilding them, and unkeyed windows are
    /// then paired with unkeyed windows in order. The two never mix, so swapping one key for another
    /// creates and closes rather than silently renaming a window.
    /// </remarks>
    private static (MountedWindow?[] Matches, bool[] Used) Match(
        IReadOnlyList<MountedWindow> previous,
        IReadOnlyList<VWindow> next)
    {
        var matches = new MountedWindow?[next.Count];
        var used = new bool[previous.Count];

        Dictionary<object, int>? byKey = null;

        for (int i = 0; i < previous.Count; i++)
        {
            if (previous[i].Node.Key is { } key)
            {
                (byKey ??= new Dictionary<object, int>()).TryAdd(key, i);
            }
        }

        if (byKey is not null)
        {
            for (int i = 0; i < next.Count; i++)
            {
                if (next[i].Key is { } key && byKey.TryGetValue(key, out int index) && !used[index])
                {
                    matches[i] = previous[index];
                    used[index] = true;
                }
            }
        }

        int cursor = 0;

        for (int i = 0; i < next.Count; i++)
        {
            if (matches[i] is not null || next[i].Key is not null)
            {
                continue;
            }

            while (cursor < previous.Count && (used[cursor] || previous[cursor].Node.Key is not null))
            {
                cursor++;
            }

            if (cursor < previous.Count)
            {
                matches[i] = previous[cursor];
                used[cursor] = true;
            }
        }

        return (matches, used);
    }
}
