# The popup API - implementation plan

Kolvir can already show an arbitrary control in a popup surface; what it cannot do is offer that as a
first-class API. `ComboBox`, `DateTimePicker`, `ToolTip` and the menu system each build a `Popup`
themselves and each re-implement placement, keyboard forwarding and close results. This plan adds an
additive, extension-method API for the common case - show this control over there, tell me how it
ended - while leaving `IPopupService`/`Popup` untouched for callers that need control.

Status of this document: planned, nothing implemented. It covers two independent pieces of work: the
UI-thread `SynchronizationContext` (step 1), which is a prerequisite and has nothing to do with
popups, and the popup API itself (steps 2-5).

## What already exists

The plan reuses all of this, so none of it is rebuilt.

| Layer | What it already gives the plan |
| --- | --- |
| `IPopupService` / `IPopupWindow` | `CreatePopup(ownerHost, dismissOnOutsideClick, takesPresses)`, `Show(ownerHost, location, size)`, `UpdateBounds`, `Hide`, `Dismissed`, and `GetPlacementArea`, which answers the display's work area in the owner's client coordinates |
| `Popup` | an arbitrary `Content` control, `Size`, `MaximumSize`, `ScaleFactor`, `DismissOnOutsideClick`, `TakesPresses`, overlay or platform presentation, `Show`, `MoveTo`, `Close`, `AddClosedHandler(Action<object>)` |

The three platform implementations of `IPopupService` are already there and are not touched:

| Platform | Surface | Keyboard |
| --- | --- | --- |
| Windows | layered popup window, `WS_EX_NOACTIVATE` + `SW_SHOWNOACTIVATE` + `MA_NOACTIVATE`; the outside press is recognised in the owner's message procedure (`DismissForOwner`) | never takes it - the owner keeps focus |
| GTK4 | `Gtk.Popover` anchored to the owning window's drawing area | popover semantics: the popover can still end up with the focus, so a capture-phase key controller on it hands each key to the owner's host and leaves the ones the owner does not take to GTK |
| AppKit | borderless child `NSWindow`, ordered above and out with its parent, outside clicks through a local monitor | never becomes key or main |

Two consequences the rest of the plan depends on:

- The keyboard stays with the owner on Windows and AppKit, and on GTK4 the backend has to hand it
  back. Either way, keyboard interactivity inside a popup has to be forwarded from the owner rather
  than hoped for.
- With no `IPopupService` registered a popup is painted in the owner window's overlay layer, which is
  the path every test runs - it is the one an application gets before a backend registers itself.

What is missing today:

- `Popup.Close()` takes no result, and its closed handler reports the popup rather than why it closed
  or with what.
- Placement is every caller's problem. `Popup` does not consult `GetPlacementArea`, so
  `DateTimePicker`, the text box's suggestion list and the menu presenter each resolve the work area
  and flip or clamp themselves.
- Nothing returns a `Task`, so no dismissal can be awaited.
- Nothing maps a control to the popup showing it. `Popup.Owner` points the other way, from the popup
  to the control it was anchored to, and the content control the popup hosts is associated with
  nothing at all.

## Design decisions

| Topic | Decision | Rationale |
| --- | --- | --- |
| Existing API | `IPopupService` and `Popup` are unchanged and remain the advanced route | The new API is additive; nothing that works today changes shape |
| Shape | Extension methods plus new types, no new members on `Window` or `Control` | A popup an application never shows costs it nothing - see the feature-cost section |
| Close notification | `Action<object, PopupResult>`, sender first | Matches the repository's `AddXHandler(Action<object>)` convention, where the first argument is the sender |
| Handler lifetime | `AddPopupClosedHandler` handlers persist for the control and fire for every popup it is later shown in | The control is the identity the caller has; unregistering is theirs to do |
| Result value | Only `ClosePopup(result)` sets `Value`, and it always means reason `Closed`; `OutsideClick`, `Escape`, `OwnerClosed` and `Cancelled` carry no value | There is no other route that could attach one, so a value on a dismissal would be a lie |
| Concurrency | One popup per owner surface - a window owns one, a popup owns one | Gives menu chains for free, and opening at one level cancels only that level |
| Placement | `PopupAlign` is a request; the implementation flips and clamps within `GetPlacementArea` when the display does not have room | What every context menu does: the display wins over the requested alignment |
| Cancellation | No `CancellationToken` | `ClosePopup` already closes it; a token would be a second way to say the same thing |
| Async context | `Application.Run()` installs a `SynchronizationContext` for the duration of the loop (step 1) | Without one, `await ShowPopupAsync(...)` resumes on a thread-pool thread and the caller touches controls off the UI thread |

## Steps

| # | Step | Scope | Done when |
| --- | --- | --- | --- |
| 1 | The UI-thread `SynchronizationContext` | A context installed by `Application.Run()` over the existing dispatch; `Post`, `Send`, `CreateCopy`; restored on exit | `await` of a task completed on the UI thread resumes on the UI thread, asserted by a test |
| 2 | Result and options types | `PopupResult`, `PopupCloseReason`, `PopupOptions`, `PopupAlign`, `PopupPlacement` | The types exist and are documented; no behaviour yet |
| 3 | The extension surface | `ShowPopupAsync` on `Window` (with and without an anchor) and on `Control`; `ClosePopup`; `AddPopupClosedHandler`/`RemovePopupClosedHandler`; the per-control session table | Showing a control returns a task that completes with the right reason for each dismissal path |
| 4 | Placement | Anchor/client-point/pointer placement, `PopupAlign`, `Offset`, flip and clamp inside the work area | A popup asked to open below a control at the bottom of the display opens above it |
| 5 | Input forwarding | An `IInputSink` registered on the owner's host while a popup is open: Escape closes, keys and typed text are forwarded into the popup's host, Tab cycles within the popup | Typing in a `TextBox` inside a popup works, and so does Escape |

## Step 1 detail - the UI-thread SynchronizationContext

There is no `SynchronizationContext`, no `ConfigureAwait` and no `TaskScheduler` anywhere in the
repository, so today an `await` inside a Kolvir application resumes on a thread pool thread. This
step comes first because the popup API's natural shape is a `Task` and the natural way to consume it
is `await`: without a context every caller would have to marshal back to the UI thread by hand
before touching the control it just got an answer about.

The design is small. `Application.Run()` creates the context and installs it before the backend's
loop, and restores it in the `finally` that already runs `TearDown`. `Post` dispatches through
`Application.BeginInvoke`, which queues through `Backend.Invoke(..., synchronous: false)`. `Send`
runs inline when the caller is already on the UI thread and goes through `Application.Invoke`
otherwise. `CreateCopy` returns the same instance, because there is one UI thread and a second
context would be a second answer to the same question.

Three properties it has to keep, all of them already true of the code it wraps:

- a `Post` made from the UI thread is still queued rather than run inline, so a continuation cannot
  run ahead of work that is already queued;
- an exception inside a posted callback goes through the same `Guard` path as any other UI callback,
  so it reaches `Application.AddUnhandledExceptionHandler` instead of unwinding into the message
  pump;
- the context is absent after `Run` returns, so a stray continuation cannot marshal onto a loop that
  has finished.

The headless suite is the other consumer. `HeadlessTestBase` restores process state and runs no loop,
and the one place a test enters `Application.Run` is `HeadlessScene.RunWhileShown`, which asserts
from inside a shown handler. A test that awaits a popup therefore either makes that handler async
and gets the context from this step for free, or installs a dispatcher of its own. That choice is
made when the step is implemented; open items carries it until then.

## Steps 2 and 3 detail - result types and the extension surface

The surface is one code block because it is the whole public contract: five small types, six methods,
and nothing else.

```csharp
public enum PopupCloseReason { Closed, OutsideClick, Escape, OwnerClosed, Cancelled }

public sealed class PopupResult
{
    public PopupCloseReason Reason { get; }
    public object? Value { get; }
    public bool TryGetValue<T>(out T value);
    public static PopupResult Closed(object? value = null);
}

public enum PopupPlacement { Pointer, Anchor, ClientPoint }

public enum PopupAlign
{
    BelowStart, BelowCenter, BelowEnd,
    AboveStart, AboveCenter, AboveEnd,
    RightOf, LeftOf, AtPointer,
}

public sealed class PopupOptions
{
    public PopupPlacement Placement { get; set; } = PopupPlacement.Anchor;
    public PopupAlign Align { get; set; } = PopupAlign.BelowStart;
    public SKPoint Offset { get; set; }
    public SKSize? Size { get; set; }
    public SKSize MaximumSize { get; set; }
    public bool EscapeCloses { get; set; } = true;
    public bool DismissOnOutsideClick { get; set; } = true;
    public bool FlipIntoView { get; set; } = true;
}

public static Task<PopupResult> ShowPopupAsync(this Window window, Control content, PopupOptions? options = null);
public static Task<PopupResult> ShowPopupAsync(this Window window, Control content, Control anchor, PopupOptions? options = null);
public static Task<PopupResult> ShowPopupAsync(this Control anchor, Control content, PopupOptions? options = null);
public static void ClosePopup(this Control content, PopupResult? result = null);
public static IDisposable AddPopupClosedHandler(this Control content, Action<object, PopupResult> handler);
public static void RemovePopupClosedHandler(this Control content, Action<object, PopupResult> handler);
```

**The session.** One internal `PopupSession` per shown popup owns the `Popup`, its `PopupResult` and
a completion source. The result is written once and every later close is a no-op, which is what keeps
the five dismissal paths - a close the application asked for, a press outside, Escape, the owner
going away, and a replacement - from double-completing a task.

**The association.** Extensions cannot add state to `Control`, so a
`ConditionalWeakTable<Control, PopupChannel>` holds each control's handlers and costs an application
that never shows a popup nothing. A control's owner surface is the session whose `UiHost` matches
the control's `Host`, and failing that its window - which is what makes a popup shown from inside a
popup a child of that popup rather than a sibling of it, and what makes opening one at a level cancel
only that level.

## Dismissal matrix

Every path below is one entry in the `PopupCloseReason` enum, and the value is only ever set by the
first row.

| Path | Result | What happens to the input |
| --- | --- | --- |
| the content calls `ClosePopup(result)` | that result, reason `Closed` | nothing - the call already happened |
| the user presses outside the popup | `OutsideClick`, no value | the press is consumed and not delivered to what it landed on |
| Escape reaches the forwarded sink | `Escape`, no value | handled by the sink |
| the owner window closes or hides | `OwnerClosed`, no value | nothing to deliver |
| a second `ShowPopupAsync` on the same owner surface | the first completes `Cancelled`, the second shows | the first popup is closed |

## Feature cost

This sits on rung 1 of [FEATURE-COST-POLICY.md](FEATURE-COST-POLICY.md) section 4: zero cost by
construction. The feature is reachable only through application code that names it - no new members
on `Control` or `Window`, no startup hook, no registration, no always-retained type that has to know
it exists - so a popup an application never shows costs that application nothing.

The pull request that implements it must report the measured NativeAOT delta on the `Kolvir.Minimal`
fixture, as the policy requires of every new feature. The expected delta is zero, because nothing is
reachable without a call.

## Testing

Core tests belong in `tests/core/Kolvir.Tests/Core/PopupTests.cs`, which already runs the overlay
fallback path - the one every application gets before a backend registers, so it is the path the new
API has to work on everywhere. What those tests must cover:

- task completion per reason: `Closed`, `OutsideClick`, `Escape`, `OwnerClosed` and `Cancelled`;
- `ClosePopup` with and without a value, and `TryGetValue<T>` on both;
- handler add and remove, and persistence across two popups shown from the same control;
- one popup per owner surface, including the `Cancelled` completion the second show produces and the
  fact that opening at one level leaves the level above it alone;
- placement: an anchor at the bottom of the work area flips, and a request that does not fit either
  way clamps;
- Escape, through the forwarded sink;
- the step-1 `SynchronizationContext`: a continuation after `await ShowPopupAsync` runs on the UI
  thread.

The rest needs a real window and a hand check, because the headless suite never has a platform popup
service:

- Windows: an outside click closes the popup and does not also activate the control under the press,
  and typing reaches a caret inside it;
- GTK4: the popover is placed where it was asked for, and what the popover does with the focus;
- AppKit: the child window stays above its parent and goes away with it.

## Open items

Implementation unknowns, not open design questions.

- **The reverse lookup.** `UiHost` does expose the window it belongs to - `UiHost.Window`, which
  `Control.Window` passes through - so the window half of the ownership rule is answered. The popup
  half is not answered by it: `Popup` assigns `window.Host.Root` and never sets a `Window` on the
  host it opens a window for, so a popup's own host cannot name one. The session table's `UiHost`
  match is what has to cover a popup shown from inside a popup, and the owning window it reports has
  to come from the owner's host rather than the popup's.
- **What a GTK4 popover does with the focus.** The backend's own comment says a popover may still end
  up with the focus and installs a capture-phase key controller for that reason. Whether a forwarded
  `IInputSink` sees a key before that controller does, and whether a text box inside a popover can be
  typed into, is not stated anywhere in the source and has to be found on a real display.
- **How the headless suite hosts the step-1 context.** The decision itself is small - an async
  `RunWhileShown` body, or a dispatcher of the test's own - but it is not made yet, and the step-1
  done-when cannot be asserted until it is.
