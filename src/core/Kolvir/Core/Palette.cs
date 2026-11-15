using SkiaSharp;

namespace Kolvir;

/// <summary>
/// The colours the built-in controls draw with. <see cref="ThemeColor"/> names one entry
/// so a control (or a text span) can say "paint me in the accent colour" instead of copying a
/// colour value that would then be frozen at the moment the control was built.
/// </summary>
public enum ThemeColor
{
    /// <summary>The window background, painted behind everything else.</summary>
    Background = 0,

    /// <summary>Fill of raised surfaces such as panels, cards and popup menus.</summary>
    Surface = 1,

    /// <summary>Default text colour.</summary>
    Foreground = 2,

    /// <summary>Text and glyphs for secondary content: captions, hints and unselected icons.</summary>
    MutedForeground = 3,

    /// <summary>Lines and outlines: control borders, separators and the track behind a value.</summary>
    Border = 4,

    /// <summary>
    /// Fill of the primary action, and of whatever the user has turned on or selected: a pressed
    /// toggle, the chosen item, a link.
    /// </summary>
    Accent = 5,

    /// <summary><see cref="Accent"/> while the pointer is over it.</summary>
    AccentHover = 6,

    /// <summary><see cref="Accent"/> while a pointer button is held down on it.</summary>
    AccentPressed = 7,

    /// <summary>Text and glyphs drawn on top of an <see cref="Accent"/> fill.</summary>
    AccentForeground = 8,

    /// <summary>
    /// Fill of the subdued chrome around the content: secondary buttons, header bands, and the
    /// glyph of a check box or radio button.
    /// </summary>
    Secondary = 9,

    /// <summary><see cref="Secondary"/> while the pointer is over it.</summary>
    SecondaryHover = 10,

    /// <summary><see cref="Secondary"/> while a pointer button is held down on it.</summary>
    SecondaryPressed = 11,

    /// <summary>Outline drawn around the control that has keyboard focus.</summary>
    FocusRing = 12,

    /// <summary>Fill of the splitter of a <see cref="SplitContainer"/>.</summary>
    Splitter = 13,

    /// <summary>
    /// The splitter under the pointer, and the same subtle hover fill on a drop-down button.
    /// </summary>
    SplitterHover = 14,

    /// <summary>Fill of an editable field such as a <see cref="TextBox"/>.</summary>
    InputBackground = 15,

    /// <summary>
    /// Fill of selected text or a selected item. Usually translucent, because it is drawn over what
    /// it selects.
    /// </summary>
    Selection = 16,

    /// <summary>Colour of the text caret.</summary>
    Caret = 17,

    /// <summary>Colour of placeholder text shown in an empty editable field.</summary>
    Placeholder = 18,

    /// <summary>Fill of a control that cannot be used.</summary>
    DisabledBackground = 19,

    /// <summary>Text and glyphs of a control that cannot be used.</summary>
    DisabledForeground = 20,
}

/// <summary>
/// One complete set of theme colours. The library ships <see cref="Light"/> and <see cref="Dark"/>;
/// an application can brand either one with a <c>with</c> expression and assign the result to
/// <see cref="Theme.Palette"/>.
/// </summary>
public sealed record Palette
{
    /// <summary>The default light palette.</summary>
    public static Palette Light { get; } = new();

    /// <summary>The palette used while the system reports a dark appearance.</summary>
    public static Palette Dark { get; } = Light with
    {
        IsDark = true,
        Background = new(0xFF1B1F24),
        Surface = new(0xFF23282F),
        Foreground = new(0xFFE6E9EE),
        MutedForeground = new(0xFF9AA5B1),
        Border = new(0xFF39414B),
        Accent = new(0xFF4C8DFF),
        AccentHover = new(0xFF6BA0FF),
        AccentPressed = new(0xFF3A79E8),
        Secondary = new(0xFF2E353E),
        SecondaryHover = new(0xFF38404A),
        SecondaryPressed = new(0xFF424B57),
        FocusRing = new(0xFF7FB0FF),
        Splitter = new(0xFF39414B),
        SplitterHover = new(0xFF4B5563),
        InputBackground = new(0xFF1A1E24),
        Selection = new(0x554C8DFF),
        Caret = new(0xFFE6E9EE),
        Placeholder = new(0xFF6F7A87),
        DisabledBackground = new(0xFF2A3038),
        DisabledForeground = new(0xFF6B7481),
    };

    /// <summary>
    /// True for a dark palette. Colour alone cannot express every difference between light and
    /// dark, so controls that draw non-colour details can branch on this.
    /// </summary>
    public bool IsDark { get; init; }

    /// <summary>Window background, behind everything else.</summary>
    public SKColor Background { get; init; } = new(0xFFF4F6F9);

    /// <summary>Fill of raised surfaces such as panels and cards.</summary>
    public SKColor Surface { get; init; } = SKColors.White;

    /// <summary>Default text colour.</summary>
    public SKColor Foreground { get; init; } = new(0xFF1F2933);

    /// <summary>Text colour for secondary, less important text.</summary>
    public SKColor MutedForeground { get; init; } = new(0xFF6B7280);

    /// <summary>Lines and outlines.</summary>
    public SKColor Border { get; init; } = new(0xFFDDE1E7);

    /// <summary>
    /// Fill of the primary action, and of whatever the user has turned on or selected: a pressed
    /// toggle, the chosen item, a link.
    /// </summary>
    public SKColor Accent { get; init; } = new(0xFF2F6FED);

    /// <summary><see cref="Accent"/> while the pointer is over it.</summary>
    public SKColor AccentHover { get; init; } = new(0xFF2560D8);

    /// <summary><see cref="Accent"/> while a pointer button is held down on it.</summary>
    public SKColor AccentPressed { get; init; } = new(0xFF1D4FBB);

    /// <summary>Text and glyphs drawn on top of an <see cref="Accent"/> fill.</summary>
    public SKColor AccentForeground { get; init; } = SKColors.White;

    /// <summary>
    /// Fill of the subdued chrome around the content: secondary buttons, header bands, and the
    /// glyph of a check box or radio button.
    /// </summary>
    public SKColor Secondary { get; init; } = new(0xFFE9EDF3);

    /// <summary><see cref="Secondary"/> while the pointer is over it.</summary>
    public SKColor SecondaryHover { get; init; } = new(0xFFDEE4EC);

    /// <summary><see cref="Secondary"/> while a pointer button is held down on it.</summary>
    public SKColor SecondaryPressed { get; init; } = new(0xFFCFD7E2);

    /// <summary>Outline drawn around the control that has keyboard focus.</summary>
    public SKColor FocusRing { get; init; } = new(0xFF7FA8F5);

    /// <summary>Fill of the splitter of an <see cref="SplitContainer"/>.</summary>
    public SKColor Splitter { get; init; } = new(0xFFDDE1E7);

    /// <summary>
    /// The splitter under the pointer, and the same subtle hover fill on a drop-down button.
    /// </summary>
    public SKColor SplitterHover { get; init; } = new(0xFFC3CBD6);

    /// <summary>Fill of editable fields such as <see cref="TextBox"/>.</summary>
    public SKColor InputBackground { get; init; } = SKColors.White;

    /// <summary>Fill of a selection (drawn over the background, so it is usually translucent).</summary>
    public SKColor Selection { get; init; } = new(0x552F6FED);

    /// <summary>Colour of the text caret.</summary>
    public SKColor Caret { get; init; } = new(0xFF1F2933);

    /// <summary>Colour of placeholder text shown in an empty editable field.</summary>
    public SKColor Placeholder { get; init; } = new(0xFF9AA3AF);

    /// <summary>Fill of a control that cannot be used.</summary>
    public SKColor DisabledBackground { get; init; } = new(0xFFE4E7EC);

    /// <summary>Text and glyphs of a control that cannot be used.</summary>
    public SKColor DisabledForeground { get; init; } = new(0xFF9AA3AF);
}
