using SkiaSharp;
using Kolvir.Observables;
using Kolvir.VirtualDom;

namespace Kolvir.Demo.VirtualDom;

/// <summary>
/// The sample application: a list window and one keyed window per open document, all rendered from
/// the same observable model.
/// </summary>
/// <remarks>
/// This is what an application-level component looks like. It derives from
/// <see cref="ApplicationComponent"/>, reads its model in <see cref="Render"/>, and the collection
/// scope makes every value it read a subscription. Adding a document opens its window, removing one
/// closes its window, and changing a title re-titles both the document's window and the list's
/// readout - all through ordinary observable changes and one render per frame. The document windows
/// also write the user's own moves, resizes and state changes into the model, so the readout tracks
/// the windows rather than the other way round. "Tidy windows" resets those live values and
/// increments the geometry token, which is the one way a render may take back a position or size the
/// user changed.
/// </remarks>
internal sealed class DocumentApplication : ApplicationComponent
{
    private readonly IObservableList<DocumentModel> _documents = Observable.CreateList<DocumentModel>();
    private readonly Dictionary<DocumentModel, DocumentPage> _pages = [];
    private readonly IObservableMutableValue<int> _geometryToken = Observable.CreateValue(0);
    private int _nextId = 1;
    private int _renames;

    public DocumentApplication() => AddDocument();

    protected override IReadOnlyList<ApplicationVNode> Render()
    {
        var count = _documents.Count;
        var geometryToken = _geometryToken.Value;
        var windows = new List<ApplicationVNode>(count + 1)
        {
            new VWindow
            {
                Key = "documents",
                Title = "Kolvir - Documents",
                ClientSize = new SKSize(480f, 320f),
                MinimumClientSize = new SKSize(360f, 220f),
                Content = BuildListContent(count),
            },
        };

        for (int i = 0; i < count; i++)
        {
            var document = _documents[i];

            // The live values are read from the model, so a move or resize the user made is
            // assigned again unchanged on the next render. The patch rule leaves an unchanged
            // assignment alone, which is what keeps the window where the user put it. Incrementing
            // the token is the one thing that takes assigned geometry back.
            windows.Add(new VWindow
            {
                Key = document,
                Title = $"{document.Title.Value} - document",
                ClientSize = document.ClientSize.Value,
                Location = document.Location.Value,
                State = document.State.Value,
                GeometryToken = geometryToken,
                CloseRequested = _ => Remove(document),
                Moved = (_, location) => document.Location.Value = location,
                Resized = (_, size) => document.ClientSize.Value = size,
                StateChanged = (_, state) => document.State.Value = state,
                Content = new VComponent(_pages[document]),
            });
        }

        return windows;
    }

    private VNode BuildListContent(int count)
    {
        var summaries = new List<string>(count);

        for (int i = 0; i < count; i++)
        {
            var document = _documents[i];
            var location = document.Location.Value;
            var clientSize = document.ClientSize.Value;

            // Reading the live geometry here is what makes this label follow the user: the node's
            // Moved/Resized/StateChanged handlers write the model, the write invalidates this render,
            // and the readout shows where the window actually is rather than what the render asked
            // for.
            summaries.Add(
                $"{document.Title.Value} at {location.X:0},{location.Y:0} " +
                $"{clientSize.Width:0}x{clientSize.Height:0} {document.State.Value}");
        }

        var readout = count == 0
            ? "The model says no document windows are open."
            : $"The model says {count} document window(s) are open: {string.Join("; ", summaries)}";

        return new VPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Thickness(16f),
            Children =
            [
                new VLabel
                {
                    Dock = DockStyle.Top,
                    AutoSize = false,
                    Height = 28f,
                    Text = "Application-level virtual DOM",
                },
                new VLabel
                {
                    Id = "readout",
                    Dock = DockStyle.Top,
                    AutoSize = false,
                    Height = 44f,
                    Text = readout,
                },
                new VButton
                {
                    Id = "open",
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    Text = "Open a document",
                    Click = _ => AddDocument(),
                },
                new VButton
                {
                    Id = "rename",
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    Enabled = count > 0,
                    Text = "Rename the first document",
                    Click = _ => RenameFirst(),
                },
                new VButton
                {
                    Id = "tidy",
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    Enabled = count > 0,
                    Text = "Tidy windows",
                    Click = _ => TidyWindows(),
                },
                new VButton
                {
                    Id = "quit",
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    Text = "Quit",
                    Click = _ => Application.Current?.Exit(),
                },
            ],
        };
    }

    private void AddDocument()
    {
        var offset = (_nextId - 1) * 28f;
        var document = new DocumentModel(
            _nextId,
            $"Document {_nextId}",
            new SKPoint(160f + offset, 60f + offset),
            new SKSize(480f, 300f));
        _nextId++;

        // The page is kept with the document so a render of the list reuses the same component
        // instance, and the child's mount and controls survive the application's own re-render.
        _pages.Add(document, new DocumentPage(document, Rename, Remove));
        _documents.Add(document);
    }

    /// <summary>
    /// Returns every document window to the layout the model opened it at. The live values are reset
    /// first - the model is meant to track the user, so putting the windows back is a model change
    /// too - and the token bump re-asserts the assigned geometry in the same render.
    /// </summary>
    private void TidyWindows()
    {
        foreach (var document in _documents)
        {
            document.Location.Value = document.HomeLocation;
            document.ClientSize.Value = document.HomeClientSize;
            document.State.Value = WindowState.Normal;
        }

        _geometryToken.Value++;
    }

    private void RenameFirst()
    {
        if (_documents.Count > 0)
        {
            Rename(_documents[0]);
        }
    }

    private void Rename(DocumentModel document) =>
        document.Title.Value = $"Document {document.Id} renamed {++_renames}";

    private void Remove(DocumentModel document)
    {
        _pages.Remove(document);
        _documents.Remove(document);
    }
}
