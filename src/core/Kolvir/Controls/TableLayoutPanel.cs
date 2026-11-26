using System.Collections;
using System.Collections.Generic;
using SkiaSharp;

namespace Kolvir;

/// <summary>
/// Arranges children on a grid of rows and columns. Sizing follows WinForms:
/// <see cref="SizeType.Absolute"/> rows/columns take a fixed size,
/// <see cref="SizeType.Percent"/> ones share the leftover space and
/// <see cref="SizeType.AutoSize"/> ones fit their content.
/// </summary>
public class TableLayoutPanel : Panel
{
    private readonly List<RowStyle> _rowStyles = new();
    private readonly List<ColumnStyle> _columnStyles = new();
    private readonly IList<RowStyle> _rowStyleView;
    private readonly IList<ColumnStyle> _columnStyleView;

    /// <summary>Creates a table with no rows or columns and four pixels of space around each cell.</summary>
    public TableLayoutPanel()
    {
        Padding = new Thickness(0f);
        CellPadding = new Thickness(4f);

        // One view per collection for the life of the table, so a caller that kept the reference
        // from the getter still writes to the table's current styles after the setter replaced them.
        _rowStyleView = new StyleView<RowStyle>(this, _rowStyles);
        _columnStyleView = new StyleView<ColumnStyle>(this, _columnStyles);
    }

    /// <summary>The row definitions, in the order they were added.</summary>
    /// <remarks>
    /// <para>
    /// The getter hands out the live list: <c>Add</c>, <c>Insert</c>, <c>Remove</c>, <c>Clear</c> and
    /// assigning to an index all lay the table out again, so a row added here takes effect without a
    /// second call. Each mutation is one layout pass, so filling a table row by row is cheaper inside
    /// <see cref="Control.SuspendLayout"/> and <see cref="Control.ResumeLayout()"/>, which fold them
    /// into one.
    /// </para>
    /// <para>
    /// The setter replaces the whole sequence at once. It compares the new sequence with the current
    /// one by value, in order, using <see cref="RowOrColumnStyle.Equals(RowOrColumnStyle)"/>; when the
    /// two say the same thing it does nothing at all - the styles already on the table stay, so the
    /// table is not rebuilt for a render that produced the same rows again. When they differ it
    /// replaces the contents and runs exactly one layout pass.
    /// </para>
    /// <para>
    /// The elements are copied, so changing the sequence after assigning it does not affect the
    /// table. A style cannot be changed in place at all; a different size is a different style.
    /// </para>
    /// </remarks>
    public IList<RowStyle> RowStyles
    {
        get => _rowStyleView;
        set => Replace(_rowStyles, _rowStyleView, value);
    }

    /// <summary>The column definitions, in the order they were added.</summary>
    /// <remarks>
    /// The getter hands out the live list and the setter replaces the whole sequence, with the
    /// behaviour <see cref="RowStyles"/> documents for rows: a mutation lays the table out once, and
    /// an assignment that says the same thing as the current columns does nothing.
    /// </remarks>
    public IList<ColumnStyle> ColumnStyles
    {
        get => _columnStyleView;
        set => Replace(_columnStyles, _columnStyleView, value);
    }

    /// <summary>How many rows the table has, which is the number of row definitions.</summary>
    public int RowCount => _rowStyles.Count;

    /// <summary>How many columns the table has, which is the number of column definitions.</summary>
    public int ColumnCount => _columnStyles.Count;

    /// <summary>Space inserted around every cell.</summary>
    public Thickness CellPadding { get; set; }

    /// <summary>Sets the row and column a child occupies.</summary>
    /// <remarks>
    /// The position is attached to the control, not stored here (see
    /// <see cref="TableLayoutPanelExtensions"/>), so it survives the control being moved to another
    /// table and a child that leaves this one leaves nothing behind.
    /// </remarks>
    public void SetCellPosition(Control control, int column, int row)
    {
        ArgumentNullException.ThrowIfNull(control);
        TableLayoutPanelExtensions.SetCell(control, column, row);
    }

    /// <summary>
    /// The cell a child was given, or (0, 0) where it has never been given one.
    /// </summary>
    public (int Column, int Row) GetCellPosition(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return TableLayoutPanelExtensions.GetCell(control);
    }

    /// <summary>
    /// Sets how many columns and rows a child covers, counting the cell its position names.
    /// </summary>
    /// <remarks>
    /// Attached to the control for the same reason the position is: it is a fact about the child, and
    /// a span that would run past the last track stops at it rather than throwing.
    /// </remarks>
    public void SetCellSpan(Control control, int columnSpan, int rowSpan)
    {
        ArgumentNullException.ThrowIfNull(control);
        TableLayoutPanelExtensions.SetSpan(control, columnSpan, rowSpan);
    }

    /// <summary>
    /// The span a child was given, one track each way where it has never been given one.
    /// </summary>
    public (int ColumnSpan, int RowSpan) GetCellSpan(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return TableLayoutPanelExtensions.GetSpan(control);
    }

    /// <summary>Adds a child at a specific cell.</summary>
    public void Add(Control control, int column, int row)
    {
        ArgumentNullException.ThrowIfNull(control);

        // The cell is set before the control is added so the layout that adding triggers is already
        // the right one. The other order lays the child out at (0, 0) and then again at its cell.
        TableLayoutPanelExtensions.SetCell(control, column, row);
        Controls.Add(control);
    }

    /// <summary>Adds a column sized as <paramref name="sizeType"/> says and lays the table out again.</summary>
    public void AddColumn(SizeType sizeType, float value) => AddColumn(new ColumnStyle(sizeType, value));

    /// <summary>Adds a row sized as <paramref name="sizeType"/> says and lays the table out again.</summary>
    public void AddRow(SizeType sizeType, float value) => AddRow(new RowStyle(sizeType, value));

    /// <summary>Adds a column definition and lays the table out again.</summary>
    /// <remarks>
    /// The same thing as <see cref="ColumnStyles"/>'s <c>Add</c>, and it goes through it so the two
    /// cannot come to differ: one call is one layout pass.
    /// </remarks>
    public void AddColumn(ColumnStyle style) => _columnStyleView.Add(style);

    /// <summary>Adds a row definition and lays the table out again.</summary>
    /// <remarks>
    /// The same thing as <see cref="RowStyles"/>'s <c>Add</c>, through the same path: one call is one
    /// layout pass.
    /// </remarks>
    public void AddRow(RowStyle style) => _rowStyleView.Add(style);

    /// <summary>Lays the table out after its styles changed, once, however many changed.</summary>
    private void OnStylesChanged()
    {
        PerformLayout();
        Invalidate();
    }

    /// <summary>
    /// Replaces a whole sequence of styles, doing nothing at all when it says what the current one
    /// already says.
    /// </summary>
    /// <remarks>
    /// The comparison is by value and in order, which is what makes an assignment from a render that
    /// rebuilt the same styles free: nothing is touched, so the styles on the table - and the layout
    /// they describe - stay as they were. A sequence that differs replaces the contents and runs one
    /// pass, not one per element.
    /// </remarks>
    private void Replace<T>(List<T> styles, IList<T> view, IList<T> value)
        where T : RowOrColumnStyle
    {
        ArgumentNullException.ThrowIfNull(value);

        // The live view is the one sequence whose contents are the current ones, so reading it while
        // replacing would read what the replacement had already written. It is also the only way a
        // caller can name the current sequence, and that case is always a no-op.
        if (ReferenceEquals(value, view) || Same(styles, value))
        {
            return;
        }

        styles.Clear();

        foreach (var style in value)
        {
            if (style is null)
            {
                throw new ArgumentException("A table's styles cannot contain null.", nameof(value));
            }

            styles.Add(style);
        }

        OnStylesChanged();
    }

    /// <summary>Whether <paramref name="other"/> holds the same styles, by value and in order.</summary>
    private static bool Same<T>(List<T> styles, IList<T> other)
        where T : RowOrColumnStyle
    {
        if (styles.Count != other.Count)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;

        for (int i = 0; i < styles.Count; i++)
        {
            if (!comparer.Equals(styles[i], other[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    protected override SKSize MeasureContent(SKSize availableSize)
    {
        var (columns, rows) = ComputeTrackSizes(availableSize);

        float width = 0f;
        float height = 0f;
        foreach (var size in columns)
        {
            width += size;
        }

        foreach (var size in rows)
        {
            height += size;
        }

        return new SKSize(width, height);
    }

    /// <inheritdoc />
    protected override void OnLayout()
    {
        var content = ContentRect;
        var (columns, rows) = ComputeTrackSizes(new SKSize(content.Width, content.Height));

        var columnOffsets = new float[columns.Length + 1];
        for (int i = 0; i < columns.Length; i++)
        {
            columnOffsets[i + 1] = columnOffsets[i] + columns[i];
        }

        var rowOffsets = new float[rows.Length + 1];
        for (int i = 0; i < rows.Length; i++)
        {
            rowOffsets[i + 1] = rowOffsets[i] + rows[i];
        }

        foreach (var child in Controls)
        {
            if (!child.Visible)
            {
                continue;
            }

            var (column, row) = GetCellPosition(child);
            column = Math.Clamp(column, 0, columns.Length - 1);
            row = Math.Clamp(row, 0, rows.Length - 1);

            var (columnSpan, rowSpan) = GetCellSpan(child);
            int lastColumn = Math.Min(column + columnSpan, columns.Length) - 1;
            int lastRow = Math.Min(row + rowSpan, rows.Length) - 1;

            var cell = new SKRect(
                content.Left + columnOffsets[column],
                content.Top + rowOffsets[row],
                content.Left + columnOffsets[lastColumn + 1],
                content.Top + rowOffsets[lastRow + 1]);

            var inner = Geometry.Deflate(cell, CellPadding);
            var childBounds = child.Bounds;
            if (child.AutoSize)
            {
                // A child that wraps is arranged at the cell width it was measured at: drawing it
                // narrower would wrap its content again, and the height it reported was computed for
                // the wider box. One that does not wrap is given only as much of the cell as it asked
                // for, which is what makes an AutoSize track hug its content.
                var desired = child.Measure(new SKSize(inner.Width, inner.Height));
                var width = child.WrapsToItsWidth ? inner.Width : MathF.Min(desired.Width, inner.Width);
                childBounds = new SKRect(
                    childBounds.Left,
                    childBounds.Top,
                    childBounds.Left + width,
                    childBounds.Top + MathF.Min(desired.Height, inner.Height));
                child.SetArrangedBounds(childBounds);
            }

            var size = childBounds.Size;
            var bounds = new SKRect(inner.Left, inner.Top, inner.Left + size.Width, inner.Top + size.Height);
            switch (child.Dock)
            {
                case DockStyle.Fill:
                    bounds = inner;
                    break;
                case DockStyle.Right:
                    bounds = new SKRect(inner.Right - size.Width, inner.Top, inner.Right, inner.Top + size.Height);
                    break;
                case DockStyle.Bottom:
                    bounds = new SKRect(inner.Left, inner.Bottom - size.Height, inner.Left + size.Width, inner.Bottom);
                    break;
            }

            child.SetArrangedBounds(bounds);
        }
    }

    private (float[] Columns, float[] Rows) ComputeTrackSizes(SKSize availableSize)
    {
        // A table can be given children and no styles at all, which is a table whose cells have not
        // been described rather than a table with no cells. Every cell a child asks for gets a track;
        // an undeclared track is AutoSize, the same answer everything else about a cell is given when
        // nobody said otherwise - it is as big as what is in it.
        IList<ColumnStyle> columnStyles = _columnStyles.Count > 0 ? _columnStyles : ImplicitColumns();
        IList<RowStyle> rowStyles = _rowStyles.Count > 0 ? _rowStyles : ImplicitRows();

        var columns = new float[columnStyles.Count];
        var rows = new float[rowStyles.Count];

        for (int i = 0; i < columnStyles.Count; i++)
        {
            var style = columnStyles[i];
            if (style.SizeType == SizeType.Absolute)
            {
                columns[i] = style.Value;
            }
        }

        for (int i = 0; i < rowStyles.Count; i++)
        {
            var style = rowStyles[i];
            if (style.SizeType == SizeType.Absolute)
            {
                rows[i] = style.Value;
            }
        }

        // What each track would be if the table were laid out right now. A percentage column does not
        // get the whole table, and a child that wraps text has to be measured at the width it will be
        // given: measured any wider it reports too few lines, so its row ends up shorter than the text
        // and the text is cut off.
        var known = (float[])columns.Clone();
        DistributePercent(columnStyles, known, availableSize.Width);

        foreach (var child in Controls)
        {
            if (!child.Visible)
            {
                continue;
            }

            var (column, row) = GetCellPosition(child);
            column = Math.Clamp(column, 0, columns.Length - 1);
            row = Math.Clamp(row, 0, rows.Length - 1);

            var (columnSpan, rowSpan) = GetCellSpan(child);
            int lastColumn = Math.Min(column + columnSpan, columns.Length) - 1;
            int lastRow = Math.Min(row + rowSpan, rows.Length) - 1;

            bool autoColumn = HasAutoSize(columnStyles, column, lastColumn);
            bool autoRow = HasAutoSize(rowStyles, row, lastRow);
            if (!autoColumn && !autoRow)
            {
                continue;
            }

            var desired = child.Measure(MeasureBasis(availableSize, known, columnStyles, column, lastColumn));

            // A child that decided its own size cannot be made smaller by the track it is in: it draws at
            // the size it chose, so a track sized to the child's content alone comes out narrower than the
            // child and lets it draw over the next track.
            float wantedWidth = child.HasOwnSize
                ? MathF.Max(desired.Width, child.Bounds.Width)
                : desired.Width;

            float wantedHeight = child.HasOwnSize
                ? MathF.Max(desired.Height, child.Bounds.Height)
                : desired.Height;

            var margin = child.Margin;

            if (autoColumn)
            {
                GrowSpannedTracks(
                    columns,
                    columnStyles,
                    column,
                    lastColumn,
                    wantedWidth + margin.Horizontal + CellPadding.Horizontal);

                // The AutoSize column just grew, so the room the percentage columns share is smaller.
                Array.Copy(columns, known, columns.Length);
                DistributePercent(columnStyles, known, availableSize.Width);
            }

            if (autoRow)
            {
                GrowSpannedTracks(
                    rows,
                    rowStyles,
                    row,
                    lastRow,
                    wantedHeight + margin.Vertical + CellPadding.Vertical);
            }
        }

        DistributePercent(columnStyles, columns, availableSize.Width);
        DistributePercent(rowStyles, rows, availableSize.Height);
        return (columns, rows);
    }

    /// <summary>A track for every column a visible child asks for, when the table declares none.</summary>
    private ColumnStyle[] ImplicitColumns()
    {
        int count = 0;

        foreach (var child in Controls)
        {
            if (child.Visible)
            {
                var (column, _) = GetCellPosition(child);
                var (columnSpan, _) = GetCellSpan(child);
                count = Math.Max(count, column + columnSpan);
            }
        }

        var tracks = new ColumnStyle[Math.Max(1, count)];

        for (int i = 0; i < tracks.Length; i++)
        {
            tracks[i] = ColumnStyle.AutoSize();
        }

        return tracks;
    }

    /// <summary>A track for every row a visible child asks for, when the table declares none.</summary>
    private RowStyle[] ImplicitRows()
    {
        int count = 0;

        foreach (var child in Controls)
        {
            if (child.Visible)
            {
                var (_, row) = GetCellPosition(child);
                var (_, rowSpan) = GetCellSpan(child);
                count = Math.Max(count, row + rowSpan);
            }
        }

        var tracks = new RowStyle[Math.Max(1, count)];

        for (int i = 0; i < tracks.Length; i++)
        {
            tracks[i] = RowStyle.AutoSize();
        }

        return tracks;
    }

    /// <summary>The size to measure a child at: the tracks it spans.</summary>
    /// <remarks>
    /// An AutoSize column is sized by the children in it, so measuring one of those against the column
    /// would be a circle: they still see the whole table. Every other child is measured at the width it
    /// is about to be given, which is what keeps its row's height honest and lets the control reuse the
    /// layout its measurement built when it is arranged.
    /// </remarks>
    private SKSize MeasureBasis(
        SKSize availableSize,
        float[] known,
        IList<ColumnStyle> columnStyles,
        int first,
        int last)
    {
        if (HasAutoSize(columnStyles, first, last) || !float.IsFinite(availableSize.Width))
        {
            return availableSize;
        }

        float width = 0f;
        for (int i = first; i <= last; i++)
        {
            width += known[i];
        }

        return new SKSize(MathF.Max(0f, width - CellPadding.Horizontal), availableSize.Height);
    }

    private static bool HasAutoSize<T>(IList<T> styles, int first, int last)
        where T : RowOrColumnStyle
    {
        for (int i = first; i <= last; i++)
        {
            if (styles[i].SizeType == SizeType.AutoSize)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Grows the AutoSize tracks of a span so together they hold <paramref name="required"/>.
    /// </summary>
    /// <remarks>
    /// A child that spans tracks asks for room over the whole span, so the shortfall is shared equally
    /// between the AutoSize tracks it covers instead of loading the whole of it onto the first. With a
    /// span of one track - the ordinary case - that is the same as sizing that track to the child.
    /// Absolute and percent tracks keep their sizes: content cannot claim them.
    /// </remarks>
    private static void GrowSpannedTracks<T>(float[] sizes, IList<T> styles, int first, int last, float required)
        where T : RowOrColumnStyle
    {
        float spanned = 0f;
        int autoTracks = 0;
        for (int i = first; i <= last; i++)
        {
            spanned += sizes[i];
            if (styles[i].SizeType == SizeType.AutoSize)
            {
                autoTracks++;
            }
        }

        float missing = required - spanned;
        if (missing <= 0f || autoTracks == 0)
        {
            return;
        }

        float share = missing / autoTracks;
        for (int i = first; i <= last; i++)
        {
            if (styles[i].SizeType == SizeType.AutoSize)
            {
                sizes[i] += share;
            }
        }
    }

    private static void DistributePercent<T>(IList<T> styles, float[] sizes, float available)
        where T : RowOrColumnStyle
    {
        float used = 0f;
        float totalPercent = 0f;
        for (int i = 0; i < styles.Count; i++)
        {
            if (styles[i].SizeType == SizeType.Percent)
            {
                totalPercent += styles[i].Value;
            }
            else
            {
                used += sizes[i];
            }
        }

        if (totalPercent <= 0f)
        {
            return;
        }

        float remaining = MathF.Max(0f, available - used);
        for (int i = 0; i < styles.Count; i++)
        {
            if (styles[i].SizeType == SizeType.Percent)
            {
                sizes[i] = remaining * styles[i].Value / totalPercent;
            }
        }
    }

    /// <summary>
    /// The list <see cref="RowStyles"/> and <see cref="ColumnStyles"/> hand out: the table's own
    /// list, with every mutation that changes it followed by one layout pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is why a node can assign styles the way it assigns any other property: mutating the live
    /// list used to add the row without laying the table out again, which left a table that had rows
    /// in its list and the old arrangement on screen, with nothing reported anywhere.
    /// </para>
    /// <para>
    /// Nothing that changes nothing notifies: a <c>Remove</c> that found no match, a <c>Clear</c> of an
    /// already empty list and an indexer assignment of an equal style all leave the table alone, which
    /// is what keeps a patch that rewrites the same styles from costing a pass each time.
    /// </para>
    /// </remarks>
    private sealed class StyleView<T> : IList<T>
        where T : RowOrColumnStyle
    {
        private readonly TableLayoutPanel _table;
        private readonly List<T> _styles;

        internal StyleView(TableLayoutPanel table, List<T> styles)
        {
            _table = table;
            _styles = styles;
        }

        public T this[int index]
        {
            get => _styles[index];
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                // Equal styles say the same thing, so the instance already there stays and nothing is
                // laid out again. A style's kind and its value are the whole of it, and both are fixed.
                if (EqualityComparer<T>.Default.Equals(_styles[index], value))
                {
                    return;
                }

                _styles[index] = value;
                _table.OnStylesChanged();
            }
        }

        public int Count => _styles.Count;

        public bool IsReadOnly => false;

        public void Add(T item)
        {
            ArgumentNullException.ThrowIfNull(item);
            _styles.Add(item);
            _table.OnStylesChanged();
        }

        public void Clear()
        {
            if (_styles.Count == 0)
            {
                return;
            }

            _styles.Clear();
            _table.OnStylesChanged();
        }

        public bool Contains(T item) => _styles.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => _styles.CopyTo(array, arrayIndex);

        public IEnumerator<T> GetEnumerator() => _styles.GetEnumerator();

        public int IndexOf(T item) => _styles.IndexOf(item);

        public void Insert(int index, T item)
        {
            ArgumentNullException.ThrowIfNull(item);
            _styles.Insert(index, item);
            _table.OnStylesChanged();
        }

        public bool Remove(T item)
        {
            // An item that is not there changes nothing, so it does not lay the table out.
            var index = _styles.IndexOf(item);

            if (index < 0)
            {
                return false;
            }

            _styles.RemoveAt(index);
            _table.OnStylesChanged();
            return true;
        }

        public void RemoveAt(int index)
        {
            // RemoveAt throws for an index the list does not have, before anything is notified.
            _styles.RemoveAt(index);
            _table.OnStylesChanged();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
