using Kolvir.Feature.VirtualDom;

namespace Kolvir.Demo.VirtualDom;

/// <summary>
/// The sample's manifest: the controls its window renders describe.
/// </summary>
/// <remarks>
/// The application-level sample still uses ordinary control nodes for each window's body. The
/// generator emits one node class per control here, so a document window writes
/// <c>new VLabel { Text = ... }</c> and nothing at run time has to find or reflect over anything.
/// </remarks>
[VirtualDom(typeof(Panel))]
[VirtualDom(typeof(Label))]
[VirtualDom(typeof(Button))]
internal partial class DemoControls;
