using Kolvir.Observables;
using SkiaSharp;

namespace Kolvir.Demo.VirtualDom;

/// <summary>
/// One document the sample has open, and the observable title a re-render reads.
/// </summary>
/// <remarks>
/// The document identity is the object itself, which is what the application render uses as a
/// window key. Its title is an observable, so changing it re-renders both the document's window
/// title and the list window's readout without either of them knowing the other exists. Its assigned
/// window position is fixed when the document is opened: deriving it from the document's index would
/// move every later window when an earlier document closes, which is the application fighting the
/// user for no reason.
/// </remarks>
internal sealed class DocumentModel
{
    public DocumentModel(int id, string title, SKPoint location)
    {
        Id = id;
        Title = Observable.CreateValue(title);
        Location = location;
    }

    public int Id { get; }

    public IObservableMutableValue<string> Title { get; }

    /// <summary>The position the application assigns this document's window.</summary>
    public SKPoint Location { get; }
}
