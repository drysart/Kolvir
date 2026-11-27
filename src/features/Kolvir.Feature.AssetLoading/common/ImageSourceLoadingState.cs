using System.Runtime.CompilerServices;
using Kolvir.Feature.InView;
using Microsoft.Extensions.DependencyInjection;

namespace Kolvir.Feature.AssetLoading;

/// <summary>
/// The per-control state behind <see cref="ImageSourceLoadingExtensions"/>: the URI, the placeholder,
/// the load in flight and the last answer.
/// </summary>
/// <remarks>
/// <para>
/// The state sits beside the control rather than in it, because a rung-3 package cannot add fields to
/// <see cref="Image"/>. It lives in a <see cref="ConditionalWeakTable{TKey,TValue}"/> keyed on the
/// control, exactly as <c>EffectiveInViewState</c> keeps its own state off every <c>Control</c>, and
/// that is also what makes the last answer readable after the load has finished and everything else
/// has gone (dev-docs/ASSET-LOADING.md section 6).
/// </para>
/// <para>
/// <b>The four moments a load starts or stops.</b> The <c>SourceUri</c>
/// setter, <see cref="Control.AddAddedToTreeHandler"/>, <see cref="Control.AddRemovedFromTreeHandler"/>
/// and <see cref="Control.AddDisposingHandler"/>, which is the pair the tree's own documentation
/// describes (section 6.2). The control is told about its own <see cref="Image.Source"/> assignments
/// through <see cref="Image.AddSourceChangedHandler"/>, which is what lets the application take the
/// control back without the feature mistaking its own writes for the application's (section 6).
/// </para>
/// <para>
/// <b>Threads.</b> Everything here runs on the UI thread. The setter and the tree notifications are
/// UI-thread work because the control tree is, and the completion this class observes is moved onto
/// that thread by the manager before the caller's task is completed, so the continuation needs no
/// dispatch of its own (sections 3.12 and 6). A loader may load anywhere and notify anywhere; that is
/// the manager's half of the same contract.
/// </para>
/// <para>
/// <b>Ownership.</b> The loaded <see cref="ImageData"/> is the feature's, because the feature's load
/// produced it, so this class disposes it when it is replaced, when the URI changes and when the
/// control is disposed. The placeholder is the application's, so it is written into
/// <see cref="Image.Source"/> and never disposed here - not even when a load succeeds over it
/// (section 6.3).
/// </para>
/// </remarks>
internal sealed class ImageSourceLoadingState
{
    /// <summary>Every control the feature has been used on, so its state survives between reads.</summary>
    private static readonly ConditionalWeakTable<Image, ImageSourceLoadingState> States = new();

    /// <summary>
    /// The controls whose URI is waiting for a service provider: assigned before the control could
    /// reach one, or detached from the tree that provided it (section 6.1).
    /// </summary>
    private static readonly ConditionalWeakTable<Image, ImageSourceLoadingState> Waiting = new();

    private static IDisposable? _layoutSubscription;

    private readonly IDisposable _addedToTreeSubscription;
    private readonly IDisposable _removedFromTreeSubscription;
    private readonly IDisposable _disposingSubscription;
    private readonly IDisposable _sourceChangedSubscription;

    private string? _sourceUri;
    private ImageData? _placeholderSource;
    private ImageData? _loaded;
    private ImageSourceLoadState _state = ImageSourceLoadState.None;
    private Exception? _error;
    private Action<object>? _stateChanged;

    private CancellationTokenSource? _cancellation;
    private IAssetLoader<ImageData>? _loader;
    private IDisposable? _inViewSubscription;

    /// <summary>
    /// The request whose completion this control is waiting on. A completion compares itself against
    /// this rather than trusting the cancellation, so a load that finishes after its control moved on
    /// cannot write a state or an image (section 6.1).
    /// </summary>
    private object? _request;

    /// <summary>
    /// The value the feature is assigning to <see cref="Image.Source"/> right now, and whether it is
    /// assigning one. The notification is compared against it rather than against a flag, so an
    /// assignment an application handler makes from inside that notification is still recognised as
    /// the application's (section 6).
    /// </summary>
    private ImageData? _writingValue;
    private bool _writingSource;

    /// <summary>
    /// Bumped whenever the relationship the control follows changes - a new URI, or the application
    /// taking the control over. An operation that writes <see cref="Image.Source"/> and then raises a
    /// state change checks it, because either of those writes can run an application handler that
    /// changes the relationship first.
    /// </summary>
    private int _generation;

    private ImageSourceLoadingState(Image image)
    {
        _addedToTreeSubscription = image.AddAddedToTreeHandler(OnAddedToTree);
        _removedFromTreeSubscription = image.AddRemovedFromTreeHandler(OnRemovedFromTree);
        _disposingSubscription = image.AddDisposingHandler(OnDisposing);
        _sourceChangedSubscription = image.AddSourceChangedHandler(OnSourceChanged);
    }

    /// <summary>The state of one control, created - and subscribed - on first use.</summary>
    /// <param name="image">The control the feature is being used on.</param>
    /// <returns>The control's state.</returns>
    public static ImageSourceLoadingState For(Image image) =>
        States.GetValue(image, static owner => new ImageSourceLoadingState(owner));

    /// <summary>The URI the control is following, or null when it is not following one.</summary>
    /// <param name="image">The control to read.</param>
    /// <returns>The URI exactly as it was assigned, or null.</returns>
    public static string? GetSourceUri(Image image) => For(image)._sourceUri;

    /// <summary>Replaces the URI the control follows, and is the only way a load starts before the
    /// control is in a tree.</summary>
    /// <param name="image">The control to change.</param>
    /// <param name="value">The new URI, or null to stop following one.</param>
    public static void SetSourceUri(Image image, string? value) => For(image).ApplySourceUri(image, value);

    /// <summary>The placeholder the control shows while it has no loaded image.</summary>
    /// <param name="image">The control to read.</param>
    /// <returns>The borrowed placeholder, or null.</returns>
    public static ImageData? GetPlaceholderSource(Image image) => For(image)._placeholderSource;

    /// <summary>Replaces the borrowed placeholder, showing it when nothing loaded is on screen.</summary>
    /// <param name="image">The control to change.</param>
    /// <param name="value">The new placeholder, or null for none.</param>
    public static void SetPlaceholderSource(Image image, ImageData? value) =>
        For(image).ApplyPlaceholderSource(image, value);

    /// <summary>Where the control's load has got to.</summary>
    /// <param name="image">The control to read.</param>
    /// <returns>The state.</returns>
    public static ImageSourceLoadState GetSourceLoadState(Image image) => For(image)._state;

    /// <summary>What the last load threw, or null.</summary>
    /// <param name="image">The control to read.</param>
    /// <returns>The failure, or null when the last load did not fail.</returns>
    public static Exception? GetSourceLoadError(Image image) => For(image)._error;

    /// <summary>Adds a handler that runs when the control's state changes.</summary>
    /// <param name="image">The control to watch.</param>
    /// <param name="handler">The handler, which receives the control.</param>
    /// <returns>A subscription that removes the handler when it is disposed.</returns>
    public static IDisposable AddHandler(Image image, Action<object> handler)
    {
        ImageSourceLoadingState state = For(image);

        state._stateChanged += handler;

        return new HandlerToken<Action<object>>(state.RemoveChanged, handler);
    }

    /// <summary>Removes a handler that <see cref="AddHandler"/> added.</summary>
    /// <param name="image">The control being watched.</param>
    /// <param name="handler">The handler to remove.</param>
    public static void RemoveHandler(Image image, Action<object> handler)
    {
        if (States.TryGetValue(image, out ImageSourceLoadingState? state))
        {
            state._stateChanged -= handler;
        }
    }

    /// <summary>
    /// Accepts a URI, or ends the relationship, without ever starting a load from a control that
    /// cannot reach its services yet.
    /// </summary>
    /// <param name="image">The control being assigned.</param>
    /// <param name="value">The new URI, or null.</param>
    /// <remarks>
    /// <para>
    /// An equal assignment does nothing at all: no restart, no state change and no write to
    /// <see cref="Image.Source"/>. That guard is what keeps a re-rendering tree, or an imperative
    /// caller that re-asserts the same URI every frame, from turning a permanent failure into a
    /// retry storm (section 6.1).
    /// </para>
    /// <para>
    /// A different URI cancels the outstanding load and releases the image it produced before the new
    /// state is set, so the control goes straight to <see cref="ImageSourceLoadState.Pending"/> with
    /// the new URI - it never passes through <see cref="ImageSourceLoadState.None"/>, because it is
    /// still following a URI, only a different one.
    /// </para>
    /// </remarks>
    private void ApplySourceUri(Image image, string? value)
    {
        if (string.Equals(_sourceUri, value, StringComparison.Ordinal))
        {
            return;
        }

        // Cancelled before anything else changes: the cancellation can resume this control's own
        // continuation inline, and `_request` being cleared first is what makes that completion stale.
        CancelLoad();
        ReleaseInView();
        ReleaseLoaded(image);

        _sourceUri = value;
        _error = null;
        _generation++;

        int generation = _generation;

        if (value is null)
        {
            Waiting.Remove(image);
            WriteSource(image, null);

            if (generation == _generation)
            {
                SetState(image, ImageSourceLoadState.None);
            }

            return;
        }

        SubscribeInView(image);
        ShowPlaceholder(image);

        if (generation != _generation)
        {
            // A handler the placeholder's assignment ran changed the relationship - assigned a
            // different URI, or took the control back by assigning Source. Its state is the answer.
            return;
        }

        SetState(image, ImageSourceLoadState.Pending);
        TryStart(image);
    }

    /// <summary>Replaces the borrowed placeholder.</summary>
    /// <param name="image">The control being assigned.</param>
    /// <param name="value">The new placeholder, or null for none.</param>
    /// <remarks>
    /// It is written into <see cref="Image.Source"/> only while the control has no loaded image for
    /// its URI, which is what makes <see cref="ImageSourceLoadState.Loaded"/> win without the feature
    /// having to clear anything on the way out. An equal assignment raises nothing, like every other
    /// settable property.
    /// </remarks>
    private void ApplyPlaceholderSource(Image image, ImageData? value)
    {
        if (ReferenceEquals(_placeholderSource, value))
        {
            return;
        }

        _placeholderSource = value;

        if (_loaded is null && _state is not ImageSourceLoadState.None)
        {
            WriteSource(image, value);
        }
    }

    /// <summary>Starts the load if the control can reach its services, and waits on a layout pass if
    /// it cannot.</summary>
    /// <param name="image">The control to start a load for.</param>
    /// <remarks>
    /// <para>
    /// <see cref="Control.Services"/> is resolved through <see cref="Control.Host"/>, which is only
    /// set once the tree a control belongs to is attached to a host. That is why this is attempted
    /// from three places rather than one: the setter (the control was already in a hosted tree),
    /// <see cref="Control.AddAddedToTreeHandler"/> (it just joined one) and a completed layout pass
    /// (the tree it was already in was attached to a host, which raises neither of the other two -
    /// <c>AttachHost</c> touches only the root). Each attempt either starts the load
    /// or leaves the control in <see cref="Waiting"/> for the next one.
    /// </para>
    /// <para>
    /// <see cref="ImageSourceLoadState.Pending"/> is already set when this runs, never after the
    /// manager is called: a loader that starts at once calls back synchronously from inside
    /// <c>LoadAsync</c>, and the control must already be waiting when it does (section 6.1).
    /// </para>
    /// </remarks>
    private void TryStart(Image image)
    {
        if (_sourceUri is null || _request is not null)
        {
            Waiting.Remove(image);

            return;
        }

        if (image.Services is not { } services)
        {
            Wait(image);

            return;
        }

        IAssetLoaderProvider? provider = services.GetService<IAssetLoaderProvider>();
        IAssetLoadManager? manager = services.GetService<IAssetLoadManager>();

        if (provider is null || manager is null)
        {
            Waiting.Remove(image);
            FailTo(image, new InvalidOperationException(
                $"No {nameof(IAssetLoaderProvider)} or {nameof(IAssetLoadManager)} is registered, so the " +
                "image cannot follow its SourceUri. Call services.AddAssetLoading() when the application's " +
                "services are configured."));

            return;
        }

        if (!provider.TryCreateAssetLoader<ImageData>(_sourceUri, out IAssetLoader<ImageData>? loader))
        {
            Waiting.Remove(image);
            FailTo(image, new NotSupportedException(
                $"No registered loader factory produces {nameof(ImageData)} for '{_sourceUri}'. Register " +
                "one with services.AddAssetLoaderFactory(), or clear the URI."));

            return;
        }

        Waiting.Remove(image);

        var cancellation = new CancellationTokenSource();
        object request = new();

        // Everything the started callback and the completion read is in place before the manager is
        // called, because either of them can run before this method returns.
        _cancellation = cancellation;
        _request = request;
        _loader = loader;
        loader.Priority = PriorityFor(image);

        Task<ImageData> task;

        try
        {
            task = manager.LoadAsync(loader, () => OnStarted(image, request), cancellation.Token);
        }
        catch
        {
            // The manager's own guards rather than a load failure - a wrong thread, a handle with a
            // request outstanding. The feature is left at Pending and nothing is watching it.
            _request = null;
            _cancellation = null;
            _loader = null;
            cancellation.Cancel();

            throw;
        }

        Observe(image, request, task);
    }

    /// <summary>The manager's started callback: the load is in flight rather than queued.</summary>
    /// <param name="image">The control the load is for.</param>
    /// <param name="request">The request that started.</param>
    private void OnStarted(Image image, object request)
    {
        if (!ReferenceEquals(_request, request))
        {
            return;
        }

        SetState(image, ImageSourceLoadState.Loading);
    }

    /// <summary>Waits for the load and applies its outcome on the UI thread.</summary>
    /// <param name="image">The control the load is for.</param>
    /// <param name="request">The request that started.</param>
    /// <param name="task">The task the manager answered.</param>
    private void Observe(Image image, object request, Task<ImageData> task) =>
        _ = CompleteAsync(image, this, request, task);

    /// <summary>
    /// Applies one completion, or drops it when the control has moved on.
    /// </summary>
    /// <param name="image">The control the load is for.</param>
    /// <param name="state">The control's state.</param>
    /// <param name="request">The request the completion belongs to.</param>
    /// <param name="task">The task the manager answered.</param>
    /// <remarks>
    /// The task is already on the UI thread when it completes - the manager moves completions there
    /// and the ambient context resumes the await there - so this needs no dispatch of its own
    /// (section 3.12). The returned task is not awaited by anyone and never faults: a handler that
    /// throws from a state change faults it, exactly as an exception from a control handler reaches
    /// whatever raised it.
    /// </remarks>
    private static async Task CompleteAsync(
        Image image,
        ImageSourceLoadingState state,
        object request,
        Task<ImageData> task)
    {
        ImageData data;

        try
        {
            data = await task;
        }
        catch (OperationCanceledException) when (task.IsCanceled)
        {
            // The caller's own cancellation, which is what a superseded, detached or disposed load
            // produces. It is not a failure and the control has already moved on.
            return;
        }
        catch (Exception error)
        {
            state.Fail(image, request, error);

            return;
        }

        state.Load(image, request, data);
    }

    /// <summary>Settles a loaded image into the control.</summary>
    /// <param name="image">The control the load is for.</param>
    /// <param name="request">The request that completed.</param>
    /// <param name="data">What it produced.</param>
    /// <remarks>
    /// A completion for a request the control has moved past releases the image it was handed and
    /// nothing else: the feature created it, so the feature disposes it, and writing it into a
    /// control that is following a different URI would be the stale completion the ordering rules
    /// forbid (section 6.1). The state change is raised after <see cref="Image.Source"/> has been
    /// assigned, so a handler reads the image the state describes.
    /// </remarks>
    private void Load(Image image, object request, ImageData data)
    {
        if (!ReferenceEquals(_request, request))
        {
            data.Dispose();

            return;
        }

        ClearCurrent();
        ReleaseLoaded(image);

        _loaded = data;
        _error = null;

        int generation = _generation;

        WriteSource(image, data);

        if (generation != _generation)
        {
            // A SourceChanged handler took the control over while the image was being written. It
            // owns the state now, and the image it replaced has already been released.
            return;
        }

        SetState(image, ImageSourceLoadState.Loaded);
    }

    /// <summary>Settles a failure into the control.</summary>
    /// <param name="image">The control the load is for.</param>
    /// <param name="request">The request that failed.</param>
    /// <param name="error">What it threw.</param>
    /// <remarks>
    /// The URI is left exactly as it was, because it is the only record of what failed and a retry
    /// needs it. The placeholder goes back on screen - the failed load's image, if an earlier one was
    /// displayed, is released first - and the state change is raised afterwards (section 6.1).
    /// </remarks>
    private void Fail(Image image, object request, Exception error)
    {
        if (!ReferenceEquals(_request, request))
        {
            return;
        }

        FailTo(image, error);
    }

    /// <summary>Applies a failure that no request is waiting on.</summary>
    /// <param name="image">The control the failure belongs to.</param>
    /// <param name="error">What went wrong.</param>
    private void FailTo(Image image, Exception error)
    {
        ClearCurrent();
        ReleaseLoaded(image);

        _error = error;

        int generation = _generation;

        WriteSource(image, _placeholderSource);

        if (generation != _generation)
        {
            return;
        }

        SetState(image, ImageSourceLoadState.Failed);
    }

    /// <summary>Cancels the load in flight and forgets it.</summary>
    /// <remarks>
    /// <para>
    /// <c>_request</c> is cleared <em>before</em> the token is cancelled. Cancelling a queued request
    /// answers its caller inside the <c>Cancel()</c> call (section 3.9), which resumes this control's
    /// own continuation before this method returns - and the request being gone is what makes that
    /// completion a stale one rather than a load that writes over the new state.
    /// </para>
    /// <para>
    /// The token source is not disposed here: a cancelled load may still be running (section 3.9),
    /// and a loader that registers on the token afterwards would find a disposed source. It is
    /// disposed when a load ends normally, where there is nothing left to register.
    /// </para>
    /// </remarks>
    private void CancelLoad()
    {
        _request = null;
        _loader = null;

        CancellationTokenSource? cancellation = _cancellation;
        _cancellation = null;

        cancellation?.Cancel();
    }

    /// <summary>Forgets the current request after it has ended, and releases its token source.</summary>
    private void ClearCurrent()
    {
        _request = null;
        _loader = null;

        CancellationTokenSource? cancellation = _cancellation;
        _cancellation = null;

        cancellation?.Dispose();
    }

    /// <summary>The tree notification: a load starts here for a control that has just joined a tree.</summary>
    /// <param name="image">The control.</param>
    private void OnAddedToTree(Image image)
    {
        if (_sourceUri is null)
        {
            return;
        }

        // Pending is the state a detached control was left in, and the restart is a fresh load for
        // the same URI - which is what the pair's own documentation says it is for (section 6.1).
        TryStart(image);
    }

    /// <summary>
    /// The tree notification: a control that left its tree is cancelled and reset to
    /// <see cref="ImageSourceLoadState.Pending"/>, not left <see cref="ImageSourceLoadState.Loading"/>.
    /// </summary>
    /// <param name="image">The control.</param>
    /// <remarks>
    /// Removal is not disposal, so the control is alive and reusable and may come back with the same
    /// URI. Pending is the right state in between; a spinner over a load that is not coming back
    /// would not be. An already-loaded image is left on screen and kept, because it is still the image
    /// the control has for its URI and nothing has failed (sections 6.1 and 6.3).
    /// </remarks>
    private void OnRemovedFromTree(Image image)
    {
        if (_sourceUri is null)
        {
            return;
        }

        CancelLoad();
        Wait(image);
        SetState(image, ImageSourceLoadState.Pending);
    }

    /// <summary>
    /// The disposal notification: cancel the load and release the image the feature created, while
    /// the control is still usable (section 6.3).
    /// </summary>
    /// <param name="image">The control being disposed.</param>
    private void OnDisposing(Image image)
    {
        CancelLoad();
        ReleaseInView();
        Waiting.Remove(image);

        // The control is already past its last paint and disposes its source of its own accord a few
        // lines after this returns, so the image is released without touching Source: assigning it
        // here would lay a disposing control out again.
        DisposeLoaded();

        _addedToTreeSubscription.Dispose();
        _removedFromTreeSubscription.Dispose();
        _disposingSubscription.Dispose();
        _sourceChangedSubscription.Dispose();
    }

    /// <summary>
    /// The source notification: an assignment the feature did not make means the application has
    /// taken the control over.
    /// </summary>
    /// <param name="image">The control whose source changed.</param>
    /// <remarks>
    /// The load is cancelled, the URI is cleared and the state is <see cref="ImageSourceLoadState.None"/>
    /// - not <see cref="ImageSourceLoadState.Failed"/>, because nothing failed. The image the
    /// application assigned is left exactly where it is, and a load that was in flight cannot replace
    /// it because there is no longer a load to deliver. The feature's own writes are excluded by the
    /// suppression flag, which is why this rule needs the notification at all (section 6).
    /// </remarks>
    private void OnSourceChanged(Image image)
    {
        if (_writingSource && ReferenceEquals(image.Source, _writingValue))
        {
            return;
        }

        CancelLoad();
        ReleaseInView();
        Waiting.Remove(image);
        ReleaseLoaded(image);

        _sourceUri = null;
        _error = null;
        _generation++;

        SetState(image, ImageSourceLoadState.None);
    }

    /// <summary>The control's effective-in-view answer changed: move its load's priority.</summary>
    /// <param name="sender">The control, which is the sender of the notification.</param>
    private static void OnEffectiveInViewChanged(object sender)
    {
        var image = (Image)sender;

        if (States.TryGetValue(image, out ImageSourceLoadingState? state))
        {
            state.ApplyPriority(image);
        }
    }

    private static void OnAddedToTree(object sender)
    {
        var image = (Image)sender;

        if (States.TryGetValue(image, out ImageSourceLoadingState? state))
        {
            state.OnAddedToTree(image);
        }
    }

    private static void OnRemovedFromTree(object sender)
    {
        var image = (Image)sender;

        if (States.TryGetValue(image, out ImageSourceLoadingState? state))
        {
            state.OnRemovedFromTree(image);
        }
    }

    private static void OnDisposing(object sender)
    {
        var image = (Image)sender;

        if (States.TryGetValue(image, out ImageSourceLoadingState? state))
        {
            state.OnDisposing(image);
        }
    }

    private static void OnSourceChanged(object sender)
    {
        var image = (Image)sender;

        if (States.TryGetValue(image, out ImageSourceLoadingState? state))
        {
            state.OnSourceChanged(image);
        }
    }

    /// <summary>The layout notification: a control that was waiting for its tree to be hosted can
    /// start now.</summary>
    /// <remarks>
    /// <see cref="UiHost.Root"/> attaching a tree to a host sets <see cref="Control.Host"/> for the
    /// whole subtree and raises neither tree notification, so this is the moment at which an image
    /// that was added to a tree before that tree reached a window can finally resolve its services.
    /// The walk is over the waiting controls only, which is empty in an application whose trees are
    /// built inside a hosted window.
    /// </remarks>
    private static void OnLayoutCompleted()
    {
        if (!AnyWaiting())
        {
            return;
        }

        var waiting = new List<(Image Image, ImageSourceLoadingState State)>();

        foreach (KeyValuePair<Image, ImageSourceLoadingState> pair in Waiting)
        {
            waiting.Add((pair.Key, pair.Value));
        }

        foreach (var (image, state) in waiting)
        {
            state.TryStart(image);
        }
    }

    private static bool AnyWaiting()
    {
        foreach (var _ in Waiting)
        {
            return true;
        }

        return false;
    }

    /// <summary>Moves a load's priority to whatever the control's visibility now says.</summary>
    /// <param name="image">The control.</param>
    private void ApplyPriority(Image image)
    {
        if (_loader is { } loader)
        {
            loader.Priority = PriorityFor(image);
        }
    }

    /// <summary>In view is <see cref="AssetLoadPriority.Visible"/>; anything else is
    /// <see cref="AssetLoadPriority.Background"/> (section 2.5).</summary>
    /// <param name="image">The control.</param>
    /// <returns>The priority to give its loader.</returns>
    private static int PriorityFor(Image image) =>
        image.EffectiveInView ? AssetLoadPriority.Visible : AssetLoadPriority.Background;

    /// <summary>Records the control as waiting for a service provider, and makes sure a completed
    /// layout pass will try it again.</summary>
    /// <param name="image">The control.</param>
    private void Wait(Image image)
    {
        if (!Waiting.TryGetValue(image, out _))
        {
            Waiting.Add(image, this);
        }

        _layoutSubscription ??= UiHost.AddLayoutCompletedHandler(static _ => OnLayoutCompleted());
    }

    /// <summary>Subscribes the priority mapping for the URI that is being followed.</summary>
    /// <param name="image">The control.</param>
    private void SubscribeInView(Image image) =>
        _inViewSubscription ??= image.AddEffectiveInViewChangedHandler(OnEffectiveInViewChanged);

    /// <summary>Drops the priority mapping when the control stops following a URI.</summary>
    private void ReleaseInView()
    {
        IDisposable? subscription = _inViewSubscription;
        _inViewSubscription = null;
        subscription?.Dispose();
    }

    /// <summary>Writes the placeholder when the control has no loaded image of its own.</summary>
    /// <param name="image">The control.</param>
    private void ShowPlaceholder(Image image)
    {
        if (_loaded is null)
        {
            WriteSource(image, _placeholderSource);
        }
    }

    /// <summary>Disposes the loaded image the feature owns, and clears it from the control.</summary>
    /// <param name="image">The control.</param>
    private void ReleaseLoaded(Image image)
    {
        ImageData? loaded = _loaded;

        if (loaded is null)
        {
            return;
        }

        _loaded = null;

        if (ReferenceEquals(image.Source, loaded))
        {
            WriteSource(image, null);
        }

        loaded.Dispose();
    }

    /// <summary>Disposes the loaded image without touching <see cref="Image.Source"/>.</summary>
    private void DisposeLoaded()
    {
        ImageData? loaded = _loaded;

        if (loaded is null)
        {
            return;
        }

        _loaded = null;
        loaded.Dispose();
    }

    /// <summary>Assigns <see cref="Image.Source"/> without the change being mistaken for the
    /// application's.</summary>
    /// <param name="image">The control.</param>
    /// <param name="value">What to assign.</param>
    /// <remarks>
    /// The recorded value swallows only the assignment made here. A handler the assignment raises may
    /// assign <see cref="Image.Source"/> again, and that nested assignment is the application's and is
    /// treated as such, because the value it leaves behind is not the one being written.
    /// </remarks>
    private void WriteSource(Image image, ImageData? value)
    {
        if (ReferenceEquals(image.Source, value))
        {
            return;
        }

        ImageData? previous = _writingValue;
        bool wasWriting = _writingSource;

        _writingValue = value;
        _writingSource = true;

        try
        {
            image.Source = value;
        }
        finally
        {
            _writingValue = previous;
            _writingSource = wasWriting;
        }
    }

    /// <summary>Changes the state and, when it changed, tells whoever is listening.</summary>
    /// <param name="image">The control.</param>
    /// <param name="state">The new state.</param>
    private void SetState(Image image, ImageSourceLoadState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        _stateChanged?.Invoke(image);
    }

    private void RemoveChanged(Action<object> handler) => _stateChanged -= handler;
}
