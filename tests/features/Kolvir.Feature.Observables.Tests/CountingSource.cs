namespace Kolvir.Feature.Observables.Tests;

/// <summary>
/// A hand-written dependency source, which is what an application with a dependency that is not an
/// observable value would write: it counts what happens to it, so a test can ask whether the layer
/// hooked it, unhooked it, and whether a raise reached anyone.
/// </summary>
internal sealed class CountingSource : IObservableEventSource
{
    private readonly List<Action<IObservableEventSource>> _handlers = [];

    /// <summary>How many times a handler has been registered, over the source's whole life.</summary>
    public int Registrations { get; private set; }

    /// <summary>How many handlers are registered now, which is what a hook and an unhook move.</summary>
    public int ActiveHandlers => _handlers.Count;

    /// <inheritdoc/>
    public bool IsDisposed => false;

    /// <inheritdoc/>
    public IDisposable AddObservableEventRaisedHandler(Action<IObservableEventSource> handler)
    {
        Registrations++;
        _handlers.Add(handler);
        return new Release(this, handler);
    }

    /// <inheritdoc/>
    public void RemoveObservableEventRaisedHandler(Action<IObservableEventSource> handler) =>
        _handlers.Remove(handler);

    /// <inheritdoc/>
    public void RaiseEvent()
    {
        foreach (var handler in _handlers.ToArray())
        {
            handler(this);
        }
    }

    private sealed class Release(CountingSource source, Action<IObservableEventSource> handler) : IDisposable
    {
        public void Dispose() => source.RemoveObservableEventRaisedHandler(handler);
    }
}
