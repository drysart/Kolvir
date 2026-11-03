# XPlatUI

A tiny, AOT-friendly, cross-platform UI layer that borrows the WinForms/WPF programming
model (controls, docking, table layout, events) but draws everything with
[SkiaSharp](https://github.com/mono/SkiaSharp). The library itself has no OS dependency: a backend
supplies the window, the input, the clipboard, the dialogs and the system appearance, and the
controls draw themselves on top of it. The same sample UI runs on all three platforms.

```
XPlatUI/                  net10.0 class library (no OS-specific code, IsAotCompatible)
  Application/            XPlatApplication, XPlatWindow, XPlatIcon, options, DI wiring, dialogs
  Core/                   primitives: control base class, geometry, painting, fonts, theme, clipboard
  Controls/               XPlatPanel, XPlatStackPanel, XPlatTableLayoutPanel, XPlatSplitContainer, XPlatScrollBar, XPlatLabel, XPlatRichLabel, XPlatButton, XPlatTextBox, XPlatListBox, XPlatImage
  Layout/                 row/column sizing styles
  Menus/                  menu bar, menu, menu item, and the context menu contract
  Platform/               the backend contract: IXPlatApplicationBackend, IXPlatWindowBackend, ...
  Selection/              text selection and its clipboard plumbing
  Services/               window-scoped service helpers
  Native/                 SkiaNativeLoader.cs - unpacks the embedded Skia library in an AOT build
  buildTransitive/        the MSBuild that embeds it, shipped with the package
  XPlatUiHost.cs          platform-neutral host: layout, painting, input routing, focus
XPlatUIDemo.App/          the one application: net10.0-windows, net10.0-macos or net10.0
  Program.cs              pick a backend, describe a window, run
  DemoUi.cs               the sample layout, plus RenderToPng for headless renders
  DemoIcon.cs             the sample icon, drawn in code so no binary asset is needed
  app.manifest            per-monitor v2 DPI awareness (Windows)
  Info.plist              the bundle identity (macOS)
XPlatUI.Backend.Win32/    net10.0-windows backend over raw Win32 (no WinForms, no System.Drawing)
XPlatUI.Backend.AppKit/   net10.0-macos backend over AppKit
XPlatUI.Backend.Gtk4/     net10.0 backend over GTK4
XPlatUI.Backend.Headless/ net10.0 backend that paints offscreen; what --render and the tests use
XPlatUI.Backend/          multi-targeting selector: the backend for whichever TFM you built for
XPlatUI.Feature.DragDrop/ drag and drop, as a separate package: one project, one TFM per platform
  common/                 the portable half: AllowDrop, the handler pairs, the router
  windows/                the opt-in, its hook and the OLE IDropTarget they register
  linux/                  the opt-in, its hook and the GtkDropTarget controllers they add
  macos/                  the opt-in, its hook, and the sink it installs on the view (below)
XPlatUI.Minimal/          the measurement fixture: one window, one label, no features
```

`XPlatUIDemo.App` builds exactly **one** target framework per operating system, chosen by
`$([MSBuild]::IsOSPlatform(...))`: `net10.0-windows` on Windows, `net10.0-macos` on macOS and
`net10.0` on Linux. That is deliberate rather than tidy. `net10.0-macos` needs the macOS workload,
and the `.app` it produces needs a Mac with the matching Xcode, so it must never appear in a TFM
list that Windows has to evaluate; and a portable `net10.0` build *on a Mac* defines neither
`WINDOWS` nor `MACOS`, so it would silently compile the GTK4 branch and produce a Linux binary.
One TFM per OS is what keeps `Program.cs`'s `#if` blocks honest.
See [On macOS (AppKit)](#on-macos-appkit).

Building `WinFormsApp3.slnx` needs the macOS workload, because the solution lists
`XPlatUI.Backend.AppKit` and `XPlatUI.Feature.DragDrop`, which both target `net10.0-macos`.
`dotnet workload install macos` satisfies it where it is not already present; a build without it
stops with `NETSDK1147`. Nothing else in the solution needs a workload, and an application that
references either project still builds only the one inner build its own framework can use.

`XPlatUI.Backend` is the way out of that `#if` for an application that wants nothing more than a
default backend. It is a project that multi-targets and references exactly one backend per target
framework, so referencing *it* is enough: the framework the application builds for decides which
backend it gets, and a publish carries only that one. `XPlatUIBackend.CreateDefault()` returns the
backend and loads the embedded Skia library on the way, so `Main` names no platform and no graphics
library. `XPlatUI.Minimal` is written that way. The selector's framework list is unconditional,
because a selector that offered only the host's framework could not be packed for the others;
restoring it therefore evaluates all three, and building the fixture needs the macOS workload on
every platform, not just on the Mac.
has to read `--classic-menus` before it can build a backend.

## Design

| Concept | WinForms equivalent | Notes |
| --- | --- | --- |
| `XPlatControl` | `Control` | Bounds, `Margin`/`Padding`, `Dock`, `Visible`/`Enabled`, colours, focus, mouse/keyboard events; any control can host children |
| `XPlatUiHost` | the window/`Form` | Owns the root control, lays out, paints into an `SKCanvas`, routes input, tracks hover/capture/focus |
| `XPlatPanel` | `Panel` | Docking layout, optional frame/card chrome, `None`/`Automatic`/`Always` scrolling per axis |
| `XPlatTableLayoutPanel` | `TableLayoutPanel` | Absolute / Percent / AutoSize rows and columns, `CellPadding`, per-cell positions |
| `XPlatSplitContainer` | `SplitContainer` | Two panels with a draggable splitter, fixed/min/max sizes |
| `XPlatScrollBar` | `ScrollBar` | Classic non-overlay bar: arrows, track paging, draggable thumb, keyboard |
| `XPlatLabel` | `Label` | Nine-way alignment, optional word wrap |
| `XPlatRichLabel` | `RichTextBox`/`FlowDocument` | Wrapping runs of styled text with interactive spans and inline child controls |
| `XPlatButton` | `Button` | Primary/Secondary/Ghost styles, hover/press/disabled states, Enter/Space activation |
| `XPlatTextBox` | `TextBox` | Single-line editor: caret, selection, clipboard, `MaxLength`, `ReadOnly`, password masking, placeholder |
| `XPlatListBox` | `ListBox` | Row list with all four `SelectionMode`s, keyboard navigation, type-ahead and optional scrollbars |
| `XPlatImage` | `PictureBox` | `SizeMode` (Normal/StretchImage/CenterImage/Zoom), `Load` helpers |
| `XPlatTheme` / `XPlatPalette` | `SystemColors` / `Application.SetColorMode` | Every default colour is a palette role, so the whole UI can switch light/dark at runtime |
| `XPlatSystemTheme` | `Application.ColorMode` | The seam a backend implements to report the OS appearance |

Every feature is held to [FEATURE-COST-POLICY.md](FEATURE-COST-POLICY.md): anything an application
does not use has to cost it close to zero in the trimmed executable. A feature reaches that by
construction where it can, by an opt-in registration where it cannot, and by its own package where
neither is practical; §8 of the policy has the measured sizes, including what opting into drag and
drop costs on each platform.

Everything is drawn in **logical (96 DPI) coordinates**; the host adapter is responsible
for scaling the canvas and the incoming pointer coordinates (the Win32 backend uses
`GetDpiForWindow() / 96f`).

## Using it

```csharp
var host = new XPlatUiHost { ClearColor = SKColors.White };

var root = new XPlatPanel { BackColor = XPlatTheme.Background, Padding = new XPlatThickness(16) };
var table = new XPlatTableLayoutPanel { Dock = DockStyle.Fill };
table.ColumnStyles.Add(XPlatColumnStyle.Percent(50));
table.ColumnStyles.Add(XPlatColumnStyle.Percent(50));
table.RowStyles.Add(XPlatRowStyle.AutoSize());
table.RowStyles.Add(XPlatRowStyle.Percent(100));

var button = new XPlatButton("Say hello") { Dock = DockStyle.Fill };
button.AddClickHandler(_ => Console.WriteLine("clicked"));

table.Add(new XPlatLabel("Hello") { AutoSize = true }, 0, 0);
table.Add(button, 1, 1);
root.Controls.Add(table);

host.Root = root;
host.SetClientSize(800, 600);   // lays out the tree
host.Paint(canvas);             // any SKCanvas: window surface, bitmap, PDF, …
```

Input is plain method calls, so any backend can drive it:

```csharp
var pt = new SKPoint(x, y);                    // logical coordinates
host.ProcessMouseMove(pt);
host.ProcessMouseDown(pt, XPlatMouseButtons.Left);
host.ProcessMouseUp(pt, XPlatMouseButtons.Left);
host.ProcessKeyDown(XPlatKey.Tab);             // cycles focus
host.ProcessTextInput("a");
```

`host.AddInvalidatedHandler` tells the adapter to repaint; `host.AddCursorChangedHandler` lets it
update the native cursor.

### Handlers

There are no C# events. Every notification is a pair of methods, `Add<Name>Handler` and
`Remove<Name>Handler`, so a subscription is a plain method call and the delegate behind it stays
private to the library:

```csharp
Action<object, XPlatMouseEventArgs> onDown = (sender, e) => Press(sender, e.Location);

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

### Split container

`XPlatSplitContainer` hosts exactly two panels with a draggable splitter between them.
Orientation follows the WinForms convention:

| `Orientation` | Layout | `SplitterDistance` is | Cursor |
| --- | --- | --- | --- |
| `Vertical` | `Panel1` left, `Panel2` right | `Panel1` width | `SizeWE` |
| `Horizontal` | `Panel1` top, `Panel2` bottom | `Panel1` height | `SizeNS` |

`Panel1` is the sized panel; `Panel2` always fills the remaining space. The distance is
clamped to `Panel1MinSize` … `Panel1MaxSize`, further limited by `Panel2MinSize` so the
second panel never collapses.

```csharp
var split = new XPlatSplitContainer
{
    Dock = DockStyle.Fill,
    Orientation = XPlatOrientation.Vertical,   // side by side
    SplitterDistance = 240f,                   // width of Panel1
    SplitterWidth = 6f,
    Panel1MinSize = 120f,
    Panel1MaxSize = 420f,
    Panel2MinSize = 180f,
};

split.Panel1.Controls.Add(new XPlatLabel("Navigation"));
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

`XPlatStackPanel` lays its children out one after another along a single axis and sizes each one
across that axis, which is the flow layout a plain panel does not have:

| `Orientation` | Children run | Each child is stretched to the panel's |
| --- | --- | --- |
| `Vertical` (default) | top to bottom | width |
| `Horizontal` | left to right | height |

A child gets its own preferred size along the stack and fills the panel across it, so a stack of
buttons is a stack of full-width rows. `Spacing` is the gap between children; there is none before
the first or after the last.

```csharp
var stack = new XPlatStackPanel
{
    Dock = DockStyle.Fill,
    Spacing = 8f,
    Padding = new XPlatThickness(12f),
    ShowFrame = true,            // a stack panel draws no frame of its own by default
};

stack.Controls.Add(new XPlatButton("Save"));
stack.Controls.Add(new XPlatButton("Cancel"));
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
stack.Controls.Add(new XPlatButton("Centred") { HorizontalAlignment = XPlatAlignment.Center });
```

- A child with `AutoSize = false` keeps the size it was given along the stack, which is how a run of
  fixed-height rows is built; it is still stretched across the stack unless it is aligned.
- `Margin` still applies: it separates one child from the next along the stack and insets it from
  the edges across it, so `Spacing` is for the gap and `Margin` for a per-child adjustment.
- A docked child is not part of the run. It takes its edge of the panel the way a docked child of
  any panel does and the stack fills what is left, which is how a header or a footer is added:

```csharp
stack.Controls.Add(new XPlatLabel("Header") { Dock = DockStyle.Top, AutoSize = false, Height = 24f });
```

- The run of children is the scrollable content, so `VerticalScroll = XPlatScrollStyle.Automatic`
  scrolls a long stack; a docked header stays put while the rows scroll under it.
- With `AutoSize = true` the stack is as long as the run and as wide as its widest child.
- A child wider than the space across the stack is clipped to the panel rather than overflowing it,
  so `Center` and `Far` never push a child outside the control.

### Scrolling

`XPlatPanel` scrolls each axis independently. `HorizontalScroll` and `VerticalScroll` take an
`XPlatScrollStyle`, and both default to `None`, so a panel that does not ask for scrolling pays
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
var list = new XPlatPanel
{
    Dock = DockStyle.Fill,
    VerticalScroll = XPlatScrollStyle.Automatic,
    Padding = new XPlatThickness(4f),
};

for (int i = 0; i < 20; i++)
{
    list.Controls.Add(new XPlatButton($"Row {i}")
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
`XPlatUiHost` maps coordinates through the same offset. Two rules follow from that:

- Only children whose `Dock` is `DockStyle.None` contribute to `ScrollExtent`. A docked child is
  pinned to the viewport and never scrolls, which is what you want for a header or a filter row.
- Content is measured from the top-left of the viewport, so a child positioned to the left of or
  above it is clipped rather than scrolled to. Lay content out from the origin.

`XPlatScrollBar` is also usable on its own, exactly like the WinForms `ScrollBar`. It reports
`Minimum`, `Maximum`, `Value`, `SmallChange` and `LargeChange`, and distinguishes a change the user
made (`AddScrollHandler`, raised by the arrows, the track, the thumb and the keyboard) from one the
application made (`AddValueChangedHandler`, raised by any change at all). Setting `Value` from code
moves the thumb and raises only the second.

The mouse wheel scrolls the control under the pointer. `XPlatUiHost.ProcessMouseWheel` starts at the
captured control, or the control under the pointer, and walks up the parent chain until something
claims the turn, so a wheel event over a nested list scrolls the list and only reaches the panel
around it once the list is at its end. `XPlatMouseEventArgs.Delta` is the distance in logical
pixels, positive toward the start of the content (up or left); a backend that reports a wheel in
detents multiplies by `XPlatUiHost.WheelPixelsPerDetent`, one that reports lines multiplies by its
own platform's line height - macOS reports a wheel in ten-point lines and scales a line up by half
again, so a typical three-line notch moves much the same distance as a detent - and one that reports
precise deltas (a touchpad) passes them straight through. A panel scrolls vertically unless `Shift`
is held and horizontal scrolling is possible, and falls back to the other axis when the preferred one
cannot move. A standalone `XPlatScrollBar` moves three `SmallChange` steps per detent.

```csharp
list.AddMouseWheelHandler((sender, e) =>
{
    if (e.Handled) return;                  // an ancestor already dealt with it
    Console.WriteLine($"wheel by {e.Delta}px");
});
```

The bars a panel creates for itself are not focusable: they take clicks, drags and the wheel, but
`Focusable` is `false`, so clicking one leaves the focus where the user was working, `Focus()` on it
does nothing and `Tab` never lands on it. That is what `XPlatControl.Focusable` is for in general -
it gates click-to-focus, `Focus()` and the tab order without affecting mouse interaction. A
standalone `XPlatScrollBar` is still `TabStop` and `Focusable`, so it can be driven from the
keyboard as before.

A press that cannot move the focus also leaves the selection alone. Dragging a scrollbar, or
clicking a plain label, therefore keeps the caret of a focused text box blinking and the text box
keeps taking what the user types; only a press that actually moves the focus, or one that lands on
another selectable control, ends the selection.

What is still missing is the overlay style: an overlay scrollbar floats over the content and does
not take a gutter. The bars are classic until that lands.

A scrolling container and a scrolling list want the same plumbing, so it lives in one internal
`XPlatScrollBars` helper that `XPlatPanel` and `XPlatListBox` both own: the visibility fixed point
(a gutter can bring the other axis into play, so the two axes are resolved together), the bar
placement, the offset clamping, `ScrollRectIntoView`, the wheel and the invalidation clip. A third
scrolling control only has to say how big its content is - `XPlatPanel` measures its children, the
list box multiplies its row count by the row height - and the helper does the rest.

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
        panel.Controls.Add(new XPlatButton($"Button {i}") { Location = new SKPoint(0f, i * 36f) });
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

`XPlatListBox` is the WinForms list box: a vertical list of rows with a selection, keyboard
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
| `Font` | Optional override of `XPlatTheme.DefaultFont` |
| `TopIndex` | The first row at the top of the viewport, clamped to what can actually scroll |
| `HorizontalScroll`, `VerticalScroll` | The same `XPlatScrollStyle` values a panel takes; vertical defaults to `Automatic`, horizontal to `None` |
| `ScrollBarThickness` | The gutter width, 16 px by default |
| `VerticalOffset`, `HorizontalOffset`, `ViewportRect` | Where the rows are scrolled to, and the area they are painted into - the client area less the gutters of the bars that are showing |
| `EmptyText` | Muted text drawn while the list has no rows |
| `EnsureVisible(i)`, `ScrollTo`, `ScrollBy` | Bring a row into view, or drive the offset directly |
| `RefreshItems()` | Redraw after mutating the objects in `Items` in place |
| `AddSelectedIndexChangedHandler`, `AddItemActivatedHandler` | Selection changed; and a row was double-clicked or confirmed with `Enter` |
| `AddDrawItemHandler` | Paints a row yourself: it is handed the canvas, the row rect, the item and an `XPlatDrawItemState` |
| `BeginUpdate()`, `EndUpdate()` | Batch many changes into one layout and one repaint |

```csharp
var list = new XPlatListBox
{
    Dock = DockStyle.Fill,
    SelectionMode = XPlatSelectionMode.One,
    VerticalScroll = XPlatScrollStyle.Automatic,
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
caret.Invalidate(XPlatGeometry.Inflate(bounds, 1f), invalidateChildren: false);
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
to `XPlatUiHost`. A control therefore cannot reach past its own bounds, and the root cannot be
reached around.

That hop is the point: it is where a container decides what a child's request means.
`ChildInvalidate` is `protected internal virtual` so a container can narrow the area, move it, or
widen it to take in the decoration it draws around the child, and it receives the area in its own
coordinates with the child's bounds already applied. `XPlatPanel` uses it to clip content to the
viewport, so a control scrolled out of sight asks for nothing and one that is half scrolled out
asks only for the part that is showing; the scrollbars are left alone, because they live in the
gutter the viewport excludes and are painted over the content rather than clipped with it.

```csharp
// A container that frames its children repaints the frame along with whatever changed inside it.
protected internal override void ChildInvalidate(XPlatControl child, SKRegion region)
{
    region.Op(ToRectI(Inflate(child.Bounds, FrameThickness)), SKRegionOperation.Union);
    base.ChildInvalidate(child, region);
}
```

The paint itself is region-exact. `XPlatUiHost.Paint` clips to the dirty region - through its
boundary path when the region is not a plain rectangle, so scattered invalidations do not repaint
the gaps between them - clears that area and renders the tree into it. `XPlatControl.Render` calls
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
is about to repaint what it arranged, so `XPlatControl` folds the requests a pass causes into one
`Invalidate` of the control being laid out, made when the pass - including any nested passes it
started - finishes. The repaint covers that control's client area, which for a scrolling container
is the viewport rather than the content. That is what lets a stack panel of a hundred thousand
buttons scroll in milliseconds instead of seconds.

Hovering is not a repaint on its own. A control that draws itself differently under the pointer
says so, by invalidating from its own `OnMouseEnter`/`OnMouseLeave`; the base `XPlatControl` only
tracks `IsMouseOver` and raises the events. Repainting every control the pointer crosses is what a
whole-window renderer gets away with, because the frame it would have drawn anyway costs the same:
here it would put a title label's whole bounds into the next region, and with it everything
between the label and whatever else changed. A label, a panel, a table and an image look the same
hovered as not, so they are left out of it.

A backend opts into partial presentation with `XPlatUiHost.PartialUpdates`. It is off by default
because a backend that rasterizes the whole surface every frame - GTK4 and the headless renderer
both do - would be left with blank patches where the retained content should be:

| Backend | Presents |
| --- | --- |
| Win32 | The painted area, blitted out of the retained DIB |
| AppKit | The painted area, through the layer's `CGImage` |
| GTK4 | The whole surface every frame |
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

Set `XPLATUI_DEBUG_PAINT` to paint every invalidated area flat red for one frame before painting its
content over it. Partial repaints are invisible by design, which is what makes them hard to trust:
this makes each one announce itself.

```powershell
$env:XPLATUI_DEBUG_PAINT = '1'      # hold the red frame for the default 80 ms
$env:XPLATUI_DEBUG_PAINT = '500'    # hold it for half a second
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

`XPlatUiHost` times the frames it paints, which is enough to see what a partial repaint buys
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

`XPlatRichLabel` flows styled runs of text into a wrapping paragraph. Formatting lives on
`XPlatTextSpan` objects held in the `Spans` collection; a span can also carry a child control
that is embedded **inline** in the text flow (CSS `inline-block` style) and a set of mouse
events that make it behave like a hyperlink.

| Member | Behaviour |
| --- | --- |
| `Spans` | Ordered list of `XPlatTextSpan`; add/remove directly or use the helpers below |
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
var rich = new XPlatRichLabel { Dock = DockStyle.Fill, WrapText = true };

rich.AddText("Read the ");
rich.AddSpan(new XPlatTextSpan("terms of service") { Underline = true, ForeColor = XPlatTheme.Accent });
rich.AddText(" and the ");
rich.AddSpan(new XPlatTextSpan("privacy policy") { Bold = true });
rich.AddText(".\nInline controls:");

var link = rich.AddLink("documentation", span => Open(span.Tag as string));
link.Tag = "https://example.com/docs";
link.HoverBackColor = XPlatTheme.Secondary;

rich.AddText(" ");
rich.AddInlineControl(new XPlatButton("Retry") { AutoSize = true }, XPlatInlineAlignment.Center);
rich.AddText(" ");
rich.AddInlineControl(badge, XPlatInlineAlignment.Bottom);

rich.AddSpanClickHandler((_, e) => Console.WriteLine($"clicked {e.Span.Text}"));
```

Inline controls are measured like any other control (`AutoSize`, `Margin`, explicit sizes) and
are laid out by the label, so they receive input normally. A control taller than the text grows
its line and pushes the whole line down, so `Top`-aligned controls never paint outside the
label.

### Text box

`XPlatTextBox` is a single-line editor: caret, selection, clipboard, password masking,
placeholder text and horizontal scrolling are all implemented by the control itself.

| Member | Behaviour |
| --- | --- |
| `Text`, `AddTextChangedHandler` | Current value. Control characters (including line breaks) are stripped so the field stays single line |
| `CaretIndex`, `SelectionStart`, `SelectionLength`, `SelectedText`, `HasSelection` | Selection state; the caret is `0 … Text.Length` |
| `Caret` | The `XPlatCaret` that draws and blinks the insertion point; its `BlinkInterval`, `Width` and `Color` can be changed |
| `Select(start, length)`, `SelectAll()`, `ClearSelection()` | Programmatic selection |
| `MaxLength` | Characters the user may type (`0` = unlimited); enforced for insertions and replacements |
| `ReadOnly` | Selection and copy still work; typing, paste, cut and delete are blocked |
| `IsPassword`, `PasswordChar` | Draws one mask glyph per character (`'\u2022'` by default) |
| `PlaceholderText` | Muted text shown while the field is empty |
| `Font` | Optional override of `XPlatTheme.DefaultFont` |
| `Copy()`, `Cut()`, `Paste()`, `InsertText(text)` | Programmatic edit operations |

Keyboard: `Left`/`Right` move by character (`Ctrl` moves by word), `Home`/`End` jump to the
edges, `Shift` extends the selection, `Backspace`/`Delete` delete the selection or one
character, and `Ctrl+A`/`C`/`X`/`V` select all, copy, cut and paste. `Enter` is *not*
consumed, so a form can still treat it as an accept gesture. The mouse sets the caret,
drags a selection and supports double click (word) and triple click (everything).

```csharp
var name = new XPlatTextBox
{
    Dock = DockStyle.Top,
    PlaceholderText = "Type your name",
    MaxLength = 40,
};
name.AddTextChangedHandler(_ => Console.WriteLine(name.Text));

var password = new XPlatTextBox { Dock = DockStyle.Top, IsPassword = true };
var readOnly = new XPlatTextBox { Dock = DockStyle.Top, ReadOnly = true, Text = "fixed" };
```

The library itself stays platform free, so one thing remains the adapter's job:

* **Clipboard** - implement `IXPlatClipboard` (`GetText`/`SetText`, plus the optional
  `GetHtml`/`SetHtml` pair) and assign an instance to `XPlatClipboard.Current` once at startup;
  without one an in-process fallback is used. The Win32 backend installs `Win32Clipboard`
  (`Win32Clipboard.cs`), which publishes a real `HTML Format` payload.

The caret is a control of its own: `XPlatCaret` owns the timer that blinks it and follows its
control's focus and enabled state, so it only runs while the caret is actually on screen and an
idle application has no timer running at all. `XPlatTextBox.Caret` exposes the instance it uses, so
its interval, width and color can be changed, and a new editable control gets the same caret with
`new XPlatCaret(this)`. A control that animates something else does the same through `CreateTimer`,
which hands back an `XPlatTimer` the control owns and that is disposed with it.

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
| `host.CopySelection()` | Writes both flavours to `XPlatClipboard`; `host.SelectionTextSeparator` joins the pieces |
| `host.ProcessContextMenu(point)` | Builds the menu: the control's own `ContextMenu`, then the `AddContextMenuOpeningHandler` handlers, then a default **Copy** item |

`XPlatRichLabel` overrides `GetSelectionHtml` so a copy keeps its formatting: `<b>`, `<i>`,
`<u>`, `<s>` and a `<span style="...">` for colour and size overrides, wrapped around the
*selected slice* of each span - a selection that starts mid-span gets markup for just those
characters. A selection spanning several controls joins their pieces with a line break.

```csharp
var html = rich.GetSelectionHtml(12, 4);   // "<b>bold</b>"
XPlatClipboard.SetHtml(rich.GetSelectionText(12, 4), html);   // both flavours at once
```

#### Built-in controls

| Control | Defaults | Notes |
| --- | --- | --- |
| `XPlatLabel`, `XPlatRichLabel` | `Selectable = false`, `Partial` | Set `Selectable`; a drag selects character by character |
| `XPlatTextBox` | `Selectable = true`, `Partial` | Its own caret and anchor answer for a drag that *starts* inside it; a drag that merely passes through it selects its text and drops the caret |
| `XPlatButton`, `XPlatImage` | `Selectable = false`, `Whole` | Set `Selectable`; a drag over the control selects it as one unit |
| `XPlatListBox` | `Selectable = false`, `Whole` | Its row selection is its own; the host selection is not involved |
| `XPlatPanel` and other containers | `Selectable = false`, `Whole` | A selectable container hides its children from the selection, so leave containers off unless the container itself is what should be selected |

A text box is selectable by default and does not paint a second overlay: `GetSelectionOffset` and
`GetSelectionRects` bridge the host's selection into the same `IndexFromX`/`MeasureX` axis the
caret already uses, so the single-selection rule above still holds.

A text box also builds its own menu - **Cut**, **Copy**, **Paste** and **Select All**, with their
shortcuts and per-item `Enabled` states - so a right-click inside one always opens a menu. Its
**Copy** falls back to `host.CopySelection()` when the box has no selection of its own, which is
what makes copying a multi-control selection work no matter which control was right-clicked.

### Context menus

`host.ProcessContextMenu(point)` builds an `XPlatContextMenu` and hands it to the presenter
installed on the host. The library only owns the model; the backend decides how to show it.

On Windows 11 the backend draws the menu itself instead of calling `TrackPopupMenuEx`, because the
Win32 menu is the old flat style while the shell and WinUI menus are rounded, softly shadowed and
backed by acrylic. The menu goes through the same Skia pipeline as the controls and is shown in a
`WS_EX_LAYERED` popup, so it looks the same on every machine and follows `XPlatTheme` in light and
dark:

| Piece | What it does |
| --- | --- |
| `XPlatMenuRenderer.Measure(menu, metrics, font, shadow)` | Lays the rows out and returns an `XPlatMenuLayout`: surface size, shadow margin, row bounds, `HitTest` and `NextPickable` |
| `XPlatMenuRenderer.Paint(canvas, layout, hovered, surfaceAlpha)` | Draws the surface, drop shadow, hover pill, check column and right-aligned shortcut hints. It does not clear the canvas, so whatever the caller painted underneath shows through a translucent surface |
| `XPlatMenuMetrics` | Row height, padding, corner radii and shadow extent; `Default` is tuned to the Windows 11 menu |
| `Win32MenuWindow.cs` | The popup: captures and blurs the backdrop, renders into a 32bpp DIB and

`XPlatMenuRenderer` contains no platform code, so a macOS or Linux backend can reuse it verbatim
instead of writing its own menu drawing. Because the popup is layered, the rounded corners and the
drop shadow come from per-pixel alpha: the window is sized surface + `ShadowExtent` on each side and
positioned so that the surface - not the window - lands exactly on the requested point.

The acrylic is produced by the popup rather than by DWM, because DWM ignores
`DWMWA_SYSTEMBACKDROP_TYPE` on a layered window and translucency without blur would leave the text
behind the menu legible through it. Instead the popup copies the screen region it is about to cover
before it is shown, blurs that copy, adds a fine noise and paints the surface over it at 80% (dark)
or 84% (light) opacity - the same three layers real acrylic is made of. The blur is baked into the
captured image because Skia ignores an image filter on the paint passed to `DrawImage`. If the
capture fails, the surface falls back to fully opaque.

The popup runs its own message loop while it is open, the way `TrackPopupMenuEx` does. Moving the
pointer moves the highlight, the arrow keys walk the enabled items (skipping separators and disabled
rows), `Enter` or a click invokes, and `Esc`, a click outside or losing activation dismisses it.

This style is used when `Environment.OSVersion.Version.Build >= 22000`. On older Windows, or if the
popup cannot be created, `Win32ContextMenuPresenter` falls back to `TrackPopupMenuEx`; pass
`--classic-menus` to the application to force the old menu on Windows 11 as well.

### Keyboard shortcuts

`ShortcutText` is only a hint: it says what the menu draws on the right, and nothing dispatches it.
A shortcut that actually works is an `XPlatShortcut` on the item:

```csharp
var dark = new XPlatMenuItem("Dark Mode")
{
    Shortcut = new XPlatShortcut(XPlatKey.D),
    Click = () => demo.SetAppearance(XPlatPalette.Dark),
};
```

`XPlatShortcut(key, modifiers)` defaults `modifiers` to `XPlatModifiers.Control`, which means the
platform's accelerator modifier rather than a literal Ctrl key: `DisplayText` says "Ctrl+D" on
Windows and Linux and "Cmd+D" on macOS, and AppKit turns the same declaration into a real key
equivalent on the menu item. A shortcut with no modifier is rejected, because a bare key belongs to
the focused control.

The window checks its menu bar - the one on the `XPlatWindow`, falling back to
`XPlatApplicationOptions.MenuBar` - before the focused control sees the key, which is the order
native menus use. A matching item claims the key even when it is disabled, so a greyed-out command
does not silently fall through to the control that has focus. Because the lookup happens in
`XPlatUiHost.ProcessKeyDown`, it works on every backend, including the Win32 one, which records the
bar without drawing it yet.

`ShortcutText` still exists for a menu that wants to describe a key it does not dispatch itself;
when it is set it wins over the shortcut's own text in the menu drawing.

### Appearance

Every default colour in the library is a palette role, so the whole UI can switch between light and
dark at runtime. `XPlatTheme.Palette` holds the current one:

```csharp
XPlatTheme.Palette = XPlatPalette.Dark;   // or XPlatPalette.Light, or your own
XPlatTheme.IsDarkMode;                    // shortcut for Palette.IsDark
var accent = XPlatTheme.ColorOf(XPlatThemeColor.Accent);
```

`XPlatPalette` is a record with one colour per `XPlatThemeColor` role - `Background`, `Surface`,
`Foreground`, `MutedForeground`, `Border`, `Accent` (`Hover`/`Pressed`/`Foreground`),
`Secondary` (`Hover`/`Pressed`), `FocusRing`, `Splitter` (`Hover`), `InputBackground`, `Selection`,
`Caret`, `Placeholder` and `DisabledBackground`/`DisabledForeground` - so a custom theme is just
`XPlatPalette.Light with { Accent = SKColors.Teal }`. Assigning the palette (or any single colour,
e.g. `XPlatTheme.Accent = …`) runs the `XPlatTheme.AddChangedHandler` handlers and the next frame
picks it up: controls never snapshot a colour at construction time.

Controls resolve their colours while painting, so nothing has to be walked or re-applied:

| Member | Meaning |
| --- | --- |
| `DefaultBackColor` | Override to return a palette colour, or `null` for "no background" |
| `BackColor` | An explicitly assigned colour, otherwise `DefaultBackColor` |
| `ForeColorRole` | The role used while `ForeColor` has not been assigned explicitly |
| `BorderColor` | An explicitly assigned colour, otherwise `XPlatTheme.Border` |
| `Opacity` | Fades the control and everything inside it as one group, 0 to 1 |
| `XPlatTextSpan.ThemeColor` | A role for one span of a rich label; `AddLink` uses `Accent` |
| `XPlatTextSpan.HoverBackColorRole` | The role tinted behind a span while it is hovered |
| `XPlatUiHost.ClearColor` | Follows `XPlatTheme.Background` until assigned |

The library itself never asks the OS anything. A backend implements the two-property
`IXPlatSystemTheme`, installs it, and calls `host.RefreshTheme()` whenever the OS reports a change:

```csharp
XPlatSystemTheme.Current = new Win32SystemTheme();   // any IXPlatSystemTheme
XPlatSystemTheme.Refresh();                         // read IsDarkMode, swap the palette if needed
```

`XPlatSystemTheme.Refresh()` returns whether the appearance actually changed, and
`host.RefreshTheme()` additionally invalidates the tree. The Win32 backend
(`Win32SystemTheme.cs`) reads
`HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` (falling back
to the undocumented `uxtheme` `ShouldSystemUseDarkMode`), reacts to `WM_SETTINGCHANGE` and
`WM_THEMECHANGED`, and darkens the title bar with
`DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)` - driven by `XPlatTheme.Changed`, so a manual
palette change updates the chrome too.

### Opacity and transparency

There are two ways to make a control see-through, and they are not the same thing.

`BackColor` can carry alpha. A background of `new SKColor(0, 0, 0, 128)` is half-transparent
black, and it blends with whatever the parent painted underneath it. That is the cheap option: it
fades the control's own background and nothing else, and its text, border and children stay fully
opaque.

`Opacity` fades the control and everything inside it as one group:

```csharp
// The whole strip - background, text and the buttons inside it - at 60%.
var strip = new XPlatPanel { Dock = XDock.Top, Height = 48f, Opacity = 0.6f };
strip.Controls.Add(new XPlatLabel("Faded as one group"));
strip.Controls.Add(new XPlatButton("And so is this"));
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

`XPlatIcon` carries a bitmap **and** an optional icon-theme name, because the three platforms take
their icons in three unrelated ways:

```csharp
var options = new XPlatApplicationOptions
{
    ApplicationName = "My App",
    Icon = XPlatIcon.FromFile("app.png"),   // Windows title bar/taskbar, macOS Dock
};

using var application = new XPlatApplication(backend, options);
var window = new XPlatWindow("My App", root);   // inherits the application icon
window.Icon = XPlatIcon.FromFile("other.png");  // ...or override it per window
```

| Platform | What happens | Which half of the icon is used |
| --- | --- | --- |
| Windows | `WM_SETICON` with `ICON_SMALL` (title bar) and `ICON_BIG` (Alt-Tab, taskbar), rebuilt at the window's DPI on `WM_DPICHANGED` | pixels |
| macOS | `NSApplication.ApplicationIconImage`, i.e. the Dock tile and app switcher for the whole process | pixels |
| Linux (GTK4) | `gtk_window_set_icon_name`, and the application-wide `gtk_window_set_default_icon_name` | `Name` |

So an icon with pixels only shows up on Windows and macOS, and `XPlatIcon.FromName("folder")` shows
up on Linux only. Set both halves on one icon to cover all three:

```csharp
var icon = XPlatIcon.FromFile("app.png");
icon.Name = "my-app";      // the installed icon-theme name, for GTK
```

On Linux the name has to exist in the desktop's icon theme, and on Wayland the window manager takes
the icon from the application's `.desktop` entry instead. A shipping macOS `.app` should also set
`CFBundleIconFile` in `Info.plist`. `XPlatIcon.GetBitmap(size)` scales once per requested size and
caches the result, and the bitmaps it returns belong to the icon: don't dispose them, and keep the
icon alive for as long as a window refers to it.

### Single instance and re-activation

Set `SingleInstance` and a second launch of the executable hands its command line to the running
process instead of starting a second copy. The second process exits from `Run` with the exit code it
was given, so a `Main` that simply returns `application.Run()` needs no extra handling:

```csharp
var options = new XPlatApplicationOptions
{
    ApplicationName = "My App",
    SingleInstance = true,          // InstanceKey defaults to ApplicationName
};

using var application = new XPlatApplication(backend, options);

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
var tool = new XPlatWindow("Tool", panel) { Owner = window };   // or: tool.Show(window);
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

The demo exposes all three: the **Owned window** button in the header opens one from the UI on every
platform, **Window → Topmost** and **Window → Owned Window** add the menu items on macOS and GTK, and
the `--topmost` / `--owned` command-line flags work everywhere (Windows has no menu bar yet). The
status line reports activation changes.

### Drag and drop

Drag and drop is a package of its own, `XPlatUI.Feature.DragDrop`, because most applications never
accept a drop and the ones that do not should not carry the code that makes one possible. Reference
the package and then opt in: until the application asks for it, a window registers no drop target,
which is what keeps OLE, the COM target, the pasteboard types and the GTK controllers out of the
build. Asking for it is one line:

```csharp
using XPlatUI.DragDrop;

var options = new XPlatApplicationOptions
{
    ConfigureServices = services => services.AddDragDrop(),
};
```

With that in place, setting `AllowDrop` on a control makes it, and everything inside it, a drop
target. Both `AllowDrop` and the four handlers come from `XPlatUI.DragDrop`. The four drag handlers
receive an `XPlatDragEventArgs`; the drop handler sets `Effect` to say what it did:

```csharp
root.AllowDrop = true;

root.AddDragEnterHandler((_, e) => e.Effect = e.Data.Contains(XPlatDataFormats.FileDrop)
    ? XPlatDragDropEffects.Copy
    : XPlatDragDropEffects.None);

root.AddDragDropHandler((_, e) =>
{
    foreach (var path in e.Data.GetFiles())
    {
        status.Text = $"Dropped {Path.GetFileName(path)}";
    }

    e.Effect = XPlatDragDropEffects.Copy;
});
```

The opt-in is per application, and it is coarse on purpose: once it is called, every window gets a
drop target, whether or not any control ever sets `AllowDrop`. `AllowDrop` can be turned on and off
on a control that is already inside a shown window, so there is no correct moment at which the
native registration could depend on it. Leaving the call out is not an error: the code above still
compiles and runs, nothing simply ever arrives. What the feature costs an application that never
references the package is measured in [FEATURE-COST-POLICY.md](FEATURE-COST-POLICY.md) §8.

`AddDragEnterHandler`, `AddDragOverHandler` and `AddDragDropHandler` share one argument type, so the
same handler can serve all three. `AddDragLeaveHandler` takes a handler with no arguments: by the
time a drag has left, there is nothing left to say.
`Location` is in the control's own logical pixels, the same units `Paint` uses, and `Modifiers`
reports the shift, control and alt keys held at that moment.

The data on offer is an `XPlatDataObject`: a bag of values keyed by `XPlatDataFormat`. `Text`,
`Html` and `FileDrop` are the formats the backends fill in themselves; `XPlatDataFormats.Custom` is
there for a drag this library is not the only participant in. `GetFiles` returns absolute paths.

| Platform | Mechanism | What a drag carries |
| --- | --- | --- |
| Windows | A hand-built `IDropTarget` COM object registered with `RegisterDragDrop`, since a source-generated `ComWrappers` one is not a valid call target under the loader lock OLE calls it from. It lives in the feature package, which duplicates the few OLE imports it needs | `CF_HDROP`, `CF_UNICODETEXT`, `HTML Format` |
| macOS | The view registers `public.file-url` and `public.utf8-plain-text` and implements `NSDraggingDestination` | `NSPasteboard` file URLs (a web URL is not a file, so an image dragged out of a browser is refused), plain text |
| Linux (GTK4) | One `GtkDropTarget` per kind of drag, both typed as text, with preload so the payload is in hand before the drop; the feature package adds them to the view's drawing area | File paths, read from the text form of the drag since a `GdkFileList` cannot be read without a portal round trip; strings |

The mechanisms themselves live in the package wherever the platform allows it, so a backend assembly
that no application opted into carries no drag and drop code at all. Windows and Linux are in that
category: the COM target is an object registered against a window handle, and a `GtkDropTarget` is a
controller added to a widget, so both moved out of `XPlatUI.Backend.Win32` and
`XPlatUI.Backend.Gtk4` and only a generic slot for releasing them when the window goes remains.
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
`XPlatDragDropRouter`, one per window: the library names none of it and keeps no state for it.

The demo turns the whole window into a drop target: dropping files prints their names on the status
line, and dropping text prints the first sixty characters.

#### Migrating from the old surface

Setting `AllowDrop` and subscribing to the four drag handlers used to be members of `XPlatControl`.
They are extension members in `XPlatUI.DragDrop` now, and the events are `Add`/`Remove` handler
pairs like every other notification, so the two things an application does with the feature look
like this:

```csharp
// before: members of the control, registered unconditionally at window creation
root.AllowDrop = true;

root.DragEnter += (_, e) => e.Effect = e.Data.Contains(XPlatDataFormats.FileDrop)
    ? XPlatDragDropEffects.Copy
    : XPlatDragDropEffects.None;

root.DragDrop += (_, e) =>
{
    foreach (var path in e.Data.GetFiles())
    {
        status.Text = $"Dropped {Path.GetFileName(path)}";
    }

    e.Effect = XPlatDragDropEffects.Copy;
};
```

```csharp
// after: the same handlers, from the feature's namespace, plus one line at startup
using XPlatUI.DragDrop;

var options = new XPlatApplicationOptions
{
    ConfigureServices = services => services.AddDragDrop(),
};

// unchanged: a control still accepts a drop by saying so
root.AllowDrop = true;

root.AddDragEnterHandler((_, e) => e.Effect = e.Data.Contains(XPlatDataFormats.FileDrop)
    ? XPlatDragDropEffects.Copy
    : XPlatDragDropEffects.None);

root.AddDragDropHandler((_, e) =>
{
    foreach (var path in e.Data.GetFiles())
    {
        status.Text = $"Dropped {Path.GetFileName(path)}";
    }

    e.Effect = XPlatDragDropEffects.Copy;
});
```

`e` is the same `XPlatDragEventArgs` in both, and `Effect`, `Data`, `GetFiles()` and `Modifiers` are
unchanged, so a handler body moves across untouched: `+=` becomes `Add<Name>Handler` and `-=`
becomes `Remove<Name>Handler`, as it did for every other notification. `DragLeave` is the one that
lost its payload, so `root.DragLeave += (_, _) => Reset();` becomes
`root.AddDragLeaveHandler(_ => Reset());`. The package reference and the `AddDragDrop()` call are
the only two lines with no counterpart. Leaving out the call still compiles and runs - the window
simply never becomes a drop target - while leaving out the package removes the members the rest of
this code is written against, which is the point of it being a package.

## Writing a backend

A backend is two interfaces plus one registration hook. `IXPlatApplicationBackend` owns the platform
loop and the platform-wide services; `IXPlatWindowBackend` owns one native window and the
`XPlatUiHost` that paints into it. `XPlatUI.Backend.Win32`, `XPlatUI.Backend.AppKit`,
`XPlatUI.Backend.Gtk4` and `XPlatUI.Backend.Headless` are the four worked examples.

1. `Initialize` gets the `XPlatApplication`; `Run` enters the platform loop and returns its exit
   code; `Quit` leaves it; `Invoke` marshals a callback onto the UI thread;
   `TryClaimSingleInstance` returns false when another process already owns the instance key, and
   runs the `AddSecondInstanceHandler` handlers when a later launch is handed over.
2. `CreateWindow` returns an `IXPlatWindowBackend`. It creates the native window and an `SKSurface`
   (or a bitmap-backed surface) of its client size, and it creates and owns the `XPlatUiHost`.
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
   `XPlatWindow` method.
6. `RegisterServices` adds the platform services to MEDI's `IServiceCollection` - the clipboard, the
   system theme, the dialog service and the context menu presenter. Doing it through one hook
   instead of a `Create*` member per service keeps adding a service from breaking every backend, and
   keeps them lazy, so the headless backend never constructs a clipboard it will not use.
7. Implement `GetScreens`; `Shutdown` releases whatever `Initialize` took.

Two contract details are easy to miss. `ShowMessage`, `OpenFile`, `SaveFile`, `SelectFolder` and
`ShowDialog` are **synchronous** but re-enter the platform loop, so a backend has to tolerate a
nested loop and keep dispatching `Invoke` callbacks inside it. And `MenuBar` is per window: AppKit
mirrors the active window's bar into the single global macOS menu bar, which is the only place the
presentations diverge.

## NativeAOT

`XPlatUI` sets `IsAotCompatible` and uses no reflection, so it publishes with NativeAOT.
Verified locally with the Win32 build of the demo application itself (no Windows Forms, no
System.Drawing). The `FolderProfile` publish profile
(`XPlatUIDemo.App/Properties/PublishProfiles/FolderProfile.pubxml`) is preconfigured for AOT +
trimming, so publishing is just:

```
dotnet publish XPlatUIDemo.App\XPlatUIDemo.App.csproj -p:PublishProfile=FolderProfile
```

or right-click **Publish** in Visual Studio. The result is a **single** self-contained
`XPlatUIDemoApp.exe` (9,270,272 bytes, 8.84 MiB, of which 5.2 MiB is the compressed Skia native
binary) and an optional `XPlatUIDemoApp.pdb` for debugging. Nothing else is needed to run it - the
AOT build opens the real window and renders the demo.

On Windows this needs the MSVC linker (`link.exe`). The profile switches to
`IlcUseEnvironmentalTools` automatically once a VC environment is initialized, so the most
reliable way to publish is from a *Developer PowerShell/Command Prompt for VS* (or a CI step that
calls `vcvars64.bat` first):

```
call "C:\Program Files\Microsoft Visual Studio\18\Enterprise\VC\Auxiliary\Build\vcvars64.bat"
dotnet publish XPlatUIDemo.App\XPlatUIDemo.App.csproj -p:PublishProfile=FolderProfile
```

From a plain shell the SDK locates the toolset itself with `vswhere.exe`; if that fails (for
example a `vswhere.exe` error leaking into the link step), publish from a Developer prompt instead.

Linux publishes the same way, and does not need a `clang` or `lld` installation: the ILCompiler
package carries its own toolchain.

```
dotnet publish XPlatUIDemo.App/XPlatUIDemo.App.csproj -c Release -r linux-x64 -p:PublishAot=true
```

The result is a **single** `publish/XPlatUIDemoApp` (9,537,712 bytes, 9.10 MiB, a stripped ELF
executable) plus an optional `XPlatUIDemoApp.dbg`. It is genuinely standalone - copy just that one
file anywhere and it runs, window and all.

`XPlatUI.Minimal` is the measurement fixture: one window, one label, no features. It publishes to
8,527,872 bytes (8.13 MiB) on Windows and 8,867,296 (8.46 MiB) on Linux, so everything the demo
demonstrates costs about 0.7 MB on Windows and 0.6 MB on Linux over the floor an application starts
from. [FEATURE-COST-POLICY.md](FEATURE-COST-POLICY.md) §8 records the baseline that every feature is
measured against.

SkiaSharp only ships a dynamic `libSkiaSharp.dll`/`libSkiaSharp.so`, so a native executable cannot
simply link it. When `PublishAot` is on, XPlatUI's own build assets (`buildTransitive/` in the
package, which `Directory.Build.targets` imports for this repository's projects) gzip the
RID-specific native library and embed it in the application as the manifest resource
`XPlatUI.Native.libSkiaSharp.dll.gz` (or `...libSkiaSharp.so.gz`); `SkiaNativeLoader`, in the
library, extracts it on first use to `%LOCALAPPDATA%\XPlatUI\native\<version>-<size>\libSkiaSharp.dll`
(`~/.local/share/XPlatUI/native/<version>-<size>/libSkiaSharp.so` on Linux, honouring
`XDG_DATA_HOME`), which is cached so later runs just load it, and redirects SkiaSharp's P/Invoke at it
before the first call into Skia. The redirect is not optional on Linux: `libSkiaSharp.so` carries no
`SONAME`, so a library loaded by full path is not what `dlopen("libSkiaSharp.so")` resolves to. The
resource belongs to the application's assembly, because the copy to embed is the one the
application's build resolved for its platform. `XPlatUIBackend.CreateDefault()` names it for you: it
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

## Running the application

```
dotnet run --project XPlatUIDemo.App
```

`Program.cs` is the whole program: it picks the backend for the platform this build is for, describes
one `XPlatWindow` around `new DemoUi().Root`, and runs the `XPlatApplication`. Everything else lives
behind the backend - `Win32WindowBackend` creates the window with raw `user32` P/Invoke, owns a 32-bit
DIB section that Skia draws into, blits it on `WM_PAINT`, and forwards mouse/keyboard messages to the
`XPlatUiHost`.

The sample is organised as a navigation list down the left and a stage on the right that shows one
demo at a time, so adding a demo is a row in `DemoUi.Demos` rather than another crowded card. Every
page is built once and kept, and switching pages re-parents one control into the stage.

To render the same sample UI headlessly (useful for visual checks in CI):

```
XPlatUIDemoApp.exe --render out.png 1280 820
XPlatUIDemoApp.exe --render out.png 1280 820 --page 3
```

Add `--light` or `--dark` to either mode to force an appearance instead of using the system one; the
header's **Switch appearance** button flips it at runtime. `--page` picks a stage page by index in
both modes, which is what makes a single page renderable on its own. `--classic-menus` forces the old
`TrackPopupMenuEx` context menus. The sample UI is laid out for a 1280 x 820 client area at 96 DPI
points and the backend scales the Skia canvas by `GetDpiForWindow(hwnd) / 96`, sizing its client
area to `1280 x 820` multiplied by that scale (clamped to the monitor work area) so the demo is
never cropped on a high-DPI monitor. The **Selection and clipboard** page is the one to try:
drag down from its label, over the whole-control button and into the text box, then right-click
the selection. The two **Performance** pages are the ones to watch scroll: **Performance: list box**
loads 1 to 100,000 rows into a list, **Performance: controls** stacks 1 to 100,000 buttons in a
stack panel, and both report what the last frame cost to draw them.

### On Linux (GTK4)

The Linux build of `XPlatUIDemo.App` uses the
[GirCore](https://github.com/gircore/gircore) GTK4 bindings, so the build needs the GTK4 runtime and
development headers, plus a font backend for SkiaSharp:

```
sudo apt install libgtk-4-1 libgtk-4-dev libfontconfig1
```

The Skia native library comes from the `SkiaSharp.NativeAssets.Linux` package, so nothing has to be
installed for it. Then:

```
dotnet run --project XPlatUIDemo.App
```

The window is a plain `Gtk.ApplicationWindow` whose child is a `Gtk.Overlay`: a `Gtk.Picture` is the
main child and a transparent `Gtk.DrawingArea` sits on top of it. Skia paints into a premultiplied
BGRA bitmap (`SKColorType.Bgra8888` with `SKAlphaType.Premul`), which is wrapped in a
`Gdk.MemoryTexture` and handed to the picture; the drawing area is what reports the widget's size and
scale factor and what receives input. The frame is not blitted with Cairo because GTK4 gives a
drawing area a Cairo *recording* surface - there are no pixels to write into - so a bitmap can only
reach the screen as a Cairo source, and Cairo answers a mutable source with a full size snapshot.
GTK keeps the render nodes those snapshots end up in, so every repaint leaked one frame buffer.
`GLib.Bytes.New` copies the frame into memory GLib owns, so a texture stays valid however the Skia
surface behind it is resized; aliasing the pixels with `NewStatic` instead faults inside the GL
driver on the first resize, because the renderer still holds the previous frame's node. The copy is
the price of that - about 1.3 ms of a 4.4 ms frame at 2560x1452 on llvmpipe.

Everything else mirrors the Win32 backend: `Gtk.GestureClick`, `Gtk.EventControllerMotion` and
`Gtk.EventControllerKey` feed `XPlatUiHost.Process*`, and the system appearance is read from
`org.gnome.desktop.interface color-scheme` (falling back to `GTK_THEME` and
`GtkSettings.GtkApplicationPreferDarkTheme`). Context menus are real `Gtk.Popover`s, and the desktop
UI font (`GtkSettings.GtkFontName`) is handed to the library through `XPlatTheme.FontFamily`, so the
controls look native on GNOME.

The same headless switches work, and they run before any GTK type is loaded, so they need no display
at all - no Xvfb, and they work over ssh:

```
dotnet XPlatUIDemoApp.dll --render out.png 1280 820 --dark
```

That ordering is load-bearing rather than tidy. `Gtk.Module.Initialize()` opens the display and
terminates the process with exit code 1 when there is none, and it does so before
`Gtk.Functions.InitCheck()` could report the failure, so anything GTK-dependent in the render path
makes it unusable on a headless machine. The renderer therefore reads neither `GtkSettings` nor
GSettings: it takes the palette from `--dark` / `--light` (light by default) and SkiaSharp's default
typeface instead of the desktop UI font. That also makes CI screenshots deterministic, at the cost of
the render not using the same font as the window does.

Set `XPLATUI_DEBUG=1` to trace host work (frames, buffer reallocation) to stderr.

### On macOS (AppKit)

The macOS build of `XPlatUIDemo.App` is a native macOS application. It targets `net10.0-macos` and
uses Apple's AppKit bindings, so the window, its title bar, the menu bar, the context menus and the
clipboard are all the real thing; only the contents of the window are drawn by the library.

That target framework is why the macOS build has to happen on a Mac: it needs the macOS workload and
the Xcode tools that bundle and sign the `.app`. `Program.cs` selects it with
`$([MSBuild]::IsOSPlatform('osx'))`, so the project is part of `WinFormsApp3.slnx` on every platform
and simply resolves to a different target framework on each. Build it on a Mac, or let the
`macOS build` GitHub Actions workflow do it - that workflow runs on pull requests against `master`
and on demand (`workflow_dispatch`), not on every push, so a branch that is still being worked on
is expected to be built locally:

```
dotnet workload install macos
dotnet build XPlatUIDemo.App/XPlatUIDemo.App.csproj
open XPlatUIDemo.App/bin/Debug/net10.0-macos/osx-arm64/XPlatUIDemoApp.app
```

The macOS workload only accepts an Xcode whose major.minor version matches the one its bindings were
generated against, and the error it raises (`requires Xcode 27.1`) is easy to mistake for a real
problem. `XPlatUIDemo.App.csproj` therefore sets `ValidateXcodeVersion` to `false` for
`net10.0-macos`: a newer Xcode builds and renders this app unchanged, so the exact-match rule only
ever gets in the way on a workstation that has been kept up to date. The guard is a property, so it
can be turned back on per build:

```
dotnet build XPlatUIDemo.App/XPlatUIDemo.App.csproj -p:ValidateXcodeVersion=true
```

Nothing here compiles native code, so a mismatched Xcode cannot actually break the build. The
`macOS build` workflow still selects the Xcode the workload expects (`/Applications/Xcode_27.1.app`
on the `xcode-27` image) rather than relying on either setting.

The bundle is produced by the build itself; its identity lives in `XPlatUIDemo.App/Info.plist`. It is
signed ad-hoc, so a copy downloaded from CI needs `xattr -dr com.apple.quarantine XPlatUIDemoApp.app`
before it will open. The same headless switches work, and they run before AppKit is initialised, so
they work over ssh too:

```
XPlatUIDemoApp.app/Contents/MacOS/XPlatUIDemoApp --render out.png 1280 820 --dark
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
keyboard events are forwarded to `XPlatUiHost.Process*` from the `NSResponder` overrides, a 250 ms
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
the host. `AppKitKeyMap` deliberately does *not* map `Command` onto `XPlatModifiers.Control`: a `Cmd`
combination that the menu bar did not claim is swallowed instead of typing its letter, while
`Control` combinations reach the library as shortcuts. The system UI font (`SF Pro Text`) is handed
to the library through `XPlatTheme.FontFamily`.

Set `XPLATUI_DEBUG=1` to trace frames and buffer reallocation to stderr. It also prints a `stats:`
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
dotnet publish XPlatUIDemo.App/XPlatUIDemo.App.csproj -c Release -r osx-arm64 -p:CreatePackage=false
open XPlatUIDemo.App/bin/Release/net10.0-macos/osx-arm64/XPlatUIDemoApp.app
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

* which rung of §4 the feature sits on, and why it could not sit on a lower one;
* the published size of an application that does not use it, measured on every platform the change
  affects and quoted against the same application before the change;
* anything in the §9 checklist that needed a judgement call, and the judgement.

§9 is the review checklist itself. A reviewer works through it line by line and asks for the answer
rather than inferring it, because the failures it catches are silent ones: a registration that runs
whether or not anything uses it, a `net10.0` assembly that lands in the wrong application, a
reflection path that only fails under AOT. The short form is that nothing new appears on an
always-retained type, nothing runs unless the application opted in, and the pull request carries a
number instead of an assurance.

A feature that ships as its own package needs no multi-platform build. The `Feature pack` workflow
packs every `XPlatUI.Feature.*` project on a macOS runner - `EnableWindowsTargeting=true` brings the
Windows reference pack with it - then opens each package and fails unless it carries a `lib/` folder
and a dependency group for every platform. That check exists because both omissions are silent in
NuGet: the consumer falls back to the `net10.0` one and ships another platform's assembly. §4 of the
policy has the details.
