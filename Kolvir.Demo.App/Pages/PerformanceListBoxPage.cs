using System.Diagnostics;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The performance page: how long a list box takes to be filled.
/// </summary>
internal sealed class PerformanceListBoxPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public PerformanceListBoxPage(DemoUi shell) : base(shell) => Initialize(Build());

    /// <summary>
    /// A list of a chosen size, with what the last frame cost to draw it. The list only paints the
    /// rows that are on screen, so scrolling a hundred thousand items should cost about what
    /// scrolling ten does; the readout is there to show that rather than claim it.
    /// </summary>
    private Panel Build()
    {
        var list = new ListBox
        {
            Dock = XDock.Fill,
            SelectionMode = SelectionMode.One,
            VerticalScroll = ScrollStyle.Automatic,
            HorizontalScroll = ScrollStyle.None,
        };

        return PerformanceCard(list, "items", count =>
        {
            list.BeginUpdate();
            try
            {
                list.Items.Clear();

                if (count > 0)
                {
                    var items = new object[count];
                    for (int index = 0; index < count; index++)
                    {
                        items[index] = $"Item {index + 1:N0}";
                    }

                    list.Items.AddRange(items);
                }
            }
            finally
            {
                list.EndUpdate();
            }

            // A smaller list cannot stay scrolled to where a larger one was.
            list.ScrollTo(0f, 0f);
        });
    }

    /// <summary>
    /// The chrome both performance pages share: a row of buttons that load a chosen number of
    /// entries into <paramref name="content"/>, and a readout of what the last frame cost.
    /// </summary>
    /// <param name="content">The control the buttons fill, and the one the readout reports on.</param>
    /// <param name="noun">What one entry is called, for the count and the status line.</param>
    /// <param name="fill">Replaces everything in <paramref name="content"/> with the given count.</param>
    private Panel PerformanceCard(Control content, string noun, Action<int> fill)
    {
        var card = Card();

        var countLabel = new Label($"no {noun}")
        {
            Dock = XDock.Right,
            AutoSize = false,
            Width = 110f,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.BottomRight,
            Margin = new Thickness(0f),
        };

        var frameLabel = new Label("Last frame -")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Thickness(0f),
        };

        // Docking is applied in declaration order, so the count takes the right edge before the
        // frame time fills what is left of the row.
        var readout = Row(20f);
        readout.Controls.Add(countLabel);
        readout.Controls.Add(frameLabel);

        var buttons = Row(32f);
        buttons.Controls.Add(LoadButton("Clear", () => Load(0)));

        foreach (int size in new[] { 1, 10, 100, 1000, 10000, 100000 })
        {
            int count = size;
            buttons.Controls.Add(LoadButton(size.ToString("N0"), () => Load(count)));
        }

        card.Controls.Add(buttons);
        card.Controls.Add(readout);
        card.Controls.Add(content);

        // The readout is the one thing on the page that is not the content, and it is painted into
        // the frame it reports on, so a naive update would measure itself: the text changes, the
        // label repaints, that repaint is the next frame, and the page never goes idle. Only
        // assigning a changed string and only reading the host every so often lets the loop die
        // once the label is all that is left repainting.
        const long ReadoutIntervalMs = 100;
        long lastReadout = long.MinValue / 2;
        string shownFrame = string.Empty;

        OnHostAvailable(content, host => host.AddFramePaintedHandler(_ =>
        {
            long now = Environment.TickCount64;
            if (now - lastReadout < ReadoutIntervalMs)
            {
                return;
            }

            lastReadout = now;

            var area = host.LastFrameArea;
            string text = $"Last frame {host.LastFrameTime.TotalMilliseconds:0.0} ms " +
                          $"over {area.Width} x {area.Height} px";

            if (text != shownFrame)
            {
                shownFrame = text;
                frameLabel.Text = text;
            }
        }));

        return card;

        Button LoadButton(string text, Action onClick)
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

        void Load(int count)
        {
            long started = Stopwatch.GetTimestamp();

            fill(count);

            countLabel.Text = count == 0 ? $"no {noun}" : $"{count:N0} {noun}";
            SetStatus($"Loaded {count:N0} {noun} in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.0} ms.");
        }
    }

    /// <summary>An invisible full-width strip a page uses to lay a row of controls out in.</summary>
}
