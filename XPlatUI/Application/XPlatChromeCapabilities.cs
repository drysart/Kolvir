namespace XPlatUI;

/// <summary>
/// Which parts of application-owned window chrome a backend implements.
/// </summary>
/// <remarks>
/// <para>
/// This is how to ask without asking for it. The seam's chrome members refuse requests they cannot
/// honour by throwing, so an application reads these first and falls back to a decorated window
/// instead of catching an exception to find out what the platform can do.
/// </para>
/// <para>
/// Dragging and resizing are deliberately not here. <see cref="IXPlatWindowBackend.TryBeginDrag"/>
/// and <see cref="IXPlatWindowBackend.TryBeginResize"/> already report whether they started
/// anything, which is the same question asked at the moment it matters, and returning false is a
/// perfectly usable answer - an application that cannot start a drag simply does not move the window.
/// </para>
/// <para>
/// A backend that has not adopted the feature reports <see cref="None"/>, which is also the default.
/// Capabilities are a property of the backend, so they do not change between windows or over the life
/// of one.
/// </para>
/// </remarks>
public sealed record XPlatChromeCapabilities
{
    /// <summary>
    /// Nothing is supported: the window is decorated by the platform, it draws its own caption
    /// buttons, and the application cannot recolour either.
    /// </summary>
    public static XPlatChromeCapabilities None { get; } = new();

    /// <summary>
    /// The backend can make the window borderless, with the application owning every visible pixel
    /// while the platform keeps the resize band and its window behaviours.
    /// </summary>
    public bool Borderless { get; init; }

    /// <summary>
    /// The backend can draw and hit-test the window's caption buttons itself, so
    /// <see cref="XPlatCaptionButtons.Platform"/> is honourable. On Windows this is the backend
    /// emulating the buttons rather than the platform drawing them.
    /// </summary>
    public bool CaptionButtons { get; init; }

    /// <summary>
    /// The backend honours <see cref="IXPlatWindowBackend.CaptionColor"/> and
    /// <see cref="IXPlatWindowBackend.BorderColor"/>.
    /// </summary>
    public bool WindowColors { get; init; }
}
