namespace Kolvir.VirtualDom;

/// <summary>
/// Registers a control so that the virtual DOM generator emits a node class for it.
/// </summary>
/// <remarks>
/// <para>
/// The attribute goes on a manifest class - any <c>partial</c> class an application declares - one
/// attribute per control:
/// </para>
/// <code>
/// [VirtualDom(typeof(Label))]
/// [VirtualDom(typeof(Button), Name = "PushButton")]
/// partial class AppControls;
/// </code>
/// <para>
/// It is a compile-time declaration rather than a run-time call on purpose: the generator sees the
/// exact type from the <c>typeof</c>, so it emits direct property assignments and the trimmer keeps
/// precisely the properties an application registered. Nothing reflects, and nothing needs
/// <c>[DynamicallyAccessedMembers]</c>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class VirtualDomAttribute : Attribute
{
    /// <summary>Registers a control for node generation.</summary>
    /// <param name="controlType">The control type to generate a node class for.</param>
    /// <exception cref="ArgumentNullException"><paramref name="controlType"/> is null.</exception>
    public VirtualDomAttribute(Type controlType)
    {
        ArgumentNullException.ThrowIfNull(controlType);
        ControlType = controlType;
    }

    /// <summary>The control type to generate a node class for.</summary>
    public Type ControlType { get; }

    /// <summary>
    /// The generated node class's name, without the <c>V</c> prefix. Defaults to the control type's
    /// own name, and exists to separate two controls that share a simple name.
    /// </summary>
    public string? Name { get; set; }
}
