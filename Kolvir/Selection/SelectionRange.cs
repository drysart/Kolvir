namespace Kolvir;

/// <summary>A contiguous slice of a control's selectable content.</summary>
public readonly struct SelectionRange : IEquatable<SelectionRange>
{
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

    public bool IsEmpty => Control is null || Length <= 0;

    public bool Equals(SelectionRange other) =>
        ReferenceEquals(Control, other.Control) && Start == other.Start && Length == other.Length;

    public override bool Equals(object? obj) => obj is SelectionRange other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Control, Start, Length);

    public static bool operator ==(SelectionRange left, SelectionRange right) => left.Equals(right);

    public static bool operator !=(SelectionRange left, SelectionRange right) => !left.Equals(right);

    public override string ToString() => IsEmpty ? "(none)" : $"{Control!.Name}[{Start}..{End})";
}
