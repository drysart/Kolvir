# App badging

Status: in progress. Branch `feature/app-badges`.

What this is: the unread count and the "red dot" on the application's own icon — the taskbar button on
Windows, the Dock tile on macOS, the dock icon on Linux. The inline `XPlatBadge` reserved in
`CONTROLS-TODO.md` is a different feature (a count drawn on a tab or a button).

## 1. The rung: 3

`XPlatUI.Feature.Badges`, one package, the four-folder layout of `FEATURE-COST-POLICY.md` §4.

A badge belongs to the application, not to a window or a control, so there is no call site the
application can name that would keep the code out of the deliverable: `XPlatApplication` is retained
by every application. Rung 1 is therefore unavailable, and rung 2 would put the implementation in the
backend assemblies, which every application of that platform ships whether it shows a badge or not.
What moves into the package is not a few glue lines: it is a Skia-drawn icon on Windows, a D-Bus
protocol on Linux, and the whole state machine. So the feature takes rung 3 and the package is the
opt-in.

The consequence for the surface is §5's rule: a separate assembly cannot add members to
`XPlatApplication`, so everything is an extension block.

**Nothing in the core library or in a backend names this feature.** The backends keep three seams
that describe platform facilities instead (§5).

## 2. What each platform actually offers

| | Windows | macOS | GTK4 (X11 and Wayland) |
|---|---|---|---|
| Count | app-drawn overlay icon | `NSDockTile.badgeLabel`, system-drawn | Unity LauncherEntry `count` |
| Text | app-drawn | any string | — |
| Dot | app-drawn | `badgeLabel = "•"` | — (nearest is `urgent`) |
| Urgent | `FlashWindowEx` | `requestUserAttention`, a Dock bounce | LauncherEntry `urgent` |
| Colour | ours | the system's red, not ours | the dock's |
| Progress | `SetProgressValue` (native on the button) | — (needs a custom `contentView`) | LauncherEntry `progress` |

Measured, not assumed:

- **Win32 has no badge API for an unpackaged application.** `ITaskbarList3::SetOverlayIcon` is the
  whole facility: the shell draws whatever `HICON` we hand it over the taskbar button, at the
  small-icon metric for that window's DPI. Everything a badge looks like is therefore our problem.
- **`ITaskbarList3`'s IID ends `…9E9F8A5EEFAF`, not `…EECAF`.** The wrong one is widely copied and
  fails as `E_NOINTERFACE` while `ITaskbarList4` succeeds, which reads like a broken Windows install
  rather than a typo. The correct value is in `shobjidl_core.h`.
- **macOS is one property**, plus `dockTile.display()` to redraw now, and `null`/empty to clear. The
  badge is a red rounded bubble drawn by the system; long text is clipped, so counts are clamped.
  Two behaviours worth knowing: the dot is a *bubble containing a bullet*, so it is the size of a
  number badge rather than a small dot, and a Dock bounce only happens while the application is not
  the active one — macOS ignores `requestUserAttention` for the frontmost app, which is also the
  right answer for "the user is already looking at us".
- **GTK4 has no urgency API at all.** Neither Ubuntu box exports `gtk_window_set_urgency_hint`; the
  only symbol is GDK's `gdk_x11_surface_set_urgency_hint`, which is X11-only and useless on Wayland.
- **Linux is a D-Bus convention, not a platform API.** Dash-to-dock (`ubuntu-dock`) *owns*
  `com.canonical.Unity` and listens for `com.canonical.Unity.LauncherEntry.Update` **signals** from
  any sender — there is no method to call, which is why a method-style call answers `UnknownMethod`.
  Properties it consumes: `count`, `count-visible`, `progress`, `progress-visible`, `urgent`,
  `updating`, `quicklist`. Verified on GNOME 46/X11 (`linux`) and GNOME 50/Wayland (`linux2`).
- **Stock GNOME Shell implements none of it** (the only references in the shell tree are inside the
  `ubuntu-dock` extension), so on a bare GNOME the feature degrades to nothing. It also *adds* the
  desktop's own notification count to ours unless `applicationCounterOverridesNotifications` is set,
  so our count is a floor rather than the total.
- The signal's first argument is `application://<desktop-id>.desktop`, and the dock matches it to an
  installed desktop entry, which the GTK4 backend's `InstanceKey` already becomes.

## 3. The API

```csharp
using XPlatUI.Badges;

options.ConfigureServices = services => services.AddAppBadges();   // the opt-in, one name per platform

app.Badge.Count = 3;          // unread count; null or <= 0 clears
app.Badge.Text = "99+";       // any short label, where the platform takes one
app.Badge.IsDot = true;       // the red dot, with no number
app.Badge.Urgent = true;      // nudge the user now
app.Badge.Capabilities;       // what this platform honours
```

| Member | Windows | macOS | GTK4 |
|---|---|---|---|
| `Count` | drawn | system bubble | `count` + `count-visible` |
| `Text` | drawn | passed through | unsupported: ignored, `Urgent` set instead |
| `IsDot` | drawn | `"•"` | unsupported: `urgent` |
| `Urgent` | taskbar flash | Dock bounce | `urgent` |
| `Capabilities` | all | all | `Count`, `Urgent` |

Precedence is `IsDot` → `Text` → `Count`; clearing is `Count = null`, `Text = null`, `IsDot = false`
and `Urgent = false`, and `Capabilities` is all-false until `AddAppBadges()` has been called, so an
application that references the package and forgets the opt-in can see the difference.

`Urgent` is edge-triggered on Windows and macOS (a flash, a bounce) and sticky on Linux (the dock
holds the highlight until it is cleared), because those are the platform's own behaviours; the doc
comment says so rather than pretending they match.

Excluded from v1: `Progress`, custom badge artwork, and per-window badges (macOS and Linux only have
an application badge at all).

## 4. Layout

```
XPlatUI.Feature.Badges/
  common/   XPlatAppBadge.cs              the state, the precedence, the capabilities
            XPlatBadgeCapabilities.cs
            IXPlatAppBadgePresenter.cs    what a platform half implements
            XPlatApplicationBadgeExtensions.cs   the extension block
  windows/  Win32BadgeServiceCollectionExtensions.cs   AddAppBadges()
            Win32BadgePresenter.cs       ITaskbarList3, DrawIconEx, one icon per window
            Win32BadgeWindowHook.cs      IXPlatWin32WindowHook: attach at Shown, detach at Disposed
            Win32BadgeIcon.cs            Skia at the small-icon metric → HICON
            Win32BadgeNativeMethods.cs   the COM interface and the GDI calls
  macos/    AppKitBadgeServiceCollectionExtensions.cs
            AppKitBadgePresenter.cs      DockTile.BadgeLabel + RequestUserAttention
  linux/    Gtk4BadgeServiceCollectionExtensions.cs
            Gtk4BadgePresenter.cs        the LauncherEntry signal
```

## 5. Seams the package reaches

| Platform | Seam | Why it is not a backend change |
|---|---|---|
| Win32 | `IXPlatWin32WindowHook`, `Handle`, `RegisterNativeTeardown`, `AddScaleChangedHandler` | Already public: a window's handle and its phases are the platform facility. |
| Win32 | **`AddMessageFilter`** (new) | `TaskbarButtonCreated` cannot be seen from a phase. Added as a generic facility that names no feature. |
| macOS | `NSApplication.SharedApplication` | Process-global, no seam needed. |
| GTK4 | `Gtk.Module.Initialize()`, `Gio.Application.GetDefault()`, `Gio.DBusConnection` | Public GLib/Gio surface, no seam needed. |

### 5.1 The message filter

`Win32WindowBackend`'s window procedure is private and a window has no way to observe a message, so
nothing in a feature package could learn that Explorer re-created a taskbar button, or that a message
arrived that the model does not name. `AddMessageFilter` / `RemoveMessageFilter` close that: a filter
sees every message before the window acts on it and can consume it.

- Runs on the UI thread, in order, over a snapshot, so a filter may add or remove filters while it
  runs and the change applies from the next message.
- `false` is the normal answer; `true` suppresses the window's own handling.
- A filter that throws is reported through the application's unhandled-exception hook and treated as
  `false`; the ones after it still run.
- A window nobody attached to costs one length check per message.

The dispatch rules live in `Win32MessageFilters`, separate from the backend, so they are tested
without a live window — the same split the backend uses for its other decisions.

## 6. Decisions

- **D1 — the badge is application-scoped.** macOS and Linux have no other kind, and Windows groups a
  process's windows under one taskbar button; a per-window API would be a lie on two platforms.
- **D2 — `IsDot` is a member, not `Text = "•"`.** On Linux there is no text badge, and on Windows the
  dot is drawn, so the dot has to be expressible without naming a character. It also reads better at
  the call site.
- **D3 — capabilities, not silent best effort.** The feature reports what the platform honours so an
  application can choose its own fallback instead of showing a badge that never appears.
- **D4 — nothing is written until the opt-in has run.** Without `AddAppBadges()` the setters are
  inert and `Capabilities` is empty.
- **D5 — the Windows badge is drawn by us, at the small-icon metric for the window's DPI**, and
  re-drawn when the window's scale changes.

## 7. Steps

| # | Scope | Done when |
|---|---|---|
| 1 | Prove the Win32 primitives: Skia pixels → `HICON` → `DrawIconEx` round trip, plus `ITaskbarList3` QI and `SetOverlayIcon` on a live window | The round trip returns the drawn pixels and `SetOverlayIcon` answers `S_OK` |
| 2 | `AddMessageFilter` on `Win32WindowBackend`, its dispatch type and tests | Order, consumption, snapshot semantics and lease removal are pinned by tests |
| 3 | `common/`: state, precedence, capabilities, presenter contract, extension block, inert-without-opt-in behaviour | Unit tests cover precedence, clearing and the absent-presenter case |
| 4 | macOS presenter and demo hooks | Count, dot and cleared are visible on the Dock of a running demo |
| 5 | Linux presenter, a desktop entry for the demo, and the signal | A count shows on Ubuntu Dock on both `linux` and `linux2` |
| 6 | Windows presenter, window hook and icon renderer | The taskbar button shows count and dot at the right size, and drops them when cleared |
| 7 | Demo page, README, `APPLICATION-MODEL.md` row, worked example in `FEATURE-COST-POLICY.md` | Docs describe what each platform does and does not do |
| 8 | Verification on all four hosts, `feature-pack` CI, PR | Every OS builds, the suites pass, the badge is seen on each platform |

## 8. Progress

| # | Result |
|---|---|
| 1 | Done. `SetOverlayIcon` answers `S_OK` and the `DrawIconEx` round trip returns the pixels drawn (16/20/24/32 px). Two findings: the shell renders the overlay in the small-icon slot, so the icon must be built at `SM_CXSMICON` for the window's DPI; and the IID typo above, which made `ITaskbarList3` look unsupported. |
| 2 | Done. `AddMessageFilter`/`RemoveMessageFilter` on `Win32WindowBackend`, dispatch in `Win32MessageFilters`, seven tests. |
| 3 | Done. `common/` is the whole model: `XPlatAppBadge` (Count, Text, IsDot, Urgent, Clear), `XPlatBadgeCapabilities`, the resolved `XPlatBadgeState`, the presenter seam and `app.Badge`. Precedence is `IsDot` → `Text` → `Count`; a count of zero or less is no badge; setting the same value twice publishes once; `Clear()` publishes once however many members were set. An unsupported presentation degrades to `Urgent`, and a platform without urgency drops it. Without the opt-in nothing is published and `Capabilities` is empty. 19 tests. |
| 4 | Done. `AddAppBadges()` and `AppKitAppBadgePresenter`: the label is the count's digits, the text as given, or a bullet for the dot, with `dockTile.display()` after every change and `RequestUserAttention` on an urgent transition. Workers are queued on `NSOperationQueue.MainQueue` rather than touching AppKit off-thread. Seen on the Dock of a harness application: `3`, a bullet, `99+`, and cleared. 19 platform-neutral tests still pass; the macOS half has no unit tests because it is a property set on a global AppKit object. |
| 5 | Done. `AddAppBadges()` and `Gtk4AppBadgePresenter`: `com.canonical.Unity.LauncherEntry.Update` every change with `count`, `count-visible` and `urgent`, all three each time because the dock keeps the last value. The desktop id comes from `Gtk4ApplicationBackend.NativeApplication.ApplicationId` — the backend registers itself in the container, so the presenter takes it as a constructor dependency instead of guessing at the id. Verified on X11 (`linux`) and Wayland (`linux2`): capabilities are count+urgent, text and dot degrade to urgent, three signals per run carry `application://com.xplatui.ibadgeharness.desktop`, and the window's `_GTK_APPLICATION_ID` matches the installed entry. Two findings: a presenter's constructor has to be **public** for the service provider to find it (an internal one fails at resolve time), and the dock cannot be photographed from these sessions at all — the shell composites it, so `xwd` misses it, `gnome-screenshot` falls back to X11 and the shell's screenshot method answers `AccessDenied`. |
| 6 | Done. `AddAppBadges()`, `Win32AppBadgePresenter` and `Win32BadgeWindowHook`: the overlay is drawn with Skia at `SM_CXSMICON` for each window's DPI and handed to `ITaskbarList3::SetOverlayIcon`; the hook attaches at `Shown`, releases at `Disposed`, and re-applies on `TaskbarButtonCreated` and `WM_DPICHANGED` through the message filter, which is what that seam was added for. A badge set from a worker is posted to each window and applied on its own thread. Counts cap at `99+` — the first cut truncated the digits, so 1200 drew "12+", which says twelve. Ten pixel tests over the drawing (disc, numeral, dot smaller than a badge, corner clear, every icon size, an empty badge making no handle) and the harness runs the whole sequence with no exceptions; the shell's rendering itself still cannot be photographed here. |
| 7 | Done. The demo's **App badge** page (index 58) sets a count of 3, a count of 150, a dot, a label, an urgent nudge and a clear, and its readout prints what is showing next to what the backend says it can draw - which is what makes Linux honest instead of looking broken, because the headless backend used for the render reports all four false and the page says so. `README.md` gains "App badging" with the precedence rule, a per-platform table, the desktop-entry requirement and a "what the platforms cannot do" list; `APPLICATION-MODEL.md` §10 turns the deferred row into what was built (and keeps notifications and tray deferred); `FEATURE-COST-POLICY.md` §8 gains the rung-3 worked example. The demo ships `com.xplatui.ixplatui.desktop` and `install-badge-desktop-entry.sh`, because the Linux half silently does nothing without an installed entry. `tests.yml` names both badge suites - the portable one in the shared list, the pixel one on the Windows leg - since a suite CI does not name never runs. Verified: the solution builds with no warnings or errors and both suites pass (19 and 10). |
| 8 | Done. Builds with no warnings on Windows, macOS, `linux` (X11) and `linux2` (Wayland), and the sample runs on all four. On Windows and macOS the launch is itself the check of the wiring: `app.Badge` is first read when a window attaches, so a presenter the container could not build would throw there. On Linux the entry installer was run against the sample's build output and the running sample's `_GTK_APPLICATION_ID` read back as `com.xplatui.ixplatui`, which is the name of the file the installer writes - the one link step 5 could only test with a harness. Suites: 19 portable, 10 Windows, 53 in the Win32 backend, and the five other feature suites, all green locally and in CI. One thing CI taught: this is the first feature suite that is not portable, and an unnarrowed restore of it evaluates the feature's macOS inner build and dies for want of a workload the job deliberately does not install (`NETSDK1147` from `XPlatUI.Backend.AppKit`, before anything is compiled). `-p:TargetFramework=net10.0-windows` is worse, not better: it overrides `XPlatUI` too, which is `net10.0`, and leaves assets restored for one framework and built for another. So the feature takes a scoped `-p:XPlatUIBadgeFrameworks=` that nothing but it reads. Five checks green on PR #64. Not photographed, as section 9 says: the Windows taskbar and the GNOME dock. |

## 9. Risks

- **Explorer restarts** clear an overlay icon; the message filter is what puts it back.
- **Stock GNOME shows nothing.** The demo has to explain that rather than look broken, and the
  Linux half has to be honest in `Capabilities`.
- **A running application with no desktop entry** cannot be matched by the dock, so the demo needs
  one installed, or the Linux badge silently does nothing.
- **The dock cannot be seen from a test session.** GNOME composits its own UI, so `xwd`/`x11grab`
  capture the application windows but not the panel or the dock, `gnome-screenshot` falls back to X11
  and finds no display, and the shell's screenshot method answers `AccessDenied`. Linux is therefore
  verified by watching what the application broadcasts and what the window claims to be, and the
  pixels wait for a real desktop.
- **macOS cannot colour the badge**, so a theme-aware badge is not portable and is not offered.
