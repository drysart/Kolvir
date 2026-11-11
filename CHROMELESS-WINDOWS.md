# Chromeless windows - implementation plan

A window mode where the application owns every visible pixel edge to edge, while the OS keeps its
resize band, snap, animations and shadow. The band sits outside the apparent window rather than as an
inset client area, and the platform still owns the caption buttons - on Windows by emulating them, so
an XPlatUI application cannot tell the three backends apart.

Status of this document: design settled by three platform spikes (Win32, AppKit, GTK4), implementation
not started. Each step is independently buildable and verifiable, so work can stop after any step with
the tree in a working state.

## Design decisions

| Topic | Decision | Rationale |
| --- | --- | --- |
| Chrome vocabulary | Two values, `XPlatWindowChrome.System` and `Borderless` | There is no third thing to describe. Being chromeless already means the app owns the window edge to edge, so a "custom chrome" value would only be a rename of `Borderless`. |
| Capability discovery | `XPlatChromeCapabilities`, a non-throwing report, kept separate from the requests | The requests have to refuse what they cannot honour, and a feature that can only be discovered by triggering an exception forces every application either into `try`/`catch` or into asking for something it should not. Capabilities answer the question; the throw stays as the guard for code that ignored the answer. |
| Caption buttons | Orthogonal knob, `XPlatCaptionButtons.None` or `Platform` | Whether the platform draws buttons has nothing to do with whether it draws a title bar. |
| Windows parity | The Win32 backend **emulates** `Platform` buttons | Otherwise the same application would have to ship two title-bar implementations. The backend draws and hit-tests them so the public API is uniform; the fact that the pixels come from the backend rather than from DWM is invisible to the app. |
| Two insets, never one | `SurfaceInset` (do not paint) and `ResizeInset` (do not put controls) are separate | Measured: GTK's 12 px band is entirely *outside* the app's pixels, macOS's 3 pt band is *on top of* them, and Win32's 13 px band is inside the client on the top edge only. A single thickness would be wrong for all three. |
| Corner handling | Separate `CornerResizeExtent` and `CornerRadius` | macOS resizes from ~13 pt at a corner while its edge band is only 3 pt, and its ~11 pt radius clips drawn content. Neither number can be derived from the other. |
| Metrics lifetime | One immutable snapshot, refreshed on size/scale/state/theme change | The values move when the window moves to another DPI or another display; polling invites tearing between fields. |
| Drag start | `TryBeginDrag()`, argument-free, called synchronously inside the pointer-down dispatch | GDK4 exposes no event serial, so `begin_move` only works while a real press is in flight. The backend caches the press (device, button, position, timestamp) so the app needs no platform knowledge. Calling it outside a press returns false instead of silently doing nothing. |
| Resize start | `TryBeginResize(edge)` exists, but is not the default path | All three platforms resize natively at the edges, so this is only for applications that draw their own handles. A `NativeResize` flag would have been a second way to say the same thing. |
| Hit priority | Caption buttons, then resize band, then app content | Measured on Win32: a press anywhere in the 92x43 button rect returns `HTCLIENT` and never reaches the app, including the 13 px that would otherwise be resize band. Without a stated order the same pixel is claimed twice. |
| Platform behaviours | All native: snap, tiling, animations, drag, minimise, shadow | A borderless window is not a popup. On Win32 the animation discriminator is `WS_CAPTION`, and `SC_RESTORE` animates where `ShowWindow(SW_RESTORE)` does not. |
| Title bar itself | Never provided by the library | The application draws it inline. There is no seam for caption-height hit testing and no `HTCAPTION` equivalent; a spike variant that did this was deliberately not adopted. |
| Accessibility on Windows | v1 ships keyboard equivalents (`Alt+F4`, `Alt+Space`) and documents them; a UIA provider is deferred | Emulated buttons are not real controls, so they are invisible to assistive technology. On macOS and GTK the drawn buttons are genuine controls. Deferring keeps the first implementation honest about the gap instead of silently shipping an unreachable window. |
| Restore flash | Solved by colour, not by message interception | A borderless Win32 window composites its non-client strip with the restored frame *before* any handler runs, so the only fix is making that strip the title bar colour (`DWMWA_CAPTION_COLOR`). Exposed as `CaptionColor` because only the application knows its own title bar colour. |

## Measured platform behaviour

The acceptance criteria for each backend. Measured by the three spikes; every number is from a real
window with real injected input, not from a calculation.

| | Win32 | macOS | GTK4 (4.14 / 4.22) |
| --- | --- | --- | --- |
| What the app paints | client only: DWM owns 13 px L/R/B plus a ~2 px top border line that crosses the title bar | the whole window (`contentView.frame == window.frame`) | the whole window |
| What the app may use for controls | client minus a 13 px top band | window minus 3 pt on every edge | every pixel - all inner presses reach the app |
| Resize band | 13 px deep, inside the client on the top edge | 3 pt on every edge, on top of app pixels | 12 logical px, entirely outside the surface |
| Corner resize extent | 13 px diagonal | ~13 pt diagonal | handled outside the surface |
| Corner radius | DWM's (square on the test VM, round on real hardware) | ~11 pt, clips content | set it explicitly - defaults are ~10 vs ~12 between versions |
| Caption buttons | none - emulated by the backend, 92x43 for all three at 192 DPI | traffic lights 14x14 at (9,9)/(32,9)/(55,9), left, clickable over paint | `Gtk.WindowControls`, right: 24x24 on 4.14, 40x24 on 4.22 |
| Strip above the content | 0 top inset; client covers the window top | 0 (`fullSizeContentView`); `contentLayoutRect` still reserves 32 pt and must not be used | 0, but only with the widget **and** the CSS together |

## API surface

```csharp
public sealed record XPlatChromeCapabilities
{
    public static XPlatChromeCapabilities None { get; }   // nothing adopted; also the default
    public bool Borderless { get; init; }
    public bool CaptionButtons { get; init; }
    public bool WindowColors { get; init; }
}

public enum XPlatWindowChrome { System, Borderless }
public enum XPlatCaptionButtons { None, Platform }
public enum XPlatCaptionButtonKind { Close, Minimize, Maximize, Restore }
public enum XPlatCaptionButtonSide { Left, Right }
[Flags] public enum XPlatResizeEdge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

public readonly record struct XPlatCaptionButton(XPlatCaptionButtonKind Kind, SKRect Bounds);

public sealed record XPlatWindowChromeMetrics
{
    public XPlatThickness SurfaceInset { get; init; }   // platform paints here - do not
    public XPlatThickness ResizeInset { get; init; }    // platform claims presses here
    public float CornerResizeExtent { get; init; }
    public float CornerRadius { get; init; }
    public XPlatCaptionButtons CaptionButtons { get; init; }      // what was actually drawn
    public XPlatCaptionButtonSide CaptionButtonSide { get; init; }
    public SKRect CaptionButtonStrip { get; init; }               // union, client coords
    public IReadOnlyList<XPlatCaptionButton> CaptionButtonBounds { get; init; }
}
```

`IXPlatWindowBackend` gains, and `XPlatWindow` mirrors 1:1 as it does for every other member:

```csharp
XPlatChromeCapabilities ChromeCapabilities { get; }
XPlatWindowChrome Chrome { get; set; }
XPlatCaptionButtons CaptionButtons { get; set; }
XPlatWindowChromeMetrics ChromeMetrics { get; }
SKColor? CaptionColor { get; set; }    // Win32 DWMWA_CAPTION_COLOR; GTK window.csd colour
SKColor? BorderColor { get; set; }     // Win32 DWMWA_BORDER_COLOR; GTK window.csd border
bool TryBeginDrag();
bool TryBeginResize(XPlatResizeEdge edge);
IDisposable AddChromeMetricsChangedHandler(Action<object> handler);
void RemoveChromeMetricsChangedHandler(Action<object> handler);
```

All geometry is logical pixels in client coordinates. Applications must never compute caption-button
geometry themselves: it differs by platform, by GTK version and by DPI.

## Normative rules

1. **Report, do not assume.** Ask `ChromeCapabilities` before setting anything - the setters throw
   when the answer is no - and read `ChromeMetrics.CaptionButtons` for what was actually drawn.
2. **Two insets.** `SurfaceInset` means do not paint; `ResizeInset` means do not hit-test controls.
3. **`TryBeginDrag()` is called synchronously from pointer-down**, returns whether a drag started, and
   on `true` the application must suppress the click it would otherwise deliver.
4. **Caption buttons win over the resize band, which wins over the application.**
5. **The platform keeps its window behaviours**; borderless is not popup.
6. **`CornerRadius` is for clipping the application's own chrome**, because the platform clips the
   surface underneath it.
7. **`Chrome` may be changed live**, but setting it before `Show` is the cheap path.

## Win32 caption-button emulation

The backend draws and handles the buttons so that `Platform` behaves as it does natively elsewhere.

1. Draw in an overlay pass after the application's Skia content, into the same surface. No child
   HWNDs, so the buttons inherit the application's DPI, scale and theming.
2. Geometry and glyphs from the platform: 92x43 for the three buttons at 192 DPI, system icon font,
   hover and press states, red close. The Win32 spike's mode P is the reference implementation.
3. Swallow input inside the strip: no forwarding, no captured clicks.
4. Repaint on state, theme, DPI, `CaptionColor` and resize; the maximise glyph becomes restore.
5. Follow `SetTitleBarAppearance(dark)` for glyph colour, and `CaptionColor` for the strip so the
   restore path stays invisible.

## Steps

| # | Step | Scope | Done when |
| --- | --- | --- | --- |
| 1 | Types and metrics model | `XPlatWindowChrome`, `XPlatCaptionButtons`, `XPlatCaptionButtonKind`, `XPlatCaptionButtonSide`, `XPlatResizeEdge`, `XPlatCaptionButton`, `XPlatWindowChromeMetrics` | Types compile; a headless test builds a snapshot and reads every field back; no backend is touched yet |
| 2 | Seam members and delegation | `XPlatChromeCapabilities` plus the nine members on `IXPlatWindowBackend`, `XPlatWindow` passthroughs, and defaults that refuse rather than no-op: system chrome, empty insets, no buttons | Solution builds and every existing test passes with no behaviour change; a backend that has adopted nothing reports `None`, refuses every chrome request at the call site, and answers `false` from drag and resize |
| 3 | Win32 borderless core | Style, `WM_NCCALCSIZE` from `rgrc[0]`, measured frame, custom `WM_NCHITTEST`, work-area clamp, metrics reporting | Insets match L13/T0/R13/B13 at 192 DPI, all four edges resize 13 px, maximise lands on the work area exactly, restore animation plays |
| 4 | Win32 drag and resize start | Press caching (device, button, position, timestamp); `TryBeginDrag`, `TryBeginResize` | A press on an app-drawn title bar drags the window with native snap and tiling; calling either one outside a press returns false |
| 5 | Win32 caption-button emulation | Overlay draw, hit priority, state/theme/DPI redraw, `CaptionButtonStrip` and bounds reporting | Buttons draw at 92x43 and take clicks; the app receives nothing inside the strip; glyphs follow `State` and `SetTitleBarAppearance` |
| 6 | Win32 colour and restore | `CaptionColor` to `DWMWA_CAPTION_COLOR`, `BorderColor` to `DWMWA_BORDER_COLOR`; propagate on change | Three consecutive minimise/restore cycles show no non-client strip in a pixel scan; border colour applies exactly or is removed |
| 7 | GTK4 borderless core | Zero-measure titlebar widget plus collapse CSS, `window.csd` border with explicit radius, metrics reporting | Strip measures 0 by pixel bbox, inner presses reach the app 100%, band is 12 logical px outside on all four edges |
| 8 | GTK4 platform buttons and drag | `Gtk.WindowControls` as the caption buttons, `Gtk.WindowHandle` for drag, decoration-layout reporting | Buttons take clicks with the strip collapsed and re-layout on maximise; window handle drag tiles; `CaptionButtonSide`/bounds match what is on screen |
| 9 | AppKit borderless core | `fullSizeContentView`, `titlebarAppearsTransparent`, `titleVisibility`, metrics reporting | `contentView.frame == window.frame`; `ResizeInset` 3, `CornerResizeExtent` 13, `CornerRadius` 11; no strip above the content |
| 10 | AppKit buttons and drag | Traffic lights as `Platform` buttons, `performWindowDrag` for `TryBeginDrag` | Lights take clicks over app paint and report their rects; dragging from an app-drawn title bar moves the window and tiles |
| 11 | Demo, docs and AOT | Chromeless window demo page, README section, headless assertions, NativeAOT publish | Demo renders identically under JIT and AOT; README documents the API and the Windows accessibility limitation |

## Verification approach

Steps 1 and 2 are checked headlessly like the rest of the library: build the model, assert fields,
run the existing suite for no regressions.

From step 3 on, each backend is checked live against the table in *Measured platform behaviour*, using
the spikes as the reference harnesses - they already contain the pixel measurement, input injection
and per-platform drivers:

- **Win32**: `spikes/Win32Chromeless` (branch `spike/win32-chromeless`) - mode P is the reference
  recipe, with the band sampling, hit-test maps and restore-cycle trace already written.
- **macOS**: `spikes/MacChromeless` (branch `spike/mac-chromeless`) - the interactive spike and its
  `--probe` harness, run on a physical Mac.
- **GTK4**: the spike on both Linux hosts - X11 for the GDK geometry rules, Wayland for the band and
  screenshot route.

A step is done when the numbers match the table within a pixel, not when the window merely looks right.
Appearance regressions are caught with the demo's `--render` output, as with every other feature.

## Progress

| Step | State | Notes |
| --- | --- | --- |
| - | Approved | Design reviewed and accepted. Implementation runs on this branch, starting at step 1. |
| 1 | Done | Seven types in `XPlatUI/Application`: `XPlatWindowChrome`, `XPlatCaptionButtons`, `XPlatCaptionButtonKind`, `XPlatCaptionButtonSide`, `XPlatResizeEdge` (flags, so two adjacent edges name a corner), `XPlatCaptionButton` (readonly record struct of kind and rect) and `XPlatWindowChromeMetrics` (sealed record). No backend and no window member changed, so behaviour is untouched: the solution builds with 0 warnings and 0 errors, and the portable suite is 481 passed, 1 skipped (the pre-existing macOS-only skip). Verified with 5 headless checks (`ChromeMetricsTests`): a default snapshot reports that the platform owns nothing and still returns an empty rather than null button list; every field round-trips with drawing order preserved; `XPlatCaptionButton` is a value; resize edges combine into corners in either order; and the two insets are proven independent across the three measured platforms - GTK empty/empty, macOS empty/3 pt, Win32 13 pt painted against 13 pt claimed at the top edge. |
| 2 | Done | The seam gained ten members - `ChromeCapabilities`, `Chrome`, `CaptionButtons`, `ChromeMetrics`, `CaptionColor`, `BorderColor`, `TryBeginDrag`, `TryBeginResize`, `AddChromeMetricsChangedHandler`, `RemoveChromeMetricsChangedHandler` - and `XPlatWindow` mirrors all ten. They are **default interface members**, which is the one new thing this step introduces: the four backends compile and behave exactly as before, and a third-party implementation of the seam is not broken by the feature existing. `XPlatChromeCapabilities` is the non-throwing half: the requests have to refuse what they cannot honour, so a feature discoverable only by throwing would push every application into `try`/`catch` or into asking for something it should not. It reports `Borderless`, `CaptionButtons` and `WindowColors`; drag and resize need no flag because `TryBeginDrag`/`TryBeginResize` already answer with a bool at the moment it matters. The defaults above it split by what a caller can do with the answer. Requests that would otherwise no-op silently **throw** `NotSupportedException` at the call site - `Chrome`, `CaptionButtons`, `CaptionColor`, `BorderColor` - because a window that silently stays decorated is the expensive failure mode. Queries that "no" is a usable answer for **degrade**: `TryBeginDrag` and `TryBeginResize` return false, `ChromeMetrics` returns one shared empty snapshot, and the handler subscription is well behaved but never fires. Verified with 4 headless checks (`ChromeSeamTests`, driving the headless backend through `XPlatWindow`): a backend with no chrome support reports `System`, `None`, empty insets and empty geometry; `ChromeCapabilities` answers `None` without throwing and agrees with the refusal; every request throws and leaves the window unchanged; and the change subscription disposes cleanly, fires never, and rejects a null handler. Solution builds 0 warnings / 0 errors; portable suite 485 passed, 1 skipped. |
| 3 | Done | `Win32WindowBackend` gained `Chrome`, `ChromeCapabilities` (reporting `Borderless` only until steps 5 and 6), `ChromeMetrics` and the change handler. In borderless mode `WM_NCCALCSIZE` lets `DefWindowProc` compute the ordinary framed client rect, **measures** the frame from `rgrc[0]` - never `rgrc[1]`, the stale current rect - then grows the client top to the window top, or to `window.top + frameHeight` while maximizing so the client lands exactly on the work area. `WM_NCHITTEST` reports the band itself from the cached metrics, zeroed while maximized, because the client covers the caption and the system would otherwise classify the top edge as client area. `OuterSizeFor` drops the caption inset in borderless mode so a requested client size stays a client size, and metrics changes are raised on state and DPI changes as well as on a chrome switch. Verified against a live window at 192 DPI: **actual insets L13 T0 R13 B13 device** (reported as 6.5/0/6.5/6.5 logical); hit test returns `TOP` for y 0-12 and `CLIENT` from y 13, the same 13 px depth on all four edges with `CLIENT` from 13 px in, and `CLIENT` at the centre; **maximized window (-13,-13)-(3853,2077) with a client of 3840x2064, the work area exactly**, maximized metrics claiming no band and the top edge hit-testing `CLIENT`; restore returns the 1600x1200 client and the normal metrics. 6 headless checks pin the mapping (`Win32ChromeMetricsTests`); the Win32 project is 21 passed and the portable suite 485 passed / 1 skipped. The restore animation is unchanged by construction - the style still carries `WS_CAPTION`, which the spike proved is the discriminator - but was not re-measured. |
