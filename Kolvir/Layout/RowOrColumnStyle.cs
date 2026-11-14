namespace Kolvir;

/// <summary>
/// Describes how a single row or column of a <see cref="TableLayoutPanel"/> is sized.
/// Mirrors the semantics of WinForms' <c>RowStyle</c>/<c>ColumnStyle</c>.
/// </summary>
public class RowOrColumnStyle
{
    private float _value;

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
    public SizeType SizeType { get; set; }

    /// <summary>
    /// Absolute height/width in pixels when <see cref="SizeType"/> is
    /// <see cref="SizeType.Absolute"/>, or a percentage (0-100) when it is
    /// <see cref="SizeType.Percent"/>. Ignored for <see cref="SizeType.AutoSize"/>.
    /// </summary>
    public float Value
    {
        get => _value;
        set => _value = float.IsNaN(value) ? 0f : MathF.Max(0f, value);
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
