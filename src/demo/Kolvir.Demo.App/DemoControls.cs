using Kolvir.Feature.VirtualDom;

namespace Kolvir.Demo;

/// <summary>
/// The sample's manifest: the controls its virtual-DOM page describes in a render.
/// </summary>
/// <remarks>
/// This is the whole setup a virtual DOM needs. The attributes are read by the generator at compile
/// time, which emits one node class per control into this namespace - so a page writes
/// <c>new VLabel { Text = "..." }</c> and nothing at run time has to find, register or reflect over
/// anything. A control that is not listed here simply has no node class, and using one is a compile
/// error rather than a run-time surprise.
/// </remarks>
[VirtualDom(typeof(Label))]
[VirtualDom(typeof(Button))]
[VirtualDom(typeof(Panel))]
[VirtualDom(typeof(TextBox))]

// The same attribute covers the label's element collection: a RichLabel node exposes Spans as a
// sequence of generated VTextSpan descriptions, because TextSpan is marked as a virtualizable element
// in core. The markup page is the one that uses it.
[VirtualDom(typeof(RichLabel))]
internal partial class DemoControls;
