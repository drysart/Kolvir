using SkiaSharp;

namespace Kolvir;

/// <summary>
/// How a control fills its background: a colour, nothing at all, or "ask my parent" - the
/// WinForms <c>BackColor</c> idea, with the "not set" state kept distinct from the transparent one.
/// </summary>
/// <remarks>
/// <para>
/// The two states that a nullable <see cref="SKColor"/> would collapse are the reason this type
/// exists. A label paints no background of its own, and it must keep doing that wherever it sits;
/// if "no background" and "no value yet" were the same null, a label inside a filled panel would
/// inherit the panel's colour and start painting a box of its own, with the label's corners and
/// opacity showing. So <see cref="None"/> is a value: it paints nothing and it stops the walk down
/// the parent chain, while <see cref="Inherit"/> - the default for the property itself - keeps
/// looking.
/// </para>
/// <para>
/// <see cref="Control.DefaultBackground"/> is where a control class says what it does when nothing
/// has been assigned to it: <see cref="None"/> for a control that draws no background, a theme role
/// for one that paints a surface. A role rather than a colour, so a palette swap still recolours a
/// live control with no rebuild.
/// </para>
/// </remarks>
public readonly struct Background : IEquatable<Background>
{
    private readonly Kind _kind;
    private readonly SKColor _color;
    private readonly ThemeColor _role;

    private Background(Kind kind, SKColor color, ThemeColor role)
    {
        _kind = kind;
        _color = color;
        _role = role;
    }

    /// <summary>Take the background the nearest ancestor sets, or this control's own default.</summary>
    public static Background Inherit => default;

    /// <summary>Paint nothing here, whatever an ancestor paints - and paint nothing for descendants either.</summary>
    public static Background None => new(Kind.None, default, default);

    /// <summary>A fixed colour. It may carry alpha, in which case it blends with what the parent painted.</summary>
    public static Background FromColor(SKColor color) => new(Kind.Color, color, default);

    /// <summary>
    /// A palette role, resolved while painting, so assigning <see cref="Theme.Palette"/> recolours
    /// this control on the next frame. This is what a control's default background is made of.
    /// </summary>
    public static Background FromThemeColor(ThemeColor role) => new(Kind.Theme, default, role);

    /// <summary>Nothing has been assigned, so the value comes from the parent or the class default.</summary>
    public bool IsInherit => _kind == Kind.Inherit;

    /// <summary>Explicitly "no background": nothing is painted, here or below.</summary>
    public bool IsNone => _kind == Kind.None;

    /// <summary>An explicitly assigned colour, fixed or a palette role.</summary>
    public bool IsColor => _kind is Kind.Color or Kind.Theme;

    /// <summary>
    /// The colour to paint, or null when this state paints nothing. A palette role is looked up
    /// now, which is what keeps a theme change a repaint rather than a rebuild.
    /// </summary>
    public SKColor? Color => _kind switch
    {
        Kind.Color => _color,
        Kind.Theme => Theme.ColorOf(_role),
        _ => null,
    };

    /// <summary>The palette role behind <see cref="Color"/>, or null for a fixed colour or no fill.</summary>
    public ThemeColor? ThemeColor => _kind == Kind.Theme ? _role : null;

    /// <summary>
    /// Compares what is painted: the kind, plus the colour or the palette role that kind carries.
    /// The fields a kind does not use are ignored, so every <see cref="Inherit"/> equals every
    /// other one.
    /// </summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>True when the two paint the same thing.</returns>
    public bool Equals(Background other) => _kind == other._kind
        && (_kind != Kind.Color || _color == other._color)
        && (_kind != Kind.Theme || _role == other._role);

    /// <summary>
    /// True when <paramref name="obj"/> is a <see cref="Background"/> that paints the same thing.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True when it is an equal <see cref="Background"/>.</returns>
    public override bool Equals(object? obj) => obj is Background other && Equals(other);

    /// <summary>A hash consistent with <see cref="Equals(Background)"/>.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine((int)_kind, _color.GetHashCode(), (int)_role);

    /// <summary>True when the two values paint the same thing.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>The result of <see cref="Equals(Background)"/>.</returns>
    public static bool operator ==(Background left, Background right) => left.Equals(right);

    /// <summary>True when the two values do not paint the same thing.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>The negation of <see cref="Equals(Background)"/>.</returns>
    public static bool operator !=(Background left, Background right) => !left.Equals(right);

    /// <summary>
    /// Describes the value for diagnostics: <c>None</c>, <c>Inherit</c>, a colour, or the name of
    /// the palette role.
    /// </summary>
    /// <returns>A short description of what this value paints.</returns>
    public override string ToString() => _kind switch
    {
        Kind.None => "None",
        Kind.Color => _color.ToString(),
        Kind.Theme => _role.ToString(),
        _ => "Inherit",
    };

    private enum Kind : byte
    {
        Inherit,
        None,
        Color,
        Theme,
    }
}
