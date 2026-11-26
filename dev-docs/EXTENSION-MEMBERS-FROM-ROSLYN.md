# C# 14 extension members from an incremental generator: what Roslyn reports, and what a scan costs

The virtual DOM generator reads a control's own and inherited instance settable properties and its
`AddXxxHandler`/`RemoveXxxHandler` pairs, and emits one VNode property per member. The owner wants the
same for **C# 14 extension members** that apply to the control: the properties in
`src/core/Kolvir/Controls/TableLayoutPanelExtensions.cs` (`extension(Control control) { public int
TableColumn { get; set; } }`) as settable VNode properties, and extension-method handler pairs as VNode
events. This document is the spike that decides whether that is possible at all, and what it costs.

**Answer up front: yes, the symbols carry everything the feature needs, but not through the API surface
the generator currently pins.** Roslyn 4.14.0 already models an `extension(...)` block as an
`INamedTypeSymbol` with `TypeKind.Extension`, and its members come back from `GetMembers()` on that
symbol with their declared accessibility - but 4.14 has no API for the *receiver*, gives a different
answer for the same assembly than Roslyn 5.x does, and cannot see block properties at all when they
arrive from metadata. Roslyn 5.x (what actually runs the generator) has `INamedTypeSymbol.IsExtension`,
`INamedTypeSymbol.ExtensionParameter` and friends, and reads metadata correctly. The feature is
buildable; the prerequisite is moving the generator's `Microsoft.CodeAnalysis.CSharp` pin from 4.14.0 to
a 5.x package, and the scan needs narrowing: walking every type costs about 20 ms and 4 MB per
compilation, where the narrowed walk costs about 1 ms.

Everything below was measured, not recalled. Section 8 has the harness.

## 1. The questions, answered

### 1.1 Does `INamedTypeSymbol.GetMembers()` on the static class return the members declared in an `extension(...)` block?

No - and yes, one level down. `GetMembers()` on `TableLayoutPanelExtensions` returns the **block itself**
as an `INamedTypeSymbol` member; the block's own `GetMembers()` returns the extension members.

Observed on `Kolvir.TableLayoutPanelExtensions`, source symbols, Roslyn 4.14.0 and 5.9.0 alike:

```
NamedType '' acc=Public TypeKind=Extension IsStatic=False containing=Kolvir.TableLayoutPanelExtensions
    Property 'TableColumn' containing=Kolvir.TableLayoutPanelExtensions.extension(Kolvir.Control)
             acc=Public IsStatic=False Type=int Get=Public(PropertyGet) Set=Public(PropertySet)
    Method 'get_TableColumn' MethodKind=PropertyGet IsStatic=False IsExtensionMethod=False
             ReceiverType=Kolvir.TableLayoutPanelExtensions.extension(Kolvir.Control) Params=[]
    Method 'set_TableColumn' MethodKind=PropertySet IsStatic=False IsExtensionMethod=False
             Params=[int value]
    ...
```

- The block symbol's `Name` is the **empty string**; `ToDisplayString()` is
  `Kolvir.TableLayoutPanelExtensions.extension(Kolvir.Control)`. `GetTypeMembers()` also returns it, so
  `GetMembers()` and `GetTypeMembers()` agree.
- Declared members report `ContainingType` = the block, `DeclaredAccessibility` = what was written
  (`Public`), `IsStatic` = false, and for accessors `MethodKind` = `PropertyGet`/`PropertySet`.
- `MethodKind` for a method declared in a block is `Ordinary`; `IsExtensionMethod` is **false** on the
  block's own copy of it (see 1.6).

The static class's `GetMembers()` therefore returns the block, not `TableColumn`. A scan has to look one
level down - and, as section 2 shows, that level is not always where the properties are.

### 1.2 Is there a dedicated API, or only `GetMembers()` plus pattern matching?

Both, depending on the Roslyn version, and the pin is on the wrong side of the split.

| API | 4.14.0 (the pin) | 5.9.0 (SDK 10.0.403, the generator's runtime) |
| --- | --- | --- |
| `INamedTypeSymbol.MightContainExtensionMethods` | yes | yes |
| `TypeKind.Extension` | yes | yes |
| `INamedTypeSymbol.IsExtension` | **no** | yes |
| `INamedTypeSymbol.ExtensionParameter` (`IParameterSymbol`) | **no** | yes |
| `INamedTypeSymbol.ExtensionGroupingName` / `ExtensionMarkerName` | **no** | yes |
| `IMethodSymbol.ReduceExtensionMember` / `AssociatedExtensionImplementation` | **no** | yes |
| `IPropertySymbol.ReduceExtensionMember` | **no** | yes |
| block syntax node | `ExtensionDeclarationSyntax` | `ExtensionBlockDeclarationSyntax` |

So under 5.x there is a first-class API; under 4.14 the answer is `GetMembers()` plus
`member is INamedTypeSymbol { TypeKind: TypeKind.Extension }`. The pin decides which one the generator
can compile against.

`INamedTypeSymbol.MightContainExtensionMethods` is not the answer to anything here: it describes a class
that declares classic `this`-parameter extension methods, and is true/false independently of whether it
has a block.

### 1.3 How do you obtain the receiver type?

Three routes exist; only one is both symbolic and version-proof.

1. **`INamedTypeSymbol.ExtensionParameter.Type`** on the block (5.x). Verified
   `ExtensionParameter = Kolvir.Control` for both source and metadata symbols. This is the intended API.
2. **`Parameters[0].Type` of the flattened static accessor on the containing class** - works in every
   combination tested (4.14 and 5.9, source and metadata). `get_TableColumn(Control)` and
   `set_TableColumn(Control, int)` are ordinary static methods on `TableLayoutPanelExtensions`.
3. Parsing the last segment of `block.ToDisplayString()`, which is `extension(Kolvir.Control)`. Works
   everywhere but is a string, not a symbol.

Under 4.14 there is a fourth, 4.14-only route - a private `'<Extension>$'` method inside the block whose
one parameter is the receiver - and it is a trap: **Roslyn 5.9 does not emit or report that member at
all.** A generator compiled against 4.14 that reaches for `<Extension>$` would find nothing when the
same code runs inside the SDK 10 compiler. That single difference is why the pin, not the algorithm, is
the problem.

Also a trap on both versions: `IMethodSymbol.ReceiverType` on a member **inside** the block is the
*block type* (`...extension(Kolvir.Control)`), not `Kolvir.Control`. `ReceiverType` is only the receiver
for the flattened method on the class.

### 1.4 Are extension-block members distinguishable from ordinary static members of the same class?

The block's members are not members of the class at all, so they are cleanly distinguishable: they are
children of a `TypeKind.Extension` symbol, they are `IsStatic == false`, and their `ContainingType` is
the block.

The **flattened copies** on the class are not distinguishable by kind. Each extension property also
appears as two ordinary static methods, `get_X` and `set_X`, with `MethodKind == Ordinary`,
`DeclaredAccessibility == Public`, and `IsExtensionMethod == false`:

```
Method 'get_TableColumn' MethodKind=Ordinary IsStatic=True IsExtensionMethod=False Params=[Kolvir.Control]
Method 'set_TableColumn' MethodKind=Ordinary IsStatic=True IsExtensionMethod=False Params=[Kolvir.Control, int]
```

A scan that walks static methods and trusts names will pick these up as if they were helpers. They are
only identifiable as accessors by knowing the block exists. (For extension *methods* the flattened copy
does carry `IsExtensionMethod == true`; see 1.6.)

### 1.5 Does `nameof(SomeStaticClass.SomeExtensionProperty)` compile?

**No**, in either Roslyn version, source or metadata:

- source symbols, `nameof(Kolvir.TableLayoutPanelExtensions.TableColumn)` - CS9286, "does not contain a
  definition for 'TableColumn' and no accessible extension member ... for receiver of type
  'TableLayoutPanelExtensions'";
- metadata symbols - CS0117 "does not contain a definition for 'TableColumn'" (4.14) or CS1929 naming
  the block as an extension method overload (5.9).

And it fails inside an attribute argument too, which is what matters here: a
`[assembly: VNodeOverride(typeof(TableLayoutPanel), nameof(TableLayoutPanelExtensions.TableColumn))]`
would not compile.

What does compile: `nameof` of an extension **method**, including one declared inside a block, because
that method really is a member of the class. `nameof(Kolvir.Undo.TextBoxUndoExtensions.Undo)` and
`nameof(Kolvir.PopupExtensions.AddPopupClosedHandler)` both bind. So an override attribute can name a
handler pair that way, but a property only as a string - or, better, keyed by the receiver type plus the
member name, which needs no `nameof` at all.

### 1.6 Is `IMethodSymbol.IsExtensionMethod` + `Parameters[0].Type` still valid for extension methods?

Yes, on both versions and both symbol sources, for classic extension methods and for extension methods
declared inside a block. The block's own copy has `IsExtensionMethod == false`, but the flattened static
method on the class has it true and carries the receiver as parameter 0:

```
Method 'AddUndoStateChangedHandler' MethodKind=Ordinary IsStatic=True IsExtensionMethod=True
       Params=[Kolvir.TextBox textBox, System.Action<object> handler]
Method 'AddPopupClosedHandler'      MethodKind=Ordinary IsStatic=True IsExtensionMethod=True
       Params=[Kolvir.Control content, System.Action<object, Kolvir.PopupResult> handler]
```

This route is worth preferring over block enumeration for handler pairs: it finds both classic and
in-block methods, in every Roslyn version, in source and in metadata.

## 2. Source symbols and metadata symbols are not the same thing

This is the part that decides the plan, so it is worth stating exactly.

`TableLayoutPanelExtensions` as **source** (a compilation over `src/core/Kolvir/*.cs`, 167 files): the
block hangs off the static class, and its 4 properties are there.

`TableLayoutPanelExtensions` as **metadata** (the built `Kolvir.dll`, which is what an application's
compilation actually holds), read by the two Roslyn versions:

| | Roslyn 4.14.0 reading 5.9-built `Kolvir.dll` | Roslyn 5.9.0 reading the same file |
| --- | --- | --- |
| `GetTypeMembers()` of the class | `TableCellInfo`, **`'<G>$6C46B128...'` (a `TypeKind.Class`)** | `TableCellInfo`, **`''` (`TypeKind.Extension`)** |
| where the block is | nested *inside* the `<G>$...` class, named `'<M>$EBB6B3...'` | a direct member of the class |
| block's properties | **absent** | 4 properties present, as in source |
| block's members | only `'<Extension>$'` | `get_/set_` accessors + the 4 properties |
| receiver | only via `<Extension>$.Parameters[0]` or the mangled display string | `ExtensionParameter.Type` = `Kolvir.Control` |

Two consequences:

- Under 4.14, an extension property's name, type, accessibility and settability are **not reachable at
  all** through the block. The flattened `get_`/`set_` accessors on the class are the only way in, and
  they cannot be told apart from ordinary statics except by the naming convention and by the fact that
  a block exists somewhere in the class.
- Under 5.9, metadata and source agree, and `ExtensionParameter` gives the receiver directly. Since the
  generator runs inside the SDK's compiler whenever an application is built, 5.9 (or whatever the
  consumer ships) is what the feature will see in practice. 4.14 is only the compile-time surface.

The pin, therefore, decides whether the generator can be *written* against the API that works.

## 3. What a scan over the real assembly finds

Scanned: `Kolvir.dll` alone, and the whole closure of `Kolvir.Demo.VirtualDom` plus
`Kolvir.Feature.DragDrop` and `Kolvir.Feature.EffectiveInView` (187 references), for extension members
whose receiver is `Control` or a base of the registered control.

For a `TableLayoutPanel` node (`TableLayoutPanel : Panel : Control`) the scan surfaces exactly:

| member | kind | type / handler | receiver | source |
| --- | --- | --- | --- | --- |
| `TableColumn` | settable extension property | `int`, public get + set | `Control` | `TableLayoutPanelExtensions` |
| `TableRow` | settable extension property | `int`, public get + set | `Control` | `TableLayoutPanelExtensions` |
| `TableColumnSpan` | settable extension property | `int`, public get + set | `Control` | `TableLayoutPanelExtensions` |
| `TableRowSpan` | settable extension property | `int`, public get + set | `Control` | `TableLayoutPanelExtensions` |
| `PopupClosed` | handler pair (classic extension methods) | `Action<object, PopupResult>` | `Control` | `PopupExtensions` |

None of those five names collides with a member of `TableLayoutPanel`, `Panel` or `Control`, or with the
node base class's reserved names.

With the feature packages referenced, the scan picks up seven more members, all receiver `Control`:

| member | kind | type / handler | source |
| --- | --- | --- | --- |
| `AllowDrop` | settable extension property | `bool`, public get + set | `Kolvir.Feature.DragDrop` |
| `DragEnter` / `DragOver` / `DragDrop` | handler pairs | `Action<object, DragEventArgs>` | `Kolvir.Feature.DragDrop` |
| `DragLeave` | handler pair | `Action<object>` | `Kolvir.Feature.DragDrop` |
| `EffectiveInView` | **get-only** extension property | `bool` | `Kolvir.Feature.EffectiveInView` |
| `EffectiveInViewChanged` | handler pair | `Action<object>` | `Kolvir.Feature.EffectiveInView` |

`EffectiveInView` is the case that forces a filter: a scan must require a public setter, exactly as the
native property walk does, or it would emit a VNode property that cannot be assigned.

For a registered `TextBox` node the same scan surfaces 15 extension properties (the table cell set does
not apply; `AutoCompleteMode`, `AutoCompleteSource`, `AutoCompleteCustomSource`, `Multiline`, `WordWrap`,
`VerticalScrollBar`, `HorizontalScrollBar`, `VerticalOffset`, `HorizontalOffset`, `Spans`, `UndoEnabled`,
`CanUndo`, `CanRedo`, `UndoCount`, `RedoCount`) and 4 methods (`Undo`, `Redo`, `ClearUndoStack`, and the
`UndoStateChanged` handler pair), plus the `ContextMenuSpanExtensions.Span` property on
`ContextMenuEventArgs`.

Totals over `Kolvir.dll`: 6 extension blocks, 19 block properties, 3 non-handler extension methods, and
2 handler pairs found through `IsExtensionMethod` (`PopupClosed`, `UndoStateChanged`). Over the whole
closure with both feature packages: 21 block properties, 12 `Add`/`Remove` methods, 7 handler pairs.

## 4. Cost

Measured on the closure described above (187 references), Roslyn 5.9, best of 25 passes after a cold
pass, one walk per pass; the same shapes measured within noise of this on 4.14.

| scan | types visited | cold | warm | allocated per pass |
| --- | --- | --- | --- | --- |
| every type in `compilation.GlobalNamespace` | 20,592 | 27.6 ms | **21.8 ms** | 4.19 MB |
| every static class, then its blocks | 1,828 static classes | 33.3 ms | 24.3 ms | 4.89 MB |
| every type, with `GetMembers()` materialised | 20,592 | 40.7 ms | 21.8 ms | 6.59 MB |
| `Kolvir` assembly only | 361 | 0.5 ms | **0.28 ms** | 80 KB |
| `Kolvir` static classes only | 33 | 0.9 ms | 0.31 ms | 91 KB |
| assemblies that reference `Kolvir` + `Kolvir` | 607 in 7 assemblies | - | **0.97 ms** (single pass) | - |

Notes on the numbers:

- The compilation-wide walk is dominated by the 176 framework assemblies, not by the application or by
  Kolvir: 20,592 types, of which 361 are Kolvir's.
- Filtering to static classes does not help. It is the same walk; `IsStatic` is a property read, and
  `GetMembers()` on 1,828 classes costs more than the cheap check saves.
- **Caching per compilation does not help.** An edit produces a new `Compilation`, and a provider derived
  from `context.CompilationProvider` re-runs for it, so a 20 ms walk is 20 ms per keystroke in the IDE.
- Narrowing to the assemblies that can possibly declare a receiver assignable from a Kolvir control -
  those that reference `Kolvir`'s assembly, plus `Kolvir` and the source module - is 0.97 ms for a
  single enumeration, around 20x cheaper than the full walk and 3 orders of magnitude cheaper in
  allocations. In this closure that is 7 assemblies out of 186, and it is not a hack: an extension
  member whose receiver is a Kolvir type has to live in an assembly that references Kolvir. The one
  thing the filter would miss is a receiver declared as a framework type (`object`, a BCL interface) in
  an assembly that does not reference Kolvir; that is a case to accept or to widen for deliberately.
- The `Kolvir`-only number is the relevant one when the generator runs inside `src/core/Kolvir`'s own
  compilation (source symbols): 361 types, 0.28 ms.

If the owner's acceptance of "a compilation-wide scan" was based on the idea that it is a one-off, the
per-keystroke number is the one to quote back: 21.8 ms and 4.19 MB per edit, against 0.97 ms narrowed.

## 5. What the generated code can write

The VNode's `ApplyTo` has to assign the extension property on the control. What compiles, measured with
the `Kolvir.dll` metadata reference and a snippet per row:

| generated statement | C# 13 | C# 14 (preview) |
| --- | --- | --- |
| `c.TableColumn = 3;` with `using Kolvir;` | CS9260 (feature not available), or CS1061 under a 4.14 reader | **compiles** |
| `c.TableColumn = 3;` fully-qualified receiver, no `using` | CS1061 | CS1061 |
| `TableLayoutPanelExtensions.set_TableColumn(c, 3);` | **compiles** | **compiles** |
| `c.AddPopupClosedHandler(h);` | compiles | compiles |
| `PopupExtensions.AddPopupClosedHandler(c, h);` | compiles | compiles |

The accessor-by-name call is the important one: `set_TableColumn` is an ordinary public static method on
the class, so calling it directly is legal in **every** combination tested - C# 13 or 14, 4.14 or 5.9
reader, with or without the namespace imported. That gives the generator an emission route that does not
depend on the consumer's language version:

- extension **properties**: emit `global::<containing class>.set_X(control, value)` and
  `global::<containing class>.get_X(control)`; never emit `control.X = value`, which needs C# 14 and the
  extension's namespace in scope;
- extension **handler pairs**: emit `global::<class>.AddXHandler(control, h)` and the `Remove` twin,
  which are ordinary static calls and were always safe.

This also means the feature does not have to raise the consumer's language version: an application on
C# 13 gets the generated properties, it just cannot write `control.TableColumn` itself.

## 6. What this means for the design

1. **Bump the generator's `Microsoft.CodeAnalysis.CSharp` pin from 4.14.0 to a 5.x package.** 5.0.0
   restores and compiles for `netstandard2.0`, and exposes `IsExtension`, `ExtensionParameter`,
   `ExtensionMarkerName` (verified by building against it). Without the bump the receiver has to come
   from the flattened accessors, which is doable but is a name-convention heuristic and is blind to
   block properties in metadata under 4.14. The cost of the bump: in a host older than Roslyn 5 (an
   SDK 9 compiler, say) the 5.x members do not exist on the loaded symbol types, so the scan would throw
   where it calls them and the generator's output would be reported as a failed analyzer rather than
   generated. That is acceptable for a feature only C# 14 consumers can use, but it should be a stated
   minimum rather than a surprise.
2. **Scan twice, once per kind, and prefer the flattened methods for handler pairs.** Properties need
   the block (`IsExtension` + `ExtensionParameter` + `GetMembers().OfType<IPropertySymbol>()` with a
   public getter and setter). Handler pairs do not, and the `IsExtensionMethod` + `Parameters[0]` route
   finds both classic and in-block pairs on both versions.
3. **Narrow the scan to assemblies that reference the control's assembly**, plus the source module. That
   is 0.97 ms instead of 21.8 ms, and it is where the members necessarily live.
4. **The override attribute cannot use `nameof` for a property.** Key it by receiver type plus member
   name, or by the containing static class plus a string - `nameof` only binds for methods.
5. **Deduplicate against native members and between blocks.** Two blocks on the same receiver can both
   declare `Foo`; the existing `taken` dictionary and the reserved-name check in `ReadProperties` apply
   unchanged. The member a VNode exposes should be decided in the same order the native walk uses
   (derived before base, properties before handler pairs).
6. **Filter on a public setter**, or `EffectiveInView` (get-only) becomes a VNode property that cannot be
   assigned.

## 7. Verdict

The owner's plan is buildable, but not as it stands in two respects. Roslyn does expose C# 14 extension
blocks through the symbol model - a block is an `INamedTypeSymbol` with `TypeKind.Extension`, its
members are reachable through `GetMembers()` with correct names, types and accessibility, and the
receiver is available - so the feature is not blocked on missing APIs. What is blocked is the 4.14 pin:
under 4.14 there is no receiver API, the block's members differ in shape between its source view and its
metadata view (`<G>$...`/`<Extension>$` - a shape 5.x does not produce at all), and block **properties**
are invisible in metadata, which is exactly the case an application's compilation presents. Moving the
pin to 5.x makes both halves straightforward: properties from `ExtensionParameter`-qualified blocks,
handler pairs from `IsExtensionMethod` + `Parameters[0]`, emitted as calls to the flattened static
accessors so that the generated code compiles in any language version. The remaining design work is
cost and naming rather than possibility: a compilation-wide scan costs about 21.8 ms and 4.19 MB per
edit and caching cannot help, so the scan should be narrowed to assemblies that reference the control's
assembly (0.97 ms measured); and the override attribute cannot `nameof` an extension property, so it has
to identify the member by receiver type and name instead of by symbol name. The expected output for the
first control to demonstrate it is four settable `int` properties on `VTableLayoutPanel`
(`TableColumn`, `TableRow`, `TableColumnSpan`, `TableRowSpan`) and one event (`PopupClosed`), with
`AllowDrop`, the four drag events and `EffectiveInViewChanged` appearing as soon as an application
references those feature packages.

## 8. Reproducing

The harness was a temporary console application (net10.0, `Microsoft.CodeAnalysis.CSharp` 4.14.0) in the
session's temp directory; it is not committed. A second copy referenced the SDK's own compiler assemblies
(`C:\Program Files\dotnet\sdk\10.0.403\Roslyn\bincore\Microsoft.CodeAnalysis.CSharp.dll`, 5.9.0.0) so the
same code could be run against the version the generator actually loads. Both read:

- **source symbols**: a `CSharpCompilation` over every `.cs` under `src/core/Kolvir` (167 files, plus a
  tree of `global using` directives, `LanguageVersion.Preview`), referenced against the framework
  assemblies present in `TRUSTED_PLATFORM_ASSEMBLIES` plus `SkiaSharp` and
  `Microsoft.Extensions.DependencyInjection`;
- **metadata symbols**: a `CSharpCompilation` with one empty tree and the runtime closure of
  `src/demo/Kolvir.Demo.VirtualDom/bin/Release/net10.0-windows` plus the built
  `Kolvir.Feature.DragDrop` and `Kolvir.Feature.EffectiveInView`.

The shape assertions worth pinning in a test once the feature is built, all cheap to express against a
compilation built by hand as the existing generator suite already does:

```
var block = extensions.GetMembers().OfType<INamedTypeSymbol>().Single(t => t.IsExtension);
block.ExtensionParameter.Type            // Kolvir.Control
block.GetMembers().OfType<IPropertySymbol>().Select(p => (p.Name, p.Type.ToDisplayString(), p.SetMethod is not null))
                                         // ("TableColumn","int",true) ... 4 rows
extensions.GetMembers().OfType<IMethodSymbol>()
          .Where(m => m.IsExtensionMethod && m.Name.EndsWith("Handler"))
                                         // AddPopupClosedHandler / RemovePopupClosedHandler, Parameters[0].Type == Control
```

and the negative one, which is the reason for the whole document: `nameof` of an extension property must
keep failing, so no attribute design may depend on it.

## 9. Outcome

The conclusions above are what the feature was built on. The pin moved to **5.9.0** rather than to
any 5.x: it is what SDK 10.0.403 ships, and an analyzer that referenced a newer Roslyn than the host
compiler would fail to load. The implementation, the rules it applies when two members want one name,
and the measured cost are recorded in `EXTENSION-MEMBERS-ON-VNODES.md`; the shape assertions in
section 8 of that list are now tests, so this document stays the record of what was measured rather
than something the code has to be re-validated against by hand.
