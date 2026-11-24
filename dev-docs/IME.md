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

### 6.2 Win32 (implemented)

The window procedure gets four messages. `WM_IME_SETCONTEXT` and `WM_IME_STARTCOMPOSITION` are
offered to `DefWindowProcW` after the library has done its part; `WM_IME_COMPOSITION` never is,
because the default handler is one of the ways a committed result becomes a `WM_CHAR`.

| Message | Value | What the backend does |
|---|---|---|
| `WM_IME_SETCONTEXT` | `0x0281` | Clear `ISC_SHOWUICOMPOSITIONWINDOW` (`0x80000000`) in `lParam` so the IME does not draw its own composition window over a library that draws the preedit itself, then pass it to `DefWindowProcW`. The flag is a DWORD in the low half of the `LPARAM`, so it is masked there and the result widened back as `lParam` is. |
| `WM_IME_STARTCOMPOSITION` | `0x010D` | `ImmGetContext`, `Host.BeginComposition()`, `ImmSetCompositionWindow` at the caret, `ImmSetCompositionFontW` from the focused control's font, `ImmReleaseContext`, then `DefWindowProcW`. A refusal is still answered, so the IME knows the field declined. |
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
- **The trace.** A run can be told to write every IME message to a file, which is what decides between
  "the application is never asked" and "it answers and is ignored" - different problems with different
  fixes. `KOLVIR_IME_LOG=1` writes `kolvir-ime.log` beside the executable, or the variable holds a
  path. Each line carries the message, `GetFocus()` against the window, the `IMR_` value, and for
  `IMR_QUERYCHARPOSITION` the incoming `dwSize` and `dwCharPos` read before anything is written, then
  the answer. It is off by default and costs one environment read per IME message when it is.
  Two switches select the experiments a trace can exonerate, and a report should say which was on:
  `KOLVIR_IME_SHOW_CARET=1` shows the one-pixel caret once it is positioned, which sets the only
  visibility flag the thread exposes (`GUI_CARETBLINKING`), and `KOLVIR_IME_REFRESH_COMPOSITIONFORM=1`
  keeps the input context's composition form at the caret outside a composition, in case the input
  stack anchors on the context instead of asking. A third, `KOLVIR_IME_TEST_POINT=x,y`, answers
  `IMR_QUERYCHARPOSITION` with exactly that screen point while filling the rest as usual, and says so
  on the answer line: a run at a point nowhere near the field decides whether the shell uses the point
  at all. Every line that reports an answer also carries the mouse pointer's position, because one
  hypothesis for a panel that lands in a fixed screen position is that it falls back to the pointer,
  and that can only be checked if the pointer is recorded at the same moments.
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
- **Popups.** `Win32PopupWindow` and `Win32MenuWindow` are deliberately left alone, for the reason
  section 6.4 gives: a non-activating popup never takes the input focus, so no composition can begin
  there.

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

**Measured, versus assumed, on this machine.** The measured facts are what a machine with no input
method installed does, and they are why section 7.3 substitutes a reader:

- `Get-WinUserLanguageList` is `en-US` alone, with the generic `0409:00000409` layout: no Japanese,
  Chinese or Korean IME exists here, so nothing below was proved with a real one.
- `ImmGetContext` still returns a valid context on such a machine, and `ImmIsIME` still answers TRUE
  for `0x0409:00000409`, which makes "no context" a poor test of "no IME".
- `ImmGetProperty(hkl, IGP_SETCOMPSTR)` is `0`, so the installed layout does not support setting a
  composition string at all. With both the composition and the reading buffer supplied at their
  documented lengths, `ImmSetCompositionStringW(SCS_SETSTR)` answers TRUE and
  `ImmGetCompositionStringW` then reads `0` bytes for `GCS_COMPSTR`, `GCS_COMPREADSTR`,
  `GCS_CURSORPOS` and `GCS_RESULTSTR`. A first attempt that passed only the composition buffer
  answered FALSE, which reads exactly like "no IME installed" and is why the call shape is recorded
  here rather than retried.
- Ignored on purpose: the input method's candidate window, its clause attributes
  (`GCS_COMPCLAUSE`, `GCS_COMPATTR`) and its own preedit styling.
- Measured with the trace on, against a real focused window on an English-only machine: the shell
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

The composition string itself is published through `IWin32ImeComposition`, which is internal and
defaults to `Win32ImeComposition.Instance`. That is forced by a measurement rather than chosen for
convenience, and the measurement is in section 6.2: on a machine with no IME the installed layout has
`IGP_SETCOMPSTR == 0`, so `ImmSetCompositionStringW(SCS_SETSTR)` answers TRUE and stores nothing, and
`ImmGetCompositionStringW` reads zero bytes even when both the composition and the reading buffer are
supplied. What the test therefore does **not** cover is listed in section 7.4.

### 7.4 What cannot be automated

A real input method's candidate window, its clause behaviour and its own preedit styling. Those are
the platform's UI, not the library's, and every platform draws them differently. They are verified
by hand, if at all, and they are the reasons section 8 leaves clause styling open.

On Windows this is also where the Imm32 reads themselves sit: `ImmGetCompositionStringW` returning a
real composition string, `ImmSetCompositionWindow` putting the input method's window where it was
asked, and `ImmNotifyIME` cancelling a live composition are all unexercised here, because this machine
has no input method to answer them. The message handling around them is covered; the calls' answers
are not.

The emoji panel is the other thing only a person can check. A test can prove the answer is right - the
caret in screen coordinates, the same point from the thread's caret, and both moving with a click - but
whether the shell's panel appears beside the caret when Win+Period is pressed is the shell's own
window, its own placement and its own animation, none of which this process can observe. That check is
by hand: put the caret mid-field, press Win+Period, and see the panel open at the caret rather than at
the window corner.

What the report says so far is worth stating precisely, because it narrows the question. Three traced
runs answered `pt=640,234` (nothing focused), `pt=2220,493` (nothing focused, the window had moved) and
`pt=2341,556` (a focused box, exactly the caret) - and the panel landed in the **same place on the
screen** every time. So the point in the answer is apparently not what places the panel, while
answering it did stop the bottom-right fallback, so something in the answer is read. Three hypotheses
fit, and they look different in a trace:

- **(a) The shell asks over IMM, reads the answer, and discards the point.** A good trace under this
  hypothesis has the `IMR_QUERYCHARPOSITION` line with a well-formed answer, heartbeats on both sides
  of it, and the panel in a position that is neither the answer nor the pointer. That is the case for
  the lighter TSF route: `ITfContextOwner.GetTextExt` with a document manager and a context,
  explicitly not a full text store.
- **(a2) The panel falls back to the mouse pointer when it has no usable text extent.** A fixed screen
  position across runs whose answers were hundreds of pixels apart fits this exactly, if the pointer
  was in the same place each time. A good trace under it has the panel where the heartbeats say the
  pointer was, and it is decided by `KOLVIR_IME_TEST_POINT=100,100`: answering with a point nowhere
  near the field either moves the panel there - the point is honoured and the defect is an offset to
  measure - or leaves it at the pointer, which names the fallback.
- **(b) The panel positions from TSF text extents and never uses this channel.** A good trace under
  this hypothesis is the opposite of both: heartbeats continuing across the moment Win+Period is
  pressed and **no** `WM_IME_REQUEST` line anywhere near it, the only IME traffic being the burst that
  arrives with focus. The gap-free run with no IME line in it is the positive evidence, and it means
  the IMM work cannot fix the placement no matter how correct it is.

The other two switches, `KOLVIR_IME_SHOW_CARET` and `KOLVIR_IME_REFRESH_COMPOSITIONFORM`, rule out a
variant of (a) in which the panel reads an anchor other than the request: a panel that uses the
thread caret is affected by the first, and one that anchors on the context's composition form by the
second, so a run with each one alone says which anchor it is reaching for.

## 8. Status and open questions

| Platform | Dead keys / Compose | Daemon-driven IME | Status |
|---|---|---|---|
| GTK4 | Implemented | Implemented | **Done** |
| Win32 | Implemented and covered by section 7.3 | Implemented, but untestable on a machine with no IME (section 6.2) | **Done** |
| macOS | Designed | Designed | Not implemented |

No input method is installed on the Windows development machine used for this branch: only `en-US`
with the generic `0409:00000409` layout. The Win32 path is therefore proved against a synthesized
composition driven through the real messages, not against a Japanese, Chinese or Korean IME. Its
candidate window, its clauses and its own preedit UI are unverified, exactly as they are on GTK4. The
emoji panel's placement is the same kind of limit: the answers it reads are tested, but the panel
itself is checked by hand (section 7.4).

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
