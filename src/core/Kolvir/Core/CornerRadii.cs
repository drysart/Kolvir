namespace Kolvir;

/// <summary>
/// The radius of each of a rectangle's four corners, in logical pixels, in the order top-left,
/// top-right, bottom-right, bottom-left.
/// </summary>
/// <remarks>
/// A rounded rectangle with one radius is the common case, so the single-value constructor and the
/// implicit conversion from a <see cref="float"/> cover it; the four-value constructor is for the
/// shapes that need it - a card whose bottom corners are square, a sheet that only rounds its top.
/// </remarks>
public readonly struct CornerRadii : IEquatable<CornerRadii>
{
    /// <summary>Every corner rounded by <paramref name="uniform"/>.</summary>
    /// <param name="uniform">The radius, in logical pixels.</param>
    public CornerRadii(float uniform)
        : this(uniform, uniform, uniform, uniform)
    {
    }

    /// <summary>Each corner rounded by its own radius, clockwise from the top left.</summary>
    /// <param name="topLeft">The top-left radius, in logical pixels.</param>
    /// <param name="topRight">The top-right radius, in logical pixels.</param>
    /// <param name="bottomRight">The bottom-right radius, in logical pixels.</param>
    /// <param name="bottomLeft">The bottom-left radius, in logical pixels.</param>
    public CornerRadii(float topLeft, float topRight, float bottomRight, float bottomLeft)
    {
        TopLeft = topLeft;
        TopRight = topRight;
        BottomRight = bottomRight;
        BottomLeft = bottomLeft;
    }

    /// <summary>Square corners: the default, and what a control without a frame is.</summary>
    public static CornerRadii None => default;

    /// <summary>The top-left radius, in logical pixels.</summary>
    public float TopLeft { get; }

    /// <summary>The top-right radius, in logical pixels.</summary>
    public float TopRight { get; }

    /// <summary>The bottom-right radius, in logical pixels.</summary>
    public float BottomRight { get; }

    /// <summary>The bottom-left radius, in logical pixels.</summary>
    public float BottomLeft { get; }

    /// <summary>Whether every corner is square.</summary>
    public bool IsNone => TopLeft <= 0f && TopRight <= 0f && BottomRight <= 0f && BottomLeft <= 0f;

    /// <summary>Whether all four corners have the same radius.</summary>
    public bool IsUniform => TopLeft == TopRight && TopRight == BottomRight && BottomRight == BottomLeft;

    /// <summary>The largest of the four radii, which is the radius a shape with one number needs.</summary>
    public float Maximum => MathF.Max(MathF.Max(TopLeft, TopRight), MathF.Max(BottomRight, BottomLeft));

    /// <summary>Every corner rounded by <paramref name="radius"/>, by conversion.</summary>
    /// <param name="radius">The radius, in logical pixels.</param>
    /// <returns>The same value, uniformly.</returns>
    public static implicit operator CornerRadii(float radius) => new(radius);

    /// <summary>Every radius multiplied by <paramref name="factor"/>.</summary>
    /// <param name="radii">The radii to scale.</param>
    /// <param name="factor">The multiplier.</param>
    /// <returns>The scaled radii.</returns>
    public static CornerRadii operator *(CornerRadii radii, float factor) =>
        new(radii.TopLeft * factor, radii.TopRight * factor, radii.BottomRight * factor, radii.BottomLeft * factor);

    /// <summary>The radii added corner by corner.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>The sum of each corner.</returns>
    public static CornerRadii operator +(CornerRadii left, CornerRadii right) =>
        new(
            left.TopLeft + right.TopLeft,
            left.TopRight + right.TopRight,
            left.BottomRight + right.BottomRight,
            left.BottomLeft + right.BottomLeft);

    /// <summary>The radii subtracted corner by corner.</summary>
    /// <param name="left">The value to subtract from.</param>
    /// <param name="right">The value to subtract.</param>
    /// <returns>The difference at each corner.</returns>
    public static CornerRadii operator -(CornerRadii left, CornerRadii right) =>
        new(
            left.TopLeft - right.TopLeft,
            left.TopRight - right.TopRight,
            left.BottomRight - right.BottomRight,
            left.BottomLeft - right.BottomLeft);

    /// <summary>Whether two radii are the same.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True when every corner matches.</returns>
    public static bool operator ==(CornerRadii left, CornerRadii right) => left.Equals(right);

    /// <summary>Whether two radii differ.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>True when any corner differs.</returns>
    public static bool operator !=(CornerRadii left, CornerRadii right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(CornerRadii other) =>
        TopLeft == other.TopLeft
        && TopRight == other.TopRight
        && BottomRight == other.BottomRight
        && BottomLeft == other.BottomLeft;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CornerRadii other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(TopLeft, TopRight, BottomRight, BottomLeft);

    /// <summary>The four radii as text, clockwise from the top left.</summary>
    /// <returns>A string such as <c>8,8,0,0</c>.</returns>
    public override string ToString() => $"{TopLeft},{TopRight},{BottomRight},{BottomLeft}";
}
