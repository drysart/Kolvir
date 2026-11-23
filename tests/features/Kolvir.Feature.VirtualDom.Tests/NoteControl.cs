using Kolvir;

namespace Kolvir.Feature.VirtualDom.Tests;

/// <summary>
/// A control with no chrome whose only own property is a nullable string, used to test the difference
/// between "not set", "set to a value" and "set to null".
/// </summary>
/// <remarks>
/// Its initial value is not the type's default on purpose: the reconciler restores a property that a
/// render stops setting to what the control's constructor left, not to <c>default(T)</c>, and a test
/// that could not tell those apart would not be testing anything.
/// </remarks>
internal sealed class NoteControl : Control
{
    /// <summary>What a freshly constructed control's <see cref="Note"/> reads as.</summary>
    internal const string Pristine = "pristine";

    /// <summary>A nullable property, so that null is a value rather than an absence.</summary>
    public string? Note { get; set; } = Pristine;
}
