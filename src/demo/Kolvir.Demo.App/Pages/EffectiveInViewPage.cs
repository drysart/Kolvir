using Kolvir.InView;
using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The effective-in-view page: a scrolling run of rows whose readouts say whether the last layout
/// left them on screen, and a panel that slides over one of them to show occlusion.
/// </summary>
/// <remarks>
/// The readouts are refreshed from the frame that is being painted rather than only from the change
/// handlers, for a practical reason: a handler establishes its baseline silently on the first pass, so
/// a readout that only listened would have nothing to show until the user scrolled. The handlers are
/// still the demonstration - the counter under the list is incremented only by a real change.
/// </remarks>
internal sealed class EffectiveInViewPage : DemoPage
{
    private readonly List<Label> _rows = new();
    private Label _changes = null!;
    private Panel _list = null!;
    private Panel _cover = null!;
    private Button _coverButton = null!;
    private int _changeCount;

    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public EffectiveInViewPage(DemoUi shell) : base(shell) => Initialize(Build());

    private Panel Build()
    {
        var card = Card();

        card.Controls.Add(Heading("Effective in view"));
        card.Controls.Add(Note(
            "EffectiveVisible answers whether the control and every container above it are marked "
            + "visible. This answers the expensive question: whether any part of the control is "
            + "actually on screen after its ancestors' content bounds clip it and everything painted "
            + "over it is taken away. Scroll the list, or cover a row, and each readout follows."));

        _changes = new Label("Changes: 0")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 18f,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.BottomLeft,
        };

        var buttons = ButtonRow();
        buttons.Height = 32f;
        buttons.Controls.Add(PageButton("Scroll down", () => _list.ScrollBy(0f, 52f)));
        buttons.Controls.Add(PageButton("Scroll up", () => _list.ScrollBy(0f, -52f)));
        _coverButton = PageButton("Cover row 5", ToggleCover);
        buttons.Controls.Add(_coverButton);

        card.Controls.Add(buttons);
        card.Controls.Add(BuildList());
        card.Controls.Add(_changes);

        return card;
    }

    private Panel BuildList()
    {
        // The frame paints, then its child list paints: that is the moment the readouts can be read
        // from the layout the frame is about to show.
        var frame = new FrameReadout
        {
            Dock = XDock.Top,
            Height = 190f,
            Padding = new Thickness(4f),
            Border = Framed(8f),
            Margin = new Thickness(0f, 0f, 0f, 8f),
            Painting = RefreshReadouts,
        };

        _list = new Panel
        {
            Dock = XDock.Fill,
            VerticalScroll = ScrollStyle.Always,
            Padding = new Thickness(4f),
        };

        frame.Controls.Add(_list);

        for (int index = 0; index < 14; index++)
        {
            int row = index;

            var label = new Label(RowText(row, inView: false))
            {
                AutoSize = false,
                Bounds = new SKRect(4f, 4f + (index * 26f), 360f, 26f + (index * 26f)),
                BackColor = Theme.Secondary,
                ForeColor = ThemeColor.MutedForeground,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Thickness(0f),
            };

            label.AddEffectiveInViewChangedHandler(sender =>
            {
                _changeCount++;
                _changes.Text = $"Changes: {_changeCount} (last: row {row + 1} became "
                    + (((Control)sender).EffectiveInView ? "visible" : "hidden") + ")";
                SetStatus($"Row {row + 1} changed to {(((Control)sender).EffectiveInView ? "in view" : "out of view")}.");
            });

            _rows.Add(label);
            _list.Controls.Add(label);
        }

        _cover = new Panel
        {
            Bounds = new SKRect(4f, 4f + (4 * 26f), 360f, 26f + (4 * 26f)),
            BackColor = Theme.Surface,
        };

        return frame;
    }

    private static string RowText(int row, bool inView) =>
        $"Row {row + 1} - {(inView ? "in view" : "out of view")}";

    private void ToggleCover()
    {
        bool covered = _cover.Parent is not null;

        if (covered)
        {
            _list.Controls.Remove(_cover);
        }
        else
        {
            _list.Controls.Add(_cover);
        }

        _coverButton.Text = covered ? "Cover row 5" : "Uncover row 5";

        // Adding or removing a child arranges the panel it went into, but it is not a whole-tree pass,
        // so the change handlers would wait for the next one. Asking for it here is the one-line way
        // an application makes "now" mean now.
        _list.Host?.PerformLayout();
    }

    private void RefreshReadouts()
    {
        // The rows are free-positioned so the panel scrolls them; that also means their width has to
        // be kept in step with the viewport by hand. Assigning the same value again costs a compare.
        float width = MathF.Max(120f, _list.ClientSize.Width - 12f);

        for (int index = 0; index < _rows.Count; index++)
        {
            var row = _rows[index];
            bool inView = row.EffectiveInView;

            row.Text = RowText(index, inView);
            row.ForeColor = inView ? ThemeColor.MutedForeground : ThemeColor.DisabledForeground;
            row.Width = width;
            _cover.Width = width;
        }
    }

    private static Button PageButton(string text, Action onClick)
    {
        var button = new Button(text)
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };

        button.AddClickHandler(_ => onClick());
        return button;
    }

    /// <summary>A framed panel that runs an action at the start of its own paint.</summary>
    /// <remarks>
    /// Painting is the moment the page knows the layout has run and the host exists, which is when a
    /// readout can be read without forcing anything. A page has no host while it is being built, so
    /// this is cheaper than waiting for a window or a frame callback that a headless render never
    /// raises.
    /// </remarks>
    private sealed class FrameReadout : Panel
    {
        public Action? Painting { get; set; }

        protected override void OnPaint(SKCanvas canvas, SKRect rect)
        {
            Painting?.Invoke();
            base.OnPaint(canvas, rect);
        }
    }
}
