using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// Measurements of a popup menu, in logical pixels at 100% scale. The defaults follow the Windows
/// 11 context menu (32 px rows, 8 px corners, 6 px padding) so that a menu drawn with
/// <see cref="XPlatMenuRenderer"/> is indistinguishable from the native one.
/// </summary>
public readonly record struct XPlatMenuMetrics
{
    /// <summary>Height of a normal item row.</summary>
    public float ItemHeight { get; init; }

    /// <summary>Height of a separator row.</summary>
    public float SeparatorHeight { get; init; }

    /// <summary>Padding between the menu edge and the first and last row, and left of the check column.</summary>
    public float Padding { get; init; }

    /// <summary>Width reserved for the check mark to the left of every label.</summary>
    public float CheckColumnWidth { get; init; }

    /// <summary>
    /// Width reserved for an item's icon, to the right of the check column. A menu that has no icons
    /// anywhere does not reserve it, so an iconless menu is exactly as wide as it always was.
    /// </summary>
    public float IconColumnWidth { get; init; }

    /// <summary>The square an icon is fitted into.</summary>
    public float IconSize { get; init; }

    /// <summary>
    /// Width reserved at the right end of a row for the chevron that marks a submenu. Only reserved
    /// when the menu has at least one submenu.
    /// </summary>
    public float ChevronColumnWidth { get; init; }

    /// <summary>Space above and below a control hosted in a row.</summary>
    public float ControlPadding { get; init; }

    /// <summary>
    /// Width a hosted control is measured at. A control that fills whatever it is given - a track
    /// bar, a progress bar - would otherwise ask for the whole screen and stretch the menu to it.
    /// </summary>
    public float ControlWidth { get; init; }

    /// <summary>Minimum space between a label and its shortcut hint.</summary>
    public float ShortcutGap { get; init; }

    /// <summary>Width a menu never goes below.</summary>
    public float MinWidth { get; init; }

    /// <summary>Corner radius of the menu itself.</summary>
    public float CornerRadius { get; init; }

    /// <summary>Corner radius of the highlight drawn behind the row under the pointer.</summary>
    public float ItemCornerRadius { get; init; }

    /// <summary>How far the drop shadow reaches past the menu on every side.</summary>
    public float ShadowExtent { get; init; }

    public static XPlatMenuMetrics Default => new()
    {
        ItemHeight = 32f,
        SeparatorHeight = 9f,
        Padding = 6f,
        CheckColumnWidth = 22f,
        IconColumnWidth = 24f,
        IconSize = 16f,
        ChevronColumnWidth = 16f,
        ControlPadding = 4f,
        ControlWidth = 220f,
        ShortcutGap = 24f,
        MinWidth = 132f,
        CornerRadius = 8f,
        ItemCornerRadius = 4f,
        ShadowExtent = 12f,
    };
}

/// <summary>
/// One laid-out row of a menu: the item plus the rectangle the pointer has to be inside to hit it.
/// <see cref="Bounds"/> is in the drawing surface's coordinates, so it already includes any shadow
/// margin the layout reserved.
/// </summary>
public readonly record struct XPlatMenuRow(XPlatMenuItem Item, SKRect Bounds)
{
    /// <summary>Where the item's icon goes. Empty when the menu has no icon column.</summary>
    public SKRect IconBounds { get; init; }

    /// <summary>Where a control hosted by the item goes. Empty for an ordinary item.</summary>
    public SKRect ControlBounds { get; init; }

    /// <summary>
    /// True when picking this row should do something: a real, enabled item that is not filled by a
    /// control of its own. A row that opens a submenu is pickable, because pressing it opens the menu.
    /// </summary>
    public bool IsPickable => !Item.IsSeparator && Item.Enabled && Item.Control is null;

    /// <summary>True when pressing this row opens <see cref="XPlatMenuItem.SubItems"/>.</summary>
    public bool IsSubMenu => !Item.IsSeparator && Item.HasSubMenu;

    /// <summary>True when the row is filled by <see cref="XPlatMenuItem.Control"/> rather than a label.</summary>
    public bool HasControl => !Item.IsSeparator && Item.Control is not null;
}

/// <summary>
/// The result of measuring a menu: how big it is and where every row sits. Layout is separate from
/// painting so a platform layer can measure once, then repaint on every hover change.
/// </summary>
public sealed class XPlatMenuLayout
{
    private readonly XPlatMenuRow[] _rows;

    internal XPlatMenuLayout(SKSize surfaceSize, float shadowExtent, XPlatMenuRow[] rows, bool hasIcons, bool hasSubMenus)
    {
        SurfaceSize = surfaceSize;
        ShadowExtent = shadowExtent;
        HasIcons = hasIcons;
        HasSubMenus = hasSubMenus;
        _rows = rows;
        FirstPickable = -1;

        for (var i = 0; i < rows.Length; i++)
        {
            if (!rows[i].IsPickable)
            {
                continue;
            }

            FirstPickable = i;
            break;
        }
    }

    /// <summary>Size of the menu itself, without the shadow margin.</summary>
    public SKSize SurfaceSize { get; }

    /// <summary>True when at least one item carries an icon, so the icon column is reserved.</summary>
    public bool HasIcons { get; }

    /// <summary>True when at least one item opens a submenu, so the chevron column is reserved.</summary>
    public bool HasSubMenus { get; }

    /// <summary>How far the shadow reaches past the menu. Zero when the layout has no shadow.</summary>
    public float ShadowExtent { get; }

    /// <summary>True when the drawing surface reserves room for a drop shadow.</summary>
    public bool HasShadow => ShadowExtent > 0f;

    /// <summary>Size of the surface to draw into, shadow margin included.</summary>
    public SKSize WindowSize => new(SurfaceSize.Width + (ShadowExtent * 2f), SurfaceSize.Height + (ShadowExtent * 2f));

    /// <summary>Where the menu itself sits inside the drawing surface.</summary>
    public SKRect SurfaceBounds => SKRect.Create(ShadowExtent, ShadowExtent, SurfaceSize.Width, SurfaceSize.Height);

    /// <summary>The rows in the order they are drawn.</summary>
    public IReadOnlyList<XPlatMenuRow> Rows => _rows;

    /// <summary>Index of the first enabled item, or -1 when the menu has none.</summary>
    public int FirstPickable { get; }

    /// <summary>Index of the row under <paramref name="point"/>, or -1 when it is over no row.</summary>
    public int HitTest(SKPoint point)
    {
        for (var i = 0; i < _rows.Length; i++)
        {
            if (_rows[i].Bounds.Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The next enabled item in <paramref name="direction"/> (+1 down, -1 up), wrapping around the
    /// ends the way a native menu does. Returns <paramref name="from"/> when nothing is pickable.
    /// </summary>
    public int NextPickable(int from, int direction)
    {
        if (FirstPickable < 0 || _rows.Length == 0)
        {
            return from;
        }

        var step = direction >= 0 ? 1 : -1;
        var index = from;

        for (var guard = 0; guard < _rows.Length; guard++)
        {
            index += step;

            if (index >= _rows.Length)
            {
                index = 0;
            }
            else if (index < 0)
            {
                index = _rows.Length - 1;
            }

            if (_rows[index].IsPickable)
            {
                return index;
            }
        }

        return from;
    }
}

/// <summary>
/// Draws a menu - a context menu, a drop-down, a submenu - with Skia in the modern Windows 11
/// style: a rounded surface with a drop shadow, a pill behind the hovered row, check and icon
/// columns, right aligned shortcut hints and a chevron on an item that opens a submenu. It is
/// deliberately free of platform code, so every backend draws the same menu.
/// </summary>
/// <remarks>
/// Measuring and painting are separate so a platform layer can cache the layout and only repaint
/// when the hovered row changes. All sizes are logical pixels; the caller scales the canvas for
/// the display.
/// </remarks>
public static class XPlatMenuRenderer
{
    /// <summary>Opacity of the line between groups of items, applied to the text colour.</summary>
    private const byte SeparatorAlpha = 0x2C;

    /// <summary>
    /// Opacity of the hover pill, applied to the text colour. A wash rather than a palette fill,
    /// because a light menu surface is already as bright as the light palette's fills: a fill leaves
    /// the hover invisible on a light menu while reading well on a dark one. Washing the text colour
    /// over the surface keeps the step the same size in both palettes.
    /// </summary>
    internal const byte HoverWashAlpha = 0x1D;

    /// <summary>Measures <paramref name="menu"/> and works out where each row sits.</summary>
    /// <param name="menu">The menu to measure.</param>
    /// <param name="metrics">Row metrics, or null for <see cref="XPlatMenuMetrics.Default"/>.</param>
    /// <param name="font">Label font, or null for <see cref="XPlatTheme.DefaultFont"/>.</param>
    /// <param name="shadow">True to reserve room for a drop shadow around the surface.</param>
    public static XPlatMenuLayout Measure(
        XPlatContextMenu menu,
        XPlatMenuMetrics? metrics = null,
        XPlatFont? font = null,
        bool shadow = true)
    {
        ArgumentNullException.ThrowIfNull(menu);
        return Measure(menu.TrimmedItems, metrics, font, shadow);
    }

    /// <summary>Measures a list of items and works out where each row sits.</summary>
    /// <param name="items">The items to lay out, in order. Trimmed before measuring.</param>
    /// <param name="metrics">Row metrics, or null for <see cref="XPlatMenuMetrics.Default"/>.</param>
    /// <param name="font">Label font, or null for <see cref="XPlatTheme.DefaultFont"/>.</param>
    /// <param name="shadow">True to reserve room for a drop shadow around the surface.</param>
    /// <param name="availableWidth">
    /// How wide the menu is allowed to be, which is what a hosted control is measured against.
    /// Infinite by default, so a menu is as wide as its contents.
    /// </param>
    public static XPlatMenuLayout Measure(
        IReadOnlyList<XPlatMenuItem> items,
        XPlatMenuMetrics? metrics = null,
        XPlatFont? font = null,
        bool shadow = true,
        float availableWidth = float.PositiveInfinity)
    {
        ArgumentNullException.ThrowIfNull(items);

        var m = metrics ?? XPlatMenuMetrics.Default;
        var f = font ?? XPlatTheme.DefaultFont;
        items = XPlatMenuTrimming.Trim(items);

        var hasIcons = false;
        var hasSubMenus = false;

        foreach (var item in items)
        {
            hasIcons |= item.Image is not null;
            hasSubMenus |= item.HasSubMenu;
        }

        var iconColumn = hasIcons ? m.IconColumnWidth : 0f;
        var chevronColumn = hasSubMenus ? m.ChevronColumnWidth : 0f;
        var columns = (m.Padding * 2f) + m.CheckColumnWidth + iconColumn;
        var controlWidth = MathF.Max(0f, availableWidth - columns - m.Padding);

        var width = m.MinWidth;
        var height = m.Padding * 2f;
        var rowHeights = new float[items.Count];

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];

            if (item.IsSeparator)
            {
                rowHeights[i] = m.SeparatorHeight;
                height += m.SeparatorHeight;
                continue;
            }

            if (item.Control is { } control)
            {
                var controlExtent = MathF.Min(m.ControlWidth, controlWidth);
                var desired = control.Measure(new SKSize(controlExtent, float.PositiveInfinity));
                rowHeights[i] = MathF.Max(m.ItemHeight, desired.Height + (m.ControlPadding * 2f));
                height += rowHeights[i];
                width = MathF.Max(width, columns + MathF.Min(desired.Width, controlExtent) + m.Padding);
                continue;
            }

            rowHeights[i] = m.ItemHeight;
            height += m.ItemHeight;

            var needed = columns + FontFor(item, f).MeasureWidth(item.DisplayText) + chevronColumn;
            if (item.ShortcutDisplayText is { } hint)
            {
                needed += m.ShortcutGap + f.MeasureWidth(hint);
            }

            width = MathF.Max(width, needed);
        }

        width = MathF.Ceiling(width);
        height = MathF.Ceiling(height);

        var extent = shadow ? m.ShadowExtent : 0f;
        var rows = new XPlatMenuRow[items.Count];
        var y = extent + m.Padding;
        var iconLeft = extent + m.Padding + m.CheckColumnWidth;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var rowHeight = rowHeights[i];
            var rowBounds = SKRect.Create(extent, y, width, rowHeight);

            var row = new XPlatMenuRow(item, rowBounds);

            if (!item.IsSeparator)
            {
                row = row with
                {
                    IconBounds = hasIcons
                        ? SKRect.Create(iconLeft, rowBounds.MidY - (m.IconSize * 0.5f), m.IconSize, m.IconSize)
                        : SKRect.Empty,
                };

                if (item.Control is not null)
                {
                    row = row with
                    {
                        ControlBounds = SKRect.Create(
                            iconLeft + iconColumn,
                            rowBounds.Top + m.ControlPadding,
                            MathF.Max(0f, width - columns - m.Padding),
                            rowHeight - (m.ControlPadding * 2f)),
                    };
                }
            }

            rows[i] = row;
            y += rowHeight;
        }

        return new XPlatMenuLayout(new SKSize(width, height), extent, rows, hasIcons, hasSubMenus);
    }

    /// <summary>Paints a measured menu onto <paramref name="canvas"/>.</summary>
    /// <param name="canvas">Target canvas, already scaled to the display's DPI.</param>
    /// <param name="layout">Layout from <see cref="Measure"/>.</param>
    /// <param name="hoveredIndex">Row to highlight, or -1.</param>
    /// <param name="surfaceAlpha">
    /// Opacity of the surface, 0 to 1. Below 1 the menu is translucent so a compositor-supplied
    /// backdrop (the Windows 11 acrylic blur) shows through; 1 paints it solid.
    /// </param>
    /// <param name="metrics">Row metrics, or null for <see cref="XPlatMenuMetrics.Default"/>.</param>
    /// <param name="font">Label font, or null for <see cref="XPlatTheme.DefaultFont"/>.</param>
    /// <param name="shadowExclusion">
    /// A rectangle, in the canvas's coordinates, that the shadow must not be painted over - the
    /// surface of the menu a submenu is joined to. Null to paint the shadow everywhere.
    /// </param>
    /// <remarks>
    /// The canvas is not cleared, so whatever the caller has already painted stays visible through a
    /// translucent surface.
    /// </remarks>
    public static void Paint(
        SKCanvas canvas,
        XPlatMenuLayout layout,
        int hoveredIndex = -1,
        float surfaceAlpha = 1f,
        XPlatMenuMetrics? metrics = null,
        XPlatFont? font = null,
        SKRect? shadowExclusion = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(layout);

        var m = metrics ?? XPlatMenuMetrics.Default;
        var f = font ?? XPlatTheme.DefaultFont;
        var alpha = Math.Clamp(surfaceAlpha, 0f, 1f);
        var bounds = layout.SurfaceBounds;

        if (layout.HasShadow)
        {
            using var shadow = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(0, 0, 0, XPlatTheme.IsDarkMode ? (byte)0x8C : (byte)0x3D),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, layout.ShadowExtent * 0.32f),
            };

            if (shadowExclusion is { } exclusion)
            {
                canvas.Save();
                canvas.ClipRect(exclusion, SKClipOperation.Difference, antialias: true);
            }

            // Offset downwards the way a raised surface casts a shadow, and keep the blur inside
            // the reserved margin so it never has a hard edge.
            var shadowBounds = bounds;
            shadowBounds.Offset(0f, 2f);
            shadowBounds.Inflate(-2f, -2f);
            canvas.DrawRoundRect(shadowBounds, m.CornerRadius, m.CornerRadius, shadow);

            if (shadowExclusion is not null)
            {
                canvas.Restore();
            }
        }

        using (var fill = new SKPaint { IsAntialias = true, Color = Scale(XPlatTheme.Surface, alpha) })
        {
            canvas.DrawRoundRect(bounds, m.CornerRadius, m.CornerRadius, fill);
        }

        using (var edge = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color = Scale(XPlatTheme.Border, MathF.Max(alpha, 0.6f)),
        })
        {
            var edgeBounds = bounds;
            edgeBounds.Inflate(-0.5f, -0.5f);
            canvas.DrawRoundRect(edgeBounds, m.CornerRadius, m.CornerRadius, edge);
        }

        DrawHover(canvas, layout, hoveredIndex, m, alpha);

        var textLeft = bounds.Left + m.Padding + m.CheckColumnWidth + (layout.HasIcons ? m.IconColumnWidth : 0f);
        var textRight = bounds.Right - m.Padding - (layout.HasSubMenus ? m.ChevronColumnWidth : 0f);
        var checkCenter = bounds.Left + m.Padding + (m.CheckColumnWidth * 0.5f);

        using var text = new SKPaint { IsAntialias = true };

        // A separator tinted from the text colour keeps the same visual weight in both palettes;
        // the border colour is too close to a dark surface to read as a line.
        var foreground = XPlatTheme.Foreground;
        using var separator = new SKPaint
        {
            IsAntialias = false,
            StrokeWidth = 1f,
            Color = new SKColor(foreground.Red, foreground.Green, foreground.Blue, (byte)(SeparatorAlpha * alpha)),
        };

        foreach (var row in layout.Rows)
        {
            var item = row.Item;

            if (item.IsSeparator)
            {
                var y = MathF.Round(row.Bounds.MidY) + 0.5f;
                canvas.DrawLine(bounds.Left + m.Padding, y, bounds.Right - m.Padding, y, separator);
                continue;
            }

            if (row.HasControl)
            {
                // The hosted control paints itself: it is a child of the surface, drawn after this.
                continue;
            }

            var rowFont = FontFor(item, f);
            var baseline = row.Bounds.MidY + (rowFont.BaselineOffset - (rowFont.LineHeight * 0.5f));

            if (item.Image is { } image && !row.IconBounds.IsEmpty)
            {
                DrawIcon(canvas, image, row.IconBounds, alpha);
            }

            if (item.Checked)
            {
                DrawCheckMark(canvas, checkCenter, row.Bounds.MidY, Scale(XPlatTheme.Accent, MathF.Max(alpha, 0.85f)));
            }

            text.Color = Scale(item.Enabled ? XPlatTheme.Foreground : XPlatTheme.DisabledForeground, MathF.Max(alpha, 0.85f));
            canvas.DrawText(item.DisplayText, textLeft, baseline, SKTextAlign.Left, rowFont.SkFont, text);

            if (item.ShortcutDisplayText is { } hint)
            {
                text.Color = Scale(item.Enabled ? XPlatTheme.MutedForeground : XPlatTheme.DisabledForeground, MathF.Max(alpha, 0.85f));
                canvas.DrawText(hint, textRight, baseline, SKTextAlign.Right, f.SkFont, text);
            }

            if (row.IsSubMenu)
            {
                DrawChevron(
                    canvas,
                    bounds.Right - m.Padding - (m.ChevronColumnWidth * 0.5f),
                    row.Bounds.MidY,
                    Scale(item.Enabled ? XPlatTheme.MutedForeground : XPlatTheme.DisabledForeground, MathF.Max(alpha, 0.85f)));
            }
        }
    }

    private static void DrawIcon(SKCanvas canvas, SKImage image, SKRect slot, float alpha)
    {
        var scale = MathF.Min(slot.Width / image.Width, slot.Height / image.Height);
        var width = image.Width * scale;
        var height = image.Height * scale;

        var target = SKRect.Create(
            slot.MidX - (width * 0.5f),
            slot.MidY - (height * 0.5f),
            width,
            height);

        using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Clamp(MathF.Round(alpha * 255f), 0f, 255f)) };
        canvas.DrawImage(image, target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
    }

    private static void DrawChevron(SKCanvas canvas, float centerX, float centerY, SKColor color)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = color,
        };

        using var builder = new SKPathBuilder();
        builder.MoveTo(centerX - 2.5f, centerY - 4f);
        builder.LineTo(centerX + 1.5f, centerY);
        builder.LineTo(centerX - 2.5f, centerY + 4f);

        using var path = builder.Snapshot();
        canvas.DrawPath(path, paint);
    }

    private static void DrawHover(SKCanvas canvas, XPlatMenuLayout layout, int hoveredIndex, XPlatMenuMetrics m, float alpha)
    {
        if (hoveredIndex < 0 || hoveredIndex >= layout.Rows.Count || !layout.Rows[hoveredIndex].IsPickable)
        {
            return;
        }

        var bounds = layout.SurfaceBounds;
        var row = layout.Rows[hoveredIndex].Bounds;
        var inset = m.Padding * 0.5f;

        var pill = SKRect.Create(
            bounds.Left + inset,
            row.Top + 2f,
            bounds.Width - (inset * 2f),
            row.Height - 4f);

        var foreground = XPlatTheme.Foreground;
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(foreground.Red, foreground.Green, foreground.Blue, (byte)(HoverWashAlpha * alpha)),
        };

        canvas.DrawRoundRect(pill, m.ItemCornerRadius, m.ItemCornerRadius, paint);
    }

    private static void DrawCheckMark(SKCanvas canvas, float centerX, float centerY, SKColor color)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.6f,
            StrokeCap = SKStrokeCap.Round,
            Color = color,
        };

        canvas.DrawLine(centerX - 4.5f, centerY, centerX - 1.5f, centerY + 3.2f, paint);
        canvas.DrawLine(centerX - 1.5f, centerY + 3.2f, centerX + 5f, centerY - 3.6f, paint);
    }

    private static XPlatFont FontFor(XPlatMenuItem item, XPlatFont font) =>
        item.IsDefault ? XPlatTheme.EmphasizedFont : font;

    private static SKColor Scale(SKColor color, float alpha) =>
        color.WithAlpha((byte)Math.Clamp(MathF.Round(alpha * 255f), 0f, 255f));
}
