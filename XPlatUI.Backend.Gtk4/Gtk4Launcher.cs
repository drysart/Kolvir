namespace XPlatUI.Backend.Gtk4;

/// <summary>
/// Opens a URI through GIO, which resolves the scheme to the application the desktop has
/// registered for it.
/// </summary>
internal sealed class Gtk4Launcher : IXPlatLauncher
{
    public bool OpenUrl(string url)
    {
        try
        {
            // GTK's own launcher is the portal-aware one: it goes through the desktop portal when
            // the session is sandboxed, and falls back to GIO directly when it is not. The binding
            // reports a failed launch by throwing, so reaching the next line means it was accepted.
            Gtk.Functions.ShowUri(null, url, 0);
            return true;
        }
        catch (Exception)
        {
            // A scheme with no handler, or no desktop to ask.
            return false;
        }
    }
}
