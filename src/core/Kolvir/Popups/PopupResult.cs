namespace Kolvir;

/// <summary>Why a popup closed.</summary>
/// <remarks>
/// Every dismissal path reports one of these, and only <see cref="Closed"/> can carry a value: a
/// value on any of the other four would describe something the user never chose.
/// </remarks>
public enum PopupCloseReason
{
    /// <summary>
    /// Application code closed the popup, which is what <c>ClosePopup</c> does. The only reason that
    /// can carry a value.
    /// </summary>
    Closed = 0,

    /// <summary>
    /// The user pressed the pointer outside the popup. The press belongs to the popup, so it is
    /// not delivered to whatever it landed on.
    /// </summary>
    OutsideClick = 1,

    /// <summary>The user pressed Escape while the popup had the keyboard.</summary>
    Escape = 2,

    /// <summary>The window the popup belongs to closed, or the control it was anchored to went away.</summary>
    OwnerClosed = 3,

    /// <summary>
    /// A second popup was opened on the same owner surface, so this one was taken down to make room
    /// for it.
    /// </summary>
    Cancelled = 4,
}

/// <summary>How a popup ended, which is what <c>ShowPopupAsync</c> completes with.</summary>
/// <remarks>
/// <para>
/// A result is written once, by whichever dismissal path happens first, and the value is only ever
/// set by application code: <c>ClosePopup(result)</c> reports <see cref="PopupCloseReason.Closed"/>
/// with whatever the caller put in it, and every other reason reports none. A dismissal therefore
/// never invents an answer the caller did not choose.
/// </para>
/// <para>
/// Read the value with <see cref="TryGetValue{T}"/> rather than by casting
/// <see cref="Value"/>: a dismissal that carries nothing is the common case, not an error.
/// </para>
/// </remarks>
public sealed class PopupResult
{
    private PopupResult(PopupCloseReason reason, object? value)
    {
        Reason = reason;
        Value = value;
    }

    /// <summary>Why the popup closed.</summary>
    public PopupCloseReason Reason { get; }

    /// <summary>
    /// What application code passed to <c>ClosePopup</c>, or null when it passed nothing or the
    /// popup was dismissed rather than closed.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Reads <see cref="Value"/> as <typeparamref name="T"/>, returning false when the popup carried
    /// nothing or carried something else.
    /// </summary>
    /// <param name="value">The value, when it is a <typeparamref name="T"/>; otherwise the default.</param>
    /// <typeparam name="T">The type the caller expects.</typeparam>
    public bool TryGetValue<T>(out T value)
    {
        if (Value is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>
    /// The result a popup closed by application code reports: reason <see cref="PopupCloseReason.Closed"/>
    /// and, optionally, whatever the caller wants the awaiting code to have.
    /// </summary>
    /// <param name="value">What the popup is closing with, or null for none.</param>
    public static PopupResult Closed(object? value = null) => new(PopupCloseReason.Closed, value);

    /// <summary>A result for a dismissal, which never carries a value.</summary>
    internal static PopupResult Dismissed(PopupCloseReason reason) => new(reason, null);
}
