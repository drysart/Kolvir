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
            new VTableLayoutPanel
            {
                Id = "cells",
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 132f,
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
