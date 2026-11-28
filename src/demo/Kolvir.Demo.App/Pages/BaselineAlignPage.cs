using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The baseline-alignment page: the same row of controls at two text sizes and two alignments, so
/// the difference between lining boxes up and lining their text up is one picture.
/// </summary>
/// <remarks>
/// The row is a horizontal <see cref="StackPanel"/> with <see cref="StackPanel.AlignBaselines"/> on,
/// holding two ordinary labels at different sizes, a rich label whose first line mixes sizes, a
/// text field, a check box and a button. A check box above it turns the property on and off live,
/// and the second row below it is the same run with the property off, so a single render shows
/// both states.
/// </remarks>
internal sealed class BaselineAlignPage : DemoPage
{
    private StackPanel _live = null!;
    private Label _readout = null!;

    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public BaselineAlignPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// A live row, the same row switched off, and a readout of where each label's first baseline
    /// ended up in the live one.
    /// </summary>
    private Panel Build()
    {
        var card = Card();

        card.Controls.Add(Heading("Baseline alignment"));

        card.Controls.Add(Note(
            "StackPanel.AlignBaselines lines the children that have a text baseline up on that " +
            "baseline instead of on the middle of their boxes. A 12 and a 21 pixel label, a mixed " +
            "rich label, a field, a check box and a button in one row all sit on one line."));

        var toggle = new CheckBox("AlignBaselines")
        {
            Dock = XDock.Top,
            Checked = true,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        };
        toggle.AddCheckedChangedHandler(_ =>
        {
            // The property's setter lays the row out again, so the readout is asked for after it
            // rather than before.
            _live.AlignBaselines = toggle.Checked;
            UpdateReadout();
            SetStatus(toggle.Checked
                ? "AlignBaselines on: the row is aligned on the lowest first-line baseline."
                : "AlignBaselines off: each child is placed by its own VerticalAlignment.");
        });
        card.Controls.Add(toggle);

        card.Controls.Add(ScrollCaption("AlignBaselines = true - text at every size shares one baseline"));
        _live = BuildRow(alignBaselines: true);
        card.Controls.Add(_live);

        card.Controls.Add(ScrollCaption("AlignBaselines = false - the same row, aligned by box"));
        card.Controls.Add(BuildRow(alignBaselines: false));

        _readout = new Label
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 22f,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Thickness(0f, 10f, 0f, 0f),
        };
        card.Controls.Add(_readout);

        return card;
    }

    /// <summary>
    /// One horizontal run of the same mixed controls. With the property on, a baseline-declaring
    /// child keeps its measured height and the row lines them up; with it off the row is what it
    /// always was.
    /// </summary>
    /// <param name="alignBaselines">Whether the run lines its children up on their baselines.</param>
    /// <returns>The run, framed and docked to the top.</returns>
    private StackPanel BuildRow(bool alignBaselines)
    {
        var row = new SurfaceStackPanel
        {
            Dock = XDock.Top,
            AutoSize = true,
            Orientation = StackOrientation.Horizontal,
            AlignBaselines = alignBaselines,
            Spacing = 18f,
            Padding = new Thickness(12f, 6f),
            Margin = new Thickness(0f, 0f, 0f, 10f),
            Border = Framed(8f),
        };

        var small = Sized("12 px", 12f);
        var large = Sized("21 px", 21f);
        row.Controls.Add(small);
        row.Controls.Add(large);

        var middle = Sized("16 px", 16f);
        row.Controls.Add(middle);

        var rich = new RichLabel
        {
            WrapText = false,
            Font = Theme.DefaultFont,
            VerticalAlignment = Alignment.Near,
        };
        rich.AddText("Rich ");
        rich.AddSpan(new TextSpan("mixed") { FontSize = 20f, Bold = true });
        rich.AddText(" run");
        row.Controls.Add(rich);

        row.Controls.Add(new TextBox
        {
            Text = "Field",
            Width = 96f,
            Height = 30f,
            VerticalAlignment = Alignment.Near,
        });

        row.Controls.Add(new CheckBox("Check")
        {
            VerticalAlignment = Alignment.Near,
        });

        row.Controls.Add(new Button("Go")
        {
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            VerticalAlignment = Alignment.Near,
        });

        // The readout describes the live row, so it has to follow the labels as the row arranges
        // them: a label's own bounds change is the moment its baseline in the row is settled.
        if (alignBaselines)
        {
            small.AddBoundsChangedHandler(_ => UpdateReadout());
            middle.AddBoundsChangedHandler(_ => UpdateReadout());
            large.AddBoundsChangedHandler(_ => UpdateReadout());
        }

        return row;
    }

    /// <summary>A label at <paramref name="size"/>, in the theme's own face.</summary>
    /// <param name="text">The text, which is also its size, so the row reads at a glance.</param>
    /// <param name="size">The font size, in logical pixels.</param>
    /// <returns>The label.</returns>
    private static Label Sized(string text, float size) => new(text)
    {
        AutoSize = true,
        Font = new Font(Theme.DefaultFont.FamilyName, size),
        VerticalAlignment = Alignment.Near,
    };

    /// <summary>
    /// Reports the first-line baseline of each label in the live row, read back from the label's
    /// own content rectangle through the same public layout function the paint path uses. Equal
    /// numbers are what the property promises; the row above shows the boxes are not equal.
    /// </summary>
    private void UpdateReadout()
    {
        if (_readout is null)
        {
            return;
        }

        var parts = new List<string>();
        foreach (var child in _live.Controls)
        {
            if (child is not Label label || string.IsNullOrEmpty(label.Text))
            {
                continue;
            }

            var content = label.ContentRect;
            var lines = TextRenderer.BuildTextLines(label.Text, label.EffectiveFont, content.Width, label.WrapText);
            if (lines.Count == 0)
            {
                continue;
            }

            float y = label.Top + TextRenderer.GetBlockBaseline(content, label.EffectiveFont, lines.Count, label.TextAlign);
            parts.Add($"{label.Text} at y={y:0.#}");
        }

        _readout.Text = parts.Count == 0
            ? "The row has not been laid out yet."
            : "First-line baselines in the live row: " + string.Join("   ", parts);
    }
}
