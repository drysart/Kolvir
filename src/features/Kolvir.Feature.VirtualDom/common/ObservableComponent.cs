using Kolvir.Observables;

namespace Kolvir.VirtualDom;

/// <summary>
/// A component that renders from a data model, and re-renders when the model state its render <em>read</em>
/// changes.
/// </summary>
/// <typeparam name="TDataModel">The model this component presents. A reference type, because a null model
/// is a state the component has to handle.</typeparam>
/// <remarks>
/// <para>
/// The other half of <see cref="Component"/>: that one is told when to render, this one works it out for
/// itself. A render is wrapped in a collection scope, so every observable value and every named dependency
/// the render reads subscribes to it as it reads, and the component is invalidated when one of them
/// changes - exactly the state the render actually looked at, not the state it might have looked at.
/// </para>
/// <code>
/// internal sealed class DocumentPage : ObservableComponent&lt;DocumentModel&gt;
/// {
///     protected override VNode RenderWithDataModel(RenderArgs&lt;DocumentModel&gt; args) =&gt; new VPanel
///     {
///         Dock = DockStyle.Fill,
///         Children =
///         [
///             new VLabel { Text = args.DataModel.Text },
///             new VLabel { Text = $"{args.DataModel.WordCount} words" },
///         ],
///     };
/// }
///
/// var page = new DocumentPage();
/// using var mount = VirtualDomRuntime.Mount(window.Root, page);
/// page.DataModel = document;      // one render, on the next frame
/// document.Text = "changed";      // and another, because the render read Text
/// </code>
/// <para>
/// <b>What re-renders it.</b> Two things, and they are the same mechanism: a dependency the render read
/// raising its event, and <see cref="DataModel"/> being set to a different model. Both end in
/// <see cref="ComponentMount.Invalidate"/>, so both are one render on the next frame however many changes
/// arrive first (section 7.1 of `VIRTUAL-DOM.md`), and neither renders inside the change - which is what
/// keeps a burst of changes from re-rendering a component that is about to be asked to render anyway.
/// </para>
/// <para>
/// <b>Two kinds of registration, two lifetimes.</b> A render may need to register something of its own -
/// a handler that exists only while a condition holds, a one-shot subscription - and
/// <see cref="RenderArgs.AddDisposable"/> takes it for the life of that render. <see cref="OnAttached"/>
/// gets <see cref="AttachedArgs.AddDisposable"/> instead, for things that must outlive every render: a
/// timer, a keyboard hook, a subscription that belongs to being on screen.
/// </para>
/// <para>
/// <b>A render that fails is still listening.</b> The collection scope subscribes as the render reads, so a
/// render that throws halfway is still subscribed to what it reached by then, and the next change to one of
/// those asks for another render. The exception itself is not caught: it travels out of
/// <see cref="Render"/> into the mount, which is where the frame's own failure boundary is. A first render
/// that throws is different - the mount has nothing to patch, so it detaches again immediately, which
/// releases that run.
/// </para>
/// <para>
/// <b>One model, one component.</b> <see cref="DataModel"/> holds the model by reference: setting the same
/// instance again is not a change, because a change <em>inside</em> a model arrives through that model's
/// own observables. A component is one subtree, so it refuses a second live mount rather than quietly
/// taking over from the first.
/// </para>
/// </remarks>
public abstract class ObservableComponent<TDataModel> : IComponent, IDisposable
    where TDataModel : class
{
    private ComponentMount? _mount;
    private TDataModel? _dataModel;
    private List<IDisposable>? _renderTokens;
    private List<IDisposable>? _attachTokens;
    private bool _isDisposed;

    /// <summary>The model this component renders, or null before one is set.</summary>
    /// <remarks>
    /// Assigning a different model invalidates the component, which is one render on the next frame.
    /// Assigning the model it already has does nothing: a component listens to changes <em>inside</em> its
    /// model through that model's own observables, so "the same instance" is never a change it needs to
    /// be told about.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The component is disposed.</exception>
    public TDataModel? DataModel
    {
        get
        {
            ThrowIfDisposed();
            return _dataModel;
        }

        set
        {
            ThrowIfDisposed();

            if (object.ReferenceEquals(value, _dataModel))
            {
                return;
            }

            _dataModel = value;
            _mount?.Invalidate();
        }
    }

    /// <summary>What a render with no model is given.</summary>
    /// <param name="AddDisposable">
    /// Registers something for the life of this render, released before the next one starts.
    /// </param>
    public readonly record struct RenderArgs(Action<IDisposable> AddDisposable);

    /// <summary>What a render with a model is given.</summary>
    /// <typeparam name="TModel">The model's type.</typeparam>
    /// <param name="DataModel">The model to render. Never null, and never the reason to check for one.</param>
    /// <param name="AddDisposable">
    /// Registers something for the life of this render, released before the next one starts.
    /// </param>
    public readonly record struct RenderArgs<TModel>(TModel DataModel, Action<IDisposable> AddDisposable);

    /// <summary>What <see cref="OnAttached"/> is given.</summary>
    /// <param name="AddDisposable">
    /// Registers something for the life of this mount, released on detach or dispose.
    /// </param>
    public readonly record struct AttachedArgs(Action<IDisposable> AddDisposable);

    /// <summary>Describes the controls the model calls for, inside a collection scope.</summary>
    /// <returns>The tree to reconcile into what is mounted.</returns>
    /// <exception cref="ObjectDisposedException">The component is disposed.</exception>
    /// <remarks>
    /// <para>
    /// Called by the mount when the component has been invalidated, and once before that to mount a tree.
    /// Each call re-collects from scratch: the previous render's subscriptions are released first, so a
    /// render that stopped reading a value stops listening to it, and both a render and the values it read
    /// are ordinary - no caching, no comparing of trees.
    /// </para>
    /// <para>
    /// It is a description and not a mutation, exactly as <see cref="IComponent.Render"/> says: changing
    /// the model from inside it is not what this is for, and calling
    /// <see cref="ComponentMount.Invalidate"/> from inside it is refused by the mount.
    /// </para>
    /// </remarks>
    public VNode Render()
    {
        ThrowIfDisposed();

        // Re-rendering re-collects: the dependency set follows the render, the way an expression's follows
        // its function. The run subscribes as the render reads, so there is no pass over the set afterwards
        // - and a render that throws is still subscribed to what it reached, which is what keeps a
        // component that failed once from being stranded.
        CleanupRenderTokens();

        var tokens = new List<IDisposable>();
        _renderTokens = tokens;

        var onChanged = new Action<IObservableEventSource>(_ => _mount?.Invalidate());

        var run = _dataModel is null
            ? Observable.CollectDependencies(
                () => RenderWithNullModel(new RenderArgs(tokens.Add)),
                onChanged)
            : Observable.CollectDependencies(
                () => RenderWithDataModel(new RenderArgs<TDataModel>(_dataModel, tokens.Add)),
                onChanged);

        // The run is the listening; the list holds it beside whatever the render registered for itself.
        tokens.Add(run);

        return run.Value;   // throws into the mount if the render failed, having stayed subscribed
    }

    /// <summary>Describes the controls to show for <paramref name="args"/>.</summary>
    /// <param name="args">The model, and a way to register something for the life of this render.</param>
    /// <returns>The tree to reconcile into what is mounted.</returns>
    protected abstract VNode RenderWithDataModel(RenderArgs<TDataModel> args);

    /// <summary>Describes the controls to show when there is no model.</summary>
    /// <param name="args">A way to register something for the life of this render.</param>
    /// <returns>An empty <see cref="VFragment"/> - no controls at all - unless overridden.</returns>
    /// <remarks>
    /// <para>
    /// Rendering nothing is the default because having no model yet is ordinary: a page is mounted before
    /// its document has loaded, and a component that showed a spinner and nothing else would be showing
    /// something it did not ask for. Override this to describe what a model-less component looks like - a
    /// placeholder, a "not loaded" line - which is a decision about that component rather than a rule.
    /// </para>
    /// <para>
    /// Nothing is where it stops: an empty fragment puts no controls in the parent, and the mount still
    /// holds the place the controls will go, so the model arriving later is one ordinary render that adds
    /// them.
    /// </para>
    /// </remarks>
    protected virtual VNode RenderWithNullModel(RenderArgs args) => new VFragment();

    /// <summary>Called once per mount, before the first render.</summary>
    /// <param name="args">A way to register something for the life of this mount.</param>
    /// <remarks>
    /// Where a subscription or a hook that must outlive renders goes. Registering it here rather than in a
    /// render means it survives every render and is released once, when the component leaves the screen.
    /// </remarks>
    protected virtual void OnAttached(AttachedArgs args) { }

    /// <summary>Called when the component leaves the mount it was rendered in.</summary>
    /// <remarks>
    /// A reset rather than a tombstone: the component can be mounted again elsewhere, and one whose first
    /// render threw is still perfectly usable. Whatever <see cref="OnAttached"/> registered is already
    /// released by the time this runs, and the render's subscriptions are released immediately after it.
    /// </remarks>
    protected virtual void OnDetached() { }

    /// <summary>Records the mount that owns this component's tree.</summary>
    /// <param name="mount">The mount that is about to render it.</param>
    /// <exception cref="InvalidOperationException">The component is already mounted.</exception>
    /// <exception cref="ObjectDisposedException">The component is disposed.</exception>
    /// <remarks>
    /// Implemented explicitly, so it belongs to <see cref="IComponent"/> rather than to this class's own
    /// surface. One component is one subtree: a second mount would leave the first one rendering a
    /// component that no longer points at it.
    /// </remarks>
    void IComponent.Attach(ComponentMount mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        ThrowIfDisposed();

        if (_mount is not null)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} is already mounted. One component is one subtree, so it cannot appear "
                + "twice in a tree; give each place its own component instance.");
        }

        _mount = mount;

        CleanupAttachTokens();

        var tokens = _attachTokens = [];
        OnAttached(new AttachedArgs(tokens.Add));
    }

    /// <summary>Forgets <paramref name="mount"/> if it is the one this component renders through.</summary>
    /// <param name="mount">The mount being disposed, or the one whose first render threw.</param>
    /// <remarks>
    /// Called both when the mount goes away and as the undo of a first render that threw - and a component
    /// can be mounted again elsewhere - so this is a reset rather than a tombstone, and a call for a mount
    /// that is not the current one is ignored.
    /// </remarks>
    void IComponent.Detach(ComponentMount mount)
    {
        if (!ReferenceEquals(_mount, mount))
        {
            return;
        }

        _mount = null;

        OnDetached();
        CleanupRenderTokens();
        CleanupAttachTokens();
    }

    /// <summary>Ends this component's life: it stops listening, and every later use throws.</summary>
    /// <remarks>
    /// <para>
    /// Disposing is what a component's owner does instead of dropping it while it is still mounted, and it
    /// detaches here rather than waiting for a <see cref="IComponent.Detach"/> that may never come - so a
    /// derived class is told once, and the mount's own detach then finds nothing to do.
    /// </para>
    /// <para>
    /// Idempotent, and it does not dispose the mount: the mount owns the controls, and closing the window
    /// is what disposes both.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (_mount is not null)
        {
            _mount = null;
            OnDetached();
        }

        _dataModel = null;
        CleanupRenderTokens();
        CleanupAttachTokens();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);

    /// <summary>Releases what the last render registered, run included.</summary>
    /// <remarks>
    /// The field is cleared before anything is disposed, so a token whose disposal raises a change through
    /// the model cannot reach a list that is being walked.
    /// </remarks>
    private void CleanupRenderTokens()
    {
        var tokens = _renderTokens;
        _renderTokens = null;
        DisposeAll(tokens);
    }

    private void CleanupAttachTokens()
    {
        var tokens = _attachTokens;
        _attachTokens = null;
        DisposeAll(tokens);
    }

    private static void DisposeAll(List<IDisposable>? tokens)
    {
        if (tokens is null)
        {
            return;
        }

        foreach (var token in tokens)
        {
            token.Dispose();
        }
    }
}
