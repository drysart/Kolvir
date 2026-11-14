namespace Kolvir;

/// <summary>A contiguous slice of a control's selectable content.</summary>
public readonly struct SelectionRange : IEquatable<SelectionRange>
{
    /// <summary>Creates a range of <paramref name="length"/> units starting at <paramref name="start"/>.</summary>
    /// <param name="control">The control the range belongs to.</param>
    /// <param name="start">
    /// The first selected unit, relative to the start of the control's content. Negative values are
    /// clamped to zero.
    /// </param>
    /// <param name="length">
    /// The number of selected units. Negative values are clamped to zero, which makes the range empty.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="control"/> is null.</exception>
    public SelectionRange(Control control, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(control);
        Control = control;
        Start = Math.Max(0, start);
        Length = Math.Max(0, length);
    }

    /// <summary>The control the range belongs to; null for the default value.</summary>
    public Control? Control { get; }

    /// <summary>First selected unit, relative to the start of the control's content.</summary>
    public int Start { get; }

    /// <summary>Number of selected units.</summary>
    public int Length { get; }

    /// <summary>One past the last selected unit.</summary>
    public int End => Start + Length;

    /// <summary>True when the range selects nothing: no control, or a length of zero.</summary>
    public bool IsEmpty => Control is null || Length <= 0;

    /// <summary>True when both ranges name the same control and the same units.</summary>
    /// <param name="other">The range to compare with.</param>
    /// <returns>True when the two are equal.</returns>
    public bool Equals(SelectionRange other) =>
        ReferenceEquals(Control, other.Control) && Start == other.Start && Length == other.Length;

    /// <summary>True when <paramref name="obj"/> is a range equal to this one.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True when the two are equal.</returns>
    public override bool Equals(object? obj) => obj is SelectionRange other && Equals(other);

    /// <summary>A hash code over the control reference, the start and the length.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Control, Start, Length);

    /// <summary>True when the two ranges are equal.</summary>
    /// <param name="left">The left range.</param>
    /// <param name="right">The right range.</param>
    /// <returns>True when the two are equal.</returns>
    public static bool operator ==(SelectionRange left, SelectionRange right) => left.Equals(right);

    /// <summary>True when the two ranges differ.</summary>
    /// <param name="left">The left range.</param>
    /// <param name="right">The right range.</param>
    /// <returns>True when the two differ.</returns>
    public static bool operator !=(SelectionRange left, SelectionRange right) => !left.Equals(right);

    /// <summary>The control's name and the half-open range, or <c>(none)</c> for an empty range.</summary>
    /// <returns>A string for diagnostics and debugging.</returns>
    public override string ToString() => IsEmpty ? "(none)" : $"{Control!.Name}[{Start}..{End})";
}
