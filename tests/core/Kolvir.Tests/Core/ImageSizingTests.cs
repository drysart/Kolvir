using SkiaSharp;

namespace Kolvir.Tests;

/// <summary>
/// How an <see cref="Image"/> relates its own size to its picture's, which is the seam that decides
/// both what a container arranges and what a container that measures it asks for.
/// </summary>
/// <remarks>
/// <para>
/// The picture is an <c>AutoSize</c> control's size and nothing else's: with auto-sizing off, the
/// control's bounds are its own, and the picture is fitted into them. A measurement that grew with
/// the picture would leak dimensions the control cannot act on, which is what made an AutoSize table
/// track inflate around an image that was visibly a fixed size.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Core")]
[TestCategory("Pixels")]
public sealed class ImageSizingTests : HeadlessTestBase
{
    [TestMethod]
    public void AnImageWithItsOwnSizeKeepsItWhenASourceIsAssigned()
    {
        // The reported case: a Width and Height are given, SizeMode is StretchImage, and then a
        // larger picture arrives. The source must be fitted into the box rather than replace it.
        using var data = ImageData.FromPicture(RecordPicture(320, 200));

        var panel = new Panel { Padding = Thickness.Empty };
        var image = new Image
        {
            Width = 40f,
            Height = 30f,
            SizeMode = SizeMode.StretchImage,
            Border = Border.None,
        };

        panel.Controls.Add(image);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);
        image.Source = data;
        scene.Paint();

        Assert.AreEqual(new SKSize(40f, 30f), image.Size, "the picture did not replace the size it was given");
        Assert.AreEqual(SKColors.Red, scene.PixelAt(1, 1), "StretchImage fills the box's top-left corner");
        Assert.AreEqual(SKColors.Red, scene.PixelAt(38, 28), "and its bottom-right");
    }

    [TestMethod]
    public void AnImageCreatedWithoutASizeDoesNotGrowWhenItsSourceIsAssigned()
    {
        // The new default: the control does not size itself to its picture, and a control with no
        // size is a 0x0 control rather than a missing default size.
        using var data = ImageData.FromPicture(RecordPicture(30, 20));

        var panel = new Panel { Padding = Thickness.Empty };
        var image = new Image();

        panel.Controls.Add(image);

        using var scene = HeadlessScene.Create(panel, 200f, 100f);
        Assert.AreEqual(SKSize.Empty, image.Size, "bounds start at zero, as they do on every control");

        image.Source = data;
        scene.Paint();

        Assert.AreEqual(SKSize.Empty, image.Size, "and assigning a source does not invent one");
    }

    [TestMethod]
    public void AnAutoSizeTrackHugsAnAutoSizedImageToItsPicture()
    {
        // The opt-in still means what it says: an AutoSize image hands its natural size to the track.
        using var data = ImageData.FromPicture(RecordPicture(120, 80));
        using var table = AutoSizeTable();

        var image = new Image
        {
            AutoSize = true,
            TableColumn = 0,
            TableRow = 0,
            Source = data,
        };

        using var neighbour = new Panel { Dock = DockStyle.Fill, TableColumn = 1, TableRow = 1 };

        table.Controls.Add(image);
        table.Controls.Add(neighbour);

        using var scene = HeadlessScene.Create(table, 200f, 100f);

        Assert.AreEqual(120f, neighbour.Bounds.Left, "the column hugs the picture's width");
        Assert.AreEqual(80f, neighbour.Bounds.Top, "and the row its height");
    }

    /// <summary>A table whose first column and row are as large as what is in them.</summary>
    private static TableLayoutPanel AutoSizeTable() => new()
    {
        Dock = DockStyle.Fill,
        Padding = Thickness.Empty,
        CellPadding = Thickness.Empty,
        ColumnStyles =
        {
            new ColumnStyle(SizeType.AutoSize, 0f),
            new ColumnStyle(SizeType.Percent, 100f),
        },
        RowStyles =
        {
            new RowStyle(SizeType.AutoSize, 0f),
            new RowStyle(SizeType.Percent, 100f),
        },
    };

    /// <summary>A solid red picture drawn directly, with no bitmap behind it.</summary>
    private static SKPicture RecordPicture(int width, int height)
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0f, 0f, width, height));
        using var paint = new SKPaint { Color = SKColors.Red };
        canvas.DrawRect(new SKRect(0f, 0f, width, height), paint);
        return recorder.EndRecording();
    }
}
