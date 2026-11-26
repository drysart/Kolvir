using System.ComponentModel;

namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// Marks a generated node member, or a generated node class, that could not be produced because more
/// than one declaration wanted its name.
/// </summary>
/// <remarks>
/// <para>
/// Two extension members can claim the same property name on a node, and two controls can claim the
/// same node class name. Neither can be resolved by choosing: the choice would be the generator's
/// rather than the application's, and it would be silent. So the generator emits a placeholder that
/// carries this attribute and nothing else - a property that holds no value, or a class that creates
/// nothing - and the analyzer beside the generator reports the conflict wherever that placeholder is
/// <em>used</em>. A placeholder nobody names costs nothing and fails nothing.
/// </para>
/// <para>
/// The attribute carries structured data rather than a message, because the analyzer writes the
/// message: the name that is in dispute, and one claim per contender. A claim is
/// <c>containing type::member</c> for a member, and the control type's own identity for a node class
/// name, which has no member to name.
/// </para>
/// <para>
/// Generated code applies it across assemblies, which is why it is public rather than internal; it is
/// not meant to be applied by hand. It is inert: nothing reads it at run time, and no generated
/// member other than the placeholder carries it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class VNodeNameConflictAttribute : Attribute
{
    /// <summary>Marks a name that more than one declaration claimed.</summary>
    /// <param name="name">The generated member or class name that is in dispute.</param>
    /// <param name="contenders">One claim per contender, in the form the analyzer reports.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="contenders"/> is null.</exception>
    public VNodeNameConflictAttribute(string name, params string[] contenders)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(contenders);

        Name = name;
        Contenders = contenders;
    }

    /// <summary>The generated member or class name that is in dispute.</summary>
    public string Name { get; }

    /// <summary>
    /// One claim per contender: <c>containing type::member</c> for a member name, and the control
    /// type's identity for a node class name.
    /// </summary>
    public string[] Contenders { get; }
}
