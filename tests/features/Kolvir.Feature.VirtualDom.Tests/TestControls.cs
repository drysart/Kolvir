using Kolvir;

namespace Kolvir.VirtualDom.Tests;

/// <summary>
/// The manifest the generator reads: one attribute per control this suite builds trees out of.
/// </summary>
/// <remarks>
/// Registration is a declaration rather than a call, which is what lets the generator see the exact
/// types and emit direct assignments. <see cref="NoteControl"/> takes a <c>Name</c> because its own
/// name would make the node class <c>VNoteControl</c>, and the tests read better without the suffix.
/// </remarks>
[VirtualDom(typeof(Label))]
[VirtualDom(typeof(Button))]
[VirtualDom(typeof(Panel))]
[VirtualDom(typeof(NoteControl), Name = "Note")]
internal partial class TestControls;
