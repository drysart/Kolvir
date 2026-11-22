namespace Kolvir;

/// <summary>
/// The value that says "nothing was assigned here, so use the inherited one".
/// </summary>
/// <remarks>
/// <para>
/// Some members are inherited when an application does not set them: a control's font, or its
/// foreground colour, falls back to what its parent or the theme supplies. To write such a member
/// as a union of "a real value" and "no value at all", this sentinel takes the place of the second
/// case - <c>Union&lt;SKColor, ThemeColor, InheritedValue&gt;</c> rather than a nullable colour plus
/// a separate flag.
/// </para>
/// <para>
/// The type carries no behaviour and nothing to configure. It has one value, and a <c>default</c>
/// <see cref="InheritedValue"/> is that value. Assign <see cref="PropertyValue.Inherited"/> where a
/// case is wanted, and read it back through <c>TryGetValue&lt;InheritedValue&gt;</c>.
/// </para>
/// <para>
/// It is only meaningful as a case of a union. A member typed as an <see cref="InheritedValue"/>
/// on its own says nothing, and the union types know nothing about inheritance: the sentinel is a
/// case like any other, and deciding that "inherit" means the parent's value is the control's job.
/// </para>
/// </remarks>
public readonly struct InheritedValue
{
    /// <summary>The word <c>Inherit</c>, which is what this value means.</summary>
    /// <returns><c>Inherit</c>.</returns>
    public override string ToString() => "Inherit";
}
