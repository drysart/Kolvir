using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The scrolling page: an automatic bar, an always bar, and both at once.
/// </summary>
internal sealed class ScrollingPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public ScrollingPage(DemoUi shell) : base(shell) => Initialize(Build());

    private Panel Build()
    {
        var card = Card();

        card.Controls.Add(ScrollCaption("VerticalScroll = Automatic"));
        card.Controls.Add(BuildAutoScrollList());
        card.Controls.Add(ScrollCaption("HorizontalScroll = Always"));
        card.Controls.Add(BuildAlwaysScrollStrip());

        return card;
    }

    /// <summary>
    /// An <see cref="ScrollStyle.Automatic"/> panel: the vertical bar appears only because the
    /// rows overflow. Only children with <see cref="DockStyle.None"/> scroll, so each row is placed
    /// explicitly instead of being docked.
    /// </summary>
    private Panel BuildAutoScrollList()
    {
        var list = new SurfacePanel
        {
            Dock = XDock.Top,
            Height = 90f,
            VerticalScroll = ScrollStyle.Automatic,
            Padding = new Thickness(4f),
            Margin = new Thickness(0f, 0f, 0f, 6f),
        };

        for (int i = 0; i < 10; i++)
        {
            var row = new Button($"Row {i + 1}")
            {
                AutoSize = false,
                Style = ButtonStyle.Secondary,
                Bounds = new SKRect(4f, 4f + (i * 24f), 128f, 26f + (i * 24f)),
            };
            row.AddClickHandler(_ => SetStatus($"Row {i + 1} clicked - hit testing follows the scroll offset."));
            list.Controls.Add(row);
        }

        return list;
    }

    /// <summary>An <see cref="ScrollStyle.Always"/> panel: the bar is shown even when it fits.</summary>
    private Panel BuildAlwaysScrollStrip()
    {
        var strip = new SurfacePanel
        {
            Dock = XDock.Top,
            Height = 36f,
            HorizontalScroll = ScrollStyle.Always,
            Padding = new Thickness(4f),
            Margin = new Thickness(0f, 0f, 0f, 6f),
        };

        for (int i = 0; i < 10; i++)
        {
            strip.Controls.Add(new Label($"Chip {i + 1}")
            {
                AutoSize = false,
                BackColor = Theme.Secondary,
                ForeColor = ThemeColor.MutedForeground,
                TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new SKRect(4f + (i * 92f), 4f, 88f + (i * 92f), 16f),
            });
        }

        return strip;
    }
}
