using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace XPlatUI.Badges;

/// <summary>
/// The badge an application gains by opting into the feature: <c>app.Badge</c>.
/// </summary>
/// <remarks>
/// <para>
/// An extension member rather than a member of <see cref="XPlatApplication"/>, because app badging is
/// a rung-3 feature: the assembly is the opt-in, and an application that never references the package
/// carries none of it. The badge is created the first time it is asked for and lives in a table keyed
/// by the application, so an application that references the package and never looks at the badge
/// pays for a table lookup and nothing else.
/// </para>
/// <para>
/// The table is weak on the application and strong on the badge, which is what a badge wants: it
/// holds nothing but the values the application set, so keeping it alive for as long as the
/// application is alive costs one object.
/// </para>
/// <para>
/// A badge that has an application to watch also subscribes to its windows' activation, because that
/// is where an attention request ends: the user coming back is what stops a flash or a bounce, so it
/// is also what sets <see cref="XPlatAppBadge.Urgent"/> back to false. Those subscriptions keep the
/// badge reachable for the application's lifetime, which the weak table already assumed.
/// </para>
/// <para>
/// The platform half is resolved once, here, from the services the application configured. When the
/// application never called <c>AddAppBadges</c> nothing is registered and the badge is created over
/// nothing: its setters are accepted and dropped, and <see cref="XPlatAppBadge.Capabilities"/> is
/// <see cref="XPlatBadgeCapabilities.None"/> so the application can tell.
/// </para>
/// </remarks>
public static class XPlatApplicationBadgeExtensions
{
    /// <summary>One badge per application, created on first use.</summary>
    private static readonly ConditionalWeakTable<XPlatApplication, XPlatAppBadge> Badges = new();

    extension(XPlatApplication application)
    {
        /// <summary>
        /// This application's badge, which is created the first time it is read.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The badge outlives every window, because the platforms only have an application badge:
        /// showing or closing a window does not change what it holds.
        /// </para>
        /// <para>
        /// Reading this before the application's services are configured - that is, before it is
        /// constructed - is not possible through this extension, and reading it after the opt-in has
        /// run always finds the platform half.
        /// </para>
        /// </remarks>
        public XPlatAppBadge Badge => Badges.GetValue(application, Create);
    }

    /// <summary>Creates the badge over the platform half, or over nothing when the feature is not opted into.</summary>
    private static XPlatAppBadge Create(XPlatApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new XPlatAppBadge(application.Services.GetService<IXPlatAppBadgePresenter>(), application);
    }
}
