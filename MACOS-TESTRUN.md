# Running the demo app on macOS

Status: the one page to read before trying to launch the sample on a Mac. Every route below was
measured on the current Mac test host, not inferred from the SDK docs. If a launch stops working,
work through this document before experimenting.

For the hosts themselves - which machine to use, what is installed on it, and how to take a
screenshot or drive the mouse and keyboard - see [TESTING-RUNBOOK.md](TESTING-RUNBOOK.md). This page
is only about getting a build to launch on macOS.

**The canonical Mac host is `macvm`** (`ssh macvm` -> `Timothys-Virtual-Machine.local`, osx-arm64,
macOS 27.2, .NET 10.0.403, Xcode 27.2). It replaced the Mac mini `Timothys-Mac-mini.local`, which is
reachable as `ssh mac` but **retired** - do not use it unless you are specifically told to. Everything
below that differs from the old host is called out as such, because the two hosts differ in ways that
silently change what your test actually measured.

The macOS build is not a loose executable. The `net10.0-macos` target builds an **`.app` bundle**, and
the only binary in it is `Contents/MacOS/KolvirDemoApp`:

```
Kolvir.Demo.App/bin/Debug/net10.0-macos/osx-arm64/KolvirDemoApp.app/Contents/MacOS/KolvirDemoApp
```

There is no `KolvirDemoApp` beside the bundle and no supported way to run it as a plain managed
assembly, because the app needs its native apphost to register with AppKit.

## Build first

```sh
export PATH="$HOME/.dotnet:$PATH"          # macvm; see "Machine prerequisites" below
cd ~/src/Kolvir
dotnet build Kolvir.Demo.App -c Debug
```

A build is required before any run route here - every route below launches whatever is already in
`bin/`.

## Machine prerequisites (`macvm`)

Three things have to be right before a build will even produce a bundle. All three were missing on a
fresh `macvm`, and each fails with an error that does not name the real problem.

### 1. `dotnet` is a user-local install

On `macvm` the SDK lives at **`~/.dotnet/dotnet`**, not `/usr/local/share/dotnet/dotnet`. It is on
`PATH` for an interactive login shell but **not** for a non-interactive `ssh macvm '...'` command, whose
`PATH` is only `/usr/bin:/bin:/usr/sbin:/sbin`. Use the absolute path, or export it first:

```sh
export PATH="$HOME/.dotnet:$PATH"
```

### 2. The `macos` workload must be installed

A plain SDK does not have it. Without it the `net10.0-macos` target fails to restore. Once:

```sh
dotnet workload install macos      # 27.0.10742 at the time of writing
dotnet workload list               # must list `macos`
```

### 3. Xcode must be the selected developer directory

Xcode 27.2 is installed at `/Applications/Xcode.app`, but a fresh VM reports the Command Line Tools
as the developer directory, and the app bundle build then fails with:

```
error : Could not find a valid Xcode developer path
```

That message blames your project; the actual problem is `xcode-select`. Permanent fix (needs `sudo`):

```sh
sudo xcode-select -s /Applications/Xcode.app/Contents/Developer
xcode-select -p        # -> /Applications/Xcode.app/Contents/Developer
```

Without `sudo` you can point a single build at Xcode instead, which was measured working:

```sh
DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer dotnet build Kolvir.Demo.App -c Debug
```

Note that the AppKit **library** builds fine with only the Command Line Tools; it is the `.app` bundle
that needs the full Xcode toolchain. A library-only build therefore proves nothing about whether the
demo can run.

## Plain `dotnet run` does not work - do not waste time on it

```sh
dotnet run --project Kolvir.Demo.App -- --page 38     # exits immediately, code 0, nothing drawn
```

It resolves to a managed exec rather than the apphost, which is why it exits successfully having
drawn nothing:

```
RunCommand   = dotnet
RunArguments = exec ".../bin/Debug/net10.0-macos/osx-arm64/KolvirDemoApp.dll"
```

No error is printed, so this looks like a silent failure rather than a misconfiguration. That is the
trap that cost time: the app never starts, and the absence of an error suggests something deeper.

## The route that works: `dotnet run` with the `mac` launch profile

`Kolvir.Demo.App/Properties/launchSettings.json` carries a `mac` profile whose `commandName` is
`Executable` and whose `executablePath` is the bundle's binary. With it, `dotnet run` launches the
app properly, and arguments after `--` reach it:

```sh
dotnet run --project Kolvir.Demo.App --launch-profile mac -- --page 38
```

Measured: the process stays alive, and `pgrep -fl KolvirDemoApp` shows the binary running with
`--page 38`. This is the preferred route.

**One caveat, measured:** an app launched this way does **not** come to the front. `lsappinfo front`
reported Finder while the demo was running. That is fine for looking at a window yourself, and fine
for any headless check, but it matters for **synthetic input**: macOS delivers synthesised keystrokes
and clicks to the frontmost application, so they will go to whatever is frontmost instead of the demo,
which makes a working feature look broken. See "Making the app frontmost without Launch Services" below for the one-line fix, which was measured working.

## The other route that works: `open -n`

```sh
open -n ~/src/Kolvir/Kolvir.Demo.App/bin/Debug/net10.0-macos/osx-arm64/KolvirDemoApp.app --args --page 38
```

Goes through Launch Services, so the app **does** become frontmost - the right choice when a check
needs to receive synthetic clicks or keystrokes. `open -n` starts a second instance even if one is
already running; use plain `open` to activate and reuse a running one.

## Running the inner binary directly

```sh
~/src/Kolvir/Kolvir.Demo.App/bin/Debug/net10.0-macos/osx-arm64/KolvirDemoApp.app/Contents/MacOS/KolvirDemoApp --page 38
```

Works - the process runs and draws. Like the launch-profile route it may not take activation, so use
it for renders and harnesses that do not need input, where it is the fastest option because it skips
Launch Services entirely.

## Which route to use

| Need | Route |
| --- | --- |
| A normal run while developing | `dotnet run --launch-profile mac -- --page N` |
| A headless render or a harness with no input | the inner binary directly, or the launch profile |
| A check that drives real clicks or keystrokes | `open -n ...app --args --page N`, because it makes the app frontmost |
| Anything at all | build first; `dotnet run` without the profile draws nothing |

## TCC permissions: what `macvm` needs, and how the failures look

A remote session gets **no** privacy permissions by default. Two are needed, and macOS attributes an
SSH session's requests to a process that is *not* the one you would guess - worth reading before you
change anything, because granting the wrong path changes nothing and gives no feedback.

Find the identity macOS is actually using:

```sh
log show --last 5m --predicate 'subsystem == "com.apple.TCC"' --style compact | grep -iE "AUTHREQ_ATTRIBUTION|deny|denied"
```

which prints:

```
attribution={responsible={ identifier=com.apple.sshd-keygen-wrapper,
                           responsible_path=/usr/libexec/sshd-keygen-wrapper,
                           binary_path=/usr/libexec/sshd-session },
             accessing={ identifier=com.apple.screencapture, binary_path=/usr/sbin/screencapture }, ...}
Service kTCCServiceScreenCapture does not allow prompting; returning denied.
```

So the **responsible** process is `/usr/libexec/sshd-keygen-wrapper`, while `/usr/libexec/sshd-session`
is only the binary it execs. Grant both, in **System Settings -> Privacy & Security** (use `+`, then
Cmd+Shift+G to type a path):

| Setting | Path | Needed for |
| --- | --- | --- |
| Accessibility | `/usr/libexec/sshd-session` | reading another app's UI, `key code`, `set frontmost` |
| Screen Recording | `/usr/libexec/sshd-keygen-wrapper` | `screencapture` |

A grant only applies to **new** connections, so reconnect rather than retrying in the same session.
The two failure messages name permissions at best indirectly:

| Message | Missing permission |
| --- | --- |
| `could not create image from display` | Screen Recording, on `sshd-keygen-wrapper` |
| `System Events got an error: osascript is not allowed assistive access. (-1728)` | Accessibility |
| `Could not find a valid Xcode developer path` | not TCC at all - see the prerequisites above |

`could not create image from display` reads like "the VM has no screen". It does: check with
`CGGetActiveDisplayList` before believing that, because a headless-looking error on a machine with a
working display will send you hunting for a display that was never the problem.

Confirm the grant landed before running anything that depends on it:

```sh
osascript -e 'tell application "System Events" to tell process "Terminal" to return name of window 1'
screencapture -x /tmp/probe.png && ls -l /tmp/probe.png
```

Both must succeed. Note that `tell application "System Events" to return name of first process`
succeeding proves **nothing** - it works with no permissions at all, which is exactly the trap that
made a broken session look healthy.

## Driving the app with `osascript`, and the `-25211` trap

With the grants above in place, `osascript` and System Events work over SSH, and UI-element queries
against a named process succeed.

There is one AppleScript form that fails, and it is worth knowing by name because the error message
blames permissions:

```
tell application "System Events" to get name of every window of every process whose name contains "Kolvir"
-> System Events got an error: osascript is not allowed assistive access. (-25211)
```

Enumerating the **windows of every** process reaches into applications that do not expose themselves
to accessibility, and System Events fails on the first one it cannot read. Address the process
directly instead, which works:

```sh
osascript -e 'tell application "System Events" to tell process "KolvirDemoApp" to get name of every window'
# -> Kolvir - AppKit
```

Note the names: the **process** is `KolvirDemoApp`, while the **window** is titled `Kolvir - AppKit`.

## Making the app frontmost without Launch Services

The launch-profile route does not activate the app, which matters because macOS delivers synthesised
input to the frontmost application. Activate it explicitly - measured working:

```sh
osascript -e 'tell application "System Events" to set frontmost of process "KolvirDemoApp" to true'
osascript -e 'tell application "System Events" to get name of first process whose frontmost is true'
# -> KolvirDemoApp
```

So `dotnet run --launch-profile mac` followed by that activation is enough to drive real clicks and
keystrokes, with no involvement of the bundle's Launch Services entry.

## Clicking and screenshotting from a script

System Events can move the pointer but its `click at {x, y}` is unreliable against Skia-drawn views.
Posting real CGEvents is what works, and it needs the Accessibility grant from the section above.
A minimal helper, compiled once with the Command Line Tools that are already installed:

```swift
// click.swift - usage: click <x> <y>   (screen points, not pixels)
import CoreGraphics
import Foundation
let a = CommandLine.arguments
let p = CGPoint(x: Double(a[1])!, y: Double(a[2])!)
let src = CGEventSource(stateID: .hidSystemState)
CGEvent(mouseEventSource: src, mouseType: .mouseMoved, mouseCursorPosition: p, mouseButton: .left)?.post(tap: .cghidEventTap)
usleep(200_000)
let down = CGEvent(mouseEventSource: src, mouseType: .leftMouseDown, mouseCursorPosition: p, mouseButton: .left)!
down.setIntegerValueField(.mouseEventClickState, value: 1)
down.post(tap: .cghidEventTap)
usleep(120_000)
let up = CGEvent(mouseEventSource: src, mouseType: .leftMouseUp, mouseCursorPosition: p, mouseButton: .left)!
up.setIntegerValueField(.mouseEventClickState, value: 1)
up.post(tap: .cghidEventTap)
```

```sh
swiftc -O click.swift -o click
./click 423 492          # points: pixel coordinate / 2
```

Diagnose a click that does nothing in this order, before suspecting the control:

1. `CGPreflightPostEventAccess()` - if `false`, events are being dropped, and the fix is the TCC grant,
   not the coordinates.
2. Did the pointer move? Read it back with `CGEvent(source: nil)!.location`.
3. **Are the coordinates in points?** Divide the screenshot pixel position by 2.
4. Is the app frontmost? Synthesised input goes to whatever is frontmost.

A click that lands in the wrong place is silent, so **measure the target instead of reading a scaled
screenshot**. Render or capture the window, then find the control from the pixel data - for a filled
accent button, scan for the longest run of the accent colour in each row and take the middle of the
band it occupies.

Ask the app to tell you what happened, too: the demo's status bar reports the result of most actions
(`Window activated - it has the keyboard focus.`, `Colour dialog cancelled; the swatch is unchanged.`,
`Light palette applied - every control recoloured on the next frame.`), so a screenshot after the
interaction distinguishes "the click missed" from "the feature is broken".

A click on a non-activating button is also a good smoke test of the whole chain: pick something with
an obvious, reversible effect (the demo's **Switch appearance** flips the palette and says so in the
status bar) and confirm it before moving on to the feature you actually care about.

## Other things that bite on this host
- **Screenshots** on `macvm` are **2992x1870 pixels for a 1496x935-point display - exactly 2x**.
  Coordinate systems are in points everywhere else (window origins, accessibility, CGEvent), so
  divide a pixel coordinate by two before clicking it. Getting this wrong does not error; it clicks
  somewhere else and the feature looks broken. Measure the target from the PNG rather than eyeballing
  the screenshot, and see "Clicking and screenshotting from a script" below.
- **The app follows the desktop appearance** and opens dark under a dark desktop. Pass `--light` when
  comparing renders, or the comparison will be of two themes rather than two builds.
- **A modal dialog that never gets focus** looks like a dialog that never opened. If a modal window
  appears but ignores Enter and Escape, check it was activated before suspecting the dialog itself.

## Close the app when you are finished testing

Always leave the Mac host as you found it: **no demo app still running**. A session that leaves the
app up causes real problems for whoever comes next - a later launch fights the instance that is
already there, synthetic input lands in the wrong process, a screenshot shows someone else's window,
and a modal dialog left open can block the next test. Do this at the end of every run, whether the
test passed or failed:

```sh
# Close the app and the `dotnet run` that started it.
pkill -f KolvirDemoApp
pkill -f "dotnet run --project Kolvir.Demo.App"

# Confirm nothing is left: both commands must print nothing.
pgrep -fl KolvirDemoApp
pgrep -fl "dotnet run --project Kolvir.Demo.App"
```

Close any modal window the test opened before killing the process where you can, so a half-finished
dialog cannot be left on screen by a process that exits mid-session. If you started the app from a
script, stop that script too rather than leaving it waiting on a child that no longer exists.

The same applies to a harness or a temporary build directory: remove what you created, and leave the
shared checkout on its own branch with a clean `git status`.

This is not a macOS-only rule - the Windows and Linux hosts are test machines too, and a demo left
running there causes the same confusion. Close it the same way after every run:

```powershell
# Windows
Stop-Process -Id (Get-Process KolvirDemoApp).Id
```

```sh
# Linux
pkill -f KolvirDemoApp
```
