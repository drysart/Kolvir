using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// Input into a subtree that is zoomed by an <see cref="Panel.ScaleFactor"/>, with the zoomed
/// panel away from the window's origin.
/// </summary>
/// <remarks>
/// The position of the zoomed panel matters. A scene whose zoomed panel is the root has every
/// ancestor at x = 0, and the mapping error this guards against - dividing the whole host coordinate
/// before subtracting the ancestors, rather than undoing the ancestors first - cancels out there. A
/// real page always has the panel somewhere else, so these tests put it inside a container at a
/// non-zero position and check that a control receives the point that is under the pointer.
/// </remarks>
[TestClass]
[TestCategory("Headless")]
public sealed class ZoomedInputTests : HeadlessTestBase
{
    private const string Sample = "The quick brown fox";

    [TestMethod]
    public void AClickInAZoomedTextBoxLandsOnTheSameCharacterAsTheUnzoomedOne()
    {
        var reference = CaretIndexAtClick(1f, 100f);
        Assert.IsTrue(reference > 0, $"the reference click landed inside the text (index {reference})");

        Assert.AreEqual(reference, CaretIndexAtClick(2f, 100f), "at 2.0 the box sees the point under the pointer");
        Assert.AreEqual(reference, CaretIndexAtClick(0.5f, 100f), "and at 0.5, where the error scales the other way");
    }

    [TestMethod]
    public void AClickPastTheEndOfAZoomedTextBoxReachesTheEndOfTheText()
    {
        Assert.AreEqual(
            Sample.Length,
            CaretIndexAtClick(2f, 200f),
            "a click past the text puts the caret after the last character, not at its start");
    }

    [TestMethod]
    public void ADragSelectInAZoomedTextBoxSelectsTheCharactersBetweenTheTwoPoints()
    {
        var start = CaretIndexAtClick(1f, 20f);
        var end = CaretIndexAtClick(1f, 120f);
        Assert.IsTrue(end > start, $"the reference drag covers some text ({start} to {end})");

        var box = TextBox(2f);
        using var scene = HeadlessScene.Create(BoxHost(box), 1000f, 400f);

        scene.Host.ProcessMouseMove(PointInBox(box, 20f));
        scene.Host.ProcessMouseDown(PointInBox(box, 20f), MouseButtons.Left, Modifiers.None);
        scene.Host.ProcessMouseMove(PointInBox(box, 120f));
        scene.Host.ProcessMouseUp(PointInBox(box, 120f), MouseButtons.Left, Modifiers.None);

        Assert.AreEqual(start, box.SelectionStart, "the selection starts under the press");
        Assert.AreEqual(end - start, box.SelectionLength, "and covers the characters the drag crossed");
        Assert.AreEqual(Sample[start..end], box.SelectedText, "which is exactly the text between them");
    }

    [TestMethod]
    public void TheHostAndControlConversionsRoundTripThroughTwoZooms()
    {
        // Two zoom levels with non-zero ancestors on both, and a product of exactly 1: a mapping
        // that divides and subtracts in the wrong order is then off by the outer panel's position
        // rather than by a factor.
        var outer = Group();
        outer.Bounds = new SKRect(37f, 23f, 337f, 223f);
        outer.ScaleFactor = 2f;

        var inner = Group();
        inner.Bounds = new SKRect(11f, 7f, 211f, 157f);
        inner.ScaleFactor = 0.5f;

        var box = new TextBox { Bounds = new SKRect(8f, 8f, 148f, 38f), Text = Sample };
        inner.Controls.Add(box);
        outer.Controls.Add(inner);

        var root = Group();
        root.Controls.Add(outer);

        using var scene = HeadlessScene.Create(root, 400f, 300f);

        Assert.AreEqual(1f, box.EffectiveScaleMultiplier, 0.0001f, "the two multipliers cancel");

        foreach (var local in new[] { SKPoint.Empty, new SKPoint(0f, 17f), new SKPoint(140f, 30f) })
        {
            Assert.AreEqual(
                local,
                box.HostToClient(box.ClientToHost(local)),
                "control to host and back is the identity, which is what the input path inverts");
        }

        // A real click goes through the host's own conversion, which has to agree with the control's.
        var probe = new SKPoint(60f, 17f);
        var expected = (int)box.GetSelectionOffset(probe);
        scene.Host.ProcessMouseDown(box.ClientToHost(probe), MouseButtons.Left, Modifiers.None);
        Assert.AreEqual(expected, box.SelectionStart, "the host delivered the point the control was asked about");
    }

    /// <summary>The caret index a click at <paramref name="localX"/> leaves in a box at <paramref name="scale"/>.</summary>
    private static int CaretIndexAtClick(float scale, float localX)
    {
        var box = TextBox(scale);
        using var scene = HeadlessScene.Create(BoxHost(box), 1000f, 400f);

        var point = PointInBox(box, localX);
        scene.Host.ProcessMouseMove(point);
        scene.Host.ProcessMouseDown(point, MouseButtons.Left, Modifiers.None);
        scene.Host.ProcessMouseUp(point, MouseButtons.Left, Modifiers.None);

        return box.SelectionStart;
    }

    /// <summary>
    /// A single-line box wider than its text, inside a zoomed panel. Every local x used by these
    /// tests is inside both the box's child space and its on-screen footprint at either multiplier.
    /// </summary>
    private static TextBox TextBox(float scale)
    {
        var box = new TextBox
        {
            Bounds = new SKRect(8f, 8f, 298f, 38f),
            Text = Sample,
        };

        var panel = Group();
        panel.ScaleFactor = scale;
        panel.Controls.Add(box);
        return box;
    }

    /// <summary>The root of a scene: the zoomed panel at (337, 23) inside a plain container.</summary>
    private static Control BoxHost(TextBox box)
    {
        var panel = box.Parent!;
        panel.Bounds = new SKRect(337f, 23f, 937f, 323f);

        var root = Group();
        root.Controls.Add(panel);
        return root;
    }

    /// <summary>A point <paramref name="localX"/> px into the box, in host coordinates.</summary>
    private static SKPoint PointInBox(TextBox box, float localX) =>
        box.ClientToHost(new SKPoint(localX, box.Height / 2f));

    private static Panel Group() => new()
    {
        BackColor = null,
        Padding = new Thickness(0f),
    };
}
