using Kolvir.Backend.Headless;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Kolvir.Feature.AssetLoading.Tests;

/// <summary>
/// A real application, a real window and a real control tree on the headless backend, with the asset
/// loading feature registered the way an application registers it.
/// </summary>
/// <remarks>
/// <para>
/// The application is created first and the window is opened separately, which is what lets a test
/// assign a <c>SourceUri</c> to a control that cannot reach its services yet: that is the state every
/// control starts in, and it is the state the ordering rules are about.
/// </para>
/// <para>
/// Everything is torn down by disposing the application, which closes and disposes its windows, so no
/// control, load or subscription outlives a test.
/// </para>
/// </remarks>
internal sealed class ImageSourceScene : IDisposable
{
    private readonly Application _application;

    /// <summary>Creates the application and registers the feature.</summary>
    /// <param name="configure">Extra registrations - normally the loader factory.</param>
    /// <param name="registerInfrastructure">
    /// False to leave <c>AddAssetLoading()</c> out, which is the misconfigured application case.
    /// </param>
    public ImageSourceScene(Action<IServiceCollection>? configure = null, bool registerInfrastructure = true)
    {
        _application = new Application(new HeadlessApplicationBackend(), new ApplicationOptions
        {
            ConfigureServices = services =>
            {
                if (registerInfrastructure)
                {
                    services.AddAssetLoading();
                }

                configure?.Invoke(services);
            },
        });
    }

    /// <summary>The application, for a test that has to reach its services.</summary>
    public Application Application => _application;

    /// <summary>Opens a window on a tree, which is what attaches the tree to a host.</summary>
    /// <param name="root">The control tree.</param>
    /// <param name="width">Client width in logical pixels.</param>
    /// <param name="height">Client height in logical pixels.</param>
    /// <returns>The window, so a test can keep or close it.</returns>
    public Window Open(Control root, float width = 200f, float height = 200f)
    {
        var window = new Window("asset loading", root) { ClientSize = new SKSize(width, height) };

        window.Host.PerformLayout();

        return window;
    }

    /// <summary>Runs another layout pass, as a frame would.</summary>
    /// <param name="window">The window whose tree to lay out.</param>
    public static void Layout(Window window) => window.Host.PerformLayout();

    /// <inheritdoc/>
    public void Dispose() => _application.Dispose();
}
