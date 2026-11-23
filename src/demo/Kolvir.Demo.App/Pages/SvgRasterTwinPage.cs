using Kolvir.Feature.Svg;
using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The raster twin page: the same picture as an SVG and as a bitmap.
/// </summary>
internal sealed class SvgRasterTwinPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public SvgRasterTwinPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// The same artwork as vector and as a thumbnail blown up, which is the argument for the feature
    /// rather than a feature of it.
    /// </summary>
    private static Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("SVG and a raster twin"));

        card.Controls.Add(new Label(
            "The same document drawn at 96 pixels, and the same document rendered once to a 16 pixel "
            + "PNG and then drawn at 96 pixels. The vector stays sharp because it is painted at the "
            + "size it is shown; the raster cannot.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 46f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        });

        var row = ThumbnailRow(height: 128f);
        row.Controls.Add(RasterTwinCell("SVG, drawn at 96", vector: true));
        row.Controls.Add(RasterTwinCell("A 16 pixel PNG at 96", vector: false));
        card.Controls.Add(row);

        return card;
    }

    private static Panel RasterTwinCell(string caption, bool vector)
    {
        var cell = new Panel
        {
            Dock = XDock.Left,
            Width = 200f,
            Padding = new Thickness(4f),
        };

        cell.Controls.Add(new Label(caption)
        {
            Dock = XDock.Bottom,
            Height = 18f,
            AutoSize = false,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        // A fixed 96x96 box, so both cells are drawn at exactly the same size and the only
        // difference between them is the source the 96 pixels came from.
        var image = new Image
        {
            Dock = XDock.Left,
            AutoSize = false,
            Width = 96f,
            Height = 96f,
            SizeMode = SizeMode.StretchImage,
            ImageAlign = ContentAlignment.MiddleCenter,
        };

        if (vector)
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(DemoIcons.Disc));
            image.Source = Images.LoadFromStream(stream);
        }
        else
        {
            image.Source = Images.LoadFromBytes(RasteriseSvg(DemoIcons.Disc, 16));
        }

        cell.Controls.Add(image);
        return cell;
    }

    /// <summary>Renders a document once to a PNG of the requested size, the way a build step would.</summary>
    private static byte[] RasteriseSvg(string source, int size)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(source));
        using var data = Images.LoadFromStream(stream);
        var picture = data.CurrentFrame!;

        using var surface = SKSurface.Create(new SKImageInfo(size, size));
        surface.Canvas.Clear(SKColors.Transparent);

        var bounds = picture.CullRect;
        surface.Canvas.Scale(size / bounds.Width, size / bounds.Height);
        surface.Canvas.DrawPicture(picture);

        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
