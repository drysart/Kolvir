using Kolvir.Feature.Observables;

namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// A view model whose <see cref="Render"/> describes the windows to show, and which re-renders itself
/// when the state its render read changes.
/// </summary>
/// <remarks>
/// <para>
/// The application-level counterpart of <see cref="ObservableComponent{TDataModel}"/>: a render is
/// wrapped in a collection scope, so every observable value and every named dependency the render
/// reads subscribes to it as it reads, and the component is invalidated when one of them changes -
/// exactly the state the render actually looked at. A developer who is used to an
/// <see cref="ObservableComponent{TDataModel}"/> writes the same code here; the only differences are
/// that the render returns a list of windows and the model is whatever fields the component keeps.
/// </para>
/// <code>
/// internal sealed class Shell : ApplicationComponent
/// {
///     private readonly IObservableList&lt;Document&gt; _documents = Observable.CreateList&lt;Document&gt;();
///
///     protected override IReadOnlyList&lt;ApplicationVNode&gt; Render() =&gt;
///     [
///         new VWindow { Key = "list", Title = "Documents", Content = new VComponent(new ListPage(_documents)) },
///         .._documents.Select(document =&gt; new VWindow
///         {
///             Key = document,
///             Title = document.Title.Value,
///             Content = new VComponent(new DocumentPage(document)),
///         }),
///     ];
/// }
/// </code>
/// <para>
/// <b>What re-renders it.</b> A dependency the render read raising its event, and
/// <see cref="Invalidate"/> being called for a change no observable saw. Both end in
/// <see cref="ApplicationMount.Invalidate"/>, so both are one render on the next frame however many
/// changes arrive first, and neither renders inside the change.
/// </para>
/// <para>
/// <b>One component, one application.</b> A component is one window set, so it refuses a second live
/// mount rather than quietly taking over from the first. Disposing it releases the render's
/// subscriptions; it does not dispose the mount, because the mount owns the windows.
/// </para>
/// </remarks>
public abstract class ApplicationComponent : IApplicationComponent, IDisposable
{
    private ApplicationMount? _mount;
    private List<IDisposable>? _renderTokens;
    private bool _isDisposed;

    /// <summary>Describes the windows this component's current state calls for.</summary>
    /// <returns>The window descriptors to reconcile into the real windows.</returns>
    /// <remarks>
    /// Called by the mount inside a collection scope, once per frame in which the state changed. It is
    /// a description and not a mutation: build and return the list, and let the reconciler work out
    /// what changed. It must not change state either: calling <see cref="Invalidate"/> from inside a
    /// render is refused, because a render that changes the state describes a window set that is
    /// already out of date.
    /// </remarks>
    protected abstract IReadOnlyList<ApplicationVNode> Render();

    /// <summary>Asks for this component to be rendered on the next frame.</summary>
    /// <remarks>
    /// <para>
    /// What an event handler calls after changing the model. It is cheap on purpose - a flag, a queue
    /// entry and a frame request - so a handler that changes several things renders once, in the
    /// frame that shows the result.
    /// </para>
    /// <para>
    /// Calling it from inside a render is refused rather than folded into the pass: a render
    /// describes what the state is, so a render that changes the state describes a window set that is
    /// already out of date. The message names the component.
    /// </para>
    /// <para>
    /// It does nothing when the component has no live mount or has been disposed: before the first
    /// mount nothing is lost, because the first render reads the state as it is by then; after a
    /// dispose there is no window set left to correct.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The component changed state from inside its own <see cref="Render"/>.
    /// </exception>
    public void Invalidate()
    {
        if (_isDisposed)
        {
            return;
        }

        _mount?.Invalidate();
    }

    /// <summary>Ends this component's life: it stops listening, and every later use throws.</summary>
    /// <remarks>
    /// Disposing is what a component's owner does instead of dropping it while it is still mounted,
    /// and it detaches here rather than waiting for an <see cref="IApplicationComponent.Detach"/> that
    /// may never come. It does not dispose the mount: the mount owns the windows, and closing those
    /// is what disposes both.
    /// </remarks>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _mount = null;
        CleanupRenderTokens();
    }

    /// <summary>Records the mount that owns this component's windows.</summary>
    /// <param name="mount">The mount that is about to render it.</param>
    /// <exception cref="InvalidOperationException">The component is already mounted.</exception>
    /// <exception cref="ObjectDisposedException">The component is disposed.</exception>
    /// <remarks>
    /// Implemented explicitly, so it belongs to <see cref="IApplicationComponent"/> rather than to
    /// this class's own surface. One component is one window set: a second mount would leave the
    /// first one rendering a component that no longer points at it.
    /// </remarks>
    void IApplicationComponent.Attach(ApplicationMount mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        ThrowIfDisposed();

        if (_mount is not null)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} is already mounted. One component is one application, so it cannot be mounted twice; give each application its own component instance.");
        }

        _mount = mount;
    }

    /// <summary>Forgets <paramref name="mount"/> if it is the one this component renders through.</summary>
    /// <param name="mount">The mount being disposed, or the one whose first render threw.</param>
    /// <remarks>
    /// Called both when the mount goes away and as the undo of a first render that threw - and a
    /// component can be mounted again elsewhere - so this is a reset rather than a tombstone, and a
    /// call for a mount that is not the current one is ignored.
    /// </remarks>
    void IApplicationComponent.Detach(ApplicationMount mount)
    {
        if (!ReferenceEquals(_mount, mount))
        {
            return;
        }

        _mount = null;
        CleanupRenderTokens();
    }

    /// <summary>Describes the windows to show, inside a collection scope.</summary>
    /// <returns>The window descriptors to reconcile into the real windows.</returns>
    /// <exception cref="ObjectDisposedException">The component is disposed.</exception>
    /// <remarks>
    /// Explicit because the mount is the only caller. Re-rendering re-collects: the previous render's
    /// subscriptions are released first, so a render that stopped reading a value stops listening to
    /// it. The run subscribes as the render reads, so a render that throws is still subscribed to
    /// what it reached, which is what keeps a component that failed once from being stranded.
    /// </remarks>
    IReadOnlyList<ApplicationVNode> IApplicationComponent.Render()
    {
        ThrowIfDisposed();

        CleanupRenderTokens();

        var tokens = new List<IDisposable>();
        _renderTokens = tokens;

        var onChanged = new Action<IObservableEventSource>(_ => _mount?.Invalidate());
        var run = Observable.CollectDependencies(Render, onChanged);

        tokens.Add(run);

        return run.Value;   // throws into the mount if the render failed, having stayed subscribed
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);

    /// <summary>Releases what the last render registered, run included.</summary>
    /// <remarks>
    /// The field is cleared before anything is disposed, so a token whose disposal raises a change
    /// through the model cannot reach a list that is being walked.
    /// </remarks>
    private void CleanupRenderTokens()
    {
        var tokens = _renderTokens;
        _renderTokens = null;

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
