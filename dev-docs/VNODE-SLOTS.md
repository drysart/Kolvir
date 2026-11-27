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
document is the mechanism that lets a node express the contents of a property like that, and the two
decisions in it that are not obvious: how a slot names the node it accepts, and what happens to the
control when the node goes away.

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
| **Borrowed** - different node class | Cannot happen: the property is typed with the one node class for the control the container made, so there is no other node class to give it. |
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
property is assigned, by the property's type: neither a `VComponent` nor a `VFragment` is a
`IVNodeFor<Panel>` for a creating slot, nor a `VPanel` for a borrowed one.

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
  it, emptied**, rather than being handed back to whatever it held before the node arrived: a node is a
  description that *makes* a control, so there is nothing the reconciler could assign that means "the
  value this property had before", and leaving the created control in place is the only answer that is
  valid for a non-nullable property either. A control that can tell null from an empty panel will see
  the difference; that is stated here rather than papered over.

The borrowed half needs to know what the removed node had assigned, and only the node's own class
knows that. So a node class whose control a borrowed slot holds carries **`UnassignedNode`** - a
static instance of itself with nothing assigned - and removal is
`UnassignedNode.PatchFrom(removed, panel)`: every property the removed node assigned goes back to its
pristine value, every extension handler it attached is detached, and a property it never mentioned is
not assigned at all. It reuses the patch path rather than adding a second restore path, which is why
it costs a static field and a two-line override instead of a line per property.

## 4. Covariance, and what holds the slot value

There are two kinds of slot and they hold their values differently, because the two questions are
different: whether a node may describe a control the node *makes*, and whether it may describe a
control the *parent* made.

### 4.1 A creating slot: the covariant marker

A settable property declared `Panel` must accept a node for **any** control that derives from `Panel`.
In this library that is not hypothetical: `GroupBox`, `StackPanel`, `TableLayoutPanel` and `TabPage`
all derive from `Panel`, so `VGroupBox`, `VStackPanel`, `VTableLayoutPanel` and `VTabPage` are all
legal contents for a settable `Panel` property. The reconciler makes the control the node describes,
so a `VTableLayoutPanel` in a `Panel` slot makes a `TableLayoutPanel` in it - exactly what a property
of that type is for.

The creating slot property is typed with a **covariant marker interface**:

```csharp
public interface IVNodeFor<out TControl> where TControl : Control
{
    VNode Node { get; }
}
```

`TControl` appears in no member position, so the variance annotation is legal, and one instantiation
covers the whole chain: a node declaring only `IVNodeFor<TableLayoutPanel>` converts implicitly to
`IVNodeFor<Panel>`, `IVNodeFor<Control>` and everything in between. **One marker per node class**, its
own control type, plus an explicit implementation returning `this`. The generated property is then
`IVNodeFor<Panel>?` - identity for `VPanel`, variance for `VTableLayoutPanel`, and a compile error at
the assignment site for a node whose control is not a `Panel`. There is no runtime check, no `is` test
and nothing to throw: the type system does the work, and it does it where the mistake is made. The
reconciler reads `slot.Node` and never cares which instantiation it came from.

Three claims are load-bearing enough to have been compiled rather than assumed, in a throwaway project
with the same shape:

1. The variance annotation with a `Control` constraint and a member compiles. It does.
2. `IVNodeFor<TableLayoutPanel>` converts to `IVNodeFor<Panel>` and to `IVNodeFor<Control>` - by
   identity, and up the chain. It does, both at a variable and at the slot property.
3. Assigning a `VLabel` - control `Label`, not a `Panel` - to `IVNodeFor<Panel>?` fails to compile:
   `CS0029`, at the assignment, naming both types. It does.

### 4.2 A borrowed slot: the node class itself

`SplitContainer.Panel1` is declared `Panel` and holds a `Panel` its constructor made. The node in that
slot is applied **to that instance**, and `ApplyTo` casts the control to the node's own control type.
Covariance is unsound here, and not in a way a null check could catch: a `VTableLayoutPanel` node is a
legal `IVNodeFor<Panel>`, but the instance it would be applied to is a plain `Panel`, so the cast
inside `ApplyTo` would fail at mount with no better message than the cast's.

So a borrowed slot is typed with **the node class for the control the property holds** - not a marker,
not the base node class:

```csharp
public VPanel? Panel1 { get; set; }
```

- `Panel1 = new VPanel { .. }` compiles by identity.
- `Panel1 = new VTableLayoutPanel()` is `CS0029` at the assignment, naming both node class names.
  There is no runtime check for it because there is nothing left for one to catch: the only value the
  property can hold is the node class for the type the container actually made.
- The reconciler reads a borrowed slot's value directly as a `VNode` - no interface, no `.Node` hop -
  so the borrowed path is the simpler of the two.
- Removal patches from the all-unassigned instance of **that same node class** (§3), which the
  property type already guarantees is the right one.

A borrowed slot is therefore narrower than the control hierarchy: a property declared `Panel` does not
accept a node for a `GroupBox`, even though `GroupBox` is a `Panel`. That is the deliberate
consequence of the choice - the panel is a `Panel`, so nothing else can describe it.

### Why not a second marker, and why not mirror the node hierarchy

Both alternatives were costed, and both were rejected.

**A second, invariant marker** (`IVNodeForExactly<T>`) would give the borrowed slot the same
compile-time rejection with one interface instead of a concrete type. It costs a second interface
instantiation and a second explicit member on **every** node class - the whole point of keeping the
marker count down - and it types the property with an interface, so the removal path has to cast the
property's value back to the node class it needs and the reconciler needs a cast to read it. The
concrete node class does both jobs with the one type the generator already had to emit, which is why
`IVNodeFor<out TControl>` survives for creating slots alone.

**Mirroring the node hierarchy** (`VTableLayoutPanel : VPanel`) would give both covariance and static
typing, and it was rejected on cost rather than taste. Nodes are emitted one flat class per control,
all deriving from `VNode`, and mirroring the control hierarchy would change that:

- **A node for an ancestor would have to exist.** Registering or sweeping `TableLayoutPanel` would
  transitively generate `VPanel`, `VControlNode` and so on, whether or not the application registered
  them - so "a node the application never names is trimmed" would stop being true. (The bounded,
  borrowed-slot-only expansion in §4.3 is the same objection at one thousandth of the size: it emits
  exactly the declared type of a borrowed slot and never an ancestor.)
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

### 4.3 The node class a borrowed slot names has to exist

Typing `Panel1` as `VPanel?` makes the generator depend on a node class for `Panel` even when nothing
registered or swept `Panel`. So it emits one on demand: for every borrowed slot in the output, the node
class for the slot's declared control type, and **nothing else**. The rules keep it from becoming a
general "generate whatever a property mentions" rule:

- **Only a borrowed slot's declared control type**, never the type's ancestors and never the type of a
  creating slot. A creating slot is typed by the marker, which needs no node class of the property's
  type to exist.
- **It recurses only through the borrowed slots of what it emits.** A node emitted this way is itself
  a node with a slot list, so its own borrowed slots pull in their types the same way - that is how a
  `SplitContainer` node found this way still brings its panels' node class. The worklist is
  `Queue`-based and terminates because each control type is taken once.
- **De-duplication is the registration identity the generator already keeps** - the control type as it
  writes it - so a control both swept and needed by a slot is claimed once and reports no diagnostic.
  (`[VirtualDomAll]` therefore changes nothing for a swept application except that the slot is now
  exactly typed.)
- **A declared type that cannot have a node class has no slot.** Abstract, static, generic, or without
  an accessible parameterless constructor: there is nothing to construct, so the property is skipped
  rather than typed by a name that would not compile.
- **A declared type whose node name went to a placeholder has no slot either.** Two controls in one
  namespace with the same node name produce an unbuildable placeholder type, and a slot typed by a
  placeholder would be a worse failure than an absent slot.

Skipping is visible in the node: the property is simply not there, and the node still carries every
other property. The alternative - a property typed `VNode?` with a runtime check - is what this design
spent two revisions removing.

## 5. What the generator emits, and what it costs

Per control whose node has a slot property (today: `VSplitContainer`):

- the slot property, tracked like any other, carrying the control property's documentation - typed
  `VPanel?` for a borrowed slot and `IVNodeFor<Panel>?` for a creating one;
- a `GetSlots` override returning the slot list, built from the tracked values and the owner control
  (the array is allocated per call, and only for a node that has slots).

Per node class, unconditionally: the `IVNodeFor<TControl>` implementation - the interface on the
class declaration and one explicit member. That is the owner's accepted cost, and section 7 measures
it rather than asserting it is free.

Per node class whose control a borrowed slot holds: the `UnassignedNode` static and its override. The
set is the exact control types of the output's borrowed slots - not their base classes, because a
borrowed slot is typed with one node class and a derived control's node can never be in it - so a
`VButton` never pays for it, and neither does a `VTableLayoutPanel` for a `Panel` slot.

Per borrowed slot: the node class for the slot's declared control type, if nothing else in the
output provided it (§4.3). That expansion is bounded to borrowed slots and de-duplicated through the
registration identity, so it adds at most one node class per borrowed-slot type and never doubles what
a sweep already produced.

Nothing else changes. The slot properties come from the property model the sweep already walks, so
`[VirtualDomAll]` picks them up with no rule of its own; extension members, the documentation
carry-over and the styles work are untouched.

## 6. Tests

`Kolvir.Feature.VirtualDom.Tests` covers the mechanism, and `Kolvir.Feature.VirtualDom.Generators.Tests`
covers what is a compile-time or emit-step question:

| Test | What it holds |
| --- | --- |
| `VSplitContainer` with a node in each panel | Both panels get their contents on the first apply, `Panel1.Parent` is the split container, the container's `Controls` holds exactly the two panels it made, and each child's `Parent` is the panel it is in. |
| A slot's node describes its control | A property the slot node assigns (`Padding`) lands on the borrowed panel itself, and a property it does not assign keeps the container's own value. |
| Patch inside a slot | Changing a child's text inside a panel patches it - the child control is the same instance - rather than recreating the panel or the child. |
| A borrowed slot is never replaced | The panel the container made survives a render that gives the slot a new node, with the node's values applied to it. |
| Replace the slot's node | In a creating slot, a node of a different class creates and assigns a new control, while an unchanged class patches in place. |
| Removal | A slot that becomes null leaves the borrowed panel empty, and back at its constructor's property values rather than the removed node's; a creating slot's property keeps the control it made, emptied. |
| Ordering | Two slots that both take `VPanel` are told apart by `Name`, including across a render where the first slot is empty and the second is not - the ordinal-alignment case, which fails if the state list is not padded to the slot ordinal. |
| Two removals, twice, in two containers | The shared all-unassigned instance carries nothing from one removal to the next: each panel takes its own node's value in both rounds. |
| Nested slots | A creating slot holding a `VSplitContainer` fills, patches and unwinds both levels. |
| One instance in two slots | Two independent subtrees are mounted and patched apart; a shared node carrying a component fails by the component's own message. |
| Covariant slot value | A `VTableLayoutPanel` in a creating `Panel` slot creates a `TableLayoutPanel` in the property. |
| A node for a non-`Panel` control, creating slot | A generator test compiles a `VLabel` into a `Panel` slot and asserts the compile fails with `CS0029` naming both types. |
| A node for a `Panel` subclass, borrowed slot | A generator test assigns a `VTableLayoutPanel` to `SplitContainer.Panel1` and asserts `CS0029` naming `VTableLayoutPanel` and `VPanel` - the runtime check the previous revision had, now a type error. |
| A borrowed slot registered alone | Registering only `SplitContainer` still produces the `VPanel` class its slot is typed with, and no diagnostic. |
| A borrowed slot also swept | The same class is emitted once, with no duplicate-registration diagnostic. |
| A borrowed slot that cannot be typed | A property whose declared type is abstract is absent from the node rather than typed by a name that does not exist, and no node class is emitted for it. |
| What is not a slot | Generator tests assert that `Control.Parent`, `Panel`'s scroll bars and a same-kind reference have no node property, and that only the controls a borrowed slot holds carry the all-unassigned instance. |
| The demo's `--check` | The document page's `VSplitContainer` is asserted by parentage and contents - two panels, in the container, each holding its own node's label - and re-asserted after a later render, which is the slot patch path in the real application. |

## 7. Rung, and the measurements

The feature sits inside `Kolvir.Feature.VirtualDom`, which is already rung 3 by packaging. Inside
that package it is **rung 1**: no seam on `Control`, no registration, no work at startup, and every
byte of it is reached only from a node class the application names. `Mounted.Slots` is allocated
only for a node that has slots, `GetSlots` returns null for every other node, and the reconciler's
slot pass is two null checks on a path it already walks.

Measured per section 8 of `FEATURE-COST-POLICY.md` - `win-x64`, `-c Release -r win-x64`, published
from build roots of equal length (`C:\kolvircost\a` for this branch at `a4351ca`, `..\b` for
`develop` at `7072be1`), so the numbers are the change rather than where it was built. The build
roots hold worktrees of the two commits, so the `..\b` is the same length as the `a`; the probe
projects are `probe/` inside each root, identical source, referencing their own worktree.

| Publication | `develop` | this branch | delta |
| --- | --- | --- | --- |
| `Kolvir.Minimal`, Skia loose, AOT (an application that never references the feature) | 3,885,056 | 3,885,056 | **0** |
| probe: 2 controls registered, `VPanel` and `VLabel` constructed, applied and patched, **no slot filled** | 7,135,232 | 7,135,232 | **0** at 512-byte resolution |
| probe: 12 controls registered, the same work, **no slot filled** | 7,135,232 | 7,135,232 | **0** at 512-byte resolution |

The managed assembly is where the change is visible, because the published image is dominated by
Skia and steps in 512-byte units:

| Managed `probe.dll` | `develop` | this branch | delta |
| --- | --- | --- | --- |
| 2 node classes | 5,498,880 | 5,499,392 | **+512** |
| 12 node classes | 5,652,992 | 5,655,552 | **+2,560** |

The two rows together are the per-node figure: ten more node classes cost 2,048 bytes, so a node
class pays about **205 bytes** for its marker interface, its instantiation and the explicit member
that returns `this` - metadata, not code, which is why ten of them fit inside four image steps. The
remainder of the 512 is the slot reconciler, `VNodeSlot` and the `Mounted.Slots` field, reached only
from a node that has slots. No IL2xxx or IL3xxx warning in any publish, and the probe run prints the
patched values rather than throwing.

The use check is a real application with the feature in use rather than a probe:
`Kolvir.Demo.VirtualDom`, whose document page fills a `VSplitContainer`, publishes AOT at 4,496,896
bytes with 0 warnings and 0 errors, and its `--check` still ends in `check: OK` including the panel
parentage and contents assertions.

The generator's output grows by what it emits, and nothing else:

| | |
| --- | --- |
| Generated source, the demo's 36-node sweep | 3,116,355 -> 3,127,622 bytes, **+11,267 (0.36%)** |
| of which: the marker on 36 node class declarations | ~1,850 bytes (~52 per class) |
| of which: 36 explicit `IVNodeFor<T>.Node => this` members | 4,869 bytes (~135 per class) |
| of which: the all-unassigned instance on the 2 controls a borrowed slot holds | ~350 bytes (static + override) |
| of which: 3 slot properties, their carried documentation and 2 `GetSlots` methods | ~4,200 bytes |

The per-node half is the cost the owner accepted for the marker interface; the per-slot half is
proportional to the number of slots. Typing the borrowed slots with their node class made the output
*smaller* than the previous revision's 3,128,991 bytes - `VPanel?` is a shorter type than
`IVNodeFor<global::Kolvir.Panel>?` and the borrowed path lost its `.Node` hop - and the
all-unassigned instance now covers 2 controls instead of the 9 the previous chain-based rule picked
up. A `VLabel` pays nothing for a mechanism it can never take part in, and neither does a
`VTableLayoutPanel`, which is not the node class a `Panel` slot takes.

## 8. Out of scope

- **Control-typed collections** - a tab control's pages, a toolbar's items. A collection of nodes is a
  keyed child list whose controls the parent owns and arranges, which is a different mechanism from a
  single slot, and it is not this one.
- **Components in slots.** `Panel1 = new VComponent(page)` would need a second mount point per slot;
  the supported spelling is one node down, `Panel1 = new VPanel { Children = [new VComponent(page)] }`.
- **A borrowed slot typed with a base node class.** `VPanel?` is the only value a `Panel` slot takes,
  even though a `GroupBox` is a `Panel`. Giving the property a base type would need either a runtime
  cast or the mirrored hierarchy (§4.2).
- **A slot on a node from a different generator run.** The slot itself works - the reconciler is
  runtime code - but the `UnassignedNode` that hands a borrowed control back is emitted by the
  compilation that can see the slot's type, so a node class from another compilation has none and a
  removed slot's panel keeps the values the removed node set. Contents still come off. Fixing it
  would mean a `Reset` member on every node class, which is the per-node cost this design avoids.
- **Extension properties as slots.** An extension property that returns a `Control` is not a slot;
  the extension catalogue's members are read for values and handler pairs, not for contents.
