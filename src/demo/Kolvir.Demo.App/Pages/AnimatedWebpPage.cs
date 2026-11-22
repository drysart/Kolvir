using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The animated WebP page: the frames, the loop and the controls that drive them.
/// </summary>
internal sealed class AnimatedWebpPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public AnimatedWebpPage(DemoUi shell) : base(shell) => Initialize(Build());

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
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Animated WebP"));

        var data = Images.LoadFromBytes(Convert.FromBase64String(DemoWebp.Spinner));

        var row = new Panel
        {
            Dock = XDock.Top,
            Height = 206f,
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

        var playPause = new Button("Pause")
        {
            AutoSize = false,
            Width = 110f,
            Bounds = new SKRect(208f, 12f, 318f, 42f),
        };
        row.Controls.Add(playPause);

        var seek = new TrackBar
        {
            Minimum = 0f,
            Maximum = 1000f,
            Value = 0f,
            SmallChange = 10f,
            LargeChange = 100f,
            TickFrequency = 100f,
            Bounds = new SKRect(208f, 50f, 468f, 78f),
        };
        row.Controls.Add(seek);

        var readout = new Label("0.00 s of 0.96 s - playing")
        {
            AutoSize = false,
            Bounds = new SKRect(208f, 84f, 468f, 104f),
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
        };
        row.Controls.Add(readout);

        row.Controls.Add(new Label(
            "48x48, twelve frames, 80 ms each, and the RIFF ANIM chunk declares an infinite loop. The "
            + "first frame is the whole canvas and the eleven after it are 8x8 ANMF sub-rectangles, so "
            + "the compositor draws each one at its own offset rather than replacing the canvas.")
        {
            AutoSize = false,
            Bounds = new SKRect(208f, 110f, 468f, 196f),
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
        });

        card.Controls.Add(row);

        bool seeking = false;

        void Update()
        {
            var position = data.Position;
            var duration = data.Duration.TotalMilliseconds;

            readout.Text = duration <= 0
                ? "still image"
                : $"{position.TotalSeconds:0.00} s of {data.Duration.TotalSeconds:0.00} s - "
                  + (data.IsPlaying ? "playing" : data.Loop ? "paused" : "stopped");

            seeking = true;
            seek.Value = duration <= 0 ? 0f : (float)(position.TotalMilliseconds / duration * 1000.0);
            seeking = false;
        }

        playPause.AddClickHandler(_ =>
        {
            if (data.IsPlaying)
            {
                data.Pause();
            }
            else
            {
                data.Play();
            }

            playPause.Text = data.IsPlaying ? "Pause" : "Play";
            Update();
        });

        seek.AddValueChangedHandler(_ =>
        {
            if (seeking || data.Duration <= TimeSpan.Zero)
            {
                return;
            }

            data.Seek(TimeSpan.FromMilliseconds(data.Duration.TotalMilliseconds * seek.Value / 1000.0));
            Update();
        });

        var timer = new Timer(TimeSpan.FromMilliseconds(50));
        timer.AddTickHandler(_ => Update());
        image.AddDisplayedChangedHandler(_ =>
        {
            if (image.IsDisplayed)
            {
                Update();
                timer.Start();
            }
            else
            {
                timer.Stop();
            }
        });

        data.Play();
        Update();

        card.Controls.Add(ScrollCaption(
            "Play and Pause are the timeline's; the seek bar re-anchors it, so a seek during playback "
            + "does not jump when the clock catches up. The timeline is the GIF feature's, unchanged."));

        return card;
    }

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
}
