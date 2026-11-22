# Union analyzers

`src/generators/Kolvir.Analyzers` is the build-time half of the union types. It is a
netstandard2.0 Roslyn analyzer project whose assembly ships inside the `Kolvir` package at
`analyzers/dotnet/cs/`, which is the folder NuGet and the compiler look in. A package consumer gets
the rules with no opt-in and no project-file change.

The rules share one category, `Kolvir.Usage`. The first release has KOLVIR0001, 0002, 0003 and
0005.

| Id | Severity | Catches |
| --- | --- | --- |
| KOLVIR0001 | Error | `TryGetValue<T>` where `T` is not a case type of the union |
| KOLVIR0002 | Error | An explicit `default`/`default(T)` targeting a union, or `new Union<...>()` |
| KOLVIR0003 | Error | A union type with duplicate case types |
| KOLVIR0005 | Warning | A `TryGetValue` result discarded as a whole expression statement |

## KOLVIR0001: TryGetValue type is not a case type

`Union<...>.TryGetValue<T>` matches `T` against the union's case types by exact type and throws
`ArgumentException` when `T` is not one of them. The run-time behaviour is deliberate and stays
unchanged; what the compiler was missing is the diagnosis at the call site. This rule is the
compile-time half of the same contract.

It fires on an instance invocation whose containing type is a constructed `Kolvir.Union<...>` and
whose `T` matches none of the constructed type's case types. The message names the offending type,
the union and every valid case, and when `InheritedValue` is one of the cases it says that is the
case `PropertyValue.Inherited` assigns.

The rule is deliberately quiet when an answer could depend on a run-time instantiation:

- an unresolved or error type;
- `dynamic`;
- `T` or a case type that is, or contains, a type parameter;
- native integers and tuples, where symbol equality and the runtime's `typeof` equality can
  diverge.

A false positive here is an error in an application's build, which is worse than a missed report,
so the rule does not guess.

## KOLVIR0002: explicit default union value

A default-initialised union has no case. `TryGetValue` answers `false` for every case and
`Switch`/`Match` refuse it, so the value cannot be read; the application meant to assign a case.
The rule is an error because the value is invalid from the moment it is written.

Only explicit forms are diagnosed:

- a target-typed `default`, such as `Union<string, int> value = default;`;
- `default(Union<...>)`;
- a parameterless `new Union<...>()`.

Two boundaries are deliberate:

- A field, property, local or parameter that is merely never assigned is **not** diagnosed. That
  needs dataflow, and a struct defaults silently everywhere; widening the rule to it would be a
  different piece of work with a different false-positive cost.
- `this = default;` inside a union constructor is **not** diagnosed. `src/core/Kolvir/Core/Union.cs`
  uses it to zero the fields before assigning the real case; the value never escapes, and it is the
  struct-constructor idiom rather than a union value. The analyzer recognizes the assignment to
  `this` and skips it.

The repository's production code has no other explicit default union. The intentional tests that
create one - the refusal tests for fonts, colours, enabled, caret, toast, image, split container,
window chrome and text spans, plus the union's own default-values tests - carry a scoped
`#pragma warning disable KOLVIR0002` with a comment saying the value is the thing under test.

## KOLVIR0003: duplicate union case types

A union whose case types repeat is broken by definition: the repeated case would mean two
constructors and two conversion operators with the same signature, which does not compile. The
rule reports every use of the generic name rather than only the first, because the type is invalid
wherever it appears. Repetition is intentional.

Types are compared with symbol equality, with `dynamic` and `object` treated as the same runtime
type, because `typeof(dynamic) == typeof(object)`. Nullable annotations do not make a repeat
distinct (`string` and `string?` are the same runtime type). Unresolved arguments and unbound
generics are left alone.

## KOLVIR0005: discarded TryGetValue result

A `TryGetValue` call used as a whole statement throws its boolean away. The `out` value is then
`default` whenever the union holds a different case, which is usually a bug and occasionally
deliberate. The rule is a warning, not an error, for that reason.

It fires only when the invocation is the entire expression of an expression statement, and only
for a valid case-type argument, so KOLVIR0001 is not duplicated. It is silent when the result is
used in any way: assigned, returned, negated, in a condition, passed as an argument, used as a
lambda body, or explicitly discarded with `_ =`. The explicit discard is the documented way to say
the read is intentional.

The two warm-up loops in `UnionTests.TryGetValueDoesNotAllocate` use `_ =` for exactly that; the
allocation assertion is unchanged.

## KOLVIR0004 is out of scope

KOLVIR0004 is deliberately not implemented. The suggested rule would have been a
"you dropped the `IDisposable`" or "you subscribed a handler and never unsubscribed" diagnostic.
It is a non-goal because the API is designed for handlers to be subscribed without an unsubscribe
path when the whole control tree goes away with the window:

> It is expected and normal that application developers will wire up handler subscriptions
> without providing a way of unregistering (e.g., when setting up a window's control tree; there's
> no need to unregister events when the entire control tree goes away when the window is closed).

A rule that fought that usage would be reporting the library's intended design. The identifier is
reserved and must not be added later without an explicit decision.

## How the analyzer is delivered

For a NuGet consumer the package does all of it. `Kolvir.nupkg` carries
`analyzers/dotnet/cs/Kolvir.Analyzers.dll`; NuGet passes it to the compiler and no project file
changes. The analyzer is not in `lib/`, so the core assembly does not reference it and an
application cannot accidentally call into it.

Inside this repository, `Directory.Build.targets` adds the analyzer as a ProjectReference at
evaluation time - so it is in the restore graph and survives a single restore followed by
`--no-restore` builds - and removes it before the build for any project whose ProjectReference
closure does not reach `src/core/Kolvir/Kolvir.csproj`. The closure walk follows literal
ProjectReference paths; an unresolvable path fails the build rather than silently misclassifying a
project. The analyzer project itself never references its own output.

The same file has a build guard that fails if a project detected as a core consumer does not have
`Kolvir.Analyzers.dll` in the compiler's `@(Analyzer)` list. A project added later cannot silently
miss the analyzer. The analyzer's own test project is a deliberate exception to the central
reference: it references the analyzer as a plain assembly so it can construct the analyzers, and
carries an explicit `Analyzer` item for the guard.

Nothing in `buildTransitive/Kolvir.targets` wires the analyzer. That file ships inside the package
and is imported by every consumer, where a ProjectReference to a path in this repository does not
exist; package consumers get the analyzer only from `analyzers/dotnet/cs`.
