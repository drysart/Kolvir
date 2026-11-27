using Kolvir;

namespace Kolvir.Feature.VirtualDom.Tests;

/// <summary>
/// A control with settable control-typed properties, one for each creating-slot shape a test needs: a
/// replaceable header, a nullable body, and a split container - which is the nested case, because a
/// split container has slots of its own.
/// </summary>
/// <remarks>
/// Nothing in this library has a settable control-typed property today, which is why the half of the
/// mechanism that creates a control needs a control of its own to be exercised against.
/// </remarks>
public sealed class HostControl : Control
{
    /// <summary>The control shown in the header. A node's header replaces it.</summary>
    public Panel Header { get; set; } = new() { Name = "header" };

    /// <summary>An optional control shown under the header, and null when there is none.</summary>
    public Panel? Body { get; set; }

    /// <summary>A split container below the body, whose own panels are slots of a slot.</summary>
    public SplitContainer? Split { get; set; }
}
