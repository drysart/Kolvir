# Extension members on VNodes

The virtual DOM generator reads a control's settable instance properties and its
`AddXxxHandler`/`RemoveXxxHandler` pairs, and emits one node property per member. It cannot see C# 14
extension members, because those are members of a static class rather than of the control: a
`VTableLayoutPanel` node has no `TableColumn` today even though
`src/core/Kolvir/Controls/TableLayoutPanelExtensions.cs` gives every `Control` one.

This document records the design for reading them. After it, an extension property or extension
handler pair that applies to a control appears on that control's node **as if it were a native
member of the control**: settable on the node in an initializer, applied by `ApplyTo`, patched by
`PatchFrom`, restored to its pristine value when a render drops it.

The measurements it depends on are in `EXTENSION-MEMBERS-FROM-ROSLYN.md` (PR #152, branch
`spike-roslyn-extension-members`). That document is the record of what Roslyn reports; this one is
the record of what we decided to do about it, and does not restate its numbers.

## 1. Roslyn 5.9.0, and why the pin is the whole prerequisite

The spike's decisive finding: when Kolvir arrives as **metadata** - which is the situation for every
application that references the package, and therefore the primary case for this generator - a
Roslyn 4.14.0 reader sees the `extension` block as a mangled `<G>$...` class with no properties in
it. Under Roslyn 5.9.0 the same file reads with `TypeKind.Extension`, `Name == ""`, and
`ExtensionParameter.Type` giving the receiver.

All four projects that pin `Microsoft.CodeAnalysis.CSharp` therefore move from 4.14.0 to **5.9.0**.

5.9.0 rather than the newest available: it is what SDK 10.0.403 - the SDK this repository builds with
- ships, and an analyzer referencing a Roslyn newer than the host compiler fails to load. This costs
no consumer compatibility, because the repository targets `net10.0` only (`Directory.Build.props`) and
the library's own source already uses C# 14 extension blocks: anything that can consume Kolvir is
already on an SDK whose compiler is Roslyn 5.x.

So that a bump failure can never be mistaken for a feature failure, the bump is its own commit,
verified on its own (`dotnet build Kolvir.slnx` plus the full test suite) before any feature code
exists.

## 2. Scan compilation-wide, narrowed to assemblies that reference Kolvir

The scan walks the referenced assemblies to find extension blocks and extension methods. Walking
every referenced type costs about 22 ms and 4 MB per pass in a real application closure, which is
per **keystroke** in an IDE build: an incremental generator's model is recomputed for every new
`Compilation`, so caching across edits buys nothing.

So the walk is narrowed to the assemblies it can be narrowed to, which the spike measured at under
1 ms: the Kolvir assembly itself, the compilation's own assembly, and the referenced assemblies whose
references include Kolvir.

The narrowing is complete by construction rather than a heuristic: to write `extension(Control ...)`
you must be able to name `Control`, so the assembly must reference the assembly that declares it.
There is one theoretical hole - type forwarding, where a receiver resolves through a forwarder in an
assembly that does not name Kolvir - so the scan watches for it rather than assuming it away: a block
whose receiver does not resolve to a type widens the scan to every referenced assembly. That costs
the full walk, but only in the case that would otherwise silently miss members.

The scan is factored as a unit `[VirtualDomAll]` reuses (section 10), because that feature needs the same
index over every available control and the same reason for not walking everything.

## 3. What becomes a node member

**Extension properties** whose receiver the control is assignable to, and which have a public setter
and getter, become settable properties. A get-only extension property is skipped, exactly as the
native walk skips a get-only property, or `EffectiveInView` would become a node property that cannot be
assigned.

**Extension handler pairs** - `AddXxxHandler(this Control, Action<T>)` with a matching
`RemoveXxxHandler` - become event properties, exposed exactly like a native event and applied through
the first-use actuator of section 11.1 rather than through a line of their own in each node class. They
are found through `IMethodSymbol.IsExtensionMethod` plus `Parameters[0].Type`, which works for classic
extension methods and for methods declared inside an `extension` block alike, and needs no block
symbol at all. The real in-library pair was read rather than assumed: `PopupExtensions` declares
`AddPopupClosedHandler(this Control, Action<object, PopupResult>)` returning `IDisposable` and
`RemovePopupClosedHandler` returning `void`, so the return type is not part of the shape.

An extension property also has to have a public getter, not just a setter: the node reads the pristine
value back through it when a render drops the property, and a property that cannot be read cannot be
restored.

Ordering, and what happens when two members want one name, follows C# rather than a rule of its own:

- a member of the control **wins silently** over an extension member of the same name, because the
  language resolves an instance member before it looks at an extension, so the extension could never
  be reached. That is the same silence a hidden base property gets, and it is not a conflict: an
  extension that extends a control with a name the control already has is dead code, not a dispute;
- a member on a more specific receiver **hides** one on a base, exactly as a derived property hides a
  base one, so a member on `Control` never wins over the same name on `TextBox`;
- anything left - two containers extending one receiver with one name - is a conflict, and is handled
  by the placeholder of section 6.

## 4. Unset values

A node restores the property a render dropped to the value the control's constructor left. The
generator already emits one shared thrown-away instance per node type for that, and an extension
property uses the same instance: the receiver is `Control`, which is a base of every control, so
`get_X(Pristine)` is exactly the right read. Nothing constructs a control per property.

`[VNodePropertyUnsetValue]` declares the pristine value as a compile-time constant instead, on any
property - native or extension - that the generator flattens. It exists for the property whose
pristine value is a fact of the API (`TableColumn` is `0`) rather than something a constructor has to
be asked, and for the case where reading a property at type load is undesirable in itself.

As built, what it removes is the *read*, not necessarily the construction. A node type still needs its
one shared control whenever any flattened property has no declared value, and a control inherits dozens
of settable properties from `Control`, so in practice the instance stays and the attribute stops that
member from being asked. The test asserts exactly that: the declared constant appears in the generated
field and the constructed control is never read for it.

It is applied to the property - including an extension property the application declares itself - and
not to the manifest. It cannot be put on a library member the application does not own, so for
`TableColumn` the runtime read is what happens; that is the case the shared instance is for.

## 5. Naming, and the overrides

An extension member is exposed under its own name, so `TableColumn` on `Control` gives
`VPanel.TableColumn`. Two different containers can extend the same receiver with the same name, and
then the name is ambiguous; a rename has to be possible, and the override is assembly-level:

```csharp
[assembly: VNodeExtensionPropertyName(typeof(TableLayoutPanelExtensions), "TableColumn", "GridColumn")]
```

The container is `typeof(...)`, the member name and the replacement are strings, and the replacement
is the **whole** exposed name rather than a prefix, so `GridColumn` is what the node property is
called.

Strings are forced rather than chosen. `nameof` does not compile on an extension property (CS9286,
in both source and metadata), while it does bind for an extension *method*, so the handler-pair half
could have used it and the property half could not - and one shape for both is worth more than the
little `nameof` would buy. An override wins over the plain name. An override that lands on a name the
node already has from a native member is an error (VD015), because the conflict is then the
application's to fix rather than something a placeholder can stand in for.

The name an override matches is the one the node would otherwise *expose*: `TableColumn` for a
property, and `DragEnter` for the pair behind `AddDragEnterHandler`, since that is the name being
disambiguated. It renames the node property only - the accessor the generated code calls keeps the
declared name, which is the compiler's lowering of the declared member and has nothing to do with what
the application decided to call it. A test overrides `Column` to `GridColumn` and asserts that the
assignment still goes to `set_Column`.

## 6. Conflicts, and the analyzer tripwire

Two extension members can want the same exposed name with no override to separate them. That is a
latent conflict: it must fail the build **when the name is used**, be silent before that, and never
fail at run time or resolve itself silently.

The node therefore exposes neither real property. It exposes one inert placeholder instead:

```csharp
[global::Kolvir.Feature.VirtualDom.VNodeNameConflict("TableColumn",
    "Kolvir.TableLayoutPanelExtensions::TableColumn", "Acme.GridExtensions::TableColumn")]
public object? TableColumn { get; set; }
```

The placeholder carries no other attribute, does not enter the property model and is not referenced
by `ApplyTo`, `PatchFrom` or anything else the generator emits. That is the requirement that would
break the build silently if it were got wrong: a generated reference to it would make the
application unbuildable rather than the conflicting name unbuildable.

The tripwire is a diagnostic rather than `[Obsolete(message, error: true)]`, for three reasons: the
message can be written from structured data instead of a prose blob baked into an attribute argument;
it gets its own identifier, so it can be documented and pointed at a help link; and it can name the
attribute to apply instead of describing it.

- **`VNodeNameConflictAttribute`** carries the exposed name and one contender per claim, so nothing is
  parsed out of prose. For a member, a contender is `containing type::member`; for a node class name
  (section 7), it is the control type's identity and there is no `::`.
- **The analyzer** lives in `Kolvir.Feature.VirtualDom.Generators`, beside `NullAndLeafAnalyzer`, in
  the same assembly that emits the placeholder - so the diagnostic cannot be missing while the
  placeholder is present. It registers operation actions for `PropertyReference` and `ObjectCreation`
  (a compound assignment wraps a property reference, so `+=` is covered), matches the attribute by
  metadata name, and fires only on **references**. An unreferenced placeholder costs nothing.
- **The diagnostics** are VD013 (a node property name claimed by two extension members) and VD014 (a
  node class name claimed by two controls). Both are errors in the `VirtualDom` category, both point
  at a help link anchored in this document, and both say that resolving **either** claim resolves the
  conflict, with paste-ready attribute lines naming each contender.

Two consequences are recorded rather than fought:

1. Without `[Obsolete]` there is no compiler backstop, so an application can silence the tripwire by
   lowering VD013 or VD014 in `.editorconfig`. That is a deliberate act on a specific identifier,
   recorded in the diff, and it is not the hazard it looks like: turning analyzers off wholesale
   (`RunAnalyzers=false`) loses the tripwire and the generator together, so no node types exist and
   the application fails to compile at the use site instead - the same failure, by a different route.
2. A placeholder property is `object?`, which rejects a lambda by itself. Assigning a lambda to a
   conflicting *handler* name therefore produces our diagnostic plus a conversion error, where a
   value-type assignment produces ours alone. Both are compile-time, which is the requirement.

As built, the analyzer is `NodeNameConflictAnalyzer`, beside `NullAndLeafAnalyzer` in the generator
assembly, and it registers `PropertyReference` and `ObjectCreation`. The messages name every claim and
carry one paste-ready line per claim -
`[assembly: VNodeExtensionPropertyName(typeof(Kolvir.TableLayoutPanelExtensions), "TableColumn",
"<new name>")]` for a member, `[assembly: VirtualDom(typeof(Acme.Button), Name = "<new name>")]` for a
node class - with the name left for the reader to choose. A conflict is only ever between extension
members: a name a native member already owns is silent, per section 3.

## 7. Duplicate node class names

The same scheme covers two distinct controls whose node class names collide - `Acme.Button` and
`Widgets.Button` both wanting `VButtonNode` in the same namespace. Emitting both would be a duplicate
type; emitting one would be a silent choice between two controls. So neither real node class is
emitted, and one placeholder type carries the name and the same attribute, so `new VButtonNode()`
reports VD014 with both controls named and the registration line that separates them.

This replaces the old VD003 "two registrations produce the same node name" error, which reported the
collision the moment the classes were declared rather than when the name was used. A genuine
duplicate registration - the *same* control registered twice - is still VD004.

As built, an unreferenced placeholder is a class with an empty body and no base: `internal sealed
class VButton;`. It is deliberately **not** a `VNode`, because a placeholder that were one could be put
in a tree and paint nothing, which is exactly the silent wrong tree the tripwire exists to prevent.
Both that `new VButton()` reports it, and the initializer that would put it in a tree, fail to compile.

## 8. Emission goes through the lowered static accessors

Generated code never writes C# 14 extension syntax:

```csharp
global::Kolvir.TableLayoutPanelExtensions.set_TableColumn(control, value);
var pristine = global::Kolvir.TableLayoutPanelExtensions.get_TableColumn(Pristine);
global::Kolvir.PopupExtensions.AddPopupClosedHandler(control, handler);
```

The spike verified `set_X`/`get_X` compile on **C# 13 and C# 14**, with and without the extension's
namespace imported, and the handler pair was always an ordinary static call. So generated code does
not depend on the consumer's language version, and cannot be broken by a namespace ambiguity between
two extension containers.

This couples the generator to the compiler's lowering name - the `set_`/`get_` prefix plus the
property's own name. That is an implementation detail of Roslyn rather than specified surface. The
dependency is real and is pinned twice: a test compiles the emitted accessor call against a real
extension block and fails if the lowering changes, and another asserts the symbol shape the scan reads
(`IsExtension` plus `ExtensionParameter`) against the real `TableLayoutPanelExtensions`, so a compiler
change surfaces as a test failure rather than as nodes that quietly lost their properties.

Note what "the property's own name" means after an override: the node property is renamed, the
accessor is not, because the lowering follows the declared member. `Column` overridden to
`GridColumn` still emits `set_Column`/`get_Column`.

A handler pair's accessor calls now live in the generated actuator (section 11.1) rather than in the
node's `ApplyTo` and `PatchFrom`: they are still ordinary static calls with the receiver as the first
argument, and the node property's setter is the only thing that names the actuator. That is what lets
an application that never assigns the member keep the pair out of its image, and why the node class
emits one `ApplyExtensionActuators` call instead of a line per pair.

## 9. No registry of node types

Nothing in this change creates a table of node types. Node classes are reachable only by
construction, so an application that never writes `new VLabel { ... }` does not retain `VLabel`, and
NativeAOT removes it. A `NodeType[]` registry would root every generated class and destroy exactly
that property - which is what makes `[VirtualDomAll]` (section 10) affordable at all, since generating a node
for every available control is only acceptable if the unused ones are removed from the image. section 11
measures that this is what happens rather than asserting it.

## 10. `[VirtualDomAll]`: a node for every control

`[VirtualDomAll]` on a class has the generator emit a node for every control the compilation can see,
into that class's namespace. The attribute names nothing and takes nothing; the compilation supplies
the list.

**What qualifies.** A control is swept when a node could be built from it: derived from `Control`,
concrete, non-generic, reachable from the generated code, and constructible without arguments.
Everything else is skipped without a diagnostic, a warning or a comment. There is no remedy an
application could apply - the node factory is `new T()`, and a control that cannot answer that is one
no node could describe - so a message about it would be noise nobody can act on.

**Reachable means reachable from where the node is written.** The node classes are emitted into the
compilation's own assembly, so a control declared there may be `public`, `internal` or `protected
internal`: an application's own `internal class MyButton : Button` gets a node, and that node class is
emitted `internal` to match - a public node class naming an internal control would not compile. A
control in a referenced assembly still has to be `public`, because nothing else is reachable across an
assembly boundary, and a `private` or `private protected` nested control is out either way: reaching it
would need the derivation the node class does not have. A name two controls dispute is emitted as a
placeholder that is as visible as the most visible of them, so a public contender keeps the conflict a
diagnostic rather than turning it into a compiler error about inconsistency.

**Where it looks.** The narrowed walk of section 2, for the reason given there and one more: the sweep is
the part of this feature that would otherwise be tempted to walk every type in the closure. An
assembly that cannot name `Control` cannot declare one, so it cannot contribute a control to the
sweep either.

**Explicit registration wins.** A control named by `[VirtualDom(typeof(X))]` anywhere in the
compilation is left to that attribute and kept out of the sweep. The duplicate-registration error
(VD004) stays for that explicit registration - it is still two names for one control - but the sweep
refuses to manufacture the duplicate itself. This is what makes the attribute safe to add to a
manifest that already lists controls, which is the way an application grows into it.

**One name, two controls.** section 7's placeholder applies unchanged, and under a sweep it stops being a
curiosity: two controls with one simple name now collide without anyone typing the name twice.
Neither node is emitted, one `[VNodeNameConflict]` type takes the name, and VD014 reports its use with
both controls named and the paste-ready override lines. An override cannot resolve a collision between
two *controls*, so the remedy is a `Name` on an explicit registration of one of them - which also
means the sweep leaves that one alone from then on.

**One file per manifest.** A class may carry both attributes. The registered controls and the sweep
are one manifest and become one file, with the registered ones first, so a project can keep the names
it chose and let the rest arrive.

**Cost.** A sweep of thirty-six controls writes 2.1 MB of source per compilation, which is the number
to know before turning it on in a large application; section 11 measures it.

## 11. Cost

**The rung is 3 for the package, and it cannot sit lower there.** The feature is not a member an
application calls into base `Kolvir`; it is the shape of code a generator writes, and the generator
ships in a package. So it lives where it already lives, in `Kolvir.Feature.VirtualDom` - a rung-3
package under `FEATURE-COST-POLICY.md` section 4 - and an application that does not reference that
package pays nothing for it: the generator and the analyzer are analyzers, which the compiler loads
and the published image does not contain, and the attributes live in the package such an application
does not reference. `Kolvir.Minimal` - no virtual DOM, no manifest, **no generated node classes at all** -
publishes at the same size at `develop` and at this branch, 3,885,056 bytes on win-x64 from worktrees
of equal root length (`b3177a8` and this change, both remeasured).

**Within the package it does not reach rung 1 for extension properties, and the reason is the rule
that makes them useful.** A member is exposed per receiver *type*, not per use. A node class the
application constructs carries every extension property that applies to its control, and `ApplyTo`
names each one inside an `if (...)` - a reference the trimmer cannot see through, so the property is
retained whether or not the application ever sets it. Emitting a member only when something writes it
is not available to a source generator: it emits types, not uses. The cost that follows is 5,632
bytes in total, measured below, and it is accepted rather than engineered away, because these are the
hottest allocation-free path a node has: routing every property set through a dictionary would trade
a real per-render allocation for a rounding error of bytes.

**Extension handler pairs do reach rung 1, by being installed on first use.** Section 11.1 is the
implemented design: until an application assigns one, nothing in any generated node names its
accessor, so the pair's attach and detach code and everything they reach are unreachable and trim
away. That is where the 23,040 bytes were, and they are no longer paid by an application that never
touches the member.

That first cost was measured on an application that **does** generate nodes - a throwaway console
probe referencing `Kolvir` and `Kolvir.Feature.VirtualDom`, carrying a manifest, and constructing
`VPanel` and `VLabel` - published the same way (`-c Release -r win-x64 -p:EmbedSkiaSharpNative=false`,
roots `C:\kolvircost\p1` and `p2`, 17 characters each):

| | win-x64 |
| --- | --- |
| probe at `develop` (`b33a768`) | 1,751,040 |
| probe at `b33a768` with the feature | 1,777,664 |
| delta | **+26,624**, 1.5 percent |

Attributed by building the generator with one half of the feature disabled:

| | delta over `develop` |
| --- | --- |
| extension properties only | +5,632 |
| extension handler pairs only | +23,040 |
| both, as the feature shipped | +26,624 |

Every one of those bytes was code the node classes named - the properties through `ApplyTo` and
`PatchFrom`, the handlers through the accessors of their own pair. The properties are cheap and stay
where they are. The handler pairs were not, and the reason is this repository's own pair:
`AddPopupClosedHandler` has a `Control` receiver, so it was a member of *every* node's class, and its
accessor reaches `PopupChannel`, its `ConditionalWeakTable` and `HandlerToken` - about 23 KB of popup
machinery that an application which never shows a popup paid for because a node had a `PopupClosed`
property it never set. An application that does show popups was already paying it, and still does.

### 11.1 Actuators installed on first use (implemented)

The handler-pair half of that cost is gone: an extension handler pair's **actuator is installed
statically, once per process, on first use** of that member. Until something assigns the node
property, nothing in the generated node names its accessor, so the popup machinery is unreachable and
trims away. Once installed, that member is applied permanently for the process. A permanent effect of
first use, rather than per-node opt-in - which is what keeps it clear of the reachability problem
`FEATURE-COST-POLICY.md` section 3 describes: the reference comes into being at run time, from code
that only runs because the application has already asked for the member.

- **Install path.** A lock on installation only, with copy-on-write publication of the actuator array
  and a volatile read at apply time. Nothing in this repository constructs nodes off the UI thread and
  no thread rule is documented, so this is cheap insurance rather than a requirement: installation
  happens once per member per process, the steady-state cost is nil, and the design does not rest on
  an undocumented invariant.
- **Signature.** One delegate type, `VNodeExtensionActuator(previous, current, target)`, because
  `PatchFrom` holds both nodes: attach, detach and change become one call, and each node emits a single
  call into `VNode.ApplyExtensionActuators` instead of per-member apply and patch lines.
- **State.** A `Dictionary<object, object?>` on `VNode`, public and marked
  `[EditorBrowsable(EditorBrowsableState.Never)]`, rather than a `ConditionalWeakTable`. Generated code
  is emitted into the *application's* assembly, so node-side storage cannot be `internal`: public
  surface is unavoidable, and `EditorBrowsable` is effective here precisely because the consumer is a
  different assembly - it does not change accessibility, so generated code can still reach it. The
  honest cost is one nullable field per `VNode` instance, not allocated until used, plus a new public
  member on a core type.
- **Keys.** A `static readonly` identity token per member rather than a string: reference equality, no
  hashing, and the token carries no reference to the accessor, so declaring it roots nothing. It is
  declared in the generated per-manifest actuator class rather than in the extension class itself,
  because a generator cannot add a field to a type in another assembly - the extension class is the
  library's, and the nodes are emitted into the application's. One token and one actuator per member
  rather than per node class, so two node classes exposing the same pair store under the same identity
  and the pair is attached once.
  The actuator, which does name the accessor, is named only from the node's setter.
- **The invariant.** A value cannot exist for a member that was never installed, because setting it is
  what installs it. The store is public, so reflection or a future deserialisation path could write it
  without installing: the apply path reports that rather than skipping the value in silence.
- **Values, not `Specified<T>`.** For a handler, "not assigned" and "set to null" have the same
  effect - no handler attached - so the property needs no unspecified state of its own. The one shared
  path is therefore also the only place patch semantics live: attach on a first apply, detach the old
  handler and attach the new one on a change, detach when a render omits it. Handlers are compared with
  `object.Equals`, so a render that produces the same delegate again does not detach and reattach it.
  All three transitions are covered in `Kolvir.Feature.VirtualDom.Tests`, and the demo's
  `UndoStateChanged` pair exercises the same path through `--check`.

**What it saves, measured on this branch.** Two probe projects at `C:\kolvircost\a-probe` and
`b-probe`, with `..\a` and `..\b` worktrees of equal-length roots (`a` is this branch, `b` is
`develop` at `b3177a8`), published `-c Release -r win-x64 -p:EmbedSkiaSharpNative=false` with
`-p:TrimmerSingleWarn=false` and no IL2xxx or IL3xxx warning. The probe constructs `VPanel` and
`VLabel` **and applies and patches them**, because a node whose `ApplyTo` is never called has that
method trimmed and would not measure the pair at all - which is also why these numbers sit above the
1,777,664 recorded above rather than beside it:

| probe, win-x64 | `develop` `b3177a8` | this branch |
| --- | --- | --- |
| nodes applied and patched, `PopupClosed` never set | 1,843,712 | 1,827,328 |
| the same, one node sets `PopupClosed` | 1,843,712 | 1,853,440 |

- **Never touching the pair: -16,384 bytes**, which clears the 512-byte resolution of these
  measurements by a wide margin. The old shape charged the pair in both probes to the byte, which is
  the rooting being removed.
- **Touching it: the cost comes back, and only then.** The new probe with `PopupClosed` set is
  26,112 bytes larger than the same probe without it; at `b3177a8` the two were identical because the
  application always paid. It is also 9,728 bytes larger than the old shape's, because the install and
  apply path is now retained alongside the popup machinery it enacts.
- The 16,384 saved rather than the 23,040 attributed is the shared install/apply path - the node
  store, the registry, the generic accessors - that every application referencing the package now
  retains: about 6,656 bytes, arithmetically 23,040 minus 16,384.

**Making it live is now observable, and permanent.** A pair nobody touches costs nothing; the first
assignment to it installs it for the rest of the process, and from then on every node of every type
whose class exposes it consults the actuator on apply and patch. That is the trade this design makes,
and it is why the effect is a first-use install rather than a per-node decision.

**Extension properties stay on the direct path, deliberately.** They are 5,632 bytes in total, and a
node property is the hottest allocation-free path a node has; a dictionary behind every set would add
a per-render allocation and a lookup to buy a rounding error of bytes. The asymmetry is a decision,
not an omission. If the mechanism ever generalises to properties cheaply - a per-member actuator that
assigns rather than attaches - that is a follow-up, not this change.

**What is still on record as unbuilt:** a per-member opt-out, an attribute naming specific extension
members to leave off the nodes. It was the cheaper design when the 23 KB shipped; now that handler
pairs cost nothing until used, it remains the only way to stop a member from being *exposed*, which is
a different question from what it costs.

**Trimming.** The decisive question for `[VirtualDomAll]` is not "same application before and after"
but "unused node types are removed". Two measurements from equal-length build roots, at the commit
that introduced the sweep:

| | win-x64 |
| --- | --- |
| probe, 3 controls registered, `VPanel` and `VLabel` constructed | 1,777,664 |
| probe, 12 controls registered, the same two constructed | 1,777,664 |
| demo, `[VirtualDomAll]`: 36 node classes, 5 constructed | 4,435,968 |
| demo, 5 explicit registrations, the same 5 constructed | 4,435,968 |

Identical to the byte in both pairs. Thirty-one swept node classes that the sample never constructs
are absent from the published image rather than merely unreferenced, which is the property
`[VirtualDomAll]` depends on: the sweep generates for every control and the application pays for the
ones it names. For scale, one constructed node class is worth about 2,048 bytes (the probe with a
single registration and one fewer constructed node measured 1,775,616). Both publishes used
`-p:TrimmerSingleWarn=false` and reported no IL2xxx or IL3xxx warning.

**Build time.** Paid per **keystroke** rather than per release, so it is the number worth knowing.
Measured with the shipped generator driven through `CSharpGeneratorDriver` over this repository's
`Kolvir.Demo.VirtualDom` compilation - five source files, 180 references, 20,622 types - with one
comment added to one file per edit, median of 15 edits after a cold run:

| | cold | per edit |
| --- | --- | --- |
| before this branch (explicit registrations) | 317 ms | 4.44 ms |
| this branch, the same explicit manifest | 347 ms | 4.66 ms |
| this branch, `[VirtualDomAll]` (36 node classes) | 354 ms | **18.05 ms** |

The 18.05 ms decomposes as: 0.9-1.5 ms for the narrowed extension scan, 0.6-1.1 ms for the control
sweep, and about 15.5 ms building 36 control models (roughly 3,400 node properties, each with a
type-display string and an unset-value lookup) and writing 2.1 MB of generated source. It is linear
in node classes at about **0.43 ms each**, so the number to predict for an application is its own
count: a project sweeping the ~36 controls in `Kolvir` pays about 18 ms per edit, and one referencing
more control packages pays proportionally more. It is not linear in manifests - one manifest or two
claiming the same controls costs the same.

Measured again for the first-use actuators of section 11.1, with the same harness over worktrees of
equal-length roots (`C:\kolvircost\a` for this change, `..\b` for `develop` at `b3177a8`; six source
files and 187 references resolved here rather than five and 180): cold 463 ms before and 457 ms after,
and a median per edit of 19.45-22.92 ms before against 20.48-24.46 ms after, over three runs each.
The ranges overlap, so the **19,499 bytes** the generated source lost - 0.9 percent, about 542 bytes
per node class on 36 classes - did not clear the noise of this measurement and is not claimed as a
speed-up. The cold compile of the demo project says the same: `-t:Rebuild --no-dependencies`, median
of three, 4,018 ms at `b3177a8` and 3,988 ms here, a 30 ms difference inside the run-to-run spread.
The 18.05 ms above stands as the baseline; what this change bought is the image, not the clock.

The control sweep only runs when a manifest actually sweeps. Without that gate the explicit-manifest
row would have paid for a walk whose answer nobody asked for; with it, an application that registers
its controls by name pays what it paid before this branch plus the extension scan it already had.

The other build-time number is the one a developer restarts the build to see. Rebuilding only the
demo project (`-t:Rebuild --no-dependencies`, which still runs the child build that produces the
generator), median of three: 3,591 ms with five explicit registrations, 4,862 ms with the sweep. So
36 node classes cost about **1.3 s** on a cold compile of that project, and 2.1 MB of source per
compilation is the reason.

Warm, with the same `Compilation`, every row is 0.04-0.05 ms: the incremental model holds, and a
keystroke is a new `Compilation` rather than a cache hit, which is why none of this is cached.

## 12. Out of scope

Sweeping is all or nothing. There is no way to exclude one control from a `[VirtualDomAll]` (register
it explicitly instead - that also keeps it out of the sweep), and no policy for silently skipping an
extension member the node cannot express. There is no per-member opt-out either; section 11 records
the 5,632 bytes extension properties still cost as the accepted price of the shape, and section 11.1
records handler pairs as installed on first use, with the measurements that came out of it. The
attributes are fixed at three: unset values, the naming override and the conflict marker. Startup and
window creation do no work for any of it: the generator and the analyzer are compile-time, and the
node classes are ordinary code reachable only where an application names them. The one run-time
effect first use adds is that assigning an extension handler member installs it for the rest of the
process - no startup work, and nothing at all for a member nobody touches.

What the node classes later gained - the documentation of the control property they expose, read from
the reference's XML file because the compiler attaches no documentation provider to a reference - and
the row and column styles a table can now assign, are in `dev-docs/VNODE-DOCUMENTATION.md`, including
what each costs in generated source and per-edit generator time.
