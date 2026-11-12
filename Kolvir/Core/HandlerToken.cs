namespace Kolvir;

/// <summary>
/// The subscription returned by an <c>AddXHandler</c> method. Disposing it removes the handler it
/// was created for; disposing it twice is a no-op, and so is disposing it after the owner of the
/// event is gone.
/// </summary>
/// <typeparam name="THandler">The handler delegate type the subscription removes.</typeparam>
/// <remarks>
/// A token holds the handler so a scope can own a subscription without keeping the callback itself.
/// A caller that already has the callback can pass it to the matching <c>RemoveXHandler</c> instead
/// and skip the token entirely.
/// </remarks>
public sealed class HandlerToken<THandler> : IDisposable
    where THandler : Delegate
{
    private readonly Action<THandler> _remove;
    private THandler? _handler;

    /// <summary>Creates a subscription that runs <paramref name="remove"/> once, on disposal.</summary>
    /// <param name="remove">Removes the handler; normally the matching <c>RemoveXHandler</c>.</param>
    /// <param name="handler">The handler that was added.</param>
    public HandlerToken(Action<THandler> remove, THandler handler)
    {
        ArgumentNullException.ThrowIfNull(remove);
        ArgumentNullException.ThrowIfNull(handler);

        _remove = remove;
        _handler = handler;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        THandler? handler = _handler;
        if (handler is null)
        {
            return;
        }

        _handler = null;
        _remove(handler);
    }
}
