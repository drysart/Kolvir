using System.ComponentModel;

namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// Enacts one extension handler pair on a control, for the node being applied and the node it replaces.
/// </summary>
/// <param name="previous">The node that produced the control on the previous render, or null on a first apply.</param>
/// <param name="current">The node being applied.</param>
/// <param name="target">The control to attach the new handler to and detach the old one from.</param>
/// <remarks>
/// <para>
/// One delegate shape covers all three cases <c>PatchFrom</c> has to express, because it holds both
/// nodes: a first apply is <paramref name="previous"/> null, a change is two different handlers, and a
/// removal is a current node that carries none. The actuator reads both values out of the nodes itself,
/// so the generated node class needs no per-member apply or patch line - one call into
/// <see cref="VNodeExtensionActuatorSet.Apply"/> covers every member the process has installed.
/// </para>
/// <para>
/// This is the machinery behind a generated node's extension handler pairs, not something an
/// application writes. It is public because generated code is emitted into the <em>application's</em>
/// assembly, where the library's internals are not reachable.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate void VNodeExtensionActuator(VNode? previous, VNode current, Control target);

/// <summary>
/// The process-wide list of extension handler pairs that have been installed, and the node-side store
/// they read their values from.
/// </summary>
/// <remarks>
/// <para>
/// An extension handler pair applies to a receiver <em>type</em> rather than to a use, so a node class
/// that names the pair's accessor roots the pair and everything the pair reaches whether or not the
/// application ever sets it. Installing on first use is what keeps that out of the published image:
/// until some assignment touches the member, nothing in any generated node names its accessor, and the
/// attach/detach code is unreachable and trims away. Once installed the member is applied permanently
/// for the process - a first-use effect rather than a per-node opt-in, which is the shape a trimmer can
/// still see through (<c>dev-docs/FEATURE-COST-POLICY.md</c> section 3).
/// </para>
/// <para>
/// Installation happens once per member per process, so the lock is only ever taken while the array is
/// still growing: the array is published copy-on-write and read through a volatile field at apply time.
/// Nothing in this repository constructs nodes off the UI thread and no thread rule is documented, so
/// this is cheap insurance rather than a requirement, and the design does not rest on an undocumented
/// invariant.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class VNodeExtensionActuatorSet
{
    private static readonly object Gate = new();

    private static volatile Entry[] s_entries = [];

    /// <summary>
    /// Records the value an extension handler member carries on <paramref name="node"/>, installing the
    /// member the first time it is touched.
    /// </summary>
    /// <param name="node">The node the member was set on.</param>
    /// <param name="token">The member's identity token, the same object every node of every class uses.</param>
    /// <param name="value">The handler, or null when the member is set to nothing.</param>
    /// <param name="actuator">What enacts the member, installed with the first value.</param>
    /// <remarks>
    /// Setting is what installs, which is the invariant the apply path relies on: a value cannot exist
    /// for a member that was never installed, unless something wrote the store without going through
    /// here. Nothing does, and <see cref="Apply"/> says so rather than skipping the value.
    /// </remarks>
    public static void Set(VNode node, object token, object? value, VNodeExtensionActuator actuator)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(actuator);

        Install(token, actuator);
        node.Extensions[token] = value;
    }

    /// <summary>Reads the value an extension handler member carries on <paramref name="node"/>.</summary>
    /// <typeparam name="T">The member's handler type.</typeparam>
    /// <param name="node">The node to read, or null for "no node", which reads as default.</param>
    /// <param name="token">The member's identity token.</param>
    /// <returns>The handler, or default when the node does not carry one.</returns>
    public static T Get<T>(VNode? node, object token)
    {
        ArgumentNullException.ThrowIfNull(token);

        var extensions = node?.ExtensionsOrNull;

        if (extensions is not null && extensions.TryGetValue(token, out var value))
        {
            return (T)value!;
        }

        return default!;
    }

    /// <summary>
    /// Enacts every installed extension handler member against <paramref name="current"/>, which is the
    /// single call a generated node makes instead of per-member apply and patch lines.
    /// </summary>
    /// <param name="previous">The node that produced the control on the previous render, or null on a first apply.</param>
    /// <param name="current">The node being applied.</param>
    /// <param name="target">The control being configured or patched.</param>
    /// <exception cref="InvalidOperationException">
    /// The node carries a value for a member that was never installed, which means something wrote the
    /// store directly and the value would otherwise be skipped in silence.
    /// </exception>
    public static void Apply(VNode? previous, VNode current, Control target)
    {
        var entries = s_entries;

        for (int i = 0; i < entries.Length; i++)
        {
            entries[i].Actuator(previous, current, target);
        }

        var extensions = current.ExtensionsOrNull;

        if (extensions is null)
        {
            return;
        }

        foreach (var token in extensions.Keys)
        {
            if (!Contains(entries, token))
            {
                throw new InvalidOperationException(
                    "A node carries an extension handler for a member that was never installed. A value "
                    + "is recorded by setting the member, which is what installs it, so the store was "
                    + "written some other way.");
            }
        }
    }

    private static void Install(object token, VNodeExtensionActuator actuator)
    {
        // Unguarded first: a member installed once is found here for the rest of the process, and the
        // lock is only taken while the list is still growing.
        var entries = s_entries;

        for (int i = 0; i < entries.Length; i++)
        {
            if (ReferenceEquals(entries[i].Token, token))
            {
                return;
            }
        }

        lock (Gate)
        {
            entries = s_entries;

            for (int i = 0; i < entries.Length; i++)
            {
                if (ReferenceEquals(entries[i].Token, token))
                {
                    return;
                }
            }

            var grown = new Entry[entries.Length + 1];
            Array.Copy(entries, grown, entries.Length);
            grown[entries.Length] = new Entry(token, actuator);

            // A plain assignment under the lock, read as a volatile field: publication of the array is
            // what the apply path synchronises on.
            s_entries = grown;
        }
    }

    private static bool Contains(Entry[] entries, object token)
    {
        for (int i = 0; i < entries.Length; i++)
        {
            if (ReferenceEquals(entries[i].Token, token))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One installed member: its identity token and the delegate that enacts it.</summary>
    private readonly struct Entry
    {
        /// <summary>Records one installed member.</summary>
        /// <param name="token">The token the member's values are stored under.</param>
        /// <param name="actuator">What enacts the member.</param>
        internal Entry(object token, VNodeExtensionActuator actuator)
        {
            Token = token;
            Actuator = actuator;
        }

        /// <summary>The token the member's values are stored under.</summary>
        internal object Token { get; }

        /// <summary>What enacts the member.</summary>
        internal VNodeExtensionActuator Actuator { get; }
    }
}
