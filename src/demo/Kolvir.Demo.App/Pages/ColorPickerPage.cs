using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The colour picker page: the three columns and the colour they settle on.
/// </summary>
internal sealed class ColorPickerPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ColorPickerPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// The spectrum colour picker, one instance per colour space plus a narrow one. Each column
    /// carries a live readout of the colour it is producing, so dragging one square shows what the
    /// same value looks like in the other two modes, and the narrow instance shows the component
    /// fields stacking instead of sharing a row.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Colour picker"));

        card.Controls.Add(new Label(
            "The square runs saturation left to right and value (RGB, HSV) or lightness (HSL) top to " +
            "bottom; the strip on the right is hue, red at the top. HSB is the same space as HSV.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 42f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        });

        var row = new Panel
        {
            Dock = XDock.Top,
            Height = 318f,
            BackColor = null,
            Padding = new Thickness(0f),
            Margin = new Thickness(0f),
        };

        row.Controls.Add(BuildColorPickerColumn("RGB", ColorMode.Rgb, 250f));
        row.Controls.Add(BuildColorPickerColumn("HSV (HSB)", ColorMode.Hsv, 250f));
        row.Controls.Add(BuildColorPickerColumn("HSL", ColorMode.Hsl, 250f));
        row.Controls.Add(BuildColorPickerColumn("Narrow: fields stack", ColorMode.Hsl, 170f));

        card.Controls.Add(row);
        return card;
    }

    private Panel BuildColorPickerColumn(string title, ColorMode mode, float width)
    {
        var column = new Panel
        {
            Dock = XDock.Left,
            Width = width,
            BackColor = null,
            Padding = new Thickness(0f),
            Margin = new Thickness(0f, 0f, 10f, 0f),
        };

        var picker = new ColorPicker
        {
            Dock = XDock.Top,
            Height = 280f,
            ColorMode = mode,
            Color = new SKColor(0x33, 0x66, 0x99),
        };

        var readout = new Label(DescribeColorPicker(picker))
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 20f,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.Accent,
            TextAlign = ContentAlignment.MiddleLeft,
            Selectable = true,
        };

        picker.AddColorChangedHandler(_ => readout.Text = DescribeColorPicker(picker));

        column.Controls.Add(new Label(title)
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 18f,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.BottomLeft,
        });

        column.Controls.Add(picker);
        column.Controls.Add(readout);
        return column;
    }

    private static string DescribeColorPicker(ColorPicker picker) =>
            $"{picker.ColorMode}  #{picker.Color.Red:X2}{picker.Color.Green:X2}{picker.Color.Blue:X2}";

    /// <summary>
    /// The font dialog page: the platform's own chooser, the choice it returned, and a subtree that
    /// switches to that font so the inherited-appearance path is visible at the same time.
    /// </summary>
}
