using System.Runtime.CompilerServices;
using SkiaSharp;

namespace Kolvir.Feature.InView.Tests;

/// <summary>
/// The change handlers: when they run, when they must not, and what the cache is allowed to remember.
/// </summary>
/// <remarks>
/// The interesting states are the quiet ones. A handler that fires on a pass that changed nothing, or
/// a cache that recomputes on every read, would both pass a test that only checked the value - which
/// is why these count calls and computations rather than asserting the answer.
/// </remarks>
[TestClass]
[TestCategory("Feature")]
[TestCategory("EffectiveInView")]
public sealed class EffectiveInViewHandlerTests : InViewTestBase
{
    [TestMethod]
    public void TheHandlerFiresOnChangeAndOnlyOnChange()
    {
        var panel = ScrollPanel(out var row);

        using var scene = new InViewScene(panel, 200f, 100f);

        var values = new List<bool>();

        using var subscription = row.AddEffectiveInViewChangedHandler(
            sender => values.Add(((Control)sender).EffectiveInView));

        scene.Layout();
        Assert.AreEqual(0, values.Count, "the first pass after registering is the baseline");

        panel.ScrollTo(0f, 80f);
        panel.ScrollTo(0f, 0f);

        CollectionAssert.AreEqual(new[] { false, true }, values);
    }

    [TestMethod]
    public void ALayoutThatChangesNothingDoesNotFireTheHandler()
    {
        var panel = ScrollPanel(out var row);

        using var scene = new InViewScene(panel, 200f, 100f);

        int count = 0;

        using var subscription = row.AddEffectiveInViewChangedHandler(_ => count++);

        scene.Layout();
        scene.Layout();
        scene.Paint();

        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public void TheInitialObservationDoesNotFireTheHandler()
    {
        var root = Root();
        var target = Item(new SKRect(20f, 20f, 60f, 60f));
        root.Controls.Add(target);

        int count = 0;

        using var subscription = target.AddEffectiveInViewChangedHandler(_ => count++);

        using var scene = new InViewScene(root, 200f, 100f);

        Assert.IsTrue(target.EffectiveInView);
        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public void RepeatedReadsDoNotRecomputeUntilTheGeometryChanges()
    {
        var panel = ScrollPanel(out var row);

        using var scene = new InViewScene(panel, 200f, 100f);

        int before = EffectiveInViewState.ComputeCount;

        Assert.IsTrue(row.EffectiveInView);

        int computed = EffectiveInViewState.ComputeCount;
        Assert.AreEqual(before + 1, computed);

        Assert.IsTrue(row.EffectiveInView);
        Assert.AreEqual(computed, EffectiveInViewState.ComputeCount, "the second read reuses the cached answer");

        panel.ScrollTo(0f, 80f);

        Assert.IsFalse(row.EffectiveInView);
        Assert.AreEqual(computed + 1, EffectiveInViewState.ComputeCount, "the scroll was a new pass");
    }

    [TestMethod]
    public void AMoveWithoutALayoutIsSeenOnTheNextRead()
    {
        var root = Root();
        var target = Item(new SKRect(20f, 20f, 60f, 60f));
        root.Controls.Add(target);

        using var scene = new InViewScene(root, 200f, 100f);

        Assert.IsTrue(target.EffectiveInView);

        target.Location = new SKPoint(300f, 20f);

        Assert.IsFalse(target.EffectiveInView, "a bounds change invalidates the cached answer without a pass");
    }

    [TestMethod]
    public void AHandlerRegisteredBeforeTheControlIsHostedFiresWhenTheValueChanges()
    {
        var root = Root();
        var target = Item(new SKRect(100f, 10f, 140f, 50f));
        root.Controls.Add(target);

        int count = 0;

        using var subscription = target.AddEffectiveInViewChangedHandler(_ => count++);

        using var scene = new InViewScene(root, 200f, 100f);

        Assert.AreEqual(0, count, "the first pass establishes the baseline");

        target.Location = new SKPoint(300f, 10f);
        scene.Layout();

        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public void AHandlerThatLaysOutAgainDoesNotRecurse()
    {
        var root = Root();
        var target = Item(new SKRect(20f, 20f, 80f, 80f));
        var cover = Cover(new SKRect(20f, 20f, 80f, 80f));

        root.Controls.Add(target);
        root.Controls.Add(cover);

        using var scene = new InViewScene(root, 200f, 100f);

        int count = 0;

        using var subscription = target.AddEffectiveInViewChangedHandler(sender =>
        {
            count++;
            ((Control)sender).Host!.PerformLayout();
        });

        scene.Layout();
        Assert.AreEqual(0, count);

        cover.Bounds = new SKRect(300f, 300f, 340f, 340f);
        scene.Layout();

        Assert.AreEqual(1, count, "the pass a handler asks for completes but reports nothing");
    }

    [TestMethod]
    public void AControlDetachedByAHandlerSettlesOnFalse()
    {
        var root = Root();
        var first = Item(new SKRect(20f, 20f, 80f, 80f));
        var second = Item(new SKRect(120f, 20f, 180f, 80f));
        var cover = Cover(new SKRect(20f, 20f, 80f, 80f));

        root.Controls.Add(first);
        root.Controls.Add(second);
        root.Controls.Add(cover);

        using var scene = new InViewScene(root, 200f, 100f);

        Assert.IsFalse(first.EffectiveInView);
        Assert.IsTrue(second.EffectiveInView);

        first.AddEffectiveInViewChangedHandler(_ => root.Controls.Remove(second));

        int detached = 0;
        bool lastValue = true;

        second.AddEffectiveInViewChangedHandler(sender =>
        {
            detached++;
            lastValue = ((Control)sender).EffectiveInView;
        });

        scene.Layout();
        Assert.AreEqual(0, detached, "the baselines are taken without firing");

        cover.Bounds = new SKRect(300f, 300f, 340f, 340f);
        scene.Layout();

        Assert.IsTrue(first.EffectiveInView);
        Assert.AreEqual(0, detached, "the detached control's value did not change until it left the tree");

        scene.Layout();

        Assert.AreEqual(1, detached);
        Assert.IsFalse(lastValue);
        Assert.IsFalse(second.EffectiveInView);
    }

    [TestMethod]
    public void ThePassComputesOnlyWhileSomeoneIsListening()
    {
        var root = Root();
        var target = Item(new SKRect(20f, 20f, 60f, 60f));
        root.Controls.Add(target);

        using var scene = new InViewScene(root, 200f, 100f);

        int before = EffectiveInViewState.ComputeCount;

        scene.Layout();
        scene.Layout();

        Assert.AreEqual(before, EffectiveInViewState.ComputeCount, "no listener, no work");

        using var subscription = target.AddEffectiveInViewChangedHandler(_ => { });

        scene.Layout();
        int afterBaseline = EffectiveInViewState.ComputeCount;
        Assert.AreEqual(before + 1, afterBaseline);

        scene.Layout();

        Assert.AreEqual(afterBaseline + 1, EffectiveInViewState.ComputeCount, "one computation per pass per watched control");
    }

    [TestMethod]
    public void TheHandlerCanBeRemovedAndDisposedWithoutAFurtherPass()
    {
        var root = Root();
        var target = Item(new SKRect(20f, 20f, 60f, 60f));
        root.Controls.Add(target);

        using var scene = new InViewScene(root, 200f, 100f);

        int count = 0;

        Action<object> handler = _ => count++;

        target.AddEffectiveInViewChangedHandler(handler);
        scene.Layout();

        target.RemoveEffectiveInViewChangedHandler(handler);
        target.Location = new SKPoint(300f, 300f);
        scene.Layout();

        Assert.AreEqual(0, count);

        using var token = target.AddEffectiveInViewChangedHandler(_ => count++);
        scene.Layout();
        token.Dispose();
        scene.Layout();

        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public void TheCacheEntryDoesNotKeepTheControlAlive()
    {
        var root = Root();

        using var scene = new InViewScene(root, 200f, 100f);

        var weak = WatchAndAbandon(root);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.IsFalse(weak.IsAlive, "the weak table and the handler token must not root a detached control");
    }

    /// <summary>Registers a handler on a control, detaches it, and returns a weak reference to it.</summary>
    /// <remarks>
    /// Not inlined so the local cannot be kept alive by the caller's frame while the assertion runs,
    /// which is the usual way a collection test lies.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference WatchAndAbandon(Panel root)
    {
        var target = Item(new SKRect(20f, 20f, 60f, 60f));
        root.Controls.Add(target);

        target.AddEffectiveInViewChangedHandler(static _ => { });

        var weak = new WeakReference(target);

        root.Controls.Remove(target);

        return weak;
    }

    private static Panel ScrollPanel(out Panel row)
    {
        var panel = new Panel
        {
            VerticalScroll = ScrollStyle.Always,
            Padding = new Thickness(0f),
            BackColor = SKColors.White,
        };

        row = Item(new SKRect(10f, 0f, 70f, 40f));

        panel.Controls.Add(row);
        panel.Controls.Add(Item(new SKRect(0f, 100f, 60f, 300f)));

        return panel;
    }
}
