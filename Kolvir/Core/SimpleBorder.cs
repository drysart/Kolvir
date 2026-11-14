using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A straight-sided frame of one colour and one thickness, with optionally rounded corners, which is
/// what a control's border was before borders became an object.
/// </summary>
/// <remarks>
/// <para>
/// Immutable, so a control class can keep one in a <c>private static readonly</c> field and share it
/// with every instance. Build a variation with the <c>With...</c> methods, which return a new instance:
/// <c>SimpleBorder.Thin.WithCornerRadius(6f)</c>.
/// </para>
/// <para>
/// <b>State colours.</b> The five <c>With...Color</c> methods give the frame a colour per
/// <see cref="ControlState"/>, resolved while painting, so the colour follows the palette and the
/// control's state with no rebuild and no reassignment. When several apply at once they are taken in
/// this order: <see cref="ControlState.Disabled"/>, <see cref="ControlState.Pressed"/>,
/// <see cref="ControlState.Active"/>, <see cref="ControlState.Focused"/>,
/// <see cref="ControlState.Hovered"/>, then the resting colour - the order the built-in controls
/// already implement by hand.
/// </para>
/// </remarks>
public sealed class SimpleBorder : Border
{
    /// <summary>The frame a control class gives itself by default: one pixel, the border palette role,
    /// square corners, no state colours.</summary>
    public static SimpleBorder Thin { get; } = new(1f, 0f, null, null, ThemeColor.Border, null, null, null, null, null);

    private readonly float _width;
    private readonly float _cornerRadius;
    private readonly Thickness? _insets;
    private readonly SKColor? _color;
    private readonly ThemeColor? _role;
    private readonly ThemeColor? _hoverRole;
    private readonly ThemeColor? _focusRole;
    private readonly ThemeColor? _activeRole;
    private readonly ThemeColor? _disabledRole;
    private readonly ThemeColor? _pressedRole;

    private SimpleBorder(
        float width,
        float cornerRadius,
        Thickness? insets,
        SKColor? color,
        ThemeColor? role,
        ThemeColor? hoverRole,
        ThemeColor? focusRole,
        ThemeColor? activeRole,
        ThemeColor? disabledRole,
        ThemeColor? pressedRole)
    {
        _width = width;
        _cornerRadius = cornerRadius;
        _insets = insets;
        _color = color;
        _role = role;
        _hoverRole = hoverRole;
        _focusRole = focusRole;
        _activeRole = activeRole;
        _disabledRole = disabledRole;
        _pressedRole = pressedRole;
    }

    /// <summary>A frame of <paramref name="width"/> logical pixels, in the border palette role.</summary>
    /// <param name="width">The thickness, in logical pixels. Zero paints nothing.</param>
    /// <param name="cornerRadius">The radius of the corners, in logical pixels.</param>
    public SimpleBorder(float width, float cornerRadius = 0f)
        : this(width, cornerRadius, null, null, ThemeColor.Border, null, null, null, null, null)
    {
    }

    /// <summary>
    /// A shape with no frame: rounds the fill and clips the control by
    /// <paramref name="cornerRadius"/>, painting nothing and taking no room.
    /// </summary>
    /// <param name="cornerRadius">The radius of the corners, in logical pixels.</param>
    /// <returns>A new border.</returns>
    public static SimpleBorder Rounded(float cornerRadius) => new(0f, cornerRadius);

    /// <summary>Thickness of the frame in logical pixels, drawn inside the control's bounds.</summary>
    public float Width => _width;

    /// <summary>
    /// Radius of the rounded corners, which also clips the control and rounds its background fill.
    /// </summary>
    public override float CornerRadius => _cornerRadius;

    /// <summary>The colour with no state colour matching.</summary>
    public SKColor Color => _color ?? Theme.ColorOf(_role ?? ThemeColor.Border);

    /// <inheritdoc />
    public override Thickness Insets(Control owner) => _insets ?? new Thickness(_width);

    /// <inheritdoc />
    public override bool NarrowsClip => _cornerRadius > 0f;

    /// <inheritdoc />
    public override bool IsRectangular => _cornerRadius <= 0f;

    /// <inheritdoc />
    public override void ClipTo(SKCanvas canvas, SKRect bounds)
    {
        if (_cornerRadius > 0f)
        {
            canvas.ClipRoundRect(
                new SKRoundRect(bounds, _cornerRadius), SKClipOperation.Intersect, antialias: true);
        }
    }

    /// <summary>This frame with a different thickness.</summary>
    /// <param name="width">The thickness, in logical pixels.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithWidth(float width) => Copy(width: width);

    /// <summary>This frame with a different corner radius.</summary>
    /// <param name="cornerRadius">The radius, in logical pixels.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithCornerRadius(float cornerRadius) => Copy(cornerRadius: cornerRadius);

    /// <summary>
    /// This frame taking a different amount of room than the stroke is thick, edge by edge.
    /// </summary>
    /// <param name="insets">The non-client area, in logical pixels.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    /// <remarks>
    /// For a frame whose band is asymmetric - a header that sits on the top edge, whose caption needs
    /// that 1-pixel inset to stay where it was. Taking a different amount of room also keeps the
    /// frame from painting a stroke of its own unless <see cref="Width"/> is non-zero, so a border
    /// that only insets is expressed as a zero-width one.
    /// </remarks>
    public SimpleBorder WithInsets(Thickness insets) => Copy(insets: insets);

    /// <summary>This frame with a fixed resting colour.</summary>
    /// <param name="color">The colour, alpha included.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithColor(SKColor color) => Copy(color: color, clearRole: true);

    /// <summary>This frame with a resting colour taken from the palette.</summary>
    /// <param name="role">The palette entry, resolved while painting.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithThemeColor(ThemeColor role) => Copy(role: role, clearColor: true);

    /// <summary>This frame with the colour it takes while the pointer is over the control.</summary>
    /// <param name="role">The palette entry, resolved while painting.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithHoverColor(ThemeColor role) => Copy(hoverRole: role);

    /// <summary>This frame with the colour it takes while the control has the keyboard focus.</summary>
    /// <param name="role">The palette entry, resolved while painting.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithFocusColor(ThemeColor role) => Copy(focusRole: role);

    /// <summary>This frame with the colour it takes while the control owns a showing overlay.</summary>
    /// <param name="role">The palette entry, resolved while painting.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    /// <remarks>
    /// The state a combo box, split button or date picker is in while its drop-down is up. It is
    /// separate from the focus colour because the pointer can move into the drop-down and the
    /// keyboard focus can follow it, and the frame should read as open either way.
    /// </remarks>
    public SimpleBorder WithActiveColor(ThemeColor role) => Copy(activeRole: role);

    /// <summary>This frame with the colour it takes while the control is disabled.</summary>
    /// <param name="role">The palette entry, resolved while painting.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithDisabledColor(ThemeColor role) => Copy(disabledRole: role);

    /// <summary>This frame with the colour it takes while a mouse button is held on the control.</summary>
    /// <param name="role">The palette entry, resolved while painting.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithPressedColor(ThemeColor role) => Copy(pressedRole: role);

    /// <summary>
    /// The colour this frame takes for <paramref name="state"/>: the state colours in the documented
    /// order, and the resting colour when none of them applies.
    /// </summary>
    /// <param name="state">The state of the control being painted.</param>
    /// <returns>The colour, alpha included.</returns>
    public SKColor ColorFor(ControlState state) =>
        (state & ControlState.Disabled) != 0 && _disabledRole is { } disabled ? Theme.ColorOf(disabled) :
        (state & ControlState.Pressed) != 0 && _pressedRole is { } pressed ? Theme.ColorOf(pressed) :
        (state & ControlState.Active) != 0 && _activeRole is { } active ? Theme.ColorOf(active) :
        (state & ControlState.Focused) != 0 && _focusRole is { } focused ? Theme.ColorOf(focused) :
        (state & ControlState.Hovered) != 0 && _hoverRole is { } hovered ? Theme.ColorOf(hovered) :
        Color;

    /// <inheritdoc />
    public override void Paint(SKCanvas canvas, SKRect bounds, Control owner)
    {
        if (_width > 0f)
        {
            Painter.DrawBorder(canvas, bounds, ColorFor(owner.State), _width, _cornerRadius);
        }
    }

    /// <summary>A copy with the given members replaced; a null argument keeps the current value.</summary>
    private SimpleBorder Copy(
        float? width = null,
        float? cornerRadius = null,
        Thickness? insets = null,
        SKColor? color = null,
        bool clearColor = false,
        ThemeColor? role = null,
        bool clearRole = false,
        ThemeColor? hoverRole = null,
        ThemeColor? focusRole = null,
        ThemeColor? activeRole = null,
        ThemeColor? disabledRole = null,
        ThemeColor? pressedRole = null) =>
        new(
            width ?? _width,
            cornerRadius ?? _cornerRadius,
            insets ?? _insets,
            color ?? (clearColor ? null : _color),
            role ?? (clearRole ? null : _role),
            hoverRole ?? _hoverRole,
            focusRole ?? _focusRole,
            activeRole ?? _activeRole,
            disabledRole ?? _disabledRole,
            pressedRole ?? _pressedRole);
}
