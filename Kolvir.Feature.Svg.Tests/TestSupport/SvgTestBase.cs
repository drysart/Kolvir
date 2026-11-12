using SkiaSharp;
using Kolvir.Undo;

namespace Kolvir.Svg.Tests;

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
    protected ImageDataFactory Factory { get; }

    protected SvgTestBase()
    {
        Factory = new ImageDataFactory([SvgImageFormatProvider.Shared]);
    }

    /// <summary>A factory over exactly the providers a test names.</summary>
    protected static ImageDataFactory FactoryWith(params IImageFormatProvider[] providers) =>
        new(providers);

    /// <summary>Loads a document, with <c>currentColor</c> resolved to <paramref name="colour"/>.</summary>
    protected ImageData LoadColoured(Stream stream, SKColor? colour) =>
        Factory.LoadFromStream(stream, new SvgOptions { CurrentColor = colour });

    /// <summary>Assigns what the factory made of the document to a control.</summary>
    protected void Load(Image image, Stream stream) => image.Source = Factory.LoadFromStream(stream);

    /// <summary>A control showing what the factory made of the document.</summary>
    protected Image Show(Stream stream) => new() { Source = Factory.LoadFromStream(stream) };

    [TestCleanup]
    public void RestoreProcessState()
    {
        Svg.ClearCache();
        Theme.FontFamily = null;
        Theme.Palette = Palette.Light;
        UndoClock.Current = TimeProvider.System;
    }
}
