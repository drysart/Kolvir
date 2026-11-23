using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Kolvir.Multiline;
using Kolvir.Feature.WoffFonts;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The fonts page: a face bundled in the assembly and one loaded from a file.
/// </summary>
internal sealed class FontsFromFilePage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public FontsFromFilePage(DemoUi shell) : base(shell) => Initialize(Build());

    private Label? _fontPageWebLabel;

    /// <summary>The line under it, which says what the loader reported once it has run.</summary>
    private Label? _fontPageWebReadout;

    /// <summary>Loads the page's web font, which needs the application's loader and therefore a window.</summary>
    /// <param name="window">The window the sample is displayed in.</param>
    internal override void OnWindowAttached(Window window) => LoadWebFontSample(window);


    /// <summary>
    /// A face that travels with the application instead of coming from the machine: every control on
    /// this page draws with IBM Plex Sans Regular read out of the assembly's resources, and the
    /// readout compares its measurements with the machine's own face.
    /// </summary>
    private Panel Build()
    {
        const string sample = "Kolvir";

        // The page owns these fonts for the life of the window: a control never disposes a font it was
        // handed, and the theme fonts are held the same way.
        var loaded = Font.FromBytes(DemoFont.Data);
        var system = new Font(loaded.Size);

        var card = Card();
        card.Controls.Add(Heading("Fonts from a file"));
        card.Controls.Add(ScrollCaption(
            $"{loaded.FamilyName}, {F(loaded.Size)} pt, read from this app's embedded resources - not a family this machine has"));

        card.Controls.Add(Caption("A label"));
        card.Controls.Add(new Label(
            "This label draws with the face in the file. Its glyph advances, line height and baseline " +
            "are the same on Windows, macOS and Linux, which a family name cannot promise: the same name " +
            "resolves to a different physical face on each desktop.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 62f,
            WrapText = true,
            Font = loaded,
            TextAlign = ContentAlignment.TopLeft,
        });

        card.Controls.Add(Caption("A multi-line text box"));
        card.Controls.Add(new TextBox
        {
            Dock = XDock.Top,
            Height = 108f,
            Multiline = true,
            Font = loaded,
            Text =
                "The caret, the wrap points and the selection geometry all come from the same face, so a " +
                "test that pins this file sees the same layout on every platform.\n\n" +
                "Nothing is looked up by name here: the bytes are the face. That is what makes a golden " +
                "image possible, and what lets a trimmed application carry its own brand.",
        });

        card.Controls.Add(Caption("A rich label: spans change size and colour and stay on this face"));
        var rich = new RichLabel
        {
            Dock = XDock.Top,
            AutoSize = true,
            WrapText = true,
            Font = loaded,
        };

        rich.AddText("The same face in ");
        rich.AddSpan(new TextSpan("a larger size") { FontSize = 17f });
        rich.AddText(", in ");
        rich.AddSpan(new TextSpan("the accent colour") { ForeColor = ThemeColor.Accent });
        rich.AddText(", and in ");
        rich.AddSpan(new TextSpan("a bold span") { Bold = true });
        rich.AddText(
            " - which stays this face rather than falling back to a system font, because one file holds " +
            "one face and no weight is invented.");
        card.Controls.Add(rich);

        card.Controls.Add(Caption("The same face as a web font, loaded through the application's font loader"));
        _fontPageWebLabel = new Label(
            "This label's face came from a .woff2 file - 63 KB against the 200 KB above - through " +
            "IFontLoader, which is the same call an application makes for a .ttf: no format branch at " +
            "the call site, and no knowledge of WOFF in the page that uses it.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 62f,
            WrapText = true,
            Font = Theme.DefaultFont,
            TextAlign = ContentAlignment.TopLeft,
        };

        card.Controls.Add(_fontPageWebLabel);

        _fontPageWebReadout = new Label("Waiting for the window, which is when services exist.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 20f,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
        };

        card.Controls.Add(_fontPageWebReadout);


        card.Controls.Add(new Label(
            $"Measured here: \"{sample}\" is {F(loaded.MeasureWidth(sample))} px in the loaded face and " +
            $"{F(system.MeasureWidth(sample))} px in the system face ({system.FamilyName}); line height " +
            $"{F(loaded.LineHeight)} against {F(system.LineHeight)}.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 10f, 0f, 0f),
        });

        return card;

        static string F(float value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Loads the fonts page's WOFF 2 sample through the application's font loader.
    /// </summary>
    /// <remarks>
    /// This is the part of the feature that matters, and it is deliberately the least code in the
    /// sample: the page asks a loader for a font and hands it bytes. It does not know they are a web
    /// font, and nothing about it would change if they were a plain file. What makes WOFF work is the
    /// single <c>AddWoffFonts()</c> in the sample's own options - a format the page never names.
    /// </remarks>
    private void LoadWebFontSample(Window window)
    {
        if (_fontPageWebLabel is not { } label || window.Services.GetService<IFontLoader>() is not { } fonts)
        {
            return;
        }

        try
        {
            var web = fonts.LoadFromBytes(DemoFont.WoffData, 15f);
            label.Font = web;

            if (_fontPageWebReadout is { } readout)
            {
                var formats = fonts is FontLoader loader ? loader.Providers.Count : 0;
                readout.Text =
                    $"Loaded through IFontLoader: \"{web.FamilyName}\" at " +
                    $"{web.Size.ToString("0.##", CultureInfo.InvariantCulture)} pt, " +
                    $"{formats} format provider(s) registered by this application. The label above asked " +
                    "for a font and got one; AddWoffFonts() is what made the bytes readable.";
            }
        }
        catch (FontFormatException exception)
        {
            // The sample opts in, so a failure here is a broken build rather than a supported state.
            // Saying so beats a page that quietly keeps the theme's face and looks fine.
            label.Text = $"The web font could not be loaded: {exception.Message}";
        }
    }

    /// <summary>
    /// The fonts page's web-font label. It is built wearing the theme's face and re-dressed once the
    /// window exists, because a page is built before there are services and the loader is a service.
    /// </summary>
}
