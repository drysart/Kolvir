namespace XPlatUI.Animations;

/// <summary>
/// The static factory methods that create the common easing functions.
/// </summary>
/// <remarks>
/// <para>
/// An easing function maps linear time to animated progress: it takes a value in <c>[0, 1]</c> and
/// returns the fraction of the way from the start value to the end value that should be shown at
/// that moment. Every curve here returns <c>0</c> for <c>0</c> and <c>1</c> for <c>1</c>, which is
/// what makes an animation reach exactly its end value at exactly its end time; a curve an
/// application writes has to do the same.
/// </para>
/// <para>
/// They are plain static methods with no state and no capture, so calling one allocates nothing.
/// Turning one into the <c>Func&lt;double, double&gt;</c> an
/// <see cref="XPlatValueAnimation"/> takes does allocate a delegate, and that happens once when the
/// animation is created rather than once per frame:
/// <code>
/// XPlatValueAnimation.Create(0f, 1f, TimeSpan.FromMilliseconds(300), SetOpacity, easing: XPlatEasing.EaseOutCubic);
/// </code>
/// </para>
/// <para>
/// The names are the usual ones: <c>Quad</c> and <c>Cubic</c> are the polynomial curves and
/// <c>Sine</c> is the quarter- and half-period sinusoid. <c>In</c> accelerates from rest,
/// <c>Out</c> decelerates to rest, and <c>InOut</c> does one and then the other.
/// </para>
/// </remarks>
public static class XPlatEasing
{
    /// <summary>Constant speed: the progress is the time.</summary>
    /// <param name="t">Linear progress in <c>[0, 1]</c>.</param>
    public static double Linear(double t) => t;

    /// <summary>Starts at rest and accelerates, following a parabola.</summary>
    /// <param name="t">Linear progress in <c>[0, 1]</c>.</param>
    public static double EaseInQuad(double t) => t * t;

    /// <summary>Starts fast and coasts to rest, following a parabola.</summary>
    /// <param name="t">Linear progress in <c>[0, 1]</c>.</param>
    public static double EaseOutQuad(double t) => 1d - ((1d - t) * (1d - t));

    /// <summary>Accelerates through the first half and coasts through the second.</summary>
    /// <param name="t">Linear progress in <c>[0, 1]</c>.</param>
    public static double EaseInOutQuad(double t) =>
        t < 0.5d ? 2d * t * t : 1d - (2d * (1d - t) * (1d - t));

    /// <summary>Starts at rest and accelerates, following a cubic.</summary>
    /// <param name="t">Linear progress in <c>[0, 1]</c>.</param>
    public static double EaseInCubic(double t) => t * t * t;

    /// <summary>Starts fast and coasts to rest, following a cubic.</summary>
    /// <param name="t">Linear progress in <c>[0, 1]</c>.</param>
    public static double EaseOutCubic(double t) => 1d - ((1d - t) * (1d - t) * (1d - t));

    /// <summary>Accelerates through the first half and coasts through the second, following a cubic.</summary>
    /// <param name="t">Linear progress in <c>[0, 1]</c>.</param>
    public static double EaseInOutCubic(double t) =>
        t < 0.5d ? 4d * t * t * t : 1d - (4d * (1d - t) * (1d - t) * (1d - t));

    /// <summary>Starts at rest and accelerates gently, following a sinusoid.</summary>
    /// <param name="t">Linear progress in <c>[0, 1]</c>.</param>
    public static double EaseInSine(double t) => 1d - Math.Cos(t * Math.PI / 2d);

    /// <summary>Starts gently and coasts to rest, following a sinusoid.</summary>
    /// <param name="t">Linear progress in <c>[0, 1]</c>.</param>
    public static double EaseOutSine(double t) => Math.Sin(t * Math.PI / 2d);

    /// <summary>Sinusoidal in and out: the gentlest of the curves here at both ends.</summary>
    /// <param name="t">Linear progress in <c>[0, 1]</c>.</param>
    public static double EaseInOutSine(double t) => (1d - Math.Cos(t * Math.PI)) / 2d;
}
