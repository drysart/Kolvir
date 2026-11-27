using System.Globalization;
using System.Text;
using Kolvir.Feature.AssetLoading;
using Kolvir.Feature.InView;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The asset loading page: a run of images long enough to scroll, each showing where its own load has
/// got to, with the manager's snapshot beside the list.
/// </summary>
/// <remarks>
/// <para>
/// This is the end-to-end demonstration the design note asks for
/// (dev-docs/ASSET-LOADING.md section 7): the manager's own tests hand it a fake loader and prove the
/// queue obeys the priority it is given, but nothing there proves the priority came from
/// <c>EffectiveInView</c>. Here the priority comes from that feature, through the <c>Image</c>
/// integration, and the list is longer than the viewport so scrolling is what changes it.
/// </para>
/// <para>
/// The readout is beside the list rather than behind a button because a stalled queue is the case
/// that matters: the top of the snapshot is the request the manager would start next, and a
/// <c>CanLoadNow</c> of false there is the whole explanation.
/// </para>
/// <para>
/// The per-row state is read once and then updated only from
/// <c>AddSourceLoadStateChangedHandler</c>, never by a timer. The priority half of a row and the
/// snapshot are re-read from the frame being painted, because a handler establishes its baseline
/// silently on the first pass: a readout that only listened would show nothing until the user
/// scrolled.
/// </para>
/// <para>
/// <b>The URIs are assigned while the page is unhosted</b>, which is the ordering an application
/// uses and the one the integration's priority confirmation exists for: the load starts when the tree
/// reaches a window, on a pass whose host may have no client area yet, so the priority taken at
/// submission can be Background even for a row that is on screen. The pass after it confirms the
/// priority from the answer that pass settled, so a queued row is Visible before the walk that would
/// start it. The snapshot's caption says so and the start order is the evidence.
/// </para>
/// </remarks>
internal sealed class AssetLoadingPage : DemoPage
{
    private const int RowCount = 40;
    private const float RowHeight = 56f;
    private const float RowSpacing = 4f;

    /// <summary>The row whose URI the factory deliberately does not claim.</summary>
    private const int FailedRow = 5;

    private const int JumpRow = 28;

    private readonly Image[] _images = new Image[RowCount];
    private readonly Panel[] _rowPanels = new Panel[RowCount];
    private readonly string[] _uris = new string[RowCount];
    private readonly ImageSourceLoadState[] _states = new ImageSourceLoadState[RowCount];
    private readonly Label[] _stateLabels = new Label[RowCount];
    private readonly Label[] _detailLabels = new Label[RowCount];

    private StackPanel _list = null!;
    private Label _summary = null!;
    private Label _snapshot = null!;
    private Label _started = null!;
    private IAssetLoadManager? _manager;
    private DemoAssetLoaderFactory? _factory;

    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public AssetLoadingPage(DemoUi shell) : base(shell) => Initialize(Build());

    private Panel Build()
    {
        var card = Card();

        card.Controls.Add(Heading("Asset loading"));
        card.Controls.Add(Note(
            "Each row is an Image following its own SourceUri, in a list longer than the viewport. "
            + "Scrolling changes EffectiveInView, which moves a load between Visible (1000) and "
            + "Background (0); the manager starts the highest first. The demo's factory is a real gate "
            + "- three fetches at a time per host - and waits 220 ms per fetch so the queue is "
            + "watchable. There is no network: the URIs are synthetic and the sample draws the tiles."));

        var buttons = ButtonRow();
        buttons.Height = 32f;
        buttons.Controls.Add(PageButton("Scroll down", () => ScrollRows(3)));
        buttons.Controls.Add(PageButton("Scroll up", () => ScrollRows(-3)));
        buttons.Controls.Add(PageButton($"Jump to row {JumpRow + 1}", Jump));
        buttons.Controls.Add(PageButton("Retry the failed row", RetryFailed));
        buttons.Controls.Add(PageButton("Reload all", ReloadAll));
        card.Controls.Add(buttons);

        card.Controls.Add(new Label(
            $"Row {FailedRow + 1} is deliberate: its URI is outside what the factory claims, so the "
            + "provider refuses it and the control is Failed with a NotSupportedException. Retrying it "
            + "is the two-assignment form - SourceUri = null, then the URI again - and an unclaimed URI "
            + "fails the same way, because the fault is configuration rather than something transient.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 32f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 0f, 0f, 4f),
        });

        _summary = new Label("Nothing has been asked for yet.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 18f,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Thickness(0f, 0f, 0f, 6f),
        };

        // The content is itself the frame that refreshes the readouts, rather than the list alone:
        // a panel paints its children after its own paint, so everything under this one - the
        // summary, the snapshot and the rows - is written before it is drawn. The effective-in-view
        // page refreshes from its list frame for the same reason; putting the summary and the
        // snapshot outside it would show them one frame behind the rows.
        var content = new FrameReadout
        {
            Dock = XDock.Fill,
            Padding = new Thickness(0f),
            Painting = RefreshReadouts,
        };
        content.Controls.Add(_summary);
        content.Controls.Add(BuildSnapshot());
        content.Controls.Add(BuildList());
        card.Controls.Add(content);

        RefreshSummary();

        return card;
    }

    private Panel BuildList()
    {
        var frame = new Panel
        {
            Dock = XDock.Fill,
            Padding = new Thickness(4f),
            Border = Framed(8f),
        };

        _list = new StackPanel
        {
            Dock = XDock.Fill,
            VerticalScroll = ScrollStyle.Always,
            Padding = new Thickness(4f),
            Spacing = RowSpacing,
        };

        frame.Controls.Add(_list);

        for (int row = 0; row < RowCount; row++)
        {
            _list.Controls.Add(BuildRow(row));
        }

        return frame;
    }

    private Panel BuildRow(int row)
    {
        string uri = row == FailedRow
            ? $"unclaimed://missing/{row + 1:00}.png"
            : $"{DemoAssetLoaderFactory.Scheme}://tiles/{row + 1:00}";
        _uris[row] = uri;

        var panel = new Panel
        {
            AutoSize = false,
            Height = RowHeight,
            BackColor = Theme.Secondary,
            Padding = new Thickness(6f, 4f, 8f, 4f),
        };

        var thumb = new Image
        {
            Dock = XDock.Left,
            AutoSize = false,
            Width = 84f,
            SizeMode = SizeMode.Zoom,
            Border = SimpleBorder.Rounded(4f),
            PlaceholderColor = ThemeColor.Background,
        };

        var state = new Label(HintFor(ImageSourceLoadState.None, inView: false))
        {
            Dock = XDock.Right,
            AutoSize = false,
            Width = 150f,
            Font = Theme.CaptionFont,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = ThemeColor.MutedForeground,
        };

        var detail = new Label($"Row {row + 1:00} · {uri}")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Thickness(10f, 0f, 10f, 0f),
        };

        panel.Controls.Add(thumb);
        panel.Controls.Add(state);
        panel.Controls.Add(detail);

        _rowPanels[row] = panel;
        _images[row] = thumb;
        _stateLabels[row] = state;
        _detailLabels[row] = detail;

        // The state handler first, so the Pending the assignment sets is seen; the in-view handler
        // after, so the integration's own subscription - made when the URI is assigned - runs before
        // this page reads a snapshot of the queue it has just reordered.
        thumb.AddSourceLoadStateChangedHandler(sender =>
        {
            _states[row] = ((Image)sender).SourceLoadState;
            RefreshRow(row);
            RefreshSummary();
            RefreshSnapshot();
        });

        // The ordinary ordering: the tree is unhosted, so the load starts on the pass that attaches
        // it, and the pass after confirms the priority that pass could not answer (section 6.1).
        thumb.SourceUri = uri;
        _states[row] = thumb.SourceLoadState;

        thumb.AddEffectiveInViewChangedHandler(_ =>
        {
            RefreshRow(row);
            RefreshSummary();
            RefreshSnapshot();
        });

        RefreshRow(row);

        return panel;
    }

    private Panel BuildSnapshot()
    {
        var panel = new SurfacePanel
        {
            Dock = XDock.Right,
            Width = 404f,
            Padding = new Thickness(12f),
            Margin = new Thickness(12f, 0f, 0f, 0f),
        };

        panel.Controls.Add(Heading("Snapshot()"));
        panel.Controls.Add(ScrollCaption("the manager's queue, read down from the top"));
        panel.Controls.Add(new Label(
            "Read down from the top: row, priority, phase, live CanLoadNow. A top entry that says "
            + "gated is why nothing below it started. The URIs are assigned while the page is "
            + "unhosted, as an application does; the pass that can answer confirms each load's "
            + "priority.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 88f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 4f, 0f, 4f),
        });

        _snapshot = new Label("The manager resolves when this page is on the stage.")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.Foreground,
            TextAlign = ContentAlignment.TopLeft,
        };
        panel.Controls.Add(_snapshot);

        _started = new Label("The factory resolves with the services.")
        {
            Dock = XDock.Bottom,
            AutoSize = false,
            Height = 84f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Thickness(0f, 6f, 0f, 0f),
        };
        panel.Controls.Add(_started);

        return panel;
    }

    private static string HintFor(ImageSourceLoadState state, bool inView) =>
        $"{state} · {(inView ? "in view" : "off screen")}";

    /// <summary>Re-reads one row's state, priority and error from the control.</summary>
    /// <param name="row">The row to refresh.</param>
    private void RefreshRow(int row)
    {
        Image image = _images[row];
        ImageSourceLoadState state = _states[row];

        _stateLabels[row].Text = HintFor(state, image.EffectiveInView);
        _stateLabels[row].ForeColor = state switch
        {
            ImageSourceLoadState.Loaded => ThemeColor.Foreground,
            ImageSourceLoadState.Loading or ImageSourceLoadState.Failed => ThemeColor.Accent,
            _ => ThemeColor.MutedForeground,
        };

        string detail = $"Row {row + 1:00} · {_uris[row]}";

        if (state == ImageSourceLoadState.Failed && image.SourceLoadError is { } error)
        {
            detail += $"\n{error.GetType().Name}";
        }

        _detailLabels[row].Text = detail;
    }

    private void RefreshSummary()
    {
        int none = 0;
        int pending = 0;
        int loading = 0;
        int loaded = 0;
        int failed = 0;
        int inView = 0;

        for (int row = 0; row < RowCount; row++)
        {
            switch (_states[row])
            {
                case ImageSourceLoadState.Pending:
                    pending++;
                    break;

                case ImageSourceLoadState.Loading:
                    loading++;
                    break;

                case ImageSourceLoadState.Loaded:
                    loaded++;
                    break;

                case ImageSourceLoadState.Failed:
                    failed++;
                    break;

                default:
                    none++;
                    break;
            }

            if (_images[row] is { } image && image.EffectiveInView)
            {
                inView++;
            }
        }

        _summary.Text =
            $"Pending {pending} · Loading {loading} · Loaded {loaded} · Failed {failed}"
            + (none > 0 ? $" · None {none}" : string.Empty)
            + $" · in view {inView} of {RowCount}";
    }

    /// <summary>Reads the manager's queue and the factory's fetch counters into the readout.</summary>
    private void RefreshSnapshot()
    {
        IAssetLoadManager? manager = ResolveManager();

        if (manager is null)
        {
            _snapshot.Text = "The manager resolves when this page is on the stage.";
        }
        else
        {
            IReadOnlyList<AssetLoadSnapshot> entries = manager.Snapshot();
            var builder = new StringBuilder();
            int shown = Math.Min(entries.Count, 5);

            for (int index = 0; index < shown; index++)
            {
                AssetLoadSnapshot entry = entries[index];
                string row = entry.Loader is DemoAssetLoader demo
                    ? demo.Row.ToString("00", CultureInfo.InvariantCulture)
                    : "--";

                builder.Append(index == 0 ? "> " : "  ")
                    .Append(row).Append("  ")
                    .Append(DescribePriority(entry.Priority)).Append("  ")
                    .Append(entry.Phase == AssetLoadPhase.Running ? "Running" : "Queued ").Append("  ")
                    .Append(entry.CanLoadNow ? "can load" : "gated")
                    .Append('\n');
            }

            if (entries.Count == 0)
            {
                builder.Append("The manager holds nothing.");
            }
            else if (entries.Count > shown)
            {
                builder.Append($"... {entries.Count - shown} more entries");
            }

            _snapshot.Text = builder.ToString().TrimEnd('\n');
        }

        DemoAssetLoaderFactory? factory = ResolveFactory();

        if (factory is null)
        {
            _started.Text = "The factory resolves with the services.";
            return;
        }

        int[] started = factory.StartedRows;
        string order = started.Length == 0
            ? "No fetch has started yet."
            : "Start order: "
              + string.Join(' ', started.Select(static row => row.ToString("00", CultureInfo.InvariantCulture)));

        _started.Text =
            $"Fetches {factory.FetchCount} started, {factory.InFlight} in flight now, "
            + $"gate {DemoAssetLoaderFactory.MaxConcurrentFetchesPerHost} per host.\n{order}";
    }

    private static string DescribePriority(int priority) => priority switch
    {
        AssetLoadPriority.Visible => "Visible   ",
        AssetLoadPriority.Background => "Background",
        _ => priority.ToString(CultureInfo.InvariantCulture),
    };

    private IAssetLoadManager? ResolveManager() =>
        _manager ??= _list.Host?.Services?.GetService<IAssetLoadManager>();

    private DemoAssetLoaderFactory? ResolveFactory() =>
        _factory ??= _list.Host?.Services?.GetServices<IAssetLoaderFactory>()
            .OfType<DemoAssetLoaderFactory>()
            .FirstOrDefault();

    /// <summary>Re-reads the readouts from the frame being painted.</summary>
    private void RefreshReadouts()
    {
        // The first measurable pass establishes the InView baseline without calling a change
        // handler, so the priority half of a row is read from the frame being painted rather than
        // only from the handler - the same reason the effective-in-view page refreshes there.
        for (int row = 0; row < RowCount; row++)
        {
            if (_images[row] is null)
            {
                continue;
            }

            RefreshRow(row);
        }

        RefreshSummary();
        RefreshSnapshot();
    }

    private void ScrollRows(int rows)
    {
        _list.ScrollBy(0f, rows * (RowHeight + RowSpacing));
        SetStatus(rows > 0
            ? "Scrolled down: the rows that came on screen were re-prioritised as Visible."
            : "Scrolled up: the rows that left the viewport were re-prioritised as Background.");
    }

    private void Jump()
    {
        _list.ScrollControlIntoView(_rowPanels[JumpRow]);
        SetStatus($"Jumped to row {JumpRow + 1}: its rows are Visible now and jump the queued Background rows.");
    }

    private void RetryFailed()
    {
        Image image = _images[FailedRow];
        string uri = _uris[FailedRow];

        image.SourceUri = null;
        image.SourceUri = uri;

        SetStatus(
            $"Row {FailedRow + 1} retried with SourceUri = null and then the URI. An unclaimed URI "
            + "fails again - the recovery path is that the failure is observable, not that it is fixed.");
    }

    private void ReloadAll()
    {
        for (int row = 0; row < RowCount; row++)
        {
            _images[row].SourceUri = null;
            _images[row].SourceUri = _uris[row];
        }

        SetStatus("Every row is Pending again, in the order the page assigned them.");
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
