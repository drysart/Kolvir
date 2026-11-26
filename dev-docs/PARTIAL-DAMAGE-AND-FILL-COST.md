# Partial damage: is the window still right, and does the fill cost follow the damage?

Two questions were asked of partial damage, and this document answers both with numbers:

1. **Is a partial repaint still correct?** Put the page in a known state, damage a small
   rectangle, repaint, and compare the whole client area against a full repaint of the same state.
2. **Does the cost of `Painter.Fill` track the damaged area, or is a card's background filled in full
   even when only part of it is damaged?**

The short answers: **the window outside the damaged area is untouched in every case, and inside it
the repaint is bit-identical to a full one except where the damage edge cuts an antialiased *curve*,
where it lands within 50 of 255 on at most 23 fringe pixels.** And **the fill cost does follow the
damaged area - a quarter of the card's pixels costs 15 percent of the fill, a sixteenth 6.3 percent,
a 900 x 20 strip 1.8 percent** - because the damage region is a canvas clip applied before the tree is
walked, and Skia's scan converter fills only its bounds.

Every number below was measured on **one** machine: a Hyper-V VM, Intel Core i9-13900K, Windows 11
Pro, remote display at 3840 x 2160 and 200% scaling, no hardware adapter - the same machine and
configuration the two earlier reports used. Within it, the numbers compare. Nothing here is a claim
about another machine or another window size.

The two reports this one refers to are not on `develop` yet: `dev-docs/IDLE-RENDERING-PROFILE.md` is on
`profile-idle-rendering-win32` and `dev-docs/ROUNDED-FILL-SPLIT.md` is on `cheaper-rounded-fill`.

## 1. Method

### 1.1 The time measurements

Same harness and flags as `dev-docs/IDLE-RENDERING-PROFILE.md` section 1:

```
KolvirDemoApp.exe --page measured-rendering --benchmark --no-hardware-acceleration --light
```

1280 x 820 logical at 2.00 (2560 x 1640 device), 30 warm-up and 600 measured frames per run, `p50` of
`UiHost.LastFrameTime` per run, runs serial, three runs per configuration, median reported.

Two temporary instruments were used and both are **not in this change**:

- **A probe in `Painter.Fill`.** With `KOLVIR_FILL_PROBE` set, each of the two `Fill` overloads read
  `Stopwatch.GetTimestamp()` around its draw and one row per call - elapsed microseconds, the
  rectangle, whether it was rounded, and `SKCanvas.DeviceClipBounds` - was buffered and written to
  CSV when the benchmark finished. Two timestamp reads and one bounds query per fill is under 0.5 µs
  of a 90 µs call, and the same probe was on for every row of the curve, so the rows are comparable
  with each other. The shipped build measures 3.038 ms p50 full-damage against 2.982 ms in this
  instrumented build, which is inside the spread of the runs themselves.
- **A damage rectangle.** The page's own partial row always damages its 180 x 180 swatch, so
  `KOLVIR_DAMAGE_RECT="left,top,width,height"` made the tick invalidate a rectangle of the size
  under test instead. Every rectangle in section 3 is anchored at (246, 155) logical, which is just
  inside the page's card (236..1260 x 145..740 logical), so each one clips the same 1024 x 595
  rounded fill.

### 1.2 The pixel measurements

The windowed harness cannot compare pixels - it reports time - so the correctness comparison is a
test: `tests/core/Kolvir.Tests/Core/PartialDamageTests.cs`, in the shipped suite, on the headless
backend.

It is the same paint: `HeadlessWindowBackend.RenderTo` calls `UiHost.Paint`, the same control tree,
the same Skia rasteriser. What the test adds is a retained surface and a byte comparison:

- paint the tree full into a bitmap - the **reference**;
- paint it full again into a second bitmap - the **subject** - so the subject starts from exactly the
  reference's pixels and only the damage can be the difference;
- invalidate the case's rectangle(s) and paint again into the same bitmap, with the canvas scaled
  exactly as `Win32WindowBackend.PaintHost` scales its own;
- assert `UiHost.LastFrameArea` is the damaged rectangle, so the frame really was partial;
- compare **every** pixel of the two client areas.

Two claims are asserted apart, because they fail for different reasons. **Nothing outside the damaged
area may change** - exact, and the one that matters. **Inside**, the repaint must reproduce the full
paint, and the agreed bound per case is in the test.

The scene is a card of the sample's shape: a `Panel` filled `0xFFE8E0D0` with
`SimpleBorder.Thin.WithCornerRadius(12f).WithInsets(Thickness.Empty)` - the same frame and radius
`SurfacePanel` uses, in core rather than demo code - over a `0xFF101820` page, with one label of text
on it. The scales are 1.00, 1.25 and 2.00.

## 2. Is partial damage still correct?

Yes, with one measured exception that is explained below.

The reference is the same for every case at a given scale, which is the point: a full repaint of the
scene, hashed over the whole client area.

| Scale | Full repaint, SHA-256 of the client bitmap |
| --- | --- |
| 1.00 | `218884F7223BDD854FAEC476CACACF6947B887560CE099457E744E582BD93D84` |
| 1.25 | `DB0072AD7A988220FD8864C4EAF01B5076D46A2E82EF1391E9D98148124F9378` |
| 2.00 | `1FE0DEFCFD1558AF6B234B7FE33CA9D4598573277F383D47426055604B567437` |

| Damage case | What the rectangle cuts | 1.00 | 1.25 | 2.00 |
| --- | --- | --- | --- | --- |
| Rounded corner cut in half | the top-left arc of the card (radius 12, centre 52,42), right edge at x = 50 | **14 px differ, max delta 36** | **23 px, 22** | **16 px, 50** |
| Hairline ring along an edge | the 1 px ring on the left edge, for 40 of its 240 px of length | identical | identical | identical |
| Through the text | the first glyphs of the label, right edge mid-line | identical | identical | identical |
| Interior of the card only | flat fill, no edge, no corner, no text | identical | identical | identical |
| Outside every card | the page above the card, which no control paints | identical | identical | identical |
| Two separate rectangles | the ring strip and a card interior, disjoint - the region path, not the rectangle path | identical | identical | identical |

"Identical" means **0 pixels differ anywhere in the client area**, and the hash equals the reference's.
**Outside the damaged area 0 pixels differ in every case at every scale**, including the corner: the
repaint leaks nothing and disturbs nothing it was not asked to touch.

Each case also asserts that its rectangle really holds the geometry it names, so a case cannot pass by
damaging empty space because a control moved: the corner and ring rectangles must contain pixels that
are neither the page colour nor the card colour (the antialiased edge), the text rectangle must hold
more than twenty glyph pixels, the interior must be entirely card colour and the outside rectangle
entirely page colour.

### 2.1 The one case that is not bit-identical, and why

Where the damage edge cuts a **curved, antialiased** edge, the partially covered pixels of that arc
come back a few percent away from the coverage a full repaint gives them - in the table above, 14
pixels at 36/255 at 1.00x, 23 at 22/255 at 1.25x, 16 at 50/255 at 2.00x. They are the arc's fringe
pixels, one per row, not whole pixels changing sides; the pixel next to each is no longer an edge pixel.

The cause is Skia's antialiasing of a clipped curve, not anything in Kolvir. That was checked by
drawing the same rounded rectangle on a bare SkiaSharp canvas with no Kolvir code in the path:

| Clip | Pixels that differ from the unclipped draw, inside the clip |
| --- | --- |
| The whole surface | 0 |
| `(0,0)-(400,300)` - nothing cut | 0 |
| `(0,0)-(53,300)` - cuts the arc at x = 50 | 13 differ, worst 35 |
| `(0,0)-(52,300)` - edge at the arc's end | 4 differ, worst 1 |
| `(0,0)-(400,42)` - bottom edge at the arc's end | 2 differ, worst 20 |
| `(0,0)-(400,43)` - bottom edge past the arc | 0 |
| `(0,0)-(60,60)` - cuts only straight edges | 4 differ, worst 1 |

So a damage edge that crosses the card's *straight* edges changes at most 4 pixels by 1/255, and one
that crosses its *arc* changes up to 50/255 on the fringe because the curve is chopped at the clip and
the remainder is approximated again (`SkScan::AntiFillPath` builds its edges clipped to the clip
rectangle, and `SkScan::AAAFillPath` then walks the chopped curve - `src/core/SkScan_AntiPath.cpp` and
`SkScan_AAAPath.cpp` in the Skia this package ships).

That is a sub-pixel difference in a fringe pixel, not a lost or leaked pixel, and the test keeps the
case with a bound of 32 pixels and 64 levels so a change in that behaviour is reported instead of
being hidden. Making it bite-exact would mean over-invalidating to the whole control whenever a damage
edge meets a rounded border, which costs that control's whole fill - the very cost section 3 is about -
so the difference is accepted.

## 3. Does the fill cost track the damaged area?

Yes. The card's fill is charged for the pixels it is clipped to, and nothing pays the full price of a
partly damaged card.

### 3.1 The card's fill against the clipped area

`Painter.Fill` probe, one run per row (the per-call averages are sums over 599 calls, which reproduce
to a few percent between runs). "Card clip" is `SKCanvas.DeviceClipBounds` at the moment of the fill -
the damage rectangle intersected with the card's own rounded clip - and the card's own rectangle is
1024 x 595 logical, 2048 x 1190 device.

| Damage rectangle | Card clip (device) | Device px | Fill per call | ns/px | All fills per frame | Frame p50 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| full window, 1280 x 820 | 2048 x 1190 | 2,437,120 | 742.5 µs | 0.305 | 1.141 ms | 2.982 ms |
| 1014 x 585 | 2028 x 1170 | 2,372,760 | 616.4 µs | 0.260 | 0.673 ms | 1.721 ms |
| 724 x 418 (half) | 1448 x 836 | 1,210,528 | 257.6 µs | 0.213 | 0.311 ms | 0.987 ms |
| 514 x 294 (quarter) | 1028 x 588 | 604,464 | 111.9 µs | 0.185 | 0.164 ms | 0.625 ms |
| 364 x 208 (sixteenth) | 728 x 416 | 302,848 | 46.7 µs | 0.154 | 0.091 ms | 0.382 ms |
| 256 x 148 | 512 x 296 | 151,552 | 20.0 µs | 0.132 | 0.056 ms | 0.294 ms |
| 181 x 104 | 362 x 208 | 75,296 | 12.6 µs | 0.168 | 0.044 ms | 0.249 ms |
| strip, 900 x 20 | 1800 x 40 | 72,000 | 13.3 µs | 0.185 | 0.018 ms | 0.129 ms |
| hairline, 900 x 8 | 1800 x 16 | 28,800 | 9.1 µs | 0.316 | 0.013 ms | 0.102 ms |
| strip, 900 x 4 | 1800 x 8 | 14,400 | 8.1 µs | 0.565 | 0.013 ms | 0.075 ms |

Read down the "Fill per call" column: **the same fill costs 742.5 µs over the whole card, 111.9 µs
over a quarter of its pixels (15.1 percent, for 24.8 percent of the area), 46.7 µs over a sixteenth
(6.3 percent for 12.4 percent), and 13.3 µs over a 900 x 20 strip (1.8 percent for 3.0 percent).** A
least-squares fit over the ten rows is **0.283 ns per clipped device pixel with an R² of 0.979**; the
intercept comes out slightly negative because the marginal cost per pixel *falls* as the destination
gets small enough to stay in cache (0.305 ns/px for 2.4 Mpx against 0.132 ns/px for 152 kpx), so the
curve is area-proportional and slightly better than proportional in the middle of the range.

The floor is a few microseconds of per-draw setup, and it is not new: in the same frame a 3 x 3
rounded fill costs 2.9 µs, and the card's fill clipped to 14,400 device px still costs 8.1 µs. That
floor is what `dev-docs/ROUNDED-FILL-SPLIT.md` already measured as per-draw cost rather than area.

### 3.2 The rest of the frame

Partial damage also paints fewer fills, because controls that cannot reach the clip are culled by
`canvas.QuickReject(_bounds)` at `Control.cs:2652`:

| Damage | `Painter.Fill` calls per frame | of which rounded | Rounded fill time per frame |
| --- | ---: | ---: | ---: |
| full window | 19 | 14 | 1.063 ms |
| any rectangle inside the card | 8 | 7 | 0.054-0.667 ms (row by row above) |
| 900 x n strip | 5 | 4 | 0.010-0.016 ms |

The whole frame follows, and the shipped build measures it without any instrumentation:

| Damage | Runs (p50, ms) | Median |
| --- | --- | ---: |
| full window | 3.038, 3.031, 3.044 | **3.038** |
| the page's own partial row (180 x 180 swatch) | 0.190, 0.194, 0.192 | **0.192** |

15.8x, against the 3.05 / 0.207 the profile recorded for the same two rows - the same figure, on the
same machine, from a build two days later.

Two cautions on reading the frame column. It is **not** a function of area alone: 181 x 104 and
900 x 20 have almost the same area (18,824 against 18,000 logical px) and cost 0.249 against 0.129 ms,
because the tall rectangle crosses more controls, so more fills and more of the tree survive culling.
And `LastFrameTime` is paint only: the Win32 backend's `BitBlt` is outside it, and `Blit` copies the
union of the stale region and the painted area, so it is proportional to the damage as well - that was
not measured here, and the profile prices the full-window blit at 1.331 ms.

### 3.3 Why the cost falls: the code path

There is no intermediate surface and no clip reset between the damage region and the fill:

1. `UiHost.PaintCore` takes the dirty region, opens a save and clips to it before the tree is walked -
   `UiHost.cs:958` calls `ClipToPaintArea(canvas, region, whole, area)`, which for a rectangular
   region is `canvas.ClipRect(...)` at `UiHost.cs:1044`, and for a region with holes in it is
   `canvas.ClipPath(boundary, SKClipOperation.Intersect, false)`. The clear at `UiHost.cs:963` and the
   tree walk at `UiHost.cs:966` both happen inside that clip.
2. `Control.Render` (`Control.cs:2638`) saves, translates, and then **intersects** with the control's
   own clip: `canvas.ClipRect(local)` at `Control.cs:2667`, or `Border.ClipTo` at `Control.cs:2663`
   for a rounded border, which is `canvas.ClipPath(..., SKClipOperation.Intersect, antialias: true)`
   at `SimpleBorder.cs:184`. Neither replaces the clip the host installed; `Border.NarrowsClip`
   narrows it.
3. `Control.OnPaintBackground` calls `Painter.Fill(canvas, rect, backColor, Border.Corners)`
   (`Control.cs:3201`), which builds the rounded path and calls `canvas.DrawPath` (`Painter.cs:49`,
   `Painter.cs:71`) into that clip. Skia's scan converter restricts its scanlines to the clip's bounds,
   so the fill is charged for the clipped area - which is what section 3.1 measures.
4. No control on this page paints through a layer. The only `SaveLayer` in `Control.Render`
   (`Control.cs:2684`) is for a faded control or an image filter, and neither of the page's cards has
   either, so there is no offscreen whose size could be the control's rather than the damage's.

So of the four possibilities the question listed, the answer is the first one working as intended: the
damage region is applied as a canvas clip before the fill, nothing in `Border.NarrowsClip` or the
control's paint resets or widens it, and no layer ignores it.

## 4. What could not be driven

- **The windowed harness cannot compare pixels.** It reports time, not readback. The correctness
  comparison therefore runs on the headless backend, which is the same `UiHost.Paint`, the same tree
  and the same Skia rasteriser, but is not the Win32 window.
- **The 2.00 and 1.25 cases are a scaled canvas, not a screen capture.** They reproduce
  `Win32WindowBackend.PaintHost`'s `canvas.Scale(_scale)` exactly, but the window itself was not
  captured and compared.
- **The demo's own flags do not expose arbitrary damage.** `--partial-damage` selects the 180 x 180
  swatch only. The five other rectangle sizes came from the temporary `KOLVIR_DAMAGE_RECT` switch,
  which is not in this change.
- **A control that paints through an opacity layer or an image filter** bounds its offscreen to the
  clip, so it should behave like everything else, but it was not measured.
- **The GPU paths were not measured.** Every row here is `--no-hardware-acceleration`
  (`mode="cpu (disabled)"` in the BENCH line).
- **Popups and menus are separate windows** and were not damaged; the menu bar takes part in the frame
  (its 1280 x 64 fill is one of the 19) but was not individually damaged.

## 5. Reproduction

Correctness - part of the suite, no setup:

```
dotnet test tests/core/Kolvir.Tests/Kolvir.Tests.csproj --filter "FullyQualifiedName~PartialDamageTests" `
    --logger "console;verbosity=detailed"
```

Each case prints its scale, the number of pixels that differ inside and outside the damaged area, the
worst channel delta and both hashes.

The curve - build, then one benchmark run per rectangle:

```powershell
dotnet build src/demo/Kolvir.Demo.App -c Release
$env:KOLVIR_FILL_PROBE = "$PWD\fill.csv"      # temporary probe, section 1.1
$env:KOLVIR_DAMAGE_RECT = "246,155,514,294"   # quarter of the card
./src/demo/Kolvir.Demo.App/bin/Release/net10.0-windows/KolvirDemoApp.exe `
    --page measured-rendering --benchmark --no-hardware-acceleration --light
```

The `BENCH` line's `area=` is the damaged rectangle and `p50=` is the frame. The two instruments are
described in section 1.1 so they can be put back; they are not part of this change.

## 6. What this change costs

Rung 1 of `FEATURE-COST-POLICY.md` section 4, zero by construction: the change is a test file and this
document, so it adds nothing to the shipped assemblies. The `Kolvir.Minimal` fixture was published
anyway, `-c Release -r win-x64 -p:EmbedSkiaSharpNative=false`, from two build roots of equal path
length - one at `develop` (`ace0d1c`), one at this branch: **3,885,056 bytes both times, a delta of
0**. The assembly the fixture contains does not change, so the number is the fixture rather than the
change; it is quoted to show the method was run, not because a test file could move it. Only win-x64
was weighed; a test no application references has no platform-specific half to differ.
