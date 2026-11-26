namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// Renames an extension member on the node classes that expose it.
/// </summary>
/// <remarks>
/// <para>
/// An extension member is exposed on a node under its own name, so a node for a control that some
/// static class extends with <c>TableColumn</c> gets a <c>TableColumn</c> property. Two containers can
/// extend the same receiver with the same name, and then neither can be exposed: the node would have
/// two sources for one property. This attribute separates them, one declaration per member:
/// </para>
/// <code>
/// [assembly: VNodeExtensionPropertyName(typeof(TableLayoutPanelExtensions), "TableColumn", "GridColumn")]
/// </code>
/// <para>
/// The container is the static class that declares the member, the member is the name the node would
/// otherwise expose - <c>TableColumn</c> for a property, and <c>DragEnter</c> for the pair behind
/// <c>AddDragEnterHandler</c> - and the replacement is the <em>whole</em> new name rather than a
/// prefix or a suffix. <c>GridColumn</c> is what the node property is called.
/// </para>
/// <para>
/// The member name is a string because <c>nameof</c> does not compile on an extension property
/// (CS9286) - a limitation of the language rather than of this attribute. It does compile on an
/// extension method, so the handler-pair half could have used it; one shape for both is worth more
/// than the little <c>nameof</c> would buy.
/// </para>
/// <para>
/// An override wins over the member's own name. One that lands on a name the node already has from a
/// native member of the control is an error (VD015): the application, not the generator, has to
/// decide which of the two it meant.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class VNodeExtensionPropertyNameAttribute : Attribute
{
    /// <summary>Renames one extension member on the nodes that expose it.</summary>
    /// <param name="container">The static class that declares the member.</param>
    /// <param name="member">The name the node would otherwise expose.</param>
    /// <param name="name">The name to expose instead, in full.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="container"/>, <paramref name="member"/> or <paramref name="name"/> is null.
    /// </exception>
    public VNodeExtensionPropertyNameAttribute(Type container, string member, string name)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(name);

        Container = container;
        Member = member;
        Name = name;
    }

    /// <summary>The static class that declares the member being renamed.</summary>
    public Type Container { get; }

    /// <summary>The name the node would otherwise expose.</summary>
    public string Member { get; }

    /// <summary>The name to expose instead, in full.</summary>
    public string Name { get; }
}
