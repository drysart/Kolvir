using Kolvir;
using Kolvir.Observables;
using SkiaSharp;

namespace Kolvir.Demo.VirtualDom;

/// <summary>
/// One document the sample has open, and the observable values a re-render reads.
/// </summary>
/// <remarks>
/// <para>
/// The document identity is the object itself, which is what the application render uses as a
/// window key. Its title is an observable, so changing it re-renders both the document's window
/// title and the list window's readout without either of them knowing the other exists.
/// </para>
/// <para>
/// The model keeps two sets of geometry. <see cref="HomeLocation"/> and
/// <see cref="HomeClientSize"/> are the layout positions the application assigns, fixed when the
/// document is opened: deriving them from the document's index would move every later window when
/// an earlier document closes, which is the application fighting the user for no reason.
/// <see cref="Location"/>, <see cref="ClientSize"/> and <see cref="State"/> are what the window
/// actually reports, written from its notifications. The render assigns the live values, so the
/// user's own move or resize comes back as an unchanged assignment - which the patch rule leaves
/// alone - and the model and the window settle on the same answer instead of echoing each other.
/// "Tidy" writes the home values back into the live ones.
/// </para>
/// </remarks>
internal sealed class DocumentModel
{
    public DocumentModel(int id, string title, SKPoint location, SKSize clientSize)
    {
        Id = id;
        Title = Observable.CreateValue(title);
        HomeLocation = location;
        HomeClientSize = clientSize;
        Location = Observable.CreateValue(location);
        ClientSize = Observable.CreateValue(clientSize);
        State = Observable.CreateValue(WindowState.Normal);
    }

    public int Id { get; }

    public IObservableMutableValue<string> Title { get; }

    /// <summary>The position the application assigns this document's window when it is tidied.</summary>
    public SKPoint HomeLocation { get; }

    /// <summary>The size the application assigns this document's window when it is tidied.</summary>
    public SKSize HomeClientSize { get; }

    /// <summary>The position the window reports, kept up to date by the node's <c>Moved</c> handler.</summary>
    public IObservableMutableValue<SKPoint> Location { get; }

    /// <summary>The size the window reports, kept up to date by the node's <c>Resized</c> handler.</summary>
    public IObservableMutableValue<SKSize> ClientSize { get; }

    /// <summary>The state the window reports, kept up to date by the node's <c>StateChanged</c> handler.</summary>
    public IObservableMutableValue<WindowState> State { get; }
}
