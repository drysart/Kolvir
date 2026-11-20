using Kolvir;
using Kolvir.Observables;

namespace Kolvir.VirtualDom.Tests;

/// <summary>
/// The observables seam: a component renders from a model, listens to exactly what that render read, and
/// re-renders once per frame when one of those values changes.
/// </summary>
[TestClass]
[TestCategory("VirtualDom")]
public sealed class ObservableComponentTests
{
    [TestMethod]
    public void AModelSetAfterMountRendersOnTheNextFrame()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var page = new TwoLabelPage();

        using var mount = VirtualDomRuntime.Mount(host, page);

        Assert.AreEqual(1, page.NullRenders, "mounting renders once, with no model");
        Assert.AreEqual(0, page.Renders, "and not through the data model it has not been given");

        page.DataModel = new PageModel();

        Assert.AreEqual(0, page.Renders, "setting the model only marks the component");

        scene.Frame();

        Assert.AreEqual(1, page.Renders, "the next frame renders it");
    }

    [TestMethod]
    public void AValueTheRenderReadAsksForOneRenderPerFrame()
    {
        // The whole point of the deferral: a handler that changes several values costs one render and one
        // tree update, not one of each per change.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var model = new PageModel();
        var page = new TwoLabelPage { DataModel = model };

        using var mount = VirtualDomRuntime.Mount(host, page);
        Assert.AreEqual(1, page.Renders);

        model.Text.Value = "changed";
        model.Words.Value = 42;

        Assert.AreEqual(1, page.Renders, "the change does not render inside itself");

        scene.Frame();

        Assert.AreEqual(2, page.Renders, "and one frame is one render however many values changed");
        Assert.AreEqual("changed", Labels(mount)[0].Text, "with the render reading the values as they are now");
        Assert.AreEqual("42 words", Labels(mount)[1].Text);
    }

    [TestMethod]
    public void ARenderStopsListeningToWhatItStoppedReading()
    {
        // Re-collection, on the same principle as an expression: the set follows the function, so a value
        // that is no longer read stops waking the component up.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var model = new PageModel();
        var page = new EitherPage { DataModel = model };

        using var mount = VirtualDomRuntime.Mount(host, page);

        model.ShowText.Value = false;
        scene.Frame();

        var renders = page.Renders;

        model.Text.Value = "not read any more";
        scene.Frame();

        Assert.AreEqual(renders, page.Renders, "the value the render stopped reading no longer asks for one");

        model.Words.Value = 7;
        scene.Frame();

        Assert.AreEqual(renders + 1, page.Renders, "and the one it started reading does");
    }

    [TestMethod]
    public void DetachingReleasesEveryValueTheRenderRead()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var model = new PageModel();
        var page = new TwoLabelPage { DataModel = model };
        var mount = VirtualDomRuntime.Mount(host, page);

        model.Text.Value = "before";
        scene.Frame();
        var renders = page.Renders;

        mount.Dispose();
        model.Text.Value = "after";

        scene.Frame();

        Assert.AreEqual(renders, page.Renders, "a disposed mount hears nothing");
    }

    [TestMethod]
    public void ARenderTokenDiesWithItsRenderAndAnAttachTokenOutlivesIt()
    {
        // The two lifetimes, made observable: what a render registers is released before the next render
        // starts, and what OnAttached registers lasts until the component leaves the mount.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var model = new PageModel();
        var page = new TokenPage { DataModel = model };

        var mount = VirtualDomRuntime.Mount(host, page);

        Assert.AreEqual(1, page.RenderProbes.Count, "the render registered one thing");
        Assert.IsFalse(page.RenderProbes[0].IsDisposed, "which is alive while that render is the current one");
        Assert.IsFalse(page.AttachProbe.IsDisposed, "and so is what OnAttached registered");

        model.Text.Value = "again";
        scene.Frame();

        Assert.AreEqual(2, page.RenderProbes.Count, "the second render registered its own");
        Assert.IsTrue(page.RenderProbes[0].IsDisposed, "the first render's thing died with the render");
        Assert.IsFalse(page.RenderProbes[1].IsDisposed);
        Assert.IsFalse(page.AttachProbe.IsDisposed, "and the attach-scoped one survived it");

        mount.Dispose();

        Assert.IsTrue(page.RenderProbes[1].IsDisposed, "the last render's thing died with the mount");
        Assert.IsTrue(page.AttachProbe.IsDisposed, "and so did the attach-scoped one");
    }

    [TestMethod]
    public void OnDetachedRunsOnceWhenTheComponentIsDisposedBeforeItsMount()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var page = new TwoLabelPage { DataModel = new PageModel() };
        var mount = VirtualDomRuntime.Mount(host, page);

        page.Dispose();
        page.Dispose();
        mount.Dispose();

        Assert.AreEqual(1, page.Detaches, "the component detaches itself, and the mount's detach finds nothing");
    }

    [TestMethod]
    public void ANullModelGoesToRenderWithNullModel()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var page = new TwoLabelPage();

        using var mount = VirtualDomRuntime.Mount(host, page);

        Assert.AreEqual(1, page.NullRenders, "the mount rendered with no model");
        Assert.AreEqual(0, page.Renders, "and did not call the model render");
    }

    [TestMethod]
    public void ANullModelWithNoOverrideRendersNothing()
    {
        // The empty fragment is the default, so a component with no model yet puts no controls in the
        // parent - and the model arriving is one ordinary render that fills the place the mount held.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var page = new ModelOnlyPage();

        using var mount = VirtualDomRuntime.Mount(host, page);

        Assert.AreEqual(0, host.Controls.Count, "an empty fragment adds nothing to the parent");
        Assert.IsNull(mount.Root, "there is no control to name");
        Assert.IsTrue(mount.IsMounted, "and the component is mounted all the same");

        page.DataModel = new PageModel();
        scene.Frame();

        Assert.AreEqual(1, host.Controls.Count, "the model's render takes the place the mount held");
        Assert.IsInstanceOfType<Panel>(mount.Root);
    }

    [TestMethod]
    public void SettingTheSameModelInstanceDoesNotRender()
    {
        // A change inside a model arrives through that model's own observables, so replacing the instance
        // is the only thing the setter can see - and replacing it with itself is not a replacement.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var model = new PageModel();
        var page = new TwoLabelPage { DataModel = model };
        using var mount = VirtualDomRuntime.Mount(host, page);

        page.DataModel = model;
        scene.Frame();

        Assert.AreEqual(1, page.Renders, "the same instance is not a change");

        page.DataModel = new PageModel();
        scene.Frame();

        Assert.AreEqual(2, page.Renders, "a different instance is");
    }

    [TestMethod]
    public void ARenderThatFailsLaterKeepsListening()
    {
        // Subscribing as the render reads is what leaves a failed render listening to what it did read, so
        // the value that broke it is also the thing that can ask for another try. A render that fails after
        // the mount reaches the frame's own boundary rather than the caller - that is the virtual DOM's
        // rule, not the observables layer's.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        Exception? boundary = null;
        using var reported = scene.Application.AddUnhandledExceptionHandler((_, args) => boundary = args.Exception);
        var model = new PageModel();
        var page = new TwoLabelPage { DataModel = model, BreakOn = "boom" };
        using var mount = VirtualDomRuntime.Mount(host, page);

        model.Text.Value = "boom";
        scene.Frame();

        Assert.IsInstanceOfType<InvalidOperationException>(boundary, "the failed render reached the frame boundary");

        model.Text.Value = "better";
        scene.Frame();

        Assert.AreEqual(3, page.Renders, "and the value that broke it asked for the render that fixed it");
        Assert.AreEqual("better", Labels(mount)[0].Text);
    }

    [TestMethod]
    public void AFirstRenderThatFailsLeavesTheComponentUnmountedAndUnwatched()
    {
        // A mount that never happened has nothing to patch, so the mount undoes the attach - which releases
        // the run that the failing render had already subscribed. The component is still usable.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var model = new PageModel();
        var page = new TwoLabelPage { DataModel = model, BreakOn = "boom" };
        model.Text.Value = "boom";

        Assert.ThrowsExactly<InvalidOperationException>(() => VirtualDomRuntime.Mount(host, page));

        model.Text.Value = "fine";
        scene.Frame();

        Assert.AreEqual(1, page.Renders, "nothing was watching the model, so nothing rendered");

        using var mount = VirtualDomRuntime.Mount(host, page);

        Assert.AreEqual(2, page.Renders, "and mounting it again works");
        Assert.AreEqual("fine", Labels(mount)[0].Text);
    }

    [TestMethod]
    public void ASecondLiveMountIsRefused()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        var other = new Panel();
        host.Controls.Add(other);
        using var scene = Scene.Create(host);
        var page = new TwoLabelPage { DataModel = new PageModel() };

        using var mount = VirtualDomRuntime.Mount(host, page);

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => VirtualDomRuntime.Mount(other, page));

        StringAssert.Contains(thrown.Message, nameof(TwoLabelPage), "the message names the component");
    }

    [TestMethod]
    public void AChangeInsideTheRenderIsRefusedLikeAnyOtherStateChange()
    {
        // The observables layer does not forbid a write during a run - it bounds one by depth - so the rule
        // that a render must not change the state it is rendering belongs where the mount keeps it.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var page = new SelfChangingPage { DataModel = new PageModel() };

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => VirtualDomRuntime.Mount(host, page));

        StringAssert.Contains(thrown.Message, "inside Render", "the mount's own message, from its own rule");
    }

    [TestMethod]
    public void ADisposedComponentRefusesEveryUse()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var page = new TwoLabelPage { DataModel = new PageModel() };

        page.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => page.Render());
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = page.DataModel);
        Assert.ThrowsExactly<ObjectDisposedException>(() => page.DataModel = new PageModel());
        Assert.ThrowsExactly<ObjectDisposedException>(() => VirtualDomRuntime.Mount(host, page));
    }

    [TestMethod]
    public void AChildComponentRerendersFromItsOwnReadWithoutTheParentRendering()
    {
        // The composition the Observable list page in the sample is built on: a parent renders one child
        // per index, and each child reads only its own index. So a write to one item re-renders that
        // child and nothing else - not the parent, which read the size, and not the other rows.
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);
        var list = Observable.CreateList("a", "b", "c");
        var rows = new RowsPage { DataModel = list };

        using var mount = VirtualDomRuntime.Mount(host, rows);
        scene.Frame();

        var parentRenders = rows.Renders;
        var firstRenders = rows.Rows[0].Renders;
        var secondRenders = rows.Rows[1].Renders;

        Assert.AreEqual(3, rows.Rows.Count, "one child per item");

        list[1] = "changed";
        scene.Frame();

        Assert.AreEqual(parentRenders, rows.Renders, "the parent read the size, which did not change");
        Assert.AreEqual(firstRenders, rows.Rows[0].Renders, "a row nobody touched is not re-rendered");
        Assert.AreEqual(secondRenders + 1, rows.Rows[1].Renders, "only the row whose item changed");

        var labels = Labels(mount);

        Assert.AreEqual("changed", labels[1].Text, "and its control carries the new item");
        Assert.AreEqual("a", labels[0].Text, "while the one beside it still carries what it had");
    }

    private static List<Label> Labels(ComponentMount mount) =>
        ((Panel)mount.Root!).Controls.OfType<Label>().ToList();

    /// <summary>
    /// A parent that renders one child per index and reads nothing but the size, the shape the
    /// Observable list page uses.
    /// </summary>
    private sealed class RowsPage : ObservableComponent<IReadOnlyObservableList<string>>
    {
        private readonly Dictionary<int, RowPage> _rows = [];

        internal int Renders { get; private set; }

        internal IReadOnlyDictionary<int, RowPage> Rows => _rows;

        protected override VNode RenderWithDataModel(RenderArgs<IReadOnlyObservableList<string>> args)
        {
            Renders++;

            var count = args.DataModel.Count;
            var children = new List<VNode>();

            for (var index = 0; index < count; index++)
            {
                if (!_rows.TryGetValue(index, out var row))
                {
                    row = new RowPage();
                    _rows.Add(index, row);
                }

                // The parent hands the model down: a child has no idea where it is mounted, and one with
                // no model renders the empty fragment its base class supplies.
                row.DataModel = args.DataModel;
                row.Index = index;

                children.Add(new VComponent(row) { Key = index });
            }

            return new VPanel { Dock = DockStyle.Fill, Children = children };
        }
    }

    /// <summary>A row that reads one index, which is the read the whole arrangement hangs on.</summary>
    private sealed class RowPage : ObservableComponent<IReadOnlyObservableList<string>>
    {
        internal int Renders { get; private set; }

        internal int Index { get; set; }

        protected override VNode RenderWithDataModel(RenderArgs<IReadOnlyObservableList<string>> args)
        {
            Renders++;

            return new VLabel { Text = args.DataModel[Index] };
        }
    }

    /// <summary>A model whose state is observable, which is the whole reason the seam exists.</summary>
    private sealed class PageModel
    {
        internal IObservableMutableValue<bool> ShowText { get; } = Observable.CreateValue(true);

        internal IObservableMutableValue<string> Text { get; } = Observable.CreateValue(string.Empty);

        internal IObservableMutableValue<int> Words { get; } = Observable.CreateValue(0);
    }

    /// <summary>Reads both values, always: the rendering every test that counts renders uses.</summary>
    private sealed class TwoLabelPage : ObservableComponent<PageModel>
    {
        internal int Renders { get; private set; }

        internal int NullRenders { get; private set; }

        internal int Detaches { get; private set; }

        /// <summary>A text value that makes the render throw, so a failing render is one flag away.</summary>
        internal string? BreakOn { get; init; }

        protected override VNode RenderWithDataModel(RenderArgs<PageModel> args)
        {
            Renders++;

            var model = args.DataModel;

            if (BreakOn is not null && model.Text.Value == BreakOn)
            {
                throw new InvalidOperationException($"the render of \"{model.Text.Value}\" failed");
            }

            return new VPanel
            {
                Dock = DockStyle.Fill,
                Children =
                [
                    new VLabel { Text = model.Text.Value },
                    new VLabel { Text = $"{model.Words.Value} words" },
                ],
            };
        }

        protected override VNode RenderWithNullModel(RenderArgs args)
        {
            NullRenders++;

            return new VPanel { Dock = DockStyle.Fill };
        }

        protected override void OnDetached() => Detaches++;
    }

    /// <summary>Reads one value or the other, so re-collection has something to move.</summary>
    private sealed class EitherPage : ObservableComponent<PageModel>
    {
        internal int Renders { get; private set; }

        protected override VNode RenderWithDataModel(RenderArgs<PageModel> args)
        {
            Renders++;

            var model = args.DataModel;

            return new VPanel
            {
                Dock = DockStyle.Fill,
                Children =
                [
                    new VLabel
                    {
                        Text = model.ShowText.Value ? model.Text.Value : $"{model.Words.Value} words",
                    },
                ],
            };
        }
    }

    /// <summary>Registers one thing per render and one per mount, so the two lifetimes can be told apart.</summary>
    private sealed class TokenPage : ObservableComponent<PageModel>
    {
        internal List<Probe> RenderProbes { get; } = [];

        internal Probe AttachProbe { get; } = new();

        protected override VNode RenderWithDataModel(RenderArgs<PageModel> args)
        {
            var probe = new Probe();
            RenderProbes.Add(probe);
            args.AddDisposable(probe);

            return new VPanel
            {
                Dock = DockStyle.Fill,
                Children = [new VLabel { Text = args.DataModel.Text.Value }],
            };
        }

        protected override void OnAttached(AttachedArgs args) => args.AddDisposable(AttachProbe);
    }

    /// <summary>Has no null-model render at all, so it shows nothing until it is given a model.</summary>
    private sealed class ModelOnlyPage : ObservableComponent<PageModel>
    {
        protected override VNode RenderWithDataModel(RenderArgs<PageModel> args) =>
            new VPanel { Dock = DockStyle.Fill };
    }

    /// <summary>Changes the model from inside the render its own data model produced.</summary>
    private sealed class SelfChangingPage : ObservableComponent<PageModel>
    {
        protected override VNode RenderWithDataModel(RenderArgs<PageModel> args)
        {
            DataModel = new PageModel();

            return new VPanel { Dock = DockStyle.Fill };
        }
    }

    /// <summary>Something to register, and to watch being released.</summary>
    private sealed class Probe : IDisposable
    {
        internal bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
