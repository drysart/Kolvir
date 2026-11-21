# Install

## What you need

- The .NET 10 SDK. Kolvir targets `net10.0`, and a platform target framework on Windows and macOS.
- A desktop to run on: Windows, macOS, or Linux with a GTK 4 runtime installed. A build needs none of
  that; a window does.

## Add the packages

```bash
dotnet add package Kolvir
dotnet add package Kolvir.Backend
```

`Kolvir` is the library. `Kolvir.Backend` supplies the platform integration and picks the right one
for the framework you build for, so an application references both and names no platform anywhere.

Add a feature package when you use the feature. Each one is the whole of its own request: an
application that never references it does not carry it.

| Package | Adds |
| --- | --- |
| `Kolvir.Feature.DragDrop` | Drag and drop between applications |
| `Kolvir.Feature.Animations` | Timeline animations and easings |
| `Kolvir.Feature.Svg` | SVG images |
| `Kolvir.Feature.AnimatedGif` | Animated GIF images |
| `Kolvir.Feature.AnimatedWebp` | Animated WebP images |
| `Kolvir.Feature.WoffFonts` | WOFF and WOFF 2 web fonts |
| `Kolvir.Feature.Audio` | Sound playback |
| `Kolvir.Feature.Badges` | A count or dot on the application's own icon |
| `Kolvir.Feature.VirtualDom` | Describing the UI as data and reconciling it |
| `Kolvir.Feature.Observables` | Change notification for your models |

## The project file

A project builds exactly one target framework per operating system: the macOS target framework needs
a workload that only a Mac supplies, and one portable target framework would build a Linux binary on a
Mac. Keep all three lines:

```xml
<PropertyGroup>
  <TargetFramework Condition="$([MSBuild]::IsOSPlatform('windows'))">net10.0-windows</TargetFramework>
  <TargetFramework Condition="$([MSBuild]::IsOSPlatform('osx'))">net10.0-macos</TargetFramework>
  <TargetFramework Condition="$([MSBuild]::IsOSPlatform('linux'))">net10.0</TargetFramework>
  <OutputType>WinExe</OutputType>
</PropertyGroup>
```

A single-targeted project would either fail on macOS or build a Linux binary on it, so keep all three
lines. `WinExe` keeps a console window from appearing behind your window on Windows; drop it if you
want the console output.

## The package feed

While Kolvir is pre-1.0, the packages come from this repository's GitHub Packages feed. Point NuGet at
it, once:

```xml
<!-- nuget.config beside your solution -->
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="kolvir" value="https://nuget.pkg.github.com/drysart/index.json" />
  </packageSources>
</configuration>
```

The feed needs credentials even for a public package: authenticate with `dotnet nuget add source` or
with a personal access token in `~/.nuget/NuGet/NuGet.Config`, and restore.

## Check that it worked

```bash
dotnet build
```

An empty build that reports no errors is the whole check; there is nothing to run yet. Next,
[your first window](first-window.md).
