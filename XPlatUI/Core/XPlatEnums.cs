namespace XPlatUI;

/// <summary>Docking edge used by container layout.</summary>
public enum DockStyle
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 3,
    Right = 4,
    Fill = 5,
}

/// <summary>Orientation of the splitter in an <see cref="XPlatSplitContainer"/>.</summary>
public enum XPlatOrientation
{
    /// <summary>The splitter runs horizontally; panels are stacked top and bottom and the adjustable size is a height.</summary>
    Horizontal = 0,

    /// <summary>The splitter runs vertically; panels sit side by side and the adjustable size is a width.</summary>
    Vertical = 1,
}

/// <summary>The axis an <see cref="XPlatStackPanel"/> stacks its children along.</summary>
public enum XPlatStackOrientation
{
    /// <summary>Children run top to bottom, and each one takes the width of the panel.</summary>
    Vertical = 0,

    /// <summary>Children run left to right, and each one takes the height of the panel.</summary>
    Horizontal = 1,
}

/// <summary>
/// How a child is placed across the axis an <see cref="XPlatStackPanel"/> does not stack along.
/// </summary>
/// <remarks>
/// The names are axis-neutral because the same value is used on both axes: <see cref="Near"/> is
/// the left for a horizontal alignment and the top for a vertical one, and <see cref="Far"/> is the
/// right and the bottom respectively.
/// </remarks>
public enum XPlatAlignment
{
    /// <summary>Fills the space across the stack. The default.</summary>
    Stretch = 0,

    /// <summary>Sized to its own preferred size and placed at the left or the top.</summary>
    Near = 1,

    /// <summary>Sized to its own preferred size and placed in the middle.</summary>
    Center = 2,

    /// <summary>Sized to its own preferred size and placed at the right or the bottom.</summary>
    Far = 3,
}

/// <summary>
/// Whether a scrolling container shows a scrollbar along one axis. Each axis of an
/// <see cref="XPlatPanel"/> is configured separately.
/// </summary>
public enum XPlatScrollStyle
{
    /// <summary>No scrollbar. Content outside the viewport is clipped and cannot be reached.</summary>
    None = 0,

    /// <summary>The scrollbar appears only while the content is larger than the viewport.</summary>
    Automatic = 1,

    /// <summary>The scrollbar is always shown, and is disabled while the content fits.</summary>
    Always = 2,
}

/// <summary>Nine-way content alignment (text, images inside a control).</summary>
public enum XPlatContentAlignment
{
    TopLeft = 0,
    TopCenter = 1,
    TopRight = 2,
    MiddleLeft = 3,
    MiddleCenter = 4,
    MiddleRight = 5,
    BottomLeft = 6,
    BottomCenter = 7,
    BottomRight = 8,
}

/// <summary>How an <see cref="XPlatImage"/> scales its bitmap inside its bounds.</summary>
public enum XPlatSizeMode
{
    /// <summary>Draws the image at its natural size, anchored top-left.</summary>
    Normal = 0,

    /// <summary>Stretches the image to fill the available area (may distort).</summary>
    StretchImage = 1,

    /// <summary>Draws the image at its natural size, centered.</summary>
    CenterImage = 2,

    /// <summary>Scales the image uniformly so it fits inside the available area.</summary>
    Zoom = 3,
}

/// <summary>How an <see cref="XPlatListBox"/> tracks which of its items are selected.</summary>
public enum XPlatSelectionMode
{
    /// <summary>Items cannot be selected. The list is read-only.</summary>
    None = 0,

    /// <summary>Exactly one item can be selected at a time, which is the default.</summary>
    One = 1,

    /// <summary>Any number of items can be selected, each toggled on its own.</summary>
    MultiSimple = 2,

    /// <summary>Any number of items can be selected; Shift selects a range and Ctrl toggles one item.</summary>
    MultiExtended = 3,
}

/// <summary>What an item being drawn by an <see cref="XPlatListBox"/> owner-draw handler looks like.</summary>
[Flags]
public enum XPlatDrawItemState
{
    None = 0,

    /// <summary>The item is part of the selection.</summary>
    Selected = 1,

    /// <summary>The item is the one the keyboard is on, which is where a focus rectangle belongs.</summary>
    Focused = 2,

    /// <summary>The control is disabled, so the item is drawn greyed out.</summary>
    Disabled = 4,

    /// <summary>The pointer is over the item and it is not selected.</summary>
    Hot = 8,
}

/// <summary>Mouse cursor requested by a control.</summary>
public enum XPlatCursor
{
    Default = 0,
    Hand = 1,
    IBeam = 2,
    Cross = 3,
    SizeAll = 4,
    SizeNS = 5,
    SizeWE = 6,
}

[Flags]
public enum XPlatMouseButtons
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 4,
}

[Flags]
public enum XPlatModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
}

/// <summary>Platform-neutral key identifiers used by the input pipeline.</summary>
public enum XPlatKey
{
    None = 0,
    Tab,
    Enter,
    Space,
    Escape,
    Backspace,
    Delete,
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    PageUp,
    PageDown,
    A,
    B,
    C,
    D,
    E,
    F,
    G,
    H,
    I,
    J,
    K,
    L,
    M,
    N,
    O,
    P,
    Q,
    R,
    S,
    T,
    U,
    V,
    W,
    X,
    Y,
    Z,
    D0,
    D1,
    D2,
    D3,
    D4,
    D5,
    D6,
    D7,
    D8,
    D9,
}

/// <summary>Sizing mode for a row or column in an <see cref="XPlatTableLayoutPanel"/>.</summary>
public enum XPlatSizeType
{
    /// <summary>Uses the fixed <see cref="XPlatColumnStyle.Value"/> (or <see cref="XPlatRowStyle.Value"/>) in logical pixels.</summary>
    Absolute = 0,

    /// <summary>Uses a percentage of the available space.</summary>
    Percent = 1,

    /// <summary>Sizes to the content of the cells in the row/column.</summary>
    AutoSize = 2,
}
