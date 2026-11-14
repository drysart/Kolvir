namespace Kolvir;

/// <summary>Docking edge used by container layout.</summary>
/// <remarks>
/// Docked children take their edge off the space that is left in the order they were added, so two
/// children docked to the same edge stack up rather than overlap.
/// </remarks>
public enum DockStyle
{
    /// <summary>
    /// The control is not docked: it keeps the position and size it was given, and only its
    /// <see cref="Control.AutoSize"/> is resolved.
    /// </summary>
    None = 0,

    /// <summary>
    /// The control spans the width of what is left and takes the height it asks for, leaving the
    /// space below it to the children docked after it.
    /// </summary>
    Top = 1,

    /// <summary>
    /// The control spans the width of what is left and takes the height it asks for, leaving the
    /// space above it to the children docked after it.
    /// </summary>
    Bottom = 2,

    /// <summary>
    /// The control spans the height of what is left and takes the width it asks for, leaving the
    /// space to its right to the children docked after it.
    /// </summary>
    Left = 3,

    /// <summary>
    /// The control spans the height of what is left and takes the width it asks for, leaving the
    /// space to its left to the children docked after it.
    /// </summary>
    Right = 4,

    /// <summary>
    /// The control takes whatever is left once every other docked child has taken its edge. Fill is
    /// applied last, so its place among the children does not matter.
    /// </summary>
    Fill = 5,
}

/// <summary>
/// Which way something runs: the splitter of an <see cref="SplitContainer"/>, or the axis the
/// thumb of an <see cref="ScrollBar"/> or <see cref="TrackBar"/> travels along.
/// </summary>
public enum Orientation
{
    /// <summary>
    /// A splitter that runs horizontally, leaving the panels stacked top and bottom with a height to
    /// adjust; or a thumb that travels left and right.
    /// </summary>
    Horizontal = 0,

    /// <summary>
    /// A splitter that runs vertically, leaving the panels side by side with a width to adjust; or a
    /// thumb that travels up and down.
    /// </summary>
    Vertical = 1,
}

/// <summary>The axis an <see cref="StackPanel"/> stacks its children along.</summary>
public enum StackOrientation
{
    /// <summary>Children run top to bottom, and each one takes the width of the panel.</summary>
    Vertical = 0,

    /// <summary>Children run left to right, and each one takes the height of the panel.</summary>
    Horizontal = 1,
}

/// <summary>
/// How a child is placed across the axis an <see cref="StackPanel"/> does not stack along.
/// </summary>
/// <remarks>
/// The names are axis-neutral because the same value is used on both axes: <see cref="Near"/> is
/// the left for a horizontal alignment and the top for a vertical one, and <see cref="Far"/> is the
/// right and the bottom respectively.
/// </remarks>
public enum Alignment
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
/// <see cref="Panel"/> is configured separately.
/// </summary>
public enum ScrollStyle
{
    /// <summary>No scrollbar. Content outside the viewport is clipped and cannot be reached.</summary>
    None = 0,

    /// <summary>The scrollbar appears only while the content is larger than the viewport.</summary>
    Automatic = 1,

    /// <summary>The scrollbar is always shown, and is disabled while the content fits.</summary>
    Always = 2,
}

/// <summary>Nine-way content alignment (text, images inside a control).</summary>
/// <remarks>
/// The vertical half places the whole block of content; the horizontal half is applied to each line
/// of it separately, so a centred block has every line centred rather than sharing one left edge.
/// </remarks>
public enum ContentAlignment
{
    /// <summary>Lines start at the left edge, and the block is pinned to the top.</summary>
    TopLeft = 0,

    /// <summary>Lines are centred horizontally, and the block is pinned to the top.</summary>
    TopCenter = 1,

    /// <summary>Lines end at the right edge, and the block is pinned to the top.</summary>
    TopRight = 2,

    /// <summary>
    /// Lines start at the left edge and the block is centred vertically. The default for text.
    /// </summary>
    MiddleLeft = 3,

    /// <summary>Lines are centred horizontally, and the block is centred vertically.</summary>
    MiddleCenter = 4,

    /// <summary>Lines end at the right edge, and the block is centred vertically.</summary>
    MiddleRight = 5,

    /// <summary>Lines start at the left edge, and the block is pinned to the bottom.</summary>
    BottomLeft = 6,

    /// <summary>Lines are centred horizontally, and the block is pinned to the bottom.</summary>
    BottomCenter = 7,

    /// <summary>Lines end at the right edge, and the block is pinned to the bottom.</summary>
    BottomRight = 8,
}

/// <summary>How an <see cref="Image"/> scales its picture inside its bounds.</summary>
public enum SizeMode
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

/// <summary>How an <see cref="ListBox"/> tracks which of its items are selected.</summary>
public enum SelectionMode
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

/// <summary>What an item being drawn by an <see cref="ListBox"/> owner-draw handler looks like.</summary>
[Flags]
public enum DrawItemState
{
    /// <summary>Nothing is set: the item is drawn plain, enabled and unselected.</summary>
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

/// <summary>
/// Which colour space an <see cref="ColorPicker"/> shows its three component fields in.
/// </summary>
/// <remarks>
/// <see cref="Hsv"/> is the space design tools call HSB; the enum uses one name for it rather than
/// carrying a synonym. <see cref="Rgb"/> and <see cref="Hsv"/> share the same saturation/value
/// square, because RGB has no two-axis shape of its own; <see cref="Hsl"/> has its own
/// saturation/lightness square. A mode change is a display change only: the colour is never
/// converted through the components, so switching modes leaves <c>Color</c> exactly as it was.
/// </remarks>
public enum ColorMode
{
    /// <summary>Three 0-255 channel fields: red, green and blue. The square is the HSV square.</summary>
    Rgb = 0,

    /// <summary>Hue in degrees (0-359), saturation and lightness as percentages (0-100).</summary>
    Hsl = 1,

    /// <summary>Hue in degrees (0-359), saturation and value as percentages (0-100). HSB is the same space.</summary>
    Hsv = 2,
}

/// <summary>Mouse cursor requested by a control.</summary>
public enum Cursor
{
    /// <summary>The platform's ordinary arrow pointer.</summary>
    Default = 0,

    /// <summary>The pointing hand, for anything clickable: a button, a link, a hovered row.</summary>
    Hand = 1,

    /// <summary>The text insertion bar, shown where typing goes: an editor or a field.</summary>
    IBeam = 2,

    /// <summary>The crosshair, for choosing a point: what a colour spectrum shows.</summary>
    Cross = 3,

    /// <summary>A four-way move cursor, for something dragged as a whole rather than resized.</summary>
    SizeAll = 4,

    /// <summary>The vertical double arrow, for a drag that moves up and down.</summary>
    SizeNS = 5,

    /// <summary>The horizontal double arrow, for a drag that moves left and right.</summary>
    SizeWE = 6,
}

/// <summary>
/// The mouse button a pointer event is about. Flagged, so a caller can name more than one, though
/// the input pipeline reports a single button per event.
/// </summary>
[Flags]
public enum MouseButtons
{
    /// <summary>No button: a move with nothing held, or a wheel turn.</summary>
    None = 0,

    /// <summary>The left button, which is the one a click or a drag normally means.</summary>
    Left = 1,

    /// <summary>The right button, which asks for the context menu.</summary>
    Right = 2,

    /// <summary>The middle button, usually the wheel pressed down.</summary>
    Middle = 4,
}

/// <summary>
/// Keyboard modifiers held during an input event, as a set: several can be down at once.
/// </summary>
[Flags]
public enum Modifiers
{
    /// <summary>No modifier is held.</summary>
    None = 0,

    /// <summary>
    /// Shift. Held with Tab it moves the focus backwards, and a selection made with it extends
    /// rather than replaces.
    /// </summary>
    Shift = 1,

    /// <summary>
    /// Control, the literal Ctrl key in an input event. In a <see cref="Shortcut"/> the same value
    /// means the platform's accelerator modifier, which is Command on macOS.
    /// </summary>
    Control = 2,

    /// <summary>
    /// Alt, which macOS spells Option. Alt with a letter reaches the window's menu bar as that
    /// item's mnemonic.
    /// </summary>
    Alt = 4,
}

/// <summary>Platform-neutral key identifiers used by the input pipeline.</summary>
/// <remarks>
/// <para>
/// A value names a physical key rather than the character it produces. The modifiers come alongside
/// it, so Shift+5 is <see cref="D5"/> with <see cref="Modifiers.Shift"/>, and the character that was
/// actually typed arrives as text input instead of as a key.
/// </para>
/// <para>
/// The set stops at the digits and the named keys. The punctuation keys report
/// <see cref="None"/> on every platform, which is why a menu mnemonic only ever has to consider a
/// letter or a digit.
/// </para>
/// </remarks>
public enum Key
{
    /// <summary>
    /// No key: what a modifier-only press and a key the backend does not name both report.
    /// </summary>
    None = 0,

    /// <summary>The Tab key. The host moves the focus forward with it, or back with Shift.</summary>
    Tab,

    /// <summary>The Enter key, including the keypad's Enter.</summary>
    Enter,

    /// <summary>The Space key.</summary>
    Space,

    /// <summary>
    /// The Escape key. Controls read it as "abandon this": close a drop-down or a menu, or put back
    /// the value an edit would have replaced.
    /// </summary>
    Escape,

    /// <summary>The Backspace key, which deletes backwards from the caret in a text control.</summary>
    Backspace,

    /// <summary>The Delete key, which deletes forwards from the caret.</summary>
    Delete,

    /// <summary>The left arrow key.</summary>
    Left,

    /// <summary>The right arrow key.</summary>
    Right,

    /// <summary>The up arrow key.</summary>
    Up,

    /// <summary>The down arrow key.</summary>
    Down,

    /// <summary>
    /// The Home key. A list takes it as its first item, a scrollbar as its minimum, and a text
    /// control as the start of its line.
    /// </summary>
    Home,

    /// <summary>
    /// The End key. A list takes it as its last item, a scrollbar as its maximum, and a text control
    /// as the end of its line.
    /// </summary>
    End,

    /// <summary>The Page Up key. A scrolling control moves back by one page with it.</summary>
    PageUp,

    /// <summary>The Page Down key. A scrolling control moves forward by one page with it.</summary>
    PageDown,

    /// <summary>The A key, in either case: Shift is reported as a modifier instead.</summary>
    A,

    /// <summary>The B key, in either case: Shift is reported as a modifier instead.</summary>
    B,

    /// <summary>The C key, in either case: Shift is reported as a modifier instead.</summary>
    C,

    /// <summary>The D key, in either case: Shift is reported as a modifier instead.</summary>
    D,

    /// <summary>The E key, in either case: Shift is reported as a modifier instead.</summary>
    E,

    /// <summary>The F key, in either case: Shift is reported as a modifier instead.</summary>
    F,

    /// <summary>The G key, in either case: Shift is reported as a modifier instead.</summary>
    G,

    /// <summary>The H key, in either case: Shift is reported as a modifier instead.</summary>
    H,

    /// <summary>The I key, in either case: Shift is reported as a modifier instead.</summary>
    I,

    /// <summary>The J key, in either case: Shift is reported as a modifier instead.</summary>
    J,

    /// <summary>The K key, in either case: Shift is reported as a modifier instead.</summary>
    K,

    /// <summary>The L key, in either case: Shift is reported as a modifier instead.</summary>
    L,

    /// <summary>The M key, in either case: Shift is reported as a modifier instead.</summary>
    M,

    /// <summary>The N key, in either case: Shift is reported as a modifier instead.</summary>
    N,

    /// <summary>The O key, in either case: Shift is reported as a modifier instead.</summary>
    O,

    /// <summary>The P key, in either case: Shift is reported as a modifier instead.</summary>
    P,

    /// <summary>The Q key, in either case: Shift is reported as a modifier instead.</summary>
    Q,

    /// <summary>The R key, in either case: Shift is reported as a modifier instead.</summary>
    R,

    /// <summary>The S key, in either case: Shift is reported as a modifier instead.</summary>
    S,

    /// <summary>The T key, in either case: Shift is reported as a modifier instead.</summary>
    T,

    /// <summary>The U key, in either case: Shift is reported as a modifier instead.</summary>
    U,

    /// <summary>The V key, in either case: Shift is reported as a modifier instead.</summary>
    V,

    /// <summary>The W key, in either case: Shift is reported as a modifier instead.</summary>
    W,

    /// <summary>The X key, in either case: Shift is reported as a modifier instead.</summary>
    X,

    /// <summary>The Y key, in either case: Shift is reported as a modifier instead.</summary>
    Y,

    /// <summary>The Z key, in either case: Shift is reported as a modifier instead.</summary>
    Z,

    /// <summary>The 0 key. The character it types depends on the layout and on Shift.</summary>
    D0,

    /// <summary>The 1 key. The character it types depends on the layout and on Shift.</summary>
    D1,

    /// <summary>The 2 key. The character it types depends on the layout and on Shift.</summary>
    D2,

    /// <summary>The 3 key. The character it types depends on the layout and on Shift.</summary>
    D3,

    /// <summary>The 4 key. The character it types depends on the layout and on Shift.</summary>
    D4,

    /// <summary>The 5 key. The character it types depends on the layout and on Shift.</summary>
    D5,

    /// <summary>The 6 key. The character it types depends on the layout and on Shift.</summary>
    D6,

    /// <summary>The 7 key. The character it types depends on the layout and on Shift.</summary>
    D7,

    /// <summary>The 8 key. The character it types depends on the layout and on Shift.</summary>
    D8,

    /// <summary>The 9 key. The character it types depends on the layout and on Shift.</summary>
    D9,
}

/// <summary>Sizing mode for a row or column in an <see cref="TableLayoutPanel"/>.</summary>
public enum SizeType
{
    /// <summary>
    /// Uses the fixed <see cref="RowOrColumnStyle.Value"/> of the row or column, in logical pixels.
    /// </summary>
    Absolute = 0,

    /// <summary>Uses a percentage of the available space.</summary>
    Percent = 1,

    /// <summary>Sizes to the content of the cells in the row/column.</summary>
    AutoSize = 2,
}

/// <summary>
/// The state of an <see cref="CheckBox"/> or <see cref="RadioButton"/>, matching the
/// WinForms <c>CheckState</c>.
/// </summary>
public enum CheckState
{
    /// <summary>Not checked.</summary>
    Unchecked = 0,

    /// <summary>Checked.</summary>
    Checked = 1,

    /// <summary>
    /// Neither checked nor unchecked. Only reachable when <see cref="ToggleControl.ThreeState"/>
    /// is on; a two-state toggle never leaves the other two.
    /// </summary>
    Indeterminate = 2,
}

/// <summary>How an <see cref="ProgressBar"/> draws its value, matching the WinForms <c>ProgressBarStyle</c>.</summary>
public enum ProgressBarStyle
{
    /// <summary>One solid run from the start of the bar to the value.</summary>
    Continuous = 0,

    /// <summary>The same run, drawn as separate blocks with gaps between them.</summary>
    Blocks = 1,

    /// <summary>
    /// A band that sweeps the bar to show work of unknown length. The value is ignored, and the
    /// animation only runs while an <see cref="Application"/> is running the message loop.
    /// </summary>
    Marquee = 2,
}

/// <summary>How an <see cref="ListView"/> arranges its items, matching the WinForms <c>View</c>.</summary>
public enum View
{
    /// <summary>One text column per item with no header, filling the control's width.</summary>
    List = 0,

    /// <summary>
    /// One column per <see cref="ListViewColumn"/>, with a header band above the rows that
    /// sorts the items when a header is clicked.
    /// </summary>
    Details = 1,
}

/// <summary>Which way a column of an <see cref="ListView"/> sorts, matching the WinForms <c>SortOrder</c>.</summary>
public enum SortOrder
{
    /// <summary>The items are shown in the order they were added.</summary>
    None = 0,

    /// <summary>Sorted from smallest to largest.</summary>
    Ascending = 1,

    /// <summary>Sorted from largest to smallest.</summary>
    Descending = 2,
}
