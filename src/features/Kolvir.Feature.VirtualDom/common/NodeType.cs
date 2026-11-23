namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// What a node type knows about the control it makes: which type it is, and how to create one.
/// </summary>
/// <remarks>
/// <para>
/// Every node class holds one static instance, and the reconciler compares two nodes' instances by
/// reference to answer "can I reuse this control?". By <em>reference</em> rather than by the control
/// type, deliberately: two node classes could wrap the same control, and patching one from the other
/// would hand a node class a previous node it cannot cast.
/// </para>
/// <para>
/// Creation goes through <see cref="Create"/> rather than <see cref="Activator"/> so that the trimmer
/// sees a real constructor call, and so a control without a parameterless constructor is a build
/// error instead of a run-time one.
/// </para>
/// </remarks>
public sealed class NodeType
{
    private readonly Func<Control> _create;

    /// <summary>Describes a control a node can make.</summary>
    /// <param name="controlType">The control type this node produces.</param>
    /// <param name="create">Creates an instance of that type.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="controlType"/> or <paramref name="create"/> is null.
    /// </exception>
    public NodeType(Type controlType, Func<Control> create)
    {
        ArgumentNullException.ThrowIfNull(controlType);
        ArgumentNullException.ThrowIfNull(create);

        ControlType = controlType;
        _create = create;
    }

    /// <summary>The control type this node produces.</summary>
    public Type ControlType { get; }

    /// <summary>Creates a new instance of <see cref="ControlType"/>.</summary>
    /// <returns>The control, configured by nobody: a node applies its properties to it afterwards.</returns>
    public Control Create() => _create();
}
