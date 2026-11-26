namespace Kolvir;

/// <summary>
/// Describes how a single row or column of a <see cref="TableLayoutPanel"/> is sized.
/// Mirrors the semantics of WinForms' <c>RowStyle</c>/<c>ColumnStyle</c>.
/// </summary>
/// <remarks>
/// <para>
/// A style is immutable. Its <see cref="SizeType"/> and value are <c>init</c>-only, so it is built
/// in a constructor or an object initialiser and is fixed from then on. Changing how a row is sized
/// means assigning a different style, which is what makes the assignment the one thing that can put
/// the change in front of the table: the collections it hands out lay the table out when their
/// contents change, and an in-place edit could not.
/// </para>
/// <para>
/// Immutability is also what makes value equality sound here. Two styles of the same kind are equal
/// when their <see cref="SizeType"/> and value agree, and a hash cannot disagree with an
/// <c>Equals</c> computed before a mutation, because there is no mutation. A row style never equals
/// a column style with the same numbers: one is a height and the other a width, and swapping them
/// means something else.
/// </para>
/// <para>
/// This equality is the one the table uses to decide that a newly assigned sequence of styles says
/// the same thing as the current one, so a render that rebuilds its styles every time is free.
/// </para>
/// </remarks>
public class RowOrColumnStyle : IEquatable<RowOrColumnStyle>
{
    private readonly float _value;

    /// <summary>
    /// Creates a style that sizes to content, the same as <see cref="AutoSize()"/>.
    /// </summary>
    public RowOrColumnStyle()
        : this(SizeType.AutoSize, 0f)
    {
    }

    /// <summary>Creates a style that sizes its row or column with <paramref name="sizeType"/>.</summary>
    /// <param name="sizeType">How the size is interpreted.</param>
    /// <param name="value">
    /// The pixels for <see cref="SizeType.Absolute"/>, or the percentage for
    /// <see cref="SizeType.Percent"/>. Ignored for <see cref="SizeType.AutoSize"/>.
    /// </param>
    public RowOrColumnStyle(SizeType sizeType, float value)
    {
        SizeType = sizeType;
        Value = value;
    }

    /// <summary>How the row or column decides its size.</summary>
    public SizeType SizeType { get; init; }

    /// <summary>
    /// Absolute height/width in pixels when <see cref="SizeType"/> is
    /// <see cref="SizeType.Absolute"/>, or a percentage (0-100) when it is
    /// <see cref="SizeType.Percent"/>. Ignored for <see cref="SizeType.AutoSize"/>.
    /// </summary>
    public float Value
    {
        get => _value;
        init => _value = float.IsNaN(value) ? 0f : MathF.Max(0f, value);
    }

    /// <summary>Creates a style of a fixed size in pixels.</summary>
    /// <param name="pixels">The size, in pixels. Negative values are clamped to zero.</param>
    /// <returns>The new style.</returns>
    public static RowOrColumnStyle Absolute(float pixels) => new(SizeType.Absolute, pixels);

    /// <summary>Creates a style sized as a percentage of the table's available room.</summary>
    /// <param name="percent">The share of the room to take, from 0 to 100.</param>
    /// <returns>The new style.</returns>
    public static RowOrColumnStyle Percent(float percent) => new(SizeType.Percent, percent);

    /// <summary>Creates a style that takes the size its content asks for.</summary>
    /// <returns>The new style.</returns>
    public static RowOrColumnStyle AutoSize() => new(SizeType.AutoSize, 0f);

    /// <summary>
    /// Answers whether another style sizes its row or column exactly the same way.
    /// </summary>
    /// <param name="other">The style to compare with.</param>
    /// <returns>
    /// True when both are the same concrete kind and agree on <see cref="SizeType"/> and
    /// <see cref="Value"/>. A row style and a column style are never equal.
    /// </returns>
    public bool Equals(RowOrColumnStyle? other) =>
        other is not null
            && other.GetType() == GetType()
            && other.SizeType == SizeType
            && other._value == _value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as RowOrColumnStyle);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(GetType(), SizeType, _value);

    /// <summary>Answers whether two styles size their row or column the same way.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when they are equal, or both are null.</returns>
    public static bool operator ==(RowOrColumnStyle? left, RowOrColumnStyle? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Answers whether two styles size their row or column differently.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>True when they are not equal.</returns>
    public static bool operator !=(RowOrColumnStyle? left, RowOrColumnStyle? right) => !(left == right);
}

/// <summary>A row definition for <see cref="TableLayoutPanel"/>.</summary>
public sealed class RowStyle : RowOrColumnStyle
{
    /// <summary>Creates a row that sizes to content, the same as <see cref="AutoSize()"/>.</summary>
    public RowStyle()
    {
    }

    /// <summary>Creates a row that sizes itself with <paramref name="sizeType"/>.</summary>
    /// <param name="sizeType">How the size is interpreted.</param>
    /// <param name="value">
    /// The pixels for <see cref="SizeType.Absolute"/>, or the percentage for
    /// <see cref="SizeType.Percent"/>. Ignored for <see cref="SizeType.AutoSize"/>.
    /// </param>
    public RowStyle(SizeType sizeType, float value)
        : base(sizeType, value)
    {
    }

    /// <summary>Creates a row of a fixed height in pixels.</summary>
    /// <param name="pixels">The height, in pixels. Negative values are clamped to zero.</param>
    /// <returns>The new row style.</returns>
    public static new RowStyle Absolute(float pixels) => new(SizeType.Absolute, pixels);

    /// <summary>Creates a row sized as a percentage of the table's available height.</summary>
    /// <param name="percent">The share of the height to take, from 0 to 100.</param>
    /// <returns>The new row style.</returns>
    public static new RowStyle Percent(float percent) => new(SizeType.Percent, percent);

    /// <summary>Creates a row that takes the height its content asks for.</summary>
    /// <returns>The new row style.</returns>
    public static new RowStyle AutoSize() => new(SizeType.AutoSize, 0f);
}

/// <summary>A column definition for <see cref="TableLayoutPanel"/>.</summary>
public sealed class ColumnStyle : RowOrColumnStyle
{
    /// <summary>Creates a column that sizes to content, the same as <see cref="AutoSize()"/>.</summary>
    public ColumnStyle()
    {
    }

    /// <summary>Creates a column that sizes itself with <paramref name="sizeType"/>.</summary>
    /// <param name="sizeType">How the size is interpreted.</param>
    /// <param name="value">
    /// The pixels for <see cref="SizeType.Absolute"/>, or the percentage for
    /// <see cref="SizeType.Percent"/>. Ignored for <see cref="SizeType.AutoSize"/>.
    /// </param>
    public ColumnStyle(SizeType sizeType, float value)
        : base(sizeType, value)
    {
    }

    /// <summary>Creates a column of a fixed width in pixels.</summary>
    /// <param name="pixels">The width, in pixels. Negative values are clamped to zero.</param>
    /// <returns>The new column style.</returns>
    public static new ColumnStyle Absolute(float pixels) => new(SizeType.Absolute, pixels);

    /// <summary>Creates a column sized as a percentage of the table's available width.</summary>
    /// <param name="percent">The share of the width to take, from 0 to 100.</param>
    /// <returns>The new column style.</returns>
    public static new ColumnStyle Percent(float percent) => new(SizeType.Percent, percent);

    /// <summary>Creates a column that takes the width its content asks for.</summary>
    /// <returns>The new column style.</returns>
    public static new ColumnStyle AutoSize() => new(SizeType.AutoSize, 0f);
}
