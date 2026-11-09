using SkiaSharp;
using XPlatUI.Undo;

namespace XPlatUI.Svg.Tests;

/// <summary>
/// The base every test class derives from: a factory with the SVG provider in it, and the
/// process-wide state a test may have changed put back afterwards.
/// </summary>
/// <remarks>
/// <para>
/// There is no registry to register into and nothing to unregister: a factory is composed from an
/// explicit list, so a test states which formats it is testing and two tests cannot see each other's.
/// That is why the old "register for one test and put the process back" machinery is gone.
/// </para>
/// <para>
/// The parse cache is still process-wide - it is a property of the package rather than of a load -
/// so it is emptied after every test: a test that counts parses must not be able to see another
/// test's parses.
/// </para>
/// </remarks>
public abstract class SvgTestBase
{
    /// <summary>The factory under test: SVG plus the built-in raster decoder.</summary>
    protected XPlatImageDataFactory Factory { get; }

    protected SvgTestBase()
    {
        Factory = new XPlatImageDataFactory([XPlatSvgImageFormatProvider.Shared]);
    }

    /// <summary>A factory over exactly the providers a test names.</summary>
    protected static XPlatImageDataFactory FactoryWith(params IXPlatImageFormatProvider[] providers) =>
        new(providers);

    /// <summary>Loads a document, with <c>currentColor</c> resolved to <paramref name="colour"/>.</summary>
    protected XPlatImageData LoadColoured(Stream stream, SKColor? colour) =>
        Factory.LoadFromStream(stream, new XPlatSvgOptions { CurrentColor = colour });

    /// <summary>Assigns what the factory made of the document to a control.</summary>
    protected void Load(XPlatImage image, Stream stream) => image.Image = Factory.LoadFromStream(stream);

    /// <summary>A control showing what the factory made of the document.</summary>
    protected XPlatImage Show(Stream stream) => new() { Image = Factory.LoadFromStream(stream) };

    [TestCleanup]
    public void RestoreProcessState()
    {
        XPlatSvg.ClearCache();
        XPlatTheme.FontFamily = null;
        XPlatTheme.Palette = XPlatPalette.Light;
        XPlatUndoClock.Current = TimeProvider.System;
    }
}
