# Your first window

This is a running window with one control in it, and then the same program with the pieces named.

## 1. Create the project

```bash
dotnet new console -n HelloKolvir
cd HelloKolvir
dotnet add package Kolvir
dotnet add package Kolvir.Backend
```

Set the target framework as [Install](install.md) shows: three conditional lines, one per operating
system. The project needs `net10.0-windows` on Windows, `net10.0-macos` on macOS and `net10.0` on
Linux.

## 2. Replace Program.cs

Put these three usings at the top of the file, and the method below inside `Program`:

```csharp
using Kolvir;
using Kolvir.Backend;
using SkiaSharp;
```

```csharp
[STAThread]
private static int Main()
{
    using var application = new Application(KolvirBackend.CreateDefault());

    var window = new Window("Hello", new Label("Hello, Kolvir."))
    {
        ClientSize = new SKSize(480f, 240f),
    };

    application.MainWindow = window;
    window.Show();
    return application.Run();
}
```

`[STAThread]` matters on Windows: the system's file dialogs do not return on a multi-threaded
apartment. It is ignored on the other two systems.

## 3. Run it

```bash
dotnet run
```

You should see:

- a window titled **Hello**, 480 by 240 logical pixels;
- the text **Hello, Kolvir.** at the left of the window, vertically centred, in the application's
  font (the system's, unless `ApplicationOptions.FontFamily` says otherwise) and the palette's
  foreground colour;
- a working title bar with minimise, maximise and close, and the window resizing with its content
  following.

The numbers are logical pixels at 96 DPI. On a display scaled to 150% the window is 720 by 360 device
pixels and looks the same size.

## What the pieces are

| Piece | What it does here |
| --- | --- |
| `KolvirBackend.CreateDefault()` | Builds the platform integration for the framework the project was built for, so the program names no platform. |
| `Application` | Owns the run loop, the services and the palette. `using` disposes it when `Main` returns. |
| `Window` | A top-level window with a title, a client size and a content control. |
| `Label` | A control that draws text. Passing it to `Window` makes it the window's content. |
| `application.Run()` | Runs until the last window closes, then returns the exit code. |

`application.MainWindow` matters when the window is not the one that keeps the application alive: it
is the window a dialog without an explicit owner is shown over, and the one that comes forward when a
second copy of the application is launched.

## Next

- [Run it on each platform](run-on-each-platform.md) for the prerequisites and what the same program
  looks like on macOS and Linux.
- [How the pieces fit together](../concepts/architecture.md) for what happens between `Run()` and the
  first frame.
- The sample application, which has a page for almost every control and feature:

  ```bash
  dotnet run --project src/demo/Kolvir.Demo.App
  ```
