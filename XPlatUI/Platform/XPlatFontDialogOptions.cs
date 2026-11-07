namespace XPlatUI;

/// <summary>
/// The starting point of a font dialog, passed to <see cref="IXPlatDialogService.PickFont"/>.
/// </summary>
/// <remarks>
/// <para>
/// Sizes are in logical pixels, the same unit <see cref="XPlatFont.Size"/> uses, so the initial
/// value is whatever the caller's current font reports. Windows and GTK show sizes in points and
/// the backends convert; macOS points and the library's logical pixels are the same unit.
/// </para>
/// <para>
/// The dialog lists the families the operating system has installed. A face loaded from a file with
/// <see cref="XPlatFont.FromFile"/>, <see cref="XPlatFont.FromStream"/> or
/// <see cref="XPlatFont.FromBytes"/> is not a system font unless the application registered it with
/// the OS, so it does not appear here. That is deliberate rather than partial support: the backend
/// cannot tell an arbitrary loaded face from an installed one, and inventing a way to add it would
/// promise a system-wide answer the platform does not give.
/// </para>
/// </remarks>
public sealed class XPlatFontDialogOptions
{
    /// <summary>
    /// Title bar text, used by GTK. Windows' <c>ChooseFont</c> has no title parameter and the macOS
    /// panel has no title of its own, so both use their platform title and ignore this.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The family selected when the dialog opens. Null or empty lets the platform choose its
    /// default, which is usually the system UI font.
    /// </summary>
    public string? InitialFamilyName { get; set; }

    /// <summary>The size selected when the dialog opens, in logical pixels. Defaults to 13, the theme's size.</summary>
    public float InitialSize { get; set; } = 13f;

    /// <summary>
    /// The weight selected when the dialog opens, on the OpenType scale of 1 to 1000. 400 is
    /// regular and 700 is bold.
    /// </summary>
    public int InitialWeight { get; set; } = 400;

    /// <summary>True when the initial selection is italic.</summary>
    public bool InitialItalic { get; set; }

    /// <summary>
    /// The smallest size the dialog offers, in logical pixels. Windows enforces the range in the
    /// dialog itself; GTK and the macOS font panel have no size limits, so there the chosen size is
    /// clamped to the range after the dialog closes instead.
    /// </summary>
    public float MinimumSize { get; set; } = 1f;

    /// <summary>
    /// The largest size the dialog offers, in logical pixels. Windows enforces the range in the
    /// dialog itself; GTK and the macOS font panel have no size limits, so there the chosen size is
    /// clamped to the range after the dialog closes instead.
    /// </summary>
    public float MaximumSize { get; set; } = 200f;

    /// <summary>
    /// <paramref name="size"/> held between <see cref="MinimumSize"/> and <see cref="MaximumSize"/>.
    /// A range that is not usable falls back to a one-pixel range instead of failing the dialog.
    /// </summary>
    /// <param name="size">The size to clamp, in logical pixels.</param>
    /// <returns>The size the dialog should use.</returns>
    public float ClampSize(float size)
    {
        var minimum = MinimumSize > 0f && float.IsFinite(MinimumSize) ? MinimumSize : 1f;
        var maximum = float.IsFinite(MaximumSize) && MaximumSize >= minimum ? MaximumSize : minimum;
        return Math.Clamp(size, minimum, maximum);
    }
}

/// <summary>
/// What the user chose in a font dialog: a family, a size, a weight and whether the face is
/// italic. It is a value, not a font, because <see cref="XPlatFont"/> is
/// <see cref="IDisposable"/> and owns a typeface; handing one back from a dialog would leave the
/// caller guessing who is responsible for it. Build the font with <see cref="CreateFont"/>, which
/// returns one the caller owns, or pass the values to an <see cref="XPlatFont"/> constructor.
/// </summary>
/// <remarks>
/// The selection carries only what the four dialog backends can agree on. A face's width,
/// OpenType features or a simulated bold/italic are not represented; <see cref="XPlatFont"/>
/// resolves the family, weight and italic the same way it does for any other font.
/// </remarks>
public sealed class XPlatFontSelection
{
    /// <summary>Creates a selection.</summary>
    /// <param name="familyName">The family the user chose.</param>
    /// <param name="size">The size, in logical pixels.</param>
    /// <param name="weight">The weight, on the OpenType scale of 1 to 1000.</param>
    /// <param name="italic">True when the chosen face is italic.</param>
    /// <exception cref="ArgumentException"><paramref name="familyName"/> is null, empty or whitespace.</exception>
    public XPlatFontSelection(string familyName, float size, int weight = XPlatFontMapping.NormalWeight, bool italic = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(familyName);

        FamilyName = familyName;
        Size = size;
        Weight = XPlatFontMapping.ClampWeight(weight);
        Italic = italic;
    }

    /// <summary>The family the user chose.</summary>
    public string FamilyName { get; }

    /// <summary>The size, in logical pixels.</summary>
    public float Size { get; }

    /// <summary>The weight on the OpenType scale of 1 to 1000.</summary>
    public int Weight { get; }

    /// <summary>True when the chosen face is italic.</summary>
    public bool Italic { get; }

    /// <summary>
    /// A new font for this selection, owned by the caller. The face is resolved by family name at
    /// the current size, so this is the same answer asking for the family anywhere else gives.
    /// </summary>
    /// <returns>A font the caller has to dispose.</returns>
    public XPlatFont CreateFont() => new(FamilyName, Size, Weight, Italic);
}

/// <summary>
/// The pure conversions a font dialog needs: the OpenType weight scale and the point/pixel scale,
/// in one place so every backend agrees and the suite can exercise them without opening a dialog.
/// </summary>
/// <remarks>
/// <para>
/// The library's weight is the OpenType value <see cref="XPlatFont.Weight"/> reports: 100 thin,
/// 400 regular, 700 bold, 900 black, up to 1000. Windows' <c>LOGFONT.lfWeight</c> and Pango's
/// <c>PangoWeight</c> use that scale directly. AppKit does not: <c>NSFontManager</c> reports a
/// 0-15 index in which 5 is regular and 9 is bold, so the mapping here is the only translation.
/// </para>
/// <para>
/// Not every OpenType weight has an AppKit counterpart. The nine common weights (100 through 900
/// in hundreds) round-trip exactly, and anything between them snaps to the nearest index. That is
/// the platform's limit, not an approximation this code can avoid: the font panel itself selects
/// one of the installed faces.
/// </para>
/// </remarks>
public static class XPlatFontMapping
{
    /// <summary>The smallest weight the OpenType scale defines.</summary>
    public const int MinimumWeight = 1;

    /// <summary>The largest weight the OpenType scale defines.</summary>
    public const int MaximumWeight = 1000;

    /// <summary>The regular weight, 400.</summary>
    public const int NormalWeight = 400;

    /// <summary>The bold weight, 700.</summary>
    public const int BoldWeight = 700;

    /// <summary>The weight <paramref name="weight"/> held to the OpenType range.</summary>
    /// <param name="weight">The weight to clamp.</param>
    /// <returns>A weight between 1 and 1000.</returns>
    public static int ClampWeight(int weight) => Math.Clamp(weight, MinimumWeight, MaximumWeight);

    /// <summary>
    /// An OpenType weight as the index <c>NSFontManager</c> uses, where 5 is regular and 9 is bold.
    /// </summary>
    /// <param name="weight">The OpenType weight.</param>
    /// <returns>An index from 0 to 15.</returns>
    public static int ToAppKitWeight(int weight) => ClampWeight(weight) switch
    {
        <= 150 => 2,
        <= 250 => 3,
        <= 350 => 4,
        <= 450 => 5,
        <= 550 => 6,
        <= 650 => 7,
        <= 750 => 9,
        <= 850 => 10,
        _ => 11,
    };

    /// <summary>
    /// An <c>NSFontManager</c> weight index as an OpenType weight. The nine common weights come back
    /// exactly as <see cref="ToAppKitWeight"/> received them.
    /// </summary>
    /// <param name="appKitWeight">The index <c>NSFontManager</c> reports.</param>
    /// <returns>The OpenType weight.</returns>
    public static int FromAppKitWeight(int appKitWeight) => appKitWeight switch
    {
        <= 2 => 100,
        3 => 200,
        4 => 300,
        5 => NormalWeight,
        6 => 500,
        7 => 600,
        8 => BoldWeight,
        9 => BoldWeight,
        10 => 800,
        11 => 900,
        _ => 900,
    };

    /// <summary>Logical pixels in a point at the 96-DPI scale the library draws at.</summary>
    public const float PixelsPerPoint = 96f / 72f;

    /// <summary>Points to logical pixels: 96 pixels per inch against 72 points per inch.</summary>
    /// <param name="points">A size in points.</param>
    /// <returns>The same size in logical pixels.</returns>
    public static float PointsToPixels(float points) => points * PixelsPerPoint;

    /// <summary>Logical pixels to points, the inverse of <see cref="PointsToPixels"/>.</summary>
    /// <param name="pixels">A size in logical pixels.</param>
    /// <returns>The same size in points.</returns>
    public static float PixelsToPoints(float pixels) => pixels / PixelsPerPoint;
}
