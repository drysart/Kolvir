using SkiaSharp;

namespace Kolvir;

/// <summary>
/// Describes the thickness of a frame around a rectangle. Used for both
/// <see cref="Control.Margin"/> and <see cref="Control.Padding"/>.
/// </summary>
public readonly struct Thickness : IEquatable<Thickness>
{
    /// <summary>A thickness of zero on every side.</summary>
    /// <remarks>
    /// Zero is also the default value of <see cref="Control.Margin"/> and
    /// <see cref="Control.Padding"/>, so a control that has never assigned one holds <c>Empty</c>.
    /// </remarks>
    public static readonly Thickness Empty = new(0f);

    /// <summary>A thickness with the same amount of space on every side.</summary>
    /// <param name="uniform">The space on each of the four sides, in logical pixels.</param>
    public Thickness(float uniform)
        : this(uniform, uniform, uniform, uniform)
    {
    }

    /// <summary>A thickness whose sides are grouped by axis rather than given one at a time.</summary>
    /// <param name="horizontal">The space on the left and the right, in logical pixels.</param>
    /// <param name="vertical">The space on the top and the bottom, in logical pixels.</param>
    public Thickness(float horizontal, float vertical)
        : this(horizontal, vertical, horizontal, vertical)
    {
    }

    /// <summary>A thickness with a separate amount of space on each side.</summary>
    /// <param name="left">The space on the left edge, in logical pixels.</param>
    /// <param name="top">The space on the top edge, in logical pixels.</param>
    /// <param name="right">The space on the right edge, in logical pixels.</param>
    /// <param name="bottom">The space on the bottom edge, in logical pixels.</param>
    public Thickness(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    /// <summary>The space on the left edge, in logical pixels.</summary>
    public float Left { get; }

    /// <summary>The space on the top edge, in logical pixels.</summary>
    public float Top { get; }

    /// <summary>The space on the right edge, in logical pixels.</summary>
    public float Right { get; }

    /// <summary>The space on the bottom edge, in logical pixels.</summary>
    public float Bottom { get; }

    /// <summary>
    /// The space the left and right sides take from the width: <see cref="Left"/> plus
    /// <see cref="Right"/>.
    /// </summary>
    public float Horizontal => Left + Right;

    /// <summary>
    /// The space the top and bottom sides take from the height: <see cref="Top"/> plus
    /// <see cref="Bottom"/>.
    /// </summary>
    public float Vertical => Top + Bottom;

    /// <summary>True when all four sides are exactly zero.</summary>
    /// <remarks>
    /// The sides are compared exactly, so a thickness built from values that cancel out - a negative
    /// left and a positive right, say - is not empty, and neither is one with a fractional side that
    /// is close to zero.
    /// </remarks>
    public bool IsEmpty => Left == 0f && Top == 0f && Right == 0f && Bottom == 0f;

    /// <summary>True when both thicknesses have the same four sides.</summary>
    /// <param name="other">The thickness to compare with.</param>
    /// <returns>True when <see cref="Left"/>, <see cref="Top"/>, <see cref="Right"/> and
    /// <see cref="Bottom"/> are equal in the two.</returns>
    public bool Equals(Thickness other) =>
        Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

    /// <summary>True when <paramref name="obj"/> is a thickness with the same four sides.</summary>
    /// <param name="obj">The value to compare with, which may be null or another type.</param>
    /// <returns>False for null or for anything that is not a <see cref="Thickness"/>.</returns>
    public override bool Equals(object? obj) => obj is Thickness other && Equals(other);

    /// <summary>A hash built from the four sides, so equal thicknesses hash alike.</summary>
    /// <returns>The hash code of the four sides, in the order left, top, right, bottom.</returns>
    public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);

    /// <summary>True when both thicknesses have the same four sides.</summary>
    /// <param name="left">The first thickness.</param>
    /// <param name="right">The second thickness.</param>
    /// <returns>The same result as <see cref="Equals(Thickness)"/>.</returns>
    public static bool operator ==(Thickness left, Thickness right) => left.Equals(right);

    /// <summary>True when the two thicknesses differ on any side.</summary>
    /// <param name="left">The first thickness.</param>
    /// <param name="right">The second thickness.</param>
    /// <returns>The negation of the equality operator: true when any side differs.</returns>
    public static bool operator !=(Thickness left, Thickness right) => !left.Equals(right);

    /// <summary>The four sides as text, in the order left, top, right, bottom.</summary>
    /// <returns>A string such as <c>8,0,8,0</c>.</returns>
    public override string ToString() => $"{Left},{Top},{Right},{Bottom}";
}
