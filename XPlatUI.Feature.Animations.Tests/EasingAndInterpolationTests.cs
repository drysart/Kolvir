using SkiaSharp;

namespace XPlatUI.Animations.Tests;

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
        [nameof(XPlatEasing.Linear), (Func<double, double>)XPlatEasing.Linear],
        [nameof(XPlatEasing.EaseInQuad), (Func<double, double>)XPlatEasing.EaseInQuad],
        [nameof(XPlatEasing.EaseOutQuad), (Func<double, double>)XPlatEasing.EaseOutQuad],
        [nameof(XPlatEasing.EaseInOutQuad), (Func<double, double>)XPlatEasing.EaseInOutQuad],
        [nameof(XPlatEasing.EaseInCubic), (Func<double, double>)XPlatEasing.EaseInCubic],
        [nameof(XPlatEasing.EaseOutCubic), (Func<double, double>)XPlatEasing.EaseOutCubic],
        [nameof(XPlatEasing.EaseInOutCubic), (Func<double, double>)XPlatEasing.EaseInOutCubic],
        [nameof(XPlatEasing.EaseInSine), (Func<double, double>)XPlatEasing.EaseInSine],
        [nameof(XPlatEasing.EaseOutSine), (Func<double, double>)XPlatEasing.EaseOutSine],
        [nameof(XPlatEasing.EaseInOutSine), (Func<double, double>)XPlatEasing.EaseInOutSine],
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
        Assert.AreEqual(0.25d, XPlatEasing.EaseInQuad(0.5d), 1e-12d);
        Assert.AreEqual(0.75d, XPlatEasing.EaseOutQuad(0.5d), 1e-12d);
        Assert.AreEqual(0.5d, XPlatEasing.EaseInOutQuad(0.5d), 1e-12d);
        Assert.AreEqual(0.125d, XPlatEasing.EaseInCubic(0.5d), 1e-12d);
        Assert.AreEqual(0.875d, XPlatEasing.EaseOutCubic(0.5d), 1e-12d);
        Assert.AreEqual(0.5d, XPlatEasing.EaseInOutCubic(0.5d), 1e-12d);
        Assert.AreEqual(0.5d, XPlatEasing.Linear(0.5d), 1e-12d);
    }

    [TestMethod]
    public void AnEaseOutCurveCoversMoreGroundEarlyThanLinear()
    {
        double linear = XPlatEasing.Linear(0.25d);
        double eased = XPlatEasing.EaseOutCubic(0.25d);

        Assert.IsTrue(eased > linear, "an ease-out reaches further than linear in the first quarter");
    }

    [TestMethod]
    public void TheFloatingPointBlendsAreExactAtBothEndsAndLinearBetween()
    {
        Assert.AreEqual(3f, XPlatInterpolation.Lerp(3f, 9f, 0d));
        Assert.AreEqual(9f, XPlatInterpolation.Lerp(3f, 9f, 1d));
        Assert.AreEqual(6f, XPlatInterpolation.Lerp(3f, 9f, 0.5d));

        Assert.AreEqual(3d, XPlatInterpolation.Lerp(3d, 9d, 0d));
        Assert.AreEqual(9d, XPlatInterpolation.Lerp(3d, 9d, 1d));
        Assert.AreEqual(6d, XPlatInterpolation.Lerp(3d, 9d, 0.5d));
    }

    [TestMethod]
    public void AColourBlendsChannelByChannelAndClampsToTheChannelsRange()
    {
        SKColor middle = XPlatInterpolation.Lerp(new SKColor(0, 0, 0, 255), new SKColor(200, 100, 40, 5), 0.5d);

        Assert.AreEqual(new SKColor(100, 50, 20, 130), middle);

        // An easing that overshoots must not wrap a channel round to the other end of the byte.
        SKColor overshot = XPlatInterpolation.Lerp(new SKColor(0, 0, 0, 0), new SKColor(200, 200, 200, 200), 2d);
        Assert.AreEqual(new SKColor(255, 255, 255, 255), overshot);
    }

    [TestMethod]
    public void PointsSizesAndRectanglesBlendComponentByComponent()
    {
        Assert.AreEqual(new SKPoint(5f, 5f), XPlatInterpolation.Lerp(new SKPoint(0f, 0f), new SKPoint(10f, 10f), 0.5d));
        Assert.AreEqual(new SKSize(5f, 15f), XPlatInterpolation.Lerp(new SKSize(10f, 10f), new SKSize(0f, 20f), 0.5d));
        Assert.AreEqual(
            new SKRect(5f, 5f, 15f, 15f),
            XPlatInterpolation.Lerp(new SKRect(0f, 0f, 10f, 10f), new SKRect(10f, 10f, 20f, 20f), 0.5d));
    }

    [TestMethod]
    public void ARectangleGrowsFromItsCentreWhenItsEdgesAreBlended()
    {
        var start = new SKRect(10f, 10f, 20f, 20f);
        var end = new SKRect(0f, 0f, 30f, 30f);

        var middle = XPlatInterpolation.Lerp(start, end, 0.5d);

        Assert.AreEqual(new SKRect(5f, 5f, 25f, 25f), middle);
        Assert.AreEqual(15f, middle.MidX, 1e-4f, "the centre does not move");
        Assert.AreEqual(15f, middle.MidY, 1e-4f);
    }

    [TestMethod]
    public void ACustomTypeIsAnimatedByTheFunctionTheCallerSupplies()
    {
        using var harness = new AnimationHarness();
        string target = string.Empty;

        var animation = new XPlatValueAnimation<string>(
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
