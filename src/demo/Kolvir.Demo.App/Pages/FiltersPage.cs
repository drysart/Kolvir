using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The filters page: a blur and a tint over whole subtrees, driven by a track bar so the reach of a
/// filter and the repaint it costs can be seen rather than described.
/// </summary>
internal sealed class FiltersPage : DemoPage
{
    private readonly List<ControlFilter> _owned = new();

    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public FiltersPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// Three copies of the same card: one plain, one blurred, and one blurred and then tinted, which
    /// is also the demonstration that the second filter sees the first one's output.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Filters"));

        var plain = FilterCell("No filter");
        var blurred = FilterCell("Blur - the whole card is one layer");
        var tinted = FilterCell("Blur, then a tint over the result");

        plain.Cell.TableColumn = 0;
        plain.Cell.TableRow = 0;
        blurred.Cell.TableColumn = 1;
        blurred.Cell.TableRow = 0;
        tinted.Cell.TableColumn = 2;
        tinted.Cell.TableRow = 0;

        var grid = new TableLayoutPanel
        {
            Dock = XDock.Top,
            Height = 190f,
            BackColor = null,
            Padding = new Thickness(0f),
            CellPadding = new Thickness(0f, 0f, 12f, 0f),
            Margin = new Thickness(0f, 0f, 0f, 12f),
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Percent, 100f),
                new ColumnStyle(SizeType.Percent, 100f),
                new ColumnStyle(SizeType.Percent, 100f),
            },
            RowStyles =
            {
                new RowStyle(SizeType.Percent, 100f),
            },
            Controls =
            {
                plain.Cell,
                blurred.Cell,
                tinted.Cell,
            },
        };

        card.Controls.Add(grid);

        var sigma = new TrackBar
        {
            Dock = XDock.Top,
            Height = 28f,
            Minimum = 0f,
            Maximum = 16f,
            Value = 6f,
            SmallChange = 1f,
            LargeChange = 4f,
            TickFrequency = 4f,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        };

        var readout = ScrollCaption("Blur sigma 6, which reaches 18 pixels past a changed one");

        void Apply()
        {
            var blur = ControlFilter.Blur(sigma.Value);
            var over = sigma.Value > 0f
                ? blur.Then(ControlFilter.Tint(new SKColor(0x20, 0x60, 0xC0, 0x66)))
                : blur;

            // The page owns the filters it hands out - a control never disposes one - so the old pair
            // is released once nothing points at it any more. The track bar makes a new pair on every
            // value, and at sigma 0 the two cards share one filter rather than making two.
            var stale = _owned.ToArray();
            _owned.Clear();

            blurred.Frame.Filter = blur;
            tinted.Frame.Filter = over;

            _owned.Add(blur);

            if (!ReferenceEquals(over, blur))
            {
                _owned.Add(over);
            }

            foreach (var previous in stale)
            {
                previous.Dispose();
            }

            readout.Text = sigma.Value > 0f
                ? $"Blur sigma {sigma.Value:0}, which reaches {blur.Margin:0} pixels past a changed one"
                : "No blur, so the cards are the same and nothing repaints whole";
        }

        sigma.AddValueChangedHandler(_ => Apply());
        Apply();

        card.Controls.Add(sigma);
        card.Controls.Add(readout);

        card.Controls.Add(Note(
            "A filtered control paints itself and its children into a layer, and the filter runs when " +
            "that layer is composited, so a blur or a tint takes the whole subtree as one picture. " +
            "ControlFilter.Margin is what the filter promises about how far its output reaches: a tint " +
            "answers 0 and the control keeps the small partial repaints it would have had anyway, while " +
            "a blur answers 3 sigma and the control repaints whole whenever anything inside it changes. " +
            "Dragging the track bar to 0 shows the same cards with no layer cost at all."));

        return card;
    }

    /// <summary>One card with a picture in it, and the caption that names it underneath.</summary>
    /// <param name="caption">What the card says about itself.</param>
    /// <remarks>
    /// The caption sits outside the frame on purpose: the filter is on the frame, so a caption inside
    /// it would be blurred along with the picture and an unreadable label says nothing about what the
    /// card is showing.
    /// </remarks>
    private static (Panel Cell, Panel Frame) FilterCell(string caption)
    {
        var cell = new Panel
        {
            Dock = XDock.Fill,
            BackColor = null,
            Padding = new Thickness(0f),
        };

        var frame = new SurfacePanel
        {
            Dock = XDock.Fill,
            Border = Framed(8f),
            Padding = new Thickness(10f),
        };

        var text = new Label(caption)
        {
            Dock = XDock.Bottom,
            AutoSize = false,
            Height = 30f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
        };

        var picture = new Image
        {
            Dock = XDock.Fill,
            SizeMode = SizeMode.Zoom,
            Border = SimpleBorder.Rounded(6f),
        };
        picture.Source = Images.LoadFromStream(CreateGradientPng(240, 140));

        frame.Controls.Add(picture);
        cell.Controls.Add(frame);
        cell.Controls.Add(text);
        return (cell, frame);
    }
}
