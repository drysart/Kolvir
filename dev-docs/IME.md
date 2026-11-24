# Text composition (IME) - design and implementation plan

`APPLICATION-MODEL.md` section 10 lists "IME / dead keys / composed input" as a Tier-3 gap with one
sentence beside it and no design behind it. This document is that design. It covers the three
platforms: GTK4 and Win32 are implemented, and macOS is designed here and left open.

Status of this document: **GTK4 and Win32 implemented, macOS designed but not implemented.**
Sections 1 to 5 are the model every platform shares; section 6 is per platform; section 7 is what
proves it; section 8 is the progress table and the questions this design deliberately leaves open.

This file is deliberately ASCII-only, like `OBSERVABLES.md` and `OBSERVABLE-COLLECTIONS.md`: write
"section 3.4" rather than a section sign, and "<-" rather than an arrow.

## 1. The problem

The input pipeline was, and on macOS still is, one-way and stateless. A backend derives characters
from the keyboard and hands them over, and the library has no way to hold text that is not committed
yet.

- **GTK4.** Before this change,
  `Gtk4SkiaView.OnKeyPressed` (`../../src/backends/Kolvir.Backend.Gtk4/Gtk4SkiaView.cs:1092` at the
  baseline commit) mapped the key value to text with `Gtk4KeyMap.MapText(args.Keyval, args.State)`
  and called `Host.ProcessTextInput(text)`. No `Gtk.IMContext` existed in the backend at all.
- **macOS.** `AppKitSkiaView.KeyDown` (`../../src/backends/Kolvir.Backend.AppKit/AppKitSkiaView.cs:782`)
  takes `AppKitKeyMap.ToText(theEvent)` after the key and the Command/Control checks. The view does
  not adopt `NSTextInputClient`, so `interpretKeyEvents:` is never called.
- **Windows.** Before this change, `Win32WindowBackend` had a case for `WM_CHAR` (handled by
  `OnChar`) and none for any `WM_IME_*` message in `HandleMessage`: a key became a single UTF-16
  unit, and one past the BMP arrived as two lone surrogates. Nothing asked the input method for a
  composition, so a dead key, a Compose sequence or an IME syllable reached the field as nothing or
  as the raw key text.

What that costs is not a missing nicety. A dead key on a `us-intl` layout, a Compose sequence, an
accent typed through Option on macOS, or a syllable typed through ibus or fcitx, all of it produces
either nothing or the raw key text. There is no preedit to show the user what is still being
composed, no boundary that says "this part is final", and no way for a control to refuse or accept
composition. For a CJK user, an accented-language user on a dead-key layout, or anyone typing
through a system input method, a text box is not usable.

The fix is a design change rather than a backend patch: a model for uncommitted text, the seam a
platform input method drives it through, and then one integration per platform.

## 2. What exists today

The pipeline below is what every backend feeds, and it is what a commit has to join:

```
platform key event
        |
        v
UiHost.ProcessKeyDown(key, modifiers)            UiHost.cs:1369
        |  an input sink, the menu bar and a shortcut see it first
        v
focused Control.OnKeyDown                        Control.cs
        |  when the key is not consumed, the backend derives text
        v
UiHost.ProcessTextInput(text)                    UiHost.cs:1404
        |
        v
focused Control.OnTextInput(TextInputEventArgs)  Control.cs:3291
        |
        v
TextBox.InsertText -> ReplaceRange                TextBox.cs
        |
        +-- undo history, validation, MaxLength
        +-- OnTextChanged -> TextChanged, multi-line layout, autocomplete
```

`TextInputEventArgs` carries only `Text` (`Core/EventArgs.cs`): there is no notion of a composition,
of a caret inside one, or of a commit that differs from typing. Every control treats the text it
receives as final, which is exactly the property a commit has to preserve.

## 3. Decisions

| Decision | Rationale |
|---|---|
| **The library draws the preedit.** | No platform window is created for the uncommitted text. One visual language, the field's own font and colour, and it works on the headless backend where a headed run cannot be automated. |
| **Preedit is not part of `Text`.** | `Text`, `SelectionStart`, `SelectionLength`, `TextChanged`, undo and validation must not see typing that can still be cancelled. A preedit is a proposal, not a value; putting it in `Text` would make every consumer observe text that may never exist. |
| **Commit is the ordinary text path.** | `CommitComposition` clears the preedit and then calls the same `ProcessTextInput` a keystroke uses, so a commit is offered to the input sinks first and then to the focused control. A commit is indistinguishable from typing to every sink, every control, undo, validation, `MaxLength` and the autocomplete handler. One insertion path, no duplicated logic, no second source of truth, and a sink that eats typed text eats commits too. |
| **Replacement ranges are deferred.** | v1 always commits at the caret, replacing the selection, exactly as typing would. Anchoring a commit to a range is what context-aware IMEs and clause styling need, and it needs the clause model first. |
| **No `ImeMode` in v1.** | There is no consumer yet, and a field that tells the platform "this is numeric" or "this is Kana" is a policy decision that should be made against real use. |
| **Composition is opt-in per control.** | The `Control` hooks default to "not interested". A non-text control ignores composition, and still receives the commit as ordinary text, because the commit is the ordinary text path. |
| **Movement and editing keys cancel.** | The library owns a key before the platform input method sees it (`ProcessKeyDown` runs first, by design, so shortcuts and menu mnemonics keep working). A key that moves the caret or edits the text therefore cancels the preedit first, then acts. |
| **Single-line scrolls, multi-line clips.** | A single-line field scrolls far enough to keep the preedit visible, the same way it follows the caret. A multi-line field draws the preedit on the caret's line and lets it clip instead of re-flowing the document for uncommitted text; that is the v1 simplification. |

## 4. The composition model

### 4.1 The host API

Four methods on `UiHost`, beside the existing `Process*` family, each wrapped in `Guard` like its
neighbours and each going through the focused control:

```csharp
// Asks the focused control whether it takes composition at all. False when nothing is focused,
// or when the control declines (a read-only or disabled field, a button, a list).
public bool BeginComposition();

// The preedit, and where the platform's caret is inside it. False when composition was refused.
public bool UpdateComposition(string text, int caretIndex);

// Clears any preedit on the focused control, then routes the text as ordinary input.
public void CommitComposition(string text);

// Drops the preedit and leaves nothing behind.
public void CancelComposition();
```

`CommitComposition` clears the preedit and then calls `ProcessTextInput`, the same entry point a
keystroke uses. That is what makes the commit indistinguishable from typing: an input sink sees it
first - the case the invariant exists for is a sink owning the keyboard - and the focused control
sees it second. There is no second insertion path, and a sink that eats typed text eats commits too.

### 4.2 The control hooks

`Control` gets protected virtual counterparts next to `OnTextInput`:

```csharp
protected virtual bool OnCompositionStarted() => false;
protected virtual bool OnCompositionUpdated(string text, int caretIndex) => false;
protected virtual void OnCompositionCancelled() { }
```

Started and updated are gated on `EffectiveEnabled`, the way `RaiseKeyDown` and `RaiseTextInput`
are. Cancelled is not: a field that is disabled mid-composition must still be able to clear what it
holds.

The default is false and no-op, which is the whole opt-in mechanism. A control that does not
override them is never touched by a preedit, and its commit arrives through `OnTextInput` exactly as
typed text does.

### 4.3 A focus change cancels

`UiHost.SetFocus` cancels the previous control's composition before it raises lost-focus. A
composition belongs to the control it started on: leaving the field ends it, and the candidate
window the platform may be showing is reset by the backend's own focus handling (sections 6.1 and
6.2).

### 4.4 `TextBox`

The preedit string and its internal caret index live beside the committed value:

- `Text`, `SelectionStart`, `SelectionLength`, `TextChanged`, undo and validation are untouched
  while composing.
- The preedit is drawn at the caret, in the field's text colour, with the field's font and a single
  underline. The caret is drawn after it, so the visual order is committed text, preedit, caret.
  The placeholder is hidden while a preedit is shown, so only one text sits at the caret.
- A single-line box scrolls far enough that the preedit's end is inside the field. A multi-line box
  draws the preedit on the caret's line from the caret and clips.
- A movement or editing key cancels the preedit first and then does its normal work.
- `MaxLength`, sanitising, the multi-line line-break normalisation and the read-only check all
  apply to the commit, because the commit is an ordinary insert.

### 4.5 Invariants

These are the contract, and each one has a headless test (section 7.1):

1. A preedit never reaches `Text`.
2. A commit is exactly as if the same characters had been typed: same text, same caret, same undo
   entry, same `TextChanged` count, same `MaxLength` clamp.
3. A cancel leaves no trace and adds no undo entry.
4. `MaxLength` applies to the commit, not to the preedit.
5. Autocomplete is driven by commits only, never by updates.
6. A focus change cancels.
7. A control that refuses composition still receives the commit as text.
8. Selection and caret do not move while the preedit grows.

## 5. The caret seam

A platform input method places its candidate window against the caret. One call on the host answers
it, and each backend converts from there into the space its platform wants:

```csharp
// UiHost: the caret rect of the focused control, in host (logical) coordinates.
public bool TryGetCaretBounds(out SKRect bounds);
```

The control side is an internal override, `Control.TryGetCaretBounds`, whose default answers the
control's own bounds. `TextBox` overrides it with the same arithmetic the autocomplete feature
already uses to hang its list under the caret
(`TextBoxAutoCompleteState.cs:685-710`):

```
origin = GetPositionInHost()                     Control.cs:713, walks parents and scroll offsets
caret  = CaretBounds                             TextBox.cs:234 (multi-line: TextBoxMultiline.cs:318)
rect   = origin + caret * EffectiveScaleMultiplier
```

The result is in host logical pixels, not device pixels. What each consumer does with it:

| Platform | Space the API wants | Conversion from host logical |
|---|---|---|
| Win32 | Client coordinates of the window (`COMPOSITIONFORM.ptCurrentPos`) | Multiply by the window's scale for device pixels; the origin is already the client origin. |
| macOS | Screen coordinates, bottom-left origin | Multiply by the backing scale, flip Y against the view's height, add the window's screen origin. |
| GTK4 | Widget coordinates of the client widget (`gtk_im_context_set_cursor_location`) | GTK positions widgets in logical pixels, so the host rect is already in the right space. |

A non-text control answers its own bounds rather than nothing, so a backend that asks while a
button has the focus still gets a sensible rectangle to anchor a candidate window to.

## 6. Per platform

### 6.1 GTK4 (implemented)

The view wraps a `Gtk.DrawingArea` (`_area`, exposed as `InputArea`, `Gtk4SkiaView.cs:359`), so the
drawing area is the client widget of the input context.

- **Context.** `Gtk.IMMulticontext.New()` with `SetClientWidget(_area)`, created once per view in
  the constructor and disposed with it. The multicontext is the object that selects an
  implementation from `GTK_IM_MODULE` and falls back to the simple context; `Gtk.IMContext` is the
  abstract base (GTK4 removed `gtk_im_context_new`), and only the multicontext consults the
  variable. Pinning `Gtk.IMContextSimple` instead would compile and work, and would make
  `GTK_IM_MODULE` meaningless, which is exactly what the live proof relies on.
- **Preedit reporting.** `UsePreedit` decides whether the context reports uncommitted text to the
  application to draw, and a plain `DrawingArea` is not a `GtkEntry`, so the default is not taken
  on trust. Measured on the Linux host with `GTK_IM_MODULE=gtk-im-context-simple` and a `us-intl`
  layout: with `SetUsePreedit(true)` the dead key emits `PreeditStart`, `PreeditChanged` with the
  pending accent, and `PreeditEnd` on the commit; with `SetUsePreedit(false)` no preedit signal
  occurs at all and the dead key is committed as the bare accent. The view therefore sets `true`
  explicitly: without it, commits still arrive and the integration looks correct while the preedit
  never appears.
- **Key routing.** `OnKeyPressed` keeps `ProcessKeyDown` first, exactly as it is today. What the
  library did not consume goes to the input context:

  ```csharp
  if (key != Key.None && Host.ProcessKeyDown(key, modifiers)) return true;
  if (_im.FilterKeypress(sender.GetCurrentEvent())) return true;
  return false;
  ```

  This replaces the `Gtk4KeyMap.MapText` fallback. The context answers by emitting `Commit` for
  final text, and `PreeditStart`/`PreeditChanged`/`PreeditEnd` for uncommitted text. Keys the
  library owns (shortcuts, Tab, the editing keys) are never seen by the input method, which is why
  a movement key cancels the preedit (section 4.4) rather than being consumed by it.
- **Signals.** `Commit` maps to `Host.CommitComposition(args.Str)`; `PreeditStart` to
  `Host.BeginComposition()`; `PreeditChanged` reads the preedit with
  `GetPreeditString(...)` and calls `Host.UpdateComposition(text, caret)`; `PreeditEnd` calls
  `Host.CancelComposition()`, which is idempotent because a commit already cleared the preedit.
- **Caret units.** GTK documents the preedit cursor position but the unit (characters or UTF-8
  bytes) is not something this design takes on trust. Measured with a `us-intl` dead key: the
  preedit is the acute accent, one character and two UTF-8 bytes, and the context reported a cursor
  position of 1, so the unit is characters. The implementation still converts defensively: the
  value is clamped into the string, and a value past the character count is treated as a byte
  offset and mapped back to a character boundary, keeping a surrogate pair whole.
- **Candidate window.** `SetCursorLocation` is pushed from `Host.TryGetCaretBounds` in widget
  coordinates. It is no longer pushed only when a preedit starts: the location is refreshed when the
  drawing area takes focus, immediately before a key is offered to the context, after the library
  consumes a key that can move the caret, after a press, a release, a wheel turn and a commit. An
  input method decides where its candidate window goes on the turn it begins a composition, so a push
  made only from `PreeditStart` arrives one turn too late and the first placement uses whatever the
  context held before. Refreshing at those points removes that staleness. It is *not* an observation
  that the window moved: the Linux host has `ibus` with no engines at all (`ibus-hangul`,
  `ibus-pinyin`, `ibus-anthy` and `ibus-libpinyin` are absent) and `sudo` needs a password, so no
  candidate window can be produced there, and `gtk-im-context-simple`, the context the live proof
  used, draws none by design. Section 7.3 records the same limit.
- **Focus.** A `Gtk.EventControllerFocus` on the drawing area does `FocusIn` when it gains focus and
  `CancelComposition`, `FocusOut` and `Reset` when it loses it, so a composition cannot outlive the
  focus.
- **Popups.** `Gtk4PopupWindow` (`:362`) forwards its keys to the owner host and is deliberately
  left alone: a non-activating popup never takes the input focus, so a composition cannot begin
  there, and its text arrives through the owner host's ordinary text path.
- **Fallback, and what it means here.** Whatever a composition or the emoji chooser inserts is drawn
  by the core text pipeline, which since this branch splits a string into runs and draws a character
  with a face that covers it (`DEVELOPER-GUIDE.md`, fonts). On GTK4 that is the same code as on
  Windows, so an emoji or a Japanese word should now be glyphs rather than boxes - but it is
  unverified on this host, for the reasons the candidate-window bullet and section 7.4 give: no engine
  for a composition to come out of, and no emoji insertion exercised.

### 6.2 Win32 (implemented)

The window procedure gets four messages. `WM_IME_SETCONTEXT` and `WM_IME_STARTCOMPOSITION` are
offered to `DefWindowProcW` after the library has done its part; `WM_IME_COMPOSITION` never is,
because the default handler is one of the ways a committed result becomes a `WM_CHAR`.

| Message | Value | What the backend does |
|---|---|---|
| `WM_IME_SETCONTEXT` | `0x0281` | Clear `ISC_SHOWUICOMPOSITIONWINDOW` (`0x80000000`) in `lParam` so the IME does not draw its own composition window over a library that draws the preedit itself, then pass it to `DefWindowProcW`. The flag is a DWORD in the low half of the `LPARAM`, so it is masked there and the result widened back as `lParam` is. |
| `WM_IME_STARTCOMPOSITION` | `0x010D` | `ImmGetContext`, `Host.BeginComposition()`, `ImmSetCompositionWindow` at the caret, `ImmSetCompositionFontW` from the focused control's font, `ImmReleaseContext`, then `DefWindowProcW`. When the control refuses - read-only, disabled - the input method is told with `ImmNotifyIME(NI_COMPOSITIONSTR, CPS_CANCEL)` instead, because declining in the host alone leaves the input method's own window on screen over a field that will never take the text. |
| `WM_IME_COMPOSITION` | `0x010F` | `GCS_COMPSTR` (`0x0008`): read the composition string and `GCS_CURSORPOS` (`0x0080`), then `Host.UpdateComposition`. `GCS_RESULTSTR` (`0x0800`): read the result string and `Host.CommitComposition`. Then `ImmSetCompositionWindow` from the caret seam so the candidate window follows the caret. |
| `WM_IME_ENDCOMPOSITION` | `0x010E` | `Host.CancelComposition()`, idempotent because a commit already cleared the preedit. |

Imm32 calls: `ImmGetContext`, `ImmReleaseContext`, `ImmGetCompositionStringW` (sized with a null
buffer, then read), `ImmSetCompositionWindow`, `ImmSetCompositionFontW`, and `ImmNotifyIME` with
`NI_COMPOSITIONSTR` (`0x0015`) and `CPS_CANCEL` (`0x0004`). The caret goes into
`COMPOSITIONFORM.ptCurrentPos` under `CFS_POINT` (`0x0002`), in client coordinates. The constants
are copied from `winuser.h` and `imm.h` rather than from memory, and two of them are easy to get
wrong: `CFS_POINT` is `0x0002` while `0x0020` is `CFS_FORCE_POSITION`, and `NI_COMPOSITIONSTR` is
`0x0015` while `0x000B` is not an `NI_` value at all.

Details that are not visible from the message table:

- **Context per message.** Every handler takes `ImmGetContext` for one message and releases it in a
  `finally`, so a composition can never outlive the call that asked about it. A window with no
  context (`IntPtr.Zero`) still reaches the host: the preedit and the commit are the library's, and
  only the positioning is lost.
- **A composition that skips its start.** Some input methods send `WM_IME_COMPOSITION` without
  `WM_IME_STARTCOMPOSITION` first, so the first `GCS_COMPSTR` opens the composition if none is open.
- **A consumed key resets the input method.** The library sees a key before the input method does, so
  a key it consumes never reaches it. The Win32 form of the GTK view's `IMContext.Reset()` is
  `ImmNotifyIME(NI_COMPOSITIONSTR, CPS_CANCEL)` plus `Host.CancelComposition()` when the window
  procedure's key path reports that the UI took the key.
- **Font.** `ImmSetCompositionFontW` is filled from `UiHost.FocusedControl.EffectiveFont`: the
  field's family, size scaled to device pixels, weight and italic, so the input method's own window
  matches the field instead of the system default. The face-name buffer is shared with the font
  dialog (`Win32LogFont`), which is why that helper moved out of the dialog service.
- **The insertion point, for an input method's own UI.** The emoji panel (Win+Period), the touch
  keyboard and a candidate window all ask Windows where the caret is before they appear, and Windows
  answers from two places that a library drawing its own caret has to fill in.
  - **The thread's system caret.** `CreateCaret(hwnd, IntPtr.Zero, 1, 1)` and `SetCaretPos`, with no
    `ShowCaret` at all: the library draws the caret, and a second blinking one would be a regression.
    The caret exists only while the window holds the keyboard focus, is recreated by a second Kolvir
    window that takes it (a caret belongs to one window and a thread has one), and is destroyed with
    the window. It is a push rather than a pull, so the sync runs after every message rather than at
    a list of call sites: a key, a click, a composition step and a relayout can each move the caret.
  - **`WM_IME_REQUEST` with `IMR_QUERYCHARPOSITION`.** An `IMECHARPOSITION` with `dwSize` set, the
    caller's `dwCharPos` left as it was, `pt` in screen coordinates, `cLineHeight` from the field's
    font and `rcDocument` the client rectangle on screen; answering means returning the structure's
    size. Every other `IMR_` value reaches `DefWindowProc` untouched, because swallowing a request
    the backend does not implement would hide it.

  Both read the same seam, `Host.TryGetCaretBounds`, so they cannot disagree about where the caret
  is. Measured on this machine before the caret existed: `GetGUIThreadInfo` reported
  `hwndCaret == 0` with an empty `rcCaret` while the seam answered a caret, which is why the emoji
  panel anchored to the window instead of the field. After it, `hwndCaret` is the focused window and
  `rcCaret` is the seam's point in client coordinates.
- **Nothing focused still answers.** `UiHost.TryGetCaretBounds` answers nothing when no control has
  the focus, and a window that refuses the request hands the shell its no-anchor fallback, which is
  the bottom-right corner of the screen. Measured with the demo app: its startup page focuses no
  text box, `IMR_QUERYCHARPOSITION` arrives about 50 ms after the window is shown, and the first
  version answered `DefWindowProc`. The client area's top-left, in screen coordinates, is the honest
  answer for a window whose controls hold no caret, so that is what it answers now, with the theme's
  line height and the same document rectangle.
- **The answered point is what places the panel.** Measured rather than assumed, because a first
  attempt was ambiguous: answering `100,100` put the panel in the screen's top-left corner, which is
  also where a no-anchor fallback would put it. Answering `1900,1000` then put it in the middle of the
  screen, with the window at a different origin and the pointer elsewhere, so the panel really does
  position from `IMR_QUERYCHARPOSITION`. That rules out both the pointer hypothesis and the idea that
  the panel uses TSF text extents instead (section 7.4).
- **The composition form follows the caret from the moment there is one.** `ImmSetCompositionWindow`
  is written on the caret sync in `SyncCaret`, which runs in the `finally` of `HandleMessage`, so one
  place covers keys, clicks, composition messages and relayout. It runs only when the rounded caret
  point changed - the same condition `SetCaretPos` already uses - so a message that does not move the
  caret costs nothing, and it does not run at all when no control has a caret. This was the root cause
  of a real defect: the form used to be written only inside a composition, which on a machine with no
  IME never happens, so it sat at the context default - `ptCurrentPos` at the client origin with a
  zero `rcArea`. The emoji panel reads this form, which is why the **first** open of a run landed at
  the window's top-left corner and every later one, after a composition had started and our handler
  had written the form, landed at the caret. Section 7.4 records how that was found. `ImmSetCandidateWindow`
  is still never called; nothing has needed it.
- **The trace.** A run can be told to write every IME message to a file, which is what decides between
  "the application is never asked" and "it answers and is ignored" - different problems with different
  fixes. `KOLVIR_IME_LOG=1` writes `kolvir-ime.log` beside the executable, or the variable holds a
  path. Each line carries the message, `GetFocus()` against the window, the `IMR_` value, and for
  `IMR_QUERYCHARPOSITION` the incoming `dwSize` and `dwCharPos` read before anything is written, then
  the answer. It is off by default and costs one environment read per IME message when it is.
  Two switches remain, both diagnostic and both off by default. `KOLVIR_IME_SHOW_CARET=1` shows the
  one-pixel caret once it is positioned, which sets the only visibility flag the thread exposes
  (`GUI_CARETBLINKING`). `KOLVIR_IME_TEST_POINT=x,y` answers `IMR_QUERYCHARPOSITION` with exactly that
  screen point while filling the rest as usual, and says so on the answer line: a run at a point
  nowhere near the field decides whether the shell uses the point at all. Every line that reports an
  answer also carries the mouse pointer's position, because a panel that fell back to the pointer would
  have explained a fixed screen position, and that can only be checked if the pointer is recorded at
  the same moments. The composition form was behind a third switch while it was an experiment; it is
  behaviour now, and the switch was removed rather than left as a way to turn the fix off.
- **The key path is traced too.** A trace of the `WM_IME_*` messages alone cannot tell "the input
  method was bypassed" from "the keystroke never reached the window": both look like silence. So the
  message pump logs the three lines that separate them, whenever the trace is on - `pump WM_KEYDOWN
  vk=0x4E swallowed=False` for the key and whether the library consumed it before `TranslateMessage`
  could hand it over, `pump WM_CHAR char=0x006E 'n'` for the character translation produced, and `pump
  WM_IME_CHAR char=0x3053 'こ'` for a character an input method produced itself. A bypassed input
  method reads as `WM_KEYDOWN` then latin `WM_CHAR`; a working one reads as `WM_KEYDOWN` then
  `WM_IME_*`; a key the library swallowed reads as `swallowed=True` with nothing after it. This is the
  instrument the first real composition attempt needs, and a test pins it (section 7.3, item 13).
- **The trace proves it is running, twice over.** The first version of it wrote only when an IME
  message arrived, and the first real run produced no file at all - which is indistinguishable from
  the feature not being in the build, the variable not reaching the process, or the panel never
  messaging the window. An empty log is not evidence of absence, so the trace now has two things that
  do not depend on an input method existing. A **startup line** is written as soon as the window
  exists: `trace enabled pid=... hwnd=... class=... exe=... log=... heartbeat=500ms`, so the file
  existing proves the instrumented build ran, that the variable arrived, and where it wrote. And a
  **heartbeat** is written every 500 ms while tracing is on, carrying `GetGUIThreadInfo`'s
  `hwndCaret` and `rcCaret`, `GetFocus()`, and whether a composition is live. The heartbeat is what
  makes a run correlatable in wall-clock time: a gap in it around the instant Win+Period was pressed,
  with no IME line in the gap, is positive evidence that the panel never messaged this process, which
  is the discriminator an empty file cannot give. Both are switched on with the log, never on their
  own, because a heartbeat without the log has nowhere to go and a log without the heartbeat cannot
  be shown to be alive.
- **`dwSize` is respected.** A request whose incoming `dwSize` is smaller than the structure is
  refused with `0` and left untouched, rather than written past what its caller allocated. Gecko and
  Chromium both discard such an answer; real callers send the full size, which the trace confirmed.
- **Text the primary face does not cover needs a fallback face to be legible.** A committed Japanese
  word drawn in a Latin face is a row of missing-glyph boxes, which is what the user saw on the first
  successful commit; the fix is in the text pipeline rather than in this backend (see the fallback
  section of `DEVELOPER-GUIDE.md`), because every control that draws a string has the same problem. The
  same applies to an emoji inserted through the panel: it is a character like any other, and it draws
  from a colour emoji face where the platform has one. Measured on this machine - 533 of 628 inked
  pixels chromatic - and then **seen by the person testing**: `かんじ` rendered as glyphs and 👌
  rendered in colour in the demo's field. Nothing is claimed for macOS or Linux colour emoji.
- **The caret moves by character, not by code unit.** The same run found that the arrow keys stepped
  into the middle of an emoji, so the caret jumped back and forth once the fallback face gave that
  half-character a width of its own. That is a defect of the text field rather than of this backend -
  stepping by a UTF-16 code unit was always wrong - and it is fixed in the core field, where
  `DEVELOPER-GUIDE.md` describes it. It is recorded here because an input method is what puts
  characters an index cannot safely step through into the field in the first place.
- **A refusal is told to the input method, not just the host.** `TextBox.OnCompositionStarted`
  answers false for a read-only or disabled field, so `Host.BeginComposition()` returns false and the
  library holds no preedit - which is invisible to the input method. It started a composition because
  it was asked to, and answering the message while staying quiet left its own window on screen over a
  field that would never accept the text: a person typing into the read-only field saw the input
  method's window appear, which no test had asked about because no test could. The handler now sends
  `ImmNotifyIME(context, NI_COMPOSITIONSTR, CPS_CANCEL, 0)` on a refusal, logs it as
  `WM_IME_STARTCOMPOSITION refused ... composition cancelled`, and while no composition is live a
  `GCS_COMPSTR` update and a `GCS_RESULTSTR` result are dropped and logged rather than shown or
  inserted. A start message that arrives *from* the cancel is not answered again, so an input method
  that re-offers a composition cannot ping-pong the window procedure; that case would log
  `refused while already refusing`.
  The narrower fix was chosen over `ImmAssociateContext(hwnd, IntPtr.Zero)`, which would disable the
  input context for the whole window: the context also answers `WM_IME_REQUEST`, so disabling it would
  take the emoji panel's positioning with it, and every focus change to a writable field would then
  have to re-associate it. Cancelling one composition leaves the window's context alone.
  `WM_IME_ENDCOMPOSITION` arriving as a consequence of the cancel is harmless: that handler clears
  `_composing` and calls `Host.CancelComposition()`, which is idempotent because there was no preedit
  to clear. Whether a real input method answers the cancel with an end-composition message is not
  measured here (section 7.5), and neither is whether the input method's window actually goes away -
  only the user's re-run of step 7 can say that.
- **Popups.** `Win32PopupWindow` and `Win32MenuWindow` are deliberately left alone, for the reason
  section 6.4 gives: a non-activating popup never takes the input focus, so no composition can begin
  there.

**What a TSF input method needs from a legacy window, as far as this machine can show.** The Japanese
IME here has no `IMM32` file (`ImmGetIMEFileNameW` and `ImmGetDescriptionW` are both empty for
`00000411`), so it can only reach an app like this one through the CUAS bridge that `msctf.dll`
provides. The measurements about that bridge, kept apart from what is still assumed:

- A focused window has an IME context, and `ImmGetContext` per message is enough for every call this
  backend makes - including `ImmSetCompositionStringW`, which stores a string once the context is the
  thread's active one (section 6.2, measurement above). `ImmAssociateContext` at creation is not
  needed for that: the default context exists as soon as the window has focus.
- Posted `WM_KEYDOWN`s taken through `TranslateMessage` produce an ordinary `WM_CHAR` and no
  composition at all, with either the Japanese or the English layout, and `ImmSetOpenStatus(himc, TRUE)`
  leaves `ImmGetOpenStatus` false and the conversion mode at `IME_CMODE_NATIVE`. Nothing in the
  `IMM32` surface turns Japanese input on for this window.
- The same thing happens in **Notepad**, which is the decisive control: the machine's Japanese IME does
  not compose in a stock application either. So the absence of `WM_IME_STARTCOMPOSITION` in a Kolvir
  trace says nothing about this backend, and neither the key path nor the
  `ISC_SHOWUICOMPOSITIONWINDOW` handling can be blamed for it.
- Unverified until the IME composes somewhere, and worth knowing in this order. First, whether
  clearing `ISC_SHOWUICOMPOSITIONWINDOW` suppresses a CUAS-bridged TSF IME's own preedit UI - the flag
  names the composition window rather than the candidate window, and if a trace ever shows a preedit in
  the readout with no input method UI on screen, this is the first thing to revert. Second, whether a
  real composition needs the context made active, as the synthesized string did. Third, whether CUAS
  wants anything else of the window. Answering `WM_IME_REQUEST` is not a precondition: Flutter never
  answers it and composes.
- For the record: one traced run with no composition running was asked `IMR_QUERYCHARPOSITION` **123
  times in 71 seconds** where earlier runs were asked four to six times. Nothing in this backend's
  per-message path calls into `Imm32` when the caret has not moved, so that polling is not ours. The
  next trace should check whether those requests line up with the 500 ms heartbeat (142 ticks in the
  same 71 seconds) or are independent of it; if they are ours, the cost is worth knowing.

Two hazards, both fixed in this branch:

1. **A commit also produces `WM_CHAR`.** Translating a `GCS_RESULTSTR` composition produces an
   ordinary `WM_CHAR` for the same text, so committing in the window procedure and letting `WM_CHAR`
   through inserts the text twice. This is the same hazard the message loop documents for keys
   (`Win32ApplicationBackend.cs:543`: "Swallowing the key here ... keeps `TranslateMessage` from also
   turning it into a `WM_CHAR`, which would double up text input"). The fix is in `OnChar`: a commit
   appends its text to a pending queue and `OnChar` drops a `WM_CHAR` whose character matches the head
   of it. Matching is character by character rather than by count, so a `WM_CHAR` that is not the
   commit's own text clears the queue and is delivered; a `WM_KEYDOWN`, a `WM_SYSKEYDOWN` and a new
   composition clear it too, so a leftover expectation cannot swallow a character the user typed.
   The test in section 7.3 fails with six characters against three if this is removed.
2. **`OnChar` truncated astral characters.** `OnChar` cast `(char)(int)wParam`, which turns a
   surrogate-pair code point into a lone surrogate. `WM_CHAR` carries UTF-16 code units, so a
   character past the BMP genuinely arrives as two messages: `OnChar` now holds the high half and
   emits the pair when the partner arrives, and drops a lone half. A composition commit is the
   realistic way to get such a character into a window.

**Measured, versus assumed, on this machine.** These were re-measured with the Japanese language pack
and Microsoft IME installed, which the first runs did not have. The correction matters more than the
result, because the first reading of the API was wrong about *why* the reads came back empty:

- `ImmGetProperty(hkl, IGP_SETCOMPSTR)` is `0` for `00000409` and `7` for `00000411`. The earlier
  conclusion - "the installed layout does not support setting a composition string at all" - was a
  property of the English layout, not of `ImmSetCompositionStringW`. `ImmIsIME` answers TRUE for both
  layouts, so neither "no context" nor "not an IME" is a usable test.
- `ImmGetIMEFileNameW` returns an empty name for `00000411` too. The Microsoft IME on Windows 11 is a
  TSF text service rather than an `IMM32` `.ime` file, and `IMM32` reaches it through the CUAS bridge
  in `msctf.dll`. That is why the name is empty, and it is the reason a message that never arrives is
  a question about the bridge rather than about a missing IME.
- `ImmSetCompositionStringW(SCS_SETSTR)` still stores nothing on an ordinary context. It stores the
  string when the window has the focus **and** the context has been made the thread's active one with
  `ImmSetActiveContext(hwnd, himc, TRUE)`; then `GCS_COMPSTR` reads back `こん` and `GCS_CURSORPOS`
  reads `2`. The zero-byte reads recorded by the first probe were a call-shape and focus result, not
  proof that the string cannot be read.
- Set that way, the IME drives the real message path, in this order: `WM_IME_STARTCOMPOSITION`,
  `WM_IME_COMPOSITION` with `lParam` `0x1B8` (the preedit), and then, on
  `ImmNotifyIME(himc, NI_COMPOSITIONSTR, CPS_COMPLETE)` - what the confirm key does - a
  `WM_IME_COMPOSITION` with `lParam` `0x1C00` carrying `GCS_RESULTSTR`, one `WM_IME_CHAR` per committed
  character, and `WM_IME_ENDCOMPOSITION`. `DefWindowProc` turns each `WM_IME_CHAR` into a `WM_CHAR`,
  measured arriving after `ImmNotifyIME` returns: `WM_CHAR` for `こ` and `ん`, from a commit that had
  already delivered them. That is the doubling hazard above in the input method's own words, and it is
  why the suppression is keyed on the committed text rather than on a flag.
- Not measured here, and as unverified as on GTK4: the candidate window's position and appearance, the
  clause attributes (`GCS_COMPCLAUSE`, `GCS_COMPATTR`) and the IME's own preedit styling. Those are
  the checklist in section 7.5.
- Measured with the trace on, against a real focused window: the shell
  does ask. `WM_IME_REQUEST` with `IMR_QUERYCHARPOSITION` arrived four to five times within 300 ms of
  the window taking the focus, every time on the focused window with `GetFocus()` matching, with
  `dwSize` = 36 and `dwCharPos` = 0, and our answer was `pt` in screen coordinates equal to the seam
  converted by `ClientToScreen`, `rcDocument` the client rectangle on screen and `cLineHeight` 35.
  So the request is answered correctly. Whether the shell then places its emoji panel with that answer
  is the one thing this machine could not be made to show (section 7.4), and the trace is what a
  report from a real session settles.

### 6.3 macOS (designed, not implemented)

`AppKitSkiaView` adopts `NSTextInputClient` and answers:

- `insertText:replacementRange:` -> `Host.CommitComposition`.
- `setMarkedText:selectedRange:replacementRange:` -> `Host.BeginComposition` (once) then
  `Host.UpdateComposition` with the marked string and the selected range's location.
- `unmarkText` -> `Host.CancelComposition`.
- `hasMarkedText`, `markedRange`, `selectedRange` -> the field's state.
- `firstRectForCharacterRange:actualRange:` -> the candidate-window seam, from
  `Host.TryGetCaretBounds`, converted to screen coordinates (backing scale, Y flipped against the
  view's height, plus the window's screen origin).
- `doCommandBySelector:` -> forwards a key the input method declined as a command.
- `attributedSubstringForProposedRange:actualRange:` and `validAttributesForMarkedText` -> the
  minimum a conforming client must answer; the first returns nothing useful (the library holds its
  own text) and the second an empty array, which is legal.

`KeyDown` changes shape but not order: `ProcessKeyDown` first, then the Command check, then the
Control check, and only what is left goes to `InterpretKeyEvents`, which is what drives the input
context. Dead keys and Option compositions then arrive as marked text and a commit instead of the
raw key text; the existing `AppKitKeyMap.ToText` fallback is removed from that path.

### 6.4 Why popups are out of scope

A platform input method attaches to the widget that holds the input focus. A non-activating popup
(menus, suggestion lists, tooltips) never takes focus, so no composition can begin on it, and the
popup's keys are forwarded to the host that owns it, where the focused control's own composition
already applies. Adding an input context per popup would create a second, competing composition for
the same keyboard.

## 7. Verification

### 7.1 Headless tests

The core is pinned by headless tests that call the four host methods and the caret seam directly,
because that contract is the same on every platform and no backend is needed to state it:

1. Each method routes to the focused control, and does nothing when nothing is focused.
2. A control that refuses composition still receives the commit as text.
3. A preedit never appears in `Text`.
4. A commit inserts at the caret exactly as typing does: same text, caret, undo entry, `MaxLength`
   clamp and `TextChanged` count.
5. A cancel restores the value exactly and adds no undo entry.
6. Selection and caret do not move while an update changes the preedit.
7. Autocomplete stays closed on an update and responds on a commit.
8. A focus change cancels.
9. The caret seam answers the caret for a text box at a known position, and the control's bounds for
   a non-text control.

### 7.2 The live GTK run

The GTK wiring can only be proved on a Linux host with a display. `GtkIMContextSimple` needs no
daemon: a dead-key layout (`us-intl`) or a Compose sequence (`compose:ralt`) drives exactly the
`PreeditChanged`/`Commit` path an ibus or fcitx session drives for CJK, so
`GTK_IM_MODULE=gtk-im-context-simple` plus one of those layouts is enough. Under X11, `xdotool` can
send the dead key or `Multi_key` and the following letters; where synthetic input is unavailable,
the run is done by hand.

Two things are measured on that host rather than assumed, and recorded here: `PreeditChanged`
fires only with `SetUsePreedit(true)` (with `false`, the dead key commits the bare accent and no
preedit signal occurs), and the preedit cursor position counts characters (a one-character,
two-byte accent reported 1).

The evidence is the demo's text page, which shows the preedit text, the preedit caret index and the
last commit. A screenshot of that readout, before and after the commit, is the proof; a headed run
without it proves nothing.

### 7.3 The Win32 test, and what it proves

`tests/backends/Kolvir.Backend.Win32.Tests/Win32ImeCompositionTests.cs` creates a real window, focuses
a real text box and sends the real `WM_IME_*` messages to its real window procedure, so the message
routing, the host calls and the text box are all the shipped ones. What it proves:

1. `WM_IME_STARTCOMPOSITION` asks the focused control for a composition, and a `GCS_COMPSTR` update
   reaches the preedit with the input method's caret index while `Text` stays empty.
2. A `GCS_RESULTSTR` commit arrives through the ordinary text path: the value and the caret are
   exactly what typing the same text would produce.
3. The `WM_CHAR` messages for those same characters do not insert the text a second time. This is the
   assertion that catches the doubling hazard: with the suppression removed, the run reports six
   characters where three were committed.
4. A `WM_CHAR` that is not the commit's own text is still delivered, so the suppression is not a
   blanket mute.
5. `WM_IME_ENDCOMPOSITION` without a commit leaves no text, no preedit and no undo entry, and a repeat
   of it changes nothing.
6. An astral character, which arrives as two `WM_CHAR` messages, reaches the field as one character
   rather than two lone surrogates.
7. The window backend's composition reader defaults to the Imm32 one, so the seam cannot quietly ship a
   fake, and the real reader answers an empty string and a zero caret when a window has no context.
8. `WM_IME_REQUEST` with `IMR_QUERYCHARPOSITION` answers the caret in screen coordinates, with the
   document rectangle and a line height from the field's font, and a click elsewhere in the field
   moves the answer, so the panel follows the caret rather than the window.
9. `GetGUIThreadInfo` reports this window's caret one pixel square at the same point, which is the
   measurement that diagnosed the emoji panel landing in the wrong place, and a request the backend
   does not implement reaches `DefWindowProc` instead of being answered.
10. The trace proves it is running before anything asks it anything: the first line names the process,
    the window, the executable and the file, and the heartbeat keeps arriving while the real message
    loop pumps. That is what stops a run that produces no file from being read as "the panel never
    asked".
11. A window with nothing focused still answers the request, from its client origin in screen
    coordinates, instead of refusing and handing the shell its no-anchor fallback.
12. The composition window form follows the caret with no composition running, at the same point
    `IMR_QUERYCHARPOSITION` answers with; a window with nothing focused leaves it untouched; and a
    message that does not move the caret does not re-issue it, which the test proves by writing a
    marker only it knows and checking the marker survives.
13. The key path is traced at the pump, where the decision is made: a letter the library leaves alone
    is logged as `swallowed=False`, which is what lets `TranslateMessage` hand it to an input method,
    and a key the library does handle is logged as `swallowed=True`. A trace that could not distinguish
    those two would read "silent input method" for both.
14. A field that refuses a composition tells the input method so: a read-only field and a disabled one
    each send exactly one cancel per start message - three starts, three cancels, no amplification -
    the preedit and the result that follow are dropped rather than shown or inserted, and a field that
    *accepts* a composition sends none, so an accepted composition is never cancelled underneath it.

The composition string itself is published through `IWin32ImeComposition`, which is internal and
defaults to `Win32ImeComposition.Instance`. The seam exists so the message path can be driven where no
composition can be produced: the hosted CI images carry no input method at all, and even here, with one
installed, setting a composition string needs the context to be the thread's active one (section 6.2).
It is held to three conditions - internal only, defaulting to the production instance, and pinned by a
test that fails if that default is replaced with a fake. It is not there because this machine lacked an
IME; it is there because a test cannot manufacture a composition without one. What the test therefore
does **not** cover is listed in section 7.4, and what a person can cover is section 7.5.

### 7.4 What cannot be automated

A real input method's candidate window, its clause behaviour and its own preedit styling. Those are
the platform's UI, not the library's, and every platform draws them differently. They are verified
by hand, if at all, and they are the reasons section 8 leaves clause styling open.

On Windows the `IMM32` calls themselves used to be listed here. With an input method installed and its
context made active they do answer: `ImmGetCompositionStringW` returns the composition string and the
caret index, `ImmSetCompositionWindow` places the input method's window, `ImmNotifyIME` completes or
cancels a live composition, and a commit really does arrive as `GCS_RESULTSTR` followed by
`WM_IME_CHAR` per character (section 6.2). What stays out of a test's reach is the IME itself. The
hosted CI runners are English-only images with no input method, so **CI cannot cover this path, and the
tests do not claim to**: what they cover is the message handling, the suppression and the caret seam.
The calls' real answers are covered by the manual checklist in section 7.5, item by item.

The emoji panel is the other thing only a person can check. A test can prove the answer is right - the
caret in screen coordinates, the same point from the thread's caret, and both moving with a click - but
whether the shell's panel appears beside the caret when Win+Period is pressed is the shell's own
window, its own placement and its own animation, none of which this process can observe. That check is
by hand: put the caret mid-field, press Win+Period, and see the panel open at the caret rather than at
the window corner.

What the report says, and how it was found, because the path matters more than the conclusion.
`IMR_QUERYCHARPOSITION` was being asked and answered correctly the whole time - `pt=2341,556` at the
caret in an early run - and that was not enough. A test point of `100,100` proved nothing: an honoured
answer in a screen corner and a no-anchor fallback look identical there. `1900,1000` landing mid-screen,
with the window at a different origin and the pointer elsewhere, proved the answered point is what
places the panel. The observation that located the real channel was that **identical answers produced
different placements**: one baseline run answered `pt=780,537` at every open - 22:32:33.163, 38.983,
39.732, 45.776, 50.707 and 53.867 - with the caret at client 140,303 throughout, and the user reported
the **first** open misplaced ("almost left aligned to the window and just below the menubar") and every
later open correct. Identical answers cannot produce two placements unless something else places the
first one, and the composition form was the only per-context state this backend can influence.

The fix is in section 6.2: the form is kept at the caret from the moment there is one, in the same
sync that moves the thread caret, so it is never stale. The confirming run was the switch that existed
at the time: with the form written ahead of the first open, the panel landed at the caret on the first
open and still at the caret on the second, after typing had moved it. The switch is gone; the behaviour
is not.

What that leaves unverified is the input method's own UI rather than its position: a real IME's
candidate list, its clause attributes and its own preedit styling. Those need an IME installed and a
person to look at them, which is section 7.5.

### 7.5 Verifying with a real input method

The Japanese language pack and its Microsoft IME are installed on this development machine, and the
person who works on it ran the checklist below with a working IME. What that settled, and what it left
open, because the difference matters:

- **Confirmed on this machine, with the Microsoft IME:** steps 1, 2, 3, 4, 5, 6 and 8 - preedit and
  preedit caret reaching the control, a commit inserting exactly once, the candidate list opening at
  the caret on the first conversion, Escape cancelling without inserting, the password field masking
  the preedit, and the emoji panel landing at the caret with the IME active. Step 4 is the one that
  mattered most: it confirms the composition form is what positions a real input method's candidate
  window, not only the emoji panel. The same run, on a build that had the fallback, confirmed that
  `かんじ` renders as glyphs rather than boxes and that an inserted 👌 draws in colour.
- **Found by that run, and fixed:** step 7 - typing into the read-only field brought up the input
  method's own composition window, because the refusal was only made to the host (section 6.2); and,
  once the emoji was in the field, the arrow keys stepping into the middle of it (section 6.2, and
  `DEVELOPER-GUIDE.md` for the text field). **Step 7 needs re-running** to confirm the input method's
  window no longer appears, and the arrow keys need a walk over the same text to confirm the caret
  moves cleanly - neither is something these tests can show against a real input method.
- **Still unverified:** whether a real input method answers the cancel with a `WM_IME_ENDCOMPOSITION`
  of its own, and whether its window actually goes away. Neither is observable from the message path
  with no IME to drive, and both are what the step-7 re-run answers.

The demo's **Text Input** page carries the evidence: the `Name` field has a composition readout under
it - `Preedit: "..."  |  preedit caret: n  |  last commit: "..."  | value: "..."` - plus a `Password`
field and a `Read only` field holding `Read-only - select, then Ctrl+C`.

Setup: run the demo, open **Text Input**, and press **Win+Space** (or use the language bar) until the
IME is Japanese and in Hiragana mode. The steps below name the readout each one expects.

1. **The preedit reaches the field.** Click `Name`, type `ni`. The field shows `に` underlined and the
   readout shows `Preedit: "に"`, `preedit caret: 1`; `value` does not change, because a composition is
   not text yet.
2. **The preedit caret reaches the field.** Type `hon` so the readout shows `Preedit: "にほん"`,
   `preedit caret: 3`. Press Left. The preedit stays `"にほん"` and `preedit caret` becomes `2`: the IME
   owns the caret inside its own string.
3. **A commit inserts once.** Press Space to convert, then Enter. The readout shows `last commit: "日本"`
   and `value: "日本"`, and the field shows 日本 **once**. `日本日本` is the `GCS_RESULTSTR` plus
   `WM_CHAR` doubling that section 6.2 suppresses. The characters must be legible glyphs, not boxes:
   the committed text is drawn through the library's own fallback (section 6.2), so a row of boxes here
   means the machine has no face for that script rather than that the commit failed.
4. **The candidate list appears at the caret on the first conversion.** Type `ka` and press Space. The
   candidate list opens just below the caret inside the field, not at the window corner and not in the
   bottom-right of the screen. Press Escape, click to move the caret to the middle of existing text, and
   repeat: the list must open at the new caret. That is the same composition form the emoji panel reads
   (section 6.2).
5. **Escape cancels without inserting.** Type `ka`, press Escape. `Preedit: ""`, `last commit` and
   `value` are both unchanged.
6. **The password field masks the preedit.** Click `Password`, type `ni`. The preedit appears masked, the
   candidate list still opens at the caret, and a commit inserts masked characters - the status line
   reads `Password length: 1` for one committed character.
7. **The read-only field refuses a composition.** Click `Read only`, type `ni`. No preedit appears, the
   readout stays empty, and the field still reads `Read-only - select, then Ctrl+C`. The input method's
   own window must not appear either: that was the failure this step found, and the cancel in section
   6.2 is the fix. A trace of the same run says `WM_IME_STARTCOMPOSITION refused ... composition
   cancelled` once per keystroke that tried.
8. **The emoji panel lands at the caret with the IME active.** With the caret mid-field, press
   **Win+Period**. The panel opens beside the caret. Switch back to English (Win+Space) and repeat: the
   panel must still land at the caret.

Steps 1-3, 5, 6 and 7 exercise the shipped `WM_IME_*` handling end to end, and step 4 and step 8 are the
ones section 7.4 cannot reach. A test could be written for steps 1-5 the same way the probe in section
6.2 was run - `LoadKeyboardLayoutW`, `ActivateKeyboardLayout`, `ImmSetActiveContext`, then
`ImmSetCompositionStringW(SCS_SETSTR)` and a pumped message loop - but it can only run where an IME is
installed, so it belongs behind a capability check (`GCS_COMPSTR` reading back empty means no IME) rather
than in the CI suite.

## 8. Status and open questions

| Platform | Dead keys / Compose | Daemon-driven IME | Status |
|---|---|---|---|
| GTK4 | Implemented | Implemented | **Done** |
| Win32 | Implemented and covered by section 7.3 | Implemented; the `IMM32` reads answer with the Microsoft IME installed (section 6.2), and the section 7.5 checklist was run by hand with it - steps 1-6 and 8 confirmed, step 7 found a refusal the input method never heard and the fix is the cancel in 6.2 | **Done** |
| macOS | Designed | Designed | Not implemented |

The Win32 path is proved against a synthesized composition driven through the real messages, and the
`IMM32` calls behind it were measured against the Microsoft IME that is installed on this
development machine (section 6.2). What that leaves is the input method's own UI - its candidate
window, its clauses and its preedit styling - which is a person's job, not a test's: section 7.5 is
the checklist, and it is the only place those items are claimed. CI cannot help here; the hosted
runners carry no input method. The emoji panel's placement is the same kind of limit: the answers it
reads are tested, and the panel itself was confirmed by hand on this machine (section 7.4).

Open questions:

1. **Clause styling.** An IME marks clauses inside the preedit (converting, converted, raw). v1
   draws the preedit as one underlined run. A clause model would give each run its own decoration
   and is the natural first extension.
2. **`ImeMode`.** A way for a field to tell the platform what kind of text it takes (password,
   numeric, kana). Left out of v1 deliberately: there is no consumer, and it is a policy decision
   about which fields get which hint.
3. **Replacement ranges.** v1 always commits at the caret. Committing into a range is what
   context-aware IMEs and clause editing need, and it changes the shape of the commit call.
4. **A public preedit surface.** The composition hooks are protected today, and a public read-only
   surface (`PreeditText`, a caret index, and a change event) is the obvious next step for an
   application that wants its own composition UI. It is deferred until clause styling settles,
   because that is what will change the shape of the surface; adding it now would mean breaking or
   carrying it. Today an application control author can observe composition by overriding the
   protected hooks, which is what the demo does.
5. **Preedit caret versus drawn caret.** The preedit carries a caret index, but v1 draws the caret
   after the whole preedit rather than inside it. The index is recorded and handed to the platform
   through the caret seam; repositioning the drawn caret inside the preedit comes with clause
   styling.
