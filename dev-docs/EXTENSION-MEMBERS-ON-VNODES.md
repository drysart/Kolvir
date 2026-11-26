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

The scan is factored as a unit a future `[VirtualDomAll]` can reuse, because that feature needs the
same index over every available control.

## 3. What becomes a node member

**Extension properties** whose receiver the control is assignable to, and which have a public setter
and getter, become settable properties. A get-only extension property is skipped, exactly as the
native walk skips a get-only property, or `EffectiveInView` would become a node property that cannot be
assigned.

**Extension handler pairs** - `AddXxxHandler(this Control, Action<T>)` with a matching
`RemoveXxxHandler` - become event properties, exposed and applied exactly like a native event. They
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

## 9. No registry of node types

Nothing in this change creates a table of node types. Node classes are reachable only by
construction, so an application that never writes `new VLabel { ... }` does not retain `VLabel`, and
NativeAOT removes it. A `NodeType[]` registry would root every generated class and destroy exactly
that property - which is what makes a future `[VirtualDomAll]` affordable at all, since generating a
node for every available control is only acceptable if the unused ones are removed.

## 10. Cost

This is rung 1 of `FEATURE-COST-POLICY.md` section 4, and the claim is that an application which does
not use the virtual DOM cannot contain a line of it: the generator and the analyzer are analyzers,
which the compiler loads and the published image does not contain, and the three attributes live in
`Kolvir.Feature.VirtualDom`, which such an application does not reference.

Measured rather than asserted. `Kolvir.Minimal` published as NativeAOT with
`-c Release -r win-x64 -p:EmbedSkiaSharpNative=false`, from two worktrees of equal root length, one at
`develop` (`b33a768`) and one at this branch:

| | win-x64 |
| --- | --- |
| `Kolvir.Minimal.exe` at `develop` | 3,885,056 |
| `Kolvir.Minimal.exe` at this branch | 3,885,056 |
| delta | **0**, below the 512-byte resolution |

The absolute is larger than the table in the policy document because the native image embeds the
absolute path of `native\Kolvir.Minimal.pdb`, and these two roots were longer than the ones that
produced that table. The pair is what makes the delta comparable, and the delta is zero. The same
publish with `-p:TrimmerSingleWarn=false` reported no IL2xxx or IL3xxx warning, and nothing about
startup or window creation changed, because no assembly the fixture contains was edited.

The build-time cost is the other half, and it is paid per **keystroke** rather than per release, so it
is the number worth knowing. Measured with the shipped generator over this repository's own
`Kolvir.Demo.VirtualDom` compilation - six source files, 187 references, 20,622 types in the
compilation - driven through `CSharpGeneratorDriver` with one comment added to one file per edit:

| | time |
| --- | --- |
| first run (JIT, first metadata load) | 657 ms |
| per edit, warm, 20 edits | min 4.6 ms, median 5.4 ms |

Most of that 5 ms is the narrowed scan: 588 types across the seven assemblies the narrowing keeps
(`Kolvir`, the demo, three backends and the feature package). The spike measured the same walk at
0.97 ms, against 21.8 ms for a walk of all 20,622 types, which is why the narrowing is the design
rather than an optimisation: without it the generator would cost about 27 ms per edit instead of
about 5, and an incremental generator's model is rebuilt for every new compilation, so a cache across
edits cannot exist.

## 11. Out of scope

`[VirtualDomAll]` - a node for every control the compilation can see - is the next layer and is not
built here. It needs this scan, which is why the scan is factored as a reusable unit, but its sweep,
its silent-skip policy and its own placeholder types are not part of this change.
