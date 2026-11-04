# Controls roadmap - what is left

Status: living document. It tracks the controls and control-adjacent features XPlatUI has not built
yet, in the order they are worth building. What has shipped, and how it behaves, is in
[README.md](README.md#design); this file is only the remaining work, so a control is *deleted* from
here when it lands rather than ticked off in place.

Each item is independently shippable: work can stop after any of them with the tree in a working
state, which is the rule [SELECTION-PLAN.md](SELECTION-PLAN.md) already follows.

---

## 1. Where the tiers stand

The tiers were set when the library had twelve controls and they still describe the right order.

| Tier | Contents | State |
| --- | --- | --- |
| 1 - common input | `XPlatCheckBox`, `XPlatRadioButton`, `XPlatProgressBar`, `XPlatGroupBox` | shipped |
| 2 - popup-based | the popup layer (`IXPlatPopupService`, `XPlatPopup`, one popup window per backend), `XPlatComboBox`, `XPlatTabControl`/`XPlatTabPage`, `XPlatToolTip` | shipped |
| 2b - unlocked by the popup layer | in-library menus, `XPlatDateTimePicker`, text-box autocomplete, drag previews | §3 |
| 3 - large list controls | `XPlatVirtualList` (the shared row base), `XPlatListView`, `XPlatTreeView` | shipped |
| 4 - small widgets | `XPlatNumericUpDown`, `XPlatTrackBar`, `XPlatLinkLabel`, `XPlatStatusStrip`/`XPlatToolStrip` | shipped |

Tier 2 is what unblocked the rest: until there was a popup that could leave the window, every
remaining control that wants one (a calendar, a suggestion list, a menu, a drag ghost) was either
clipped by its ancestors or impossible.

## 2. What "done" means

A control is finished when all of the following are true. The last three batches were held to it, and
a reviewer should ask for each one rather than infer it.

- It lives in `XPlatUI/Controls` and contains no platform code. Anything that needs a platform goes
  behind a seam in `XPlatUI/Platform` that a backend implements and a null implementation satisfies,
  so the portable library still builds and runs with nothing installed.
- It derives from `XPlatControl`, or from the closest existing control the way `XPlatCheckBox`
  derives from `XPlatToggleControl`, and follows the conventions the others follow:
  - the constructor sets `TabStop`, `Cursor`, `Padding` and `MinimumSize`; anything that measures its
	own text also sets `AutoSize`;
  - there is a `XPlatXxx(string text)` convenience constructor where text is meaningful;
  - `MeasureContent` answers `AutoSize`, and the paint path uses `XPlatPainter.Fill`,
	`XPlatPainter.DrawBorder` and `XPlatPainter.DrawFocusRing` rather than raw canvas state;
  - colours are read from `XPlatTheme` *at paint time*, so a palette swap recolours a live control
	with no rebuild;
  - events come in `AddXxxHandler` / `RemoveXxxHandler` pairs returning `XPlatHandlerToken<T>`.
- Input is complete: pointer press inside/release inside is a click, hover and press are distinct
  states, `TabStop`/`TabIndex` order it, the keys a native control of that kind would take are
  handled and marked `e.Handled`, and focus draws a ring.
- Disabled is complete: no input, `XPlatTheme.DisabledBackground` / `DisabledForeground`, any press
  cancelled and capture released in `OnEnabledChanged`. The cascade from a disabled ancestor is
  inherited from `XPlatControl` and needs no code.
- Anything time-based uses `CreateTimer`/`XPlatTimer` and stops when there is nothing to do. An idle
  application must run no timers; only the marquee progress bar, tool-tip delays and the caret blink
  are allowed to have one, and each of them stops.
- It has a README section (in the `Design` table as well as its own `###` section) and a demo page
  appended to `Demos` in `XPlatUIDemo.App/DemoUi.cs` - appended, not inserted, because `--page N`
  and the render harness address pages by index.
- It is verified on Windows, macOS and GTK4: a headless `--render` of the new page on all three, plus
  a real-window check wherever the control's value is interaction (a popup, a drag, a keyboard path).
- The pull request names the rung of [FEATURE-COST-POLICY.md](FEATURE-COST-POLICY.md) §4 it sits on
  and says why it could not sit lower.

## 3. Unlocked by the popup layer

These four are pure control work now; the hard foundation is in place.

### 3.1 In-library menus

The biggest consistency win left. The menu *model* is already portable - `XPlatMenu`,
`XPlatMenuItem`, `XPlatContextMenu`, `XPlatMenuMetrics`, `XPlatMenuLayout` and `XPlatMenuRenderer`
contain no platform code - but its *presentation* is not:

| Surface | Today |
| --- | --- |
| Context menu, Windows 11 | Drawn by XPlatUI: `Win32MenuWindow` renders `XPlatMenuRenderer` into a layered popup with captured, blurred acrylic and a drop shadow. This is the target look. |
| Context menu, macOS | Native `NSMenu.PopUpMenu` - system font, system spacing, no icons, no custom rows, no controls inside. |
| Context menu, GTK4 | Native `Gtk.Popover` - the theme's look, not ours. |
| Menu bar | Native on all three, and on Win32 it is recorded and not drawn at all. |

What it needs:

1. Generalise the part of `Win32MenuWindow` that is not menu-specific (layered popup, backdrop
   capture, work-area placement, nested pump, dismissal) so it sits behind the popup service rather
   than being a menu window. `XPlatPopup` already covers placement and dismissal, so this may mostly
   be deletion.
2. Draw the context menu through `XPlatMenuRenderer` in a popup on macOS and GTK4 as well, so all
   three platforms look the same and follow `XPlatTheme`.
3. Draw the menu bar in the window on Windows and Linux. On macOS the app menu bar has to stay
   native: AppKit owns the system bar, and Cmd+Q, Cmd+W and the Edit-menu clipboard items come from
   being in it.
4. Icons and in-menu controls, which the current renderer has no room for (`XPlatMenuMetrics` has a
   check column and a shortcut gap, no icon column).

Open question: replace the native path or keep both. The likely answer is a
`MenuPresentation` option (`XPlatUI` | `Native`) defaulting to `XPlatUI` on Windows and Linux and
native for the macOS bar, with `--classic-menus` staying as the escape hatch it is today.

### 3.2 `XPlatDateTimePicker`

A field plus a calendar in a popup: month grid with a header for month and year, today and clear
affordances, `Value`, `MinDate`, `MaxDate`, `Format` (`Short`/`Long`/`Time`/`CustomFormat`),
`ValueChanged`, and keyboard editing of the field (arrows step the segment under the caret, Up/Down
step the day, `PageUp`/`PageDown` the month).

Notes:

- Culture-aware formatting and parsing come from `System.Globalization`, which the projects already
  pull in (nothing sets `InvariantGlobalization`), so this costs no new dependency - but it is worth
  stating in the pull request, because ICU data is the sort of thing an AOT size review asks about.
- Open question: an editable field that parses typed dates, or a read-only field that only opens the
  calendar. WinForms allows both; the read-only one is a fraction of the work and has no partial-input
  states to get wrong.

### 3.3 Text-box autocomplete

`XPlatTextBox` grows `AutoCompleteMode` (`None`/`Suggest`/`Append`/`SuggestAppend`),
`AutoCompleteSource` (`CustomSource`/`HistoryList`) and `AutoCompleteCustomSource`, with the
suggestion list in a non-focus-taking popup under the caret. Up/Down/Enter/Escape stay with the text
box, exactly as they already do for the combo box's drop-down, and the list is an `XPlatListBox`
filtered as the text changes.

This is the cheapest of the four: it is a re-arrangement of `XPlatComboBox`'s drop-down plumbing and
needs no new primitives.

### 3.4 Drag previews

Two separable halves, and only the first belongs to the library proper:

1. A drag ghost - a popup following the pointer showing a translucent snapshot of the dragged
   content. Non-interactive, so it is close to `XPlatToolTip` with a different placement rule.
2. Drag *out*: starting an OS drag from an `XPlatDataObject`. The drop half exists in
   `XPlatUI.Feature.DragDrop` (`AllowDrop`, `XPlatDragDropRouter`); the outbound half is
   per-platform and is the larger job.

Do (1) first. (2) is worth its own plan document, the way selection got one.

## 4. Deliberately not planned

Recorded so the decision is not re-litigated every time someone asks:

| Not building | Why |
| --- | --- |
| `XPlatDataGridView`, `XPlatPropertyGrid` | A grid is a project of its own: cell editors, span, freeze, per-cell formatting. `XPlatTableLayoutPanel` plus a virtualized list covers the ordinary cases. |
| `XPlatWebView` | A browser engine per platform, and it would dominate the binary - the opposite of the point. |
| MDI | No platform does it well; use separate windows, which the model already supports with `Owner`. |
| VS-style dockable/floatable panels | Large, and the split container plus layout panels cover the common arrangement. |
| Ribbon | Nobody wants it back. |
| Printing / print preview | Belongs to an application, not to a Skia-drawn control toolkit. |

## 5. Suggested order

1. **In-library menus** (§3.1) - the largest consistency win, the renderer already exists, and it
   removes the last place where the same UI looks different per platform.
2. **`XPlatDateTimePicker` and text-box autocomplete** (§3.2, §3.3) - both are popup work now, and
   both are things an application notices missing on day one.
3. **Drag previews** (§3.4) - small, and independent of everything above.
