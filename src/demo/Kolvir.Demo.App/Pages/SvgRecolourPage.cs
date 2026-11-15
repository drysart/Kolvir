using Kolvir.Svg;
using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The SVG recolour page: one source drawn in the palette's own colours.
/// </summary>
internal sealed class SvgRecolourPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public SvgRecolourPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// <c>currentColor</c> following the palette, which is the one part of an SVG a theme can change.
    /// </summary>
    /// <remarks>
    /// The colours are resolved while each document is parsed, so a palette change reloads the
    /// documents rather than repainting them. Two colours of the same icon are two cache entries and
    /// a third switch back to the first parses nothing, which is why following the palette this way
    /// does not cost a parse per frame or per theme toggle.
    /// </remarks>
    private static Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("SVG recolour"));

        card.Controls.Add(new Label(
            "The same document resolved to four fixed colours, then to three theme roles. The bottom "
            + "row follows the palette - switch the appearance from the header and it recolours with "
            + "everything else, which is what a fixed fill above cannot do.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 40f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        });

        var fixedRow = ThumbnailRow();
        foreach (var (label, colour) in new (string, SKColor)[]
                 {
                         ("Foreground", new SKColor(0x22, 0x22, 0x22)),
                         ("Accent", new SKColor(0x2f, 0x6f, 0xd0)),
                         ("Warning", new SKColor(0xc0, 0x6a, 0x00)),
                         ("Error", new SKColor(0xc4, 0x2b, 0x2b)),
                 })
        {
            fixedRow.Controls.Add(RecolourCell(DemoIcons.CurrentColorBadge, 40f, colour, label));
        }

        card.Controls.Add(fixedRow);

        var themeRow = ThumbnailRow();
        var themed = new List<(Image Image, string Role)>();

        foreach (string role in new[] { "Foreground", "Accent", "Muted" })
        {
            var cell = new Panel
            {
                Dock = XDock.Left,
                Width = 120f,
                BackColor = null,
                Padding = new Thickness(4f),
            };

            cell.Controls.Add(new Label("Theme: " + role)
            {
                Dock = XDock.Bottom,
                Height = 18f,
                AutoSize = false,
                Font = Theme.CaptionFont,
                ForeColorRole = ThemeColor.MutedForeground,
                TextAlign = ContentAlignment.TopLeft,
            });

            var image = new Image
            {
                Dock = XDock.Fill,
                AutoSize = false,
                SizeMode = SizeMode.Zoom,
                ImageAlign = ContentAlignment.MiddleCenter,
            };

            Reload(image, DemoIcons.CurrentColorBadge, ThemeColour(role));
            themed.Add((image, role));
            cell.Controls.Add(image);
            themeRow.Controls.Add(cell);
        }

        card.Controls.Add(themeRow);

        card.Controls.Add(new Label("The badge is drawn with currentColor, so one document is every colour.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 22f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        // Reloading on a palette change is what makes the row follow the theme. It is one call per
        // image and no parse for a colour already seen, because the cache is keyed on the colour.
        // The subscription is never disposed: the handler lives as long as the process, which is
        // what a demo wants and an application would tie to its window instead.
        Theme.AddChangedHandler(_ =>
        {
            foreach (var (image, role) in themed)
            {
                Reload(image, DemoIcons.CurrentColorBadge, ThemeColour(role));
            }
        });

        return card;
    }

    /// <summary>One recoloured icon with the colour named under it.</summary>
    private static Panel RecolourCell(string source, float size, SKColor colour, string label)
    {
        var cell = new Panel
        {
            Dock = XDock.Left,
            Width = 120f,
            BackColor = null,
            Padding = new Thickness(4f),
        };

        cell.Controls.Add(new Label(label)
        {
            Dock = XDock.Bottom,
            Height = 18f,
            AutoSize = false,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        var image = new Image
        {
            Dock = XDock.Fill,
            AutoSize = false,
            SizeMode = SizeMode.Zoom,
            ImageAlign = ContentAlignment.MiddleCenter,
        };

        Reload(image, source, colour);
        cell.Controls.Add(image);
        return cell;
    }

    private static void Reload(Image image, string source, SKColor colour)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(source));

        // The control does not own what it shows, so replacing an image here releases the one it
        // was showing by hand rather than relying on the control to do it.
        image.Source?.Dispose();
        image.Source = Images.LoadFromStream(stream, new SvgOptions { CurrentColor = colour });
    }

    private static SKColor ThemeColour(string role) => role switch
    {
        "Accent" => Theme.Accent,
        "Muted" => Theme.MutedForeground,
        _ => Theme.Foreground,
    };
}
