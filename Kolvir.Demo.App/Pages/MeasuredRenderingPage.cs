using System.Diagnostics;
using System.Globalization;
using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The measured rendering page: what a frame costs, and the benchmark that measures it.
/// </summary>
internal sealed class MeasuredRenderingPage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public MeasuredRenderingPage(DemoUi shell) : base(shell) => Initialize(Build());

    // The timer exists only while the page is displayed, and the
    // benchmark drives the same timer and the same invalidation as the rolling readout, so CPU and
    // GL runs are comparable rather than merely similar.
    private const int BenchmarkWarmupFrames = 30;
    private const int BenchmarkMeasuredFrames = 600;
    private const int RollingFrameWindow = 120;

    // A run that stops being painted used to look like a hang: the only output is the BENCH line at
    // the end, so a window that is covered, minimised or on another workspace stalls the count and
    // says nothing, on every backend and for both damage modes. The watchdog says so instead.
    private const int BenchmarkStallCheckSeconds = 5;
    private const int BenchmarkStallSeconds = 10;
    private const int BenchmarkStallReportSeconds = 30;

    private Timer? _benchmarkWatchdog;
    private int _watchdogFrames;
    private long _watchdogStartedAt;
    private long _watchdogAdvancedAt;
    private long _watchdogReportedAt;
    private Timer? _metricsTimer;
    private Timer? _pageBenchmarkTimer;
    private bool _pageBenchmarkHandlerAdded;
    private UiHost? _pageBenchmarkHost;
    private Control? _metricsPage;
    private UiHost? _metricsHost;
    private Label? _metricsReadout;
    private Panel? _metricsSwatch;
    private Button? _measureButton;
    private Button? _damageButton;
    private bool _metricsFullDamage = true;
    private bool _measuring;
    private int _measureFrame;
    private long _measureFirst;
    private long _measureLast;
    private SKRectI _measureLastArea;
    private readonly List<double> _measureSamples = [];
    private readonly Queue<(double Milliseconds, long Timestamp)> _rollingFrames = new();
    private long _rollingUpdatedAt;
    private long _lastRecordedFrame = -1;

    private string? _graphicsPath;
    private string? _graphicsRenderer;
    private Func<float>? _scaleProvider;

    /// <summary>
    /// The measured-rendering page: the live presentation path, a rolling frame-time readout and a
    /// fixed benchmark, all driven by one 15 ms timer that runs only while the page is displayed.
    /// </summary>
    /// <remarks>
    /// The benchmark invalidates the window (or a small swatch, in partial mode) for the same
    /// number of frames under either backend, so a CPU run and a GL run are comparable. The frame
    /// time is <see cref="UiHost.LastFrameTime"/>, which is the host's paint only; a GL run's
    /// <c>SwapBuffers</c> happens after that and is not in the number, which the README states.
    /// </remarks>
    private Panel Build()
    {
        var card = Card();
        _metricsPage = card;

        _metricsReadout = new Label("frame: waiting for the first paint")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 22f,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        };

        _measureButton = new Button($"Measure {BenchmarkMeasuredFrames} frames")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        _measureButton.AddClickHandler(_ => StartMeasurement());

        _damageButton = new Button("Damage: full")
        {
            Dock = XDock.Left,
            AutoSize = true,
            Style = ButtonStyle.Secondary,
            Margin = new Thickness(0f, 0f, 8f, 0f),
        };
        _damageButton.AddClickHandler(_ => ToggleDamage());

        var buttons = Row(32f);
        buttons.Controls.Add(_measureButton);
        buttons.Controls.Add(_damageButton);

        // The partial-damage target is deliberately separate from the readout: repainting this
        // swatch every frame costs about nothing, and it is the area the partial numbers describe.
        _metricsSwatch = new SurfacePanel
        {
            Dock = XDock.Left,
            Width = 180f,
            Height = 180f,
            BackColor = new SKColor(0x2F, 0x6F, 0x8F),
            Border = Framed(8f),
        };

        var swatchRow = Row(180f);
        swatchRow.Controls.Add(_metricsSwatch);

        var caveat = new Label(
            "One 15 ms timer (about 66 Hz) drives both the rolling readout and the benchmark; it runs only while this page is " +
            "displayed. The damage toggle changes what is repainted: partial damage repaints only the swatch. Under GL the " +
            "damaged region goes into a retained buffer and is presented with one full-buffer blit, so a GL partial frame " +
            "reports the swatch area while the blit is outside the frame number (the BENCH line is paint only). The readout's " +
            "own label repaints about four times a second and is part of the frame it reports.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            WrapText = true,
            Height = 84f,
            Font = Theme.CaptionFont,
            ForeColorRole = ThemeColor.MutedForeground,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Thickness(0f, 12f, 0f, 0f),
        };

        card.Controls.Add(_metricsReadout);
        card.Controls.Add(buttons);
        card.Controls.Add(swatchRow);
        card.Controls.Add(caveat);

        // 15 ms is below the Windows 15.625 ms system-timer period, so the backend really uses its
        // high-resolution timer: the loop runs at about 66 Hz and 630 frames take about ten
        // seconds. A 16.67 ms interval would be rounded up to 31.25 ms on this backend.
        _metricsTimer = new Timer(TimeSpan.FromMilliseconds(15.0));
        _metricsTimer.AddTickHandler(_ => OnMetricsTick());

        // The marquee rule: the timer exists only while this page is on screen. Leaving the page
        // stops it, and returning starts it and paints once immediately.
        card.AddDisplayedChangedHandler(_ =>
        {
            if (card.IsDisplayed)
            {
                _metricsTimer.Start();
                OnMetricsTick();
            }
            else
            {
                _metricsTimer.Stop();
            }
        });

        OnHostAvailable(card, host =>
        {
            _metricsHost = host;
            host.AddFramePaintedHandler(_ => OnMetricsFramePainted(host));
        });

        return card;
    }

    private void OnMetricsTick()
    {
        if (_metricsHost is null || _metricsPage is not { IsDisplayed: true })
        {
            return;
        }

        if (_metricsFullDamage)
        {
            _metricsHost.Invalidate();
        }
        else
        {
            _metricsSwatch?.Invalidate();
        }
    }

    private void StartMeasurement()
    {
        if (_measuring || _metricsPage is not { IsDisplayed: true })
        {
            return;
        }

        _measuring = true;
        _measureFrame = 0;
        _measureFirst = 0;
        _measureLast = 0;
        _measureLastArea = SKRectI.Empty;
        _measureSamples.Clear();

        if (_measureButton is not null)
        {
            _measureButton.Enabled = false;
            _measureButton.Text = "Measuring...";
        }

        if (_metricsReadout is not null)
        {
            _metricsReadout.Text = $"measuring {BenchmarkMeasuredFrames} frames after {BenchmarkWarmupFrames} warm-up...";
        }

        SetStatus("Benchmark running - the page is invalidated on every tick; nothing else changes.");

        StartBenchmarkWatchdog();
    }

    private void ToggleDamage()
    {
        _metricsFullDamage = !_metricsFullDamage;

        if (_damageButton is not null)
        {
            _damageButton.Text = _metricsFullDamage ? "Damage: full" : "Damage: partial";
        }

        SetStatus(_metricsFullDamage
            ? "Full damage: every tick invalidates the whole window."
            : "Partial damage: every tick invalidates only the 180 x 180 swatch.");
    }

    private void OnMetricsFramePainted(UiHost host)
    {
        // A page benchmark can add its own handler while the measured-rendering page's handler is
        // still registered; both fire for the same frame, so count each frame once.
        if (host.FrameCount == _lastRecordedFrame)
        {
            return;
        }

        _lastRecordedFrame = host.FrameCount;

        double milliseconds = host.LastFrameTime.TotalMilliseconds;
        long timestamp = Stopwatch.GetTimestamp();

        _rollingFrames.Enqueue((milliseconds, timestamp));
        while (_rollingFrames.Count > RollingFrameWindow)
        {
            _rollingFrames.Dequeue();
        }

        if (_measuring)
        {
            _measureFrame++;

            // The glyph atlas and shader programs are uploaded lazily on the first frames, so the
            // warm-up is discarded rather than averaged into a number it would poison.
            if (_measureFrame > BenchmarkWarmupFrames)
            {
                if (_measureSamples.Count == 0)
                {
                    _measureFirst = timestamp;
                }

                _measureSamples.Add(milliseconds);
                _measureLast = timestamp;
                _measureLastArea = host.LastFrameArea;

                if (_measureSamples.Count >= BenchmarkMeasuredFrames)
                {
                    FinishMeasurement(host);
                }
            }

            return;
        }

        long now = Environment.TickCount64;
        if (now - _rollingUpdatedAt < 250)
        {
            return;
        }

        _rollingUpdatedAt = now;
        UpdateMetricsReadout(host);
    }

    private void UpdateMetricsReadout(UiHost host)
    {
        if (_metricsReadout is null || _rollingFrames.Count == 0)
        {
            return;
        }

        var frames = _rollingFrames.ToArray();
        var values = frames.Select(frame => frame.Milliseconds).OrderBy(value => value).ToArray();
        double mean = values.Average();
        double p95 = Percentile(values, 0.95);
        double worst = values[^1];
        double last = frames[^1].Milliseconds;

        double seconds = frames.Length > 1
            ? Stopwatch.GetElapsedTime(frames[0].Timestamp, frames[^1].Timestamp).TotalSeconds
            : 0;
        double framesPerSecond = seconds > 0 ? (frames.Length - 1) / seconds : 0;

        double scale = _scaleProvider?.Invoke() ?? 1f;
        var client = host.ClientSize;
        var area = host.LastFrameArea;

        _metricsReadout.Text =
            $"last {last:0.0} | mean {mean:0.0} | p95 {p95:0.0} | worst {worst:0.0} ms | " +
            $"{framesPerSecond:0} fps | area {area.Width} x {area.Height} px | " +
            $"client {client.Width:0} x {client.Height:0} @ {scale:0.00}x | " +
            $"damage {(_metricsFullDamage ? "full" : "partial")}";
    }

    private void FinishMeasurement(UiHost host)
    {
        _measuring = false;
        _pageBenchmarkTimer?.Stop();
        StopBenchmarkWatchdog();

        var values = _measureSamples.OrderBy(value => value).ToArray();
        double mean = values.Average();
        double p50 = Percentile(values, 0.50);
        double p95 = Percentile(values, 0.95);
        double max = values[^1];

        double seconds = _measureLast > _measureFirst
            ? Stopwatch.GetElapsedTime(_measureFirst, _measureLast).TotalSeconds
            : 0;
        double framesPerSecond = seconds > 0 ? values.Length / seconds : 0;

        double scale = _scaleProvider?.Invoke() ?? 1f;
        var client = host.ClientSize;
        string mode = _graphicsPath ?? "cpu (unknown)";
        string renderer = _graphicsRenderer ?? "none";
        string damage = _metricsFullDamage ? "full" : "partial";

        // One machine-readable line per run. Numbers use the invariant culture so a comma decimal
        // separator on the desktop that produced them cannot change what a script reads.
        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"BENCH mode=\"{mode}\" renderer=\"{renderer}\" size={client.Width:0}x{client.Height:0} " +
            $"scale={scale:0.00} damage={damage} frames={values.Length} warmup={BenchmarkWarmupFrames} " +
            $"p50={p50:0.000} p95={p95:0.000} max={max:0.000} mean={mean:0.000} fps={framesPerSecond:0.0} " +
            $"area={_measureLastArea.Width}x{_measureLastArea.Height}");

        Console.Error.WriteLine(line);

        if (_metricsReadout is not null)
        {
            _metricsReadout.Text =
                $"benchmark {damage}: p50 {p50:0.0} | p95 {p95:0.0} | max {max:0.0} | mean {mean:0.0} ms | " +
                $"{framesPerSecond:0.0} fps | area {_measureLastArea.Width} x {_measureLastArea.Height} px | " +
                $"client {client.Width:0} x {client.Height:0} @ {scale:0.00}x";
        }

        if (_measureButton is not null)
        {
            _measureButton.Enabled = true;
            _measureButton.Text = $"Measure {BenchmarkMeasuredFrames} frames";
        }

        SetStatus(
            $"Benchmark {damage}: p50 {p50:0.0} ms, p95 {p95:0.0} ms, max {max:0.0} ms, " +
            $"{framesPerSecond:0.0} fps. The BENCH line is on stderr.");
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 0)
        {
            return 0;
        }

        int rank = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }

    /// <summary>
    /// Starts the stall watchdog: while a measurement is in progress it checks every five seconds
    /// whether the completed-frame count has advanced, and reports to stderr when it has not for ten.
    /// A normal run never prints anything from here, and a run that finishes stops it.
    /// </summary>
    private void StartBenchmarkWatchdog()
    {
        long now = Environment.TickCount64;
        _watchdogStartedAt = now;
        _watchdogAdvancedAt = now;
        _watchdogReportedAt = long.MinValue / 2;
        _watchdogFrames = _measureFrame;

        if (_benchmarkWatchdog is null)
        {
            _benchmarkWatchdog = new Timer(TimeSpan.FromSeconds(BenchmarkStallCheckSeconds));
            _benchmarkWatchdog.AddTickHandler(_ => OnBenchmarkWatchdogTick());
        }

        _benchmarkWatchdog.Start();
    }

    private void StopBenchmarkWatchdog() => _benchmarkWatchdog?.Stop();

    private void OnBenchmarkWatchdogTick()
    {
        if (!_measuring)
        {
            StopBenchmarkWatchdog();
            return;
        }

        long now = Environment.TickCount64;
        if (_measureFrame != _watchdogFrames)
        {
            _watchdogFrames = _measureFrame;
            _watchdogAdvancedAt = now;
            return;
        }

        long stalled = now - _watchdogAdvancedAt;

        // Repeat at most every 30 s, so a window that stays covered for minutes does not spew.
        if (stalled < BenchmarkStallSeconds * 1000L ||
            now - _watchdogReportedAt < BenchmarkStallReportSeconds * 1000L)
        {
            return;
        }

        _watchdogReportedAt = now;
        int total = BenchmarkWarmupFrames + BenchmarkMeasuredFrames;
        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"BENCH stalled: {_measureFrame}/{total} frames after {(now - _watchdogStartedAt) / 1000}s " +
            $"(no frames for {stalled / 1000}s)"));
    }

    /// <summary>
    /// The same 630-frame measurement on whatever page is showing, for pages whose content is
    /// heavier than the measured-rendering page's own swatch. The page is invalidated in full on
    /// every tick, and the frame time is still <see cref="UiHost.LastFrameTime"/>.
    /// </summary>
    private void StartPageBenchmark()
    {
        if (_measuring)
        {
            return;
        }

        var host = Root.Host ?? _metricsHost;
        if (host is null)
        {
            // Not attached yet; the Shown handler that calls this runs before the first layout on
            // some backends, so say so rather than silently measuring nothing.
            Console.Error.WriteLine("BENCH skipped: the window is not attached to a host yet");
            return;
        }

        _measuring = true;
        _metricsFullDamage = true;
        _measureFrame = 0;
        _measureFirst = 0;
        _measureLast = 0;
        _measureLastArea = SKRectI.Empty;
        _measureSamples.Clear();
        _pageBenchmarkHost = host;

        if (!_pageBenchmarkHandlerAdded)
        {
            host.AddFramePaintedHandler(_ => OnMetricsFramePainted(host));
            _pageBenchmarkHandlerAdded = true;
        }

        if (_pageBenchmarkTimer is null)
        {
            _pageBenchmarkTimer = new Timer(TimeSpan.FromMilliseconds(15.0));
            _pageBenchmarkTimer.AddTickHandler(_ => OnPageBenchmarkTick());
        }

        _pageBenchmarkTimer.Start();

        if (_metricsReadout is not null)
        {
            _metricsReadout.Text = $"measuring {BenchmarkMeasuredFrames} frames after {BenchmarkWarmupFrames} warm-up...";
        }

        SetStatus("Benchmark running - the page is invalidated in full on every tick.");

        StartBenchmarkWatchdog();
    }

    private void OnPageBenchmarkTick()
    {
        if (!_measuring)
        {
            _pageBenchmarkTimer?.Stop();
            return;
        }

        (_pageBenchmarkHost ?? Root.Host)?.Invalidate();
    }

    /// <summary>Shows <paramref name="message"/> in the footer's status line.</summary>
    /// <param name="message">The text to show.</param>
    /// <remarks>
    /// Public so a host can report something the sample UI did not cause itself, such as a second
    /// launch being handed to the running window.
    /// </remarks>
    /// <summary>
    /// Publishes the live presentation path for the footer and the measured-rendering page. The
    /// backend only knows it once the window really exists, so the harness calls this from
    /// <see cref="Window.AddShownHandler"/> rather than before <c>Show</c>.
    /// </summary>
    /// <param name="path"><c>gl</c> or <c>cpu (...)</c>, from the Win32 backend.</param>
    /// <param name="renderer">The vendor and renderer string, or null on the CPU path.</param>
    public void SetGraphicsInfo(string path, string? renderer)
    {
        _graphicsPath = path;
        _graphicsRenderer = renderer;

        if (Shell.GraphicsReadout is not null)
        {
            Shell.GraphicsReadout.Text = string.IsNullOrWhiteSpace(renderer)
                ? $"gfx: {path}"
                : $"gfx: {path} - {renderer}";
            Shell.GraphicsReadout.Visible = true;

            // Toggling visibility does not re-run layout - the control explains why in
            // Control.OnChildVisibleChanged - so the footer has to be laid out explicitly or
            // the newly visible label keeps the zero-width slot it had while hidden.
            Shell.GraphicsReadout.Parent?.PerformLayout();
        }
    }

    /// <summary>The live scale factor, for the page's readout; the harness supplies it from the window.</summary>
    public void SetScaleProvider(Func<float> scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        _scaleProvider = scale;
    }

    /// <summary>
    /// Starts the measured-rendering page's benchmark once that page is displayed. This is what
    /// <c>--benchmark</c> calls, so a script can capture the BENCH line without a pointer; the
    /// button on the page is the same entry point.
    /// </summary>
    /// <param name="partialDamage">
    /// True to measure the partial-damage row. The <c>--partial-damage</c> switch passes this so a
    /// script can capture all four rows without a pointer, exactly as the on-page toggle does.
    /// </param>
    public void StartBenchmark(bool partialDamage = false)
    {
        if (_metricsPage is { IsDisplayed: true })
        {
            if (partialDamage && _metricsFullDamage)
            {
                ToggleDamage();
            }

            StartMeasurement();
            return;
        }

        StartPageBenchmark();
    }
}
