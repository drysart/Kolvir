namespace Kolvir;

/// <summary>
/// One family an application registered, with the faces it supplied.
/// </summary>
/// <remarks>
/// This is the answer to "why is my text this face", and the enumeration an application builds its
/// own family picker from: the platform's font dialog lists the operating system's families, not
/// these. Each face reports the family name recorded inside its file, which may differ from the name
/// it is registered under and is kept for exactly that kind of diagnosis.
/// </remarks>
public sealed class RegisteredFontFamily
{
    /// <summary>Describes a registered family.</summary>
    /// <param name="familyName">The name the family resolves under.</param>
    /// <param name="faces">The faces registered for it, in no particular order.</param>
    internal RegisteredFontFamily(string familyName, IReadOnlyList<FontFace> faces)
    {
        FamilyName = familyName;
        Faces = faces;
    }

    /// <summary>The name the family resolves under, which is the name to pass to a <see cref="Font"/>.</summary>
    public string FamilyName { get; }

    /// <summary>
    /// The faces registered for this family, each with the style it carries and the family name
    /// recorded in its own file.
    /// </summary>
    /// <remarks>
    /// A request for a weight or a slant the family does not have is served by the nearest face it
    /// does have, preferring a matching slant. A family that registered only a regular face therefore
    /// serves every request with that face - the same non-synthesis rule the platform's families
    /// follow, rather than a second rule of the registry's own.
    /// </remarks>
    public IReadOnlyList<FontFace> Faces { get; }
}
