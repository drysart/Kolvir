namespace Kolvir.VirtualDom;

/// <summary>
/// Turns a node tree into controls, and patches the controls it already made.
/// </summary>
/// <remarks>
/// <para>
/// The whole reconciler is driven by comparing the <em>previous node</em> with the next one: old
/// values come from the tree it already holds, so nothing reads a control's current property state
/// back out. That keeps <see cref="Control"/> free of any virtual-DOM awareness, and it is what makes
/// "this property is no longer mentioned, so restore it" answerable at all - only the node knows which
/// properties it assigned.
/// </para>
/// <para>
/// A container's children are updated inside one <see cref="Control.SuspendLayout"/> /
/// <see cref="Control.ResumeLayout()"/> pair. Without it, inserting ten rows would lay the container
/// out ten times; with it the whole update costs one pass, which matters because a reorder issues one
/// <see cref="ControlCollection.Move"/> per moved child and each of those asks for a layout.
/// </para>
/// </remarks>
internal static class Reconciler
{
    /// <summary>Creates a control for a node that has no previous control to reuse.</summary>
    /// <param name="node">The node to make a control for.</param>
    /// <returns>The mounted record, with no children reconciled yet.</returns>
    internal static Mounted Create(VNode node)
    {
        var control = node.NodeType.Create();
        node.ApplyTo(control);
        return new Mounted(node, control);
    }

    /// <summary>Detaches a mounted control from wherever it sits, and drops its subtree.</summary>
    /// <param name="mounted">The record to unmount.</param>
    internal static void Unmount(Mounted mounted)
    {
        mounted.Control.Parent?.Controls.Remove(mounted.Control);
        mounted.Children.Clear();
    }

    /// <summary>
    /// Reconciles the children of <paramref name="owner"/> from <paramref name="previous"/> to
    /// <paramref name="next"/>, then re-enters each child.
    /// </summary>
    /// <param name="owner">The control whose <see cref="Control.Controls"/> this maintains.</param>
    /// <param name="previous">The mounted children as they stand, replaced by the new list.</param>
    /// <param name="next">The children the new node asks for.</param>
    internal static void ReconcileChildren(Control owner, List<Mounted> previous, IReadOnlyList<VNode> next)
    {
        if (previous.Count == 0 && next.Count == 0)
        {
            return;
        }

        owner.SuspendLayout();

        try
        {
            var (matches, used) = Match(previous, next);

            for (int i = 0; i < previous.Count; i++)
            {
                if (!used[i])
                {
                    Unmount(previous[i]);
                }
            }

            var updated = new List<Mounted>(next.Count);

            for (int i = 0; i < next.Count; i++)
            {
                var node = next[i];
                var match = matches[i];
                Mounted mounted;

                if (match is not null && ReferenceEquals(match.Node.NodeType, node.NodeType))
                {
                    node.PatchFrom(match.Node, match.Control);
                    match.Node = node;
                    mounted = match;
                }
                else
                {
                    if (match is not null)
                    {
                        // Same slot, different node class: the control cannot be patched, so it is
                        // replaced. The old subtree goes with it.
                        Unmount(match);
                    }

                    mounted = Create(node);
                }

                Place(owner, mounted.Control, i);
                updated.Add(mounted);
            }

            previous.Clear();
            previous.AddRange(updated);

            for (int i = 0; i < next.Count; i++)
            {
                ReconcileChildren(updated[i].Control, updated[i].Children, next[i].Children);
            }
        }
        finally
        {
            owner.ResumeLayout();
        }
    }

    /// <summary>Puts <paramref name="child"/> at <paramref name="index"/> in <paramref name="owner"/>.</summary>
    private static void Place(Control owner, Control child, int index)
    {
        var current = owner.Controls.IndexOf(child);

        if (current < 0)
        {
            owner.Controls.Insert(index, child);
        }
        else if (current != index)
        {
            owner.Controls.Move(current, index);
        }
    }

    /// <summary>
    /// Pairs each new child with the previous one it should reuse, keyed first and then by position.
    /// </summary>
    /// <remarks>
    /// Keyed children are matched by key, which is what makes a reorder move controls instead of
    /// rebuilding them. Unkeyed children are then paired with unkeyed children in order, which handles
    /// the ordinary append and replace cases without any bookkeeping.
    /// </remarks>
    /// <remarks>
    /// The two never mix: a keyed node with no matching key is a new element, and a keyed child the new
    /// list does not ask for is removed. Letting a keyed child be claimed positionally would mean
    /// swapping one key for another silently renames a control, which is exactly the identity a key is
    /// there to control.
    /// </remarks>
    private static (Mounted?[] Matches, bool[] Used) Match(
        IReadOnlyList<Mounted> previous,
        IReadOnlyList<VNode> next)
    {
        var matches = new Mounted?[next.Count];
        var used = new bool[previous.Count];

        Dictionary<object, int>? byKey = null;

        for (int i = 0; i < previous.Count; i++)
        {
            if (previous[i].Node.Key is { } key)
            {
                // A duplicate key is a caller mistake; the first occurrence wins rather than throwing,
                // because the alternative is a render that fails months after the typo.
                (byKey ??= new Dictionary<object, int>()).TryAdd(key, i);
            }
        }

        if (byKey is not null)
        {
            for (int i = 0; i < next.Count; i++)
            {
                if (next[i].Key is { } key && byKey.TryGetValue(key, out int index) && !used[index])
                {
                    matches[i] = previous[index];
                    used[index] = true;
                }
            }
        }

        int cursor = 0;

        for (int i = 0; i < next.Count; i++)
        {
            if (matches[i] is not null || next[i].Key is not null)
            {
                continue;
            }

            while (cursor < previous.Count && (used[cursor] || previous[cursor].Node.Key is not null))
            {
                cursor++;
            }

            if (cursor < previous.Count)
            {
                matches[i] = previous[cursor];
                used[cursor] = true;
            }
        }

        return (matches, used);
    }
}
