using SkiaSharp;

namespace Kolvir.Feature.InView;

/// <summary>
/// One pass over a host's paint tree, in paint order, answering "is any part of this control not
/// covered" for a set of controls.
/// </summary>
/// <remarks>
/// <para>
/// The walk mirrors <see cref="UiHost.Paint"/>: the menu bar, then the root tree, then the overlays
/// in the order they were added. Inside a control, children are visited in
/// <see cref="Control.Controls"/> order - later children paint on top - and a
/// <see cref="Panel"/>'s two scrollbars last, because <see cref="Panel"/> paints them after the
/// scrolled content and they are not in <see cref="Control.Controls"/> at all.
/// </para>
/// <para>
/// Each visited control is given a paint rank. A control covers a target only if its rank is greater
/// than the target's subtree end, which is the same as "painted after everything the target paints",
/// so the target's own descendants never count as occluding it. Coverage is
/// <see cref="Control.Opaque"/> - the library's own "paints every pixel of its bounds" contract - and
/// nothing else; see dev-docs/EFFECTIVE-IN-VIEW.md for what that can and cannot report.
/// </para>
/// <para>
/// The surviving area is computed by subtracting each covering rectangle, from the highest rank down,
/// which is exact for the rectangle model and collapses to nothing in one subtraction when a popup
/// covers the whole target. The walk happens once per pass for every listener, not once per
/// listener, which is what keeps the cost proportional to the number of listeners rather than to the
/// tree.
/// </para>
/// </remarks>
internal sealed class InViewSnapshot
{
    /// <summary>How many rectangles a target's surviving area is kept in before the tail is dropped.</summary>
    /// <remarks>
    /// Subtracting many small occluders from one rectangle can split it into pieces without bound. The
    /// cap is far above anything a normal tree produces (one coverer leaves at most four pieces) and
    /// dropping the tail can only hide a sliver, which is the same failure a sampled test would have.
    /// </remarks>
    private const int MaxPieces = 32;

    /// <summary>How many grid cells a target may span before the lookup falls back to every occluder.</summary>
    private const int MaxQueryCells = 2048;

    /// <summary>Cell width and height of the occluder grid, in logical pixels.</summary>
    private const float CellSize = 128f;

    private readonly HashSet<Control> _watched;
    private readonly Dictionary<Control, Target> _targets = new();
    private readonly List<Occluder> _occluders = new();
    private readonly Dictionary<(int Row, int Column), List<int>> _cells = new();

    private List<SKRect> _pieces = new();
    private List<SKRect> _scratch = new();
    private readonly List<int> _query = new();

    private int _nextRank;

    private InViewSnapshot(HashSet<Control> watched) => _watched = watched;

    /// <summary>Walks the host's paint tree and remembers the rectangles of the controls asked about.</summary>
    /// <param name="host">The host whose tree to walk.</param>
    /// <param name="watched">The controls to answer for.</param>
    public static InViewSnapshot Build(UiHost host, IReadOnlyList<Control> watched)
    {
        var set = new HashSet<Control>(watched.Count);
        foreach (var control in watched)
        {
            set.Add(control);
        }

        var snapshot = new InViewSnapshot(set);
        var client = new SKRect(0f, 0f, host.ClientSize.Width, host.ClientSize.Height);

        snapshot.Visit(host.MenuBar, client, visible: true);
        snapshot.Visit(host.Root, client, visible: true);

        foreach (var overlay in host.Overlays)
        {
            snapshot.Visit(overlay, client, visible: true);
        }

        snapshot.BuildGrid();
        return snapshot;
    }

    /// <summary>True when some part of <paramref name="control"/> survives clipping and occlusion.</summary>
    /// <param name="control">The control to answer for; one this snapshot was built for.</param>
    /// <remarks>
    /// A control that is not in the tree - detached, hidden under a hidden ancestor, or never laid
    /// out - was never visited, and answers false rather than throwing.
    /// </remarks>
    public bool Evaluate(Control control)
    {
        if (!_targets.TryGetValue(control, out var target) || InViewGeometry.IsEmpty(target.Rect))
        {
            return false;
        }

        _query.Clear();
        Query(target.Rect, _query);
        _query.Sort((left, right) => _occluders[right].Rank.CompareTo(_occluders[left].Rank));

        _pieces.Clear();
        _pieces.Add(target.Rect);

        foreach (int index in _query)
        {
            var occluder = _occluders[index];

            // Anything at or before the target's subtree end is painted under it: the target's own
            // children are part of it, and an earlier sibling cannot cover a pixel the target paints.
            if (occluder.Rank <= target.End)
            {
                continue;
            }

            bool touched = false;

            _scratch.Clear();

            foreach (var piece in _pieces)
            {
                if (InViewGeometry.IsEmpty(InViewGeometry.Intersect(piece, occluder.Rect)))
                {
                    _scratch.Add(piece);
                    continue;
                }

                touched = true;
                InViewGeometry.Subtract(piece, occluder.Rect, _scratch);
            }

            if (!touched)
            {
                continue;
            }

            if (_scratch.Count == 0)
            {
                return false;
            }

            (_pieces, _scratch) = (_scratch, _pieces);
            _scratch.Clear();

            if (_pieces.Count > MaxPieces)
            {
                _pieces.RemoveRange(MaxPieces, _pieces.Count - MaxPieces);
            }
        }

        return _pieces.Count > 0;
    }

    private int Visit(Control? control, SKRect clip, bool visible)
    {
        if (control is null || !visible || !control.Visible)
        {
            return _nextRank - 1;
        }

        var rect = InViewGeometry.Intersect(InViewGeometry.HostRect(control), clip);
        if (InViewGeometry.IsEmpty(rect))
        {
            // Nothing this control or anything inside it paints can reach the clip.
            return _nextRank - 1;
        }

        int rank = _nextRank++;

        if (_watched.Contains(control))
        {
            _targets[control] = new Target(rect);
        }

        if (control.Opaque)
        {
            _occluders.Add(new Occluder(rank, rect));
        }

        // Children are clipped by this control's own area and, for a scrolling container, by its
        // viewport as well. The bars are outside that viewport: the panel paints them in its own
        // space after the content.
        var contentClip = rect;

        if (control is Panel panel && !panel.ViewportRect.IsEmpty)
        {
            contentClip = InViewGeometry.Intersect(
                contentClip,
                InViewGeometry.MapRect(panel, panel.ViewportRect));
        }

        for (int index = 0; index < control.Controls.Count; index++)
        {
            Visit(control.Controls[index], contentClip, visible: true);
        }

        if (control is Panel scroller)
        {
            Visit(scroller.VerticalScrollBar, rect, visible: true);
            Visit(scroller.HorizontalScrollBar, rect, visible: true);
        }

        if (_watched.Contains(control) && _targets.TryGetValue(control, out var target))
        {
            target.End = _nextRank - 1;
        }

        return _nextRank - 1;
    }

    private void BuildGrid()
    {
        for (int index = 0; index < _occluders.Count; index++)
        {
            var rect = _occluders[index].Rect;

            int left = Cell(rect.Left);
            int top = Cell(rect.Top);
            int right = Cell(rect.Right - 0.001f);
            int bottom = Cell(rect.Bottom - 0.001f);

            for (int row = top; row <= bottom; row++)
            {
                for (int column = left; column <= right; column++)
                {
                    var key = (row, column);

                    if (!_cells.TryGetValue(key, out var bucket))
                    {
                        bucket = new List<int>(4);
                        _cells[key] = bucket;
                    }

                    bucket.Add(index);
                }
            }
        }
    }

    private void Query(SKRect rect, List<int> result)
    {
        int left = Cell(rect.Left);
        int top = Cell(rect.Top);
        int right = Cell(rect.Right - 0.001f);
        int bottom = Cell(rect.Bottom - 0.001f);

        long cells = (long)(right - left + 1) * (bottom - top + 1);

        if (cells > MaxQueryCells)
        {
            for (int index = 0; index < _occluders.Count; index++)
            {
                result.Add(index);
            }

            return;
        }

        for (int row = top; row <= bottom; row++)
        {
            for (int column = left; column <= right; column++)
            {
                if (!_cells.TryGetValue((row, column), out var bucket))
                {
                    continue;
                }

                foreach (int index in bucket)
                {
                    if (!result.Contains(index))
                    {
                        result.Add(index);
                    }
                }
            }
        }
    }

    private static int Cell(float value) => (int)MathF.Floor(value / CellSize);

    private readonly struct Occluder
    {
        public Occluder(int rank, SKRect rect)
        {
            Rank = rank;
            Rect = rect;
        }

        public int Rank { get; }

        public SKRect Rect { get; }
    }

    private sealed class Target
    {
        public Target(SKRect rect) => Rect = rect;

        /// <summary>The last rank this control's subtree occupies; anything after it paints above.</summary>
        public int End { get; set; }

        public SKRect Rect { get; }
    }
}
