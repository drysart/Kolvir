using SkiaSharp;

namespace XPlatUI;

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
/// <see cref="DefaultBackground"/> is where a control class says what it does when nothing has been
/// assigned to it: <see cref="None"/> for a control that draws no background, a theme role for one
/// that paints a surface. A role rather than a colour, so a palette swap still recolours a live
/// control with no rebuild.
/// </para>
/// </remarks>
public readonly struct XPlatBackground : IEquatable<XPlatBackground>
{
    private readonly Kind _kind;
    private readonly SKColor _color;
    private readonly XPlatThemeColor _role;

    private XPlatBackground(Kind kind, SKColor color, XPlatThemeColor role)
    {
        _kind = kind;
        _color = color;
        _role = role;
    }

    /// <summary>Take the background the nearest ancestor sets, or this control's own default.</summary>
    public static XPlatBackground Inherit => default;

    /// <summary>Paint nothing here, whatever an ancestor paints - and paint nothing for descendants either.</summary>
    public static XPlatBackground None => new(Kind.None, default, default);

    /// <summary>A fixed colour. It may carry alpha, in which case it blends with what the parent painted.</summary>
    public static XPlatBackground FromColor(SKColor color) => new(Kind.Color, color, default);

    /// <summary>
    /// A palette role, resolved while painting, so assigning <see cref="XPlatTheme.Palette"/> recolours
    /// this control on the next frame. This is what a control's default background is made of.
    /// </summary>
    public static XPlatBackground FromThemeColor(XPlatThemeColor role) => new(Kind.Theme, default, role);

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
        Kind.Theme => XPlatTheme.ColorOf(_role),
        _ => null,
    };

    /// <summary>The palette role behind <see cref="Color"/>, or null for a fixed colour or no fill.</summary>
    public XPlatThemeColor? ThemeColor => _kind == Kind.Theme ? _role : null;

    public bool Equals(XPlatBackground other) => _kind == other._kind
        && (_kind != Kind.Color || _color == other._color)
        && (_kind != Kind.Theme || _role == other._role);

    public override bool Equals(object? obj) => obj is XPlatBackground other && Equals(other);

    public override int GetHashCode() => HashCode.Combine((int)_kind, _color.GetHashCode(), (int)_role);

    public static bool operator ==(XPlatBackground left, XPlatBackground right) => left.Equals(right);

    public static bool operator !=(XPlatBackground left, XPlatBackground right) => !left.Equals(right);

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
