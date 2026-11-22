using System.Globalization;
using Kolvir.Backend.Headless;
using Kolvir.DragDrop;
using Kolvir.Multiline;
using SkiaSharp;
using XDock = Kolvir.DockStyle;

namespace Kolvir.Demo;

/// <summary>
/// The drag and drop lifetime page: the wrapper counts a drag produces.
/// </summary>
internal sealed class DragLifetimePage : DemoPage
{
    /// <summary>Builds the page, once, for the life of the window.</summary>
    /// <param name="shell">The window the page is shown in.</param>
    public DragLifetimePage(DemoUi shell) : base(shell) => Initialize(Build());

    // The counts come from the feature's own diagnostic (Win32DropReferenceTrace, reached through
    // InternalsVisibleTo) and the collection is forced
    // here, in the demo, because that is the only way to pull a wrapper's finalizer into the middle
    // of a drag: the library must not garbage collect on an application's behalf.
    private CheckBox? _dragLifetimeCollect;
    private Label? _dragLifetimeCurrent;
    private Label? _dragLifetimeSequence;
    private Label? _dragLifetimeCounts;
    private Label? _dragLifetimeVerdict;
    private Label? _dragLifetimePayload;
    private TextBox? _dragLifetimeLog;
    private readonly List<int> _dragLifetimeSamples = [];
    private readonly List<(string Time, string Text)> _dragLifetimeEvents = [];
#if WINDOWS
    // The sampler that writes these is Win32-only while the page that reads them is shared, so the
    // fields are conditional: without this guard a --no-incremental build on Linux and macOS sees
    // reads with no writes and reports CS0649 for each of them, which looks like seven dead fields.
    private int _dragLifetimeMinimum;
    private bool _dragLifetimeFell;
    private int _dragLifetimeFallFrom;
    private int _dragLifetimeFallTo;
    private string? _dragLifetimeFallEvent;
    private IntPtr _dragLifetimePointer;
    private string? _dragLifetimeLastEvent;
#endif
    private int _dragLifetimeDrops;
    private string? _dragLifetimeFirstPayload;
    private string? _dragLifetimeSecondPayload;


    /// <summary>
    /// Page 56: the Win32 drag/drop reference-count defect, measured rather than described.
    /// </summary>
    /// <remarks>
    /// The shell owns the dragged data object and lends the drop target an <c>IDataObject*</c> for
    /// the duration of a drag. A wrapper created over that borrowed pointer believes it owns the
    /// reference and releases it later, so the live count falls - during a drag if a collection
    /// pulls the finalizer forward, or on the second conversion of the same pointer, which is what
    /// the <c>Drop</c> sample shows. This page samples the count at every OLE entry point and states
    /// the criterion: it must never fall.
    /// </remarks>
    private Panel Build()
    {
        var card = Card();
        card.Controls.Add(Heading("Drag/drop lifetime"));

        // Says, on every platform, whether the numbers below mean anything here.
#if WINDOWS
        const string platformNote =
            "Win32 only. macOS and GTK4 hand a drag to the feature by a different mechanism, so this " +
            "page cannot measure them and shows no samples there.";
#else
        const string platformNote =
            "Informational on this platform. The defect is Win32 specific - the drag/drop feature uses " +
            "a different mechanism here - so no reference count is sampled and no verdict is reached.";
#endif

        card.Controls.Add(new Label(platformNote)
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 32f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            Margin = new Thickness(0f, 0f, 0f, 6f),
        });

        card.Controls.Add(new Label(
            "Drag files from Explorer into the box below, then drag a text selection (select some text " +
            "in a browser or Notepad and drag it): the two sources hand over data objects with " +
            "different reference counts. Drop something different the second time - the second drop is " +
            "what shows the over-release's other symptom, a payload that is stale or gone.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 48f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        });

        _dragLifetimeCollect = new CheckBox("Collect during drag")
        {
            Dock = XDock.Top,
            Checked = true,
            AutoSize = true,
            Margin = new Thickness(0f, 0f, 0f, 4f),
        };
        card.Controls.Add(_dragLifetimeCollect);

        card.Controls.Add(new Label(
            "With this on, every drag-over forces a blocking gen-2 collection, which is what pulls the " +
            "wrapper's finalizer into the middle of the drag instead of leaving it to chance. Turn it " +
            "off to measure the other failure: the second conversion of the same pointer, at Drop. A " +
            "drop that never arrives at all is the same defect further along - the object was already gone. " +
            "With collection off the count can still look flat, because the shell takes its own reference " +
            "for the drop at the moment the wrapper cache gives the borrowed one back: the two cancel, and " +
            "the dip hides in the arithmetic instead of disappearing.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 64f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            Margin = new Thickness(0f, 0f, 0f, 8f),
        });

        var zone = new SurfacePanel
        {
            Dock = XDock.Top,
            Height = 76f,
            Border = Framed(8f),
            Padding = new Thickness(8f),
            Margin = new Thickness(0f, 0f, 0f, 10f),
        };
        zone.Controls.Add(new Label("Drop files or text here")
        {
            Dock = XDock.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = ThemeColor.MutedForeground,
        });

        // The zone is the measured area; the window itself already takes drops, so a drag that
        // lands on the navigation list is sampled too, but only this control forces a collection.
        zone.AllowDrop = true;
        zone.AddDragEnterHandler((_, e) => OnDragLifetimeEnter(e));
        zone.AddDragOverHandler((_, e) => OnDragLifetimeOver(e));
        zone.AddDragDropHandler((_, e) => OnDragLifetimeDrop(e));
        card.Controls.Add(zone);

        _dragLifetimeCurrent = Readout("Current sample: none yet");
        _dragLifetimeSequence = Readout("This drag: -");
        _dragLifetimeCounts = Readout("Minimum this drag: -   Drops this session: 0");
        _dragLifetimeVerdict = new Label(
            "Waiting: the reference count must never fall during a drag.")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 34f,
            WrapText = true,
            Font = Theme.EmphasizedFont,
            Margin = new Thickness(0f, 4f, 0f, 4f),
        };
        _dragLifetimePayload = new Label("Drop 1: -   Drop 2: -")
        {
            Dock = XDock.Top,
            AutoSize = false,
            Height = 34f,
            WrapText = true,
            Font = Theme.CaptionFont,
            ForeColor = ThemeColor.MutedForeground,
            Margin = new Thickness(0f, 0f, 0f, 4f),
        };
        card.Controls.Add(_dragLifetimeCurrent);
        card.Controls.Add(_dragLifetimeSequence);
        card.Controls.Add(_dragLifetimeCounts);
        card.Controls.Add(_dragLifetimeVerdict);
        card.Controls.Add(_dragLifetimePayload);

        _dragLifetimeLog = new TextBox
        {
            Dock = XDock.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            Font = Theme.CaptionFont,
            Text = "Drag something over the window: the last 12 samples appear here.",
        };
        card.Controls.Add(_dragLifetimeLog);

#if WINDOWS
        // One subscription for the life of the window: the trace belongs to the feature, not to the
        // page, and the samples keep arriving while another page is shown.
        Win32DropReferenceTrace.Sampled += OnDragReferenceSampled;
#endif

        return card;
    }

    /// <summary>Builds one line of the lifetime page's readouts.</summary>
    private static Label Readout(string text) => new(text)
    {
        Dock = XDock.Top,
        AutoSize = false,
        Height = 18f,
        Font = Theme.CaptionFont,
        TextAlign = ContentAlignment.BottomLeft,
    };

    /// <summary>Records a sample the feature took, and rebuilds what the page shows.</summary>
#if WINDOWS
    private void OnDragReferenceSampled(Win32DropReferenceSample sample)
    {
        // A new drag starts with the count the shell handed over, so the baseline is per drag: two
        // different data objects need not have the same count, and comparing across drags would
        // report a fall that never happened.
        if (sample.Event == Win32DropReferenceTrace.EnterEvent || _dragLifetimeSamples.Count == 0)
        {
            _dragLifetimeSamples.Clear();
            _dragLifetimeMinimum = sample.Count;
            _dragLifetimeFell = false;
            _dragLifetimeFallFrom = 0;
            _dragLifetimeFallTo = 0;
            _dragLifetimeFallEvent = null;
            _dragLifetimePointer = sample.DataObject;
        }
        else if (sample.Count < _dragLifetimeSamples[^1] && !_dragLifetimeFell)
        {
            // The criterion is "never falls", so the comparison is against the sample before it
            // rather than against the first one: the count may legitimately be acquired by the
            // shell mid-drag, but it must never be given up.
            _dragLifetimeFell = true;
            _dragLifetimeFallFrom = _dragLifetimeSamples[^1];
            _dragLifetimeFallTo = sample.Count;
            _dragLifetimeFallEvent = sample.Event;
        }

        _dragLifetimeSamples.Add(sample.Count);
        _dragLifetimeMinimum = Math.Min(_dragLifetimeMinimum, sample.Count);
        _dragLifetimeLastEvent = sample.Event;

        // The Drop sample is a second conversion of the pointer DragEnter already converted, and it
        // is where the wrapper cache releases the incoming reference synchronously. Marking it keeps
        // that decrement attributable without a screenshot of the debugger.
        var secondConversion =
            sample.Event == Win32DropReferenceTrace.DropEvent &&
            sample.DataObject == _dragLifetimePointer;

        var line =
            $"{Timestamp()}  {sample.Event,-9} ref {sample.Count,-2}  0x{sample.DataObject.ToInt64():X}" +
            (secondConversion ? "  second conversion of this pointer" : string.Empty);

        _dragLifetimeEvents.Add((Timestamp(), line));
        TrimDragLifetimeLog();
        RefreshDragLifetimePage();
    }
#endif

    /// <summary>Appends the payload the drop zone just read to the sample line it belongs to.</summary>
    private void AppendDragLifetimePayload(string payload)
    {
        if (_dragLifetimeEvents.Count == 0)
        {
            return;
        }

#if WINDOWS
        if (_dragLifetimeLastEvent is not ("DragEnter" or "Drop"))
        {
            return;
        }
#endif

        var last = _dragLifetimeEvents[^1];
        _dragLifetimeEvents[^1] = (last.Time, $"{last.Text}  {payload}");
        TrimDragLifetimeLog();
        RefreshDragLifetimePage();
    }

    private void TrimDragLifetimeLog()
    {
        while (_dragLifetimeEvents.Count > 12)
        {
            _dragLifetimeEvents.RemoveAt(0);
        }
    }

    /// <summary>Runs on every drag-over the zone receives, so the collection lands mid-drag.</summary>
    private void OnDragLifetimeOver(DragEventArgs e)
    {
        e.Effect = DragDropEffects.Copy;

        if (_dragLifetimeCollect?.Checked == true)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
        }

        RefreshDragLifetimePage();
    }

    private void OnDragLifetimeEnter(DragEventArgs e)
    {
        e.Effect = DragDropEffects.Copy;
        AppendDragLifetimePayload(DescribeDragPayload(e.Data));
    }

    private void OnDragLifetimeDrop(DragEventArgs e)
    {
        e.Effect = DragDropEffects.Copy;

        var payload = DescribeDragPayload(e.Data);
        _dragLifetimeDrops++;

        if (_dragLifetimeDrops == 1)
        {
            _dragLifetimeFirstPayload = payload;
        }
        else
        {
            _dragLifetimeSecondPayload = payload;
        }

        SetStatus($"Dropped {payload} on the lifetime page.");
        AppendDragLifetimePayload(payload);
    }

    /// <summary>Says what a dropped payload holds, in one line, for the log and the readouts.</summary>
    private string DescribeDragPayload(DataObject data)
    {
        var files = data.GetFiles();
        if (files.Length > 0)
        {
            return $"files: {string.Join(", ", files.Select(Path.GetFileName))}";
        }

        var text = data.GetText();
        return string.IsNullOrEmpty(text) ? "nothing this sample can read" : $"text: \"{Shell.Preview(text)}\"";
    }

    /// <summary>
    /// Rebuilds the readout, the verdict and the log. The verdict states the criterion in words
    /// because the numbers alone are easy to read as "fine".
    /// </summary>
    private void RefreshDragLifetimePage()
    {
        if (_dragLifetimeCurrent is not { } current ||
            _dragLifetimeSequence is not { } sequenceLabel ||
            _dragLifetimeCounts is not { } countsLabel ||
            _dragLifetimeVerdict is not { } verdict ||
            _dragLifetimePayload is not { } payloadLabel ||
            _dragLifetimeLog is not { } log)
        {
            return;
        }

        var sequence = string.Join(' ', _dragLifetimeSamples);
        current.Text = _dragLifetimeSamples.Count == 0
            ? "Current sample: none yet"
            : CurrentSampleText();
        sequenceLabel.Text = _dragLifetimeSamples.Count == 0
            ? "This drag: -"
            : $"This drag: {sequence}";
        countsLabel.Text = DragLifetimeCountsText();

        if (_dragLifetimeSamples.Count == 0)
        {
            verdict.Text = "Waiting: the reference count must never fall during a drag.";
            verdict.ForeColor = InheritedValue.Instance;
            verdict.ForeColor = ThemeColor.MutedForeground;
        }
        else
        {
            ApplyDragLifetimeVerdict(verdict, sequence);
        }

        payloadLabel.Text =
            $"Drop 1: {_dragLifetimeFirstPayload ?? "-"}   " +
            $"Drop 2: {_dragLifetimeSecondPayload ?? "-"}";

        if (_dragLifetimeSecondPayload is not null)
        {
            var secondDrop = _dragLifetimeSecondPayload == "nothing this sample can read"
                ? "FAIL - the second drop carried nothing; the object was gone by then."
                : _dragLifetimeSecondPayload == _dragLifetimeFirstPayload
                    ? "Second drop: same payload as the first - fine if you dragged the same thing, FAIL if you did not."
                    : "Second drop: PASS - it carried its own payload.";

            payloadLabel.Text += $"   {secondDrop}";
        }

        log.Text = _dragLifetimeEvents.Count == 0
            ? "Drag something over the window: the last 12 samples appear here."
            : string.Join(Environment.NewLine, _dragLifetimeEvents.Select(entry => entry.Text));
    }

    /// <summary>Builds the counts line, whose minimum is the one number Win32 has to measure.</summary>
    private string DragLifetimeCountsText()
    {
#if WINDOWS
        return
            $"Minimum this drag: {(_dragLifetimeSamples.Count == 0 ? "-" : _dragLifetimeMinimum.ToString(CultureInfo.InvariantCulture))}" +
            $"   Drops this session: {_dragLifetimeDrops}";
#else
        return $"Minimum this drag: -   Drops this session: {_dragLifetimeDrops}";
#endif
    }

    /// <summary>States the criterion against the samples the drag produced.</summary>
    private void ApplyDragLifetimeVerdict(Label verdict, string sequence)
    {
#if WINDOWS
        if (_dragLifetimeFell)
        {
            verdict.Text =
                $"FAIL - the reference count fell from {_dragLifetimeFallFrom} to {_dragLifetimeFallTo} " +
                $"at {_dragLifetimeFallEvent ?? "a drag event"}: {sequence}. The count must never fall " +
                $"while the shell is still using the object, and it did.";
            verdict.ForeColor = new SKColor(0xFFD32F2F);
        }
        else
        {
            verdict.Text =
                $"PASS - the count held at {_dragLifetimeSamples[0]} for the whole drag: {sequence}.";
            verdict.ForeColor = new SKColor(0xFF2E7D32);
        }
#else
        // No sample can arrive where the sampler does not exist, so the page never reaches a verdict
        // here; this repeats the resting line rather than inventing a second one to read.
        verdict.Text = "Waiting: the reference count must never fall during a drag.";
        verdict.ForeColor = InheritedValue.Instance;
        verdict.ForeColor = ThemeColor.MutedForeground;
#endif
    }

    private static string Timestamp() =>
        DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>The last sample in words, which is the only line a reader has to trust.</summary>
    private string CurrentSampleText()
    {
#if WINDOWS
        return $"Current sample: {_dragLifetimeSamples[^1]} at {Win32DropReferenceTrace.Last?.Event}";
#else
        return "Current sample: not sampled on this platform";
#endif
    }
}
