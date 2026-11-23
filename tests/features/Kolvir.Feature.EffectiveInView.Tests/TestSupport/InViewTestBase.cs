using SkiaSharp;

namespace Kolvir.Feature.InView.Tests;

/// <summary>
/// The base every test class derives from, so the feature's process-wide state is put back between
/// tests and one test's controls cannot answer for another's.
/// </summary>
/// <remarks>
/// The feature keeps its cache and its watched set in static weak tables and subscribes once to the
/// static layout event, all of which are process-wide by design. Clearing them is what lets the
/// tests assert on exact counts and run in any order.
/// </remarks>
public abstract class InViewTestBase
{
    [TestInitialize]
    public void ResetFeature()
    {
        EffectiveInViewState.ResetForTests();
        Theme.Palette = Palette.Light;
    }

    [TestCleanup]
    public void RestoreProcessState() => Theme.Palette = Palette.Light;

    /// <summary>A panel small enough to stay on screen, with its background left transparent.</summary>
    protected static Panel Root() => new() { Padding = new Thickness(0f) };

    /// <summary>A control at an explicit rectangle with an opaque background, so it covers what it overlaps.</summary>
    /// <param name="bounds">Where it sits in its parent's client coordinates.</param>
    /// <param name="colour">The colour it paints; opaque by default.</param>
    protected static Panel Cover(SKRect bounds, SKColor? colour = null) =>
        new() { Bounds = bounds, BackColor = colour ?? SKColors.Red };

    /// <summary>A control at an explicit rectangle that paints its background only.</summary>
    /// <param name="bounds">Where it sits in its parent's client coordinates.</param>
    protected static Panel Item(SKRect bounds) => new() { Bounds = bounds, BackColor = SKColors.Blue };
}
