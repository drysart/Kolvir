using Kolvir.Observables;

namespace Kolvir.Demo.VirtualDom;

/// <summary>
/// One document the sample has open, and the observable title a re-render reads.
/// </summary>
/// <remarks>
/// The document identity is the object itself, which is what the application render uses as a
/// window key. Its title is an observable, so changing it re-renders both the document's window
/// title and the list window's readout without either of them knowing the other exists.
/// </remarks>
internal sealed class DocumentModel
{
    public DocumentModel(int id, string title)
    {
        Id = id;
        Title = Observable.CreateValue(title);
    }

    public int Id { get; }

    public IObservableMutableValue<string> Title { get; }
}
