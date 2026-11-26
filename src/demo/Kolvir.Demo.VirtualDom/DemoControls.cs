using Kolvir.Feature.VirtualDom;

namespace Kolvir.Demo.VirtualDom;

/// <summary>
/// The sample's manifest: every control its windows can describe, swept rather than listed.
/// </summary>
/// <remarks>
/// <para>
/// <c>[VirtualDomAll]</c> has the generator emit a node for every control it can build one for - every
/// public, concrete, non-generic <see cref="Control"/> with a parameterless constructor - so a document
/// window writes <c>new VLabel { Text = ... }</c> and nothing at run time has to find or reflect over
/// anything. The sample says it this way because it is the way an application that wants the whole
/// control set should say it: naming each control is a list to keep up to date, and a control missing
/// from the list has no node class at all.
/// </para>
/// <para>
/// The nodes the sweep generates are still per control and per property: a control type no window
/// constructs is not in the published image, which is what keeps a sweep from being a size argument
/// against itself.
/// </para>
/// </remarks>
[VirtualDomAll]
internal partial class DemoControls;
