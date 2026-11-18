# Observables - design and implementation plan

A small, non-threaded change-notification layer for data models, in its own package
(`Kolvir.Feature.Observables`, rung 3 of `FEATURE-COST-POLICY.md`). A model holds `IObservableValue<T>`
for its state; anything that wants to know when a value changes registers a handler; and an
*expression* observable recomputes itself from the values it read the last time it ran, which is the
piece that makes "re-render when anything the render depended on changes" answerable without
`INotifyPropertyChanged`, strings, or reflection.

The immediate purpose is the other half of the component layer: `VIRTUAL-DOM.md` §7.3 left
`IComponent` as a seam, and this is what plugs into it. A component whose `Render()` runs inside a
dependency-collection scope knows what its render read, so a data model that changes can ask that
component to re-render - and the render still goes through the frame queue, so a burst of model
changes is still one render (see §9).

Status of this document: **living plan, no code yet.** Section 3 records the decisions and the
reasons; section 12 is the order of work; section 11 lists what is still open, including five things
decided here for the first time that should be confirmed before the code exists.

## 1. Goals and non-goals

Goals:

- **Values that can be watched.** `Observable.CreateValue<T>(initial)` returns an
  `IObservableMutableValue<T>`, so the creator can write and everyone else can only read.
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
- Glitch-free, topologically-ordered delivery (§3.5): a listener may be called more than once for one
  logical change and may observe an intermediate value.
- Collections. Observable dictionaries/lists are the next step and are deliberately out of scope;
  §7 provides the primitive they need (a dependency per key, plus one for the shape of the collection).
- Binding a control to an observable without a component in between.

## 2. The type model

```
IObservableEventSource                      "something happened" - the dependency primitive
  bool IsLive { get; }
  IDisposable AddObservableEventRaisedHandler(Action<IObservableEventSource> handler)
  void RemoveObservableEventRaisedHandler(Action<IObservableEventSource> handler)
  void DependUpon()                         default: Observable.PublishRead(this)
  void RaiseEvent()

IObservableValue : IObservableEventSource, IDisposable
  object? Value { get; }

IObservableValue<T> : IObservableValue
  new T Value { get; }

IObservableMutableValue<T> : IObservableValue<T>       ← what CreateValue returns
  new T Value { get; set; }
```

| Piece | Shape | Purpose |
| --- | --- | --- |
| `IObservableEventSource` | handler pair, `DependUpon`, `RaiseEvent`, `IsLive` | The dependency primitive. A named dependency and a collection's per-key dependency are this and nothing more: they carry no value, only "this particular thing changed". |
| `IObservableValue` | `object? Value` | The untyped view, for a homogeneous dependency set and for `PublishRead`. Reading it publishes. |
| `IObservableValue<T>` | `new T Value { get; }` | What a dependency set holds. Read-only on purpose: a subscriber can watch a value but cannot write it. |
| `IObservableMutableValue<T>` | `new T Value { get; set; }` | What `CreateValue` hands back, to the one piece of code that owns the state. |
| `Observable` | static | `CreateValue<T>`, `CreateExpression<T>`, `GetNamed`, `PublishRead`, `CollectDependencies`. |

Three consequences worth stating plainly:

- **Read-only is a compile-time promise, not a guard.** A dependency set holds
  `IObservableValue<T>`, and a determined caller can cast to `IObservableMutableValue<T>`. The same
  trust level as `Control`'s internals, and it costs nothing to keep out of an API.
- **The mutable type is the only thing that can be written.** An expression's result is an
  `IObservableValue<T>`, so the only way to change an expression's value is to change something it
  read - which is the property that makes the dependency graph trustworthy.
- **`IObservableEventSource` is not `IDisposable`.** A named dependency is shared: disposing it would
  take the dependency away from every other holder. Only the *value* types are disposable (§3.10), and
  the handle returned by `AddObservableEventRaisedHandler` is how a single registrant unregisters.

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
| **An expression compares its result the same way, and updates its dependency set either way.** | The dependencies that produced the value are still the dependencies that produce it, and they can change without the *result* changing (§3.6). Only the raise is conditional. |

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
| **An entry exists only while it has a handler, and is evicted when the last one is removed.** | That is the memory story: a dependency nobody is listening to is not memory. Eviction is deferred while a raise is in progress (§3.4), because a handler is allowed to remove itself during its own dispatch. |
| **A caller that reads a key repeatedly holds the handle.** | `GetNamed` is a dictionary lookup on an `object` key, which boxes a value-type key; the model that owns the entries caches the handles it hands out. This is what the collection types in the next step will do internally. |

### 3.3 Collecting dependencies: a stack, not a slot

`PublishRead(source)` appends the source to the *innermost* active collection scope. A scope is
entered by `CreateExpression` while it runs its function, and by `CollectDependencies(body)` (§4).
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
| **A raise iterates a snapshot of the handler list.** | A handler is allowed to subscribe and unsubscribe freely, including itself, and none of that may disturb the dispatch that is running. Mutations apply to the next raise. |
| **Each handler gets its own boundary.** | An exception escaping one handler would abandon the rest of the snapshot; it goes to `Application.HandleNativeBoundary` instead and the loop continues. If the application chooses to let it rethrow (no `Application.Current`, or `Handled` left false), it propagates from `RaiseEvent` and the handlers behind it did not run - the same policy, and the same caveat, as `RenderQueue`'s batch in `VIRTUAL-DOM.md` §7.2. |
| **`RaiseEvent` is on the interface and is public.** | A source that owns a change raises it: a dictionary raising a key's dependency, a hand-written source raising on a timer tick. It is not something a consumer calls - a consumer changes the model and the model raises. Documented rather than restricted, because restricting it would need a second type for "the raiser". |
| **`RaiseEvent` on a dead source is a no-op.** | A dictionary that outlives its listeners should not have to check. |

### 3.5 Delivery order is not a topological order

Changes propagate synchronously and immediately: a raw observable raises its event as soon as it
changes, and the user's decision is that this stays true - deferring is the component layer's job,
not the observable's. That means a diamond - `C = f(A)`, `D = g(A, C)` - can call `D` twice for one
change to `A`, and can call `D` while `C` still holds its old value. This is a deliberate trade:

- **It is unobservable for the intended use.** The only consumer with a real ordering problem is a
  UI render, and renders go through the frame queue: a burst of model changes marks components and
  the frame renders each one once, reading a graph that has finished propagating. `VIRTUAL-DOM.md`
  §7.1 already made this argument for `SetState`.
- **The alternatives cost more than they are worth now.** Depth-ordered dispatch (a depth per source,
  a priority queue) is close to topological order and would remove most re-entrancy; lazy
  pull-on-read is glitch-free by construction but makes every read a recursive walk. Both are
  additive later, because nothing outside this package depends on the order.
- **What a listener may assume:** it is called when something it depends on changed, the graph has
  finished propagating before the outermost raise returns, and it must not assume it is called once
  per logical change or that the value it reads is the final one.

### 3.6 Expressions: cold, hot, and what a read means

An expression is one function plus a cached result plus the set of sources that result was computed
from. It has two states:

| State | Dependencies hooked | `Value` read |
| --- | --- | --- |
| **Cold** - no handlers on it | no | re-runs the function every time, in a collection scope whose result is discarded |
| **Hot** - at least one handler | yes, all of them | returns the cached result |

| Decision | Why |
| --- | --- |
| **Cold expressions hook nothing.** | An expression nobody watches is a function, and paying to keep its dependencies wired up would make an unused derived value cost the same as a used one - and would keep its whole dependency tree alive. A cold read discards the dependencies it collected rather than keeping a stale set: the next transition to hot re-runs and re-collects anyway, so a kept set could only ever be wrong. |
| **Cold reads always re-run**, even though a cached result exists. | The cache cannot be trusted when nothing is telling the expression that its inputs moved. This is the user's rule, and it is the only self-consistent one: a cache is valid exactly when the subscription that maintains it is live. |
| **Hot reads never re-run.** | A hot expression's cache is maintained by its dependency hooks, so a read is a field. A render that reads the same expression twice must not cost two runs. |
| **Becoming hot evaluates once, then hooks,** and raises nothing. | The subscriber count just went from zero to one; there is no earlier subscriber that could have missed a change, and the new subscriber can read `Value` for the current one. |
| **Becoming cold unhooks everything and keeps the result.** | Cheap teardown, and the value stays readable. It is just no longer trusted, per the row above. |
| **A dependency raise re-runs the function, unhooks the old set, hooks the new one, and raises only if the result differs.** | Re-collecting on *every* run is what makes conditional reads work: `if (flag) x = a.Value;` makes `a` a dependency only in the runs where it is read. Hooking the new set even when the result is unchanged is what keeps the graph honest for a value that is recomputed but equal. |

### 3.7 A function that throws is a cached result

- **At creation** the exception propagates out of `CreateExpression`: an expression that cannot
  produce a value has no value to be watched, and the constructor is the only place where throwing is
  unambiguous.
- **On a later re-run** the exception is cached in place of the value (the user's rule), the
  expression raises, and every read throws it. So a broken expression does not silently serve a stale
  value, and the failure reaches whoever is watching rather than only whoever is reading.
- **Success to failure and failure to success are both changes.** Failure to failure is **not**: the
  expression is still broken, subscribers already know, and re-raising on every dependency change
  would turn one bug into a storm. The cached exception is replaced, so a later read reports the
  newest message.
- **The report happens once, at the point of evaluation,** through
  `Application.HandleNativeBoundary` - the same place a render's failure goes. A handler that then
  reads `Value` gets the exception thrown into it, which the dispatch loop isolates (§3.4).

### 3.8 Cycles, and expressions that mutate

| Decision | Why |
| --- | --- |
| **A thread-static evaluation stack detects cycles.** Before running, an expression checks whether it is already on the stack; if it is, it throws naming the chain (`A -> B -> A`). | A dependency cycle in a push-based graph is infinite recursion, and a `StackOverflowException` under .NET kills the process with no report. Naming the chain turns a crash into a message. The stack also gives expressions diagnostic names, so `CreateExpression` takes an optional `string? name` used only in messages. |
| **Mutating an observable while a collection scope is active throws.** | An expression is a pure function of what it read. A write inside one raises into other expressions while the collector is mid-run, so the set being collected no longer describes what happened. `Observable` carries a collection depth for this, and `set`/`RaiseEvent` refuse while it is non-zero. Cheap - one static compare on a write - and it names the expression. |
| **A mutation from inside a *handler*** is ordinary and allowed. | Handlers run with no collection active: they may write other values, and those changes cascade. That is the event-driven style the layer is for. |

### 3.9 Single-threaded, with no locks

Every observable is UI-thread state. Reads are a static field check and a `bool`; writes and raises
are list operations. No locks, no `Interlocked`, no `volatile`. Debug builds assert the thread that
created the first source, so a mistake is a message in development rather than a heisenbug in
production. A background thread that changes state marshals, exactly as it does to touch a control.

### 3.10 Dropping a reference is free; `Dispose` is the escape hatch

| Decision | Why |
| --- | --- |
| **Nothing needs disposing for memory.** | A source with no handlers roots nothing: the handler list is the only thing it holds, and the registrant holds the token. Dropping the last reference to an unwatched value collects it, with no bookkeeping anywhere. This is what makes observables usable as plain model state. |
| **`Dispose` marks a source dead and clears its handlers.** | The escape hatch for the case that *is* a problem: a long-lived source with registrants who forgot to unregister. Disposal drops their closures, so a forgotten registration cannot keep a dead screen's state alive for the life of the process. |
| **Disposal raises one last time, with `IsLive` already false.** | So a registrant can tell why its dependency went quiet - the final raise is the notification, and `IsLive` is what distinguishes it from an ordinary change. A handler may still read the last value during that raise; reads throw only after `Dispose` returns. This is `OnCompleted` semantics from Rx and it is the one call in this section that is a design choice rather than a consequence; the alternative (clear the list silently) is in §10. |
| **After disposal:** `set` and `RaiseEvent` are no-ops, `Value` reads throw `ObjectDisposedException`, and `Dispose` is idempotent. | A dead source is a bug in the reader's lifetime, and saying so is more useful than returning a stale value forever. |
| **An expression whose dependency dies fails loudly.** | The final raise wakes it, the re-run's read of the dead dependency throws `ObjectDisposedException`, that becomes the expression's cached result (§3.7), so it raises in turn and every read of it throws. A cascade of "the thing below me is gone" is the honest outcome; the alternative is a derived value that silently freezes. |
| **Registering a handler is the onus of whoever registers.** | The token is the unregister. An expression unhooks every dependency when it goes cold and on disposal, so the common accidental leak - a derived value that outlives its listeners - cannot happen. |

## 4. The collector, and the primitive under expressions

```csharp
// Internally, an expression runs its function inside a collection scope, and reads publish into it:
using (Observable.BeginCollection(scope))   // internal; CollectDependencies is the public face
{
    result = func();
}
```

`CollectDependencies(Action body)` is the public version of the same thing, returning the distinct
sources the body read:

```csharp
var deps = Observable.CollectDependencies(() => _panel.Render());
foreach (var dep in deps)
{
    tokens.Add(dep.AddObservableEventRaisedHandler(_ => Invalidate()));
}
```

It exists because the component layer (§9) needs exactly this and *not* an expression: a render
produces a tree it does not want to compare, so it wants "the dependencies of this run" plus "call me
when one of them changes". Expressing that through `CreateExpression` would mean comparing `VNode`
trees to decide nothing.

## 5. Reading and writing, in one place

```csharp
// A model: the creator keeps the mutable handle, everything else sees the read-only one.
internal sealed class DocumentModel
{
    private readonly IObservableMutableValue<string> _text = Observable.CreateValue(string.Empty);

    public IObservableValue<string> Text => _text;

    public void Type(string text) => _text.Value = text;      // raises only if it differs
}

// A derived value: no registration, no strings, and it hooks nothing until someone listens.
var wordCount = Observable.CreateExpression(
    () => document.Text.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
    name: "word count");
```

## 6. API surface, in full

| Member | Returns | Notes |
| --- | --- | --- |
| `Observable.CreateValue<T>(T initialValue)` | `IObservableMutableValue<T>` | Always a standalone value, unrelated to any other observable. |
| `Observable.CreateExpression<T>(Func<T>, string? name = null)` | `IObservableValue<T>` | Runs once at creation; throws if that throws. Optional comparer overload. |
| `Observable.GetNamed(object key)` | `IObservableEventSource` | The same instance for the same key, for as long as the entry lives. Compose a multi-part name into one key object; the layer does not look inside it. |
| `Observable.PublishRead(IObservableEventSource)` | `void` | The primitive behind `DependUpon`. Public because a hand-written source needs it. |
| `Observable.CollectDependencies(Action)` | `IReadOnlyList<IObservableEventSource>` | The component seam (§9). |

No `services.AddObservables()`: there is nothing to register, no process-wide state that needs a
container, and the named table is created on first use.

## 7. What collections will need (next step, not this one)

Recorded here so the primitive is built to fit them rather than retrofitted:

- **Access by key or index**, iteration and `Count` are **different dependencies**. A read by key
  depends on that key's named source; `Count`, `Keys` and enumeration depend on a *shape* source for
  the collection, which is raised by any addition, removal or clear.
- **A change raises both** the touched key's source and the shape source (both, when a write replaces
  an existing value). Raised after the mutation completes, never inside a loop over storage, because
  a handler is allowed to re-enter the collection.
- **Per-collection scoping** is the collection's own business, since `GetNamed` takes one opaque key: a
  dictionary keys its dependencies however it likes - `(this, key)` for a tuple, or its own key type
  that compares on both - so two dictionaries that both hold a `"name"` entry cannot collide without
  the layer knowing anything about ownership.
- **Enumerating does not depend on every key** - that would hook N sources to render a list. It
  depends on the shape source, and rows that care about their own values depend on their own keys.

## 8. Failure policy summary

| Situation | Behaviour |
| --- | --- |
| Expression's function throws at creation | propagates out of `CreateExpression` |
| Expression's function throws on a re-run | cached as the result, reported to `Application.HandleNativeBoundary`, raised, and every read throws it |
| A handler throws during a raise | isolated per handler, reported to `Application.HandleNativeBoundary`, the rest of the snapshot still runs (unless the application lets it rethrow) |
| A dependency cycle is read | `InvalidOperationException` naming the chain, before any recursion |
| A write inside a running expression | `InvalidOperationException` naming the expression |
| Reading a disposed value or expression | `ObjectDisposedException` |
| Registering on an evicted named dependency | the handle is dead: `IsLive` is false, registration is a no-op, and a later `GetNamed` returns a fresh handle. It cannot happen while an entry is still watched - a handler is what keeps the entry alive |

## 9. The component seam (a later step, in the Virtual DOM feature)

`VIRTUAL-DOM.md` §7.3 split the mount's protocol out so a base class could be replaced. This is the
replacement's core:

```csharp
internal sealed class ObservableComponent : IComponent
{
    private ComponentMount? _mount;
    private List<IDisposable> _tokens = [];

    public VNode Render()
    {
        // Re-rendering re-collects: the dependency set follows the render, exactly as an
        // expression's follows its function.
        foreach (var token in _tokens) { token.Dispose(); }
        _tokens.Clear();

        VNode tree = null!;
        foreach (var dep in Observable.CollectDependencies(() => tree = Build()))
        {
            _tokens.Add(dep.AddObservableEventRaisedHandler(_ => _mount?.Invalidate()));
        }

        return tree;
    }

    public void Attach(ComponentMount mount) => _mount = mount;
    public void Detach(ComponentMount mount) { /* unhook every token */ }
}
```

Two things this gets for free, and one it must respect:

- **A data change is a re-render on the next frame, not inside the raise.** The handler calls
  `ComponentMount.Invalidate`, which marks the component and asks for a frame; the render happens at
  the top of the next frame (`VIRTUAL-DOM.md` §7.1). So the glitches in §3.5 cannot reach the screen,
  and a handler that changes five model values still costs one render.
- **The dependency set is re-collected per render**, so a render that stops reading a value stops
  listening to it.
- **`Detach` must unhook**, or a component that lost its mount keeps its handlers and every model
  change calls a dead mount's `Invalidate` (a no-op, but the closures stay alive).

## 10. Options considered and rejected

| Option | Why not |
| --- | --- |
| `INotifyPropertyChanged` and string property names | The failure this layer exists to avoid: a typo is a compile error here and silence there, and a handler cannot know what a caller actually read. |
| `System.IObservable<T>` / `IObserver<T>` and `Subscribe` | The name collides outright with the BCL's generic interface in any file that has `using System;` (measured: CS0104), and Rx's contract - a sequence with completion and errors on a subscription - describes a stream, not a mutable value and its readers. `IsLive` plus a final raise borrows the useful part of the completion story without the subscription type. |
| Weak references between a source and its handlers | Every read of the graph would test and resurrect, and the semantics of "the handler went away" would have to be invented. Explicit tokens and disposal (§3.10) put the cost where the mistake is. |
| Named dependencies that carry a value | A value that lives in an evictable entry resets when the last listener leaves - and eviction happens in the middle of a re-evaluation that unhooks before re-hooking (§3.2). The event-only form has no such state to lose. |
| Disposal that clears handlers silently, with no final raise | Simpler, and the alternative chosen is a judgement call: a final raise makes a dependency's death visible to whoever was watching instead of quietly freezing a derived value. Worth confirming (§11). |
| Lazy, pull-on-read evaluation for all expressions | Glitch-free and no wasted runs, but every read becomes a recursive walk and every value needs a dirty flag. §3.6's cold/hot split gets the useful half of it (no work when nobody watches) without that cost. |
| A lock, or `Interlocked` guards, to allow cross-thread use | The graph's invariants are per-dispatch-list, and a correct thread-safe version is a different design (queued delivery, or per-thread graphs). The library is single-threaded everywhere else. |
| Ordering dispatch by dependency depth | Would remove most of §3.5's re-entrancy. Additive later if a use appears; nothing outside the package depends on delivery order. |

## 11. Risks and open questions

- **Delivery is not glitch-free (§3.5).** A listener may run twice for one logical change and may read
  an intermediate value. Acceptable for renders because of the frame queue; a listener that *acts*
  on the intermediate value needs to know. Documented, not mitigated.
- **A wide graph re-runs often.** One change re-runs every hot expression that depends on it, and
  each of those may raise to its own subscribers. To be measured on a realistic page; the escape
  hatches are the comparer (stop the cascade) and coalescing at the component.
- **Equality is the trap for object results.** An expression returning a freshly built object that
  does not override `Equals` raises on every run. For `VNode` trees that is why the component seam
  does not use an expression at all (§4).
- **Object keys box value types on a miss.** A dictionary keyed by `int` pays a box per `GetNamed`
  call that misses its cache. Collections will cache handles per key; a caller that reads in a hot
  path should hold the handle.
- **The named table is process-wide, and only as large as what is watched.** Names are opaque `object` keys,
  so the growth is bounded by the keys that someone is actually listening to - but a long-lived
  scope with churning keys relies on eviction, which only happens when the last handler goes.
- **`IsLive` and the final raise are additions this design makes to the spec** (§3.10, §10) and are
  the likeliest thing to be reconsidered.
- **To confirm before the code exists:** `IsLive`; the final
  raise on disposal; `CollectDependencies` as public API; refusing mutation inside a collection
  scope; and the optional `name` on `CreateExpression`.

## 12. Steps

| # | Step | Done when |
| --- | --- | --- |
| 1 | Package skeleton, the interfaces, the mutable value and snapshot dispatch | A value raises only when it actually changes; a handler can subscribe and unsubscribe during dispatch; a throwing handler does not stop the others |
| 2 | The collector stack and `CollectDependencies` | Nested scopes stay separate; duplicates collapse; a read outside a scope costs nothing; an exception restores the scope |
| 3 | Expressions: cold/hot, caching, re-collection, equality | A cold expression re-runs on every read and hooks nothing; a hot one hooks its dependencies, re-runs on a change, re-collects, and raises only when the result differs |
| 4 | Named dependencies: one opaque key, one handle, eviction | Two handles for one key are the same object; an entry disappears when its last handler goes, and not during a raise |
| 5 | Guards: cycles, mutation inside a scope, disposed reads | Each throws a named message instead of recursing, silently corrupting, or serving a stale value |
| 6 | The component seam in `Kolvir.Feature.VirtualDom` | A component whose render reads model values re-renders on the next frame after one changes, and stops listening to a value its render stopped reading |
| 7 | A demo page, the developer-guide section, and the AOT measurement | The published sample renders the page byte-for-byte identically to the JIT build, and a minimal app publishes with no observables artifact |

Step 6 is the reason for the other six, and it is deliberately last: the primitives are testable
without a window, and the seam is testable without a second renderer.

### 12.1 What else has to change

Five things outside the new code, in the shape `VIRTUAL-DOM.md` §8.1 recorded for its feature:

1. **`src/features/Kolvir.Feature.Observables/`** in the rung-3 four-folder layout, all of the code in
   `common/`, namespace `Kolvir.Observables`, `IsAotCompatible`. Referencing `Kolvir` core for
   `Application.HandleNativeBoundary` and nothing else.
2. **`tests/features/Kolvir.Feature.Observables.Tests/`**, which needs no reference to core beyond what
   the scene tests already use.
3. **`Kolvir.slnx`** gets both, under `/src/features/` and `/tests/features/`. The solution is
   hand-listed, not globbed.
4. **`.github/workflows/tests.yml`** gets a restore step and a test step for the new suite, and the
   workflow's path filter needs `src/features/Kolvir.Feature.Observables/**`.
5. **`packages.yml`** and the pack globs, which are what publish a feature package at all.

XML documentation is mandatory and CS1591 is an error for anything that ships
(`Directory.Build.props`), so every public member in this design needs a `<summary>` - the interface
table in §2 is that list.

## 13. Verification approach

Headless tests in `tests/features/Kolvir.Feature.Observables.Tests`, in the same shape as the other
feature suites: no window, no frame, no timing. The claims that get special treatment:

| Claim | How it is proven |
| --- | --- |
| **A raise dispatches over a snapshot** | A handler that removes itself, and one that adds another, both leave the current dispatch's remaining handlers running and the next dispatch reflecting the change. |
| **One entry, one identity** | Two `GetNamed` calls for one key return the same instance, and an expression reading the same key twice hooks it once (asserted on the handler count, not on the set). |
| **Cold work costs nothing** | An expression with no handlers has zero dependency hooks (asserted on each dependency's handler count) and re-runs on every read; the second handler registration stops it re-running. |
| **Re-collection follows the function** | A conditional read adds and removes a dependency as the condition flips, and a value that stops being read stops raising. |
| **Equality keeps the graph quiet** | Writing an equal value raises nothing; an expression whose result is unchanged raises nothing but still re-hooks a changed dependency set. |
| **Failures are visible, not silent** | A function that throws on a re-run leaves the expression raising once, reporting once, and throwing on every read; a cycle throws with the chain named; a write inside a scope throws; a read after `Dispose` throws `ObjectDisposedException`. |
| **The seam re-renders on the frame after a change** | In the Virtual DOM suite: a component reading two model values renders once per frame however many of them changed, stops listening to a value its render stopped reading, and unhooks everything when detached. |
| **AOT- and trim-clean** | The sample's published render is byte-identical to the JIT build's, and `Kolvir.Minimal` publishes with no observables artifact. The feature has no reflection, no `dynamic`, and no serializers, so this is expected rather than arranged. |

## 14. Progress

| Step | State | Notes |
| --- | --- | --- |
| 1 | Not started | |
| 2 | Not started | |
| 3 | Not started | |
| 4 | Not started | |
| 5 | Not started | |
| 6 | Not started | |
| 7 | Not started | |
