# Splitting a rounded background fill: measured, and not worth keeping

`dev-docs/IDLE-RENDERING-PROFILE.md` section 2.2 puts 0.799 ms of the measured page's frame into
`SurfacePanel`'s background fill: one antialiased path per card, per frame, from
`Control.OnPaintBackground` → `Painter.Fill(canvas, rect, backColor, Border.Corners)`. The obvious
saving is to antialias only what needs it - fill the interior with a plain rectangle and draw the four
corner regions as antialiased fills - on the reasoning that analytic-AA coverage is charged across the
whole shape while only the corners have curvature.

This document records the measurement of that idea. **It is a negative result: the split is slower
than the shape it replaces, and it was not kept.** The numbers also answer a question the profile
left open, which is what to expect from retention (§5.3 item 2): almost nothing of the cost is in the
shape or its area.

## 1. Method

Same harness, page, flags and machine as `dev-docs/IDLE-RENDERING-PROFILE.md` section 1:

```
KolvirDemoApp.exe --page measured-rendering --benchmark --no-hardware-acceleration --light
```

1280 x 820 logical at the session's 2.00 display scale (2560 x 1640 device), 30 warm-up and 600
measured frames per run, `p50` of `UiHost.LastFrameTime` per run, all runs serial on one machine.
The per-stage split came from temporary instrumentation: `Stopwatch` timestamps around
`RoundedRect.Build` and `canvas.DrawPath` inside `Painter.Fill(..., CornerRadii)`, gated on an
environment variable and removed before this branch landed.

Two candidate interiors were measured against the shipped antialiased path:

- **split1** - one non-antialiased `DrawRect` over the whole rect, plus one antialiased path holding
  the four corner regions. The interior covers the cut corners too, so this variant is only valid
  where the canvas is already clipped to the rounded shape; it is measured as the cheapest possible
  interior.
- **split5** - five non-antialiased rectangles tiling the area outside the four corner squares, in
  one non-antialiased path, plus the same corner path. This is the variant that would ship: it needs
  no clip to be visually correct, and it handles per-corner radii (each band is inset by the largest
  radius on its side).

The corner path draws, for each corner with a non-zero clamped radius, the part of its corner square
that is inside the arc: move to where the arc starts, arc to where it ends, line to the square's inner
corner, close.

### Why the two variants were measured together

Measuring each variant in its own process does not work here: the per-call average moves by up to 10%
between processes for the *same* code, because the page's nine-or-so fills per frame differ in size by
an order of magnitude and the mix shifts slightly with timing. So the two variants were made to share a
run, alternating call by call, and the run was repeated with the parity inverted. Each variant then
sees the same *set* of rectangles across the pair of runs - the expensive set in one and the cheap set
in the other - and the pair average is a mix-controlled comparison. The repeats reproduce to within
0.2%, which is what makes the numbers below quotable:

| Run | pair of calls | path total | split total |
| --- | --- | ---: | ---: |
| `ab` | path on even calls | 0.1389 | 0.0552 (on odd) |
| `ab2` | path on odd calls | 0.0438 | 0.1485 (on even) |

## 2. Per-call cost, milliseconds per `Painter.Fill(..., CornerRadii)`

Mix-controlled pair averages from the two runs above (milliseconds per call; a call is one rounded
background fill, of any size):

| What | ms/call | Share of the shipped fill |
| --- | ---: | ---: |
| Shipped: clamped radii + path build | 0.0012 | 1.3% |
| Shipped: antialiased path fill | 0.0902 | 98.7% |
| **Shipped total** | **0.0914** | 100% |
| split5: path build (interior + corners) | 0.0017 | 1.8% |
| split5: non-antialiased interior (five bands) | 0.0877 | 96.0% |
| split5: antialiased corners (one path) | 0.0125 | 13.7% |
| **split5 total** | **0.1019** | **+11.5%** |
| split1: non-antialiased interior (one rect) | 0.0878 | 96.1% |
| split1: antialiased corners | 0.0124 | 13.6% |
| **split1 total** | **0.1015** | **+11.1%** |

Three readings, and they are the whole result:

- **Turning antialiasing off over the interior saves 2.6 µs of a 90 µs fill, 2.8%.** The premise -
  that analytic AA is charged across the whole rectangle - does not hold for Skia's rasteriser on a
  solid convex path. The interior spans are blitted like a rectangle; the AA work is per edge. What
  remains is memory traffic, and a rectangle pays exactly the same traffic as the path.
- **The four corner fills cost 12.5 µs, about five times what the interior's antialiasing cost.** Four
  small antialiased paths are dearer than one large one, because what is being paid for is per-draw
  path setup (and the combination with the control's own antialiased clip), not the pixels.
- **The five-band interior (0.0877) costs the same as one rectangle (0.0878).** The decomposition
  itself is free; there is no cheaper interior to find by tiling differently.

Path construction is 1.2-1.7 µs per call, 1.3-1.8% of the fill. Across the ~9.4 rounded fills this
page paints per frame that is about 13 µs, an order of magnitude below the run-to-run spread of the
frame median (below), so caching an `SKPath` per control - which `IDLE-RENDERING-PROFILE.md`
section 5.3 item 2 permits - would not move the frame either.

## 3. Frame medians

Seven runs per variant, same page, size and scale, uninstrumented, serial:

| Variant | Runs (p50, ms) | Median | Spread |
| --- | --- | ---: | ---: |
| Shipped (antialiased path) | 2.936, 2.968, 2.972, 2.976, 3.008, 3.028, 3.132 | **2.976** | 0.196 |
| split5 | 2.966, 3.030, 3.031, 3.033, 3.039, 3.108, 3.237 | **3.033** | 0.271 |

The split is **1.9% slower**, inside the spread and in the wrong direction. The per-call accounting
predicts about +0.10 ms per frame (9.4 fills at +0.0105 ms), which the frame median cannot resolve at
this spread, and nothing like the 5% improvement the experiment was gated on.

## 4. Render diff

`--render` at 1280 x 820, light appearance, for the measured-rendering and borders pages: the split
variants produce **byte-identical PNGs** to the shipped path (equal SHA-256 across the variants on
both pages: shipped, split1 and split5 on measured-rendering, shipped and split5 on borders). At 1x
the page's card rectangles land on whole device pixels, so the non-antialiased
interior edges snap to exactly where the antialiased path edges were and the seams the split risks at
a fractional offset do not arise. The corner fills are drawn from the same geometry the path used.
That is a clean result for the render harness, but it is not evidence about 2.00 scale, which
`--render` cannot produce: it paints at 1x and has no scale argument.

## 5. Decision

The change is reverted. It fails the gate (a median improvement of at least 5%) in the wrong
direction, and it fails for a reason that also rules out the idea's other forms: the fill's cost is
memory traffic over the card's area, which any variant that still paints the card every frame pays,
and the antialiased edges are ~3% of it. Nothing in the shape - interior, corners or the path that
describes them - is worth splitting or caching. The lever the profile already identified
(§5.3: paint less - partial damage, or retaining static content) is the only one on this cost.

## 6. What this says about retention

`IDLE-RENDERING-PROFILE.md` §5.3 item 2 proposes caching a static card's painted content and §5.4 asks
for a prototype. These numbers bound what such a cache can win, and say what it cannot be justified
by:

- After the split the corner fills are 12.5 µs per card fill, about 0.12 ms per frame for this page's
  9.4 rounded fills. A card's four corners are 4 x r² = 256 logical pixels, 1,024 device pixels at
  2.00; at the page's measured fill rate (the whole-surface clear is 1.215 ms for 4.2 M device pixels,
  0.29 ns/px) that area is worth ~0.3 µs. The corners cost about 40 times their area, so the cost
  tracks the number of antialiased path draws, not the corner area.
- The same is true of the shipped single path: 90 µs per fill for a shape whose edges are a few
  thousand pixels; it is bandwidth over the interior, not coverage at the edges.
- So shape-level retention - caching the path, or retaining the geometry - has nothing left to win.
  Only retaining the *pixels* (an `SKPicture` or an offscreen surface replayed instead of repainted)
  can remove the ~1 ms per frame the cards' backgrounds cost, and that is the memory-hungry option the
  owner is weighing, not a cheap one.

## 7. Reproduction

The instrumentation and both variants were temporary and are not in this branch. To repeat it, add an
environment-gated switch in `Painter.Fill(SKCanvas, SKRect, SKColor, CornerRadii)` that draws either
`RoundedRect.Build` + `DrawPath` (shipped) or the five interior bands + corner path (split5), alternate
on a counter, and run the benchmark twice with the parity inverted, as in section 1. The per-frame
figures come from the same `BENCH` line the profile's section 7 describes.

## 8. What this change costs

Rung 1, zero cost by construction: the branch changes no compiled code, so an application that does
not use it cannot contain any of it. The `Kolvir.Minimal` fixture publishes at 3,885,568 bytes on
win-x64 from this tree (`-c Release -r win-x64 -p:EmbedSkiaSharpNative=false`), the same tree as
`develop`; the delta is **no measurable difference at 512-byte resolution**. Only win-x64 was
weighed: there is nothing platform-specific to differ.
