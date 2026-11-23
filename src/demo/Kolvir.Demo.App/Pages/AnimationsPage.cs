using Kolvir.Feature.Animations;
using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The animations page: the easing curves, the clocks and the frames.
/// </summary>
internal sealed class AnimationsPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public AnimationsPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// The animation feature: one timeline with five animations on it, driven by the application's
    /// single frame clock. A block eases across its track, a bar blends two colours while it eases,
    /// and three tiles arrive in a stagger because each one's start time is later than the one
    /// before. Start, Pause, Resume, Restart and End all act on that one set.
    /// </summary>
    /// <remarks>
    /// The set is built with the page and started by a button, so a headless render - which builds
    /// every page and starts nothing - draws it in its rest position and costs no timer at all. The
    /// readout is itself an animation: a linear 0-to-1 value whose callback writes a percentage, so
    /// the timeline's progress can be watched and the "callback assigns the current value" contract
    /// is visible in something other than a control's bounds.
    /// </remarks>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Animations"));

        card.Controls.Add(new Label(
            "One AnimationSet, one frame clock. The block eases across its track, the bar blends " +
            "blue into pink while it eases in, and the three tiles start 150 ms apart so they " +
            "arrive in a stagger. Start, Pause, Resume, Restart and End all act on the same set, " +
            "and the clock stops as soon as it is not running.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 58f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        });

        var readout = new Label("Not started - press Start")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 20f,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Thickness(0f, 0f, 0f, 6f),
        };

        var stage = new Panel
        {
            Dock = XDock.Fill,
            Padding = new Thickness(0f),
        };

        var eastward = new SKColor(0xFF2F6FED);
        var westward = new SKColor(0xFFEC4899);

        var runner = new SurfacePanel
        {
            Bounds = new SKRect(8f, 14f, 52f, 58f),
            BackColor = eastward,
            Border = Framed(8f),
        };
        stage.Controls.Add(runner);

        stage.Controls.Add(new SurfacePanel
        {
            Bounds = new SKRect(8f, 62f, 348f, 64f),
            BackColor = Theme.Border,
            Border = Framed(1f),
        });

        var bar = new SurfacePanel
        {
            Bounds = new SKRect(8f, 82f, 348f, 102f),
            BackColor = eastward,
            Border = SimpleBorder.Thin.WithCornerRadius(4f),
        };
        stage.Controls.Add(bar);

        var tiles = new List<Panel>();
        SKColor[] tileColors =
        [
            new(0xFF2F6FED),
                new(0xFF7C3AED),
                new(0xFFEC4899),
            ];

        // The tiles begin below their resting place and fully transparent, so the first frame of a
        // run is what brings them in rather than a value the page was built at.
        for (int index = 0; index < tileColors.Length; index++)
        {
            var tile = new SurfacePanel
            {
                Bounds = new SKRect(8f + (index * 76f), 186f, 72f + (index * 76f), 250f),
                BackColor = tileColors[index],
                Border = Framed(10f),
                Opacity = 0f,
            };
            tiles.Add(tile);
            stage.Controls.Add(tile);
        }

        TimeSpan timeline = TimeSpan.FromMilliseconds(900);
        int finishes = 0;

        var set = new AnimationSet();

        void Report(string state) =>
            readout.Text =
                $"{state}: {set.Progress * 100d:0} per cent of {timeline.TotalMilliseconds:0} ms elapsed, " +
                $"{finishes} finish handler run(s)";

        // Something that moves, with an ease that starts and ends at rest: a linear position reads
        // as mechanical over this distance, and the curve is the difference between the two.
        set.Add(ValueAnimation.Create(
            8f,
            312f,
            timeline,
            value => runner.Left = value,
            easing: Easing.EaseInOutCubic));

        // Something that changes colour, with a curve of its own, so two animations on one timeline
        // can be seen easing differently from each other.
        set.Add(ValueAnimation.Create(
            eastward,
            westward,
            timeline,
            value => bar.BackColor = value,
            easing: Easing.EaseInQuad));

        // A staggered set: same animation, three start times, so they arrive in sequence.
        for (int index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];
            var stagger = TimeSpan.FromMilliseconds(150 * index);
            var length = TimeSpan.FromMilliseconds(320);

            set.Add(ValueAnimation.Create(
                tile.Top,
                158f,
                length,
                value => tile.Top = value,
                startTime: stagger,
                easing: Easing.EaseOutCubic));

            set.Add(ValueAnimation.Create(
                0f,
                1f,
                length,
                value => tile.Opacity = value,
                startTime: stagger,
                easing: Easing.EaseOutQuad));
        }

        // The readout is a destination like any other, which is the point of the callback: it is a
        // value the animation assigns, not a control.
        set.Add(ValueAnimation.Create(0f, 1f, timeline, _ => Report("Running")));

        set.AddFinishedHandler(_ =>
        {
            finishes++;
            Report("Finished");
        });

        var buttons = ButtonRow();

        static Button RowButton(string text, Action onClick)
        {
            var button = new Button(text)
            {
                Dock = XDock.Left,
                AutoSize = true,
                Style = ButtonStyle.Secondary,
                Margin = new Thickness(0f, 0f, 6f, 0f),
            };
            button.AddClickHandler(_ => onClick());
            return button;
        }

        buttons.Controls.Add(RowButton("Start", () =>
        {
            set.Start();
            Report("Running");
        }));

        buttons.Controls.Add(RowButton("Pause", () =>
        {
            set.Pause();
            Report("Paused");
        }));

        buttons.Controls.Add(RowButton("Resume", () =>
        {
            set.Resume();
            Report("Running");
        }));

        buttons.Controls.Add(RowButton("Restart", () =>
        {
            set.Restart();
            Report("Running");
        }));

        buttons.Controls.Add(RowButton("End", () =>
        {
            set.End();
            Report("Ended");
        }));

        card.Controls.Add(buttons);
        card.Controls.Add(readout);
        card.Controls.Add(stage);

        return card;
    }

    /// <summary>
    /// The spectrum colour picker, one instance per colour space plus a narrow one. Each column
    /// carries a live readout of the colour it is producing, so dragging one square shows what the
    /// same value looks like in the other two modes, and the narrow instance shows the component
    /// fields stacking instead of sharing a row.
    /// </summary>
}
