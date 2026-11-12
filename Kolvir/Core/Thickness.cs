using SkiaSharp;

namespace Kolvir;

/// <summary>
/// Describes the thickness of a frame around a rectangle. Used for both
/// <see cref="Control.Margin"/> and <see cref="Control.Padding"/>.
/// </summary>
public readonly struct Thickness : IEquatable<Thickness>
{
    public static readonly Thickness Empty = new(0f);

    public Thickness(float uniform)
        : this(uniform, uniform, uniform, uniform)
    {
    }

    public Thickness(float horizontal, float vertical)
        : this(horizontal, vertical, horizontal, vertical)
    {
    }

    public Thickness(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public float Left { get; }

    public float Top { get; }

    public float Right { get; }

    public float Bottom { get; }

    public float Horizontal => Left + Right;

    public float Vertical => Top + Bottom;

    public bool IsEmpty => Left == 0f && Top == 0f && Right == 0f && Bottom == 0f;

    public bool Equals(Thickness other) =>
        Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

    public override bool Equals(object? obj) => obj is Thickness other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);

    public static bool operator ==(Thickness left, Thickness right) => left.Equals(right);

    public static bool operator !=(Thickness left, Thickness right) => !left.Equals(right);

    public override string ToString() => $"{Left},{Top},{Right},{Bottom}";
}
