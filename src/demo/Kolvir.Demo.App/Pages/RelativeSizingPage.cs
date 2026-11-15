using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The relative sizing page: children asking for a share of the parent, for their own measured size,
/// and for one of the two by way of a value that could not be read at all.
/// </summary>
internal sealed class RelativeSizingPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public RelativeSizingPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// One stage with four children that each ask for their size a different way, plus a readout of
    /// what was asked for and what came of it - which changes as the window is resized, because the
    /// percentages are answered on every layout pass.
    /// </summary>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Relative sizing"));
        card.Controls.Add(Note(
            "Width, Height, Left and Top take a number of pixels, a percentage of the parent's content area, " +
            "or auto. Resize the window: the percentages follow it, and a value that could not be read is " +
            "laid out as auto while still reporting itself as invalid."));

        var readout = ScrollCaption(string.Empty);
        readout.Margin = new Thickness(0f, 0f, 0f, 8f);

        var stage = new Panel
        {
            Dock = XDock.Fill,
            Border = Framed(8f),
            Padding = new Thickness(0f),
            BackColor = null,
        };

        var percent = new Panel
        {
            Bounds = new SKRect(0f, 0f, 0f, 0f),
            Border = SimpleBorder.Rounded(6f),
            BackColor = new SKColor(0xFF3D7DF2),
            Left = "6%",
            Top = "12%",
            Width = "38%",
            Height = "28%",
        };

        var half = new Panel
        {
            Bounds = new SKRect(0f, 0f, 0f, 0f),
            Border = SimpleBorder.Rounded(6f),
            BackColor = new SKColor(0xFFA0522D),
            Left = "50%",
            Top = "12%",
            Width = "44%",
            Height = "60%",
        };

        var autoSize = new Label("auto: the width this text needs")
        {
            Left = "6%",
            Top = "50%",
            Width = "auto",
            Height = "auto",
            BackColor = new SKColor(0xFF15803D),
            Font = Theme.CaptionFont,
        };

        var invalid = new Label("invalid: laid out as auto, still reported as invalid")
        {
            Left = "6%",
            Top = "82%",
            Width = "half of the room, please",
            Height = "auto",
            BackColor = new SKColor(0xFF7C3AED),
            Font = Theme.CaptionFont,
        };

        stage.Controls.Add(percent);
        stage.Controls.Add(half);
        stage.Controls.Add(autoSize);
        stage.Controls.Add(invalid);
        card.Controls.Add(readout);
        card.Controls.Add(stage);

        void Report()
        {
            readout.Text =
                $"{percent.Width} of {stage.ContentRect.Width:0} px = {percent.Bounds.Width:0} px  |  " +
                $"box {half.Width} x {half.Height}  |  " +
                $"label {autoSize.Width} = {autoSize.Bounds.Width:0} px  |  " +
                $"unreadable text laid out as auto = {invalid.Bounds.Width:0} px";
        }

        stage.AddBoundsChangedHandler(_ => Report());
        Report();

        return card;
    }
}
