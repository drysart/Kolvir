# Node slots for control-typed properties

A generated node could describe every settable property of its control, and none of the control's
other contents. That leaves out the one thing a `SplitContainer` is for:

```csharp
// SplitContainer.cs
public Panel Panel1 { get; }
public Panel Panel2 { get; }
```

Both panels are created by the `SplitContainer`'s own constructor, and neither can be assigned, so no
node could say what is *in* them - which is most of the reason to use the control at all. This
document is the mechanism that lets a node express the contents of a property like that, and the one
decision in it that is not obvious: how a slot accepts a node for a control that merely derives from
the property's type.

## 1. Which properties this is about, and which it is not

A **slot** is a property whose type derives from `Control`, which a control hands out or accepts, and
which a node fills with a node instead of a value. The candidates in this repository were read one at
a time rather than assumed:

| Member | What it actually is | Node property? |
| --- | --- | --- |
| `SplitContainer.Panel1`, `Panel2` | `Panel` instances the container's constructor makes and adds to its own `Controls` | **Yes.** The whole point of the split container. |
| `Control.Parent` | `Control?`, set by the parent's `Controls` collection | **No.** The reconciler maintains it; a node that named it would describe the tree, not the control. |
| `Panel.HorizontalScrollBar`, `VerticalScrollBar` (and the same on `VirtualList`, `TextBoxMultiline`) | `ScrollBar?`, made and dropped by the scrolling feature | **No.** A slot has to have a control to fill at mount time; one that may be null is the control's own, created when the control needs it. |
| `MenuView.RootMenu`, `DeepestMenu` | `MenuView`, the outermost or innermost view of the menu chain - this view itself when there is no parent | **No.** A reference to a control of the same *kind*, which already has a node describing it; applying a second node to it would describe nothing new and disturb what the first one set. |
| `ComboBox.DropDownList` | `ListBox`, the popup list the combo box made and says is reachable "for the things the combo box does not forward - item drawing, item height, scrollbars" | **Yes.** A subtree of its own, which a node configures. Its *items* come from `Items`, so a node should set its properties rather than its children. |
| `Panel.Control`, `WrapPanel.Control` | A field of a private nested struct (`DockedChild`, `FlowItem`) | No - not a property of the control at all. |
| `ListView.Column`, `ListBox.Canvas`, … | Properties of event-argument classes | No - they are what a handler is told, not what a control holds. |
| `ListView.Columns`, `TabControl` pages | Control-typed **collections** | Not in this change (section 8). |
| `ListViewColumn` | Not a `Control`; a plain class | No. |
| `SplitButton.Menu`, `Control.ContextMenu` | `ContextMenu`, which is not a `Control` | No. |
| `TextSpan.Owner` | `RichLabel?`, on a class that is not a `Control` | No - the declaration is not on the control's chain. |

That leaves three: `SplitContainer.Panel1`, `SplitContainer.Panel2` and `ComboBox.DropDownList`. The
generalisation is real - an application's own container with its own handed-out panel is the same
shape, and a settable control-typed property is the other half - but the borrowed kind has exactly
three users today, and the design is written so that finding nothing to do costs nothing.

**What makes a property a slot, rather than a name on a list.** A control-typed property is a slot
when all three of these hold:

1. **It is a control of another kind** - its declared type is not the declaring control's type or a
   base of it. A property of the same kind is a reference to a control like this one: `Parent` is
   `Control` on `Control`, `MenuView.RootMenu` is `MenuView` on `MenuView`, and both already have a
   node describing the control they return.
2. **It can always hold one** - a get-only property that may be null is the control's own, made and
   dropped as the control needs it, and there is nothing to fill at mount time.
3. **Its setter, if it has one, is public** - a setter the control keeps to itself is the control's
   own state, written by its own machinery.

The three clauses are structural, so they need no attribute, no name list, and hold for a control this
repository has never seen. `Parent` is excluded by the second and third together, the scroll bars by
the second alone, and the menu chain by the first - each for the reason a reader would give.

## 2. The mechanism

The reconciler already has the shape this needs: `Mounted` is the record that survives a render, a
node is replaced every render, and the diff is driven by the previous **node** because only the node
knows what it assigned. A slot is a second kind of state on that record.

```
Mounted (node → control)
├── Children      the mounted children of control.Controls    (exists today)
└── Slots         one mounted child per slot, by slot ordinal  (this change)
```

`Mounted.Slots` is a list parallel to the node's slot list, holding `null` for an empty slot, and
allocated only for a node that has slots. Each entry is an ordinary `Mounted`, so a slot's patch,
removal and nested subtree work exactly like a one-element `Children`:

| Case | What happens |
| --- | --- |
| **Borrowed** (get-only: `Panel1`) - first render | `node.ApplyTo(target.Panel1)`, then reconcile that node's `Children` into `target.Panel1.Controls`. The reconciler never creates the panel and never places it. |
| **Borrowed** - later render, same node class | `node.PatchFrom(previous, target.Panel1)`, then reconcile the panel's children. The panel instance survives, so the container keeps whatever it holds for it. |
| **Borrowed** - different node class | The old node's subtree comes off, the new node is applied to the *same* panel. A borrow is in place by construction: the container made the control. |
| **Creating** (get-plus-set) - first render | `nodeType.Create()`, `node.ApplyTo(control)`, then assign **the control** to the property and reconcile its children. |
| **Creating** - same node class | Patch the control the slot already made. **Nothing is assigned**, which is the create-slot half of "an unchanged value is not re-applied". |
| **Creating** - different node class | Create a new control, apply, assign it, and drop the old one with its subtree. |
| **Removal** (the node becomes `null`) | Section 3. |

The state list is parallel to the node's slot list *by ordinal*, which has one consequence worth
writing down: a slot that is empty on the render that first fills its neighbours still has to hold its
place. A list that appended only the filled slots would put slot 2's state where slot 0's belongs, and
the next render would pair slot 2's node with slot 0's control - one slot's node applied into another
slot's control, which is the failure this parallelism exists to prevent.

**The slot's control is not also a child.** For a borrowed slot this is the entire correctness
argument: `SplitContainer`'s constructor already added `Panel1` to `Controls`, so a mechanism that
mounted it as a child would add it twice - `ControlCollection` would either throw or move the panel
out of the position the container keeps it in. The reconciler therefore never calls `Place` on a slot
control; what it mounts is the *contents* of one. Nothing else in the reconciler's child handling
touches `owner.Controls` except `Place`, so the rule is one line to keep and one parentage assertion
in the tests.

A slot node is an ordinary control node: not a `VComponent` (a component has no `NodeType` to create
the property's control with, and a borrowed slot would have to be a second mount point) and not a
`VFragment` (a fragment has no control of its own, and a slot holds one). Both are refused where the
property is assigned, by the property's type - `IVNodeFor<Panel>?` is a type neither implements.

**One node instance fills two slots as two trees.** A node is a description read during apply and patch
and never written, so `Panel1 = shared; Panel2 = shared` mounts two independent subtrees from one
description - which is what `Children = [node, node]` already does for an ordinary container. What a
node instance cannot carry into two slots is a **component**: `VComponent` refuses a second mount by
name, exactly as it does twice in `Children`, so the failure names the component rather than one slot
being driven by another's subtree. The alternative - refusing a repeated node instance outright - would
be a stricter rule than the tree itself keeps, for no state that could actually be corrupted.

## 3. Removal

Two different answers, because the two kinds of slot own different things.

- **Borrowed.** The panel is the container's, so it stays: what comes off is everything the node put
  *inside* it, and the panel's own properties are handed back to what the container's constructor
  gave them. That second half is not optional bookkeeping. Leaving them would break the rule the rest
  of the reconciler keeps - a property a node does not assign means "the control's own value" - for
  the next node to fill that slot, which would inherit `Dock`, `Padding` and anything else the
  removed node happened to set.
- **Creating.** The control the slot made is dropped with its subtree. **The property keeps holding
  it, emptied**, rather than being handed back to whatever it held before the node arrived: an
  `IVNodeFor<T>` is a description that *makes* a control, so there is nothing the reconciler could
  assign that means "the value this property had before", and leaving the created control in place is
  the only answer that is valid for a non-nullable property either. A control that can tell null from
  an empty panel will see the difference; that is stated here rather than papered over.

The borrowed half needs to know what the removed node had assigned, and only the node's own class
knows that. So a node class whose control can appear in a slot carries **`UnassignedNode`** - a
static instance of itself with nothing assigned - and removal is
`UnassignedNode.PatchFrom(removed, panel)`: every property the removed node assigned goes back to its
pristine value, every extension handler it attached is detached, and a property it never mentioned is
not assigned at all. It reuses the patch path rather than adding a second restore path, which is why
it costs a static field and a two-line override instead of a line per property.

## 4. Covariance, and what holds the slot value

A property declared `Panel` must accept a node for **any** control that derives from `Panel`. In this
library that is not hypothetical: `GroupBox`, `StackPanel`, `TableLayoutPanel` and `TabPage` all
derive from `Panel`, so `VGroupBox`, `VStackPanel`, `VTableLayoutPanel` and `VTabPage` are all legal
contents for `SplitContainer.Panel1`.

The slot property is typed with a **covariant marker interface**:

```csharp
public interface IVNodeFor<out TControl> where TControl : Control
{
    VNode Node { get; }
}
```

`TControl` appears in no member position, so the variance annotation is legal, and one instantiation
covers the whole chain: a node declaring only `IVNodeFor<TableLayoutPanel>` converts implicitly to
`IVNodeFor<Panel>`, `IVNodeFor<Control>` and everything in between. **One marker per node class**, its
own control type, plus an explicit implementation returning `this`. The generated slot property is
then `IVNodeFor<Panel>?` - identity for `VPanel`, variance for `VTableLayoutPanel`, and a compile
error at the assignment site for a node whose control is not a `Panel`. There is no runtime check, no
`is` test and nothing to throw: the type system does the work, and it does it where the mistake is
made.

Four claims are load-bearing enough to have been compiled rather than assumed, in a throwaway project
with the same shape:

1. The variance annotation with a `Control` constraint and a member compiles. It does.
2. `IVNodeFor<TableLayoutPanel>` converts to `IVNodeFor<Panel>` and to `IVNodeFor<Control>` - by
   identity, and up the chain. It does, both at a variable and at the slot property.
3. Assigning a `VLabel` - control `Label`, not a `Panel` - to `IVNodeFor<Panel>?` fails to compile:
   `CS0029`, at the assignment, naming both types. It does.
4. The explicit cast back from the `VNode` the reconciler reads to `IVNodeFor<T>` that the creating
   path needs - `(IVNodeFor<Panel>)node` - is a valid runtime conversion for an object that
   implements `IVNodeFor<TableLayoutPanel>`, because CLR variance conversions are real casts. It is.

The reconciler reads `slot.Node` and never cares which instantiation it came from.

### 4.1 What a borrowed slot's instance actually is

The marker interface settles the *declared* type, which is not the whole of a borrowed slot's
requirement, and the difference decides what a `VSplitContainer` accepts:

`SplitContainer.Panel1` is declared `Panel` and holds a `Panel` its constructor made. The node in that
slot is applied **to that instance**, and `ApplyTo` casts the control to the node's own control type -
so a `VTableLayoutPanel` node, for all that it is an `IVNodeFor<Panel>`, would cast the panel to
`TableLayoutPanel` and throw. Covariance is sound exactly where the reconciler *creates* the control
(a creating slot hands a `TableLayoutPanel` to a `Panel`-typed property, which is what a property of
that type is for); it is not sound where the control already exists with a type of its own.

So a borrowed slot takes a node for the control it holds - `Panel1 = new VPanel { .. }` - and the node
for a derived control is refused at the mount, naming both:

> A Panel slot takes a node for that control, but this one makes a TableLayoutPanel. …

This is the one runtime check in the mechanism, and it is not the one the marker interface replaced:
the type system answers "is this node for a `Panel`", and only the runtime can say what a get-only
property actually handed back. The alternative was a second, invariant marker (`IVNodeForExactly<T>`)
so that `Panel1 = new VTableLayoutPanel` would not compile at all; it costs a second interface
instantiation and a second explicit member on **every** node class, and it would reject a container
that hands out a derived instance - the case where the check above is exactly right. It was not worth
the per-node cost.

One consequence for nesting follows from the same fact: a slot inside a slot goes through a
**creating** slot, because a borrowed slot can only be filled by a node for the panel itself.
`HostControl.Split = new VSplitContainer { Panel1 = .. }` is the shape the tests use;
`VSplitContainer.Panel1 = new VSplitContainer` is not expressible, and correctly so - the outer panel
is a `Panel`, not a `SplitContainer`.

### Why not mirror the node hierarchy

`VTableLayoutPanel : VPanel` would give the same covariance statically, and it was rejected on cost,
not on taste. Nodes are emitted one flat class per control, all deriving from `VNode`, and mirroring
the control hierarchy would change that:

- **A node for an ancestor would have to exist.** Registering or sweeping `TableLayoutPanel` would
  transitively generate `VPanel`, `VControlNode` and so on, whether or not the application registered
  them - so "a node the application never names is trimmed" would stop being true.
- **`sealed` would have to come off** every generated node class that has a subclass, which is a
  public-API change to code that shipped as `sealed`.
- **`ApplyTo`/`PatchFrom` would become overrides needing base chaining**, because the properties of
  the derived control include every inherited one: the emitter would have to split one control's
  property list per declaring type in its chain and emit a class per level, instead of one flat
  class per control.
- **The duplicate-name placeholders could not join the hierarchy.** A placeholder is deliberately
  not a `VNode` - "a placeholder that were one could be put in a tree and do nothing" - so a
  conflicted ancestor would leave a hole in the middle of the chain.
- **The guarantee would still stop at the assembly boundary.** Nodes generated in a referenced
  library derive from that library's `VNode` subclasses, not this compilation's, so a `VPanel`-typed
  slot would still reject a perfectly good `TableLayoutPanel` node from another generator run.

The marker interface is two lines per node class, exists once per control type in metadata, and
composes across assemblies because the interface is the contract rather than the class.

## 5. What the generator emits, and what it costs

Per control whose node has a slot property (today: `VSplitContainer`):

- the slot property, tracked like any other (`Specified<IVNodeFor<Panel>?>`), carrying the control
  property's documentation;
- a `GetSlots` override returning the slot list, built from the tracked values and the owner control
  (the array is allocated per call, and only for a node that has slots).

Per node class, unconditionally: the `IVNodeFor<TControl>` implementation - the interface on the
class declaration and one explicit member. That is the owner's accepted cost, and section 7 measures
it rather than asserting it is free.

Per node class whose control can appear in a slot: the `UnassignedNode` static and its override. That
set is computed from the same property models - a node gets one when one of the slot types declared
in this output is on its control's chain - so a `VButton` never pays for it.

Nothing else changes. The slot properties come from the property model the sweep already walks, so
`[VirtualDomAll]` picks them up with no rule of its own; extension members, the documentation
carry-over and the styles work are untouched.

## 6. Tests

`Kolvir.Feature.VirtualDom.Tests` covers the mechanism, and `Kolvir.Feature.VirtualDom.Generators.Tests`
covers what is now a compile-time question:

| Test | What it holds |
| --- | --- |
| `VSplitContainer` with a node in each panel | Both panels get their contents on the first apply, `Panel1.Parent` is the split container, the container's `Controls` holds exactly the two panels it made, and each child's `Parent` is the panel it is in. |
| A slot's node describes its control | A property the slot node assigns (`Padding`) lands on the borrowed panel itself, and a property it does not assign keeps the container's own value. |
| Patch inside a slot | Changing a child's text inside a panel patches it - the child control is the same instance - rather than recreating the panel or the child. |
| Replace the slot's node | A different node class in a borrowed slot reconciles into the same panel; in a creating slot it creates and assigns a new control. |
| Removal | A slot that becomes null leaves the borrowed panel empty, and back at its constructor's property values rather than the removed node's; a creating slot's property keeps the control it made, emptied. |
| Ordering | Two slots with different node classes land in the right panels, including across a render where the first slot is empty and the second is not - the ordinal-alignment case. |
| Two removals, twice, in two containers | The shared all-unassigned instance carries nothing from one removal to the next: each panel takes its own node's value in both rounds. |
| Nested slots | A creating slot holding a `VSplitContainer` fills, patches and unwinds both levels. |
| One instance in two slots | Two independent subtrees are mounted and patched apart; a shared node carrying a component fails by the component's own message. |
| Covariant slot value | A `VTableLayoutPanel` in a creating `Panel` slot creates a `TableLayoutPanel` in the property. |
| The wrong node for a borrowed slot | Refused at the mount, naming the node's control and the panel's. |
| Wrong node type, statically | A generator test compiles a `VLabel` into a `Panel` slot and asserts the compile fails with `CS0029` naming both types. |
| What is not a slot | Generator tests assert that `Control.Parent` and `Panel`'s scroll bars have no node property, and that only controls a slot can hold carry the all-unassigned instance. |
| The demo's `--check` | The document page's `VSplitContainer` is asserted by parentage and contents - two panels, in the container, each holding its own node's label - and re-asserted after a later render, which is the slot patch path in the real application. |

## 7. Rung, and the measurements

The feature sits inside `Kolvir.Feature.VirtualDom`, which is already rung 3 by packaging. Inside
that package it is **rung 1**: no seam on `Control`, no registration, no work at startup, and every
byte of it is reached only from a node class the application names. `Mounted.Slots` is allocated
only for a node that has slots, `GetSlots` returns null for every other node, and the reconciler's
slot pass is two null checks on a path it already walks.

Measured per section 8 of `FEATURE-COST-POLICY.md` - `win-x64`, `-c Release -r win-x64`, published
from build roots of equal length (`C:\kolvircost\a` for this branch, `..\b` for `develop` at
`7072be1`), so the numbers are the change rather than where it was built. The build roots hold
worktrees of the two commits; the probe projects are `C:\kolvircost\a-probe` and `b-probe`, identical
source, referencing their own worktree.

| Publication | `develop` | this branch | delta |
| --- | --- | --- | --- |
| `Kolvir.Minimal`, Skia loose, AOT (an application that never references the feature) | 3,885,056 | 3,885,056 | **0** |
| probe: 12 controls registered, `VPanel` and `VLabel` constructed, applied and patched, **no slot filled** | 1,856,512 | 1,857,024 | **+512** |
| use probe: the same feature with a `VSplitContainer`'s panels filled, patched and emptied | - | 1,890,304 | (use check, no pair) |

Both pairs were published twice from clean output and came out to the byte the second time, so the
512 is the image and not the `FileAlignment` boundary moving. It is one step of this method's
resolution: the marker interface - an implementation and its instantiation on each node class the
application actually constructs - costs **less than 512 bytes** in the published image for two
constructed node classes and the whole slot reconciler. The managed `Probe.dll` shows the change is
really there (+3,584 bytes over 202,752), which is why the flat native size is reported as a
sub-resolution cost rather than as nothing. No IL2xxx or IL3xxx warning in either publish, and the
use probe prints the correct panel contents, patch and restore.

The generator's output grows by what it emits, and nothing else:

| | |
| --- | --- |
| Generated source, the demo's 36-node sweep | 3,116,355 -> 3,128,991 bytes, **+12,636 (0.41%)** |
| of which: the marker on 36 node class declarations | ~1,850 bytes (~52 per class) |
| of which: 36 explicit `IVNodeFor<T>.Node => this` members | 4,869 bytes (~135 per class) |
| of which: the all-unassigned instance on the 9 controls a slot can hold | 1,567 bytes (static + override) |
| of which: 3 slot properties, their carried documentation and 2 `GetSlots` methods | ~4,200 bytes |

The per-node half is the cost the owner accepted for the marker interface; the per-slot half is
proportional to the number of slots, and the all-unassigned instance is emitted only for the nine
controls a slot can hold - a `VLabel` pays nothing for a mechanism it can never take part in.

## 8. Out of scope

- **Control-typed collections** - a tab control's pages, a toolbar's items. A collection of nodes is a
  keyed child list whose controls the parent owns and arranges, which is a different mechanism from a
  single slot, and it is not this one.
- **Components in slots.** `Panel1 = new VComponent(page)` would need a second mount point per slot;
  the supported spelling is one node down, `Panel1 = new VPanel { Children = [new VComponent(page)] }`.
- **A slot on a node from a different generator run.** The slot itself works - the reconciler is
  runtime code - but the `UnassignedNode` that hands a borrowed control back is emitted by the
  compilation that can see the slot's type, so a node class from another compilation has none and a
  removed slot's panel keeps the values the removed node set. Contents still come off. Fixing it
  would mean a `Reset` member on every node class, which is the per-node cost this design avoids.
- **Extension properties as slots.** An extension property that returns a `Control` is not a slot;
  the extension catalogue's members are read for values and handler pairs, not for contents.
