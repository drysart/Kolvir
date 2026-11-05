using SkiaSharp;

namespace XPlatUI;

/// <summary>
/// The palette and fonts the built-in controls draw with. Controls read these values while
/// they paint, so assigning <see cref="Palette"/> recolours the whole UI on the next frame;
/// <see cref="AddChangedHandler"/> tells a backend that a frame is due.
/// </summary>
/// <remarks>
/// The palette is process-wide, the way branding usually is. To follow the operating system's
/// light/dark preference use <see cref="XPlatSystemTheme"/>; to force one, assign
/// <see cref="Palette"/> directly.
/// </remarks>
public static class XPlatTheme
{
    private static Action<object>? _changed;
    private static XPlatPalette _palette = XPlatPalette.Light;

    /// <summary>Adds a handler that runs after <see cref="Palette"/> was given a different palette.</summary>
    public static IDisposable AddChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _changed += handler;
        return new XPlatHandlerToken<Action<object>>(RemoveChangedHandler, handler);
    }

    /// <summary>Removes a handler that <see cref="AddChangedHandler"/> added.</summary>
    public static void RemoveChangedHandler(Action<object> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _changed -= handler;
    }

    /// <summary>
    /// The colours in use. Assign <see cref="XPlatPalette.Light"/>, <see cref="XPlatPalette.Dark"/>
    /// or a palette of your own.
    /// </summary>
    public static XPlatPalette Palette
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
    public static SKColor ColorOf(XPlatThemeColor color) => color switch
    {
        XPlatThemeColor.Background => _palette.Background,
        XPlatThemeColor.Surface => _palette.Surface,
        XPlatThemeColor.Foreground => _palette.Foreground,
        XPlatThemeColor.MutedForeground => _palette.MutedForeground,
        XPlatThemeColor.Border => _palette.Border,
        XPlatThemeColor.Accent => _palette.Accent,
        XPlatThemeColor.AccentHover => _palette.AccentHover,
        XPlatThemeColor.AccentPressed => _palette.AccentPressed,
        XPlatThemeColor.AccentForeground => _palette.AccentForeground,
        XPlatThemeColor.Secondary => _palette.Secondary,
        XPlatThemeColor.SecondaryHover => _palette.SecondaryHover,
        XPlatThemeColor.SecondaryPressed => _palette.SecondaryPressed,
        XPlatThemeColor.FocusRing => _palette.FocusRing,
        XPlatThemeColor.Splitter => _palette.Splitter,
        XPlatThemeColor.SplitterHover => _palette.SplitterHover,
        XPlatThemeColor.InputBackground => _palette.InputBackground,
        XPlatThemeColor.Selection => _palette.Selection,
        XPlatThemeColor.Caret => _palette.Caret,
        XPlatThemeColor.Placeholder => _palette.Placeholder,
        XPlatThemeColor.DisabledBackground => _palette.DisabledBackground,
        XPlatThemeColor.DisabledForeground => _palette.DisabledForeground,
        _ => _palette.Foreground,
    };

    // The individual colours read and write the current palette: assigning one replaces the
    // palette with an edited copy. Prefer Palette when switching appearance.

    public static SKColor Background
    {
        get => _palette.Background;
        set => Assign(_palette with { Background = value });
    }

    public static SKColor Surface
    {
        get => _palette.Surface;
        set => Assign(_palette with { Surface = value });
    }

    public static SKColor Foreground
    {
        get => _palette.Foreground;
        set => Assign(_palette with { Foreground = value });
    }

    public static SKColor MutedForeground
    {
        get => _palette.MutedForeground;
        set => Assign(_palette with { MutedForeground = value });
    }

    public static SKColor Border
    {
        get => _palette.Border;
        set => Assign(_palette with { Border = value });
    }

    public static SKColor Accent
    {
        get => _palette.Accent;
        set => Assign(_palette with { Accent = value });
    }

    public static SKColor AccentHover
    {
        get => _palette.AccentHover;
        set => Assign(_palette with { AccentHover = value });
    }

    public static SKColor AccentPressed
    {
        get => _palette.AccentPressed;
        set => Assign(_palette with { AccentPressed = value });
    }

    public static SKColor AccentForeground
    {
        get => _palette.AccentForeground;
        set => Assign(_palette with { AccentForeground = value });
    }

    public static SKColor Secondary
    {
        get => _palette.Secondary;
        set => Assign(_palette with { Secondary = value });
    }

    public static SKColor SecondaryHover
    {
        get => _palette.SecondaryHover;
        set => Assign(_palette with { SecondaryHover = value });
    }

    public static SKColor SecondaryPressed
    {
        get => _palette.SecondaryPressed;
        set => Assign(_palette with { SecondaryPressed = value });
    }

    public static SKColor FocusRing
    {
        get => _palette.FocusRing;
        set => Assign(_palette with { FocusRing = value });
    }

    public static SKColor Splitter
    {
        get => _palette.Splitter;
        set => Assign(_palette with { Splitter = value });
    }

    public static SKColor SplitterHover
    {
        get => _palette.SplitterHover;
        set => Assign(_palette with { SplitterHover = value });
    }

    /// <summary>Fill of editable fields such as <see cref="XPlatTextBox"/>.</summary>
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

    public static SKColor DisabledBackground
    {
        get => _palette.DisabledBackground;
        set => Assign(_palette with { DisabledBackground = value });
    }

    public static SKColor DisabledForeground
    {
        get => _palette.DisabledForeground;
        set => Assign(_palette with { DisabledForeground = value });
    }

    private static string? _fontFamily;
    private static XPlatFont? _fontFace;
    private static int _fontVersion = 1;
    private static XPlatFont _defaultFont = new(size: 13f);
    private static XPlatFont _captionFont = new(size: 11.5f);
    private static XPlatFont _emphasizedFont = new(size: 13f, weight: 600);
    private static XPlatFont _titleFont = new(size: 24f, weight: 700);
    private static XPlatFont _sectionFont = new(size: 15f, weight: 600);

    /// <summary>
    /// The font family the built-in controls draw with, or null to use the operating system's
    /// default. A backend sets this once at start-up so the UI matches the desktop it runs on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It can be assigned at any time: the theme fonts are rebuilt, and because a control resolves
    /// its font when it measures or paints rather than when it is built, controls that already exist
    /// pick the new face up - and re-lay out, since text at another face needs other room. The
    /// previous font objects are left undisposed because a control may still be holding one from a
    /// paint pass. A handful of fonts is a few bytes, and assigning one runs a handful of times.
    /// </para>
    /// <para>
    /// Assigning a family name also drops a face given to <see cref="UseFont"/>, so this is the way
    /// back to a family the platform provides.
    /// </para>
    /// </remarks>
    public static string? FontFamily
    {
        get => _fontFamily;
        set
        {
            if (string.Equals(_fontFamily, value, StringComparison.Ordinal) && _fontFace is null)
            {
                return;
            }

            _fontFamily = value;
            _fontFace = null;
            RebuildFonts();

            // A static event has no instance to name as the sender.
            _changed?.Invoke(null!);
        }
    }

    /// <summary>
    /// Draws every built-in control with <paramref name="face"/>, whichever desktop the application
    /// runs on: one call instead of assigning a font to every control. This is how an application
    /// says "use this font everywhere":
    /// <c>XPlatTheme.UseFont(XPlatFont.FromFile("Fonts/Inter-Regular.ttf"))</c>.
    /// </summary>
    /// <param name="face">
    /// A font built with <see cref="XPlatFont.FromFile"/>, <see cref="XPlatFont.FromStream"/> or
    /// <see cref="XPlatFont.FromBytes"/>, or any <see cref="XPlatFont"/> whose face is the one to
    /// use. The theme keeps it for the rest of the process and derives its fonts from it, so do not
    /// dispose it afterwards.
    /// </param>
    /// <remarks>
    /// The theme fonts are rebuilt from the face, so <see cref="TitleFont"/>,
    /// <see cref="SectionFont"/> and <see cref="EmphasizedFont"/> become that face at their own sizes
    /// rather than a bold or semibold one: a file holds one face, and no weight is synthesised. It
    /// can be called after the controls exist, the way <see cref="FontFamily"/> can: the effective
    /// font is resolved when it is used, so a live tree switches to the new face and re-lays out.
    /// </remarks>
    public static void UseFont(XPlatFont face)
    {
        ArgumentNullException.ThrowIfNull(face);

        // The previous face is left undisposed for the same reason the previous theme fonts are: a
        // control may still be painting with one of its fonts. Loading a font is a start-up only
        // operation, so holding one face per assignment costs nothing worth reclaiming.
        _fontFace = face;
        _fontFamily = null;
        RebuildFonts();

        // A static event has no instance to name as the sender.
        _changed?.Invoke(null!);
    }

    public static XPlatFont DefaultFont => _defaultFont;

    public static XPlatFont CaptionFont => _captionFont;

    public static XPlatFont EmphasizedFont => _emphasizedFont;

    public static XPlatFont TitleFont => _titleFont;

    public static XPlatFont SectionFont => _sectionFont;

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
    /// One theme font at <paramref name="size"/>, from the loaded face when there is one. A face
    /// loaded from data is a single face, so the weight is only meaningful on the family-name path;
    /// asking the loaded face for semibold would otherwise silently fall back to the system default.
    /// </summary>
    private static XPlatFont ThemeFont(float size, int weight = 400) =>
        _fontFace is { } face ? face.WithSize(size) : new XPlatFont(_fontFamily, size, weight);

    private static void Assign(XPlatPalette palette)
    {
        _palette = palette;
        _changed?.Invoke(null!);
    }
}
