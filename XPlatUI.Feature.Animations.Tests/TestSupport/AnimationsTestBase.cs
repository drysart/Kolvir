using XPlatUI.Undo;

namespace XPlatUI.Animations.Tests;

/// <summary>
/// The base every test class derives from, so process-wide state a test may have changed is put back
/// afterwards even when the test fails.
/// </summary>
/// <remarks>
/// <para>
/// The theme, the clipboard, the undo clock and the launcher are static by design - they are
/// process-wide decisions an application makes once - and a test that builds an
/// <see cref="XPlatApplication"/> installs them. Restoring them here is what lets these tests run in
/// any order and several times in a row.
/// </para>
/// <para>
/// Tests in this assembly are not parallelised: an animation is driven on the UI thread, and the
/// library assumes a single UI thread, so the suite is deliberately serial. It also means the one
/// test that builds an application never races another test for it.
/// </para>
/// </remarks>
public abstract class AnimationsTestBase
{
    [TestCleanup]
    public void RestoreProcessState()
    {
        XPlatTheme.FontFamily = null;
        XPlatTheme.Palette = XPlatPalette.Light;
        XPlatUndoClock.Current = TimeProvider.System;
        XPlatClipboard.SetText(null);
        XPlatClipboard.SetHtml(null, null);
        XPlatLauncher.Current = null!;
    }
}
