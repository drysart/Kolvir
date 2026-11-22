using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The WebP frames page: every frame the file holds, drawn at once.
/// </summary>
internal sealed class WebpFramesPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public WebpFramesPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// The WebP frame-stepping page: one frame at a time, backwards or forwards, on a paused
    /// animation whose boundaries come from the timeline rather than from a table.
    /// </summary>
    /// <remarks>
    /// Stepping is a seek to a frame's start, and the starts are walked from
    /// <see cref="ImageData.TimeUntilNextFrame"/>, which is the same number the control
    /// schedules its repaints by. Because every frame was decoded up front, stepping backwards costs
    /// what stepping forwards does, and the sub-rectangle frames make the offset visible when the
    /// readout names the frame.
    /// </remarks>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("WebP frames"));

        var data = Images.LoadFromBytes(Convert.FromBase64String(DemoWebp.Spinner));
        var starts = FrameStarts(data);
        int index = 0;

        var row = new Panel
        {
            Dock = XDock.Top,
            Height = 206f,
            BackColor = null,
            Padding = new Thickness(0f),
        };

        var image = new Image
        {
            AutoSize = false,
            SizeMode = SizeMode.Zoom,
            ImageAlign = ContentAlignment.MiddleCenter,
            Border = SimpleBorder.Rounded(6f),
            Bounds = new SKRect(0f, 4f, 192f, 196f),
            Source = data,
        };
        row.Controls.Add(image);

        var previous = new Button("Previous")
        {
            AutoSize = false,
            Width = 100f,
            Bounds = new SKRect(208f, 12f, 308f, 42f),
        };
        row.Controls.Add(previous);

        var next = new Button("Next")
        {
            AutoSize = false,
            Width = 100f,
            Bounds = new SKRect(318f, 12f, 418f, 42f),
        };
        row.Controls.Add(next);

        var readout = new Label(string.Empty)
        {
            AutoSize = false,
            Bounds = new SKRect(208f, 54f, 468f, 76f),
        };
        row.Controls.Add(readout);

        row.Controls.Add(new Label(
            "The animation is paused and the buttons seek to a frame boundary. The readout's second "
            + "number is the frame's start on the timeline, which is what the player schedules its "
            + "repaints by.")
        {
            AutoSize = false,
            Bounds = new SKRect(208f, 84f, 468f, 196f),
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
        });

        card.Controls.Add(row);

        void Show(int wanted)
        {
            index = Math.Clamp(wanted, 0, starts.Count - 1);
            data.Seek(starts[index]);
            readout.Text = $"Frame {index + 1} of {starts.Count} - starts at {starts[index].TotalMilliseconds:0} ms";
        }

        previous.AddClickHandler(_ => Show(index - 1));
        next.AddClickHandler(_ => Show(index + 1));
        Show(0);

        return card;
    }


    /// <summary>
    /// The animated WebP page: the picture itself, play and pause, a seek bar, and a readout that says
    /// where in the pass it is.
    /// </summary>
    /// <remarks>
    /// The readout is a timer of the demo's own, not the player's, exactly as the GIF page does it:
    /// the player runs one only to repaint when a frame boundary arrives - about twelve times a
    /// second for this file - and this text wants a number every fifty milliseconds. The demo timer
    /// follows the same rule the player follows: it exists only while the page is displayed, and the
    /// position it prints comes from the clock rather than from the ticks.
    /// </remarks>
}
