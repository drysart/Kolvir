namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// What an element node type knows about the element it describes: which type it is, and how to make
/// one.
/// </summary>
/// <remarks>
/// <para>
/// This is the element counterpart of <see cref="NodeType"/>. An element is not a
/// <see cref="Control"/>, so it cannot be created through <see cref="NodeType"/>, which is typed as
/// returning one; a descriptor of its own is what keeps that distinction in the type system instead
/// of in a cast somebody has to remember.
/// </para>
/// <para>
/// Creation goes through a real constructor call for the same reason <see cref="NodeType"/> does:
/// the trimmer sees <c>new TextSpan()</c>, and an element type without an accessible parameterless
/// constructor is a build error rather than a run-time one.
/// </para>
/// </remarks>
public sealed class ElementType
{
    private readonly Func<object> _create;

    /// <summary>Describes an element type a node can make.</summary>
    /// <param name="type">The element type this descriptor produces.</param>
    /// <param name="create">Creates an instance of that type.</param>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> or <paramref name="create"/> is null.</exception>
    public ElementType(Type type, Func<object> create)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(create);

        Type = type;
        _create = create;
    }

    /// <summary>The element type this descriptor produces.</summary>
    public Type Type { get; }

    /// <summary>Creates a new instance of <see cref="Type"/>, configured by nobody.</summary>
    /// <returns>The element; a node applies its properties to it afterwards.</returns>
    public object Create() => _create();
}
