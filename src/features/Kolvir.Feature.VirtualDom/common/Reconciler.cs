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
/// <para>
/// A node's property slots are reconciled by the same rules one control at a time
/// (<see cref="ReconcileSlots"/>). A slot's control is deliberately outside the child machinery: a
/// borrowed slot's control was made and placed by the control that owns the property, so the reconciler
/// fills it and never places it, and the panel a split container shows is not parented twice.
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

    /// <summary>Detaches a mounted control from wherever it sits, disposes it, and drops its subtree.</summary>
    /// <param name="mounted">The record to unmount.</param>
    /// <remarks>
    /// <para>
    /// The walk is recursive, because a component can be nested below ordinary nodes: a render that
    /// puts a <see cref="VComponent"/> inside a panel leaves the panel's record holding the child's
    /// mount, and dropping that record without disposing the mount would leave the child rendering into
    /// a control that is no longer in any tree - and refusing to be mounted again, because it is still
    /// mounted as far as it knows.
    /// </para>
    /// <para>
    /// Every call is a removal the tree intends to keep. A reorder never reaches here, because
    /// <see cref="Place(Control, Mounted, int)"/> moves a control inside one owner's list, and a child
    /// that appears under a different parent is unmounted and created fresh rather than re-parented.
    /// The control is therefore disposed, which is what gives work started for it - an in-flight asset
    /// load, for one - something to cancel when its node goes away.
    /// </para>
    /// </remarks>
    internal static void Unmount(Mounted mounted)
    {
        // A slot's control may not be the tree's to detach - a borrowed slot's belongs to the control
        // that made it - so each slot is released its own way before the ordinary teardown runs.
        DetachSlots(mounted);
        DetachCollections(mounted);

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
            // Detach first and dispose second: RemovedFromTree and then Disposing is the order Control's
            // own documentation promises a child, and the parent pointer is what makes the first one
            // fire. Every control that reaches here is being removed for good - a borrowed slot's
            // control never does, because DetachSlot only calls Unmount for a slot that is not
            // borrowed - so the disposal is unconditional rather than predicated on a case.
            control.Parent?.Controls.Remove(control);
            control.Dispose();
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
                ReconcileSlots(control, updated[i], next[i]);
                ReconcileCollections(control, updated[i], next[i]);
            }
        }
        finally
        {
            owner.ResumeLayout();
        }
    }

    /// <summary>
    /// Reconciles the property slots of <paramref name="node"/> into the record that node owns, which
    /// is the same patch-and-remove rule the children follow applied to one control per slot.
    /// </summary>
    /// <param name="owner">The control <paramref name="node"/> describes, which a borrowed slot's control comes from.</param>
    /// <param name="mounted">The record whose slot state is updated.</param>
    /// <param name="node">The node being applied or patched.</param>
    /// <remarks>
    /// <para>
    /// Each slot is paired with the previous render's slot at the same ordinal. That is sound because
    /// both lists come from the same generated node class - a node whose class changed is a new control,
    /// not a patch - so a mispairing cannot be produced by a render.
    /// </para>
    /// <para>
    /// A slot holds one control, so "patch" is a node type match and "replace" is anything else: a
    /// borrowed slot is only ever replaced in place, on the control the property already holds, while a
    /// creating slot gets a new control and assigns it.
    /// </para>
    /// </remarks>
    internal static void ReconcileSlots(Control owner, Mounted mounted, VNode node)
    {
        var slots = node.GetSlots(owner);

        if (slots is null || slots.Length == 0)
        {
            DetachSlots(mounted);
            return;
        }

        var state = mounted.Slots ??= [];

        for (int i = 0; i < slots.Length; i++)
        {
            var next = slots[i].Node;
            var previous = i < state.Count ? state[i] : null;

            if (next is null)
            {
                if (previous is not null)
                {
                    DetachSlot(previous);
                    state[i] = null;
                }

                continue;
            }

            if (previous is not null && CanReuse(previous.Node, next))
            {
                next.PatchFrom(previous.Node, previous.Control!);
                previous.Node = next;
                ReconcileChildren(previous.Control!, previous.Children, next.Children);
                ReconcileSlots(previous.Control!, previous, next);
                ReconcileCollections(previous.Control!, previous, next);
                continue;
            }

            if (previous is not null)
            {
                DetachSlot(previous);
                state[i] = null;
            }

            // Padded rather than appended: a slot that was empty on an earlier render left no entry, so
            // the list has to reach this ordinal before it can hold this slot's state. Appending instead
            // would put slot 2's state where slot 0's belongs.
            while (state.Count <= i)
            {
                state.Add(null);
            }

            state[i] = MountSlot(owner, slots[i]);
        }

        for (int i = slots.Length; i < state.Count; i++)
        {
            if (state[i] is { } extra)
            {
                DetachSlot(extra);
            }
        }

        if (state.Count > slots.Length)
        {
            state.RemoveRange(slots.Length, state.Count - slots.Length);
        }
    }

    /// <summary>Mounts one slot's node onto its control: the property's own, or one made for it.</summary>
    /// <param name="owner">The control the node describes.</param>
    /// <param name="slot">The slot, as the node described it.</param>
    /// <returns>The record for the slot.</returns>
    /// <remarks>
    /// A borrowed slot is applied to the control the property already holds, which is never created and
    /// never placed: for <c>SplitContainer</c> the panel is already a child of the container, so placing
    /// it again would either throw or move it out of the position the container keeps it in. A creating
    /// slot's control is made here and assigned after the node has been applied to it, so a setter that
    /// reads the property back sees a configured control.
    /// </remarks>
    private static Mounted MountSlot(Control owner, in VNodeSlot slot)
    {
        var node = slot.Node!;
        Mounted mounted;

        if (slot.Control is { } target)
        {
            node.ApplyTo(target);
            mounted = new Mounted(node, target) { Borrowed = true };
        }
        else
        {
            var control = node.NodeType.Create();
            node.ApplyTo(control);
            slot.Assign!(owner, control);
            mounted = new Mounted(node, control);
        }

        ReconcileChildren(mounted.Control!, mounted.Children, node.Children);
        ReconcileSlots(mounted.Control!, mounted, node);
        ReconcileCollections(mounted.Control!, mounted, node);

        return mounted;
    }

    /// <summary>Releases every slot a record holds, emptying the list so a later render starts fresh.</summary>
    /// <param name="mounted">The record whose slots are released.</param>
    private static void DetachSlots(Mounted mounted)
    {
        if (mounted.Slots is not { } slots)
        {
            return;
        }

        foreach (var slot in slots)
        {
            if (slot is not null)
            {
                DetachSlot(slot);
            }
        }

        slots.Clear();
    }

    /// <summary>Releases one slot: its subtree comes off, and its control is dealt with by its kind.</summary>
    /// <param name="slot">The slot's record.</param>
    /// <remarks>
    /// <para>
    /// A borrowed slot's control stays where its owner put it, and goes back to the property values the
    /// owner's constructor gave it. That restore is not bookkeeping: leaving them would break the rule
    /// the rest of the reconciler keeps - a property a node does not assign means "the control's own
    /// value" - for the next node to fill the slot, which would inherit whatever the removed node set.
    /// The all-unassigned instance is what supplies the "node that says nothing" side of the patch.
    /// </para>
    /// <para>
    /// A creating slot's control goes with its subtree. The property keeps holding it, emptied: an
    /// <see cref="IVNodeFor{TControl}"/> is a description that <em>makes</em> a control, so there is
    /// nothing the reconciler could assign that means "the value this property had before the node
    /// arrived", and leaving the control it made is the only answer valid for a non-nullable property.
    /// </para>
    /// </remarks>
    private static void DetachSlot(Mounted slot)
    {
        if (!slot.Borrowed)
        {
            Unmount(slot);
            return;
        }

        DetachSlots(slot);
        DetachCollections(slot);

        foreach (var child in slot.Children)
        {
            Unmount(child);
        }

        slot.Children.Clear();

        if (slot.Control is { } control && slot.Node.UnassignedNode is { } restored)
        {
            restored.PatchFrom(slot.Node, control);
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

    /// <summary>
    /// Reconciles the get-only element collections of <paramref name="node"/> into the record that
    /// node owns.
    /// </summary>
    /// <param name="owner">The control <paramref name="node"/> describes.</param>
    /// <param name="mounted">The record whose collection state is updated.</param>
    /// <param name="node">The node being applied or patched.</param>
    /// <remarks>
    /// The same patch-and-remove rule the children and slots follow, applied to a collection whose
    /// elements are not controls: the live elements are matched by key and then by position, a match
    /// is patched in place, the surplus is appended and the shortfall is removed. Every structural
    /// change goes through the collection itself, so the control's own add and remove notifications
    /// still run - which is what parents an element's inline control and unparents it again.
    /// </remarks>
    internal static void ReconcileCollections(Control owner, Mounted mounted, VNode node)
    {
        var collections = node.GetCollections(owner);

        if (collections is null || collections.Length == 0)
        {
            DetachCollections(mounted);
            return;
        }

        var state = mounted.Collections ??= [];

        while (state.Count < collections.Length)
        {
            state.Add(new MountedCollection());
        }

        for (int i = collections.Length; i < state.Count; i++)
        {
            DetachCollection(state[i]);
        }

        if (state.Count > collections.Length)
        {
            state.RemoveRange(collections.Length, state.Count - collections.Length);
        }

        for (int i = 0; i < collections.Length; i++)
        {
            // A descriptor whose Nodes is null is a render that said nothing about the collection.
            // That is not "make it empty": the elements already there are left exactly as they are.
            if (collections[i].Nodes is null)
            {
                continue;
            }

            ReconcileCollection(collections[i], state[i]);
        }
    }

    /// <summary>Reconciles one collection against the elements that are in it.</summary>
    /// <param name="collection">The live collection and the descriptions this render gave it.</param>
    /// <param name="state">The elements the previous render tracked.</param>
    private static void ReconcileCollection(VNodeCollection collection, MountedCollection state)
    {
        // An imperative user can add or remove an element between renders. Put the tracked records
        // back in step with the live collection before matching anything: an element nobody described
        // is adopted with no previous description, and a described element that left is forgotten.
        var known = new Dictionary<object, MountedElement>(ReferenceEqualityComparer.Instance);

        foreach (var tracked in state.Elements)
        {
            known[tracked.Element] = tracked;
        }

        var synced = new List<MountedElement>(collection.Count);

        for (int i = 0; i < collection.Count; i++)
        {
            var element = collection.At(i);

            if (known.Remove(element, out var existing))
            {
                synced.Add(existing);
            }
            else
            {
                synced.Add(new MountedElement(element));
            }
        }

        foreach (var gone in known.Values)
        {
            DetachElementSlots(gone);
            gone.Node?.Detach(gone.Element);
        }

        state.Elements.Clear();
        state.Elements.AddRange(synced);

        var nodes = collection.Nodes!;
        var count = nodes.Count;
        var matches = new MountedElement?[count];
        var matchIndex = new int[count];
        var used = new bool[state.Elements.Count];
        var keys = new HashSet<object>();
        var previousByKey = new Dictionary<object, int>();

        for (int i = 0; i < state.Elements.Count; i++)
        {
            if (state.Elements[i].Node?.Key is { } key && !previousByKey.ContainsKey(key))
            {
                previousByKey.Add(key, i);
            }
        }

        // Keys win over positions: a keyed description finds the element that carried the same key
        // last render, wherever it now sits, and that element is moved to the description's position.
        for (int j = 0; j < count; j++)
        {
            matchIndex[j] = -1;

            if (nodes[j].Key is not { } key)
            {
                continue;
            }

            if (!keys.Add(key))
            {
                throw new InvalidOperationException(
                    $"Two elements in one render share the key '{key}'. A key identifies an element, so a duplicate is ambiguous; give the elements distinct keys or leave them unkeyed.");
            }

            if (previousByKey.TryGetValue(key, out var index) && !used[index])
            {
                matches[j] = state.Elements[index];
                matchIndex[j] = index;
                used[index] = true;
            }
        }

        // Everything else is matched by position: an unkeyed description takes the previous element
        // at its ordinal, provided no keyed description has already claimed that element.
        for (int j = 0; j < count; j++)
        {
            if (matches[j] is not null || nodes[j].Key is not null)
            {
                continue;
            }

            if (j < state.Elements.Count && !used[j])
            {
                matches[j] = state.Elements[j];
                matchIndex[j] = j;
                used[j] = true;
            }
        }

        var remove = new bool[state.Elements.Count];

        for (int i = 0; i < remove.Length; i++)
        {
            remove[i] = !used[i];
        }

        var targets = new MountedElement?[count];
        var relink = new bool[count];

        for (int j = 0; j < count; j++)
        {
            var match = matches[j];

            if (match is null)
            {
                continue;
            }

            if (match.Node is not null && !Reusable(match.Node, nodes[j]))
            {
                // A different description class cannot patch this element, so the element is
                // replaced. The old one goes through the same removal path as a shortfall.
                remove[matchIndex[j]] = true;
                continue;
            }

            targets[j] = match;
        }

        for (int i = state.Elements.Count - 1; i >= 0; i--)
        {
            if (!remove[i])
            {
                continue;
            }

            var old = state.Elements[i];
            DetachElementSlots(old);
            old.Node?.Detach(old.Element);

            var index = ReferenceIndexOf(collection, old.Element);

            if (index >= 0)
            {
                collection.RemoveAt(index);
            }
        }

        for (int j = 0; j < count; j++)
        {
            var target = targets[j];

            if (target is null)
            {
                continue;
            }

            var node = nodes[j];

            if (target.Node is null)
            {
                node.ApplyTo(target.Element);
            }
            else
            {
                node.PatchFrom(target.Node, target.Element);
            }

            target.Node = node;
            relink[j] = ReconcileElementSlots(target.Element, target, node);
        }

        for (int j = 0; j < count; j++)
        {
            if (targets[j] is not null)
            {
                continue;
            }

            var node = nodes[j];
            var element = collection.ElementType.Create();
            var target = new MountedElement(element);

            node.ApplyTo(element);
            target.Node = node;
            ReconcileElementSlots(element, target, node);
            targets[j] = target;
        }

        // Put the live collection into flow order. A reorder is a move through the collection's own
        // primitive where it has one, so an inline control is not unparented; an element that did not
        // move is left alone. A retained element whose inline control changed is relinked even when
        // its position did not change, because that is what parents the new control.
        for (int i = 0; i < count; i++)
        {
            var target = targets[i]!;
            var element = target.Element;
            var current = ReferenceIndexOf(collection, element);

            if (current == i && !relink[i])
            {
                continue;
            }

            if (!relink[i] && current >= 0)
            {
                // An order-only change is a move, exactly as it is for children: the element keeps
                // its identity and its control stays parented where it is.
                collection.Move(current, i);
                continue;
            }

            if (current >= 0)
            {
                collection.RemoveAt(current);
            }

            collection.Insert(i, element);
        }

        state.Elements.Clear();

        for (int i = 0; i < count; i++)
        {
            state.Elements.Add(targets[i]!);
        }
    }

    /// <summary>Whether a description can patch the element another description produced.</summary>
    private static bool Reusable(VElementDescription previous, VElementDescription next) =>
        ReferenceEquals(previous.ElementType, next.ElementType);

    /// <summary>Reconciles the control-typed members of one element.</summary>
    /// <returns>True when a member the collection parents was replaced or cleared.</returns>
    private static bool ReconcileElementSlots(object element, MountedElement mounted, VElementDescription node)
    {
        var slots = node.GetSlots(element);

        if (slots is null || slots.Length == 0)
        {
            DetachElementSlots(mounted);
            mounted.Slots = null;
            return false;
        }

        var state = mounted.Slots ??= [];
        var changed = false;

        for (int i = 0; i < slots.Length; i++)
        {
            while (state.Count <= i)
            {
                state.Add(null);
            }

            var slot = slots[i];
            var next = slot.Node;
            var previous = i < state.Count ? state[i] : null;

            if (next is null)
            {
                if (previous is not null)
                {
                    DetachElementSlot(previous);
                    state[i] = null;

                    if (!previous.Borrowed)
                    {
                        slot.Assign(element, null);
                        changed = true;
                    }
                }

                continue;
            }

            if (previous is not null && previous.Control is not null && CanReuse(previous.Node, next))
            {
                next.PatchFrom(previous.Node, previous.Control);
                previous.Node = next;
                ReconcileChildren(previous.Control, previous.Children, next.Children);
                ReconcileSlots(previous.Control, previous, next);
                ReconcileCollections(previous.Control, previous, next);
                continue;
            }

            if (next is VComponent or VFragment)
            {
                throw new InvalidOperationException(
                    "An element's control member takes a control node, not a component or a fragment: there is no control to create for it, so the element has nowhere to put it. Return a control from the component's render instead.");
            }

            if (previous is not null)
            {
                DetachElementSlot(previous);
                state[i] = null;
            }

            var current = slot.IsBorrowed ? slot.Control : slot.Current(element);

            // A creating slot's control went with the previous node, which disposed it: taking that
            // instance over again would patch a disposed control. A different control the application
            // put on the element in the meantime is still good to take over.
            if (!slot.IsBorrowed && previous is not null && ReferenceEquals(current, previous.Control))
            {
                current = null;
            }

            var relink = previous is not null;
            Mounted mountedSlot;

            if (current is not null && next.NodeType.ControlType.IsInstanceOfType(current))
            {
                next.ApplyTo(current);
                mountedSlot = new Mounted(next, current) { Borrowed = slot.IsBorrowed };
            }
            else
            {
                if (current is not null)
                {
                    current.Parent?.Controls.Remove(current);
                }

                var control = next.NodeType.Create();
                next.ApplyTo(control);

                if (!slot.IsBorrowed)
                {
                    slot.Assign(element, control);
                }

                mountedSlot = new Mounted(next, control);
                relink = true;
            }

            ReconcileChildren(mountedSlot.Control!, mountedSlot.Children, next.Children);
            ReconcileSlots(mountedSlot.Control!, mountedSlot, next);
            ReconcileCollections(mountedSlot.Control!, mountedSlot, next);
            state[i] = mountedSlot;

            if (!slot.IsBorrowed && relink)
            {
                changed = true;
            }
        }

        for (int i = slots.Length; i < state.Count; i++)
        {
            if (state[i] is { } extra)
            {
                DetachElementSlot(extra);
            }
        }

        if (state.Count > slots.Length)
        {
            state.RemoveRange(slots.Length, state.Count - slots.Length);
        }

        return changed;
    }

    /// <summary>Releases everything the elements of one collection attached.</summary>
    private static void DetachCollection(MountedCollection state)
    {
        foreach (var element in state.Elements)
        {
            DetachElementSlots(element);
            element.Node?.Detach(element.Element);
        }

        state.Elements.Clear();
    }

    /// <summary>Releases every collection a record tracks.</summary>
    private static void DetachCollections(Mounted mounted)
    {
        if (mounted.Collections is not { } collections)
        {
            return;
        }

        foreach (var collection in collections)
        {
            DetachCollection(collection);
        }

        collections.Clear();
    }

    /// <summary>Releases every control-typed member of one element.</summary>
    private static void DetachElementSlots(MountedElement element)
    {
        if (element.Slots is not { } slots)
        {
            return;
        }

        foreach (var slot in slots)
        {
            if (slot is not null)
            {
                DetachElementSlot(slot);
            }
        }

        slots.Clear();
    }

    /// <summary>Releases one control-typed member of an element.</summary>
    /// <remarks>
    /// A creating member's control is a control the node made, so it comes off with its subtree. A
    /// borrowed member's control belongs to the element's owner, so it is restored in place and never
    /// detached.
    /// </remarks>
    private static void DetachElementSlot(Mounted slot)
    {
        if (!slot.Borrowed)
        {
            Unmount(slot);
            return;
        }

        DetachSlots(slot);
        DetachCollections(slot);

        foreach (var child in slot.Children)
        {
            Unmount(child);
        }

        slot.Children.Clear();

        if (slot.Control is { } control && slot.Node.UnassignedNode is { } restored)
        {
            restored.PatchFrom(slot.Node, control);
        }
    }

    /// <summary>The position of <paramref name="element"/> in <paramref name="collection"/>, or -1.</summary>
    private static int ReferenceIndexOf(VNodeCollection collection, object element)
    {
        for (int i = 0; i < collection.Count; i++)
        {
            if (ReferenceEquals(collection.At(i), element))
            {
                return i;
            }
        }

        return -1;
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
    internal static bool CanReuse(VNode previous, VNode next)
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
