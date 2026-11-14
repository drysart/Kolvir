using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A container that runs its children along one axis and starts a new line whenever the next one
/// would not fit: rows left to right that wrap downwards, or columns top to bottom that wrap to the
/// right. It is what a tag row, a group of buttons and a thumbnail grid are made of.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StackPanel"/> never wraps and <see cref="TableLayoutPanel"/> needs its
/// columns known up front, so neither can lay out a run of items whose count or widths are only
/// known at run time.
/// </para>
/// <para>
/// An item is laid out at its own size along the run, and across the line according to its
/// alignment: <see cref="Alignment.Stretch"/>, the default, makes it as tall as the tallest
/// item on its row (or as wide as the widest on its column), and <see cref="Alignment.Near"/>,
/// <see cref="Alignment.Center"/> and <see cref="Alignment.Far"/> leave it at its own size
/// at that edge or in the middle. <see cref="Spacing"/> is the gap between two items on a line and
/// between two lines; margins still apply on top of it.
/// </para>
/// <para>
/// The run is the scrollable content, so the panel scrolls like any other: a row that does not fit
/// the height scrolls, and an item wider than the panel overflows to the right rather than being
/// clipped away. A docked child is not part of the run - it takes its edge first, the way a docked
/// child of an <see cref="Panel"/> does - which is how a header or a footer is added to a flow.
/// </para>
/// </remarks>
public class WrapPanel : Panel
{
    private StackOrientation _orientation = StackOrientation.Horizontal;
    private float _spacing;

    // The items of the run, measured once per layout pass. A line has to be known as a whole before
    // its first item can be placed - its height is the tallest of them - so the run is collected
    // first and then walked a line at a time.
    private readonly List<FlowItem> _items = [];

    /// <summary>
    /// Creates a horizontal flow with no padding of its own, so that its bounds are exactly the run of
    /// its children.
    /// </summary>
    public WrapPanel()
    {
        // A flow is a layout container: its bounds are exactly the run of its children.
        Padding = Thickness.Empty;
    }

    /// <summary>
    /// The direction the items run in. <see cref="StackOrientation.Horizontal"/>, the default,
    /// places rows left to right that wrap downwards; <see cref="StackOrientation.Vertical"/>
    /// places columns top to bottom that wrap to the right.
    /// </summary>
    public StackOrientation Orientation
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

    /// <summary>The gap between two items on a line and between two lines, in logical pixels. Defaults to zero.</summary>
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
    /// An auto-sized wrap panel is the run of its children in the room it was given: as many items
    /// per line as fit, so a narrow measurement is a taller panel.
    /// </summary>
    protected override SKSize MeasureContent(SKSize availableSize)
    {
        Collect(availableSize);
        return Flow(new SKRect(0f, 0f, availableSize.Width, availableSize.Height), place: false);
    }

    /// <summary>Docks the docked children, then flows the rest through what is left.</summary>
    protected override SKSize ArrangeChildren(SKRect area)
    {
        area = LayoutDockedChildren(area);

        Collect(new SKSize(area.Width, area.Height));
        return Flow(area, place: true);
    }

    /// <summary>Measures every child of the run once, so the placing pass reads sizes rather than measuring again.</summary>
    private void Collect(SKSize availableSize)
    {
        _items.Clear();

        bool horizontal = _orientation == StackOrientation.Horizontal;

        foreach (var child in Controls)
        {
            if (!child.Visible || child.Dock != DockStyle.None)
            {
                continue;
            }

            var margin = child.Margin;
            var available = horizontal
                ? new SKSize(MathF.Max(0f, availableSize.Width - margin.Horizontal), availableSize.Height)
                : new SKSize(availableSize.Width, MathF.Max(0f, availableSize.Height - margin.Vertical));

            var desired = child.AutoSize ? child.Measure(available) : child.Size;

            _items.Add(new FlowItem(
                child,
                new SKSize(MathF.Max(0f, desired.Width), MathF.Max(0f, desired.Height)),
                margin,
                horizontal ? child.VerticalAlignment : child.HorizontalAlignment));
        }
    }

    /// <summary>
    /// Breaks the collected items into lines and places them, or only measures the lines when
    /// <paramref name="place"/> is false. Returns the size of the whole run.
    /// </summary>
    private SKSize Flow(SKRect area, bool place)
    {
        bool horizontal = _orientation == StackOrientation.Horizontal;

        float lineOrigin = horizontal ? area.Left : area.Top;
        float limit = horizontal ? area.Right : area.Bottom;
        float cross = horizontal ? area.Top : area.Left;

        float extentMain = 0f;
        float extentCross = 0f;

        int index = 0;
        while (index < _items.Count)
        {
            int start = index;
            float main = lineOrigin;
            float lineCross = 0f;

            // First pass over the line: where it ends, and how broad it is.
            while (index < _items.Count)
            {
                var item = _items[index];
                float mainOuter = MainOf(item.Size) + MainMarginOf(item.Margin);
                float crossOuter = CrossOf(item.Size) + CrossMarginOf(item.Margin);

                // The first item on a line always goes there, even when it does not fit: an item
                // wider than the panel has to be somewhere, and letting it overflow is what makes
                // the scrollbar appear rather than losing it.
                if (index > start && main + _spacing + mainOuter > limit)
                {
                    break;
                }

                if (index > start)
                {
                    main += _spacing;
                }

                main += mainOuter;
                lineCross = MathF.Max(lineCross, crossOuter);
                index++;
            }

            // Second pass: place the line now that its breadth is known.
            float position = lineOrigin;
            for (int i = start; i < index; i++)
            {
                var item = _items[i];
                var margin = item.Margin;

                float own = MainOf(item.Size);
                float across = CrossOf(item.Size);
                float acrossOffset = 0f;

                if (item.Alignment == Alignment.Stretch)
                {
                    across = MathF.Max(across, lineCross - CrossMarginOf(margin));
                }
                else
                {
                    float room = MathF.Max(0f, lineCross - CrossMarginOf(margin) - across);
                    acrossOffset = item.Alignment switch
                    {
                        Alignment.Center => room / 2f,
                        Alignment.Far => room,
                        _ => 0f,
                    };
                }

                if (place)
                {
                    float mainStart = position + (horizontal ? margin.Left : margin.Top);
                    float crossStart = cross + (horizontal ? margin.Top : margin.Left) + acrossOffset;

                    item.Control.Bounds = horizontal
                        ? new SKRect(mainStart, crossStart, mainStart + own, crossStart + across)
                        : new SKRect(crossStart, mainStart, crossStart + across, mainStart + own);
                }

                position += MainOf(item.Size) + MainMarginOf(margin);
                if (i + 1 < index)
                {
                    position += _spacing;
                }
            }

            extentMain = MathF.Max(extentMain, position - lineOrigin);
            extentCross += lineCross;
            cross += lineCross;

            if (index < _items.Count)
            {
                cross += _spacing;
            }
        }

        return horizontal
            ? new SKSize(extentMain, extentCross)
            : new SKSize(extentCross, extentMain);
    }

    private float MainOf(SKSize size) =>
        _orientation == StackOrientation.Horizontal ? size.Width : size.Height;

    private float CrossOf(SKSize size) =>
        _orientation == StackOrientation.Horizontal ? size.Height : size.Width;

    private float MainMarginOf(Thickness margin) =>
        _orientation == StackOrientation.Horizontal ? margin.Horizontal : margin.Vertical;

    private float CrossMarginOf(Thickness margin) =>
        _orientation == StackOrientation.Horizontal ? margin.Vertical : margin.Horizontal;

    /// <summary>One item of the run and what the current pass knows about it.</summary>
    private readonly struct FlowItem
    {
        public FlowItem(Control control, SKSize size, Thickness margin, Alignment alignment)
        {
            Control = control;
            Size = size;
            Margin = margin;
            Alignment = alignment;
        }

        public Control Control { get; }

        public SKSize Size { get; }

        public Thickness Margin { get; }

        public Alignment Alignment { get; }
    }
}
