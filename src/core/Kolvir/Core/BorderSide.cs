namespace Kolvir;

/// <summary>One edge of a frame, as a border names it when it is told to treat the edges differently.</summary>
/// <remarks>
/// The order is the order the four edges are stored in, clockwise from the top, so it is also the
/// order a per-edge colour set is indexed by.
/// </remarks>
public enum BorderSide
{
    /// <summary>The top edge.</summary>
    Top,

    /// <summary>The right-hand edge.</summary>
    Right,

    /// <summary>The bottom edge.</summary>
    Bottom,

    /// <summary>The left-hand edge.</summary>
    Left,
}
