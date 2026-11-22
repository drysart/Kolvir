using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The font dialog page: the choice, the preview and the readout.
/// </summary>
internal sealed class FontDialogPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public FontDialogPage(DemoUi shell) : base(shell) => Initialize(Build());

    // The fonts are the page's for the life of the window, like the
    // appearance page's: a control never disposes a font it was handed, and a paint may still be
    // using the previous one when the next choice arrives.
    private Panel? _fontDialogStage;
    private Label? _fontDialogReadout;
    private FontSelection? _fontDialogSelection;
    private readonly List<Font> _fontDialogFonts = [];

    private static string DescribeSelection(FontSelection selection) =>
        $"{selection.FamilyName}, {selection.Size:0.#} logical px, weight {selection.Weight}, " +
        (selection.Italic ? "italic" : "upright");

    /// <summary>
    /// The font dialog page: the platform's own chooser, the choice it returned, and a subtree that
    /// switches to that font so the inherited-appearance path is visible at the same time.
    /// </summary>
    private Panel Build()
    {
        var initial = Theme.DefaultFont;
        _fontDialogSelection = SelectionOf(initial);

        // The page owns this font for the life of the window. Controls never dispose a font they
        // were handed, so replacing it means keeping it rather than letting the next paint race a
        // Dispose.
        var font = _fontDialogSelection.CreateFont();
        _fontDialogFonts.Add(font);

        var card = Card();
        card.Controls.Add(Heading("Font dialog"));
        card.Controls.Add(new Label(
            "PickFont shows the platform's own font chooser - ChooseFont on Windows, NSFontPanel on " +
            "macOS and GtkFontChooserDialog on GTK - and returns a value describing the choice, or " +
            "null when the dialog was cancelled. The value is not a font: the caller builds and owns " +
            "the Font.")
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

        _fontDialogReadout = new Label
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 44f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        };
        card.Controls.Add(_fontDialogReadout);

        var buttons = ButtonRow();

        var choose = new Button("Choose font...")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        choose.AddClickHandler(_ => ShowFontDialog());

        var reset = new Button("Reset")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
        };
        reset.AddClickHandler(_ => ResetFontDialog());

        buttons.Controls.Add(choose);
        buttons.Controls.Add(reset);
        card.Controls.Add(buttons);

        // The subtree: the panel assigns the chosen font and everything under it inherits it,
        // including the button, whose class default is the theme's emphasized font.
        var stage = new SurfacePanel
        {
            Dock = XDock.Top,
            Height = 236f,
            Padding = new Thickness(14f, 10f),
            Border = Framed(8f),
            Font = font,
            BackColor = ThemeColor.Secondary,
        };
        _fontDialogStage = stage;

        stage.Controls.Add(new Label(
            "The quick brown fox jumps over the lazy dog. This label inherits the panel's font, so " +
            "the whole subtree re-lays itself out when the choice changes.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 74f,
            WrapText = true,
            TextAlign = ContentAlignment.TopLeft,
        });

        var nested = new SurfacePanel
        {
            Dock = XDock.Top,
            Height = 72f,
            Padding = new Thickness(10f, 6f),
            Border = Framed(6f),
        };

        nested.Controls.Add(new Label(
            "This label sits in a nested panel that assigns nothing, so it takes the font from one " +
            "level up - the same font, at the same size.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            TextAlign = ContentAlignment.TopLeft,
        });

        stage.Controls.Add(nested);
        stage.Controls.Add(new Button("A button under the same panel")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = ButtonStyle.Ghost,
            Margin = new Thickness(0f, 8f, 0f, 0f),
        });
        card.Controls.Add(stage);

        card.Controls.Add(new Label(
            "The dialog lists the families the operating system has installed. A face loaded with " +
            "Font.FromFile is not a system font unless the OS knows it too, so it does not " +
            "appear here; that is documented rather than half-supported.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 10f, 0f, 0f),
        });

        UpdateFontDialogReadout();
        return card;
    }

    private void ShowFontDialog()
    {
        if (OwnerWindow is not { } owner || _fontDialogSelection is not { } current)
        {
            return;
        }

        var chosen = owner.Application.Dialogs.PickFont(
            new FontDialogOptions
            {
                Title = "Choose a font",
                InitialFamilyName = current.FamilyName,
                InitialSize = current.Size,
                InitialWeight = current.Weight,
                InitialItalic = current.Italic,
                MinimumSize = 6f,
                MaximumSize = 48f,
            },
            owner);

        if (chosen is null)
        {
            SetStatus("Font dialog cancelled; the preview is unchanged.");
            return;
        }

        ApplyFontSelection(chosen);
        SetStatus($"Chose {DescribeSelection(chosen)}.");
    }

    private void ResetFontDialog()
    {
        if (_fontDialogStage is { } stage)
        {
            stage.Font = InheritedValue.Instance;
            _fontDialogSelection = SelectionOf(stage.EffectiveFont);
        }

        UpdateFontDialogReadout();
        SetStatus("The preview inherits the theme's font again.");
    }

    private void ApplyFontSelection(FontSelection selection)
    {
        _fontDialogSelection = selection;

        // The page owns every font it makes. Keeping the previous one alive is deliberate: a paint
        // may still be holding it when the assignment lands, and a handful of fonts is nothing.
        var font = selection.CreateFont();
        _fontDialogFonts.Add(font);

        if (_fontDialogStage is not null)
        {
            _fontDialogStage.Font = font;
        }

        UpdateFontDialogReadout();
    }

    private void UpdateFontDialogReadout()
    {
        if (_fontDialogReadout is null || _fontDialogSelection is not { } selection)
        {
            return;
        }

        var effective = _fontDialogStage?.EffectiveFont;
        _fontDialogReadout.Text =
            $"Selection: {DescribeSelection(selection)}." +
            (effective is null
                ? string.Empty
                : $"\nThe subtree reports {effective.FamilyName}, {effective.Size:0.#} logical px, " +
                  $"weight {effective.Weight}, {(effective.Italic ? "italic" : "upright")}.");
    }

    private static FontSelection SelectionOf(Font font) =>
            new(string.IsNullOrWhiteSpace(font.FamilyName) ? "sans-serif" : font.FamilyName,
                font.Size,
                font.Weight,
                font.Italic);
}
