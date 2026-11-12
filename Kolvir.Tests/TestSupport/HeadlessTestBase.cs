using Kolvir;
using Kolvir.Undo;

namespace Kolvir.Tests;

/// <summary>
/// The base every test class derives from, so the process-wide state a test may have changed is put
/// back afterwards even when the test fails.
/// </summary>
/// <remarks>
/// <para>
/// The theme, the clipboard, the undo clock and the menu presenter are static by design - they are
/// process-wide decisions an application makes once - which makes them the only shared state in the
/// suite. Restoring them here is what lets the tests run in any order and several times in a row.
/// </para>
/// <para>
/// Tests in this assembly are not parallelised. Parallelism would put two tests over the same static
/// palette, and the library itself assumes a single UI thread, so the suite is deliberately serial.
/// </para>
/// </remarks>
public abstract class HeadlessTestBase
{
    [TestCleanup]
    public void RestoreProcessState()
    {
        Theme.FontFamily = null;
        Theme.Palette = Palette.Light;
        UndoClock.Current = TimeProvider.System;
        Clipboard.SetText(null);
        Clipboard.SetHtml(null, null);
        // The setter accepts null and installs its no-op fallback, which is what a test that put a
        // recording launcher in place has to put back.
        Launcher.Current = null!;
    }
}
