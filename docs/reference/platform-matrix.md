# Platform matrix

What each operating system supports. Anything this page does not list behaves the same everywhere, and
no other page repeats these differences: the rest of the guide links here.

| Feature | Windows | macOS | Linux |
| --- | --- | --- | --- |
| Chromeless window (Kolvir draws the frame) | Yes | Yes | Yes |
| The system's caption buttons, still working | Yes | Yes | Yes |
| Window colours (`CaptionColor`, `BorderColor`) | Windows 11 (build 22000) or later | No | Yes |
| Keep-above (`TopMost`) | Yes | Yes | No |
| Menu bar inside the window | Yes | No: always the system bar at the top of the screen | Yes |
| Menus drawn by Kolvir | Yes | Yes, apart from the menu bar | Yes |
| The system's own menus (`MenuPresentation.Native`) | Yes | Yes | Yes |
| Message, file, folder and font dialogs | Yes | Yes | Yes |
| Single instance | Yes | Yes | Yes |
| App badging | Yes | Yes | Only on desktops that support it |
| Drag and drop | Yes | Yes | Yes |
| Audio playback | Yes | Yes | Yes, through PulseAudio |
| Animations | Yes | Yes | Yes |
| SVG, animated GIF, animated WebP, WOFF fonts | Yes | Yes | Yes |
| GPU acceleration | Yes, on by default | Yes, off by default | Yes, on by default |

## The qualified rows

**Window colours.** Setting `Window.CaptionColor` or `Window.BorderColor` where the system does not
support it throws `NotSupportedException`. On Windows the support starts at Windows 11 build 22000.
Both take a colour, a palette role or `InheritedValue.Instance` for the platform's own; a role is
resolved when it is assigned and again when the palette changes.

**Keep-above.** `Window.TopMost = true` is accepted everywhere and has no effect on Linux.

**App badging.** A badge is a request to the desktop, and a desktop can have nothing to show it with.
On Linux that means a dock that implements the launcher convention - Ubuntu's dock does, a stock GNOME
session does not - and where nothing implements it the badge is silently not shown.

**GPU acceleration.** Turning it off with `ApplicationOptions.GfxHardwareAcceleration = false`, or
launching with `--no-hardware-acceleration`, changes how a window is painted and nothing else about
your application.

## Asking at run time

The answers above can differ between two versions of the same system, so ask rather than remember:

| Question | How to ask |
| --- | --- |
| Can this window be chromeless, keep the system's caption buttons, or be recoloured? | `window.ChromeCapabilities` |
| Which drawing path is this application using? | `window.Services.GetService<IGpuAccelerationService>()` |
| Can the desktop show a badge, and which kinds? | `appBadge.Capabilities` |
| Which menus does the system draw here? | `ApplicationOptions.MenuPresentation` |
