# Where an idle frame's time goes, and whether it got slower

The **Measured rendering** page was reported as "hanging around like 2.5 ms per frame, when
previously it was around 0.8 ms". The owner has since confirmed the two figures came from **different
machines at different window sizes**, which voids the comparison: this page's frame is proportional
to the pixels it paints, so its milliseconds mean nothing outside the machine, display scale and
window size that produced them.

So this document is not a defence of a number. It is four things:

1. **What an idle frame spends its time on**, per stage and per control, measured on one machine
   (section 2), including what the most expensive control is actually doing (section 2.2).
2. **Whether the cost moved across the history**, in relative terms, sampled on every commit that
   touches the render path (section 3), with the resolution of that method stated so the claim can
   be judged.
3. **Why a raw-millisecond readout makes a window change look like a regression**, and the smallest
   change that would stop it (section 4), with the one frame-size change this repository has made.
4. **Whether layout and painting could be parallelised** (section 5), which is mostly an argument
   that the 15x already available from painting less beats anything threads can offer.

Every absolute number below was measured on **one** machine: a Hyper-V VM, Intel Core i9-13900K,
Windows 11 Pro 26300, remote display at 3840 x 2160 and 200% scaling, **no hardware adapter**
(section 6). Within that machine, in one configuration, the numbers compare. They are not claims
about any other machine, and no cross-machine comparison appears in this document.

## 1. Method

| What | Value |
| --- | --- |
| Page | `--page measured-rendering` |
| Harness | `--benchmark` (the page's own 30 warm-up + 600 measured frames, one line to stderr) |
| Backend | `--no-hardware-acceleration` |
| Appearance | `--light` |
| Window | the demo's design client size: 1280 x 820 logical at 2.00 = 2560 x 1640 device |
| Statistic | `p50` of `UiHost.LastFrameTime` per run; median of runs per sample |
| Runs | 5 per configuration for the tables in section 2; 3 per commit for the sweep in section 3 |

`LastFrameTime` is the host's paint - the number the page's readout shows - and excludes the Win32
backend's `BitBlt`, which is reported separately.

The stage split came from temporary instrumentation: timestamps around each stage of
`UiHost.PaintCore`, around each control's own `Render` (clip, background, own paint, border), around
`BitBlt`, plus `GC.GetAllocatedBytesForCurrentThread()` and a count of `Measure`/`Render` calls. It was
gated on an environment variable, added about 0.02 ms to the frame, and is not part of this change.

**What could not be measured headless.** `--render` cannot drive the benchmark:
`HeadlessApplicationBackend.Run()` drains queued work and returns, so the page's 15 ms timer never
reaches 630 frames and `HeadlessWindowBackend` never paints unprompted. The sweep is therefore
scripted against the windowed path: build clean, launch, wait for the `BENCH` line, end the process.
`--benchmark` on any page other than `measured-rendering` is also inert in this build - it prints
`BENCH skipped: the window is not attached to a host yet` from `StartPageBenchmark` - so a second
page cannot be measured from the command line without fixing that first (section 5.3, item 4).

## 2. Where an idle frame's time goes

One instrumented run, 600 frames, full damage, 2.00 scale, 1280 x 820 logical. Milliseconds per
frame; the host paint total includes the instrument's own ~0.02 ms.

| Stage | ms/frame | Share |
| --- | ---: | ---: |
| Frame-starting hooks | 0.000 | 0% |
| Whole-tree layout (`PerformLayout`) | 0.002 | 0% |
| Deferred arrangements (`FlushArrangements`) | 0.001 | 0% |
| Draw total | 3.336 | 100% |
| - clear (`SKCanvas.DrawPaint` over the whole surface) | 1.215 | 36% |
| - menu bar | 0.106 | 3% |
| - root tree | 2.015 | 60% |
| - overlays | 0.000 | 0% |
| **Host paint (`LastFrameTime`)** | **3.349** | 100% |

Measured separately in the same session at the same configuration, and outside that number:

| Stage | ms/frame |
| --- | ---: |
| `BitBlt` of the painted area to the window DC (GDI) | 1.331 |
| `Render` (make-current plus host paint) | 3.311 |

So the whole idle frame at the window is about **4.6 ms**: ~3.3 ms painting into the DIB and ~1.3 ms
copying it out. An idle frame does **no layout at all** - nothing asks for one - and paints 29
controls.

### 2.1 Per control: self and inclusive

These are the figures the earlier version of this report published without saying which they were.
They are **self** times: the control's own clip, background fill, `OnPaint` and border, excluding
everything its children spent. `Inclusive` is the same control including its whole subtree, averaged
over that type's instances. Both are per frame; `n` is how many of that type are painted per frame.

| Control | n | self | inclusive | of the self: clip | background | own paint | border |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `SurfacePanel` | 3 | 0.965 | 0.464 | 0.037 | 0.799 | 0.000 | 0.128 |
| `Button` | 6 | 0.355 | 0.059 | 0.110 | 0.203 | 0.039 | 0.003 |
| `ListBox` | 1 | 0.321 | 0.372 | 0.007 | 0.156 | 0.092 | 0.066 |
| `Label` | 6 | 0.193 | 0.032 | 0.002 | 0.003 | 0.187 | 0.001 |
| `MenuBarView` | 1 | 0.096 | 0.096 | 0.004 | 0.005 | 0.086 | 0.001 |
| `TextBox` | 1 | 0.060 | 0.060 | 0.011 | 0.018 | 0.014 | 0.018 |
| `ScrollBar` | 1 | 0.048 | 0.048 | 0.004 | 0.001 | 0.043 | 0.000 |
| `SplitContainer` | 1 | 0.030 | 1.627 | 0.000 | 0.001 | 0.029 | 0.000 |
| `Panel` | 9 | 0.007 | 0.752 | 0.002 | 0.003 | 0.000 | 0.001 |
| **sum** | **29** | **2.075** | not summable | | | | |

Three things to read off it:

- **Self times partition the frame.** The column sums to 2.075 ms against the 2.121 ms of root tree
  (2.015) plus menu bar (0.106); the 2% difference is the instrument. So these rows are a breakdown
  of the draw, not a sample of it - and layout is 0.00 of it, for the reason above. The per-type
  figures are within the frame's **tree bucket** (plus the menu bar row), not fractions of the whole
  frame: the clear and the blit are separate and are not attributed to any control.
- **Inclusive times must not be summed.** They nest: `SplitContainer` is 1.627 because both halves of
  the window are under it, and the nine `Panel`s average 0.752 because one of them is the root. Use
  inclusive to answer "how heavy is this subtree", self to answer "what does this control cost".
- **`Panel` and `Label` are leaves that look free and are not.** `Panel`'s self is 0.007 ms and its
  inclusive average 0.752: it is a pure container. `Label`'s self is 0.193 and its inclusive 0.032
  because a label has no children - the columns invert for leaves.

### 2.2 What `SurfacePanel` is doing for 0.97 ms

`SurfacePanel` (`src/demo/Kolvir.Demo.App/SurfacePanel.cs`) is the sample's card: a `Panel` whose
`Border` is `SimpleBorder.Thin.WithCornerRadius(8f)` and whose background defaults to
`ThemeColor.Surface`. It has no `OnPaint` at all - `paint=0.000` above is not a rounding artefact.
Its 0.965 ms of self time is:

| What it is doing | ms/frame | Where |
| --- | ---: | --- |
| Background fill | 0.799 | `Control.OnPaintBackground` → `Painter.Fill(canvas, rect, backColor, Border.Corners)` |
| Border ring | 0.128 | `SimpleBorder.Paint` → `PaintRing` |
| Antialiased rounded clip | 0.037 | `SimpleBorder.ClipTo` → `SKPathBuilder` → `canvas.ClipPath(..., antialias: true)` |
| Own paint | 0.000 | none |

So it is not rendering an offscreen Skia surface, not compositing, not scaling, and not
re-rasterising a cached layer. It is **two large antialiased rounded rectangles** - a fill and a
hairline ring - over a card that covers most of the window, plus an antialiased path clip that
`Border.NarrowsClip` makes every rounded control pay for on every frame. Three are painted per frame;
two are large.

That is the single biggest item in the tree, and it is the first thing to attack if the frame is to
get cheaper (section 5.3). This measurement does not say whether the antialiased *clip* is what makes
the fill expensive or the fill itself; separating those two is the obvious next experiment.

### 2.3 Scaling, allocation, and what is not there

On this machine the same page at 1.00 scale (process made DPI-unaware for one experiment, then
reverted) measures **1.06 ms p50**, with clear 0.290, tree 0.805, menu 0.065, blit 0.318. Between the
two scales the pixel-bound stages move by exactly 4x, the tree by 2.4x, and allocation not at all
(61 KB both ways). That is why a millisecond figure cannot travel between configurations: the clear
and the blit are memory bandwidth over `logical width x height x scale x scale x 4` bytes.

`GC.GetAllocatedBytesForCurrentThread()` over the paint reports **61,022 B per frame** - about
4 MB/s of gen-0 garbage at 66 Hz. Per-label probes put 40-50 KB of it in a single control: the page's
526-character wrapped caveat label, because `TextRenderer.Draw` calls `BuildTextLines`, which
`Substring`s the source once per measurement step and allocates a `TextLine` per line, on every
paint. The other five labels together allocate about 1 KB.

Nothing else is in the frame: no layout, 1.1 content measurements per frame (all cache hits in
`Control.Measure`'s epoch cache), no overlays, no animations on this page.

## 3. Did it regress? A relative sweep of the render path

The page and its `BENCH` line first exist in `8001265` (2026-11-09), and `--page` took a position
rather than a page id until 2026-11-14, so that commit is the start of the measurable range. Between
it and `ace0d1c` there are 91 first-parent commits, of which **44 touch the render path** by
basename - `UiHost`, `Control`, `TextRenderer`, `Font`, `Painter`, `SimpleBorder`, `Border`, the nine
control types this page paints, `Win32WindowBackend`, `MeasuredRenderingPage` - excluding pure
renames. All 44 were measured: every one is a chance for the paint to change, so the sampling is
dense exactly where a regression would live rather than uniform across commits that touch nothing.

Each sample: `git clean -xdf`, check out, Release build from an empty `obj`, three benchmark runs,
median of their p50. Nothing else differed between samples - same page, same flags, same window
(1280 x 820 @ 2.00 on every run, and the run's own `BENCH` line says so), same 600 frames after 30
warm-up, same machine, runs serial.

| Commit | Date | p50 per run (ms) | Median | Δ vs previous |
| --- | --- | --- | ---: | ---: |
| `8001265` | 11-09 | 3.048, 3.090, 3.108 | 3.090 | - |
| `e35a5e0` | 11-11 | 3.048, 3.115, 3.118 | 3.115 | +0.8% |
| `037afd6` | 11-11 | 3.057, 3.089, 3.090 | 3.089 | -0.8% |
| `82400b7` | 11-12 | 3.036, 3.062, 3.068 | 3.062 | -0.9% |
| `8e50d06` | 11-12 | 3.114, 3.241, 3.267 | 3.241 | +5.8% |
| `4c04f9e` | 11-12 | 3.115, 3.132, 3.286 | 3.132 | -3.4% |
| `743f8d4` | 11-12 | 3.147, 3.247, 3.280 | 3.247 | +3.7% |
| `1f5cad5` | 11-13 | 3.107, 3.109, 3.181 | 3.109 | -4.3% |
| `ec5a57b` | 11-13 | 3.060, 3.100, 3.310 | 3.100 | -0.3% |
| `ff91b3b` | 11-14 | 3.085, 3.117, 3.125 | 3.117 | +0.5% |
| `7904254` | 11-14 | 3.108, 3.239, 3.286 | 3.239 | +3.9% |
| `f7dcb8e` | 11-14 | 3.276, 3.388, 3.483 | 3.388 | +4.6% |
| `5329368` | 11-14 | 3.136, 3.296, 3.297 | 3.296 | -2.7% |
| `208c16a` | 11-14 | 3.159, 3.237, 3.240 | 3.237 | -1.8% |
| `8a81839` | 11-15 | 3.085, 3.134, 3.136 | 3.134 | -3.2% |
| `32ab8ca` | 11-15 | 3.097, 3.099, 3.153 | 3.099 | -1.1% |
| `b9fc484` | 11-15 | 3.084, 3.117, 3.131 | 3.117 | +0.6% |
| `c20233c` | 11-15 | 3.063, 3.106, 3.156 | 3.106 | -0.4% |
| `74fb8a7` | 11-15 | 2.915, 2.927, 3.009 | 2.927 | -5.8% |
| `0942ac2` | 11-15 | 2.925, 2.939, 2.979 | 2.939 | +0.4% |
| `32110b6` | 11-15 | 2.916, 3.054, 3.334 | 3.054 | +3.9% |
| `7a78097` | 11-15 | 2.978, 3.025, 3.063 | 3.025 | -0.9% |
| `304e48b` | 11-15 | 2.983, 3.020, 3.170 | 3.020 | -0.2% |
| `f963b57` | 11-15 | 2.910, 2.910, 2.937 | 2.910 | -3.6% |
| `c1823d8` | 11-15 | 2.895, 2.998, 3.061 | 2.998 | +3.0% |
| `859e5cf` | 11-15 | 2.909, 2.998, 3.088 | 2.998 | 0.0% |
| `19524e2` | 11-17 | 3.002, 3.020, 3.075 | 3.020 | +0.7% |
| `2605c02` | 11-20 | 2.916, 2.922, 2.928 | 2.922 | -3.2% |
| `4e89273` | 11-20 | 2.955, 2.967, 2.998 | 2.967 | +1.5% |
| `ba8fe31` | 11-20 | 2.924, 3.014, 3.073 | 3.014 | +1.6% |
| `bf998a1` | 11-20 | 2.926, 2.926, 2.930 | 2.926 | -2.9% |
| `370c63b` | 11-20 | 2.857, 2.857, 2.890 | 2.857 | -2.4% |
| `5b057c7` | 11-21 | 2.883, 2.903, 2.975 | 2.903 | +1.6% |
| `57ea535` | 11-22 | 2.900, 2.920, 3.097 | 2.920 | +0.6% |
| `90cc752` | 11-22 | 3.037, 3.083, 3.107 | 3.083 | +5.6% |
| `3c2578d` | 11-23 | 3.176, 3.300, 3.351 | 3.300 | +7.0% |
| `b57a0aa` | 11-23 | 3.064, 3.257, 3.260 | 3.257 | -1.3% |
| `c0c4762` | 11-24 | 2.957, 2.970, 3.021 | 2.970 | -8.8% |
| `b302982` | 11-24 | 3.127, 3.232, 3.257 | 3.232 | +8.8% |
| `ab36c2d` | 11-25 | 2.982, 3.004, 3.076 | 3.004 | -7.1% |
| `e33afcd` | 11-25 | 2.968, 2.972, 3.031 | 2.972 | -1.1% |
| `9f003fe` | 11-25 | 3.034, 3.099, 3.130 | 3.099 | +4.3% |
| `3ee9d41` | 11-25 | 3.024, 3.090, 3.241 | 3.090 | -0.3% |
| `9ed2745` | 11-25 | 3.118, 3.216, 3.260 | 3.216 | +4.1% |

### The claim, in relative terms

**Median idle frame cost on this machine stayed between 2.86 and 3.39 ms across all 44 commits that
touch the render path, from the page's introduction to `ace0d1c` - within -7.5% / +9.6% of the
3.09 ms centre - and the second half of the range averages 3.042 ms against the first half's
3.162 ms, so the net drift over the range is -3.8%.** No commit made this frame 30% more expensive,
and the perf work in `perf/one-text-build`, `feature/text-measure-cache` and the two popup-adjacent
fixes left it slightly cheaper rather than dearer.

### The resolution, and whether it would have caught the report

Repeat runs of one build differ by **3.1% at the median and 13.7% at worst**; the same commit measured
in two separate sweeps (different checkouts, different minutes) gave medians of 3.23 and 3.09 ms, 4.5%
apart. Three runs per commit therefore resolve a commit's cost to roughly **5%**, which is what the
sweep actually shows: the largest adjacent-commit change anywhere in the 44 samples is **+8.8%**
(`b302982`), and it comes immediately after a **-8.8%** (`c0c4762`) - a bounce between two samples,
not a step.

**Would this method have seen a 30% regression? Yes.** A 30% step is 6x the commit-level resolution
and 3x the worst single-run spread, and it would have to land on a commit that touches the render
path - all of which were measured, with the commits around them as controls. **Did it? No.** The
biggest adjacent change is 8.8% and it is cancelled by its neighbour, and the range as a whole drifts
*down*.

A uniform sweep of every 8th first-parent commit (13 samples, 2 runs each, *all* commits rather than
only render-path ones) was run first and says the same thing: 2.91-3.34 ms, no step. It is the
control for anything the by-basename selection might have missed - a change to the page list, say,
which `DemoUi.cs` covers and which the 44-sample set excludes.

## 4. What the readout reports, and the smallest honest fix

The page's readout today is:

```
last 3.0 | mean 3.1 | p95 4.3 | worst 7.1 ms | 66 fps | area 1280 x 820 px | client 1280 x 820 @ 2.00x | damage full
```

and the machine-readable line it writes is `BENCH ... size=1280x820 scale=2.00 ... area=1280x820`.

Two problems, and they are the reason a different machine's number could read as a regression here:

1. **`area` is not in pixels.** `UiHost.LastFrameArea` is documented as logical pixels, and the
   readout labels it `px`. On the run above the frame painted 4,198,400 device pixels and the line
   says 1,049,600.
2. **Nothing normalises.** There is no device size, no megapixel count and no per-pixel figure in
   either the readout or the `BENCH` line, so two runs at different window sizes or display scales
   cannot be compared without multiplying two fields on a calculator.

The smallest change that makes the readout honest - proposed, not made here, because it changes the
demo's UI rather than fixing a defect:

```csharp
// MeasuredRenderingPage.UpdateMetricsReadout
int deviceWidth = (int)MathF.Round(area.Width * scale);
int deviceHeight = (int)MathF.Round(area.Height * scale);
double megapixels = deviceWidth * (double)deviceHeight / 1_000_000.0;

_metricsReadout.Text =
    $"last {last:0.0} | mean {mean:0.0} | {mean / megapixels:0.00} ms/Mpx | " +
    $"p95 {p95:0.0} | worst {worst:0.0} ms | {framesPerSecond:0} fps | " +
    $"area {area.Width} x {area.Height} logical ({deviceWidth} x {deviceHeight} device, " +
    $"{megapixels:0.00} Mpx) | client {client.Width:0} x {client.Height:0} @ {scale:0.00}x | " +
    $"damage {(_metricsFullDamage ? "full" : "partial")}";
```

and the same three fields appended to the `BENCH` line in `FinishMeasurement`:

```csharp
$"device={deviceWidth}x{deviceHeight} mpx={megapixels:0.000} ms_per_mpx={mean / megapixels:0.000} "
```

Two caveats so the number is not oversold. `ms/Mpx` is **not** invariant across sizes for this page -
measured 0.73 ms/Mpx at 2.00 scale against 1.01 at 1.00 on this machine, because per-control work is
per logical control and does not scale with pixels - so it is a *comparator*, not a normaliser: two
runs at different sizes with the same `ms/Mpx` are the same code painting more pixels, not a speed
change. And it does nothing about display scale on its own; that is why the device size goes on the
same line.

### The one frame-size change in this repository

For completeness, because a size change is the other way a millisecond figure can move without code
changing. The demo's design client size was measured at both ends of the history:

| Date | Commit | Design size (logical) | Device at 2.00 | Painted |
| --- | --- | --- | --- | ---: |
| 2026-10-28 | `907422a` | 1040 x 760 | 2080 x 1520 (measured) | 3.16 Mpx |
| 2026-10-29 | `0dc1a5a` (PR #1) | 1280 x 760 | 2560 x 1520 | 3.89 Mpx |
| 2026-10-31 | `e615601` (PR #8) | 1280 x 820 | 2560 x 1640 (measured at `ace0d1c`) | 4.20 Mpx |

That is **+33% in area**, all of it before the measured page existed, and nothing has changed it
since. The scale factor has no such history: the manifest has declared `PerMonitorV2` since the first
commit, the Win32 backend has read `GetDpiForWindow` and scaled the canvas since the first window
harness, `ApplicationOptions.DpiAwareness` has defaulted to `PerMonitor` since it was introduced, and
no commit persists or restores a window size. The 200% in the `BENCH` line is this session's display.

## 5. Could layout and painting be parallelised?

Short answer: **painting, no; and the effort is aimed at the wrong problem.** Layout is already free,
the 15x that is actually on the table comes from painting less, and the arithmetic on a thread-split
tree walk comes out negative once the split has to be composited back. The rest of this section is
the argument.

### 5.1 Where the pass is serial by construction

- **One canvas over one raster surface.** `Win32WindowBackend.Render()` takes `_skia` (created in
  `EnsureBuffer`/`RestorePresentation` as `SKSurface.Create(imageInfo, _dibBits, width * 4)` - a
  raster surface wrapping the GDI DIB), does `canvas.Scale(_scale)`, and hands that one canvas to
  `UiHost.Paint(canvas, full)`. There is no second surface and no device context to divide.
- **Paint order and clip state live in the canvas save-stack, carried by a single traversal.**
  `Control.Render(SKCanvas)` does `Save`/`Translate`, then either `canvas.ClipRect` or
  `Border.ClipTo` (which for a rounded border is `SKPathBuilder` → `ClipPath` with antialiasing),
  paints its background, selection and content, calls `RenderChildren` in z-order, paints its
  border, and `Restore`s. Two subtrees cannot share that stack without interleaving, and the layering
  (`canvas.SaveLayer` for opacity or an image filter) composites a subtree as a unit.
- **`UiHost.IsPainting` is a process-wide static** (`_paintDepth`, a `static int` incremented with
  `Interlocked`) precisely because a GPU surface can re-enter the paint through the software path.
  Any worker thread painting would make that flag - and the controls that consult it to avoid
  invalidating during a paint - report on the wrong thread's work.
- **The frame's published state is single-threaded by design:** `_dirty`/`_dirtyAll` are handed over
  once per frame in `PaintCore`, and `LastFrameTime`/`LastFrameArea`/`FrameCount` are the benchmark's
  unit - `FrameCount` is what the page counts frames with.
- **The present is serial and after the paint:** `Win32WindowBackend.OnPaint` calls `Render()` and
  then `Blit(hdc, area)` (`BitBlt`) on the UI thread, then `EndPaint`. The HWND's DC belongs to the UI
  thread.
- **SkiaSharp gives a raster surface no executor.** `SKSurface.Create(imageInfo, bits, rowBytes)`
  has no `GrContext` and no task group exposed, and Skia's CPU rasteriser is not parallel by default.
  Any intra-frame parallelism has to come from *our* splitting into several surfaces and compositing
  them - which is the arithmetic below.

### 5.2 What could be parallelised, and the honest ceiling

All figures from section 2, at 2.00 scale: tree walk 2.02 ms, clear 1.22, blit 1.33.

| Candidate | Best case | What it costs | Verdict |
| --- | --- | --- | --- |
| Banded rasterisation: N horizontal bands, each its own surface, composited | 4-way perfect split of 2.02 → 0.51 ms | 4 extra surfaces ≈ 16.8 MB per frame; the composite is a full-surface copy of 16.8 MB, which the `BitBlt` measurement puts at ~1.3 ms | **Net loss**, even at perfect scaling |
| Independent sibling subtrees to their own surfaces | 2-way split of this tree (nav column 0.32 vs card area ~1.6) → 1.6 ms | one extra surface for the page area plus a composite of that area, ≥0.9 ms at the measured fill rate; wall time is set by the largest subtree, not the count of them | **Worse than today**; Amdahl caps it at 20% before costs |
| Off-thread present | overlap the 1.33 ms `BitBlt` with idle time | needs a second DIB plus `UpdateLayeredWindow` (the popup paths already do this) and a redesign of the present path; still cannot overlap the paint that produces the pixels | ~1.3 ms of a 4.6 ms frame, high risk, does not touch the 3.3 ms |
| Parallel *layout* | 0.00 ms measured | - | **Nothing to win**; layout is not in the frame at all |

The load-bearing comparison is with what already exists: **partial damage on this same page costs
0.207 ms against 3.05 ms full, ~15x**, and it is not threads - it is a 180 x 180 swatch instead of a
1280 x 820 window. A four-way perfect split of the tree walk bottoms out near 0.5 ms plus a
composite; it cannot approach 15x, and on this arithmetic it does not even beat doing nothing.

### 5.3 The cheaper alternatives, ranked by measured evidence

1. **Paint less: partial damage - 15x, already implemented.** `UiHost.PartialUpdates` is on for Win32
   (`Win32WindowBackend` sets it true), `Control.Invalidate` adds a rectangle to `_dirty`, and the
   partial row measures 0.207 ms against 3.05. The measured page's 3.05 ms is a *deliberate* full
   repaint: its 15 ms timer calls `UiHost.Invalidate()` with no argument, which sets `_dirtyAll`. That
   is the benchmark doing its job, and it is why this number is not the library's idle cost.
2. **Stop re-filling what did not change - 0.8-1.0 ms of 3.3, medium risk.** `SurfacePanel` spends
   0.80 ms per frame filling two large antialiased rounded rectangles that have not changed, and
   `Button` backgrounds add 0.20. Caching a static card's painted content (an `SKPicture` or an
   offscreen surface keyed on size, theme and appearance) removes most of that. Risk: invalidation
   correctness, one extra surface per cached card, memory proportional to the visible page.
3. **Stop re-laying-out text - allocation only.** 40-50 KB per frame from one wrapped label
   (`TextRenderer.Draw` → `BuildTextLines` on every paint); the six labels' paint is 0.19 ms, under
   the noise floor, so this buys garbage and gen-0 pause frequency rather than milliseconds.
4. **Make a control page measurable, so "it is only this page" can be checked.** `--benchmark` on any
   other page prints `BENCH skipped: the window is not attached to a host yet`, because
   `StartPageBenchmark` runs from the window's shown handler before the page has a host. Deferring it
   to the first painted frame is a few lines and makes every page comparable from a script.
5. **Threads.** Negative on the arithmetic above.
6. **Decouple the present - 1.3 ms, high risk.** A second buffer and a worker that blits while the UI
   thread paints the next frame; worthwhile only if the present is ever seen as the limit, which on
   this machine it is not.

Ranked by expected gain per unit of risk: partial damage (done) ≫ static-card caching (0.8-1.0 ms,
bounded, measurable) > text layout cache (allocation) > the page-benchmark fix (no frame cost, better
measurement) > present decoupling > threads.

### 5.4 Recommendation, and the experiment that would settle it

**Do not build a threaded renderer.** On the numbers above it loses to the composite it needs, and
the same machine already demonstrates a 15x by painting a twentieth of the window instead of by
splitting the work.

If the static-card idea is worth one prototype, this is the experiment that decides it, with the same
discipline as section 3:

- **Change:** cache `SurfacePanel`'s background fill and ring for one card (an `SKPicture` recorded on
  first paint and replayed while size, theme, appearance and state are unchanged, dropped otherwise).
- **Measurement:** the page's full-damage p50 over 5 runs at 1280 x 820 @ 2.00, before and after, on
  this machine. **Decision rule:** a fall of at least 0.5 ms (≥15%) in the median, with the
  partial-damage row unchanged within its spread.
- **Correctness gate:** `--render --page measured-rendering` byte-identical before and after, plus the
  existing visual tests - a cached card that misses a repaint is a worse bug than 0.9 ms.

If that prototype does not clear the decision rule, the next thing to measure is the cost of the
antialiased `ClipPath` on rounded controls (0.037 ms here for three cards, but it forces coverage
masks over every fill inside them), which needs a measurement rather than an estimate.

## 6. What this measurement is not

- **One machine.** A Hyper-V VM with no hardware adapter: `d3d12: no hardware adapter; vulkan: no
  driver installed (-9); no gl interface; gdi: no wgl context (error 2000)`. Every accelerated tier
  fails, so both `GfxHardwareAcceleration` settings land on the same GDI CPU path, and this host
  cannot speak to what a GPU does. Every absolute figure in this document describes this machine at
  this session's 200% scale only; change is quoted as a percentage for that reason.
- **One configuration per comparison.** Section 3 varies one commit and nothing else; the 1.00 versus
  2.00 comparison in 2.3 varies one property and nothing else. No number here is compared across
  machines, display scales or window sizes.
- **A page that repaints everything on purpose.** The measured page invalidates the whole window
  every 15 ms. Its full-damage row is a full repaint by construction; the partial row is what an idle
  application that repaints something small actually costs.

## 7. Reproducing this

```
dotnet build Kolvir.slnx
src/demo/Kolvir.Demo.App/bin/Debug/net10.0-windows/KolvirDemoApp.exe ^
    --page measured-rendering --benchmark --no-hardware-acceleration --light
# add --partial-damage for the swatch row
```

The `BENCH` line goes to stderr and the process stays open after frame 630, so a script should read
the line and then end it. Keep the window at the design size and unobstructed: a covered or minimised
window stops being painted and the run stalls (the page's watchdog says so after 10 s). Leave the
machine otherwise idle - one competing build or run moves the median by several percent, which is the
resolution quoted in section 3.
