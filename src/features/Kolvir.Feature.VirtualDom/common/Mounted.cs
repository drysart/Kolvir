namespace Kolvir.VirtualDom;

/// <summary>
/// One node, the control it owns, and the mounted children underneath them both.
/// </summary>
/// <remarks>
/// This is the reconciler's record of what is currently on screen. It exists because the diff is
/// driven by the previous <em>node</em> rather than by the control: the node knows which properties
/// were assigned, which is what makes "restore this property" answerable without reading the control
/// back out.
/// </remarks>
internal sealed class Mounted
{
    internal Mounted(VNode node, Control control)
    {
        Node = node;
        Control = control;
    }

    /// <summary>The node that produced <see cref="Control"/> on the latest render.</summary>
    internal VNode Node { get; set; }

    /// <summary>The control currently on screen for this node.</summary>
    internal Control Control { get; }

    /// <summary>The mounted children of <see cref="Control"/>, in control order.</summary>
    internal List<Mounted> Children { get; } = [];
}
