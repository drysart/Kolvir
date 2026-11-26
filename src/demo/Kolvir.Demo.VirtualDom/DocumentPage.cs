using Kolvir.Feature.Observables;
using Kolvir.Feature.VirtualDom;

namespace Kolvir.Demo.VirtualDom;

/// <summary>
/// The control component inside one document window: the window's body, rendered from its document.
/// </summary>
/// <remarks>
/// <para>
/// This is the ordinary control-level composition the application level sits above. The window's
/// <see cref="VWindow.Content"/> is a bare <see cref="VComponent"/> wrapping this page, so the page
/// keeps its own mount and its own render count while the application component owns only the
/// window's presence and title.
/// </para>
/// <para>
/// The table at the bottom is the sample's use of extension members. Its children are ordinary labels
/// and buttons, and where each one sits in the grid - <c>TableColumn</c>, <c>TableRow</c> and the two
/// spans - are extension properties of <see cref="Control"/> that the generator found and put on their
/// nodes as if they were native. Nothing in this file mentions
/// <see cref="TableLayoutPanelExtensions"/>. The text box below it shows the other half: <c>UndoEnabled</c>
/// and <c>UndoStateChanged</c> are an extension property and a handler pair declared inside one
/// <c>extension(TextBox)</c> block, and the node exposes them exactly as it exposes <c>Text</c>.
/// </para>
/// </remarks>
internal sealed class DocumentPage : ObservableComponent<DocumentModel>
{
    private readonly Action<DocumentModel> _rename;
    private readonly Action<DocumentModel> _close;
    private readonly IObservableMutableValue<int> _row = Observable.CreateValue(1);
    private readonly IObservableMutableValue<int> _shape = Observable.CreateValue(0);
    private readonly IObservableMutableValue<string> _activity = Observable.CreateValue("undo: nothing yet");
    private int _undoChanges;

    public DocumentPage(DocumentModel document, Action<DocumentModel> rename, Action<DocumentModel> close)
    {
        DataModel = document;
        _rename = rename;
        _close = close;
    }

    protected override VNode RenderWithDataModel(RenderArgs<DocumentModel> args) => new VPanel
    {
        Dock = DockStyle.Fill,
        Padding = new Thickness(16f),
        Children =
        [
            new VLabel
            {
                Id = "title",
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 28f,
                Text = args.DataModel.Title.Value,
            },
            new VLabel
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 24f,
                Text = $"Document #{args.DataModel.Id}",
            },
            new VButton
            {
                Id = "rename",
                Dock = DockStyle.Top,
                AutoSize = true,
                Text = "Rename this document",
                Click = _ => _rename(args.DataModel),
            },
            new VButton
            {
                Id = "close",
                Dock = DockStyle.Top,
                AutoSize = true,
                Text = "Close this document",
                Click = _ => _close(args.DataModel),
            },
            new VButton
            {
                Id = "reshape",
                Dock = DockStyle.Top,
                AutoSize = true,
                Text = "Change the table's rows",
                Click = _ => _shape.Value = (_shape.Value + 1) % Shapes,
            },
            new VTableLayoutPanel
            {
                Id = "cells",
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 88f,

                // The table's row and column definitions are ordinary settable properties on the node,
                // assigned whole rather than one row at a time. Each render builds a fresh sequence,
                // and the control compares it with the one it already has: an equal sequence costs
                // nothing at all, and a changed one is a single assignment that lays the table out once.
                RowStyles = Rows(_shape.Value),
                ColumnStyles =
                [
                    new ColumnStyle(SizeType.Absolute, 160f),
                    new ColumnStyle(SizeType.Percent, 100f),
                ],
                Children =
                [
                    new VLabel
                    {
                        Id = "cell-title",
                        TableColumn = 0,
                        TableRow = 0,
                        TableColumnSpan = 2,
                        AutoSize = false,
                        Height = 24f,
                        Text = "Where the cells are is an extension property",
                    },
                    new VButton
                    {
                        Id = "shift",
                        TableColumn = 0,
                        TableRow = _row.Value,
                        AutoSize = true,
                        Text = "Move this button down a row",
                        Click = _ => Shift(),
                    },
                    new VButton
                    {
                        Id = "popup",
                        TableColumn = 1,
                        TableRow = 1,
                        TableRowSpan = 2,
                        AutoSize = true,
                        Text = "Show a popup",
                        Click = anchor => ShowPopup((Control)anchor),
                    },
                ],
            },
            new VTextBox
            {
                Id = "notes",
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 28f,
                Text = "Type here",

                // Both from one extension(TextBox) block. UndoEnabled is an extension property and
                // UndoStateChanged is the handler pair behind AddUndoStateChangedHandler, exposed on
                // the node exactly as Click is.
                UndoEnabled = true,
                UndoStateChanged = _ => _activity.Value = $"undo: changed {++_undoChanges} time(s)",
            },
            new VLabel
            {
                Id = "activity",
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 24f,
                Text = _activity.Value,
            },
        ],
    };

    /// <summary>
    /// Moves the button to the row below. This is the sample's <c>PatchFrom</c> path for an extension
    /// property: the next render leaves the old row assigned on the previous node, so the patch has to
    /// take the control out of the cell it had and put it in the new one.
    /// </summary>
    private void Shift() => _row.Value = _row.Value == 1 ? 2 : 1;

    /// <summary>How many row sequences the reshape button cycles through.</summary>
    private const int Shapes = 5;

    /// <summary>
    /// The row definitions for one shape, as the node property takes them: a whole sequence, not an
    /// <c>Add</c> per row. The three that matter are a changed value, a reordering and a sequence that
    /// is shorter than the one before - and the last is empty, which is a table with no declared rows.
    /// </summary>
    private static IList<RowStyle> Rows(int shape) => shape switch
    {
        1 => [new RowStyle(SizeType.Absolute, 40f), new RowStyle(SizeType.Percent, 100f)],
        2 => [new RowStyle(SizeType.Percent, 100f), new RowStyle(SizeType.Absolute, 40f)],
        3 => [new RowStyle(SizeType.Percent, 100f)],
        4 => [],
        _ => [new RowStyle(SizeType.Absolute, 24f), new RowStyle(SizeType.Percent, 100f)],
    };

    private void ShowPopup(Control anchor)
    {
        var content = new Label
        {
            Text = "Popup content",
            Padding = new Thickness(8f),
            AutoSize = true,
        };

        _ = anchor.ShowPopupAsync(content);
    }
}
