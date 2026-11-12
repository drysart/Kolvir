using Gio;
using XPlatUI.Backend.Gtk4;

namespace XPlatUI.Badges;

/// <summary>
/// Shows the badge on the desktop's application icon, by emitting the Unity LauncherEntry signal the
/// docks listen for.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no platform API for this.</b> GTK 4 has no badge and no urgency call at all — the only
/// urgency symbol in the library is GDK's X11-only escape hatch, which does nothing on Wayland — so
/// what a Linux application has is a convention that dock implementations follow: it broadcasts
/// <c>com.canonical.Unity.LauncherEntry.Update</c> on the session bus and the dock, if it implements
/// the protocol, draws a badge on the icon whose desktop entry matches the first argument.
/// </para>
/// <para>
/// <b>The signal is a broadcast, not a call.</b> Dash-to-dock (which is what Ubuntu ships, as
/// <c>ubuntu-dock</c>) owns the well-known name <c>com.canonical.Unity</c> and subscribes to the
/// signal from any sender; there is no method to invoke, and a method-style call answers
/// <c>UnknownMethod</c>. The properties it reads are <c>count</c> and <c>count-visible</c>, plus
/// <c>urgent</c> for the highlight this backend uses for urgency.
/// </para>
/// <para>
/// <b>The desktop entry is the whole contract.</b> The first argument is
/// <c>application://&lt;desktop-id&gt;.desktop</c>, and the dock matches it against installed entries,
/// so an application that runs out of a build directory with no <c>.desktop</c> file gets nothing at
/// all — silently, which is the one failure mode worth documenting. The id comes from the backend
/// rather than being reconstructed here, because it is derived from the application's instance key
/// and the two must agree with the file that was installed.
/// </para>
/// <para>
/// <b>Stock GNOME Shell implements none of this.</b> The dock extension is what draws the badge, so on
/// a bare GNOME desktop the feature reports its capabilities and shows nothing.
/// </para>
/// <para>
/// <b>Threading.</b> Emitting is a write on a D-Bus connection, which GLib allows from any thread, so
/// no marshalling is needed here. The desktop id is resolved from the backend and cached: it cannot
/// change while the application runs.
/// </para>
/// </remarks>
internal sealed class Gtk4AppBadgePresenter : IXPlatAppBadgePresenter
{
    private const string Interface = "com.canonical.Unity.LauncherEntry";
    private const string ObjectPath = "/com/canonical/Unity/LauncherEntry";

    private readonly Gtk4ApplicationBackend _backend;

    /// <summary>The desktop id, resolved once the application exists and kept for the run.</summary>
    private string? _desktopId;

    private DBusConnection? _bus;

    /// <remarks>
    /// Public because that is the only kind of constructor the service provider will look at, even on
    /// a type that is itself internal: a private or internal constructor makes the registration
    /// unresolvable at run time with a message that names the type rather than the access level.
    /// </remarks>
    public Gtk4AppBadgePresenter(Gtk4ApplicationBackend backend) => _backend = backend;

    /// <summary>
    /// A number and an attention highlight. The protocol has no text and no dot, so the model turns
    /// those into <c>urgent</c> before they reach here.
    /// </summary>
    public XPlatBadgeCapabilities Capabilities { get; } = new()
    {
        Count = true,
        Urgent = true,
    };

    /// <inheritdoc />
    public void Apply(XPlatBadgeState state)
    {
        var desktopId = DesktopId();

        if (desktopId is null)
        {
            // The application is not up yet, so there is no id to name and no dock listening for one.
            // The next change tries again; a badge set before Run() is the only case that reaches it.
            return;
        }

        var bus = _bus ??= DBusConnection.Get(BusType.Session);

        if (bus is null)
        {
            return;
        }

        var showing = state.Kind == XPlatBadgeKind.Count;

        // Every property is sent on every change rather than only the one that differs: the dock
        // keeps the last value it saw, so a badge that clears has to say so explicitly.
        var builder = GLib.VariantBuilder.New(GLib.VariantType.New("a{sv}"));
        builder.AddValue(GLib.Variant.NewDictEntry(
            GLib.Variant.NewString("count"),
            GLib.Variant.NewVariant(GLib.Variant.NewInt64(showing ? state.Count : 0))));
        builder.AddValue(GLib.Variant.NewDictEntry(
            GLib.Variant.NewString("count-visible"),
            GLib.Variant.NewVariant(GLib.Variant.NewBoolean(showing))));
        builder.AddValue(GLib.Variant.NewDictEntry(
            GLib.Variant.NewString("urgent"),
            GLib.Variant.NewVariant(GLib.Variant.NewBoolean(state.Urgent))));

        bus.EmitSignal(
            destinationBusName: null,
            objectPath: ObjectPath,
            interfaceName: Interface,
            signalName: "Update",
            parameters: GLib.Variant.NewTuple([
                GLib.Variant.NewString($"application://{desktopId}.desktop"),
                builder.End()]));
    }

    /// <summary>
    /// The application's desktop id, which is the id the backend gave the Gtk application and therefore
    /// the name its <c>.desktop</c> file must have.
    /// </summary>
    private string? DesktopId()
    {
        if (_desktopId is { Length: > 0 })
        {
            return _desktopId;
        }

        _desktopId = _backend.NativeApplication?.ApplicationId;
        return _desktopId is { Length: > 0 } ? _desktopId : null;
    }
}
