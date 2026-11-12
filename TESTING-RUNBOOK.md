# Testing runbook

Status: the one page to read before running anything on a test host. Each host below was measured -
versions read from the machine, screenshot and input routes exercised - rather than inferred. When a
route stops working, work through this document before experimenting.

The suite that CI runs is not this: `README.md` ("Running the tests") has the local `dotnet test`
commands and the category filter, and `.github/workflows/tests.yml` is what the pull request runs. This
document is about the machines that have a **desktop**, where a window can be seen, clicked and
photographed.

## Pick a host

| Host | `ssh` alias | What it is | Use it for |
|---|---|---|---|
| Windows box | (this machine) | Windows 11 Pro, 200% scale | The default for Windows work: builds, the Win32 backend, driving the sample with real input, headless renders. |
| `linux` | `ssh linux` | Ubuntu 24.04, GNOME 46, **X11** | The X11 baseline. Everything GTK4-related except Wayland-specific behaviour. |
| `linux2` | `ssh linux2` | Ubuntu 26.04, GNOME 50, **Wayland** | Anything that has to be proven on Wayland, which is the mode a user on a current GNOME desktop gets. |
| `macvm` | `ssh macvm` | macOS 27.2 VM, arm64 | The canonical macOS host. A VM, so nothing of the user's is on it. |
| `mac` | `ssh mac` | macOS 27.0 Mac mini, arm64 | **Only after confirming with the user.** It is their real desktop. See below. |

### Use criteria

- **`mac` needs an explicit yes from the user before you touch it.** It is the user's own desktop, with
  their browser and their work on it. During this runbook's own probe, `osascript` clicking at an
  innocent-looking coordinate reached in and clicked the user's Safari window. Prefer `macvm` for
  everything interactive; use `mac` when the question is specifically about real hardware (graphics
  driver, a real display, the Dock on a physical machine) and say what you are about to do first.
  `screencapture` is read-only and is the one thing that is safe there, but it still captures whatever
  the user is looking at.
- `linux` is the cheap, fast host for X11 and for anything that does not care which display server is
  in play.
- `linux2` is the acceptance host for Wayland. If a change touches window chrome, focus, activation or
  anything GTK4 reports differently under Wayland, prove it here.
- The Windows box is the machine the agent runs on, so `Stop-Process`, `dotnet` and the file system are
  local and immediate. It is also a shared desktop: launching a window puts it on the user's screen.

## The Windows box

| | |
|---|---|
| OS | Windows 11 Pro, build 26300 (`Microsoft Windows NT 10.0.26300.0`) |
| CPU | 13th Gen Intel Core i9-13900K |
| Display | **3840 x 2160 physical**, 200% scale (system DPI 192) - a logical desktop of 1920 x 1080, delivered by an RDP virtual display |
| Shell | Windows PowerShell 5.1 |
| .NET SDK | 10.0.403, including the `macos` and `maui` workloads |

The scale factor is the trap, and it bites twice. A DPI-unaware process is lied to about the whole
desktop, not just about coordinates: it is told the screen is 1920 x 1080 and its captures come back as
the **top-left quarter** of the real 3840 x 2160 display, which looks like a complete desktop and is not.
`[System.Windows.Forms.Screen]::AllScreens` reports the lie; `GetSystemMetrics(SM_CXSCREEN)` in an aware
process reports 3840.

Anything that clicks, measures or captures has to call

```powershell
Add-Type -Namespace Native -Name Dpi -MemberDefinition @'
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
'@
[Native.Dpi]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null   # PerMonitorAwareV2
```

before the first `user32` call, and then use **physical** pixels everywhere: 3840 x 2160 of them. The
same `Add-Type` trick gives you the rest: `GetWindowRect`, `ClientToScreen`, `SetCursorPos`,
`mouse_event`, `SetWindowPos`.

### Build and run

```powershell
dotnet build XPlatUI.slnx -v q --nologo            # everything, all target frameworks
dotnet build XPlatUIDemo.App\XPlatUIDemo.App.csproj # just the sample
```

The solution builds every framework of every feature package, so it needs the `macos` workload. It is
installed here; on a machine without it, build the specific project instead.

```powershell
$exe = "XPlatUIDemo.App\bin\Debug\net10.0-windows\XPlatUIDemoApp.exe"
Start-Process -FilePath $exe -PassThru | ForEach-Object { $_.Id }   # remember the pid
```

The window title is `XPlat UI - Win32`. Find it with `Get-Process -Name XPlatUIDemoApp` and read
`MainWindowHandle`: `FindWindow` with the title failed here, and the handle from the process is what the
user actually sees.

At 200% scale the sample opens around 2586 x 1711 physical, which fits the 3840 x 2160 display, so
nothing has to be moved on this host. The 59-item navigation list is longer than the visible list box
either way: scroll it (wheel, +/-120 a notch) rather than trying to see it all at once.

The window does have a minimum size - asking for 1900 x 1050 comes back about 1503 tall - so treat
`SetWindowPos` as a request:

```powershell
[IntPtr]$hwnd = (Get-Process -Name XPlatUIDemoApp).MainWindowHandle
[Native.Pos]::SetWindowPos($hwnd, [IntPtr]::Zero, 10, 20, 1900, 1500, 0x0004)   # SWP_NOZORDER
```

### Screenshots

```powershell
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$gfx = [System.Drawing.Graphics]::FromImage($bmp)
$gfx.CopyFromScreen($left, $top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
$gfx.Dispose(); $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
```

In a DPI-aware process that captures exactly the rectangle asked for, in physical pixels - so ask for
the real desktop, 3840 x 2160, or for a window's `GetWindowRect`. Asking for 1920 x 1080 is not an
error and not a smaller screenshot: it is the top-left quarter, captured without complaint.

Three capture traps:

- **`PrintWindow` is not a screen capture.** On the Windows 11 taskbar it returns only the tray and the
  search box, because the task buttons live in a composition island that does not render into a DC.
  `CopyFromScreen` is different: it copies what the compositor is showing, so the taskbar - buttons,
  underlines and **badge overlay icons** - comes out correctly. A 90-pixel strip along the bottom of the
  4K display (y 2070-2160) with a running harness shows its button and the red count disc on top of it,
  which makes badges checkable by eye: crop about 200 px either side of the button and look.
- **A taskbar *flash* may not be photographable, though.** Take the burst anyway - 100-odd frames at
  ~100 ms across the moment the request is made, hashing each frame and saving only the changes - but do
  not conclude from a flat sequence that the flash did not happen. In one measured run over an RDP
  session (130 frames spanning `Urgent` being set while the window was away) the four distinct frames
  were all hover and relayout noise; the overlay icon and the button never brightened. Trust the API
  return value and the app's own readout for `Urgent`, and treat the pixels as a bonus.
- **A locked session has no error.** The capture simply returns a stale or black frame.

For a picture of a page without a window at all, use the sample's headless renderer, which needs no
desktop and no message loop:

```powershell
& $exe --render "$env:TEMP\page.png" 1400 900 --page 58 --dark
```

### Mouse and keyboard

```powershell
[Native]::SetCursorPos($x, $y)                       # physical pixels
[Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)   # LEFTDOWN
[Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)   # LEFTUP
```

- **The wheel takes +/-120 per notch**, not +/-1: `mouse_event(0x0800, 0, 0, [uint32]4294967176, ...)` scrolls
  down one notch (that literal is -120 as a `uint32`; `0xFFFFFFFF` is rejected by PowerShell's binder).
- **`SetForegroundWindow` on another process's window usually does nothing.** A real click activates it
  and is delivered, which is what a test wants anyway.
- **Keyboard:** `[System.Windows.Forms.SendKeys]::SendWait("%{TAB}")`, `"{ENTER}"`, `"^a"`. The sample's
  status bar says "click a button, or Tab/Enter to drive the UI from the keyboard", so a page can be
  driven without the pointer.
- Coordinates come from a screenshot taken by **the same DPI-aware process**. Read them off the image,
  then click.

## `linux` (X11)

| | |
|---|---|
| OS | Ubuntu 24.04.5 LTS, kernel 7.0.0-42-generic |
| Desktop | GNOME Shell 46.0, **X11**, `ubuntu-dock@ubuntu.com` |
| Display | `:10.0`, 3840 x 2160 (RDP); the sample opens 2560 x 1520 at DPI 192 |
| .NET | 10.0.114 at `/usr/lib/dotnet` (`dotnet` is on `PATH`) |
| Installed | `xwd`, `ffmpeg`, `python3`, `libXtst.so.6` |
| Missing | `xdotool`, `xte`, `ydotool`, `wmctrl`, `scrot`, `import`, `gnome-screenshot` |

`DISPLAY` is **empty in a non-interactive ssh session**; every command that touches the display has to
start with `export DISPLAY=:10.0`.

### Build and run

```sh
cd ~/src/XPlatUI
git fetch -q origin && git checkout -q -B <branch> origin/<branch>
dotnet build XPlatUIDemo.App/XPlatUIDemo.App.csproj -v q --nologo
nohup ./XPlatUIDemo.App/bin/Debug/net10.0/XPlatUIDemoApp > /tmp/demo.log 2>&1 &
xwininfo -root -tree | grep -i "xplat ui"       # handle + geometry, e.g. 0x3200004 2560x1520+28+98
```

Run the binary from a terminal you can see, or the demo's GTK warnings go to a log nobody reads - and
those warnings matter: `Gtk-CRITICAL ... assertion 'GTK_IS_WIDGET (widget)' failed` is a real defect, not
noise.

### Screenshots

```sh
export DISPLAY=:10.0
xwd -id 0x3200004 -silent | ffmpeg -y -loglevel error -i - /tmp/window.png   # one window
xwd -root -silent   | ffmpeg -y -loglevel error -i - /tmp/screen.png         # the whole screen
```

`xwd` needs the window **id**; `xwininfo -root -tree | grep -i xplat` has it. Do not be surprised when
the image is bigger than the screen: the window's own size is what gets captured.

**The panel and the dock cannot be captured this way.** GNOME composites its own UI outside any X11
window, so `xwd -root` shows the wallpaper and the applications but not the dock, and
`gnome-screenshot` is not installed (on `linux2` it is, and it falls back to X11 and fails).

### Mouse and keyboard

No injection tool is installed, but `libXtst.so.6` is, so XTest through `ctypes` works with no
packages to install:

```sh
export DISPLAY=:10.0
python3 /tmp/xtest.py warp-click 125 499     # root coordinates, physical pixels
python3 /tmp/xtest.py click 5                # button 5 = one wheel notch down (4 = up)
python3 /tmp/xtest.py key Return             # by keysym name
```

Verified on this host: clicking the sample's navigation list switched the page. Coordinates are **root**
coordinates, which is the window's position from `xwininfo` plus an offset measured in the `xwd` capture
(both physical).

`/tmp/xtest.py` is this, and nothing needs installing to run it:

```python
#!/usr/bin/env python3
import ctypes, sys, time
x11 = ctypes.CDLL("libX11.so.6")
xtst = ctypes.CDLL("libXtst.so.6")
x11.XOpenDisplay.restype = ctypes.c_void_p
x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
x11.XStringToKeysym.restype = ctypes.c_ulong
x11.XKeysymToKeycode.restype = ctypes.c_ubyte

dpy = x11.XOpenDisplay(None)
if not dpy:
    sys.exit("cannot open the display; export DISPLAY=:10.0")

d = ctypes.c_void_p(dpy)
cmd = sys.argv[1]
if cmd == "warp-click":
    xtst.XTestFakeMotionEvent(d, -1, int(sys.argv[2]), int(sys.argv[3]), 0)
    x11.XFlush(d); time.sleep(0.15)
    xtst.XTestFakeButtonEvent(d, 1, True, 0); x11.XFlush(d); time.sleep(0.05)
    xtst.XTestFakeButtonEvent(d, 1, False, 0); x11.XFlush(d)
elif cmd == "click":                      # 1 left, 4 wheel up, 5 wheel down
    button = int(sys.argv[2]) if len(sys.argv) > 2 else 1
    xtst.XTestFakeButtonEvent(d, button, True, 0); x11.XFlush(d); time.sleep(0.05)
    xtst.XTestFakeButtonEvent(d, button, False, 0); x11.XFlush(d)
elif cmd == "key":
    code = x11.XKeysymToKeycode(d, x11.XStringToKeysym(sys.argv[2].encode()))
    xtst.XTestFakeKeyEvent(d, code, True, 0); x11.XFlush(d); time.sleep(0.03)
    xtst.XTestFakeKeyEvent(d, code, False, 0); x11.XFlush(d)
time.sleep(0.2)
```

Kill things **by pid**. `pkill -f <pattern>` matches its own command line over ssh and can take the ssh
session down with it.

## `linux2` (Wayland)

| | |
|---|---|
| OS | Ubuntu 26.04.1 LTS, kernel 7.0.0-42-generic |
| Desktop | GNOME Shell 50.1, **Wayland**, `ubuntu-dock@ubuntu.com`, GTK 4.22 |
| Display | 2560 x 1600 (`Virtual-1`) |
| .NET | 10.0.114 at `/usr/lib/dotnet` |
| Installed | `ydotool` + `ydotoold`, `gnome-screenshot`, `python3`, `dbus-monitor` |
| Missing | `xdotool`, `grim`, `wmctrl` |

Every command needs the session's environment:

```sh
export XDG_RUNTIME_DIR=/run/user/1000
export WAYLAND_DISPLAY=wayland-0
export YDOTOOL_SOCKET=/run/user/1000/.ydotool_socket
```

### Build and run

Same as `linux`. Note that the sample is single-instance: launching it twice only activates the window
that is already open, so a "new build didn't change anything" is usually the old process still running.
Check `pgrep -f XPlatUIDemoApp` before believing a build.

### Screenshots

The obvious routes all fail:

- `org.gnome.Shell.Screenshot.Screenshot` -> `AccessDenied: Screenshot is not allowed`.
- `gnome-screenshot -f out.png` -> "Unable to use GNOME Shell's builtin screenshot interface, resorting
  to fallback X11", then a Gdk assertion, then nothing.
- `grim` is not installed and would not work on GNOME anyway.

**The desktop portal works, with no permission prompt.** `org.freedesktop.portal.Screenshot` returns a
request handle; the `Response` signal on that handle carries a `uri` to the PNG:

```sh
gdbus call --session --dest org.freedesktop.portal.Desktop \
  --object-path /org/freedesktop/portal/desktop \
  --method org.freedesktop.portal.Screenshot.Screenshot "" "{}"
# -> (objectpath '/org/freedesktop/portal/desktop/request/1_344/t')
```

Then subscribe to `org.freedesktop.portal.Request.Response` on that path. Doing the call and the wait
together is easier in `python3` with `Gio`/`GLib`, which is already installed:

```python
import gi, shutil, sys
from gi.repository import Gio, GLib

out = sys.argv[1]
bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
res = bus.call_sync("org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop",
                    "org.freedesktop.portal.Screenshot", "Screenshot",
                    GLib.Variant("(sa{sv})", ("", {})), None, Gio.DBusCallFlags.NONE, -1, None)
handle = res.unpack()[0]
loop, state = GLib.MainLoop(), {}

def on_response(conn, sender, path, iface, signal, params):
    state["response"], state["results"] = params.unpack()
    loop.quit()

bus.signal_subscribe("org.freedesktop.portal.Desktop", "org.freedesktop.portal.Request",
                     "Response", handle, None, Gio.DBusSignalFlags.NONE, on_response)
GLib.timeout_add_seconds(20, loop.quit)
loop.run()
uri = state["results"].get("uri")
if not uri:
    sys.exit(f"screenshot failed: {state}")
shutil.copy(uri.replace("file://", ""), out)
print(out)
```

The response is `(0, {'uri': 'file:///home/trf/Pictures/Screenshot-N.png'})`.

Two notes: the PNG can come back at a different pixel size than the display mode, so read the image size
before deriving coordinates; and the file lands in the user's `~/Pictures`, so delete it afterwards.

This is the only route that photographs the **dock**, since the shell's own UI is inside the same
compositor.

### Mouse and keyboard

`ydotool` and its daemon are installed and running (`ydotoold`, socket at
`$XDG_RUNTIME_DIR/.ydotool_socket`).

```sh
ydotool click 0xC0            # left click: 0xC0 = down+up, 0x01 right, 0x02 middle
ydotool key 56:1 15:1 15:0 56:0   # Alt+Tab, as raw evdev keycodes
ydotool mousemove -a -x N -y N
ydotool mousemove -w -x 0 -y N    # wheel, relative
```

**Absolute coordinates are 16-bit device coordinates, not pixels.** `-a -x 32767 -y 32767` is the middle
of the screen whatever its resolution, so a pixel has to be scaled up (`x * 65535 / width`). Even then
the mapping is worth calibrating before trusting it: in this session the scaled clicks landed inside the
right *window* but not on the intended control. **Keyboard is the dependable route** - Alt+Tab to focus
the window you want, then Tab/Enter/arrows - and it needs no coordinates at all.

CLI keyboard input also demonstrates things a click cannot: it is the only way to reach the shell's own
UI (Alt+Tab, the overview) from a script.

## `macvm` (canonical macOS host)

| | |
|---|---|
| Host | `Timothys-Virtual-Machine.local`, arm64 |
| OS | macOS 27.2 (build 26B123) |
| Xcode | 27.2 - needs `-p:ValidateXcodeVersion=false` (see `MACOS-TESTRUN.md`) |
| .NET | 10.0.403 at **`~/.dotnet/dotnet`**, not on the ssh `PATH` |

`MACOS-TESTRUN.md` is the document for building and launching on this machine; it is not repeated here.
In short: export `PATH="$HOME/.dotnet:$PATH"`, build with `-p:ValidateXcodeVersion=false`, and run the
**`.app` bundle** (`bin/Debug/net10.0-macos/osx-arm64/XPlatUIDemoApp.app`) with `open` - there is no
loose executable beside it.

The clone is `~/src/XPlatUI`; throwaway harnesses live under `~/badge` (`~/badge/src` is a second clone,
`~/badge/harness` the harness project that references it).

### Screenshots

```sh
screencapture -x /tmp/shot.png              # whole screen, no sound, no interaction
screencapture -x -R 0,0,400,300 /tmp/r.png  # a region
```

Verified over ssh on both Macs. This is read-only and safe to run anywhere.

### Mouse and keyboard

```sh
osascript -e 'tell application "System Events" to click at {400, 400}'
osascript -e 'tell application "System Events" to key code 36'      # Return
osascript -e 'tell application "System Events" to keystroke "abc"'
```

System Events is already permitted on the VMs, so this works out of the box; `cliclick` is not
installed. Two caveats:

- `click at {x, y}` answers with the element it hit (`group 1 of scroll area 1 of application process
  Finder`), which is a useful confirmation that the click had a target.
- **That same call on `mac` reached into the user's Safari window.** On a machine someone is using, GUI
  automation is not read-only, whatever the script says.

## `mac` (the Mac mini - ask first)

| | |
|---|---|
| Host | `Timothys-Mac-mini.local`, arm64 |
| OS | macOS 27.0 (build 26A428) |
| Xcode | 27.2, same `ValidateXcodeVersion` workaround |
| .NET | 10.0.401 at **`/usr/local/share/dotnet/dotnet`** (a different path from `macvm`) |

`MACOS-TESTRUN.md` calls this host **retired**: do not use it unless you are specifically told to, and
confirm with the user first. It is their desktop - a browser with work open, windows they care about,
and a Dock they are looking at. `screencapture` is fine; clicking and typing are not.

## Recipes that work everywhere

### Proving an activation-driven behaviour without any input

Anything that reacts to the user coming back - an attention request ending, focus-dependent state - can
be tested with **two windows and no mouse**: show a second window, activate it (the first is now
"away"), set the state, activate the first again, and log the state after each step. The activation that
ends the request is the real one, because it comes from the platform, not from a synthetic message. This
is how the badge feature's urgent reset was proven on all three platforms, in a harness that logs
`while away: urgent=True`, `after coming back: urgent=False`, `after asking again: urgent=True`.

On GTK4 the activation arrives as a `notify::is-active` **after** the `Activate()` call returns, so leave
a second between activating another window and setting the state, or the request is ended by its own
setup.

### Linux desktop entries (dock badges, app icons, window matching)

- The badge signal names `application://<desktop-id>.desktop`, and the dock matches that against
  **installed** entries in `~/.local/share/applications`. No entry, no badge, silently.
- `StartupWMClass` must be the window's WM class, which for a GTK 4 app is the **binary name**, not the
  application id. An entry that gets this wrong can leave the app with no dock item at all, which looks
  exactly like a badge that does not work.
- `XPlatUIDemo.App/install-badge-desktop-entry.sh` writes the sample's entry with the build's absolute
  path; `XPlatUIDemo.App/com.xplatui.ixplatui.desktop` is the file it installs. Remove the entry when
  you are done.

### Files and scripts you ship to a host

- **Endings.** A `.sh` written by PowerShell has CRLF and will fail on the host. Write it with
  `[IO.File]::WriteAllText($path, $text.Replace("`r`n", "`n"))`.
- **Encoding.** `Get-Content -Raw` plus `[IO.File]::WriteAllText` mangles UTF-8 unless the writer is
  given `New-Object System.Text.UTF8Encoding($false)`: U+00A7 comes back as U+00C3 U+201A U+00C2
  U+00A7, and nothing in the result says so. Read the file back rather than trusting the write; the
  markdown is kept in ASCII for this reason, which removes the whole class of failure.
- **Copying a project file** with `scp` once turned a newline into the literal `&#10;` mid-file. Read the
  file back on the host after copying it.

### Leaving a host as you found it

1. Kill the processes you started - by pid.
2. Remove installed desktop entries and any screenshots you left in `~/Pictures` or `/tmp`.
3. Remove the throwaway clone or harness if you created one for the test.
4. `git status` in the shared clone should be clean; if you checked out a branch there, say which.
