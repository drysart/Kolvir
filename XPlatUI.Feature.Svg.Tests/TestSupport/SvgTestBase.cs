using XPlatUI.Undo;

namespace XPlatUI.Svg.Tests;

/// <summary>
/// The base every test class derives from, so process-wide state a test changed is put back
/// afterwards even when the test fails.
/// </summary>
/// <remarks>
/// Image format providers are process-wide by design, and every test that registers one has to
/// leave the process as it found it or the next test inherits SVG support it did not ask for. The
/// registrations a test opens through this type are disposed after it, and the parse cache is
/// emptied, because a test that counts parses must not be able to see another test's parses.
/// </remarks>
public abstract class SvgTestBase
{
    private readonly Stack<IDisposable> _registrations = new();

    /// <summary>Registers a provider for the length of one test.</summary>
    protected IDisposable Register(IXPlatImageFormatProvider provider)
    {
        var handle = XPlatImageFormats.Add(provider);
        _registrations.Push(handle);
        return handle;
    }

    /// <summary>Registers the SVG provider for the length of one test.</summary>
    protected IDisposable RegisterSvg() => Register(XPlatSvgImageFormatProvider.Shared);

    [TestCleanup]
    public void RestoreProcessState()
    {
        while (_registrations.Count > 0)
        {
            _registrations.Pop().Dispose();
        }

        XPlatSvg.ClearCache();
        XPlatTheme.FontFamily = null;
        XPlatTheme.Palette = XPlatPalette.Light;
        XPlatUndoClock.Current = TimeProvider.System;
    }
}
