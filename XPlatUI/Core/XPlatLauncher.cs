namespace XPlatUI;

/// <summary>
/// Hands a URI to the desktop so the user's own handler - a browser, a mail client, the file
/// manager - opens it. A host adapter installs a platform implementation into
/// <see cref="XPlatLauncher.Current"/>; when none is installed nothing is opened.
/// </summary>
public interface IXPlatLauncher
{
    /// <summary>
    /// Opens <paramref name="url"/>. Returns true when the platform accepted the URI, false when
    /// it has no handler for it or no way to ask for one. The call does not wait for the handler,
    /// so success says the URI was handed over, not that anything came up.
    /// </summary>
    bool OpenUrl(string url);
}

/// <summary>Process-wide access point for opening URIs in the user's default handler.</summary>
public static class XPlatLauncher
{
    private static IXPlatLauncher _current = new NoLauncher();

    /// <summary>
    /// The launcher in use. Adapters assign a platform implementation; assigning null restores the
    /// no-op fallback, which reports every request as unopened.
    /// </summary>
    public static IXPlatLauncher Current
    {
        get => _current;
        set => _current = value ?? new NoLauncher();
    }

    /// <summary>
    /// Opens <paramref name="url"/> with the handler registered for its scheme, returning false
    /// when nothing could be started. The URI is expected to come from the application: a scheme
    /// such as <c>file:</c> reaches the local file system, so user-supplied text belongs behind a
    /// scheme allow-list, not straight through here.
    /// </summary>
    public static bool OpenUrl(string url)
    {
        if (!IsLaunchable(url))
        {
            return false;
        }

        return _current.OpenUrl(url);
    }

    /// <summary>
    /// True when <paramref name="url"/> is a non-empty URI with no control characters. A newline
    /// or tab in the middle would let a URI carry a second argument into a shell-based opener.
    /// </summary>
    public static bool IsLaunchable(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        for (var i = 0; i < url.Length; i++)
        {
            if (char.IsControl(url[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Fallback used when no platform launcher is installed.</summary>
    private sealed class NoLauncher : IXPlatLauncher
    {
        public bool OpenUrl(string url) => false;
    }
}
