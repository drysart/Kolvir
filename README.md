# Kolvir

A tiny, AOT-friendly, cross-platform UI layer for .NET that borrows the WinForms/WPF programming
model - controls, docking, table layout, events - and draws everything itself with
[SkiaSharp](https://github.com/mono/SkiaSharp).

The library has no OS dependency of its own. A backend supplies the window, the input, the
clipboard, the dialogs and the system appearance; the controls paint themselves on top of it. One
control tree runs unchanged on Windows, macOS and Linux, and the same tree can be rendered
headlessly to a PNG, which is what the screenshots in the documentation are made from.

Kolvir is pre-1.0. The package feed carries test versions while the API settles.

```
Windows            raw Win32            net10.0-windows
macOS              AppKit               net10.0-macos
Linux              GTK4                 net10.0
```

## What it gives you

- **The WinForms model, without WinForms.** `Dock`, `DockStyle.Fill`, `Margin`/`Padding`,
  `TabIndex` and tab order, `Enabled`/`EffectiveEnabled`, `Visible`, and events as `Add...Handler` pairs.
- **Layout.** A `TableLayoutPanel` with attached cells (`TableColumn`, `TableRow`, and the spans),
  a `SplitContainer` that drags, `StackPanel`, `WrapPanel`, and scrolling panels.
- **Controls.** Label, RichLabel, Button, CheckBox, RadioButton, ProgressBar, TextBox (single- and
  multi-line, with undo, spans and autocomplete), ListBox, ListView, TreeView, ComboBox, TabControl,
  GroupBox, TrackBar, NumericUpDown, DateTimePicker, LinkLabel, ColorPicker, ToolTip, SplitButton,
  Expander, ToolStrip, Toast, BusyIndicator, Overlay, Image, MenuBar/ContextMenu, and the common
  dialogs.
- **Text.** Embedded TrueType and web fonts, text selection, and the clipboard through the backend.
- **Theme.** One palette with a light and a dark presentation, swapped at run time; every control
  paints from it, including its borders and focus rings.
- **Images.** Bitmaps, SVG, animated GIF and animated WebP through one format-provider seam, so
  `Images.LoadFromStream(...)` reads whichever it is handed.
- **AOT.** No reflection, no serializers: the sample application publishes with NativeAOT on all
  three platforms.

Features that cost native code are separate packages, so an application pays only for what it uses:
drag and drop, animations, app badging, audio, SVG, animated GIF, animated WebP and web fonts. There
is also a virtual DOM, `Kolvir.Feature.VirtualDom`, for applications that would rather describe their
controls than build them - see [the developer guide](dev-docs/DEVELOPER-GUIDE.md#virtual-dom).

## Getting started

Kolvir needs .NET 10. A project builds exactly one target framework per operating system, because
the macOS backend needs the macOS workload and only a Mac can supply it:

```xml
<PropertyGroup>
  <TargetFramework Condition="$([MSBuild]::IsOSPlatform('windows'))">net10.0-windows</TargetFramework>
  <TargetFramework Condition="$([MSBuild]::IsOSPlatform('osx'))">net10.0-macos</TargetFramework>
  <TargetFramework Condition="$([MSBuild]::IsOSPlatform('linux'))">net10.0</TargetFramework>
</PropertyGroup>
```

```bash
dotnet add package Kolvir
dotnet add package Kolvir.Backend     # the selector: whichever backend matches the TFM
```

Then the whole program is the window and a call to run it:

```csharp
using SkiaSharp;
using Kolvir;
using Kolvir.Backend;

using var application = new Application(KolvirBackend.CreateDefault());
var window = new Window("Hello", new Label("Hello, Kolvir."))
{
    ClientSize = new SKSize(480f, 240f),
};

application.MainWindow = window;
window.Show();
return application.Run();
```

`KolvirBackend.CreateDefault()` picks the backend for the target framework the application built for
and makes SkiaSharp's native library resolvable; `Application` registers the services everything else
resolves from. Both are what an application used to have to arrange itself.

## The sample

The repository's own application is the best tour of the library: sixty-three pages, one per control
or feature, with the code beside each one.

```bash
dotnet run --project src/demo/Kolvir.Demo.App
```

A page can also be rendered headlessly - useful for screenshots, visual checks in CI, and looking at
a control without a window server:

```bash
dotnet run --project src/demo/Kolvir.Demo.App -- --render out.png 1000 800 --page borders
```

`--page` takes a page's id (the first column of the sample's page table, such as `audio`) or its
position in the list; `--light` and `--dark` force an appearance.

## Documentation

- [`docs/`](docs/index.md) - the guide for applications built with Kolvir: install, a first window,
  the concepts, a task guide per feature, and a control reference generated from the library's own
  documentation. Build it with `dotnet run --project src/tools/Kolvir.Docs -- build`.
- [`dev-docs/DEVELOPER-GUIDE.md`](dev-docs/DEVELOPER-GUIDE.md) - for working **on** Kolvir: the design,
  the controls, the application model and the platform notes.
- [`dev-docs/APPLICATION-MODEL.md`](dev-docs/APPLICATION-MODEL.md) - how an application is put
  together, and the decisions behind the API.
- [`dev-docs/DOCS-PLAN.md`](dev-docs/DOCS-PLAN.md) - how the guide is organised and how it is built.

## Working on Kolvir

The repository is laid out so a project's place says what it is:

```
Kolvir.slnx      every project, at the top level, in solution folders matching the directories
src/core         Kolvir, and Kolvir.Backend which selects a backend
src/backends     the Win32, AppKit, GTK4 and headless backends
src/features     the separate feature packages
src/generators   the build-time halves that ship inside a feature package
src/demo         the sample application, and the minimal measurement fixture
tests            a test project per project, in the same folders src/ uses
dev-docs         the design and rationale documents
docs             the application developer guide: Markdown sources, nav.yml and its screenshots
src/tools        the documentation tool and the snippets it compiles
```

Start with [`dev-docs/DEVELOPER-GUIDE.md`](dev-docs/DEVELOPER-GUIDE.md), which has the build and
test commands, the rules a control has to follow before it ships, and the platform runbooks. The
test suite runs headless on all three platforms:

```bash
dotnet build Kolvir.slnx
dotnet test Kolvir.slnx
```
