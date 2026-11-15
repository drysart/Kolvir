# Virtual DOM - design and implementation plan

An optional, React-shaped architecture for Kolvir applications: a view model component has a
`Render()` method that returns a description of a control tree - a *virtual* tree of plain data -
which a reconciler diffs into the real `Control` tree. The application never builds controls by hand:
it states what the UI should be for its current state and the reconciler works out what to change.

The whole design is shaped by one constraint: **it must survive trimming and NativeAOT**. A virtual
DOM is conventionally built out of reflection - strings naming types, dictionaries of property
setters, `Activator.CreateInstance` - and every one of those is precisely what the trimmer cannot see.
The design below keeps reflection out of the compile-time path entirely, so a virtual DOM application
publishes exactly like any other Kolvir application.

Status of this document: living plan. Each step is independently buildable and verifiable, so work
can stop after any step with the tree in a working state. Decisions are recorded in section 3 with
their rationale; the steps in section 8 are the order of work; section 10 lists what is still open.

## 1. Goals and non-goals

Goals:

- A view model component whose `Render()` returns a description of its UI, and a reconciler that
  turns that description into real controls and then patches them when a later `Render()` differs.
- **Terse, type-safe syntax for building the description.** No `createElement`-style verbosity, no
  reflection, no strings for property names, and compiler errors (not run-time surprises) for
  mistakes. Detailed in section 4.
- AOT- and trim-clean by construction, on the same terms as the rest of the library.
- No cost whatsoever to an application that does not use it: it is a `Kolvir.Feature.*` package
  (rung 3 of `FEATURE-COST-POLICY.md`) and the base `Kolvir` assemblies are untouched.

Non-goals, for this plan:

- A markup language. The design deliberately stays in C#; a XAML/JSX-like front end is a *later,
  additive* option that would lower to the same node tree (section 9).
- Two-way data binding. State lives in the component, `Render()` produces a tree, and events call
  back into the component. Binding expressions are a possible later layer, not a foundation.
- A general-purpose serialization format for UI. The node tree is an in-memory description, not a
  document format to load from disk.
- Animations, transitions, or layout ownership. Layout remains the layout system's business.
- Replacing the imperative control API. Every existing application keeps working unchanged.

## 2. The AOT constraint, and what it forces

This section is the reasoning behind most of section 3, so it comes first.

**The trimmer cannot be told anything at run time.** A source of roots that is *shaped* at run time
therefore cannot exist. Specifically:

- `[DynamicDependency]` is evaluated at compile time against constant arguments. There is no
  runtime equivalent; a call like `Register(typeof(Button))` cannot add roots, no matter how it is
  written.
- `[DynamicallyAccessedMembers]` *is* runtime-shaped but compile-time-checked: the trimmer propagates
  the requirement from the annotation through every call site. `void Register([DynamicallyAccessedMembers(...)] Type t)`
  called with a literal `typeof(Button)` roots `Button`'s members and produces an `IL20xx` warning
  (rather than silent breakage) if the argument cannot be seen statically.
- Reflection over a `Type` obtained at run time is *possible* under NativeAOT, but only for members
  the trimmer kept, and the keeping is what the annotation above arranges.

So there are two distinct problems hiding behind "is this AOT-safe", and they have different answers:

| Question | Answer |
| --- | --- |
| Can a runtime registration call tell the trimmer to keep a control's properties? | No. Nothing at run time can. |
| Can a registration API nevertheless be *safe* under AOT? | Yes, if the type is visible at the call site, through `[DynamicallyAccessedMembers]`. |
| Do we need it at all? | No, for the primary path. A source generator sees the type statically and emits direct property assignments, which root the setters with no annotations and no reflection. |

The last row is the design: **the registration is a declaration the compiler consumes, not a call the
runtime interprets.** That is the difference between the design we rejected (section 9) and the one
adopted.

One more consequence worth stating plainly: a registered control is *fully* retained - its constructor
is called and each generated assignment references a setter - so the trimmer removes nothing of it.
That is the intended cost, it is opt-in per type, and it is exactly what "register a control" means.
A control registered in a manifest and never used in any `Render()` is still retained; that is why the
manifest is expected to be short and per application rather than a library-wide list.

## 3. Design decisions

| Topic | Decision | Rationale |
| --- | --- | --- |
| Rung | Separate package, `Kolvir.Feature.VirtualDom`, namespace `Kolvir.VirtualDom` | Rung 3 of `FEATURE-COST-POLICY.md`. The base deliverable must not contain a UI framework that most applications will not use, and a feature that adds members to core types cannot take a lower rung anyway. |
| Project layout | The four-folder rung-3 layout, all code in `common/`, with empty `windows/`, `macos/`, `linux/` folders | Reconciling a tree is portable arithmetic over object graphs. The folders stay because the package's `lib/` folders and dependency groups come from those same inner builds; a single-target exception would fight `check-package-layout.py` for no gain. |
| Generator packaging | A second assembly, `Kolvir.Feature.VirtualDom.Generators`, referenced `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` | The generator is build-time only. It must not appear in the deliverable, and it targets `netstandard2.0` because analyzers do. |
| How a control becomes useable from a tree | A **manifest**: a `partial` class carrying one `[VirtualDom(typeof(X))]` attribute per control | Compile-time, so the generator sees the exact type and can emit direct code. This is the whole reason the design is AOT-clean. Rejected: a runtime `Register<T>()` call, whose effects no generator can see and whose types the trimmer cannot learn about. |
| Attribute shape | `[AttributeUsage(Class, AllowMultiple = true)]`, ctor takes the single `Type`, other options as settable properties (`Name`) | Follows `System.Text.Json`'s `[JsonSerializable]`: one attribute instance is a per-type configuration record, so future options (`Name`, `IgnoreProperties`, a custom binder) have somewhere to live without reshaping the API or adding overloads. |
| Manifest is `partial` | Yes | It is the generator's write target. Today it holds only the factories and binders; later a generated runtime table or registration hook lands there without a base class or a second attribute. |
| Where the generated factories live | A **`V` static class nested inside the manifest**, so `using static MyApp.V;` gives bare `Label(...)`, `Button(...)` | This is the syntax win (section 4). Nesting also solves the multi-assembly problem: `MyApp.V` and `Plugins.V` cannot collide, whereas a flat `class V` per assembly would make every cross-assembly reference ambiguous. |
| What a factory does | Emits direct assignments into the node's property list - one per declared parameter - never reflection | The setters are rooted by the references themselves, so no annotation is needed anywhere on this path, and the trimmer's view is exactly the properties the developer actually registered. |
| Binder | A generated per-control `Set(Control, string name, object? value) : bool` alongside the factory | The reconciler needs to patch an existing control from a changed node without knowing its type. A generated `switch` over names is the reflective binder's job done statically, and it doubles as the drift test hook (it returns `false` for an unknown name, so a test can assert every public settable property is handled). |
| What is diffed | The **previous node against the next node**, never the node against the control | Halves the problem: the binder needs setters, because old values come from the tree we already hold. Nothing reads a control's current property state back out, which keeps `Control` free of any virtual-DOM awareness and avoids the hardest reflective cases (getter-only and private-setter properties, boxing on read of value types). The one exception is the pristine snapshot below, which is read once per type rather than per diff. |
| Optional property arguments | Every factory parameter is a `Specified<T>?`: `null` is "not in this node", an implicit conversion from `T` is "set to this", and `Specified<T>.Reset` is an explicit "put it back" | A plain `T?` cannot express three states, and two of them are needed: `Font`, `Color` and `ContextMenu` all have a meaningful `null`, so "no font" has to be distinguishable from "this node says nothing about the font". The wrapper never reaches the node - the factory unwraps it and stores the plain `T`, so no `Specified<T>` is ever boxed. |
| What "default" means | Not `default(T)`: the **pristine value**, snapshotted per property from a freshly constructed control | `Label.Text` starts at `""`, `Control.Name` at the type name, and a control's constructor may set `Dock = Fill` while the declared default is `None`. `default(T)` would be wrong for most properties, so each `NodeType` records what its control looked like before any node touched it - which needs a generated getter alongside the setter, read once when the first control of that type is created. |
| Node type | One non-generic `VNode` class holding a generated `NodeType` (creator + binder + CLR type), a `VProp[]` of name/value pairs, and a `VNode[]` of children | Keeps the reconciler non-generic: no generic dispatch, no variance pain in child lists. Compile-time type safety lives entirely in the generated factory's signature, which is where a developer actually types the code. |
| Property names | Interned string literals emitted by the generator, not an enum or an index | A literal is interned, so a name costs no allocation; the binder's string `switch` compiles to a hashed dispatch. An enum would put a build-time ordering into a public API for no measurable gain. |
| `Key` | On the `VNode` base, so every element can be identified in a list | Requirement of keyed reconciliation (section 6). On the base type it needs no per-type support. |
| Events | Generated parameters map to Kolvir's `AddXxxHandler`/`RemoveXxxHandler` method pairs, not to `+=` | Kolvir exposes handlers as method pairs rather than `event` members (`FEATURE-COST-POLICY.md` 5.4). The generator knows the pairs, so an event property is a named parameter with a delegate value; the reconciler keeps the attached delegate so it can detach the old one and attach the new one across renders. |
| Control creation | Generated `Func<Control>` per type, emitted in the binder's `NodeType` | `Activator.CreateInstance` is the classic trimming trap. A generated `new Label()` also makes a missing parameterless constructor a build error rather than a run-time one. |
| Reordering children | `ControlCollection.Move`, added to core for this | Added to `Kolvir` on rung 1 rather than worked around: `Remove` + `Insert` detaches and re-attaches the child, which re-reads enabled/displayed/appearance state for its subtree and re-runs `OnControlAdded`, so a keyed reorder would disturb exactly the state it exists to preserve. A general reorder primitive is worth having on its own terms and costs nothing to an application that does not call it. |
| Reflective escape hatch | Available, but explicitly not the default: an annotated `Type`-taking registration, and unannotated overloads marked `[RequiresUnreferencedCode]` | The only case that genuinely needs it is a type supplied at run time - a plugin, or a control set read from configuration. Marking the unannotated overloads means an AOT build gets a warning instead of a post-trim crash. |
| Renders are event-driven | `SetState` renders synchronously; no per-frame rendering, no scheduled batching in the first cut | Batching is an optimisation for a problem that does not exist yet in a UI driven by clicks and messages, and it needs a scheduling seam (`FEATURE-COST-POLICY.md` 6.2) that this feature is not entitled to invent. Section 10 lists coalescing as the follow-up. |
| Markup front end | Not in this plan | See section 9. Deciding it early would constrain the node model for no benefit; the node model is the stable ABI, and a markup generator can be added later behind it. |

## 4. What a developer writes

The manifest, once per application (or per library that contributes controls):

```csharp
using Kolvir.VirtualDom;

[VirtualDom(typeof(StackPanel))]
[VirtualDom(typeof(Label))]
[VirtualDom(typeof(Button))]
[VirtualDom(typeof(RichLabel), Name = "RichText")]   // two label-ish types, distinct names
partial class AppControls;
```

A component:

```csharp
using static AppControls.V;

sealed class ChatPanel : Component
{
    readonly string _title;
    readonly IReadOnlyList<Message> _messages;

    public ChatPanel(string title, IReadOnlyList<Message> messages)
    {
        _title = title;
        _messages = messages;
    }

    public override VNode Render() => Column(
        gap: 8,
        children: [
            RichText(text: _title, font: bold),
            .._messages.Select(m => Label(key: m.Id, text: m.Text)),
            Button(text: "Send", onClick: SendPressed),
        ]);
}
```

Three things make that readable, and each is a decision rather than an accident:

1. **Bare names.** `using static AppControls.V;` imports the generated factories, so elements read as
   names rather than `V.Label(...)` or `VNode.Of<Label>(...)`. This is the closest plain C# gets to a
   markup syntax, and it is what makes a large tree bearable.
2. **Collection expressions for children.** `children: [a, b, ..more]` gives nesting without wrapping
   calls, plus LINQ spread for lists. `params`-compatible overloads cover the common case.
3. **Named arguments for properties.** The generator emits one parameter per public settable property,
   so `text:`, `font:`, `onClick:` are checked at compile time, renamed with the property, and
   completed by IntelliSense. Setting a property is a visible, named thing - nothing hidden in a
   string.
4. **Optional arguments that stay clean.** A parameter is `Specified<T>?` with an implicit conversion
   from `T`, so `Label(text: "hi")` reads exactly as it would with a plain `string?` parameter while
   still distinguishing three cases: not passed at all, passed a value, and deliberately reset
   (`Label(font: Specified<Font?>.Reset)`). The last is needed because `Font`, `Color` and
   `ContextMenu` all have a meaningful `null`, so "no font" and "says nothing about the font" are
   different statements (section 6).

The trade-off this buys: the factories are a generated surface, so their shape follows the controls
rather than being hand-designed. That is the same trade `System.Text.Json`'s source generator makes,
and the same one `FEATURE-COST-POLICY.md` 5.1 argues for - generate the shape instead of reflecting
at run time.

## 5. What the generator emits

For each manifest, three things, all in the manifest's own namespace, all `internal` except the
factories:

```csharp
partial class AppControls
{
    internal static class V
    {
        public static VNode Label(
            Specified<string>? text = null,
            Specified<Font?>? font = null,
            ...) => ...;

        public static VNode Button(
            Specified<string>? text = null,
            Specified<Action<object>?>? onClick = null,
            ...) => ...;
    }
}
```

- **`V` (public, nested)** - one factory per registered control. Each allocates a `VNode` with a
  `VProp[]` sized to the number of specified arguments, so an unset property costs nothing. A
  parameter is `Specified<T>?`, and the factory stores the unwrapped `T`, so the wrapper exists only
  at the call site.
- **`NodeType` singletons (internal)** - one per control: the `Type`, a `Func<Control>` creator, the
  binder, and the pristine values captured from a freshly constructed control. `VNode.NodeType` is
  how the reconciler answers "can I reuse this control?" in one reference comparison.
- **Binders (internal)** - `bool Set(Control c, string name, object? value)` with one `case` per
  property, each a direct assignment inside a typed cast, plus a generated `object? Get(Control c,
  string name)` used only to fill in the pristine snapshot.

The generated code is emitted with `#nullable enable`, fully qualified type names (so it compiles
whatever `using`s the manifest's file has), and no dependencies on analyzer-only types.

**Diagnostics the generator reports**, each on the offending attribute line:

| Id | Condition |
| --- | --- |
| VD001 | The attributed type is not a `Control`, or is abstract without a generated subclass. |
| VD002 | No accessible parameterless constructor. |
| VD003 | Two registrations produce the same factory name (`Name` on the attribute fixes it). |
| VD004 | The same type is registered more than once across the compilation's manifests. |
| VD005 | `Name` is not a valid C# identifier. |
| VD006 | A public settable property is a delegate but matches neither an `AddXxxHandler`/`RemoveXxxHandler` pair nor a known event shape (it is then treated as an ordinary property, with a warning that the reconciler will assign it as a field). |
| VD007 | The manifest class is not `partial`. |

Diagnostics rather than silent omission is the entire point of the manifest: the alternative (a
factory that is simply missing) turns into a confusing compile error at a call site that has nothing
to do with the real mistake.

## 6. Reconciliation semantics

The reconciler walks the previous and next trees together, and for each pair decides *reuse*,
*create*, or *replace*. Nothing about the control tree is read back; the previous tree is the
authority on what the control currently looks like.

| Case | Action |
| --- | --- |
| Same `NodeType` at the same position | Reuse the control. Diff props: for each named prop in the new node, set it only if the value differs from the old node's; for each prop present in the old node and absent from the new one, restore its pristine value from the `NodeType`'s snapshot, so "absent" is well defined rather than "leave whatever was there". |
| Different `NodeType` | Create the new control, set all its props, insert it in place of the old one, unmount the old subtree. |
| No previous node | Create, set all props, mount. |
| No next node | Unmount the subtree: detach events, `Controls.Remove`, drop references. |

### 6.1 A property is present, set, or reset

A node lists the properties it says something about and omits the rest. Which of the three things an
argument meant is decided once, at the call site, and what reaches the node is one bit plus a value:

| Argument | In the node | On mount | On a patch from a node that had it |
| --- | --- | --- | --- |
| not passed (`null`) | absent | control keeps what its constructor gave it | restored to the pristine value |
| `Label(text: "hi")` | present, `"hi"` | set | set, if it differs from the old node |
| `Label(font: Specified<Font?>.Reset)` | absent | as above | as above |

`Specified<T>` is:

```csharp
public readonly struct Specified<T>
{
    public Specified(T value)
    {
        Value = value;
        IsSpecified = true;
    }

    public T Value { get; }

    public bool IsSpecified { get; }

    public static Specified<T> Reset => default;

    public static Specified<T> From(T value) => new(value);

    public static implicit operator Specified<T>(T value) => new(value);
}
```

Four properties of that shape are deliberate:

- **The zero value is the safe one.** `IsSpecified` is false when a `Specified<T>` has been
  zero-initialised, which is what `default`, an array element, and an uninitialised local all give.
  A flag whose *zero* value meant "explicitly set" would be wrong for exactly those cases, because
  struct field initialisers do not run for `default(T)` - so the absent state has to be the one you
  get for free.
- **No constructor for the absent state.** `Reset` is `default`, so the struct needs no parameterless
  constructor at all (C# requires those to be `public`, and one would be pointless here).
- **`Reset` and "not passed" agree on purpose.** Absence means "restore the pristine value" on a
  patch, so the explicit spelling produces the same node state as the implicit one. It exists for
  legibility and for the case where a caller wants to say it out loud.
- **There is no generic form.** Method type inference does not flow through a user-defined conversion,
  so a generic helper over `Specified<T>` (`Show(null)`, or a generic `Of<T>` factory) fails with
  CS0411 before the conversion is ever considered. The generated factories are non-generic per control
  precisely so this never comes up: `Label`'s parameter is `Specified<string>? text`, not some
  `Specified<T>` the compiler has to infer.

What is *not* the same is `null` as a value. `font: null` binds to the nullable struct - the compiler
prefers the nullable conversion over the implicit operator - so it means "absent". Setting a nullable
property to `null` ("no font, inherit") is spelled `font: (Font?)null`, or through an explicit
`Specified<Font?>.From(null)` if that reads better. That distinction is the reason the wrapper exists:
with a plain `Font? font = null` parameter, "no font" and "says nothing about the font" are the same
call.

The third state is genuinely a third state, not a hole in the first two: `Label(text: "")` is a
*specified* empty string and will clear the text, while `Label()` says nothing about it at all. Every
one of those bindings was checked against the compiler rather than assumed, including the surprise
that `(Font?)null` is required for "set it to null".

The wrapper is a call-site device only. The factory unwraps it and stores the plain `T` in the node's
`VProp`, so no `Specified<T>` is ever boxed and the node's shape is unchanged.

Children, in order of preference:

1. **Keyed matching** when both lists contain keys: children are matched by `Key`, so a reorder
   moves controls rather than recreating them and, crucially, preserves control state (scroll
   position, selection, caret, focus).
2. **Positional matching** otherwise, one index at a time, which is what handles the overwhelmingly
   common append/change case without any bookkeeping.
3. **Mixing** is allowed: keyed children are matched by key and unkeyed children fill the remaining
   slots positionally. A key change is a replacement, never an update.

Ordering after matching: the reconciler emits the minimum set of `Insert`/`Remove`/`Move` operations to
turn the old order into the new one. Moves go through `ControlCollection.Move`, which reorders the
child in place and runs `OnChildMoved` rather than the attach/detach pair - so a reordered control
keeps its own state (scroll position, caret, a label's built layout) and a container does not redo
whatever it hangs off a child being added. It never clears and rebuilds a set of siblings, which
would destroy state and re-run layout for the whole subtree.

Events: the reconciler keeps the previously attached delegate per (control, event name). A changed
delegate detaches the old one through the generated `RemoveXxxHandler` and attaches the new one; an
unchanged delegate does nothing. This matters because attaching twice on every render would leak a
handler per render.

Text: `Label.Text` and `RichLabel.Text` are ordinary props, so they diff like any other. `RichLabel`
additionally supports inline controls, which are `VNode`s rather than props: a child `VNode` whose
`NodeType` is a control in a `RichLabel` context is added through `AddInlineControl`. This is the one
place where "children" and "props" interact, and it is decided by the parent's capability, not the
child's type (section 10 lists it as the fiddliest part of the design).

## 7. The application-facing API

The plan's surface, in the order a developer meets it:

| Piece | Shape | Purpose |
| --- | --- | --- |
| `VirtualDomAttribute` | `[VirtualDom(typeof(Label), Name = "…")]`, `AllowMultiple` | The manifest entry (section 3). |
| `VNode` | Non-generic node: `NodeType`, `VProp[]`, `VNode[]`, `Key` | The description (section 3). |
| `Specified<T>` | `readonly struct`: `Value`, `IsSpecified`, `Reset`, implicit conversion from `T` | The optional-argument wrapper (section 6.1). Never stored in a node. |
| `Component` | `abstract VNode Render();`, `void SetState()` | The view model base. `SetState` marks the component dirty and renders synchronously, then patches. |
| `VirtualDom.Mount(Control parent, VNode root)` | Returns a `MountHandle` | Turns a tree into live controls under a container (typically the window's root panel). |
| `MountHandle.Update(VNode root)` | Reconciles the new tree against what is mounted | What a component's render calls; also usable directly, without components, for a one-shot tree. |
| `MountHandle.Unmount()` | Detaches everything and drops references | Window closing, or replacing a whole screen. |
| `services.AddVirtualDom()` | `IServiceCollection` extension | Consistent with the other features and `APPLICATION-MODEL.md` section 5; registers the reconciler's services (currently none beyond the reflective table, when one exists). |

A component can be used without the component layer at all (`Mount` + `Update` by hand), and the
component layer is a thin wrapper over that. Keeping the two apart is deliberate: it means the
reconciler is testable without any state machinery, and an application that wants its own scheduling
can have it.

## 8. Steps

| # | Step | Scope | Done when |
| --- | --- | --- | --- |
| 1 | Package skeleton and the node model | `Kolvir.Feature.VirtualDom` project in the rung-3 layout, `VNode`, `VProp`, `NodeType`, `Specified<T>`, hand-built trees | A tree can be constructed by hand and asserted on; the project packs with the same `lib/` folders as the other features |
| 2 | Mount, and the first hand-written binder | `VirtualDom.Mount`, create-only reconciliation, `MountHandle`; a hand-written `Label` binder with generated-style `Set`/`Get` next to the contract it will later replace | Mounting a hand-built tree puts a real `Label` with the right text under a container |
| 3 | Property diffing and reset | Old-vs-new prop diff, the pristine snapshot captured from a fresh control on first use, `Update` | A changed tree patches in place: the same control instance survives, a dropped prop returns to its pristine value (not `default(T)`), and `Specified<T>.Reset` behaves the same as dropping it |
| 4 | Structure: create, replace, unmount | `NodeType` mismatch handling, positional children, subtree unmount, event detach | Inserting, removing and retyping children mid-list leave a correct tree and no leaked handlers |
| 5 | Keys and reordering | Keyed matching, mixed keyed/unkeyed lists, minimum move sequence over `ControlCollection.Move` | Reversing a keyed list moves controls instead of recreating them, proven by reference identity and by scroll position surviving |
| 6 | Events | Generated `AddXxxHandler`/`RemoveXxxHandler` mapping, delegate identity tracking | Re-rendering with the same handler subscribes once; a new handler replaces the old one |
| 7 | The source generator | Attribute, `ForAttributeWithMetadataName`, `V` factories, `NodeType`s, binders, diagnostics VD001-VD007 | The manifest in section 4 compiles, and the drift test (section 11) passes for every built-in control |
| 8 | The component layer | `Component`, `SetState`, dirty tracking, render-and-patch | A headless scene shows new controls after a `SetState`, and `Render` runs exactly once per change |
| 9 | Reflective escape hatch | Annotated `Type` registration, `[RequiresUnreferencedCode]` unannotated overloads, the runtime table | A control registered by `typeof` at run time mounts through the reflective binder, and an AOT build warns on the unannotated overload |
| 10 | Demo, docs and AOT verification | A virtual-DOM card in the demo app, `DEVELOPER-GUIDE.md` sections, single-file AOT publish | The published exe renders the card identically to the JIT build, and the trimmer's report shows only the registered controls |

Steps 2-6 are deliberately written to work with a hand-written binder so the reconciler is complete
and tested *before* the generator exists. The generator then replaces the hand-written surface
without touching the reconciler, which is what keeps step 7 a packaging-and-codegen change rather than
a redesign.

## 9. Options considered and rejected

| Option | Why not |
| --- | --- |
| Runtime registration: `VirtualDom.Register<Button>()` at startup | A generator cannot see the call's effect, and the trimmer cannot learn the type from it. Recovering safety means `[DynamicallyAccessedMembers]` plus a reflective binder - slower, and the reflective binder is the path with real AOT risk. Kept only as the escape hatch for types that genuinely arrive at run time (step 9). |
| `[DynamicDependency]` on the registration method | Compile-time constants only. It cannot express "whatever the application passes to this call". |
| String-named types and properties parsed at run time (a real DSL) | Property names as run-time strings need reflection the trimmer cannot see, are unverifiable until run time, and are slower. This is the one option that would make an AOT build genuinely unreliable. |
| `VNode.Of<T>(...)` generic factories, without a manifest or generator | AOT-safe (with an annotation on the type parameter) but verbose at every call site, and setting a property still needs a name/value pair. Acceptable as an internal primitive; rejected as the developer-facing syntax. |
| Hand-written static factories (`V.Label(...)` written by a person) | Perfect AOT story, but every control's surface is maintained by hand and drifts from the control. Only worth it for types the generator cannot see. |
| Lambda/builder closures (`V.Column(c => { c.Gap(8); })`) | Poor inference, awkward with LINQ, and it reads as imperative code rather than a description. |
| A markup file compiled by a generator (XAML/JSX-like) | The only way to get a genuine markup feel, because C# has no macro system. A large, additive front end - parser, generator, diagnostics, editor story. Explicitly deferred, not rejected: it lowers to the same node tree, so it can be added later without touching anything below section 4. |

## 10. Risks and open questions

- **Reordering a `RichLabel`'s inline controls does not reorder its text.** Inline content order lives
  in the label's span collection, not in `Controls`, so `ControlCollection.Move` on a `RichLabel`
  changes z-order only and the text flow does not follow. A keyed reorder of inline controls
  therefore needs the span moved, not the child - which means the reconciler cannot treat inline
  children as ordinary children (see the next bullet). Either the span collection grows a move of its
  own, or inline controls are ordered by a prop the label applies.
- **Inline controls in a `RichLabel` sit between "children" and "props"** (section 6). Deciding that a
  `RichLabel`'s children are inline controls, based on the parent rather than the child, is the
  simplest rule available but the least obvious; step 4 is where it gets tested.
- **A node tree per render is allocation.** A React-style `Render()` rebuilds everything it describes.
  That is fine for event-driven UI and unacceptable for anything rendering per frame, which is why
  section 3 rules out per-frame rendering. If someone needs per-frame animation, the answer is
  animating props of existing controls, not re-rendering a tree; a `ShouldUpdate` subtree skip is the
  follow-up if it turns out to be needed.
- **The pristine snapshot is read from a real control, so it can only describe properties the
  constructor leaves stable.** A property whose initial value depends on something outside the control
  (a theme colour read lazily, a computed size) gives a snapshot that is right at the moment it is
  taken and may not be later. The mitigation is that the snapshot is taken per `NodeType` on first
  use and only consulted when a prop is dropped, so the exposure is narrow; the alternative, matching
  `default(T)`, is wrong far more often.
- **Name collisions are a per-manifest concern.** Two manifests imported into one file make shared
  names ambiguous, which is a compile error at the call site rather than a wrong tree, so it is
  annoying but safe. `Name` on the attribute is the escape.
- **The reflective path is a real user-visible failure mode** if someone publishes AOT after ignoring
  an `IL2xxx` warning. Mitigated by `[RequiresUnreferencedCode]` on the unannotated overloads, not by
  documentation alone.
- **Batching renders** (coalescing several `SetState` calls in one message, or rendering before paint
  rather than synchronously) is deliberately not in the plan. It needs a scheduling seam of its own
  and should be decided when there is a UI that needs it.

## 11. Verification approach

Every step is checked with headless tests in `tests/features/Kolvir.Feature.VirtualDom.Tests`, which
build a tree, mount it into a `HeadlessScene`, and inspect the resulting `Control` tree - the same
approach the selection and layout work uses, and the reason none of this needs a window.

Five claims get special treatment, because they are the ones that would otherwise only fail in a
published application:

| Claim | How it is proven |
| --- | --- |
| **No mirror drift** between a control and its generated factory | A test enumerates every public instance settable property of every control the test manifest registers and asserts the generated binder accepts each name. The binder returns `false` for an unknown name, so this is a direct check of the generated code's coverage - and it fails on the day a property is added to a control without regenerating. |
| **Absence, a value, and reset stay three different things** | A render that drops a prop restores the pristine value, asserted against properties whose pristine value is not `default(T)` (`Text` starts at `""`, `Name` at the type name, a control's `Dock` may start at `Fill`). `Specified<T>.Reset` must produce the same node state as omitting the argument, and `font: null` must mean "absent" rather than "set to null". |
| **Patches instead of rebuilds** | Assertions on reference identity: the control instance is the same object across a render, and keyed reorders preserve specific state (a `RichLabel`'s layout is not rebuilt, a `Panel`'s scroll position is unchanged). |
| **No handler leaks** | A re-render with the same delegate leaves the click count at one, proven by raising the event through `Control`'s own trigger. |
| **AOT- and trim-clean** | `dotnet publish` with `PublishTrimmed` and `PublishAot`, treating `IL2xxx` warnings as errors, plus the demo app's `--render <png>` compared byte-for-byte between the JIT and published builds. An application that does not reference the package must show no virtual-DOM assembly in its output at all. |

The demo app gains a virtual-DOM card in the same shape as the existing feature cards, so the
published-build render comparison covers the new code path automatically.

## 12. Progress

| Step | State | Notes |
| --- | --- | --- |
| 1 | Not started | |
| 2 | Not started | |
| 3 | Not started | |
| 4 | Not started | |
| 5 | Not started | |
| 6 | Not started | |
| 7 | Not started | |
| 8 | Not started | |
| 9 | Not started | |
| 10 | Not started | |
