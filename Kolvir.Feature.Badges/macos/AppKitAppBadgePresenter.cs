using System.Globalization;
using AppKit;
using Foundation;

namespace Kolvir.Badges;

/// <summary>
/// Shows the badge on the Dock tile, which is the only place macOS has for one.
/// </summary>
/// <remarks>
/// <para>
/// The whole presentation is one property. <c>NSDockTile.BadgeLabel</c> takes a short string and the
/// system draws it as a red bubble with white text over the application's icon, so every presentation
/// the feature offers arrives here as a string: the count as its digits, the label as it was given,
/// and the dot as a bullet, which is what an application shows when it wants a dot rather than a
/// number and is the shape the system draws for it.
/// </para>
/// <para>
/// <b>The bubble's colour is the system's.</b> A badge here is always the same red, whatever the theme
/// or the icon, which is the one presentation the feature cannot match to its own palette - and the
/// reason a themed badge is not offered at all.
/// </para>
/// <para>
/// <b>AppKit is main-thread only, the badge included.</b> The model lets a worker set a count, so a
/// call from anywhere but the main thread is queued on the main queue rather than touching the Dock
/// tile directly.
/// </para>
/// <para>
/// <b>Urgent is a bounce, and it fires once per transition.</b> Setting a count while the application
/// is already urgent does not bounce the Dock again; clearing the flag does not take a bounce back,
/// because a bounce has already happened. That is why the last value is remembered rather than the
/// platform being asked to stop.
/// </para>
/// </remarks>
internal sealed class AppKitAppBadgePresenter : IAppBadgePresenter
{
    /// <summary>
    /// The label macOS draws as a dot. A bullet, because the system renders any short string in the
    /// same bubble and one bullet is what fills it without looking like a number.
    /// </summary>
    private const string DotLabel = "\u2022";

    /// <summary>The last urgent value applied, so a bounce is one per transition rather than one per change.</summary>
    private bool _urgent;

    /// <inheritdoc />
    public BadgeCapabilities Capabilities { get; } = BadgeCapabilities.All;

    /// <inheritdoc />
    public void Apply(BadgeState state)
    {
        if (NSThread.IsMain)
        {
            ApplyOnMainThread(state);
            return;
        }

        // NSOperationQueue rather than NSApplication.BeginInvokeOnMainThread: reading the shared
        // application from a worker would itself be the out-of-thread AppKit call this is avoiding.
        NSOperationQueue.MainQueue.AddOperation(() => ApplyOnMainThread(state));
    }

    private void ApplyOnMainThread(BadgeState state)
    {
        var application = NSApplication.SharedApplication;
        var dock = application?.DockTile;

        if (application is null || dock is null)
        {
            return;
        }

        // An empty label is how the Dock is told to drop the badge; null is accepted too, but the
        // binding declares the property non-nullable and an empty string says the same thing.
        dock.BadgeLabel = state.Kind switch
        {
            BadgeKind.Count => state.Count.ToString(CultureInfo.CurrentCulture),
            BadgeKind.Text => state.Text ?? string.Empty,
            BadgeKind.Dot => DotLabel,
            _ => string.Empty,
        };

        dock.Display();

        if (state.Urgent && !_urgent)
        {
            application.RequestUserAttention(NSRequestUserAttentionType.CriticalRequest);
        }

        _urgent = state.Urgent;
    }
}
