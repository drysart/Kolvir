namespace Kolvir;

/// <summary>
/// The values an application names when it assigns a property that can inherit.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Inherited"/> is the readable spelling of the "take the parent's value" case that
/// <see cref="InheritedValue"/> represents. The type stays <see cref="InheritedValue"/> so that it
/// can appear as a union case; this class is only where the sentinel is named at an assignment,
/// where <c>PropertyValue.Inherited</c> says what is meant and <c>InheritedValue</c> alone does not.
/// </para>
/// <para>
/// The value is a property rather than a field on purpose: it cannot be reassigned, it allocates
/// nothing, and the value it returns is the same <c>default</c> <see cref="InheritedValue"/> the
/// type has.
/// </para>
/// </remarks>
public static class PropertyValue
{
    /// <summary>
    /// The "nothing was assigned here, so take the inherited one" case: the value to assign when an
    /// inherited property should go back to following its parent.
    /// </summary>
    /// <value>The one <see cref="InheritedValue"/>, identical to <c>default</c>.</value>
    public static InheritedValue Inherited => default;
}
