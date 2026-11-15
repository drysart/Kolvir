using System.Globalization;

namespace Kolvir;

/// <summary>What a <see cref="ControlLength"/> measures against.</summary>
public enum ControlLengthKind
{
    /// <summary>A number of logical pixels, the way every size and position was written before.</summary>
    Absolute = 0,

    /// <summary>A share of the parent's content area along the same axis.</summary>
    Percent = 1,

    /// <summary>The size the control asks for itself - its measured size. A position treats it as the start.</summary>
    Auto = 2,

    /// <summary>
    /// Text that could not be parsed. It behaves exactly as <see cref="Auto"/> does, and keeps a kind
    /// of its own so a value that never was a number can still be told apart from one that was.
    /// </summary>
    Invalid = 3,
}

/// <summary>
/// A width, a height, or a distance from the parent's edge: either a number of pixels, a percentage
/// of the parent's content area, or left to the control itself.
/// </summary>
/// <remarks>
/// <para>
/// A number is what it always was - <c>control.Width = 120</c> is 120 logical pixels - and the
/// percentages and the "auto" that this type adds are how a control asks for a share of the room it
/// is given rather than a fixed amount of it:
/// </para>
/// <code>
/// control.Width = 120;      // 120 px
/// control.Width = "120px";  // the same, spelled out
/// control.Width = "50%";    // half of the parent's content width
/// control.Width = "auto";   // the width the control measures at
/// control.Width = "120q";   // Invalid: behaves as auto, and says so when asked
/// </code>
/// <para>
/// A string is parsed once, when it is assigned, so an invalid value is a stored state rather than
/// an exception: <see cref="Kind"/> reports <see cref="ControlLengthKind.Invalid"/> and the
/// control lays out as if it had been given <c>auto</c>. Reading the value back as a number gives
/// the number for an absolute value and <c>NaN</c> for everything else, so arithmetic on a
/// percentage cannot silently pass a share off as a pixel count.
/// </para>
/// </remarks>
public readonly record struct ControlLength
{
    /// <summary>Creates a value of the given kind. Prefer the factories and the conversions.</summary>
    /// <param name="value">The number the kind applies to: pixels, or a percentage from 0 to 100.</param>
    /// <param name="kind">What the number means.</param>
    public ControlLength(float value, ControlLengthKind kind)
    {
        Value = value;
        Kind = kind;
    }

    /// <summary>The number the <see cref="Kind"/> applies to.</summary>
    public float Value { get; }

    /// <summary>What the value measures against.</summary>
    public ControlLengthKind Kind { get; }

    /// <summary>The value that parse failures produce. Behaves as <see cref="Auto"/>.</summary>
    public static ControlLength Invalid { get; } = new(0f, ControlLengthKind.Invalid);

    /// <summary>The value that asks the control to size or place itself.</summary>
    public static ControlLength Auto { get; } = new(0f, ControlLengthKind.Auto);

    /// <summary>True unless the value came from text that could not be parsed.</summary>
    public bool IsValid => Kind != ControlLengthKind.Invalid;

    /// <summary>True for a number of logical pixels.</summary>
    public bool IsAbsolute => Kind == ControlLengthKind.Absolute;

    /// <summary>True for a share of the parent's content area.</summary>
    public bool IsPercent => Kind == ControlLengthKind.Percent;

    /// <summary>True when the control decides, which is what <c>auto</c> asks for and an invalid value means.</summary>
    public bool IsAuto => Kind is ControlLengthKind.Auto or ControlLengthKind.Invalid;

    /// <summary>A number of logical pixels.</summary>
    /// <param name="value">The number of pixels.</param>
    public static ControlLength Absolute(int value) => new(value, ControlLengthKind.Absolute);

    /// <summary>A number of logical pixels.</summary>
    /// <param name="value">The number of pixels.</param>
    public static ControlLength Absolute(float value) => new(value, ControlLengthKind.Absolute);

    /// <summary>A number of logical pixels.</summary>
    /// <param name="value">The number of pixels.</param>
    public static ControlLength Absolute(double value) => new((float)value, ControlLengthKind.Absolute);

    /// <summary>A share of the parent's content area, 0 to 100.</summary>
    /// <param name="percent">The share.</param>
    public static ControlLength Percent(int percent) => new(percent, ControlLengthKind.Percent);

    /// <summary>A share of the parent's content area, 0 to 100.</summary>
    /// <param name="percent">The share.</param>
    public static ControlLength Percent(float percent) => new(percent, ControlLengthKind.Percent);

    /// <summary>A share of the parent's content area, 0 to 100.</summary>
    /// <param name="percent">The share.</param>
    public static ControlLength Percent(double percent) => new((float)percent, ControlLengthKind.Percent);

    /// <summary>
    /// Reads <c>120</c>, <c>"120px"</c>, <c>"50%"</c>, <c>"auto"</c> or <c>false</c>, returning
    /// <see cref="Invalid"/> for anything else rather than throwing.
    /// </summary>
    /// <param name="text">The text to read. Null or empty is invalid.</param>
    /// <remarks>
    /// Parsing is culture-invariant, so <c>"12.5%"</c> means twelve and a half percent everywhere.
    /// </remarks>
    public static ControlLength Parse(string? text) => TryParse(text, out var value) ? value : Invalid;

    /// <summary>Reads a value, reporting whether the text could be understood.</summary>
    /// <param name="text">The text to read.</param>
    /// <param name="value">The value read, or <see cref="Invalid"/>.</param>
    public static bool TryParse(string? text, out ControlLength value)
    {
        value = Invalid;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();

        if (trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            value = Auto;
            return true;
        }

        var kind = ControlLengthKind.Absolute;
        var number = trimmed;

        if (trimmed.EndsWith('%'))
        {
            kind = ControlLengthKind.Percent;
            number = trimmed[..^1];
        }
        else if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            number = trimmed[..^2];
        }

        if (!float.TryParse(
                number.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            !float.IsFinite(parsed))
        {
            return false;
        }

        value = new ControlLength(parsed, kind);
        return true;
    }

    /// <summary>
    /// True when working out what this value means needs the control's own measurement:
    /// <c>auto</c>, and an invalid value, which behaves as one.
    /// </summary>
    /// <remarks>
    /// A number of pixels or a percentage is decided by the parent alone. Measuring to answer those
    /// is wasted work, and on a control that lays text out it is the expensive kind of wasted work,
    /// so the parents check this before asking.
    /// </remarks>
    public bool NeedsMeasure => Kind is ControlLengthKind.Auto or ControlLengthKind.Invalid;

    /// <summary>
    /// The number of pixels this value means when the parent gives <paramref name="basis"/> and the
    /// control measures at <paramref name="auto"/>.
    /// </summary>
    /// <param name="basis">What a percentage is a share of: the parent's content area along this axis.</param>
    /// <param name="auto">What the control measures at, which is what <c>auto</c> and an invalid value mean.</param>
    public float Resolve(float basis, float auto) => Kind switch
    {
        ControlLengthKind.Absolute => Value,
        ControlLengthKind.Percent => basis * Value / 100f,
        _ => auto,
    };

    /// <summary>Writes the value the way it would be parsed: <c>120px</c>, <c>50%</c>, <c>auto</c>, <c>invalid</c>.</summary>
    public override string ToString() => Kind switch
    {
        ControlLengthKind.Absolute => Value.ToString(CultureInfo.InvariantCulture) + "px",
        ControlLengthKind.Percent => Value.ToString(CultureInfo.InvariantCulture) + "%",
        ControlLengthKind.Auto => "auto",
        _ => "invalid",
    };

    /// <summary>A number of pixels.</summary>
    /// <param name="value">The number of pixels.</param>
    public static implicit operator ControlLength(int value) => Absolute(value);

    /// <summary>A number of pixels.</summary>
    /// <param name="value">The number of pixels.</param>
    public static implicit operator ControlLength(float value) => Absolute(value);

    /// <summary>A number of pixels.</summary>
    /// <param name="value">The number of pixels.</param>
    public static implicit operator ControlLength(double value) => Absolute(value);

    /// <summary>Text, read with <see cref="Parse"/>: an unreadable value becomes <see cref="Invalid"/>.</summary>
    /// <param name="text">The text to read.</param>
    public static implicit operator ControlLength(string? text) => Parse(text);

    /// <summary>
    /// The number of pixels, for an absolute value only: anything else is <c>NaN</c>, so a share can
    /// never pass for a pixel count by accident.
    /// </summary>
    /// <param name="value">The value to read.</param>
    public static implicit operator float(ControlLength value) =>
        value.Kind == ControlLengthKind.Absolute ? value.Value : float.NaN;
}
