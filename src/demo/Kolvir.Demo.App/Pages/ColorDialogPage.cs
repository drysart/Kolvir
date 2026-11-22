using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The colour dialog page: the swatch, the readout and a cancelled dialog.
/// </summary>
internal sealed class ColorDialogPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ColorDialogPage(DemoUi shell) : base(shell) => Initialize(Build());

    // The swatch is the current colour and the readout is the same value in
    // text, so a cancelled dialog can be told apart from a chosen one at a glance.
    private SKColor _colorDialogColor = new(0x33, 0x66, 0x99);
    private Panel? _colorDialogSwatch;
    private Label? _colorDialogReadout;

    /// <summary>
    /// The colour dialog page: the swatch the colour dialog returns, a readout of the same value,
    /// and a button that opens the dialog modally on this window. The owner comes from
    /// <see cref="OwnerWindow"/>, which is the root control's window, so the button works no matter
    /// which page is showing.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Colour dialog"));

        card.Controls.Add(new Label(
            "ColorDialog is drawn by the library, not by the platform: it is an ordinary " +
            "Window holding ColorPicker, with OK and Cancel, shown through the same modal " +
            "path an application uses for its own dialogs. Enter accepts, Escape cancels, and Cancel " +
            "returns null.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 58f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 10f),
        });

        var swatch = new SurfacePanel
        {
            Dock = XDock.Top,
            Height = 110f,
            Border = Framed(8f),
            BackColor = _colorDialogColor,
            Margin = new Thickness(0f, 0f, 0f, 10f),
        };
        _colorDialogSwatch = swatch;
        card.Controls.Add(swatch);

        _colorDialogReadout = new Label
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 40f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.Accent,
            TextAlign = ContentAlignment.TopLeft,
            Selectable = true,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        };
        card.Controls.Add(_colorDialogReadout);

        var buttons = ButtonRow();

        var choose = new Button("Choose colour...")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Primary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        choose.AddClickHandler(_ => ShowColorDialog());

        var reset = new Button("Reset")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
        };
        reset.AddClickHandler(_ => ResetColorDialog());

        buttons.Controls.Add(choose);
        buttons.Controls.Add(reset);
        card.Controls.Add(buttons);

        card.Controls.Add(new Label(
            "The dialog is library-drawn on every platform, which is the difference from the font " +
            "dialog next door: the picker already exists as a portable control, so a native colour " +
            "chooser would replace tested code with three different answers. A font panel has no " +
            "portable substitute, so that one is still the platform's own.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 10f, 0f, 0f),
        });

        UpdateColorDialogReadout();
        return card;
    }

    private void ShowColorDialog()
    {
        if (OwnerWindow is not { } owner)
        {
            return;
        }

        var dialog = new ColorDialog(new ColorDialogOptions
        {
            Title = "Choose a colour",
            InitialColor = _colorDialogColor,
            ColorMode = ColorMode.Rgb,
        });

        var chosen = dialog.Show(owner);
        if (chosen is null)
        {
            SetStatus("Colour dialog cancelled; the swatch is unchanged.");
            return;
        }

        _colorDialogColor = chosen.Value;
        UpdateColorDialogReadout();
        SetStatus($"Chose {DescribeColor(_colorDialogColor)}.");
    }

    private void ResetColorDialog()
    {
        _colorDialogColor = new SKColor(0x33, 0x66, 0x99);
        UpdateColorDialogReadout();
        SetStatus("The swatch is back to #336699.");
    }

    private void UpdateColorDialogReadout()
    {
        if (_colorDialogSwatch is not null)
        {
            _colorDialogSwatch.BackColor = _colorDialogColor;
        }

        if (_colorDialogReadout is not null)
        {
            _colorDialogReadout.Text =
                $"Current colour: {DescribeColor(_colorDialogColor)}. Opening the dialog again " +
                "starts from this value.";
        }
    }
}
