using Kolvir.Feature.Observables;
using Kolvir.Feature.VirtualDom;

namespace Kolvir.Demo.VirtualDom;

/// <summary>
/// The control component inside one document window: the window's body, rendered from its document.
/// </summary>
/// <remarks>
/// This is the ordinary control-level composition the application level sits above. The window's
/// <see cref="VWindow.Content"/> is a bare <see cref="VComponent"/> wrapping this page, so the page
/// keeps its own mount and its own render count while the application component owns only the
/// window's presence and title.
/// </remarks>
internal sealed class DocumentPage : ObservableComponent<DocumentModel>
{
    private readonly Action<DocumentModel> _rename;
    private readonly Action<DocumentModel> _close;

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
        ],
    };
}
