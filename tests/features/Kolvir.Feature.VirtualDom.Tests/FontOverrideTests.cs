using Kolvir;
using Kolvir.Feature.VirtualDom;

namespace Kolvir.Feature.VirtualDom.Tests;

/// <summary>
/// A generated node property for an inherited font override: the third union case reaches the
/// control, and the reconciler compares it by value.
/// </summary>
/// <remarks>
/// The generator is generic over the property's declared type and names no font type anywhere, so
/// widening <see cref="Control.Font"/> to three cases changes the node's property type without a
/// line of generator change. These tests are the evidence: the node accepts
/// <c>InheritedFont.WithSize(18f)</c>, the control reports the override, and a re-render that builds
/// an equal-but-new override patches nothing - which is <see cref="FontOverride"/>'s value equality
/// doing the work through the union.
/// </remarks>
[TestClass]
[TestCategory("VirtualDom")]
public sealed class FontOverrideTests
{
    [TestMethod]
    public void AnOverrideSetOnANodeReachesTheControl()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VLabel
        {
            Text = "x",
            Font = InheritedFont.WithSize(18f),
        });

        var label = mount.Root!;
        Assert.IsTrue(label.Font.TryGetValue<FontOverride>(out _), "the node should assign the override case");
        Assert.AreEqual(18f, label.EffectiveFont.Size, 0.01f, "and the control composes it");
        Assert.AreEqual(
            Theme.DefaultFont.FamilyName,
            label.EffectiveFont.FamilyName,
            "keeping every member it did not name");
    }

    [TestMethod]
    public void ARenderThatRebuildsAnEqualOverridePatchesNothing()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VLabel
        {
            Text = "x",
            Font = InheritedFont.WithSize(18f),
        });

        var label = mount.Root!;
        Assert.IsTrue(label.Font.TryGetValue<FontOverride>(out var first));

        // A new object, the same value: exactly what a render pass produces when it writes the
        // override inline.
        mount.Update(new VLabel { Text = "x", Font = InheritedFont.WithSize(18f) });

        Assert.AreSame(label, mount.Root, "the control is patched, not replaced");
        Assert.IsTrue(label.Font.TryGetValue<FontOverride>(out var second));
        Assert.AreSame(first, second, "an equal override never reaches the control, so nothing was invalidated");
    }

    [TestMethod]
    public void DroppingTheOverrideRestoresTheInheritedFont()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        using var scene = Scene.Create(host);

        using var mount = VirtualDomRuntime.Mount(host, new VLabel
        {
            Text = "x",
            Font = InheritedFont.WithSize(18f),
        });

        mount.Update(new VLabel { Text = "x" });

        var label = mount.Root!;
        Assert.IsTrue(label.Font.TryGetValue<InheritedValue>(out _), "the pristine font is inherit");
        Assert.AreSame(Theme.DefaultFont, label.EffectiveFont, "so the class default answers again");
    }
}
