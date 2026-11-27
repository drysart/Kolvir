# Element collections on nodes

Status: implemented. Rung: **3** for the feature (`Kolvir.Feature.VirtualDom`), with two **rung 1**
additions to core, both measured at zero cost for an application that does not use them.

This note records why a generated node can manage the contents of a control's **get-only collection
of element objects**, using `RichLabel.Spans` as the case that forced it. It is the third variation of
a theme the generator has hit: a settable property, a get-only control slot, and now a get-only
collection of things that are not controls.

---

## 1. The obstacle

`RichLabel.Spans` is:

```csharp
public TextSpanCollection Spans => _spans;
```

`TextSpanCollection` is an `IList<TextSpan>` the label owns. A `TextSpan` is a sealed class carrying
mutable styling, a `Tag`, six handler lists and live state (`Owner`, `IsMouseOver`, `IsPressed`).
More importantly, `RichLabel.OnSpanAdded` parents the span's own `Control` into the label:

```csharp
if (span.Control is { } control && !ReferenceEquals(control.Parent, this))
{
    Controls.Add(control);
}
```

and `OnSpanRemoved` removes it again. So a render cannot replace the collection. Replacing it would
destroy and re-parent inline controls and re-register every handler on every frame. The spans have to
be **reconciled against the live `TextSpan` instances**.

The mutation path already exists and notifies the owner: `Add`, `Insert`, `RemoveAt`, the indexer and
`Clear` all set `Owner` and call `OnSpanAdded`/`OnSpanRemoved`. The design uses that path rather than a
parallel one.

---

## 2. Where the descriptions come from

The generator resolves symbols at compile time. It already knows every property, type, handler pair,
accessor and collection, so a runtime reflection-like description layer would turn knowledge the
build already has into data the image pays for. It would also be needed only for reflection-loaded
types, plugins not referenced at compile time, or data-driven schemas - none of which VDOM has.

The three answers to "where do descriptors come from" were considered:

| Source | Verdict |
|---|---|
| Hand-authored member lists beside each type | Rejected. They drift silently the first time a property is added and nothing detects it. |
| Reflection | Rejected. Trim-hostile, and it would put IL2xxx risk into the core assembly every application pays for. |
| Source-generated | Sound, but not as a runtime descriptor table. The generator can emit the description class and its direct assignments; an indirection table would make a loop out of code it can write straight. |

The chosen shape is therefore a **source-generated description class**: `VTextSpan` has real typed
properties and generated `ApplyTo`/`PatchFrom`/`Detach`, exactly as a control node does. There is no
runtime member lookup, no dynamic access and no descriptor table.

---

## 3. The three candidates

The owner's relaxation made `RichLabel`'s API fair game. The three candidates were costed:

### Candidate 1 - a description type in core, and the label reconciles

Introduce an immutable span description in core, and have `RichLabel` match, patch, add and remove
its live `TextSpan`s from a sequence of descriptions.

**Cost.** A new public core type, a second way to express spans, and reconciliation state in the
label. The hard part is not the values; it is the child control. A description that carries a
`Control` must keep *the same control instance* across a patch, and the control's own subtree is
patched by the VDOM reconciler's `Mounted` records. Moving that state into the label either makes the
label understand node/child-control reconciliation - which core must not do - or splits the state in
two, one half in the label and one in the feature. It is more machinery for the same answer, and the
only consumer is the VDOM feature.

**Verdict.** The shape is right; the location was wrong. The description type belongs beside the
feature that consumes it.

### Candidate 2 - make `TextSpan` a value, move live state into the label

`TextSpan` becomes an immutable value with value equality, and `Owner`, hover/press state, handler
lists and the inline control move into label-owned state keyed by identity or key.

**Cost.** A real redesign of the label and a breaking change for anyone touching `Owner`, hover state,
or mutating a span in place. The payoff is value equality, but value equality is not what this feature
needs: the reconciler is driven by the previous *node*, not by comparing element values. Control
identity and handler detachment still have to be solved. This is the largest change on the list for
the smallest additional benefit.

**Verdict.** Rejected for this feature. It would be a separate redesign with its own migration story.

### Candidate 3 - one sequence of an abstraction implemented by both

`Spans` becomes a sequence of an interface that both a mutable `TextSpan` and a declarative
description satisfy, and the label interrogates whichever it is given.

**Cost.** A heterogeneous list is where surprises live: a mutable element and an immutable element
in one collection, with different notions of identity, handler ownership and control lifetime. The
label has to branch on the implementation for every operation.

**Verdict.** Rejected. It adds complexity to the imperative path, which must stay first class.

### Recommendation

A **feature-side description class**, with reconciliation state in the `Mounted` tree:

- `VTextSpan : VElementDescription` is generated with direct typed members, handler pairs and a
  control slot.
- The generated `VRichLabel` node exposes `Spans` as `IReadOnlyList<VTextSpan>?`.
- The reconciler matches descriptions to the live `TextSpan`s and mutates them through
  `TextSpanCollection`'s public API; `Mounted` holds which `TextSpan` corresponds to which
  description.
- `RichLabel` and `TextSpan` are otherwise unchanged, and the imperative path is unchanged.

This is candidate 1's shape with the description sited where its consumer is. It passes the three
gates:

1. **Rung 1 by construction.** Core gains only an inert marker attribute and one collection move
   primitive; an application that never uses the feature references neither reachable path.
2. **Trim-safe.** No `System.Reflection`, no dynamic dispatch, no registrar. The generated code is
   direct assignment; the only new runtime types are in the feature package.
3. **Minimal, with no unnamed second consumer.** There is no general core description layer. The
   generator's element rule is general within the feature, and a second collection needs only a
   marked element type and a get-only `IList<T>`.

---

## 4. What is generated

### 4.1 The element rule

A class is an element type when it is marked with `[VirtualizableElement]`
(`Kolvir.VirtualizableElementAttribute`):

```csharp
[VirtualizableElement]
public sealed class TextSpan { .. }
```

and it is concrete, non-generic, accessible from the generated code, has an accessible parameterless
constructor, and is not a `Control`. The compilation's own assembly and the referencing assemblies are
scanned for the attribute. Ineligible declarations are reported as **VD016**, because the attribute
is an explicit declaration and silently ignoring it would leave a collection mysteriously absent.

The attribute lives in `Kolvir` rather than in the feature because the element type it marks lives
there too: a core type cannot name a type from a package that references core. It carries no
behaviour and no state. Its name describes what the type is - an element a render can virtualise -
rather than the feature that currently reads it, because core's public vocabulary outlives any one
consumer and the collection rule is meant to generalise beyond this one.

### 4.2 The collection rule

A get-only property whose type implements `IList<T>`, where `T` is a marked element type, is a
collection property:

```csharp
public TextSpanCollection Spans => _spans;   // TextSpanCollection : IList<TextSpan>
```

A public setter is not one of these: the ordinary property path already assigns it and the control
compares the sequence it was given (`TableLayoutPanel.RowStyles` works that way today). The rule is
structural, so `[VirtualDomAll]` picks up `RichLabel.Spans` without a RichLabel-specific case.

The generated node property is a sequence of descriptions:

```csharp
public IReadOnlyList<VTextSpan>? Spans { get; set; }
```

`[VirtualDomAll]` therefore emits one `VTextSpan` description class and the `Spans` member on
`VRichLabel`; no other control pays for it.

### 4.3 The description class

`VTextSpan : VElementDescription` has:

- the text and styling members as typed properties (`Text`, `Bold`, `Italic`, `Underline`,
  `Strikethrough`, `ForeColor`, `HoverBackColor`, `FontSize`, `FontFamily`, `Font`,
  `InlineAlignment`, `Cursor`, `Tag`);
- the six handler pairs exposed exactly as a control node exposes a native pair (`Click`,
  `MouseDown`, `MouseUp`, `MouseMove`, `MouseEnter`, `MouseLeave`);
- `Control` as a **creating slot** typed `IVNodeFor<Control>?`, so a control node is constructed
  inline;
- the inherited `Key`.

`ApplyTo` and `PatchFrom` are direct assignments; `PatchFrom` detaches a replaced handler and attaches
the new one only when the delegate differs; `Detach` removes what the description attached. The
control slot is reconciled in place: the same control instance survives a patch, and a different
node class replaces it only when the node class changes.

### 4.4 What the shape looks like

```csharp
new VRichLabel
{
    Spans =
    [
        new VTextSpan { Text = "hello " },
        new VTextSpan
        {
            Text = "click ",
            Bold = true,
            HoverBackColor = ThemeColor.Accent,
            Click = (_, _) => Open(),
            Control = new VButton { Text = "go" },
        },
    ],
}
```

The collection expression, the inline element construction and the nested control node all work
because `Spans` is an ordinary sequence-typed node property, not a special method.

---

## 5. Reconciliation

Per-instance state is in the `Mounted` tree, alongside slot state:

- `Mounted.Collections` has one `MountedCollection` per collection property, in declaration order.
- `MountedCollection.Elements` holds one `MountedElement` per live element: the element, the
  description that currently describes it, and the mounted control slots of that description.

The node is rebuilt every render and holds none of this.

### 5.1 Matching

Keys win over positions:

1. A keyed description matches the previous element with the same key, wherever it has moved to.
   Duplicate keys in one render are an error with a clear message; a key that identifies two elements
   identifies neither.
2. An unkeyed description matches the previous element at its ordinal, provided a keyed description
   has not already claimed it.
3. A keyed description with no matching key is new. It does not steal a positional element whose key
   is different; a key is an identity claim, and changing it means a new element.

When keys and positions disagree, the key wins and the element is **moved** to the keyed
description's position.

### 5.2 Patching and churn

- A matched element is patched in place: the same `TextSpan` (or other element) instance is retained,
  and only the members that changed are assigned.
- A handler is detached from the old delegate and attached to the new one only when the delegate
  changed. A handler omitted after being set is detached. A removed element has its handlers detached
  by `Detach`.
- A reorder with keys is a move, not a remove and an insert. It uses the collection's own move
  primitive where one exists (see section 6), so the element keeps its `Owner` and its inline control
  stays parented to the label.
- Equal input does no collection work at all: no insert, no remove, no move, no property assignment
  and no handler churn.

### 5.3 A span whose control changes

The old control is taken off with `Unmount`, the new one is made, and the span is relinked through
`RemoveAt`/`Insert` at its position. `OnSpanRemoved` sees the new control (still unparented) and does
nothing; `OnSpanAdded` parents it. The old control never leaks into `Controls`, and the new one is
parented exactly once.

### 5.4 Removal

`RichLabel.OnSpanRemoved` already removes a span's control when it is parented to the label, so removal
through `RemoveAt` is sufficient. The description's `Detach` runs first to remove handlers, and its
control slots are unmounted. The label's existing behaviour is unchanged.

### 5.5 Identity across two labels

`TextSpan.Owner` is single-valued. The node path respects that: the reconciler creates each element
through `ElementType.Create()` and owns it in its collection state, so an element is never adopted
from another label. An application that imperatively adds one `TextSpan` to two labels still gets the
old, unsupported behaviour - the second collection takes ownership - and this feature does not change
or introduce that. The declarative path cannot produce it.

### 5.6 Omitted, empty and null

- The property omitted (never assigned, or assigned `null`): "this render says nothing about the
  collection". The elements already there are left exactly as they are.
- The property assigned `[]`: the collection is emptied.
- A control used imperatively, with no node anywhere, is untouched: no reconcile is called.

This is the same distinction an ordinary node property draws between "not assigned" and "assigned a
value", with one difference the collection cannot avoid: there is no pristine snapshot to restore on
omission, so omission leaves the last description in place rather than rebuilding the control's
constructor state.

---

## 6. The one core change: `TextSpanCollection.Move`

A move through `RemoveAt` + `Insert` calls `OnSpanRemoved`/`OnSpanAdded`, which unparent and re-parent
the span's control. That is the churn goal 2 forbids, and it is exactly what children already avoid:
`ControlCollection.Move` moves a child without running the remove/add path.

Spans had no equivalent. The minimal addition is:

```csharp
public void Move(int fromIndex, int toIndex)
```

on `TextSpanCollection`. It:

- moves the span in `_items`;
- keeps `Owner` unchanged;
- does **not** call `OnSpanRemoved`/`OnSpanAdded`;
- moves the span's control inside the label's `Controls` with `ControlCollection.Move`, so painting
  and hit-testing order follow the span order and the control is never unparented;
- calls `RichLabel.OnSpanMoved()`, which invalidates layout - a move is a layout change, not a
  content change, so hover/press identity is left alone.

The cost is one public method and one internal notification. `Add`, `Insert`, `RemoveAt` and the
indexer are unchanged. Imperative users get a useful primitive; the VDOM uses it only when the
collection type exposes a public `Move(int,int)`, which the generator detects from symbols. A
collection without one falls back to remove and insert, so the general mechanism does not depend on
the core addition.

No other core change: `RichLabel`, `TextSpan` and `TextSpanCollection`'s existing members are
untouched.

---

## 7. Goals, and how they are met

1. **Easy inline creation.** `Spans = [new VTextSpan { .. }, new VTextSpan { Control = new VButton {
   .. } }]` is a collection expression over an ordinary sequence-typed node property, with nested
   control nodes in the same initializer.
2. **Minimal churn.** Matching is key-then-position, the same rule children follow. A keyed reorder is
   a move; a retained element and its control keep their instances; a handler is detached rather than
   orphaned; equal input does nothing.
3. **Generalisation.** Any marked element type and any get-only `IList<T>` of it works. The second
   collection needs no framework change: mark the element type, expose the list. The core move
   primitive is used when the collection type has one and is not required otherwise.

---

## 8. Measurements

### 8.1 Cost to an application that does not use the feature

`Kolvir.Minimal` (one window, one label, no virtual DOM) published with NativeAOT, `win-x64`,
Skia loose, from build roots of equal path length:

| Build | Bytes |
|---|---:|
| `develop` (`d79b58a`) | 3,885,568 |
| this branch | 3,885,568 |
| delta | **0**, below the 512-byte method resolution |

The core additions are rung 1: the attribute is metadata on a type the application already has, and
`TextSpanCollection.Move`/`RichLabel.OnSpanMoved` are unreachable from an application that never calls
`Move`. The virtual-DOM description types live in the rung-3 feature package and are not in the
minimal application at all.

### 8.2 Trim warnings

Publishing with `-p:TrimmerSingleWarn=false` produced **no IL2xxx or IL3xxx warnings**. There is no
reflection, no dynamic access and no attribute-driven registration in the new code.

### 8.3 Generator cost, when it is used

Measured on the sample's `[VirtualDomAll]` manifest (`Kolvir.Demo.VirtualDom`), generated `.g.cs`
bytes:

| | Bytes |
|---|---:|
| `develop` | 3,127,622 |
| this branch | 3,150,131 |
| delta | **+22,509** |

The delta is one element description class (`VTextSpan`, 21,947 bytes, with every `TextSpan` member
and six handler pairs) plus the `VRichLabel` collection property and `GetCollections` wiring (562
bytes). A node class with no collection emits no `GetCollections` override and pays one virtual call
that returns null.

---

## 9. Tests

Feature suite (`Kolvir.Feature.VirtualDom.Tests`), 12 new tests:

- inline creation, including a control node inside a span;
- a retained element patched rather than replaced;
- an element added in the middle and one removed, with the right sequence and retained instances;
- a handler attached, replaced (old detached), omitted (detached) and invoked;
- an unchanged render does no collection or member work (counted on the list and the element);
- a keyed reorder moves without recreating;
- `VRichLabel` spans arrive on the control;
- a span and its control are retained across a patch;
- replacing a span's control removes the old one;
- removing a span removes its control;
- a keyed reorder keeps spans and their controls;
- the imperative label path, with no node anywhere, including `Spans.Move`.

Generator suite, 5 new tests:

- a get-only `IList<MarkedElement>` becomes a sequence property with a description class;
- a collection with a public `Move` gets the move delegate;
- `[VirtualDomAll]` picks up `RichLabel.Spans`;
- a marked element type in a referenced assembly is found;
- an element description name that a control node already uses is reported and the property omitted.

Core suite: two `RichLabelTests` cases for `Spans.Move` - one moving a span backward and one
forward - keeping ownership and the control parented.

Full solution: `dotnet build Kolvir.slnx -c Release` - 0 warnings, 0 errors; `dotnet test
Kolvir.slnx` - 1,956 passed, 21 skipped, 0 failed across 16 test assemblies.
