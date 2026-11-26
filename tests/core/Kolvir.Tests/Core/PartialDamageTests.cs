using System.Security.Cryptography;
using SkiaSharp;
using Kolvir;

namespace Kolvir.Tests;

/// <summary>
/// A partial repaint leaves the window as a full repaint of the same state would, and touches
/// nothing outside the area it was asked to paint.
/// </summary>
/// <remarks>
/// <para>
/// This is the property the whole retained-surface design rests on: the host clears and repaints
/// only the region it was asked to, and everything outside it keeps the pixels of the frame
/// before. If any of it is off, the window shows a seam - a card whose rounded corner is cut flat,
/// a hairline ring that stops halfway, half a glyph - and no test of the dirty region itself would
/// catch it, because the region is right and the paint is what differs.
/// </para>
/// <para>
/// So the comparison is made the only way it cannot be argued with: paint the tree full, repeat the
/// paint into a second surface, invalidate a small area of the second one, paint it again, and
/// compare <b>every</b> pixel of the two client areas. The reference and the subject are the same
/// tree in the same state, painted by the same code, so a difference is the damage clip's doing.
/// </para>
/// <para>
/// There are two claims, and they are asserted separately because they fail for different reasons.
/// <b>Nothing outside the damaged area may change</b> - that one is exact, and it is the one that
/// matters: it is what "the rest of the frame is still there" means. <b>Inside</b>, the repaint must
/// reproduce the full paint, and for every case but one it does, exactly. The exception is an
/// antialiased <i>curve</i> that the damage edge cuts: Skia chops the arc to the clip and
/// approximates the remainder with a different subdivision, so the partially covered pixels of that
/// arc move by up to 50 of 255. See <c>dev-docs/PARTIAL-DAMAGE-AND-FILL-COST.md</c>; the case that
/// shows it is kept, with its bound, so the day the bound moves the test says so rather than hiding
/// it inside a tolerance applied to the other five cases.
/// </para>
/// <para>
/// The cases are the ones where a clip edge has something to cut: a rounded corner, a hairline ring,
/// a line of text, the flat interior of a card, the empty page around it, and a region of two
/// separate rectangles, which takes the clip path rather than the rectangle path in
/// <c>UiHost.ClipToPaintArea</c>. Each is repeated at three scales, including one that is not a
/// whole number, because a damage rectangle snapped to whole logical pixels still lands on a
/// fractional device pixel when the display scale is 1.25.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Pixels")]
public sealed class PartialDamageTests : HeadlessTestBase
{
    private const float WindowWidth = 400f;
    private const float WindowHeight = 300f;

    /// <summary>The scales every case is driven at.</summary>
    /// <remarks>
    /// 1.00 is whole-pixel everywhere. 2.00 is the scale the profiling documents measured at, and
    /// whole logical damage edges stay whole device pixels there. 1.25 is the one that cannot: a
    /// damage edge at logical 43 is device 53.75, so the clip itself lands mid-pixel and Skia has to
    /// decide what a half-covered column means.
    /// </remarks>
    private static readonly float[] Scales = [1.00f, 1.25f, 2.00f];

    /// <summary>The page behind the card.</summary>
    private static readonly SKColor PageColor = new(0xFF101820);

    /// <summary>The card's own fill, which no other element of the scene uses.</summary>
    private static readonly SKColor CardColor = new(0xFFE8E0D0);

    private static readonly SKRect CardBounds = new(40f, 30f, 360f, 270f);
    private const float CardRadius = 12f;

    public TestContext TestContext { get; set; } = null!;

    [DataTestMethod]
    [DataRow("a rounded corner cut in half")]
    [DataRow("the hairline ring cut along an edge")]
    [DataRow("a rectangle through the text")]
    [DataRow("the interior of the card only")]
    [DataRow("a rectangle outside every card")]
    [DataRow("two separate rectangles")]
    public void APartialRepaintLeavesTheWindowIdenticalToAFullRepaint(string what)
    {
        foreach (float scale in Scales)
        {
            AssertCase(what, scale);
        }
    }

    /// <summary>
    /// Two full repaints of one state hash the same, which is what makes the comparison below a
    /// statement about the damage clip rather than about the rasteriser.
    /// </summary>
    [TestMethod]
    public void TwoFullRepaintsOfOneStateAreIdentical()
    {
        var (root, _) = BuildScene();
        using var scene = HeadlessScene.Create(root, WindowWidth, WindowHeight);
        scene.Host.PartialUpdates = true;

        using var first = Paint(scene, 2.00f);
        using var second = Paint(scene, 2.00f);

        Assert.AreEqual(Hash(first), Hash(second), "two full repaints of one state should hash the same");
        TestContext.WriteLine($"full repaint at 2.00x: {Hash(first)}");
    }

    private void AssertCase(string what, float scale)
    {
        var (root, text) = BuildScene();
        using var scene = HeadlessScene.Create(root, WindowWidth, WindowHeight);
        scene.Host.PartialUpdates = true;

        // The paint origin of a control, which is what the damage rectangles below are derived
        // from: it includes the padding of every container between the control and the window, so
        // the case is about the geometry rather than about a guessed offset.
        var textOrigin = text.GetPositionInHost();
        Assert.AreEqual(
            new SKPoint(CardBounds.Left, CardBounds.Top),
            root.Controls[0].GetPositionInHost(),
            "the card should sit where the cases put it, with no container offset");

        var damage = DamageFor(what, textOrigin);

        using var reference = Paint(scene, scale);

        AssertDamageCrossesItsGeometry(what, reference, scale, damage);

        // The subject is primed with a full paint of the same state, so its pixels outside the
        // damage are already the ones the reference has and cannot be the difference.
        using var subject = Paint(scene, scale);

        foreach (var rect in damage)
        {
            scene.Host.Invalidate(rect);
        }

        PaintOnto(scene, subject, scale);

        Assert.AreEqual(
            BoundsOf(damage),
            scene.Host.LastFrameArea,
            $"{what} at {scale:0.00}x: the frame should cover exactly the damaged area, so this is a " +
            "partial paint and not a full one wearing a partial label");

        var (outside, worstOutside, inside, worstInside) = Compare(reference, subject, damage, scale);
        string full = Hash(reference);
        string partial = Hash(subject);

        Assert.AreEqual(
            0,
            outside,
            $"{what} at {scale:0.00}x: the repaint changed {outside} pixels outside the damaged area " +
            $"(worst channel delta {worstOutside}). full={full} partial={partial}");

        var (allowedPixels, allowedDelta, why) = ToleranceFor(what);

        Assert.IsTrue(
            inside <= allowedPixels && worstInside <= allowedDelta,
            $"{what} at {scale:0.00}x: the repaint differs from a full one in {inside} pixels inside " +
            $"the damaged area, worst channel delta {worstInside}; the agreed bound is {allowedPixels} " +
            $"pixels and {allowedDelta} levels ({why}). full={full} partial={partial}");

        TestContext.WriteLine(
            $"{what} at {scale:0.00}x: {inside} px inside differ, worst {worstInside} (bound " +
            $"{allowedPixels}/{allowedDelta}), {outside} outside; full={full} partial={partial}");
    }

    /// <summary>The damage rectangles one named case invalidates, in host coordinates.</summary>
    private static SKRect[] DamageFor(string what, SKPoint textOrigin) => what switch
    {
        // The top-left corner of the card is the arc of a circle of radius 12 centred at (52, 42).
        // The rectangle's right edge at x = 50 therefore cuts through the arc, which is the case
        // this test exists for: antialiased curvature meeting the damage edge.
        "a rounded corner cut in half" => [new SKRect(34f, 24f, 50f, 46f)],

        // A strip across the left edge, so the hairline ring is covered for part of its length and
        // left alone for the rest of it.
        "the hairline ring cut along an edge" => [new SKRect(36f, 100f, 46f, 140f)],

        // The first glyphs of the label, from inside its top-left towards the middle of the line:
        // the right-hand edge of the damage lands in the middle of the text, not before or after it.
        "a rectangle through the text" => [new SKRect(textOrigin.X + 6f, textOrigin.Y + 3f, textOrigin.X + 90f, textOrigin.Y + 25f)],

        // Flat card interior: no edge, no corner, no text, nothing else to clip.
        "the interior of the card only" => [new SKRect(150f, 80f, 250f, 120f)],

        // The page above the card, which no control of the tree paints at all.
        "a rectangle outside every card" => [new SKRect(100f, 2f, 200f, 20f)],

        // Two rectangles that do not touch, so the dirty region is a region rather than a
        // rectangle and the paint reaches it through the boundary path.
        "two separate rectangles" => [new SKRect(36f, 100f, 46f, 140f), new SKRect(150f, 200f, 260f, 240f)],

        _ => throw new ArgumentOutOfRangeException(nameof(what), what, "no such damage case"),
    };

    /// <summary>
    /// How far from a full repaint a case is allowed to land inside its damaged area, and why.
    /// </summary>
    /// <remarks>
    /// Everything except the corner is exact. The corner is bounded because the repair of a clipped
    /// arc is not the same arithmetic as the repair of a whole one: Skia chops the curve to the clip
    /// and approximates the remainder, so the arc's partial-coverage pixels land a few percent away
    /// from the coverage the unclipped arc gave them. This machine measures 14 pixels at 36/255 at
    /// 1.00x, 23 at 22/255 at 1.25x and 16 at 50/255 at 2.00x - about one fringe pixel per row of
    /// the arc, never a whole pixel changing sides. The bounds are set just above that, so the test
    /// moves if the behaviour does instead of hiding it.
    /// </remarks>
    private static (int Pixels, int Delta, string Why) ToleranceFor(string what) => what switch
    {
        "a rounded corner cut in half" => (32, 64, "one fringe pixel per row of the clipped arc"),
        _ => (0, 0, "exact"),
    };

    /// <summary>
    /// Checks that the reference bitmap holds the geometry the case names, so a case cannot pass by
    /// damaging empty space because a control moved.
    /// </summary>
    private static void AssertDamageCrossesItsGeometry(
        string what,
        SKBitmap reference,
        float scale,
        SKRect[] damage)
    {
        foreach (var rect in damage)
        {
            var device = DeviceRect(rect, scale);

            switch (what)
            {
                case "a rounded corner cut in half":
                case "the hairline ring cut along an edge":
                    Assert.IsTrue(
                        CountNotEqualTo(reference, device, PageColor, CardColor) > 0,
                        $"{what} at {scale:0.00}x: the damage should contain the antialiased edge it " +
                        "is supposed to cut, so it must hold pixels that are neither the page nor the card");
                    break;

                case "a rectangle through the text":
                    Assert.IsTrue(
                        CountNotEqualTo(reference, device, CardColor) > 20,
                        $"{what} at {scale:0.00}x: the damage should hold glyph pixels of the label");
                    break;

                case "the interior of the card only":
                    Assert.AreEqual(
                        0,
                        CountNotEqualTo(reference, device, CardColor),
                        $"{what} at {scale:0.00}x: the damage should be flat card interior and nothing else");
                    break;

                case "a rectangle outside every card":
                    Assert.AreEqual(
                        0,
                        CountNotEqualTo(reference, device, PageColor),
                        $"{what} at {scale:0.00}x: the damage should be the empty page and nothing else");
                    break;
            }
        }
    }

    /// <summary>A scene with one rounded, hairline-ringed card and one label of text on it.</summary>
    private static (Panel Root, Label Text) BuildScene()
    {
        var text = new Label("Partial damage cuts through this text")
        {
            Bounds = new SKRect(60f, 140f, 300f, 168f),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var card = new Panel
        {
            Bounds = CardBounds,
            Padding = Thickness.Empty,
            BackColor = CardColor,
            Border = SimpleBorder.Thin.WithCornerRadius(CardRadius).WithInsets(Thickness.Empty),
        };

        card.Controls.Add(text);

        var root = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = Thickness.Empty,
            BackColor = PageColor,
        };

        root.Controls.Add(card);
        return (root, text);
    }

    /// <summary>Paints full, and returns the pixels the caller owns.</summary>
    private static SKBitmap Paint(HeadlessScene scene, float scale)
    {
        scene.Host.Invalidate();
        var bitmap = new SKBitmap(Info(scale));
        PaintOnto(scene, bitmap, scale);
        return bitmap;
    }

    /// <summary>
    /// Paints into <paramref name="bitmap"/> the way a retained backend does: one canvas over the
    /// buffer that still holds the frame before, scaled exactly as the Win32 backend scales its own.
    /// </summary>
    private static void PaintOnto(HeadlessScene scene, SKBitmap bitmap, float scale)
    {
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale(scale);
        scene.WindowBackend.RenderTo(canvas);
        canvas.Flush();
    }

    private static SKImageInfo Info(float scale) => new(
        (int)MathF.Round(WindowWidth * scale),
        (int)MathF.Round(WindowHeight * scale),
        SKColorType.Bgra8888,
        SKAlphaType.Premul);

    /// <summary>The device pixels a logical rectangle covers, the way Skia's clip rounds it out.</summary>
    private static SKRectI DeviceRect(SKRect rect, float scale) => new(
        (int)MathF.Floor(rect.Left * scale),
        (int)MathF.Floor(rect.Top * scale),
        (int)MathF.Ceiling(rect.Right * scale),
        (int)MathF.Ceiling(rect.Bottom * scale));

    /// <summary>The bounds of the dirty region, which is what the host reports as the frame's area.</summary>
    private static SKRectI BoundsOf(IReadOnlyList<SKRect> damage)
    {
        var bounds = new SKRect(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);

        foreach (var rect in damage)
        {
            bounds = new SKRect(
                MathF.Min(bounds.Left, rect.Left),
                MathF.Min(bounds.Top, rect.Top),
                MathF.Max(bounds.Right, rect.Right),
                MathF.Max(bounds.Bottom, rect.Bottom));
        }

        return new SKRectI(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right),
            (int)MathF.Ceiling(bounds.Bottom));
    }

    /// <summary>
    /// Counts the differing pixels inside and outside the damaged area, and the largest channel
    /// difference on each side of that line. "Outside" is exact by design; "inside" is what the
    /// repaint is allowed to land a few levels away from.
    /// </summary>
    private static (int Outside, int WorstOutside, int Inside, int WorstInside) Compare(
        SKBitmap expected,
        SKBitmap actual,
        IReadOnlyList<SKRect> damage,
        float scale)
    {
        var regions = damage.Select(rect => DeviceRect(rect, scale)).ToArray();

        int outside = 0;
        int worstOutside = 0;
        int inside = 0;
        int worstInside = 0;

        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                var left = expected.GetPixel(x, y);
                var right = actual.GetPixel(x, y);

                if (left == right)
                {
                    continue;
                }

                int delta = Math.Max(
                    Math.Max(Math.Abs(left.Red - right.Red), Math.Abs(left.Green - right.Green)),
                    Math.Max(Math.Abs(left.Blue - right.Blue), Math.Abs(left.Alpha - right.Alpha)));

                if (regions.Any(region => region.Contains(x, y)))
                {
                    inside++;
                    worstInside = Math.Max(worstInside, delta);
                }
                else
                {
                    outside++;
                    worstOutside = Math.Max(worstOutside, delta);
                }
            }
        }

        return (outside, worstOutside, inside, worstInside);
    }

    /// <summary>Pixels of <paramref name="device"/> that are neither of the two named colours.</summary>
    private static int CountNotEqualTo(SKBitmap bitmap, SKRectI device, SKColor first, SKColor? second = null)
    {
        int count = 0;

        for (int y = Math.Max(0, device.Top); y < Math.Min(bitmap.Height, device.Bottom); y++)
        {
            for (int x = Math.Max(0, device.Left); x < Math.Min(bitmap.Width, device.Right); x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel != first && pixel != second)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>The SHA-256 of a bitmap's pixels, which is the comparison and the record of it.</summary>
    private static string Hash(SKBitmap bitmap) => Convert.ToHexString(SHA256.HashData(bitmap.GetPixelSpan()));
}
