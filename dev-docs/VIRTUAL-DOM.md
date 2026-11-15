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
| Attribute shape | `[AttributeUsage(Class, AllowMultiple = true)]`, ctor takes the single `Type`, other options as settable properties (`Name`) | Follows `System.Text.Json`'s `[JsonSerializable]`: one attribute instance is a per-type configuration record, so future options (`Name`, `IgnoreProperties`, a custom node shape) have somewhere to live without reshaping the API or adding overloads. |
| Manifest is `partial` | Yes | It is the generator's write target: it emits one node class per registered control into the manifest's namespace, and later a generated runtime table or registration hook lands on the manifest itself without a base class or a second attribute. |
| Where the generated node types live | One `V<Control>` class per registered control, in the **manifest's own namespace**, named by the attribute's `Name` when given | Nodes are ordinary classes a developer constructs with an object initializer, so a plain `using MyApp;` is all a view file needs. Separate namespaces per manifest give the multi-assembly story for free. |
| What a node holds | One **tracked property per settable control property**: a value plus a was-set flag, behind an ordinary `{ get; set; }`-shaped property | This is the whole point of switching from parameters to properties (section 6.1): calling the setter *is* the record that a property was specified, so "omitted", "set to a value" and "set to `null`" are all distinguishable without a wrapper type, a sentinel, or a nullable annotation trick. |
| How a property is omitted | By not assigning it: `new VLabel { }` sets nothing, `new VLabel { Text = "hi" }` sets one thing, `new VLabel { Text = null }` sets it to null | Falls straight out of the setter being the signal, and it is what removes `Specified<T>` from the public surface entirely (section 9 records the parameter-based design this replaced). |
| Children | `IReadOnlyList<VNode> Children { get; init; } = []` on the `VNode` base, so `Children = [ a, ..more ]` gets collection expressions and spread | `init` rather than a get-only `List` because a collection *initializer* cannot express spread (`..more`), which is what a message list needs. The reconciler ignores children on a node whose control is not a container, and VD011 warns when one is set. |
| What applying a node does | Generated `ApplyTo(Control)` and `PatchFrom(previous, Control)` members: direct typed assignments, no names, no dictionary, no `switch` on strings | The reconciler must patch a control without knowing its type. Generating the assignments per node type does that statically, and since the previous node is typed too, the diff is a field-by-field comparison rather than a lookup. |
| What is diffed | The **previous node against the next node**, never the node against the control | The old values come from the tree we already hold, so nothing reads a control's current property state back out. That keeps `Control` free of any virtual-DOM awareness and avoids the hardest reflective cases (getter-only and private-setter properties). The one exception is the pristine snapshot below, read once per type rather than per diff. |
| A `null` for a non-nullable property | The generated property is typed non-nullable, so `Text = null` is CS8625 at the assignment, which an analyzer raises to an error (VD010); the generated setter keeps an `ArgumentNullException` as the backstop for values no analysis can prove | C# only warns and only where nullable is enabled, so in a `#nullable disable` file the assignment would silently store a null. An analyzer error is the only way to fail the build without changing the severity of warnings in an application's unrelated code. `null!` stays quiet on purpose: that is the caller overriding the analysis, so the backstop is the right thing to hear from. |
| What "default" means | Not `default(T)`: the **pristine value**, snapshotted per property from a freshly constructed control | `Label.Text` starts at `""`, `Control.Name` at the type name, and a control's constructor may set `Dock = Fill` while the declared default is `None`. `default(T)` would be wrong for most properties, so each node class records what its control looked like before any node touched it, in static fields read once from a freshly constructed control through the control's own getters - nothing generated, nothing reflected. |
| Node type | One `VNode` base holding `Key` and `Children`, and a generated concrete class per control holding its own typed properties | The reconciler works against the base, so it stays non-generic; compile-time type safety is the node class's property types, which is where a developer actually types. |
| Property names | **None exist.** A property is a field on a generated class, not a name in a dictionary | Removes the interned string, the `VProp[]` and the hashed string dispatch that the factory-based design needed, and makes the diff a comparison of typed values. |
| `Key` | On the `VNode` base, so every element can be identified in a list | Requirement of keyed reconciliation (section 6). On the base type it needs no per-type support. |
| Events | Generated event properties map to Kolvir's `AddXxxHandler`/`RemoveXxxHandler` method pairs, not to `+=` | Kolvir exposes handlers as method pairs rather than `event` members (`FEATURE-COST-POLICY.md` 5.4). The generator knows the pairs, so an event is a delegate property; the reconciler keeps the attached delegate so it can detach the old one and attach the new one across renders. |
| Control creation | A generated `Create()` on each node type returning a new control | `Activator.CreateInstance` is the classic trimming trap. A generated `new Label()` also makes a missing parameterless constructor a build error rather than a run-time one. |
| Name collisions | A control property whose name matches a `VNode` member (`Key`, `Children`) is reported as VD012 and left out of the generated class | Flattening puts control properties in the same namespace as the node's own members, so a clash has to be diagnosed rather than silently shadowed. No built-in control declares either name today, so the diagnostic is insurance. |
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
using MyApp;            // the manifest's namespace, where the generated node types live

sealed class ChatPanel : Component
{
    readonly string _title;
    readonly IReadOnlyList<Message> _messages;

    public ChatPanel(string title, IReadOnlyList<Message> messages)
    {
        _title = title;
        _messages = messages;
    }

    public override VNode Render() => new VStack
    {
        Gap = 8,
        Children =
        [
            new VRichText { Text = _title, Font = bold },
            .._messages.Select(m => new VLabel { Key = m.Id, Text = m.Text }),
            new VButton { Text = "Send", OnClick = SendPressed },
        ],
    };
}
```

That is ordinary C# object and collection syntax, which is the point: the generator produces classes
whose property names and types are the control's own, so everything the editor already does for a
`Label` - completion, rename, "go to definition", compile-time type checking - works on a node without
any of it being wired up.

The three decisions that make it work:

1. **A node class per control, not a factory method.** `new VLabel { Text = "hi" }` is an object
   initializer over a generated class, so every property is a real property with the control's type.
   Setting a property is a visible, named assignment - nothing hidden in a string or a dictionary.
2. **Collection expressions for children.** `Children = [a, b, ..more]` gives nesting without wrapping
   calls, plus spread for any `IEnumerable<VNode>`, which is what turns a message list into one line.
   This is why `Children` is `init`-only: a collection *initializer* (`Children = { ... }`) cannot
   express spread.
3. **Omitting a property is the absence of an assignment.** `new VLabel { }` says nothing about the
   text; `new VLabel { Text = "" }` sets it to empty; `new VLabel { Text = null }` would be setting it
   to null, which is only legal where the control property is nullable. All three are distinguishable
   without a wrapper type, because the setter call is itself the record that the property was
   specified (section 6.1).

What this costs against the factory syntax considered earlier is verbosity: `new VLabel { Text = "hi" }`
is longer than `Label(text: "hi")`. What it buys is unambiguous setters, no `Specified<T>` in the
calling convention, no property-name strings anywhere, and a diff that compares typed fields. Section 9
records the factory design this replaced.

The generated surface follows the controls rather than being hand-designed, which is the same trade
`System.Text.Json`'s source generator makes and the same one `FEATURE-COST-POLICY.md` 5.1 argues for -
generate the shape instead of reflecting at run time.
## 5. What the generator emits

For each registered control, one node class, emitted into the manifest's namespace. It holds a tracked
property per settable control property, and the three members the reconciler needs:

```csharp
public sealed class VLabel : VNode
{
    private Specified<string> _text;
    public string Text { get => _text.Value!; set => _text = value; }

    private Specified<Font> _font;
    public Font Font { get => _font.Value; set => _font = value; }

    public override NodeType NodeType => LabelType;

    private static readonly NodeType LabelType = new(typeof(Label), static () => new Label());

    // What the constructor left, read once for the type rather than once per property dropped.
    private static readonly string PristineText = new Label().Text;
    private static readonly Font PristineFont = new Label().Font;

    public override void ApplyTo(Control control)
    {
        var label = (Label)control;
        if (_text.IsSpecified) label.Text = _text.Value!;
        if (_font.IsSpecified) label.Font = _font.Value;
    }

    public override void PatchFrom(VNode previous, Control control)
    {
        var old = (VLabel)previous;
        var label = (Label)control;
        if (!_text.Equals(old._text)) label.Text = _text.IsSpecified ? _text.Value! : PristineText;
        if (!_font.Equals(old._font)) label.Font = _font.IsSpecified ? _font.Value : PristineFont;
    }
}
```

The three members the reconciler drives are **public rather than internal**, because the generated
class is emitted into the consuming assembly and an internal contract cannot be overridden from
there. They carry `[EditorBrowsable(Never)]` so they stay out of IntelliSense for an author who is only
writing trees: they are the generated-implementation contract, not the API an application writes
against.

Three things to notice:

- **`Specified<T>` is machinery, not a calling convention.** It is public only because generated code in a consuming assembly has to name it (and is hidden from IntelliSense for that reason). It survives as the field type
  behind a tracked property - a value plus a was-set flag - but it never appears in a signature a
  developer writes, and its `Reset` member is gone, because absence already means "not assigned".
  This is what removed the public wrapper, the implicit operator, the nullable-conversion trap and
  the CS0411 generic-inference note from the design (section 9).
- **No names, no `VProp[]`, no string `switch`.** `ApplyTo` and `PatchFrom` are generated typed
  assignments over the node's own fields, and the diff compares `Specified<T>` values directly. The
  metadata that used to be a property name is now the field.
- **`NodeType` is the identity the reconciler uses** - one static instance per node class, compared by
  reference, which is what answers "can I reuse this control?" in one step and what stops one node
  class patching a control another node class made (its `PatchFrom` could not cast the previous node).

The `VNode` base itself is hand-written in `Kolvir.Feature.VirtualDom`: `Key`, `Children`, the abstract
`ApplyTo`/`PatchFrom`/`NodeType` members, and the reconciler's entry points. The generated code is
emitted with `#nullable enable`, fully qualified type names (so it compiles whatever `using`s the
manifest's file has), and no dependencies on analyzer-only types.

**Diagnostics the generator reports**, each on the offending attribute line:

| Id | Condition |
| --- | --- |
| VD001 | The attributed type is not a `Control`, or is abstract without a generated subclass. |
| VD002 | No accessible parameterless constructor. |
| VD003 | Two registrations produce the same node class name (`Name` on the attribute fixes it). |
| VD004 | The same type is registered more than once across the compilation's manifests. |
| VD005 | `Name` is not a valid C# identifier. |
| VD006 | A public settable property is a delegate but matches neither an `AddXxxHandler`/`RemoveXxxHandler` pair nor a known event shape (it is then treated as an ordinary property, with a warning that it will be assigned as a delegate field). |
| VD007 | The manifest class is not `partial`. |
| VD012 | A public settable property has the same name as a `VNode` member (`Key`, `Children`), so it cannot be flattened. Reported as an error, because silently dropping it would leave a property that cannot be set declaratively. Nothing in `Kolvir` declares either name today. |

Plus two from the analyzer half, both reported as **errors** rather than warnings:

| Id | Condition |
| --- | --- |
| VD010 | A `null` is assigned to a generated property whose type is non-nullable. C# catches this as CS8625 and CS8604, but as warnings, and not at all in a file with `#nullable disable` - where the assignment would silently store a null. |
| VD011 | `Children` is assigned on a node whose control is not a container. |

VD010 needs no lookup table: it reads the generated property's declared type, which the generator
wrote. What it deliberately leaves to the compiler is the set of cases the compiler already handles
better - it has flow analysis, so a `string?` local that may hold null is CS8604 there whether or not
we say anything.

The analyzer lives in the same assembly as the generator, which is referenced as an `Analyzer`, so
both arrive together and neither reaches the deliverable.

Diagnostics rather than silent omission is the entire point of the manifest: the alternative (a node
class that is simply missing, or silently short a property) turns into a confusing compile error at a
call site that has nothing to do with the real mistake.
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

### 6.1 Omitted, set, or set to null

A node lists the properties it says something about, by assigning them, and says nothing about the
rest by not assigning them:

| Node | In the node | On mount | On a patch where the previous node set it |
| --- | --- | --- | --- |
| `new VLabel { }` | absent | control keeps what its constructor gave it | restored to the pristine value |
| `new VLabel { Text = "hi" }` | present, `"hi"` | set | set, if it differs from the previous node |
| `new VLabel { Font = null }` | present, `null` | set to `null` ("no font, inherit") | set to `null` |

There is no fourth spelling for "put it back": not assigning the property already means that, so an
explicit reset would be an alias for absence. The factory-based design carried a `Reset` member for
exactly this reason and this design does not need it.

Nothing needs to be assigned for a node to be valid, so assigning a property is always a statement that
the view wants that value there. If the previous render set one and this render does not, the previous
value is wrong, and it is restored to the pristine value described below.

**This is why properties beat parameters.** A parameter cannot tell "argument omitted" from "argument
passed as `null`" when both are representable, so the earlier design wrapped every parameter in a
`Specified<T>` struct. It worked, but the wrapper leaked into every call site, needed a
nullable-conversion workaround (a `(Font?)null` cast), and of its four parameter shapes two quietly
did the wrong thing:

| Parameter shape | `null` argument means | Diagnostic |
| --- | --- | --- |
| `Specified<string?>` | value `null` | none |
| `Specified<string?>?` | absent | none |
| `Specified<string>` (non-nullable) | value `null` | CS8625 warning |
| `Specified<string>?` (non-nullable) | absent | none at all |

A setter has none of these problems: there is nothing to convert, nothing to infer, and no competing
nullable conversion. `Text = null` on a nullable property is a value; on a non-nullable one it is CS8625
at the assignment, which is where a developer can see it. `Specified<T>` survives only as the private
field type behind a tracked property, where its `IsSpecified` flag is the record of the setter having
run.

**What "pristine" means, and where it comes from.** Not `default(T)`: `Label.Text` starts at `""`,
`Control.Name` at the type name, and a control's constructor may set `Dock = Fill` while the declared
default is `None`. So each node class holds the pristine values as static fields, read from a freshly
constructed control when the type is first used - the control's own public getters, no generated
accessor and no reflection - and consulted only when a property is dropped, never per diff. A property whose initial value depends on
something outside the control (a theme colour read lazily) gives a snapshot that is right when taken
and may not stay right; the exposure is narrow, since the snapshot is only consulted when a property is
dropped, and it is still more accurate than `default(T)`.
Children, in order of preference:

1. **Keyed matching** when both lists contain keys: children are matched by `Key`, so a reorder
   moves controls rather than recreating them and, crucially, preserves control state (scroll
   position, selection, caret, focus).
2. **Positional matching** otherwise, one index at a time, which is what handles the overwhelmingly
   common append/change case without any bookkeeping.
3. **The two never mix.** A keyed node with no matching key is a new element, and a keyed child the
   new list no longer asks for is removed; an unkeyed node only takes an unkeyed leftover. Letting a
   keyed child be claimed positionally would mean that swapping one key for another silently renamed
   a control, which is exactly the identity the key exists to control.

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
| `VirtualDomAttribute` | `[VirtualDom(typeof(Label), Name = "...")]`, `AllowMultiple` | The manifest entry (section 3). |
| `VNode` | Abstract base: `Key`, `Children` (init-only `IReadOnlyList<VNode>`), and the abstract `NodeType`/`ApplyTo`/`PatchFrom` members the reconciler drives | The description (section 3). Everything a developer writes derives from a generated class over this. |
| `V<Control>` (generated) | One class per registered control: a tracked property per settable control property, plus `Create`, `ApplyTo`, `PatchFrom` and a `NodeType` | What a view actually constructs (section 5). `Specified<T>` is a private field type inside it, never a public signature. |
| `Component` | `abstract VNode Render();`, `void SetState()` | The view model base. `SetState` marks the component dirty and renders synchronously, then patches. |
| `VirtualDomRuntime.Mount(Control parent, VNode root)` | Returns a `MountHandle` | Turns a tree into live controls under a container (typically the window's root panel). Named `VirtualDomRuntime` because a type called `VirtualDom` would sit in the `Kolvir.VirtualDom` namespace and shadow it at every call site. |
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
| 1 | Package skeleton and the node model | `Kolvir.Feature.VirtualDom` project in the rung-3 layout, `VNode`, `NodeType`, the internal `Specified<T>` field type, hand-written node classes for two controls | A tree can be built by hand with object initializers and asserted on; the project packs with the same `lib/` folders as the other features |
| 2 | Mount, and applying a node | `VirtualDom.Mount`, create-only reconciliation, `MountHandle`, `ApplyTo` on the hand-written nodes | Mounting a hand-built tree puts a real `Label` with the right text under a container |
| 3 | Patching and the pristine snapshot | `PatchFrom`, tracked properties, the pristine snapshot captured from a fresh control on first use, `Update` | A changed tree patches in place: the same control instance survives, a property that stops being set returns to its pristine value (not `default(T)`), and one that stays set to the same value is not re-assigned |
| 4 | Structure: create, replace, unmount | `NodeType` mismatch handling, positional children, subtree unmount, event detach | Inserting, removing and retyping children mid-list leave a correct tree and no leaked handlers |
| 5 | Keys and reordering | Keyed matching, mixed keyed/unkeyed lists, minimum move sequence over `ControlCollection.Move` | Reversing a keyed list moves controls instead of recreating them, proven by reference identity and by scroll position surviving |
| 6 | Events | Generated `AddXxxHandler`/`RemoveXxxHandler` mapping, delegate identity tracking | Re-rendering with the same handler subscribes once; a new handler replaces the old one |
| 7 | The source generator and its analyzer | Attribute, `ForAttributeWithMetadataName`, node classes with tracked properties, `Create`/`ApplyTo`/`PatchFrom`, diagnostics VD001-VD007 and VD012, plus the analyzer half with VD010 and VD011 | The manifest in section 4 compiles, the drift test (section 11) passes for every built-in control, and snippets that assign `null` to a non-nullable property or children to a leaf node fail the build - VD010 also with `#nullable disable` |
| 8 | The component layer | `Component`, `SetState`, dirty tracking, render-and-patch | A headless scene shows new controls after a `SetState`, and `Render` runs exactly once per change |
| 9 | Reflective escape hatch | Annotated `Type` registration, `[RequiresUnreferencedCode]` unannotated overloads, the runtime table | A control registered by `typeof` at run time mounts through the reflective binder, and an AOT build warns on the unannotated overload |
| 10 | Demo, docs and AOT verification | A virtual-DOM card in the demo app, `DEVELOPER-GUIDE.md` sections, single-file AOT publish | The published exe renders the card identically to the JIT build, and the trimmer's report shows only the registered controls |

Steps 1-6 are written against two hand-written node classes so the reconciler is complete and tested
*before* the generator exists. The generator then replaces those classes without touching the
reconciler, which is what keeps step 7 a codegen change rather than a redesign.
### 8.1 Where the pieces live, and what else has to change

Three projects:

| Path | What it is |
| --- | --- |
| `src/features/Kolvir.Feature.VirtualDom/` | The runtime: `VNode`, `NodeType`, the internal `Specified<T>`, the reconciler (`MountHandle`), `Component`, `VirtualDom.Mount`/`Update`, `VirtualDomAttribute`. Four-folder rung-3 layout with all the code in `common/` and empty platform folders. |
| `src/generators/Kolvir.Feature.VirtualDom.Generators/` | `netstandard2.0`: the incremental generator and the `DiagnosticAnalyzer`. Deliberately **outside** `src/features/*/`, because the pack job globs that tree and would otherwise publish it as a package of its own. |
| `tests/features/Kolvir.Feature.VirtualDom.Tests/` | Headless tests, plus the hand-written node classes that steps 1-6 use. |

Five things outside the new code that the feature cannot land without:

1. **`Kolvir.slnx`** gets all three projects, under `/src/features/`, `/src/generators/` and
   `/tests/features/`. The solution is hand-listed, not globbed.
2. **`.github/workflows/tests.yml`** enumerates test projects one at a time - a `restore` step
   (`-p:TargetFramework=net10.0`, for the reason the other feature suites give) and a `test` step.
   Two added blocks; nothing is discovered automatically.
3. **XML documentation is mandatory and enforced.** `Directory.Build.props` sets
   `GenerateDocumentationFile` on anything that ships and raises **CS1591 as an error**, so every
   public member of the runtime assembly needs a `<summary>`. The test project is exempt by the
   `.Tests` naming rule.
4. **The analyzer has to be packed, not just referenced.** In this repository a `ProjectReference`
   with `OutputItemType="Analyzer"` is enough; a consumer gets neither generator nor analyzer that
   way. Both ship inside the feature package under `analyzers/dotnet/cs/`, which is how
   `System.Text.Json`'s generator travels, and the per-TFM inner builds have to add them exactly once
   rather than three times.
5. **`pack-nuget` needs no change** - it globs `src/features/*/*.csproj` - but the package-layout
   check will then report a `Kolvir.Feature.VirtualDom` row, which is the point at which the net11
   question in section 10 becomes visible if it ever matters.
## 9. Options considered and rejected

| Option | Why not |
| --- | --- |
| Runtime registration: `VirtualDom.Register<Button>()` at startup | A generator cannot see the call's effect, and the trimmer cannot learn the type from it. Recovering safety means `[DynamicallyAccessedMembers]` plus a reflective binder - slower, and the reflective binder is the path with real AOT risk. Kept only as the escape hatch for types that genuinely arrive at run time (step 9). |
| `[DynamicDependency]` on the registration method | Compile-time constants only. It cannot express "whatever the application passes to this call". |
| String-named types and properties parsed at run time (a real DSL) | Property names as run-time strings need reflection the trimmer cannot see, are unverifiable until run time, and are slower. This is the one option that would make an AOT build genuinely unreliable. |
| Factory methods with `Specified<T>` parameters - the design this replaced | AOT-clean and workable, but it put a wrapper in every call site to answer a question a setter answers for free, and it did not actually make `null` safe: three of its four parameter shapes were measured mishandling it (section 6.1). Switching to a class with tracked properties deleted the wrapper from the public surface, the property-name strings, the `VProp[]` and the hashed name dispatch, and made the diff a comparison of typed fields. Kept here because the wrapper's problems are the reason for the current shape, not an accident of it. |
| Union types (C# 15 / .NET 11) as the three-state wrapper | Measured on SDK 11.0.100 against `net11.0`: a union with a non-nullable case, `union Prop(string, Absent, Reset)`, rejects `Take(null)` with **CS8625, still a warning**; with a nullable case (`string?`) `null` is silent, exactly as `Specified<string?>` is. The rule the compiler applies is "a null literal converted to a non-nullable reference type", which does not care whether the destination is a union case or an implicit operator, so a union changes the spelling and not the severity. Unions are otherwise a good fit - `union Prop<T>(T, Absent, Reset)` compiles, and "no case set" being the zero value is exactly the absent state with no inverted flag needed - but they need a `net11.0` leg in the package and buy nothing this design does not already have. Recorded as a revisit in section 10. |
| Promoting CS8625/CS8604 to errors from the package's `buildTransitive` props | No code at all, and it covers more than a null-literal analyzer can, because the compiler's flow analysis already handles a possibly-null variable. Rejected because it would silently make every null-to-non-nullable in a referencing project an error - a policy an application should choose for itself, not inherit from adding a UI package. It stays a documented one-liner (section 10). |
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
- **`null` for a non-nullable property is an error only because we make it one.** The compiler reports
  it as CS8625/CS8604 and says nothing at all in a `#nullable disable` file, so VD010 is the load
  bearing part. An application that would rather treat every null-to-non-nullable as an error can say
  `<WarningsAsErrors>CS8625;CS8604</WarningsAsErrors>` and get more coverage than VD010 has; that is
  the documented alternative rather than the default, because it changes the severity of warnings in
  code that has nothing to do with the virtual DOM.
- **Unions are worth revisiting on .NET 11, but not for this.** Measured on SDK 11.0.100, a union
  rejects `null` for a non-nullable case with the same CS8625 warning and nothing stronger, so
  switching would cost a `net11.0` leg in the package for no behavioural gain. What it would buy is
  `union Specified<T>(T, Absent)` - "no case set" as the zero value, which is the absent state for
  free without the inverted flag. Revisit if and when the whole library moves to .NET 11.
- **Flattening costs verbosity, and that is the accepted trade.** `new VLabel { Text = "hi" }` is longer
  than a factory would be, and the difference compounds when nesting. It buys unambiguous setters and
  the removal of the entire wrapper/name/dictionary layer, which is the reason it was chosen (section
  9). If verbosity turns out to hurt, terse helpers can be added on top - a hand-written
  `VLabel Text(string text) => new VLabel { Text = text };` for the two or three shapes a codebase uses
  most costs nothing and changes no part of the design below section 4.
- **`Children` on a leaf is diagnosed, not prevented.** The property lives on the `VNode` base so
  collection expressions and spread work the same everywhere, which means a leaf node can accept
  children the reconciler will ignore. VD011 makes it a build error rather than a silent no-op.
- **Name collisions are a per-namespace concern.** Node classes are generated into the manifest's
  namespace, so two manifests registering the same simple name in the same namespace collide as a
  compile error rather than a wrong tree - annoying but safe. Within one manifest VD003 catches it, and
  `Name` on the attribute is the escape.
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

Six claims get special treatment, because they are the ones that would otherwise only fail in a
published application:

| Claim | How it is proven |
| --- | --- |
| **No mirror drift** between a control and its generated node class | For every control the test manifest registers, a test enumerates the control's public instance settable properties and asserts the generated class exposes each one **with the same property type**. Types matter as much as names here: a node property typed `object` would compile and then fail at the assignment. The test fails on the day a property is added to a control without regenerating. |
| **Omitted, set, and set to null stay distinct** | `new VLabel { Text = "" }` clears the text while `new VLabel { }` says nothing about it; a render that drops a property restores the pristine value, asserted against properties whose pristine value is not `default(T)` (`Text` starts at `""`, `Name` at the type name, a control's `Dock` may start at `Fill`); and `Font = null` arrives at the control as null rather than as absence. |
| **Patches instead of rebuilds** | Assertions on reference identity: the control instance is the same object across a render, and keyed reorders preserve specific state (a `RichLabel`'s layout is not rebuilt, a `Panel`'s scroll position is unchanged). |
| **No handler leaks** | A re-render with the same delegate leaves the click count at one, proven by raising the event through `Control`'s own trigger. |
| **A `null` for a non-nullable property does not build** | The analyzer is exercised with the compiler's own test harness (`Microsoft.CodeAnalysis.CSharp.Analyzer.Testing`): a snippet assigning `null` to a generated non-nullable property must produce VD010 as an error, and the same snippet under `#nullable disable` must produce it too - that second case is the reason the analyzer exists at all. A `null!` must produce nothing. |
| **AOT- and trim-clean** | `dotnet publish` with `PublishTrimmed` and `PublishAot`, treating `IL2xxx` warnings as errors, plus the demo app's `--render <png>` compared byte-for-byte between the JIT and published builds. An application that does not reference the package must show no virtual-DOM assembly in its output at all. |

The demo app gains a virtual-DOM card in the same shape as the existing feature cards, so the
published-build render comparison covers the new code path automatically.

## 12. Progress

| Step | State | Notes |
| --- | --- | --- |
| 1 | Done | `Kolvir.Feature.VirtualDom` in the rung-3 layout: `VNode`, `NodeType`, the public-but-hidden `Specified<T>`, `Mounted`, `Reconciler`, `MountHandle`, `VirtualDomRuntime`. Hand-written `VLabel`/`VNote`/`VButton`/`VPanel` stand in for the generator, so steps 2-6 test the reconciler before any codegen exists. 26 headless tests. |
| 2 | Done | `Mount` creates the root and applies its properties; a control is reused when the new node is the same node class and replaced in place when it is not. Verified by reference identity across `Update`, plus a scene test that a mounted tree is laid out by the host. |
| 3 | Done | Property diffing arrived with `PatchFrom`: a property that stops being set returns to the pristine value its constructor left (asserted against a control whose pristine value is not `default(T)`), an assigned `null` is a value rather than an absence, and an unchanged value is not re-assigned. |
| 4 | Not started | |
| 5 | Not started | |
| 6 | Not started | |
| 7 | Not started | |
| 8 | Not started | |
| 9 | Not started | |
| 10 | Not started | |
