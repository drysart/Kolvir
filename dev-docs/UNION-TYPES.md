# Union types

A property that can hold one of several unrelated kinds of value - a literal colour, a theme role,
or "inherit" - needs a type that says which one is set. `Union<T1, T2>`, `Union<T1, T2, T3>` and
`Union<T1, T2, T3, T4>` in `src/core/Kolvir/Core/Union.cs` are that type, and
`src/core/Kolvir/Core/InheritedValue.cs` is the sentinel the "inherit" case is made of.

Status: the types and their tests are implemented. Nothing uses them yet: `Control.Font`,
`Foreground`, `ForeColor`, `ForeColorRole`, `Background` and `BackColor` stay exactly as they
are, as do the `Effective*` and `Clear*` methods. There is no Roslyn analyzer, and `Control` has no
`Effective*` property. This page records the decisions those next steps have to work with.

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

This is also the only guard in this branch; the analyzer that reports the mistake at compile time
is a later piece of work.

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

The name matches `Specified<T>` in `Kolvir.VirtualDom`, which already uses `IsSpecified` for "this
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
