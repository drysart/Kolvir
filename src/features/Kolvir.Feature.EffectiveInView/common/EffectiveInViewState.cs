using System.Runtime.CompilerServices;

namespace Kolvir.Feature.InView;

/// <summary>
/// The cached answer and the change handlers a control carries once the feature is in use.
/// </summary>
/// <remarks>
/// <para>
/// The state sits beside the control rather than in it, because the feature is an opt-in and the
/// base class must not carry its fields. Two tables key on the control: <c>States</c> holds every
/// control that has been read and is never enumerated, and <c>Watched</c> holds only the controls
/// with at least one handler, which is what a completed pass walks. Both keys are weak, so a
/// control's state goes away with the control.
/// </para>
/// <para>
/// A read computes from the layout that has already happened and caches until the next pass; it
/// never starts a pass and never fires a handler. A control with a handler is recomputed by the
/// pass itself, before any handler runs, so a handler that reads the property sees the value the
/// event is about.
/// </para>
/// <para>
/// Everything here is single-threaded, like the rest of the library: the property is a UI-thread
/// question and the docs say so.
/// </para>
/// </remarks>
internal sealed class EffectiveInViewState
{
    /// <summary>Every control that has been read, so its answer is cached.</summary>
    private static readonly ConditionalWeakTable<Control, EffectiveInViewState> States = new();

    /// <summary>Only the controls with a handler; the pass enumerates this one.</summary>
    private static readonly ConditionalWeakTable<Control, EffectiveInViewState> Watched = new();

    /// <summary>One invalidation subscription per host, so a repaint request drops every cached answer.</summary>
    private static readonly ConditionalWeakTable<UiHost, InvalidationWatch> Hosts = new();

    private static IDisposable? _layoutSubscription;
    private static bool _dispatching;
    private static bool _hasWatchers;
    private static long _geometryEpoch;
    private static int _computeCount;

    private bool _hasValue;
    private bool _value;
    private long _epoch;
    private bool _hasBaseline;
    private bool _reported;
    private Action<object>? _changed;

    private EffectiveInViewState(Control control) =>
        control.AddBoundsChangedHandler(OnBoundsChanged);

    /// <summary>Reads, computing and caching when nothing has changed since the last answer.</summary>
    /// <param name="control">The control being asked about.</param>
    /// <returns>True when some part of the control is genuinely on screen.</returns>
    public static bool Read(Control control)
    {
        // The two gates that cost nothing and are exact. A hidden control is not in view whatever the
        // last pass said, and a control with no host has no geometry to reason about.
        var host = control.Host;

        if (host is null || !control.EffectiveVisible)
        {
            return false;
        }

        var state = States.GetValue(control, static owner => new EffectiveInViewState(owner));
        long epoch = _geometryEpoch;

        // A read has to hear about the next pass, or a scroll would leave the cached answer stale.
        // The subscriptions only increment a counter until something is watched, so they cost one
        // call per pass and per repaint, and no allocation.
        EnsureSubscribed();
        EnsureHostWatch(host);

        if (state._hasValue && state._epoch == epoch)
        {
            return state._value;
        }

        state._value = Compute(control, host);
        state._hasValue = true;
        state._epoch = epoch;

        return state._value;
    }

    /// <summary>Adds a handler and makes the control one the pass keeps up to date.</summary>
    /// <param name="control">The control to watch.</param>
    /// <param name="handler">The handler to add.</param>
    /// <returns>A token that removes the handler when disposed.</returns>
    public static IDisposable AddHandler(Control control, Action<object> handler)
    {
        var state = States.GetValue(control, static owner => new EffectiveInViewState(owner));

        state._changed += handler;

        if (!Watched.TryGetValue(control, out _))
        {
            Watched.Add(control, state);
        }

        _hasWatchers = true;
        EnsureSubscribed();

        if (control.Host is { } host)
        {
            EnsureHostWatch(host);
        }

        return new HandlerToken<Action<object>>(state.RemoveChanged, handler);
    }

    /// <summary>Removes a handler, and stops watching the control when it was the last one.</summary>
    /// <param name="control">The control to stop watching.</param>
    /// <param name="handler">The handler to remove.</param>
    public static void RemoveHandler(Control control, Action<object> handler)
    {
        if (!States.TryGetValue(control, out var state))
        {
            return;
        }

        state._changed -= handler;

        if (state._changed is null)
        {
            // With nobody listening there is no baseline to compare against: the next registration
            // observes afresh rather than reporting everything that happened in the gap.
            state._hasBaseline = false;
            Watched.Remove(control);
            _hasWatchers = !IsWatchedEmpty();
        }
    }

    /// <summary>Drops every cached answer and every subscription. For the test suite only.</summary>
    internal static void ResetForTests()
    {
        Unsubscribe();
        States.Clear();
        Watched.Clear();
        Hosts.Clear();
        _hasWatchers = false;
        _geometryEpoch = 0;
        _computeCount = 0;
        _dispatching = false;
    }

    /// <summary>How many times a value has been computed; the cache's observable.</summary>
    internal static int ComputeCount => _computeCount;

    private static bool Compute(Control control, UiHost host)
    {
        _computeCount++;

        var snapshot = InViewSnapshot.Build(host, [control]);
        return snapshot.Evaluate(control);
    }

    private static void OnBoundsChanged(object sender)
    {
        if (States.TryGetValue((Control)sender, out var state))
        {
            state._hasValue = false;
        }
    }

    private static void EnsureSubscribed() =>
        _layoutSubscription ??= UiHost.AddLayoutCompletedHandler(static host => Dispatch(host));

    private static void EnsureHostWatch(UiHost host) =>
        Hosts.GetValue(host, static owner => new InvalidationWatch(owner));

    private static void Unsubscribe()
    {
        _layoutSubscription?.Dispose();
        _layoutSubscription = null;
    }

    private static bool IsWatchedEmpty()
    {
        foreach (var _ in Watched)
        {
            return false;
        }

        return true;
    }

    /// <summary>Recomputes the watched controls of one host and fires the handlers that changed.</summary>
    /// <param name="host">The host whose tree the pass just settled.</param>
    private static void Dispatch(UiHost host)
    {
        // Every completed pass invalidates the cached answers, even one that arrives while handlers are
        // running: the geometry they read has changed, and a lazy read after this must not reuse the
        // old value. The host's own invalidations do the same, which is how a tree that changed without
        // a pass - a child added, a control hidden - is noticed.
        EnsureHostWatch(host);
        _geometryEpoch++;

        if (!_hasWatchers || _dispatching)
        {
            // Nothing is listening, or a handler changed something and laid it out while the outer
            // dispatch is still running. Either way this pass only invalidates: the next one reports.
            return;
        }

        _dispatching = true;

        try
        {
            var states = new List<(Control Control, EffectiveInViewState State)>();
            var detached = new List<(Control Control, EffectiveInViewState State)>();
            var stale = new List<(Control Control, EffectiveInViewState State)>();

            foreach (var pair in Watched)
            {
                if (pair.Value._changed is null)
                {
                    stale.Add((pair.Key, pair.Value));
                    continue;
                }

                if (pair.Key.Host is null)
                {
                    // It left every tree since the last pass and was on screen then: that is a change,
                    // and this is the pass that reports it.
                    if (pair.Value._hasBaseline && pair.Value._reported)
                    {
                        detached.Add((pair.Key, pair.Value));
                    }

                    continue;
                }

                if (ReferenceEquals(pair.Key.Host, host))
                {
                    states.Add((pair.Key, pair.Value));
                }
            }

            foreach (var (control, state) in stale)
            {
                state._hasBaseline = false;
                Watched.Remove(control);
            }

            if (states.Count == 0 && detached.Count == 0)
            {
                _hasWatchers = !IsWatchedEmpty();
                return;
            }

            var targets = new List<Control>(states.Count);

            foreach (var (control, _) in states)
            {
                targets.Add(control);
            }

            // A host with no client area says nothing about what is on screen: every rectangle in it
            // is empty, so a pass that arrives before the window has a size must not become the
            // baseline a later, real pass is then measured against.
            bool measurable = host.ClientSize.Width > 0f && host.ClientSize.Height > 0f;

            var snapshot = InViewSnapshot.Build(host, targets);
            var changes = new List<(Control Control, EffectiveInViewState State)>();

            // Every value is settled before any handler runs, so a handler that reads another watched
            // control sees the same pass, and one handler cannot change the inputs of another.
            foreach (var (control, state) in states)
            {
                bool value = snapshot.Evaluate(control);

                state._value = value;
                state._hasValue = true;
                state._epoch = _geometryEpoch;
                _computeCount++;

                if (!state._hasBaseline)
                {
                    if (!measurable)
                    {
                        continue;
                    }

                    // Registering establishes the baseline; only a later change is an event, which is
                    // what every other AddXHandler in the library does.
                    state._hasBaseline = true;
                    state._reported = value;
                }
                else if (state._reported != value)
                {
                    state._reported = value;
                    changes.Add((control, state));
                }
            }

            foreach (var (control, state) in detached)
            {
                state._value = false;
                state._hasValue = true;
                state._epoch = _geometryEpoch;
                changes.Add((control, state));
            }

            foreach (var (control, state) in changes)
            {
                if (control.Host is null)
                {
                    // Detached before or during the dispatch: it is not on screen anywhere, and this is
                    // the last pass that will mention it.
                    state._reported = false;
                    state._changed?.Invoke(control);

                    continue;
                }

                if (!ReferenceEquals(control.Host, host))
                {
                    // A handler moved it to another host. This pass's answer was computed against the
                    // tree that did not include it, so the new host's next pass reports instead.
                    continue;
                }

                state._changed?.Invoke(control);
            }
        }
        finally
        {
            _dispatching = false;
        }
    }

    private void RemoveChanged(Action<object> handler) => _changed -= handler;

    /// <summary>
    /// One invalidation subscription for a host.
    /// </summary>
    /// <remarks>
    /// A repaint request means something on screen may have changed - a child was added or removed, a
    /// control was hidden, a background or a filter moved - and none of those run a layout pass. The
    /// cached answers are therefore dropped by the same counter the pass uses, and the next read
    /// recomputes. The handler itself is static, so the host owns it and this object is only the
    /// marker that says the subscription exists.
    /// </remarks>
    private sealed class InvalidationWatch
    {
        public InvalidationWatch(UiHost host) => host.AddInvalidatedHandler(OnInvalidated);

        private static void OnInvalidated(object sender) => _geometryEpoch++;
    }
}
