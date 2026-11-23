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
their rationale; the steps in section 9 are the order of work; section 11 lists what is still open.

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
  additive* option that would lower to the same node tree (section 10).
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
runtime interprets.** That is the difference between the design we rejected (section 10) and the one
adopted.

One more consequence worth stating plainly: a registered control is *fully* retained - its constructor
is called and each generated assignment references a setter - so the trimmer removes nothing of it.
That is the intended cost, it is opt-in per type, and it is exactly what "register a control" means.
A control registered in a manifest and never used in any `Render()` is still retained; that is why the
manifest is expected to be short and per application rather than a library-wide list.

## 3. Design decisions

| Topic | Decision | Rationale |
| --- | --- | --- |
| Rung | Separate package, `Kolvir.Feature.VirtualDom`, namespace `Kolvir.Feature.VirtualDom` | Rung 3 of `FEATURE-COST-POLICY.md`. The base deliverable must not contain a UI framework that most applications will not use, and a feature that adds members to core types cannot take a lower rung anyway. |
| Project layout | The four-folder rung-3 layout, all code in `common/`, with empty `windows/`, `macos/`, `linux/` folders | Reconciling a tree is portable arithmetic over object graphs. The folders stay because the package's `lib/` folders and dependency groups come from those same inner builds; a single-target exception would fight `check-package-layout.py` for no gain. |
| Generator packaging | A second assembly, `Kolvir.Feature.VirtualDom.Generators`, referenced `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` | The generator is build-time only. It must not appear in the deliverable, and it targets `netstandard2.0` because analyzers do. |
| How a control becomes useable from a tree | A **manifest**: a `partial` class carrying one `[VirtualDom(typeof(X))]` attribute per control | Compile-time, so the generator sees the exact type and can emit direct code. This is the whole reason the design is AOT-clean. Rejected: a runtime `Register<T>()` call, whose effects no generator can see and whose types the trimmer cannot learn about. |
| Attribute shape | `[AttributeUsage(Class, AllowMultiple = true)]`, ctor takes the single `Type`, other options as settable properties (`Name`) | Follows `System.Text.Json`'s `[JsonSerializable]`: one attribute instance is a per-type configuration record, so future options (`Name`, `IgnoreProperties`, a custom node shape) have somewhere to live without reshaping the API or adding overloads. |
| Manifest is `partial` | Yes | It is the generator's write target: it emits one node class per registered control into the manifest's namespace, and later a generated runtime table or registration hook lands on the manifest itself without a base class or a second attribute. |
| Where the generated node types live | One `V<Control>` class per registered control, in the **manifest's own namespace**, named by the attribute's `Name` when given | Nodes are ordinary classes a developer constructs with an object initializer, so a plain `using MyApp;` is all a view file needs. Separate namespaces per manifest give the multi-assembly story for free. |
| What a node holds | One **tracked property per settable control property**: a value plus a was-set flag, behind an ordinary `{ get; set; }`-shaped property | This is the whole point of switching from parameters to properties (section 6.1): calling the setter *is* the record that a property was specified, so "omitted", "set to a value" and "set to `null`" are all distinguishable without a wrapper type, a sentinel, or a nullable annotation trick. |
| How a property is omitted | By not assigning it: `new VLabel { }` sets nothing, `new VLabel { Text = "hi" }` sets one thing, `new VLabel { Text = null }` sets it to null | Falls straight out of the setter being the signal, and it is what removes `Specified<T>` from the public surface entirely (section 10 records the parameter-based design this replaced). |
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
| Renders are event-driven, and coalesced | `SetState` marks the component and asks for a frame; the marked components render once each at the start of the next frame a host draws | One `SetState` per keystroke is a flag, and a handler that changes five things costs one render and one tree update instead of five - so there is no layout thrashing to work around and no cost to "just change it again". The seam is `UiHost.AddFrameStartingHandler`, added to core for this (section 7.1): it is the one point where the tree can still change and still be drawn by the frame that is starting. |
| Markup front end | Not in this plan | See section 10. Deciding it early would constrain the node model for no benefit; the node model is the stable ABI, and a markup generator can be added later behind it. |

## 4. What a developer writes

The manifest, once per application (or per library that contributes controls):

```csharp
using Kolvir.Feature.VirtualDom;

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
calling convention, no property-name strings anywhere, and a diff that compares typed fields. Section 10
records the factory design this replaced.

The generated surface follows the controls rather than being hand-designed, which is the same trade
`System.Text.Json`'s source generator makes and the same one `FEATURE-COST-POLICY.md` 5.1 argues for -
generate the shape instead of reflecting at run time.
## 5. What the generator emits

For each registered control, one node class, emitted into the manifest's namespace. It holds a tracked
property per settable control property, and the three members the reconciler needs:

```csharp
[LeafNode]                                                  // the control never arranges children
public sealed class VLabel : VNode
{
    private static readonly NodeType Kind =
        new NodeType(typeof(Label), () => new Label());

    // One control answers for every pristine value this node type needs.
    private static readonly Label Pristine = new Label();
    private static readonly string PristineText = Pristine.Text;

    private Specified<string> _text;
    public string Text
    {
        get => _text.Value;
        set => _text = new Specified<string>(value);
    }

    private Specified<Font> _font;
    public Font Font
    {
        get => _font.Value;
        set => _font = new Specified<Font>(value);
    }

    public override NodeType NodeType => Kind;

    public override void ApplyTo(Control control)
    {
        var target = (Label)control;
        if (_text.IsSpecified) target.Text = _text.Value;
        if (_font.IsSpecified) target.Font = _font.Value;
    }

    public override void PatchFrom(VNode previous, Control control)
    {
        var old = (VLabel)previous;
        var target = (Label)control;

        if (!_text.Equals(old._text)) target.Text = _text.IsSpecified ? _text.Value : PristineText;
        if (!_font.Equals(old._font)) target.Font = _font.IsSpecified ? _font.Value : PristineFont;
    }
}
```

The three members the reconciler drives are **public rather than internal**, because the generated
class is emitted into the consuming assembly and an internal contract cannot be overridden from
there. They carry `[EditorBrowsable(Never)]` so they stay out of IntelliSense for an author who is only
writing trees: they are the generated-implementation contract, not the API an application writes
against.

Five things to notice:

- **`Specified<T>` is machinery, not a calling convention.** It is public only because generated code in a consuming assembly has to name it (and is hidden from IntelliSense for that reason). It survives as the field type
  behind a tracked property - a value plus a was-set flag - but it never appears in a signature a
  developer writes, and its `Reset` member is gone, because absence already means "not assigned".
  This is what removed the public wrapper, the implicit operator, the nullable-conversion trap and
  the CS0411 generic-inference note from the design (section 10).
- **The setter constructs a `Specified<T>` rather than relying on an implicit conversion, and there is
  no implicit conversion to rely on.** An assignment operator taking `T` is only applicable where the
  source converts to its parameter by a *standard* conversion, and for a nullable reference type
  argument - `Specified<string?>` from a `string?`, the very case whose point is that null is a value -
  it does not. Measured on the shipping compiler: `_text = value;` is CS0029 for `Specified<string?>`
  and compiles for `Specified<string>`, so the operator would have worked for most properties and
  failed for the interesting ones. The constructor call has no such rule to satisfy, so the operator
  was deleted rather than documented.
- **One control is constructed per node type, not per property.** `Pristine` is what "restore the
  value the constructor left" reads from, and reading it off a fresh `new Label()` per property would
  construct a control for every property a control has - dozens - the first time the node type is
  touched.
- **`[LeafNode]` is generated, not written.** It marks a node whose control never overrides `OnLayout`,
  which is where a container decides where its children go; the analyzer uses it for VD011
  (section 6). A hand-written node can carry it too.
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
| VD001 | The attributed type is not a `Control`. |
| VD002 | No accessible parameterless constructor. |
| VD003 | Two registrations produce the same node class name (`Name` on the attribute fixes it). |
| VD004 | The same type is registered more than once, in one manifest or across several. |
| VD005 | `Name` is not a valid C# identifier. |
| VD007 | The manifest class is not `partial`. |
| VD012 | A public settable property has the same name as a member the node class already has - the `VNode` base's `Key`/`Children`/`NodeType`/`ApplyTo`/`PatchFrom`, or a static field the emitter writes (`Kind`, `Pristine`). Reported as an error, because silently dropping it would leave a property that cannot be set declaratively. A *hidden* base property is not this case: the derived declaration is the one a caller sees and the one the node keeps, silently - `TextBox` and `RichLabel` both hide properties and both have to work. |

Plus two from the analyzer half, both reported as **errors** rather than warnings:

| Id | Condition |
| --- | --- |
| VD010 | A `null` is assigned to a node property whose type is non-nullable. C# catches this as CS8625 and CS8604, but as warnings, and not at all in a file with `#nullable disable` - where the assignment would silently store a null and throw from the control's own setter. |
| VD011 | `Children` is assigned on a node marked `[LeafNode]`, that is, one whose control does not arrange children. |

VD010 needs no lookup table: it reads the assigned property's declared type, which is the control's own
type because the node flattened it. It works from ordinary C# - any assignment to a property of a
`VNode`, in an object initializer or written out as a statement - and treats three cases as the caller's
business:

- **`null!`** is the caller overruling the analysis, so it stays quiet and the run-time backstop is
  what speaks. The postfix `!` is recognised in the syntax, because a null-forgiving operator is not
  visible in the operation tree.
- **A nullable property** is a property where null is a value, so `Token = null` is a real instruction.
  This is the case the three-state design exists for (section 6.1).
- **The constant, not just the flow state.** A null literal is recognised by its constant value, which
  the semantic model reports whether or not the nullable context is enabled - which is the whole
  reason VD010 exists, since in a `#nullable disable` file the flow state says nothing at all. A
  possibly-null *value* is reported from the flow state, which is exactly the CS8604 case.

VD011 leans on the `[LeafNode]` attribute rather than on a table of leaf controls: every `Control` has a
`Controls` collection, so children assigned to a `Label` are accepted and then painted by nobody, and
`OnLayout` is where a type answers whether it arranges them. `Children = []` says what not assigning it
says, so it is allowed.

The analyzer lives in the same assembly as the generator, which is referenced as an `Analyzer`, so
both arrive together and neither reaches the deliverable. It analyses user code only
(`GeneratedCodeAnalysisFlags.None`), and the generated trees are recognised as generated by their
`<auto-generated/>` header.

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
child's type (section 11 lists it as the fiddliest part of the design).

### 6.2 Fragments and the empty node

A `VFragment` is a node with no control and any number of children. It exists because two things a
render has to be able to say have no node to say them: *these five nodes, at the top level* and
*nothing*. Every other node describes exactly one control, so the first can only be expressed by
inventing a container - which lays out, paints and takes a slot in the z-order the tree did not ask
for - and the second cannot be expressed at all.

The reconciler inlines it. `ReconcileChildren` replaces each fragment in a child list with its
children, recursively, before anything is matched; a fragment is therefore never matched, placed,
patched or unmounted, because by then it is not in the list. `[a, fragment([b, c]), d]` and
`[a, b, c, d]` reconcile identically - same controls, same order, same identity across renders - and
`new VFragment()` reconciles into nothing at all.

Three consequences follow from inlining rather than from a choice:

| Consequence | What it means |
| --- | --- |
| **A fragment is not a sibling** | Its children are matched among *all* of the parent's children, so a `Key` has to be unique across the whole list and not per fragment. |
| **A key on the fragment is refused** | Inlining leaves nothing for a key to identify. Ignoring it would silently rename the identity the caller meant to control, so it is an `InvalidOperationException` that names the node. |
| **Nesting is ordinary, cycles are not** | Inlining is recursive, so a fragment that ends up containing itself would never finish: past 64 levels it throws instead of taking the stack with it. |

The empty fragment is what lets `ObservableComponent.RenderWithNullModel` default to showing nothing
(section 11 of [`OBSERVABLES.md`](OBSERVABLES.md)), and it needed one thing the rest did not: a mount
that owns no controls. `MountHandle` therefore owns a *slice* of its parent's children - a list, which
a root fragment fills with two controls or none - and `IsMounted` is a state rather than "there is a
root control". A component whose render is an empty fragment is mounted, listens to what its render
read, and holds its place, so the controls its next render makes go where the parent said the child
is rather than at the end of the parent.

The asymmetry that survives is that a component's slot is one record in its parent however many nodes
it renders. Placement therefore carries a running total of `Mounted.ControlCount` and asks a child
mount to place its own span, which is what keeps the sibling after it from being laid out over the top
of what the child rendered.

## 7. The application-facing API

The plan's surface, in the order a developer meets it:

| Piece | Shape | Purpose |
| --- | --- | --- |
| `VirtualDomAttribute` | `[VirtualDom(typeof(Label), Name = "...")]`, `AllowMultiple` | The manifest entry (section 3). |
| `VNode` | Abstract base: `Key`, `Children` (init-only `IReadOnlyList<VNode>`), and the abstract `NodeType`/`ApplyTo`/`PatchFrom` members the reconciler drives | The description (section 3). Everything a developer writes derives from a generated class over this. |
| `V<Control>` (generated) | One class per registered control: a tracked property per settable control property, plus `Create`, `ApplyTo`, `PatchFrom` and a `NodeType` | What a view actually constructs (section 5). `Specified<T>` is a private field type inside it, never a public signature. |
| `IComponent` | `VNode Render();`, `void Attach(ComponentMount);`, `void Detach(ComponentMount);` | Everything the mount needs from a component and nothing else - the seam a base class of one's own implements (section 7.3). `SetState` is deliberately absent: it is the application-facing half, which the mount never calls. |
| `Component` | `IComponent`, plus `void SetState()` | The supplied view model base. `SetState` marks the component and asks for a frame; the render itself runs at the start of the next frame. |
| `ComponentMount` | `IComponent`, `Root`, `IsMounted`, `Invalidate`, `Dispose` | What `Mount` returns for a component: the handle a window owns, which is also how a `SetState` finds something to patch. |
| `VComponent` | A `VNode` wrapping an `IComponent`, with the inherited `Key` | A component as a child of another component's render (section 7.2). |
| `VirtualDomRuntime.Mount(Control parent, IComponent component)` | Returns a `ComponentMount` | The same entry point with the scheduling wrapped up (section 7.1). |
| `VirtualDomRuntime.Mount(Control parent, VNode root)` | Returns a `MountHandle` | Turns a tree into live controls under a container (typically the window's root panel). Named `VirtualDomRuntime` because a type called `VirtualDom` would sit in the `Kolvir.Feature.VirtualDom` namespace and shadow it at every call site. |
| `MountHandle.Update(VNode root)` | Reconciles the new tree against what is mounted | What a component's render calls; also usable directly, without components, for a one-shot tree. |
| `MountHandle.Unmount()` | Detaches everything and drops references | Window closing, or replacing a whole screen. |
| `services.AddVirtualDom()` | `IServiceCollection` extension | **Not in step 8.** This feature runs no code of its own: there is no timer, no hook and no process-wide state, so there is nothing for a container to hold, and an empty registration would be ceremony that lies about being the opt-in. The opt-in is the `Mount` call, and an application that references the package without ever calling it keeps nothing, because none of the code is reachable. The row stays for step 9, which does have something to register - the reflective table - and that is when a `ConfigureServices` call becomes worth writing. |

A component is the whole of the state machinery: there is no observable, no binding, no notification
and no scheduler. That is a decision rather than a gap - see section 7.1.

A component can be used without the component layer at all (`Mount` + `Update` by hand), and the
component layer is a thin wrapper over that. Keeping the two apart is deliberate: it means the
reconciler is testable without any state machinery, and an application that wants its own scheduling
can have it.

### 7.1 What `SetState` means

The loop is one line at the call site, and it does not render:

```csharp
private void OnAddClick(object sender)
{
    _count++;
    SetState();          // marks the component; the next frame renders it
}
```

`SetState` is a flag, a queue entry and a request for a frame. Everything queued is rendered at the start
of the next frame any host draws, which is where `UiHost.Paint` raises its frame-starting handlers - the
one point where the tree can still change and still be drawn by the frame that is starting.

The consequences, which are the reason for the design:

- **A handler that changes five things costs one render and one tree update**, not five of each. A list
  edited in a loop, or a form handler that sets three fields, cannot thrash layout: the tree is touched
  once, in the frame that shows the result.
- **`SetState` is cheap enough to call casually.** It allocates one queue entry, and a second call before
  the frame is a no-op rather than a second render.
- **A test draws a frame** (`Scene.Frame`, which calls the real `Host.Paint`) between the state change
  and the assertion. That is not a testing compromise: the frame is when the work happens, so a test
  that skips it is asserting on something the application would not yet have done.
- **The first render is not deferred.** A mount has to produce a control before it can be placed, so
  `Mount` renders once, synchronously, and every later render waits for a frame.

Four decisions follow from that:

| Decision | Why |
| --- | --- |
| **Coalesced to the next frame, not to a timer.** No scheduler, no `await`, no debounce. | The frame is the deadline that already exists and is already the moment the screen changes, so waiting for anything else would either delay the update for no reason or render more often than the screen can show it. |
| **`Render` must not change state, and is not iterated to a fixed point.** | A render is a description of the state it was given; one that changes the state has described a tree that is already out of date, and there is no fixed point to settle on. So the settling loop and its hundred-pass cap are gone, and `SetState` from inside a render is refused with a message naming the component. This is what buys the one-and-done pass. |
| **No live tree, nothing to do.** A `SetState` before the first mount and a `SetState` after the mount was disposed are both no-ops. | The two states with no tree to patch get one rule rather than two. Before a mount nothing is lost, because the first render reads the state as it is by then - so storing state first and mounting second is a legal order. After one there is no screen left to correct, and an event that arrives after its window closed is ordinary. A component that is *never* mounted shows no controls at all, and that absence is the diagnosis; an exception at the first state change would say less, and would make construction order matter. |
| **The batch is taken before any render runs.** | A render can dispose a mount that is still waiting its turn - a list that removes a row while both are queued - so the batch is a snapshot and a disposed mount renders nothing. Which of the two orders a frame happens to take is invisible in the result, and both are tested. |

The queue is process-wide rather than per window, because a mount can exist before any window holds it.
Every host that draws drains it, so a second window painting simply finds an empty queue; the work that
was deferred is drawn by whichever frame comes first, which is the frame the user is about to see. Its
subscription to the frame is never removed, because the alternative was measured: an idle check is one
delegate call and an integer compare (about 3 ns against a 5,000 ns frame), while subscribing and
unsubscribing around each active frame measured 400 ns a frame and allocates a handler token.

A component owns its whole tree, and the tree can hold other components. A child component is a node -
a `VComponent` - so a parent decides that a child is there and where it sits, and the child decides what
it looks like. Section 7.2 is the whole of that rule.

A component owns its whole tree, and the tree can hold other components. A child component is a node -
a `VComponent` - so a parent decides that a child is there and where it sits, and the child decides what
it looks like. Section 7.2 is the whole of that rule.

### 7.2 Components as children

A component puts another component in its output by emitting a `VComponent` for it:

```csharp
new VPanel
{
    Dock = DockStyle.Fill,
    Children = [.. _rows.Select(row => new VComponent(row) { Key = row })],
}
```

The reconciler mounts it: the child gets its own `ComponentMount`, and the control the child's render
produces is placed **directly** in the parent's control, with no wrapper of its own - which is one
thing this costs less than a React-shaped framework, where a component boundary usually needs a
container element or a presenter control to exist.

| Rule | Why |
| --- | --- |
| **The parent owns the place, the child owns the content.** | The parent decides whether there is a child here and in what order; the child decides what it says and re-renders itself when its own state changes. That division is what keeps a child's state out of the parent's, and what makes the two render counts move independently. |
| **A slot is matched by component instance.** | Emitting the same component again keeps its mount, so a parent's render neither re-renders the child nor rebuilds its controls: caret, scroll position, selection and focus all survive. A different instance in the same slot is a different component, so the old mount is disposed and the new one rendered fresh - nothing can be inherited across that boundary. |
| **A child that can move needs a `Key`.** | Without one, children are matched by position, so a reorder pairs each slot with its neighbour's component, finds a different instance, and rebuilds both. With one, the controls are moved, which is the same guarantee section 6 gives an ordinary keyed list. |
| **A child's `SetState` renders the child and nothing else.** | The child's mount patches the child's own subtree, and both components ride the same frame batch. The parent is not involved, so a keystroke in one row cannot re-render the list or its siblings. |
| **A parent's `SetState` does not render its children.** | The child's subtree belongs to the child's mount. A parent render walks past it, which is the point: a parent that renders ten frames running still costs one render per child, and only for the children that asked for one. |
| **One component is one mount.** | The same instance emitted twice is refused rather than mounted twice, because the second mount would silently take over the component and leave the first patching a tree nothing points at. Give each place its own instance. |
| **Removing a child node disposes its mount.** | Its controls are removed and its state is no longer rendered, because the parent said the child is not there any more. Unwinding is recursive: a removed child takes its own children with it. It has to be, because a component can sit below ordinary nodes - a `VPanel` in a render holds the child's mount in the panel's record, not the root's - and a record dropped without disposing that mount leaves the child rendering into a control no tree points at, and refusing to be mounted again because it still believes it is mounted. |
| **A render that throws is the application's to handle, and does not strand the batch.** | The exception goes to `Application.HandleNativeBoundary`, the same place every other exception raised from a frame goes, and the rest of the batch still renders. Letting it out of the drain instead would strand every component behind it: their entries have already left the queue and their pending flag makes the next `SetState` a no-op, so they would never render again. For the same reason the flag is cleared before a render runs, so a component that threw renders on the next frame like any other. A boundary that chooses to rethrow still leaves the batch reachable rather than lost. |
| **A first render that throws leaves nothing half-built.** | The mount attaches the component before the first render (it has to, or a render that changes state could not be caught), so a throw there detaches again and rethrows. Otherwise the component would point at a mount that never produced a root, and every later `SetState`, plus any attempt to mount it again, would be a null dereference. |

A `VComponent` is a slot and not a control, so it cannot be the root of a `MountHandle` - a root
component is mounted as a component, with `Mount(parent, component)` - and it cannot be given
`Children`, because the child's own render is what says what the child contains. Both are refused with
a message rather than quietly ignored.

### 7.3 What the mount needs, and what it does not

The mount does not need a `Component`. It needs a small protocol, and the difference matters because a
component base class is exactly the kind of thing an application wants its own version of: a `record`,
a source-generated view model, a wrapper over an existing one, or one that schedules differently.

| Member | Who calls it | Why the mount needs it |
| --- | --- | --- |
| `VNode Render()` | the mount, once per frame in which the component was invalidated | it is the tree |
| `void Attach(ComponentMount mount)` | the mount, before its first render | so the component has a mount to invalidate, and can refuse a second live one |
| `void Detach(ComponentMount mount)` | the mount, on dispose, and on a first render that threw | so the component stops pointing at a mount that is gone - and can be mounted again |

`SetState` is **not** a member. The mount never calls it - it is the application-facing half, the call an
event handler makes - so requiring it would force every implementation to reproduce a spelling the mount
has no opinion about, and to decide for itself what "state" means. What the interface does need behind
that spell is the primitive, `ComponentMount.Invalidate()`, which is public for exactly this
reason. `Component.SetState` is one line over it, and a base class of one's own calls it from its own
setter. Without that, `IComponent` would be a seam nothing outside the assembly could use.

| Decision | Why |
| --- | --- |
| **The bookkeeping is a field on the implementation, not a table on the mount.** | A side table keyed by component would be a static registry a component could be silently double-registered in, and a weak-keyed one cannot work at all here: the value is the mount, the mount holds the component, and a strong path from value to key keeps the key alive, so nothing is ever collected. A field on the implementation is faster and correct, and the interface is what lets any implementation have one. |
| **`Attach`/`Detach` are explicit implementations on `Component`.** | They belong to the mount's half, so `Component`'s own surface stays `Render` and `SetState` rather than growing two calls an application should never make. |
| **`VComponent` and `Mount` take the interface.** | Otherwise a component with its own base class could be mounted but not composed, and composition is where the two halves of the design meet. |

## 8. The application level

The control layer owns one window's body. The application layer owns the windows themselves, so an
application whose window set follows a model is written the same way a control subtree is: derive a
component, override `Render`, read the model, mutate it, and let the framework re-render. The only
differences are that the node is a window descriptor and the top-level render returns a list of them.

```csharp
internal sealed class Shell : ApplicationComponent
{
    private readonly IObservableList<Document> _documents = Observable.CreateList<Document>();
    private readonly IObservableMutableValue<int> _geometryRevision = Observable.CreateValue(0);

    protected override IReadOnlyList<ApplicationVNode> Render()
    {
        var geometryRevision = _geometryRevision.Value;

        return
        [
            new VWindow
            {
                Key = "list",
                Title = "Documents",
                ClientSize = new SKSize(480f, 320f),
                Content = new VComponent(_listPage),
            },
            .. _documents.Select(document => new VWindow
            {
                Key = document,
                Title = document.Title.Value,
                ClientSize = new SKSize(480f, 300f),
                Location = document.Location,
                GeometryToken = geometryRevision,
                CloseRequested = _ => _documents.Remove(document),
                Content = new VComponent(document.Page),
            }),
        ];
    }
}
```

The list page and each document's page are held with the model rather than constructed in `Render`,
which is what keeps the child component's mount (and its controls) across the application's own
re-renders; a new page instance in the same `Content` slot would be a different component and would be
mounted fresh. `Render` runs inside the same collection scope `ObservableComponent` uses, so every observable value
and named dependency the render reads subscribes it. The model can be any fields the component keeps;
there is no `DataModel` property because an application is one component with one model, and the
generic wrapper would only be ceremony. `Invalidate()` is the one render on the next frame that
`SetState` is, and calling it from inside a render is refused the same way.

| Piece | Shape | Purpose |
| --- | --- | --- |
| `ApplicationVNode` | Abstract base: `Key`, and nothing else today | The application description. Deliberately not a `VNode`. |
| `VWindow` | `Title`, `ClientSize`, `Location`, `State`, `Resizable`, `TopMost`, `Icon`, `MenuBar`, `MinimumClientSize`, `MaximumClientSize` as tracked properties; `GeometryToken`; `Content`; `CloseRequested`, `Moved`, `Resized`, `StateChanged`, `Activated`, `Deactivated` | One window, with the same omitted/set/unassigned distinction a control node has, plus the notifications for changes made outside a render. |
| `IApplicationComponent` | `IReadOnlyList<ApplicationVNode> Render()`, `Attach(ApplicationMount)`, `Detach(ApplicationMount)` | The mount's half of the protocol, exactly as `IComponent` is the control mount's half. |
| `ApplicationComponent` | The interface, `Invalidate()`, and `IDisposable` | The supplied base class. |
| `ApplicationMount` | `Invalidate()`, `Dispose()` | Owns the real windows and their content mounts. |
| `VirtualDomRuntime.Mount(Application, IApplicationComponent)` | Returns an `ApplicationMount` | The application-level entry point; the two control-level overloads are untouched. |

### 8.1 Why `ApplicationVNode` is not a `VNode`

A `VNode` is a contract with the control reconciler: `NodeType`, `ApplyTo`, `PatchFrom`, and
`Children`. A window has none of those in that form. `NodeType` would be a type that cannot be
created, `ApplyTo` and `PatchFrom` would take a `Control` they can never receive, and `Children` is a
list of control nodes rather than an owned-window list. Deriving would therefore ship four members
whose only implementation is a throw.

Keeping the hierarchies apart does something a run-time check cannot: a `VWindow` in a control tree
and a `VLabel` in an application render are compile errors. It also means an application node can
never be handed to `VirtualDomRuntime.Mount(Control, VNode)`, and a control node can never be
mistaken for a window. The application reconciler is small enough that the separate base costs
nothing, and it is the seam a tray-icon or menu-bar node will extend without disturbing the control
contract.

### 8.2 Identity, creation and patching

Windows are matched by `VWindow.Key` first and then by position among the unkeyed ones - the same
rule `Reconciler` applies to control children. Keys have to be unique among the windows one render
returns, because a key identifies a window rather than merely decorating it; a duplicate is refused
with a message that names it. A matched window keeps its OS `Window` instance, so a reorder moves the
window in the render's list without recreating it, changing focus, changing geometry, or resetting the
display state.

A new window is created by constructing a `Window`, capturing its pristine property values, creating
a fill `Panel` that becomes `Window.Content`, mounting `Content` into that panel, applying the
properties this node assigned, and showing the window. Several new windows in one render all appear
before the frame that asked for them is painted.

Patching follows the control rule exactly: only a property whose tracked value changed is assigned, a
property the previous node assigned and this one does not is restored to the pristine value captured
from that window, and a property whose value is unchanged is not assigned at all. The last part is
what keeps a user's resize or move from being snapped back by a render that repeats the size or
position it originally asked for.

Assigned geometry is therefore a suggestion, not an ongoing command: a render that keeps assigning
the same `ClientSize`, `Location`, `State`, `MinimumClientSize` or `MaximumClientSize` leaves a
window where the user put it. `VWindow.GeometryToken` is how an application asks for the geometry
back. When the token differs from the previous render's token, every one of those five properties the
node assigns is applied again even though its value did not change; when the token is unchanged,
nothing is re-applied. The token is compared with `object.Equals`, the same way `VWindow.Key` is, so
a stable object, a string, or a monotonically increasing counter all work. A counter is the usual
choice for a "tidy windows" or "reset layout" action, because incrementing it is the statement "take
the assigned geometry back now"; the demo's "Tidy windows" button does exactly that. Only geometry
answers to the token: `Title`, `Resizable`, `TopMost`, `Icon`, `MenuBar` and `Content` keep the
ordinary changed-value rule. An assignment that is withdrawn still restores the pristine value,
whether the token changed or not.

The pristine values are captured per window rather than from a static snapshot, because a window's
defaults are not wholly fixed: the icon and menu bar start from the application's options, and the
size and location come from the backend. A window built from nothing is the authority on what it
looked like before a node touched it.

### 8.3 The control boundary

`VWindow.Content` is the only place the two hierarchies meet. It is an ordinary `VNode`, and the
mount reconciles it with `MountHandle`. It may also be a bare `VComponent`, which is mounted through
the existing `Mount(Control, IComponent)` overload and keeps its own state and render count - so
`Content = new VComponent(page)` works without a wrapper panel. The host panel is the same either way,
so changing the content root from a control node to a component root and back is one mount disposed
and the other started, not a second window.

`MountHandle` still refuses a `VComponent` as its own root. That is unchanged and deliberate: a tree
handle owns control nodes, and a component root is mounted by the component mount. The application
mount is what chooses between the two overloads.

### 8.4 Closing, and what the framework refuses

A window disappears when a later render omits its node: the mount disposes the content mount and
closes the window. That is the framework's own close and it never raises `CloseRequested`.

A close the framework did not initiate - the platform's X button, or a direct `Window.Close()` - is
always cancelled, and the node currently on screen has its `CloseRequested` handler invoked. Four
consequences:

| Rule | Why |
| --- | --- |
| **No handler means nothing happens.** | The window stays open, which is the only safe default: closing it would let a platform button destroy state the model still believes in. |
| **The handler is read from the latest node.** | A render can replace the delegate, so a closure over current state is current without recreating the window. Reading the previous node would leave it holding last frame's document. |
| **It runs outside a render.** | Changing the model and calling `Invalidate()` there is legal; the window closes when the render that follows omits it. |
| **The application's own exit is let through.** | `Application.Exit()` closes every window through the same closing handler. Cancelling that would veto every exit, so `Application.IsExiting` is the one signal the handler consults: while it is true, the close belongs to the application shutdown and is allowed. |

`Application.IsExiting` is a small addition to the core `Application` type for exactly this
distinction. Without it the application level would either have to block `Exit()` forever or lose the
rule that a native close can be refused.

### 8.5 Batching, the first render, and empty output

Application renders are coalesced like control renders: `Invalidate()` marks the mount and asks for a
frame, and the marked mounts render once at the start of the next frame any host draws, through the
same `UiHost.AddFrameStartingHandler` tick `RenderQueue` uses. Window creation, patching and closing
therefore happen at a frame boundary, never inside a control reconcile pass and never inside a render.
The queue is separate from `RenderQueue` but has the same two properties that matter: a disposed mount
is forgotten before its turn, and a render that throws is reported to the application while the rest
of the batch still runs.

The first render is the exception, and it happens inside `Mount` before `Application.Run()`. It has to:
with no window yet there is no host drawing a frame, and therefore no frame in which the first window
could be created. For the same reason, a component whose first render yields no windows has nothing to
request a frame from, so an application that will later open its first window has to make that window
part of the first render. Empty output is otherwise ordinary: no windows, no special shutdown path,
and `CheckShutdownMode` sees the last window close in the usual way.

### 8.6 Owned windows

An owned window - a `VWindow` whose owner is another `VWindow` - is not supported yet. There is no
owner property today, so the shape is reserved rather than half-built: when it arrives it will be a
property on the window node, with the owner matched like any other window, not a child node. This is
a not-yet-supported boundary, not an illegal one.

### 8.7 Window notifications

A `VWindow` can carry handlers for changes made outside the render, in the same style as
`CloseRequested`:

| Handler | Payload | Meaning |
| --- | --- | --- |
| `Moved` | `SKPoint` | The user (or the platform) moved the window. |
| `Resized` | `SKSize` | The user resized the window's client area. |
| `StateChanged` | `WindowState` | The window was minimized, maximized, restored, or otherwise changed state. |
| `Activated` / `Deactivated` | none; the window is the sender | The window gained or lost focus. |

The rule that matters is what does **not** raise them: a change the mount applied from the render.
Creation, a changed assignment, the `GeometryToken` re-assert, and the pristine restore when an
assignment is withdrawn are all the render's own work, so they are suppressed while the mount is
applying them. If they were announced, every render that assigns `ClientSize` or `Location` would
echo straight back into the model - noise at best, a feedback loop at worst. These handlers mean
"something outside the render changed this window", and that is the distinction to rely on.

The handlers are read from the most recently rendered node, so a render can replace a delegate and a
closure over current state is current without recreating the window. Nothing is raised once the
window is gone. `StateChanged` deliberately carries the state rather than being split into
`Minimized`, `Maximized` and `Restored`: one handler is enough to follow the window, and the state
value says which transition happened. There is deliberately no `Closed` handler either: the
application closes a window by omitting it from the render, so a close notification would only tell
the model what it just did itself.

The intended pattern is a loop that settles by itself. The handler writes the window's new value into
the model, and the next render assigns exactly that value:

```csharp
new VWindow
{
    Key = document,
    Location = document.Location.Value,
    ClientSize = document.ClientSize.Value,
    State = document.State.Value,
    Moved = (_, location) => document.Location.Value = location,
    Resized = (_, size) => document.ClientSize.Value = size,
    StateChanged = (_, state) => document.State.Value = state,
    Content = new VComponent(document.Page),
}
```

The user moves the window; `Moved` writes the model; the write re-renders; the render assigns the
value the user already chose. The ordinary unchanged-assignment rule then leaves the window alone, so
one render settles it and a later frame does not move it again. `GeometryToken` remains the opposite
action: the way to take assigned geometry back after the user changed it.

`Moved` is limited by the platform, not by the node. Windows and macOS report real moves; GTK4 has no
supported move signal, and Wayland does not expose a surface position at all, so `Moved` never fires
there. Do not infer a position from assignments: an application that needs a location on GTK4 reads
`Window.Location` at the moment it needs it.

## 9. Steps

| # | Step | Scope | Done when |
| --- | --- | --- | --- |
| 1 | Package skeleton and the node model | `Kolvir.Feature.VirtualDom` project in the rung-3 layout, `VNode`, `NodeType`, the internal `Specified<T>` field type, hand-written node classes for two controls | A tree can be built by hand with object initializers and asserted on; the project packs with the same `lib/` folders as the other features |
| 2 | Mount, and applying a node | `VirtualDom.Mount`, create-only reconciliation, `MountHandle`, `ApplyTo` on the hand-written nodes | Mounting a hand-built tree puts a real `Label` with the right text under a container |
| 3 | Patching and the pristine snapshot | `PatchFrom`, tracked properties, the pristine snapshot captured from a fresh control on first use, `Update` | A changed tree patches in place: the same control instance survives, a property that stops being set returns to its pristine value (not `default(T)`), and one that stays set to the same value is not re-assigned |
| 4 | Structure: create, replace, unmount | `NodeType` mismatch handling, positional children, subtree unmount, event detach | Inserting, removing and retyping children mid-list leave a correct tree and no leaked handlers |
| 5 | Keys and reordering | Keyed matching, mixed keyed/unkeyed lists, minimum move sequence over `ControlCollection.Move` | Reversing a keyed list moves controls instead of recreating them, proven by reference identity and by scroll position surviving |
| 6 | Events | Generated `AddXxxHandler`/`RemoveXxxHandler` mapping, delegate identity tracking | Re-rendering with the same handler subscribes once; a new handler replaces the old one |
| 7 | The source generator and its analyzer | Attribute, `ForAttributeWithMetadataName`, node classes with tracked properties, `ApplyTo`/`PatchFrom`, diagnostics VD001-VD005, VD007 and VD012, plus the analyzer half with VD010 and VD011 | The manifest in section 4 compiles, the generated classes pass the whole existing suite unchanged, and snippets that assign `null` to a non-nullable property or children to a leaf node fail the build - VD010 also with `#nullable disable` |
| 8 | The component layer | `Component`, `SetState`, the render queue drained by the frame, render-and-patch | A headless scene shows new controls on the frame after a `SetState`, and `Render` runs exactly once per frame in which the state changed |
| 9 | Reflective escape hatch | Annotated `Type` registration, `[RequiresUnreferencedCode]` unannotated overloads, the runtime table | A control registered by `typeof` at run time mounts through the reflective binder, and an AOT build warns on the unannotated overload |
| 10 | Demo, docs and AOT verification | A virtual-DOM card in the demo app, `DEVELOPER-GUIDE.md` sections, single-file AOT publish | The published exe renders the card identically to the JIT build, and the trimmer's report shows only the registered controls |

Steps 1-6 are written against two hand-written node classes so the reconciler is complete and tested
*before* the generator exists. The generator then replaces those classes without touching the
reconciler, which is what keeps step 7 a codegen change rather than a redesign.
### 9.1 Where the pieces live, and what else has to change

Four projects:

| Path | What it is |
| --- | --- |
| `src/features/Kolvir.Feature.VirtualDom/` | The runtime: `VNode`, `NodeType`, the internal `Specified<T>`, the reconciler (`MountHandle`), `Component`, `ComponentMount`, `VComponent` for a child component, `VirtualDomRuntime.Mount`/`Update`, `VirtualDomAttribute`. Four-folder rung-3 layout with all the code in `common/` and empty platform folders. |
| `src/generators/Kolvir.Feature.VirtualDom.Generators/` | `netstandard2.0`: the incremental generator and the `DiagnosticAnalyzer`. Deliberately **outside** `src/features/*/`, because the pack job globs that tree and would otherwise publish it as a package of its own. |
| `tests/features/Kolvir.Feature.VirtualDom.Tests/` | Headless tests, which consume the generated node classes through an analyzer reference - the thing a consumer does - so a codegen change is tested by the same suite that tested the hand-written classes. |
| `tests/generators/Kolvir.Feature.VirtualDom.Generators.Tests/` | The build-time half's own suite: it compiles a source string, runs the generator over it, then runs the analyzer over the result. That is the only way to assert what a manifest becomes and what a broken one reports. |

Five things outside the new code that the feature cannot land without:

1. **`Kolvir.slnx`** gets all four projects, under `/src/features/`, `/src/generators/`,
   `/tests/features/` and `/tests/generators/`. The solution is hand-listed, not globbed, and the
   folders are named by path because the SDK silently drops a nested `<Folder>`.
2. **`.github/workflows/tests.yml`** enumerates test projects one at a time - a `restore` step and a
   `test` step - so each suite is two added blocks; nothing is discovered automatically. No leg
   narrows a framework: a feature builds for the platform the runner is on plus the portable
   framework, from `KolvirTargetFrameworks` in `Directory.Build.props`, and only the pack job asks
   for every platform, by name. This item used to record the opposite - a scoped
   `-p:KolvirVirtualDomFrameworks=` for each suite, because a feature that listed all three
   frameworks made every restore follow all three into the backends behind them. The workflow's path
   filter needs `src/generators/**` as well, and so does `packages.yml`.
3. **XML documentation is mandatory and enforced.** `Directory.Build.props` sets
   `GenerateDocumentationFile` on anything that ships and raises **CS1591 as an error**, so every
   public member of the runtime assembly needs a `<summary>`. The two test projects are exempt by the
   `.Tests` naming rule. The analyzer's `EnforceExtendedAnalyzerRules` adds its own requirement:
   `AnalyzerReleases.Shipped.md` and `.Unshipped.md`, because a rule's identifier becomes part of the
   package's interface the moment it ships.
4. **The analyzer has to be packed, not just referenced.** A consumer of the package gets the
   generator and the analyzer from `analyzers/dotnet/cs/`, which is how `System.Text.Json`'s
   generator travels, added once rather than per target framework because pack runs once. In this
   repository the feature project does **not** reference the generator as an analyzer: it has no
   manifest of its own, so all it needs is the dll at pack time, and a project reference would put a
   `netstandard2.0` project into the graph of everything that references the feature. That matters
   because `dotnet publish -p:PublishAot=true` passes `PublishAot` as a *global* property, which
   reaches every project in the graph, and a `netstandard2.0` project cannot satisfy it - the publish
   stops with **NETSDK1207 on the generator itself**, in an application that never asked for one.
   Neither `UndefineProperties` nor `GlobalPropertiesToRemove` on the reference helps, because the
   check is made inside the referenced build.

   The generator is therefore built by a **separate `dotnet build` process** at pack time, and the
   sample takes it as a plain `<Analyzer>` dll built the same way. The first attempt used the
   `MSBuild` task with `RemoveProperties`, and it built the generator with **no reference assemblies
   at all** on a clean machine (CS0518 for `System.String`): the task inherits the calling build's
   global properties, and under `dotnet pack` there is no reliable way to un-inherit all of them. A
   new process starts from the environment, which is what a generator project needs, and `dotnet
   build` restores first, so a fresh checkout works. Measured: with `bin/` and `obj/` of the
   generator deleted, `dotnet pack` produces the package with the analyzer inside, and `dotnet
   publish -p:PublishAot=true` of the sample succeeds. The two test projects still take the generator
   as an analyzer reference, because a test project is never published.
5. **`pack-nuget` needs no change** - it globs `src/features/*/*.csproj`, and the layout checker only
   opens `lib/` and the dependency groups, so an extra `analyzers/` folder neither needs nor trips it.
   Verified by packing the feature locally and listing the `.nupkg`.
## 10. Options considered and rejected

| Option | Why not |
| --- | --- |
| Runtime registration: `VirtualDom.Register<Button>()` at startup | A generator cannot see the call's effect, and the trimmer cannot learn the type from it. Recovering safety means `[DynamicallyAccessedMembers]` plus a reflective binder - slower, and the reflective binder is the path with real AOT risk. Kept only as the escape hatch for types that genuinely arrive at run time (step 9). |
| `[DynamicDependency]` on the registration method | Compile-time constants only. It cannot express "whatever the application passes to this call". |
| String-named types and properties parsed at run time (a real DSL) | Property names as run-time strings need reflection the trimmer cannot see, are unverifiable until run time, and are slower. This is the one option that would make an AOT build genuinely unreliable. |
| Factory methods with `Specified<T>` parameters - the design this replaced | AOT-clean and workable, but it put a wrapper in every call site to answer a question a setter answers for free, and it did not actually make `null` safe: three of its four parameter shapes were measured mishandling it (section 6.1). Switching to a class with tracked properties deleted the wrapper from the public surface, the property-name strings, the `VProp[]` and the hashed name dispatch, and made the diff a comparison of typed fields. Kept here because the wrapper's problems are the reason for the current shape, not an accident of it. |
| Union types (C# 15 / .NET 11) as the three-state wrapper | Measured on SDK 11.0.100 against `net11.0`: a union with a non-nullable case, `union Prop(string, Absent, Reset)`, rejects `Take(null)` with **CS8625, still a warning**; with a nullable case (`string?`) `null` is silent, exactly as `Specified<string?>` is. The rule the compiler applies is "a null literal converted to a non-nullable reference type", which does not care whether the destination is a union case or an implicit operator, so a union changes the spelling and not the severity. Unions are otherwise a good fit - `union Prop<T>(T, Absent, Reset)` compiles, and "no case set" being the zero value is exactly the absent state with no inverted flag needed - but they need a `net11.0` leg in the package and buy nothing this design does not already have. Recorded as a revisit in section 11. |
| Promoting CS8625/CS8604 to errors from the package's `buildTransitive` props | No code at all, and it covers more than a null-literal analyzer can, because the compiler's flow analysis already handles a possibly-null variable. Rejected because it would silently make every null-to-non-nullable in a referencing project an error - a policy an application should choose for itself, not inherit from adding a UI package. It stays a documented one-liner (section 11). |
| `VNode.Of<T>(...)` generic factories, without a manifest or generator | AOT-safe (with an annotation on the type parameter) but verbose at every call site, and setting a property still needs a name/value pair. Acceptable as an internal primitive; rejected as the developer-facing syntax. |
| Hand-written static factories (`V.Label(...)` written by a person) | Perfect AOT story, but every control's surface is maintained by hand and drifts from the control. Only worth it for types the generator cannot see. |
| Lambda/builder closures (`V.Column(c => { c.Gap(8); })`) | Poor inference, awkward with LINQ, and it reads as imperative code rather than a description. |
| A markup file compiled by a generator (XAML/JSX-like) | The only way to get a genuine markup feel, because C# has no macro system. A large, additive front end - parser, generator, diagnostics, editor story. Explicitly deferred, not rejected: it lowers to the same node tree, so it can be added later without touching anything below section 4. |

## 11. Risks and open questions

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
  That is fine for event-driven UI, and renders are coalesced to one per component per frame, so a
  handler that changes five things still builds one tree. It stays unacceptable for per-frame
  animation - a component that re-rendered every frame would allocate a tree every frame - so the
  answer there is animating props of existing controls, and a `ShouldUpdate` subtree skip is the
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
- **Deferral relies on the frame.** A `SetState` that nothing ever paints is a state change that never
  reaches the screen, so the queue is drained from the frame-starting hook rather than from a timer and
  the sample's `--render` path proves a frame is all it takes. The risk that remains is an application
  that changes state while no window is painting at all, which is a program with nothing to show.
- **A render that throws leaves its own subtree however far it got.** The exception is reported and the
  application keeps running, but nothing rolls the controls back to the tree the previous render
  produced: a reconcile that threw after inserting two of five children leaves the two in place. Doing
  better would mean buffering every patch until the render completed, which costs a copy of the
  operation list per render to protect against a case that only exists while a bug is live - and a
  render that throws is a bug the report names. The next render of that component patches from the node
  tree it now holds, so it converges rather than staying wrong.
- **A component's slot may be several controls, so placement depends on a running total.** A fragment
  makes the number of top-level nodes a render produces a per-render quantity, so `ReconcileChildren`
  advances by `Mounted.ControlCount` rather than by one, and a child mount places its own span when the
  parent moves it. What keeps that arithmetic right is that a mount's controls stay contiguous in its
  parent from the position it was placed at. A control inserted into the parent by hand between a
  mount's renders would break it - the same assumption the single-root handle made before fragments
  existed, and no worse, but now it covers several controls instead of one.

## 12. Verification approach

Every step is checked with headless tests in `tests/features/Kolvir.Feature.VirtualDom.Tests`, which
build a tree, mount it into a `HeadlessScene`, and inspect the resulting `Control` tree - the same
approach the selection and layout work uses, and the reason none of this needs a window.

Eleven claims get special treatment, because they are the ones that would otherwise only fail in a
published application:

| Claim | How it is proven |
| --- | --- |
| **A frame-starting handler's work is drawn by the frame it was raised for** | `FrameStartingTests` in the core suite: a control added from a frame-starting handler is measured and arranged by the paint that is already underway, and a handler is called once per frame and told which host is drawing. That is the property the whole deferral rests on. |
| **No mirror drift** between a control and its generated node class | For every control the test manifest registers, a test enumerates the control's public instance settable properties and asserts the generated class exposes each one **with the same property type**. Types matter as much as names here: a node property typed `object` would compile and then fail at the assignment. The test fails on the day a property is added to a control without regenerating. |
| **Omitted, set, and set to null stay distinct** | `new VLabel { Text = "" }` clears the text while `new VLabel { }` says nothing about it; a render that drops a property restores the pristine value, asserted against properties whose pristine value is not `default(T)` (`Text` starts at `""`, `Name` at the type name, a control's `Dock` may start at `Fill`); and `Font = null` arrives at the control as null rather than as absence. |
| **Patches instead of rebuilds** | Assertions on reference identity: the control instance is the same object across a render, and keyed reorders preserve specific state (a `RichLabel`'s layout is not rebuilt, a `Panel`'s scroll position is unchanged). |
| **No handler leaks** | A re-render with the same delegate leaves the click count at one, proven by raising the event through `Control`'s own trigger. |
| **Composition keeps the two halves apart** | `CompositionTests` holds the directions separately: a child's `SetState` raises the child's render count and leaves the parent's alone while the screen follows the frame, a parent's render leaves the child's count and its control identity untouched, a keyed reorder moves the children's controls by reference rather than rebuilding them, a different instance in the same slot leaves the replaced child with nothing to render, a mount disposed by an earlier render in the same batch renders nothing, and nesting three levels deep changes nothing (section 7.2). The seam takes the same treatment: `ComponentTests` mounts a component that implements `IComponent` directly, with no `Component` above it, and `CompositionTests` composes one as a child, so "the mount needs the interface and not the base class" is a test and not a claim (section 7.3). |
| **A failure inside a frame stays inside it** | Four `CompositionTests` name the shapes a render can fail in, and each was confirmed to fail with its fix removed rather than merely pass with it: a render that throws in the middle of a batch is reported to the application and the components behind it in the same frame still render (without the per-entry catch, the one after it reports one render instead of two); the component that threw renders again on the next frame; a component whose *first* render throws can be mounted once it stops (without the detach, it is a `NullReferenceException` at the next `SetState`); and unmounting a subtree that holds a component below ordinary nodes disposes that component, so it stops rendering and can be mounted elsewhere (without the recursion, it renders a second time). |
| **A `null` for a non-nullable property does not build** | `tests/generators/...Generators.Tests` runs the analyzer over a real compilation and asserts VD010 for a null literal, for a possibly-null variable, and for both under `#nullable disable`; and asserts nothing for a value, for a nullable property, and for `null!`. The end-to-end wiring - an analyzer arriving from the package and failing a build - was checked by hand: a `Children = [child]` on a `VLabel` in the feature suite stops its build with VD011. |
| **AOT- and trim-clean** | Measured on Windows, `win-x64`: `dotnet publish -c Release -r win-x64 -p:PublishAot=true` of the sample succeeds and its `--render virtual-dom` output is **byte-for-byte identical** to the JIT build's (57,658 bytes for `--page virtual-dom` at the default window size, compared with `SequenceEqual`, and two JIT runs measured identical so the comparison is meaningful rather than lucky), so the generated node classes, the reconciler, the component layer, the composition path and the frame-drained render queue all survive native compilation unchanged. `Kolvir.Minimal`, which does not use the feature, publishes with **no virtual-DOM artifact at all** in its output. What this does *not* cover: the other two platforms, and a publish from the release workflow rather than a developer machine. |
| **The generator does not get in a publish's way** | `PublishAot` is a global property and a `netstandard2.0` project cannot satisfy it, so a project reference to the generator fails an AOT publish with NETSDK1207 (section 9.1, item 4). The feature carries no such reference, and both the sample and the pack build the generator in a `dotnet build` child process, where the inheritance that causes it does not exist. Verified from a deleted generator `bin/` and `obj/`, because the first attempt - the `MSBuild` task with `RemoveProperties` - passed locally and then built the generator with no references at all on a clean machine. |

| **A fragment is inlined, and an empty one takes no room** | `FragmentTests` holds twelve cases (section 6.2): a fragment's children take its place among the parent's siblings by reference identity, nesting flattens, a root fragment owns one control per child and a root empty one owns none but takes what it is given later, an empty child component occupies no slot and appears in its own place when it has something to show, a grandchild component inside a fragment gets the right slot, a component that renders two nodes does not have its parent's sibling laid out over it, and the key and cycle refusals are asserted by message. The placement arithmetic was also seen failing with its fix removed: writing the index as `startIndex + i` instead of a running total breaks three of them. |

The demo app gains a virtual-DOM card in the same shape as the existing feature cards, so the
published-build render comparison covers the new code path automatically.

## 13. Progress

| Step | State | Notes |
| --- | --- | --- |
| 1 | Done | `Kolvir.Feature.VirtualDom` in the rung-3 layout: `VNode`, `NodeType`, the public-but-hidden `Specified<T>`, `Mounted`, `Reconciler`, `MountHandle`, `VirtualDomRuntime`. Hand-written `VLabel`/`VNote`/`VButton`/`VPanel` stand in for the generator, so steps 2-6 test the reconciler before any codegen exists. 26 headless tests. |
| 2 | Done | `Mount` creates the root and applies its properties; a control is reused when the new node is the same node class and replaced in place when it is not. Verified by reference identity across `Update`, plus a scene test that a mounted tree is laid out by the host. |
| 3 | Done | Property diffing arrived with `PatchFrom`: a property that stops being set returns to the pristine value its constructor left (asserted against a control whose pristine value is not `default(T)`), an assigned `null` is a value rather than an absence, and an unchanged value is not re-assigned. |
| 4 | Done | Structure arrived with the reconciler in [#100](https://github.com/drysart/Kolvir/pull/100): a child added, removed or retyped mid-list leaves a correct tree, and a removed subtree is unmounted with its handlers. `ChildrenTests` holds the cases. This row said "Not started" for a while - the work was in the code and the table was stale. |
| 5 | Done | Keyed matching plus `ControlCollection.Move`, from [#99](https://github.com/drysart/Kolvir/pull/99) and [#100](https://github.com/drysart/Kolvir/pull/100). Reversing a keyed list moves the controls instead of rebuilding them, proven by reference identity; keyed and unkeyed children mix without a keyed node ever being claimed positionally. |
| 6 | Done | Generated `Click`-style event properties over the `AddXxxHandler`/`RemoveXxxHandler` pairs, with the attached delegate tracked so a changed handler detaches the old one. Same render subscribes once, and a handler the node stops setting is removed. `EventTests` holds the cases. |
| 7 | Done | `src/generators/Kolvir.Feature.VirtualDom.Generators`: the incremental generator, `LeafNodeAttribute`, and `NullAndLeafAnalyzer` (VD010, VD011). The hand-written `VLabel`/`VNote`/`VButton`/`VPanel` are gone: the feature suite now consumes generated classes through an analyzer reference and its 26 tests pass unchanged, which is the point of step 7 having been deferred. A second suite, `tests/generators/...Generators.Tests`, holds 30 tests: the emission shape, VD001-VD005/VD007/VD012, VD010/VD011 with their false-positive cases, and the mirror-drift check over 14 real controls. The analyzer was also seen failing a real build (a `Children` on a `VLabel` in the feature suite stops it with VD011). Found and fixed on the way: an implicit conversion does not apply to a nullable reference type argument (section 5), and a file name derived from the namespace alone collided when one namespace held two manifests. |
| 8 | Done | `Component` and `ComponentMount`: `Render`/`SetState`, the mount that owns the tree, and the render queue drained at the start of the next frame through `UiHost.AddFrameStartingHandler` (added to core for it), so a handler that changes five things renders once. `Render` is one pass with no settling loop, and one that changes state is refused rather than iterated. One rule for the two states that have no tree to patch - a state change before the first mount or after `Dispose` does nothing, and the first render picks up what was stored in the meantime. Composition arrived with it as `VComponent` (section 7.2): a child component is a node, matched by instance, keyed when it can move, one mount per instance. 29 tests across `ComponentTests` and `CompositionTests`, including a click through the headless host that asserts nothing rendered until a frame, three state changes before a frame costing one render, a change made with no host at all that the first frame drains, a mount disposed by an earlier render in the same batch, a text box echo that is not a second render, and three levels of nesting. A review of the whole branch then found three ways a *failing* render could take the rest of the frame with it - no per-entry isolation in the drain, an unwinding that did not recurse below ordinary nodes, and an attach that outlived a first render that threw - all three fixed in the mount, the reconciler and the drain, each with a regression test confirmed to fail without its fix (sections 7.2 and 12). Section 7.1 records the four `SetState` decisions. The component layer also split into a protocol and an implementation - `IComponent` is what the mount needs, `Component` is what an application derives from - so the base class is replaceable without touching the mount (section 7.3). `AddVirtualDom()` is deliberately absent - there is nothing to register until step 9. |
| 9 | Not started | |
| 10 | Done | A **Virtual DOM** card in the sample (`--page virtual-dom`), the `DEVELOPER-GUIDE.md` section, and the AOT verification measured rather than asserted: the `win-x64` published build renders the card byte-for-byte identically to the JIT build, and `Kolvir.Minimal` publishes with no virtual-DOM artifact. The work also found a real publish failure - `PublishAot` is global, a `netstandard2.0` generator cannot satisfy it, and a project reference therefore breaks `dotnet publish` for applications that never asked for a generator (section 9.1, item 4). |

Beyond the ten steps, `VFragment` (section 6.2) was added to close the gap section 11 of
[`OBSERVABLES.md`](OBSERVABLES.md) recorded: a node that makes no control, so that a render can return
several top-level nodes and `ObservableComponent.RenderWithNullModel` can default to showing nothing.
That made a mount own a slice of its parent's children rather than one child, which is the only part
of it that reaches code written before it - and the reason `Mounted.ControlCount` exists.

The application level (section 8) is the later addition: `ApplicationVNode`, `VWindow`,
`IApplicationComponent`, `ApplicationComponent`, `ApplicationMount` and
`VirtualDomRuntime.Mount(Application, IApplicationComponent)`. It keeps the control reconciler
untouched, reaches the control tree only through `VWindow.Content`, and renders through its own
frame-drained queue. The demo is `src/demo/Kolvir.Demo.VirtualDom`, and its `--check` mode drives the
open, native-close and rename flows through the headless host. That work also added
`Application.IsExiting`, the one core member it needed: without it the mount's native-close rule would
veto every `Application.Exit()`. `Kolvir.Minimal` on win-x64 publishes at 3,575,808 bytes before and
after, so the delta against an application that does not reference the feature is 0.
