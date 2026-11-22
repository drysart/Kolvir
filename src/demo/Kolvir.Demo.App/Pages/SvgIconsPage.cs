using Kolvir.Svg;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The SVG icons page: one icon at every size the sample uses.
/// </summary>
internal sealed class SvgIconsPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public SvgIconsPage(DemoUi shell) : base(shell) => Initialize(Build());

    private static readonly float[] SvgIconSizes = [16f, 24f, 32f, 48f, 64f];

    /// <summary>
    /// A gallery: five documents, each drawn at five sizes.
    /// </summary>
    /// <remarks>
    /// Every one of them is loaded through the same factory call a PNG goes through, so what the
    /// page shows is the path an application gets for free once the image format provider is
    /// registered.
    /// </remarks>
    private static Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("SVG icons"));

        card.Controls.Add(new Label(
            "Five documents at 16, 24, 32, 48 and 64 pixels. Each is parsed once and the same "
            + "picture is re-recorded for each control, at whatever size the control asks for.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 34f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        });

        foreach (var (name, source) in new[]
                 {
                         ("Star", DemoIcons.Star),
                         ("Gear", DemoIcons.Gear),
                         ("Heart", DemoIcons.Heart),
                         ("Tick", DemoIcons.Tick),
                         ("Disc", DemoIcons.Disc),
                     })
        {
            var row = ThumbnailRow();
            row.Controls.Add(new Label(name)
            {
                Dock = XDock.Left,
                Width = 64f,
                AutoSize = false,
                Font = Theme.CaptionFont,
                ForeColor = ThemeColor.MutedForeground,
                TextAlign = ContentAlignment.MiddleLeft,
            });

            foreach (float size in SvgIconSizes)
            {
                row.Controls.Add(SvgCell(source, size));
            }

            card.Controls.Add(row);
        }

        return card;
    }

    /// <summary>One icon at one size, in a cell the gallery can line up.</summary>
    private static Panel SvgCell(string source, float size)
    {
        var cell = new Panel
        {
            Dock = XDock.Left,
            Width = size + 20f,
            BackColor = null,
            Padding = new Thickness(6f),
        };

        var image = new Image
        {
            Dock = XDock.Fill,
            AutoSize = false,
            SizeMode = SizeMode.Zoom,
            ImageAlign = ContentAlignment.MiddleCenter,
        };

        using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(source)))
        {
            image.Source = Images.LoadFromStream(stream);
        }

        cell.Controls.Add(image);
        return cell;
    }
}
