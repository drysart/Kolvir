namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// Has the virtual DOM generator emit a node class for every control the compilation can see.
/// </summary>
/// <remarks>
/// <para>
/// Where <see cref="VirtualDomAttribute"/> names one control per line, this sweeps them all:
/// </para>
/// <code>
/// [VirtualDomAll]
/// partial class AppControls;
/// </code>
/// <para>
/// The generator walks the source and the referenced assemblies that can see Kolvir's controls and
/// emits a node for every one that is public, concrete, non-generic and constructible without
/// arguments. Anything else is skipped without a word: a node factory needs <c>new T()</c>, so a
/// control that cannot answer that has no remedy to offer and a diagnostic about it would be noise
/// nobody can act on.
/// </para>
/// <para>
/// A control named by an explicit <see cref="VirtualDomAttribute"/> anywhere in the compilation is
/// left to that attribute, including the duplicate-registration error two attributes would give it.
/// That is what lets an application add this to a manifest it already has: the controls it chose
/// keep the names it chose, and the rest arrive without being listed.
/// </para>
/// <para>
/// The node classes are shaped exactly as a registered one, so the trimming argument is unchanged -
/// nothing reflects, no list of node types survives into the build, and a node type an application
/// never constructs is removed from the published image.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class VirtualDomAllAttribute : Attribute;
