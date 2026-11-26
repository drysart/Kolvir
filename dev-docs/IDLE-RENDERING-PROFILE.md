# Where an idle frame's time goes (Windows, software raster)

An owner report said the **Measured rendering** page had slowed from about 0.8 ms to about 2.5 ms per
frame "at some point", with no difference from GPU acceleration. This document is the answer: a
per-stage breakdown of an idle frame, a sweep of every commit in which the page's own benchmark
exists, and what it does and does not say.

Short version:

- **The time is pixels, not a regression.** On the machine measured, a full-damage idle frame costs
  **3.05 ms p50** and has cost 2.9-3.2 ms at *every* commit since the page and its benchmark first
  existed (2026-11-09). Clearing the surface (1.16 ms) and walking the tree (1.97 ms) are the whole
  number; no single stage moved.
- **Most of the 3x is the display's scale factor.** The same window and the same page at 1.00 scale
  paints 1.06 ms today. The host is at 200%, so the frame rasterises 2560 x 1640 device pixels rather
  than 1280 x 820; the two pixel-bound stages move by exactly 4x. A 0.8 ms frame is what a 1x-sized
  buffer costs, and no commit in this repository can produce one at 2x.
- **There is no GPU on the measured host**, so "GPU acceleration makes no difference" is expected
  rather than informative: every accelerated tier fails and both configurations are the GDI CPU
  path. `--no-hardware-acceleration` is therefore not a constraint here, it is the only path.

## 1. Method

| What | Value |
| --- | --- |
| Page | `--page measured-rendering` |
| Harness | `--benchmark` (the page's own 30 warm-up + 600 measured frames, one line to stderr) |
| Backend | `--no-hardware-acceleration` (and, separately, with acceleration requested) |
| Appearance | `--light` |
| Window | the demo's design client size, 1280 x 820 logical at 2.00 scale = 2560 x 1640 device |
| Host | Hyper-V VM, Intel Core i9-13900K, Windows 11 Pro 26300; "Microsoft Hyper-V Video" and "Microsoft Remote Display Adapter", 3840 x 2160 at 200% |
| Runs | median of 5 for each row; the spread is the min-max of those 5 |

Every number below is `UiHost.LastFrameTime` - the host's paint - unless it says otherwise, because
that is the number the page's readout shows and the number the owner quoted. It excludes the Win32
backend's `BitBlt` to the window, which is reported separately.

The stage split came from temporary instrumentation: timestamps around each stage of
`UiHost.PaintCore`, around each control's own `Render` span, and around `BitBlt`, plus
`GC.GetAllocatedBytesForCurrentThread()` for allocations and a count of `Measure`/`Render` calls. It
was gated on an environment variable, is not part of this change, and has been removed.

### What could not be measured headless

`--render` cannot drive the benchmark. `HeadlessApplicationBackend.Run()` drains queued work and
returns - there is no message loop - so the 15 ms timer that invalidates the page never reaches 630
frames, and `HeadlessWindowBackend` never paints unless someone asks it to. The bisect below is
therefore scripted against the windowed path: build, launch, wait for the `BENCH` line, kill.

`--benchmark` on any page *other* than the measured-rendering page is also inert in this build: it
calls `StartPageBenchmark`, which finds no host attached yet and prints
`BENCH skipped: the window is not attached to a host yet`. The "control page" idea therefore needs
the fix noted in section 6 rather than a flag.

## 2. Where an idle frame's time goes

One instrumented run, 600 frames, full damage, 2.00 scale, 1280 x 820 logical. Numbers are
milliseconds per frame. The instrument costs about 0.02 ms of the 3.25 total.

Both p50 numbers in this report are the same shape: the 2.5 ms the owner reports and the 3.05 ms
measured here differ by 20%, which is what two host states cost rather than two commits, and the
repository's own 2026-11-09 record for a software rasteriser is 3.157 ms, which is where this host
lands. Nothing below depends on which of 2.5 or 3.05 is used - the sweep compares like with like on
one machine.

| Stage | ms/frame | Share |
| --- | ---: | ---: |
| Frame-starting hooks | 0.000 | 0% |
| Whole-tree layout (`PerformLayout`) | 0.002 | 0% |
| Deferred arrangements | 0.001 | 0% |
| Draw total | 3.233 | 99% |
| - clear (`SKCanvas.DrawPaint` over the whole surface) | 1.160 | 36% |
| - menu bar | 0.108 | 3% |
| - root tree | 1.965 | 60% |
| - overlays | 0.000 | 0% |
| **Host paint (`LastFrameTime`)** | **3.250** | 100% |

Outside the reported number, and on the same frames:

| Stage | ms/frame |
| --- | ---: |
| `BitBlt` of the dirty area to the window DC (GDI, 2560 x 1640) | 1.331 |
| `Render` (make-current + host paint) | 3.311 |

So the whole idle frame at the window is about **4.6 ms**: 3.25 ms painting into the DIB and 1.33 ms
blitting it out. A frame spends no time laying out - nothing asks for a layout while idle - and 29
controls are painted per frame (1.1 content measurements, all cache hits).

Per control type, own time (excluding children), same frames:

| Control | ms/frame | Painted | Allocation |
| --- | ---: | ---: | ---: |
| `SurfacePanel` | 0.943 | 3 | 1,448 B |
| `Button` | 0.341 | 6 | 6,033 B |
| `ListBox` (the navigation list) | 0.309 | 1 | 7,304 B |
| `Label` | 0.193 | 6 | 42,671 B |
| `MenuBarView` | 0.095 | 1 | 672 B |
| `TextBox` | 0.059 | 1 | 928 B |
| `ScrollBar` | 0.049 | 1 | 952 B |
| `SplitContainer` | 0.030 | 1 | 352 B |
| `Panel` | 0.005 | 9 | 0 B |

The clear is pure memory bandwidth: 16.8 MB of premultiplied BGRA written at about 14 GB/s, which is
what the machine's memory does. The BitBlt is the same 16.8 MB read and written again. Neither can be
made cheaper without painting less, and an idle *application* paints far less than this page does on
purpose (section 6).

Allocation totals **61,019 B per frame** - about 4 MB/s of gen-0 garbage at 66 Hz - and it is
attributed almost entirely to the page's own long wrapped caveat label: 40-50 KB of it comes from
that single 526-character `Label`, because `TextRenderer` re-wraps and re-slices the text on every
paint instead of remembering the layout. The remaining five labels together account for about 1 KB.

## 3. When it got slower: it did not

The measured-rendering page and the `BENCH` line first exist in `8001265` (2026-11-09,
"feature/gpu-acceleration-win32"), so the measurable window is that commit to `ace0d1c`. Before it
there is no page and no benchmark to run, and `--page` was a position in the list rather than a page
id until 2026-11-14, so any earlier commit would have to grow a harness before it could be measured.

Sweep: every 8th first-parent commit across the range, each one checked out clean (`git clean -xdf`),
built Release from an empty `obj`, and measured twice. The table lists commit, date, and the two p50
values in milliseconds.

| Commit | Date | p50 (ms) | Subject |
| --- | --- | --- | --- |
| `8001265` | 2026-11-09 | 3.120, 3.336 | gpu-acceleration-win32 (page introduced) |
| `93e3568` | 2026-11-10 | 3.147, 3.160 | chore/rename-solution |
| `4c04f9e` | 2026-11-12 | 3.016, 3.048 | feature/panel-no-chrome |
| `f7dcb8e` | 2026-11-14 | 3.203, 3.253 | feature/borders-phase2 |
| `b9fc484` | 2026-11-15 | 3.163, 3.200 | feature/layout-measure-cost |
| `c1823d8` | 2026-11-15 | 3.032, 3.038 | perf/one-text-build |
| `dd64ca2` | 2026-11-18 | 2.980, 3.033 | feature/observables |
| `370c63b` | 2026-11-20 | 3.082, 3.096 | feature/window-move-notification |
| `f9f79c0` | 2026-11-22 | 3.031, 3.181 | feature/union-analyzers |
| `c0c4762` | 2026-11-24 | 2.982, 3.103 | feature/ime-win32 |
| `919461d` | 2026-11-25 | 2.939, 2.943 | popup-placement |
| `92a519c` | 2026-11-25 | 2.906, 2.914 | write-popups-plan |
| `ace0d1c` | 2026-11-25 | 2.988, 3.001 | fix-win32-d3d12-resize-glitch |

Median across the 26 runs: **3.03 ms**. Range: 2.906-3.336. The curve is flat; if anything the last
three points are 4-6% below the first one, inside the run-to-run spread. There is no step and no
drift, so the popup feature, the `SynchronizationContext` installed by `feature/ui-sync-context`, and
the eight popup layers are all innocent of the frame cost: none of them moved it.

The repository's own record agrees. `DEVELOPER-GUIDE.md` records a CPU full-damage `p50` of **2.917 ms**
for this exact page on an RTX 5090 at 2.00 scale, measured 2026-11-09 - the same commit family as the
first row above. The page has never been a sub-millisecond frame on the CPU path at 2x.

## 4. What the 0.8 ms is

Two measurements say it is not a commit:

1. **The frame scales with device pixels, and this host is at 200%.** Forcing the process DPI-unaware
   (a temporary edit to `app.manifest`, reverted) makes the app paint a 1280 x 820 buffer at
   1.00 scale. Same page, same content, same build:

   | Stage | 1.00 scale | 2.00 scale | Ratio |
   | --- | ---: | ---: | ---: |
   | clear | 0.290 | 1.160 | 4.0x |
   | menu bar | 0.065 | 0.108 | 1.7x |
   | root tree | 0.805 | 1.965 | 2.4x |
   | **host paint** | **1.174** | **3.250** | **2.8x** |
   | `BitBlt` | 0.318 | 1.331 | 4.2x |
   | allocation | 61,019 B | 61,019 B | 1.0x |

   Measured p50 at 1.00: 1.061 and 1.064 ms (two runs); the instrumented run above reports 1.078.
   The pixel-bound stages move exactly with area; the tree moves less than that because text shaping
   and control walking are per-logical-glyph and do not quadruple.

   A 0.8 ms frame is therefore a 1x-shaped frame of this page, or the same page in a slightly smaller
   window. It is not a shape any commit in this repository can produce at 2x - the clear alone is
   1.16 ms at 2x before a single glyph is drawn.

2. **Nothing else in the history records a number that low either.** The sub-millisecond rows this
   repository has ever written down are accelerated ones: GL full-damage 0.487 ms on an RTX 5090,
   Metal full-damage 0.883 ms on an M4 for the heavy images page, Metal full-damage 0.642 ms on a
   paravirtual GPU. The CPU rows are 1.85 ms (M4), 2.26 ms (paravirtual) and 2.92 ms (RTX 5090).

Two mundane explanations fit the report equally well and cannot be separated from the outside: the
session that produced 0.8 ms was at 100% scaling (a different monitor or a different remote session),
or it was an accelerated path that has since stopped working on this host.

### The adapter this run used

None. With acceleration requested the demo reports:

```
gfx: cpu (d3d12: no hardware adapter; vulkan: no driver installed (-9); no gl interface; gdi: no wgl context (error 2000))
```

and the benchmark line the same, with `p50=3.007`. This is a Hyper-V VM whose only display adapters
are "Microsoft Hyper-V Video" and "Microsoft Remote Display Adapter", so there is no vendor ICD,
no hardware D3D12 adapter and no WGL context; Skia rasterises on the CPU into a GDI DIB. That is also
why "GPU acceleration makes no difference": on this host both settings end up in the same place. A
GL/Metal comparison needs a machine with a real adapter - the numbers in `DEVELOPER-GUIDE.md` were
taken on one.

## 5. Median and spread, all rows on this host

Debug build at `ace0d1c`, five runs each, `--light`, `--no-hardware-acceleration`, 600 frames after
30 warm-up.

| Configuration | p50 (ms, 5 runs) | Median | p95 (range) | max (range) |
| --- | --- | ---: | --- | --- |
| full damage, 1280 x 820 @ 2.00 | 3.069, 3.317, 3.002, 3.050, 3.007 | **3.050** | 4.10-4.84 | 7.14-9.48 |
| partial damage, 180 x 180 @ 2.00 | 0.207, 0.203, 0.206, 0.209, 0.208 | **0.207** | 0.26-0.29 | 0.62-4.26 |
| full damage, 1280 x 820 @ 1.00 (DPI-unaware) | 1.061, 1.064 | **1.062** | 1.46-1.51 | 5.26-6.75 |

Run-to-run spread on the full-damage row is about 2% around the median, so a 5% difference between
two configurations is not a difference on this instrument; the ones quoted above are 2.8x and larger.

## 6. Options, with what each is worth

Nothing is shipped in this change: there is no regression to revert, and none of these changes the
frame by more than this instrument's noise floor.

1. **Remember wrapped text layout.** A `Label` re-wraps and re-slices its text on every paint, so the
   page's one long caveat label allocates 40-50 KB per frame. Caching the laid-out lines keyed on
   (text, font generation, width, wrap) would take the frame from 61 KB to about 20 KB. The label's
   own paint is 0.19 ms of a 3.05 ms frame, so the expected time win is under 0.05 ms - below the
   spread - and the win is garbage and gen-0 pause frequency, not p50. Expected saving stated
   honestly: allocation, not time.
2. **Do not re-lay out single-line text either.** A fast path in `TextRenderer.Draw` for text with no
   line breaks and no wrapping saved 600-2,000 B per control in a local experiment (total 61,019 to
   57,539 B) and nothing measurable in time. Not worth the code on its own; it is worth folding into
   option 1 if that cache is ever written.
3. **The all-or-nothing idle repaint is the page's own design.** The page invalidates the whole
   window every 15 ms to measure full damage; that is what makes the clear and the `BitBlt` the two
   biggest numbers. An application that repaints a caret or a swatch paints 0.207 ms (the partial
   row) instead of 3.05 ms, which is the partial-damage machinery working as intended. If the owner
   wants an idle application to cost less, the answer is partial damage at the caller, not a change
   in the paint path.
4. **`--benchmark` on a page other than `measured-rendering` does nothing.** `StartPageBenchmark`
   runs from the window's shown handler before a page has a host, so it prints
   `BENCH skipped: the window is not attached to a host yet`. Fixing that (defer to the first
   painted frame) would make the control-page measurement possible from the command line without a
   pointer, which is what this investigation wanted and had to do with temporary instrumentation.

## 7. Reproducing this

```
dotnet build Kolvir.slnx
src/demo/Kolvir.Demo.App/bin/Debug/net10.0-windows/KolvirDemoApp.exe ^
    --page measured-rendering --benchmark --no-hardware-acceleration --light
# add --partial-damage for the swatch row
```

The `BENCH` line goes to stderr; the process stays open after the 630th frame, so a script should
read the line and then end it. Keep the window at the design size and unobstructed: a covered or
minimised window stops being painted and the run stalls (the page's watchdog says so after 10 s).
Leave the machine otherwise idle; one competitor run moves the median by a few percent.
