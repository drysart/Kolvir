# Union types

A property that can hold one of several unrelated kinds of value - a literal colour, a theme role,
or "inherit" - needs a type that says which one is set. `Union<T1, T2>`, `Union<T1, T2, T3>` and
`Union<T1, T2, T3, T4>` in `src/core/Kolvir/Core/Union.cs` are that type, and
`src/core/Kolvir/Core/InheritedValue.cs` is the sentinel the "inherit" case is made of.

Status: the types, their tests and the appearance properties are implemented. `Control.Font`,
`ForeColor` and `BackColor` are unions now, each with a get-only `Effective<Property>`, and
`ToolStripItem`/`ToolStripLabel` follow the same shape. `Control.Foreground`, `Control.Background`,
the `Background` type, `ForeColorRole` and the `Clear*` aliases are gone. The non-control
colour-or-role pairs joined them: `TextSpan.ForeColor` / `HoverBackColor`, `TextBoxSpan.Color` and
`SimpleBorder`'s edges. The last single-property colours are unions too: `Toast.BarColor`,
`SplitContainer.SplitterColor`, `Image.PlaceholderColor`, `Caret.Color`, `UiHost.ClearColor` and
`Window.CaptionColor` / `BorderColor` - see section 12. `ColorPicker.Color` is deliberately not
one: it is the colour the user picks, application data rather than a theme choice. `Control.Font`
and `ToolStripItem.Font` have since grown a third case, `FontOverride`, which makes the arity-3
type the one those two use while every other appearance property stays arity 2 or 3 as listed in
section 10. The union types have Roslyn diagnostics for the invalid shapes the compiler accepts -
see [ANALYZERS.md](ANALYZERS.md).

---

## 1. The shape, and why it is hand-written

A union is a `readonly struct` with one `byte` arm discriminator, one field per case type, one
constructor per case type and one implicit conversion per case type. Arm `0` means "default
initialised", and arms `1..N` are the cases in declaration order.

The constructor-per-case shape is deliberate. It is also the shape a source generator behind a
`[Union]` attribute would emit, so if the attribute arrives with C# 15 and .NET 11, this type can be
kept as the implementation or generated from the same skeleton without changing a single call site.
The attribute itself is not available here: everything in this repository targets `net10.0`.

Zero-initialisation is the reason the first arm is `1` rather than `0`. A `byte` field cannot be
left at a sentinel that is also a real value, and `default(T)` for a struct is all zeroes, so "no
case" has to be the zero value and the cases have to start above it.

## 2. The type knows nothing about inheritance

`Union<>` has no notion of a parent, a default, a theme or a fallback. `InheritedValue` is a case
like any other, and `Union<SKColor, ThemeColor, InheritedValue>` treats "inherit" exactly as it
treats a colour.

The reason is that inheritance is behaviour, and behaviour belongs to the control that has a parent.
A union that resolved "inherit" itself would need to know about `Control`, and every other
three-way choice in the library - text, thickness, alignment - would inherit that dependency for a
feature it does not have. `InheritedValue` is therefore a marker with one member and a `ToString`
that says `Inherit`; `Control` decides what inheriting means when it reads the union.

This is also why a default-initialised union is refused rather than treated as inherit: a union
that was never assigned is a mistake in the application, and turning it into "inherit" would make
that mistake look like a deliberate choice. See section 7.

## 3. `TryGetValue<T>` matches the exact case type

The one discriminator reads a case back:

```csharp
if (union.TryGetValue<ThemeColor>(out ThemeColor role)) { ... }
```

It compares `typeof(T)` against each `typeof(T1)`, `typeof(T2)`, ... and reinterprets the payload
with `Unsafe.As<T1, T>` only after the type equality has succeeded. It is never `is T`.

The reason is measured, not theoretical. On .NET 10, an `is T` test matches types that `T` is
assignable from, so a `string` held in `Union<object, string, ...>` answers `TryGetValue<object>` as
well as `TryGetValue<string>`, and which one wins depends on the order of the arms. Order-dependent
answers in a type whose entire purpose is to say which case is set are not acceptable; exact
matching makes the answer a property of the arm alone.

The cost of the exact check is nothing on the path that matters. For a struct instantiation - which
is what the colour and font properties are - the JIT sees `typeof(T) == typeof(T1)` for a known
`T` and folds both comparisons away. The FullOpts code for
`Union<SKColor, ThemeColor, InheritedValue>.TryGetValue<SKColor>` is 21 bytes: compare the arm byte
with `1`, branch, copy four bytes, return. There is no type test in it at all. A shared generic
instantiation (every case type a reference type) costs one comparison of the cached method handle
of `T` with that of the arm's case type, plus the arm compare - still no allocation.

`TryGetValue` never boxes in either shape. `UnionTests.TryGetValueDoesNotAllocate` locks that in:
zero bytes over 1,000,000 matching calls on `Union<SKColor, ThemeColor, InheritedValue>`.

## 4. A type argument that is not a case throws

`TryGetValue<object>` on a union whose cases are none of `object` throws `ArgumentException` naming
the union and its case types. It does not return `false`.

Returning `false` would make a coding error - asking a union a question it cannot answer - look
like an ordinary "not this arm" answer, and the caller would go on to do nothing in a handler that
should never be reachable. The distinction the method draws is deliberate:

- `T` is not a case type: a bug, and it throws.
- `T` is a case type but another arm is stored: a normal answer, and it returns `false`, leaving
  the `out` parameter at `default`.

This is also the run-time guard; [KOLVIR0001 in ANALYZERS.md](ANALYZERS.md#kolvir0001-trygetvalue-type-is-not-a-case-type)
reports the same mistake at compile time.

## 5. There is no `object? Value`

A `Value` property, or an `out object?` overload, would be the obvious convenience and is
deliberately absent. Reading a value-type case through `object?` boxes it, once per read, on a path
that runs inside layout and paint. The typed read is the only read, and it is free.

The same reasoning rules out an implicit conversion out of the union. A union converts in, never
out: which case would it convert to?

## 6. Pattern matching over the contents does not work, so `Switch` and `Match` do the job

A hand-written struct cannot be pattern matched against its payloads. `x is ThemeColor` where `x` is
a union is `CS8121` - the expression is never of that type - and `(object)x is ThemeColor` compiles
and is silently `false`, because the box is the union and not its payload. Neither is an accident
that can be fixed by writing the pattern differently; it is what a struct with a discriminator is.

Exhaustiveness therefore comes from the arity, not from a pattern:

- `Switch(Action<T1>, Action<T2>, ...)` for handlers that only act;
- `Switch<TState>(TState, Action<TState, T1>, ...)` so a handler can be a `static` lambda with the
  captured values passed as state;
- `Match<TState, TResult>(TState, Func<TState, T1, TResult>, ...)` for handlers that produce a value.

All of them call `ThrowIfUnspecified()` first, and all of them take one delegate per case type.
That is the point: adding a case type to a union changes its arity, so every `Switch` and `Match`
over it stops compiling until the new case has a handler. A dispatch that silently does nothing for
a case nobody thought about is the failure mode this design exists to prevent.

There is no bare `Func<T1, TResult>` form; it would only save one argument and would bring back
closures for anything that needs to see more than the payload.

## 7. A default-initialised union is unspecified, and says so

`default(Union<...>)` holds no case, and that state is called unspecified: nothing was assigned, so
the union is not any of its case types. `IsUnspecified` reports it and `ThrowIfUnspecified()` throws
`InvalidOperationException`, whose message names the union and says it was never assigned. `Switch`
and `Match` throw on it rather than doing nothing.

The name matches `Specified<T>` in `Kolvir.Feature.VirtualDom`, which already uses `IsSpecified` for "this
render assigned the property". Unspecified is the same idea one layer down, and one word for it in
both places is worth more than a local invention.

The alternative - treating the zero value as "inherit" - was rejected. It would make a property
that was never assigned indistinguishable from one that was deliberately set to inherit, and the
first is a bug that should be loud. It would also bake inheritance into a type that section 2 keeps
ignorant of it.

Equality has to say something about that state, so two default-initialised unions compare equal to
each other and share a hash code. They are still not a case.

## 8. Details worth knowing

- The case types must be distinct. Two identical case types would declare two constructors and two
  conversion operators with the same signature, which does not compile, so the constraint is enforced
  by the compiler rather than by documentation alone.
- A case may hold a null reference: assigning `null` to a reference-typed case is a valid
  assignment. This is why the annotation on the `out` parameter is `[MaybeNullWhen(false)]` - the
  parameter is null when the answer is `false`, and it may also be null when the answer is `true`.
  `[NotNullWhen(false)]` would be a lie in the other direction.
- `ToString` names the stored case: `ThemeColor.MutedForeground` when the first case is stored (a
  named constant reads with a dot), `SKColor(#ff404040)` for the rest (a value reads in
  parentheses), `InheritedValue(Inherit)` for the sentinel, and
  `Union<SKColor, ThemeColor, InheritedValue>(unspecified)` for the default. The `[DebuggerDisplay]`
  uses it, and so should test failures and exception messages.
- There is no arity above four. A fifth case type is a design smell before it is a missing overload;
  add the arity when something needs it, not in advance.
- There is no shared non-generic base class or helper. It would exist only to hold a discriminator
  and would sit between the union and its cases for no gain.

## 9. Tests

`tests/core/Kolvir.Tests/Core/UnionTests.cs` covers every arity for assignment and explicit
construction, and then the shared contract in detail on
`Union<SKColor, ThemeColor, InheritedValue>`: the hit, the miss on another arm, the
`ArgumentException` for a type that is not a case (including `TryGetValue<object>`), exact-type
matching in a union whose first case is `object`, `IsUnspecified` and `ThrowIfUnspecified`, `Switch`
and `Match` with and without state, equality and hashing, `ToString`, a null payload in a
reference-typed case, and the no-allocation assertion.

## 10. Control appearance properties

The first consumers are `Control.Font`, `Control.ForeColor`, `Control.BackColor` and
`Control.Enabled`, plus `ToolStripItem.Font` and `ToolStripLabel.ForeColor`. The shape is the same
in each case:

| Property | Union |
| --- | --- |
| `Control.Font` | `Union<Font, FontOverride, InheritedValue>` |
| `Control.ForeColor` | `Union<SKColor, ThemeColor, InheritedValue>` |
| `Control.BackColor` | `Union<SKColor, ThemeColor, InheritedValue>` |
| `Control.Enabled` | `Union<bool, InheritedValue>` |
| `ToolStripItem.Font` | `Union<Font, FontOverride, InheritedValue>` |

The property reports the assignment. The get-only `EffectiveFont` / `EffectiveForeColor` /
`EffectiveBackColor` resolves it: the value assigned here, else the nearest ancestor that assigned
one, else the class default (`DefaultFont` / `DefaultForeColor` / `DefaultBackColor`). The backing
field is initialised to `PropertyValue.Inherited`, so "never assigned" reads back as Inherit; a
`default` union is refused by `ThrowIfUnspecified()` in the `Effective*` getter rather than being
treated as inherit.

The font's middle case is a partial description (`FontOverride`), which is the one place a union
case is not a complete value: it composes onto the inherited `Font` in `EffectiveFont` and returns a
complete one. That is why the font properties carry an arity-3 union with two reference cases and
the colour properties do not; section 5 of [FONTS.md](FONTS.md) is the design, and the note's
section 9 is the one compatibility break - an exhaustive `Switch`/`Match` over `Control.Font` gains
an arm.

`Control.Enabled` is the same shape with one difference: `EffectiveEnabled` ANDs the local answer
down the chain rather than taking the nearest assignment, because disabling a container disables
everything inside it. `PropertyValue.Inherited` and an explicit `true` therefore resolve
identically, and an explicit `true` does not override a disabled ancestor. The arm still earns its
place in the raw property: it reports that nothing was assigned, which is what lets the colour
dialog save and restore the owner's state without turning an inherited value into an explicit
`false`.

Three decisions are worth recording, because each one is a place the obvious alternative is wrong:

- **Inheritance stays in `Control`, not in `Union<>`.** The union has no parent, default or theme;
  the control walks its own chain and caches what an ancestor assigned. Class defaults and palette
  roles are read live, so a palette swap is a repaint.
- **"Paint nothing" is a transparent colour, not a fourth case.** `Background.None` was removed; a
  class that paints nothing returns `SKColors.Transparent` from `DefaultBackColor`. The subtle part
  is where the transparency comes from: a *class default* that is transparent stops the walk (a
  label keeps painting nothing inside a filled panel), while an *assigned* transparent is an
  ordinary colour and answers for descendants. That is the one behaviour the old struct encoded
  that the union does not, so `Control` encodes it.
- **`EffectiveBackColor` is `SKColor`, not `SKColor?`.** Alpha 0 is how "nothing is painted" is
  spelled, which is what `Opaque`, `OnPaintBackground` and the Win32 erase colour read. A fallback
  rule follows the same principle: the least destructive value first (`Transparent` for a
  background), the most neutral one where transparency would be destructive (`Theme.Foreground` for
  text, `Theme.DefaultFont` for a font).

`Clear*` is gone in favour of `= PropertyValue.Inherited`, and the two names for one thing are
merged under the WinForms name: `Foreground` and `ForeColorRole` became `ForeColor`, `Background`
and `BackColor` became `BackColor`.

The tests are `tests/core/Kolvir.Tests/Core/AppearanceUnionTests.cs` and the ported
`InheritedAppearanceTests`: what each property reports, what each `Effective*` resolves through a
parent chain, role resolution after a palette swap, the transparent class default versus the
assigned transparent, the cache being dropped by a later ancestor assignment and by reparenting, an
unparented control's class default, a `default` union being refused, and the `ToolStripItem` chain.

## 11. Spans and borders

A span and a border now offer one concept as one property, too:

| Property | Union | Resolved by |
| --- | --- | --- |
| `TextSpan.ForeColor` | `Union<SKColor, ThemeColor, InheritedValue>` | `EffectiveForeColor` |
| `TextSpan.HoverBackColor` | `Union<SKColor, ThemeColor, InheritedValue>` | `EffectiveHoverBackColor` |
| `TextBoxSpan.Color` | `Union<SKColor, ThemeColor, InheritedValue>` | `EffectiveColor(TextBox owner)` |
| a `SimpleBorder` edge's colour | `Union<SKColor, ThemeColor>` | `ColorFor(side, state)` |

A span is not a control, so its `InheritedValue` does not walk a parent chain: it means the owning
control. `TextSpan` reads `Owner.EffectiveForeColor`, which is the label's resolved text colour,
and falls back to `ThemeColor.Foreground` when the span has no label. `TextBoxSpan` holds no owner
reference by design - it is caller-owned and may be handed to more than one box - so the field is
passed to `EffectiveColor`, and the inherited arm answers with that field's `EffectiveForeColor`.

`TextSpan.HoverBackColor` has no tint to inherit: `InheritedValue` is `SKColors.Transparent`, which
is "paint nothing", and a `ThemeColor` arm is tinted to `0x1A` while it is resolved. The label keeps
the decision that a disabled label greys every span, because that is label state rather than span
state.

A border is immutable and carries no per-control state, so there is no `SimpleBorder.Effective*`.
The union is stored on the edge and resolved in `ColorFor` while painting, and it has no
`InheritedValue` arm because a border has no parent to inherit from. That is deliberate: an
`Effective*` on a border would be a value with nothing to resolve.

## 12. The single-property colours

The last colours that were nullable are unions of the same shape, each with a get-only reader:

| Property | Union | Resolved by | Inherit means |
| --- | --- | --- | --- |
| `Toast.BarColor` | `Union<SKColor, ThemeColor, InheritedValue>` | `EffectiveBarColor` | the colour the severity implies |
| `SplitContainer.SplitterColor` | `Union<SKColor, ThemeColor, InheritedValue>` | `EffectiveSplitterColor` | `ThemeColor.Splitter` |
| `Image.PlaceholderColor` | `Union<SKColor, ThemeColor, InheritedValue>` | `EffectivePlaceholderColor` | `SKColors.Transparent`, so no placeholder |
| `Caret.Color` | `Union<SKColor, ThemeColor, InheritedValue>` | `EffectiveColor` | `ThemeColor.Caret` |
| `UiHost.ClearColor` | `Union<SKColor, ThemeColor, InheritedValue>` | `EffectiveClearColor` | `ThemeColor.Background` |
| `Window.CaptionColor` | `Union<SKColor, ThemeColor, InheritedValue>` | none | the platform's own default |
| `Window.BorderColor` | `Union<SKColor, ThemeColor, InheritedValue>` | none | the platform's own default |

None of these walks a parent chain: there is no parent to ask and no ancestor whose answer should
win. `InheritedValue` here means "nothing was assigned", and the class default is the least
destructive value available - the theme's own answer where there is one, transparent where the
property is a fill that can be absent. Each of those is what a null meant before, so no control
changed its appearance.

`ColorPicker.Color` stays a plain `SKColor`. It is the colour the user picked in a colour picker,
which is application data rather than a theme choice, so an inherit arm or a palette role would say
nothing useful about it.

The window colours are the one pair with no `Effective*`. They go to a backend that takes a
concrete `SKColor` or null for the platform's default, and the platform owns the actual pixels, so
no later reader can resolve a role. The window therefore resolves one with `Theme.ColorOf` when it
is assigned and hands over the result, and re-resolves it from the application's palette-change
loop, so an assigned role keeps following the theme. `PropertyValue.Inherited` passes null.
`Window.CaptionColor` and `Window.BorderColor` report the assignment;
`Window.Backend.CaptionColor` and `BorderColor` report what the platform holds.

The tests are `WindowChromeColorTests`, `CaretTests`, `UiHostColorTests`, and cases added to
`ToastTests`, `ContainerLayoutTests` and `PixelInvariantTests`: the assigned case, a role resolved
after a palette swap, the unset fallback, the default-union refusal, and a pixel assertion that the
painted result is the one the old nullable property produced.