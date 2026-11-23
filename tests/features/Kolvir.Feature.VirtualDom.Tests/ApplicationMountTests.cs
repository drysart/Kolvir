using SkiaSharp;
using Kolvir;
using Kolvir.Backend.Headless;
using Kolvir.Feature.Observables;

namespace Kolvir.Feature.VirtualDom.Tests;

/// <summary>
/// The application level: an application component renders a window set, and the mount keeps the real
/// windows in step with it - by key, by position, by property, and by the close rules.
/// </summary>
[TestClass]
[TestCategory("VirtualDom")]
public sealed class ApplicationMountTests
{
    [TestMethod]
    public void MountingRendersOnceAndCreatesEveryWindow()
    {
        var component = new TestApplicationComponent(_ =>
        [
            Window("one", new VLabel { Text = "a" }),
            Window("two", new VLabel { Text = "b" }),
        ]);

        using var scene = ApplicationScene.Create(component);

        Assert.AreEqual(1, component.Renders, "mounting renders the component once");
        CollectionAssert.AreEqual(
            new[] { "one", "two" },
            scene.Windows.Select(window => window.Title).ToArray(),
            "every window in the first render exists");
        Assert.AreEqual("a", scene.Content(scene.Window("one")).Controls[0].Text);
        Assert.AreEqual("b", scene.Content(scene.Window("two")).Controls[0].Text);
    }

    [TestMethod]
    public void KeyedReorderKeepsTheSameWindowsAndTheirState()
    {
        var component = new TestApplicationComponent(_ =>
        [
            Window("a", new VButton { Text = "a" }, key: "a"),
            Window("b", new VButton { Text = "b" }, key: "b"),
        ]);

        using var scene = ApplicationScene.Create(component);

        var a = scene.Window("a");
        var b = scene.Window("b");
        var button = scene.Content(a).Controls[0];

        a.Location = new SKPoint(40f, 50f);
        a.State = WindowState.Minimized;
        a.Host.SetFocus(button);

        component.Renderer = _ =>
        [
            Window("b", new VButton { Text = "b" }, key: "b"),
            Window("a", new VButton { Text = "a" }, key: "a"),
        ];

        component.Invalidate();
        scene.Frame();

        Assert.AreSame(a, scene.Window("a"), "a keyed window that moves keeps its OS window");
        Assert.AreSame(b, scene.Window("b"), "and so does the other one");
        Assert.AreEqual(new SKPoint(40f, 50f), a.Location, "a reorder does not move it");
        Assert.AreEqual(WindowState.Minimized, a.State, "a reorder does not change its state");
        Assert.AreSame(button, a.Host.FocusedControl, "a reorder does not change focus");
    }

    [TestMethod]
    public void ANewKeyCreatesAndAMissingKeyCloses()
    {
        var component = new TestApplicationComponent(_ => [Window("a", key: "a")]);
        using var scene = ApplicationScene.Create(component);

        var a = scene.Window("a");

        component.Renderer = _ =>
        [
            Window("a", key: "a"),
            Window("b", key: "b"),
        ];

        component.Invalidate();
        scene.Frame();

        Assert.AreEqual(2, scene.Windows.Count, "the new key creates a window");
        var b = scene.Window("b");
        Assert.AreSame(a, scene.Window("a"), "the old window is untouched");

        component.Renderer = _ => [Window("b", key: "b")];

        component.Invalidate();
        scene.Frame();

        Assert.AreEqual(1, scene.Windows.Count, "the missing key closes its window");
        Assert.IsTrue(a.IsClosed);
        Assert.AreSame(b, scene.Window("b"));
    }

    [TestMethod]
    public void AWindowCreatedByAFrameHasItsAssignedGeometryWhenTheFrameReturns()
    {
        var component = new TestApplicationComponent(_ => [Window("list", key: "list")]);
        using var scene = ApplicationScene.Create(component);

        var list = scene.Window("list");
        bool sampled = false;
        SKSize? paintedSize = null;
        SKPoint? paintedLocation = null;

        using var token = list.Host.AddFramePaintedHandler(_ =>
        {
            if (sampled)
            {
                return;
            }

            sampled = true;

            if (scene.Windows.FirstOrDefault(window => window.Title == "doc") is { } created)
            {
                paintedSize = created.Backend.ClientSize;
                paintedLocation = created.Backend.Location;
            }
        });

        component.Renderer = _ =>
        [
            Window("list", key: "list"),
            new VWindow
            {
                Key = "doc",
                Title = "doc",
                ClientSize = new SKSize(240f, 140f),
                Location = new SKPoint(50f, 60f),
                Content = new VLabel { Text = "doc" },
            },
        ];

        component.Invalidate();
        scene.Frame();

        Assert.IsTrue(sampled, "the frame that created the window was painted");
        Assert.AreEqual(new SKSize(240f, 140f), paintedSize,
            "the window created inside the paint is sized before the paint returns");
        Assert.AreEqual(new SKPoint(50f, 60f), paintedLocation,
            "and it is placed before the paint returns");
        var doc = scene.Window("doc");
        Assert.AreEqual(new SKSize(240f, 140f), doc.ClientSize, "no deferred resize is still pending");
        Assert.AreEqual(new SKPoint(50f, 60f), doc.Location, "and the location is the one the render assigned");
    }

    [TestMethod]
    public void AnEmptyRenderMakesNoWindows()
    {
        var component = new TestApplicationComponent(_ => []);
        using var scene = ApplicationScene.Create(component);

        Assert.AreEqual(1, component.Renders, "the first render still happens at mount");
        Assert.AreEqual(0, scene.Windows.Count, "and an empty render means no windows");

        component.Invalidate();
        component.Invalidate();

        Assert.AreEqual(1, component.Renders, "with no window there is no frame source, so nothing renders again");
    }

    [TestMethod]
    public void DuplicateKeysAreRefused()
    {
        var component = new TestApplicationComponent(_ =>
        [
            Window("one", key: "same"),
            Window("two", key: "same"),
        ]);

        using var application = new Application(new Kolvir.Backend.Headless.HeadlessApplicationBackend());

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => VirtualDomRuntime.Mount(application, component));

        StringAssert.Contains(exception.Message, "same Key", "the message names the duplicated key");
    }

    [TestMethod]
    public void ANonWindowNodeIsRefused()
    {
        var component = new TestApplicationComponent(_ => [new VNotAWindow()]);

        using var application = new Application(new Kolvir.Backend.Headless.HeadlessApplicationBackend());

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => VirtualDomRuntime.Mount(application, component));

        StringAssert.Contains(exception.Message, nameof(VNotAWindow), "the message names the offending type");
        StringAssert.Contains(exception.Message, "not supported yet", "and says other application nodes are not supported yet");
    }

    [TestMethod]
    public void ANullRenderIsRefused()
    {
        var component = new TestApplicationComponent(_ => null!);

        using var application = new Application(new Kolvir.Backend.Headless.HeadlessApplicationBackend());

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => VirtualDomRuntime.Mount(application, component));

        StringAssert.Contains(exception.Message, "returned null", "the message says a list belongs there");
    }

    [TestMethod]
    public void AnAssignedPropertyIsApplied()
    {
        var component = new TestApplicationComponent(_ =>
            [Window("one", new VLabel { Text = "body" }, clientSize: new SKSize(300f, 200f))]);

        using var scene = ApplicationScene.Create(component);
        var window = scene.Window("one");

        Assert.AreEqual("one", window.Title);
        Assert.AreEqual(new SKSize(300f, 200f), window.ClientSize);

        component.Renderer = _ =>
            [Window("two", new VLabel { Text = "body" }, clientSize: new SKSize(300f, 200f))];

        component.Invalidate();
        scene.Frame();

        Assert.AreSame(window, scene.Window("two"), "a changed property patches the window");
    }

    [TestMethod]
    public void AnUnchangedAssignedPropertyIsNotReapplied()
    {
        var component = new TestApplicationComponent(_ =>
            [Window("one", clientSize: new SKSize(300f, 200f))]);

        using var scene = ApplicationScene.Create(component);
        var window = scene.Window("one");

        window.ClientSize = new SKSize(420f, 280f);

        component.Invalidate();
        scene.Frame();

        Assert.AreEqual(
            new SKSize(420f, 280f),
            window.ClientSize,
            "the render still asks for 300x200, but an unchanged assignment must not snap a user resize back");
    }

    [TestMethod]
    public void AChangedGeometryTokenReappliesAssignedGeometry()
    {
        object token = 0;
        var component = new TestApplicationComponent(_ =>
        [
            new VWindow
            {
                Key = "one",
                Title = "one",
                ClientSize = new SKSize(300f, 200f),
                Location = new SKPoint(10f, 20f),
                State = WindowState.Normal,
                MinimumClientSize = new SKSize(200f, 120f),
                MaximumClientSize = new SKSize(900f, 700f),
                GeometryToken = token,
            },
        ]);

        using var scene = ApplicationScene.Create(component);
        var window = scene.Window("one");

        window.ClientSize = new SKSize(420f, 280f);
        window.Location = new SKPoint(400f, 300f);
        window.State = WindowState.Minimized;
        window.MinimumClientSize = new SKSize(100f, 80f);
        window.MaximumClientSize = new SKSize(1000f, 800f);

        component.Invalidate();
        scene.Frame();

        Assert.AreEqual(new SKSize(420f, 280f), window.ClientSize,
            "an unchanged token leaves a user resize alone");
        Assert.AreEqual(new SKPoint(400f, 300f), window.Location,
            "and a user move alone");
        Assert.AreEqual(WindowState.Minimized, window.State);
        Assert.AreEqual(new SKSize(100f, 80f), window.MinimumClientSize);
        Assert.AreEqual(new SKSize(1000f, 800f), window.MaximumClientSize);

        token = 1;
        component.Invalidate();
        scene.Frame();

        Assert.AreEqual(new SKSize(300f, 200f), window.ClientSize,
            "a changed token re-applies the assigned size even though it did not change");
        Assert.AreEqual(new SKPoint(10f, 20f), window.Location,
            "and the assigned location");
        Assert.AreEqual(WindowState.Normal, window.State,
            "and the assigned state");
        Assert.AreEqual(new SKSize(200f, 120f), window.MinimumClientSize,
            "and the assigned minimum");
        Assert.AreEqual(new SKSize(900f, 700f), window.MaximumClientSize,
            "and the assigned maximum");
    }

    [TestMethod]
    public void AGeometryTokenDoesNotReapplyUnassignedGeometry()
    {
        object token = 0;
        var component = new TestApplicationComponent(_ =>
        [
            new VWindow
            {
                Key = "one",
                Title = "one",
                ClientSize = new SKSize(300f, 200f),
                Location = new SKPoint(10f, 20f),
                GeometryToken = token,
            },
        ]);

        using var scene = ApplicationScene.Create(component);
        var window = scene.Window("one");

        window.State = WindowState.Minimized;
        window.MinimumClientSize = new SKSize(111f, 222f);
        window.MaximumClientSize = new SKSize(333f, 444f);

        token = 1;
        component.Invalidate();
        scene.Frame();

        Assert.AreEqual(WindowState.Minimized, window.State,
            "a token change does not re-apply a state the node does not assign");
        Assert.AreEqual(new SKSize(111f, 222f), window.MinimumClientSize,
            "nor a minimum the node does not assign");
        Assert.AreEqual(new SKSize(333f, 444f), window.MaximumClientSize,
            "nor a maximum the node does not assign");
    }

    [TestMethod]
    public void WithdrawingGeometryRestoresPristineValueWithOrWithoutATokenChange()
    {
        var component = new TestApplicationComponent(_ =>
        [
            new VWindow
            {
                Key = "one",
                Title = "one",
                ClientSize = new SKSize(300f, 200f),
                Location = new SKPoint(10f, 20f),
                GeometryToken = 0,
            },
        ]);

        using var scene = ApplicationScene.Create(component);
        var window = scene.Window("one");

        window.ClientSize = new SKSize(420f, 280f);
        window.Location = new SKPoint(400f, 300f);

        component.Renderer = _ => [new VWindow { Key = "one", GeometryToken = 0 }];

        component.Invalidate();
        scene.Frame();

        Assert.AreEqual(default(SKSize), window.ClientSize,
            "withdrawing an assignment restores the pristine size when the token is unchanged");
        Assert.AreEqual(default(SKPoint), window.Location,
            "and the pristine location when the token is unchanged");

        component.Renderer = _ =>
        [
            new VWindow
            {
                Key = "one",
                ClientSize = new SKSize(300f, 200f),
                Location = new SKPoint(10f, 20f),
                GeometryToken = 0,
            },
        ];

        component.Invalidate();
        scene.Frame();

        window.ClientSize = new SKSize(420f, 280f);
        window.Location = new SKPoint(400f, 300f);

        component.Renderer = _ => [new VWindow { Key = "one", GeometryToken = 1 }];

        component.Invalidate();
        scene.Frame();

        Assert.AreEqual(default(SKSize), window.ClientSize,
            "a token change still restores a withdrawn size");
        Assert.AreEqual(default(SKPoint), window.Location,
            "and a withdrawn location");
    }

    [TestMethod]
    public void AWindowReportsItsOwnMovesResizesAndStateChanges()
    {
        VWindow? movedNode = null;
        VWindow? resizedNode = null;
        VWindow? stateNode = null;
        VWindow? activatedNode = null;
        VWindow? deactivatedNode = null;
        var movedTo = default(SKPoint);
        var resizedTo = default(SKSize);
        var stateTo = default(WindowState);

        var component = new TestApplicationComponent(_ =>
        [
            new VWindow
            {
                Key = "one",
                Title = "one",
                Location = new SKPoint(10f, 20f),
                ClientSize = new SKSize(300f, 200f),
                State = WindowState.Normal,
                Moved = (node, location) =>
                {
                    movedNode = node;
                    movedTo = location;
                },
                Resized = (node, size) =>
                {
                    resizedNode = node;
                    resizedTo = size;
                },
                StateChanged = (node, state) =>
                {
                    stateNode = node;
                    stateTo = state;
                },
                Activated = node => activatedNode = node,
                Deactivated = node => deactivatedNode = node,
            },
        ]);

        using var scene = ApplicationScene.Create(component);
        var window = scene.Window("one");
        var backend = (HeadlessWindowBackend)window.Backend;

        backend.Location = new SKPoint(200f, 300f);
        backend.ClientSize = new SKSize(420f, 280f);
        backend.State = WindowState.Minimized;
        backend.Activate();
        backend.SimulateDeactivate();

        Assert.AreEqual("one", movedNode?.Key, "Moved comes from the current node");
        Assert.AreEqual(new SKPoint(200f, 300f), movedTo, "Moved carries the new location");
        Assert.AreEqual("one", resizedNode?.Key);
        Assert.AreEqual(new SKSize(420f, 280f), resizedTo, "Resized carries the new client size");
        Assert.AreEqual("one", stateNode?.Key);
        Assert.AreEqual(WindowState.Minimized, stateTo, "StateChanged carries the new state");
        Assert.AreEqual("one", activatedNode?.Key, "Activated carries the window");
        Assert.AreEqual("one", deactivatedNode?.Key, "Deactivated carries the window");
    }

    [TestMethod]
    public void ChangesTheRenderAppliedAreNotAnnounced()
    {
        var size = new SKSize(300f, 200f);
        var location = new SKPoint(10f, 20f);
        var state = WindowState.Normal;
        object token = 0;
        int moved = 0;
        int resized = 0;
        int stateChanged = 0;

        var component = new TestApplicationComponent(_ =>
        [
            new VWindow
            {
                Key = "one",
                Title = "one",
                ClientSize = size,
                Location = location,
                State = state,
                GeometryToken = token,
                Moved = (_, _) => moved++,
                Resized = (_, _) => resized++,
                StateChanged = (_, _) => stateChanged++,
            },
        ]);

        using var scene = ApplicationScene.Create(component);
        var window = scene.Window("one");
        var backend = (HeadlessWindowBackend)window.Backend;

        Assert.AreEqual(0, moved + resized + stateChanged,
            "the properties applied while the window was created are the render's own work");

        backend.ClientSize = new SKSize(420f, 280f);
        backend.Location = new SKPoint(400f, 300f);
        backend.State = WindowState.Minimized;

        Assert.AreEqual(1, moved, "a user move is announced once");
        Assert.AreEqual(1, resized, "a user resize is announced once");
        Assert.AreEqual(1, stateChanged, "a user state change is announced once");

        moved = 0;
        resized = 0;
        stateChanged = 0;
        token = 1;
        component.Invalidate();
        scene.Frame();

        Assert.AreEqual(0, moved + resized + stateChanged,
            "a geometry token re-assert is the mount applying the render, not a user change");
        Assert.AreEqual(new SKSize(300f, 200f), window.ClientSize, "the token put the assigned size back");
        Assert.AreEqual(new SKPoint(10f, 20f), window.Location, "and the assigned location");
        Assert.AreEqual(WindowState.Normal, window.State, "and the assigned state");

        size = new SKSize(420f, 280f);
        location = new SKPoint(400f, 300f);
        state = WindowState.Minimized;
        component.Invalidate();
        scene.Frame();

        Assert.AreEqual(0, moved + resized + stateChanged,
            "a changed assignment the mount applies is not announced back to the node");
        Assert.AreEqual(new SKSize(420f, 280f), window.ClientSize);
        Assert.AreEqual(new SKPoint(400f, 300f), window.Location);
        Assert.AreEqual(WindowState.Minimized, window.State);

        component.Renderer = _ => [new VWindow { Key = "one", Title = "one", GeometryToken = token }];
        component.Invalidate();
        scene.Frame();

        Assert.AreEqual(0, moved + resized + stateChanged,
            "a withdrawn assignment restoring the pristine values is not announced either");
        Assert.AreEqual(default(SKSize), window.ClientSize);
        Assert.AreEqual(default(SKPoint), window.Location);
        Assert.AreEqual(WindowState.Normal, window.State);
    }

    [TestMethod]
    public void AnEventHandlerComesFromTheLatestNode()
    {
        int first = 0;
        int second = 0;

        var component = new TestApplicationComponent(_ =>
        [
            new VWindow
            {
                Key = "one",
                Title = "one",
                Moved = (_, _) => first++,
            },
        ]);

        using var scene = ApplicationScene.Create(component);
        var window = scene.Window("one");

        component.Renderer = _ =>
        [
            new VWindow
            {
                Key = "one",
                Title = "one",
                Moved = (_, _) => second++,
            },
        ];

        component.Invalidate();
        scene.Frame();

        window.Backend.Location = new SKPoint(50f, 60f);

        Assert.AreEqual(0, first, "the handler replaced by the latest render is no longer used");
        Assert.AreEqual(1, second, "the latest node's handler receives the move");
    }

    [TestMethod]
    public void EventsAreNotRaisedOnceTheWindowIsGone()
    {
        int moved = 0;

        var component = new TestApplicationComponent(_ =>
        [
            new VWindow
            {
                Key = "one",
                Title = "one",
                Moved = (_, _) => moved++,
            },
        ]);

        using var scene = ApplicationScene.Create(component);
        var window = scene.Window("one");
        var backend = (HeadlessWindowBackend)window.Backend;

        component.Renderer = _ => [];
        component.Invalidate();
        scene.Frame();

        Assert.IsTrue(window.IsClosed, "the render that omits the window closes it");

        backend.Location = new SKPoint(70f, 80f);

        Assert.AreEqual(0, moved, "a window that is gone raises nothing");
    }

    [TestMethod]
    public void AHandlerThatWritesTheValueBackSettlesWithoutMovingTheWindow()
    {
        var location = Observable.CreateValue(new SKPoint(10f, 20f));
        int moves = 0;

        var component = new TestApplicationComponent(_ =>
        [
            new VWindow
            {
                Key = "one",
                Title = "one",
                Location = location.Value,
                Moved = (_, movedTo) =>
                {
                    moves++;
                    location.Value = movedTo;
                },
            },
        ]);

        using var scene = ApplicationScene.Create(component);
        var window = scene.Window("one");
        var userLocation = new SKPoint(400f, 500f);

        window.Backend.Location = userLocation;
        scene.Frame();

        Assert.AreEqual(1, moves, "the user's move is announced once");
        Assert.AreEqual(userLocation, window.Location,
            "the render assigns what the model recorded, and an unchanged value does not move the window back");
        Assert.AreEqual(2, component.Renders, "the model change caused exactly one re-render");

        scene.Frame();

        Assert.AreEqual(userLocation, window.Location, "a later frame leaves the settled window alone");
        Assert.AreEqual(2, component.Renders, "and does not render again on its own");
    }

    [TestMethod]
    public void AnUnassignedPropertyRestoresThePristineValue()
    {
        var component = new TestApplicationComponent(_ =>
            [Window("set", clientSize: new SKSize(300f, 200f), resizable: false)]);

        using var scene = ApplicationScene.Create(component);
        var window = scene.Window("set");

        Assert.AreEqual("set", window.Title);
        Assert.IsFalse(window.Resizable);

        component.Renderer = _ => [new VWindow { ClientSize = new SKSize(300f, 200f) }];

        component.Invalidate();
        scene.Frame();

        Assert.AreEqual(string.Empty, window.Title, "a title the new node does not assign goes back to the window's pristine title");
        Assert.IsTrue(window.Resizable, "and a dropped Resizable goes back to the window's pristine value");
    }

    [TestMethod]
    public void APlainContentNodeIsPatchedInPlace()
    {
        var component = new TestApplicationComponent(_ =>
            [Window("one", new VLabel { Text = "before" })]);

        using var scene = ApplicationScene.Create(component);
        var window = scene.Window("one");
        var label = scene.Content(window).Controls[0];

        component.Renderer = _ => [Window("one", new VLabel { Text = "after" })];

        component.Invalidate();
        scene.Frame();

        Assert.AreSame(label, scene.Content(window).Controls[0], "the control is patched, not rebuilt");
        Assert.AreEqual("after", label.Text);
    }

    [TestMethod]
    public void ABareComponentContentRootIsMounted()
    {
        var page = new PageComponent("page");
        var component = new TestApplicationComponent(_ =>
            [Window("one", new VComponent(page))]);

        using var scene = ApplicationScene.Create(component);
        var window = scene.Window("one");

        var label = scene.Content(window).Controls[0];
        Assert.AreEqual("page", label.Text, "a VComponent root works without a wrapper panel");
        Assert.AreEqual(1, page.Renders);

        page.Change("changed");
        scene.Frame();

        Assert.AreSame(label, scene.Content(window).Controls[0], "the child's own mount patches its control");
        Assert.AreEqual("changed", label.Text);
        Assert.AreEqual(1, component.Renders, "and the application render is not involved");
    }

    [TestMethod]
    public void ContentCanChangeShapeBetweenRenders()
    {
        var page = new PageComponent("component");
        var component = new TestApplicationComponent(_ =>
            [Window("one", new VLabel { Text = "node" })]);

        using var scene = ApplicationScene.Create(component);
        var window = scene.Window("one");

        var nodeLabel = scene.Content(window).Controls[0];
        Assert.AreEqual("node", nodeLabel.Text);
        Assert.AreEqual(1, scene.Content(window).Controls.Count);

        component.Renderer = _ => [Window("one", new VComponent(page))];
        component.Invalidate();
        scene.Frame();

        Assert.AreEqual(1, scene.Content(window).Controls.Count, "the node mount is gone");
        Assert.IsNull(nodeLabel.Parent, "and its control is detached");
        Assert.AreEqual("component", scene.Content(window).Controls[0].Text);
        Assert.AreEqual(1, page.Renders);

        component.Renderer = _ => [Window("one", new VLabel { Text = "node again" })];
        component.Invalidate();
        scene.Frame();

        Assert.AreEqual(1, scene.Content(window).Controls.Count, "the component mount is gone");
        Assert.AreEqual("node again", scene.Content(window).Controls[0].Text);

        var renders = page.Renders;
        component.Renderer = _ => [Window("one", new VComponent(page))];
        component.Invalidate();
        scene.Frame();

        Assert.AreEqual("component", scene.Content(window).Controls[0].Text, "the same component instance can be mounted again");
        Assert.AreEqual(renders + 1, page.Renders, "with a fresh first render");
    }

    [TestMethod]
    public void ACloseRequestKeepsTheWindowOpenAndRaisesTheHandler()
    {
        var requests = 0;
        var open = true;

        var component = new TestApplicationComponent(_ =>
            open
                ? [Window("document", key: "document", closeRequested: _ => requests++)]
                : [Window("list", key: "list", content: new VLabel { Text = "list" })]);

        using var scene = ApplicationScene.Create(component);
        var window = scene.Window("document");

        window.Backend.Close();

        Assert.AreEqual(1, requests, "a native close raises CloseRequested");
        Assert.IsFalse(window.IsClosed, "the framework refuses the close it did not initiate");
        Assert.AreEqual(1, scene.Windows.Count, "so the window is still open");

        scene.Frame();
        Assert.IsFalse(window.IsClosed, "and a frame with no model change leaves it alone");

        open = false;
        component.Invalidate();
        scene.Frame();

        Assert.IsTrue(window.IsClosed, "the window disappears when a later render omits it");
        Assert.AreEqual(1, requests, "the framework's own close does not raise CloseRequested");
    }

    [TestMethod]
    public void ACloseRequestWithNoHandlerLeavesTheWindowOpen()
    {
        var component = new TestApplicationComponent(_ => [Window("document")]);

        using var scene = ApplicationScene.Create(component);
        var window = scene.Window("document");

        window.Backend.Close();

        Assert.IsFalse(window.IsClosed, "no handler means nothing happens");
        Assert.AreEqual(1, scene.Windows.Count);

        scene.Frame();
        Assert.IsFalse(window.IsClosed);
    }

    [TestMethod]
    public void TheHandlerIsReadFromTheLatestNode()
    {
        var first = 0;
        var second = 0;

        var component = new TestApplicationComponent(_ =>
            [Window("document", closeRequested: _ => first++)]);

        using var scene = ApplicationScene.Create(component);
        var window = scene.Window("document");

        component.Renderer = _ => [Window("document", closeRequested: _ => second++)];
        component.Invalidate();
        scene.Frame();

        window.Backend.Close();

        Assert.AreEqual(0, first, "the old delegate is not called");
        Assert.AreEqual(1, second, "the delegate from the latest render is");
    }

    [TestMethod]
    public void AValueTheRenderReadAsksForOneRenderPerFrame()
    {
        var title = Observable.CreateValue("one");
        var component = new TestApplicationComponent(_ =>
            [Window(title.Value, content: new VLabel { Text = title.Value })]);

        using var scene = ApplicationScene.Create(component);
        Assert.AreEqual(1, component.Renders);

        title.Value = "two";
        title.Value = "three";

        Assert.AreEqual(1, component.Renders, "observable changes only mark the component");
        Assert.AreEqual(1, scene.Windows.Count);

        scene.Frame();

        Assert.AreEqual(2, component.Renders, "one frame is one render however many values changed");
        Assert.AreEqual("three", scene.Window("three").Title, "the render read the values as they are now");
    }

    [TestMethod]
    public void InvalidateCoalescesToOneRenderPerFrame()
    {
        var component = new TestApplicationComponent(_ => [Window("one")]);
        using var scene = ApplicationScene.Create(component);

        component.Invalidate();
        component.Invalidate();
        component.Invalidate();

        Assert.AreEqual(1, component.Renders, "Invalidate only marks the component");

        scene.Frame();

        Assert.AreEqual(2, component.Renders, "three calls and one render");
    }

    [TestMethod]
    public void InvalidateFromInsideARenderIsRefused()
    {
        var component = new InvalidatingApplicationComponent();

        using var application = new Application(new Kolvir.Backend.Headless.HeadlessApplicationBackend());

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => VirtualDomRuntime.Mount(application, component));

        StringAssert.Contains(exception.Message, nameof(InvalidatingApplicationComponent));
        StringAssert.Contains(exception.Message, "inside Render");
    }

    [TestMethod]
    public void OneComponentCannotBeMountedTwice()
    {
        var component = new TestApplicationComponent(_ => [Window("one")]);

        using var application = new Application(new Kolvir.Backend.Headless.HeadlessApplicationBackend());
        using var mount = VirtualDomRuntime.Mount(application, component);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => VirtualDomRuntime.Mount(application, component));

        StringAssert.Contains(exception.Message, nameof(TestApplicationComponent));
        StringAssert.Contains(exception.Message, "already mounted");
    }

    [TestMethod]
    public void DisposeClosesEveryWindowAndAllowsARemount()
    {
        var component = new TestApplicationComponent(_ => [Window("one")]);
        using var scene = ApplicationScene.Create(component);

        var window = scene.Window("one");
        scene.Mount.Dispose();

        Assert.IsTrue(window.IsClosed);
        Assert.AreEqual(0, scene.Windows.Count);

        using var again = VirtualDomRuntime.Mount(scene.Application, component);

        Assert.AreEqual(2, component.Renders, "a detached component can be mounted again");
        Assert.AreEqual(1, scene.Windows.Count);
    }

    [TestMethod]
    public void ApplicationExitIsNotVetoed()
    {
        var component = new TestApplicationComponent(_ => [Window("one")]);
        using var scene = ApplicationScene.Create(component);

        Assert.AreEqual(1, scene.Windows.Count);

        scene.Application.Exit();

        Assert.AreEqual(0, scene.Windows.Count, "the application's own exit closes the window");
    }

    private static VWindow Window(
        string title,
        VNode? content = null,
        object? key = null,
        SKSize? clientSize = null,
        bool? resizable = null,
        Action<VWindow>? closeRequested = null) => new()
        {
            Key = key,
            Title = title,
            ClientSize = clientSize ?? new SKSize(300f, 200f),
            Content = content,
            Resizable = resizable ?? true,
            CloseRequested = closeRequested,
        };

    private sealed class TestApplicationComponent : ApplicationComponent
    {
        public TestApplicationComponent(Func<TestApplicationComponent, IReadOnlyList<ApplicationVNode>> renderer) =>
            Renderer = renderer;

        public Func<TestApplicationComponent, IReadOnlyList<ApplicationVNode>> Renderer { get; set; }

        public int Renders { get; private set; }

        protected override IReadOnlyList<ApplicationVNode> Render()
        {
            Renders++;
            return Renderer(this);
        }
    }

    private sealed class InvalidatingApplicationComponent : ApplicationComponent
    {
        protected override IReadOnlyList<ApplicationVNode> Render()
        {
            Invalidate();
            return [];
        }
    }

    private sealed class PageComponent : Component
    {
        private string _text;

        public PageComponent(string text) => _text = text;

        public int Renders { get; private set; }

        public override VNode Render()
        {
            Renders++;
            return new VLabel { Text = _text };
        }

        public void Change(string text)
        {
            _text = text;
            SetState();
        }
    }

    private sealed class VNotAWindow : ApplicationVNode;
}
