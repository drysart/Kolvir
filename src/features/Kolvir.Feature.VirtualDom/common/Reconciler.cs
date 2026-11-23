namespace Kolvir.Feature.VirtualDom;

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
/// <para>
/// Fragments never get this far: <see cref="ReconcileChildren"/> inlines them into their parent before
/// matching anything, so every node below that point owns exactly one control - which is the invariant
/// the rest of the class reads, including <see cref="Mounted.ControlCount"/>, the one exception that
/// lets a component own a span of its parent's children.
/// </para>
/// </remarks>
internal static class Reconciler
{
    /// <summary>The deepest chain of inlined fragments a tree may use before it is refused.</summary>
    /// <remarks>
    /// Only a cycle can reach it - a fragment is a plain node, so nesting one inside another is ordinary
    /// and shallow - and the check exists so that a node that somehow contains itself fails by name
    /// instead of finishing the process off with a stack overflow.
    /// </remarks>
    private const int MaxFragmentDepth = 64;

    /// <summary>Creates a control for a node that has no previous control to reuse.</summary>
    /// <param name="owner">The control the new control is parented to.</param>
    /// <param name="node">The node to make a control for.</param>
    /// <returns>The mounted record, with no children reconciled yet.</returns>
    /// <remarks>
    /// A <see cref="VComponent"/> is mounted rather than created: the component renders its own root,
    /// and the mount keeps the child's subtree in step from then on. The root control is appended to
    /// <paramref name="owner"/> and then moved into place by the caller, which is one move per inserted
    /// child inside a single suspended layout.
    /// </remarks>
    internal static Mounted Create(Control owner, VNode node)
    {
        if (node is VComponent component)
        {
            var mount = new ComponentMount(owner, component.Component);

            return new Mounted(node, mount.Root) { ComponentMount = mount };
        }

        var control = node.NodeType.Create();
        node.ApplyTo(control);
        return new Mounted(node, control);
    }

    /// <summary>Detaches a mounted control from wherever it sits, and drops its subtree.</summary>
    /// <param name="mounted">The record to unmount.</param>
    /// <remarks>
    /// The walk is recursive, because a component can be nested below ordinary nodes: a render that
    /// puts a <see cref="VComponent"/> inside a panel leaves the panel's record holding the child's
    /// mount, and dropping that record without disposing the mount would leave the child rendering into
    /// a control that is no longer in any tree - and refusing to be mounted again, because it is still
    /// mounted as far as it knows.
    /// </remarks>
    internal static void Unmount(Mounted mounted)
    {
        if (mounted.ComponentMount is { } mount)
        {
            // Disposing the child's mount removes its controls and unwinds whatever it mounted in turn.
            mount.Dispose();
            return;
        }

        foreach (var child in mounted.Children)
        {
            Unmount(child);
        }

        mounted.Children.Clear();

        if (mounted.Control is { } control)
        {
            control.Parent?.Controls.Remove(control);
        }
    }

    /// <summary>
    /// Reconciles the children of <paramref name="owner"/> from <paramref name="previous"/> to
    /// <paramref name="next"/>, then re-enters each child.
    /// </summary>
    /// <param name="owner">The control whose <see cref="Control.Controls"/> this maintains.</param>
    /// <param name="previous">The mounted children as they stand, replaced by the new list.</param>
    /// <param name="next">The children the new node asks for.</param>
    /// <param name="startIndex">
    /// Where in <paramref name="owner"/> the first of these children belongs. Zero for a control's own
    /// children, and not zero for a mount that owns a slice of a parent's list - a window's root panel
    /// already holding something, or a component whose controls sit between two of its parent's.
    /// </param>
    /// <remarks>
    /// Fragments are inlined first, so everything below deals in nodes that each own exactly one control:
    /// a fragment is never matched, placed or patched, because it is no longer in the list.
    /// </remarks>
    internal static void ReconcileChildren(
        Control owner,
        List<Mounted> previous,
        IReadOnlyList<VNode> next,
        int startIndex = 0)
    {
        next = Inline(next);

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

            // The place the next child belongs at, carried from child to child: a component whose render
            // put several nodes at the top level owns that many controls, so the one after it is further
            // along than its sibling index would suggest.
            var index = startIndex;

            for (int i = 0; i < next.Count; i++)
            {
                var node = next[i];
                var match = matches[i];
                Mounted mounted;

                if (node is VComponent { Children.Count: > 0 })
                {
                    throw new InvalidOperationException(
                        "A VComponent takes its controls from the component's render, so it cannot be given Children as well - return them from the component's own Render instead.");
                }

                if (match is not null && CanReuse(match.Node, node))
                {
                    // A component node has no properties of its own: its subtree belongs to the child's
                    // mount, which this render deliberately leaves alone. The control test covers that
                    // same case - a component is the only record that can have no control.
                    if (node is not VComponent && match.Control is { } control)
                    {
                        node.PatchFrom(match.Node, control);
                    }

                    match.Node = node;
                    mounted = match;
                }
                else
                {
                    if (match is not null)
                    {
                        // Same slot, different node: the control cannot be patched, so it is replaced.
                        // The old subtree goes with it, which for a component means its mount is
                        // disposed and the child's state stops being rendered.
                        Unmount(match);
                    }

                    mounted = Create(owner, node);
                }

                Place(owner, mounted, index);
                index += mounted.ControlCount;
                updated.Add(mounted);
            }

            previous.Clear();
            previous.AddRange(updated);

            for (int i = 0; i < next.Count; i++)
            {
                if (next[i] is VComponent || updated[i].Control is not { } control)
                {
                    // A component's subtree belongs to the child's mount, which reconciles it itself.
                    continue;
                }

                ReconcileChildren(control, updated[i].Children, next[i].Children);
            }
        }
        finally
        {
            owner.ResumeLayout();
        }
    }

    /// <summary>Puts every control <paramref name="child"/> stands for at <paramref name="index"/>.</summary>
    /// <remarks>
    /// A component owns its controls rather than merely pointing at one - a fragment in its render can
    /// give it several - so it is asked to move its own span. That keeps the parent's arithmetic honest:
    /// it advances by <see cref="Mounted.ControlCount"/>, which is what the child actually placed.
    /// </remarks>
    internal static void Place(Control owner, Mounted child, int index)
    {
        if (child.ComponentMount is { } mount)
        {
            mount.PlaceIn(index);
            return;
        }

        Place(owner, child.Control!, index);
    }

    /// <summary>Puts <paramref name="child"/> at <paramref name="index"/> in <paramref name="owner"/>.</summary>
    internal static void Place(Control owner, Control child, int index)
    {
        var current = owner.Controls.IndexOf(child);

        if (current < 0)
        {
            owner.Controls.Insert(Math.Min(index, owner.Controls.Count), child);
        }
        else if (current != index)
        {
            owner.Controls.Move(current, Math.Min(index, owner.Controls.Count - 1));
        }
    }

    /// <summary>
    /// Replaces each <see cref="VFragment"/> in <paramref name="nodes"/> with its children, in place.
    /// </summary>
    /// <param name="nodes">The child nodes as the tree gives them.</param>
    /// <returns>
    /// The nodes that stand for controls, which is <paramref name="nodes"/> itself when it holds no
    /// fragment - the ordinary case, and the reason a tree without fragments allocates nothing extra.
    /// </returns>
    private static IReadOnlyList<VNode> Inline(IReadOnlyList<VNode> nodes)
    {
        List<VNode>? flat = null;

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];

            if (node is not VFragment fragment)
            {
                flat?.Add(node);
                continue;
            }

            if (flat is null)
            {
                flat = new List<VNode>(nodes.Count);

                for (int j = 0; j < i; j++)
                {
                    flat.Add(nodes[j]);
                }
            }

            Inline(fragment, flat, 1);
        }

        return flat ?? nodes;
    }

    /// <summary>Adds a fragment's children to <paramref name="flat"/>, inlining the fragments among them.</summary>
    /// <param name="fragment">The fragment being inlined.</param>
    /// <param name="flat">The flattened children of the fragment's parent, appended to.</param>
    /// <param name="depth">How many fragments deep the walk is, so that a cycle fails by name.</param>
    private static void Inline(VFragment fragment, List<VNode> flat, int depth)
    {
        if (fragment.Key is not null)
        {
            throw new InvalidOperationException(
                "A VFragment cannot have a Key. Its children take its place among their parent's children, so a fragment has no identity of its own to key - give the key to the child whose identity it is.");
        }

        if (depth > MaxFragmentDepth)
        {
            throw new InvalidOperationException(
                $"A VFragment is nested more than {MaxFragmentDepth} deep. Fragments are inlined into their parent, so this is a fragment that contains itself as a descendant: break the cycle, or return a container node where the nesting really is that deep.");
        }

        foreach (var child in fragment.Children)
        {
            if (child is VFragment nested)
            {
                Inline(nested, flat, depth + 1);
                continue;
            }

            flat.Add(child);
        }
    }

    /// <summary>
    /// Whether <paramref name="next"/> can take over the mounted record <paramref name="previous"/> left,
    /// instead of replacing it.
    /// </summary>
    /// <remarks>
    /// Two nodes of the same control class are patched, which is the ordinary case. A component node is
    /// matched by the component <em>instance</em> instead: the same child keeps its mount - and so its
    /// state and the controls showing it - while a different child in the same slot is a different
    /// component and inherits nothing from the one it replaces. The two kinds never match each other.
    /// </remarks>
    private static bool CanReuse(VNode previous, VNode next)
    {
        if (previous is VComponent mounted)
        {
            return next is VComponent node && ReferenceEquals(mounted.Component, node.Component);
        }

        return next is not VComponent && ReferenceEquals(previous.NodeType, next.NodeType);
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
