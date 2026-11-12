# Kolvir

A tiny, AOT-friendly, cross-platform UI layer that borrows the WinForms/WPF programming
model (controls, docking, table layout, events) but draws everything with
[SkiaSharp](https://github.com/mono/SkiaSharp). The library itself has no OS dependency: a backend
supplies the window, the input, the clipboard, the dialogs and the system appearance, and the
controls draw themselves on top of it. The same sample UI runs on all three platforms.

```
Kolvir/                   net10.0 class library (no OS-specific code, IsAotCompatible)
  Application/            Application, Window, Icon, options, DI wiring, dialogs
  Core/                   primitives: control base class, geometry, painting, fonts, theme, clipboard
  Controls/               Panel, StackPanel, TableLayoutPanel, SplitContainer, GroupBox, ScrollBar, Label, RichLabel, Button, CheckBox, RadioButton, ProgressBar, TextBox, VirtualList, ListBox, ListView, TreeView, ComboBox, TabControl, TabPage, ToolTip, Image
  Layout/                 row/column sizing styles
  Menus/                  menu bar, menu, menu item, and the context menu contract
  Popups/                 the popup contract (IPopupService) and Popup, which floats a control above its window
  Platform/               the backend contract: IApplicationBackend, IWindowBackend, ...
  Selection/              text selection and its clipboard plumbing
  Services/               window-scoped service helpers
  Native/                 SkiaNativeLoader.cs - unpacks the embedded Skia library in an AOT build
  buildTransitive/        the MSBuild that embeds it, shipped with the package
  UiHost.cs               platform-neutral host: layout, painting, input routing, focus
Kolvir.Demo.App/          the one application: net10.0-windows, net10.0-macos or net10.0
  Program.cs              pick a backend, describe a window, run
  DemoUi.cs               the sample layout, plus RenderToPng for headless renders
  DemoIcon.cs             the sample icon, drawn in code so no binary asset is needed
  app.manifest            per-monitor v2 DPI awareness (Windows)
  Info.plist              the bundle identity (macOS)
Kolvir.Backend.Win32/     net10.0-windows backend over raw Win32 (no WinForms, no System.Drawing)
Kolvir.Backend.AppKit/    net10.0-macos backend over AppKit
Kolvir.Backend.Gtk4/      net10.0 backend over GTK4
Kolvir.Backend.Headless/  net10.0 backend that paints offscreen; what --render and the tests use
Kolvir.Backend/           multi-targeting selector: the backend for whichever TFM you built for
Kolvir.Feature.DragDrop/  drag and drop, as a separate package: one project, one TFM per platform
  common/                 the portable half: AllowDrop, the handler pairs, the router
  windows/                the opt-in, its hook and the OLE IDropTarget they register
  linux/                  the opt-in, its hook and the GtkDropTarget controllers they add
  macos/                  the opt-in, its hook, and the sink it installs on the view (below)
Kolvir.Feature.Animations/ timeline animations, as a separate package: same layout, all portable
  common/                 the set, the value animations, the easings and the app-wide frame clock
  windows/ macos/ linux/  empty: an animation is arithmetic, a clock and an invalidation
Kolvir.Feature.Svg/       SVG images, as a separate package: same layout, all portable
  common/                 the format provider, the parse cache, the currentColor load helpers
  windows/ macos/ linux/  empty: Svg.Skia renders to an SKPicture on every platform
Kolvir.Minimal/           the measurement fixture: one window, one label, no features
```

`Kolvir.Demo.App` builds exactly **one** target framework per operating system, chosen by
`$([MSBuild]::IsOSPlatform(...))`: `net10.0-windows` on Windows, `net10.0-macos` on macOS and
`net10.0` on Linux. That is deliberate rather than tidy. `net10.0-macos` needs the macOS workload,
and the `.app` it produces needs a Mac with the matching Xcode, so it must never appear in a TFM
list that Windows has to evaluate; and a portable `net10.0` build *on a Mac* defines neither
`WINDOWS` nor `MACOS`, so it would silently compile the GTK4 branch and produce a Linux binary.
One TFM per OS is what keeps `Program.cs`'s `#if` blocks honest.
See [On macOS (AppKit)](#on-macos-appkit).

Building `Kolvir.slnx` needs the macOS workload, because the solution lists
`Kolvir.Backend.AppKit` and `Kolvir.Feature.DragDrop`, which both target `net10.0-macos`.
`dotnet workload install macos` satisfies it where it is not already present; a build without it
stops with `NETSDK1147`. Nothing else in the solution needs a workload, and an application that
references either project still builds only the one inner build its own framework can use.

`Kolvir.Backend` is the way out of that `#if` for an application that wants nothing more than a
default backend. It is a project that multi-targets and references exactly one backend per target
framework, so referencing *it* is enough: the framework the application builds for decides which
backend it gets, and a publish carries only that one. `KolvirBackend.CreateDefault()` returns the
backend and loads the embedded Skia library on the way, so `Main` names no platform and no graphics
library. `Kolvir.Minimal` is written that way. The selector's framework list is unconditional,
because a selector that offered only the host's framework could not be packed for the others;
restoring it therefore evaluates all three, and building the fixture needs the macOS workload on
every platform, not just on the Mac.
has to read `--classic-menus` before it can build a backend.

## Design

| Concept | WinForms equivalent | Notes |
| --- | --- | --- |
| `Control` | `Control` | Bounds, `Margin`/`Padding`, `Dock`, `Visible`/`Enabled` (a disabled container disables its subtree) with `EffectiveVisible`/`IsDisplayed` answering for the whole chain, `Font`/`Foreground`/`Background` inherited down the tree, focus, mouse/keyboard events; any control can host children |
| `Background` | `BackColor` | The background state: inherit, paint nothing, or a colour or palette role - three states, because "no value" and "draw nothing" are not the same thing |
| `UiHost` | the window/`Form` | Owns the root control, lays out, paints into an `SKCanvas`, routes input, tracks hover/capture/focus |
| `Panel` | `Panel` | Docking layout, optional frame/card chrome, `None`/`Automatic`/`Always` scrolling per axis, and `ScaleFactor` to zoom the subtree |
| `TableLayoutPanel` | `TableLayoutPanel` | Absolute / Percent / AutoSize rows and columns, `CellPadding`, per-cell positions |
| `SplitContainer` | `SplitContainer` | Two panels with a draggable splitter, fixed/min/max sizes |
| `WrapPanel` | `FlowLayoutPanel` (wrapping) | Items flow along one axis and start a new line at the available size, with a gap between items and lines |
| `GroupBox` | `GroupBox` | Labelled frame: the caption sits in a break in the top edge and the content is laid out below it |
| `Expander` | - | Collapsible section: a header with a chevron and the content folded away under it; `IsExpanded` folds the section to its header and puts it back |
| `ScrollBar` | `ScrollBar` | Classic non-overlay bar: arrows, track paging, draggable thumb, keyboard |
| `Label` | `Label` | Nine-way alignment, optional word wrap |
| `LinkLabel` | `LinkLabel` | A rich label styled as a link that hands its URI to the desktop through `ILauncher` |
| `RichLabel` | `RichTextBox`/`FlowDocument` | Wrapping runs of styled text with interactive spans and inline child controls |
| `Button` | `Button` | Primary/Secondary/Ghost styles, hover/press/disabled states, Enter/Space activation |
| `SplitButton` / `DropDownButton` | `ToolStripSplitButton` / `ToolStripDropDownButton` | One button that is both a command and a menu: the main area raises a click and the arrow opens `Menu` in a popup; a drop-down button opens the menu from its whole face |
| `CheckBox` | `CheckBox` | Square glyph plus label; two-state by default, `ThreeState` adds the indeterminate state |
| `RadioButton` | `RadioButton` | Round glyph; one checked per group, grouped by parent or by `GroupName`, arrow keys move the selection |
| `ProgressBar` | `ProgressBar` | Continuous/Blocks/Marquee fill between `Minimum` and `Maximum`; only a marquee bar runs a timer, at the display's refresh rate by default |
| `BusyIndicator` | - | An indeterminate spinner: a ring with a rotating arc, on a timer that runs only while it is on screen and busy |
| `Overlay` | - | A modal layer: dims what is under it, takes every press and swallows every key while it shows |
| `Toast` | - | A transient message bar: an accent bar coloured by severity, a message and a close glyph, dismissed by a countdown or by the user |
| `TextBox` | `TextBox` | Single-line editor: caret, selection, clipboard, `MaxLength`, `ReadOnly`, password masking, placeholder |
| `TextBox` + `Kolvir.AutoComplete` | `TextBox.AutoCompleteMode` | `AutoCompleteMode`, `AutoCompleteSource` and `AutoCompleteCustomSource` as an opt-in extension: a filtered suggestion list in a popup under the caret, inline append, and a bounded history of committed values |
| `TextBox` + `Kolvir.Multiline` | `TextBox.Multiline` | `Multiline` and `WordWrap` as an opt-in extension: soft-wrapped visual lines, vertical scrolling, caret movement by visual line with a preferred column, and selection across lines |
| `TextBox` + `Kolvir.Spans` | `TextBox` (spell-check marks) | `Spans` as an opt-in extension: caller-owned annotated runs drawn as a squiggly underline, wrapped and scrolled with the text, and the span a right-click landed in reported on the context-menu event args |
| `TextBox` + `Kolvir.Undo` | `TextBox.UndoEnabled` | `UndoEnabled`, `CanUndo`/`CanRedo`, `Undo()`, `Redo()` and `ClearUndoStack()` as an opt-in extension: a coalescing history (five-second runs of typing or deleting, one entry per selection replacement) that records the caret, the selection and a multi-line field's scroll offsets with each entry |
| `NumericUpDown` | `NumericUpDown` | A text box with a spinner: `decimal` value, `Minimum`/`Maximum`/`Increment`/`DecimalPlaces`, arrow-key, wheel and held-button stepping |
| `TrackBar` | `TrackBar` | Horizontal or vertical track filled to the knob, tick marks, drag and click-to-page, arrow/`Home`/`End`/`PageUp`/`PageDown` keys |
| `ColorPicker` | `ColorDialog` (the picker half) | Spectrum square with saturation across and value or lightness down, a hue strip beside it, three component fields and a hex field; `ColorMode` switches the fields between RGB, HSL and HSV without changing the colour |
| `VirtualList` | - | The base the row lists share: virtualization, scrolling, selection, type-ahead and the visible-band paint loop |
| `ListBox` | `ListBox` | Row list with all four `SelectionMode`s, keyboard navigation, type-ahead and optional scrollbars |
| `ListView` | `ListView` | `List` and `Details` views over the same virtualized rows: columns, header-click sorting, grid lines |
| `TreeView` | `TreeView` | Indented nodes with expanders and optional check boxes; the flattened visible nodes are the rows |
| `ComboBox` | `ComboBox` | Edit field plus a drop-down list shown in a popup, so it can leave the window |
| `DateTimePicker` | `DateTimePicker` | Editable date and time field with a calendar in a popup: `Value`/`MinDate`/`MaxDate`, four formats, segment stepping and culture-aware typed parsing |
| `TabControl` / `TabPage` | `TabControl` / `TabPage` | Tab strip with a scrolling overflow, one page visible at a time |
| `ToolTip` | `ToolTip` | One instance serves many controls; shows after a hover delay in a popup that does not take input |
| `ToolStrip` / `StatusStrip` | `ToolStrip` / `StatusStrip` | A docked bar of *items* - button, label, separator, spring, progress bar - painted in one pass and taken as a single tab stop |
| `MenuView` | `ContextMenuStrip` (the surface) | The portable menu: rows, hover pill, check/icon columns, shortcut hints, submenus in popups of their own and real controls hosted in a row. No platform code |
| `MenuBarView` | `MenuStrip` | The menu bar drawn inside the window, above the root control; reached with `Alt` plus a mnemonic and switched by hovering |
| `MenuPresenter` | `ToolStripDropDown` plumbing | Shows an `ContextMenu` in an `Popup` with `MenuView`, on every backend, one popup per level of the chain |
| `MenuPresentation` | - | Chooses the library's menus or the platform's, per application |
| `Image` | `PictureBox` | Draws an `ImageData` - a still picture or an animation, which are the same thing with different durations: `Source`, `SizeMode` (Normal/StretchImage/CenterImage/Zoom), `ImageAlign`, `PlaceholderColor`. It decodes nothing and owns nothing: `IImageDataFactory` loads, the caller disposes |
| `Theme` / `Palette` | `SystemColors` / `Application.SetColorMode` | Every default colour is a palette role, so the whole UI can switch light/dark at runtime |
| `Font` | `Font` | A size plus a face: resolved by family name, or loaded from a file, stream or byte span, so a pinned `.ttf` measures identically on every platform |
| `Theme.UseFont` | `Font` / `PrivateFontCollection` | One line makes a loaded face the application's font: the theme fonts are rebuilt from it at their own sizes |
| `IDialogService.PickFont` | `FontDialog` | The platform's own font chooser: returns an `FontSelection` (family, size, weight, italic) or null when cancelled; the caller creates and owns the `Font` |
| `ColorDialog` | `ColorDialog` (the dialog half) | A modal window around `ColorPicker` with OK and Cancel: library-drawn on every platform, returns the chosen `SKColor` or null, preserves the caller's alpha without offering an alpha control |
| `SystemTheme` | `Application.ColorMode` | The seam a backend implements to report the OS appearance |
| `ILauncher` | `Process.Start` with `UseShellExecute` | The seam a backend implements to open a URI: `ShellExecute`, `NSWorkspace.OpenUrl`, `Gtk.UriLauncher` |
| `Kolvir.Tests` | a test project | One portable `net10.0` MSTest assembly that drives the library through the headless backend, so a control is exercised with no display; a test that can only run on one platform carries a `Platform.*` category rather than being skipped by accident |
| `AnimationSet` + `Kolvir.Animations` | a hand-written storyboard, or `System.Windows.Media.Animation` | `Start()`/`Pause()`/`Resume()`/`Restart()`/`End()` and one finished handler, as an opt-in package: one or more value animations on a shared timeline, eased by the factories in `Easing`, advanced by a single application-wide frame clock that runs only while something is animating |

Every feature is held to [FEATURE-COST-POLICY.md](FEATURE-COST-POLICY.md): anything an application
does not use has to cost it close to zero in the trimmed executable. A feature reaches that by
construction where it can, by an opt-in registration where it cannot, and by its own package where
neither is practical; section 8 of the policy has the measured sizes, including what opting into drag and
drop costs on each platform.

Controls that do not exist yet, and the order they are worth building in, are tracked in
[CONTROLS-TODO.md](CONTROLS-TODO.md).

### Running the tests

`Kolvir.Tests` is the portable suite. It targets `net10.0`, references the library and the
headless backend, and needs no display, window server or network, so the same command works on
Windows, macOS and Linux:

```
dotnet test Kolvir.Tests/Kolvir.Tests.csproj
```

Nothing in the suite needs a desktop. A check that does - a real taskbar flash, a Dock bounce, a
window that has to lose and regain focus - has to run on a host, and every host is described in
[TESTING-RUNBOOK.md](TESTING-RUNBOOK.md): its versions and use criteria, its display, and the
screenshot and input-injection routes that actually work on it.

Three things keep the run deterministic, and a test that breaks one of them is a bug to fix rather
than a flake to retry:

- **Time is driven, not waited for.** `UndoClock.Current` is a `TimeProvider`, and
  `HeadlessScene`-based tests install a clock that only moves when the test calls `Advance`. There
  is no `Thread.Sleep` in the suite and no assertion that depends on wall-clock time.
- **The font is pinned.** `TestFont` loads the demo application's embedded IBM Plex Sans from
  `Kolvir.Demo.App/Fonts`, so a measured width, a wrap point or a caret position is the same
  number on every platform. Tests that assert a metric pin it; tests that assert the default
  behaviour do not.
- **Nothing is shared.** The theme, the clipboard, the undo clock and the menu presenter are
  static by design, so `HeadlessTestBase`'s cleanup restores them after every test and the suite
  is not parallelised.

Tests are categorised with `[TestCategory]`, and the categories are the contract the CI matrix
filters on:

| Category | Meaning |
| --- | --- |
| `Core` | Pure library behaviour: geometry, fonts, layout maths, defaults. Runs everywhere. |
| `Headless` | Needs an application, a window or the host, driven through the headless backend. Runs everywhere. |
| `Pixels` | Samples raster output, restricted to solid fills and semantic colours so it is stable across the three rasterisers. Runs everywhere. |
| `Platform.Win32`, `Platform.AppKit`, `Platform.Gtk4` | Can only pass on one platform. The portable CI job skips these; a platform job can select them. |
| `Feature`, `DragDrop` | The drag and drop feature's own suite. Runs everywhere. |
| `Feature`, `Animations` | The animations feature's own suite. Runs everywhere. |
| `Feature`, `Svg` | The SVG feature's own suite. Runs everywhere. |
| `AnimatedGif` | The animated GIF feature's own suite. Runs everywhere. |
| `AnimatedWebp` | The animated WebP feature's own suite. Runs everywhere. |

To run exactly what CI runs:

```
dotnet test Kolvir.Tests/Kolvir.Tests.csproj \
  --filter "TestCategory!=Platform.Win32&TestCategory!=Platform.AppKit&TestCategory!=Platform.Gtk4"
```

A rung-3 feature is a package of its own, so its tests are an assembly of their own:

```
dotnet test Kolvir.Feature.DragDrop.Tests/Kolvir.Feature.DragDrop.Tests.csproj \
  -p:TargetFramework=net10.0

dotnet test Kolvir.Feature.Animations.Tests/Kolvir.Feature.Animations.Tests.csproj \
  -p:TargetFramework=net10.0

dotnet test Kolvir.Feature.Svg.Tests/Kolvir.Feature.Svg.Tests.csproj \
  -p:TargetFramework=net10.0

dotnet test Kolvir.Feature.AnimatedGif.Tests/Kolvir.Feature.AnimatedGif.Tests.csproj \
  -p:TargetFramework=net10.0

dotnet test Kolvir.Feature.AnimatedWebp.Tests/Kolvir.Feature.AnimatedWebp.Tests.csproj \
  -p:TargetFramework=net10.0
```

All six portable suites are what the `Tests` workflow dispatches, on all three platforms.

The Win32 backend has a Windows-only suite of its own, because its fallback decision is internal
and its target framework is `net10.0-windows`:

```
dotnet test Kolvir.Backend.Win32.Tests/Kolvir.Backend.Win32.Tests.csproj
```

The `Tests` workflow runs it on the Windows leg only. It drives the GL fallback decision with a
probe that always fails, which is what a machine with no OpenGL ICD looks like; the accelerated
path needs a real driver and is verified by hand through the demo's measured-rendering page.

`Kolvir.Feature.Animations.Tests` is wired the same way and for the same reasons - `Feature` and
`Animations` categories, the same filter, plain `net10.0` on every platform, and the same
`-p:TargetFramework=net10.0` narrowing, because the feature lists all three frameworks
unconditionally for `dotnet pack`. What its tests drive is the same code production runs: a
`TestClock` the test advances and a `ManualFrameSource` the test fires are handed to a real
`AnimationScheduler`, so the timeline's arithmetic and the clock's start and stop edges are
exercised exactly rather than approximately, and nothing sleeps. The one thing the suite cannot show
is a real timer waking a real window, which is what the demo's **Animations** page is for.

`Kolvir.Feature.DragDrop.Tests` uses the same categories, plus `Feature` and `DragDrop`, and the
same filter. It targets plain `net10.0` on every platform even though the feature itself is split by
platform: the tests only touch the feature's `common/` folder, and a test project that targeted
`net10.0-macos` would need the .NET for macOS SDK with an exactly matching Xcode on whichever Mac
builds it, which is a requirement no test project should impose.

`-p:TargetFramework=net10.0` is what keeps that promise on a machine that has no macOS workload.
`Kolvir.Feature.DragDrop` lists `net10.0-windows;net10.0-macos;net10.0` unconditionally, because one
`dotnet pack` has to produce all three dependency groups, and restore follows every framework in the
reference graph rather than only the inner build the tests bind. Restoring or testing the test
project without the property therefore resolves the `net10.0-macos` inner build, finds
`Kolvir.Backend.AppKit` behind it and stops with `NETSDK1147`. The property selects the single
portable inner build instead; the plural `-p:TargetFrameworks=` does not work as a switch, because it
reaches the restore graph and not the referenced projects' builds. The `Tests` workflow passes it on
both its restore and its test step, so a local run with it matches CI.

That suite drives the feature's router and per-control state directly, and the feature project grants
it `InternalsVisibleTo` for exactly that. Both types are internal on purpose - an application never
names them and the package ships no public API for them - but the behaviour they carry is real
behaviour (which control a drag reaches, when it is told the drag arrived or left, what effect the
target settles on) and it regresses silently if nothing watches it. The grant is one `ItemGroup` on
the feature project, names only `Kolvir.Feature.DragDrop.Tests`, and adds no public API for testing;
`Kolvir.Tests` and the core library are untouched. What still cannot be tested here is the native
half - a real drag from another application - which is what the demo application exercises.

The suite deliberately has no committed golden images. Rasterisation of text differs by a fraction
between platforms even with the font pinned, so a committed image would either have to be pinned to
one platform or be fuzzy in a way that hides real changes; the pixel tests that do exist sample a
solid fill whose colour a palette role decides.

Everything is drawn in **logical (96 DPI) coordinates**; the host adapter is responsible
for scaling the canvas and the incoming pointer coordinates (the Win32 backend uses
`GetDpiForWindow() / 96f`).

## Using it

```csharp
var host = new UiHost { ClearColor = SKColors.White };

var root = new Panel { BackColor = Theme.Background, Padding = new Thickness(16) };
var table = new TableLayoutPanel { Dock = DockStyle.Fill };
table.ColumnStyles.Add(ColumnStyle.Percent(50));
table.ColumnStyles.Add(ColumnStyle.Percent(50));
table.RowStyles.Add(RowStyle.AutoSize());
table.RowStyles.Add(RowStyle.Percent(100));

var button = new Button("Say hello") { Dock = DockStyle.Fill };
button.AddClickHandler(_ => Console.WriteLine("clicked"));

table.Add(new Label("Hello") { AutoSize = true }, 0, 0);
table.Add(button, 1, 1);
root.Controls.Add(table);

host.Root = root;
host.SetClientSize(800, 600);   // lays out the tree
host.Paint(canvas);             // any SKCanvas: window surface, bitmap, PDF, ...
```

Input is plain method calls, so any backend can drive it:

```csharp
var pt = new SKPoint(x, y);                    // logical coordinates
host.ProcessMouseMove(pt);
host.ProcessMouseDown(pt, MouseButtons.Left);
host.ProcessMouseUp(pt, MouseButtons.Left);
host.ProcessKeyDown(Key.Tab);             // cycles focus
host.ProcessTextInput("a");
```

`host.AddInvalidatedHandler` tells the adapter to repaint; `host.AddCursorChangedHandler` lets it
update the native cursor.

### Focus and tab order

`Tab` and `Shift+Tab` walk the controls that are both `TabStop` and `Focusable`, skipping anything
invisible or disabled and anything inside an invisible or disabled container. The order is the one
WinForms has, because an application author coming from it will expect it:

- **Per container.** Each container visits its own children in `TabIndex` order, so a control inside
  a nested panel is ordered against its siblings there and never against the controls outside that
  panel. The lowest index is visited first; the default, 0, leaves the children in the order they
  were added.
- **Equal indices keep tree order.** Two controls with the same `TabIndex` are visited in the order
  they were added, so the walk is deterministic rather than dependent on how a sort permutes equals.
  With every index at its default that makes the whole walk plain tree order.
- **A focusable container is one step.** A container that is itself a `TabStop` - a split container,
  for instance - takes its own place among its siblings, and its children are visited straight after
  it: index 1 on a container puts the container, then its contents, between index 0 and index 2.
- **It wraps.** `Tab` at the last stop goes back to the first, and `Shift+Tab` at the first goes to
  the last; `host.FocusedControl` is the control the walk landed on.
- **A container whose content is not showing takes no stops.** A collapsed `Expander` is the
  case: its section is still in the tree - folding is a matter of how much is drawn, not of hiding -
  so its children are left out of the walk even though they are `Visible`. The container answers
  this itself, by overriding `IsContentReachable`, so any container that arranges its children to
  nothing is treated the same way. `SetFocus`, and so `Focus()`, refuse such a control as well: a
  control the user cannot see cannot be focused by anything.

```csharp
var name = new TextBox { TabIndex = 0, Dock = DockStyle.Top };
var notes = new TextBox { TabIndex = 2, Dock = DockStyle.Fill, Multiline = true };
var ok = new Button("OK") { TabIndex = 1, Dock = DockStyle.Bottom };   // visited second
```

The demo's **Tab order** page - `--page 34` - is the same rules by hand: it was created in a
different order from its indices, carries a nested container whose children order among themselves,
a scrollable panel that is a tab stop, two controls that share an index, and two that are meant to be
skipped, with a readout naming whatever has the focus, its `TabIndex` and its place in the sequence.

### Visibility and display state

`Visible` is the flag a control owns, and like `Enabled` it is only one of the answers that matter:
hiding a container hides everything inside it, and a window that was never shown shows nothing at
all. Two computed properties carry the rest, so a control that does work on a timer never has to
watch its ancestors itself:

| Member | Meaning |
| --- | --- |
| `Visible` | The control's own flag; setting it is what an application does |
| `EffectiveVisible` | That flag and every container above it - the answer to "would a paint reach this control" |
| `IsDisplayed` | `EffectiveVisible` and attached to a host and inside a window that has been shown and has not closed - the answer to "would the user see it" |
| `OnDisplayedChanged()` | Called on a control, and then down its subtree, when either answer changes: `Visible` was assigned, the control changed parent, the root was attached or detached, or the window was shown or closed |

The marquee progress bar and the caret are the two controls whose clocks hang off this: they start
when the control becomes displayed and stop the moment it stops being, including when only a
container or the window changed, with no timer left running for a window nobody is looking at.

```csharp
var busy = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee };
busy.IsDisplayed;      // false until the window is shown
card.Visible = false;  // the sweep stops; the bar is still Visible itself
card.Visible = true;   // and starts again from the phase it stopped at
```

`IsDisplayed` is about being shown, not about being on-screen: a minimized or fully covered window
still counts as displayed, because no window system reports occlusion to a client portably.

### Handlers

There are no C# events. Every notification is a pair of methods, `Add<Name>Handler` and
`Remove<Name>Handler`, so a subscription is a plain method call and the delegate behind it stays
private to the library:

```csharp
Action<object, MouseEventArgs> onDown = (sender, e) => Press(sender, e.Location);

IDisposable subscription = control.AddMouseDownHandler(onDown);
control.RemoveMouseDownHandler(onDown);      // the same callback: unsubscribes it
subscription.Dispose();                      // or hand the token around instead
```

The handler's first argument is the object that raised it, which is the same object `Add` was called
on. A second argument carries the payload where there is one: `Action<object>` when there is not,
`Action<object, TPayload>` when there is, with the args class the WinForms event would have carried.
Handlers run on the UI thread, in the order they were added, and a handler that throws stops the
ones after it and reaches the `AddUnhandledExceptionHandler` handlers.

`Add` returns an `IDisposable` token that removes the handler when it is disposed, so a scope can own
a subscription without storing the callback. A caller that already holds the callback can pass it to
`Remove` and skip the token.

### Wrap panel

`WrapPanel` lays its children out along one axis and starts a new line whenever the next one
would not fit - rows left to right that wrap downwards, the default, or columns top to bottom that
wrap to the right. It is the layout a tag row, a group of buttons or a thumbnail grid wants, which
`StackPanel` cannot do at all and `TableLayoutPanel` can only do with the columns known up
front.

| Member | Behaviour |
| --- | --- |
| `Orientation` | `Horizontal` (rows that wrap down) or `Vertical` (columns that wrap right) |
| `Spacing` | The gap between two items on a line and between two lines; margins apply on top of it |
| `HorizontalAlignment` / `VerticalAlignment` on each child | Across the line: `Stretch` (the default) fills the line's breadth, the others keep the item's own size at that edge or in the middle |

A line is as broad as its broadest item, and that is only known once the whole line is measured, so
a pass measures the run first and places it second. An item wider than the panel is not lost: it
takes a line of its own and overflows, which is what makes the scrollbar appear. Docked children are
not part of the run - they take their edge first, the way they do in any panel - so a header or a
footer sits above or below the flow.

```csharp
var tags = new WrapPanel { Dock = DockStyle.Fill, Spacing = 6f };
foreach (var tag in new[] { "design", "layout", "skia", "cross-platform" })
{
    tags.Controls.Add(new Button(tag) { AutoSize = true });
}
```

### Split container

`SplitContainer` hosts exactly two panels with a draggable splitter between them.
Orientation follows the WinForms convention:

| `Orientation` | Layout | `SplitterDistance` is | Cursor |
| --- | --- | --- | --- |
| `Vertical` | `Panel1` left, `Panel2` right | `Panel1` width | `SizeWE` |
| `Horizontal` | `Panel1` top, `Panel2` bottom | `Panel1` height | `SizeNS` |

`Panel1` is the sized panel; `Panel2` always fills the remaining space. The distance is
clamped to `Panel1MinSize` ... `Panel1MaxSize`, further limited by `Panel2MinSize` so the
second panel never collapses.

```csharp
var split = new SplitContainer
{
    Dock = DockStyle.Fill,
    Orientation = Orientation.Vertical,   // side by side
    SplitterDistance = 240f,                   // width of Panel1
    SplitterWidth = 6f,
    Panel1MinSize = 120f,
    Panel1MaxSize = 420f,
    Panel2MinSize = 180f,
};

split.Panel1.Controls.Add(new Label("Navigation"));
split.Panel2.Controls.Add(content);
split.AddSplitterMovedHandler(_ => Console.WriteLine($"panel 1 is now {split.SplitterDistance:0} px"));
root.Controls.Add(split);
```

Dragging is handled internally with mouse capture, so the pointer keeps resizing the panel
even when it strays over the panels or outside the control. The splitter highlights on
hover, drag and focus, and can be moved with the arrow keys (`Left`/`Right` for a vertical
split, `Up`/`Down` for a horizontal one) when the container has keyboard focus. Set
`IsSplitterFixed` to lock it, and `SplitterColor`/`ShowSplitterGrip` to restyle it.
`AddSplitterMovingHandler` runs continuously during a drag; `AddSplitterDistanceChangedHandler` runs
whenever the distance changes for any reason.

### Stack panel

`StackPanel` lays its children out one after another along a single axis and sizes each one
across that axis, which is the flow layout a plain panel does not have:

| `Orientation` | Children run | Each child is stretched to the panel's |
| --- | --- | --- |
| `Vertical` (default) | top to bottom | width |
| `Horizontal` | left to right | height |

A child gets its own preferred size along the stack and fills the panel across it, so a stack of
buttons is a stack of full-width rows. `Spacing` is the gap between children; there is none before
the first or after the last.

```csharp
var stack = new StackPanel
{
    Dock = DockStyle.Fill,
    Spacing = 8f,
    Padding = new Thickness(12f),
    ShowFrame = true,            // a stack panel draws no frame of its own by default
};

stack.Controls.Add(new Button("Save"));
stack.Controls.Add(new Button("Cancel"));
root.Controls.Add(stack);
```

`HorizontalAlignment` on a child of a vertical stack, or `VerticalAlignment` on a child of a
horizontal one, opts out of the stretch and leaves the child at its own size across the stack:

| Value | Placed |
| --- | --- |
| `Stretch` | filling the stack; the default |
| `Near` | at the left, or the top |
| `Center` | in the middle |
| `Far` | at the right, or the bottom |

```csharp
stack.Controls.Add(new Button("Centred") { HorizontalAlignment = Alignment.Center });
```

- A child with `AutoSize = false` keeps the size it was given along the stack, which is how a run of
  fixed-height rows is built; it is still stretched across the stack unless it is aligned.
- `Margin` still applies: it separates one child from the next along the stack and insets it from
  the edges across it, so `Spacing` is for the gap and `Margin` for a per-child adjustment.
- A docked child is not part of the run. It takes its edge of the panel the way a docked child of
  any panel does and the stack fills what is left, which is how a header or a footer is added:

```csharp
stack.Controls.Add(new Label("Header") { Dock = DockStyle.Top, AutoSize = false, Height = 24f });
```

- The run of children is the scrollable content, so `VerticalScroll = ScrollStyle.Automatic`
  scrolls a long stack; a docked header stays put while the rows scroll under it.
- With `AutoSize = true` the stack is as long as the run and as wide as its widest child.
- A child wider than the space across the stack is clipped to the panel rather than overflowing it,
  so `Center` and `Far` never push a child outside the control.

### Scrolling

`Panel` scrolls each axis independently. `HorizontalScroll` and `VerticalScroll` take an
`ScrollStyle`, and both default to `None`, so a panel that does not ask for scrolling pays
nothing for it:

| Style | Layout | Behaviour |
| --- | --- | --- |
| `None` | No gutter | Children outside the client area are clipped and cannot be reached |
| `Automatic` | Gutter appears only when needed | Shown while the content is larger than the viewport |
| `Always` | Gutter always reserved | The bar is shown even when the content fits, and is disabled |

The bar is the classic, non-overlay style: it takes a `ScrollBarThickness` (16 px by default)
gutter out of the client area, and the remaining area is `ViewportRect`. Content is measured from
the top-left of the viewport into `ScrollExtent`, and the visible bars are `HorizontalScrollBar` /
`VerticalScrollBar` if you need to restyle or drive them.

```csharp
var list = new Panel
{
    Dock = DockStyle.Fill,
    VerticalScroll = ScrollStyle.Automatic,
    Padding = new Thickness(4f),
};

for (int i = 0; i < 20; i++)
{
    list.Controls.Add(new Button($"Row {i}")
    {
        Bounds = new SKRect(4f, 4f + (i * 28f), 200f, 32f + (i * 28f)),
    });
}

list.ScrollTo(0f, 120f);                    // absolute, clamped to the reachable range
list.ScrollBy(0f, -16f);                    // relative
list.ScrollControlIntoView(list.Controls[7]);
root.Controls.Add(list);
```

Scrolling is a paint-time translation, so children keep the `Bounds` you gave them and a scrolled
panel moves them as a whole: hit testing, focus, selection and drag and drop all follow, because
`UiHost` maps coordinates through the same offset. Two rules follow from that:

- Only children whose `Dock` is `DockStyle.None` contribute to `ScrollExtent`. A docked child is
  pinned to the viewport and never scrolls, which is what you want for a header or a filter row.
- Content is measured from the top-left of the viewport, so a child positioned to the left of or
  above it is clipped rather than scrolled to. Lay content out from the origin.

`ScrollBar` is also usable on its own, exactly like the WinForms `ScrollBar`. It reports
`Minimum`, `Maximum`, `Value`, `SmallChange` and `LargeChange`, and distinguishes a change the user
made (`AddScrollHandler`, raised by the arrows, the track, the thumb and the keyboard) from one the
application made (`AddValueChangedHandler`, raised by any change at all). Setting `Value` from code
moves the thumb and raises only the second.

The mouse wheel scrolls the control under the pointer. `UiHost.ProcessMouseWheel` starts at the
captured control, or the control under the pointer, and walks up the parent chain until something
claims the turn, so a wheel event over a nested list scrolls the list and only reaches the panel
around it once the list is at its end. `MouseEventArgs.Delta` is the distance in logical
pixels, positive toward the start of the content (up or left); a backend that reports a wheel in
detents multiplies by `UiHost.WheelPixelsPerDetent`, one that reports lines multiplies by its
own platform's line height - macOS reports a wheel in ten-point lines and scales a line up by half
again, so a typical three-line notch moves much the same distance as a detent - and one that reports
precise deltas (a touchpad) passes them straight through. A panel scrolls vertically unless `Shift`
is held and horizontal scrolling is possible, and falls back to the other axis when the preferred one
cannot move. A standalone `ScrollBar` moves three `SmallChange` steps per detent.

```csharp
list.AddMouseWheelHandler((sender, e) =>
{
    if (e.Handled) return;                  // an ancestor already dealt with it
    Console.WriteLine($"wheel by {e.Delta}px");
});
```

The bars a panel creates for itself are not focusable: they take clicks, drags and the wheel, but
`Focusable` is `false`, so clicking one leaves the focus where the user was working, `Focus()` on it
does nothing and `Tab` never lands on it. That is what `Control.Focusable` is for in general -
it gates click-to-focus, `Focus()` and the tab order without affecting mouse interaction. A
standalone `ScrollBar` is still `TabStop` and `Focusable`, so it can be driven from the
keyboard as before.

A press that cannot move the focus also leaves the selection alone. Dragging a scrollbar, or
clicking a plain label, therefore keeps the caret of a focused text box blinking and the text box
keeps taking what the user types; only a press that actually moves the focus, or one that lands on
another selectable control, ends the selection.

What is still missing is the overlay style: an overlay scrollbar floats over the content and does
not take a gutter. The bars are classic until that lands.

A scrolling container and a scrolling list want the same plumbing, so it lives in one internal
`ScrollBars` helper that `Panel` and `ListBox` both own: the visibility fixed point
(a gutter can bring the other axis into play, so the two axes are resolved together), the bar
placement, the offset clamping, `ScrollRectIntoView`, the wheel and the invalidation clip. A third
scrolling control only has to say how big its content is - `Panel` measures its children, the
list box multiplies its row count by the row height - and the helper does the rest.

### Panel zoom

`Panel.ScaleFactor` is a **multiplier applied to the effective scale factor of the subtree**
under that panel. It is not the absolute device scale factor: `Window.ScaleFactor` and
`IWindowBackend.ScaleFactor` report the logical-to-physical pixel ratio of the display, while
this says how far the subtree is zoomed inside the window. The two multiply together only when a
point finally reaches the platform.

The default, `1.0`, leaves measurement, arrangement, painting, hit testing and damage exactly as
they were. At `2.0` the logical space the children are measured and arranged in is halved and they
are painted at twice the size; a docked `DockStyle.Fill` child still fills the panel, while the
fixed metrics inside it - fonts, paddings, a 20 px button - double on screen. At `0.5` the space
doubles and the children are painted at half size.

| Root device scale | Panel `ScaleFactor` | Space its children see | A 20 px button on screen |
| --- | --- | --- | --- |
| 2.0 | 1.0 | 100 x 100 | 40 x 40 device px |
| 2.0 | 2.0 | 50 x 50 | 80 x 80 device px |
| 2.0 | 0.5 | 200 x 200 | 20 x 20 device px |

Multipliers compose down the tree, and no effective value is stored or propagated: each panel acts
only on its own child space, so a panel at `2.0` containing a panel at `0.5` behaves exactly as an
unzoomed one. `Control.EffectiveScaleMultiplier` is the product of the multipliers above a
control, and every conversion between control and host coordinates (`GetPositionInHost`,
`ClientToHost`, `HostToClient`, and the host's own input mapping) multiplies by it.

The panel's own chrome does not zoom. Its border, `Padding`, corner radius and scrollbars keep their
size; only the subtree is affected. An application that wants more room around zoomed content
changes `Padding` itself. Scroll offsets and `ScrollExtent` are reported in the same content space
the children are laid out in, while the bars stay in panel space and never zoom, and the damage
rectangle a child asks for is multiplied back into panel space and rounded outwards.

Containers that derive from `Panel` inherit the property for free: `GroupBox`,
`Overlay`, `Expander`, `StackPanel`, `TableLayoutPanel`, `TabPage` and
`WrapPanel`. `SplitContainer`, `TabControl`, `VirtualList` and `ToolStrip`
derive from `Control` and do not zoom.

Popup surfaces opened from a control inside a zoomed panel - combo drop-downs, date pickers,
autocomplete lists, split-button menus, context menus and tooltips - are sized, painted, hit tested
and placed at the owner's effective multiplier, so a drop-down is never half the size of the field
that opened it. A popup hosts its content in a zoom wrapper for painting and input, and scales the
outer window or overlay size and position. Menus that belong to the window rather than to a control
- the `MenuBar` drop-downs - stay unzoomed, because the bar itself is outside the panel.
Images are recorded into their pictures with linear sampling, so a raster image shrunk by a zoom is
filtered rather than point sampled; animated GIF and WebP frames keep their own nearest sampling by
design, which is what keeps pixel art crisp.

```csharp
var zoom = new Panel { Dock = DockStyle.Fill, ScaleFactor = 1.75f };
zoom.Controls.Add(new Label("A 14 px label, at 24.5 px on screen") { Dock = DockStyle.Top });
```

The demo's **Zoom** page - `--page 55` - drives a panel from 0.5x to 3.0x and puts a reciprocal panel
inside it, so the readout shows the effective multiplier cancelling back to 1.00.

### Adding many children at once

Adding a child lays its parent out again and asks for a repaint, which is the right thing when a
control is added one at a time and the wrong thing when a container is filled in a loop: laying out
`n` children that way costs a pass over a growing child list and a walk up the parent chain per
child, so filling a panel with ten thousand of them spends most of its time re-arranging the ones
already there. `SuspendLayout` and `ResumeLayout` fold the batch into one pass and one repaint:

```csharp
panel.SuspendLayout();
try
{
    panel.Controls.Clear();
    for (int i = 0; i < count; i++)
    {
        panel.Controls.Add(new Button($"Button {i}") { Location = new SKPoint(0f, i * 36f) });
    }
}
finally
{
    panel.ResumeLayout();
}
```

`SuspendLayout` nests, and the outermost `ResumeLayout` does the work, in this order: the layout the
batch asked for, then a single `Invalidate` if anything asked for a repaint while it was suspended.
Nothing can be painted in between, because a frame is driven by the host's loop and the batch runs
inside one turn of it, so folding the repaints costs nothing and saves the parent chain walk per
child. `ResumeLayout(false)` drops the deferred layout - for a caller that is about to arrange the
container itself - but still repaints.

`PerformLayout` is public and safe to call at any time; it is what a container does to itself when
something changes that only it can resolve. `OnLayout` is the protected hook a container overrides.
Neither of them is how a caller arranges children: set `Dock`, `Bounds` or `Location` and let the
control do it.

### List box

`ListBox` is the WinForms list box: a vertical list of rows with a selection, keyboard
navigation, type-ahead and optional scrollbars. It has no children and paints its own rows, so a
thousand items cost one control.

| Member | Behaviour |
| --- | --- |
| `Items` | An `IList<object?>` view over the rows; `Add`, `AddRange`, `Insert`, `RemoveAt`, `Clear` and the indexer all repaint what they touch |
| `ItemCount` | Shorthand for `Items.Count` |
| `GetItemText(item)` | How an item becomes a row - `ToString()` by default; override it to keep the objects you added |
| `SelectionMode` | `None`, `One` (default), `MultiSimple` or `MultiExtended` |
| `SelectedIndex`, `SelectedItem` | The one selected row, or `-1`/`null` when nothing is selected |
| `SelectedIndices`, `SelectedItems` | Every selected row, in ascending order |
| `GetSelected(i)`, `SetSelected(i, bool)`, `ClearSelected()`, `SelectAll()` | The per-row form of the same thing |
| `GetItemRect(i)`, `IndexFromPoint(point)` | Row geometry, in control coordinates; `IndexFromPoint` returns the nearest row and `-1` above the first |
| `ItemHeight` | Row height; follows `Font.LineHeight` until assigned explicitly |
| `Font` | Optional override of `Theme.DefaultFont` |
| `TopIndex` | The first row at the top of the viewport, clamped to what can actually scroll |
| `HorizontalScroll`, `VerticalScroll` | The same `ScrollStyle` values a panel takes; vertical defaults to `Automatic`, horizontal to `None` |
| `ScrollBarThickness` | The gutter width, 16 px by default |
| `VerticalOffset`, `HorizontalOffset`, `ViewportRect` | Where the rows are scrolled to, and the area they are painted into - the client area less the gutters of the bars that are showing |
| `EmptyText` | Muted text drawn while the list has no rows |
| `EnsureVisible(i)`, `ScrollTo`, `ScrollBy` | Bring a row into view, or drive the offset directly |
| `RefreshItems()` | Redraw after mutating the objects in `Items` in place |
| `AddSelectedIndexChangedHandler`, `AddItemActivatedHandler` | Selection changed; and a row was double-clicked or confirmed with `Enter` |
| `AddDrawItemHandler` | Paints a row yourself: it is handed the canvas, the row rect, the item and an `DrawItemState` |
| `BeginUpdate()`, `EndUpdate()` | Batch many changes into one layout and one repaint |

```csharp
var list = new ListBox
{
    Dock = DockStyle.Fill,
    SelectionMode = SelectionMode.One,
    VerticalScroll = ScrollStyle.Automatic,
    EmptyText = "Nothing to show",
};

list.Items.AddRange(new object[] { "Overview", "Text input", "Dialogs", "Split container" });
list.SelectedIndex = 0;
list.AddSelectedIndexChangedHandler(_ => ShowCard(list.SelectedIndex));
root.Controls.Add(list);
```

The mouse works the way the platform does: a plain click selects the row under the pointer,
`Ctrl`+click toggles it, `Shift`+click extends from the anchor, and dragging extends the selection
to whichever row is under the pointer. In `MultiSimple` a plain click still replaces the selection -
only `Ctrl` toggles - and the keyboard moves the selection with the cursor while `Space` adds or
removes the row under it. In `MultiExtended` `Shift`+arrow extends from the anchor, `Ctrl`+arrow
moves the cursor without changing the selection, and `Space` toggles the row under it.
`Home`/`End` jump to the ends, `PageUp`/`PageDown` move a viewport at a time, and typing a letter
jumps to the next row that starts with it - typing again within 700 ms cycles through the matches
and extends the prefix, so `c` `h` reaches "Charts" while a dead end restarts the search from the
new character. Every move the keyboard makes scrolls the list just far enough to keep the row it
landed on whole, and never sideways, so a list the user has scrolled across stays where it was put.

Selection is stored as a sorted list of indices, so membership is a binary search rather than a
scan. A change repaints only the rows whose state actually differed, clipped to the viewport, which
is why hovering and selecting stay cheap on a long list. The row the focus rectangle is on counts
as a change in its own right, so moving it repaints the row it leaves as well as the row it lands
on - in `None` mode, where nothing is ever selected, that rectangle is the only thing a click moves.

### Virtual row lists

`ListBox`, `ListView` and `TreeView` are the same control with a different idea of
what a row is, so everything that is not about the row lives in one abstract `VirtualList`
base: the scrolling helper, the selection set and its four modes, keyboard navigation and
type-ahead, hover, drag-select and the wheel, the `BeginUpdate`/`EndUpdate` batch, and the paint
loop that measures the rows, clips to the viewport and draws only the band that is showing. A
subclass answers five questions and inherits the rest:

| Member | Behaviour |
| --- | --- |
| `ItemCount` | How many rows there are *now* - a tree answers with its visible node count, so collapsing a branch changes it |
| `GetItemText(i)` | The row's label, which is also what type-ahead matches against |
| `GetItem(i)` | The object behind a row, which is what `SelectedItem` returns |
| `MeasureItemWidth()` | The widest row, from which the horizontal extent is derived |
| `OnDrawItem(canvas, i, bounds, state, textColor)` | Paints one row; `state` carries `Selected`/`Focused`/`Hot`/`Disabled`, and `textColor` is already resolved for them |

Every member the list box documents above - `SelectionMode`, `SelectedIndex`, `GetItemRect`,
`IndexFromPoint`, `EnsureVisible`, `VerticalOffset`, `BeginUpdate` and the rest - is on the base, so
it behaves the same way on all three. What the base does not own is what a row *is*: the list box
keeps an `IList<object?>`, the list view keeps `ListViewItem`s, the tree keeps nodes.

Three protected hooks exist for a subclass that is not a plain row list: `ScrollClient` (the area
the rows are painted into, which the list view shrinks by its header), `ScrollPadding` and
`DefaultItemHeight`. `LeadIndex`, `HotIndex`, `SetLead` and `SelectOnly` expose the cursor the base
already tracks, so a subclass can move it without re-implementing selection, and `TextBoundsOf`,
`DrawItemText` and `DrawFocusRectangle` keep the text metrics and the focus rectangle in one place.

One trap is worth naming because it cost a regression: a subclass must not declare
`protected string GetItemText(object? item)` next to its `GetItemText(int index)` override. An
`object?` argument binds to the more specific overload, so the row loop's boxed index would reach
it and every row would render its own number. The list box calls its object form
`GetItemTextOf(object?)` for exactly that reason.

### List view

`ListView` is the WinForms list view: the same rows as either a single column of text
(`View.List`) or a grid (`View.Details`), with header-click sorting and optional grid
lines.

| Member | Behaviour |
| --- | --- |
| `Items` | An `IList<ListViewItem>`; an item's `Text` is its first cell, and `SetCell`/`GetCell` or the `(text, cells)` constructor fill the rest |
| `Columns` | An `IList<ListViewColumn>` of `Text`, `Width`, `Alignment` and an optional `Comparer` |
| `View` | `List` (one text column, no header) or `Details` (columns under a header) |
| `HeaderHeight` | Height of the header band in `Details`; the rows are laid out below it |
| `GridLines` | Draws the column separators and the row rules in `Details` |
| `AllowColumnResize` | Whether a column's right edge can be dragged in the header; `true` by default |
| `SortColumn`, `SortOrder` | Which column shows the header glyph, and which way it points |
| `ListViewColumn.Comparer` | Orders two rows for that column; `null` compares the cell text with the current culture |
| `Sort()` | Sorts `Items` by the current column and keeps the selection on the items it was on |
| `AddColumnClickHandler` | A header was clicked; setting `Cancel` on the arguments stops the built-in sort so an application can sort its own way |
| `ColumnFromPoint(point)` | Which column a point is in, or `-1` |

```csharp
var list = new ListView { Dock = DockStyle.Fill, View = View.Details, GridLines = true };

list.Columns.Add(new ListViewColumn("Name", 180f));
list.Columns.Add(new ListViewColumn("Size", 80f, ContentAlignment.MiddleRight));

foreach (var file in files)
{
    var item = new ListViewItem(file.Name);
    item.SetCell(1, file.Size);
    list.Items.Add(item);
}

list.SortColumn = 0;
list.SortOrder = SortOrder.Ascending;
list.Sort();
root.Controls.Add(list);
```

A header click sorts by that column, or reverses the order when it is already the sorted one, which
is the behaviour every file browser has. The comparison is always written ascending: `SortOrder`
turns it around, and rows that compare equal keep the order they were added in. `Sort` permutes an
index array with `Array.Sort` rather than a LINQ `OrderBy`, because the library does not reference
`System.Linq`; the original position is the tie-break, which is what makes the sort stable.

Sorting on cell text is wrong as soon as the cell is formatted, because `"812 KB"` sorts above
`"4 KB"`. Give the column a `Comparer` and it orders the rows however the application wants. The
comparison is handed the two items whole, so the real value can live in `Tag`:

```csharp
var size = new ListViewColumn("Size", 80f, ContentAlignment.MiddleRight)
{
    Comparer = (left, right) => BytesOf(left).CompareTo(BytesOf(right)),
};

list.Columns.Add(size);
list.Items.Add(new ListViewItem("build.log", "812 KB") { Tag = 831_488L });

static long BytesOf(ListViewItem item) => item.Tag is long bytes ? bytes : 0L;
```

Setting `Comparer` re-sorts straight away when that column is the one being sorted by, so the rows
never sit in an order the comparer disagrees with.

Rows in `Details` are a fixed height for the whole control, so the row bands line up with the header
columns and the vertical grid lines are one stroke per column. The horizontal extent is the sum of
the column widths, so narrowing the control below that raises the horizontal scrollbar rather than
squeezing a column.

Dragging a column's right edge in the header resizes it, with the pointer turning into a left-right
arrow over the edge. Set `AllowColumnResize` to `false` to take the drag away and leave the widths to
code. Columns stop at 24 pixels, and the last column is only a drag target while it is narrower than
the space left for it - once it is stretched to fill the control its width is the leftover rather
than a width of its own, so set `Width` instead.

### Tree view

`TreeView` is the WinForms tree view: nodes with an indented label, a triangular expander when
they have children, and an optional check box.

| Member | Behaviour |
| --- | --- |
| `Nodes` | The root `TreeNodeCollection`; every node has the same `Nodes` collection for its children |
| `Indent` | Pixels of indentation per level, 18 by default |
| `CheckBoxes` | Shows a check box on every row; off by default |
| `SelectedNode` | The one selected node, or `null` |
| `AddAfterCheckHandler` | A node's check state changed; the argument is the node |
| `ExpandAll()`, `CollapseAll()` | Set every branch's state at once, in one layout pass |
| `IndexOf(node)` | The node's row index among the visible nodes, or `-1` when it is inside a collapsed branch |

An `TreeNode` carries `Text`, `Tag`, `Nodes`, `Parent`, `Owner`, `Level`, `HasChildren`,
`FirstNode`/`LastNode`, `IsExpanded`, `IsVisible`, `Selected` and `CheckState`/`Checked`, plus
`Expand`, `Collapse`, `Toggle`, `EnsureVisible` and `Remove`. Nothing is flattened up front: the
visible nodes are rebuilt from the roots when the structure or an expansion changes, and that list
is what the base virtualizes, so a collapsed branch costs nothing to paint no matter how deep it is.

```csharp
var tree = new TreeView { Dock = DockStyle.Fill, CheckBoxes = true };

var src = new TreeNode("src");
src.Nodes.AddRange(new[] { new TreeNode("Core"), new TreeNode("Controls") });
tree.Nodes.Add(src);
src.Expand();

tree.SelectedNode = src.Nodes[0];
tree.AddAfterCheckHandler(node => Console.WriteLine(((TreeNode)node).CheckState));
```

The mouse and the keyboard do what a tree view does. Clicking the expander toggles the branch
without moving the selection, clicking the check box toggles the check without selecting, and
clicking the label selects. Double-clicking or `Enter` activates the row, which toggles a parent and
does nothing on a leaf. `Right` expands, `Left` collapses or moves to the parent, and `Space`
toggles the check box when `CheckBoxes` is on and the expander otherwise; `+`, `-` and `*` expand,
collapse and expand-everything, arriving through text input because `Key` has no punctuation.

Checking a node pushes the state down to its whole subtree and then re-derives every ancestor from
its children, so a parent whose children are all checked reads `Checked`, one whose children are
all unchecked reads `Unchecked`, and anything in between reads `Indeterminate`. That derivation is
what the demo's `Controls` branch shows: two of its children are checked at start-up, so it opens
in the indeterminate state.

### Check boxes and radio buttons

`CheckBox` and `RadioButton` are the same control with a different glyph, so the state
machine, the label, the measuring and the input all live in one shared `ToggleControl` base.

| Member | Behaviour |
| --- | --- |
| `CheckState`, `AddCheckedChangedHandler` | `Unchecked`, `Checked` or `Indeterminate` |
| `Checked` | Convenience view of the state: `true` for `Checked`, and setting it picks that state |
| `ThreeState` | Opts a check box into the indeterminate state; a radio button ignores it |
| `AutoCheck` | When `false` the control reports the click through `OnClick` and leaves the state to the application |
| `TextAlign`, `GlyphSize`, `GlyphGap`, `Font` | Label side, glyph box, gap between glyph and label, label font |
| `GroupName` | Radio buttons only: name of the group, see below |

A click or `Space` advances the state, so a three-state check box cycles `Unchecked` -> `Checked` ->
`Indeterminate` -> `Unchecked`; a two-state one is a plain toggle. Radio buttons group by their
parent, so two groups in one container need either two containers or a distinct `GroupName` on
each - `GroupName` is checked first and grouping by parent is what remains, which is a superset of
the WinForms rule. Checking one clears the rest of its group, and the arrow keys move the check
down the group (wrapping at the ends) as well as moving the focus.

Neither control paints a background, so the surface behind them shows through; only the glyph and
the label are drawn, and a disabled one greys both.

```csharp
var remember = new CheckBox { Text = "Remember me", Checked = true };
remember.AddCheckedChangedHandler(_ => Console.WriteLine(remember.CheckState));

var partial = new CheckBox { Text = "Some files", ThreeState = true, CheckState = CheckState.Indeterminate };

var small = new RadioButton { Text = "Small", GroupName = "size", Checked = true };
var large = new RadioButton { Text = "Large", GroupName = "size" };
```

### Progress bar

`ProgressBar` shows a value between `Minimum` and `Maximum` as a filled track. It takes no
input, so it is neither focusable nor a tab stop.

| Member | Behaviour |
| --- | --- |
| `Minimum`, `Maximum`, `Value`, `Percent` | The range and where in it the bar sits; `Value` is clamped, `Percent` is the fraction as `0 ... 1` |
| `Step`, `PerformStep()`, `Increment(amount)` | `Step` is what `PerformStep` adds; both clamp and repaint only when the value moved |
| `Style` | `Continuous`, `Blocks` (a segmented fill) or `Marquee` |
| `MarqueeAnimationSpeed` | How often a marquee frame is drawn. Null, the default, is the display's own frame time; a `TimeSpan` pins it |

Only a `Marquee` bar starts a timer, and only while it is `IsDisplayed` - its own `Visible`,
every container above it and a window that has been shown and has not closed - and there is an
application to tick it, so an idle continuous bar costs nothing. Hiding the bar, hiding a container
around it, taking it out of the window or closing the window stops the sweep; showing it again
resumes from the phase it stopped at, with the one timer the bar built. A continuous bar repaints
only the strip the value actually crossed.

A marquee that leaves `MarqueeAnimationSpeed` null asks
`Screen.RefreshRate` for the refresh rate of the display the window is on and animates at that
frame time, falling back to 30 ms when the platform cannot report one. The rate is read when the
sweep starts, so a bar that begins animating on another display picks that display up; a running one
does not query the platform from inside its paint. A platform timer cannot tick faster than the OS
allows, so the wait itself belongs to the backend: on Windows, where the default timer cannot fire
faster than its 15.625 ms period, a sub-period interval is run on a high-resolution waitable timer,
which is why a 240 Hz display really does animate at about 240 frames a second instead of 64. Each
frame advances by the time that actually passed rather than by the interval that was asked for, so
the sweep runs at `MarqueeSweepsPerSecond` whatever the timer manages. The repaint rate is the
display's, though, so a fast panel costs more repaints than a fixed 30 ms would: set a speed
explicitly when that matters more than smoothness.

```csharp
var progress = new ProgressBar { Dock = DockStyle.Top, Maximum = 200 };
progress.Value = 80;                      // 40%
progress.PerformStep();                   // 90

var busy = new ProgressBar { Dock = DockStyle.Top, Style = ProgressBarStyle.Marquee };
var calmer = new ProgressBar
{
    Dock = DockStyle.Top,
    Style = ProgressBarStyle.Marquee,
    MarqueeAnimationSpeed = TimeSpan.FromMilliseconds(50),   // ignore the display's own rate
};
```

### Busy indicator and overlay

A long operation needs a readout that is not a determinate bar, and often a way to stop the user
from driving the window while it runs. Those are two controls rather than one, because the two
questions have independent answers: a spinner is useful on its own (in a status strip, in a dialog,
next to the row that is loading), and a modal layer is useful with content that is not a spinner (a
confirmation panel, a progress row, a form). Folding them together would put a timer in every
application that only wanted a blocker, and a dimming surface in every application that only wanted
a spinner.

| Control | Member | Behaviour |
| --- | --- | --- |
| `BusyIndicator` | `IsBusy` | Whether the ring is going round; defaults to true, and clearing it stops the clock |
| | `Thickness`, `AnimationSpeed` | The width of the ring, and how often the sweep advances |
| `Overlay` | `Dim` | How much of what is under it is covered, 0 to 1, painted with the palette's background so it darkens a light window and lightens a dark one |
| | `ChildAlignment` | Where a child that is not docked sits; `MiddleCenter` by default |
| | `FadeDuration` | `Kolvir.Feature.Animations` only: how long the layer takes to dim in, or null - the default - to appear at once |

The spinner is the second control whose value is time, so it obeys the rule the marquee bar set: it
runs a timer only while it is `IsDisplayed` and `IsBusy` and there is an application to deliver the
ticks, and it stops otherwise. It is also always a circle: the shorter of the control's two sides is
the diameter and the ring is centred in the room that leaves, so a spinner in a wide row or a tall
column is round and in the middle rather than an oval stretched to the control's edges, and the same
side sets the stroke so a wide control does not get a heavier ring. The layer is an ordinary control,
so it takes the presses by being the topmost thing in its container; putting it beside the content it
holds still means both are `DockStyle.Fill` and cover the same area - a child docked to an edge keeps
its band and is not covered. Keyboard input follows the focus rather than the pointer, so the layer
also joins the window as an `IInputSink` while it is on screen: every key and every typed
character stops there, which is the same seam an open menu uses. Hiding or disabling the layer gives
the keyboard back.

On its own the layer appears at full strength in one frame. `Kolvir.Feature.Animations` adds a
`FadeDuration` extension member, which dims the layer in over that duration when it becomes visible -
and only then, because a layer on screen is what a fade is for:

```csharp
// With Kolvir.Feature.Animations referenced and AddAnimations() called:
overlay.FadeDuration = TimeSpan.FromMilliseconds(120);
overlay.Visible = true;                              // dims in, holding the keyboard throughout
```

There is deliberately no fade out: the layer is taken off the screen by assigning `Visible`, and a
fade out would mean the control holding on to a hidden layer until a clock it may not have finished.

```csharp
var overlay = new Overlay();                    // Dock = Fill
var busy = new Panel { AutoSize = false, Size = new SKSize(220f, 72f) };
busy.Controls.Add(new BusyIndicator { Dock = DockStyle.Left, Margin = new Thickness(16f) });
busy.Controls.Add(new Label("Loading the project...") { Dock = DockStyle.Fill });
overlay.Controls.Add(busy);                          // centred by ChildAlignment

page.Controls.Add(overlay);                          // over the content, while the work runs
```

### Toast

`Toast` is the message that does not need a dialog: an accent bar, one line of text and a close
glyph, which takes itself away after a few seconds. It is an ordinary control rather than a window,
so the application decides where transient messages belong - docked across the top of a page, at the
bottom of a card, beside the row that saved - and the layout moves around it, which a floating window
would not.

| Member | Behaviour |
| --- | --- |
| `Text` | The message |
| `Severity` | `Info`, `Success`, `Warning` or `Error`, which is what the accent bar is coloured for |
| `BarColor` | Overrides the severity's colour, for an application that brands its own messages |
| `Duration` | How long it shows before dismissing itself; `Toast.DefaultDuration` is four seconds, and null waits for `Dismiss` |
| `Show()`, `Dismiss()`, `IsOpen` | The message state, so one control can be built once and reused |
| `AddDismissedHandler` | Runs once the message went, whether by its countdown, its close glyph or the keyboard |
| `ShowProgress` | How far the message has come in, 0 to 1, clamped. It follows `IsOpen` on its own, it is the message's opacity and how far it sits out of its docked edge, and it is what an animation drives |
| `AddShowingHandler`, `AddDismissingHandler` | Run before `Show` and `Dismiss` move the progress; a handler that assigns `ShowProgress` takes the transition over |

The countdown is a timer and follows the rule the marquee bar set: it runs only while the message is
open and `IsDisplayed` and there is an application to deliver the ticks, so a toast on a page the
user has switched away from stops rather than expiring unseen. The close glyph dismisses on a press
released over it, a press that slides off does nothing, and a focused toast answers Escape, Enter and
Space.

A dismissal is over when `ShowProgress` reaches 0, not when `Dismiss` is called, so a message driven
by an animation stays on screen until the transition finishes and is taken out of the layout then.
Everything that brings the progress to 0 removes it - including a transition that was cancelled - so
a toast can never be left half faded over the page. A message on its way out has already left the Tab
order and given up the focus, and coming back with `Show` puts both back.

On its own the message is a jump in and a jump out. `Kolvir.Feature.Animations` adds `ShowDuration`
and `DismissDuration` extension members over exactly that seam, which fade and slide the message in
from the edge it is docked against and reverse it on the way out:

```csharp
// With Kolvir.Feature.Animations referenced and AddAnimations() called:
toast.ShowDuration = TimeSpan.FromMilliseconds(240);
toast.DismissDuration = TimeSpan.FromMilliseconds(180);
```

The fade and the slide come from the one `ShowProgress` number, so they cannot disagree, and the
slide is a single translate of the whole control rather than a change to its geometry - the accent
bar, the glyph and the text keep their shape all the way in.

```csharp
var toast = new Toast("Project saved") { Dock = DockStyle.Bottom, Severity = ToastSeverity.Success };
page.Controls.Add(toast);

toast.Show();                                        // up, and counting down from four seconds
toast.Text = "Could not reach the server";
toast.Severity = ToastSeverity.Error;
toast.Duration = null;                               // an error waits for the user
toast.Show();
```

### Group box

`GroupBox` is a labelled frame: the caption sits in a break in the top edge and the content is
laid out below it. It is an `Panel`, so it keeps the scrolling and the child management of one.

The caption band is reserved by overriding `ContentBounds` rather than by adding to `Padding`, so
the layout, the hit testing and the invalidation clipping all move together and a scrolled child
cannot paint over the caption. The break in the frame is cut out of the border with a difference
clip instead of being painted over, so a group box on a coloured or textured background still looks
right.

```csharp
var group = new GroupBox { Dock = DockStyle.Top, Height = 140f, Text = "Appearance" };
group.Controls.Add(new CheckBox { Dock = DockStyle.Top, Text = "Show grid" });
group.Controls.Add(new CheckBox { Dock = DockStyle.Top, Text = "Snap to grid" });
```

`Enabled` cascades, so disabling a group box greys its caption, its frame and everything inside it,
and none of the content takes input. Only the flag that was set is remembered: a control that was
disabled on its own stays disabled when its container is enabled again, and one that was only
disabled by its container comes back with it. Moving a subtree under a disabled container disables
it the same way, without touching a single `Enabled` flag on the children.

### Expander

`Expander` is the collapsible version of a group box: a header with a chevron and a caption,
and the content folded away under it. It is an `Panel`, so the content keeps the docking,
scrolling and child management of one, and the header is taken out of the layout by overriding the
content bounds rather than by adding to the padding - a scrolled child can never be arranged or
painted under the caption.

| Member | Behaviour |
| --- | --- |
| `IsExpanded` | The state the section is heading for. Collapsing remembers the height and shrinks to the header; expanding restores it |
| `ExpandProgress` | How far open it is drawn, 0 to 1, clamped. It follows `IsExpanded` on its own, and is what an animation drives |
| `ArrowRotationDegrees` | How far the chevron is turned: 0 points right, 90 points down. Exactly `ExpandProgress * 90` |
| `ExpandedHeight` | The height the content shows at, which is what expanding puts back |
| `AddExpandedChangedHandler` | Runs when `IsExpanded` changed, before the progress follows it |
| `AddExpandProgressChangedHandler` | Runs when the progress moved |
| the header | A press and release on it toggles the section; Enter, Space, Left and Right do the same from the keyboard |

There are two states and only one of them is the caller's. `IsExpanded` is the target - what was
asked for, what the header toggles - and `ExpandProgress` is the visual state: 0 folded, 1 open,
anything between a section that is half open. Nothing animating, the two always agree, because
changing `IsExpanded` moves the progress itself; an application never assigns `ExpandProgress` and
never has to. They disagree only while something is driving the progress, which is the whole reason
the number is public: a section that is half open is expanded by `IsExpanded` and 0.5 by
`ExpandProgress`.

Both the content height and the arrow come from that one number, so they cannot drift apart. The
chevron is a single shape turned by `ArrowRotationDegrees`: pointing right at 0 and exactly a quarter
turn clockwise - pointing down - at 1.

A folded expander arranges its content into nothing: the children are still in the tree, so they
come back on expand, but they occupy no area, are not painted and are not hit tested. Part-way open
they are laid out in the room that is left and clipped at the folded edge, so a fold in progress
shows the top of the content. An `AutoSize` expander needs none of the height remembering - it
measures as its header when folded, and as much of its content as the progress allows otherwise - so
the two agree without guessing which one a section is.

The keyboard follows the target rather than the fold. The header is a tab stop and keeps its place
in the order whether the section is open or shut; the content is a tab stop only while `IsExpanded`
is true, because a folded expander reports `IsContentReachable` false. The answer changes the moment
`IsExpanded` is set, not when the fold finishes, so a section on its way shut takes no new focus and
does not change its mind at the last frame. If a control inside the content holds the focus when the
section is collapsed - by click, by the keyboard, by an animation finishing - the focus moves to the
header, the nearest thing the user was looking at, rather than being left on a control that is no
longer on screen. The order is re-read every time Tab is pressed, so a section folded and unfolded
under the keyboard behaves the way it looks.

Hiding a container is deliberately different: it keeps the focus its child held, so a field inside a
hidden panel keeps its caret and picks it up again when the panel is shown. That is a documented
behaviour with a test behind it, and a fold is not a hide.

On its own the fold is a jump. Animating it belongs to `Kolvir.Feature.Animations`, which this
control does not reference; that package adds an `ExpandDuration` extension member and animates
`ExpandProgress`, so with one line the arrow and the height move together over the duration, and with
none the control behaves exactly as described above.

```csharp
var appearance = new Expander("Appearance") { Dock = DockStyle.Top, Height = 180f };
appearance.Controls.Add(new CheckBox { Dock = DockStyle.Top, Text = "Show grid" });
appearance.AddExpandedChangedHandler(sender =>
    Console.WriteLine(((Expander)sender).IsExpanded ? "opened" : "closed"));

// With Kolvir.Feature.Animations referenced and AddAnimations() called:
appearance.ExpandDuration = TimeSpan.FromMilliseconds(180);
```

A collapse that starts while the section is still opening turns round from wherever it has got to,
a duration removed mid-flight settles the section exactly where `IsExpanded` says it belongs, and an
application that never registered the clock snaps instead of leaving a section frozen half open.

### Combo boxes

`ComboBox` is an input field with a chevron; clicking it opens a drop-down list in a popup, so
the list is not clipped by the window and can overhang whatever is below it. The field is the
control and the list is a separate `ListBox` living in that popup, which is why the control
keeps the keyboard focus the whole time it is open - the arrow keys, Enter and Escape all go to the
combo box, not to a second focused control.

| Member | Behaviour |
| --- | --- |
| `Items`, `ItemCount`, `DropDownList` | The rows; `DropDownList` is the underlying list, for its selection and row rendering |
| `SelectedIndex`, `SelectedItem`, `SelectedText` | The selection; `-1` is nothing selected and leaves the field empty |
| `Text`, `EmptyText` | `Text` is the selected item's text; `EmptyText` is the placeholder painted when nothing is selected |
| `MaxDropDownHeight` | The tallest the list may be before it scrolls (240 logical pixels by default) |
| `IsDropDownOpen`, `OpenDropDown()`, `CloseDropDown()` | The drop-down state |
| `AddSelectedIndexChangedHandler` | Called with the combo box when the selection changes |

A drop-down opens downwards and flips above the field when there is no room below it. Disabling
the control closes the drop-down, and a disabled combo box neither opens nor takes input. The
chevron and the text switch to `Theme.DisabledForeground`.

```csharp
var size = new ComboBox { Dock = DockStyle.Top, EmptyText = "Choose a size" };
size.Items.AddRange("Small", "Medium", "Large");
size.AddSelectedIndexChangedHandler(sender => Console.WriteLine(((ComboBox)sender).SelectedText));
```

### Date and time pickers

`DateTimePicker` is an editable date field with a calendar in a popup. The field is an
`TextBox`, so typing, selection, the clipboard and the caret work the way they do everywhere
else; the calendar is a control drawn inside an `Popup`, so it can hang past the window when
the platform layer provides popup windows. As with the combo box, the field keeps the keyboard
while the popup is open.

| Member | Behaviour |
| --- | --- |
| `Value` | `DateTime?`; null is the empty field, and a value outside the bounds is clamped |
| `MinDate`, `MaxDate` | The allowed range of dates (defaults: `DateTime.MinValue` and `DateTime.MaxValue`); a clamped value keeps its time of day |
| `Format`, `CustomFormat` | `Short`, `Long`, `Time` or `CustomFormat`; patterns come from the current culture |
| `IsDropDownOpen`, `OpenDropDown()`, `CloseDropDown()` | The calendar |
| `AddValueChangedHandler` | Called with the picker when `Value` changes |

Typed text is parsed against the current culture and the current `Format` when the edit is
committed: on Enter, when the focus leaves, or when the calendar opens. Nothing is parsed while the
user is still typing, so a half-typed date is never rewritten under the caret. An entry that cannot
be parsed reverts to the last good value instead of throwing, and Escape does the same on demand. A
date-only entry keeps the time of day the field already held, and a time-only entry keeps its date.
Culture-aware formatting and parsing come from `System.Globalization`, which every project already
uses.

The calendar opens below the field and flips above it when there is no room, clamped to the display
when the platform layer reports one. It draws a month and year header with previous and next
buttons, a weekday row that follows the culture's first day of the week, a six-week grid with today
outlined and the value filled in, and Today and Clear buttons in the footer. Days outside
`MinDate`..`MaxDate` are muted and cannot be picked.

```csharp
var when = new DateTimePicker(DateTime.Now) { Format = DateTimePickerFormat.Short };
when.MinDate = DateTime.Today;
when.MaxDate = DateTime.Today.AddYears(1);
when.AddValueChangedHandler(sender => Console.WriteLine(((DateTimePicker)sender).Value));
```

Left and right move between the date's segments (the month, the day, the year...), Up and Down step
the segment under the caret, and Page Up/Page Down step the month. While the calendar is open the
arrows move its highlight, Enter takes it, and Escape closes it. Alt+Down opens the calendar and
Alt+Up closes it.

### Tab controls

`TabControl` draws a strip of tabs and lays the selected `TabPage` out in the area below
it; the other pages stay in `Controls` but are hidden, so their state survives a switch. A
`TabPage` is an `Panel` with no frame of its own, so it scrolls and docks like one.

| Member | Behaviour |
| --- | --- |
| `TabPages` | The pages, in strip order (the same collection as `Controls`) |
| `SelectedIndex`, `SelectedTab` | The visible page; `-1` selects nothing |
| `Font` | The tab captions; the strip measures itself from it |
| `AddSelectedIndexChangedHandler` | Called with the tab control when the selection changes |

The strip scrolls with the wheel when the tabs do not fit, and the keyboard works the way it does
in WinForms: the left and right arrows move the selection, `Home`/`End` jump to the ends, and
`Ctrl`+`Tab`/`Ctrl`+`Shift`+`Tab` cycle it.

```csharp
var tabs = new TabControl { Dock = DockStyle.Fill };
var general = new TabPage("General");
general.Controls.Add(new CheckBox { Dock = DockStyle.Top, Text = "Show grid" });
tabs.TabPages.Add(general);
tabs.TabPages.Add(new TabPage("Advanced"));
```

### Tool tips

One `ToolTip` serves any number of controls, like WinForms': give it the text for each
control and it watches their pointer enter and leave. The tip appears after `InitialDelay`, stays
for `AutoPopDelay`, wraps at `MaximumWidth` and is shown in a popup that does **not** take input,
so moving the pointer across it still reaches whatever is underneath.

A tip belongs to the pointer rather than to the control it describes: it opens just below and to
the right of the pointer, flips to the other side when it would otherwise leave the window, and
only falls back to hanging off the control when the pointer's position is not known.

| Member | Behaviour |
| --- | --- |
| `SetToolTip(control, text)`, `GetToolTip(control)` | The text for a control; `null` or empty removes it |
| `Active` | `false` hides the tip and stops new ones appearing |
| `InitialDelay`, `AutoPopDelay` | Hover delay and how long the tip stays up, in milliseconds |
| `MaximumWidth` | Wrapping width; the tip grows downwards instead |
| `IsVisible`, `Hide()` | The current tip |

A tip is only scheduled while the pointer is actually resting on the control, and the timer is
disposed with the tool tip, so an idle application runs nothing. A control that stops being
displayed - hidden, moved into a hidden container, or in a window that has closed - takes its
pending or drawn tip with it and stops the delay, because there is no mouse-leave to say so when a
container goes away. `Dispose` the instance when the controls it describes go away.

```csharp
using var tips = new ToolTip { InitialDelay = 500 };
tips.SetToolTip(saveButton, "Save the document");
tips.SetToolTip(helpButton, "Open the help window");
```

### Numeric up-down and track bar

`NumericUpDown` is an `TextBox` with a spinner pair in its right-hand edge, so the value
can be typed or stepped. The value is a `decimal` rather than a float, because stepping a money
amount a hundred times has to land on the amount and not on a rounding artefact.
`DecimalPlaces` decides how many of those decimals are held and shown, and every value that comes
in - typed, stepped or assigned - is rounded to it and clamped to `Minimum`..`Maximum`.

Typing stays free-form: the text is parsed as it changes and `Value` follows, but the text is only
rewritten from the value when the edit is committed - on Enter, when the focus leaves, or on any
step - so a half-typed `-` or `1.` is never taken away from the user. Characters a number cannot
contain are dropped as they are typed, along with a second decimal separator, and parsing and
formatting use the current culture so the separator is the one the user's keyboard produces. The
arrow keys, the wheel and the buttons all step by `Increment`, and a held button repeats.

`TrackBar` fills its track up to the knob, so the value reads without finding the knob first,
and `TickFrequency` puts marks where the round values are. Pressing the knob starts a drag that
keeps the grab point, so grabbing it off-centre does not make it jump under the pointer; pressing
the track pages by `LargeChange` toward the press. Arrow keys move by `SmallChange`, `PageUp` and
`PageDown` by `LargeChange`, and `Home`/`End` jump to the ends. `Orientation` turns the track, the
knob and the ticks through ninety degrees.

```csharp
var amount = new NumericUpDown(1.5m)
{
    Minimum = 0m,
    Maximum = 99.99m,
    Increment = 0.25m,
    DecimalPlaces = 2,
};

var zoom = new TrackBar { Minimum = 25f, Maximum = 400f, Value = 100f, TickFrequency = 25f };
zoom.AddValueChangedHandler(_ => SetStatus($"Zoom {zoom.Value:0}%"));
```

### Colour picker

`ColorPicker` is a spectrum square with a hue strip beside it, three component fields and a
hex field. The square's horizontal axis is saturation, from zero at the left to full at the right;
its vertical axis is value in RGB and HSV and lightness in HSL, full at the top and zero at the
bottom. The hue strip carries the third axis with 0 degrees (red) at the top and 359 at the bottom.
RGB and HSV share the same square because RGB has no two-axis shape of its own; HSL has its own
saturation/lightness square, whose top and bottom rows are white and black all the way across
because lightness alone decides the colour there.

`ColorMode` decides what the three component fields show, and it is a display change only:
switching between `Rgb`, `Hsl` and `Hsv` never converts the colour through the components, so
`Color` is preserved exactly and no `AddColorChangedHandler` notification is raised. The fields are
rewritten from the colour - and the hue is remembered - when the mode changes; they do not keep the
text of the space they were showing. HSB is the same space as HSV, so the enum has no separate
`Hsb` value; a value read from a design tool that calls it HSB belongs in `Hsv`.

The hue is kept as an axis of its own rather than read back from the colour every time. That is
what stops a grey from sending the square to red and the H field to zero: a grey has no hue of its
own, so the picker keeps the last hue it had and the square stays on that hue while saturation or
lightness takes the colour to grey.

| Mode | Components | Range |
| --- | --- | --- |
| `Rgb` | R, G, B | 0-255 each |
| `Hsl` | H, S, L | 0-359 degrees, then 0-100 percent |
| `Hsv` | H, S, V | 0-359 degrees, then 0-100 percent |

The component fields parse as they are typed, so a valid value updates the picker immediately and
an out-of-range one is clamped on the way in: typing `300` into a channel holds the colour at 255
and typing `400` into the hue holds it at 359. The text is not rewritten underneath the user while
the edit is in progress; Enter, the focus leaving, or one of the arrow keys puts the field back to
the value actually held. Enter then moves the focus on rather than staying in the field, Escape
puts the field back without committing, the arrow keys step by one and Page Up / Page Down by ten.
A field selects its contents when it takes the focus, so a value can be replaced by typing.

The hex field takes an HTML/CSS colour: `#RRGGBB`, the `#RGB` shorthand, or either without the
leading hash, in upper or lower case. It writes back uppercase `#RRGGBB`. A complete valid value is
applied while it is typed; an incomplete or malformed one leaves the colour alone, and Enter or
leaving the field rewrites it from the current colour. The picker edits the three colour channels,
so the alpha a colour arrives with is preserved through every edit and is never shown; an alpha
hex form is not accepted rather than being guessed at as either ARGB or RGBA.

The square's image is generated from the current hue at the resolution the canvas is painting at,
and it is cached per hue, space and physical size. A repaint, a hover, a focus change or a marker
move reuses that image; only a new hue, a switch to or from `Hsl`, or a resize regenerates it, and
that is what keeps a drag in the square from paying for a per-pixel loop per frame. The hue strip
is a linear gradient shader and needs no cache.

Pressing in the square focuses it and starts a drag whose value follows the pointer; leaving the
square clamps to the nearest edge and the drag continues until the button is released. The arrow
keys move saturation by one percent and value or lightness by one percent, Page Up and Page Down
move value or lightness by ten, and Home and End put saturation at its ends. The hue strip moves one
degree per arrow press and ten per page press, and Home and End jump to 0 and 359. Tab visits the
square, the hue strip, the three component fields and then the hex field.

```csharp
var picker = new ColorPicker
{
    Color = new SKColor(0x33, 0x66, 0x99),
    ColorMode = ColorMode.Hsv,
};

picker.AddColorChangedHandler(_ => SetStatus($"Chose #{picker.Color.Red:X2}{picker.Color.Green:X2}{picker.Color.Blue:X2}"));
```

### Link labels

`LinkLabel` is an `RichLabel` whose text is styled as a link, so extra spans can be added
around it to build a sentence. Clicking it - or pressing Enter or Space while it has focus - hands
its `Url` to the desktop and raises `AddLinkClickedHandler` with an `LinkClickedEventArgs` that
says which span was clicked and whether the platform accepted the URI. Setting `AutoLaunch` to
`false` makes the label a notification only, which is what an application that navigates inside
itself wants.

Opening a URI needs the platform, so it goes through `ILauncher`: each backend installs its
implementation into `Launcher.Current` (`ShellExecute` on Win32, `NSWorkspace.OpenUrl` on
AppKit, `Gtk.UriLauncher` on GTK4), and the fallback opens nothing and reports failure. The URI is
expected to come from the application - a `file:` URI reaches the local file system, so
user-supplied text belongs behind a scheme allow-list rather than straight into `OpenUrl`.

```csharp
var link = new LinkLabel("Project home", "https://github.com/drysart/Kolvir");
link.AddLinkClickedHandler((_, e) => SetStatus(e.Opened ? "Opened" : "Could not open"));
```

### Tool strips and status strips

A tool strip holds *items* rather than child controls, because a strip that accepted children would
just be a panel. An item is a plain object - `ToolStripButton`, `ToolStripLabel`,
`ToolStripSeparator`, `ToolStripProgressBar` or `ToolStripSpring` - so a bar of nine
items costs one control, one paint pass and one hit test rather than nine of each.
`StatusStrip` is the same control with the status-bar defaults.

| Member | Behaviour |
| --- | --- |
| `Items` | The item list: add, insert, remove and reorder without rebuilding the bar |
| `ToolStripItem.Alignment` | `Near` packs from the left, `Far` from the right; a `Spring` takes the slack between them |
| `ToolStripItem.Width` | A fixed width, or `NaN` to measure the item's own content |
| `ToolStripItem.Enabled` | A disabled item greys out and takes no input while the rest of the bar keeps working |
| `ToolStripButton.AddClickHandler` | Runs when the item is clicked, or activated from the keyboard |
| `ToolStripProgressBar` | `Minimum`/`Maximum`/`Value`/`Percent`; the fill needs no timer, unlike a marquee `ProgressBar` |

The bar is a single tab stop: the arrow keys move a selection along the items, `Home` and `End`
jump to the ends, and Enter or Space activates the selected item, so a strip does not put every one
of its items into the tab order. `ToolStrip` docks to the top of its container and
`StatusStrip` to the bottom, each at its own natural height.

```csharp
var toolbar = new ToolStrip();
toolbar.Items.Add(new ToolStripButton("Save"));
toolbar.Items.Add(new ToolStripSeparator());
toolbar.Items.Add(new ToolStripSpring());
toolbar.Items.Add(new ToolStripLabel("UTF-8"));
```

### Split buttons

`SplitButton` is one button with two halves: the main area is the default action and the arrow
at its right edge opens `Menu`. `DropDownButton` is the same control with the halves merged -
pressing anywhere opens the menu, and `AddClickHandler` still runs, which mirrors the WinForms
`ToolStripDropDownButton`.

| Member | Behaviour |
| --- | --- |
| `Menu` | The `ContextMenu` the arrow opens: items, submenus, separators and mnemonics |
| `ArrowWidth` | Width of the arrow region, 24 logical pixels by default; it is part of the measured width |
| `IsDropDownOpen`, `OpenDropDown()`, `CloseDropDown()` | The menu's state |
| `AddClickHandler` | The default action: a press and release in the main area, or Enter/Space while focused |

What separates the two halves is where the press started: a press that begins in the arrow and is
released over the label does nothing, the way a press that wanders off an ordinary button does. The
arrow region darkens under the pointer so it is clear which half is about to be hit. `Down` opens the
menu from the keyboard; once it is open the menu owns the keyboard, so its own arrows, Enter and
Escape are what move and dismiss it.

The menu is the library's own `MenuView` in an `Popup` shown by `MenuPresenter`, so it
draws the same on all three platforms, opens submenus in popups of their own, and can leave the
window when the backend provides popup windows. Nothing is built until the menu is first opened, and
disabling or hiding the button closes an open menu.

```csharp
var save = new SplitButton("Save") { Dock = DockStyle.Top, AutoSize = true };
save.AddClickHandler(_ => document.Save());          // the main area
save.Menu.Add("Save as...", () => document.SaveAs());
save.Menu.Add("Export as PDF", () => document.Export());

var more = new DropDownButton("More") { Dock = DockStyle.Top, AutoSize = true };
more.Menu.Add("Rename", () => document.Rename());
more.Menu.Add("Archive", () => document.Archive());
```

### Popups

A popup is a control floated above the window: a drop-down list, a tool tip, anything that has to
escape the clipping of the control that owns it. `Popup` is the portable half. It is given an
owner and a content control, and it either asks the platform layer for a real window or paints the
content inside the owning window.

| Piece | Role |
| --- | --- |
| `Popup(owner)` | `Content`, `Size`, `MaximumSize`, `DismissOnOutsideClick`, `Show(location)`, `MoveTo`, `Close()`, `IsOpen`, `AddClosedHandler` |
| `Presentation` | `Auto` (the default), `Window` or `Overlay`; `ActualPresentation` reports which one happened |
| `IPopupService` | The platform seam: `CreatePopup(ownerHost, dismissOnOutsideClick)` returns an `IPopupWindow` or `null` |
| `PopupService.Current` | The process-wide service, registered by the backend next to the other adapters |
| `IPopupWindow` | `Host`, `DismissesOnOutsideClick`, `Dismissed`, `Show(ownerHost, location, size)`, `UpdateBounds`, `Hide` |

When no service is registered - or when it declines, because the owner is not on screen yet - the
popup falls back to the host's overlay layer, which paints it above the root control and routes
input to it first. That is what keeps the library platform free: a build that never opens a popup
pays nothing for the feature, and the same code runs on a backend that has no popup surfaces.

```csharp
var popup = new Popup(this) { Content = new ListBox(), Size = new SKSize(200f, 160f) };
popup.AddClosedHandler(_ => Invalidate());
popup.Show(new SKPoint(x, y));      // logical, in the owner's coordinate space
```

The platform decides where the popup really lands, because only it knows where the owning window
is on screen and how the screen is arranged:

| Backend | Surface |
| --- | --- |
| Win32 | An owned, layered `WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE` window drawn with `UpdateLayeredWindow`, clamped to the monitor's work area |
| AppKit | A borderless child `NSWindow` ordered above its parent, that never becomes key or main |
| GTK4 | A `Gtk.Popover` anchored to the owner's widget, which is allowed to extend past the window |
| Headless | None; popups stay on the overlay path |

A popup window is never activated and never takes focus, so the owning control keeps the keyboard
while its drop-down is open. Dismissal is the platform's job too: a press outside the popup, a
scroll or a resize closes it, and `Dismissed` tells `Popup` to raise its `Closed` event.

The press that dismisses a popup belongs to the popup: it is consumed, not delivered to whatever it
landed on. That is what the overlay presentation gets from its dismiss catcher, which covers the
window and swallows the click, and a platform window has to match it - otherwise a press on the
control that opened a pop-up both closes and reaches that control, which acts on a pop-up that is
already gone. A combo box is where it shows: it reopens its list on the very click that closed it.
So the Win32 window procedure returns without dispatching the button press that dismissed a popup,
the AppKit local monitor returns `nil` for it, and on GTK4 `Gtk.Popover.Autohide` grabs the pointer
so the press never reaches the window behind the popover. Wheel, pointer motion and resize are not
presses: they dismiss the popup and are still delivered.

GTK4 needs two things of its own, because a popover is styled and painted by the toolkit rather
than by the backend:

- A popover positions itself by its own CSS margin rect and draws its child inside its padding, so
  the theme's shadow and rounded corner would show up as a border of dead space around a popup that
  the portable layer sized to exactly its content. `Gtk4PopupWindow` installs a display-wide
  `Gtk.CssProvider` at application priority that strips padding, margin, border, background and box
  shadow from popovers carrying its own style class - popovers only, nothing else.
- A popover paints when something asks it to and not again, unlike a window, which repaints often
  enough to hide a frame that arrived late. `Gtk4SkiaView` therefore asks for one extra render pass
  once a frame has been rasterized (see `SchedulePicturePass`), deferred to an idle source so the
  request is made from outside the pass and names a widget that is not already waiting to draw.
  Without it a popup kept showing its first, empty frame until the pointer moved over it.

### Invalidation and partial painting

`Invalidate` says what changed, and the host keeps the answer as a region rather than a single
rectangle, so two distant changes cost two small areas instead of the box around them. Every
overload takes coordinates in the control's own space and drops anything outside its client area:

| Call | Repaints |
| --- | --- |
| `Invalidate()` | The whole control, including its children |
| `Invalidate(invalidateChildren: false)` | The whole control, but not the children |
| `Invalidate(SKRect)` | An area of the control, including its children |
| `Invalidate(SKRect, invalidateChildren)` | An area of the control, with the children left in or out |
| `Invalidate(SKRegion)` | A set of areas of the control, including its children |
| `Invalidate(SKRegion, invalidateChildren)` | A set of areas of the control, with the children left in or out |

`invalidateChildren: false` subtracts the children that cover their own bounds from the area, so a
control that changed its own appearance without moving anything does not make the paint visit the
children at all. It is the difference between a caret blink costing one column of one text box and
costing the whole window:

```csharp
// The caret blinks by repainting its own column, not the control around it.
caret.Invalidate(Geometry.Inflate(bounds, 1f), invalidateChildren: false);
```

A child is only left out when it is `Opaque`, because one that is not lets the background show
through, and a repaint that skipped it would leave a hole where its own paint should have been.
The property is computed rather than set - it is false for a transparent or translucent
`BackColor`, for rounded corners and for any `Opacity` below 1 - so nothing has to remember to keep
a flag in step with what the control paints. Override it when a control covers its bounds by other
means, or when it punches a hole in an otherwise solid background.

A control never talks to the host. It clips the area to its own client area, maps what is left into
its parent's coordinates and calls the parent's `ChildInvalidate`; the default implementation of
that hook clips the area to the parent's client area and invalidates the parent in turn, so the
request walks up the tree one control at a time, and only the root - which has no parent - hands it
to `UiHost`. A control therefore cannot reach past its own bounds, and the root cannot be
reached around.

That hop is the point: it is where a container decides what a child's request means.
`ChildInvalidate` is `protected internal virtual` so a container can narrow the area, move it, or
widen it to take in the decoration it draws around the child, and it receives the area in its own
coordinates with the child's bounds already applied. `Panel` uses it to clip content to the
viewport, so a control scrolled out of sight asks for nothing and one that is half scrolled out
asks only for the part that is showing; the scrollbars are left alone, because they live in the
gutter the viewport excludes and are painted over the content rather than clipped with it.

```csharp
// A container that frames its children repaints the frame along with whatever changed inside it.
protected internal override void ChildInvalidate(Control child, SKRegion region)
{
    region.Op(ToRectI(Inflate(child.Bounds, FrameThickness)), SKRegionOperation.Union);
    base.ChildInvalidate(child, region);
}
```

The paint itself is region-exact. `UiHost.Paint` clips to the dirty region - through its
boundary path when the region is not a plain rectangle, so scattered invalidations do not repaint
the gaps between them - clears that area and renders the tree into it. `Control.Render` calls
`canvas.QuickReject` on its own bounds before doing any work, so a subtree that cannot reach the
clip is not walked at all. That is where most of the saving is, and it applies on every backend
whether or not the backend can present part of a frame.

Moving or resizing a control repaints the area it vacated as well as the area it covers, without
being asked. Without that a control that moved would leave a copy of itself behind, which is
exactly what culling would otherwise make permanent. The vacated area is worked out in the parent's
coordinates and offered to the parent's `ChildInvalidate` like any other request, so the same
clipping applies to it: a child scrolled out of sight of a scrolling panel has vacated an area that
runs from where it used to be to the far end of the content extent, and a request that reached the
host directly would dirty a rectangle taller than the surface on every scroll step. The container's
`ChildInvalidate` is the only place that can trim it back to the viewport.

Arranging a child moves it, so a pass over many children asks for many repaints, and each one would
otherwise walk up the parent chain. No frame can be painted while the pass is running, and the pass
is about to repaint what it arranged, so `Control` folds the requests a pass causes into one
`Invalidate` of the control being laid out, made when the pass - including any nested passes it
started - finishes. The repaint covers that control's client area, which for a scrolling container
is the viewport rather than the content. That is what lets a stack panel of a hundred thousand
buttons scroll in milliseconds instead of seconds.

Hovering is not a repaint on its own. A control that draws itself differently under the pointer
says so, by invalidating from its own `OnMouseEnter`/`OnMouseLeave`; the base `Control` only
tracks `IsMouseOver` and raises the events. Repainting every control the pointer crosses is what a
whole-window renderer gets away with, because the frame it would have drawn anyway costs the same:
here it would put a title label's whole bounds into the next region, and with it everything
between the label and whatever else changed. A label, a panel, a table and an image look the same
hovered as not, so they are left out of it.

A backend opts into partial presentation with `UiHost.PartialUpdates`. It is off by default
because a backend that rasterizes the whole surface every frame - GTK4's CPU path and the headless
renderer both do - would be left with blank patches where the retained content should be:

| Backend | Presents |
| --- | --- |
| Win32 | The painted area from the DIB, or the whole retained GL buffer after a damage-only paint |
| AppKit | The painted area, through the layer's `CGImage` |
| GTK4 | The whole surface every frame on the CPU path; the projected damage inside a retained GL buffer on the accelerated one |
| Headless | The whole surface, so `--render` is always complete |

`Paint` returns the `SKRectI` it covered, `PendingUpdateBounds` reports what the next paint will
cover before it happens, and `IsUpdatePending` says whether anything has been invalidated at all -
enough for a backend to skip asking the platform for a frame it would only redraw identically.
`IncludeInNextPaint` adds an area to the next frame *without* raising the invalidated event. It is
there for a host that wants to force an area into the next frame without scheduling one; the
framework itself no longer needs it, because a control that moves now asks its parent, as above.

The request the platform is given has to be aimed at the same area. A backend that invalidates its
whole window on every request gets its whole client area handed back as stale on the next frame,
and presenting that means copying the whole surface out of the buffer for nothing. The Win32
backend therefore invalidates `PendingUpdateBounds` rather than the window.

What the platform reports as stale is not the same set as what the UI asked for. Windows keeps a
single update rectangle, so two small changes at opposite ends of a row arrive as the box around
both - and adding that box to the paint would repaint everything between them, which is exactly
the over-painting the region exists to avoid. A backend that keeps its surface between frames
answers it the way WinForms does: copy the stale rectangle straight out of the retained buffer,
and let `Paint` cover only what was really invalidated.

```csharp
// A backend that keeps its surface between frames.
_host.PartialUpdates = true;

void OnPaint(SKRectI stale)     // the platform's update rectangle
{
    if (!_host.IsUpdatePending)
    {
        PresentFromBuffer(stale);   // nothing new to draw: copy what is already there
        return;
    }

    var area = _host.Paint(_canvas);
    PresentFromBuffer(SKRectI.Union(stale, area));
}
```

### Seeing what gets repainted

Set `KOLVIR_DEBUG_PAINT` to paint every invalidated area flat red for one frame before painting its
content over it. Partial repaints are invisible by design, which is what makes them hard to trust:
this makes each one announce itself.

```powershell
$env:KOLVIR_DEBUG_PAINT = '1'      # hold the red frame for the default 80 ms
$env:KOLVIR_DEBUG_PAINT = '500'    # hold it for half a second
```

The red frame covers exactly the region the paint was given - the same clip, the same holes in it -
and leaves the dirty region alone, so the frame that follows paints that same area and nothing else.
A whole-surface paint is never tinted: startup, a resize and a full `Invalidate()` repaint
everything anyway, so a flash of red would only hide the result instead of pointing at it. The
variable is read once per process, so it has to be set before launch.

The content frame is scheduled for after the hold rather than painted straight away, so the hold
does not block the UI thread - it only delays the second frame. Painting both frames in one pass
would not work on a backend that shows what it drew only once the message loop turns, which is what
macOS does: the content frame would overwrite the red one before either was on screen.

`--focus` focuses the first text box on startup. A blinking caret invalidates a five pixel column
every 530 ms, which makes a steady supply of small partial repaints to watch - handy when the
machine driving the demo is not the machine looking at it.

### Frame timing

`UiHost` times the frames it paints, which is enough to see what a partial repaint buys
without reaching for a profiler:

| Member | Reports |
| --- | --- |
| `LastFrameTime` | Library time spent on the last frame: clipping, clearing and walking the tree |
| `LastFrameArea` | The `SKRectI` that frame covered, in logical pixels - the surface or the dirty region |
| `FrameCount` | How many frames have been painted |
| `AddFramePaintedHandler` | A handler that runs after a frame, for a readout of the last one |

The time is the library's own work, so it leaves out whatever the backend spends presenting the
frame: that part belongs to the platform and the library cannot see it. The debug paint's red frame
is not counted either - the content frame that replaces it is.

The stats are published after the tree has drawn, so a control that reads them while painting shows
the frame *before* its own. That is what a readout of the last frame has to say, and it keeps the
readout from invalidating itself on every frame:

```csharp
host.AddFramePaintedHandler(_ =>
    caption.Text = $"Last frame {host.LastFrameTime.TotalMilliseconds:0.0} ms");
```

The handler runs after the paint has taken the dirty region, so the invalidation its assignment
causes belongs to the next frame instead of being lost. That is also why a readout on a live page
should assign only a changed string: otherwise it paints a frame that changes it again, forever.

The sample has two **Performance** pages, and the pair is the point. Both load 1 to 100,000 entries
and report the last frame beside them; one fills a list box with rows, the other fills a panel with
real buttons. Scrolling the list stays flat in the item count, because it paints only the rows that
are on screen, and moves with the size of the *area* instead - which is what the area half of the
readout is for. The controls page has nothing virtualised: every button is a control, so the panel
arranges and walks all of them on every scroll and a hit test has all of them to sift. That is the
cost the list page avoids, and the reason to have both. On GTK4 both report whole-surface frames,
because that backend presents the whole surface every frame.

### Rich label

`RichLabel` flows styled runs of text into a wrapping paragraph. Formatting lives on
`TextSpan` objects held in the `Spans` collection; a span can also carry a child control
that is embedded **inline** in the text flow (CSS `inline-block` style) and a set of mouse
events that make it behave like a hyperlink.

| Member | Behaviour |
| --- | --- |
| `Spans` | Ordered list of `TextSpan`; add/remove directly or use the helpers below |
| `AddText(text)`, `AddSpan(span)`, `AddLink(text, onClick)` | Convenience appenders; `AddLink` returns the span so you can set `Tag`, colours or extra handlers |
| `AddInlineControl(control, alignment)` | Places `control` in the flow, aligned `Top`/`Center`/`Bottom` to its text line |
| `WrapText` | Word wrap (default `true`); when `false` the label is as wide as its longest line |
| `LineSpacing`, `TextAlign` | Extra leading between lines; nine-way block + line alignment |
| `Text` | The flow as plain text; assigning replaces every span with one unstyled span |
| `AddSpanClickHandler`, `SpanAt(point)` | Click notification and hit testing; a span also takes `AddClickHandler`, `AddMouseDownHandler`, `AddMouseUpHandler`, `AddMouseMoveHandler`, `AddMouseEnterHandler` and `AddMouseLeaveHandler` |

Span formatting: `Bold`, `Italic`, `Underline`, `Strikethrough`, `ForeColor`, `HoverBackColor`,
`FontSize`, `FontFamily`, or a whole `Font` override. Decorations and the hover/press background
are drawn once per contiguous run of a span, so an underline or highlight carries across the
spaces between its words instead of restarting at each one. Spans are hit-tested per **line**, so a
link that wraps across two lines is clickable on both, and a press on one span followed by a
release over another is not treated as a click. Any span with a mouse handler becomes
interactive: it shows a hand cursor by default (override with `Cursor`) and reports
`IsMouseOver`/`IsPressed` for custom painting. A hard line break is a `\n` inside a span's
text.

```csharp
var rich = new RichLabel { Dock = DockStyle.Fill, WrapText = true };

rich.AddText("Read the ");
rich.AddSpan(new TextSpan("terms of service") { Underline = true, ForeColor = Theme.Accent });
rich.AddText(" and the ");
rich.AddSpan(new TextSpan("privacy policy") { Bold = true });
rich.AddText(".\nInline controls:");

var link = rich.AddLink("documentation", span => Open(span.Tag as string));
link.Tag = "https://example.com/docs";
link.HoverBackColor = Theme.Secondary;

rich.AddText(" ");
rich.AddInlineControl(new Button("Retry") { AutoSize = true }, InlineAlignment.Center);
rich.AddText(" ");
rich.AddInlineControl(badge, InlineAlignment.Bottom);

rich.AddSpanClickHandler((_, e) => Console.WriteLine($"clicked {e.Span.Text}"));
```

Inline controls are measured like any other control (`AutoSize`, `Margin`, explicit sizes) and
are laid out by the label, so they receive input normally. A control taller than the text grows
its line and pushes the whole line down, so `Top`-aligned controls never paint outside the
label.

### Text box

`TextBox` is a single-line editor: caret, selection, clipboard, password masking,
placeholder text and horizontal scrolling are all implemented by the control itself. Multi-line
editing is a separate opt-in feature, described in [Multi-line text](#multi-line-text), and so are
spell-checker style annotations, described in [Text box spans](#text-box-spans).

| Member | Behaviour |
| --- | --- |
| `Text`, `AddTextChangedHandler` | Current value. Control characters (including line breaks) are stripped so the field stays single line. Assigning a shorter value clamps the caret and the selection to the new end rather than resetting them, and assigning the value the field already holds does nothing - which is what makes an echo binding free |
| `CaretIndex`, `SelectionStart`, `SelectionLength`, `SelectedText`, `HasSelection` | Selection state; the caret is `0 ... Text.Length` |
| `Caret` | The `Caret` that draws and blinks the insertion point; its `BlinkInterval`, `Width` and `Color` can be changed |
| `Select(start, length)`, `SelectAll()`, `ClearSelection()` | Programmatic selection |
| `MaxLength` | Characters the user may type (`0` = unlimited); enforced for insertions and replacements |
| `ReadOnly` | Selection and copy still work; typing, paste, cut and delete are blocked |
| `IsPassword`, `PasswordChar` | Draws one mask glyph per character (`'\u2022'` by default) |
| `PlaceholderText` | Muted text shown while the field is empty |
| `Font` | Optional override of `Theme.DefaultFont` |
| `Copy()`, `Cut()`, `Paste()`, `InsertText(text)` | Programmatic edit operations |

Keyboard: `Left`/`Right` move by character (`Ctrl` moves by word), `Home`/`End` jump to the
edges, `Shift` extends the selection, `Backspace`/`Delete` delete the selection or one
character, and `Ctrl+A`/`C`/`X`/`V` select all, copy, cut and paste. `Enter` is *not*
consumed, so a form can still treat it as an accept gesture. The mouse sets the caret,
drags a selection and supports double click (word) and triple click (everything).

```csharp
var name = new TextBox
{
    Dock = DockStyle.Top,
    PlaceholderText = "Type your name",
    MaxLength = 40,
};
name.AddTextChangedHandler(_ => Console.WriteLine(name.Text));

var password = new TextBox { Dock = DockStyle.Top, IsPassword = true };
var readOnly = new TextBox { Dock = DockStyle.Top, ReadOnly = true, Text = "fixed" };
```

The library itself stays platform free, so one thing remains the adapter's job:

* **Clipboard** - implement `IClipboard` (`GetText`/`SetText`, plus the optional
  `GetHtml`/`SetHtml` pair) and assign an instance to `Clipboard.Current` once at startup;
  without one an in-process fallback is used. The Win32 backend installs `Win32Clipboard`
  (`Win32Clipboard.cs`), which publishes a real `HTML Format` payload.

The caret is a control of its own: `Caret` owns the timer that blinks it and follows its
control's focus, enabled state and display state, so it only runs while the caret is actually on
screen - hiding a container above a focused field stops the blink, and showing it again resumes it -
and an idle application has no timer running at all. `TextBox.Caret` exposes the instance it uses, so
its interval, width and color can be changed, and a new editable control gets the same caret with
`new Caret(this)`. A control that animates something else does the same through `CreateTimer`,
which hands back an `Timer` the control owns and that is disposed with it. The wait itself comes
from the backend's `CreateTimer`, so a control asks for an interval and the platform decides how to
honour it; the underlying timer is created on the first `Start`, so a timer that never runs never
asks for one.

### Text box autocomplete

Autocomplete is an opt-in extension in `Kolvir.AutoComplete`, so a text box that never turns it
on carries none of its state and an application that never imports the namespace carries none of
its code. It adds the members a WinForms text box has:

| Member | Behaviour |
| --- | --- |
| `AutoCompleteMode` | `None` (the default), `Suggest`, `Append` or `SuggestAppend` |
| `AutoCompleteSource` | `CustomSource` (the values in `AutoCompleteCustomSource`) or `HistoryList` (the values the user has committed in this box) |
| `AutoCompleteCustomSource` | The values to suggest; read on every change, so adding one takes effect on the next keystroke |

The suggestion list is an `ListBox` in an `Popup` under the caret, so it can leave the
window when the platform layer provides popup windows. It never takes the keyboard: the text box
keeps the caret and forwards the keys to the list, the way the combo box does with its drop-down.
`Up`/`Down` move the highlight, `Enter` takes it, `Escape` closes the list without clearing the
text, and `Tab` closes it on the way to the next control. `Down` (or `Alt+Down`) on a closed field
opens the list with every value.

`Suggest` only shows the list. `Append` only completes: the rest of the first match is appended to
what was typed and selected, so the next keystroke replaces it. `SuggestAppend` does both. The
filter always runs against what the user typed, never against the appended text, and a deletion
never re-completes, so `Backspace` and `Delete` always delete something. Disabling the box closes
the list, and a read-only box neither opens it nor completes.

`HistoryList` records a value when the user commits it - with `Enter`, by taking a suggestion, or
by leaving the field - deduplicated case-insensitively, most recent first, and capped at 64 values
so it cannot grow without bound.

```csharp
using Kolvir.AutoComplete;

var city = new TextBox
{
    AutoCompleteMode = AutoCompleteMode.SuggestAppend,
    AutoCompleteSource = AutoCompleteSource.CustomSource,
};
city.AutoCompleteCustomSource.Add("Amsterdam");
city.AutoCompleteCustomSource.Add("Athens");
```

### Multi-line text

`Multiline` turns the same `TextBox` into a multi-line editor. It is an opt-in extension in
`Kolvir.Multiline`, so a text box that never turns it on carries none of its state and an
application that never imports the namespace carries none of its code. It adds:

| Member | Behaviour |
| --- | --- |
| `Multiline` | Off by default. When true `Enter` inserts a line break, the text wraps or scrolls sideways, and a scrollbar appears when the content is taller than the box. Turning it off again drops the line breaks and releases the feature |
| `WordWrap` | True by default. True wraps at word boundaries and never scrolls sideways; false keeps each logical line on one visual line and adds a horizontal bar |
| `VerticalScrollBar`, `HorizontalScrollBar`, `VerticalOffset`, `HorizontalOffset` | The bars and the scroll state, for an application that wants to read them |

```csharp
using Kolvir.Multiline;

var notes = new TextBox
{
    Dock = XDock.Fill,
    Multiline = true,
    PlaceholderText = "Write something",
};
```

The line model is visual: a logical line is the run between two line breaks, and word wrapping
splits a logical line into further visual lines without inserting characters. The caret and the
selection are still plain text indices, so `CaretIndex`, `Select`, `SelectionStart`,
`SelectedText` and the control-level selection all keep working unchanged, and a selection may
span any number of visual and logical lines. Soft wrapping breaks at the start of a word that does
not fit, so the whitespace before it stays at the end of the line instead of opening the next one;
a word wider than the box is broken between two of its own characters.

Keyboard, in addition to the single-line keys: `Enter` inserts a line break, `Up`/`Down` move by
visual line and keep the column they started in, even across a line that is too short to have one,
`PageUp`/`PageDown` turn a page and take the view with them, and `Home`/`End` go to the start and
end of the *visual* line the caret is drawn on. `Left`/`Right` move by character, so they cross a
soft wrap and a line break the same way. `Ctrl+Home`/`Ctrl+End` still reach the start and end of
the whole value.

Three behaviours are deliberate and worth stating:

* **`Enter` inserts a line break and `Ctrl+Enter` is left unhandled**, so a form around a
  multi-line field still has an accept gesture; `Enter` in a single-line field is untouched.
* **`Tab` still moves the focus.** A multi-line field here is not a code editor, and a tab that is
  invisible in a proportional font is worse than one that leaves the field. Pasted tabs are
  expanded to four spaces, so the value never contains a tab the user cannot type.
* **Autocomplete completes one line at a time.** The suggestion list hangs from the caret's own
  line rather than from the bottom of a tall field, a suggestion replaces the text on that line and
  leaves every other line alone, the history source records nothing from a multi-line field, and
  `Append`/`SuggestAppend` complete nothing at all - a suffix cannot cross a line break, and
  rewriting a document from a single-line suggestion would throw away the rest of it - so both
  behave as `Suggest`. Typing a line break closes the list, because the new line's empty prefix has
  nothing to suggest.

Scrolling reuses `ScrollBars`, the helper the scrolling containers share, so the bars, the
wheel, the drag and the clamping behave like every other scrolling control. The scrolled region is
the text area inside the padding; a bar takes its gutter out of that area, so the text never runs
underneath one.

`MaxLength` counts characters, and a line break is one character, so a limit applies to the value
the box actually holds rather than to a platform-specific encoding of it. Nothing is inserted past
the limit, and an insertion that would cross it is cut where the limit falls.

Line breaks are normalised on every path that brings text in - assignment, typing, paste and
programmatic insertion - so `CRLF` and a lone `CR` both become the single `'\n'` the line model
uses. Copy and cut hand out that value, and `Text` never holds a `\r`, so text that leaves and
pastes back is unchanged.

### Text box spans

`Spans` marks runs of characters with a squiggly underline, the way a spelling or grammar checker
marks a word. It is an opt-in extension in `Kolvir.Spans`, so a text box that never assigns a list
carries none of its state and an application that never imports the namespace carries none of its
code. It adds:

| Member | Behaviour |
| --- | --- |
| `Spans` | The annotated runs, or null. Assigning replaces the whole set; assigning null or an empty list removes the feature again |
| `TextBoxSpan` | One run: `Start`, `Length`, `End`, the squiggle's `Color`/`ThemeColor`, its `Thickness`, `Amplitude` and `Wavelength`, and a `Tag` for the caller's own data |
| `ContextMenuEventArgs.Span` | The span the right-click landed in, or null, as an extension member on the existing context-menu event args |

```csharp
using Kolvir.Spans;

// A subclass hangs whatever the checker needs on the base type.
sealed class SpellingError(int start, int length, string[] suggestions) : TextBoxSpan(start, length)
{
    public string[] Suggestions { get; } = suggestions;
}

// A grammar note is the same squiggle in a different colour and shape.
var grammar = new TextBoxSpan(start, length) { ThemeColor = ThemeColor.Accent };

field.Spans = [ new SpellingError(start, length, ["their", "there"]), grammar ];

// The field answers its own right-click, the way any control does. The menu it contributes already
// holds Cut/Copy/Paste/Select All; the handler puts the suggestions in front of them.
field.AddContextMenuOpeningHandler(args =>
{
    if (args.Span is not SpellingError error || args.Menu is not { } menu)
    {
        return;
    }

    menu.Insert(0, new MenuItem("&Add to dictionary", () => Ignore(error)));
    menu.Insert(0, new MenuItem("&Ignore", () => Ignore(error)));
    menu.Insert(0, new MenuItem("&Replace...", () => Suggest(error.Suggestions)));
    menu.Insert(3, MenuItem.Separator);       // then the ordinary editing commands
});
```

The squiggle is drawn from the same geometry the selection highlight uses - one rectangle per
visual line, clipped to the text area and shifted by the scroll offset - so a span that crosses a
soft wrap in a multi-line field is marked on both visual lines, a span on a line scrolled out of
view is not drawn at all, and a span on a half-scrolled line is squeezed into the sliver that is
left instead of being drawn past it.

Vertically it sits in the space the font reserves under the letters: the band starts a small gap
below the baseline and ends inside the descent, both of them fractions of the font size rather than
pixel constants, so the marker follows the text at any size and any display scale. A glyph without
a descender therefore never touches it, and a glyph with one crosses it.

The paint order is selection highlight, squiggle, text, caret - the same on a one-line and a
multi-line field. The wash goes under the squiggle because `Theme.Selection` is caller-supplied
and may be opaque, which would otherwise hide a marked word exactly while the reader is looking at
it, whereas a descender crossing the wave reads better than a wave cut out of the descender. A
marked word therefore stays marked while it is selected, and the caret is never covered.

Three contract decisions are worth stating:

* **The list is caller-owned.** The box keeps the reference, never copies, reorders, merges or
  mutates it, and never rewrites a span when the text changes. A checker that re-runs on every
  keystroke can clear its own `List<TextBoxSpan>`, refill it, and assign it again: no
  allocation on the box's side, one repaint, and the new contents are what is drawn.
* **Stale spans are clamped, never thrown.** A span that starts past the end of the text draws
  nothing and resolves no right-click, one that runs past the end is drawn up to the last
  character, and a span with a length of zero is skipped. Editing the text under a set of spans is
  therefore safe; keeping them in step is the caller's job, because only the caller knows what the
  annotation meant.
* **Overlaps are allowed.** Both spans paint, in list order, and a right-click inside both resolves
  to the last one in the list - the one drawn on top.

`Start` and `Length` are fixed at construction and a negative value throws
`ArgumentOutOfRangeException`; a start or length beyond the current text is not an error, because
a span is validated against the text that is drawn rather than the text that was there when it was
made. The squiggle's shape is data rather than code: `Amplitude` sets how far the wave rises above
and falls below its own centre line (zero draws a straight underline, which is how some editors
mark a warning), and `Wavelength` sets one full cycle's width.

### Text box undo and redo

`UndoEnabled` gives a text box an undo history. It is an opt-in extension in `Kolvir.Undo`, so a
text box that never turns it on carries none of its state and an application that never imports the
namespace carries none of its code. It adds:

| Member | Behaviour |
| --- | --- |
| `UndoEnabled` | Off by default. True keeps a history; turning it off again releases it and drops what it held |
| `CanUndo`, `CanRedo` | Whether `Undo()`/`Redo()` would change the text. Both report false while the box is `ReadOnly` |
| `UndoCount`, `RedoCount` | How many entries each side holds |
| `Undo()`, `Redo()` | Take the newest entry back out of the value, or put it back. Each returns false when there was nothing to do |
| `ClearUndoStack()` | Drops both sides and leaves the text and the insertion point alone. A no-op on a box that never enabled the history |
| `AddUndoStateChangedHandler` / `RemoveUndoStateChangedHandler` | Called after an edit is recorded, undone, redone or dropped - every moment `CanUndo` or `CanRedo` can change - so a menu or a pair of buttons can follow the field |
| `UndoClock.Current` | The time source the five-second window is measured from. Defaults to `TimeProvider.System` |

```csharp
using Kolvir.Undo;

var notes = new TextBox
{
    Multiline = true,
    UndoEnabled = true,
};

notes.AddUndoStateChangedHandler(_ => refreshUndoButtons());   // Ctrl+Z is already the box's own
refreshUndoButtons();

void refreshUndoButtons()
{
    undoButton.Enabled = notes.CanUndo;
    redoButton.Enabled = notes.CanRedo;
}
```

`Ctrl+Z` undoes; `Ctrl+Y` and `Ctrl+Shift+Z` both redo, which is the GTK and modern Windows
spelling for redo as well as the one Windows has always had. On macOS the same gestures are
`Cmd+Z` and `Cmd+Shift+Z`: AppKit consumes a Command chord before a view ever sees it, so the
AppKit backend's own Edit menu carries **Undo** and **Redo** next to Cut, Copy, Paste and Select
All and routes them back into the UI host, exactly as it already does for the editing commands.
The physical Control key works there too, because `Modifiers.Control` is what the AppKit
backend reports for it.

Six behaviours are deliberate and worth stating:

* **A run of characters is one entry.** Each character joins the entry before it while it arrives
  within five seconds of the *previous character*; a longer pause starts a new entry, and the
  five-second mark itself still belongs to the run. Backspace and Delete coalesce by the same rule
  on runs of their own, so a burst of typing undoes as one step, a burst of deleting undoes as one
  step, and the two never merge into one entry.
* **A change over a non-zero selection is always one entry of its own**, and it ends any run. Undo
  therefore restores the replaced text in one step instead of un-replacing it a character at a time.
  Characters typed *after* that replacement are a new run, so a word typed over a selection is two
  entries: the replaced run, then the rest of the word.
* **The insertion point is part of every entry.** Each one records the caret and the selection on
  both sides of the change, including the direction of a right-to-left selection, and undo and redo
  put them back - so the caret returns to where the user was typing rather than to position 0 or to
  the end of the value.
* **A programmatic `Text` assignment is undoable when it changes the value, and free when it does
  not.** The setter already ignores an assignment of the value the box holds, so a two-way binding
  that echoes each keystroke back writes the same string, records nothing and does not break the
  running character entry. An assignment that really changes the value - a document loaded into the
  field, a form resetting it - is one entry that restores the previous value. A binding that
  *transforms* what it echoes (trimming, upper-casing) is the one case that records an entry per
  write, so call `ClearUndoStack()` after loading data, or leave `UndoEnabled` off on such a field.
* **Read-only has no undo.** A read-only field records nothing, `CanUndo` and `CanRedo` report false,
  and `Undo()`/`Redo()` do nothing. The history is kept rather than dropped, so a field that is
  locked for a while gets it back when it is unlocked. `ClearUndoStack()` still works.
* **The history has a depth and a memory cap.** It keeps the newest 100 entries, and drops the
  oldest whenever that, or a one-million-character budget for the text the two stacks pin, is
  exceeded. An entry is a delta - the run it replaced, the run it inserted, two insertion points
  and the scroll offsets - so typing in a large multi-line document costs a few bytes an entry
  rather than a copy of the document. A paste larger than the budget keeps its own entry and drops
  everything older, so the history can never pin an unbounded amount of memory.

Undo and redo are ordinary text changes as far as the rest of the toolkit is concerned, which is
what makes them cooperate with the two features that also watch the value:

* **Spans are the caller's, as always.** The box never rewrites a `Spans` list, so undo restores
  text and leaves the annotations describing the text they were computed for. A checker re-marks
  after the change - `AddTextChangedHandler(_ => Remark(field))` covers typing, paste and undo with
  one line - and a stale span is clamped where it is drawn rather than throwing.
* **Autocomplete treats undo as an assignment.** It adopts the restored value, drops any inline
  completion that was pending and closes the suggestion list, so there is no half-applied
  completion for the next keystroke to extend.
* **Multi-line fields record the view with the entry.** Undo restores the caret, scrolls it into
  view, and then puts the recorded scroll offsets back, so the view returns to where the edit was
  made. Undo spans line breaks like any other character. Turning `Multiline` on or off clears the
  history, because the value changes shape - line breaks are added to or dropped from it - and an
  entry from one shape cannot be applied to the other.

The history runs no timer. The five-second window is measured from `UndoClock.Current` at the
moment of each edit, so an idle application has nothing ticking for it, and a test can replace the
clock and drive the whole history - gaps and all - without sleeping.

**Undo and Redo also lead the box's context menu.** The block a box contributes to a right-click is
now Undo, Redo, a separator, Cut, Copy, Paste, a separator, Select All, the order an editor reads
in. Both rows are enabled by the value the menu is built from - `CanUndo` and `CanRedo`, so a
read-only field shows them disabled - and both run the same operation the keys do, which means a
click and `Ctrl+Z` are the same edit and both put the caret back. A box that never turned
`UndoEnabled` on gets **no** Undo or Redo rows at all: it has no history to offer, and a consumer who
did not ask for one should not have two rows in the menu they designed appear for it. A box that
*has* the history keeps both rows even when they are disabled, so the menu does not change shape as
the stack fills and empties. Because the rows are part of the control's own block, a handler that
replaces `args.Menu` still replaces all of it, Undo and Redo included.

### Selection and the clipboard

`Selectable` opts a control into pointer selection; `SelectionStyle` chooses between selecting
the control as a whole (`Whole`) and selecting the text inside it (`Partial`). A drag can start
in one control and finish in another, so the selection lives on the host
(`host.Selection`) rather than in a control, and `host.ClearSelection()` plus
`control.ClaimSelection()` keep exactly one selection - of any kind, including a text box's own
caret and selection - alive at a time.

When a selection is copied, every control it touches contributes its own piece in visual order:

| Member | Behaviour |
| --- | --- |
| `GetSelectionText(start, length)` | The plain-text piece for a slice; override to control what a copy pastes into a text target |
| `GetSelectionHtml(start, length)` | The HTML piece for the same slice, or `null` (default) to fall back to the escaped plain text |
| `host.CopySelection()` | Writes both flavours to `Clipboard`; `host.SelectionTextSeparator` joins the pieces |
| `host.ProcessContextMenu(point)` | Builds the menu: the control's own handler, then its `ContextMenu`, then the `AddContextMenuOpeningHandler` handlers on the host, then a default **Copy** item |

`RichLabel` overrides `GetSelectionHtml` so a copy keeps its formatting: `<b>`, `<i>`,
`<u>`, `<s>` and a `<span style="...">` for colour and size overrides, wrapped around the
*selected slice* of each span - a selection that starts mid-span gets markup for just those
characters. A selection spanning several controls joins their pieces with a line break.

```csharp
var html = rich.GetSelectionHtml(12, 4);   // "<b>bold</b>"
Clipboard.SetHtml(rich.GetSelectionText(12, 4), html);   // both flavours at once
```

#### Built-in controls

| Control | Defaults | Notes |
| --- | --- | --- |
| `Label`, `RichLabel` | `Selectable = false`, `Partial` | Set `Selectable`; a drag selects character by character |
| `TextBox` | `Selectable = true`, `Partial` | Its own caret and anchor answer for a drag that *starts* inside it; a drag that merely passes through it selects its text and drops the caret |
| `Button`, `Image` | `Selectable = false`, `Whole` | Set `Selectable`; a drag over the control selects it as one unit |
| `CheckBox`, `RadioButton`, `ProgressBar` | `Selectable = false`, `Whole` | Only the check state of a toggle is its own; the label is not selectable text |
| `ListBox` | `Selectable = false`, `Whole` | Its row selection is its own; the host selection is not involved |
| `Panel` and other containers | `Selectable = false`, `Whole` | A selectable container hides its children from the selection, so leave containers off unless the container itself is what should be selected |

A text box is selectable by default and does not paint a second overlay: `GetSelectionOffset` and
`GetSelectionRects` bridge the host's selection into the same `IndexFromX`/`MeasureX` axis the
caret already uses, so the single-selection rule above still holds.

A text box also builds its own menu - **Undo** and **Redo** when the box has an undo history, then
**Cut**, **Copy**, **Paste** and **Select All**, with their shortcuts and per-item `Enabled` states
- so a right-click inside one always opens a menu. Its
**Copy** falls back to `host.CopySelection()` when the box has no selection of its own, which is
what makes copying a multi-control selection work no matter which control was right-clicked.

### Context menus

`host.ProcessContextMenu(point)` builds an `ContextMenu` and hands it to the presenter
installed on the host. The library owns the model; the presenter decides how to draw it, and there
are two families of presenter: the platform's own menus, and the portable one that draws every menu
with Skia. The portable one is the default, because a menu drawn from Skia is the same menu on
every platform and can carry icons, submenus and controls in a row.

| Piece | What it does |
| --- | --- |
| `MenuItem` / `MenuBar` / `ContextMenu` | The model: items, separators, submenus, checked and disabled state, shortcuts, icons and hosted controls |
| `Control.AddContextMenuOpeningHandler` | A control's own right-click behaviour: runs for the control under the pointer, or the nearest ancestor that has one, and receives the menu the control contributed so it can add to it |
| `UiHost.AddContextMenuOpeningHandler` | The window-wide fallback, run only when nothing under the pointer ran a handler or supplied a menu |
| `MenuRenderer` | `Measure(menu, metrics, font, shadow, availableWidth, availableHeight)` lays the rows out and returns an `MenuLayout`: surface size, shadow margin, row bounds, scroll state, `HitTest` and `NextPickable`. `Paint(canvas, layout, hovered, surfaceAlpha, ..., scrollOffset)` draws the surface, drop shadow, hover pill, check column, icons, right-aligned shortcut hints and - for a menu that does not fit - the arrows at either end. It does not clear the canvas, so whatever the caller painted underneath shows through a translucent surface |
| `MenuMetrics` | Row height, padding, corner radii, shadow extent, the check/icon/control column widths and `ScrollIndicatorHeight`; `Default` is tuned to the Windows 11 menu |
| `MenuView` | The control that shows one menu level, its in-menu controls and the submenu it has open |
| `MenuPresenter` | Shows an `ContextMenu` in an `Popup` with `MenuView`, one popup per level of the chain |
| `MenuBarView` | The menu bar drawn inside the window |
| `Win32MenuWindow.cs` | The popup window the Win32 backend shows a menu in: layered, so the rounded corners and the drop shadow come from per-pixel alpha, with the acrylic backdrop described below |

`MenuRenderer` contains no platform code, so every backend draws the same menu from it.

A context menu is **composed**, not replaced. The control under the pointer contributes its own
menu first, and every handler after it receives that same menu to add to: a text box starts from
Undo and Redo when it has an undo history, then Cut, Copy, Paste and Select All, each enabled by the
rule its command obeys (nothing selected means
no Cut or Copy, a read-only field means no Cut or Paste, an empty clipboard means no Paste, empty
text means no Select All). A container can give every child the same handler, and nothing outside
the control has to know which control was clicked:

```csharp
// Spell-check suggestions in front of the field's own editing commands, with one separator
// between the two blocks. Attached once, where the field is built.
field.AddContextMenuOpeningHandler(args =>
{
    if (args.Span is not { } span)
    {
        return;                                     // the editing commands stay as they are
    }

    var menu = args.Menu ??= new ContextMenu();
    menu.Insert(0, new MenuItem($"&Ignore \"{span.Tag}\"", () => Ignore(span)));
    menu.Insert(0, new MenuItem($"&Replace \"{span.Tag}\"", () => Replace(span)));
    menu.Insert(2, MenuItem.Separator);         // Cut / Copy / Paste follow
});

// The window's own menu over its empty space stays a window concern.
host.AddContextMenuOpeningHandler((_, args) =>
{
    if (args.Control is not TextBox && host.Selection.IsEmpty)
    {
        args.Menu = BuildPageMenu();                // nothing under the pointer had one of its own
    }
});
```

The order `ProcessContextMenu` asks in is: the control's own menu (its `ContextMenu`, or the
nearest ancestor's, or what it builds from its own state) handed to the control's handlers, then to
its nearest ancestor with a handler, then - if no handler ran and no menu came out of that - to the
host's handlers, and finally the selection's **Copy** menu. The host's handlers are the
application-wide fallback, so a text box's editing commands are never shadowed by a page menu. The
event arguments say where the menu came from - `Control` and `Location`, and for a text box with
annotations the span that was hit.

To suppress what the control contributed, replace `args.Menu` (`args.Menu = new ContextMenu()`)
or clear it (`args.Menu?.Clear()`) and add your own; a handler that only adds leaves the block
intact. Answering matters: when control, handlers and selection all decline, `ProcessContextMenu`
shows nothing on any presenter, so a page with no menu-bearing control under the pointer looks
unresponsive. A window that wants a menu over its empty space therefore answers itself.

### Menus drawn by the library

`MenuView` is the menu surface. It is a plain control: it paints through the normal control
paint path, takes its colours from `Theme` at paint time and answers `MeasureContent`, which is
what lets the presenter size the popup around it:

```csharp
var menu = new ContextMenu()
    .Add(new MenuItem("&Open...") { Image = openIcon, Shortcut = new Shortcut(Key.O) })
    .AddSeparator()
    .Add(new MenuItem("Show &grid") { Checked = true });

listView.ContextMenu = menu;
```

A menu bar is the same view with a strip above it. `MenuBarView` lays the top-level titles out
in a row, paints the hover and open states, and opens a drop-down in an `Popup` so it can hang
past the bottom of the window:

| Behaviour | How it works |
| --- | --- |
| Reaching the bar | The bar is not focusable and takes no part in tab order. `Alt` plus a mnemonic is what reaches it, because `UiHost.ProcessKeyDown` offers the key to the bar before the focused control |
| Switching menus | Pressing another title while a menu is open switches menus instead of closing and reopening, the way a native bar does |
| Keys inside a menu | Up/Down walk the pickable rows (separators, disabled rows and control rows are skipped), Right opens a submenu, Left closes it, `Home`/`End` jump, `Tab` moves between the menu's own rows rather than leaving the menu, `Enter` or `Space` invokes, `Esc` closes the innermost menu and then dismisses the whole chain, and a mnemonic letter picks a row directly |
| Dismissal | A click outside, losing activation or `Esc` closes the chain. The popup never takes the keyboard, so the menu is pushed onto the *owning* window as an `IInputSink` for as long as it is open |

Submenus are separate popups, not a child view drawn inside the menu that opened them: the presenter
keeps a chain of levels, each with its own `Popup` and `MenuView`, and opening a submenu
opens the next window instead of growing the one already on screen. That is what keeps a menu still:
a submenu that is taller than the room beside its parent, or that opens and closes as the pointer
crosses a row, can never resize or move the menu it hangs off. A submenu sits against the edge of the
menu that opened it, overlapping by `SubMenuOverlap`, with its first row level with the row that
opened it - so it is placed a little above that row, as `MenuMetrics.Padding` dictates.

Which side a submenu opens on is decided against the display, not the window: the presenter asks
`IPopupService.GetPlacementArea` for the area a popup is allowed to occupy, which on Windows is
the monitor's work area and on macOS the screen's, and a submenu that would not fit to the right
inside that area opens to the left of the menu that opened it instead. Each level is clamped into the
area on its own, which is also why a menu can hang past the edge of the window it was opened from.
Once a chain has turned left it keeps going left, so a submenu of a submenu does not zigzag back
across its parent. GTK4 is the one backend that reports no area, because a popover is clipped by its
window there, so a menu on Linux is laid out inside the window and flips at the window's edge.

Submenus nest as deeply as the model does: `MenuItem.SubItems` is the same list at every level,
and since every level measures, paints and hit-tests only its own rows while the keyboard walks the
chain from the innermost menu outwards, nothing has to know how long the chain is. Nothing caps the
depth;
the demo's context menu carries `Recent > Program.cs > a place in the file` to prove it. On macOS
the same chain reaches the system bar, because `AppKitMenuBar` translates every level into a real
`NSMenu` submenu and lets AppKit draw and drive it.

A menu never grows past the room it is given. The presenter caps the root menu to the placement area
minus the room its drop shadow needs, and a submenu is capped to the room below the row that opened
it - sliding up to the top of the available space if not even one row fits below that row. A menu
that still does not fit keeps its width, stops growing, and scrolls its rows instead:

| Part | How it works |
| --- | --- |
| The window | `MenuLayout.ContentHeight` is the height the rows need and `SurfaceSize.Height` the height the menu is allowed, so the surface is a window onto the rows. `ContentViewport` is the surface minus the two strips |
| The strips | `ScrollIndicatorHeight` (20 by default) is reserved at the top and bottom and painted opaque, so a half-scrolled row cannot read through the arrows. The arrow at an end is greyed out once there is nothing more in that direction. Set `ScrollIndicatorHeight` to zero to turn scrolling off and let the menu overflow instead |
| The pointer | The wheel scrolls; `MenuView.ScrollBy` and `ScrollTo` are the same thing from code, and a press on either arrow scrolls by one row and never picks an item |
| The keyboard | `Highlight` scrolls the highlighted row into view, so Up/Down/Home/End walk a long menu instead of moving the highlight off the visible part of it |

An item can carry an icon, a tick, or a control. Icons are drawn in their own column and the tick in
the check column, and `MenuTrimming.Trim` drops a separator that would lead, trail or double up,
so a menu built by filtering a list still looks right. An in-menu control is a real control: it is
parented to the open menu view, laid out in its row and taken out again when the menu closes. A
control has one parent, so if two open menus share one, only the first shows it.

`MenuItem.DisplayText` is the label with the mnemonic marker removed and never contains `&`;
`Text` is the one that does. Mnemonics are matched against `MenuBar.Menus[i].Mnemonic`, which is
the letter the item declared.

### Choosing native or portable menus

`ApplicationOptions.MenuPresentation` picks the family, and a backend resolves it once when it
creates a window:

| Value | Meaning |
| --- | --- |
| `Auto` | The library's menus, on every platform |
| `Kolvir` | Always the library's menus |
| `Native` | Always the platform's menus: the system bar on macOS, `TrackPopupMenuEx` and GTK4 popovers on Windows and Linux |

A window drawn with Skia is drawn with Skia's menus too, so `Auto` does not fall back to the
platform anywhere: a menu keeps its shape, its icons and its keyboard handling wherever the
application runs.

The menu bar is the one exception, and only on macOS. The platform has a single menu bar for the
whole process, owned by AppKit and drawn at the top of the screen, and no way to put one in a
window, so the bar there is the system's whichever presentation is asked for. It carries the same
menus, with the same drop-downs, icons, shortcuts and submenus, but it is the system's row of titles
and not `MenuBarView`, so what belongs to that view is not available: no bar inside the window,
no `MenuBarView.TitleHeight` or bar font, and no controls hosted in a bar row. The system bar
also follows the active window rather than belonging to one window, and macOS answers a mnemonic
from the first character of a title rather than from the `&` marker. A drop-down that is too tall for
the screen scrolls with AppKit's own arrows rather than the library's, so `ScrollIndicatorHeight`
does not reach it. This is a platform limitation, not a switch.

`MenuPresentation.Native` is the escape hatch, and on macOS it additionally hands context menus
to AppKit.

The sample takes `--menus kolvir|native` to compare the two on any platform. On Windows,
`--classic-menus` is a further step down to `TrackPopupMenuEx` even when the portable menu is
selected, which is only useful for looking at the difference.

The acrylic on Windows is produced by the popup rather than by DWM, because DWM ignores
`DWMWA_SYSTEMBACKDROP_TYPE` on a layered window and translucency without blur would leave the text
behind the menu legible through it. Instead the popup copies the screen region it is about to cover
before it is shown, blurs that copy, adds a fine noise and paints the surface over it at 80% (dark)
or 84% (light) opacity - the same three layers real acrylic is made of. The blur is baked into the
captured image because Skia ignores an image filter on the paint passed to `DrawImage`. If the
capture fails, the surface falls back to fully opaque.


### Keyboard shortcuts

`ShortcutText` is only a hint: it says what the menu draws on the right, and nothing dispatches it.
A shortcut that actually works is an `Shortcut` on the item:

```csharp
var dark = new MenuItem("Dark Mode")
{
    Shortcut = new Shortcut(Key.D),
    Click = () => demo.SetAppearance(Palette.Dark),
};
```

`Shortcut(key, modifiers)` defaults `modifiers` to `Modifiers.Control`, which means the
platform's accelerator modifier rather than a literal Ctrl key: `DisplayText` says "Ctrl+D" on
Windows and Linux and "Cmd+D" on macOS, and AppKit turns the same declaration into a real key
equivalent on the menu item. A shortcut with no modifier is rejected, because a bare key belongs to
the focused control.

The window checks its menu bar - the one on the `Window`, falling back to
`ApplicationOptions.MenuBar` - before the focused control sees the key, which is the order
native menus use. A matching item claims the key even when it is disabled, so a greyed-out command
does not silently fall through to the control that has focus. The lookup walks into submenus, so a
shortcut declared one or more levels down is still dispatched. Because the lookup happens in
`UiHost.ProcessKeyDown`, it works on every backend and whichever way the bar is presented; on
macOS the key equivalent on the `NSMenuItem` answers it first.

`ShortcutText` still exists for a menu that wants to describe a key it does not dispatch itself;
when it is set it wins over the shortcut's own text in the menu drawing.

### Appearance

Every default colour in the library is a palette role, so the whole UI can switch between light and
dark at runtime. `Theme.Palette` holds the current one:

```csharp
Theme.Palette = Palette.Dark;   // or Palette.Light, or your own
Theme.IsDarkMode;                    // shortcut for Palette.IsDark
var accent = Theme.ColorOf(ThemeColor.Accent);
```

`Palette` is a record with one colour per `ThemeColor` role - `Background`, `Surface`,
`Foreground`, `MutedForeground`, `Border`, `Accent` (`Hover`/`Pressed`/`Foreground`),
`Secondary` (`Hover`/`Pressed`), `FocusRing`, `Splitter` (`Hover`), `InputBackground`, `Selection`,
`Caret`, `Placeholder` and `DisabledBackground`/`DisabledForeground` - so a custom theme is just
`Palette.Light with { Accent = SKColors.Teal }`. Assigning the palette (or any single colour,
e.g. `Theme.Accent = ...`) runs the `Theme.AddChangedHandler` handlers and the next frame
picks it up: controls never snapshot a colour at construction time.

Controls resolve their colours while painting - through the inherited `Background`/`Foreground`
when the control itself assigns nothing - so a palette change re-applies nothing:

| Member | Meaning |
| --- | --- |
| `Background` | The background state - inherited from an ancestor, painted from a colour or palette role, or nothing at all; see [Inherited appearance](#inherited-appearance) |
| `BackColor` | The WinForms name for `Background`: the effective colour, or `null` when nothing is painted |
| `DefaultBackground` | Override to return the palette role a control of this class paints - the base control paints nothing |
| `ForeColorRole` | The role used while neither this control nor an ancestor assigns a `Foreground` |
| `BorderColor` | An explicitly assigned colour, otherwise `Theme.Border` |
| `Opacity` | Fades the control and everything inside it as one group, 0 to 1 |
| `TextSpan.ThemeColor` | A role for one span of a rich label; `AddLink` uses `Accent` |
| `TextSpan.HoverBackColorRole` | The role tinted behind a span while it is hovered |
| `UiHost.ClearColor` | Follows `Theme.Background` until assigned |

The library itself never asks the OS anything. A backend implements the two-property
`ISystemTheme`, installs it, and calls `host.RefreshTheme()` whenever the OS reports a change:

```csharp
SystemTheme.Current = new Win32SystemTheme();   // any ISystemTheme
SystemTheme.Refresh();                         // read IsDarkMode, swap the palette if needed
```

`SystemTheme.Refresh()` returns whether the appearance actually changed, and
`host.RefreshTheme()` additionally invalidates the tree. The Win32 backend
(`Win32SystemTheme.cs`) reads
`HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` (falling back
to the undocumented `uxtheme` `ShouldSystemUseDarkMode`), reacts to `WM_SETTINGCHANGE` and
`WM_THEMECHANGED`, and darkens the title bar with
`DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)` - driven by `Theme.Changed`, so a manual
palette change updates the chrome too.

### Inherited appearance

`Font`, `Foreground` and `Background` are properties of `Control`, and they are inherited down
the tree the way WinForms' `Font`, `ForeColor` and `BackColor` are. Whatever a control assigns
answers for itself and for everything inside it; a control that assigns none asks its parent, and a
tree that assigns none falls back to the class default, which is where `Theme` supplies the
role.

```csharp
var group = new Panel { Font = title, Foreground = Theme.Accent, Background = Background.FromThemeColor(ThemeColor.Secondary) };

group.Controls.Add(new Label("24 pt, accent, on the secondary colour"));       // inherits all three
group.Controls.Add(new Label("has its own") { Font = small });                 // overrides the font only
group.Controls.Add(new TextBox());                                             // paints its own input fill
```

The precedence is the one WPF uses for an inherited property: **a value assigned here, then the
nearest ancestor that assigns one, then this control's class default.** `DefaultFont` and
`DefaultBackground` are the class defaults, and `Theme` is what they contain:

| Member | Meaning |
| --- | --- |
| `Font` | The effective font: assigned here, else the nearest ancestor's, else `DefaultFont` |
| `DefaultFont` | The font a control of this class draws with when nothing else answers; the base control returns `Theme.DefaultFont`, a button and a group box `EmphasizedFont` |
| `ClearFont()` | Forget the font assigned here, so the control inherits one again |
| `Foreground` (`ForeColor`) | The effective text colour, inherited the same way; `ForeColorRole` is the class default and is *not* inherited |
| `Background` | An `Background`: `Inherit`, `None`, or a colour or palette role |
| `DefaultBackground` | The role this class paints when nothing else answers; `None` - the base control's answer - means it paints nothing |
| `BackColor` | The WinForms name for `Background`: the effective colour, or `null` when nothing is painted |
| `ClearBackground()`, `ClearForeground()` | Back to inheritance |

Only an assigned value is inherited; a class default is not. That is what keeps today's look intact:
a label inside a group box stays at `DefaultFont` rather than picking up the caption's
`EmphasizedFont`, and a label under a panel that sets `ForeColorRole` keeps its own role rather than
inheriting the panel's. Assign a `Foreground` when a whole subtree really should share one.

**`Background` has three states, and the difference between the last two is the point.**

* `Background.Inherit` - the initial value: ask the parent.
* `Background.None` - paint nothing here.
* a colour, or `Background.FromThemeColor(role)` - paint that, here and for everything below.

A nullable colour would have to mean both "no value" and "draw nothing", and then a label inside a
filled panel would silently start painting a box of its own - with its own corners and opacity -
the first time a panel got a colour. So a control whose class default is `None` (a label, a toggle,
anything that draws no chrome) keeps painting nothing wherever it sits, and a control that does
paint (a panel, a field) takes the nearest ancestor's assigned colour instead of its own default.
Only a colour is inherited, not `None`: a layout container that paints nothing is the ordinary
parent of a card that does, and suppressing what is inside it would take the card away with the
container. A control that must not be filled assigns `None` to itself.

**Nothing is captured when a control is built.** The effective font is resolved when text is
measured and painted, never in a constructor, so a control created before the theme was configured
still adopts it, and a live tree can be changed afterwards:

```csharp
var panel = new Panel();
panel.Controls.Add(new Label("measured at one size"));
...                                   // the tree is built, laid out and on screen
panel.Font = Theme.TitleFont;    // the label re-measures and the panel re-lays out
Theme.FontFamily = "Inter";      // and the same for the theme, for every control that did not assign a font
```

What the parent chain contributes is walked once and cached on the control that asked - not on every
glyph measurement. The cache is dropped for a control and everything under it when a value is
assigned to an ancestor, when the control changes parent, or when the host sees a new theme font
generation. Class defaults and palette roles are *not* cached: they are read when they are used, so
a palette swap is still a repaint rather than a rebuild, exactly as before. A font is the size of
the text, so any of these changes re-runs layout - `UiHost` lays the whole tree out again on the
frame that follows a theme font change, and assigning a font re-measures from the root at once.

Fonts are not owned by the control that draws with them. Never dispose one that was inherited or
came from `Theme`; keep an assigned font alive for as long as it is in use, and let the theme
own the face given to `Theme.UseFont`.

The cost is the one [FEATURE-COST-POLICY.md](FEATURE-COST-POLICY.md) section 3 names for members on a type
every application touches, and this feature is deliberately that: the three properties *are* the
control's appearance, not an optional extra, so there is no lower rung to sit on. Measured on the
trimmed `Kolvir.Minimal` fixture, `win-x64`, publishing at the commit before this one and after it:
8,651,264 -> 8,654,848 bytes, a difference of **+3,584 bytes (0.04%)** - the size of the three
properties, their resolution and the per-class defaults, less the thirteen per-control font fields
and properties they replace.

The demo's appearance page - `--page 33` - puts a panel that assigns a font, a foreground colour and
a background role on screen, with what its children make of them, one label that overrides both, and
a label that paints nothing in front of a panel that does. Its buttons change the panel's font and
the theme's face on a live tree; `--appearance-change` makes the same two changes before a
`--render` writes the frame, so the page can be screenshotted before and after it.
`Kolvir.Tests`' `InheritedAppearanceTests` runs the same resolution rules headlessly, one test per
rule, through the headless backend.

### Fonts

`Font` is a size plus a face, and the face can come from the machine or from font data:

```csharp
using var body = new Font("Segoe UI", 13f);           // a family the desktop has
using var pinned = Font.FromFile("Fonts/Inter-Regular.ttf");
using var embedded = Font.FromStream(resourceStream);  // or FromBytes(bytes)
using var title = pinned.WithSize(24f);                      // the same face, larger
```

A loaded font is the same physical face on every platform, so `MeasureWidth`, `LineHeight`,
`BaselineOffset`, wrapping and caret geometry measure the same on Windows, macOS and Linux - to a
thousandth of a pixel. A loaded face's advances and the font's own vertical metrics are taken
unhinted and linear (`LinearMetrics`, `Hinting = None`, `BaselineSnap = false`), which is what stops
FreeType on Linux rounding advances to whole pixels when CoreText and DirectWrite do not; what is
left is each backend's own fixed-point rounding, under 0.001 px on the demo face at 13 pt. Those
three settings apply only to a loaded face, so a font resolved by family name keeps exactly the
numbers it had. That is what a test pins a `.ttf` for, and what a trimmed single-file application
needs when it carries its brand in the executable. A family name gets none of that: the same name
resolves to whatever face each desktop calls by that name, so a layout measured on one platform does
not have to match another.

`FromFile` reads the whole file, `FromStream` reads the whole stream and `FromBytes` copies the span,
so nothing is tied to the source afterwards. A path that does not exist throws
`FileNotFoundException`; a stream or span that is not a font throws `ArgumentException`, and so does
empty data. Disposing a font twice does nothing, and a font obtained with `WithSize` keeps working
after the one it came from is disposed: each derived font holds its own reference to the face.

**A loaded font is one face.** `WithSize` keeps it, and a weight or italic that the data does not
contain is satisfied by the same face: nothing is synthesised, so `Weight` and `Italic` describe the
face actually used rather than what was asked for. Span formatting in `RichLabel` follows the
same rule - a span that changes only weight, slant or size is derived from the label's own font, so
a loaded face never falls back to a system family on a bold span. Load the bold file separately when
an emphasis needs a real bold face.

To make a face the application's font, say so once instead of per control:

```csharp
Theme.UseFont(Font.FromFile("Fonts/Inter-Regular.ttf"));   // a file
Theme.UseFont(Font.FromBytes(embeddedFontBytes));          // or an embedded resource
```

It can be called at any point in the application's life, not only before the controls exist: a
control resolves its font when it measures and paints (see
[Inherited appearance](#inherited-appearance)), so a live tree switches to the new face and lays
itself out again on the next frame. The theme's fonts are rebuilt from that face at their own sizes,
so `DefaultFont`, `CaptionFont`, `TitleFont`, `SectionFont` and `EmphasizedFont` all come from it;
because one file is one face, the large and emphasized ones are that face at their sizes rather than
a bold one. Assigning `Theme.FontFamily` goes back to a family name, and the existing
`ApplicationOptions.FontFamily` path is unchanged - and, like `UseFont`, it can be assigned
after the controls exist.

This lives on `Theme` rather than growing `ApplicationOptions` on purpose. An option is
read by `Application` at start-up - a type every application runs - so a font read there would
be reachable from every published application rather than only from the one that asked for it. The
loading path stays behind application code: `ApplicationOptions` gained no member.

The measured cost is one image-alignment step and nothing that runs. The `Kolvir.Minimal` fixture
publishes at 3,194,368 bytes on `win-x64` against 3,189,248 at `b83e6cd`, a difference of 5,120
bytes (0.16%) that is the size of one alignment step on that image - the picture is quantised, so a
change of this size cannot be resolved more finely. What is there on every app's behalf is the
theme's font source (one static reference and one branch when the theme fonts are rebuilt) and one
flag on `Font` saying that a face is a single face, which fits in padding the object already
had: no new per-instance field and no startup work. The loaders themselves are only reachable from
the application code that calls them, and `ApplicationOptions` keeps its old shape.

The demo's font page - `--page 32` - draws a label, a multi-line text box and a
rich label with IBM Plex Sans Regular carried as an embedded resource (`Kolvir.Demo.App/Fonts`,
OFL licensed), and prints the width and line height it measures against the system face's.
`Kolvir.Tests`' `FontLoadingTests` asserts the same measurements and failure paths headlessly, so
the identities that make a pinned face portable can be compared on every runner, and
`--embedded-font` applies the face to the whole sample UI, including a `--render`, which is the
application-wide path in one switch.

### Font dialog

`PickFont` is the platform's own font chooser, reached through the same dialog service as files and
message boxes:

```csharp
var choice = window.Application.Dialogs.PickFont(
    new FontDialogOptions
    {
        Title = "Choose a font",
        InitialFamilyName = preview.Font.FamilyName,
        InitialSize = preview.Font.Size,
        InitialWeight = preview.Font.Weight,
        InitialItalic = preview.Font.Italic,
        MinimumSize = 6f,
        MaximumSize = 48f,
    },
    window);

if (choice is null)
{
    return;                       // cancelled; nothing changed
}

var font = choice.CreateFont();   // the caller owns this one
preview.Font = font;
```

The result is an `FontSelection`, not an `Font`, and that is deliberate. `Font` is
`IDisposable` and owns a typeface, so a dialog returning one would leave the caller guessing whether
the dialog kept a reference, whether the control it assigns it to owns it, and who disposes the one
that was there before. A selection is an inert value with `FamilyName`, `Size`, `Weight` and
`Italic`; `CreateFont()` is the one-line way to turn it into a font the caller owns. `null` is the
only cancellation answer, so there is no "okay but empty" case to check for.

Sizes are **logical pixels**, the unit `Font.Size` uses. Windows and GTK show point sizes and
the backend converts them at 96 DPI; macOS points and the library's logical pixels are the same
unit. A size the user did not change is handed back exactly as it went in rather than round-tripped
through a unit that cannot represent every pixel size.

The implementation is the native chooser on every platform: `ChooseFontW` on Windows,
`NSFontPanel` on macOS and `Gtk.FontChooserDialog` on GTK, each under the nested-loop contract in
[the dialog seam](APPLICATION-MODEL.md#9-dialogs). Windows' `CHOOSEFONT` and GTK's dialog already
run a modal loop. macOS is the odd one: `NSFontPanel` is a floating panel that does not own a loop
or even have OK and Cancel. The backend adds an accessory view with **Select** and **Cancel**, hides
the panel's close and minimize buttons, and runs the panel in an application-modal session until one
of those is pressed. Return selects, Escape cancels, and a `finally` aborts the session if anything
else throws, so the synchronous contract is real rather than a panel left floating over a caller
that already returned. The backend's common-mode poll timer keeps `Invoke` work flowing through the
nested loop, exactly as it does for an `NSAlert`. Because `NSFontPanel` is shared and AppKit
remembers its frame in the application's defaults, every modal presentation gives it a frame of at
least 600 by 400 points, centred on the owner window (or on the visible screen when there is no
owner) and clamped inside the visible frame of the screen the owner is on, in points rather than
backing pixels.

Weight is the OpenType value `Font.Weight` reports - 400 regular, 700 bold - which `LOGFONT`
and Pango use directly. AppKit does not: `NSFontManager` reports a 0-15 index where 5 is regular and
9 is bold. `FontMapping` holds that translation in one place, and the nine common weights (100
through 900 in hundreds) round-trip exactly; anything between them snaps to the nearest face the
platform has, because that is what the font panel can select. The same class holds the point/pixel
conversion, so the arithmetic is unit-tested instead of being duplicated in three backends.

The dialog lists the families the operating system has installed. A face loaded with
`Font.FromFile`, `FromStream` or `FromBytes` is not a system font unless the application also
registered it with the OS, so it does not appear - and if a loaded face is the current font, the
dialog opens on the nearest installed family instead. That is documented rather than half-supported:
a backend cannot tell an arbitrary loaded face from an installed one, and offering to add it would
promise a system-wide result the platform does not give.

The demo's **Font dialog** page opens the chooser, then applies the result to a panel so the labels,
a nested panel and a button all inherit it. It is appended after the colour picker's page: the
colour picker is index 36 and the font dialog is index 37.
`Kolvir.Tests`' `FontDialogTests` covers the scripted headless answer, cancellation, the options
round-trip and every pure conversion; the native dialogs themselves are the part that only a real
window can prove.

This is **rung 1**: nothing runs and no dialog code is reachable unless the application calls
`PickFont`. The `Kolvir.Minimal` fixture publishes at 3,203,072 bytes on `win-x64` both before and
after the change, a delta of 0, and the published image contains neither `comdlg32` nor
`ChooseFontW`. The use check - the same fixture with one `PickFont` call - publishes at 3,207,680
bytes (+4,608, 0.14%), contains the `comdlg32` import and publishes with
`-p:TrimmerSingleWarn=false` reporting no IL2xxx or IL3xxx warning. Unlike a new always-registered
service, the method added here sits behind `Application.Dialogs`, which the fixture never
calls, so the whole dialog service is trimmed in an application that does not use it.

### Colour dialog

`ColorDialog` is the colour picker in its own modal window: the same `ColorPicker` an
application can put on a page, with OK and Cancel underneath it.

```csharp
var dialog = new ColorDialog(new ColorDialogOptions
{
    Title = "Choose a colour",
    InitialColor = preview.BackColor ?? SKColors.Black,
    ColorMode = ColorMode.Hsv,
});

var chosen = dialog.Show(window);   // blocks until the dialog closes
if (chosen is null)
{
    return;                          // Cancel, Escape or the window's close button
}

preview.BackColor = chosen.Value;
```

This one is **drawn by the library**, and the asymmetry with the native font dialog beside it is
deliberate. A font chooser has no portable substitute: the installed families, the face list and
the preview belong to the operating system, so the font dialog is three native implementations
behind `IDialogService`. A colour chooser does have one, because `ColorPicker` already
implements the spectrum, the hue strip, the component fields and the hex field, and the suite
already exercises it. A native colour chooser would replace that tested behaviour with three
answers that look and behave differently, and the seam would grow for no new capability. The
dialog is therefore an ordinary `Window` built from public controls, on every platform, with
no platform code and no change to `IDialogService`.

Modality is the application model's rather than a loop of this class's own: `Show` calls
`Window.ShowDialog(owner)`, which disables the owner and re-enters the platform's loop on all
three backends and returns `DialogResult`. The owner's content is also disabled for the
duration and restored afterwards. That is redundant where the window manager already blocks the
owner, but it means the guarantee holds on a backend with no window manager - the headless one -
which is what lets the suite assert it. Disabling the content would otherwise leave the owner
answering no keys at all, so the control that had the keyboard before the dialog opened is given
it back when the dialog closes. With `owner` null the dialog is not parented to anything and is
centred on the primary screen.

OK is the default button, so **Enter accepts** wherever the focus is, including the picker's
component and hex fields - picking a colour with the mouse does not mean going back to OK before
Enter works. A focused button presses itself instead, so Enter on Cancel cancels. **Escape
cancels** wherever the focus is. Closing the window with its own close button is a cancel, like
every other dialog, and the call returns null. The window is fixed at 300 by 372 logical
pixels and is not resizable: the picker's field layout is designed for that size, and a colour
dialog has one job. It centres on the owner's window - or on the primary screen when there is no
owner - and is clamped inside that screen's working area with a 24 pixel margin, shrinking if the
screen is smaller than the dialog, so it never lands partly off-screen.

`InitialColor` reaches the picker exactly and comes back exactly if it is accepted untouched,
including its alpha. **Alpha is not offered**: the picker edits RGB and has no alpha control, so
neither does the dialog and there is no option to request one. The alpha the colour arrives with is
preserved through every edit rather than being guessed at, so an opaque colour stays opaque.
`ColorMode` selects which component fields the picker shows - `Rgb`, `Hsl` or `Hsv` - and, as on
the control, changes only the representation, never the colour.

The demo's **Colour dialog** page - index 38, appended after the colour picker's page 36 and the
font dialog's page 37 - opens the dialog from a button, shows the chosen colour as a swatch and in
a hex/RGB readout, and reopens from the current value. The owner is resolved from `Root.Window`, so
the button works on whichever page is showing. `Kolvir.Tests`' `ColorDialogTests` drives the
dialog headlessly with the host's own input entry points: OK is clicked at the button's position,
Enter and Escape go through `ProcessKeyDown`, the returned colour is compared, the owner's blocked
state is asserted while the dialog is open, and the centring and clamping arithmetic is checked
against the synthetic 1920 by 1080 screen. The one thing a headless test cannot show is the nested
platform loop itself; that, and the real window positions, are verified on a real window on each
backend.

This is **rung 1** by construction: the dialog is a plain class built from public controls, it is
not registered anywhere, and nothing in the library reaches it. The `Kolvir.Minimal` fixture
publishes at 8,660,480 bytes on `win-x64` both before and after the change - a delta of 0 - because
an application that never names `ColorDialog` keeps none of it. The use check - the same
fixture with one `ColorDialog.Show` call - publishes at 8,988,672 bytes (+328,192, 3.79%),
which is the whole picker becoming reachable, and publishes with `-p:TrimmerSingleWarn=false`
reporting no IL2xxx or IL3xxx warning.

### Opacity and transparency

There are two ways to make a control see-through, and they are not the same thing.

`BackColor` can carry alpha. A background of `new SKColor(0, 0, 0, 128)` is half-transparent
black, and it blends with whatever the parent painted underneath it. That is the cheap option: it
fades the control's own background and nothing else, and its text, border and children stay fully
opaque.

`Opacity` fades the control and everything inside it as one group:

```csharp
// The whole strip - background, text and the buttons inside it - at 60%.
var strip = new Panel { Dock = XDock.Top, Height = 48f, Opacity = 0.6f };
strip.Controls.Add(new Label("Faded as one group"));
strip.Controls.Add(new Button("And so is this"));
```

Below 1 the control paints into a layer of its own, which is then composited once at that opacity.
That is what makes it a group: two children that overlap at half opacity do not darken where they
meet, the way they would if each were faded on its own. It also costs a surface the size of the
control's clip for the length of the paint, so leave `Opacity` at 1 unless the control really is
translucent - for something that only needs a see-through background, use the alpha in
`BackColor` instead. A control faded all the way to 0 paints nothing and its subtree is not walked.

Both of them make the control non-opaque, which is what the invalidation chain needs to know: a
control that shows what is behind it cannot be skipped when the thing behind it repaints. See
[Invalidation and partial painting](#invalidation-and-partial-painting) for the `Opaque` contract.

Transparency is a paint-time property. A translucent control blends with what its *parent* painted,
so a sibling that overlaps it does not show through it; hit testing still belongs to the topmost
control whose bounds contain the point, so a transparent panel over a button swallows clicks meant
for the button. Both are what WinForms does, and both are the price of not keeping a compositor in
the library.

### Window icons

`Icon` carries a bitmap **and** an optional icon-theme name, because the three platforms take
their icons in three unrelated ways:

```csharp
var options = new ApplicationOptions
{
    ApplicationName = "My App",
    Icon = Icon.FromFile("app.png"),   // Windows title bar/taskbar, macOS Dock
};

using var application = new Application(backend, options);
var window = new Window("My App", root);   // inherits the application icon
window.Icon = Icon.FromFile("other.png");  // ...or override it per window
```

| Platform | What happens | Which half of the icon is used |
| --- | --- | --- |
| Windows | `WM_SETICON` with `ICON_SMALL` (title bar) and `ICON_BIG` (Alt-Tab, taskbar), rebuilt at the window's DPI on `WM_DPICHANGED` | pixels |
| macOS | `NSApplication.ApplicationIconImage`, i.e. the Dock tile and app switcher for the whole process | pixels |
| Linux (GTK4) | `gtk_window_set_icon_name`, and the application-wide `gtk_window_set_default_icon_name` | `Name` |

So an icon with pixels only shows up on Windows and macOS, and `Icon.FromName("folder")` shows
up on Linux only. Set both halves on one icon to cover all three:

```csharp
var icon = Icon.FromFile("app.png");
icon.Name = "my-app";      // the installed icon-theme name, for GTK
```

On Linux the name has to exist in the desktop's icon theme, and on Wayland the window manager takes
the icon from the application's `.desktop` entry instead. A shipping macOS `.app` should also set
`CFBundleIconFile` in `Info.plist`. `Icon.GetBitmap(size)` scales once per requested size and
caches the result, and the bitmaps it returns belong to the icon: don't dispose them, and keep the
icon alive for as long as a window refers to it.

### Single instance and re-activation

Set `SingleInstance` and a second launch of the executable hands its command line to the running
process instead of starting a second copy. The second process exits from `Run` with the exit code it
was given, so a `Main` that simply returns `application.Run()` needs no extra handling:

```csharp
var options = new ApplicationOptions
{
    ApplicationName = "My App",
    SingleInstance = true,          // InstanceKey defaults to ApplicationName
};

using var application = new Application(backend, options);

if (application.IsPrimaryInstance)
{
    application.AddSecondInstanceHandler((_, e) => window.Activate());
}

application.Run();
```

`AddSecondInstanceHandler` handlers always run on the UI thread, with `e.Arguments` holding the
second launch's command line (empty when it had none). The default action is `Activate()`, which
brings the main window forward - and runs the `Activated` handlers, so one handler covers both a
re-launch and the user clicking the Dock icon or taskbar button:

```csharp
application.AddActivatedHandler((_, e) => { /* window came forward */ });
application.AddSecondInstanceHandler((_, e) =>
{
    OpenDocument(e.Arguments[0]);
    e.Handled = true;               // suppress the default Activate()
});
```

Setting `Handled` in either handler replaces that notification's default action; setting it in a
second-instance handler also skips the activation it would otherwise perform. `InstanceKey` scopes
the claim, so two different applications - or two profiles of one application - can run side by side.

The three platforms get there differently, and only the first two are complete:

| Platform | Mechanism | Re-launch handling |
| --- | --- | --- |
| Windows | Named mutex plus a named-pipe listener in the first process; the second connects, writes its command line and exits | `Activate()` restores a minimized window and calls `SetForegroundWindow`, falling back to a taskbar flash when Windows refuses the foreground |
| macOS | Same lock and pipe. Finder and `open -a` are already single-instance, so this covers a direct launch of the binary | `applicationShouldHandleReopen`; `Activate()` uses `activateIgnoringOtherApps` |
| Linux (GTK4) | `Gio.Application` uniqueness over D-Bus, with a reverse-DNS id derived from `InstanceKey` | A forwarded launch arrives as GTK's `command-line` signal and becomes a second-instance notification; `Activate()` calls `gtk_window_present` |

GTK is the reason the claim is a backend member rather than something the library does itself: on
Wayland only the toolkit's own D-Bus registration can raise another process's window, so the
backend has to own the mechanism. `TryClaimSingleInstance` runs after `Initialize` and returns false
when this process is the second one; the headless backend always returns true.

### Owned windows, topmost and activation

`Owner` makes one window belong to another, `TopMost` floats a window above everything, and
`AddActivatedHandler`/`AddDeactivatedHandler` report when a window gains or loses the foreground:

```csharp
var tool = new Window("Tool", panel) { Owner = window };   // or: tool.Show(window);
tool.TopMost = true;                                            // needs Owner == null

window.AddActivatedHandler(_ => status.Text = "Foreground");
window.AddDeactivatedHandler(_ => status.Text = "Background");
```

An owned window sits above its owner, minimizes with it and closes with it. The close is a cascade
the library performs itself: no platform does it for us, so closing an owner disposes the windows
whose `Owner` is that window, in every backend. `Owner` may be assigned before or after `Show()` -
assigning it on a visible window restacks the native window rather than throwing.

| Platform | `Owner` | `TopMost` |
| --- | --- | --- |
| Windows | The owner's `HWND` as the window's parent, applied with `GWLP_HWNDPARENT` plus a frame-changing `SetWindowPos` so the taskbar regroups | `HWND_TOPMOST` |
| macOS | `AddChildWindow:ordered:`, detached with `RemoveChildWindow:` | `NSWindowLevel.Floating` |
| Linux (GTK4) | `gtk_window_set_transient_for`, cleared when the owner is removed | **not implemented**: GTK4 removed `gtk_window_set_keep_above` and Wayland leaves stacking to the compositor, so the value is recorded and ignored |

Wayland is also where ownership is weakest: `SetTransientFor` is a hint, so an owned window may not
be stacked above its owner by the compositor. It still minimizes and closes with it.

A modal window hands activation back to its owner *before* it is destroyed, and on Windows that
ordering is load-bearing. Destroying the foreground window makes Windows activate the next
*eligible* one, and a modal dialog has disabled its owner, so the owner is not eligible: the
foreground lands on some other application's window, which is raised over the owner until the
dialog's own re-activation runs. That is the frame or two of flicker that follows a dialog's OK, and
the Win32 backend avoids it by re-enabling and re-activating the owner from `Dispose`, while the
dialog is still the foreground window. Measured on one close, the owner stayed inactive with a
foreign window on top of it for 9-30 ms. AppKit and GTK end their own modal sessions the same way,
so the ordering only has to be arranged here.

The demo exposes all three: the **Owned window** button in the header opens one from the UI on every
platform, **Window -> Topmost** and **Window -> Owned Window** add the menu items on macOS and GTK, and
the `--topmost` / `--owned` command-line flags work everywhere (Windows has no menu bar yet). The
status line reports activation changes.

### Chromeless windows

A borderless window hands its caption to the application: the platform keeps its shadow, its rounded
corners, its resize band and its window behaviours, and the application owns every pixel inside. Ask
first, because a backend that cannot do it throws rather than silently leaving the window decorated:

```csharp
var window = new Window("Chromeless", panel);

// Capabilities answer without throwing, so this is how an application finds out.
var caps = window.ChromeCapabilities;
if (caps.Borderless)
{
    window.Chrome = WindowChrome.Borderless;         // System is the default
}

if (caps.CaptionButtons)
{
    window.CaptionButtons = CaptionButtons.Platform; // or None and draw your own
}
else
{
    window.CaptionButtons = CaptionButtons.None;
}
```

Setting a member the backend does not implement throws `NotSupportedException` at the call site, and
leaves the window as it was. "Borderless" is reported per backend, not per window, and does not change
over the life of one window.

#### What the platform still owns

`ChromeMetrics` is the other half of the contract: read it rather than assuming what was delivered.
It is a snapshot, so read it again after a change; `AddChromeMetricsChangedHandler` fires when it
moves.

```csharp
var metrics = window.ChromeMetrics;

float top = metrics.SurfaceInset.Top;       // do not paint here: the platform paints it
float left = metrics.ResizeInset.Left;      // do not put controls here: the platform claims the press
foreach (var button in metrics.CaptionButtonBounds)
{
    // button.Kind is Close, Minimize, Maximize or Restore; button.Bounds is in client pixels
}
```

| Platform | `SurfaceInset` | `ResizeInset` | `CornerResizeExtent` | `CornerRadius` | Caption buttons |
| --- | --- | --- | --- | --- | --- |
| Windows | 6.5 left, right and bottom at 200% DPI, 0 top | 6.5 along the top edge only | 6.5 | 0 - the platform exposes no radius, so nothing may clip the content | Emulated by the backend, 46 x 22.5 logical each at the right |
| macOS | none - the content covers the window | 3 pt on every edge | 13 pt | 11 pt | The platform's traffic lights, 14 x 14 at (9,9), (32,9) and (55,9) on the left |
| Linux (GTK4) | none, or 1 px per side when `BorderColor` is set | none - the band sits outside the content | 0 | 12, set explicitly so both GTK versions agree | `Gtk.WindowControls`, on the side the desktop's decoration layout names |

The two insets mean different things and are not interchangeable. `SurfaceInset` is pixel space the
application never sees; `ResizeInset` is pixel space it *does* see and must keep clear of interactive
controls, because the platform claims presses there first. `CornerRadius` is what to clip your own
chrome to, since the platform clips what is underneath it.

#### Dragging and resizing from the application's own title bar

A borderless window has no title bar for the platform to drag by, so the application asks for the
drag from inside its own pointer-down:

```csharp
titleBar.AddMouseDownHandler((_, _) =>
{
    if (window.TryBeginDrag())
    {
        // the platform took the press; do not also deliver the click
    }
});

// an application-drawn grip can start a resize the same way
bool resizing = window.TryBeginResize(ResizeEdge.Bottom | ResizeEdge.Right);
```

Both return whether anything started, and both only work from inside a press that is still in flight,
which is why the backend caches what the platform needs. `TryBeginDrag` moves the window with the
native behaviour (Windows snap and tiling, macOS `performWindowDragWithEvent:`); `TryBeginResize` is
honoured on Windows and GTK4 and reports `false` on macOS, which has no public way to begin an edge
drag of its own.

#### Window colours

`CaptionColor` and `BorderColor` recolour the two surfaces a borderless window can still show under
it: Windows uses them for DWM's caption strip and its own 1 px border, GTK4 for the window's CSS
border. A null colour means the platform's own, not "no colour" - give both a colour if you want no
visible platform chrome at all. `ChromeCapabilities.WindowColors` says whether the backend honours
them; Windows only can from build 22000.

`Chrome` may be changed while the window is open, and the metrics follow it both ways; setting it
before `Show()` is the cheap path because the style is in place before the first frame.

#### Windows: the accessibility gap

On macOS and GTK the caption buttons are the platform's own controls: they take their own clicks and
are visible to assistive technology. On Windows there is no caption in a borderless window, so the
backend draws and hit-tests the buttons itself - deliberately indistinguishable from the API's point
of view, but **not real controls**, so a screen reader cannot reach them and a keyboard cannot tab to
them.

The window itself stays reachable, because borderless is still a window: `Alt+F4` closes it,
`Alt+Space` opens the system menu, `Alt+Tab`, the taskbar, `Win+arrow` snapping and the resize border
all keep working. An application that needs its buttons reachable by assistive technology should ask
for `CaptionButtons.None`, draw its own focusable buttons, and keep those keyboard equivalents
working. A UI Automation provider for the emulated strip is deferred rather than forgotten - see
[CHROMELESS-WINDOWS.md](CHROMELESS-WINDOWS.md).

The demo has a **Chromeless window** page that switches its own window between the two in the UI,
requests the platform buttons both ways, starts a drag from a strip it draws itself, and shows the
capability report and metrics snapshot it read back. The sample's own header strip - the title, the
subtitle and the space around them - is a drag handle too, so the window can be moved by its title
area in either chrome; the two buttons in that header keep their own clicks.

### App badging

An unread count or a dot on the application's own icon - the taskbar button on Windows, the Dock tile
on macOS, the dock icon on Linux - is a package of its own, `Kolvir.Feature.Badges`, because an
application that never shows one should not carry three platform implementations of it. Reference the
package and opt in with one line:

```csharp
var options = new ApplicationOptions
{
    ConfigureServices = services => services.AddAppBadges(),
};

using var application = new Application(KolvirBackend.CreateDefault(), options);
```

`AddAppBadges()` is the whole opt-in and has the same name on every platform, so nothing in the
application names a backend. The badge itself is a member of the application rather than of a window,
because two of the three platforms only have an application icon and Windows groups a process's
windows under one taskbar button:

```csharp
application.Badge.Count = 3;      // unread items; null, zero or less hides the badge
application.Badge.Text = "99+";   // any short label, where the platform takes one
application.Badge.IsDot = true;   // a dot, with no number
application.Badge.Urgent = true;  // a flash, a bounce or a highlight, as the platform does it
application.Badge.Clear();        // all four in one change
```

The presentations overlap on purpose, so setting them from different parts of an application does not
need coordination: **`IsDot` beats `Text`, which beats `Count`**. A dot is "something happened and I
have no number for it", which is the more urgent thing to say.

#### What each platform shows

| | Windows | macOS | GTK4 (X11 and Wayland) |
|---|---|---|---|
| `Count` | drawn into the overlay icon, capped at `99+` | the Dock's own red bubble | Unity LauncherEntry `count` |
| `Text` | drawn | passed through; long labels are clipped | - |
| `IsDot` | drawn | a bullet inside the bubble | - |
| `Urgent` | the taskbar button flashes | the Dock icon bounces | the dock holds a highlight |
| `Capabilities` | all four | all four | `Count` and `Urgent` |

`Badge.Capabilities` reports that table, and an application should read it rather than assume: asking
for a presentation the platform cannot draw is **not** silently dropped - it becomes `Urgent`, so the
user still learns that something happened. `Capabilities` is empty (all false) when the application
never called `AddAppBadges()`, which is how an application can tell it forgot.

#### Urgent is a request, not a state

A flash, a bounce and a dock highlight all stop when the user comes back to the application, so
`Urgent` asks for something rather than describing it: the badge sets the flag back to `false` once a
window of the application has been activated, which is the same event on all three platforms. That
makes it read truthfully, and it is also what lets an application ask again - a flag that stayed
`true` would not be a second request on Windows or macOS.

Because the badge can therefore change without the application, it reports every change:

```csharp
application.Badge.AddChangedHandler(badge => UpdateTrayIcon(badge.Count, badge.Urgent));
```

The handler runs after the platform half has been told, so a readout built from it agrees with what
the platform is showing. The `Count`, `Text` and `IsDot` members are still kept until changed: only
the request is a moment.

#### Linux needs a desktop entry

Linux has no badge API at all: GTK 4 has neither a badge nor an urgency call, and the only urgency
symbol in the library is GDK's X11-only escape hatch, which does nothing on Wayland. What exists is a
convention - the application broadcasts `com.canonical.Unity.LauncherEntry.Update` on the session bus
and a dock that implements the protocol (Ubuntu's `ubuntu-dock`, dash-to-dock, Plank) draws the badge
on the icon whose **desktop entry** matches the id in the signal. Two consequences:

- The application must have an installed `.desktop` file whose name is its application id, which on
  GTK 4 is derived from `InstanceKey`. Run out of a build directory with no entry installed and
  nothing appears - silently, which is the one failure worth documenting.
- Stock GNOME Shell implements none of it, so a bare GNOME desktop shows nothing however the
  capabilities read.

The demo ships `Kolvir.Demo.App/com.kolvir.ikolvir.desktop` and
`Kolvir.Demo.App/install-badge-desktop-entry.sh`, which writes it to
`~/.local/share/applications` with the build's absolute path. Run the script once and the **App
badge** page's count appears on the dock.

#### What the platforms cannot do

- **Windows draws the badge itself**, so its look is this library's: a red disc with a white ring and
  a white numeral, at the small-icon size for the window's DPI, redrawn when the window moves to a
  display with a different scale. The shell throws an overlay away when it re-creates a taskbar
  button - an Explorer restart - and the feature listens for that and re-applies.
- **macOS chooses the colour.** The bubble is always the system's red, and the dot is a bullet inside
  a bubble that is the size of a number badge rather than a small dot. A Dock bounce only happens
  while the application is *not* the active one, because macOS skips it for the frontmost app, which
  is also the right answer for "the user is already looking at us".
- **Urgent is a nudge, not a state, on Windows and macOS**: it fires on the transition, and clearing it
  by hand does not take a flash or a bounce back. What happens instead is that the platform ends it:
  the flag reads `false` again once the user has come back, which on Linux is also what releases the
  dock's highlight.

The demo has an **App badge** page that sets a count, a dot, a label and an urgent nudge, clears them
again, and reports what the backend says it can draw. Its **Urgent in 3 s** button waits three seconds
before nudging, because Windows and macOS skip a flash or a bounce while the application is active -
three seconds is enough time to switch to another window and watch the taskbar or the Dock. It clears
the flag first, since the nudge is the transition rather than the state, which is what makes the
button usable more than once.

### Drag and drop

Drag and drop is a package of its own, `Kolvir.Feature.DragDrop`, because most applications never
accept a drop and the ones that do not should not carry the code that makes one possible. Reference
the package and then opt in: until the application asks for it, a window registers no drop target,
which is what keeps OLE, the COM target, the pasteboard types and the GTK controllers out of the
build. Asking for it is one line:

```csharp
using Kolvir.DragDrop;

var options = new ApplicationOptions
{
    ConfigureServices = services => services.AddDragDrop(),
};
```

With that in place, setting `AllowDrop` on a control makes it, and everything inside it, a drop
target. Both `AllowDrop` and the four handlers come from `Kolvir.DragDrop`. The four drag handlers
receive an `DragEventArgs`; the drop handler sets `Effect` to say what it did:

```csharp
root.AllowDrop = true;

root.AddDragEnterHandler((_, e) => e.Effect = e.Data.Contains(DataFormats.FileDrop)
    ? DragDropEffects.Copy
    : DragDropEffects.None);

root.AddDragDropHandler((_, e) =>
{
    foreach (var path in e.Data.GetFiles())
    {
        status.Text = $"Dropped {Path.GetFileName(path)}";
    }

    e.Effect = DragDropEffects.Copy;
});
```

The opt-in is per application, and it is coarse on purpose: once it is called, every window gets a
drop target, whether or not any control ever sets `AllowDrop`. `AllowDrop` can be turned on and off
on a control that is already inside a shown window, so there is no correct moment at which the
native registration could depend on it. Leaving the call out is not an error: the code above still
compiles and runs, nothing simply ever arrives. What the feature costs an application that never
references the package is measured in [FEATURE-COST-POLICY.md](FEATURE-COST-POLICY.md) section 8.

`AddDragEnterHandler`, `AddDragOverHandler` and `AddDragDropHandler` share one argument type, so the
same handler can serve all three. `AddDragLeaveHandler` takes a handler with no arguments: by the
time a drag has left, there is nothing left to say.
`Location` is in the control's own logical pixels, the same units `Paint` uses, and `Modifiers`
reports the shift, control and alt keys held at that moment.

The data on offer is an `DataObject`: a bag of values keyed by `DataFormat`. `Text`,
`Html` and `FileDrop` are the formats the backends fill in themselves; `DataFormats.Custom` is
there for a drag this library is not the only participant in. `GetFiles` returns absolute paths.

| Platform | Mechanism | What a drag carries |
| --- | --- | --- |
| Windows | A hand-built `IDropTarget` COM object registered with `RegisterDragDrop`, since a source-generated `ComWrappers` one is not a valid call target under the loader lock OLE calls it from. It lives in the feature package, which duplicates the few OLE imports it needs | `CF_HDROP`, `CF_UNICODETEXT`, `HTML Format` |
| macOS | The view registers `public.file-url` and `public.utf8-plain-text` and implements `NSDraggingDestination` | `NSPasteboard` file URLs (a web URL is not a file, so an image dragged out of a browser is refused), plain text |
| Linux (GTK4) | One `GtkDropTarget` per kind of drag, both typed as text, with preload so the payload is in hand before the drop; the feature package adds them to the view's drawing area | File paths, read from the text form of the drag since a `GdkFileList` cannot be read without a portal round trip; strings |

The mechanisms themselves live in the package wherever the platform allows it, so a backend assembly
that no application opted into carries no drag and drop code at all. Windows and Linux are in that
category: the COM target is an object registered against a window handle, and a `GtkDropTarget` is a
controller added to a widget, so both moved out of `Kolvir.Backend.Win32` and
`Kolvir.Backend.Gtk4` and only a generic slot for releasing them when the window goes remains.
macOS is the exception, because AppKit delivers a drag to the `NSView` that registered for the
dragged types, through `NSDraggingDestination` methods on that view's own class; there is no separate
destination object to hand the window, so the five overrides cannot leave `AppKitSkiaView`. What they
mean still does: the view names nothing from the package, it hands over an `AppKitDragPayload` and
asks an `IAppKitDragSink` for an `AppKitDragEffect`, and the feature's hook implements that sink and
translates. A view that is never given a sink never registers for a dragged type, so an AppKit
application that did not opt in still carries nothing of the feature.

A drop is routed by hit testing, then by walking up the parents, so an `AllowDrop` panel accepts a
drop anywhere inside it and a child that does not want the drop passes it to its parent. A control
counts as wanting the drop when its `DragEnter` or `DragOver` answers with something other than
`None`, so a text box that takes text but not files lets a file drop land on the panel behind it.
The feature clamps whatever `Effect` a handler sets to the effects the drag source allowed, so a
control cannot claim a move the source never offered. All of that routing lives in the package's own
`DragDropRouter`, one per window: the library names none of it and keeps no state for it.

The demo turns the whole window into a drop target: dropping files prints their names on the status
line, and dropping text prints the first sixty characters.

The `IDataObject*` a Win32 drop target is handed is borrowed: the shell owns that reference and keeps
using the object for the rest of the drag, so the feature takes one of its own before wrapping it and
releases exactly that before returning. Page 56, **Drag/drop lifetime**, measures that contract: it
samples the object's live reference count at `DragEnter`, every `DragOver` and `Drop` with a net-zero
`AddRef`/`Release` pair, shows the sequence and the verdict - the count must never fall during a drag -
and says FAIL with the numbers when it does. `KOLVIR_DRAG_TRACE=1` writes the same samples to
standard error. Its "Collect during drag" toggle forces a collection on every drag-over, which is what
pulls a wrapper's finalizer into the middle of a drag instead of leaving the damage to chance.

#### Migrating from the old surface

Setting `AllowDrop` and subscribing to the four drag handlers used to be members of `Control`.
They are extension members in `Kolvir.DragDrop` now, and the events are `Add`/`Remove` handler
pairs like every other notification, so the two things an application does with the feature look
like this:

```csharp
// before: members of the control, registered unconditionally at window creation
root.AllowDrop = true;

root.DragEnter += (_, e) => e.Effect = e.Data.Contains(DataFormats.FileDrop)
    ? DragDropEffects.Copy
    : DragDropEffects.None;

root.DragDrop += (_, e) =>
{
    foreach (var path in e.Data.GetFiles())
    {
        status.Text = $"Dropped {Path.GetFileName(path)}";
    }

    e.Effect = DragDropEffects.Copy;
};
```

```csharp
// after: the same handlers, from the feature's namespace, plus one line at startup
using Kolvir.DragDrop;

var options = new ApplicationOptions
{
    ConfigureServices = services => services.AddDragDrop(),
};

// unchanged: a control still accepts a drop by saying so
root.AllowDrop = true;

root.AddDragEnterHandler((_, e) => e.Effect = e.Data.Contains(DataFormats.FileDrop)
    ? DragDropEffects.Copy
    : DragDropEffects.None);

root.AddDragDropHandler((_, e) =>
{
    foreach (var path in e.Data.GetFiles())
    {
        status.Text = $"Dropped {Path.GetFileName(path)}";
    }

    e.Effect = DragDropEffects.Copy;
});
```

`e` is the same `DragEventArgs` in both, and `Effect`, `Data`, `GetFiles()` and `Modifiers` are
unchanged, so a handler body moves across untouched: `+=` becomes `Add<Name>Handler` and `-=`
becomes `Remove<Name>Handler`, as it did for every other notification. `DragLeave` is the one that
lost its payload, so `root.DragLeave += (_, _) => Reset();` becomes
`root.AddDragLeaveHandler(_ => Reset());`. The package reference and the `AddDragDrop()` call are
the only two lines with no counterpart. Leaving out the call still compiles and runs - the window
simply never becomes a drop target - while leaving out the package removes the members the rest of
this code is written against, which is the point of it being a package.

### Animations

Animations are a package of their own, `Kolvir.Feature.Animations`, because an application that
animates nothing should not carry a timeline. Nothing in it is native - an animation is arithmetic,
a clock and an invalidation - so the whole feature is portable code and the package has one
`common/` folder and three empty platform folders. Reference it and opt in:

```csharp
using Kolvir.Animations;

var options = new ApplicationOptions
{
    ConfigureServices = services => services.AddAnimations(),
};
```

The opt-in registers one **frame clock for the application**, not one per control. Every
`AnimationSet` attaches to it, and it starts when the first set starts and stops when the last
one finishes, pauses or is disposed - so an application sitting still runs no timer at all, which is
the rule `CONTROLS-TODO.md` section 2 sets for everything time-based in the library.

An **`AnimationSet`** is one or more value animations on a shared timeline:

```csharp
var set = new AnimationSet();

set.Add(ValueAnimation.Create(
    0f, 1f, TimeSpan.FromMilliseconds(240), value => card.Opacity = value,
    easing: Easing.EaseOutCubic));

// a stagger: same animation, three start times, measured from the same zero
for (int i = 0; i < 3; i++)
{
    var tile = tiles[i];
    set.Add(ValueAnimation.Create(
        0f, 1f, TimeSpan.FromMilliseconds(300), value => tile.Opacity = value,
        startTime: TimeSpan.FromMilliseconds(80 * i)));
}

using var subscription = set.AddFinishedHandler(finished => Console.WriteLine($"{finished} is over"));

set.Start();
```

An **`ValueAnimation`** carries a start value, an end value, a duration, a start time relative
to the set, an easing curve and the callback that assigns the current value. `ValueAnimation.Create`
has overloads for `float`, `double`, `SKColor`, `SKPoint`, `SKSize` and `SKRect`; for anything else,
construct `ValueAnimation<T>` with your own blend, which is the trim-safe way to extend it -
there is no registry, no reflection and no static state:

```csharp
var lift = new ValueAnimation<Level>(
    Level.Quiet, Level.Loud, TimeSpan.FromMilliseconds(500),
    value => meter.SetLevel(value),
    (from, to, t) => Level.Blend(from, to, t));
```

**Easing** functions are static methods on `Easing`, pure and allocation-free: `Linear`,
`EaseInQuad`, `EaseOutQuad`, `EaseInOutQuad`, and the same three for `Cubic` and `Sine`. Each maps
`0` to `0` and `1` to `1`, which is what makes an animation settle exactly on its end value; a curve
you write has to do the same. `Interpolation` has the blends, as overloads of `Lerp` so a
method group picks the right one.

The five methods and what each one means:

| Call | What it does |
| --- | --- |
| `Start()` | Begins the timeline. On a set that is already running it does nothing, so calling it twice is harmless; on a paused set it resumes. On a finished set it does nothing - use `Restart()`. |
| `Pause()` | Freezes the timeline where it is and lets go of the frame clock. |
| `Resume()` | Continues a paused timeline. The request listed only four methods; a pause with no resume is a trap, so this is the fifth. |
| `Restart()` | Back to zero and playing again, from any state including finished. It is a new run, so the finished handler runs again. |
| `End()` | Finishes now: every animation is assigned its end value and the finished handler runs once. Ending one that has already finished does nothing. |

A few consequences worth knowing before you rely on them:

- **The finished handler is an `Add`/`Remove` pair**, not a C# `event`, like every other
  notification in the library: `AddFinishedHandler(Action<object>)` returns a token that unsubscribes
  on dispose, and the argument is the set. It runs once per run, on the UI thread, after the values
  are already at their end. A handler that throws is reported through
  `Application.AddUnhandledExceptionHandler` and stops the handlers after it, exactly as a C#
  event would.
- **`End()` mid-flight runs the handler.** The timeline is over however it got there.
- **A zero-length set costs nothing.** A set with no animations, or only zero-duration ones at time
  zero, finishes inside `Start()` without ever asking for a frame.
- **A zero-duration animation applies its end value** as soon as its start time arrives.
- **A set is as long as its last animation**, so `startTime` lengthens the set rather than pushing
  the timeline past its own end. The animation list is fixed by the first `Start()`: adding one after
  that throws, because a length that changes underneath a running frame cannot be measured against.
- **Values are assigned only when they change**, and a restarted set assigns its start values again
  even when they match what it finished on. A frame that changes nothing calls no callback.
- **The clock stops at the end of the frame**, not in the middle of it. A finished handler that runs
  from a frame therefore still sees `scheduler.IsRunning == true`, and a handler that starts or
  restarts a set keeps the clock running rather than stopping and restarting it for one frame's work.
  A set ended from application code - by `End()`, `Pause()` or `Dispose()` - stops the clock before
  the call returns.
- **A callback that throws is reported, not retried.** It goes to
  `Application.AddUnhandledExceptionHandler` (the same route drag and drop uses) and the rest of
  the frame still runs, so one bad animation cannot freeze the others. With no application in the
  process there is nowhere to report it and the exception surfaces at the caller.

A **control gains its animation as an extension member**, not as a property the library carries:
`Expander` has `ExpandDuration`, contributed by this package and stored in a weak table keyed
by the control (FEATURE-COST-POLICY.md section 5). A section given one animates its `ExpandProgress` on the
application's clock, which moves the content height and the arrow together; a section given none
does exactly what the control does on its own. Because the duration is not on the control, an
application that never references the package pays nothing for it, and because the control asks the
application for the scheduler, an application that set a duration without calling `AddAnimations()`
gets a snap rather than a section stuck half open:

```csharp
appearance.ExpandDuration = TimeSpan.FromMilliseconds(180);   // null, the default, jumps
```

The same shape covers the two controls that had no animation of their own:

| Control | Extension member | What it drives |
| --- | --- | --- |
| `Expander` | `ExpandDuration` | `ExpandProgress`, so the height and the chevron fold together |
| `Toast` | `ShowDuration`, `DismissDuration` | `ShowProgress`, so the message fades and slides in from its docked edge and reverses on the way out |
| `Overlay` | `FadeDuration` | `Opacity`, so the layer - and the card inside it - dims in as one picture |

Each of the three progress values follows its control on its own, so none of them is state that only
an animation can reach: `ExpandProgress` follows `IsExpanded`, `ShowProgress` follows `IsOpen`, and
`Opacity` defaults to fully opaque and is clamped. A duration removed mid-flight settles the control
on its target in every case, and no scheduler means a snap.

The demo's **Animations** page runs nine animations on one timeline - a block that eases across its
track, a bar that blends two colours on a different curve, three tiles that each arrive in a stagger
of position and opacity, and a readout that is itself an animated value - with Start, Pause, Resume,
Restart and End wired to the one set. The demo's **Expander** page puts an animated section beside an
instant one, which is the shortest way to see what the duration buys; **Toast animation** puts a
switch on the same page that takes both durations away, and **Overlay fade** shows a layer dimming in
over the panel it holds still.

### Images and animations

A picture is an animation whose duration is zero, so there is one type, one timeline and one code
path - not a still path and an animation path that have to be kept in step. `Image` draws an
`ImageData` and does nothing else with it:

```csharp
var image = new Image { SizeMode = SizeMode.Zoom };
image.Source = factory.LoadFromStream(stream);    // a PNG is a zero-duration animation
```

The control's image surface is `Source`, `SizeMode`, `ImageAlign` and `PlaceholderColor`. It has no
loader - `IImageDataFactory` is where bytes become an image - and it has no ownership of what it
shows. That is `PictureBox`'s convention and the one to arrive with: you create the image, you assign
it, you dispose it. Disposing the control does not dispose the image, and assigning a second image
does not dispose the first, so a long-running page that replaces its image has to dispose the old one
itself. A disposed image draws nothing rather than throwing, because a paint runs on the paint thread
where there is nobody to catch anything.

#### The data: one timeline

```csharp
public interface IImageDataSource : IDisposable
{
    TimeSpan Duration { get; }                        // Zero for a still picture
    SKSize NaturalSize { get; }                       // fixed for the source, never per-frame
    bool Loops { get; }                               // what the file declares
    SKPicture FrameAt(TimeSpan position);             // position already wrapped and clamped
    TimeSpan? TimeUntilNextFrame(TimeSpan position);  // null when there is nothing to wait for
}

public sealed class ImageData : IDisposable
{
    public ImageData(IImageDataSource source);
    public static ImageData FromPicture(SKPicture picture, bool disposePictureWithData = false);

    public TimeSpan Duration { get; }
    public SKSize NaturalSize { get; }
    public bool Loop { get; set; }                    // starts from the source, then it is yours
    public bool IsPlaying { get; }
    public TimeSpan Position { get; }
    public SKPicture? CurrentFrame { get; }

    public void Play();
    public void Pause();
    public void Seek(TimeSpan position);
    public TimeSpan? TimeUntilNextFrame(TimeSpan position);
}
```

`FromPicture` is the low-level way in, and it keeps "show this picture" a one-liner; the format
provider seam hands its sources through it. `disposePictureWithData` is the ownership switch that
matters: true gives the data the picture, and the source that produces it has to outlive every
picture it hands out.

`NaturalSize` is fixed for the source rather than measured from the current frame, and it is what
`AutoSize` measures and every `SizeMode` scales from. An animation whose frames differ by a pixel
therefore cannot make the layout twitch once a second.

**`Position` is computed, never accumulated.** `Play` records an anchor - a position and a
`TimeProvider` timestamp - and every read is `anchor.Position + (now - anchor.Timestamp)`, wrapped
when the animation loops and clamped otherwise. There is no timer in that arithmetic, which is why an
animation that spent three seconds behind another page is three seconds further on when it comes
back: time spent hidden counts, and the frame it shows is the one the clock says rather than the one
it left on. `TimeProvider` is the same seam `UndoClock` uses, and it is `TimeProvider.System`
unless an application supplies another, so a test drives the whole timeline with the `TestClock` that
already exists.

The rest of the timeline follows from that one rule:

| Situation | What happens |
| --- | --- |
| Ticks | They repaint, and only repaint. A tick never advances anything, so a dropped or late tick costs a repaint rather than a frame of timeline. |
| `Seek(position)` | Re-anchors and clamps to `[0, Duration]`, so seeking during playback does not jump when the clock catches up. |
| Paused | Frozen: `Position` moves only while `IsPlaying`. |
| Playing to the end, `Loop` true | Wraps and keeps playing. `Position` is always inside `[0, Duration)`; it is not a total elapsed time, and there is no second value for one. |
| Playing to the end, `Loop` false | Stops on the last frame. |
| `Play` at the end of a one-shot | Restarts from the beginning. |
| `Duration == Zero` | Never plays and never ticks. `Play`, `Pause` and `Seek` are no-ops rather than exceptions: a still picture is the degenerate case, not an error. |

`Loop` starts as whatever the file declared and is settable afterwards; once assigned, the
assignment wins.

Playback rate is fixed at 1.0x in this cut. The arithmetic lives in one method, so a rate multiplies
`(now - anchor.Timestamp)` in one place rather than being threaded through the frame scheduling.

#### What wakes the control up

The repaint timer is gated the way the marquee is (PR #39): it exists only while the animation is
playing, has a non-zero duration, and `IsDisplayed` is true. Paused, hidden or still, there is no
timer at all.

It is also scheduled to the next frame boundary rather than to a fixed display rate: asked
`TimeUntilNextFrame`, the control sets the timer for that long, so a four-frames-a-second GIF wakes
four times a second rather than sixty. A boundary nearer than a millisecond is treated as a
millisecond, so a file full of zero-length frames cannot spin the clock. Becoming displayed starts
the timer again and repaints immediately from the current position, which is the frame the clock says.

The **Animated GIF**, **GIF frames** and **GIF out of view** demo pages show all three: the first
with play, pause, a seek bar and a position readout, the second stepping frames on a paused
animation, and the third scrolling the picture out of a scroll panel and back - the readout keeps
counting while it is gone, and the frame it comes back on is the one the readout describes.

#### Loading: the factory

```csharp
public interface IImageDataFactory
{
    ImageData LoadFromPath(string path, IImageFormatOptions? options = null);
    ImageData LoadFromStream(Stream stream, IImageFormatOptions? options = null);
    ImageData LoadFromBytes(ReadOnlySpan<byte> bytes, IImageFormatOptions? options = null);
}
```

One call loads anything the application can read, and the answer to "how do I load this image" does
not depend on which formats happen to be enabled. An application resolves the factory from its
services - `Application` registers one, composed from every provider the features declared, and
`Application.Services` is the provider it lives in - and assigns what comes back:

```csharp
var factory = services.GetRequiredService<IImageDataFactory>();

image.Source = factory.LoadFromPath("art/logo.png");
```

A factory with nothing registered reads every raster format Skia does - PNG, JPEG, WebP, GIF (its
first frame), BMP and ICO - because the built-in decoder is part of the factory rather than of a
package. Without a container, compose by hand:

```csharp
var factory = new ImageDataFactory();                              // raster formats only
var withSvg = new ImageDataFactory([SvgImageFormatProvider.Shared]);
```

The list is copied and sorted once, and it is the factory's for its lifetime: registration is a
startup concern, so there is no add, no remove, no handle and no lock, and two factories in one
process - or two tests in one process - cannot see each other's formats.

| Situation | What happens |
| --- | --- |
| A load fails to recognise the bytes, or the decoder fails | It throws `ImageFormatException`. There is no bool-and-handler convention left: the caller is the one who asked for the image, so a failure is an ordinary exception where it can be handled. |
| Two providers claim the same bytes | The higher `Priority` wins; at equal priority the later registration wins. A load never tries a second provider for the same bytes. |
| A provider throws while sniffing | It has not claimed the bytes - the exception is swallowed and the next provider is asked - because identifying a stream must not be able to fail a load. |
| The claiming provider throws | The load throws an `ImageFormatException` naming the provider, with what it threw inside it, and does **not** fall through to the next claim. |
| No provider claims the bytes | The built-in decoder gets them. It is fallback behaviour, not a provider with a priority, which is exactly why registering a GIF or WebP provider takes that format away from it. |
| The stream cannot seek | It is buffered, bounded by `BufferLimit` (64 MiB by default), rather than refused - a network stream or a pipe is the case a real application hits. A larger one can be copied to a file first. |
| The data outlives the factory | Fine, and expected: the data owns fully decoded pixels, not the stream or the factory. |
| Options for a format nobody registered | The load throws, rather than silently ignoring what the caller configured. |

`LoadFromPath` passes the file extension to the provider as a hint; the other two have only bytes, so
a provider identifies itself from the content.

#### The provider seam

A format arrives as an `IImageFormatProvider`:

```csharp
public interface IImageFormatProvider
{
    string Name { get; }
    int Priority { get; }
    bool CanDecode(ReadOnlySpan<byte> header);
    ImageData Create(Stream stream, string? extensionHint, IImageFormatOptions? options);
}
```

A provider is asked about *the first 512 bytes* and, if it claims them, is handed the whole seekable
stream and returns the `ImageData` the control will play. There is no reflection, no assembly
scanning and no registration by attribute: a provider is added to the service container, and the
`Add...()` call a feature package ships is that call with the package's provider in it.

Options travel through the same seam. `IImageFormatOptions` is an empty marker, so core can
carry a format's settings without knowing what any of them mean; a package declares the concrete
type and overloads the factory methods with it, and the provider that claims the bytes is the one
that receives them:

```csharp
using Kolvir.Svg;

image.Source = factory.LoadFromPath("icons/badge.svg", new SvgOptions
{
    CurrentColor = Theme.Foreground,
});
```

Only the claiming provider sees the options, so handing GIF options to a load of a PNG is not an
error - nothing reads them. A provider that *does* claim bytes and is then handed options of another
format fails the load instead of ignoring them, because a silently ignored setting is
indistinguishable from a broken one.

#### What the demo shows

The **Animated GIF** page plays the 48x48 sample with play, pause, a seek bar and a position readout;
**GIF frames** steps it a frame at a time and prints where each frame starts on the timeline; **GIF
out of view** is the hidden-time case made visible. **Animated WebP** and **WebP frames** are the
same two pages for a WebP container, whose frames are sub-rectangles rather than full canvases. The
SVG pages below are the same control and the same factory call with a different provider registered.

### SVG images

SVG is a package of its own, `Kolvir.Feature.Svg`, built on [Svg.Skia]. A vector renderer is not
something a toolkit can trim down to nothing, so it does not live in `Kolvir`: an application that
shows no SVG does not reference the package and therefore does not contain it
(FEATURE-COST-POLICY.md section 4, rung 3). Reference it and opt in:

```csharp
using Kolvir.Svg;

var options = new ApplicationOptions
{
    ConfigureServices = services => services.AddSvgImages(),
};
```

or, without a service container, `new ImageDataFactory([SvgImageFormatProvider.Shared])`.
Either way the package's contribution is one registered **format provider** - the seam the images
section above describes - and after that nothing about the call site changes:

```csharp
var icon = new Image { SizeMode = SizeMode.Zoom };
icon.Image = factory.LoadFromPath("icons/star.svg");   // the same call that reads a PNG
```

Referencing the package is not the opt-in; the `AddSvgImages()` call is. An application that
references it and never calls it gets no SVG at all: nothing claims the bytes, the built-in decoder
does not read XML, and the load throws an `ImageFormatException` naming no provider - the same
failure a stream that is not an image has always had.

#### What the package adds to the seam

The provider (at priority 100, above anything else registered) is the whole of its loading story:
there is no `Svg` loader, no second way to get an `ImageData`, and nothing an application
can call that skips the factory. What the package adds around it is the document work - the secure
parse, the cache, `currentColor` resolution - and one options type, which reaches the provider as a
**typed overload** of the same three factory calls:

```csharp
using Kolvir.Svg;

image.Source = factory.LoadFromPath("icons/badge.svg", new SvgOptions
{
    CurrentColor = Theme.Foreground,
});
```

Those overloads parse and validate nothing themselves: they hand the options to the factory, which
hands them to whichever provider claims the bytes. Validate in the provider, where the format lives;
an extension method that has real work to do is a sign that the work belongs in core.

#### Sizing, and what `viewBox` means to `AutoSize`

The picture's bounds are the document's natural size, and that is what `AutoSize` measures and what
every `SizeMode` scales from:

| Document | Natural size |
| --- | --- |
| `viewBox="0 0 24 24"`, no width or height | 24 x 24 |
| `width="48" height="24" viewBox="0 0 24 12"` | 48 x 24 - the document's own size wins |
| `width="60" height="20" viewBox="0 0 30 30" preserveAspectRatio="none"` | 60 x 20 |
| `viewBox="10 20 30 40"` | 30 x 40, recorded from the content's own origin |

Give the root element a `viewBox`. A document with no `viewBox` and no width or height has no
intrinsic size, and the renderer resolves that to a one-pixel picture rather than failing - a real
icon always has one.

#### Recolouring: `currentColor`

An icon that draws with `currentColor` can follow the palette, and nothing else can be recoloured
without rewriting the document:

```csharp
image.Source = factory.LoadFromPath("icons/badge.svg", new SvgOptions
{
    CurrentColor = Theme.Foreground,
});
```

The colour is resolved **while the document is parsed**, because that is when the renderer resolves
`currentColor`, so the parse cache is keyed on the colour as well as the bytes. Two colours of one
document are two parses; every load after that at either colour is a cache hit and a fresh recording.
Switching a small palette back and forth therefore settles and stops parsing, which is what makes it
safe to reload icons on a theme change:

```csharp
Theme.AddChangedHandler(_ =>
{
    image.Image?.Dispose();
    image.Source = factory.LoadFromPath("icons/badge.svg", new SvgOptions
    {
        CurrentColor = Theme.Foreground,
    });
});
```

Reloading recolours also shows the ownership rule in use: the control does not dispose what it was
showing, so the handler disposes the previous `ImageData` itself before assigning the next one.
A document that fails to parse throws out of the load, which is where the code that asked for it can
catch it.

#### Caching

A document is parsed once per (content, colour) and the parsed result is reused: each load gets a
fresh recording of it - a few hundred bytes of display list - so that the control's ownership rule
never disposes something the cache is still handing out, and two controls showing one icon draw two
recordings of one parse. The cache holds 64 entries and evicts the oldest, so an application that
renders an SVG per database row does not leak; `Svg.ClearCache()` empties it, and a control's
recording outlives the parsed document it came from.

#### Security

A document is data from outside the program, and the refusals are the feature's, not the renderer's
defaults. Each line below is measured, and the reason is stated because "the renderer does not do
it today" is not the same claim as "this feature refuses it":

- **External references resolve to nothing.** A `file://` URL, a bare `http(s)` location and a
  relative sibling file were each tried against three external-resource policies and two load
  routes, and none of them appeared in the output - Svg.Skia's asset loader does not fetch a
  document's references, it substitutes a broken-image placeholder. The feature narrows that further
  rather than relying on it: the policy it parses with is *same-document and data: only*, so a
  `<use href="#shape">` and a `data:` URI resolve and nothing else may, whatever a future renderer
  learns to fetch. A document parsed from a stream also has no base URI, so there is nothing for a
  relative reference to resolve against.
- **Script does not run.** `<script>` elements and `onload`-style attributes have no effect: the
  document is parsed in the renderer's secure processing mode, JavaScript is off, and there is no
  scripting runtime to run it with even if it were not.
- **Entity expansion is refused by this feature.** A document that declares a DOCTYPE or an ENTITY
  is rejected before it is parsed, in both UTF-8 and UTF-16, because entity expansion is unbounded
  work on a document of any size. This one is the feature's own check and it is load-bearing:
  removing it makes the entity-bomb test fail, because the renderer happily expands the document.
- **The document cap is one megabyte.** The whole document is in memory by the time it is parsed, so
  the limit is one the library chose rather than one it inherited.
- **Malformed input surfaces as an `ImageFormatException`** with the parser's exception inside
  it, thrown by the load that asked for the document. It is never thrown at the paint thread, and an
  image a control is already showing stays on screen.

#### What actually renders

Measured against Svg.Skia 5.2.3 with the settings above. This is not full SVG 1.1, and the gaps are
listed rather than implied:

| SVG feature | Support |
| --- | --- |
| `rect`, `circle`, `ellipse`, `line`, `polyline`, `polygon`, `path` (including arcs) | Yes |
| `g`, `transform`, `viewBox`, `preserveAspectRatio`, nested `<svg>` | Yes |
| `fill`/`stroke` with colours, `opacity`, `stroke-dasharray`, line joins and caps | Yes |
| Linear and radial gradients, `pattern` | Yes |
| `clipPath`, `mask` | Yes |
| `use`, `symbol` | Yes |
| `text`, `tspan`, `text-anchor`, font family and size | Yes, through the system's fonts. On Linux Svg.Skia shapes it with the HarfBuzz native, which is the reason this package carries 2-3 MB of native library; on Windows `<text>` renders without it |
| Filters: `feGaussianBlur`, `feDropShadow`, `feColorMatrix`, and the rest of the SVG 1.1 set | Yes |
| CSS in a `<style>` element and `class` selectors | Yes; `@import` is not followed |
| `data:` URI images | Yes |
| `marker` | Partially: markers draw, but `markerWidth`/`markerHeight` scaling is not applied, so a marker is drawn in user units |
| SMIL animation (`<animate>`, `<animateTransform>`, `<set>`) | No: the document is rendered as a static frame |
| SVG fonts (`<font>` inside the document) | Passed through, not exercised here |
| `foreignObject` | No: its content is not drawn |
| External images, external CSS, external `<use>` | Refused by design - see Security |

The demo's **SVG icons**, **SVG recolour** and **SVG and a raster twin** pages show all of the
above that a viewer needs to believe: the first at five sizes, the second following the palette, and
the third comparing a vector disc with a 16-pixel PNG of itself blown up to 96.

The rows that fail silently if they regress - a gradient that comes out flat, text that parses and
draws nothing because nothing shaped it, a filter that is parsed and ignored - are pixel assertions
in `SvgFidelityTests` rather than only a claim in this table: the gradient is sampled at both stops
and in the middle, the pattern at both halves of a tile, the blur three units outside the shape
against an unfiltered twin, and the transform and clip path at the pixels they decide.

#### What it costs

Measured the way FEATURE-COST-POLICY.md section 8 says to: one fixture, published twice, with the project
reference to `Kolvir.Feature.Svg` and the `AddSvgImages()` call switched off and on by a property,
so the two numbers differ by the feature and nothing else. The feature build calls `AddSvgImages()`,
loads a document through the factory, draws the image and writes it out. `PublishAot`,
`-p:EmbedSkiaSharpNative=false`, three RIDs. The fixture is `Kolvir.Minimal` with one `Image`
on its page - 88 KB above the policy's 3,203,072 floor on win-x64, which is what an image control
costs before any format is added:

| Measurement | win-x64 | linux-x64 | osx-arm64 |
| --- | --- | --- | --- |
| Executable, feature not referenced (the floor) | 3,292,160 | 4,218,192 | 4,383,216 |
| Executable, feature used | 15,759,360 | 17,032,224 | 14,653,248 |
| Delta | +12,467,200 (3.8x the floor) | +12,814,032 (3.0x) | +10,270,032 (2.3x) |
| HarfBuzz native beside it | 2,065,248 | 3,059,496 | 2,901,024 |
| Whole publish folder, feature not referenced | 16,195,936 | 16,480,600 | 21,585,363 |
| Whole publish folder, feature used | 30,728,384 | 32,354,128 | 34,756,558 |
| Delta | +14,532,448 | +15,873,528 | +13,171,195 |

The publish reported no IL2xxx or IL3xxx warning on any of the three, with
`-p:TrimmerSingleWarn=false`, so the cost is code that is genuinely reachable rather than trimming
that gave up.

##### Where the bytes are

Attributed from the ILC map file (`-p:IlcGenerateMapFile=true`) by summing the machine-code bytes of
every node under its assembly, win-x64, feature build minus floor build. Code is 10.7 MB of the
12.5 MB the executable grows; the rest is metadata and data sections.

| Where | Bytes | What it is |
| --- | --- | --- |
| `Svg.SceneGraph` | +928,964 | the renderer that walks the document and issues Skia commands |
| `Svg.Custom` | +748,340 | the XML parser and the SVG element model |
| `Svg.Skia` | +153,740 | the Svg-to-Skia bridge |
| `Svg.Animation` | +87,426 | SMIL |
| `Svg.Model` | +60,995 | shared model types |
| `ShimSkiaSharp` | +115,301 | the immediate-mode drawing model Svg.Skia renders through |
| `ExCSS` | +330,302 | the CSS parser behind `<style>` and `class` |
| `HarfBuzzSharp` | +36,791 | the managed half of text shaping |
| `Kolvir.Feature.Svg` | +5,690 | this package: the provider, the cache, the secure parse |
| `System.Private.CoreLib` | +1,402,755 | generic instantiations, string and collection code the model pulls in |
| `System.Net` | +757,892 | the HTTP stack, see below |
| `System.Private.Xml` | +332,912 | the XML reader the document is parsed with |
| `System.Text` | +306,977 | encodings, plus `System.Text.RegularExpressions` |
| `System.Linq`, `System.Collections` | +356,704 | the model's queries and maps |
| `System.Security` | +109,989 | TLS, pulled in behind `System.Net` |
| `System.Diagnostics` | +53,684 | stack traces |
| linker bookkeeping, unrelated vtables and boxed statics | +4,775,243 | metadata blobs, stack-trace mapping, frozen segments, vtables |

So the SVG stack itself - the seven assemblies above `Kolvir.Feature.Svg` - is **2.47 MB**. The
other 8.3 MB is what the BCL adds once a large generic-heavy model with an XML parser, a CSS parser
and a regex engine becomes reachable, plus the linker's own metadata. That is the shape of the
number, and it is why "trim the SVG packages" does not get far.

##### What was tried, and what it measured

- **Trim descriptors.** Every `svg.*` package and `shimskiasharp` ship no `ILLink` descriptor, no
  `.props`/`.targets` and no `IsTrimmable=false`. Nothing in the packages roots anything; the
  trimmer is doing its job and the cost is reachable code, not a rooting accident.
- **`Svg.Animation`.** SMIL costs 87,426 bytes of code. It cannot be dropped without forking
  `Svg.Custom`, which puts `SvgAnimationElement` in the element hierarchy that every document is
  parsed into, and `Svg.Skia`, which enumerates animation elements. 0.8% of the delta, for a fork.
- **HarfBuzz natives.** This is the one place the packages waste real bytes - 2.1 MB on win-x64,
  3.1 MB on linux-x64, 2.9 MB on osx-arm64 - and the measurement says the three platforms disagree
  about whether it can go:
  - win-x64: it can be removed. With `libHarfBuzzSharp.dll` deleted and the three
    `HarfBuzzSharp.NativeAssets.*` packages excluded, `<text>` renders pixel-identically, because
    the Windows `libSkiaSharp.dll` shapes text itself.
  - linux-x64: it cannot. `Svg.Skia` reaches HarfBuzz for `<text>` and the load fails with
    `DllNotFoundException`, surfaced correctly as an `ImageFormatException` at the factory call
    rather than thrown at the paint thread.
  - osx-arm64: it cannot, and not even later - the AOT image links
    `@executable_path/../../Contents/MonoBundle/libHarfBuzzSharp.dylib` at load time, so removing
    the file stops the process from starting at all.

  So the native stays, and text is the reason it is there. A Windows-only exclusion is technically
  possible and not worth a platform-special case that would make `Load` behave differently on one
  RID.
- **`-p:IlcOptimizationPreference=Size`.** No gain: 15,762,432 against 15,759,360, 3 KB larger.
- **`-p:StackTraceSupport=false -p:IlcGenerateStackTraceData=false`.** Saves 965,120 bytes on
  win-x64, 1,261,664 on linux-x64 and 1,103,424 on osx-arm64, and it is worth knowing about - but
  it is a whole-application policy that takes away exception stack traces everywhere and shrinks
  the floor too. It is not a feature's lever, and this package does not set it. With it, the
  executable delta falls to +11.5 MB on all three RIDs.

##### The verdict

10 MB of managed code is what this costs, and it cannot be trimmed further from this side. 2.5 MB
of it is SVG, 8.3 MB is BCL the SVG model drags along, and the two things that would move it
materially are outside the package: the HTTP stack, which enters through
`Svg.SvgElement`'s static constructor calling `new HttpClient()` unconditionally and through
`WebRequest` in `Svg.Model.Services.SvgService.GetImageFromWeb` and
`Svg.Skia.SkiaSvgAssetLoader.OpenFontResourceStream` (about 0.87 MB of code with TLS behind it, and
dead for an icon), and SMIL. Both need a fork of Svg.Skia, not a knob here. Nothing else on the
list pays this: it is rung 3, so an application that does not reference the package is the floor,
unchanged, and an application that does should know it is buying a document renderer rather than a
icon loader - the demo's *SVG and a raster twin* page is the trade to look at before deciding.

What the fixture proves, and what it does not: it proves the whole path an application uses - the
provider, the secure parse, the cache, the recording and the control's paint - so the numbers are
what opting in costs. It does not prove that a document using more of SVG pulls nothing more: it has
one gradient, one `<text>` and no filter, and a program that uses filters and masks keeps code this
one trims.

The seam every application does pay is small. `Kolvir.Minimal` publishes at 3,203,072 before and
after this change - the same bytes, because it names no image - and a fixture that is the floor plus
one `Image` loading a PNG publishes at 3,325,440 against 3,318,784 at `1b63ea1`, so the factory,
the provider interface and the header sniff cost 6,656 bytes to an application that shows an image
and never adds a format.

[Svg.Skia]: https://github.com/wieslawsoltes/Svg.Skia

### Animated GIF

Animated GIF is a package of its own, `Kolvir.Feature.AnimatedGif`, and it is a small one: SkiaSharp
already decodes GIF frames, so what the package adds is the compositor, the loop-declaration walk and
the timeline glue, with no native library of its own. Reference it and opt in:

```csharp
var options = new ApplicationOptions
{
    ConfigureServices = services => services.AddAnimatedGifImages(),
};
```

**A GIF without it is still an image.** The built-in raster decoder reads the first frame, the
control shows a picture, and that is the documented fallback rather than an error - what an
application that has not enabled the feature should expect. Enabling it registers a provider that
claims `GIF87a` and `GIF89a`, and claiming is all it takes: the built-in decoder only sees bytes
somebody else declined, so a GIF stops being a still picture and becomes an animation without a
single change at the call site.

```csharp
using Kolvir;   // GifOptions

image.Source = factory.LoadFromPath("art/spinner.gif");

// or, from a stream, with the feature's own settings
image.Source = factory.LoadFromStream(stream, new GifOptions
{
    DecodedSizeLimit = 32L * 1024 * 1024,
});
```

#### What the file says, and what the animation gets

| In the file | What the animation gets |
| --- | --- |
| Per-frame delays | Per-frame delays: a GIF with mixed delays plays mixed delays |
| A delay of zero | `GifOptions.ZeroDelayMinimum`, 100 ms by default |
| A Netscape loop extension with any count, `0` or `N` | `Loop` starts `true` |
| No loop extension | `Loop` starts `false` |
| Frames | All of them, decoded up front |
| The canvas size | `NaturalSize`, fixed for the whole animation |

**`Loop` is a bool on purpose.** The extension defines `0` as "loop forever" and `N` as "play N
times", so any count at all means the file asked to repeat, and a file with no count asked to play
once. The count itself is not kept: a finite count is a total number of passes, and the timeline
tracks a position inside one pass rather than a total elapsed time, so honouring it would mean a
second value that nothing else uses. A caller who wants exactly three passes counts them.

**Zero delays.** A zero delay means "as fast as possible", which taken literally asks the timeline for
the next frame immediately and leaves a visible animation waking at the millisecond floor forever -
a busy loop doing nothing useful. 100 ms is the minimum browsers settled on for a zero-delay GIF, so
an animation authored for the web already looks the way this plays it. Only an exact zero is
replaced: a declared 10 ms is a delay the author chose, and it is played at 10 ms.

#### Memory, and the limit

Every frame is decoded and composited up front, because the timeline lets a viewer seek anywhere -
backwards included - and a lazy window cannot go back. That makes the cost predictable, frames x width
x height x 4 bytes, and it is bounded by `GifOptions.DecodedSizeLimit`: 256 MiB by default, which
a 2000x2000 GIF of 60 frames exceeds at 960 MB of pixels. A file over the limit is refused rather than
partially decoded, so a load either produces a complete animation or throws with the numbers in the
message.

#### Compositing, which is the part that goes wrong quietly

GIF frames are mostly deltas: a frame names a rectangle and usually carries only the pixels inside it,
so the picture at frame *n* is the canvas after frames 0 to *n* have been composited. Skia will
reconstruct a frame from the previous one, but the disposal rule is per frame and the blend flag is
per frame, so the compositing is this package's job rather than a flag passed to the decoder.

| Frame's disposal | What happens to its rectangle before the next frame draws |
| --- | --- |
| Keep (0, 1) | Nothing: the next frame draws over it |
| Restore background (2) | Cleared to transparent |
| Restore previous (3) | Put back to what it was before this frame drew, from a snapshot |

The blend flag decides whether a frame replaces the pixels in its rectangle or is drawn over them;
GIF's default is to blend, which is what makes the transparent index mean anything. Restore
background is cleared to transparent rather than to the logical-screen background colour, because
that is what browsers do, the background index is absent or meaningless in most animated files, and a
control that fills its background cannot tell the difference.

Wrong compositing produces frames that look nearly right - right size, right timing, subtly corrupt -
which is why the tests assert the pixel content of specific frames rather than trusting the demo:
each disposal method has a fixture whose earlier frames leave something behind, and the assertions are
on colours in regions rather than on a hash.

#### What it costs

Measured the way FEATURE-COST-POLICY.md section 8 says to, on win-x64: `Kolvir.Minimal` published with
`PublishAot` and `-p:EmbedSkiaSharpNative=false`, then published again with the project reference and
an `AddAnimatedGifImages()` call added and one GIF loaded and drawn. The two numbers differ by the
feature and nothing else.

| Measurement | Bytes |
| --- | --- |
| Executable, feature not referenced (the floor at this commit) | 3,283,968 |
| Executable, feature used | 3,575,808 |
| Delta | +291,840 (285 KB, +8.9%) |
| `libSkiaSharp.dll` beside it | 12,903,776, unchanged |

No new native library: Skia already carries the GIF decoder, so the package is the compositor, the
loop walk and the glue. What the fixture proves is that the whole path an application uses - the
provider, `SKCodec`, the compositing, the timeline - is reachable and cheap. What it does not prove
is that a large file is free: frame memory is `frames x width x height x 4` and that is bounded at
run time by `DecodedSizeLimit`, not by the size of the executable.

The rest is the `ImageData` every image has: `Loop` can be reassigned after the load, `Play`,
`Pause` and `Seek` are the same timeline a PNG has, and the control never disposes it.

The demo's **Animated GIF** page plays the sample with play, pause, a seek bar and a position readout;
**GIF frames** steps it frame by frame; **GIF out of view** scrolls the picture out of a frame and
back with a button, so the frame it returns on can be compared with the readout.

### Animated WebP

Animated WebP is a package of its own, `Kolvir.Feature.AnimatedWebp`, and it follows the animated GIF
package rung for rung: SkiaSharp already decodes WebP, so what the package adds is the RIFF container
walk, the compositor and the timeline glue, with no native library of its own. Reference it and opt
in:

```csharp
var options = new ApplicationOptions
{
    ConfigureServices = services => services.AddAnimatedWebpImages(),
};
```

**A WebP without it is still an image.** The built-in raster decoder reads WebP, so an application
that has not enabled the feature still shows the file - frame one, as a still. Enabling the feature
registers a provider that claims every `RIFF....WEBP` stream. A stream that turns out not to be
animated is decoded through the feature's still path, which records the picture the same way the
built-in decoder does; a test loads one file through both paths and compares every pixel. Enabling
the feature therefore cannot make a static WebP worse, which is what the GIF package promises for a
single-frame GIF as well.

```csharp
using Kolvir;   // WebpOptions

image.Source = factory.LoadFromPath("art/spinner.webp");

// or, from a stream, with the feature's own settings
image.Source = factory.LoadFromStream(stream, new WebpOptions
{
    DecodedSizeLimit = 32L * 1024 * 1024,
});
```

#### The container, and what the animation gets

A WebP file is `RIFF`, a 32-bit little-endian size, `WEBP`, then chunks of a four-character code, a
size and a payload padded to an even length. The package walks that chain once, and what it reads is:

| Chunk | What the package uses |
| --- | --- |
| `VP8X` | The animation bit and the canvas width and height (`NaturalSize`) |
| `ANIM` | That a loop is declared: `Loop` starts true for any `ANIM` chunk. The 16-bit count is not read or preserved; the background colour is read but not used for disposal |
| `ANMF` | One per frame: the offset in two-pixel units, the frame rectangle, the duration in milliseconds, the blend bit and the disposal bit |
| `ALPH`, `VP8 `, `VP8L` | The frame's own image data, moved into a stand-alone container so Skia decodes that frame alone rather than the canvas with earlier frames composed onto it |

The offsets are stored in two-pixel units, so an odd offset cannot occur in a valid file; the composer
draws the frame at `(x, y)` on a full canvas and hands out a picture of the canvas. A frame is decoded
exactly once: the parser moves the frame's image chunks, and the decoder reads that result.

| In the file | What the animation gets |
| --- | --- |
| Per-frame durations | Per-frame durations: a WebP with mixed durations plays mixed durations |
| A duration of zero | `WebpOptions.ZeroDelayMinimum`, 100 ms by default - the same number `GifOptions` uses |
| An `ANIM` chunk with any count, `0` or `N` | `Loop` starts `true` |
| No `ANIM` chunk | `Loop` starts `false` |
| A single `ANMF` | A still picture: duration zero, no loop - the GIF feature's answer for a single-frame GIF |
| The `VP8X` canvas | `NaturalSize`, fixed for the whole animation |

**The loop rule never re-derives a second answer.** WebP's `ANIM` count has the same shape as GIF's
Netscape extension: `0` is "forever" and `N` is "N times", while the playback model has a boolean
whose pass is a position inside one pass, not a total elapsed time. Any declared count therefore
means `Loop` starts true, no declaration means false, and the finite count is not preserved. A caller
who wants exactly three passes counts them - see the GIF section above for the longer version.

**Zero durations match the GIF feature on purpose.** A zero duration taken literally asks the
timeline for the next frame immediately, which is a visible busy loop; 100 ms is the minimum browsers
already apply and the number the GIF package chose. A declared 10 ms is still played at 10 ms. The
two packages are separate, so the number is duplicated rather than shared; the tests in each suite
assert it, which is what keeps them from drifting apart.

#### Compositing, and the one place WebP differs most

WebP frames are deltas: an `ANMF` names a rectangle and usually carries only the pixels inside it, so
frame *n* is the canvas after frames 0 to *n* have been composited. The blend and disposal bits are
per frame:

| Frame's blend bit | What happens inside its rectangle |
| --- | --- |
| Clear (0) | The frame is drawn over the canvas, so a transparent pixel lets the previous picture through |
| Set (1) - "do not blend" | The frame replaces the rectangle, transparent pixels included |

| Frame's disposal bit | What happens to its rectangle before the next frame draws |
| --- | --- |
| 0 - keep | Nothing: the next frame draws over it |
| 1 - dispose to background | Cleared to transparent |

**WebP has two disposal methods, not GIF's three.** There is no "restore previous" in the format, so
there is no snapshot and no third branch in the compositor; a `SKCodecAnimationDisposalMethod` with
value 3 cannot come out of a valid WebP. Disposal 1 is cleared to transparent rather than to the
`ANIM` background colour, because that is what browsers and libwebp do, the colour is absent or
meaningless in most files, and a control that fills its background cannot tell the difference - the
same decision, and the same reasoning, as the GIF package.

The blend bit is easy to invert, and inverting it produces frames that look nearly right - right
size, right timing, a different picture under every transparent pixel - so the suite asserts the
pixel content of specific frames: a blending frame over red, a do-not-blend frame with a transparent
pixel, and a frame after a background disposal. Both the blend branch and the disposal branch were
broken on purpose during development and the tests failed, which is what makes them evidence rather
than decoration.

#### Memory, and the limit

Every frame is decoded and composited up front, because the timeline lets a viewer seek anywhere -
backwards included - and a lazy window cannot go back. The cost is predictable, frames x canvas
width x canvas height x 4 bytes, and it is bounded by `WebpOptions.DecodedSizeLimit`: 256 MiB by
default, which a 2000x2000 WebP of 60 frames exceeds at 960 MB of pixels. A file over the limit is
refused rather than partially decoded, so a load either produces a complete animation or throws with
the numbers in the message.

#### What it costs

Measured the way FEATURE-COST-POLICY.md section 8 says to, on win-x64, linux-x64 and osx-arm64:
`Kolvir.Minimal` built with `PublishAot` and `-p:EmbedSkiaSharpNative=false`, then built again with
an `Image` showing a PNG through the built-in decoder, then built a third time with the WebP
package referenced, `AddAnimatedWebpImages()` called and the demo's animated WebP loaded and drawn.
The three numbers separate the image control and the timeline from the feature:

| Measurement | win-x64 | linux-x64 | osx-arm64 |
| --- | --- | --- | --- |
| Executable, one label (the floor at this commit) | 3,283,968 | 4,276,144 | 4,080,304^ |
| Executable, one `Image` showing a PNG (image control and timeline paid for) | 3,526,656 | 4,590,160 | 4,080,304^ |
| Executable, the same control showing an animated WebP with the feature registered | 3,582,464 | 4,636,032 | 4,080,304^ |
| Feature only, against the PNG fixture | +55,808 (54.5 KB) | +45,872 (44.8 KB) | 0 |
| Whole use path, against the label fixture | +298,496 (291.5 KB, +9.1%) | +359,888 (351.5 KB, +8.4%) | 0 |
| `libSkiaSharp` beside it | 12,903,776 | 12,262,408 | 15,804,304 |

^ macOS was measured with `dotnet build -c Release -r osx-arm64 -p:PublishAot=true` rather than
`dotnet publish`, because `dsymutil` fails during publish on that host; that failure is pre-existing
on master. The app bundle's binary is the one quoted, and the pre-strip `nativelibraries` binary was
4,103,584 for all three builds as well. The arm64 image is linked in 16 KB pages and the feature fits
in slack the linker had already paid for: the file lengths are byte-for-byte identical while the
hashes differ and the feature's assembly is present in the WebP build and absent from the floor.
That is the same zero-cost shape FEATURE-COST-POLICY.md records for other arm64 features.

No new native library: Skia already carries the WebP decoder, so the package is the RIFF walk, the
compositor and the glue. What the fixture proves is that the whole path an application uses - the
service-collection extension, the provider, Skia's WebP decoder, the compositor and the timeline - is
reachable and cheap on all three platforms. What it does not prove is that a large file is free:
frame memory is `frames x width x height x 4` and that is bounded at run time by
`DecodedSizeLimit`, not by the size of the executable. It also does not isolate Skia's decoder from
the package, because the PNG fixture already links the built-in raster path and the WebP decoder
lives in the same Skia library; the +55,808 and +45,872 byte figures are the package's own code and
the metadata that keeps it reachable, while osx-arm64 absorbs the same code into existing page slack.

The rest is the `ImageData` every image has: `Loop` can be reassigned after the load, `Play`,
`Pause` and `Seek` are the same timeline a PNG has, and the control never disposes it.

The demo's **Animated WebP** page plays the sample with play, pause, a seek bar and a position
readout; **WebP frames** steps it through the timeline's own boundaries. There is deliberately no
WebP copy of the **GIF out of view** page: that page makes a claim about the timeline - time passes
while the picture is hidden - and the timeline is not per-format. A second copy would exercise the
same core code with a different byte stream and would not test anything WebP-specific.

## Hardware acceleration (Windows)

`ApplicationOptions.GfxHardwareAcceleration` is a `bool`: **true by default on Windows and
Linux, false on macOS**. The macOS default is explained in
[Hardware acceleration (macOS)](#hardware-acceleration-macos) and the Linux one, which is also
`true`, in [Hardware acceleration (Linux)](#hardware-acceleration-linux); neither is a carried-over
value. When it is true the Win32 backend
tries to build a WGL-backed Skia surface: a context on the window's own device
context, `GRGlInterface.Create()` plus `Validate()`, `GRContext.CreateGl()`, a
`GRBackendRenderTarget` for the default framebuffer, and an `SKSurface`. Only when every one of
those succeeds does the window present with `wglSwapBuffers`; any failure silently falls back to
the existing DIB + `BitBlt` path.

```csharp
var options = new ApplicationOptions
{
    // Windows and macOS. True by default on Windows and Linux, false on macOS; this is the shape
    // to use when the launcher owns the command line and wants the switch honoured explicitly.
    GfxHardwareAcceleration = !args.Contains("--no-hardware-acceleration"),
};
```

`--no-hardware-acceleration` is the switch. It is read from `ResolveCommandLine()` **in the
constructor**, so an explicit `CommandLine` list is honoured when it is passed to
`ApplicationOptions(IReadOnlyList<string>)` - the form to use when the arguments are known at
construction. `CommandLine` is a settable property, but assigning it after construction does not
re-apply its switches: a launcher that owns the command line either uses the overload that takes it
or assigns `GfxHardwareAcceleration` itself. An explicit assignment to `GfxHardwareAcceleration`
wins over whatever the constructor resolved.

The rules the implementation follows:

- **WGL only when the option is true.** `Win32PopupWindow` and `Win32MenuWindow` stay on the DIB
  path, and the headless backend has no such seam. AppKit has its own Metal seam, described in
  [Hardware acceleration (macOS)](#hardware-acceleration-macos), and GTK4 its own GL seam,
  described in [Hardware acceleration (Linux)](#hardware-acceleration-linux). `--render` is
  headless and cannot reach any of them.
- **Every step is checked.** Context creation, the GL interface and its `Validate()`, the Skia
  context, `GRBackendRenderTarget.IsValid` and `SKSurface.Create()` each can fail. A machine with
  no OpenGL ICD - a Hyper-V VM, an RDP session with no vendor driver - renders exactly as it did
  before.
- **GL and GDI never share a window.** When GL comes up the retained DIB is released and
  `WM_PAINT` presents with a blit and `wglSwapBuffers`; it does not `BitBlt`. `WM_PRINTCLIENT`
  paints one CPU frame into a temporary DIB so screenshots and visual capture still work.
- **The pixel format is chosen on a throwaway window, not on the real one.** A window's format can
  be set once and never changed, so the probe spends a hidden 1 x 1 window per flavour.
  `gdi32`'s `ChoosePixelFormat`/`SetPixelFormat` is tried first, because that is the app-facing
  path a vendor ICD expects; if it cannot produce a Skia-usable context, `opengl32`'s
  `wglChoosePixelFormat`/`wglSetPixelFormat` pair is tried on a fresh window - that is the Mesa
  path. The probe also creates and validates a `GRGlInterface` on the throwaway, so a stack with
  WGL but only GDI Generic OpenGL 1.1 (no interface Skia can use) never spends the real window's
  one-shot format. Only a flavour that passed every step is used
  on the real window. The framebuffer's sample count comes from `GL_SAMPLES` and its stencil depth
  from the described pixel format, and both are passed to `GRBackendRenderTarget` - never
  hardcoded.
- **Vsync is one interval** when `wglSwapIntervalEXT` exists.
- **A lost context is a fallback, not an exception.** A driver reset, an RDP change, or a resize
  that cannot rebuild the render target disposes the GL stack and returns the window to the DIB
  for the rest of its life.
- **The context lives on the UI thread.** Creation, make-current, paint, flush and swap all happen
  there; nothing runs on a timer or a worker.
- **The frame lives in a retained offscreen buffer.** The host's `SKRegion` decides what the UI
  invalidated, and that is now exactly what is repainted: a partial invalidation paints the damaged
  region into a persistent RGBA8 offscreen surface, then one Skia blit stamps the whole buffer onto
  the window's default framebuffer and `wglSwapBuffers` presents it. No raw shader, no GLSL; the
  blit is Skia's own. If nothing was damaged and Windows is not asking for pixels, no frame is
  submitted at all. An uncovered or restored window blits the retained buffer without repainting
  the tree. The offscreen buffer has undefined contents when it is created, so the first frame
  after creation and after any resize or DPI change is painted in full.
- **Three tiers with a reason.** Offscreen-FBO GL is the normal Tier 1. If the offscreen surface
  cannot be created, or a snapshot/blit fails, the window drops to direct-to-FBO-0 GL for the rest
  of its life (with one full repaint to recover the frame); if the context or the swap fails, it
  drops to the CPU DIB. Each step is named, so a screenshot says which tier is live.

`Win32WindowBackend.GraphicsPath` reports `gl` or `cpu (...)` with the reason, and `GraphicsRenderer`
reports the GL vendor and device. The reason names the failing step rather than a generic
"no context", for example:

```
gfx: cpu (no gl interface; gdi: no wgl context (error 2000))
```

Set `KOLVIR_GL_TRACE=1` to print every probe attempt, the chosen flavour and format number, the
described colour/stencil/sample counts and the renderer to stderr. The demo prints the one-line
path and renderer at startup and keeps them in its footer, so a screenshot says which path produced
its numbers; the **Measured rendering** page is where the CPU/GL A/B is run.

Measured cost on the `Kolvir.Minimal` floor (Windows x64, NativeAOT, Skia loose, the command in
section 8 of [FEATURE-COST-POLICY.md](FEATURE-COST-POLICY.md)):

| Measurement | Bytes |
| --- | ---: |
| `Kolvir.Minimal` floor before this change | 3,283,456 |
| After the Win32 GL seam, the flavour probe, damage-aware present and the `GfxHardwareAcceleration` option | 3,334,656 |
| Delta | +51,200 (+1.56 %) |

The WGL/P-invoke half is a handful of functions and one internal type set; it is not a package and
adds no native asset. `opengl32.dll` is an operating-system component and the shipped
`libSkiaSharp.dll` already contains the GL backend, so nothing new is shipped. The retained
offscreen surface and its snapshot/`DrawImage` present add about 5 KB, because those Skia APIs were
already retained by the drawing set. The opt-in trace writes its UTF-8 bytes straight to the
standard error handle instead of `Console.Error`, because routing it through `Console.Error` alone
cost about 75 KB in a trimmed native image.

## Hardware acceleration (macOS)

`ApplicationOptions.GfxHardwareAcceleration` is the same `bool` as Windows, with the same
mechanics, but a different default: **true on Windows and Linux, false on macOS**. The AppKit
backend honours the flag exactly as the Win32 backend does - there is no separate opt-in mechanism
- so an application whose content is heavy turns Metal on by assigning the property. The demo
assigns it explicitly (`GfxHardwareAcceleration = !args.Contains("--no-hardware-acceleration")`),
which is what keeps the showcase showing the accelerated path, and `--no-hardware-acceleration` is
the same off switch on every platform.

The reason for the macOS default is the measured crossover in
[Physical Apple silicon](#measured-on-physical-apple-silicon): on a physical M4 Metal is 15-18x
faster on the image-heavy page and 1.6x slower on light text-heavy content, so the CPU path is the
non-regressing default and acceleration is a decision the application makes with its own content in
view. Opt in when your content is image-heavy; an application with only light text should leave the
default alone.

When it is enabled the AppKit backend tries to build a Metal-backed Skia context on the window's own
`NSView`: `MTLDevice.SystemDefault`, one `MTLCommandQueue`, a `CAMetalLayer` sublayer with
`PixelFormat = Bgra8Unorm`, `FramebufferOnly = false` and a `DrawableSize` of bounds x backing
scale, then `GRContext.CreateMetal(GRMtlBackendContext)`. Only when every one of those succeeds does
the window present from the layer's drawable; any failure silently falls back to the existing
`CGImage`-through-the-layer path.

No new native library ships. The macOS `libSkiaSharp.dylib` already contains the Metal backend and
the AppKit half is managed code over the Metal and CoreAnimation bindings, so this seam adds no
package reference and no native asset - unlike the Windows work, there is not even a P/Invoke
surface.

The rules the implementation follows:

- **Metal only when the option is true.** The AppKit backend reads the same flag as Win32 and has
  no gate of its own; the platform default (on for Windows and Linux, off for macOS) is what makes
  Metal opt-in on macOS. Popups and menus stay on the CPU path, and the headless `--render` path
  has no such seam; GTK4 has its own GL seam, described in
  [Hardware acceleration (Linux)](#hardware-acceleration-linux).
- **Every step is checked.** The device, the command queue, the layer, the Skia context and the
  first offscreen surface each can fail. Each failure names the step and leaves the window on the
  CPU path with no Metal state behind, for example `cpu (no metal device)`,
  `cpu (no command queue)`, `cpu (no gr context)` or `cpu (no metal surface: 2560 x 1600)`.
- **The frame lives in a retained offscreen buffer.** The host's `SKRegion` decides what changed and
  only that region is painted into a persistent Metal-backed surface at device-pixel size. Present
  is one Skia `Snapshot()` + `DrawImage` onto the drawable's surface, then `PresentDrawable` +
  `Commit`. No shader and no second texture of our own.
- **The direct tier is full-frame only.** A drawable's contents are undefined after present, so the
  tier that paints straight into a fresh drawable always paints the whole frame. It is chosen for
  the rest of the window's life if the retained buffer could not be created or a snapshot/blit
  failed.
- **A missing drawable skips the frame.** `NextDrawable()` returns null when the compositor has no
  surface ready; the frame is dropped without spinning or throwing, and the retained buffer keeps
  the damage for the next one.
- **Resize and scale changes recreate the drawable and the offscreen, then repaint in full.** The
  drawable size is bounds x `BackingScaleFactor`, so a Retina window is 2x and the same
  logical-vs-device trap the Windows work had applies here. `DidChangeBackingProperties` asks for a
  frame; `EnsureSize` sees the new pixel size and the next paint is full.
- **All Metal work is on the UI thread.** `UpdateLayer` creates the drawable, paints, flushes and
  commits; nothing runs on a timer or a worker.
- **Colour matches the CPU path.** The layer uses a device-RGB `CGColorSpace`, the same language the
  CPU path's `CGColorSpace.CreateDeviceRGB` speaks; the page comparison below is what proves there
  is no shift rather than an assumption.

`AppKitWindowBackend.GraphicsPath` reports `metal` or `cpu (...)` with the reason, and
`GraphicsRenderer` reports the Metal device name. Set `KOLVIR_METAL_TRACE=1` to print the device,
queue, context, drawable sizes, per-frame damage state and every failure to stderr. The demo prints
`gfx: metal; renderer: <device>` at startup and keeps the line in its footer.

### Measuring the AppKit GPU path by hand

The footer always names the live path. The demo assigns `GfxHardwareAcceleration` explicitly, so it
opts into Metal; an application that wants the accelerated path must do the same, because the macOS
default is the CPU path. The measured-rendering page (index 54) works exactly as it does on Windows:

1. Run the demo on the machine being measured - host access, screenshots and input injection are in
   [TESTING-RUNBOOK.md](TESTING-RUNBOOK.md). Check the footer says `gfx: metal` and the device is
   the one you expect. On a fallback it says `gfx: cpu (...)` with the step that failed; rerun with
   `KOLVIR_METAL_TRACE=1` to capture every step.
2. Open **Measured rendering**, choose **Damage: full** or **Damage: partial**, and press
   **Measure 600 frames**. The first 30 frames are discarded.
3. Press again with the other damage mode.
4. Quit, rerun with `--no-hardware-acceleration`, and repeat. That is the CPU half of the A/B.

`--page N --benchmark` starts the same measurement on any page, not only 54. Page 54 is
deliberately light, so it is the wrong page for a full-frame comparison on Apple silicon; **page 11
(Images and layout)** is the heavy one. `--partial-damage` selects the partial row on page 54, so
all four of its rows can be captured without a pointer. The `BENCH` line has exactly the Windows
shape:

```
BENCH mode="metal" renderer="Apple M4" size=1280x820 scale=2.00 damage=full frames=600 warmup=30 p50=0.883 p95=1.207 max=7.823 mean=0.872 fps=60.1 area=1280x820
```

`p50`/`p95`/`max`/`mean` are `UiHost.LastFrameTime`, the host's paint. The offscreen snapshot,
the drawable blit, `PresentDrawable` and `Commit` happen after that and are **not** in the number, so
the Metal partial row is the paint of the 180 x 180 swatch plus an unmeasured full-buffer blit and a
vsync'd present, not the whole frame. `fps` is the wall-clock cadence and does include the present.

#### Measured on physical Apple silicon

Mac mini (Apple M4), macOS 27.0, `AGXMetalG16G_B0`, 1280 x 820 at 2.00, 600 frames after 30
warm-up, measured 2026-11-09. The demo assigns `GfxHardwareAcceleration` explicitly; the CPU rows
are `--no-hardware-acceleration`. Debug and AOT agree, so this is not a build artefact.

| Mode | Damage | Debug p50 | AOT p50 | Debug mean | Painted area |
| --- | --- | ---: | ---: | ---: | --- |
| Metal | full | 2.949 | 2.848 | 2.684 | 1280 x 820 |
| Metal | partial | 0.118 | 0.088 | 0.119 | 180 x 180 |
| CPU | full | 1.852 | 1.823 | 1.898 | 1280 x 820 |
| CPU | partial | 0.116 | 0.112 | 0.120 | 180 x 180 |

On this page **Metal is 1.6x slower for a full frame** (2.95 vs 1.85 ms Debug; 2.86 vs 1.83 AOT),
the opposite of the Windows result. Partial damage is a wash (0.118 vs 0.116), and partial Metal
includes the full-buffer blit, so the blit is not the problem. Forcing **Tier 2** (direct to the
drawable, no offscreen, no `Snapshot`) measured **2.948 ms p50** against Tier 1's 2.949: the
retained buffer and the snapshot are free on this hardware, and the 2.77 ms is the GPU rasterising
this text-heavy page. Reworking the present design would not recover it.

Page 54 is not the page that decides the design. On the heavy page, **page 11 (Images and layout)**,
the same builds give:

| Mode | Debug p50 | AOT p50 | Debug p95 | Debug fps | AOT fps | Painted area |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Metal | 0.883 | 0.748 | 1.207 | 60.1 | 60.0 | 1280 x 820 |
| CPU | 13.257 | 13.186 | 13.569 | 42.2 | 42.5 | 1280 x 820 |

**Metal is 15-18x faster** (0.75-0.88 vs 13.2-13.3 ms p50) and holds 60 fps where the CPU path
drops to 42. The crossover is content: Apple's tile-based GPU pays a per-frame cost on many small
antialiased glyphs and wins on large image fills. That is why the macOS default is the CPU path:
an application with image-heavy content assigns `GfxHardwareAcceleration = true` and gains an order
of magnitude; an application with only light text stays on the path that was already under budget.
The Windows default stays on because the Win32 measurements win on both.

#### Measured on macvm (paravirtual GPU)

Apple M4 (Virtual), macOS 27.2, `AppleParavirtGPUMetalIOGPUFamily`, 1280 x 800 logical client at
2.00 scale (the VM's work area is 800 high), Debug build, 600 frames after 30 warm-up, measured
2026-11-09. CPU rows are `--no-hardware-acceleration`. **These are paravirtual-GPU numbers: they
show the shape of the change, not what physical Apple hardware does.** They were taken from a Debug
build because a Release non-AOT app bundle is rejected by macOS code-signing validation on this VM
(the same failure reproduces on unmodified `master`); the AOT executable is measured separately
below.

| Mode | Damage | p50 | p95 | max | mean | Painted area |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| Metal | full | 0.642 | 0.861 | 5.317 | 0.672 | 1280 x 800 |
| Metal | partial | 0.038 | 0.072 | 0.131 | 0.043 | 180 x 180 |
| CPU | full | 2.257 | 3.090 | 6.750 | 2.407 | 1280 x 800 |
| CPU | partial | 0.177 | 0.287 | 0.437 | 0.192 | 180 x 180 |

A full frame is **3.5x cheaper under Metal on this paravirtual device** (0.642 vs 2.257 ms p50),
and the partial row really does paint only the 180 x 180 swatch. That is the VM's shape only: the
physical section above shows page 54 reversing (Metal 1.6x slower) while page 11 keeps a large
Metal win. Use the physical numbers for any decision; this table is here because a VM with a
paravirtual GPU is what `macvm` provides for CI.

#### Correctness: Metal vs CPU, all 54 pages

The same `DemoUi` page tree was painted twice at 2560 x 1640 device pixels, once into a raster
`SKSurface` and once into a Skia surface on `GRContext.CreateMetal` over the same device and queue,
then read back and compared channel by channel. 34 of the 54 pages differ by at most 30/255 with
**zero pixels above 32/255**. The worst non-animation page is the SVG icon gallery at **74/255**,
where 398 of 4,198,400 pixels (0.0095 %) differ by more than 32 - all of them antialiasing edges on
the vector icons. The two live-animation pages (49 and 52) differ at up to 201/255 in 131 pixels
because two freshly built instances are at different animation frames; that is timing, not the
rasteriser. Nothing is structurally different, and flat fills and text stay within the same 10-11/255
rounding the Windows work measured.

The CPU path itself is unchanged: headless `--render` of all 54 pages is byte-identical to `master`
on 53 pages, and the one difference is page 49's animated GIF phase - the same timing difference the
Windows comparison recorded.

Measured cost on the `Kolvir.Minimal` fixture (osx-arm64, NativeAOT, the app-bundle binary because
the full publish can fail in `dsymutil` on some macOS hosts, a pre-existing failure on `master`).
`master` and this branch were built back to back on the same machine with the same toolchain
(macvm, SDK 10.0.403) and the same command:

```
dotnet build Kolvir.Minimal/Kolvir.Minimal.csproj -c Release -r osx-arm64 \
  -p:PublishAot=true -p:TrimmerSingleWarn=false
```

| Measurement | Bytes |
| --- | ---: |
| `Kolvir.Minimal` floor before this change | 4,080,320 |
| After the Metal seam, the tier fallback, the platform default and the `GraphicsPath` diagnostics | 4,080,640 |
| Delta | +320 (+0.008 %) |

The Metal half is a small managed type set over bindings the AppKit backend already references; it
ships no native library. Both builds report **0 IL2xxx/IL3xxx warnings** with
`-p:TrimmerSingleWarn=false`, so nothing here is kept by a trimmer workaround. A physical-Mac
measurement of the same pair before the API simplification gave the same +320 bytes.

## Hardware acceleration (Linux)

`ApplicationOptions.GfxHardwareAcceleration` is the same `bool`, with the same mechanics and
the same `--no-hardware-acceleration` switch. It defaults to **true on Linux**, as it does on
Windows, and the GTK4 backend honours it exactly as the other two do: an application that assigns
it false, or launches with the switch, gets the CPU texture path and nothing else changes.

That default is a decision taken for this platform, not a value left where it was. The reasoning,
the measurement it rests on and what is still unmeasured are in
[the default paragraph](#measured-on-llvmpipe-software-gl-x11) below; the short version is that GTK4
already composites through GL, the CPU path pays a full-surface upload every frame that this one
does not, the measured end-to-end result is ahead or level on both test pages and on both display
servers, and every failure returns to the CPU path with a named reason.

When it is enabled the GTK4 backend tries to build a Skia context on the `Gdk.GLContext` that a
`Gtk.GLArea` hands it, and presents the control tree through that area instead of through the
`Gdk.MemoryTexture` the CPU path uses. Every step is checked and any failure is silent: the window
keeps the CPU frame it was already showing and never fails because acceleration was asked for.

The compatibility question this platform has, and the other two do not, is what GDK gives you.
GTK4's backends ask **EGL** for a **GLES** context, on X11 and on Wayland alike, and SkiaSharp's
Linux `libSkiaSharp.so` assembles its native interface over **GLX**. So `GRGlInterface.Create()`
returns null here - the native library contains no `eglGetProcAddress` at all - and the seam
assembles Skia's **GLES** interface instead, handing it `eglGetProcAddress` as the resolver. That is
the whole bridge: no new native library, no second context, and no dependency on how SkiaSharp was
packaged. It has been run on both display servers, which is where the Wayland rows below come from.

The rules the implementation follows:

- **GL only when the option is true.** `Gtk4PopupWindow` keeps the CPU constructor, so popups and
  menus stay on the CPU path, and `--render` is headless and never reaches this code. The choice is
  made once per window: a window that falls back stays on the CPU for its lifetime.
- **The seam comes up underneath the frame the user is already looking at.** The GL area is the
  bottom child of the view's grid, the CPU picture sits above it and the input drawing area above
  that. GTK still snapshots - and so still renders - an area an opaque sibling covers, so the seam
  proves itself while the CPU frame is what is on screen; only once it has painted does the picture
  hide.
- **Every step is checked.** The area's context, the interface and its `Validate()`, the Skia
  context, the framebuffer the area has bound and the retained surface each can fail. The reason
  names the failing step: `cpu (disabled)`, `cpu (no gl context)`, `cpu (gl area: <gtk message>)`,
  `cpu (no gl interface)`, `cpu (interface invalid)`, `cpu (no gr context)`,
  `cpu (no gl surface: 2560 x 1520)`, `cpu (no gl framebuffer)` or `cpu (gl context lost)`.
- **The framebuffer is read before Skia touches anything.** GTK does not render into the default
  framebuffer: in a GTK4 window the area's framebuffer is an offscreen one, and its id has to come
  from `glGetIntegerv(GL_FRAMEBUFFER_BINDING)`. Reading it after creating a Skia texture surface
  answers with *Skia's* framebuffer instead, and a blit into that leaves the window empty - which is
  exactly what happened the first time this seam was run.
- **Foreign GL state is reset every frame.** GTK, the window system and the previous frame all touch
  the context, and Skia caches the state it set last time. Without `ResetContext` the second frame
  draws with stale bindings and text comes out as solid boxes; with it, every tier paints correctly
  from any frame onwards.
- **The frame lives in a retained offscreen buffer.** The host's `SKRegion` decides what the UI
  invalidated, and that is exactly what is repainted into a persistent RGBA8 GPU surface; present is
  one Skia `Snapshot()` + `DrawImage` onto the area's framebuffer. The retained surface has undefined
  contents when it is created, so the first frame after creation, resize or scale change is painted
  in full.
- **The direct tier is full-frame only.** GTK does not promise the area's framebuffer still holds
  the previous frame, so the tier that paints straight into it paints every frame in full. It is
  chosen for the rest of the window's life if the retained buffer could not be created or a
  snapshot/blit failed, with one full repaint to recover the frame.
- **All GL work is on the GTK thread, in the render signal.** Creation, make-current, paint, blit
  and flush all happen there; nothing is scheduled on a worker and no second context exists.
- **Resize and scale changes recreate the retained buffer, then repaint in full.** The buffer is
  sized in device pixels (`width x scale`), so a 2.00 window is 2560 x 1520 for a 1280 x 760 client,
  and `EnsureSize` sees the new pixel size and forces the full paint.
- **A frame is asked for, not assumed.** A `Gtk.GLArea` renders only when something queues a render
  on it, and queueing from inside the render signal is folded into the pass already running, so an
  invalidation that arrives during a paint is deferred to an idle and retried there.

`Gtk4WindowBackend.GraphicsPath` reports `gl` or `cpu (...)` with the reason, and
`GraphicsRenderer` reports the GL vendor and renderer. The footer and the `BENCH` line name both, so
a screenshot says which path produced its numbers:

```
gfx: gl; renderer: Mesa / llvmpipe (LLVM 20.1.2, 256 bits)
```

Unlike Win32 and AppKit, the GTK4 decision cannot be made while the window is being shown - a GL
context only exists once the area has been allocated and drawn - so `AddGraphicsPathChangedHandler`
runs once it is made, and reads immediately for a window that never asked for acceleration. Set
`KOLVIR_GL_TRACE=1` to print the framebuffer, the retained target, per-frame damage and every
failure to stderr.

### Measuring the GTK4 GPU path by hand

The measured-rendering page (index 54) works exactly as it does on Windows and macOS, and
`--page N --benchmark` starts the same measurement on any page:

1. Make sure no other instance is running first: the demo is `SingleInstance = true`, so a launch
   can be handed to a window that is already up instead of starting a new one - the status line then
   reads "A second launch was handed to this window" and the `BENCH` line you get back describes the
   *other* window's geometry, or none arrives at all. `pgrep -af KolvirDemoApp` before each run.
2. **Keep the window in sight.** Leave it unobscured on the current workspace, not minimised, and the
   session unlocked. The measurement counts frames the host actually painted, and GTK stops painting
   a toplevel that reports itself non-visible - so a covered window starves the count and the run
   stalls with no error at all, on both display servers and both tiers. Full damage stalls exactly
   like partial, and the CPU path exactly like the GPU one: covering the window with an opaque
   fullscreen window from 6 s to 56 s delayed a run that normally finishes at 15 s to 65-68 s, at
   9.1-9.6 fps instead of about 49, and every run completed within seconds of the cover going away.
   A window that gets no frames at all (minimised, another workspace, a locked session) never
   finishes. A `BENCH stalled: ...` line on stderr names that case; see below.
3. Run the demo - host access, screenshots and input injection are in
   [TESTING-RUNBOOK.md](TESTING-RUNBOOK.md). The footer says `gfx: gl` and the renderer string, or
   `gfx: cpu (...)` with the step that failed; rerun with `KOLVIR_GL_TRACE=1` to capture every step.
4. Open **Measured rendering**, choose **Damage: full** or **Damage: partial**, and press
   **Measure 600 frames**. The first 30 frames are discarded.
5. Press again with the other damage mode.
6. Quit, rerun with `--no-hardware-acceleration`, and repeat. That is the CPU half of the A/B.

The `BENCH` line has the same shape as the other two platforms. On software GL, **`fps` is the
number to trust and `p50` is not**: `LastFrameTime` stops before the window system composites the
frame, and on llvmpipe the rasterisation a GPU would do on the way to the screen happens largely
during presentation, outside the measurement. Compare the two paths by achieved frame rate, and read
the paint column as what it is labelled - paint time, present excluded.

A run that stops being painted prints `BENCH stalled: 137/630 frames after 15s (no frames for 10s)`
to stderr instead of hanging silently. It is a watchdog, not an error: it fires while no frame has
completed for ten seconds and repeats at most every thirty, and it never fires on a run that is
making progress. If you see it, bring the window forward and the run continues where it stopped.

On Mesa 26 the log also carries `libEGL warning: failed to get driver name for fd -1`,
`MESA: error: ZINK: failed to choose pdev` and `libEGL warning: egl: failed to create dri2 screen`
before `gfx: gl`. That is Mesa trying Vulkan first and falling back to llvmpipe; it happens before
the seam runs and changes nothing.

#### Measured on llvmpipe (software GL), X11

Ubuntu 24.04.5 under a hypervisor, kernel 7.0, a Microsoft Hyper-V virtual VGA adapter with no GPU,
Mesa 25.2.8 on **llvmpipe (LLVM 20.1.2, 256 bits)**, X11, 1280 x 760 logical client at 2.00 scale
(2560 x 1520 device pixels), 600 frames after 30 warm-up, measured 2026-11-09. CPU rows are
`--no-hardware-acceleration`. **There is no GPU in this machine, so these numbers show the shape of
the change and nothing about hardware performance.** Every number comes from a software rasteriser
that both paths are paying for.

| Mode | Damage | p50 | p95 | max | mean | fps | Painted area |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| GL (page 54, light) | full | 0.889 | 1.399 | 5.200 | 0.954 | 25.5 | 1280 x 760 |
| GL (page 54, light) | partial | 0.066 | 0.114 | 0.284 | 0.074 | 28.6 | 180 x 180 |
| CPU (page 54, light) | full | 4.142 | 5.066 | 8.259 | 4.165 | 21.1 | 1280 x 760 |
| CPU (page 54, light) | partial | 4.112 | 4.886 | 7.421 | 4.126 | 21.5 | 1280 x 760 |
| GL (page 11, heavy) | full | 0.387 | 0.712 | 1.032 | 0.423 | 24.5 | 1280 x 760 |
| CPU (page 11, heavy) | full | 88.933 | 92.146 | 105.381 | 89.295 | 7.5 | 1280 x 760 |

**On X11, page 54 gets about a quarter more frames on llvmpipe: 25.5 fps on the seam against
21.1 fps on the CPU path** (a second run of the same rows on the same host gave 21.8 against 17.5,
so the ratio holds at roughly 1.2-1.25x while the absolute cadence moves by 15 % between runs). Both
paths are capped by software compositing of a 2560 x 1520 area, which is why the frame rate is the
honest measure and the paint column is not.

That quarter is an **X11 number, not a Linux-wide one**. On Wayland the same comparison is a wash -
1.01-1.04x - because the CPU path's full-surface upload is cheaper on that stack and the compositing
cap is higher, so page 54's frame rate stops being about the paint at all. See
[the Wayland rows below](#measured-on-llvmpipe-software-gl-wayland): the light page does not
regress on Wayland, it just stops being the case that acceleration buys frames there, while the
heavy page keeps its 3.7-3.85x on both.

The paint column is paint time only. Page 54's p50 ratio is 4.7x (0.889 against 4.142 ms), and that
figure says precisely one thing: Skia's work on this page is 4.7x cheaper when it runs in the GL
context than when it rasterises into a bitmap. It does not say the window is 4.7x faster, because
the ~40 ms of compositing that follows `LastFrameTime` is common to both paths, and on llvmpipe a
good part of the GL path's rasterisation is inside that common term. Quoting the ratio as a
performance win would be wrong, and this is the only place it appears.

What that quarter does buy comes from a copy the CPU path pays and this one does not: the CPU path
rasterises into a reused BGRA bitmap and then uploads the whole 2560 x 1520 surface to a fresh
`Gdk.MemoryTexture` every frame (see the GTK4 platform section - that copy is deliberate and worth
about 1.3 ms of that path's frame), while the GL path blits a retained surface that never leaves
video memory. So the fraction of the frame budget that is convertible is real but partial, and it
grows as the paint cost grows - it is the whole of page 11's 3.7-3.85x and only a quarter of page 54
on X11, where the upload is a small share of a frame that compositing dominates.

Page 11 is the one page where the paint column does translate, because there the CPU cost is
rasterisation that cannot be deferred: 88.9 ms of it, giving 7.5 fps against 24.5 fps on the seam -
and that holds on Wayland too, at 10.3 fps against 39.7. Page 54's partial row is the other extreme:
0.066 ms of paint under 28.6 fps, which is to say paint has stopped mattering and the frame rate is
set entirely by presentation. The CPU partial row on page 54 reports the whole frame because the
GTK4 CPU path has always rasterized every frame into a reused buffer and cannot present part of it -
that is the pre-existing behaviour, not something this change altered.

#### Measured on llvmpipe (software GL), Wayland

A second VM, Ubuntu 26.04.1, kernel 7.0, Mesa 26.0.8 on **llvmpipe (LLVM 21.1.8, 256 bits)**, a
GNOME Wayland session, 1280 x 723 logical client at 2.00 scale (2560 x 1446 device pixels), 600
frames after 30 warm-up. Its GL path is GDK's **EGL GLES** context exactly as on X11, so the same
`CreateGles` seam drives it: `gfx: gl; renderer: Mesa / llvmpipe (LLVM 21.1.8, 256 bits)`. Same
caveat as above - software rasteriser, no GPU, shape only.

| Row | p50 | fps |
| --- | ---: | ---: |
| GL (page 54, light) | 0.919 | 41.0 |
| CPU (page 54, light) | 3.912 | 39.3 |
| GL (page 54, light, partial) | 0.068 | 49.2 (area 180 x 180) |
| GL (page 11, heavy) | 0.367 | 39.7 |
| CPU (page 11, heavy) | 75.639 | 10.3 |

Page 54 is **1.04x** end to end here and page 11 is **3.85x**; a second paired run of the same rows
gave 40.4 against 39.9 fps and 38.9 against 10.5, which is 1.01x and 3.7x. The partial row on the
CPU path paints the whole frame at about 3.9 ms and 39 fps, as it does on X11, for the reason above.

Read the two tables separately: the client is 1280 x 760 on X11 and 1280 x 723 on Wayland because
that is the size the Wayland toplevel negotiates, so the rows are shapes for the same pages and not
pixel-for-pixel comparisons. What the pair establishes is where the acceleration actually pays.
Page 11 is the same on both stacks - a 3.7-3.85x frame rate against a paint cost that cannot be
deferred anywhere - and page 54 is content-dependent on the compositing stack: a quarter more frames
on X11, a wash on Wayland, never a regression. Neither table says anything about a real GPU.

**The Linux default stays at `true`, and that is a decision made here rather than a value inherited
from `master`.** Nothing measured above proves it on hardware - neither machine has a GPU.
What the evidence does establish, and what the decision rests on:

- GTK4 already composites these windows through GL. The option does not add a GL stack that would
  otherwise be absent; it only decides whether Skia paints into the one GTK is using anyway.
- Both measured comparisons come out ahead or level, never behind: 1.0-1.07x the frame rate on the
  light page (a wash on Wayland rather than a loss) and 3.7-3.85x on the heavy page, on X11 and
  Wayland alike.
- The CPU path pays a full-surface `Gdk.MemoryTexture` upload every frame that this path does not
  pay at all. That asymmetry is structural rather than a property of llvmpipe, and it is what the
  heavy page converts into frames and the light page cannot.
- Every failure is contained and named. Any step of the seam failing drops to the exact CPU path
  that shipped before this change, with a `gfx: cpu (...)` reason, and `--no-hardware-acceleration`
  forces it. A wrong call here costs performance, not correctness - which is verified byte-identical
  below.

Still unmeasured, and worth a real GPU before anyone calls the default proven:

- **Any hardware Linux GPU.** All numbers here are llvmpipe. On a real driver the compositing term
  that caps both paths gets much smaller, so the absolute headroom shrinks; whether the *ratio*
  moves with it is unknown and could go either way.
- **A multisampled framebuffer.** The VM reports 0 samples, so the seam builds a 0-sample
  `GRBackendRenderTarget`. It does read `GL_SAMPLES` and clamp it to `GetMaxSurfaceSampleCount`, so a
  driver that reports 4 takes a path that is written and traced but has never run.
- **Whether a hardware driver returns the GLES interface here.** Both VMs hand out an **EGL**
  `GLES` context, not GLX, on X11 and on Wayland alike, which is why the interface is assembled with
  `CreateGles` over `eglGetProcAddress` instead of `GRGlInterface.Create()`. That is the shape of
  this stack, not a compatibility shim, but it has only been seen on Mesa in VMs.
- **Any Wayland compositor other than GNOME's**, **another X11 window manager**, and a display at a
  scale other than 2.00. Runtime scale change could not be exercised on either machine; see
  [Lifecycle](#lifecycle).
- **The correctness capture on Wayland.** The GL-versus-CPU screenshot comparison below was made on
  X11, where a window can be captured with `ffmpeg -f x11grab`; GNOME's Wayland session offers no
  equivalent from ssh, so the Wayland claim here rests on the seam taking the same code path, on the
  `gfx:` line naming the renderer, and on the frame rates being those of a real composited window.

#### Correctness: GL vs CPU, the demo pages

The demo window was captured on the same X11 display twice at 1280 x 760 @ 2.00, once with
`--no-hardware-acceleration` and once on the GL path, and the two screenshots were compared channel
by channel over the client area:

| Page | Max channel difference | Pixels changed |
| --- | ---: | ---: |
| 1 (stack panel) | 10/255 | 0.83 % |
| 11 (images and layout) | 10/255 | 28.1 % |
| 54 (measured rendering) | 23/255 | 0.98 % |

Page 1's differences are antialiased text edges. Page 11's 28 % is a photograph's resampling: the GPU
and CPU rasterisers filter a scaled bitmap slightly differently, and every pixel of that image moves
by at most 10/255 - the amplified diff is a uniform field of that image, with no structural
difference anywhere else. Page 54 is compared below its live readout, which prints numbers that
change every frame and cannot be compared between two runs. All three are inside the 11-30/255
antialiasing band the Windows and macOS comparisons recorded. The CPU path itself is unchanged:
the same window captured with `--no-hardware-acceleration` on `master` and on this branch is
**byte-identical** over the whole client area (0 pixels differ, page 1, 1280 x 760 @ 2.00), which is
what proves the tier really is the code that was already there.

Measured cost on the `Kolvir.Minimal` fixture (`linux-x64`, NativeAOT, the loose Skia row of section 8 in
[FEATURE-COST-POLICY.md](FEATURE-COST-POLICY.md)):

```
dotnet publish Kolvir.Minimal/Kolvir.Minimal.csproj -c Release -r linux-x64 \
  -p:EmbedSkiaSharpNative=false -p:TrimmerSingleWarn=false
```

| Measurement | Bytes |
| --- | ---: |
| `Kolvir.Minimal` floor before this change | 4,276,144 |
| After the GL seam, its tier fallback and the `GraphicsPath` diagnostics | 4,338,944 |
| Delta | +62,800 (+1.47 %) |

The GL half is managed code plus two P/Invoke declarations (`eglGetProcAddress` and `dlsym`); it
ships no native library, and `libSkiaSharp.so` already contains the GL backend. The delta is larger
than the other two backends' because the managed SkiaSharp GPU surface API is new to this assembly.
Both builds report **0 IL2xxx/IL3xxx warnings** with `-p:TrimmerSingleWarn=false`.

#### Lifecycle

Exercised on the X11 VM, each with a screenshot to confirm the frame survived: window resize,
minimise and restore, occluded-then-uncovered, and close-then-reopen. The CPU path was captured on
`master` and on this branch for the last of those and compares byte-identical, so a reopened window
starts on the same code it always did.

Exercised on the Wayland VM: open, resize, minimise and restore, occluded-then-uncovered, and a full
measurement on every row. The one difference worth knowing is that a Wayland (and, it turns out, an
X11) toplevel that is not visible is not painted at all, so a run whose window is covered stalls
rather than slowing down - which is why the harness now says `BENCH stalled: ...` instead of going
quiet. Resize and scale both arrive at the same place: the retained buffer is sized in device pixels
and `EnsureSize` forces a full repaint when that size changes.

Not exercised: a **runtime scale-factor change** (both VMs report 2.00 and offer no way to change it
over ssh), **suspend/resume of the GL context**, and a **non-GNOME compositor**. What covers them in
the code: `EnsureSize` for the scale case, and a lost context drops to the CPU picture with
`cpu (gl context lost)` rather than painting into nothing.

## Writing a backend

A backend is two interfaces plus one registration hook. `IApplicationBackend` owns the platform
loop and the platform-wide services; `IWindowBackend` owns one native window and the
`UiHost` that paints into it. `Kolvir.Backend.Win32`, `Kolvir.Backend.AppKit`,
`Kolvir.Backend.Gtk4` and `Kolvir.Backend.Headless` are the four worked examples.

1. `Initialize` gets the `Application`; `Run` enters the platform loop and returns its exit
   code; `Quit` leaves it; `Invoke` marshals a callback onto the UI thread;
   `TryClaimSingleInstance` returns false when another process already owns the instance key, and
   runs the `AddSecondInstanceHandler` handlers when a later launch is handed over.
2. `CreateWindow` returns an `IWindowBackend`. It creates the native window and an `SKSurface`
   (or a bitmap-backed surface) of its client size, and it creates and owns the `UiHost`.
3. On resize: `Host.SetClientSize(width / scale, height / scale)`. On paint: `canvas.Scale(scale)`
   then `Host.Paint(canvas)`, which paints everything unless the backend has opted into
   `PartialUpdates`. A backend that keeps its surface between frames sets `PartialUpdates = true`,
   uses the returned area to present just what was painted, and copies any rectangle the platform
   reports stale out of its retained surface rather than adding it to the paint - the platform's
   update rectangle is a box around everything it thinks changed, which is usually wider than what
   actually did. A surface that was just reallocated holds garbage, so the first paint into it has
   to be `Host.Paint(canvas, full: true)`.
4. Forward pointer, key and text events into `Host.ProcessMouseMove/Down/Up/Leave`,
   `ProcessKeyDown/Up`, `ProcessTextInput` and `ProcessContextMenu`, dividing coordinates by the
   scale factor. There is no tick to forward: a control that needs time runs its own timer.
5. Run the `Shown`, `CloseRequested`, `Closed`, `Activated`, `Deactivated`, `ClientSizeChanged`,
   `ScaleChanged` and `StateChanged` handlers; the model forwards each to the matching
   `Window` method.
6. `RegisterServices` adds the platform services to MEDI's `IServiceCollection` - the clipboard, the
   system theme, the dialog service and the context menu presenter. Doing it through one hook
   instead of a `Create*` member per service keeps adding a service from breaking every backend, and
   keeps them lazy, so the headless backend never constructs a clipboard it will not use.
7. Implement `GetScreens`, reporting each display's refresh rate when the platform can supply one -
   it is what a marquee progress bar animates at - and `Shutdown` releases whatever `Initialize`
   took.
8. Implement `CreateTimer`, returning an `ITimerBackend` that calls the callback it is handed
   once per interval. `new ThreadPoolTimerBackend(tick)` is the whole implementation for a platform
   whose own timer is fine-grained; the Win32 backend answers an interval under the 15.625 ms system
   timer period with a high-resolution waitable timer instead, because a pool timer there cannot
   fire any faster than the period however small the interval is. It is a `Create*` member rather
   than a registered service for the same reason `CreateWindow` is: a timer is an object the backend
   owns for as long as a control needs it, not a singleton the container holds. Dropping overlapping
   ticks and moving the callback to the UI thread stay in `Timer`, so a backend only has to get
   the wait right.
9. Optionally assign an `IPopupService` to `PopupService.Current` so popups become real
   windows instead of overlays inside the owning window. It is a static rather than a DI service
   because a popup belongs to one owner host rather than to the application, and because leaving it
   null is a valid implementation: the overlay fallback then draws every popup inside the window.

Two contract details are easy to miss. `ShowMessage`, `OpenFile`, `SaveFile`, `PickFolder`,
`PickFont` and `ShowDialog` are **synchronous** but re-enter the platform loop, so a backend has to tolerate a
nested loop and keep dispatching `Invoke` callbacks inside it. And `MenuBar` is per window: on macOS
a backend draws the bar in the window like any other platform, and additionally mirrors the active
window's bar into the single global macOS menu bar, because AppKit requires one to exist and the
standard Edit key equivalents come from being in it.

## NativeAOT

`Kolvir` sets `IsAotCompatible` and uses no reflection, so it publishes with NativeAOT.
Verified locally with the Win32 build of the demo application itself (no Windows Forms, no
System.Drawing). The `FolderProfile` publish profile
(`Kolvir.Demo.App/Properties/PublishProfiles/FolderProfile.pubxml`) is preconfigured for AOT +
trimming, so publishing is just:

```
dotnet publish Kolvir.Demo.App\Kolvir.Demo.App.csproj -p:PublishProfile=FolderProfile
```

or right-click **Publish** in Visual Studio. The result is a **single** self-contained
`KolvirDemoApp.exe` (9,270,272 bytes, 8.84 MiB, of which 5.2 MiB is the compressed Skia native
binary) and an optional `KolvirDemoApp.pdb` for debugging. Nothing else is needed to run it - the
AOT build opens the real window and renders the demo.

On Windows this needs the MSVC linker (`link.exe`). The profile switches to
`IlcUseEnvironmentalTools` automatically once a VC environment is initialized, so the most
reliable way to publish is from a *Developer PowerShell/Command Prompt for VS* (or a CI step that
calls `vcvars64.bat` first):

```
call "C:\Program Files\Microsoft Visual Studio\18\Enterprise\VC\Auxiliary\Build\vcvars64.bat"
dotnet publish Kolvir.Demo.App\Kolvir.Demo.App.csproj -p:PublishProfile=FolderProfile
```

From a plain shell the SDK locates the toolset itself with `vswhere.exe`; if that fails (for
example a `vswhere.exe` error leaking into the link step), publish from a Developer prompt instead.

Linux publishes the same way, and does not need a `clang` or `lld` installation: the ILCompiler
package carries its own toolchain.

```
dotnet publish Kolvir.Demo.App/Kolvir.Demo.App.csproj -c Release -r linux-x64 -p:PublishAot=true
```

The result is a **single** `publish/KolvirDemoApp` (9,537,712 bytes, 9.10 MiB, a stripped ELF
executable) plus an optional `KolvirDemoApp.dbg`. It is genuinely standalone - copy just that one
file anywhere and it runs, window and all.

`Kolvir.Minimal` is the measurement fixture: one window, one label, no features. It publishes to
8,527,872 bytes (8.13 MiB) on Windows and 8,867,296 (8.46 MiB) on Linux, so everything the demo
demonstrates costs about 0.7 MB on Windows and 0.6 MB on Linux over the floor an application starts
from. [FEATURE-COST-POLICY.md](FEATURE-COST-POLICY.md) section 8 records the baseline that every feature is
measured against.

SkiaSharp only ships a dynamic `libSkiaSharp.dll`/`libSkiaSharp.so`, so a native executable cannot
simply link it. When `PublishAot` is on, Kolvir's own build assets (`buildTransitive/` in the
package, which `Directory.Build.targets` imports for this repository's projects) gzip the
RID-specific native library and embed it in the application as the manifest resource
`Kolvir.Native.libSkiaSharp.dll.gz` (or `...libSkiaSharp.so.gz`); `SkiaNativeLoader`, in the
library, extracts it on first use to `%LOCALAPPDATA%\Kolvir\native\<version>-<size>\libSkiaSharp.dll`
(`~/.local/share/Kolvir/native/<version>-<size>/libSkiaSharp.so` on Linux, honouring
`XDG_DATA_HOME`), which is cached so later runs just load it, and redirects SkiaSharp's P/Invoke at it
before the first call into Skia. The redirect is not optional on Linux: `libSkiaSharp.so` carries no
`SONAME`, so a library loaded by full path is not what `dlopen("libSkiaSharp.so")` resolves to. The
resource belongs to the application's assembly, because the copy to embed is the one the
application's build resolved for its platform. `KolvirBackend.CreateDefault()` names it for you: it
calls `SkiaNativeLoader.EnsureLoaded(Assembly.GetEntryAssembly())` before it returns the backend, so
an application's `Main` never mentions SkiaSharp at all. Calling `EnsureLoaded` yourself as well is
harmless; it is idempotent. A regular (non-AOT) build is untouched:
the native library is resolved the usual way (from the package cache, or copied next to the
executable for a RID-specific build) and the loader does nothing. If you would rather ship a loose
native library, set `EmbedSkiaSharpNative` to `false` - the publish then emits the executable and
`libSkiaSharp.dll`/`libSkiaSharp.so` side by side again.

The Skia embedding targets run for the Windows and Linux target frameworks - the two that are
published with AOT. macOS is deliberately left out: an `.app` bundle already carries its native
libraries as loose files, so there is nothing to fold into a single executable, and the bundle *is*
the deliverable. See [Bundle size](#bundle-size).

### The SVG feature's second native library

`Kolvir.Feature.Svg` renders `<text>` through Svg.Skia, which shapes it with **HarfBuzzSharp** - a
native library of its own, separate from SkiaSharp's. A publish of an application that shows an SVG
used to drop `libHarfBuzzSharp.dll`/`.so`/`.dylib` beside the executable even though `libSkiaSharp`
had stopped being loose. The feature now carries build assets of its own (`buildTransitive/` in its
package, imported by `Directory.Build.targets` here) that do exactly what the core ones do: find the
native the build resolved, gzip it, embed it in the application as
`Kolvir.Feature.Svg.Native.libHarfBuzzSharp.dll.gz` (or `.so`/`.dylib`), and keep it out of the
publish output. `HarfBuzzNativeLoader`, in the feature assembly, extracts it on first use to
`%LOCALAPPDATA%\Kolvir\native\harfbuzz\<version>-<size>\` (`~/.local/share/Kolvir/native/harfbuzz`
on Linux, `~/Library/Application Support/Kolvir/native/harfbuzz` on macOS) and redirects
HarfBuzzSharp's P/Invoke at it.

Three things about it are worth knowing rather than discovering:

- **It needs no call.** The loader runs from a module initializer, so an application that calls
  `AddSvgImages()` has the resolver in place before the first shaped `<text>` without adding a line
  to `Main`. The guarantee is about the assembly, not about `AddSvgImages()` specifically: a
  framework-dependent run runs the initializer the first time the feature is touched, so an
  application that only references the package never loads it and pays nothing; a NativeAOT publish
  either trims the assembly away when nothing uses it - measured: no cache directory appears and
  nothing runs - or runs its initializer during startup, before `Main` - also measured: the cache
  exists by the first line of `Main`. Either way the cost lands only on applications that use the
  feature.
- **The resolver belongs to HarfBuzzSharp's assembly, not SkiaSharp's.** Both loaders register
  against different assemblies, which is why they coexist. If an application has already registered
  its own HarfBuzz resolver, `NativeLibrary.SetDllImportResolver` refuses a second one ("A resolver
  is already set for the assembly"); the loader treats that as somebody else's decision, leaves the
  default probing alone and does not fail startup. The library is extracted either way, so that
  resolver can point at it if it wants to. Under NativeAOT that cannot happen from `Main` - the
  initializer has already run - so this is a framework-dependent-build possibility, and the guard
  exists for it.
- **`EmbedHarfBuzzSharpNative`** is the opt-out, beside `EmbedSkiaSharpNative` and with the same
  default: unset means embedded in an AOT publish. Set it to `false` and the publish is the
  executable plus a loose `libHarfBuzzSharp.*` again.

A Windows or Linux `dotnet publish -c Release -r <rid> -p:PublishAot=true` now emits **the
executable and its own symbol file, and nothing else** - no `libSkiaSharp.*` and no
`libHarfBuzzSharp.*`. Measured on the demo app:

| RID | executable before | executable after | loose native removed |
| --- | --- | --- | --- |
| win-x64 | 22,616,576 | 23,661,568 (+1,044,992) | 2,065,248 |
| linux-x64 | 23,056,880 | 24,294,000 (+1,237,120) | 3,059,496 |

macOS is excluded here too, and this one was measured rather than copied. A Kolvir macOS
application publishes as a `net10.0-macos` **`.app` bundle** even with `PublishAot` on, and its AOT
executable records the dylib as a *load-time* dependency: `otool -L` lists
`@executable_path/../../Contents/MonoBundle/libHarfBuzzSharp.dylib`, so deleting the file stops dyld
before any managed code runs and a resolver could never help. A plain `net10.0` Mach-O AOT build is
a different shape, and it *does* work - a probe published that way extracted the dylib to
`~/Library/Application Support/Kolvir/native/harfbuzz/` and shaped text with it, which is why the
exclusion is about the bundle and not about what macOS allows. The demo's macOS bundle is unchanged
by this: executable 15,790,224 -> 15,790,256 bytes, HarfBuzz still loose inside the bundle.

## Running the application

Work that has to run on a testing host - this machine, one of the Linux VMs, the Apple silicon Macs,
CI - starts with [TESTING-RUNBOOK.md](TESTING-RUNBOOK.md): which host to use and when, its OS, display
server and display size, how to take a screenshot of it, and how to inject mouse and keyboard input,
all of which differ host by host. Its recipes were run rather than transcribed, and where a route is
known not to work it says so.

```
dotnet run --project Kolvir.Demo.App
```

`Program.cs` is the whole program: it picks the backend for the platform this build is for, describes
one `Window` around `new DemoUi().Root`, and runs the `Application`. Everything else lives
behind the backend - `Win32WindowBackend` creates the window with raw `user32` P/Invoke, owns a 32-bit
DIB section that Skia draws into (or a WGL-backed Skia surface when hardware acceleration is on),
blits it on `WM_PAINT` (or swaps the GL surface), and forwards mouse/keyboard messages to the
`UiHost`.

The sample is organised as a navigation list down the left and a stage on the right that shows one
demo at a time, so adding a demo is a row in `DemoUi.Demos` rather than another crowded card. Every
page is built once and kept, and switching pages re-parents one control into the stage.

To render the same sample UI headlessly (useful for visual checks in CI):

```
KolvirDemoApp.exe --render out.png 1280 820
KolvirDemoApp.exe --render out.png 1280 820 --page 3
```

Add `--light` or `--dark` to either mode to force an appearance instead of using the system one; the
header's **Switch appearance** button flips it at runtime. `--page` picks a stage page by index in
both modes, which is what makes a single page renderable on its own.
`Kolvir.Tests`' `UndoHistoryTests` covers the history headlessly instead: it drives real text boxes
through the host's own input entry points with a clock the test moves by hand, and one failed
assertion names the coalescing rule that broke.
`--undo-focus` puts the caret in the undo page's field, the way `--span-menu`
opens a menu a script cannot click. `--classic-menus` forces the old
`TrackPopupMenuEx` context menus. The sample UI is laid out for a 1280 x 820 client area at 96 DPI
points and the backend scales the Skia canvas by `GetDpiForWindow(hwnd) / 96`, sizing its client
area to `1280 x 820` multiplied by that scale (clamped to the monitor work area) so the demo is
never cropped on a high-DPI monitor. The **Selection and clipboard** page is the one to try:
drag down from its label, over the whole-control button and into the text box, then right-click
the selection. The two **Performance** pages are the ones to watch scroll: **Performance: list box**
loads 1 to 100,000 rows into a list, **Performance: controls** stacks 1 to 100,000 buttons in a
stack panel, and both report what the last frame cost to draw them.

### Measuring the Win32 GPU path by hand

The footer always names the live path - `gfx: gl - <vendor> / <renderer>` or
`gfx: cpu (<step>)` - and the same line is printed at startup. The last demo page,
**Measured rendering** (index 54), turns that into numbers:

1. Run the demo on the machine being measured. Check the footer says `gfx: gl` and that the
   renderer is the GPU you expect. On a fallback it says `gfx: cpu (...)` with the step that
   failed, for example `cpu (no gl interface; gdi: no wgl context (error 2000))`; rerun with
   `KOLVIR_GL_TRACE=1` to capture every probe attempt for a bug report.
   That is the fallback doing its job rather than a failure to record.
2. Open **Measured rendering**, choose **Damage: full** or **Damage: partial**, and press
   **Measure 600 frames**. The page is invalidated from one 15 ms timer (about 66 Hz, which is what
   makes the Win32 backend use its high-resolution timer instead of rounding to 31.25 ms); the first 30 frames are
   discarded so the lazily uploaded glyph atlas and shaders do not poison the mean.
3. Press again with the other damage mode if the partial/full comparison is what you want.
4. Quit, rerun with `--no-hardware-acceleration`, and repeat. That is the CPU half of the A/B.

`--page 54 --benchmark` starts the same measurement from the command line, so the console line
can be captured without a pointer; with any other page it does nothing.

Every run writes one machine-readable line to stderr:

```
BENCH mode="gl" renderer="Intel Corporation / Intel(R) Arc(TM) A770 Graphics" size=2560x1640 scale=2.00 damage=full frames=600 warmup=30 p50=0.512 p95=0.903 max=1.804 mean=0.589 fps=59.9 area=2560x1640
```

`mode` is `gl` or `cpu (...)`, `renderer` is the GL vendor and device (`none` on the CPU path),
`size` is the client size, `scale` is the DPI scale, `area` is the last painted area in pixels,
and the timings are milliseconds. The line is written with the invariant culture, so a desktop
with a comma decimal separator does not change it. `fps` is the wall-clock cadence over the
measured frames, so it includes the GPU and present cost; it can sit well below the timer rate on a
software rasterizer even while the CPU paint time (`p50`/`mean`) stays small.

The readout is driven by the same timer and is included in the frame it reports; it repaints its
own label about four times a second, so a completely idle window has one small repaint roughly
every 250 ms. In **partial** mode the measured area is the 180 x 180 swatch, which does not
overlap the readout; the caveat is the timer itself, not the label's area. The frame time is the
host's paint (`UiHost.LastFrameTime`), so a GL run's `SwapBuffers` happens after the number
and is not included.

The timer starts only when the page is displayed and stops when it is left, which is the same
marquee rule the progress bar follows. An idle application with a different page open runs no
timers from this page.

A software rasterizer is useful for correctness and meaningless for timing. Mesa's
`opengl32.dll` beside the executable (the Mesa build the development VM uses) makes the whole GL
chain run on the Microsoft Basic Render Driver, which is enough to prove pixels and fallback but
produces no statement about GPU performance. Real numbers need a machine with a vendor ICD; the
demo is instrumented so those can be captured by hand and pasted back with no ambiguity.

#### Measured on real hardware

NVIDIA GeForce RTX 5090 with the vendor ICD, Windows, 1280 x 820 logical client at 2.00 scale,
600 frames after 30 warm-up frames, measured 2026-11-09. The CPU rows are
`--no-hardware-acceleration`. Times in milliseconds; this is the damage-aware build.

| Mode | Damage | p50 | p95 | max | mean | Painted area |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| GL | full | 0.487 | 0.582 | 3.446 | 0.502 | 1280 x 820 |
| GL | partial | 0.047 | 0.056 | 0.159 | 0.046 | 180 x 180 |
| CPU | full | 2.917 | 3.432 | 6.416 | 2.919 | 1280 x 820 |
| CPU | partial | 0.177 | 0.222 | 0.548 | 0.183 | 180 x 180 |

A full frame is **6.0x cheaper under GL** (0.487 vs 2.917 ms p50). A partial frame is **3.8x
cheaper under GL** (0.047 vs 0.177). That reverses the first revision, where whole-surface present
made the GL partial row 2.3x *more* expensive than the CPU one (0.416 vs 0.178). The retained buffer
costs **+0.070 ms on a full frame** (0.417 -> 0.487) and takes **8.9x off a partial frame** (0.416
-> 0.047).

Both hardware runs are timer-capped at 64.9-65.0 fps, so at this workload the win is headroom, not
frame rate. The frame-rate difference appears when the workload approaches the frame budget: a full
frame now leaves 6x more of it.

#### What the frame number excludes

`p50`/`p95`/`max`/`mean` are the host's paint (`UiHost.LastFrameTime`). A GL frame's
offscreen-to-window blit and `wglSwapBuffers` happen after that paint and are **not** in the number.
The blit was timed separately at **0.054 ms** on the software VM, but it has **not** been timed on
this hardware, so the true hardware partial frame is 0.047 ms plus an unmeasured blit - still
comfortably under the CPU path's 0.177, but 0.047 is not the whole frame. A full GL frame likewise
carries that blit on top of 0.487.

#### Development VM (software rasterizer)

The retained-buffer path is also measured here with Mesa on the Microsoft Basic Render Driver,
1280 x 820 at 2.00 scale, 600 frames after 30 warm-up. **These are software-rasterizer numbers: they
show the shape of the change, not hardware speed.** `p50`/`p95`/`max`/`mean` are the host's paint, as
everywhere else, so the GL rows exclude the present blit. The blit was timed separately by
snapshotting and blitting an unchanged 2560 x 1640 buffer: about **0.054 ms per frame** at steady
state on this VM, which is the floor the GL partial row adds.

| Mode | Damage | p50 | p95 | max | mean | Painted area |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| GL | full | 0.598 | 1.110 | 9.743 | 0.760 | 1280 x 820 |
| GL | partial | 0.068 | 0.112 | 0.193 | 0.074 | 180 x 180 |
| CPU | full | 3.157 | 4.382 | 8.105 | 3.253 | 1280 x 820 |
| CPU | partial | 0.198 | 0.240 | 0.704 | 0.205 | 180 x 180 |

A partial GL frame now paints the 180 x 180 swatch instead of the whole window, and the full-buffer
blit adds about 0.05 ms, so on this software device the partial GL total is roughly 0.12 ms against
the CPU path's 0.198 ms. A full GL frame is about 0.65 ms against 3.157 ms. Use these for the shape;
the hardware table above is the one to quote.

### On Linux (GTK4)

The Linux build of `Kolvir.Demo.App` uses the
[GirCore](https://github.com/gircore/gircore) GTK4 bindings, so the build needs the GTK4 runtime and
development headers, plus a font backend for SkiaSharp:

```
sudo apt install libgtk-4-1 libgtk-4-dev libfontconfig1
```

The Skia native library comes from the `SkiaSharp.NativeAssets.Linux` package, so nothing has to be
installed for it. Then:

```
dotnet run --project Kolvir.Demo.App
```

The window is a plain `Gtk.ApplicationWindow` whose child is a `Gtk.Box` holding the view: a
`Gtk.Grid` whose single cell carries the surface (`Gtk.Picture` on the CPU path, `Gtk.GLArea` on the
accelerated one), a transparent `Gtk.DrawingArea` on top of it, and, once the seam has painted, no
picture at all. Skia paints into a premultiplied BGRA bitmap (`SKColorType.Bgra8888` with
`SKAlphaType.Premul`), which is wrapped in a `Gdk.MemoryTexture` and handed to the picture; the
drawing area is what reports the widget's size and scale factor and what receives input. The
accelerated path is described in [Hardware acceleration (Linux)](#hardware-acceleration-linux): the
same host paints into a retained GPU surface and blits it onto the area's framebuffer. The frame is not blitted with Cairo because GTK4 gives a
drawing area a Cairo *recording* surface - there are no pixels to write into - so a bitmap can only
reach the screen as a Cairo source, and Cairo answers a mutable source with a full size snapshot.
GTK keeps the render nodes those snapshots end up in, so every repaint leaked one frame buffer.
`GLib.Bytes.New` copies the frame into memory GLib owns, so a texture stays valid however the Skia
surface behind it is resized; aliasing the pixels with `NewStatic` instead faults inside the GL
driver on the first resize, because the renderer still holds the previous frame's node. The copy is
the price of that - about 1.3 ms of a 4.4 ms frame at 2560x1452 on llvmpipe.

Everything else mirrors the Win32 backend: `Gtk.GestureClick`, `Gtk.EventControllerMotion` and
`Gtk.EventControllerKey` feed `UiHost.Process*`, and the system appearance is read from
`org.gnome.desktop.interface color-scheme` (falling back to `GTK_THEME` and
`GtkSettings.GtkApplicationPreferDarkTheme`). Context menus are real `Gtk.Popover`s, and the desktop
UI font (`GtkSettings.GtkFontName`) is handed to the library through `Theme.FontFamily`, so the
controls look native on GNOME.

The same headless switches work, and they run before any GTK type is loaded, so they need no display
at all - no Xvfb, and they work over ssh:

```
dotnet KolvirDemoApp.dll --render out.png 1280 820 --dark
```

That ordering is load-bearing rather than tidy. `Gtk.Module.Initialize()` opens the display and
terminates the process with exit code 1 when there is none, and it does so before
`Gtk.Functions.InitCheck()` could report the failure, so anything GTK-dependent in the render path
makes it unusable on a headless machine. The renderer therefore reads neither `GtkSettings` nor
GSettings: it takes the palette from `--dark` / `--light` (light by default) and SkiaSharp's default
typeface instead of the desktop UI font. That also makes CI screenshots deterministic, at the cost of
the render not using the same font as the window does.

Set `KOLVIR_DEBUG=1` to trace host work (frames, buffer reallocation) to stderr.

### On macOS (AppKit)

The macOS build of `Kolvir.Demo.App` is a native macOS application. It targets `net10.0-macos` and
uses Apple's AppKit bindings, so the window, its title bar, the menu bar, the context menus and the
clipboard are all the real thing; only the contents of the window are drawn by the library.

That target framework is why the macOS build has to happen on a Mac: it needs the macOS workload and
the Xcode tools that bundle and sign the `.app`. `Program.cs` selects it with
`$([MSBuild]::IsOSPlatform('osx'))`, so the project is part of `Kolvir.slnx` on every platform
and simply resolves to a different target framework on each. Build it on a Mac, or let the
`macOS build` GitHub Actions workflow do it - that workflow runs on pull requests against `master`
and on demand (`workflow_dispatch`), not on every push, so a branch that is still being worked on
is expected to be built locally:

```
dotnet workload install macos
dotnet build Kolvir.Demo.App/Kolvir.Demo.App.csproj
open Kolvir.Demo.App/bin/Debug/net10.0-macos/osx-arm64/KolvirDemoApp.app
```

The macOS workload only accepts an Xcode whose major.minor version matches the one its bindings were
generated against, and the error it raises (`requires Xcode 27.1`) is easy to mistake for a real
problem. `Kolvir.Demo.App.csproj` therefore sets `ValidateXcodeVersion` to `false` for
`net10.0-macos`: a newer Xcode builds and renders this app unchanged, so the exact-match rule only
ever gets in the way on a workstation that has been kept up to date. The guard is a property, so it
can be turned back on per build:

```
dotnet build Kolvir.Demo.App/Kolvir.Demo.App.csproj -p:ValidateXcodeVersion=true
```

Nothing here compiles native code, so a mismatched Xcode cannot actually break the build. The
`macOS build` workflow still selects the Xcode the workload expects (`/Applications/Xcode_27.1.app`
on the `xcode-27` image) rather than relying on either setting.

The bundle is produced by the build itself; its identity lives in `Kolvir.Demo.App/Info.plist`. It is
signed ad-hoc, so a copy downloaded from CI needs `xattr -dr com.apple.quarantine KolvirDemoApp.app`
before it will open. The same headless switches work, and they run before AppKit is initialised, so
they work over ssh too:

```
KolvirDemoApp.app/Contents/MacOS/KolvirDemoApp --render out.png 1280 820 --dark
```

`AppKitSkiaView` is a plain `NSView`, flipped so y grows downwards like every other backend, and it is
layer-backed: the view returns true from `WantsUpdateLayer` and does its work in `UpdateLayer`, so a
frame never goes through a CoreGraphics context of its own. Skia paints into an `SKBitmap` of
premultiplied BGRA pixels, one `CGDataProvider` wraps those pixels where they are, and each frame
builds a `CGImage` over that provider and hands it to the view's `CALayer` as its `Contents`, with
`ContentsGravity` set to `Resize` and `ContentsScale` set to the backing scale factor. A frame
costs no copy and allocates nothing but the image itself. The provider is built with the
`Action<IntPtr>` overload on purpose: the `(IntPtr, int, bool)` overload reads its `bool` as *owns
the buffer* and, when it is false, copies the whole block before wrapping it, which on a Retina
window is a 16 MB `memcpy` per frame. The `CGImage` is rebuilt for every frame, because
CoreGraphics treats an image as immutable and keeps whatever it decoded out of the provider's
memory: one cached image would pin the window to the contents of the very first frame, which is
drawn before the control tree exists. Because the view is flipped, its layer reports
`GeometryFlipped` and `ContentsAreFlipped`; Core Animation compensates when it composites the layer,
so the image shows the right way up. Rendering that layer on its own with `RenderInContext`
*does* mirror it, so to capture what the screen shows, render the window's root layer - it is the
unflipped one - and the result matches the `--render` image. There is no SkiaSharp view for AppKit
in SkiaSharp 4.x, which is why the view draws itself.

Being layer backed costs one thing that a `DrawRect` view gets for free. AppKit does not mark a
layer backed view dirty when its bounds change: the default is to leave the layer showing what it
already has and let the compositor stretch it to the new size. The symptom is unmistakable - a drag
shows the frame from before the drag started, scaled, for the whole length of the drag, and the
window only paints at its new size once the mouse is released. `NeedsDisplayOnBoundsChange` would be
the documented switch for this, but the .NET binding does not surface it, so the view overrides
`SetFrameSize` and asks for a frame whenever the size actually changed. That is the difference
between one frame for a two hundred step drag and one frame per step.

The buffer is made a
quarter again as large as the window needs, so a drag across the screen reallocates it once instead
of once per frame - each of those was a multi-megabyte native allocation that the garbage collector
never sees - and it is only given back once the window has held a size that needs under half its
area for three quarters of a second. Waiting for the window to hold still is what makes the smaller
headroom affordable: an in-and-out drag would otherwise be one reallocation per step, and every one
of those leaves a freed multi-megabyte block in the allocator's large zone, which is where the
process's memory high-water mark came from. Because the
buffer outgrows the window, each frame clips the canvas to the window before painting; the clip is
applied *after* the scale, in the same logical units the controls paint in, or it would only cover
the top left `1/scale` of the view. Mouse and
keyboard events are forwarded to `UiHost.Process*` from the `NSResponder` overrides, a 250 ms
`NSTimer` drains the UI-thread queue and doubles as the poll for a system appearance change
(`NSAppearance.FindBestMatch`), and a tracking area keeps `MouseMoved`/`MouseEntered`/`MouseExited`
coming so hover states and cursors work.

Measuring that memory needs `vmmap -summary <pid>` and its "Physical footprint" line, not `ps`:
resident size counts over a gigabyte of shared library pages that the process never dirtied, and it
reads about twice the real number. An idle window of 1280x820 on a Retina screen sits near 128 MB,
of which roughly a third is the .NET runtime, a quarter is the two IOSurfaces the window server
keeps for the window, and the rest is the drawing buffer and Skia. While frames are actually
flowing - a drag, a blinking caret - the footprint rises by another ~90 MB of window server
surfaces, and it falls back within half a minute of the window going quiet, so a spike there is
the compositor pipelining updates rather than a leak.

A live resize is the worst case, because it is the only time frames arrive as fast as the machine
can produce them. Driving the window through 200 resizes between 720x520 and 1560x980 points on a
2x screen, the peak footprint is 212 MB. Getting there took two fixes. The first was the buffer's
1.25x headroom and its settle-gated shrink: before them the same drag peaked at 1.0 GB, because the
old code reallocated on nearly every frame and the freed multi-megabyte blocks piled up faster than
the allocator could return them.

The second was the presentation. Blitting the frame's `CGImage` into the view's own CoreGraphics
context - `CGContext.DrawImage` from `DrawRect`, which is the obvious thing to write - made
CoreGraphics hold a full-window backing store per frame in flight, and at near-full-screen on a 2x
screen that is tens of megabytes a frame: the same drag peaked at 1.8 GB. Four runs of the same
drag isolated it. Returning before the `CGImage` existed peaked at 228 MB; building the image but
not drawing it, 224 MB; drawing it, 1798 MB; and drawing it without the flipped CTM, 1858 MB. So
the image is free, the flip is irrelevant, and the blit is the entire cost. Handing the same image
to the layer instead peaked at 150 MB in that harness, against 123 MB for a window that paints
nothing but a solid colour. Two rules follow from it: never `DrawImage` a window-sized image into a
view's context during a live resize, and when the layer stops showing the frame it was given, check
that `ReleaseBuffer` cleared `Contents` before freeing the pixels - a layer holding an image over
freed memory draws whatever the allocator left there.

macOS reports accelerators through `Command` rather than `Control`, so `AppKitMenuBar` gives Cut, Copy,
Paste and Select All real menu items whose `Cmd` key equivalents feed the same editing commands into
the host. `AppKitKeyMap` deliberately does *not* map `Command` onto `Modifiers.Control`: a `Cmd`
combination that the menu bar did not claim is swallowed instead of typing its letter, while
`Control` combinations reach the library as shortcuts. The system UI font (`SF Pro Text`) is handed
to the library through `Theme.FontFamily`.

Set `KOLVIR_DEBUG=1` to trace frames and buffer reallocation to stderr. It also prints a `stats:`
line every five seconds - frame rate, frames drawn, buffers allocated, managed heap, total managed
allocation and resident memory - which is how a runaway repaint (a high frame rate with idle input)
or a per-frame retention (resident memory climbing while the frame rate stays low) shows itself.
The line prints resident memory, which is fine for spotting a trend but reads about twice the real
figure; take the absolute number from `vmmap -summary` (`Process.PrivateMemorySize64` is always zero
on macOS). If a
frame cannot be produced at all, the view paints the reason over the whole window instead of
leaving it blank, so a broken setup reports itself on screen rather than only on a console.

### Bundle size

Only `dotnet publish` runs the trimmer: a plain `dotnet build` leaves a fully untrimmed bundle, and
`LinkMode` decides how much a publish takes away. All four were measured (`osx-arm64`, apparent size
on disk) and rendered:

| bundle | size | files | dlls | render |
| --- | --- | --- | --- | --- |
| `dotnet build` (nothing trimmed) | 137.2 MB | 197 | 177 | reference |
| `publish -p:LinkMode=None -p:TrimMode=copy` | 137.8 MB | 197 | 177 | pixel identical |
| `publish -p:LinkMode=SdkOnly` (the default) | 35.0 MB | 42 | 22 | pixel identical |
| `publish -p:LinkMode=Full` | 35.4 MB | 39 | 19 | pixel identical |

So publishing with the default settings is worth **102 MB** and changes nothing on screen.
`LinkMode=None` on its own does *not* switch the trimmer off; it also needs `TrimMode=copy`.

`LinkMode=Full` is not worth turning on. It only drops `System.Runtime`,
`System.Runtime.InteropServices` and `System.Threading` (the facade assemblies) and then rewrites
`Microsoft.macOS.dll` 473 KB *larger*, so the bundle ends up 0.4 MB bigger than the default for
exactly the same pixels. The project leaves `LinkMode` alone.

Publishing therefore needs no extra switches:

```
dotnet publish Kolvir.Demo.App/Kolvir.Demo.App.csproj -c Release -r osx-arm64 -p:CreatePackage=false
open Kolvir.Demo.App/bin/Release/net10.0-macos/osx-arm64/KolvirDemoApp.app
```

Two details are easy to trip over:

* The bundle is written **next to the build output**, not into `publish/` - `AppBundleDir` is derived
  from the build output directory. `-p:CreatePackage=false` stops the SDK additionally producing an
  installer `.pkg`.
* Without `-r`, the macOS SDK defaults to **`osx-x64`** even on an Apple Silicon runner. That is not
  just a different file layout: the rendered pixels differ from an `osx-arm64` build, which looks
  exactly like a trimming regression. Pass `-r osx-arm64` (or `osx-x64`) explicitly when comparing.

What is left is almost entirely native code that no trimmer can touch: `libSkiaSharp.dylib` is
15.2 MB, `libcoreclr.dylib` 5.9 MB and `libclrjit.dylib` 3.0 MB out of the ~35 MB. The remaining
~11 MB of managed assemblies is what a NativeAOT publish would remove. It is not wired up for macOS
because the `.app` bundle is already the unit of distribution: folding the runtime into the executable
would not make the deliverable any smaller, only less debuggable. Windows and Linux do publish AOT
(see [NativeAOT](#nativeaot)).

The `macOS build` workflow publishes all three link modes, renders each bundle with `--render`,
compares the *decoded* bitmaps (`sips` to BMP plus `cmp`, because a one pixel change rewrites most of
a PNG) and reports the numbers in the job summary.

## Contributing a feature

New features are held to [FEATURE-COST-POLICY.md](FEATURE-COST-POLICY.md). A pull request that adds
one says, in its description:

* which rung of section 4 the feature sits on, and why it could not sit on a lower one;
* the published size of an application that does not use it, measured on every platform the change
  affects and quoted against the same application before the change;
* anything in the section 9 checklist that needed a judgement call, and the judgement.

Section 9 is the review checklist itself. A reviewer works through it line by line and asks for the answer
rather than inferring it, because the failures it catches are silent ones: a registration that runs
whether or not anything uses it, a `net10.0` assembly that lands in the wrong application, a
reflection path that only fails under AOT. The short form is that nothing new appears on an
always-retained type, nothing runs unless the application opted in, and the pull request carries a
number instead of an assurance.

A feature that ships as its own package needs no multi-platform build. The `Feature pack` workflow
packs every `Kolvir.Feature.*` project on a macOS runner - `EnableWindowsTargeting=true` brings the
Windows reference pack with it - then opens each package and fails unless it carries a `lib/` folder
and a dependency group for every platform. That check exists because both omissions are silent in
NuGet: the consumer falls back to the `net10.0` one and ships another platform's assembly. Section 4 of the
policy has the details.

### Tests come with the change

A change is not finished until the behaviour it adds is covered, and the tests land in the same
pull request rather than a follow-up. A bug fix carries a regression test that fails before the fix
and passes after it; a new feature carries tests for the behaviour it introduces, including the
public surface an application is meant to use.

- They go in `Kolvir.Tests`, or in the feature's own `Kolvir.Feature.*.Tests` assembly when the
  work lives outside the core library - the same place the surrounding tests for that area live,
  rather than a new file of their own.
- They run headless, on all three platforms, with no display, window server or network, and under
  the same category filter the [Tests workflow](.github/workflows/tests.yml) uses.
- They cover the behaviour, not incidental internals: what an application can see and depend on,
  through the host's own input entry points rather than by calling private helpers.

The suite's own conventions - a hand-driven clock, the pinned font, no shared state between tests -
are what make that possible; [Running the tests](#running-the-tests) describes them. The same rule is
one of the bullets in "What done means" in [CONTROLS-TODO.md](CONTROLS-TODO.md#2-what-done-means).

### The markdown stays in 7-bit ASCII

Every `.md` file in this repository is plain 7-bit ASCII, and a change keeps it that way: `-` rather
than a typographic dash, `...` rather than an ellipsis character, `->` rather than an arrow, `x`
rather than a multiplication sign, `section 8` rather than a section sign.

The reason is not typographic purity. These files have been saved by editors and scripts that
guessed a Windows code page, and every such save turned one character into two and then into three -
a section sign came back as four characters of rubble, an arrow as eight - with nothing in the result
to say what was meant. Plain ASCII cannot be mis-guessed: a file that is ASCII reads the same
whatever tool touches it next. The whole documentation set was repaired once after exactly that
happened, and that repair cost far more than catching it in review would have.

Checking is one line, and it should print nothing:

```
grep -nP '[^\x00-\x7F]' *.md
```
