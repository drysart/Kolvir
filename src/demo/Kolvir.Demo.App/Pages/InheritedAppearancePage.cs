using System.Globalization;
using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The appearance page: what a font and a colour do to a subtree.
/// </summary>
internal sealed class InheritedAppearancePage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public InheritedAppearancePage(DemoUi shell) : base(shell) => Initialize(Build());

    // The fonts are the page's for the life of the window: controls
    // never dispose a font they were handed, and the theme keeps the face it is given.
    private Panel? _appearanceStage;
    private Label? _appearanceInherited;
    private Panel? _appearanceNested;
    private Label? _appearanceLabel;
    private Label? _appearanceOverrides;
    private Label? _appearanceReadout;
    private Control? _appearanceChainSize;
    private Control? _appearanceChainWeight;
    private Control? _appearanceChainAgain;
    private Font? _appearanceSubtreeFont;
    private Font? _appearanceBigFont;

    /// <summary>
    /// The inherited appearance page: one panel that assigns a font and a foreground, what its
    /// descendants make of it, one control that overrides both inside the subtree, a chain of three
    /// partial font overrides that compose down the tree, and a label that paints nothing on top of
    /// an ancestor that does. The buttons change the appearance of a live tree - the panel's font,
    /// or the whole theme's face - and the text re-lays itself out.
    /// </summary>
    private Panel Build()
    {
        // The page names the bundled face's family, which the application registered; nothing here
        // owns a font, so nothing here disposes one.
        _appearanceSubtreeFont ??= new Font(DemoFont.Register(), 14f);
        _appearanceBigFont ??= new Font(DemoFont.Register(), 21f);

        var card = Card();
        card.Controls.Add(Heading("Inherited appearance"));
        card.Controls.Add(new Label(
            "Font, foreground and background are inherited down the tree, the way they are in WinForms. " +
            "A control that assigns one answers for itself and for everything inside it; one that " +
            "assigns none asks its parent and then falls back to its class default. Nothing is captured " +
            "when a control is built, so changing the theme - or one panel's font - moves a live tree. " +
            "A font can be assigned in part as well: InheritedFont.WithSize names one member and keeps " +
            "the rest, which is how the panel's last three labels go 20, then 20 bold, then 12 bold.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 56f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 10f),
        });

        var buttons = ButtonRow();
        buttons.Height = 32f;

        var grow = new Button("Grow the panel's font")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        grow.AddClickHandler(_ => SetAppearanceDemo(size: 21f));

        var themeFace = new Button("Use the bundled face everywhere")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        themeFace.AddClickHandler(_ =>
        {
            Theme.FontFamily = DemoFont.Register();
            UpdateAppearanceReadout();
        });

        var reset = new Button("Reset")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
        };
        reset.AddClickHandler(_ =>
        {
            _appearanceStage?.Font = PropertyValue.Inherited;
            Theme.FontFamily = null;
            UpdateAppearanceReadout();
        });

        buttons.Controls.Add(grow);
        buttons.Controls.Add(themeFace);
        buttons.Controls.Add(reset);
        card.Controls.Add(buttons);

        _appearanceReadout = new Label
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 62f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        };
        card.Controls.Add(_appearanceReadout);

        // The subtree: a panel that assigns a font, a foreground colour and a background role. Every
        // control added below inherits all three unless it says otherwise.
        var stage = new SurfacePanel
        {
            Dock = XDock.Top,
            Height = 268f,
            Padding = new Thickness(12f, 8f),
            Border = Framed(8f),
            Font = _appearanceSubtreeFont,
            ForeColor = Theme.Accent,
            BackColor = ThemeColor.Secondary,
        };
        _appearanceStage = stage;

        var inherited = new Label("Inherits the panel's font, colour and background role.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 34f,
        };
        _appearanceInherited = inherited;

        // A panel that paints a background of its own and assigns nothing: it takes the colour its
        // parent assigned instead of its own surface, which is the inheritance doing its work.
        var nested = new SurfacePanel
        {
            Dock = XDock.Top,
            Height = 76f,
            Padding = new Thickness(8f, 6f),
            Border = Framed(6f),
        };
        _appearanceNested = nested;

        var noBackground = new Label(
            "This label paints nothing at all, so the colour behind it - the panel's, inherited one " +
            "level up - is what shows. Its transparent class default stops the walk, so it resolves " +
            "to nothing rather than to a colour of its own.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            TextAlign = ContentAlignment.TopLeft,
        };
        _appearanceLabel = noBackground;
        nested.Controls.Add(noBackground);

        // The one control that overrides the subtree it sits in.
        var overrides = new Label("This one assigns its own font and colour.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 24f,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
        };
        _appearanceOverrides = overrides;

        var button = new Button("A button under the same panel")
        {
            Dock = XDock.Top,
            AutoSize = true,
            Style = ButtonStyle.Ghost,
            Margin = new Thickness(0f, 6f, 0f, 0f),
        };

        // Three levels of override, each one nested inside the level above so it composes onto what
        // that level resolved rather than onto the panel's 14 pt: 20 pt, then bold at 20 pt, then
        // back to 12 pt and still bold. Nothing here names a family, so the panel's family and
        // colour travel all the way down.
        var first = new Panel { Dock = XDock.Top, Height = 78f, Padding = new Thickness(0f) };
        first.Font = InheritedFont.WithSize(20f);
        first.Controls.Add(new Label("InheritedFont.WithSize(20f) - 20 pt, the panel's family and colour")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 28f,
        });
        _appearanceChainSize = first;

        var second = new Panel { Dock = XDock.Top, Height = 50f, Padding = new Thickness(0f) };
        second.Font = InheritedFont.WithWeight(700);
        second.Controls.Add(new Label("...then WithWeight(700) - still 20 pt, now bold")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 28f,
        });
        _appearanceChainWeight = second;

        var third = new Label("...then WithSize(12f) - 12 pt and still bold")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 22f,
        };
        third.Font = InheritedFont.WithSize(12f);
        _appearanceChainAgain = third;

        // The chain is a control tree, not a list: the second level is inside the first, and the
        // third is inside the second, which is what makes the composition go down the tree.
        second.Controls.Add(third);
        first.Controls.Add(second);

        stage.Controls.Add(inherited);
        stage.Controls.Add(nested);
        stage.Controls.Add(overrides);
        stage.Controls.Add(button);
        stage.Controls.Add(first);
        card.Controls.Add(stage);

        card.Controls.Add(new Label(
            "A background is a colour, a palette role, or \"inherit\". A colour or a role assigned to " +
            "a control answers for everything under it. A control whose class default is transparent " +
            "- a label, a plain panel - paints nothing wherever it sits, because that default stops " +
            "the walk rather than inheriting; an assigned transparent, by contrast, is an ordinary " +
            "colour and does answer for descendants.\n\n" +
            "Nothing here is captured at construction: the effective font, colour and background are " +
            "read when the control is measured and painted. What the parent chain contributes is walked " +
            "once and cached, and dropped when an ancestor assigns a value or a control changes parent. " +
            "Palette roles are resolved when they are read, so a palette swap is a repaint; a font " +
            "change re-runs layout.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 12f, 0f, 0f),
        });

        UpdateAppearanceReadout();
        return card;
    }

    /// <summary>
    /// Changes the font of the panel the demo subtree hangs off, which is the runtime half of the
    /// page: the controls already exist, and their text is measured again at the new size.
    /// </summary>
    /// <param name="size">The size to give the subtree, or null to let it inherit again.</param>
    public void SetAppearanceDemo(float? size)
    {
        if (!size.HasValue)
        {
            _appearanceStage?.Font = PropertyValue.Inherited;
        }
        else if (_appearanceStage is not null && _appearanceBigFont is not null)
        {
            _appearanceStage.Font = size.Value >= 21f ? _appearanceBigFont : _appearanceSubtreeFont!;
        }

        UpdateAppearanceReadout();
    }

    /// <summary>Switches the application's font to the bundled face, or back to the system's.</summary>
    /// <param name="bundled">True for the face the application carries, false for the platform's.</param>
    public void SetAppearanceDemoFace(bool bundled)
    {
        if (bundled)
        {
            Theme.FontFamily = DemoFont.Register();
        }
        else
        {
            Theme.FontFamily = null;
        }

        UpdateAppearanceReadout();
    }

    /// <summary>
    /// Prints what the tree resolves to right now: the background state of each level, the font size
    /// the labels ended up with, and the size of the text - which is what changes when a font does.
    /// </summary>
    private void UpdateAppearanceReadout()
    {
        if (_appearanceReadout is null)
        {
            return;
        }

        _appearanceReadout.Text =
            $"panel: {Describe(_appearanceStage)}   |   " +
            $"inherited label: {Describe(_appearanceInherited)}   |   " +
            $"nested panel: {Describe(_appearanceNested)}\n" +
            $"label that paints nothing: {Describe(_appearanceLabel)}   |   " +
            $"inherited label text: {S(_appearanceInherited is { } label ? label.EffectiveFont.MeasureWidth(label.Text) : null)} px wide, " +
            $"{S(_appearanceInherited?.EffectiveFont.LineHeight)} px line height\n" +
            $"override inside the subtree: {Describe(_appearanceOverrides)}   |   override chain: " +
            $"{DescribeFont(_appearanceChainSize)} | {DescribeFont(_appearanceChainWeight)} | {DescribeFont(_appearanceChainAgain)}";

        static string DescribeFont(Control? control) =>
            control is null
                ? "-"
                : $"{control.EffectiveFont.Size:0.#} pt w{control.EffectiveFont.Weight}" +
                  (control.EffectiveFont.Italic ? " italic" : string.Empty);

        static string Describe(Control? control)
        {
            if (control is null)
            {
                return "-";
            }

            string background = control.BackColor.Match(
                control,
                static (Control _, SKColor color) => color.Alpha == 0
                    ? "none"
                    : $"#{color.Alpha:X2}{color.Red:X2}{color.Green:X2}{color.Blue:X2}",
                static (Control _, ThemeColor role) => role.ToString(),
                static (Control _, InheritedValue _) => "inherit");

            return $"{control.EffectiveFont.Size:0.#} pt, {background}";
        }

        static string S(float? value) =>
            value is { } v ? v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "-";
    }
}
