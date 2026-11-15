namespace Kolvir;

/// <summary>
/// A point inside a control's selectable content: the control plus an offset in its selection
/// units. The offset is fractional on purpose - a point inside a whole control maps to the middle
/// of its single unit (0.5), which is what makes a whole control selectable from either drag
/// direction.
/// </summary>
public readonly struct SelectionPosition : IEquatable<SelectionPosition>
{
    /// <summary>Creates a position in <paramref name="control"/>'s content.</summary>
    /// <param name="control">The control the position belongs to.</param>
    /// <param name="offset">
    /// The offset into the control's <see cref="Control.SelectionUnitCount"/> units. Negative
    /// values are clamped to zero.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="control"/> is null.</exception>
    public SelectionPosition(Control control, float offset)
    {
        ArgumentNullException.ThrowIfNull(control);
        Control = control;
        Offset = MathF.Max(0f, offset);
    }

    /// <summary>The control the position belongs to; null for the default value.</summary>
    public Control? Control { get; }

    /// <summary>Offset into the control's <see cref="Control.SelectionUnitCount"/> units.</summary>
    public float Offset { get; }

    /// <summary>True when the position names no control, which is what the default value is.</summary>
    public bool IsEmpty => Control is null;

    /// <summary>True when both positions name the same control at the same offset.</summary>
    /// <param name="other">The position to compare with.</param>
    /// <returns>True when the two are equal.</returns>
    public bool Equals(SelectionPosition other) =>
        ReferenceEquals(Control, other.Control) && Offset == other.Offset;

    /// <summary>True when <paramref name="obj"/> is a position equal to this one.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True when the two are equal.</returns>
    public override bool Equals(object? obj) => obj is SelectionPosition other && Equals(other);

    /// <summary>A hash code over the control reference and the offset.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Control, Offset);

    /// <summary>True when the two positions are equal.</summary>
    /// <param name="left">The left position.</param>
    /// <param name="right">The right position.</param>
    /// <returns>True when the two are equal.</returns>
    public static bool operator ==(SelectionPosition left, SelectionPosition right) => left.Equals(right);

    /// <summary>True when the two positions differ.</summary>
    /// <param name="left">The left position.</param>
    /// <param name="right">The right position.</param>
    /// <returns>True when the two differ.</returns>
    public static bool operator !=(SelectionPosition left, SelectionPosition right) => !left.Equals(right);

    /// <summary>The control's name and the offset, or <c>(none)</c> for an empty position.</summary>
    /// <returns>A string for diagnostics and debugging.</returns>
    public override string ToString() => IsEmpty ? "(none)" : $"{Control!.Name}@{Offset:0.##}";
}
