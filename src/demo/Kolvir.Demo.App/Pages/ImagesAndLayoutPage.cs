using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The images and layout page: the bitmap path, and the layout that sizes it.
/// </summary>
internal sealed class ImagesAndLayoutPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ImagesAndLayoutPage(DemoUi shell) : base(shell) => Initialize(Build());

    private static Panel Build()
    {
        var card = Card();

        // Opacity fades a control and everything inside it as a single group, so the gradient and
        // the panel it sits in are composited once over the card instead of each blending on its
        // own. A faded control is not opaque, which is what tells the card that repainting it has
        // to bring back what shows through.
        var faded = new Panel
        {
            Dock = XDock.Fill,
            BackColor = null,
            Padding = new Thickness(0f),
            Opacity = 0.6f,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        };

        var image = new Image
        {
            Dock = XDock.Fill,
            SizeMode = SizeMode.Zoom,
            ImageAlign = ContentAlignment.MiddleCenter,
            Border = SimpleBorder.Rounded(8f),
        };
        image.Source = Images.LoadFromStream(CreateGradientPng(320, 200));
        faded.Controls.Add(image);
        card.Controls.Add(faded);

        card.Controls.Add(new Label("A gradient drawn with SkiaSharp, shown with Image (Zoom) at Opacity 0.6.")
        {
            Dock = XDock.Bottom,
            AutoSize = false,
            Height = 34f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        });

        return card;
    }
}
