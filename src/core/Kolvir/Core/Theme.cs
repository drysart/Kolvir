using SkiaSharp;

namespace Kolvir;

/// <summary>
/// The palette and fonts the built-in controls draw with. Controls read these values while
/// they paint, so assigning <see cref="Palette"/> recolours the whole UI on the next frame;
/// <see cref="AddChangedHandler"/> tells a backend that a frame is due.
/// </summary>
/// <remarks>
/// The palette is process-wide, the way branding usually is. To follow the operating system's
/// light/dark preference use <see cref="SystemTheme"/>; to force one, assign
/// <see cref="Palette"/> directly.
/// </remarks>
public static class Theme
{
    private static Action<object>? _changed;
    private static Palette _palette = Palette.Light;

    /// <summary>Adds a handler that runs after <see cref="Palette"/> was given a different palette.</summary>
    public static IDisposable AddChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _changed += handler;
        return new HandlerToken<Action<object>>(RemoveChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddChangedHandler"/> added.</summary>
    public static void RemoveChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _changed -= handler;
    }

    /// <summary>
    /// The colours in use. Assign <see cref="Palette.Light"/>, <see cref="Palette.Dark"/>
    /// or a palette of your own.
    /// </summary>
    public static Palette Palette
    {
        get => _palette;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_palette, value))
            {
                return;
            }

            Assign(value);
        }
    }

    /// <summary>True while a dark palette is in use.</summary>
    public static bool IsDarkMode => _palette.IsDark;

    /// <summary>Resolves one palette entry by name, for controls that defer a colour to paint time.</summary>
    public static SKColor ColorOf(ThemeColor color) => color switch
    {
        ThemeColor.Background => _palette.Background,
        ThemeColor.Surface => _palette.Surface,
        ThemeColor.Foreground => _palette.Foreground,
        ThemeColor.MutedForeground => _palette.MutedForeground,
        ThemeColor.Border => _palette.Border,
        ThemeColor.Accent => _palette.Accent,
        ThemeColor.AccentHover => _palette.AccentHover,
        ThemeColor.AccentPressed => _palette.AccentPressed,
        ThemeColor.AccentForeground => _palette.AccentForeground,
        ThemeColor.Secondary => _palette.Secondary,
        ThemeColor.SecondaryHover => _palette.SecondaryHover,
        ThemeColor.SecondaryPressed => _palette.SecondaryPressed,
        ThemeColor.FocusRing => _palette.FocusRing,
        ThemeColor.Splitter => _palette.Splitter,
        ThemeColor.SplitterHover => _palette.SplitterHover,
        ThemeColor.InputBackground => _palette.InputBackground,
        ThemeColor.Selection => _palette.Selection,
        ThemeColor.Caret => _palette.Caret,
        ThemeColor.Placeholder => _palette.Placeholder,
        ThemeColor.DisabledBackground => _palette.DisabledBackground,
        ThemeColor.DisabledForeground => _palette.DisabledForeground,
        _ => _palette.Foreground,
    };

    // The individual colours read and write the current palette: assigning one replaces the
    // palette with an edited copy. Prefer Palette when switching appearance.

    /// <summary>
    /// Window background, behind everything else. A host also clears the window with it when no
    /// clear colour was given, so a window follows the theme on its own.
    /// </summary>
    /// <remarks>
    /// Every individual colour on <see cref="Theme"/> behaves the same way: the getter reads the
    /// current palette and the setter replaces the palette with an edited copy and notifies the
    /// changed handlers. Assigning one colour therefore keeps the rest of the palette, but it also
    /// discards any palette object that was assigned before; assign <see cref="Theme.Palette"/> when
    /// switching appearance wholesale.
    /// </remarks>
    public static SKColor Background
    {
        get => _palette.Background;
        set => Assign(_palette with { Background = value });
    }

    /// <summary>Fill of raised surfaces such as panels, the menu bar and popup menus.</summary>
    public static SKColor Surface
    {
        get => _palette.Surface;
        set => Assign(_palette with { Surface = value });
    }

    /// <summary>Default text colour.</summary>
    public static SKColor Foreground
    {
        get => _palette.Foreground;
        set => Assign(_palette with { Foreground = value });
    }

    /// <summary>Text and glyphs for secondary content: captions, hints and unselected icons.</summary>
    public static SKColor MutedForeground
    {
        get => _palette.MutedForeground;
        set => Assign(_palette with { MutedForeground = value });
    }

    /// <summary>Lines and outlines: control borders, separators and the track behind a value.</summary>
    public static SKColor Border
    {
        get => _palette.Border;
        set => Assign(_palette with { Border = value });
    }

    /// <summary>
    /// Fill of the primary action, and of whatever the user has turned on or selected.
    /// </summary>
    public static SKColor Accent
    {
        get => _palette.Accent;
        set => Assign(_palette with { Accent = value });
    }

    /// <summary><see cref="Accent"/> while the pointer is over it.</summary>
    public static SKColor AccentHover
    {
        get => _palette.AccentHover;
        set => Assign(_palette with { AccentHover = value });
    }

    /// <summary><see cref="Accent"/> while a pointer button is held down on it.</summary>
    public static SKColor AccentPressed
    {
        get => _palette.AccentPressed;
        set => Assign(_palette with { AccentPressed = value });
    }

    /// <summary>Text and glyphs drawn on top of an <see cref="Accent"/> fill.</summary>
    public static SKColor AccentForeground
    {
        get => _palette.AccentForeground;
        set => Assign(_palette with { AccentForeground = value });
    }

    /// <summary>
    /// Fill of the subdued chrome around the content: secondary buttons, header bands, and the
    /// glyph of a check box or radio button.
    /// </summary>
    public static SKColor Secondary
    {
        get => _palette.Secondary;
        set => Assign(_palette with { Secondary = value });
    }

    /// <summary><see cref="Secondary"/> while the pointer is over it.</summary>
    public static SKColor SecondaryHover
    {
        get => _palette.SecondaryHover;
        set => Assign(_palette with { SecondaryHover = value });
    }

    /// <summary><see cref="Secondary"/> while a pointer button is held down on it.</summary>
    public static SKColor SecondaryPressed
    {
        get => _palette.SecondaryPressed;
        set => Assign(_palette with { SecondaryPressed = value });
    }

    /// <summary>Outline drawn around the control that has keyboard focus.</summary>
    public static SKColor FocusRing
    {
        get => _palette.FocusRing;
        set => Assign(_palette with { FocusRing = value });
    }

    /// <summary>Fill of the splitter of a <see cref="SplitContainer"/>.</summary>
    public static SKColor Splitter
    {
        get => _palette.Splitter;
        set => Assign(_palette with { Splitter = value });
    }

    /// <summary>
    /// The splitter under the pointer, and the same subtle hover fill on a drop-down button.
    /// </summary>
    public static SKColor SplitterHover
    {
        get => _palette.SplitterHover;
        set => Assign(_palette with { SplitterHover = value });
    }

    /// <summary>Fill of editable fields such as <see cref="TextBox"/>.</summary>
    public static SKColor InputBackground
    {
        get => _palette.InputBackground;
        set => Assign(_palette with { InputBackground = value });
    }

    /// <summary>Fill of a text selection inside an editable field (drawn over the background).</summary>
    public static SKColor Selection
    {
        get => _palette.Selection;
        set => Assign(_palette with { Selection = value });
    }

    /// <summary>Color of the text caret.</summary>
    public static SKColor Caret
    {
        get => _palette.Caret;
        set => Assign(_palette with { Caret = value });
    }

    /// <summary>Color of placeholder text shown in an empty editable field.</summary>
    public static SKColor Placeholder
    {
        get => _palette.Placeholder;
        set => Assign(_palette with { Placeholder = value });
    }

    /// <summary>Fill of a control that cannot be used.</summary>
    public static SKColor DisabledBackground
    {
        get => _palette.DisabledBackground;
        set => Assign(_palette with { DisabledBackground = value });
    }

    /// <summary>Text and glyphs of a control that cannot be used.</summary>
    public static SKColor DisabledForeground
    {
        get => _palette.DisabledForeground;
        set => Assign(_palette with { DisabledForeground = value });
    }

    private static string? _fontFamily;
    private static int _fontVersion = 1;
    private static Font _defaultFont = new(size: 13f);
    private static Font _captionFont = new(size: 11.5f);
    private static Font _emphasizedFont = new(size: 13f, weight: 600);
    private static Font _titleFont = new(size: 24f, weight: 700);
    private static Font _sectionFont = new(size: 15f, weight: 600);

    /// <summary>
    /// The font family the built-in controls draw with, or null to use the operating system's
    /// default. A backend sets this once at start-up so the UI matches the desktop it runs on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It can be assigned at any time: the theme fonts are rebuilt, and because a control resolves
    /// its font when it measures or paints rather than when it is built, controls that already exist
    /// pick the new face up - and re-lay out, since text at another face needs other room.
    /// </para>
    /// <para>
    /// The name may be one the operating system provides, or one an application registered with
    /// <see cref="IFontRegistry"/>. A registered name wins over a system family of the same name, so
    /// assigning the name the platform's own UI family has - which is what a backend reports and what
    /// an application passes in <see cref="ApplicationOptions.FontFamily"/> - is the way to draw an
    /// application that looks like its desktop with a face the application supplies.
    /// </para>
    /// </remarks>
    public static string? FontFamily
    {
        get => _fontFamily;
        set
        {
            if (string.Equals(_fontFamily, value, StringComparison.Ordinal))
            {
                return;
            }

            _fontFamily = value;
            RebuildFonts();

            // A static event has no instance to name as the sender.
            _changed?.Invoke(null!);
        }
    }

    /// <summary>
    /// The font a control draws with when it does not name one: 13 logical pixels in
    /// <see cref="FontFamily"/>, or in the platform's default family.
    /// </summary>
    /// <remarks>
    /// The theme fonts are rebuilt whenever <see cref="FontFamily"/> changes, so read the property
    /// when measuring or painting rather than holding on to the value.
    /// </remarks>
    public static Font DefaultFont => _defaultFont;

    /// <summary>Font for small print: 11.5 logical pixels at weight 400.</summary>
    /// <remarks>
    /// The weight is what the family is asked for. A family with only a regular face - a registered
    /// one, or a platform family with no other weight - draws its regular face and still reports the
    /// weight requested here.
    /// </remarks>
    public static Font CaptionFont => _captionFont;

    /// <summary>Font for body text that has to stand out: 13 logical pixels at weight 600.</summary>
    /// <remarks>
    /// The weight is what the family is asked for, so a family that has a semibold face draws it,
    /// and one that does not draws its nearest face.
    /// </remarks>
    public static Font EmphasizedFont => _emphasizedFont;

    /// <summary>Font for a window or dialog title: 24 logical pixels at weight 700.</summary>
    /// <remarks>
    /// The weight is what the family is asked for, so a family that has a bold face draws it, and one
    /// that does not draws its nearest face.
    /// </remarks>
    public static Font TitleFont => _titleFont;

    /// <summary>Font for a section heading: 15 logical pixels at weight 600.</summary>
    /// <remarks>
    /// The weight is what the family is asked for, so a family that has a semibold face draws it,
    /// and one that does not draws its nearest face.
    /// </remarks>
    public static Font SectionFont => _sectionFont;

    /// <summary>
    /// Changes whenever the theme fonts are rebuilt. A host compares it before laying out and
    /// painting, so a face or size that changed under an existing tree is one more layout pass
    /// rather than a control that keeps measuring with a font it no longer draws with.
    /// </summary>
    internal static int FontVersion => _fontVersion;

    private static void RebuildFonts()
    {
        // Nothing caches a resolved theme font - the effective font is read when it is used - but
        // what a control caches that was derived from one has to be dropped, and the layout of text
        // at another size has to be re-run. This is how a host notices.
        _fontVersion++;

        _defaultFont = ThemeFont(13f);
        _captionFont = ThemeFont(11.5f);
        _emphasizedFont = ThemeFont(13f, 600);
        _titleFont = ThemeFont(24f, 700);
        _sectionFont = ThemeFont(15f, 600);
    }

    /// <summary>
    /// Reports that how a family resolves has changed, without the theme naming a different family.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registering a face with <see cref="IFontRegistry"/>, or unregistering one, leaves every
    /// description identical and changes the face behind a name. The mechanism that has to run
    /// anyway - drop derived appearance caches, re-measure, repaint as one piece - is the one an
    /// assignment to <see cref="FontFamily"/> already triggers, and a host is already subscribed to
    /// it. So the registry bumps the same version through the same notify rather than a per-family
    /// equivalent, which would need every control to remember which families it used.
    /// </para>
    /// <para>
    /// Blunt rather than precise, and startup-shaped rather than per frame: it is one integer
    /// increment on a path an application runs once per registered family.
    /// </para>
    /// </remarks>
    internal static void NotifyFontsChanged()
    {
        _fontVersion++;

        // A static event has no instance to name as the sender.
        _changed?.Invoke(null!);
    }

    /// <summary>One theme font at <paramref name="size"/> from <see cref="FontFamily"/>.</summary>
    /// <param name="size">The size to draw at, in logical pixels.</param>
    /// <param name="weight">The weight to ask for, on the 1-1000 scale.</param>
    /// <returns>A description; nothing native is created until text is measured with it.</returns>
    private static Font ThemeFont(float size, int weight = 400) => new(_fontFamily, size, weight);

    private static void Assign(Palette palette)
    {
        _palette = palette;
        _changed?.Invoke(null!);
    }
}
