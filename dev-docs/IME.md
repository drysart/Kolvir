# Text composition (IME) - design and implementation plan

`APPLICATION-MODEL.md` section 10 lists "IME / dead keys / composed input" as a Tier-3 gap with one
sentence beside it and no design behind it. This document is that design. It covers the three
platforms, but only GTK4 is implemented in this branch: Windows and macOS are designed here and
left open.

Status of this document: **GTK4 implemented, Win32 and macOS designed but not implemented.**
Sections 1 to 5 are the model every platform shares; section 6 is per platform; section 7 is what
proves it; section 8 is the progress table and the questions this design deliberately leaves open.

This file is deliberately ASCII-only, like `OBSERVABLES.md` and `OBSERVABLE-COLLECTIONS.md`: write
"section 3.4" rather than a section sign, and "<-" rather than an arrow.

## 1. The problem

The input pipeline was, and on Windows and macOS still is, one-way and stateless. A backend derives
characters from the keyboard and hands them over, and the library has no way to hold text that is
not committed yet.

- **GTK4.** Before this change,
  `Gtk4SkiaView.OnKeyPressed` (`../../src/backends/Kolvir.Backend.Gtk4/Gtk4SkiaView.cs:1092` at the
  baseline commit) mapped the key value to text with `Gtk4KeyMap.MapText(args.Keyval, args.State)`
  and called `Host.ProcessTextInput(text)`. No `Gtk.IMContext` existed in the backend at all.
- **macOS.** `AppKitSkiaView.KeyDown` (`../../src/backends/Kolvir.Backend.AppKit/AppKitSkiaView.cs:782`)
  takes `AppKitKeyMap.ToText(theEvent)` after the key and the Command/Control checks. The view does
  not adopt `NSTextInputClient`, so `interpretKeyEvents:` is never called.
- **Windows.** `Win32WindowBackend` has a case for `WM_CHAR` (`:1680`, handled by `OnChar` at
  `:2665`) and none for any `WM_IME_*` message in `HandleMessage` (`:1509`).

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
window the platform may be showing is reset by the backend's own focus handling (section 6.1).

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

### 6.2 Win32 (designed, not implemented)

The window procedure gets four messages. `WM_IME_SETCONTEXT` and `WM_IME_STARTCOMPOSITION` are
offered to `DefWindowProcW` after the library has done its part.

| Message | Value | What the backend does |
|---|---|---|
| `WM_IME_SETCONTEXT` | `0x0281` | Clear `ISC_SHOWUICOMPOSITIONWINDOW` (`0x1`) in `lParam` so the IME does not draw its own composition window over a library that draws the preedit itself, then pass it on. |
| `WM_IME_STARTCOMPOSITION` | `0x010D` | `ImmGetContext`, `Host.BeginComposition()`, `ImmReleaseContext`. A refusal is still answered, so the IME knows the field declined. |
| `WM_IME_COMPOSITION` | `0x010F` | `GCS_COMPSTR` (`0x8`): read the composition string and `GCS_CURSORPOS` (`0x80`), then `Host.UpdateComposition`. `GCS_RESULTSTR` (`0x800`): read the result string and `Host.CommitComposition`. Then `ImmSetCompositionWindow` from the caret seam so the candidate window follows the caret. |
| `WM_IME_ENDCOMPOSITION` | `0x010E` | `Host.CancelComposition()`. |

Imm32 calls: `ImmGetContext`, `ImmReleaseContext`, `ImmGetCompositionStringW`,
`ImmSetCompositionWindow`. The caret goes into `COMPOSITIONFORM.ptCurrentPos` in client
coordinates.

Two hazards, both real and both already visible in this codebase:

1. **A commit also produces `WM_CHAR`.** Translating a `GCS_RESULTSTR` composition produces an
   ordinary `WM_CHAR` for the same text, so committing in the window procedure and letting `WM_CHAR`
   through doubles the text. The codebase already documents the same hazard for keys at
   `Win32ApplicationBackend.cs:543` ("Swallowing the key here ... keeps `TranslateMessage` from also
   turning it into a `WM_CHAR`, which would double up text input"), so the fix lives in the same
   place: the message loop suppresses the `WM_CHAR` that belongs to a commit, or the window
   procedure consumes the composition and sets a flag the loop honours.
2. **`OnChar` truncates astral characters.** `OnChar` casts `(char)(int)wParam`, which turns a
   surrogate-pair code point into a lone surrogate. A composition commit is the realistic way to get
   an astral character into a window, so this has to be fixed with the composition work: use
   `WM_UNICHAR`/`UTF-32` or build the string from `char.ConvertFromUtf32`, and drop `.Length == 2`
   strings only when both halves are genuinely a pair.

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

The backends cannot be driven from a test, so the core is pinned by headless tests that call the
four host methods and the caret seam directly:

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

### 7.3 What cannot be automated

A real input method's candidate window, its clause behaviour and its own preedit styling. Those are
the platform's UI, not the library's, and every platform draws them differently. They are verified
by hand, if at all, and they are the reasons section 8 leaves clause styling open.

On the Linux host used for this work even the by-hand check could not be made: `ibus` is installed
but no engine is (`ibus-hangul`, `ibus-pinyin`, `ibus-anthy` and `ibus-libpinyin` are all absent) and
`sudo` needs a password, while `gtk-im-context-simple` draws no candidate window by design. The
cursor-location maintenance described in section 6.1 is therefore reasoned from the order the
signals arrive in, not an observed candidate-window move.

## 8. Status and open questions

| Platform | Dead keys / Compose | Daemon-driven IME | Status |
|---|---|---|---|
| GTK4 | Implemented | Implemented | **Done in this branch** |
| Win32 | Designed | Designed | Not implemented |
| macOS | Designed | Designed | Not implemented |

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
