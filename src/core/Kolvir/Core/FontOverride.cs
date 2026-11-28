using System.Globalization;

namespace Kolvir;

/// <summary>
/// A partial font description: the members it names replace those of the inherited font, and the
/// members it leaves unset keep the inherited ones.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Font"/> is a complete description - it is a dictionary key, it measures and it draws -
/// so a font with a member left unset would have to refuse to measure or invent a value, and an
/// unset family would collide with the value that already means "the platform's default" (section 5
/// of the font design note). A partial description is therefore a different type, with no measuring
/// or drawing surface at all. It is meaningful only as a case of <see cref="Control.Font"/> (or
/// <see cref="ToolStripItem.Font"/>), where <see cref="Control.EffectiveFont"/> composes it onto the
/// font the control inherits and returns a complete <see cref="Font"/>. Nothing partial ever reaches
/// the font manager.
/// </para>
/// <para>
/// An override is immutable and compares by value: two overrides that name the same members are
/// interchangeable. That is not a convenience - <see cref="Control.Font"/> compares its cases with
/// value equality, so a render pass that rebuilds <c>InheritedFont.WithSize(14f)</c> assigns what the
/// control already has and invalidates nothing.
/// </para>
/// <para>
/// There is no public constructor: an override is written as a chain started by one of the
/// <see cref="InheritedFont"/> members, such as <c>InheritedFont.WithSize(14f)</c>. An unset member is
/// a null one, and an explicitly empty family (<see cref="string.Empty"/>) or a zero size are real
/// values a description can mean - the empty family is the platform's default family, exactly as it
/// is for <see cref="Font"/>.
/// </para>
/// </remarks>
public sealed class FontOverride : IEquatable<FontOverride>
{
    internal FontOverride()
    {
    }

    private FontOverride(string? familyName, float? size, int? weight, bool? italic)
    {
        FamilyName = familyName;
        Size = size;
        Weight = weight;
        Italic = italic;
    }

    /// <summary>
    /// The family to ask for, or null to keep the inherited one.
    /// </summary>
    /// <remarks>
    /// A name resolves exactly as it does for <see cref="Font"/>: a family the platform has, a family
    /// an application registered with <see cref="FontRegistry"/>, or <see cref="string.Empty"/> for
    /// the platform's default family. Null is not that last case - it means the member was never
    /// named, so the inherited family is kept.
    /// </remarks>
    public string? FamilyName { get; }

    /// <summary>The size to ask for, in logical pixels, or null to keep the inherited one.</summary>
    /// <remarks>
    /// Zero is a size a caller can mean and composes to a font that draws at zero; null leaves the
    /// inherited size alone.
    /// </remarks>
    public float? Size { get; }

    /// <summary>
    /// The weight to ask for, on the 1-1000 scale, or null to keep the inherited one.
    /// </summary>
    /// <remarks>
    /// As with <see cref="Font.Weight"/>, the outcome is the family's nearest face: no weight is
    /// synthesised for a family that does not have one.
    /// </remarks>
    public int? Weight { get; }

    /// <summary>
    /// Whether to ask for an italic face, or null to keep the inherited answer.
    /// </summary>
    /// <remarks>
    /// False is an answer and not a gap: it asks for an upright face, which for a family without one
    /// is the face it has.
    /// </remarks>
    public bool? Italic { get; }

    /// <summary>This override with its family replaced.</summary>
    /// <param name="familyName">
    /// The family to ask for, or null to keep the family inherited. An empty or whitespace-only name
    /// means the platform's default family.
    /// </param>
    /// <returns>A new override; this one is unchanged.</returns>
    /// <remarks>
    /// Null keeps the inherited family, which is the opposite of <see cref="Font.WithFamily"/>: a
    /// partial description leaves a member alone where a complete one has to name a value, so there
    /// the same null asks for the platform's default family. The signature is identical and the
    /// meaning is not.
    /// </remarks>
    public FontOverride WithFamily(string? familyName) => new(familyName, Size, Weight, Italic);

    /// <summary>This override with its size replaced.</summary>
    /// <param name="size">The size to ask for, in logical pixels, or null to keep it inherited.</param>
    /// <returns>A new override; this one is unchanged.</returns>
    public FontOverride WithSize(float? size) => new(FamilyName, size, Weight, Italic);

    /// <summary>This override with its weight replaced.</summary>
    /// <param name="weight">
    /// The weight to ask for, on the 1-1000 scale, or null to keep it inherited.
    /// </param>
    /// <returns>A new override; this one is unchanged.</returns>
    public FontOverride WithWeight(int? weight) => new(FamilyName, Size, weight, Italic);

    /// <summary>This override with its italic flag replaced.</summary>
    /// <param name="italic">
    /// True to ask for an italic face, false to ask for an upright one, or null to keep the inherited
    /// answer.
    /// </param>
    /// <returns>A new override; this one is unchanged.</returns>
    public FontOverride WithItalic(bool? italic) => new(FamilyName, Size, Weight, italic);

    /// <summary>This override with the named members replaced and the rest left as they are.</summary>
    /// <param name="family">
    /// The family to ask for, or null to leave this override's family as it is: unset when it was
    /// unset, and the value it already names when it was set. Empty or whitespace-only means the
    /// platform's default family.
    /// </param>
    /// <param name="size">
    /// The size to ask for, in logical pixels, or null to leave this override's size as it is.
    /// </param>
    /// <param name="weight">
    /// The weight to ask for, on the 1-1000 scale, or null to leave this override's weight as it is.
    /// </param>
    /// <param name="italic">
    /// True to ask for an italic face, false to ask for an upright one, or null to leave this
    /// override's slant as it is.
    /// </param>
    /// <returns>
    /// A new override, or this instance itself when every argument is null so a no-op neither
    /// allocates nor changes identity.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Null means "leave this member as the receiver has it", uniformly: an unset member stays unset
    /// and a member the override already names keeps the value it names. The one-call form is
    /// therefore additive - <c>InheritedFont.WithSize(14f).With(weight: 700)</c> names the size and
    /// the weight and inherits the rest.
    /// </para>
    /// <para>
    /// <b>It cannot clear a member.</b> A null argument is a no-op, so <c>With(size: null)</c> keeps
    /// the size this override already names. Clearing is the single-member method:
    /// <see cref="WithSize"/>(null) removes the size, and the other three do the same for their
    /// member. That asymmetry is deliberate - one method asks for values, the other edits the
    /// description's shape.
    /// </para>
    /// <para>
    /// The family argument is the exception that looks like the rule: null leaves the family
    /// inherited here, while a null <c>family</c> argument to <see cref="Font.With"/> keeps that
    /// font's own family. Both mean "leave the receiver's family as it is"; the difference is what
    /// the receiver's family is, unset on an override and concrete on a font.
    /// </para>
    /// </remarks>
    public FontOverride With(
        string? family = null,
        float? size = null,
        int? weight = null,
        bool? italic = null) =>
        family is null && size is null && weight is null && italic is null
            ? this
            : new(family ?? FamilyName, size ?? Size, weight ?? Weight, italic ?? Italic);

    /// <summary>
    /// <paramref name="inherited"/> with every member this override names replaced, and the rest kept.
    /// </summary>
    /// <param name="inherited">The complete font the control would draw with without this override.</param>
    /// <returns>
    /// A complete font - <paramref name="inherited"/> itself when this override names nothing that
    /// differs from it, so the steady state allocates nothing.
    /// </returns>
    /// <remarks>
    /// This is the only thing that turns a partial description into a complete one, and it is where
    /// the invariant recorded in section 5 of the font design note is kept: only a <see cref="Font"/>
    /// reaches the realization.
    /// </remarks>
    internal Font Compose(Font inherited)
    {
        ArgumentNullException.ThrowIfNull(inherited);

        string familyName = FamilyName ?? inherited.FamilyName;
        float size = Size ?? inherited.Size;
        int weight = Weight ?? inherited.Weight;
        bool italic = Italic ?? inherited.Italic;

        return string.Equals(familyName, inherited.FamilyName, StringComparison.Ordinal)
            && size.Equals(inherited.Size)
            && weight == inherited.Weight
            && italic == inherited.Italic
            ? inherited
            : new Font(familyName, size, weight, italic);
    }

    /// <summary>Whether <paramref name="other"/> names the same members.</summary>
    /// <param name="other">The override to compare with.</param>
    /// <returns>True when the family, size, weight and italic all agree, unset ones included.</returns>
    public bool Equals(FontOverride? other) =>
        other is not null &&
        string.Equals(FamilyName, other.FamilyName, StringComparison.Ordinal) &&
        EqualityComparer<float?>.Default.Equals(Size, other.Size) &&
        EqualityComparer<int?>.Default.Equals(Weight, other.Weight) &&
        EqualityComparer<bool?>.Default.Equals(Italic, other.Italic);

    /// <summary>Whether <paramref name="obj"/> is an override that names the same members.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True when it is an equal <see cref="FontOverride"/>.</returns>
    public override bool Equals(object? obj) => Equals(obj as FontOverride);

    /// <summary>A hash code over the four members.</summary>
    /// <returns>The hash code, which agrees with <see cref="Equals(FontOverride)"/>.</returns>
    public override int GetHashCode() => HashCode.Combine(
        FamilyName is null ? 0 : StringComparer.Ordinal.GetHashCode(FamilyName),
        Size,
        Weight,
        Italic);

    /// <summary>The members this override names, for debuggers and log lines.</summary>
    /// <returns>A string such as <c>FontOverride(size = 14)</c>, or <c>FontOverride(none)</c>.</returns>
    public override string ToString()
    {
        var members = new List<string>(4);

        if (FamilyName is { } family)
        {
            members.Add("family = " + (family.Length == 0 ? "platform default" : family));
        }

        if (Size is { } size)
        {
            members.Add(string.Create(CultureInfo.InvariantCulture, $"size = {size}"));
        }

        if (Weight is { } weight)
        {
            members.Add(string.Create(CultureInfo.InvariantCulture, $"weight = {weight}"));
        }

        if (Italic is { } italic)
        {
            members.Add(italic ? "italic = true" : "italic = false");
        }

        return members.Count == 0
            ? "FontOverride(none)"
            : "FontOverride(" + string.Join(", ", members) + ")";
    }
}

/// <summary>
/// The starting points for a <see cref="FontOverride"/>: each member returns an override of the font
/// a control inherits that names one thing.
/// </summary>
/// <remarks>
/// <para>
/// <c>label.Font = InheritedFont.WithSize(14f)</c> is the whole idea: the label keeps the family,
/// weight and slant it inherits and draws at 14 - which is a different statement from
/// <c>Theme.DefaultFont.WithSize(14f)</c>, a complete font that names its base and so stops
/// inheriting everything the parent chain would have supplied.
/// </para>
/// <para>
/// The members chain, because each returns a <see cref="FontOverride"/>:
/// <c>InheritedFont.WithSize(14f).WithItalic(true)</c>. A null argument leaves that member
/// inherited, which is how a chain is narrowed back. <see cref="With"/> sets several members in one
/// call, and <see cref="AsIs"/> is the member that names none.
/// </para>
/// <para>
/// This is a type rather than a value so the name resolves at an assignment with nothing but
/// <c>using Kolvir;</c> in scope.
/// </para>
/// </remarks>
public static class InheritedFont
{
    /// <summary>
    /// The override that names nothing: every member keeps the inherited value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is the identity of this class. <c>AsIs.WithSize(14f)</c> names exactly what
    /// <see cref="WithSize"/> does, and composing it onto a font returns that font object itself,
    /// because nothing it names differs from it.
    /// </para>
    /// <para>
    /// Its use beyond the rename is in conditionals. <see cref="PropertyValue.Inherited"/> is a
    /// different arm of the property's union - it is not a <see cref="FontOverride"/> - so a
    /// conditional whose two branches both describe an override cannot reach for it. With
    /// <c>AsIs</c> both branches can be overrides:
    /// <c>cond ? InheritedFont.WithSize(14f) : InheritedFont.AsIs</c>.
    /// </para>
    /// <para>
    /// Assigning it is equivalent to assigning nothing: the property reports the override where an
    /// unassigned property reports <see cref="PropertyValue.Inherited"/>, and the font it resolves to
    /// is the same object either way.
    /// </para>
    /// </remarks>
    public static readonly FontOverride AsIs = new();

    /// <summary>
    /// An override that names the given members and leaves the rest inherited.
    /// </summary>
    /// <param name="family">
    /// The family to ask for, or null to leave the family inherited. Empty or whitespace-only means
    /// the platform's default family.
    /// </param>
    /// <param name="size">The size to draw at, in logical pixels, or null to leave it inherited.</param>
    /// <param name="weight">
    /// The weight to ask for, on the 1-1000 scale where 400 is regular and 700 is bold, or null to
    /// leave it inherited.
    /// </param>
    /// <param name="italic">
    /// True to ask for an italic face, false to ask for an upright one, or null to leave the
    /// inherited answer.
    /// </param>
    /// <returns>The override; with every argument null, <see cref="AsIs"/>.</returns>
    /// <remarks>
    /// This is <see cref="AsIs"/> with the named members replaced, so null means "leave that member
    /// inherited". It starts from an override that names nothing, so unlike
    /// <see cref="FontOverride.With"/> there is no previously set member for a null to keep: every
    /// null member is unset. Setting several members in one call:
    /// <c>InheritedFont.With(family: "Courier New", weight: 700)</c>.
    /// </remarks>
    public static FontOverride With(
        string? family = null,
        float? size = null,
        int? weight = null,
        bool? italic = null) =>
        AsIs.With(family, size, weight, italic);

    /// <summary>
    /// An override that asks for <paramref name="familyName"/> and leaves the rest inherited.
    /// </summary>
    /// <param name="familyName">
    /// The family to ask for, or null to leave the family inherited. Empty or whitespace-only means
    /// the platform's default family.
    /// </param>
    /// <returns>The override.</returns>
    public static FontOverride WithFamily(string? familyName) => AsIs.WithFamily(familyName);

    /// <summary>An override that asks for <paramref name="size"/> and leaves the rest inherited.</summary>
    /// <param name="size">The size to draw at, in logical pixels, or null to leave it inherited.</param>
    /// <returns>The override.</returns>
    public static FontOverride WithSize(float? size) => AsIs.WithSize(size);

    /// <summary>An override that asks for <paramref name="weight"/> and leaves the rest inherited.</summary>
    /// <param name="weight">
    /// The weight to ask for, on the 1-1000 scale where 400 is regular and 700 is bold, or null to
    /// leave it inherited.
    /// </param>
    /// <returns>The override.</returns>
    public static FontOverride WithWeight(int? weight) => AsIs.WithWeight(weight);

    /// <summary>An override that asks for a slant and leaves the rest inherited.</summary>
    /// <param name="italic">
    /// True to ask for an italic face, false to ask for an upright one, or null to leave the
    /// inherited answer.
    /// </param>
    /// <returns>The override.</returns>
    public static FontOverride WithItalic(bool? italic) => AsIs.WithItalic(italic);
}
