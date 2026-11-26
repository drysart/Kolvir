namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// Declares, as a compile-time constant, the value a node restores a property to when a render stops
/// setting it.
/// </summary>
/// <remarks>
/// <para>
/// A generated node restores a dropped property to what the control's constructor leaves it at, which
/// it finds out by constructing one control of the node's type and reading every pristine value off
/// it once. That is right for a property whose default is a decision the constructor makes, and it is
/// not always what a property wants: <c>TableColumn</c> is <c>0</c> because the table says so, and
/// asking a control for it costs a control.
/// </para>
/// <para>
/// Applied to a property, this attribute replaces that read with the constant:
/// </para>
/// <code>
/// [VNodePropertyUnsetValue(0)]
/// public int Column
/// {
///     get => ...;
///     set => ...;
/// }
/// </code>
/// <para>
/// The value has to be a compile-time constant of the property's own type - a primitive, a string, an
/// enum, or <c>null</c> for a property that can hold one. Anything else is ignored and the node falls
/// back to reading the value from a constructed control, which is always correct and merely costs a
/// construction per node type.
/// </para>
/// <para>
/// The attribute is read from the property the generator flattens, so it reaches a control's own
/// properties and the extension properties an application declares. It cannot be put on a member of
/// a library the application does not own, and for those the constructed-control read is what
/// happens.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class VNodePropertyUnsetValueAttribute : Attribute
{
    /// <summary>Declares the value a node restores the property to.</summary>
    /// <param name="value">The value, which has to be a compile-time constant of the property's type.</param>
    public VNodePropertyUnsetValueAttribute(object? value) => Value = value;

    /// <summary>The value a node restores the property to.</summary>
    public object? Value { get; }
}
