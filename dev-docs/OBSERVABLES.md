# Observables - design and implementation plan

A small, non-threaded change-notification layer for data models, in its own package
(`Kolvir.Feature.Observables`, rung 3 of `FEATURE-COST-POLICY.md`). A model keeps an observable as a
private backing field and exposes plain properties over it (section 5). An expression observable
recomputes itself from the values it read the last time it ran, which is the piece that makes
"re-render when anything the render depended on changes" answerable without `INotifyPropertyChanged`,
strings, or reflection.

The immediate purpose is the other half of the component layer: `VIRTUAL-DOM.md` section 7.3 left
`IComponent` as a seam, and this is what plugs into it. A component whose `Render()` runs inside a
dependency-collection scope knows what its render read, so a data model that changes can ask that
component to re-render - and the render still goes through the frame queue, so a burst of model
changes is still one render (see section 9).

Status of this document: **living plan, no code yet.** Section 3 records the decisions and the
reasons; section 12 is the order of work; section 11 lists what is still open, including four things
decided here for the first time that should be confirmed before the code exists.

This file is deliberately ASCII-only: write "section 7.1" rather than a section sign, and "<-"
rather than an arrow. Those two characters have been mangled by tooling often enough that the
habit is not worth keeping, and nothing in the document needs them.

## 1. Goals and non-goals

Goals:

- **Values that can be watched.** `Observable.CreateValue<T>(initial)` returns an
  `IObservableMutableValue<T>`, kept private and exposed as an ordinary property - so the creator
  can write, readers read, and nobody outside sees an observable to hold on to.
- **Dependencies that are discovered, not declared.** An expression observable runs its function,
  notes which observables that run read, and subscribes to exactly those. No strings, no attributes,
  no registration lists, and no reflection - so it stays AOT-clean.
- **Cheap when nothing is watching.** A value nobody has a handler on costs one field; a named
  dependency nobody is listening to costs one dictionary lookup and nothing else; an expression with
  no handlers runs nothing.
- **Zero cost to applications that do not use it.** A separate package, no base-library changes, no
  initialization, and no process-wide machinery unless a source is created.
- **Single-threaded by construction.** Every observable is UI-thread state, like every control and
  every component. No locks, no `volatile`, no thread-affinity checks beyond a debug assertion.

Non-goals, for this plan:

- Thread safety, or cross-thread notification. A background thread that wants to change a value
  marshals to the UI thread first, exactly as it does for `Control` today.
- Declared dependency graphs (attributes, interfaces, registration). Dependencies are what a run
  actually read.
- `INotifyPropertyChanged`, `PropertyChangedEventArgs`, or string property names. Nothing here knows
  what a "property" is.
- Glitch-free, topologically-ordered delivery (section 3.5): a listener may be called more than
  once for one logical change and may observe an intermediate value.
- Asynchronous bodies. A collection scope is active while the body runs, so a body that suspends
  and resumes would collect whatever else ran in between - a different design, and one this plan
  does not do (section 10).
- Collections. Observable dictionaries/lists are the next step and are deliberately out of scope;
  section 7 provides the primitive they need (a dependency per key, plus one for the shape of the
  collection).
- Binding a control to an observable without a component in between.

## 2. The type model

```

IObservableEventSource                      "something happened" - the dependency primitive
  bool IsDisposed { get; }
  IDisposable AddObservableEventRaisedHandler(Action<IObservableEventSource> handler)
  void RemoveObservableEventRaisedHandler(Action<IObservableEventSource> handler)
  void DependUpon()                         default: Observable.PublishRead(this)
  void RaiseEvent()

IObservableValue : IObservableEventSource, IDisposable
  object? Value { get; }

IObservableValue<T> : IObservableValue
  new T Value { get; }

IObservableMutableValue<T> : IObservableValue<T>       <- what CreateValue returns
  new T Value { get; set; }
```

| Piece | Shape | Purpose |
| --- | --- | --- |
| `IObservableEventSource` | handler pair, `DependUpon`, `RaiseEvent`, `IsDisposed` | The dependency primitive. A named dependency and a collection's per-key dependency are this and nothing more: they carry no value, only "this particular thing changed". |
| `IObservableValue` | `object? Value` | The untyped view, for a homogeneous dependency set and for `PublishRead`. Reading it publishes. |
| `IObservableValue<T>` | `new T Value { get; }` | What a dependency set holds. Read-only on purpose: a subscriber can watch a value but cannot write it. |
| `IObservableMutableValue<T>` | `new T Value { get; set; }` | What `CreateValue` hands back, to the one piece of code that owns the state. |
| `Observable` | static | `CreateValue<T>`, `CreateExpression<T>`, `GetNamed`, `RaiseNamedEvent`, `PublishRead`, `CollectDependencies`, `CollectDependencies<T>`. |

Three consequences worth stating plainly:

- **Read-only is a compile-time promise, not a guard.** A dependency set holds
  `IObservableValue<T>`, and a determined caller can cast to `IObservableMutableValue<T>`. The same
  trust level as `Control`'s internals, and it costs nothing to keep out of an API.
- **The mutable type is the only thing that can be written.** An expression's result is an
  `IObservableValue<T>`, so the only way to change an expression's value is to change something it
  read - which is the property that makes the dependency graph trustworthy.
- **`IObservableEventSource` is not `IDisposable`.** A named dependency is shared: disposing it would
  take the dependency away from every other holder. Only the *value* types are disposable
  (section 3.10), and the handle returned by `AddObservableEventRaisedHandler` is how a single
  registrant unregisters.

`DependUpon()` is a default interface method over `Observable.PublishRead(this)`, so an
implementation cannot forget to publish a read, and there is one name for the action rather than two.
`PublishRead` has to be public for that body to compile, which is right anyway: a hand-written source
in another assembly needs it.

## 3. Decisions and why

### 3.1 The value: equality, and where "changed" is decided

| Decision | Why |
| --- | --- |
| **A write of an equal value raises nothing.** | The whole point of an event is that something a listener cares about is different. A model that recomputes and assigns 42 over 42 should not wake anything up, and a setter that always raises would make every "changed" handler an "assigned" handler. |
| **Equality is `EqualityComparer<T>.Default`.** | The best comparer available is the type's own: `IEquatable<T>`, `override Equals`, or the operator-free default for structs. It is also what every other equality decision in the library uses, so a record's value semantics work with no ceremony. An optional `IEqualityComparer<T>` overload covers the rest (a case-insensitive string, a key rather than a whole object). |
| **An expression compares its result the same way - except for exceptions.** | The dependencies that produced the value are still the dependencies that produce it, and they can change without the *result* changing (section 3.6). Only the raise is conditional. An exception result is the one case a reader may care about in detail, so it is compared by identity unless the expression is given its own `IEqualityComparer<Exception>`, and it is never equal to a value (section 3.7). |

### 3.2 Named dependencies are event sources, not values

A named dependency exists to say "the thing behind this key changed". It carries no value, because a
value that lives in a dictionary entry with no listeners is a value that vanishes when the last
listener goes - and, worse than vanishing, it would be reset *during* a re-evaluation that unhooks
before re-hooking, because that unhook is momentarily the last listener. Keeping the value in the
model and the *event* in the named source means eviction has no consequence to get wrong.

| Decision | Why |
| --- | --- |
| **`GetNamed(object key)` returns an `IObservableEventSource`.** | One `object` key, and no notion of an owner baked into the API: a dependency whose name is logically several values composes them itself - a tuple, a small key object, whatever its owner decides - so the layer never has to guess which part of a name is the scope. Object keys rather than strings: no formatting on a read path that runs inside renders, no separator-escaping aliasing (`"a|b"` vs `"a"` + `"b"`), and value keys come out exact. The backing table is `Dictionary<object, Entry>` with `EqualityComparer<object>.Default`, so a tuple key compares structurally with no extra layer. |
| **One handle instance per entry, returned to every caller.** | The dependency set is a `HashSet<IObservableEventSource>`, so two handles for one entry that compare by reference would be two dependencies - and an expression that read the same key twice would hook it twice and unhook it once. The entry owns its handle and `GetNamed` returns it. This also removes an allocation from every read. |
| **An entry exists only while it has a handler, and is evicted when the last one is removed.** | That is the memory story: a dependency nobody is listening to is not memory. Eviction is deferred while a raise or an evaluation is in progress: a handler is allowed to remove itself during its own dispatch (section 3.4), and an expression unhooks its whole set before it re-runs (section 3.6.1) - evicting in the middle of that would destroy a handle the same run is about to re-hook. |
| **A handle is valid only while something is registered on it.** | Eviction is what makes a handle stale, so the only safe long-lived holder is the thing that keeps it alive by holding a registration token - an expression, a component. A reader resolves the handle per run (`GetNamed(key).DependUpon()`, or a helper that does) and an owner that only wants to announce a change uses `RaiseNamedEvent(key)`, which resolves it at the moment of the raise. Caching a handle across an expression's re-collection, or as a model's field for raising, is the one way to make a dependency silently stop working - which is why a stale handle throws rather than ignoring the call (section 3.10). |
| **Raising does not need a handle: `Observable.RaiseNamedEvent(object key)`.** | The common shape is a model that raises without wanting to register - a dictionary announcing that a key changed - and going through `GetNamed` for it would create an entry and a handle for a key nobody is watching, which is an allocation and a dictionary write per mutation that is thrown away immediately. `RaiseNamedEvent` raises the entry if there is one and does nothing if there is not, so an unwatched key costs one lookup. `GetNamed` keeps the opposite behaviour deliberately: a caller asking for a handle is about to register on it, so there has to be an entry to register on. |

### 3.3 Collecting dependencies: a stack, not a slot

`PublishRead(source)` appends the source to the *innermost* active collection scope. A scope is
entered by `CreateExpression` while it runs its function, and by `CollectDependencies(body)`
(section 4).
Nested scopes are isolated: the outer run never sees what the inner run read.

- **Isolation is correct, not just convenient** (this was a review question, answered by the design
  rather than by convenience): if the outer function builds an inner expression and *reads its
  `Value`*, that read is itself a dependency on the inner expression. The outer run then hears about
  any inner change through the inner expression's own event, and re-evaluates. So the inner
  expression's dependencies do not need to appear in the outer set as well - and if they did, the
  outer run would re-evaluate once per deep dependency instead of once per inner change, which is
  strictly more work.
- **Every read publishes**, typed and untyped, so a consumer that reads through `IObservableValue`
  is tracked like one that reads `IObservableValue<T>`.
- **A read outside any scope is a no-op**: `PublishRead` null-checks a static field. That is the cost
  of every ordinary property read in an application that never uses expressions.
- **The scope is restored in `finally`**, so an exception from a function cannot leave the collector
  pointing at a scope that is no longer running.
- **Duplicates are removed per run** by reference identity, so an expression that reads `Count` in a
  loop hooks the count source once.

### 3.4 Dispatch: snapshot, isolate, never re-enter the list

| Decision | Why |
| --- | --- |
| **A raise iterates a snapshot of the handler list.** | A handler is allowed to subscribe and unsubscribe freely, including itself, and none of that may disturb the dispatch that is running. Mutations apply to the next raise, and a handler that disposes the source mid-dispatch does not stop the snapshot either: the remaining handlers are already in the list being walked. |
| **Nothing is caught: an exception from a handler propagates.** | A raise is a synchronous callback and behaves like one: as with a multicast delegate, the first handler that throws ends the notification and the exception travels out of `RaiseEvent` to whoever caused the change. This library has no reporting channel to hand it to and no policy to impose, and each alternative was worse - swallowing it silently, inventing a target for it, or rethrowing after the loop, which would assume that the remaining handlers must still be told even though nothing can vouch for the state after a bug. A use case that needs more (a render that must survive its own failure, say) is where that judgement belongs (section 8.1). The consequence to be aware of: handlers after the throwing one do not run for that change. |
| **`RaiseEvent` is on the interface and is public.** | A source that owns a change raises it: a dictionary raising a key's dependency, a hand-written source raising on a timer tick. It is not something a consumer calls - a consumer changes the model and the model raises. Documented rather than restricted, because restricting it would need a second type for "the raiser". |
| **`RaiseEvent` on a disposed source throws `ObjectDisposedException`.** | A dead source is a lifetime bug in whoever still holds it, and a silent no-op makes that bug invisible: the call looks like it announced something. The case that *is* ordinary - a dictionary raising for a key that nobody is watching - is not this one: `RaiseNamedEvent` resolves the entry and does nothing when there is none (section 3.2), because an absent entry is "no listener", not "dead source". |

### 3.5 Delivery order is not a topological order

Changes propagate synchronously and immediately: a raw observable raises its event as soon as it
changes, and the user's decision is that this stays true - deferring is the component layer's job,
not the observable's. That means a diamond - `C = f(A)`, `D = g(A, C)` - can call `D` twice for one
change to `A`, and can call `D` while `C` still holds its old value. This is a deliberate trade:

- **It is unobservable for the intended use.** The only consumer with a real ordering problem is a
  UI render, and renders go through the frame queue: a burst of model changes marks components and
  the frame renders each one once, reading a graph that has finished propagating. `VIRTUAL-DOM.md`
  section 7.1 already made this argument for `SetState`.
- **The alternatives cost more than they are worth now.** Depth-ordered dispatch (a depth per source,
  a priority queue) is close to topological order and would remove most re-entrancy; lazy
  pull-on-read is glitch-free by construction but makes every read a recursive walk. Both are
  additive later, because nothing outside this package depends on the order.
- **What a listener may assume:** it is called when something it depends on changed, the graph has
  finished propagating before the outermost raise returns, and it must not assume it is called once
  per logical change or that the value it reads is the final one.

### 3.6 Expressions: cold, hot, and what a read means

An expression is one function plus a cached result plus the set of sources that result was computed
from. Nothing is computed when it is created: `CreateExpression` only records the function, and the
first run happens when something asks for the value - a read, or the first handler attached. It has
two states:

| State | Dependencies hooked | `Value` read |
| --- | --- | --- |
| **Cold** - no handlers on it | no | re-runs the function every time, in a collection scope whose result is discarded |
| **Hot** - at least one handler | yes, all of them | returns the cached result |

| Decision | Why |
| --- | --- |
| **Creation runs nothing.** | A derived value that nobody reads should cost a delegate and nothing else - not a run, not a dependency set, not an exception it might throw before anyone is listening. It also means a function that throws is only ever discovered by somebody who asked for the result, which is the same rule as a read.
| **Cold expressions hook nothing.** | An expression nobody watches is a function, and paying to keep its dependencies wired up would make an unused derived value cost the same as a used one - and would keep its whole dependency tree alive. A cold read discards the dependencies it collected rather than keeping a stale set: the next transition to hot re-runs and re-collects anyway, so a kept set could only ever be wrong. |
| **Cold reads always re-run**, even though a cached result may exist. | The cache cannot be trusted when nothing is telling the expression that its inputs moved. This is the user's rule, and it is the only self-consistent one: a cache is valid exactly when the subscription that maintains it is live. |
| **Hot reads never re-run.** | A hot expression's cache is maintained by its dependency hooks, so a read is a field. A render that reads the same expression twice must not cost two runs. |
| **Becoming hot evaluates once, then hooks,** and raises nothing. | It is the first run when nothing has read the expression yet, and it is what makes the value hot-cached. The subscriber count just went from zero to one, so no earlier subscriber could have missed a change, and the new one can read `Value` for the current result. If that run throws, the registration still succeeds: the exception is cached, the dependencies it reached are hooked, and the subscriber finds out by reading (3.7). Failing the registration instead would put a lifetime decision on a type call - who unregisters the handler that was refused? - for a bug the read surfaces anyway. |
| **Becoming cold unhooks everything and keeps the result.** | Cheap teardown, and the value stays readable. It is just no longer trusted, per the row above. |
| **A dependency raise unhooks the old set, re-runs the function, then hooks the new set, and raises only if the result differs.** | The order is load-bearing, not a matter of taste (3.6.1). Re-collecting on *every* run is what makes conditional reads work: `if (flag) x = a.Value;` makes `a` a dependency only in the runs where it is read. Hooking the new set even when the result is unchanged is what keeps the graph honest for a value that is recomputed but equal. |

#### 3.6.1 Why the old set is unhooked first

| Decision | Why |
| --- | --- |
| **Unhook, then run, then hook.** | A dependency the expression no longer reads must not be able to start a second re-evaluation while the first is still running. Unhooking first means the run is the last thing the old set can influence: by the time the function executes, nothing in the old set reaches this expression at all, so a value the run is about to stop reading - or one that a handler of another expression just changed - cannot re-enter it. Hook-then-run would have exactly that hole, and the damage is not a duplicate render: a nested re-evaluation would re-collect while the outer run is mid-collect, so the two runs would interleave their dependency sets. |
| **A run that throws hooks what it read before the throw, and nothing else.** | The set is what the run accessed, on the failure path as much as the success one, and that is enough to recover: the exception happened at a point the run had already reached, so it was caused by a value the run had already read, and it cannot go away until one of those values changes. Hooking anything more - the old set, or values the run never reached - would be registering a dependency the result does not actually have. Two things follow. A value read *after* the throwing statement never contributed to the exception, so it does not need to wake the expression. And a function that throws before reading anything at all ends up hooked to nothing: reads still throw the cached exception (3.7), so it is loud when reached, but the only things that can revive it are reading it or recreating it - the honest cost of not inventing dependencies. |
| **A successful run whose result is unchanged still swaps the set.** | The set describes the current run, and a conditional read can change the set without changing the result. Keeping the old set would leave the expression hooked to values it no longer reads and deaf to ones it does. |

### 3.7 A function that throws is a cached result

- **A throw is a cached result, on the first run and on any later one.** There is no special "at
  creation" case, because creation does not run the function (3.6). Whether the run that threw was
  started by a read, by the first handler, or by a dependency change, the outcome is the same: the
  exception is cached in place of the value, the dependencies the run reached are hooked (3.6.1), and
  every read throws it. So a broken expression never silently serves a stale value.
- **Only a read has a caller to throw to.** A read can throw the exception at the code that asked. Every
  other run has nobody to throw at: a dependency-started run recomputes the cached result - the
  exception is the result - and then decides whether to raise its own value-changed event, and a run
  started by the first handler registers without raising to the handler that caused it (becoming hot
  raises nothing, 3.6). Whoever needs the value finds out when it reads it, which is where the
  exception belongs anyway.
- **By default every difference in the result is a change, including one exception replacing another.**
  A reader can branch on the exception it finds - its type, its message, an inner exception, a data
  entry - so "still failing, differently" is not the same result and subscribing to it must say so.
  With no comparer supplied, two exceptions are never equal to each other, because a run that throws
  builds a new exception, so a broken expression raises on each re-run that fails. A value and an
  exception are never equal under any comparer: only two exceptions are compared as exceptions.
- **An exception comparer is how an application decides that, and it is a judgement, not a default.**
  `CreateExpression` takes an optional `IEqualityComparer<Exception>`; the layer has no opinion about
  which exception details matter, because that is app-specific knowledge - a service that already
  reported "backend unavailable" twice does not want the second wait to look like progress, while a
  reader that switches on the message does. Suppressing a repeat affects only the notification: the new
  exception still replaces the cached one, so a read after a suppressed raise throws the newest
  exception, not the one the comparer called equivalent.

```csharp
// Collapse a repeated failure into one notification, while a read after it still gets the
// newest exception.
var total = Observable.CreateExpression(
    () => _service.Total(),
    exceptionComparer: EqualityComparer<Exception>.Create(
        (a, b) => a.GetType() == b.GetType() && a.Message == b.Message));
```
- **Nothing is reported anywhere.** This library has no report channel (section 8.1): a failure is
  visible through the value itself - a read throws it, and a success-to-failure or failure-to-failure
  change raises the event on the expression. A subscriber that reads `Value` in response gets
  the exception thrown into it, and that throw propagates out of `RaiseEvent` like any other
  (section 3.4), which is where a use case that cares has to catch it.
  where a use case that cares has to catch it.

### 3.8 Cycles, and expressions that mutate

| Decision | Why |
| --- | --- |
| **A thread-static evaluation stack detects cycles.** Before running, an expression checks whether it is already on the stack; if it is, it throws naming the chain (`Model.cs:88 -> Model.cs:214 -> Model.cs:88`). | A dependency cycle in a push-based graph is infinite recursion, and a `StackOverflowException` under .NET kills the process with no report. Naming the chain turns a crash into a message. The stack carries a name per expression (3.8.1), which is what makes the chain readable. |
| **Mutating an observable while a collection scope is active throws.** | An expression is a pure function of what it read. A write inside one raises into other expressions while the collector is mid-run, so the set being collected no longer describes what happened. `Observable` carries a collection depth for this, and `set`/`RaiseEvent` refuse while it is non-zero. Cheap - one static compare on a write - and it names the expression. |
| **A mutation from inside a *handler*** is ordinary and allowed. | Handlers run with no collection active: they may write other values, and those changes cascade. That is the event-driven style the layer is for. |
#### 3.8.1 How an expression is named

Two overloads, and the split exists to keep a path out of an application that did not ask for one:

| Overload | What the compiler emits |
| --- | --- |
| `CreateExpression(f, name, ...)` | nothing extra - a name was given, so this signature has no caller attributes to fill |
| `CreateExpression(f, ...)` | the creation site: a `[CallerFilePath]` constant and a `[CallerLineNumber]` integer |

An application that names the expressions it cares about therefore pays nothing at all, and one that
does not still gets `Model.cs:88` in the cycle chain and in the "changed state from inside its own
function" message. A name is *required* on the named overload and absent from the located one, which is
what makes the call site choose instead of the binder guessing - `CreateExpression(f, "total")` and
`CreateExpression(f, "total", comparer)` bind to the named one, `CreateExpression(f)` and
`CreateExpression(f, comparer)` to the located one. Nothing is reflected or looked up either way, and
the path is reduced to a file name only when a message is built (section 11 records what that costs).

### 3.9 Single-threaded, with no locks

Every observable is UI-thread state. Reads are a static field check and a `bool`; writes and raises
are list operations. No locks, no `Interlocked`, no `volatile`. Debug builds assert the thread that
created the first source, so a mistake is a message in development rather than a heisenbug in
production. A background thread that changes state marshals, exactly as it does to touch a control.

### 3.10 Dropping a reference is free; `Dispose` is the escape hatch

| Decision | Why |
| --- | --- |
| **Nothing needs disposing for memory.** | A source with no handlers roots nothing: the handler list is the only thing it holds, and the registrant holds the token. Dropping the last reference to an unwatched value collects it, with no bookkeeping anywhere. This is what makes observables usable as plain model state. |
| **`Dispose` marks the source dead, raises once, then clears the handlers - in that order.** | 1. It is dead: `IsDisposed` is true and `Value` throws `ObjectDisposedException` from that moment. 2. It raises its event once, to the handlers still registered, so a registrant learns its dependency is gone instead of quietly ceasing to hear from it. 3. It clears the list, which is what drops the registrants' closures - and the reason any of this exists, since a forgotten registration on a long-lived source would otherwise keep a dead screen's state alive for the life of the process. |
| **The order is the behaviour, not an implementation detail.** | A handler that reads `Value` during that final raise gets `ObjectDisposedException` rather than a last value: the source has stopped being meaningful, and handing back a number that will never update again is the kind of "still working" lie a lifetime bug hides behind. A handler that acts on the notification alone - unregister, release, mark the row gone - never reads and is unaffected; one that does read has that read throw out of `Dispose` like any other raise (section 3.4), with the handler list still released, because the release is in a `finally` and a leaked registration is the one outcome disposal exists to prevent. Raising first and marking dead afterwards would let a handler read a value it cannot rely on, which is a worse thing to hand to code that is being told to let go. |
| **After disposal every use throws, except the two that must not.** | `Value` (read or write), `RaiseEvent`, and registering a handler all throw `ObjectDisposedException`. `RemoveObservableEventRaisedHandler` stays safe - teardown paths dispose tokens without checking what they point at - and `Dispose` is idempotent. A dead source is a bug in the caller's lifetime, and saying so beat-by-beat is more useful than silently accepting writes to something nothing will ever read again. |
| **An expression whose dependency dies fails loudly.** | The final raise wakes it, the re-run's read of the dead dependency throws `ObjectDisposedException`, that becomes the expression's cached result (section 3.7), so it raises in turn and every read of it throws. A cascade of "the thing below me is gone" is the honest outcome; the alternative is a derived value that silently freezes. |
| **Unregistering a handler is the onus of whoever registers.** | The token is the unregister. An expression unhooks every dependency when it goes cold and on disposal, so the common accidental leak - a derived value that outlives its listeners - cannot happen. |

## 4. The collector, and the primitive under expressions

```csharp
// Internally, an expression runs its function inside a collection scope, and reads publish into it:
using (Observable.BeginCollection(scope))   // internal; CollectDependencies is the public face
{
    result = func();
}
```

Two public forms. `CollectDependencies(Action body)` runs a body that has no result;
`CollectDependencies<T>(Func<T> body)` runs one that does, and returns the result alongside the set:

| Member | Gives you |
| --- | --- |
| `Dependencies` | the distinct sources the body read, in first-read order |
| `IsFaulted`, `Fault` | whether the body threw, and the exception without throwing it again |
| `Value` (the `Func<T>` form) | the body's result; **throws `Fault`** if the body threw |
| `Throw()` | rethrows `Fault` if there is one, for the `Action` form which has no `Value` to throw through |

```csharp
var run = Observable.CollectDependencies(() => _panel.Render());
foreach (var dep in run.Dependencies)
{
    tokens.Add(dep.AddObservableEventRaisedHandler(_ => Invalidate()));
}

return run.Value;   // throws into whoever called this, with the set already hooked
```

| Decision | Why |
| --- | --- |
| **The set comes back even when the body threw.** | That is the rule a failing expression already follows (section 3.6.1): the dependencies a run *reached* are what its result - success or exception - was computed from, and therefore what can change it. Throwing straight through would hand the caller an exception and no set, which for a render means a component that never re-renders again. |
| **`Value` throws rather than returning a default.** | A caller that wants the result has to deal with the failure at the point where it uses it, and cannot mistake "the body failed" for "the body returned null". A caller that only wants the dependencies never reads `Value` and is unaffected. |
| **Nothing is reported to anyone.** | The caller is right here: this is a synchronous call with somebody waiting, so the exception goes to them through `Value`. There is no report channel to send it to as well (section 8.1). |

It exists because the component layer (section 9) needs exactly this and *not* an expression: a render
produces a tree it does not want to compare, so it wants "the dependencies of this run" plus "call me
when one of them changes". Expressing that through `CreateExpression` would mean comparing `VNode`
trees to decide nothing.

## 5. Reading and writing, in one place

```csharp
// A model. The observable is a backing field with a change notification, not something the
// model hands out: its own surface is ordinary properties.
internal sealed class DocumentModel
{
    private readonly IObservableMutableValue<string> _text = Observable.CreateValue(string.Empty);

    public string Text
    {
        get => _text.Value;
        set => _text.Value = value;      // raises only if it differs
    }
}

// A derived value: no registration, no strings, and it hooks nothing until someone listens.
var wordCount = Observable.CreateExpression(
    () => document.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
```

A read-only model is the same with the setter left off: `public string Text => _text.Value;`.

| Decision | Why |
| --- | --- |
| **The model exposes plain properties, not the observable.** | Dependency tracking happens at the *read*, not at the handle: the getter reads `Value`, and that read publishes into whatever collection scope is running - so an expression that uses `document.Text` is tracked exactly as one that used `document.Text.Value` would be. Handing out the observable instead would invite callers to hook the model's internals, which puts a registration where the model did not agree to keep one, and it makes every property name a two-name thing (`Text` and `TextObservable`). It is a backing field with a notification; the field stays private. |
| **A consumer that needs the dependency can still get it.** | Not by holding the observable - by reading the property inside an expression or inside `CollectDependencies`. That is the only way a consumer legitimately "cares that it is observable", and it is the same read either way. |

## 6. API surface, in full

| Member | Returns | Notes |
| --- | --- | --- |
| `Observable.CreateValue<T>(T initialValue)` | `IObservableMutableValue<T>` | Always a standalone value, unrelated to any other observable. |
| `Observable.CreateExpression<T>(Func<T>, string? name, IEqualityComparer<T>? = null, IEqualityComparer<Exception>? = null)` | `IObservableValue<T>` | The named form, and **it carries no caller attributes on purpose**: passing a name selects this overload, so no file path is compiled into the application at that call site (3.8). |
| `Observable.CreateExpression<T>(Func<T>, IEqualityComparer<T>? = null, IEqualityComparer<Exception>? = null, [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)` | `IObservableValue<T>` | The unnamed form: the compiler supplies the creation site, which becomes the expression's name in messages. Records the function and evaluates nothing; it runs on the first read or the first handler (3.6). In both overloads the first comparer decides when two `T` results are the same and the second when two exceptions are (3.7); null means `EqualityComparer<T>.Default` and, for exceptions, never equal. |
| `Observable.GetNamed(object key)` | `IObservableEventSource` | The same instance for the same key, for as long as the entry lives. Compose a multi-part name into one key object; the layer does not look inside it. Creates the entry if there is none, because the caller is about to register on it. |
| `Observable.RaiseNamedEvent(object key)` | `void` | Raises the named dependency for `key` without materialising a handle, and **without creating an entry**: a raise for a key nobody watches is one lookup and nothing else. |
| `Observable.PublishRead(IObservableEventSource)` | `void` | The primitive behind `DependUpon`. Public because a hand-written source needs it. |
| `Observable.CollectDependencies(Action)` | `Collected` | The dependencies of one synchronous run, `IsFaulted`/`Fault` if the body threw, and `Throw()` for the form that has no `Value`. |
| `Observable.CollectDependencies<T>(Func<T>)` | `Collected<T>` | The same plus `Value` - the body's result, or the exception thrown through it. This is the component seam (section 9). |

No `services.AddObservables()`: there is nothing to register, no process-wide state that needs a
container, and the named table is created on first use.

## 7. What collections will need (next step, not this one)

Recorded here so the primitive is built to fit them rather than retrofitted:

- **Access by key or index**, iteration and `Count` are **different dependencies**. A read by key
  depends on that key's named source; `Count`, `Keys` and enumeration depend on a *shape* source for
  the collection, which is raised by any addition, removal or clear.
- **A change raises both** the touched key's source and the shape source (both, when a write replaces
  an existing value). The per-key half is `Observable.RaiseNamedEvent(key)`, which costs a lookup and
  creates nothing for a key nobody is watching - a collection that mutates in a loop is not going to
  allocate an entry per mutation. Raised after the mutation completes, never inside a loop over
  storage, because a handler is allowed to re-enter the collection.
- **Per-collection scoping** is the collection's own business, since `GetNamed` takes one opaque key: a
  dictionary keys its dependencies however it likes - `(this, key)` for a tuple, or its own key type
  that compares on both - so two dictionaries that both hold a `"name"` entry cannot collide without
  the layer knowing anything about ownership.
- **Enumerating does not depend on every key** - that would hook N sources to render a list. It
  depends on the shape source, and rows that care about their own values depend on their own keys.

## 8. Failure policy summary

| Situation | Behaviour |
| --- | --- |
| Expression's function throws on its first run (first read or first handler) | cached as the result, the dependencies it reached are hooked, and every read throws it; a read-started run throws to that caller, and a run started by the first handler attaches without raising |
| Expression function throws on a dependency-started re-run | cached as the result, raised (a new exception is a changed result), and every read throws it. The raise is the only notification the library gives - there is no report channel (section 8.1) |
| A handler throws during a raise | nothing is caught: it propagates out of `RaiseEvent`, and the handlers after it in that dispatch do not run |
| A collector body throws | the exception is returned in `Fault`, `Value` throws it, and the set the body reached comes back with it |
| A dependency cycle is read | `InvalidOperationException` naming the chain, before any recursion |
| A write inside a running expression | `InvalidOperationException` naming the expression |
| Using a disposed source: reading `Value` or writing it, `RaiseEvent`, or registering a handler | `ObjectDisposedException` |
| Registering on an evicted named dependency | the handle is dead: `IsDisposed` is true and every use throws `ObjectDisposedException`, registering included. That is deliberate - a cached handle that quietly stops working is the one way a dependency can go silent, so it is made loud. A later `GetNamed` for the same key returns a fresh handle, and the situation cannot arise while an entry is still watched, since a handler is what keeps the entry alive |

### 8.1 The library imposes no failure policy, and talks to nobody

Nothing in this package reports an exception anywhere. It does not reference `Application`, a host, a
window or any other part of Kolvir - it is a data-model library over the BCL, which is what makes it
usable in a test, a console host or a server process as much as in a UI (section 12.1). So an exception
from a handler is simply not caught (section 3.4), an exception from a function is stored as the
expression's result and raised like any other change (section 3.7), and the only acknowledgement a
failure gets is the value itself, which throws it to whoever reads it.

That is deliberate, because the right response is different for different uses and the library cannot
know which it is in the middle of:

| Use | What it should do about a throw | Where that lives |
| --- | --- | --- |
| A virtual DOM component whose render reads model values | Let the render fail into the frame's own boundary, having already hooked the dependencies it did read, so the component re-renders on the next change instead of being stranded | `VIRTUAL-DOM.md` section 7.2 and this document section 9 |
| A model used by an event handler | Nothing: the exception surfaces where the change was made, which is inside a control event and therefore inside the host's own boundary | The application |
| A test | Nothing: the exception arrives at the assertion that caused the change, unwrapped and with the original stack | The test |
| A background or server process | Catch where the change is made and log it; there is no UI to show it in | The application |

The pattern those share is that the caller of the change is the outermost frame that can do anything
useful - so the library leaves the decision to it rather than shipping a policy it would have to call
back into an application to apply.

## 9. The component seam (a later step, in the Virtual DOM feature)

`VIRTUAL-DOM.md` section 7.3 split the mount's protocol out so a base class could be replaced. This is
the replacement: a base class in `Kolvir.Feature.VirtualDom` that holds a data model, renders from it
inside a collection scope, and listens to exactly what that render read.

```csharp
public abstract class ObservableComponent<TDataModel> : IComponent, IDisposable
    where TDataModel : class
{
    private ComponentMount? _mount;
    private TDataModel? _dataModel;
    private List<IDisposable>? _deps;
    private List<IDisposable>? _attachDeps;
    private bool _isDisposed;

    public TDataModel? DataModel
    {
        get
        {
            ThrowIfDisposed();
            return _dataModel;
        }
        set
        {
            ThrowIfDisposed();

            // Identity, not value equality: replacing the model is the only change this setter can
            // see, and a change *inside* a model arrives through that model's own observables.
            if (object.ReferenceEquals(value, _dataModel)) { return; }

            _dataModel = value;
            _mount?.Invalidate();   // all of "a data change asks for a render"
        }
    }

    public readonly record struct RenderArgs(Action<IDisposable> AddDisposable);
    public readonly record struct RenderArgs<T>(T DataModel, Action<IDisposable> AddDisposable);
    public readonly record struct AttachedArgs(Action<IDisposable> AddDisposable);

    public VNode Render()
    {
        ThrowIfDisposed();

        // Re-rendering re-collects: the dependency set follows the render, exactly as an
        // expression's follows its function.
        CleanupDependencies();
        var deps = _deps = new List<IDisposable>();

        var run = _dataModel is null
            ? Observable.CollectDependencies(() => RenderWithNullModel(new RenderArgs(deps.Add)))
            : Observable.CollectDependencies(
                () => RenderWithDataModel(new RenderArgs<TDataModel>(_dataModel, deps.Add)));

        // Hooking before reading the result is the point: a render that threw still leaves the
        // component listening to what it read, so the next change to one of those values asks for
        // another render instead of leaving the component stranded (section 3.6.1).
        foreach (var dep in run.Dependencies)
        {
            deps.Add(dep.AddObservableEventRaisedHandler(_ => _mount?.Invalidate()));
        }

        return run.Value;   // throws into the frame boundary if the render failed
    }

    protected abstract VNode RenderWithDataModel(RenderArgs<TDataModel> args);

    // TODO: the intended default is "a node that produces no control", which needs an empty node type
    // this feature does not have yet (section 11). Until then, not overriding it is worth being loud
    // about, rather than quietly rendering nothing - which looks exactly like a broken binding.
    protected virtual VNode RenderWithNullModel(RenderArgs args) =>
        throw new NotSupportedException(
            $"{GetType().Name} has no data model and does not override RenderWithNullModel.");

    protected virtual void OnAttached(AttachedArgs args) { }
    protected virtual void OnDetached() { }

    void IComponent.Attach(ComponentMount mount)
    {
        ThrowIfDisposed();
        if (_mount is not null) { throw new InvalidOperationException("Already mounted."); }

        _mount = mount;
        CleanupAttachDependencies();
        var attachDeps = _attachDeps = new List<IDisposable>();
        OnAttached(new AttachedArgs(attachDeps.Add));
    }

    void IComponent.Detach(ComponentMount mount)
    {
        // A stale mount, or the undo of a first render that threw, is not this component's mount.
        if (!ReferenceEquals(_mount, mount)) { return; }

        _mount = null;
        OnDetached();
        CleanupDependencies();
        CleanupAttachDependencies();
    }

    public void Dispose()
    {
        if (_isDisposed) { return; }
        _isDisposed = true;

        // Disposed while still mounted: detach here rather than wait for a Detach that may never
        // come, so a derived class is told once and the mount's later Detach finds nothing to do.
        if (_mount is not null)
        {
            _mount = null;
            OnDetached();
        }

        _dataModel = null;
        CleanupDependencies();
        CleanupAttachDependencies();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);

    private void CleanupDependencies()
    {
        var tokens = _deps;
        _deps = null;
        DisposeAll(tokens);
    }

    private void CleanupAttachDependencies()
    {
        var tokens = _attachDeps;
        _attachDeps = null;
        DisposeAll(tokens);
    }

    private static void DisposeAll(List<IDisposable>? tokens)
    {
        if (tokens is null) { return; }
        foreach (var token in tokens) { token.Dispose(); }
    }
}
```

**Two token lists with two lifetimes** is the part an example does not have, and it is what makes this
a base class:

| List | Lifetime | What belongs in it |
| --- | --- | --- |
| `_deps` | one render | the handlers on the observables that render read, plus anything the render itself registered through `RenderArgs.AddDisposable` - a subscription set up behind a condition, a one-shot listener |
| `_attachDeps` | one mount | whatever `OnAttached` registered through `AttachedArgs.AddDisposable` - a timer, a keyboard hook, a subscription that should outlive every render |

Clearing the render list at the top of a render, and both on detach, is what makes re-collecting work: a
render that stops reading a value stops listening to it, and one that starts reading it starts. Both
lists are cleared before their tokens are disposed, so a token's `Dispose` that raises a change back
through the model cannot re-enter the list being walked.

Three things this gets for free:

- **A data change is a re-render on the next frame, not inside the raise.** The handler calls
  `ComponentMount.Invalidate`, which marks the component and asks for a frame; the render happens at
  the top of the next frame (`VIRTUAL-DOM.md` section 7.1). So the glitches in section 3.5 cannot reach
  the screen, and a handler that changes five model values still costs one render.
- **A write during the render cannot loop.** `Invalidate` is refused from inside a render, which
  `IComponent.Render` already states and which is the same rule as a write inside a collection scope
  (section 3.8).
- **A render that fails *later* keeps its dependencies, and a first render that fails keeps none.**
  Hooking before `run.Value` is what leaves a failing render listening, so the next change to one of
  the values it did read asks for another render instead of stranding it. On a *first* render the mount
  undoes the attach instead, so `Detach` releases those hooks again immediately - correct, because a
  component that never produced a tree has nothing to patch.

Three things it must respect:

- **`Detach` cannot trust its caller.** The mount calls it both on dispose and as the undo of a
  throwing first render, and a component can be mounted again elsewhere, so it is a reset rather than a
  tombstone - hence the `ReferenceEquals` guard, and hence `Attach` refusing a second live mount
  instead of overwriting `_mount`.
- **`OnDetached` runs exactly once**, from whichever of `Detach` and `Dispose` happens first, because
  both clear `_mount` before calling it. A derived class that releases attach-scoped resources in
  either place gets one call, not two.
- **`Dispose` is ordered like the sources are** (section 3.10): mark dead first, so a `DataModel` read
  or a `Render` from inside a token's `Dispose` throws rather than half-working, and drop the
  references last.

`where TDataModel : class` is not an accident. `ReferenceEquals` needs it, and so does "no model yet",
which wants `null` rather than `Nullable<TDataModel>`. A struct model would be a different base class
over the same core, and is not designed here. Identity is the only question the setter asks, which is
why it compares references and not values: a change inside a model is announced by that model, so all
that is left for the setter to notice is the instance itself being swapped.

The one consequence outside this section: `Kolvir.Feature.VirtualDom` gains a package reference to
`Kolvir.Feature.Observables`. The dependency runs one way only - observables still know nothing about
components (section 8.1).

## 10. Options considered and rejected

| Option | Why not |
| --- | --- |
| `INotifyPropertyChanged` and string property names | The failure this layer exists to avoid: a typo is a compile error here and silence there, and a handler cannot know what a caller actually read. |
| `System.IObservable<T>` / `IObserver<T>` and `Subscribe` | The name collides outright with the BCL's generic interface in any file that has `using System;` (measured: CS0104), and Rx's contract - a sequence with completion and errors on a subscription - describes a stream, not a mutable value and its readers. `IsDisposed` plus a final raise borrows the useful part of the completion story without the subscription type. |
| Weak references between a source and its handlers | Every read of the graph would test and resurrect, and the semantics of "the handler went away" would have to be invented. Explicit tokens and disposal (section 3.10) put the cost where the mistake is. |
| Hard-coding an exception comparison - type and message - to suppress "still failing" notifications | A reader may branch on any detail of the exception it finds, so no built-in comparison can be right for every application: one wants repeats collapsed, another wants the message to matter. The comparison is therefore an optional `IEqualityComparer<Exception>` on the expression (section 3.7), and the default is identity - a failing re-run is a change. |
| Named dependencies that carry a value | A value that lives in an evictable entry resets when the last listener leaves - and eviction happens in the middle of a re-evaluation that unhooks before re-hooking (section 3.2). The event-only form has no such state to lose. |
| Disposal that clears handlers silently, with no final raise | Simpler, and the alternative chosen is a judgement call: a final raise makes a dependency's death visible to whoever was watching instead of quietly freezing a derived value. Worth confirming (section 11). |
| Lazy, pull-on-read evaluation for all expressions | Glitch-free and no wasted runs, but every read becomes a recursive walk and every value needs a dirty flag. Section 3.6's cold/hot split gets the useful half of it (no work when nobody watches) without that cost. |
| A lock, or `Interlocked` guards, to allow cross-thread use | The graph's invariants are per-dispatch-list, and a correct thread-safe version is a different design (queued delivery, or per-thread graphs). The library is single-threaded everywhere else. |
| Ordering dispatch by dependency depth | Would remove most of section 3.5's re-entrancy. Additive later if a use appears; nothing outside the package depends on delivery order. |
| An async collection, `CollectDependenciesAsync(Func<Task>)` | A scope has to be active while the body runs, and an async body suspends: everything the UI thread does in the meantime would publish into whichever scope the thread happened to hold, so the set would describe someone else's reads. Making it correct needs the scope in an `AsyncLocal` rather than the static slot, which is a second collector, a slower read on the fast path (or a two-tier check), and a value that cannot be an expression anyway - a value has to be current when it is read. Left out rather than half-built; a loader that needs to re-run can collect around its synchronous part and `await` between collections. |

## 11. Risks and open questions

- **Delivery is not glitch-free (section 3.5).** A listener may run twice for one logical change and
  may read an intermediate value. Acceptable for renders because of the frame queue; a listener that
  *acts* on the intermediate value needs to know. Documented, not mitigated.
- **A wide graph re-runs often.** One change re-runs every hot expression that depends on it, and
  each of those may raise to its own subscribers. To be measured on a realistic page; the escape
  hatches are the comparer (stop the cascade) and coalescing at the component.
- **A broken expression raises on every re-run, unless it is told not to.** Each failing run hands out a
  new exception, and by default a new exception is a changed result (section 3.7), so a dependency
  that churns while a function is broken notifies on every change. An application that would rather
  collapse repeats passes an `IEqualityComparer<Exception>`, which puts the judgement where the
  knowledge is - but a comparer that is too loose hides a change a reader would have acted on.
- **`CallerFilePath` embeds the build machine's paths in the assembly, wherever no name is given.**
  Measured on two throwaway assemblies: four unnamed `CreateExpression` calls in one file embed the
  file's **absolute** path exactly once (identical literals deduplicate, and the DLL grew by about 2 KB
  against the named-only build), while an assembly whose calls all pass a name embeds no path and no
  file name at all. So it is a per-file cost, paid only by call sites that did not name themselves, and
  a published binary then carries `C:\Users\...` or `/home/...` strings it does not need. Three
  mitigations, in order of preference: name the expressions that matter (3.8 - that overload has no
  caller attributes, so nothing is emitted), render only `Path.GetFileName(file)` plus the line and
  never display the whole path, and set `PathMap` when reproducible or path-free output matters, since
  the compiler applies it to these constants too.
- **Equality is the trap for object results.** An expression returning a freshly built object that does
  not override `Equals` raises on every run. That is why the component seam does not compare the trees
  it renders: a render that returns a fresh tree would raise on every re-run, and the frame queue is
  what collapses those into one render.
- **Object keys box value types on a miss.** A dictionary keyed by `int` pays a box per `GetNamed` call
  that misses its cache. Collections will cache handles per key; a caller that reads in a hot path
  should hold the handle.
- **The named table is process-wide, and only as large as what is watched.** Names are opaque `object`
  keys, so the growth is bounded by the keys that someone is actually listening to - but a long-lived
  scope with churning keys relies on eviction, which only happens when the last handler goes.
- **`IsDisposed` and the final raise are additions this design makes to the spec** (section 3.10,
  section 10) and are the likeliest thing to be reconsidered.
- **The component seam has no way to render nothing yet** (section 9). `RenderWithNullModel` wants to
  default to a node that produces no control, and the Virtual DOM feature has no such node type - every
  node describes one control, and an empty `Children` list on a container is not the same thing. So the
  default throws for now, and either an empty node type is added or the local model in
  `ObservableComponent` becomes `TDataModel` plus a flag.
- **To confirm before the code exists:** `IsDisposed`; the final
  raise on disposal; `CollectDependencies` as public API; refusing mutation inside a collection
  scope; and `ObservableComponent` living in the Virtual DOM feature rather than in a third package
  over both (the reference runs one way, so the placement is a choice, not a necessity).

## 12. Steps

| # | Step | Done when |
| --- | --- | --- |
| 1 | Package skeleton, the interfaces, the mutable value and snapshot dispatch | A value raises only when it actually changes; a handler can subscribe and unsubscribe during dispatch, including itself; a throwing handler propagates out of the raise and the handlers behind it do not run |
| 2 | The collector stack, `CollectDependencies` and `CollectDependencies<T>` | Nested scopes stay separate; duplicates collapse; a read outside a scope costs nothing; an exception restores the scope and still arrives with the set the body read |
| 3 | Expressions: cold/hot, caching, re-collection, equality, the exception comparer | `CreateExpression` runs nothing; a cold expression re-runs on every read and hooks nothing; a hot one hooks its dependencies, re-runs on a change, re-collects, and raises only when the result differs |
| 4 | Named dependencies: one opaque key, one handle, raising without a handle, eviction | Two handles for one key are the same object; `RaiseNamedEvent` reaches a watched key and does nothing for an unwatched one; an entry disappears when its last handler goes, and not during a raise |
| 5 | Guards: cycles, mutation inside a scope, disposed reads | Each throws a named message instead of recursing, silently corrupting, or serving a stale value |
| 6 | The component seam in `Kolvir.Feature.VirtualDom` | A component whose render reads model values re-renders on the next frame after one changes, stops listening to a value its render stopped reading, and releases render-scoped and attach-scoped tokens independently (section 9) |
| 7 | A demo page, the developer-guide section, and the AOT measurement | The published sample renders the page byte-for-byte identically to the JIT build, and a minimal app publishes with no observables artifact |

Step 6 is the reason for the other six, and it is deliberately last: the primitives are testable
without a window, and the seam is testable without a second renderer.

### 12.1 What else has to change

Five things outside the new code, in the shape `VIRTUAL-DOM.md` section 8.1 recorded for its feature:

1. **`src/features/Kolvir.Feature.Observables/`** in the rung-3 four-folder layout, all of the code in
   `common/`, namespace `Kolvir.Observables`, `IsAotCompatible`, and **no reference to Kolvir at all** -
   nothing here needs a control, a host or an application (section 8.1).
2. **`tests/features/Kolvir.Feature.Observables.Tests/`**, which references the package under test and
   nothing else - no scene, no host, no window.
3. **`Kolvir.slnx`** gets both, under `/src/features/` and `/tests/features/`. The solution is
   hand-listed, not globbed.
4. **`.github/workflows/tests.yml`** gets a restore step and a test step for the new suite. The
   workflow's `src/features/**` and `tests/**` filters already cover both trees, so only the two
   steps are new.

XML documentation is mandatory and CS1591 is an error for anything that ships
(`Directory.Build.props`), so every public member in this design needs a `<summary>` - the interface
table in section 2 is that list.

## 13. Verification approach

Headless tests in `tests/features/Kolvir.Feature.Observables.Tests`, in the same shape as the other
feature suites: no window, no frame, no timing. The claims that get special treatment:

| Claim | How it is proven |
| --- | --- |
| **A raise dispatches over a snapshot** | A handler that removes itself, and one that adds another, both leave the current dispatch's remaining handlers running and the next dispatch reflecting the change. |
| **One entry, one identity** | Two `GetNamed` calls for one key return the same instance, and an expression reading the same key twice hooks it once (asserted on the handler count, not on the set). `RaiseNamedEvent` reaches the handler of a watched key and does nothing at all for an unwatched one. The cost claim - one lookup, no entry, no allocation - is a measurement on the same day the read costs are measured, not a test, because "no entry was created" is not observable through the public API. |
| **Cold work costs nothing** | `CreateExpression` runs the function zero times; an expression with no handlers has zero dependency hooks (asserted on a counting source) and re-runs on every read; attaching the first handler runs it once more and stops the re-running. |
| **Re-collection follows the function** | A conditional read adds and removes a dependency as the condition flips, and a value that stops being read stops raising. |
| **A failed run still yields its dependencies** | `CollectDependencies(() => throw ...)` returns the sources it read before the throw, `IsFaulted` true, and a `Value` that throws the same exception instance - for both collectors - which is what lets the seam hook the set and then fail. |
| **The old set is gone before the run** | A hand-written `IObservableEventSource` that counts its own registrations (public API, no internal hook) is read by an expression; while the function executes after a dependency raise, its count is zero for everything the previous run had hooked, and one again once the run finishes. That is 3.6.1 made observable: the run cannot be re-entered by a set it has already let go of. |
| **Equality keeps the graph quiet, except for exceptions** | Writing an equal value raises nothing; an expression whose result is unchanged raises nothing but still re-hooks a changed dependency set. A re-run that fails again raises by default, because the replacement exception is a changed result, and does not raise when the expression was given an `IEqualityComparer<Exception>` that calls the two equivalent - while a read after that still throws the newer exception (3.7). |
| **Failures are visible, not silent** | A function that throws on a dependency-started re-run leaves the expression raising (a new exception is a changed result) and throwing on every read, with nothing thrown at anybody during the run itself; a cycle throws with the chain named; a write inside a scope throws; and every use after `Dispose` throws `ObjectDisposedException` - read, write, raise, or register. Nothing is reported anywhere, which is asserted by the absence of an application dependency in the package (section 8.1). |
| **Disposal is ordered, and the order is observable** | A handler registered on a value source sees `IsDisposed` true and gets `ObjectDisposedException` if it reads `Value` during the final notification, while a handler that only counts the notification sees it exactly once - so "dead first, then one raise, then the list is cleared" is asserted rather than assumed. An expression whose dependency is disposed raises in turn, with the `ObjectDisposedException` as its cached result. Afterwards, `Value`, `RaiseEvent` and registration all throw while removing a handler and disposing again stay safe. |
| **The seam re-renders on the frame after a change** | In the Virtual DOM suite: a component reading two model values renders once per frame however many of them changed, stops listening to a value its render stopped reading, and unhooks everything when detached. A token registered through `RenderArgs.AddDisposable` dies on the next render while one registered through `AttachedArgs.AddDisposable` survives it and dies on detach, and `OnDetached` runs exactly once across dispose-then-detach. |
| **AOT- and trim-clean** | The sample's published render is byte-identical to the JIT build's, and `Kolvir.Minimal` publishes with no observables artifact. The feature has no reflection, no `dynamic`, and no serializers, so this is expected rather than arranged. |

## 14. Progress

| Step | State | Notes |
| --- | --- | --- |
| 1 | Done | The package, the four interfaces, `CreateValue`/`ObservableValue<T>` and snapshot dispatch, plus the read scope the collectors will use. Sixteen tests, no reference to Kolvir, and the package packs three `lib/` folders with an empty dependency group each |
| 2 | Not started | |
| 3 | Not started | |
| 4 | Not started | |
| 5 | Not started | |
| 6 | Not started | |
| 7 | Not started | |