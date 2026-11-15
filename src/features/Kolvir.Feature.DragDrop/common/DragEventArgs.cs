using SkiaSharp;

namespace Kolvir.DragDrop;

/// <summary>
/// Carries the data being dragged and where the pointer is, for the drag phases a control receives
/// while its <c>AllowDrop</c> is set.
/// </summary>
/// <remarks>
/// <see cref="Effect"/> starts out as the most useful action the source allows, and a handler
/// narrows it (or sets <see cref="DragDropEffects.None"/> to refuse the drop) before the
/// backend is told what to show in its drag cursor.
/// </remarks>
public class DragEventArgs
{
    /// <summary>Creates the arguments for one drag event.</summary>
    /// <param name="data">The payload being dragged.</param>
    /// <param name="location">The pointer, in the receiving control's client coordinates.</param>
    /// <param name="allowedEffects">What the drag source allows the target to do.</param>
    /// <param name="modifiers">The modifier keys held down.</param>
    public DragEventArgs(
        DataObject data,
        SKPoint location,
        DragDropEffects allowedEffects,
        Modifiers modifiers = Modifiers.None)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
        Location = location;
        AllowedEffects = allowedEffects;
        Modifiers = modifiers;
        Effect = BestEffect(allowedEffects);
    }

    /// <summary>The payload being dragged.</summary>
    public DataObject Data { get; }

    /// <summary>The pointer, in the receiving control's client coordinates.</summary>
    public SKPoint Location { get; }

    /// <summary>The modifier keys held down.</summary>
    public Modifiers Modifiers { get; }

    /// <summary>What the drag source allows the target to do.</summary>
    public DragDropEffects AllowedEffects { get; }

    /// <summary>
    /// What the target will do with the data. The backend clamps it to
    /// <see cref="AllowedEffects"/>, so setting an action the source refuses shows a "no drop"
    /// cursor.
    /// </summary>
    public DragDropEffects Effect { get; set; }

    private static DragDropEffects BestEffect(DragDropEffects allowed)
    {
        if (allowed.HasFlag(DragDropEffects.Copy))
        {
            return DragDropEffects.Copy;
        }

        if (allowed.HasFlag(DragDropEffects.Move))
        {
            return DragDropEffects.Move;
        }

        return allowed.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link : DragDropEffects.None;
    }
}
