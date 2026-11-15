using SkiaSharp;

namespace Kolvir.Animations.Tests;

/// <summary>
/// The static factory methods for easing curves, and the blends a value animation is given.
/// </summary>
/// <remarks>
/// <para>
/// The easing curves have one contract between them and the timeline: they map 0 to 0 and 1 to 1.
/// Anything else would leave an animation that never quite reaches its start value or never quite
/// reaches its end, which is the kind of bug that looks like a rounding error and is not. The
/// midpoints are pinned as well, because the shape of the curve is the whole reason for having one.
/// </para>
/// <para>
/// The blends are asserted through the same path the timeline uses, so what is checked is the value
/// an animation would assign rather than the arithmetic in isolation.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("Animations")]
public sealed class EasingAndInterpolationTests : AnimationsTestBase
{
    public static IEnumerable<object[]> Curves =>
    [
        [nameof(Easing.Linear), (Func<double, double>)Easing.Linear],
        [nameof(Easing.EaseInQuad), (Func<double, double>)Easing.EaseInQuad],
        [nameof(Easing.EaseOutQuad), (Func<double, double>)Easing.EaseOutQuad],
        [nameof(Easing.EaseInOutQuad), (Func<double, double>)Easing.EaseInOutQuad],
        [nameof(Easing.EaseInCubic), (Func<double, double>)Easing.EaseInCubic],
        [nameof(Easing.EaseOutCubic), (Func<double, double>)Easing.EaseOutCubic],
        [nameof(Easing.EaseInOutCubic), (Func<double, double>)Easing.EaseInOutCubic],
        [nameof(Easing.EaseInSine), (Func<double, double>)Easing.EaseInSine],
        [nameof(Easing.EaseOutSine), (Func<double, double>)Easing.EaseOutSine],
        [nameof(Easing.EaseInOutSine), (Func<double, double>)Easing.EaseInOutSine],
    ];

    [TestMethod]
    [DynamicData(nameof(Curves))]
    public void EveryCurveStartsAtZeroAndEndsAtOne(string name, Func<double, double> curve)
    {
        Assert.AreEqual(0d, curve(0d), 1e-12d, $"{name} must begin at the animation's start value");
        Assert.AreEqual(1d, curve(1d), 1e-12d, $"{name} must settle on the animation's end value");
    }

    [TestMethod]
    [DynamicData(nameof(Curves))]
    public void EveryCurveIsMonotonic(string name, Func<double, double> curve)
    {
        double previous = curve(0d);

        for (int step = 1; step <= 100; step++)
        {
            double next = curve(step / 100d);
            Assert.IsTrue(next >= previous - 1e-12d, $"{name} went backwards at {step} per cent");
            previous = next;
        }
    }

    [TestMethod]
    public void TheCurvesAreDistinguishableAtTheMidpoint()
    {
        // The numbers are the curves' definitions, not a tuning: a quadratic curve at half time is a
        // quarter of the way, and its ease-out mirror is three quarters.
        Assert.AreEqual(0.25d, Easing.EaseInQuad(0.5d), 1e-12d);
        Assert.AreEqual(0.75d, Easing.EaseOutQuad(0.5d), 1e-12d);
        Assert.AreEqual(0.5d, Easing.EaseInOutQuad(0.5d), 1e-12d);
        Assert.AreEqual(0.125d, Easing.EaseInCubic(0.5d), 1e-12d);
        Assert.AreEqual(0.875d, Easing.EaseOutCubic(0.5d), 1e-12d);
        Assert.AreEqual(0.5d, Easing.EaseInOutCubic(0.5d), 1e-12d);
        Assert.AreEqual(0.5d, Easing.Linear(0.5d), 1e-12d);
    }

    [TestMethod]
    public void AnEaseOutCurveCoversMoreGroundEarlyThanLinear()
    {
        double linear = Easing.Linear(0.25d);
        double eased = Easing.EaseOutCubic(0.25d);

        Assert.IsTrue(eased > linear, "an ease-out reaches further than linear in the first quarter");
    }

    [TestMethod]
    public void TheFloatingPointBlendsAreExactAtBothEndsAndLinearBetween()
    {
        Assert.AreEqual(3f, Interpolation.Lerp(3f, 9f, 0d));
        Assert.AreEqual(9f, Interpolation.Lerp(3f, 9f, 1d));
        Assert.AreEqual(6f, Interpolation.Lerp(3f, 9f, 0.5d));

        Assert.AreEqual(3d, Interpolation.Lerp(3d, 9d, 0d));
        Assert.AreEqual(9d, Interpolation.Lerp(3d, 9d, 1d));
        Assert.AreEqual(6d, Interpolation.Lerp(3d, 9d, 0.5d));
    }

    [TestMethod]
    public void AColourBlendsChannelByChannelAndClampsToTheChannelsRange()
    {
        SKColor middle = Interpolation.Lerp(new SKColor(0, 0, 0, 255), new SKColor(200, 100, 40, 5), 0.5d);

        Assert.AreEqual(new SKColor(100, 50, 20, 130), middle);

        // An easing that overshoots must not wrap a channel round to the other end of the byte.
        SKColor overshot = Interpolation.Lerp(new SKColor(0, 0, 0, 0), new SKColor(200, 200, 200, 200), 2d);
        Assert.AreEqual(new SKColor(255, 255, 255, 255), overshot);
    }

    [TestMethod]
    public void PointsSizesAndRectanglesBlendComponentByComponent()
    {
        Assert.AreEqual(new SKPoint(5f, 5f), Interpolation.Lerp(new SKPoint(0f, 0f), new SKPoint(10f, 10f), 0.5d));
        Assert.AreEqual(new SKSize(5f, 15f), Interpolation.Lerp(new SKSize(10f, 10f), new SKSize(0f, 20f), 0.5d));
        Assert.AreEqual(
            new SKRect(5f, 5f, 15f, 15f),
            Interpolation.Lerp(new SKRect(0f, 0f, 10f, 10f), new SKRect(10f, 10f, 20f, 20f), 0.5d));
    }

    [TestMethod]
    public void ARectangleGrowsFromItsCentreWhenItsEdgesAreBlended()
    {
        var start = new SKRect(10f, 10f, 20f, 20f);
        var end = new SKRect(0f, 0f, 30f, 30f);

        var middle = Interpolation.Lerp(start, end, 0.5d);

        Assert.AreEqual(new SKRect(5f, 5f, 25f, 25f), middle);
        Assert.AreEqual(15f, middle.MidX, 1e-4f, "the centre does not move");
        Assert.AreEqual(15f, middle.MidY, 1e-4f);
    }

    [TestMethod]
    public void ACustomTypeIsAnimatedByTheFunctionTheCallerSupplies()
    {
        using var harness = new AnimationHarness();
        string target = string.Empty;

        var animation = new ValueAnimation<string>(
            "a",
            "c",
            TimeSpan.FromMilliseconds(100),
            v => target = v,
            (from, to, t) => t < 0.5d ? from : to);

        Assert.AreEqual("a", animation.From);
        Assert.AreEqual("c", animation.To);
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), animation.Duration);

        var set = harness.Set();
        set.Add(animation);
        set.Start();

        Assert.AreEqual("a", target, "the start value went through the caller's own blend");

        harness.Frame(50);
        Assert.AreEqual("c", target, "and so did the value half way through");
    }
}
