namespace Kolvir;

/// <summary>
/// One font description, in the form <see cref="FontManager"/> keys on.
/// </summary>
/// <remarks>
/// <para>
/// This is the description as a value: family name, size, weight and italic, with equality over
/// exactly those four fields. The manager cannot be keyed by <see cref="Font"/> instead, because a
/// key that held the font would keep the font alive, the font keeps its realization alive, so the
/// realization's finalizer would never run and one entry would leak per description for the life of
/// the process. A value key leaves the only strong reference to a realization with the fonts that
/// asked for it.
/// </para>
/// <para>
/// The family name is normalised by <see cref="Font"/> already: null, empty and whitespace are all
/// the one unnamed key. Sizes compare with exact <c>float</c> equality rather than a tolerance,
/// because two sizes that differ at all can measure differently and that is the entire question a
/// key answers.
/// </para>
/// </remarks>
internal readonly struct FontKey : IEquatable<FontKey>
{
    /// <summary>Creates a key from the four description fields.</summary>
    /// <param name="family">The normalised family name, or the empty string for no family.</param>
    /// <param name="size">The size in logical pixels.</param>
    /// <param name="weight">The requested weight on the 1-1000 scale.</param>
    /// <param name="italic">True when an italic face was requested.</param>
    internal FontKey(string family, float size, int weight, bool italic)
    {
        Family = family;
        Size = size;
        Weight = weight;
        Italic = italic;
    }

    /// <summary>The normalised family name; the empty string means the platform's default family.</summary>
    internal string Family { get; }

    /// <summary>The size in logical pixels.</summary>
    internal float Size { get; }

    /// <summary>The requested weight on the 1-1000 scale.</summary>
    internal int Weight { get; }

    /// <summary>True when an italic face was requested.</summary>
    internal bool Italic { get; }

    /// <summary>Whether <paramref name="other"/> is the same description.</summary>
    /// <param name="other">The key to compare with.</param>
    /// <returns>True when all four fields match.</returns>
    public bool Equals(FontKey other) =>
        Size == other.Size &&
        Weight == other.Weight &&
        Italic == other.Italic &&
        string.Equals(Family, other.Family, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="obj"/> is an equal key.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True when it is a <see cref="FontKey"/> with the same fields.</returns>
    public override bool Equals(object? obj) => obj is FontKey other && Equals(other);

    /// <summary>A hash code over the four fields.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Family, Size, Weight, Italic);
}
