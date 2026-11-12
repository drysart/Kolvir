using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A container that runs its children along one axis - top to bottom, or left to right - and places
/// each one across the other axis according to its <see cref="Control.HorizontalAlignment"/> or
/// <see cref="Control.VerticalAlignment"/>.
/// </summary>
/// <remarks>
/// <para>
/// A child gets its preferred size along the stacking axis and is, by default, stretched across the
/// stack: the full width of the panel for <see cref="StackOrientation.Vertical"/>, the full
/// height for <see cref="StackOrientation.Horizontal"/>. Setting the matching alignment on a
/// child to <see cref="Alignment.Near"/>, <see cref="Alignment.Center"/> or
/// <see cref="Alignment.Far"/> leaves it at its preferred size across the stack instead, and
/// places it at that edge or in the middle. A child whose <see cref="Control.AutoSize"/> is
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
/// child of an <see cref="Panel"/> is, and the stack fills what is left, which is how a header
/// or a footer is added to a stack.
/// </para>
/// <para>
/// The run of children is the scrollable content, so the panel scrolls like any other:
/// <see cref="Panel.VerticalScroll"/> for a vertical stack, and
/// <see cref="Panel.HorizontalScroll"/> for a horizontal one.
/// </para>
/// </remarks>
public class StackPanel : Panel
{
    private StackOrientation _orientation = StackOrientation.Vertical;
    private float _spacing;

    // The children of the run, by index, in the order the last arrange pass placed them. A run is
    // ordered along the stacking axis and does not overlap there, so hit testing and painting can
    // binary search for the children that matter instead of walking every one of them. Only the
    // order is kept: the geometry is read back from each child, so a child that is resized or moved
    // outside of a layout pass cannot leave the search looking in the wrong place.
    private readonly List<int> _run = [];
    private bool _runValid;

    public StackPanel()
    {
        // A stack is a layout container rather than a card, so it does not paint a surface of its
        // own and its bounds are exactly the run of its children.
        ShowFrame = false;
        Padding = Thickness.Empty;
    }

    /// <summary>The axis the children are stacked along. Defaults to <see cref="StackOrientation.Vertical"/>.</summary>
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
        bool vertical = Orientation == StackOrientation.Vertical;
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

        if (VerticalScroll == ScrollStyle.Always)
        {
            content.Width += ScrollBarThickness;
        }

        if (HorizontalScroll == ScrollStyle.Always)
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

        _run.Clear();
        _runValid = false;

        if (!HasChildren)
        {
            _runValid = true;
            return MeasureExtent(content);
        }

        bool vertical = Orientation == StackOrientation.Vertical;

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

        // The extent is the union of the run, so it is taken from the same pass that places the
        // children instead of walking them all a second time.
        float right = content.Right;
        float bottom = content.Bottom;

        for (int i = 0; i < Controls.Count; i++)
        {
            var child = Controls[i];
            if (!child.Visible || child.Dock != DockStyle.None)
            {
                continue;
            }

            var margin = child.Margin;
            float crossBox = MathF.Max(0f, crossAvailable - (vertical ? margin.Horizontal : margin.Vertical));
            float crossPosition = crossOrigin + (vertical ? margin.Left : margin.Top);

            var alignment = vertical ? child.HorizontalAlignment : child.VerticalAlignment;
            bool stretch = alignment == Alignment.Stretch;

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
                    Alignment.Center => (crossBox - crossLength) / 2f,
                    Alignment.Far => crossBox - crossLength,
                    _ => 0f,
                };
            }

            if (!first)
            {
                main += Spacing;
            }

            first = false;

            float mainPosition = main + (vertical ? margin.Top : margin.Left);
            var slot = Slot(mainPosition, mainLength, crossPosition, crossLength);
            child.Bounds = slot;
            main = mainPosition + mainLength + (vertical ? margin.Bottom : margin.Right);

            _run.Add(i);

            var outer = Geometry.Inflate(slot, margin);
            right = MathF.Max(right, outer.Right);
            bottom = MathF.Max(bottom, outer.Bottom);
        }

        _runValid = true;
        return new SKSize(MathF.Max(0f, right - content.Left), MathF.Max(0f, bottom - content.Top));
    }

    /// <summary>Paints the children that can be seen, in the order they were declared.</summary>
    protected override void RenderScrolledChildren(SKCanvas canvas)
    {
        if (!_runValid)
        {
            base.RenderScrolledChildren(canvas);
            return;
        }

        var docked = DockedChildren;
        var (first, last) = VisibleRun();

        if (first > last)
        {
            for (int i = 0; i < docked.Count; i++)
            {
                docked[i].Control.Render(canvas);
            }

            return;
        }

        int firstOrder = _run[first];
        int lastOrder = _run[last];

        // Docked children are not part of the run, so they are painted either side of it. They are
        // few: a stack docks a header or a footer, not a run.
        for (int i = 0; i < docked.Count; i++)
        {
            if (docked[i].Order < firstOrder)
            {
                docked[i].Control.Render(canvas);
            }
        }

        // The run children between the first and last visible one are exactly the visible ones,
        // because the run is in declaration order and the visible children are a contiguous part of
        // it. Walking the declaration order through that span keeps docked children in the right
        // place and paints the rest of the run not at all.
        for (int i = firstOrder; i <= lastOrder; i++)
        {
            Controls[i].Render(canvas);
        }

        for (int i = 0; i < docked.Count; i++)
        {
            if (docked[i].Order > lastOrder)
            {
                docked[i].Control.Render(canvas);
            }
        }
    }

    /// <summary>
    /// Finds the child under a point by searching the run along the stacking axis instead of
    /// walking every child.
    /// </summary>
    protected override Control? HitTestChildren(SKPoint local)
    {
        if (!_runValid)
        {
            return base.HitTestChildren(local);
        }

        // Children are positioned in content coordinates and painted through the offset, so the
        // point is moved into that space to match them.
        var offset = ContentOffset;
        var content = new SKPoint(local.X - offset.X, local.Y - offset.Y);
        float main = Orientation == StackOrientation.Vertical ? content.Y : content.X;

        // Docked children are not in the run, so they are checked one at a time, topmost first.
        var docked = DockedChildren;
        Control? dockedHit = null;
        int dockedOrder = -1;
        for (int i = docked.Count - 1; i >= 0; i--)
        {
            if (docked[i].Control.HitTest(content) is { } hit)
            {
                dockedHit = hit;
                dockedOrder = docked[i].Order;
                break;
            }
        }

        var slot = RunAt(main);
        if (slot >= 0)
        {
            // The run does not overlap along the stacking axis, so this is the only child of the
            // run that can contain the point; declaration order only decides it against a docked
            // child that happens to cover the same place.
            int order = _run[slot];
            if (order > dockedOrder && Controls[order].HitTest(content) is { } hit)
            {
                return hit;
            }
        }

        return dockedHit;
    }

    internal override void OnControlAdded(Control control)
    {
        _runValid = false;
        base.OnControlAdded(control);
    }

    internal override void OnControlRemoved(Control control)
    {
        _runValid = false;
        base.OnControlRemoved(control);
    }

    internal override void OnChildVisibleChanged(Control child)
    {
        // Hiding or showing a child does not re-run layout, so the cached run would keep painting
        // and hit testing the child where it used to be. Dropping the cache falls back to walking
        // the children until the next arrange pass rebuilds it.
        _runValid = false;
        base.OnChildVisibleChanged(child);
    }

    /// <summary>The run slots that can be seen, as an inclusive range; empty when none can.</summary>
    private (int First, int Last) VisibleRun()
    {
        if (_run.Count == 0)
        {
            return (0, -1);
        }

        bool vertical = Orientation == StackOrientation.Vertical;
        var offset = ContentOffset;

        // The run's slot positions and the offset are content space, so the viewport is taken in
        // that space too: a zoomed panel's viewport covers less content than panel space.
        var viewport = ContentViewportInContentSpace;

        // Content is painted at the offset, so a child is on screen when its own band, shifted by
        // the offset, overlaps the viewport.
        float shift = vertical ? offset.Y : offset.X;
        float low = (vertical ? viewport.Top : viewport.Left) - shift;
        float high = (vertical ? viewport.Bottom : viewport.Right) - shift;

        int first = FirstSlotEndingAfter(low);
        int last = LastSlotStartingBefore(high);
        return (first, last);
    }

    private int FirstSlotEndingAfter(float position)
    {
        int low = 0;
        int high = _run.Count - 1;
        int found = _run.Count;

        while (low <= high)
        {
            int mid = (low + high) >> 1;
            if (MainEndOf(_run[mid]) > position)
            {
                found = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        return found;
    }

    private int LastSlotStartingBefore(float position)
    {
        int low = 0;
        int high = _run.Count - 1;
        int found = -1;

        while (low <= high)
        {
            int mid = (low + high) >> 1;
            if (MainStartOf(_run[mid]) < position)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found;
    }

    /// <summary>The run slot whose band contains a position along the stacking axis, or -1.</summary>
    private int RunAt(float main)
    {
        int low = 0;
        int high = _run.Count - 1;

        while (low <= high)
        {
            int mid = (low + high) >> 1;
            int index = _run[mid];

            if (main < MainStartOf(index))
            {
                high = mid - 1;
            }
            else if (main >= MainEndOf(index))
            {
                low = mid + 1;
            }
            else
            {
                return mid;
            }
        }

        return -1;
    }

    private float MainStartOf(int index)
    {
        var bounds = Controls[index].Bounds;
        return Orientation == StackOrientation.Vertical ? bounds.Top : bounds.Left;
    }

    private float MainEndOf(int index)
    {
        var bounds = Controls[index].Bounds;
        return Orientation == StackOrientation.Vertical ? bounds.Bottom : bounds.Right;
    }
}
