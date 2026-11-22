# Run it on each platform

The same program runs on all three systems. What differs is what you install, which target framework
you build for, and what the window looks like once it is up.

| | Windows | macOS | Linux |
| --- | --- | --- | --- |
| Target framework | `net10.0-windows` | `net10.0-macos` | `net10.0` |
| Besides the .NET 10 SDK | nothing | the macOS workload and Xcode | the GTK 4 runtime |
| Build it on | Windows, or any machine with `EnableWindowsTargeting` | a Mac | any machine |
| Output | an executable | an `.app` bundle | an executable |

## Windows

```bash
dotnet run
```

The window appears with the system's title bar and frame. Recolouring the caption strip with
`Window.CaptionColor` needs Windows 11 (build 22000 or later); on Windows 10 that request throws
`NotSupportedException`, so ask first with `Window.ChromeCapabilities` and see the
[platform matrix](../reference/platform-matrix.md). The colour is a literal `SKColor`, a palette
role, or `InheritedValue.Instance` for the platform's own; a role is resolved when it is assigned and
again on a palette change.

**Per-monitor DPI.** Add an application manifest so the window scales per monitor rather than relying
on bitmap stretching:

```xml
<application xmlns="urn:schemas-microsoft-com:asm.v3">
  <windowsSettings>
    <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
  </windowsSettings>
</application>
```

Reference it from the project with `<ApplicationManifest>app.manifest</ApplicationManifest>`.

**Seeing console output.** A `WinExe` has no console of its own. Set
`new ApplicationOptions { AttachConsole = true }` to borrow the launching process's console for
diagnostics.

## macOS

```bash
dotnet workload install macos
dotnet build
open bin/Debug/net10.0-macos/osx-arm64/HelloKolvir.app
```

The bundle is produced by the build. Its name comes from `<AssemblyName>`, and its identity from an
`Info.plist` in the project directory:

| Key | Value |
| --- | --- |
| `CFBundleName` | the name shown in the menu bar |
| `CFBundleDisplayName` | the name shown in Finder |
| `CFBundleIdentifier` | a reverse-DNS identifier, unique to your application |
| `CFBundleExecutable` | the assembly name |
| `CFBundlePackageType` | `APPL` |
| `CFBundleVersion`, `CFBundleShortVersionString` | your version numbers |
| `LSMinimumSystemVersion` | the oldest macOS you support |
| `NSHighResolutionCapable` | `true` |
| `NSPrincipalClass` | `NSApplication` |

The build signs the bundle ad hoc. A copy that came from a download needs its quarantine flag cleared
before it will open:

```bash
xattr -dr com.apple.quarantine HelloKolvir.app
```

The macOS workload refuses an Xcode whose version does not match its bindings, with an error that
names the Xcode it wants. A newer Xcode builds and runs the application unchanged, so the check can be
turned off for a build:

```bash
dotnet build -p:ValidateXcodeVersion=false
```

The menu bar at the top of the screen is the system's, whatever `MenuPresentation` asks for; the
menus under it, and context menus, follow the setting.

## Linux

Install the GTK 4 runtime; on Debian and Ubuntu that is the `libgtk-4-1` package, and a desktop
install usually has it already.

```bash
dotnet run
```

The window is drawn entirely by Kolvir, including its title bar strip and caption buttons, so it looks
the same on GNOME, KDE and anything else with a GTK 4 runtime. Without a display, `dotnet run` ends
with an error from GTK rather than a window; use [the headless render](#rendering-with-no-window)
instead.

**Keep-above does not work here.** `Window.TopMost = true` is accepted and has no effect. See the
[platform matrix](../reference/platform-matrix.md) for the rest.

## Rendering with no window

Reference `Kolvir.Backend.Headless` and paint the same control tree to a PNG instead of showing it.
Nothing reads the display, so this works in a test, on a build server and over ssh:

```csharp
var backend = new HeadlessApplicationBackend();
using var application = new Application(backend);

var window = new Window("Hello", new Label("Hello, Kolvir."))
{
    ClientSize = new SKSize(480f, 240f),
};

window.Show();
application.Run();
backend.LastWindow!.SavePng("hello.png");
```

Add `using Kolvir.Backend.Headless;` beside the other usings. The run returns as soon as the window
has been laid out and painted, and the image is the same control tree the window would show.

## Publishing

Publish for each system you ship for, from a machine that can build it:

| System | Runtime identifier |
| --- | --- |
| Windows x64 | `win-x64` |
| Windows on Arm | `win-arm64` |
| macOS on Apple silicon | `osx-arm64` |
| macOS on Intel | `osx-x64` |
| Linux x64 | `linux-x64` |
| Linux on Arm | `linux-arm64` |

```bash
dotnet publish -c Release -r linux-x64
```
