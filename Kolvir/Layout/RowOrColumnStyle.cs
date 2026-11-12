namespace Kolvir;

/// <summary>
/// Describes how a single row or column of a <see cref="TableLayoutPanel"/> is sized.
/// Mirrors the semantics of WinForms' <c>RowStyle</c>/<c>ColumnStyle</c>.
/// </summary>
public class RowOrColumnStyle
{
    private float _value;

    public RowOrColumnStyle()
        : this(SizeType.AutoSize, 0f)
    {
    }

    public RowOrColumnStyle(SizeType sizeType, float value)
    {
        SizeType = sizeType;
        Value = value;
    }

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

    public static RowOrColumnStyle Absolute(float pixels) => new(SizeType.Absolute, pixels);

    public static RowOrColumnStyle Percent(float percent) => new(SizeType.Percent, percent);

    public static RowOrColumnStyle AutoSize() => new(SizeType.AutoSize, 0f);
}

/// <summary>A row definition for <see cref="TableLayoutPanel"/>.</summary>
public sealed class RowStyle : RowOrColumnStyle
{
    public RowStyle()
    {
    }

    public RowStyle(SizeType sizeType, float value)
        : base(sizeType, value)
    {
    }

    public static new RowStyle Absolute(float pixels) => new(SizeType.Absolute, pixels);

    public static new RowStyle Percent(float percent) => new(SizeType.Percent, percent);

    public static new RowStyle AutoSize() => new(SizeType.AutoSize, 0f);
}

/// <summary>A column definition for <see cref="TableLayoutPanel"/>.</summary>
public sealed class ColumnStyle : RowOrColumnStyle
{
    public ColumnStyle()
    {
    }

    public ColumnStyle(SizeType sizeType, float value)
        : base(sizeType, value)
    {
    }

    public static new ColumnStyle Absolute(float pixels) => new(SizeType.Absolute, pixels);

    public static new ColumnStyle Percent(float percent) => new(SizeType.Percent, percent);

    public static new ColumnStyle AutoSize() => new(SizeType.AutoSize, 0f);
}
