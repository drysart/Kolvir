using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A straight-sided frame whose four edges and four corners can each be set separately: what a
/// control's border was before borders became an object, generalised.
/// </summary>
/// <remarks>
/// <para>
/// Immutable, so a control class keeps one in a <c>private static readonly</c> field and shares it with
/// every instance. A variation is a new instance: <c>SimpleBorder.Thin.WithCornerRadius(6f)</c>,
/// <c>SimpleBorder.Thin.WithWidths(new Thickness(4f, 1f, 1f, 1f))</c>,
/// <c>SimpleBorder.Thin.WithThemeColor(BorderSide.Left, ThemeColor.Accent)</c>.
/// </para>
/// <para>
/// <b>Edges.</b> <see cref="Widths"/> is one thickness per side and each side has its own resting
/// colour, so an accent edge, or a frame that is only partly drawn, is one border rather than a border
/// plus a decoration. Where two edges of different widths meet, the corner is mitred through the
/// diagonal, the way a browser draws it, and the inner corner radius shrinks so the inner outline stays
/// parallel to the outer one.
/// </para>
/// <para>
/// <b>Corners.</b> <see cref="Corners"/> is one radius per corner, so a card can have square feet or a
/// sheet can round only its top. A control rounds its own fill by the same shape, and the clip follows
/// it, which is what lets what is behind the control show through the corners.
/// </para>
/// <para>
/// <b>State colours.</b> The five <c>With...Color</c> methods give the whole frame a colour per
/// <see cref="ControlState"/>, resolved while painting, so the colour follows the palette and the
/// control's state with no rebuild and no reassignment. When several apply at once they are taken in
/// this order: <see cref="ControlState.Disabled"/>, <see cref="ControlState.Pressed"/>,
/// <see cref="ControlState.Active"/>, <see cref="ControlState.Focused"/>,
/// <see cref="ControlState.Hovered"/>, then the resting colour - the order the built-in controls
/// already implement by hand. A state colour covers every edge, because a frame whose edges differ only
/// while hovered is not a shape anything in this library needs.
/// </para>
/// <para>
/// <b>A dashed frame is not a <see cref="SimpleBorder"/>.</b> A dash belongs to a stroke, and a frame
/// with a width per side has to be filled rather than stroked to keep its mitres; the two cannot be the
/// same object. A dashed border is a <see cref="Border"/> of its own that strokes a path.
/// </para>
/// </remarks>
public sealed class SimpleBorder : Border
{
    /// <summary>The frame a control class gives itself by default: one pixel, the border palette role,
    /// square corners, no state colours.</summary>
    public static SimpleBorder Thin { get; } = new(new Thickness(1f), CornerRadii.None);

    private static readonly Edge DefaultEdge = new(Color: null, Role: ThemeColor.Border);

    /// <summary>How far the caption's break reaches past the text, on each side.</summary>
    private const float CaptionGap = 4f;

    /// <summary>Room above and below the line the caption is drawn on.</summary>
    private const float CaptionLeading = 2f;

    private readonly Thickness _widths;
    private readonly CornerRadii _corners;
    private readonly Thickness? _insets;
    private readonly Edge[] _edges;
    private readonly ThemeColor? _hoverRole;
    private readonly ThemeColor? _focusRole;
    private readonly ThemeColor? _activeRole;
    private readonly ThemeColor? _disabledRole;
    private readonly ThemeColor? _pressedRole;
    private readonly bool _showsControlText;

    private SimpleBorder(
        Thickness widths,
        CornerRadii corners,
        Thickness? insets,
        Edge[] edges,
        ThemeColor? hoverRole,
        ThemeColor? focusRole,
        ThemeColor? activeRole,
        ThemeColor? disabledRole,
        ThemeColor? pressedRole,
        bool showsControlText)
    {
        _widths = widths;
        _corners = corners;
        _insets = insets;
        _edges = edges;
        _hoverRole = hoverRole;
        _focusRole = focusRole;
        _activeRole = activeRole;
        _disabledRole = disabledRole;
        _pressedRole = pressedRole;
        _showsControlText = showsControlText;
    }

    /// <summary>
    /// A frame of <paramref name="width"/> logical pixels on every side, in the border palette role.
    /// </summary>
    /// <param name="width">The thickness, in logical pixels. Zero paints nothing.</param>
    /// <param name="cornerRadius">The radius of the corners, in logical pixels.</param>
    public SimpleBorder(float width, float cornerRadius = 0f)
        : this(new Thickness(width), new CornerRadii(cornerRadius))
    {
    }

    /// <summary>A frame of the given thickness per side, in the border palette role.</summary>
    /// <param name="widths">The thickness of each side, in logical pixels.</param>
    /// <param name="cornerRadius">The radius of the corners, in logical pixels.</param>
    public SimpleBorder(Thickness widths, float cornerRadius = 0f)
        : this(widths, new CornerRadii(cornerRadius))
    {
    }

    /// <summary>A frame of the given thickness per side, with its own radius per corner.</summary>
    /// <param name="widths">The thickness of each side, in logical pixels.</param>
    /// <param name="corners">The radius of each corner, in logical pixels.</param>
    public SimpleBorder(Thickness widths, CornerRadii corners)
        : this(widths, corners, null, [DefaultEdge, DefaultEdge, DefaultEdge, DefaultEdge], null, null, null, null, null, false)
    {
    }

    /// <summary>
    /// A shape with no frame: rounds the fill and clips the control by
    /// <paramref name="cornerRadius"/>, painting nothing and taking no room.
    /// </summary>
    /// <param name="cornerRadius">The radius of the corners, in logical pixels.</param>
    /// <returns>A new border.</returns>
    public static SimpleBorder Rounded(float cornerRadius) => new(default, new CornerRadii(cornerRadius));

    /// <summary>Thickness of each edge in logical pixels, drawn inside the control's bounds.</summary>
    public Thickness Widths => _widths;

    /// <summary>Radius of each corner, which also clips the control and rounds its fill.</summary>
    public override CornerRadii Corners => _corners;

    /// <summary>
    /// Whether the frame draws the owning control's <see cref="Control.Text"/> in a break in its top
    /// edge, the way a group box does.
    /// </summary>
    public override bool ShowsControlText => _showsControlText;

    /// <inheritdoc />
    /// <remarks>
    /// A frame that shows the control's text takes the caption's band out of the top, so the content
    /// starts below it; the band is measured from the control's font, which is why this needs the
    /// owner. A control with no text gets the plain frame's insets.
    /// </remarks>
    public override Thickness Insets(Control owner)
    {
        var insets = _insets ?? _widths;
        float band = CaptionBand(owner);

        return band > 0f
            ? new Thickness(insets.Left, MathF.Max(insets.Top, band), insets.Right, insets.Bottom)
            : insets;
    }

    /// <inheritdoc />
    public override bool NarrowsClip => !_corners.IsNone;

    /// <inheritdoc />
    public override bool IsRectangular => _corners.IsNone;

    /// <inheritdoc />
    public override bool VariesByState =>
        _hoverRole is not null
        || _focusRole is not null
        || _activeRole is not null
        || _disabledRole is not null
        || _pressedRole is not null;

    /// <inheritdoc />
    public override void ClipTo(SKCanvas canvas, SKRect bounds)
    {
        if (_corners.IsNone)
        {
            return;
        }

        using var builder = new SKPathBuilder();
        RoundedRect.AddTo(builder, bounds, RoundedRect.Clamp(_corners, bounds));
        using var path = builder.Snapshot();
        canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);
    }

    /// <summary>
    /// The colour an edge takes for <paramref name="state"/>: the state colour in the documented order
    /// when one applies, otherwise that edge's own colour, and the border palette role when it has none.
    /// </summary>
    /// <param name="side">The edge being painted.</param>
    /// <param name="state">The state of the control being painted.</param>
    /// <returns>The colour, alpha included.</returns>
    public SKColor ColorFor(BorderSide side, ControlState state)
    {
        if (StateRoleFor(state) is { } role)
        {
            return Theme.ColorOf(role);
        }

        var edge = _edges[(int)side];
        return edge.Color ?? Theme.ColorOf(edge.Role ?? ThemeColor.Border);
    }

    /// <summary>This frame with the same thickness on every side.</summary>
    /// <param name="width">The thickness, in logical pixels.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithWidth(float width) => With(widths: new Thickness(width));

    /// <summary>This frame with a thickness per side.</summary>
    /// <param name="widths">The thickness of each side, in logical pixels.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithWidths(Thickness widths) => With(widths: widths);

    /// <summary>This frame with the same corner radius on every corner.</summary>
    /// <param name="cornerRadius">The radius, in logical pixels.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithCornerRadius(float cornerRadius) => With(corners: new CornerRadii(cornerRadius));

    /// <summary>This frame with a radius per corner.</summary>
    /// <param name="corners">The radius of each corner, in logical pixels.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithCorners(CornerRadii corners) => With(corners: corners);

    /// <summary>
    /// This frame taking a different amount of room than the stroke is thick, edge by edge.
    /// </summary>
    /// <param name="insets">The non-client area, in logical pixels.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    /// <remarks>
    /// For a frame whose band is asymmetric - a header that sits on the top edge, whose caption needs
    /// that pixel of inset to stay where it was. Taking a different amount of room also keeps a
    /// zero-width frame from painting a stroke of its own, so a border that only insets is one.
    /// </remarks>
    public SimpleBorder WithInsets(Thickness insets) => With(insets: insets);

    /// <summary>
    /// This frame drawing the owning control's <see cref="Control.Text"/> in a break in its top edge,
    /// the way a group box does.
    /// </summary>
    /// <param name="show">False to paint a plain frame again.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    /// <remarks>
    /// <para>
    /// The top edge is moved down by half the caption band so the text sits centred on it, the edge is
    /// cut away behind the text rather than painted over - so the caption reads the same on any
    /// background - and the band comes out of the top of the content.
    /// </para>
    /// <para>
    /// The band is measured from the control's font during layout, so a control that changes its text
    /// from empty to something has to lay out again for the content to move.
    /// </para>
    /// </remarks>
    public SimpleBorder WithControlText(bool show = true) => With(showsControlText: show);

    /// <summary>This frame with a fixed resting colour on every side.</summary>
    /// <param name="color">The colour, alpha included.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithColor(SKColor color) =>
        WithColor(BorderSide.Top, color)
            .WithColor(BorderSide.Right, color)
            .WithColor(BorderSide.Bottom, color)
            .WithColor(BorderSide.Left, color);

    /// <summary>This frame with a fixed resting colour on one side.</summary>
    /// <param name="side">The edge to recolour.</param>
    /// <param name="color">The colour, alpha included.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithColor(BorderSide side, SKColor color) => WithEdge(side, new Edge(color, null));

    /// <summary>This frame with a resting colour taken from the palette on every side.</summary>
    /// <param name="role">The palette entry, resolved while painting.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithThemeColor(ThemeColor role) =>
        WithThemeColor(BorderSide.Top, role)
            .WithThemeColor(BorderSide.Right, role)
            .WithThemeColor(BorderSide.Bottom, role)
            .WithThemeColor(BorderSide.Left, role);

    /// <summary>This frame with a resting colour taken from the palette on one side.</summary>
    /// <param name="side">The edge to recolour.</param>
    /// <param name="role">The palette entry, resolved while painting.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithThemeColor(BorderSide side, ThemeColor role) => WithEdge(side, new Edge(null, role));

    /// <summary>This frame with the colour it takes while the pointer is over the control.</summary>
    /// <param name="role">The palette entry, resolved while painting.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithHoverColor(ThemeColor role) => With(hoverRole: role);

    /// <summary>This frame with the colour it takes while the control has the keyboard focus.</summary>
    /// <param name="role">The palette entry, resolved while painting.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithFocusColor(ThemeColor role) => With(focusRole: role);

    /// <summary>This frame with the colour it takes while the control owns a showing overlay.</summary>
    /// <param name="role">The palette entry, resolved while painting.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    /// <remarks>
    /// The state a combo box, split button or date picker is in while its drop-down is up. It is
    /// separate from the focus colour because the pointer can move into the drop-down and the keyboard
    /// focus can follow it, and the frame should read as open either way.
    /// </remarks>
    public SimpleBorder WithActiveColor(ThemeColor role) => With(activeRole: role);

    /// <summary>This frame with the colour it takes while the control is disabled.</summary>
    /// <param name="role">The palette entry, resolved while painting.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithDisabledColor(ThemeColor role) => With(disabledRole: role);

    /// <summary>This frame with the colour it takes while a mouse button is held on the control.</summary>
    /// <param name="role">The palette entry, resolved while painting.</param>
    /// <returns>A new border; this one is unchanged.</returns>
    public SimpleBorder WithPressedColor(ThemeColor role) => With(pressedRole: role);

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Four edges in one colour are filled as a single ring, so the mitres between them cannot show a
    /// seam. Four edges in different colours are filled as four mitred pieces instead, because a ring
    /// is one fill and cannot carry four colours.
    /// </para>
    /// <para>
    /// When the frame shows the control's text, the whole frame is moved down by half the caption band
    /// so the text is centred on its top edge, and the edge is cut away behind the text rather than
    /// painted over.
    /// </para>
    /// </remarks>
    public override void Paint(SKCanvas canvas, SKRect bounds, Control owner)
    {
        if (bounds.Width <= 0f || bounds.Height <= 0f)
        {
            return;
        }

        float band = CaptionBand(owner);
        SKRect? caption = band > 0f ? CaptionRect(owner, bounds, band) : null;
        var frame = caption is { } box
            ? new SKRect(bounds.Left, bounds.Top + MathF.Round(band / 2f), bounds.Right, bounds.Bottom)
            : bounds;

        if (_widths.Horizontal > 0f || _widths.Vertical > 0f)
        {
            if (caption is { } cut)
            {
                canvas.Save();
                canvas.ClipRect(
                    new SKRect(cut.Left - CaptionGap, bounds.Top - 1f, cut.Right + CaptionGap, cut.Bottom),
                    SKClipOperation.Difference,
                    antialias: true);
                PaintFrame(canvas, frame, owner);
                canvas.Restore();
            }
            else
            {
                PaintFrame(canvas, frame, owner);
            }
        }

        if (caption is { } shown)
        {
            TextRenderer.Draw(
                canvas,
                owner.Text,
                owner.Font,
                owner.Enabled ? owner.ForeColor : Theme.DisabledForeground,
                shown,
                ContentAlignment.MiddleLeft);
        }
    }

    /// <summary>Paints the four edges into <paramref name="bounds"/>, which is the frame's own box.</summary>
    private void PaintFrame(SKCanvas canvas, SKRect bounds, Control owner)
    {
        var state = owner.State;
        var top = ColorFor(BorderSide.Top, state);
        var right = ColorFor(BorderSide.Right, state);
        var bottom = ColorFor(BorderSide.Bottom, state);
        var left = ColorFor(BorderSide.Left, state);

        if (top == right && right == bottom && bottom == left)
        {
            PaintRing(canvas, bounds, top);
            return;
        }

        PaintSide(canvas, bounds, BorderSide.Top, top);
        PaintSide(canvas, bounds, BorderSide.Right, right);
        PaintSide(canvas, bounds, BorderSide.Bottom, bottom);
        PaintSide(canvas, bounds, BorderSide.Left, left);
    }

    /// <summary>The height of the band the control's text sits in, or zero when there is no text.</summary>
    private float CaptionBand(Control owner) =>
        _showsControlText && !string.IsNullOrEmpty(owner.Text)
            ? MathF.Ceiling(owner.Font.LineHeight) + CaptionLeading
            : 0f;

    /// <summary>The box the caption is drawn in, lined up with the content under it.</summary>
    private SKRect CaptionRect(Control owner, SKRect bounds, float band)
    {
        float left = bounds.Left + Insets(owner).Left + owner.Padding.Left;
        float width = TextRenderer.Measure(owner.Text, owner.Font, float.PositiveInfinity, wrap: false).Width;

        return new SKRect(left, bounds.Top, left + MathF.Ceiling(width), bounds.Top + band);
    }

    private ThemeColor? StateRoleFor(ControlState state) =>
        (state & ControlState.Disabled) != 0 && _disabledRole is { } disabled ? disabled :
        (state & ControlState.Pressed) != 0 && _pressedRole is { } pressed ? pressed :
        (state & ControlState.Active) != 0 && _activeRole is { } active ? active :
        (state & ControlState.Focused) != 0 && _focusRole is { } focused ? focused :
        (state & ControlState.Hovered) != 0 && _hoverRole is { } hovered ? hovered :
        null;

    private void PaintRing(SKCanvas canvas, SKRect bounds, SKColor color)
    {
        if (color.Alpha == 0)
        {
            return;
        }

        using var builder = new SKPathBuilder();
        RoundedRect.AddTo(builder, bounds, RoundedRect.Clamp(_corners, bounds));

        var inner = Geometry.Deflate(bounds, _widths);
        if (inner.Width > 0f && inner.Height > 0f)
        {
            RoundedRect.AddTo(builder, inner, InnerCorners(bounds, inner));
        }

        using var path = builder.Snapshot();
        path.FillType = SKPathFillType.EvenOdd;

        using var paint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawPath(path, paint);
    }

    private void PaintSide(SKCanvas canvas, SKRect bounds, BorderSide side, SKColor color)
    {
        if (color.Alpha == 0 || WidthOf(side) <= 0f)
        {
            return;
        }

        using var builder = new SKPathBuilder();
        AddSide(builder, bounds, side);
        using var path = builder.Snapshot();

        using var paint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawPath(path, paint);
    }

    /// <summary>
    /// Appends one edge of the ring: the outer outline from the midpoint of its first corner to the
    /// midpoint of its second, and back along the inner outline. Cutting the ring at the corner
    /// midpoints is what makes the four pieces tile it exactly.
    /// </summary>
    private void AddSide(SKPathBuilder builder, SKRect bounds, BorderSide side)
    {
        var outer = RoundedRect.Clamp(_corners, bounds);
        var inner = Geometry.Deflate(bounds, _widths);
        var innerCorners = InnerCorners(bounds, inner);

        var (first, second) = CornersOf(side);
        var (firstStart, firstEnd) = RoundedRect.AnglesOf(first);
        var (secondStart, secondEnd) = RoundedRect.AnglesOf(second);
        float firstMid = (firstStart + firstEnd) / 2f;
        float secondMid = (secondStart + secondEnd) / 2f;

        float firstRadius = RoundedRect.RadiusOf(outer, first);
        float secondRadius = RoundedRect.RadiusOf(outer, second);
        float innerFirstRadius = RoundedRect.RadiusOf(innerCorners, first);
        float innerSecondRadius = RoundedRect.RadiusOf(innerCorners, second);

        var outerFirst = RoundedRect.OvalFor(bounds, first, firstRadius);
        var outerSecond = RoundedRect.OvalFor(bounds, second, secondRadius);
        var innerFirst = RoundedRect.OvalFor(inner, first, innerFirstRadius);
        var innerSecond = RoundedRect.OvalFor(inner, second, innerSecondRadius);

        builder.MoveTo(RoundedRect.PointAt(outerFirst, firstRadius, firstMid));
        RoundedRect.ArcTo(builder, outerFirst, firstRadius, firstMid, firstEnd);
        builder.LineTo(RoundedRect.PointAt(outerSecond, secondRadius, secondStart));
        RoundedRect.ArcTo(builder, outerSecond, secondRadius, secondStart, secondMid);
        builder.LineTo(RoundedRect.PointAt(innerSecond, innerSecondRadius, secondMid));
        RoundedRect.ArcTo(builder, innerSecond, innerSecondRadius, secondMid, secondStart);
        builder.LineTo(RoundedRect.PointAt(innerFirst, innerFirstRadius, firstEnd));
        RoundedRect.ArcTo(builder, innerFirst, innerFirstRadius, firstEnd, firstMid);
        builder.Close();
    }

    /// <summary>The inner radii: each corner's radius less the thicker of the two edges meeting there.</summary>
    private CornerRadii InnerCorners(SKRect bounds, SKRect inner)
    {
        if (inner.Width <= 0f || inner.Height <= 0f)
        {
            return CornerRadii.None;
        }

        var outer = RoundedRect.Clamp(_corners, bounds);

        return RoundedRect.Clamp(
            new CornerRadii(
                MathF.Max(0f, outer.TopLeft - MathF.Max(_widths.Left, _widths.Top)),
                MathF.Max(0f, outer.TopRight - MathF.Max(_widths.Right, _widths.Top)),
                MathF.Max(0f, outer.BottomRight - MathF.Max(_widths.Right, _widths.Bottom)),
                MathF.Max(0f, outer.BottomLeft - MathF.Max(_widths.Left, _widths.Bottom))),
            inner);
    }

    private static (RoundedRect.Corner First, RoundedRect.Corner Second) CornersOf(BorderSide side) => side switch
    {
        BorderSide.Top => (RoundedRect.Corner.TopLeft, RoundedRect.Corner.TopRight),
        BorderSide.Right => (RoundedRect.Corner.TopRight, RoundedRect.Corner.BottomRight),
        BorderSide.Bottom => (RoundedRect.Corner.BottomRight, RoundedRect.Corner.BottomLeft),
        _ => (RoundedRect.Corner.BottomLeft, RoundedRect.Corner.TopLeft),
    };

    private float WidthOf(BorderSide side) => side switch
    {
        BorderSide.Top => _widths.Top,
        BorderSide.Right => _widths.Right,
        BorderSide.Bottom => _widths.Bottom,
        _ => _widths.Left,
    };

    private SimpleBorder With(
        Thickness? widths = null,
        CornerRadii? corners = null,
        Thickness? insets = null,
        ThemeColor? hoverRole = null,
        ThemeColor? focusRole = null,
        ThemeColor? activeRole = null,
        ThemeColor? disabledRole = null,
        ThemeColor? pressedRole = null,
        bool? showsControlText = null) =>
        new(
            widths ?? _widths,
            corners ?? _corners,
            insets ?? _insets,
            _edges,
            hoverRole ?? _hoverRole,
            focusRole ?? _focusRole,
            activeRole ?? _activeRole,
            disabledRole ?? _disabledRole,
            pressedRole ?? _pressedRole,
            showsControlText ?? _showsControlText);

    private SimpleBorder WithEdge(BorderSide side, Edge edge)
    {
        var edges = (Edge[])_edges.Clone();
        edges[(int)side] = edge;

        return new SimpleBorder(
            _widths,
            _corners,
            _insets,
            edges,
            _hoverRole,
            _focusRole,
            _activeRole,
            _disabledRole,
            _pressedRole,
            _showsControlText);
    }

    /// <summary>One side's resting colour: a fixed colour, a palette role, or neither for the default.</summary>
    private readonly record struct Edge(SKColor? Color, ThemeColor? Role);
}