using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// A container that runs its children along one axis - top to bottom, or left to right - and places
/// each one across the other axis according to its <see cref="XPlatControl.HorizontalAlignment"/> or
/// <see cref="XPlatControl.VerticalAlignment"/>.
/// </summary>
/// <remarks>
/// <para>
/// A child gets its preferred size along the stacking axis and is, by default, stretched across the
/// stack: the full width of the panel for <see cref="XPlatStackOrientation.Vertical"/>, the full
/// height for <see cref="XPlatStackOrientation.Horizontal"/>. Setting the matching alignment on a
/// child to <see cref="XPlatAlignment.Near"/>, <see cref="XPlatAlignment.Center"/> or
/// <see cref="XPlatAlignment.Far"/> leaves it at its preferred size across the stack instead, and
/// places it at that edge or in the middle. A child whose <see cref="XPlatControl.AutoSize"/> is
/// false keeps the size it was given along the stacking axis, which is how a stack of fixed-height
/// rows is built.
/// </para>
/// <para>
/// Margins still apply: they separate one child from the next along the stack and inset it from the
/// edges across the stack. <see cref="Spacing"/> adds a gap between children without also adding one
/// at the ends of the run.
/// </para>
/// <para>
/// A docked child is not part of the stack. It is placed against the panel's edge the way a docked
/// child of an <see cref="XPlatPanel"/> is, and the stack fills what is left, which is how a header
/// or a footer is added to a stack.
/// </para>
/// <para>
/// The run of children is the scrollable content, so the panel scrolls like any other:
/// <see cref="XPlatPanel.VerticalScroll"/> for a vertical stack, and
/// <see cref="XPlatPanel.HorizontalScroll"/> for a horizontal one.
/// </para>
/// </remarks>
public class XPlatStackPanel : XPlatPanel
{
    private XPlatStackOrientation _orientation = XPlatStackOrientation.Vertical;
    private float _spacing;

    public XPlatStackPanel()
    {
        // A stack is a layout container rather than a card, so it does not paint a surface of its
        // own and its bounds are exactly the run of its children.
        ShowFrame = false;
        Padding = XPlatThickness.Empty;
    }

    /// <summary>The axis the children are stacked along. Defaults to <see cref="XPlatStackOrientation.Vertical"/>.</summary>
    public XPlatStackOrientation Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation == value)
            {
                return;
            }

            _orientation = value;
            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>The gap left between one child and the next, in logical pixels. Defaults to zero.</summary>
    public float Spacing
    {
        get => _spacing;
        set
        {
            var clamped = MathF.Max(0f, value);
            if (MathF.Abs(_spacing - clamped) < 0.01f)
            {
                return;
            }

            _spacing = clamped;
            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>
    /// An auto-sized stack is as long as the run of its children and as wide (or tall) as the widest
    /// of them. Docked children are left out: they would fill the panel instead.
    /// </summary>
    protected override SKSize MeasureContent(SKSize availableSize)
    {
        bool vertical = Orientation == XPlatStackOrientation.Vertical;
        float length = 0f;
        float breadth = 0f;
        bool first = true;

        foreach (var child in Controls)
        {
            if (!child.Visible || child.Dock != DockStyle.None)
            {
                continue;
            }

            var size = child.Measure(availableSize);
            if (!first)
            {
                length += Spacing;
            }

            first = false;
            length += (vertical ? size.Height : size.Width) +
                      (vertical ? child.Margin.Vertical : child.Margin.Horizontal);
            breadth = MathF.Max(breadth, (vertical ? size.Width : size.Height) +
                                         (vertical ? child.Margin.Horizontal : child.Margin.Vertical));
        }

        var content = vertical ? new SKSize(breadth, length) : new SKSize(length, breadth);

        if (VerticalScroll == XPlatScrollStyle.Always)
        {
            content.Width += ScrollBarThickness;
        }

        if (HorizontalScroll == XPlatScrollStyle.Always)
        {
            content.Height += ScrollBarThickness;
        }

        return content;
    }

    /// <summary>
    /// Runs the children along the stacking axis and places each one across it. Docked children are
    /// placed first and are not part of the run.
    /// </summary>
    protected override SKSize ArrangeChildren(SKRect area)
    {
        // Docked children take their edges first; the stack runs inside what is left of the panel.
        var content = area;
        area = LayoutDockedChildren(area);

        if (!HasChildren)
        {
            return MeasureExtent(content);
        }

        bool vertical = Orientation == XPlatStackOrientation.Vertical;

        float MainStart(SKRect rect) => vertical ? rect.Top : rect.Left;
        float CrossStart(SKRect rect) => vertical ? rect.Left : rect.Top;
        float CrossLength(SKRect rect) => vertical ? rect.Width : rect.Height;
        float MainOf(SKSize size) => vertical ? size.Height : size.Width;
        float CrossOf(SKSize size) => vertical ? size.Width : size.Height;

        SKRect Slot(float mainPosition, float mainLength, float crossPosition, float crossLength) =>
            vertical
                ? new SKRect(crossPosition, mainPosition, crossPosition + crossLength, mainPosition + mainLength)
                : new SKRect(mainPosition, crossPosition, mainPosition + mainLength, crossPosition + crossLength);

        float main = MainStart(area);
        float crossOrigin = CrossStart(area);
        float crossAvailable = CrossLength(area);
        bool first = true;

        foreach (var child in Controls)
        {
            if (!child.Visible || child.Dock != DockStyle.None)
            {
                continue;
            }

            var margin = child.Margin;
            float crossBox = MathF.Max(0f, crossAvailable - (vertical ? margin.Horizontal : margin.Vertical));
            float crossPosition = crossOrigin + (vertical ? margin.Left : margin.Top);

            var alignment = vertical ? child.HorizontalAlignment : child.VerticalAlignment;
            bool stretch = alignment == XPlatAlignment.Stretch;

            // A wrapping label measures against the width it is given, so the cross axis has to be
            // the real one even though the stacking axis is free to grow.
            var available = vertical
                ? new SKSize(crossBox, area.Height)
                : new SKSize(area.Width, crossBox);

            // Nothing across the stack has to be measured when the child fills it, and a child that
            // keeps its own size does not have to be measured at all.
            var desired = child.AutoSize || !stretch ? child.Measure(available) : default;

            float mainLength = MathF.Max(0f, child.AutoSize ? MainOf(desired) : MainOf(child.Size));
            float crossLength = crossBox;

            if (!stretch)
            {
                var own = MathF.Max(0f, child.AutoSize ? CrossOf(desired) : CrossOf(child.Size));
                crossLength = MathF.Min(own, crossBox);
                crossPosition += alignment switch
                {
                    XPlatAlignment.Center => (crossBox - crossLength) / 2f,
                    XPlatAlignment.Far => crossBox - crossLength,
                    _ => 0f,
                };
            }

            if (!first)
            {
                main += Spacing;
            }

            first = false;

            float mainPosition = main + (vertical ? margin.Top : margin.Left);
            child.Bounds = Slot(mainPosition, mainLength, crossPosition, crossLength);
            main = mainPosition + mainLength + (vertical ? margin.Bottom : margin.Right);
        }

        return MeasureExtent(content);
    }
}
