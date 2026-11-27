namespace Kolvir.Feature.VirtualDom;

/// <summary>
/// One element in a control's get-only collection, the description that currently describes it, and
/// the mounted control slots its description fills.
/// </summary>
/// <remarks>
/// This is to <see cref="VElementDescription"/> what <see cref="Mounted"/> is to <see cref="VNode"/>:
/// the reconciler's record of what is currently in the live collection, so that the next render can
/// patch the same element instead of replacing it. It lives in the <see cref="Mounted"/> of the
/// control that owns the collection, which is where per-instance state belongs; the description is
/// rebuilt every render and holds none of it.
/// </remarks>
internal sealed class MountedElement
{
    internal MountedElement(object element) => Element = element;

    /// <summary>The live element in the control's collection.</summary>
    internal object Element { get; }

    /// <summary>
    /// The description that produced <see cref="Element"/> on the latest render, or null for an
    /// element the node adopted from the control itself.
    /// </summary>
    internal VElementDescription? Node { get; set; }

    /// <summary>The mounted state of each control-typed member of the element, by ordinal.</summary>
    internal List<Mounted?>? Slots { get; set; }
}

/// <summary>The mounted state of one get-only element collection.</summary>
internal sealed class MountedCollection
{
    /// <summary>The elements currently tracked, in the collection's flow order.</summary>
    internal List<MountedElement> Elements { get; } = [];
}
