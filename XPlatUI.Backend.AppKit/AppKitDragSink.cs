using SkiaSharp;

namespace XPlatUI.Backend.AppKit;

/// <summary>
/// What a drag over the view would do, in the backend's own terms. The bits are the backend's, so
/// the assembly can describe a drag without naming any type from the drag and drop feature.
/// </summary>
[Flags]
public enum AppKitDragEffect
{
    /// <summary>Nothing: the view refuses the drag and AppKit shows a "no drop" cursor.</summary>
    None = 0,

    /// <summary>The view takes a copy; the source keeps its original.</summary>
    Copy = 1,

    /// <summary>The view takes the data away, so the source may delete its original.</summary>
    Move = 2,

    /// <summary>The view keeps a reference to the data instead of a copy of it.</summary>
    Link = 4,
}

/// <summary>
/// What a drag over the view carries: the files or text its pasteboard offered, the actions its
/// source allows, and the modifier keys that were down.
/// </summary>
/// <remarks>
/// AppKit hands the payload over once, at the moment the drag enters, and the later calls only move
/// it. The pointer is therefore passed alongside this on every call rather than held here.
/// </remarks>
public readonly struct AppKitDragPayload
{
    /// <summary>Creates a payload.</summary>
    /// <param name="files">The full paths of the files the drag carries, or an empty array.</param>
    /// <param name="text">The plain text the drag carries, or null.</param>
    /// <param name="allowedEffects">What the drag source allows the view to do.</param>
    /// <param name="modifiers">The modifier keys held down.</param>
    public AppKitDragPayload(
        string[] files,
        string? text,
        AppKitDragEffect allowedEffects,
        XPlatModifiers modifiers)
    {
        Files = files;
        Text = text;
        AllowedEffects = allowedEffects;
        Modifiers = modifiers;
    }

    /// <summary>The full paths of the files the drag carries, or an empty array.</summary>
    public string[] Files { get; }

    /// <summary>The plain text the drag carries, or null.</summary>
    public string? Text { get; }

    /// <summary>What the drag source allows the view to do.</summary>
    public AppKitDragEffect AllowedEffects { get; }

    /// <summary>The modifier keys held down.</summary>
    public XPlatModifiers Modifiers { get; }
}

/// <summary>
/// Receives the drags a view is registered for, and says what the view would do with them.
/// </summary>
/// <remarks>
/// The overrides AppKit sends a drag to have to live on the view itself, so the view cannot shed
/// them; what it can shed is everything they mean. A view with no sink never registers for a
/// dragged type, which is what keeps an application that does not use the feature free of it.
/// <para>
/// The sink is public because its implementation lives in a feature package, and a backend must
/// not reference a feature package. A public type in the backend is the only thing the two can
/// share, so this is the seam: the view speaks it, the feature implements it, and neither names the
/// other. Nothing here is named after a feature, so an application that never opted in carries the
/// interface and never an implementation of it.
/// </para>
/// </remarks>
public interface IAppKitDragSink
{
    /// <summary>Reports a drag entering the view, and what a drop there would do.</summary>
    /// <param name="location">The pointer, in the view's own coordinates.</param>
    /// <param name="payload">What the drag carries.</param>
    AppKitDragEffect DragEntered(SKPoint location, AppKitDragPayload payload);

    /// <summary>Reports a drag moving over the view, and what a drop there would do.</summary>
    /// <param name="location">The pointer, in the view's own coordinates.</param>
    /// <param name="payload">What the drag carries.</param>
    AppKitDragEffect DragUpdated(SKPoint location, AppKitDragPayload payload);

    /// <summary>Reports a drag leaving the view without being dropped.</summary>
    void DragExited();

    /// <summary>Reports a drag released over the view, and what the view did with it.</summary>
    /// <param name="location">The pointer, in the view's own coordinates.</param>
    /// <param name="payload">What the drag carries.</param>
    AppKitDragEffect Dropped(SKPoint location, AppKitDragPayload payload);
}
