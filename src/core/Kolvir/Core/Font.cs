using System.Globalization;

namespace Kolvir;

/// <summary>
/// A font description: a family name, a size, a weight and an italic flag, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="Font"/> is cheap to create and cheap to throw away, which is what a Virtual DOM
/// render needs: it builds one per text node per pass. Creating one calls nothing native - with one
/// exception, the first font constructed without a family, which asks the platform for its default
/// family name once and caches the answer for the process. The heavyweight object - the typeface, the
/// <c>SKFont</c>, the metrics and the measurement caches - is created the first time this font is
/// asked to measure or draw, and is shared by every font with the same description.
/// </para>
/// <para>
/// Two fonts with the same family, size, weight and italic compare equal, and equal fonts draw
/// with the same face: they resolve to one shared realization. That is why assigning a font a
/// control already has is a no-op, and why a control's cached layout survives a re-render that
/// rebuilds the same font.
/// </para>
/// <para>
/// The family name is the description's own name and is reported as it was given:
/// <see cref="FamilyName"/> never reports the face the platform happened to resolve. A description
/// constructed with no family - null, empty or whitespace - names the platform's default
/// <em>family</em> instead of staying blank, so it can ask for a weight or a slant like any other and
/// so that it shares one realization with the same family named explicitly. Only a platform that
/// reports no default family at all leaves the name empty. <see cref="Weight"/> and
/// <see cref="Italic"/> are likewise what was asked for, not what the resolved face contains.
/// </para>
/// <para>
/// A face that is not one the operating system provides is registered with
/// <see cref="FontRegistry"/> under a family name, and then named here like any other family.
/// Nothing is owned and nothing is disposable: a font holds a description, and the realization it
/// acquired lives exactly as long as some font still refers to it.
/// </para>
/// </remarks>
public sealed class Font : IEquatable<Font>
{
    private readonly string _familyName;

    /// <summary>
    /// The realization this font acquired, or null before it has needed one. Never disposed: the
    /// object releases its own native handles when it is collected, and the manager holds it
    /// weakly, so the last font that refers to it decides when that is.
    /// </summary>
    private RealizedFont? _realized;

    /// <summary>The resolution epoch <see cref="_realized"/> was created under.</summary>
    private int _realizedEpoch = -1;

    /// <summary>
    /// Creates a font at <paramref name="size"/> in the family the platform treats as its default.
    /// </summary>
    /// <param name="size">The size to draw at, in logical pixels.</param>
    /// <param name="bold">True to ask for weight 700 instead of 400.</param>
    /// <param name="italic">True to ask for an italic face.</param>
    /// <remarks>
    /// The family is the platform's default one, named rather than left unset: the same call can still
    /// draw with a different face on each desktop, but "the default family, in bold" is something the
    /// description can ask for and get. Pass a family name, or register a face with
    /// <see cref="FontRegistry"/> and name it, when text has to look the same everywhere.
    /// </remarks>
    public Font(float size, bool bold = false, bool italic = false)
        : this(null, size, bold, italic)
    {
    }

    /// <summary>
    /// Creates a font in <paramref name="familyName"/> at <paramref name="size"/>.
    /// </summary>
    /// <param name="familyName">
    /// The family to resolve: one the platform has, or one an application registered with
    /// <see cref="FontRegistry"/>. Null, empty or whitespace means the platform's default family, which
    /// is named here rather than left unset. A family nothing resolves falls back to the platform's
    /// default face rather than failing.
    /// </param>
    /// <param name="size">The size to draw at, in logical pixels.</param>
    /// <param name="bold">True to ask for weight 700 instead of 400.</param>
    /// <param name="italic">True to ask for an italic face.</param>
    /// <remarks>
    /// Either resolution selects the nearest face the family has to the requested weight and
    /// slant, and neither synthesises one the family lacks: asking for bold in a family that only
    /// has a regular face draws the regular face. <see cref="Weight"/> and <see cref="Italic"/>
    /// therefore report what was asked for, not what was used.
    /// </remarks>
    public Font(string? familyName, float size, bool bold = false, bool italic = false)
        : this(familyName, size, bold ? 700 : 400, italic)
    {
    }

    /// <summary>
    /// Creates a font at <paramref name="size"/> in the platform's default family, at a numeric
    /// weight.
    /// </summary>
    /// <param name="size">The size to draw at, in logical pixels.</param>
    /// <param name="weight">
    /// The weight to ask for, on the 1-1000 scale where 400 is regular and 700 is bold.
    /// </param>
    /// <param name="italic">True to ask for an italic face.</param>
    /// <remarks>
    /// As with the overload that takes a <c>bold</c> flag, the family is whatever the platform treats
    /// as its default, so the face is not the same on every desktop.
    /// </remarks>
    public Font(float size, int weight, bool italic = false)
        : this(null, size, weight, italic)
    {
    }

    /// <summary>
    /// Creates a font in <paramref name="familyName"/> at <paramref name="size"/> and a numeric
    /// weight.
    /// </summary>
    /// <param name="familyName">
    /// The family to resolve, or null for the platform's default family, which is named rather than
    /// left unset. A family the platform does not have also falls back to the default face.
    /// </param>
    /// <param name="size">The size to draw at, in logical pixels.</param>
    /// <param name="weight">
    /// The weight to ask for, on the 1-1000 scale where 400 is regular and 700 is bold. The family's
    /// nearest weight is used.
    /// </param>
    /// <param name="italic">True to ask for an italic face.</param>
    /// <remarks>
    /// Unlike a size, which the library scales itself, the weight and the slant are answered by
    /// whichever faces the family has: the choice is the platform's for a system family, and the
    /// application's for a family it registered.
    /// </remarks>
    public Font(string? familyName, float size, int weight, bool italic = false)
    {
        // A description always names a real family: "no family" means *the platform's default family*,
        // and resolving it here rather than at realization is what makes `new Font(13f)` and
        // `new Font(preferredFamily, 13f)` one key and therefore one realization, and what lets a
        // weight be asked of a description that was never given a family. A platform that reports no
        // default family leaves the description unnamed, which is the same behaviour as before the
        // rule rather than a failure.
        _familyName = string.IsNullOrWhiteSpace(familyName)
            ? RealizedFont.DefaultFamilyName() ?? string.Empty
            : familyName;
        Size = size;
        Weight = weight;
        Italic = italic;
    }

    /// <summary>The size this font draws at, in logical pixels.</summary>
    public float Size { get; }

    /// <summary>
    /// The weight that was asked for, on the 1-1000 scale where 400 is regular and 700 is bold.
    /// </summary>
    /// <remarks>
    /// This is the description's value, not a report of the face that resolved. A family that has
    /// only a regular face draws the regular face for every weight, and a registered face reports
    /// the style of its file through <see cref="FontRegistry.Families"/>.
    /// </remarks>
    public int Weight { get; }

    /// <summary>Whether an italic face was asked for.</summary>
    /// <remarks>
    /// As with <see cref="Weight"/>, this says what the description asked for. No slant is
    /// synthesised, so a family without an italic face draws its regular one.
    /// </remarks>
    public bool Italic { get; }

    /// <summary>
    /// The family name this font was created with, or - when it was created without one - the name of
    /// the platform's default family.
    /// </summary>
    /// <remarks>
    /// An echo of the description rather than a report of the face: a font naming a family the machine
    /// does not have answers that name, even though it draws with the platform's default face. That is
    /// what makes <c>new Font(f.FamilyName, f.Size, f.Weight, f.Italic)</c> reproduce the same font.
    /// A description created without a family is named when it is constructed, so this reports a real
    /// family rather than the empty string unless the platform reports no default family at all.
    /// </remarks>
    public string FamilyName => _familyName;

    /// <summary>Distance between two baselines, in logical pixels.</summary>
    /// <remarks>
    /// Reading this realizes the font: the number comes from the face's own metrics, which is the
    /// one native read this property cannot avoid.
    /// </remarks>
    public float LineHeight => Realize().LineHeight;

    /// <summary>Distance from the top of a line box to the baseline.</summary>
    /// <remarks>Reading this realizes the font, for the same reason <see cref="LineHeight"/> does.</remarks>
    public float BaselineOffset => Realize().BaselineOffset;

    /// <summary>The advance width of <paramref name="text"/> in this font, in logical pixels.</summary>
    /// <param name="text">The text to measure. Null or an empty string measures zero.</param>
    /// <returns>The width the text occupies when drawn at this font's size.</returns>
    /// <remarks>
    /// The width comes from the face's own advances, so a face registered from data measures the
    /// same on every platform; a face resolved by family name measures however that platform's file
    /// lays the text out. Empty text answers zero without realizing the font at all.
    /// </remarks>
    public float MeasureWidth(string? text) =>
        string.IsNullOrEmpty(text) ? 0f : Realize().MeasureWidth(text);

    /// <summary>A font with the same family, weight and italic at another size.</summary>
    /// <param name="size">The size for the new font, in logical pixels.</param>
    /// <returns>A new description; this one is unchanged.</returns>
    public Font WithSize(float size) => Derive(size, Weight, Italic);

    /// <summary>This font with the same size, weight and italic in <paramref name="familyName"/>.</summary>
    /// <param name="familyName">
    /// The family for the new font, or null for the platform's default family. An empty or
    /// whitespace-only name also means the platform's default family.
    /// </param>
    /// <returns>A new description; this one is unchanged.</returns>
    /// <remarks>
    /// <para>
    /// Null means the platform's default family because a <see cref="Font"/> always names a real
    /// family: the constructor resolves the argument exactly as it does for
    /// <c>new Font((string?)null, size)</c>.
    /// </para>
    /// <para>
    /// That is the opposite of <see cref="FontOverride.WithFamily"/>, which leaves the family
    /// inherited when it is given null, and the opposite of this type's own
    /// <see cref="With"/>, where a null <c>family</c> argument keeps this font's family. On this
    /// method null is a value, not an omission.
    /// </para>
    /// </remarks>
    public Font WithFamily(string? familyName) => new(familyName, Size, Weight, Italic);

    /// <summary>This font with its weight replaced.</summary>
    /// <param name="weight">
    /// The weight to ask for, on the 1-1000 scale where 400 is regular and 700 is bold.
    /// </param>
    /// <returns>A new description; this one is unchanged.</returns>
    /// <remarks>
    /// As with the constructor, the family's nearest weight is used and none is synthesised, so
    /// <see cref="Weight"/> reports what was asked for rather than what resolved.
    /// </remarks>
    public Font WithWeight(int weight) => Derive(Size, weight, Italic);

    /// <summary>This font with its slant replaced.</summary>
    /// <param name="italic">True to ask for an italic face, false to ask for an upright one.</param>
    /// <returns>A new description; this one is unchanged.</returns>
    /// <remarks>
    /// No slant is synthesised: asking for italic in a family without an italic face draws the face
    /// the family has, and <see cref="Italic"/> still reports the request.
    /// </remarks>
    public Font WithItalic(bool italic) => Derive(Size, Weight, italic);

    /// <summary>This font with the named members replaced and the rest kept.</summary>
    /// <param name="family">
    /// The family for the new font, or null to keep this font's family. Null here is not the null
    /// <see cref="WithFamily"/> takes: this method leaves the family alone, where that one asks for
    /// the platform's default family.
    /// </param>
    /// <param name="size">The size for the new font, in logical pixels, or null to keep this one.</param>
    /// <param name="weight">
    /// The weight to ask for, on the 1-1000 scale, or null to keep this font's weight.
    /// </param>
    /// <param name="italic">
    /// True to ask for an italic face, false to ask for an upright one, or null to keep this font's
    /// slant.
    /// </param>
    /// <returns>
    /// A new description, or this instance itself when every argument is null so a no-op neither
    /// allocates nor changes identity.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A <see cref="Font"/> names every member, so null cannot clear anything: it means "leave this
    /// member as the receiver has it", which is the same rule <see cref="FontOverride.With"/> uses.
    /// The visible outcome is different for the family, because the two types give null different
    /// meanings there: this method keeps this font's family, and a null <c>family</c> argument never
    /// reaches <see cref="WithFamily"/>'s "the platform's default" case.
    /// </para>
    /// <para>
    /// This is the one-call form of the four single-member modifiers:
    /// <c>font.With(family: "Courier New", weight: 700)</c> replaces exactly those two members and
    /// keeps the size and the slant.
    /// </para>
    /// </remarks>
    public Font With(
        string? family = null,
        float? size = null,
        int? weight = null,
        bool? italic = null) =>
        family is null && size is null && weight is null && italic is null
            ? this
            : new Font(family ?? _familyName, size ?? Size, weight ?? Weight, italic ?? Italic);

    /// <summary>
    /// A font with the same family at <paramref name="size"/>, <paramref name="weight"/> and
    /// <paramref name="italic"/> style.
    /// </summary>
    /// <param name="size">The size for the new font, in logical pixels.</param>
    /// <param name="weight">The weight to ask for, on the 1-1000 scale.</param>
    /// <param name="italic">True to ask for an italic face.</param>
    /// <returns>A new description; this one is unchanged.</returns>
    /// <remarks>
    /// Pure description arithmetic: the family is carried across as it is, because the constructor has
    /// already resolved "no family" to the platform's default family name. This used to need a special
    /// case - naming the default family only when the style changed - because a description with no
    /// family could not express "the default family in bold"; resolving the name at construction makes
    /// that unnecessary, and section 2.1 of the note records why the special case existed and what it
    /// was hiding.
    /// </remarks>
    internal Font Derive(float size, int weight, bool italic) =>
        new(_familyName, size, weight, italic);

    /// <summary>This description in the form the manager keys on.</summary>
    internal FontKey Key => new(_familyName, Size, Weight, Italic);

    /// <summary>
    /// The shared, heavyweight font this description resolves to: created on first use, and reused
    /// by every equal font for as long as it is alive.
    /// </summary>
    /// <returns>The realization for this description.</returns>
    /// <remarks>
    /// The epoch is compared before use because registering or unregistering a family changes what
    /// a description resolves to without changing the description. Reading it first means an
    /// invalidation that lands while this call runs makes the next call resolve again rather than
    /// caching the stale answer.
    /// </remarks>
    internal RealizedFont Realize()
    {
        int epoch = FontManager.Epoch;
        var realized = _realized;

        if (realized is null || _realizedEpoch != epoch)
        {
            realized = FontManager.Realize(this);
            _realizedEpoch = epoch;
            _realized = realized;
        }

        return realized;
    }

    /// <summary>Whether <paramref name="other"/> describes the same font.</summary>
    /// <param name="other">The font to compare with.</param>
    /// <returns>True when the family, size, weight and italic are all the same.</returns>
    public bool Equals(Font? other) =>
        other is not null &&
        Size == other.Size &&
        Weight == other.Weight &&
        Italic == other.Italic &&
        string.Equals(_familyName, other._familyName, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="obj"/> is a font that describes the same font.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True when it is an equal <see cref="Font"/>.</returns>
    public override bool Equals(object? obj) => Equals(obj as Font);

    /// <summary>A hash code over the four description fields.</summary>
    /// <returns>The hash code, which agrees with <see cref="Equals(Font)"/>.</returns>
    public override int GetHashCode() =>
        HashCode.Combine(Size, Weight, Italic, StringComparer.Ordinal.GetHashCode(_familyName));

    /// <summary>The description, for debuggers and log lines.</summary>
    /// <returns>The family, size, weight and slant, without realizing the font.</returns>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"Font({(_familyName.Length == 0 ? "platform default" : _familyName)}, {Size}, w{Weight}, {(Italic ? "italic" : "upright")})");
}
