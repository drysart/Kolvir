using SkiaSharp;

namespace Kolvir;

/// <summary>
/// A request to show <see cref="Menu"/>. The location is in logical pixels in the host's
/// coordinate space, which is the space the adapter receives from
/// <see cref="UiHost.ProcessContextMenu"/>; converting it to screen coordinates is the
/// presenter's job, because only the platform layer knows where the surface sits on screen.
/// </summary>
public sealed class ContextMenuRequest
{
    internal ContextMenuRequest(
        ContextMenu menu,
        UiHost host,
        Control? control,
        SKPoint location)
    {
        Menu = menu;
        Host = host;
        Control = control;
        Location = location;
    }

    /// <summary>The menu to show, as the control and the opening handlers left it.</summary>
    public ContextMenu Menu { get; }

    /// <summary>
    /// The window the menu is shown in, which is the coordinate space of <see cref="Location"/>.
    /// </summary>
    public UiHost Host { get; }

    /// <summary>The control the pointer was over, or null when it was over empty surface.</summary>
    public Control? Control { get; }

    /// <summary>Where to show the menu, in logical pixels in the host's coordinate space.</summary>
    public SKPoint Location { get; }
}

/// <summary>
/// Handed to the handlers <see cref="UiHost.AddContextMenuOpeningHandler"/> added before a
/// context menu is shown, so an application can see where the right-click landed - which control,
/// at which point - and build the menu for it.
/// </summary>
/// <remarks>
/// The handlers run for every right-click, whether or not the control under the pointer carries a
/// menu of its own. Assigning <see cref="Menu"/> takes precedence over the menu the control offered
/// and over the host's default selection menu.
/// </remarks>
public sealed class ContextMenuEventArgs
{
    internal ContextMenuEventArgs(Control? control, SKPoint location)
    {
        Control = control;
        Location = location;
    }

    /// <summary>The control the pointer was over, or null when it was over empty surface.</summary>
    public Control? Control { get; }

    /// <summary>Where the menu will be shown, in logical pixels in the host's coordinate space.</summary>
    public SKPoint Location { get; }

    /// <summary>
    /// The menu being composed. It starts as what the control under the pointer contributes - a
    /// text box starts with Cut, Copy, Paste and Select All, enabled for the current state - so a
    /// handler adds its own items to it, with <see cref="ContextMenu.Insert"/> to put them in
    /// front or <see cref="ContextMenu.Add(string, Action?)"/> to append them. Assigning a different
    /// menu, or clearing this one, suppresses the control's contribution.
    /// </summary>
    public ContextMenu? Menu { get; set; }

    /// <summary>
    /// What the control under the pointer found for this press, if it records anything - a text box
    /// puts the annotated span the pointer landed in here. Object-typed on purpose: naming a
    /// feature's own types would publish them to applications that never use the feature, and this
    /// costs one pointer on an object that only exists while a menu is being built.
    /// </summary>
    internal object? Hit { get; set; }
}

/// <summary>
/// Implemented by the platform layer to show <see cref="ContextMenu"/> values. A Windows
/// adapter uses a native menu; other platforms do the same with their own menu API, which keeps
/// the library free of any menu rendering code.
/// </summary>
public interface IContextMenuPresenter
{
    /// <summary>Shows <paramref name="request"/> and runs the picked item's command, if any.</summary>
    void Show(ContextMenuRequest request);
}

/// <summary>
/// Process-wide access point for the context menu presenter. When none is installed menus are
/// silently not shown, so the library never depends on a platform being present.
/// </summary>
public static class ContextMenuPresenter
{
    private static IContextMenuPresenter? _current;

    /// <summary>
    /// The presenter in use. Adapters assign a platform implementation; assigning null restores
    /// the no-op fallback.
    /// </summary>
    public static IContextMenuPresenter? Current
    {
        get => _current;
        set => _current = value;
    }
}
