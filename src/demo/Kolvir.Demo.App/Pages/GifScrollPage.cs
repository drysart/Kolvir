using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The GIF out of view page: a scrolling panel whose images are only decoded when seen.
/// </summary>
internal sealed class GifScrollPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public GifScrollPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// The page that shows time passing while nobody is looking: scroll the picture out of the
    /// scroll panel, wait, scroll back, and the frame is the one the clock says rather than the one
    /// it left on.
    /// </summary>
    /// <remarks>
    /// Nothing special makes this happen - the position is computed from a clock rather than
    /// accumulated by a timer - so the page exists to make the claim visible: the readout keeps
    /// counting while the picture is scrolled away, and the frame that comes back is the one the
    /// readout describes.
    /// </remarks>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("GIF out of view"));

        var data = Images.LoadFromBytes(Convert.FromBase64String(DemoGif.Spinner));

        // A stack inside a fixed frame, which is the scrolling container the Scrolling page uses: the
        // run of children is what makes the picture scrollable out of the frame.
        var viewportFrame = new Panel
        {
            Dock = XDock.Top,
            Height = 150f,
            BackColor = null,
            Padding = new Thickness(0f),
            Margin = new Thickness(0f, 0f, 0f, 6f),
        };

        var viewport = new StackPanel
        {
            Dock = XDock.Fill,
            VerticalScroll = ScrollStyle.Automatic,
            Padding = new Thickness(4f),
            Spacing = 4f,
            Border = Framed(8f),
        };
        viewportFrame.Controls.Add(viewport);

        var image = new Image
        {
            AutoSize = true,
            MinimumSize = new SKSize(120f, 120f),
            HorizontalAlignment = Alignment.Near,
            SizeMode = SizeMode.Zoom,
            Border = SimpleBorder.Rounded(6f),
            Source = data,
        };
        viewport.Controls.Add(image);

        for (int row = 0; row < 8; row++)
        {
            viewport.Controls.Add(new Label($"Filler row {row + 1} - scroll past the picture")
            {
                AutoSize = false,
                Height = 22f,
                Font = Theme.CaptionFont,
                ForeColorRole = ThemeColor.MutedForeground,
            });
        }

        card.Controls.Add(viewportFrame);

        // The page runs its own cycle so the claim can be watched without touching anything, but a
        // page that pulls the viewport out from under the pointer is worse than one that needs a
        // button: every leg first checks that the offset is still the one the cycle itself wrote,
        // and the first scroll somebody else does stops the cycle for good. What the page is showing
        // - that time passes while the picture is out of view - is still automatic.
        float home = viewport.VerticalOffset;
        float owned = home;
        bool tookOver = false;

        var awayTimer = new Timer(TimeSpan.FromMilliseconds(2300));
        var backTimer = new Timer(TimeSpan.FromMilliseconds(700));

        void StopCycle()
        {
            awayTimer.Stop();
            backTimer.Stop();
        }

        bool StillOurs()
        {
            if (tookOver)
            {
                return false;
            }

            if (MathF.Abs(viewport.VerticalOffset - owned) < 0.5f)
            {
                return true;
            }

            // Somebody scrolled: the page gives the viewport up rather than dragging it back to
            // where the demonstration wanted it.
            tookOver = true;
            StopCycle();
            SetStatus("You scrolled it, so the page has handed the scrolling back to you.");
            return false;
        }

        void CycleAway()
        {
            if (!StillOurs())
            {
                return;
            }

            owned = 140f;
            viewport.ScrollTo(0f, owned);
            backTimer.Start();
        }

        void CycleBack()
        {
            if (!StillOurs())
            {
                return;
            }

            owned = home;
            viewport.ScrollTo(0f, home);
            awayTimer.Start();
        }

        awayTimer.AddTickHandler(_ =>
        {
            awayTimer.Stop();
            CycleAway();
        });

        backTimer.AddTickHandler(_ =>
        {
            backTimer.Stop();
            CycleBack();
        });

        var buttons = new Panel
        {
            Dock = XDock.Top,
            Height = 34f,
            BackColor = null,
            Padding = new Thickness(0f),
            Margin = new Thickness(0f, 0f, 0f, 6f),
        };

        var away = new Button("Scroll the picture away")
        {
            AutoSize = false,
            Width = 200f,
            Bounds = new SKRect(0f, 2f, 200f, 32f),
        };

        var back = new Button("Scroll it back")
        {
            AutoSize = false,
            Width = 150f,
            Bounds = new SKRect(210f, 2f, 360f, 32f),
        };

        away.AddClickHandler(_ =>
        {
            SetStatus("The picture is scrolled out of view; the timeline is not.");

            // A click is the user asking the page to move it, so the cycle is re-armed from wherever
            // the panel is now.
            tookOver = false;
            owned = viewport.VerticalOffset;
            CycleAway();
        });
        back.AddClickHandler(_ =>
        {
            SetStatus("The picture is back; the frame is the one the clock reached.");

            tookOver = false;
            owned = viewport.VerticalOffset;
            CycleBack();
        });

        bool travelled = false;

        buttons.Controls.Add(away);
        buttons.Controls.Add(back);
        card.Controls.Add(buttons);

        var readout = ScrollCaption("Position 0.00 s of 0.96 s");

        var timer = new Timer(TimeSpan.FromMilliseconds(50));
        timer.AddTickHandler(_ =>
            readout.Text = $"Position {data.Position.TotalSeconds:0.00} s of {data.Duration.TotalSeconds:0.00} s");

        image.AddDisplayedChangedHandler(_ =>
        {
            if (image.IsDisplayed)
            {
                timer.Start();

                // The cycle starts the first time the page is looked at, so the demonstration happens
                // without anyone clicking anything.
                if (!travelled)
                {
                    travelled = true;
                    awayTimer.Start();
                }
            }
            else
            {
                timer.Stop();
                StopCycle();
            }
        });

        data.Play();
        card.Controls.Add(readout);

        card.Controls.Add(Note(
            "Scroll the picture away, wait, then scroll it back: the frame you get is the one the clock "
            + "says, not the one it left on. Nothing in the player arranges that - the position is "
            + "computed from a clock rather than accumulated by a timer - so the readout keeps counting "
            + "while the picture is out of the frame and the frame that comes back agrees with it. The "
            + "page runs the cycle itself so it can be watched without touching anything, and it stops "
            + "the moment you scroll the panel yourself: a demonstration that fights the pointer is "
            + "worse than one that needs a button."));

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
