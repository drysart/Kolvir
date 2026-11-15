using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The wrap panel page: the wrapping, the alignment and the panel that reflows.
/// </summary>
internal sealed class WrapPanelPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public WrapPanelPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// <see cref="WrapPanel"/>: a tag row that wraps at the page's width, a column flow of
    /// swatches, and a row whose items are of different heights so the stretch is visible.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Wrap panel"));

        card.Controls.Add(new Label(
            "Items flow along one axis and start a new line when the next one does not fit. Rows " +
            "that wrap downwards are the default; columns that wrap to the right are the other " +
            "orientation. A line is as broad as its broadest item, so Stretch - the default across " +
            "a line - makes the rest of the line match it.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 58f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 10f),
        });

        card.Controls.Add(new Label("Tags")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 22f,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        var tags = new WrapPanel
        {
            Dock = XDock.Top,
            Height = 96f,
            Spacing = 6f,
            Margin = new Thickness(0f, 0f, 0f, 14f),
            Border = Framed(8f),
        };

        foreach (string tag in new[]
        {
                "design", "layout", "skia", "cross-platform", "aot", "trimming",
                "popups", "menus", "text", "accessibility", "windows", "macos",
                "linux", "tests",
            })
        {
            var chip = new Button(tag)
            {
                AutoSize = true,
                Style = ButtonStyle.Secondary,
            };
            chip.AddClickHandler(_ => SetStatus($"Tag \"{tag}\" clicked - the flow wrapped around it."));
            tags.Controls.Add(chip);
        }

        card.Controls.Add(tags);

        card.Controls.Add(new Label("Heights across one line")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 22f,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        var heights = new WrapPanel
        {
            Dock = XDock.Top,
            Height = 56f,
            Spacing = 8f,
            Margin = new Thickness(0f, 0f, 0f, 14f),
            Border = Framed(8f),
        };

        var stretched = new SurfacePanel
        {
            AutoSize = false,
            Size = new SKSize(120f, 24f),
            Border = SimpleBorder.Thin.WithCornerRadius(4f),
            BackColor = new SKColor(0x33, 0x66, 0x99),
        };
        heights.Controls.Add(stretched);

        var centred = new SurfacePanel
        {
            AutoSize = false,
            Size = new SKSize(120f, 24f),
            Border = SimpleBorder.Thin.WithCornerRadius(4f),
            BackColor = new SKColor(0x2A, 0x9D, 0x8F),
            VerticalAlignment = Alignment.Center,
        };
        heights.Controls.Add(centred);

        // The tallest item sets the line, and the Stretched one beside it fills that height.
        heights.Controls.Add(new SurfacePanel
        {
            AutoSize = false,
            Size = new SKSize(120f, 44f),
            Border = SimpleBorder.Thin.WithCornerRadius(4f),
            BackColor = new SKColor(0xD9, 0x82, 0x2B),
        });

        card.Controls.Add(heights);

        card.Controls.Add(new Label("Columns, wrapping to the right")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 22f,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        var swatches = new WrapPanel
        {
            Dock = XDock.Top,
            Height = 120f,
            Orientation = StackOrientation.Vertical,
            Spacing = 4f,
            Margin = new Thickness(0f, 0f, 0f, 14f),
            Border = Framed(8f),
        };

        var palette = new[]
        {
                new SKColor(0x33, 0x66, 0x99), new SKColor(0x2A, 0x9D, 0x8F),
                new SKColor(0xD9, 0x82, 0x2B), new SKColor(0xB0, 0x3A, 0x3A),
                new SKColor(0x6A, 0x4C, 0x93), new SKColor(0x4C, 0x7A, 0x34),
                new SKColor(0x88, 0x88, 0x88),
            };

        for (int index = 0; index < palette.Length; index++)
        {
            var swatch = new SurfacePanel
            {
                AutoSize = false,
                Size = new SKSize(44f, 26f),
                Border = Framed(4f),
                BackColor = palette[index],
            };
            swatch.AddClickHandler(_ => SetStatus(
                $"Swatch #{index + 1} is {DescribeColor(palette[index])}; the column wrapped where it ran out of height."));
            swatches.Controls.Add(swatch);
        }

        card.Controls.Add(swatches);

        card.Controls.Add(new Label(
            "The swatches run down the 120-pixel column and start a second column when the height " +
            "runs out, which is the same layout with the axes swapped.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        return card;
    }

    /// <summary>
    /// <see cref="Expander"/>: the settings-page shape, one section open and one folded, with a
    /// disabled one to show that the cascade reaches the header as well as the content.
    /// </summary>
}
