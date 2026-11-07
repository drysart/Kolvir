# Running the demo app on macOS

Status: the one page to read before trying to launch the sample on a Mac. Every route below was
measured on `Timothys-Mac-mini.local` (osx-arm64, .NET 10.0.401) on 2026-11-07, not inferred from
the SDK docs. If a launch stops working, work through this document before experimenting.

The macOS build is not a loose executable. The `net10.0-macos` target builds an **`.app` bundle**, and
the only binary in it is `Contents/MacOS/XPlatUIDemoApp`:

```
XPlatUIDemo.App/bin/Debug/net10.0-macos/osx-arm64/XPlatUIDemoApp.app/Contents/MacOS/XPlatUIDemoApp
```

There is no `XPlatUIDemoApp` beside the bundle and no supported way to run it as a plain managed
assembly, because the app needs its native apphost to register with AppKit.

## Build first

```sh
cd ~/src/XPlatUI
/usr/local/share/dotnet/dotnet build XPlatUIDemo.App -c Debug
```

`dotnet` is not on `PATH` for `bash -lc` on the Mac host; use the absolute path shown. A build is
required before any run route here — every route below launches whatever is already in `bin/`.

## Plain `dotnet run` does not work — do not waste time on it

```sh
dotnet run --project XPlatUIDemo.App -- --page 38     # exits immediately, code 0, nothing drawn
```

It resolves to a managed exec rather than the apphost, which is why it exits successfully having
drawn nothing:

```
RunCommand   = dotnet
RunArguments = exec ".../bin/Debug/net10.0-macos/osx-arm64/XPlatUIDemoApp.dll"
```

No error is printed, so this looks like a silent failure rather than a misconfiguration. That is the
trap that cost time: the app never starts, and the absence of an error suggests something deeper.

## The route that works: `dotnet run` with the `mac` launch profile

`XPlatUIDemo.App/Properties/launchSettings.json` carries a `mac` profile whose `commandName` is
`Executable` and whose `executablePath` is the bundle's binary. With it, `dotnet run` launches the
app properly, and arguments after `--` reach it:

```sh
/usr/local/share/dotnet/dotnet run --project XPlatUIDemo.App --launch-profile mac -- --page 38
```

Measured: the process stays alive, and `pgrep -fl XPlatUIDemoApp` shows the binary running with
`--page 38`. This is the preferred route.

**One caveat, measured:** an app launched this way does **not** come to the front. `lsappinfo front`
reported Finder while the demo was running. That is fine for looking at a window yourself, and fine
for any headless check, but it matters for **synthetic input**: macOS delivers synthesised keystrokes
and clicks to the frontmost application, so they will go to whatever is frontmost instead of the demo,
which makes a working feature look broken. See "Making the app frontmost without Launch Services" below for the one-line fix, which was measured working.

## The other route that works: `open -n`

```sh
open -n ~/src/XPlatUI/XPlatUIDemo.App/bin/Debug/net10.0-macos/osx-arm64/XPlatUIDemoApp.app --args --page 38
```

Goes through Launch Services, so the app **does** become frontmost — the right choice when a check
needs to receive synthetic clicks or keystrokes. `open -n` starts a second instance even if one is
already running; use plain `open` to activate and reuse a running one.

## Running the inner binary directly

```sh
~/src/XPlatUI/XPlatUIDemo.App/bin/Debug/net10.0-macos/osx-arm64/XPlatUIDemoApp.app/Contents/MacOS/XPlatUIDemoApp --page 38
```

Works — the process runs and draws. Like the launch-profile route it may not take activation, so use
it for renders and harnesses that do not need input, where it is the fastest option because it skips
Launch Services entirely.

## Which route to use

| Need | Route |
| --- | --- |
| A normal run while developing | `dotnet run --launch-profile mac -- --page N` |
| A headless render or a harness with no input | the inner binary directly, or the launch profile |
| A check that drives real clicks or keystrokes | `open -n …app --args --page N`, because it makes the app frontmost |
| Anything at all | build first; `dotnet run` without the profile draws nothing |

## Driving the app with `osascript`, and the `-25211` trap

`osascript` and System Events **work** on this host over SSH; accessibility is already granted
(`AXIsProcessTrusted()` reports `true`, and UI-element queries against a named process succeed). Do
not go looking for TCC settings to change, and do not switch to a CGEvent helper for that reason.

There is one AppleScript form that fails, and it is worth knowing by name because the error message
blames permissions:

```
tell application "System Events" to get name of every window of every process whose name contains "XPlat"
→ System Events got an error: osascript is not allowed assistive access. (-25211)
```

Enumerating the **windows of every** process reaches into applications that do not expose themselves
to accessibility, and System Events fails on the first one it cannot read. Address the process
directly instead, which works:

```sh
osascript -e 'tell application "System Events" to tell process "XPlatUIDemoApp" to get name of every window'
# → XPlat UI - AppKit
```

Note the names: the **process** is `XPlatUIDemoApp`, while the **window** is titled `XPlat UI - AppKit`.

## Making the app frontmost without Launch Services

The launch-profile route does not activate the app, which matters because macOS delivers synthesised
input to the frontmost application. Activate it explicitly — measured working:

```sh
osascript -e 'tell application "System Events" to set frontmost of process "XPlatUIDemoApp" to true'
osascript -e 'tell application "System Events" to get name of first process whose frontmost is true'
# → XPlatUIDemoApp
```

So `dotnet run --launch-profile mac` followed by that activation is enough to drive real clicks and
keystrokes, with no involvement of the bundle's Launch Services entry.

## Other things that bite on this host
- **Screenshots** are 3840×2160 on this display (Retina 2×). Window origins reported by the windowing
  layer are in points; multiply by two for pixels.
- **The app follows the desktop appearance** and opens dark under a dark desktop. Pass `--light` when
  comparing renders, or the comparison will be of two themes rather than two builds.
- **A modal dialog that never gets focus** looks like a dialog that never opened. If a modal window
  appears but ignores Enter and Escape, check it was activated before suspecting the dialog itself.
