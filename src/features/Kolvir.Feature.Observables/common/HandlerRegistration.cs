namespace Kolvir.Observables;

/// <summary>
/// The subscription token: what unregisters one handler from one source, once.
/// </summary>
/// <remarks>
/// <para>
/// Registration is a subscription, not a claim, so the token is the whole of the unsubscribe: nobody
/// has to remember what was registered where, and disposing the token twice does nothing the second
/// time.
/// </para>
/// <para>
/// It releases both references on the first dispose, so a token that outlives its source holds no
/// source and no closure. It also calls
/// <see cref="IObservableEventSource.RemoveObservableEventRaisedHandler"/> rather than reaching into
/// the list, which is why that member is the one thing that stays legal on a disposed source: a
/// teardown path disposes tokens without checking what they point at.
/// </para>
/// </remarks>
internal sealed class HandlerRegistration : IDisposable
{
    private IObservableEventSource? _source;
    private Action<IObservableEventSource>? _handler;

    internal HandlerRegistration(IObservableEventSource source, Action<IObservableEventSource> handler)
    {
        _source = source;
        _handler = handler;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        var source = _source;
        var handler = _handler;

        if (source is null || handler is null)
        {
            return;
        }

        _source = null;
        _handler = null;

        source.RemoveObservableEventRaisedHandler(handler);
    }
}
