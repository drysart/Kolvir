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
| **A handle is a name, not an entry: `GetNamed` makes a fresh one every call, and handles for equal keys compare equal.** | The dependency set de-duplicates by the source's own equality (section 3.3), so two handles for one key are one dependency - an expression that reads the same key twice hooks it once, whichever instances it got. Comparing by key is what makes them interchangeable, and interchangeability is what frees the handle of a lifetime: the table resolves the key on every operation, so a handle cannot be stale, and a function or a model may keep one for as long as it likes. The cost is an allocation per call, for an object that holds a key and nothing else. |
| **An entry exists only while it has a handler, and is dropped the moment the last one is removed.** | That is the memory story: a dependency nobody is listening to is not memory, and the table is exactly as large as what is subscribed. It can be dropped on the spot, with no deference to what is in progress, because nobody holds the entry: a raise walks an immutable snapshot, so a handler that unregisters itself mid-dispatch removes the key without disturbing the walk; an expression that released a key and then re-read it registers on a new entry; and a handle is a name, so it resolves to whichever entry exists. Making the handle *be* the entry - one shared instance per key - is the design that forces eviction to wait for the first idle point, and that wait is all the deferral buys. |
| **A handle has no lifetime, so nothing about it can fail.** | It holds no handlers and no value, and `IsDisposed` is always false: the only thing the table can take away is the entry, and the handle does not own one. That removes a whole class of bug - a handle cached a moment too long either throws somewhere unrelated (a stale-handle rule) or quietly stops hearing its key (no rule at all) - and it lets a model hold the handle it raises and a function hold the one it reads, which is the natural way to write both. |
| **Raising does not need a handle: `Observable.RaiseNamedEvent(object key)`.** | The common shape is a model that raises without wanting to register - a dictionary announcing that a key changed - and going through `GetNamed` for it would allocate a handle for a key nobody is watching. `RaiseNamedEvent` raises the entry if there is one and does nothing if there is not, so an unwatched key is a boxed lookup and no more. `GetNamed` creates nothing either: an entry appears when something first subscribes, so a handle for a key nobody watches costs one object and no table write. |

### 3.3 Collecting dependencies: a stack, not a slot

`PublishRead(source)` hands the source to the *innermost* active collection scope, which records it
**and subscribes to it there and then**. A scope is entered by `CreateExpression` while it runs its
function, and by `CollectDependencies(body, onChanged)` (section 4). Nested scopes are isolated: the
outer run never sees what the inner run read.

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
- **Duplicates are removed per run** by the source's own equality, so an expression that reads `Count` in a
  loop subscribes to the count source once, and one that reads a named key twice - through two different
  handles - subscribes to it once as well. A value or an expression has no equality of its own, so for them
  this is reference identity; a named handle compares by its key (section 3.2).
- **A read that returns has already subscribed.** The scope subscribes during the read rather than
  afterwards, so there is no window in which a body holds a value and is not yet watching what
  produced it. It is also why a cold expression read from inside an outer run is hot by the time the
  read returns: the value handed back is the one it keeps, rather than the result of an unmonitored
  run that is thrown away and immediately recomputed.
- **A cold read collects nothing**, because it happens with no run in progress - if one were, the read
  would have subscribed this expression and made it hot before the question was asked.
- **A change from inside a run is allowed.** A function may write a value it did not read - scratch
  state, a last-evaluated stamp - and any other expression that value feeds re-evaluates nested,
  inside this run, exactly as it would from a handler. A write to something *this expression* read is
  allowed too, and is where a loop comes from: the run is subscribed to what it read, so the raise
  comes straight back into a run that is still going, and it does so again at every level until the
  evaluation depth refuses one (section 3.8). Nothing about collection forbids a write.

### 3.4 Dispatch: snapshot, isolate, never re-enter the list

| Decision | Why |
| --- | --- |
| **A raise iterates a snapshot of the handler list, and the snapshot is free.** | The handlers are an `ImmutableArray<Action<IObservableEventSource>>`, so subscribing or unsubscribing *replaces* the array rather than editing it: the raise that is running keeps walking the array it began with, and no copy is made to get there. Registering copies, which happens once per subscription; raising copies nothing, which happens on every change. That is what lets a handler subscribe and unsubscribe freely - including itself - without disturbing the dispatch that is running: mutations apply to the next raise, and a handler that disposes the source mid-dispatch does not stop the walk either, because the remaining handlers are already in the array being read. |
| **Nothing is caught: an exception from a handler propagates.** | A raise is a synchronous callback and behaves like one: as with a multicast delegate, the first handler that throws ends the notification and the exception travels out of `RaiseEvent` to whoever caused the change. This library has no reporting channel to hand it to and no policy to impose, and each alternative was worse - swallowing it silently, inventing a target for it, or rethrowing after the loop, which would assume that the remaining handlers must still be told even though nothing can vouch for the state after a bug. A use case that needs more (a render that must survive its own failure, say) is where that judgement belongs (section 8.1). The consequence to be aware of: handlers after the throwing one do not run for that change. |
| **A raise counts how deep it is nested, in a depth it shares with a cold read.** | Dispatch is where a runaway graph actually happens: a handler runs inside the raise that called it, so a chain that comes back around is a stack of raises, one frame per turn. Counting there needs to know nothing about what the chain runs through, which is what makes it cheap enough to do on every raise and complete enough to catch a loop that wanders through several values and expressions without ever closing on the same one. A cold read is counted in the same depth, because a cold run nests the same way (section 3.8). |
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
| **Cold** - no handlers on it | no | re-runs the function every time, with no run in progress and so nothing collected |
| **Hot** - at least one handler | yes, all of them | returns the cached result |

| Decision | Why |
| --- | --- |
| **Creation runs nothing.** | A derived value that nobody reads should cost a delegate and nothing else - not a run, not a dependency set, not an exception it might throw before anyone is listening. It also means a function that throws is only ever discovered by somebody who asked for the result, which is the same rule as a read.
| **Cold expressions hook nothing.** | An expression nobody watches is a function, and paying to keep its dependencies wired up would make an unused derived value cost the same as a used one - and would keep its whole dependency tree alive. A cold read is a read with no run in progress, so there is nothing to collect and nothing to keep: the read calls the function directly. Read from inside somebody else's run it would not be cold at all - that read subscribes it and makes it hot before the question is asked (section 3.3). |
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

### 3.8 Loops, and how deep evaluation may nest

| Decision | Why |
| --- | --- |
| **Every step of evaluation counts how deep it is nested, and the step that would nest deeper than `Observable.MaxEventStackSize` (fifty) is refused** with an `InvalidOperationException` naming the source and the limit; the rest of the chain is abandoned. A step is one event raise or one cold read. | A graph built on notification can loop, and every loop ends in the same place: another step taken inside the one before it. Counting steps therefore catches one wherever it is, without knowing which values or expressions it runs through, and it costs a field read and a compare per step - nothing per value read, nothing per frame. The two steps are counted together because they are the same recursion: a handler may write and the write raises, and a cold expression's function runs and may read another expression, which runs in turn. Counting only raises left the second shape uncatchable, and counting only expressions left the first. Tried and rejected: a per-expression "am I running" flag catches only the expression that closes the loop, and cannot see a loop that passes through three objects and never returns to the same one; a blanket ban on writing during a run refuses a real pattern - a function keeping its own scratch state - to prevent a bug. A number rather than a proof, because depth is all either step can see, and because legitimate evaluation does nest: one change settling a handful of dependent expressions is a chain of three or four, and so is reading a graph of derived values from the top while nothing is subscribed. Fifty sits far above any of that, and the property exists for the application whose graphs are deeper. |
| **The refusal is sticky until the outermost step unwinds.** Every raise or cold read attempted by anything still unwinding through that chain is refused too. | One `catch` between the refused step and the original change is enough to restart the descent from one level up and run the same number of levels again - and something does catch: a collector turns a throwing run into a cached failure, which is the documented behaviour for every other exception (section 3.7). Sticky turns "stops eventually, after burning an unpredictable amount of work" into "stops once, where the limit is", and leaves the failure in the collector's cache like any other, so a read afterwards throws it. |
| **A mutation from inside a handler is ordinary and allowed.** | Handlers exist to write: that is the event-driven style the layer is for, and it is how a change to one model settles another. A cascade is not a loop merely because it nests. |

Two loops, and both are now refused rather than left to the stack:

```csharp
// A handler that writes what it hears about: each write raises, and the raise reaches the handler again.
var value = Observable.CreateValue(0);
value.AddObservableEventRaisedHandler(_ => value.Value++);
value.Value = 1;                       // InvalidOperationException at fifty levels

// An expression whose function reads itself, with nothing subscribed - so the read is a plain run.
IObservableValue<int>? self = null;
self = Observable.CreateExpression(() => self!.Value + 1);
_ = self.Value;                        // InvalidOperationException at fifty levels
```

A chain that ends is not evidence the graph is sound, and a chain that does not end is not evidence it
is broken: the limit knows only how deep it got, and the message names the source of the step that was
refused, which is the end of the chain rather than its cause. What it buys is that the depth is bounded
by a number the application chose - so a loop arrives as an exception in a caller that can log it,
instead of as a `StackOverflowException` that kills the process with no report at all.

#### 3.8.1 How an expression is named

Two overloads, and the split exists to keep a path out of an application that did not ask for one:

| Overload | What the compiler emits |
| --- | --- |
| `CreateExpression(f, name, ...)` | nothing extra - a name was given, so this signature has no caller attributes to fill |
| `CreateExpression(f, ...)` | the creation site: a `[CallerFilePath]` constant and a `[CallerLineNumber]` integer |

An application that names the expressions it cares about therefore pays nothing at all, and one that
does not still gets `Model.cs:88` as the source named by a refused step (section 3.8). A name is
*required* on the named overload and absent from the located one, which is
what makes the call site choose instead of the binder guessing - `CreateExpression(f, "total")` and
`CreateExpression(f, "total", comparer)` bind to the named one, `CreateExpression(f)` and
`CreateExpression(f, comparer)` to the located one. Nothing is reflected or looked up either way, and
the path is reduced to a file name only when a message is built (section 11 records what that costs).

### 3.9 Single-threaded, with no locks

Every observable is UI-thread state. Reads are a static field check and a `bool`; writes and raises
are list operations; the evaluation depth is a plain `int` on the same thread. No locks, no `Interlocked`,
no `volatile`. Debug builds assert the thread that created the first source, so a mistake is a message
in development rather than a heisenbug in production. A background thread that changes state marshals,
exactly as it does to touch a control.

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

A run happens inside a collection scope. `PublishRead` hands the source to that scope, and the scope
**records it and subscribes to it there and then**, with the handler the caller supplied:

```csharp
// Internally, which is also what the public form does:
using (ReadScope.Begin(new CollectionScope(onChanged, description)))
{
    result = body();
}
```

Two public forms. `CollectDependencies(Action body, Action<IObservableEventSource> onChanged)` runs a
body that has no result; `CollectDependencies<T>(Func<T> body, onChanged)` runs one that does, and
returns the result alongside the subscriptions:

| Member | Gives you |
| --- | --- |
| `Dependencies` | the distinct sources the body read, in first-read order, each already subscribed |
| `IsFaulted`, `Fault` | whether the body threw, and the exception without throwing it again |
| `Value` (the `Func<T>` form) | the body's result; **throws `Fault`** if the body threw |
| `Throw()` | rethrows `Fault` if there is one, for the `Action` form which has no `Value` to throw through |
| `Dispose()` | releases every subscription the run took |

```csharp
// A render, in full: the run is listening to what it read by the time it returns, and the token for
// that listening is the run itself.
_render?.Dispose();
_render = Observable.CollectDependencies(() => _panel.Render(), _ => Invalidate());

return _render.Value;   // throws into whoever called this, still subscribed
```

| Decision | Why |
| --- | --- |
| **The set comes back even when the body threw.** | That is the rule a failing expression already follows (section 3.6.1): the dependencies a run *reached* are what its result - success or exception - was computed from, and therefore what can change it. Throwing straight through would hand the caller an exception and no set, which for a render means a component that never re-renders again. |
| **`Value` throws rather than returning a default.** | A caller that wants the result has to deal with the failure at the point where it uses it, and cannot mistake "the body failed" for "the body returned null". A caller that only wants the dependencies never reads `Value` and is unaffected. |
| **Nothing is reported to anyone.** | The caller is right here: this is a synchronous call with somebody waiting, so the exception goes to them through `Value`. There is no report channel to send it to as well (section 8.1). |
| **Subscribing belongs to the read, not to a later pass over the set.** | Recording a source and hooking it after the body returns leaves a window in which the body has a value and is not listening to what produced it, and makes a cold expression cost two runs: one unmonitored run whose result is thrown away, then the real one when the outer run hooks it. Subscribing during the read closes the window and collapses the two runs into one - what the read returns is what the expression keeps. |
| **`Collected` is what holds the listening.** | So the caller has one thing to dispose rather than a token list it has to build, and so the failure path needs no special case: a body that throws is still subscribed to what it reached, because the subscription happened before the throw. |

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
| `Observable.GetNamed(object key)` | `IObservableEventSource` | A handle naming `key`, freshly made and equal to every other handle for the same key. It holds nothing, so it cannot go stale and may be kept (3.2). Compose a multi-part name into one key object; the layer does not look inside it. Nothing is created in the table until something subscribes. |
| `Observable.RaiseNamedEvent(object key)` | `void` | Raises the named dependency for `key` without materialising a handle, and **without creating an entry**: a raise for a key nobody watches is one lookup and nothing else. |
| `Observable.PublishRead(IObservableEventSource)` | `void` | The primitive behind `DependUpon`. Public because a hand-written source needs it. |
| `Observable.MaxEventStackSize` | `int` | How deep evaluation may nest - one raise or one cold read per level: fifty by default, and the step past it is refused (3.8). Read on every step, so it is a field rather than a lookup; setting it below one throws. |
| `Observable.CollectDependencies(Action, Action<IObservableEventSource>)` | `Collected` | Runs the body once, subscribing `onChanged` to every source it reads **as it reads it**. Carries `IsFaulted`/`Fault`, and `Throw()` for the form that has no `Value`. Dispose the result to stop listening. |
| `Observable.CollectDependencies<T>(Func<T>, Action<IObservableEventSource>)` | `Collected<T>` | The same plus `Value` - the body's result, or the exception thrown through it. This is the component seam (section 9). |
| `Collected` | `Dependencies`, `IsFaulted`, `Fault`, `Throw()`, `Dispose()` | The first collector's result: distinct sources in first-read order by the source's own equality, each already subscribed, the exception the body threw, and a way to rethrow it that a `Value`-less caller can use. Disposing it releases the subscriptions. |
| `Collected<T> : Collected` | `+ Value` | The second collector's result. Reading `Value` throws `Fault` when there is one, rethrown with its original stack, so a caller cannot mistake a failure for a `default`. |

No `services.AddObservables()`: there is nothing to register, no process-wide state that needs a
container, and the named table is created on first use.

## 7. What collections will need (designed in `OBSERVABLE-COLLECTIONS.md`)

Recorded here so the primitive is built to fit them rather than retrofitted. Three of these - a
list, a set and a dictionary, each with change records and batches - are specified and planned in
[`OBSERVABLE-COLLECTIONS.md`](OBSERVABLE-COLLECTIONS.md); the rest of the family follows them.

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
| A chain of raises or cold reads nests deeper than `Observable.MaxEventStackSize` | `InvalidOperationException` naming the source, the step and the limit, thrown from the step instead of recursing further; the rest of the chain is abandoned, and a collector that was running turns it into that run's cached failure like any other exception |
| A write from inside a running expression | allowed, and delivered like any other change; a write to something the run had already read raises back into the run, and that loop is what the depth limit stops |
| Using a disposed source: reading `Value` or writing it, `RaiseEvent`, or registering a handler | `ObjectDisposedException` |
| Raising a named key nobody watches | nothing at all: one lookup, no entry created, nobody called. A handle is not a resource, so there is no handle to go stale either - `IsDisposed` on one is always false (3.2) |

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

## 9. The component seam, in the Virtual DOM feature

`VIRTUAL-DOM.md` section 7.3 split the mount's protocol out so a base class could be replaced. This is
the replacement: `ObservableComponent<TDataModel>`, in `common/ObservableComponent.cs` of
`Kolvir.Feature.VirtualDom`, holding a data model, rendering from it inside a collection scope, and
listening to exactly what that render read.

```csharp
public abstract class ObservableComponent<TDataModel> : IComponent, IDisposable
    where TDataModel : class
{
    private ComponentMount? _mount;
    private TDataModel? _dataModel;
    private List<IDisposable>? _renderTokens;
    private List<IDisposable>? _attachTokens;
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
        // expression's follows its function. The run subscribes itself as the render reads, so there
        // is no pass over the set afterwards - and a render that throws is still subscribed to what it
        // reached, which is what stops a component that failed once from being stranded.
        CleanupRenderTokens();
        var deps = _renderTokens = new List<IDisposable>();

        var run = _dataModel is null
            ? Observable.CollectDependencies(
                () => RenderWithNullModel(new RenderArgs(deps.Add)),
                _ => _mount?.Invalidate())
            : Observable.CollectDependencies(
                () => RenderWithDataModel(new RenderArgs<TDataModel>(_dataModel, deps.Add)),
                _ => _mount?.Invalidate());

        // The run is the listener; the list holds it beside whatever the render registered for itself.
        deps.Add(run);

        return run.Value;   // throws into the frame boundary if the render failed
    }

    protected abstract VNode RenderWithDataModel(RenderArgs<TDataModel> args);

    // Nothing at all: an empty fragment makes no control, and the mount holds the place the controls
    // will go, so the model arriving later is one ordinary render (VIRTUAL-DOM.md section 6.2).
    protected virtual VNode RenderWithNullModel(RenderArgs args) => new VFragment();

    protected virtual void OnAttached(AttachedArgs args) { }
    protected virtual void OnDetached() { }

    void IComponent.Attach(ComponentMount mount)
    {
        ThrowIfDisposed();
        if (_mount is not null) { throw new InvalidOperationException("Already mounted."); }

        _mount = mount;
        CleanupAttachTokens();
        var attachDeps = _attachTokens = new List<IDisposable>();
        OnAttached(new AttachedArgs(attachDeps.Add));
    }

    void IComponent.Detach(ComponentMount mount)
    {
        // A stale mount, or the undo of a first render that threw, is not this component's mount.
        if (!ReferenceEquals(_mount, mount)) { return; }

        _mount = null;
        OnDetached();
        CleanupRenderTokens();
        CleanupAttachTokens();
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
        CleanupRenderTokens();
        CleanupAttachTokens();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);

    private void CleanupRenderTokens()
    {
        var tokens = _renderTokens;
        _renderTokens = null;
        DisposeAll(tokens);
    }

    private void CleanupAttachTokens()
    {
        var tokens = _attachTokens;
        _attachTokens = null;
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
| `_renderTokens` | one render | the run itself, which is the subscription to everything that render read, plus anything the render registered through `RenderArgs.AddDisposable` - a subscription set up behind a condition, a one-shot listener |
| `_attachTokens` | one mount | whatever `OnAttached` registered through `AttachedArgs.AddDisposable` - a timer, a keyboard hook, a subscription that should outlive every render |

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
  `IComponent.Render` already states: a render that changed the state has described a tree that is
  already out of date. Changing a *model* from inside a render is not refused by the observables layer
  - the layer stops a loop by depth, not by forbidding writes (section 3.8) - so the rule belongs here,
  where the render is.
- **A render that fails *later* keeps its dependencies, and a first render that fails keeps
  none.** Subscribing during the read is what leaves a failing render listening, so the next
  change to one of the values it did read asks for another render instead of stranding it. On a
  *first* render the mount undoes the attach instead, so `Detach` releases that run again
  immediately - correct, because a component that never produced a tree has nothing to patch.

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
| Named dependencies that carry a value | A value that lives in an evictable entry resets when the last listener leaves - and the entry is dropped the instant a re-evaluation unhooks, which is before it re-hooks (section 3.2). The event-only form has no such state to lose. |
| Disposal that clears handlers silently, with no final raise | Simpler, and the alternative chosen is a judgement call: a final raise makes a dependency's death visible to whoever was watching instead of quietly freezing a derived value. Worth confirming (section 11). |
| Lazy, pull-on-read evaluation for all expressions | Glitch-free and no wasted runs, but every read becomes a recursive walk and every value needs a dirty flag. Section 3.6's cold/hot split gets the useful half of it (no work when nobody watches) without that cost. |
| Detecting a cycle rather than bounding the depth: a per-expression "am I running" flag, or an evaluation stack that names the chain | A flag catches only the expression that closes the loop, costs a store on every run, and misses a loop that passes through three objects and never comes back to the same one. Naming the chain means an evaluation stack - an entry pushed per run and searched per raise - which is per-operation cost on the hot path for a diagnostic. A depth counted per step is a field read and a compare, catches every loop that takes another step - a raise or a cold run - and names the source whose step was refused (section 3.8). |
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
- **Object keys box value types.** A dictionary keyed by `int` pays a box per `GetNamed` or
  `RaiseNamedEvent` call, and the handle it returns is one small allocation. A caller that reads a keyed
  dependency in a hot path should hold the handle the run reads through, which is safe precisely because a
  handle has no lifetime (3.2).
- **The depth limit bounds a loop, it does not explain one.** The message names the source of the
  step that was refused, which is the end of the chain and not its cause, so it points at a place in
  the graph to start looking. Fifty is also a guess at a legitimate maximum: a page with a deep enough
  graph of derived values - read cold, every level of it runs - would have to raise the property, and
  nothing warns before it does. Both are the price of not tracking an evaluation stack, which is what
  naming a chain would need (section 3.8).
- **The named table is process-wide, and only as large as what is watched.** Names are opaque `object`
  keys, so the growth is bounded by the keys that something is registered on: an entry is dropped the
  moment its last handler goes. A long-lived scope with churning keys therefore costs nothing beyond the
  handles it holds, which are names and die with their holders.
- **`IsDisposed` and the final raise are additions this design makes to the spec** (section 3.10,
  section 10) and are the likeliest thing to be reconsidered.
- **The empty render is the default, and it looks like a component nobody gave a model to.** The
  component seam renders nothing before a model is set (section 9), which is right for a page that is
  mounted before its document has loaded and indistinguishable from a mistake. A derived class that
  wants to be told so overrides `RenderWithNullModel` with a placeholder. The node it returns by
  default - an empty `VFragment` - arrived with the fragment work in `VIRTUAL-DOM.md` section 6.2,
  after this section was written.
- **To confirm before the code exists:** `IsDisposed`; the final raise on disposal;
  `CollectDependencies` as public API and `Collected` as the thing that holds the listening; and
  `ObservableComponent` living in the Virtual DOM feature rather than in a third package over both
  (the reference runs one way, so the placement is a choice, not a necessity).

## 12. Steps

| # | Step | Done when |
| --- | --- | --- |
| 1 | Package skeleton, the interfaces, the mutable value and snapshot dispatch | A value raises only when it actually changes; a handler can subscribe and unsubscribe during dispatch, including itself; a throwing handler propagates out of the raise and the handlers behind it do not run |
| 2 | The collector stack, `CollectDependencies` and `CollectDependencies<T>` | Nested scopes stay separate; duplicates collapse; a read outside a scope costs nothing; an exception restores the scope and still arrives with the set the body read |
| 3 | Expressions: cold/hot, caching, re-collection, equality, the exception comparer | `CreateExpression` runs nothing; a cold expression re-runs on every read and hooks nothing; a hot one hooks its dependencies, re-runs on a change, re-collects, and raises only when the result differs |
| 4 | Named dependencies: one opaque key, a handle that names it, raising without a handle, eviction | Handles for one key are the same dependency; `RaiseNamedEvent` reaches a watched key and does nothing for an unwatched one; an entry disappears the moment its last handler goes, and a handle still works afterwards |
| 5 | Guards: a loop, disposed reads | A loop is refused with an `InvalidOperationException` that names the source, the step and the limit, instead of recursing until the stack dies, and the next change is unaffected - whether the loop is a handler writing what it hears about, an expression reading itself cold, or a mix of the two. Disposed reads shipped with step 1; the depth limit shipped with step 3 |
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
5. **`Kolvir.Feature.VirtualDom`** gains a package reference to this one, for section 9's base class and
   nothing else, and its test suite gains a project reference so its components can read a real model.
   The reference runs one way: nothing here knows a component exists.

XML documentation is mandatory and CS1591 is an error for anything that ships
(`Directory.Build.props`), so every public member in this design needs a `<summary>` - the interface
table in section 2 is that list.

## 13. Verification approach

Headless tests in `tests/features/Kolvir.Feature.Observables.Tests`, in the same shape as the other
feature suites: no window, no frame, no timing. The claims that get special treatment:

| Claim | How it is proven |
| --- | --- |
| **A raise dispatches over a snapshot** | A handler that removes itself, and one that adds another, both leave the current dispatch's remaining handlers running and the next dispatch reflecting the change. |
| **A loop is refused, once, whichever step it takes** | A value whose own handler writes it loops; the write throws `InvalidOperationException` naming the limit, the value is one write past it, an ordinary raise afterwards still works (so the depth was restored rather than left poisoned), and the same chain at a lower `MaxEventStackSize` stops sooner - the property is read on every step rather than captured. An expression whose function writes what it read ends the same way, with the refusal in its cache and thrown by a read. An expression that reads itself with nothing subscribed is refused too, as is a pair of them reading each other, and so is a legitimate three-deep chain of cold reads when the limit is set below it - so the depth covers a run and not only a notification. A raise with no handlers is not counted at all. |
| **A key is one dependency, however many handles name it** | Two `GetNamed` calls for one key return equal but distinct handles, and a body that depends on one key through two of them reports one dependency - which is the assertion that proves the set compares by equality rather than by reference. An expression reading the same key twice re-runs once per raise, since a second registration would call it twice. Every handle for a key hears a raise made through any other, and one that was not registered on still works after the key was let go - so the handle's lack of a lifetime is asserted rather than assumed. `RaiseNamedEvent` reaches the handler of a watched key and does nothing at all for an unwatched one. |
| **Cold work costs nothing** | `CreateExpression` runs the function zero times; an expression with no handlers has zero subscriptions (asserted on a counting source) and re-runs on every read - a cold read being a read with no run in progress, so there is nothing to collect; attaching the first handler runs it once more, subscribes that run, and stops the re-running. |
| **Re-collection follows the function** | A conditional read adds and removes a dependency as the condition flips, and a value that stops being read stops raising. |
| **A read subscribes as it reads** | A counting source read inside a `CollectDependencies` body reports one handler **from inside the body**, before the read returns; a raise afterwards reaches the caller's handler, and disposing the run leaves zero handlers. Reading a cold expression from inside somebody else's run runs it once, not twice, and the next read of it is a field. |
| **A failed run still yields its dependencies** | `CollectDependencies(() => { read; throw new ...; })` returns the sources it read before the throw, `IsFaulted` true, and still subscribed to them; `Throw()` rethrows that exact instance, and the `Func<T>` form adds a `Value` that throws the same instance with its original stack. That is what lets a render fail and still be listening to what could revive it. |
| **The old set is gone before the run** | A hand-written `IObservableEventSource` that counts its own registrations (public API, no internal hook) is read by an expression; while the function executes after a dependency raise, its count is zero for everything the previous run had hooked, and one again once the run finishes. That is 3.6.1 made observable: the run cannot be re-entered by a set it has already let go of. |
| **Failures are visible, not silent** | A function that throws on a dependency-started re-run leaves the expression raising (a new exception is a changed result) and throwing on every read, with nothing thrown at anybody during the run itself; a chain of raises that loops is refused at `MaxEventStackSize` with the source and the limit named, and the raise is what changes, not the values; and every use after `Dispose` throws `ObjectDisposedException` - read, write, raise, or register. Nothing is reported anywhere, which is asserted by the absence of an application dependency in the package (section 8.1). |
| **Disposal is ordered, and the order is observable** | A handler registered on a value source sees `IsDisposed` true and gets `ObjectDisposedException` if it reads `Value` during the final notification, while a handler that only counts the notification sees it exactly once - so "dead first, then one raise, then the list is cleared" is asserted rather than assumed. An expression whose dependency is disposed raises in turn, with the `ObjectDisposedException` as its cached result. Afterwards, `Value`, `RaiseEvent` and registration all throw while removing a handler and disposing again stay safe. |
| **The seam re-renders on the frame after a change** | In the Virtual DOM suite: a component reading two model values renders once per frame however many of them changed, stops listening to a value its render stopped reading, and unhooks everything when detached. A token registered through `RenderArgs.AddDisposable` dies on the next render while one registered through `AttachedArgs.AddDisposable` survives it and dies on detach, and `OnDetached` runs exactly once across dispose-then-detach. |
| **AOT- and trim-clean** | Measured on Windows 11 with the demo's `FolderProfile` (NativeAOT, full trimming) and `--render` on the observables page: the published executable's PNG is byte-identical to the JIT build's, SHA256 `A763FF18...642BC8`, so nothing about the page depends on a JIT-only path. `Kolvir.Minimal` publishes to `Kolvir.Minimal.exe` (8,911,360 bytes) with no `Kolvir.Feature.Observables` file beside it and no occurrence of the assembly's name inside the executable. The publish emits one trim warning, `IL2104` on `SharpGen.Runtime`, which arrives with Vortice and predates this feature. The feature has no reflection, no `dynamic` and no serializers, so all of this is expected rather than arranged. |

## 14. Progress

| Step | State | Notes |
| --- | --- | --- |
| 1 | Done | The package, the four interfaces, `CreateValue`/`ObservableValue<T>` and snapshot dispatch, plus the read scope the collectors will use. Sixteen tests, no reference to Kolvir, and the package packs three `lib/` folders with an empty dependency group each |
| 2 | Done | `Collected`/`Collected<T>` and the two `CollectDependencies` overloads over the scope stack, subscribing as the body reads and disposed to stop listening. Fifteen tests, including nested isolation, de-duplication of a repeated read, the scope being restored when a body throws, and a write from inside the body being delivered rather than refused |
| 3 | Done | `CreateExpression` in both forms, `ObservableExpression<T>` with the cold/hot states, release-run-resubscribe, cached exceptions, the value comparer and the exception comparer. Twenty-five tests, including conditional re-collection, the comparer suppressing a repeat without suppressing the update, an outer expression turning a cold inner one hot by reading it, and a run that writes what it read ending at the depth limit |
| 4 | Done | `GetNamed`/`RaiseNamedEvent` over a table that holds a key's handlers only while something is subscribed, with handles that are names - equal by key and free of any lifetime. Eleven tests, including one dependency for a key read through two handles, every handle hearing a raise made through another, a handle that outlives the key, a self-removing handler that does not disturb the rest of the raise, and a raised key counting against the evaluation depth |
| 5 | Done | `Observable.MaxEventStackSize`, fifty by default, counted per step in one depth: an event raise (through the dispatch every value and expression notifies by) and a cold read (through the expression itself). The step past it is refused, the rest of the chain stays refused until the outermost step unwinds, and nothing outlives the call that started it. Nine tests in their own suite. Naming a loop's chain is not done and is a non-goal (3.8) |
| 6 | Done | `ObservableComponent<TDataModel>` in `Kolvir.Feature.VirtualDom`, over a package reference to this one, rendering inside a collection scope with the run as the listener. Thirteen tests in the Virtual DOM suite: a value the render read asks for one render per frame, a value it stopped reading stops asking, detaching releases everything, render-scoped and attach-scoped tokens die at their own times, and a render that fails later keeps listening while a first render that fails leaves the component unmounted and mountable again |
| 7 | Done | An **Observables** page in the sample beside the Virtual DOM one - a card that renders an observable model, four plain buttons above it that change that model, and a render count that moves once per frame - plus an `### Observables` section in the developer guide. Published with NativeAOT: the page's render is byte-identical to the JIT build's, and `Kolvir.Minimal` carries nothing from this package |