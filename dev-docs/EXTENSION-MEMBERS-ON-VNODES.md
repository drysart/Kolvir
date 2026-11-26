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

**Extension properties** whose receiver the control is assignable to and which have a public setter
become settable properties. A get-only extension property is skipped, exactly as the native walk
skips a get-only property, or `EffectiveInView` would become a node property that cannot be assigned.

**Extension handler pairs** - `AddXxxHandler(this Control, Action<T>)` with a matching
`RemoveXxxHandler` - become event properties, exposed and applied exactly like a native event. They
are found through `IMethodSymbol.IsExtensionMethod` plus `Parameters[0].Type`, which works for classic
extension methods and for methods declared inside an `extension` block alike, and needs no block
symbol at all. The real in-library pair was read rather than assumed: `PopupExtensions` declares
`AddPopupClosedHandler(this Control, Action<object, PopupResult>)` returning `IDisposable` and
`RemovePopupClosedHandler` returning `void`, so the return type is not part of the shape.

The ordering is the native walk's: properties before events, and within each, the most specific
receiver before the least, so a member on `Control` never wins over the same name on `TextBox`.

## 4. Unset values

A node restores the property a render dropped to the value the control's constructor left. The
generator already emits one shared thrown-away instance per node type for that, and an extension
property uses the same instance: the receiver is `Control`, which is a base of every control, so
`get_X(Pristine)` is exactly the right read. Nothing constructs a control per property.

`[VNodePropertyUnsetValue]` declares the pristine value as a compile-time constant instead, on any
property - native or extension - that the generator flattens. It exists for the property whose
pristine value is a fact of the API (`TableColumn` is `0`) rather than something a constructor has to
be asked, and for the case where constructing a control at type load is undesirable. With it the node
type needs no discarded control at all.

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
   lowering VD013 or VD014 in `.editorconfig`. That is a deliberate act on a specific identifier.
   Turning analyzers off wholesale does not lose the tripwire, it loses the generator too - no
   analyzer means no generated node types, so the application fails to compile at the use site
   instead, which is the same outcome by a different route.
2. A placeholder property is `object?`, which rejects a lambda by itself. Assigning a lambda to a
   conflicting *handler* name therefore produces our diagnostic plus a conversion error, where a
   value-type assignment produces ours alone. Both are compile-time, which is the requirement.

## 7. Duplicate node class names

The same scheme covers two distinct controls whose node class names collide - `Acme.Button` and
`Widgets.Button` both wanting `VButtonNode` in the same namespace. Emitting both would be a duplicate
type; emitting one would be a silent choice between two controls. So neither real node class is
emitted, and one placeholder type carries the name and the same attribute, so `new VButtonNode()`
reports VD014 with both controls named and the registration line that separates them.

This replaces the old VD003 "two registrations produce the same node name" error, which reported the
collision the moment the classes were declared rather than when the name was used. A genuine
duplicate registration - the *same* control registered twice - is still VD004.

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
dependency is real and is pinned: a test compiles the emitted accessor call against a real extension
block and fails if the lowering changes, so the day it moves it is a test failure rather than a
generated node that silently sets nothing.

## 9. No registry of node types

Nothing in this change creates a table of node types. Node classes are reachable only by
construction, so an application that never writes `new VLabel { ... }` does not retain `VLabel`, and
NativeAOT removes it. A `NodeType[]` registry would root every generated class and destroy exactly
that property - which is what makes a future `[VirtualDomAll]` affordable at all, since generating a
node for every available control is only acceptable if the unused ones are removed.

## 10. Cost

A generator change that only affects generated code can be free: the generator runs at build time and
ships as an analyzer, so it is not in the published image, and the generated members cost only what
their use costs. This change is verified to be that case rather than assumed to be - `Kolvir.Minimal`
does not adopt the virtual DOM at all, so its NativeAOT image is the measurement of "no cost", at the
512-byte resolution `FEATURE-COST-POLICY.md` section 8 gives, from build roots of equal length.

The scan's per-edit cost is reported in the pull request as the narrowed walk over this repository's
own compilations, and the generator's total time is reported against the same build.

## 11. Out of scope

`[VirtualDomAll]` - a node for every control the compilation can see - is the next layer and is not
built here. It needs this scan, which is why the scan is factored as a reusable unit, but its sweep,
its silent-skip policy and its own placeholder types are not part of this change.
