namespace Kolvir;

/// <summary>
/// The transient conditions a control can be in, as a set of flags, for chrome that colours itself by
/// state.
/// </summary>
/// <remarks>
/// <para>
/// This is what a <see cref="Border"/> reads when it decides which colour to paint. It exists as a
/// value rather than as a set of properties on <see cref="Control"/> because four of its five members
/// are already properties there (<see cref="Control.Enabled"/>, <see cref="Control.IsFocused"/>,
/// <see cref="Control.IsMouseOver"/>, <see cref="Control.IsPressed"/>) and the fifth is not: a popup
/// being open is declared separately by <see cref="ComboBox"/>, <see cref="SplitButton"/> and
/// <see cref="DateTimePicker"/>, and a border that read it directly would have to know and cast to all
/// three. A bit says the same thing without the cast.
/// </para>
/// <para>
/// More than one flag can be set at once - a focused control the pointer is over is
/// <see cref="Focused"/> and <see cref="Hovered"/> - so a palette of choices has to state which wins.
/// <see cref="SimpleBorder"/> documents its order; a border of your own chooses its own.
/// </para>
/// </remarks>
[Flags]
public enum ControlState
{
    /// <summary>At rest, and enabled: nothing else applies.</summary>
    None = 0,

    /// <summary>The pointer is over the control, as last decided by the host's hit testing.</summary>
    Hovered = 1,

    /// <summary>The control has the keyboard focus.</summary>
    Focused = 2,

    /// <summary>The control is disabled, which outranks every other state for chrome.</summary>
    Disabled = 4,

    /// <summary>A mouse button is held down on the control.</summary>
    Pressed = 8,

    /// <summary>
    /// The control is in a state of its own that its class reports through
    /// <see cref="Control.State"/>: a combo box's list being open, for instance. The base
    /// implementation never sets it.
    /// </summary>
    Active = 16,
}
