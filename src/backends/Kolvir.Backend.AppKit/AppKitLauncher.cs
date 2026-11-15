using AppKit;
using Foundation;

namespace Kolvir.Backend.AppKit;

/// <summary>
/// Opens a URI through Launch Services, which resolves the scheme to the application the user has
/// registered for it.
/// </summary>
internal sealed class AppKitLauncher : ILauncher
{
    public bool OpenUrl(string url)
    {
        using var nsUrl = NSUrl.FromString(url);
        if (nsUrl is null)
        {
            return false;
        }

        return NSWorkspace.SharedWorkspace.OpenUrl(nsUrl);
    }
}
