using Kolvir;

namespace Kolvir.Feature.VirtualDom.Tests;

/// <summary>
/// Event handlers across a render: the same delegate must not be attached twice, and a changed one
/// must replace the old rather than add to it.
/// </summary>
[TestClass]
[TestCategory("VirtualDom")]
public sealed class EventTests
{
    [TestMethod]
    public void RerenderingWithTheSameHandlerSubscribesOnce()
    {
        using var session = new Session();
        int clicks = 0;
        Action<object> handler = _ => clicks++;

        session.Update(new VButton { Dock = DockStyle.Fill, Text = "go", Click = handler });
        session.Click();

        session.Update(new VButton { Dock = DockStyle.Fill, Text = "go", Click = handler });
        session.Click();

        Assert.AreEqual(2, clicks, "two clicks is two handlers' worth of work, not four");
    }

    [TestMethod]
    public void AChangedHandlerReplacesTheOldOne()
    {
        using var session = new Session();
        int first = 0;
        int second = 0;

        session.Update(new VButton
        {
            Dock = DockStyle.Fill,
            Text = "go",
            Click = _ => first++,
        });

        session.Update(new VButton
        {
            Dock = DockStyle.Fill,
            Text = "go",
            Click = _ => second++,
        });

        session.Click();

        Assert.AreEqual(0, first, "the replaced handler must not run");
        Assert.AreEqual(1, second, "and the new one must run once");
    }

    [TestMethod]
    public void DroppingAHandlerDetachesIt()
    {
        using var session = new Session();
        int clicks = 0;

        session.Update(new VButton { Dock = DockStyle.Fill, Text = "go", Click = _ => clicks++ });
        session.Update(new VButton { Dock = DockStyle.Fill, Text = "go" });
        session.Click();

        Assert.AreEqual(0, clicks, "a handler the node no longer sets is removed");
    }

    [TestMethod]
    public void AHandlerSurvivesARenderThatDidNotChangeIt()
    {
        using var session = new Session();
        int clicks = 0;
        Action<object> handler = _ => clicks++;

        session.Update(new VButton { Dock = DockStyle.Fill, Text = "go", Click = handler });
        session.Update(new VButton { Dock = DockStyle.Fill, Text = "pressed", Click = handler });
        session.Click();

        Assert.AreEqual(1, clicks);
        Assert.AreEqual("pressed", session.Button.Text, "and the rest of the patch still applied");
    }

    /// <summary>A mounted button filling a scene, so a click can be routed at it.</summary>
    private sealed class Session : IDisposable
    {
        private readonly Panel _host = new() { Dock = DockStyle.Fill };
        private readonly Scene _scene;
        private readonly MountHandle _mount;

        public Session()
        {
            _scene = Scene.Create(_host);
            _mount = VirtualDomRuntime.Mount(_host, new VPanel { Dock = DockStyle.Fill, Children = [new VButton { Dock = DockStyle.Fill }] });
        }

        public Button Button => (Button)((Panel)_mount.Root!).Controls[0];

        public void Update(VNode button)
        {
            _mount.Update(new VPanel { Dock = DockStyle.Fill, Children = [button] });
            _scene.Layout();
        }

        public void Click() => _scene.Click(Button);

        public void Dispose()
        {
            _mount.Dispose();
            _scene.Dispose();
        }
    }
}
