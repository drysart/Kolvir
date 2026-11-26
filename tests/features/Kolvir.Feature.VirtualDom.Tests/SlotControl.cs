using Kolvir;

namespace Kolvir.Feature.VirtualDom.Tests;

/// <summary>
/// A control whose only interesting surface is the extension member a static class adds to it, so a
/// node's extension half can be watched against a real control.
/// </summary>
/// <remarks>
/// The constructor leaves <see cref="SlotValue"/> at <see cref="ConstructorValue"/>, and the extension
/// property declares a different pristine value, so a test can tell which of the two a node restored.
/// </remarks>
public sealed class SlotControl : Control
{
    /// <summary>What the constructor leaves <see cref="SlotValue"/> at.</summary>
    public const int ConstructorValue = 9;

    /// <summary>The state behind the <c>Slot</c> extension property.</summary>
    public int SlotValue { get; set; } = ConstructorValue;

    /// <summary>The event behind the <c>Pinged</c> extension handler pair.</summary>
    public event Action<object>? Pinged;

    /// <summary>Fires the event, so a test can tell whether a node's handler is attached.</summary>
    public void Raise() => Pinged?.Invoke(this);
}

/// <summary>The extension members a node for <see cref="SlotControl"/> has to expose as its own.</summary>
public static class SlotControlExtensions
{
    extension(SlotControl control)
    {
        /// <summary>The slot, which a node restores to 5 rather than to what the constructor left.</summary>
        [VNodePropertyUnsetValue(5)]
        public int Slot
        {
            get => control.SlotValue;
            set => control.SlotValue = value;
        }
    }

    /// <summary>Attaches a handler, the way the library's extension pairs do.</summary>
    /// <param name="control">The control the handler follows.</param>
    /// <param name="handler">The handler.</param>
    /// <returns>Disposes to detach the handler.</returns>
    public static IDisposable AddPingedHandler(this SlotControl control, Action<object> handler)
    {
        control.Pinged += handler;

        return new Subscription(control, handler);
    }

    /// <summary>Detaches a handler that <see cref="AddPingedHandler"/> attached.</summary>
    /// <param name="control">The control the handler follows.</param>
    /// <param name="handler">The handler.</param>
    public static void RemovePingedHandler(this SlotControl control, Action<object> handler) =>
        control.Pinged -= handler;

    private sealed class Subscription(SlotControl control, Action<object> handler) : IDisposable
    {
        public void Dispose() => control.Pinged -= handler;
    }
}
