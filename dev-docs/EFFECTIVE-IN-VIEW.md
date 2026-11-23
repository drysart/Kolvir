# EffectiveInView - design and implementation notes

Status: implemented as `Kolvir.Feature.EffectiveInView`. Rung 2 in behaviour, rung 3 in packaging.

---

## 1. What it answers, and what it does not

`EffectiveVisible` is the cheap question: the control and every container above it have their
`Visible` flag set. It is O(depth) and exact, and it is on `Control` because it is asked everywhere.

`EffectiveInView` is the expensive question: is any part of the control genuinely on screen? A
control can be effectively visible and still not be visible to the user:

- it is scrolled out of a scrolling container's viewport;
- it was placed outside its parent's content area;
- it is fully covered by an opaque control painted above it;
- its ancestor's content bounds do not include it, or the window is too small for it.

The answer is true when a non-empty part of the control's rectangle survives both the clip chain of
its ancestors and the controls painted over it. Partly scrolled out, partly covered, or partly off
the edge is true.

It is not a window-manager question. The property does not require the window to be shown, so a
headless window and a window that has not been shown yet still answer, and a closed window still
answers about the geometry of the tree. An application that needs "and the user can see the window"
combines it with `IsDisplayed`. This is deliberate: the test suite and the sample's headless render
never show a window, and a property that answered false there would be untestable and would say
nothing about layout.

It is also not a per-pixel question. See section 3.

## 2. The predicate

Computed on the UI thread, from the last completed pass, in host (logical) coordinates.

1. **Gates.** `Host` is not null, `EffectiveVisible` is true, and the control has a non-empty
   rectangle. Each is O(depth) and exact.
2. **The control's rectangle.** `GetPositionInHost()` gives the top-left, and
   `EffectiveScaleMultiplier` gives the zoom between the control's own space and the host's. Both are
   public, and between them they compose every scroll offset and every `Panel.ScaleFactor` above.
   This reproduces what `Control.Render` translates and clips without reaching into it.
3. **The host's client area.** `(0, 0, host.ClientSize.Width, host.ClientSize.Height)`. The menu bar
   does not need a special case: `UiHost.PerformLayout` gives the root `(0, top, w, h)` and the bar
   `(0, 0, w, top)`, and every control clips to its own client rectangle, so the root's own clip
   already excludes the bar's strip.
4. **The ancestor clip chain.** Walking `Parent` upwards:
   - every control clips itself and its children to its client rectangle (`Control.Render`), so
     intersect with `ancestor.ClientRect` mapped by `ancestor.ClientToHost`;
   - a `Panel` clips scrolled content further, to the same rectangle `Panel.ViewportRect` reports
     (`ScrollBars.EffectiveViewport`), so intersect with `panel.ViewportRect` mapped by
     `panel.ClientToHost`;
   - the two scrollbars are the exception: they are not in `Controls`, they are painted after the
     viewport clip is restored, and they live in the gutter the viewport excludes. Walking up from
     `panel.VerticalScrollBar` or `panel.HorizontalScrollBar` skips the viewport intersection. A bar
     therefore answers true in its own gutter, and a control under the gutter answers false because
     the viewport, not the bar, clipped it.
5. **Occlusion** (section 3): subtract every opaque control painted above from what survived.

`VirtualList` inherits `Panel` and clips its rows through the same helper, so the panel rule covers
it. A container that clips with its own rounded border is modelled as a rectangle; see section 3.

## 3. Occlusion, and why it is not a hit test

The library's own contract for "this control paints every pixel of its bounds" is `Control.Opaque`:
no filter, opacity 1, a rectangular border and an opaque effective background. It is already used to
decide that an opaque sibling covers a repaint area, so the feature uses the same contract and
nothing else.

A control occludes a point when it is painted above the target, is visible, has area, is `Opaque`,
and the point is inside its own host rectangle intersected with its own clip chain. The target's own
descendants never count - a target covered by its own child is still the target on screen.

**Paint order** is the order `UiHost.Paint` uses: the menu bar, then the root tree, then the overlays
in the order they were added. Inside a control, `Controls` order (later children paint on top) and,
for a `Panel`, the two bars last, because `RenderBars` runs after the scrolled content and the bars
are not in `Controls`. A control counts as "above" the target when its paint rank is past the end of
the target's subtree, which is the same as "painted after everything the target paints".

**Exact subtraction, not sampling.** With rectangle clips and whole-rectangle occluders the answer is
exact: start with the clipped rectangle and remove each occluder's visible rectangle, from the
highest rank down, until nothing is left. Descending rank means a popup or a full-window panel
collapses the answer in one subtraction. A sampled test (corners and centre, the obvious thing to do
with a hit test) can only be wrong in one direction - it misses a sliver between samples - and it buys
nothing once the occluders are already rectangles.

The subtraction is bounded: a target's surviving area is kept in at most 32 pieces, and a target that
spans more than 2048 grid cells falls back to scanning every occluder. A pathological tree - one
target covered by thousands of small opaque controls - can therefore miss a sliver, exactly as a
sampled test would. Ordinary trees never reach either bound: one coverer leaves at most four pieces.

**Why not `UiHost.HitTest`.** It is private, and it is the wrong predicate:

| Case | Painted | `HitTest` |
| --- | --- | --- |
| A transparent control above the target | nothing over it | returns it, so a sample would report occluded |
| A disabled opaque control above | covers the target | returns null there and the walk falls through to the target, so a sample would report visible |
| The dismiss catcher behind a popup | nothing; it paints nothing | returns it for a press, and `PassesHoverThrough` is internal so a feature cannot skip it |

The disabled and transparent rows are both pinned by tests.

**What occlusion cannot report.** These are the honest limits, and they all lean the same way: a
false answer is trustworthy, a true answer can be too generous.

- `Opaque` is false for a control that paints opaque *content* on a transparent background. There are
  no overrides of `Opaque` in the library today, so a full-bleed `Image` or an SVG view over a
  control is treated as transparent and the covered control reports true. The extension point is
  `Opaque`; an application control that covers its bounds can override it.
- A rounded border clips to a rounded rectangle; the model treats it as a rectangle, so a corner can
  over-report.
- A control with `Opacity < 1` or a filter is not `Opaque` and is treated as transparent.
- Custom child clipping that a container does itself is not captured. The library's own clip sites
  are `Panel`/`VirtualList` (captured), the tab strip and the text controls' content clipping (their
  own paint, no child affected), and the multiline text box's scrollbars, which paint above children
  but are not children. A control under one of those bars can report true.

## 4. Cost

- **Nothing is computed until asked for.** An application that never names the feature ships none of
  it (the package is not referenced) and an application that references it but never reads it or
  subscribes pays nothing at run time.
- **A lazy read** builds a snapshot for that one control: one walk of the paint tree, O(tree *
  depth) because `GetPositionInHost` walks to the root for each control, plus the rectangle work.
  That is the "expensive operation" this feature exists to keep off `Control`. Repeated reads in the
  same state are cached (section 5), so the walk happens once per change, not once per read.
- **A watched pass** builds one snapshot for every watched control on that host, so the tree is
  walked once per pass rather than once per listener. Per listener the work is its occluder query
  plus subtraction, bounded by the local density of opaque controls. With N listeners the pass costs
  one tree walk plus O(N), not N tree walks.
- **A pass with nobody listening** builds no snapshot and allocates nothing: the work is the call into
  the notification and its first check, and the watched set is not enumerated because the feature
  tracks whether anything is watched.
- **The static subscriptions** are one delegate on `UiHost` for completed layouts and, per host that
  has been read or watched, one invalidation handler. The invalidation handler only increments a
  counter; it is what makes a change that repaints without laying out - a child added, a control
  hidden - drop the cached answers.

**Measured cost of opting in.** `Kolvir.Minimal` published with NativeAOT for `win-x64` (Release,
the settings the fixture carries) is 9,055,744 bytes. The same fixture with one probe that reads
`EffectiveInView` and registers a handler - so both halves of the feature are reachable - is
9,094,656 bytes: **+38,912 bytes (+0.43%)**, which is the whole feature in the deliverable, not the
cost of the seam. The delta is an upper bound: an application that reads the property but never
subscribes drops the dispatch code with the handler. The seam's own cost for an application that does
not reference the package is whatever the measurement above attributes to the core change, and the
only thing the core change adds to a non-listening pass is the call and its first check.

### Re-measuring

The fixture is `src/demo/Kolvir.Minimal`: one window, one label, no features. Release carries
`PublishAot`, `PublishTrimmed`, `TrimMode=full` and the SkiaSharp embedding, so the publish is the
measurement.

```sh
# Baseline, from a clean checkout.
dotnet publish src/demo/Kolvir.Minimal/Kolvir.Minimal.csproj -c Release -r win-x64
# -> src/demo/Kolvir.Minimal/bin/Release/net10.0-windows/win-x64/publish/Kolvir.Minimal.exe
#    ......................... 9,055,744 bytes

# Opted in. Add to the fixture's csproj:
#   <ProjectReference Include="..\..\features\Kolvir.Feature.EffectiveInView\Kolvir.Feature.EffectiveInView.csproj" />
# and to Program.cs, so both halves of the feature are reachable:
#   using Kolvir.Feature.InView;
#   var probe = new Label("Probe");
#   content.Controls.Add(probe);
#   probe.AddEffectiveInViewChangedHandler(static _ => { });
#   _ = probe.EffectiveInView;
dotnet publish src/demo/Kolvir.Minimal/Kolvir.Minimal.csproj -c Release -r win-x64
#    ......................... 9,094,656 bytes

# Delta: +38,912 bytes (+0.43%)
```

The same two commands with `-r linux-x64` and `-r osx-arm64`, run on those hosts, are the other two
rows of the comparison in FEATURE-COST-POLICY.md section 8; `win-x64` is the one quoted here because
that is the host this was measured on. Remove the reference and the probe afterwards - the fixture is
the floor every feature is measured against, and it must stay feature-free.

## 5. Caching and invalidation

The answer is cached per control in a `ConditionalWeakTable<Control, EffectiveInViewState>`, the
same shape the drag and drop feature uses. A second table holds only the controls with a handler, so
the pass enumerates the watched set and not every control that was ever read. Both tables key weakly
and their values hold no control, so a control that is collected takes its state with it; a test pins
this with a `WeakReference` and a forced collection.

The cache is thrown away by:

| Change | How it is seen |
| --- | --- |
| A completed layout pass | the layout notification |
| A scroll that moved the content | the same notification, raised by the scroll helper |
| An arrangement queued by `RequestLayout` and flushed by a paint or a press | the same notification |
| Any repaint request - a child added or removed, a control hidden, a background or filter changed, a theme swap | the host's invalidation handler |
| The control's own `Bounds` assigned directly, with no pass | `AddBoundsChangedHandler` on that control |
| The control becoming hidden | the read-time `EffectiveVisible` gate, and the invalidation a visibility change already sends |
| A control attached to a different host | the read-time `Host` gate, and the host's pass only evaluates the controls attached to it |

A pass on a host whose client area is empty does not become the baseline a later, real pass is
measured against. The window is constructed before it is given a size, so a pass at that moment would
otherwise make every control look "out of view" and turn the first real pass into a change event.

The cache is single-threaded, like every other read of the control tree.

## 6. Change handlers, re-entrancy, and the first observation

`AddEffectiveInViewChangedHandler` returns a `HandlerToken` and `RemoveEffectiveInViewChangedHandler`
removes the same callback, exactly like `AddBoundsChangedHandler` and every other event in the
library. Registering makes the control one the pass keeps up to date. The first pass after
registering establishes the baseline and does not call the handler, so subscribing to a control that
is already on screen does not look like a change. Removing the last handler drops the baseline, so
the next registration starts afresh rather than reporting everything that happened in the gap.

Handlers run after the pass has finished arranging, in subscription order, and values for every
watched control are settled before the first handler runs. A handler that reads another watched
control therefore sees the same pass. A handler that throws stops those after it and the exception
reaches `Application.UnhandledException` through the caller's guard; the tree is already arranged, so
nothing is corrupted.

Two guards keep a handler from recursing:

- `UiHost` suppresses a nested notification: a handler may call `PerformLayout`, and that pass
  completes normally, but it does not raise the layout event again.
- The feature suppresses a nested dispatch: a notification that arrives while handlers are running
  only invalidates the cache. The next pass reports.

Handlers do not run for a read. A timer or an animation that reads the property never fires them.

## 7. Unhosted, never laid out, detached

- **No host.** `EffectiveInView` is false and does not throw. A read creates no host subscription and
  no cache entry for a control that is not hosted.
- **Hosted but never laid out.** False: the root has no rectangle yet, so nothing does.
- **Registered before the control is hosted.** The registration is kept; the first pass on the host
  it lands on establishes the baseline silently, and later changes fire.
- **Detached before a pass.** A control that had a handler and was on screen now has no host; the
  pass reports false once and the handler hears it. A control that is moved to another host is left
  to that host's next pass.
- **Detached during dispatch.** Values are settled before handlers run, so the pass is unaffected.
  Before each handler is called the control is checked again; if it left every tree, the reported
  value becomes false and the handler fires once. No throw.

## 8. The seam this needed, and the rung

The feature is a package of its own and sees only the public API. The one thing the public API did
not offer was a way to hear that the tree's geometry had settled without registering something with a
window - there is no host-attached notification either, so per-host subscription would have to
discover hosts.

The core library therefore gained one additive pair:

```csharp
public static IDisposable AddLayoutCompletedHandler(Action<UiHost> handler);
public static void RemoveLayoutCompletedHandler(Action<UiHost> handler);
```

It is static for the same reason `AddFrameStartingHandler` is: the work is process-wide, every
handler is told which host completed, and a feature can subscribe before any window exists. It is
raised at the end of `PerformLayout`, when a standalone `FlushArrangements` actually arranged
something, and when a scroll offset changes outside a pass - because a scroll settles the same
geometry a pass does without running one.

It costs nothing to an application that does not subscribe: no snapshot, no enumeration, nothing but
the call into the notification and its first check. It adds no field to any control and no virtual to
a retained type. The measurement in section 4 is the number this argument rests on.

The rung story is in two halves. In behaviour it is rung 2: the seam is the opt-in and nothing runs
until a feature subscribes to it. In packaging it is rung 3: the implementation ships as
`Kolvir.Feature.EffectiveInView`, so an application that does not reference it does not contain it at
all. Section 4 of FEATURE-COST-POLICY.md requires a rung-3 feature's surface to be extension members,
which is exactly what this is - an extension property and a pair of handler methods.

## 9. Tests

`tests/features/Kolvir.Feature.EffectiveInView.Tests` runs on every platform and needs no display.
It covers, among others:

- scrolled out, partly scrolled out, and below the viewport of a scrolling panel;
- moved outside a parent's bounds, and moved back;
- fully covered, partly covered, covered by an opaque grandchild of a transparent sibling, and
  covered by an overlay;
- a disabled opaque cover (the hit test would miss it) and a transparent control above (the hit test
  would wrongly report it);
- a panel's scrollbar in its own gutter, and a control clipped by the gutter;
- a hidden ancestor;
- handler firing on change and only on change, no firing on a pass that changed nothing, no firing
  for the first observation, no recursion when a handler lays out again, and a detached control
  settling on false;
- the cache computing once for repeated reads, and one computation per pass per watched control;
- a child added after the pass and a cover shown again, both without a forced pass;
- the weak table not keeping a detached control alive.

`tests/core/Kolvir.Tests/Core/LayoutCompletedTests.cs` covers the seam itself: one event per pass,
none for a paint with nothing to arrange, one for a queued arrangement, one for a scroll that moved,
none for the pass a handler starts, and the two no-root cases - nothing for a host with neither a
tree nor a menu bar, one for a menu bar with no tree below it.

## 10. Decisions

- The window's shown and closed state is not part of the answer; `IsDisplayed` is the window-manager
  question and the docs say to combine them.
- One event covers a completed pass and a scroll that moved content, rather than two. Both mean "the
  geometry the last layout produced is settled", which is what a listener needs.
- Occlusion is exact rectangle subtraction against `Opaque`, not sampling through `HitTest`.
- The first observation after registering is silent.
- The package is `Kolvir.Feature.EffectiveInView` and the namespace is `Kolvir.Feature.InView`. This is
  the one feature that does not use the single-word `Kolvir.<Feature>` namespace the others use -
  deliberate, not an oversight. The last segment still differs from the property name, so the `using`
  does not share a name with the member it brings into scope.
