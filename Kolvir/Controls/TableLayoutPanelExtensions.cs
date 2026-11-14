using System.Runtime.CompilerServices;

namespace Kolvir;

/// <summary>
/// The cell a control occupies in a <see cref="TableLayoutPanel"/>, as state attached to the control
/// rather than held by the table.
/// </summary>
/// <remarks>
/// A control has one parent, so its cell is a property of the control: it can be set in the
/// initializer that creates it, before the table it will belong to exists as a variable, and it stays
/// with the control if the control moves to another table. The table reads it while it arranges its
/// children and keeps no copy of its own - the division WPF draws between an attached property and
/// the panel that reads it.
///
/// The state lives in a <see cref="ConditionalWeakTable{TKey,TValue}"/>, so nothing is added to
/// <see cref="Control"/>'s own surface and the entry goes away with the control. The properties below
/// are extension members, which a consumer needs C# 14 to use;
/// <see cref="TableLayoutPanel.SetCellPosition"/> and <see cref="TableLayoutPanel.GetCellPosition"/>
/// reach the same state from any language version.
/// </remarks>
public static class TableLayoutPanelExtensions
{
    private static readonly ConditionalWeakTable<Control, TableCellInfo> Cells = new();

    /// <summary>The cell a control occupies, or (0, 0) where it has never been given one.</summary>
    internal static (int Column, int Row) GetCell(Control control) =>
        Cells.TryGetValue(control, out var cell) ? (cell.Column, cell.Row) : (0, 0);

    /// <summary>The cell a control covers, or one track each way where it has never been given one.</summary>
    internal static (int ColumnSpan, int RowSpan) GetSpan(Control control) =>
        Cells.TryGetValue(control, out var cell) ? (cell.ColumnSpan, cell.RowSpan) : (1, 1);

    /// <summary>
    /// Moves a control to a cell. Nothing here looks for the table: the state goes on the control,
    /// and the parent is needed only for the layout the move invalidates - the counterpart of the
    /// <c>AffectsParentArrange</c> flag WPF puts on such a property. The parent is null while a
    /// control is still being initialized, which is the common case and costs nothing.
    /// </summary>
    internal static void SetCell(Control control, int column, int row)
    {
        var cell = Cells.GetOrCreateValue(control);
        cell.Column = Math.Max(0, column);
        cell.Row = Math.Max(0, row);
        InvalidateParent(control);
    }

    /// <summary>Widens a control onto the tracks next to its cell, keeping at least one track each way.</summary>
    internal static void SetSpan(Control control, int columnSpan, int rowSpan)
    {
        var cell = Cells.GetOrCreateValue(control);
        cell.ColumnSpan = Math.Max(1, columnSpan);
        cell.RowSpan = Math.Max(1, rowSpan);
        InvalidateParent(control);
    }

    private static void InvalidateParent(Control control)
    {
        if (control.Parent is { } parent)
        {
            parent.PerformLayout();
            parent.Invalidate();
        }
    }

    extension(Control control)
    {
        /// <summary>
        /// The column this control occupies in its <see cref="TableLayoutPanel"/>. Setting it while
        /// the control is in a table moves the control and lays the table out again.
        /// </summary>
        public int TableColumn
        {
            get => GetCell(control).Column;
            set => SetCell(control, value, GetCell(control).Row);
        }

        /// <summary>
        /// The row this control occupies in its <see cref="TableLayoutPanel"/>. Setting it while the
        /// control is in a table moves the control and lays the table out again.
        /// </summary>
        public int TableRow
        {
            get => GetCell(control).Row;
            set => SetCell(control, GetCell(control).Column, value);
        }

        /// <summary>
        /// How many columns this control covers, counting the one its <c>TableColumn</c>
        /// names. One unless set; a span that would run past the last column stops at it.
        /// </summary>
        public int TableColumnSpan
        {
            get => GetSpan(control).ColumnSpan;
            set => SetSpan(control, value, GetSpan(control).RowSpan);
        }

        /// <summary>
        /// How many rows this control covers, counting the one its <c>TableRow</c> names.
        /// One unless set; a span that would run past the last row stops at it.
        /// </summary>
        public int TableRowSpan
        {
            get => GetSpan(control).RowSpan;
            set => SetSpan(control, GetSpan(control).ColumnSpan, value);
        }
    }

    /// <summary>What is attached to a control that has been given a cell or a span.</summary>
    private sealed class TableCellInfo
    {
        public int Column;

        public int Row;

        public int ColumnSpan = 1;

        public int RowSpan = 1;
    }
}
