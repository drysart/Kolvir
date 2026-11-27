# Asset loading - design note

Status: **design note only.** No code, no project, nothing under `src/`. Rung: **3** for
`Kolvir.Feature.AssetLoading`, and **3** again for the follow-up package that gives an `Image` a URI.
The measurement that `FEATURE-COST-POLICY.md` asks every feature to state comes with the code, not
with this note (section 7).

This records a design that was settled in review before it was written down. Sections 2 to 4 are the
decided shape and the reasons for it; section 5 is what the manager does, in the order it does it;
section 6 is the control integration, described and not built; section 7 is the packages; section 8
is the alternatives that lost, in one place, so a reviewer can see what was considered; section 9 is
what is genuinely still open.

The register is the one `OBSERVABLE-COLLECTIONS.md` and `ELEMENT-COLLECTIONS.md` use: a decision is
recorded with its reason, and an option that was rejected is recorded with the reason it lost. Where
something is not decided, section 9 says so rather than a section here quietly deciding it.

## 1. What the feature is for

A UI loads many assets, images above all. Five requirements, and the third and fourth are what
decide the shape:

1. What is on screen loads first.
2. Everything else loads later, and still loads.
3. The set of visible controls changes as the user acts - a scroll, a tab switch - so a priority has
   to be changeable *after* the load is enqueued.
4. When the need goes away - the image control is removed, disposed, or its source replaced - a
   queued load must never start, and a running one must be cancelled.
5. The control that asked has to be able to say where its load has got to, because that is what a
   placeholder, a spinner and an error message are drawn from - and the distinction a UI most needs is
   "submitted, waiting" against "actually in flight".

1 and 2 make this a priority queue. 4 makes it a queue whose entries can be withdrawn, which is not
the same as a queue with a cancellation token at the far end: a withdrawn entry has to leave, and a
withdrawn running load has to stop consuming whatever the loader was spending on it. Removal and
disposal are two different ways of withdrawing, and neither is currently observable from a feature
package (6.2) - so the note keeps them apart instead of letting "disposed" stand in for both.

5 is the requirement that reaches back into the manager. A caller cannot tell a queued load from a
running one from the outside - both are "I have a task and it has not finished" - so the manager has
to say when a queued load starts, which is the one thing added to its signature for this (2.4).

Two things are deliberately *not* here. There is no cache and no in-flight sharing in the manager,
and there is no concurrency limit in the manager. Both are real and both belong to the loader
(sections 3.1 and 3.3), which is the same decision seen from two directions: the manager owns order,
the loader owns capacity and bytes.

## 2. The interfaces, as decided

```csharp
public interface IAssetLoader
{
    int Priority { get; set; }

    IDisposable AddPriorityChangedHandler(Action<object> handler);
    void RemovePriorityChangedHandler(Action<object> handler);

    bool CanLoadNow { get; }

    IDisposable AddCanLoadNowChangedHandler(Action<object> handler);
    void RemoveCanLoadNowChangedHandler(Action<object> handler);

    Task<object?> LoadAsync(CancellationToken cancellationToken);
}

public interface IAssetLoader<TResult> : IAssetLoader
{
    new Task<TResult> LoadAsync(CancellationToken cancellationToken);
}

public interface IAssetLoadManager
{
    Task<TResult> LoadAsync<TResult>(
        IAssetLoader<TResult> loader,
        Action? onStarted,
        CancellationToken cancellationToken);
}
```

The loader is a **handle for one request from one consumer**, not a fetch. It carries that consumer's
priority and that consumer's token, and it is cheap to make - which is what makes the sharing in
section 4.1 possible at all.

`Priority` is a property because something sets it after the fact, and the handler pair is what tells
the manager that the queue order it computed is now stale (section 3.7). Higher wins; the manager
walks descending. `CanLoadNow` is a question the manager asks, never a rule it applies (3.2).

`LoadAsync`, not the draft's `ExecuteAssetLoadAsync`. The manager schedules and prioritises; it does
not execute. "Execute" also says the token runs the work, which is the one thing the manager does not
do to a loader - it hands the loader a token, and the loader decides what to do with it (3.9).

### 2.1 The factory: why the draft is replaced

The draft factory the feature was proposed with does not compile:

```csharp
// Does not compile: CS0693, then CS0535.
public interface IAssetLoaderFactory<TResult> : IAssetLoaderFactory
{
    new bool TryCreateAssetLoader<TResult>(string uri, [NotNullWhen(true)] IAssetLoader<TResult>? result);
}
```

It puts a *generic method* on a generic interface, so the method's `TResult` shadows the interface's
(CS0693) - the `IAssetLoader<TResult>` in the signature is the method's parameter, not the interface's
- which makes the member it declares a different member from the one the base interface declares, so
the base member is left unimplemented (CS0535). Both drafts also dropped `out`, so even a version that
compiled would answer "yes, one could be made" and hand back nothing.

What ships instead:

```csharp
public interface IAssetLoaderFactory
{
    bool TryCreateAssetLoader(Type resultType, string uri, [NotNullWhen(true)] out IAssetLoader? loader);
}

public interface IAssetLoaderFactory<TResult> : IAssetLoaderFactory
{
    bool TryCreateAssetLoader(string uri, [NotNullWhen(true)] out IAssetLoader<TResult>? loader);
}
```

Three reasons for this shape rather than repairing the draft in place:

- **The provider holds one list of factories and has to ask all of them the same question.** The
  question is "can you make *a* loader for this URI that produces this result type", and the result
  type is a value the provider is holding, not a type argument it chose. `Type resultType` is that
  question. A generic method cannot be called when the type argument is only known at run time
  without reflection, which `FEATURE-COST-POLICY.md` section 3 lists among the patterns that keep
  code alive - the trimmer cannot see which instantiation was asked for.
- **`out`, and `[NotNullWhen(true)] out`.** The loader being created is the point of the method; a
  caller that has already checked the `bool` should not need a null-forgiving operator afterwards.
- **The typed member stays typed where a factory is written.** A factory that produces `ImageData`
  implements `IAssetLoaderFactory<ImageData>` and writes
  `bool TryCreateAssetLoader(string uri, out IAssetLoader<ImageData>? loader)` with no cast. This is
  the two-member pattern rather than the `IObservableList<T>.Count` redeclaration:
  `OBSERVABLE-COLLECTIONS.md` section 2 had to redeclare a member because two base interfaces each
  contributed one; here the two members ask different things and coexist.

A typed factory therefore also has to answer the non-generic member, one line comparing `resultType`
with `typeof(TResult)` and forwarding. Whether `IAssetLoaderFactory<TResult>` supplies that line as a
default interface implementation, or every factory writes it, is open question 9.9.

### 2.2 Registration

```csharp
services.AddAssetLoading();
services.AddAssetLoaderFactory(new HttpClientLoaderFactory());   // a factory, not a loader
```

`AddAssetLoading()` registers the manager and the provider. `AddAssetLoaderFactory` takes a factory,
because a factory is the thing that answers "can I make a loader for this URI" for a whole scheme or
format; a loader cannot, since a loader is one request. Registration order is the provider's order
(2.3).

### 2.3 The provider

The provider resolves many factories into one question, and the first factory that answers wins:

```csharp
public interface IAssetLoaderProvider
{
    bool TryCreateAssetLoader<TResult>(string uri, [NotNullWhen(true)] out IAssetLoader<TResult>? loader);
}
```

First match wins rather than "the most specific factory wins", because there is no ordering the
provider could compute that the application cannot express better by registering its factories in the
order it wants - and `IEnumerable<IAssetLoaderFactory>` already preserves registration order, so the
order is free and visible.

Not a dictionary keyed by scheme: a factory may claim by scheme, by host, by file extension, or by
nothing at all (an embedded-resource loader claims a single prefix), and a provider that enumerated
the possible keyings would be inventing a taxonomy that the factories already know. The exact member
set of the provider is open question 9.8.

### 2.4 The started notification, and why it is a callback

`onStarted` is the smallest thing that answers requirement 5. A queued load and a running load look
identical from the caller's side, so the manager says when one stops waiting and starts. Three things
about it are decided, and each is a trap if it is assumed rather than written:

- **It is raised by the walk, on the UI thread, at the moment the manager has called the loader's
  `LoadAsync` and is about to observe its completion.** Not when the caller asked for the load - that
  can be many walks, and many loads, earlier.
- **It is raised synchronously, from inside the manager's `LoadAsync`, when the loader starts at once**
  - a cache hit, an embedded resource already in memory. So a caller must set its own waiting state
  *before* it calls the manager, not after the call returns. This is 3.8's re-entrancy seen from the
  caller's side: the manager's walk can re-enter itself through a synchronous completion, and the
  caller's own bookkeeping is inside that loop.
- **A load cancelled while queued never raises it; one cancelled while running already has.** That is
  the whole reason the notification exists - it is what makes "pending" and "loading" separable at all
  (5 keeps the two cancellation paths apart for the same reason).

One ordering detail follows, and it is not cosmetic: the notification goes out after the loader's
`LoadAsync` has returned its task but *before* that task's completion can be delivered to the caller.
For a loader that completes synchronously, the continuation is attached after the notification, so a
caller never hears "loaded" before it hears "started".

The rejected alternative was an `IAssetLoadRequest<TResult>` handle - a type holding the task, an
`AddStartedHandler` pair, and a per-request `Priority` - which is the shape this should become if
per-request priority or state is ever wanted outside the manager. It is not wanted yet: a callback is
one parameter that callers can ignore, where a handle is a new type in every call site and a second
way to express a priority the loader already carries. Open question 9.5 keeps that choice visible.

## 3. Decisions, and why

### 3.1 Concurrency limits live in the loaders, not in the manager

Policies differ per loader kind, and they differ in shape as well as in number. An `https` loader may
want an overall limit *and* a separate limit per domain, because servers are per domain and a limit of
six across all hosts neither protects one server from the app nor lets the app use two servers at
once. An embedded-resource loader wants no limit at all - the bytes are already in the image, and a
limit there is a stutter for nothing. A factory can own that counting: it knows the domains, it knows
which loaders share a domain, and it can push availability down to its loaders as `CanLoadNow`.

The alternative was `MaxConcurrentLoads` on the manager, and I suggested it. It was rejected, and the
reason is not only "policies differ": the manager counts *handles*, while the limit that matters is
on *fetches*. With sharing (4.1) two handles can sit over one fetch, so a manager-side number is not
the number of requests - a limit of six could be running three fetches, and how far apart the two
numbers are depends on how much the application happens to share at that moment. A limit that cannot
be stated as a limit is not a limit; it is a rumour about one.

### 3.2 `CanLoadNow` is a loader-reported veto, not a manager rule

`CanLoadNow` means "may a load start right now, according to whoever knows". Only the loader knows:
the limit may be per domain, per format, a memory budget, or nothing. The manager never applies a rule
of its own; it asks, and the answer is a veto or the absence of one.

"The manager never polls" (3.7) and "the manager asks" are not in tension: a walk reads the property
once per queued loader, and a walk happens at enqueue and at a change notification. What the manager
does not do is watch it on a timer, or loop on it.

The walk skipping a vetoed loader and continuing to the next one follows from this: a closed gate for
one loader says nothing about another loader's gate, so the walk cannot stop at the first "no"
(section 5).

### 3.3 Caching and in-flight sharing are the loaders' responsibility

Both are the same knowledge, in the same place: what a URI's bytes are, and whether one is already on
its way. That is the factory's, because the factory wrote the URI logic. Putting either in the
manager would mean the manager keying something - and the manager is handed an opaque handle, so it
can key nothing except the handle's identity, which is per request and therefore useless as a cache
key. A layer above the manager does not work either: it would have to key by URI, refcount, cancel,
and re-notify, which is a factory with an extra hop in front of it, and it would still have the wrong
count for 3.1's reason.

### 3.4 `ImageData`, not `SKImage`

Kolvir already has the type this feature should produce. `ImageData`
(`src/core/Kolvir/Imaging/ImageData.cs`) is `IDisposable`, `Image.Source` is `ImageData?`
(`src/core/Kolvir/Controls/Image.cs`), and `ImageDataFactory`
(`src/core/Kolvir/Imaging/ImageDataFactory.cs`) composes the `IImageFormatProvider`s registered in
dependency injection - `Application.cs` builds it with
`new ImageDataFactory(provider.GetServices<IImageFormatProvider>())`. That seam is how animated GIF,
animated WebP and SVG get in at all: `services.AddAnimatedGifImages()`,
`services.AddAnimatedWebpImages()`, `services.AddSvgImages()`.

An `SKImage` result bypasses every bit of that. A URL pointing at a GIF would produce a still first
frame, because the animation clock lives on `ImageData`; a URL pointing at an SVG would produce
nothing, because the SVG provider is one of the `IImageFormatProvider`s the factory was built with.
The draft's `SKImage` is the rejected alternative, and it lost because it would silently change what a
URL of a given format means - the difference would look like a bug in the application's assets.

A consequence worth writing down rather than discovering: producing an `ImageData` is UI-thread
state, so the decode is the part of a load that happens on the UI thread after the bytes arrive
(3.12).

### 3.5 Priority applies only while a load is queued

A running load is never preempted. Re-prioritising changes the order of what has *not* started; it
does not throw away work in flight. This has to be said plainly, because "priorities change when you
scroll" invites the reader to expect a partially completed fetch to be abandoned and restarted.

Two reasons it is a decision and not an omission. A preempted fetch has to be resumed rather than
restarted to be worth doing, and no loader contract here carries a resumable position - the loader
signature is "give me the whole thing". And a restart costs the bytes already received plus a new
connection, on a scroll, which is the moment the app is already busiest.

### 3.6 Equal priorities are delivered in submission order

FIFO within a priority, with a sequence number as the tie-break. `List<T>.Sort` is an introsort and is
not stable, so equal keys can come back in any order; relying on luck here would produce a queue whose
order changes when the array happens to grow past a size threshold. The tie-break is the fix, and it
is the mechanism `ObservableSortedReadOnlySetView` already uses for the same problem - its tree key is
`(user comparison, arrival number)`, which keeps two tied items both present with arrival order as the
tie order (`OBSERVABLE-COLLECTIONS.md`, section 6.7).

The sequence number is taken at enqueue and *kept* when the priority changes. Otherwise re-prioritising
an entry would also move it ahead of equal-priority entries that were submitted earlier, and a scroll
that nudges one image would reorder its whole row.

### 3.7 The manager never polls

It walks once per enqueue, and once per `CanLoadNow` change. Nothing else - no timer, no per-frame
scan, no loop that re-tests until nothing changes.

Two reasons. A poll's cost would depend on the queue length, at a rate the application did not choose,
on the UI thread, for a queue that is usually idle. And the events are sufficient: a gate opens only
because a load finished or because a limit was raised, and both of those raise
`CanLoadNowChanged`; a priority changes only because something set it, and that raises
`PriorityChanged`. There is no state change the manager needs to hear about that has no event.

The walk itself is O(n) in the queued loaders; section 9.10 keeps that cost as an open question rather
than assuming it is fine.

### 3.8 The walk is re-entrancy safe

A loader can complete synchronously - a cache hit, an embedded resource already in memory - so the
walk's own "start this one" call can re-enter the walk through a completion. And a `CanLoadNow`
handler can enqueue, so a notification raised *inside* a walk re-enters it too. Recursing here is not
merely unbounded; it reorders the queue that is being walked, because the inner walk starts loads the
outer walk has already decided against.

The shape is a "walking" flag and a "walk again" flag: a re-entrant call sets the second flag and
returns, and the outer walk repeats when it finishes. `ObservableChangeBatch.Drain` is the same
mechanism for the same reason - `_isDelivering` makes a change raised from inside a handler queue
rather than nest, and the queue is what keeps the chain flat
(`src/features/Kolvir.Feature.Observables/common/ObservableChangeBatch.cs`).

### 3.9 Cancellation is the manager's guarantee, not the loader's

A loader may ignore the token entirely, and some will - a loader over an already-open stream has
nothing to cancel. So the manager cannot rely on any loader honouring it. Three things follow, and
they are all the manager's:

- **A cancelled caller's task is completed cancelled, at cancellation time.** Not when the underlying
  load happens to finish, and not with the result if the load finished in the meantime. The caller
  asked to stop; the answer arrives when it asks.
- **A late result is never delivered.** If the underlying task completes after the caller's task was
  cancelled, the result goes nowhere. This is the case that matters: the token fired, the loader kept
  going, the bytes arrived, and the control that wanted them is gone.
- **The load keeps counting against availability until the underlying task actually ends.** The
  manager does not release anything when it stops waiting. If it told the gate the slot was free on
  cancellation, a cancelled-but-still-running load would let the app silently exceed whatever limit
  the loader is enforcing - the overrun would be invisible, because the only thing that would notice
  is the server. So the manager keeps a continuation on the underlying task purely to walk again when
  it really ends.

The guarantee is about what the manager does once it is *told*. It does not supply the telling: the
token is cancelled by whoever owns the consumer, and for the case requirement 4 opens with - the image
control removed from the UI - nothing in the tree does, because removal is not observable from a
package (section 6.2). Section 5 keeps the two cases apart, because only one of them has a load to
stop.

### 3.10 Every handler subscription is released on every exit path

Completion, cancellation, an exception from the loader, a throw from the caller's continuation - all
of them unsubscribe both the priority handler and the availability handler. The existing pattern is
`EffectiveInViewState`'s `HandlerToken<Action<object>>`
(`src/features/Kolvir.Feature.EffectiveInView/common/EffectiveInViewState.cs`), and the manager uses
the same shape rather than a hand-rolled remove-in-a-finally.

The reason is sharper than tidiness here: a cancelled load is still running (3.9), so the loader
outlives the manager's interest in it. A handler that fires then would walk on behalf of an entry the
queue no longer holds, and each leaked pair is a delegate holding a dead entry alive for the life of
the handle.

### 3.11 A loader instance has at most one outstanding request

Submitting a live instance a second time is rejected; the manager throws rather than joining, queueing
or ignoring. This is deliberately narrower than "a loader is single use forever": an instance may be
submitted again once its request has finished. The distinction is load-bearing rather than pedantic -
section 4.1 is why reuse and non-reuse both exist.

### 3.12 Threads: fetch off-thread, decode and every queue change on the UI thread

- `LoadAsync` is called by application code on the UI thread. The manager's queue is UI-thread state,
  like the control tree, so a walk and a completion both happen there.
- A loader's own fetching runs off the thread - an HTTP request, a file read - and an internal
  `await` on that work uses `ConfigureAwait(false)` like the rest of the library's internals.
- The bytes are turned into an `ImageData` on the UI thread, because that is what the result is:
  `ImageDataFactory` composes the registered providers and an animated `ImageData` carries the clock
  its frames are computed from (3.4).
- The continuation that touches the queue or calls back into a control therefore uses the ambient
  context, which `Application.Run` supplies: `UiSynchronizationContext`
  (`src/core/Kolvir/Application/UiSynchronizationContext.cs`) is installed for the duration of the
  loop, so an `await` in UI code resumes on the thread that owns the controls it is about to touch.
- So the task handed to a caller completes with the UI context intact. A caller awaits
  `manager.LoadAsync(loader, token)` and assigns `image.Source` on the next line without a dispatch of
  its own. `UiSynchronizationContext.Post` queues even when the caller is already on the UI thread, so
  a completion never overtakes work the loop has already queued.

The one thing this must not be read as: "the manager marshals". The manager does not need a post of
its own for the walk it starts from `LoadAsync`, because it is already on the UI thread; it needs the
ambient context for the continuations, which is what the installed context gives it.

### 3.13 Handler pairs, not events, and disposable tokens

`Add...ChangedHandler`/`Remove...ChangedHandler` returning `IDisposable`, with `Action<object>`
receiving the sender, is the shape `ControlEffectiveInViewExtensions` already uses
(`src/features/Kolvir.Feature.EffectiveInView/common/ControlEffectiveInViewExtensions.cs`). Two
reasons to follow it rather than inventing an `event` on the loader: a host that already funnels
`Action<object>` handlers through one dispatch path can do the same for these, and the VirtualDom
generator already recognises exactly this pair as an event property when it scans for extension
members (`EXTENSION-MEMBERS-ON-VNODES.md`, section 3), which a hand-rolled event on an interface would
not be.

### 3.14 Only the generic form is offered to callers

`IAssetLoader<TResult>.LoadAsync` redeclares the return type, and the manager's `LoadAsync<TResult>` is
generic, so a caller's `await` is a `Task<ImageData>`. The non-generic `LoadAsync` exists because the
manager holds `IAssetLoader` - it is what the manager calls - and it is not what an application is
invited to call, so nobody awaits `Task<object?>` by accident and then casts.

The redeclaration boxes a *value* result: `Task<T>` where `T` is a struct is not `Task<object?>`, so
the non-generic member allocates a boxed copy to satisfy the signature. That is fine for `ImageData`,
which is a class - there is no boxing at all. It would not be fine for a loader of a struct result, and
no such loader is planned.

### 3.15 The control integration reuses the InView feature, it does not invent visibility

`src/features/Kolvir.Feature.EffectiveInView/common/ControlEffectiveInViewExtensions.cs` already
answers "is any part of this control genuinely on screen": clipped by every ancestor, reduced by
everything opaque painted over it, computed from the last completed layout pass, and driven from
`UiHost.AddInvalidatedHandler` (`EffectiveInViewState.cs`). `AddEffectiveInViewChangedHandler(
Action<object>)` returning `IDisposable` is already the change notification, occlusion included.

That handler is what moves an image's load priority, and nothing else needs to exist. In the words this
decision was taken in: this is *why* the InView feature was built. So a second visibility notion would
be a second answer to a question that already has one, and a worse one: a hand-rolled
"is my control in the viewport" would ignore occlusion, ignore ancestor clips, and need its own
invalidation hook.

## 4. The two consequences of loader-owned sharing

Sharing and per-consumer priority pull in opposite directions: one copy of the bytes, two controls that
each want to say when they want it. These two sections are where that is resolved, and they are the
subtle part of this design.

### 4.1 Sharing sits under the handle, not instead of it

`Priority` is per instance and per consumer, and so is the cancellation token. If a factory handed the
*same* loader object to two controls, those two controls would fight over one `Priority` and one
cancellation: one scroll would reorder the other control's load, and one control being disposed would
cancel the other's image. So a loader is a per-request **handle**: one per caller, its own priority,
its own token, cheap - a couple of fields, a handler list, and a reference to what it shares.

The factory refcounts the shared fetch beneath the handles and aborts it only when the last one is
cancelled:

```
  control A ---- handle A (priority Visible, token A) --\
                                                         >-- one fetch for one URI -- bytes
  control B ---- handle B (priority Background, token B)/
```

- Each handle's `LoadAsync` registers its interest and awaits the same task.
- A handle's cancellation decrements the count and completes *that* caller's task cancelled (3.9);
  nothing else changes.
- The count reaching zero cancels the fetch. The fetch's token is the factory's, not any caller's,
  which is exactly right: no single caller's token means "nobody wants this any more".
- Two handles over one fetch complete together; a late result after the last cancellation is dropped
  with the fetch.
- All of this assumes each handle is actually cancelled. A control removed from the UI cancels
  nothing today (6.2), so the count under a removed image never reaches zero on its own.

This is what lets loader-owned caching coexist with per-consumer priority, and it is why the manager's
rule is "one outstanding request per instance" (3.11) rather than "an instance is used once". An
instance is a handle; a handle is used once *at a time*, which is all the manager needs to promise.

### 4.2 Availability gates are advisory, not slot-reserving

The manager's walk in priority order is the thing that turns "capacity exists" into priority order. A
gate that *reserves* on request destroys that: the reservation goes to whoever asked first, so a
Background load starts ahead of a Visible one because it queued earlier. The priority queue would then
be decoration, and the app would show exactly the stutter the feature exists to prevent.

So the contract is:

- `CanLoadNow` means "is there capacity right now, evaluated now". It reserves nothing: asking does
  not consume a slot, and two asks with no load started in between give the same answer.
- The change event means "ask again" - not "here is your slot".

Two things make that contract workable rather than fragile:

- **The count moves at submission, not at first byte.** A load is counted when its `LoadAsync` returns
  its task, synchronously, so the gate read immediately after the manager starts a load reflects it.
  A walk therefore cannot overshoot by starting everything in one pass while the gates still say yes.
- **"Ask again" is scoped to the affected waiters.** With per-domain limits, a slot freeing on one
  domain should notify that domain's handles and no others; otherwise every scroll re-walks the whole
  queue for a slot that was never theirs. Each handle raises its own `CanLoadNowChanged` when its own
  domain has room, so the manager hears "one of your queued loaders may start now", which is the only
  thing it can act on.

A reusable gate helper - a counter, a change notification, and the per-key refcounting - is probably
worth shipping so that every https-style factory does not reimplement it. It is open question 9.1, not
a decision, because its shape is not settled and everything above stands without it.

## 5. What the manager does

Stated as an order of operations, because the interaction between the walk and the callbacks is where
a queue like this goes wrong.

**On `LoadAsync(loader, onStarted, token)`:**

1. Reject a loader with a request outstanding (3.11).
2. Subscribe both handlers (3.10).
3. Insert the entry with the next sequence number (3.6), remembering `onStarted` with it.
4. Walk.

**On a walk:**

1. If already walking, set the "walk again" flag and return (3.8).
2. Loop: take the highest-priority queued entry - priority descending, sequence ascending. If there is
   none, stop.
3. Ask its `CanLoadNow` (3.2). If false, set it aside and continue to the *next* entry - a closed gate
   for one loader says nothing about another's, and with per-domain limits the next entry is often the
   one that can start.
4. If true, remove it from the queue, call its `LoadAsync` with the caller's token, raise its
   `onStarted` on the UI thread, and attach the completion continuation. The raise comes before the
   continuation is attached so that a loader completing synchronously cannot deliver a result first
   (2.4) - which also means `onStarted` throwing leaves no continuation to detach, and the entry
   fails like any other throw from a loader.
5. Repeat until no queued entry can start.
6. Clear the flag; if "walk again" was set, walk once more.

**On a completion** for a caller that has not cancelled: unsubscribe both handlers, hand the result to
the caller's task, walk. The continuation is *observed* on the UI thread because it resumed on the
ambient context, not because the manager posted anything (3.12).

**On a cancellation, and the two cases are different:**

- **The entry is still queued.** It leaves the queue and is never started - that is requirement 4's
  first half, and it is also why `onStarted` is never raised for it (2.4). No load exists to stop, so
  there is nothing to watch: both handlers come off immediately, the caller's task is completed
  cancelled, and the walk continues with the entries below it.
- **The load is already running.** Its `onStarted` was raised when it started, so the caller has
  already been told. The caller's task is completed cancelled at once (3.9), the priority handler comes
  off - the queue order no longer concerns this entry - and the availability handler stays until the
  underlying task actually ends, because the slot is still occupied until then (3.9). When the
  underlying task ends, that handler comes off and the manager walks, for the loaders that were vetoed
  while this one held a slot.

Collapsing the two would either leave a cancelled queued entry being started later, or free a slot a
running load still holds; they are the only two states an entry has, and each has its own exit.

**On `PriorityChanged`**: move the entry with its sequence number unchanged (3.6) and walk.

**On `CanLoadNowChanged`**: walk.

The manager never disposes a handle: the loader interface has no `Dispose`, and a handle is released by
cancelling it, which is the only thing the manager has ever done to one.

## 6. The control integration (described, not built)

A URI to ask for, and a property, an error value and a change handler pair to say where the request
has got to, as C# 14 extension members in a feature package in the shape
`ControlEffectiveInViewExtensions` already uses:

```csharp
public static class ImageSourceLoadingExtensions
{
    extension(Image image)
    {
        public string? SourceUri { get; set; }
        public ImageSourceLoadState SourceLoadState { get; }
        public Exception? SourceLoadError { get; }
        public IDisposable AddSourceLoadStateChangedHandler(Action<object> handler);
        void RemoveSourceLoadStateChangedHandler(Action<object> handler);
    }
}
```

with five states:

| State | Meaning |
| --- | --- |
| `None` | No URI. Nothing has been asked for. |
| `Pending` | A URI was accepted and the load is queued, not started. |
| `Loading` | The manager said the load started (2.4). |
| `Loaded` | `Image.Source` has been assigned from the load. |
| `Failed` | The load failed; `SourceLoadError` says why. |

`Pending` and `Loading` are separate only because of 2.4, and that is the whole point of the
notification: without it there is one "not finished" state, and a UI cannot tell a placeholder from a
spinner.

**A state property, not two event pairs.** The obvious alternative is `AddSourceLoadCompletedHandler`
and `AddSourceLoadFailedHandler`, and it was rejected:

- **A consumer wants the current answer, not the transitions.** A control that renders a placeholder,
  a spinner or an error message is asking "what should I draw now", and that is a property read. Two
  event pairs answer "what happened at some point", which a template has to turn back into a state -
  and it has to get the initial case right on its own, before either event has fired.
- **A state cannot be missed; an event can.** A load that fails synchronously - a malformed URI, a
  factory that throws - is already `Failed` by the time the caller's next line runs, and a handler
  subscribed afterwards hears nothing. A state property plus a change handler has no such window: the
  read gives the answer, and the handler exists to learn that the answer changed.
- **It is the shape the rest of the library uses.** `Visible`, `Enabled` and `EffectiveInView` are
  state plus a change pair, so a VirtualDom render binds a property (`EXTENSION-MEMBERS-ON-VNODES.md`
  section 3 - and the handler pair is what the generator recognises as an event property), and an
  application has one idiom to learn rather than a second one for images.

The counter-argument, recorded because it is real: a completed handler could hand over the `ImageData`
directly, and a state property only says *that* it finished. That advantage does not apply here,
because this feature assigns `Image.Source` itself - the consumer never needs the bytes, only to know
whether to draw a spinner. A consumer that does want the bytes is better served by the manager
directly (2), which is the general path.

**Set.** Resolve `IAssetLoaderProvider` through `Control.Services` (which is `Host?.Services`,
`src/core/Kolvir/Core/Control.cs`) - not through a window, not through a static. Ask it for an
`ImageData` loader for the URI, submit it to `IAssetLoadManager`, and on completion assign
`Image.Source` and raise the state change. A new URI cancels the outstanding load: the token source,
the loader, and the InView handler token all go. Disposal and removal are the other two ways a load
should stop, and neither is observable (6.2).

**Priority.** `EffectiveInView` maps to `Priority` through `AddEffectiveInViewChangedHandler`, one
subscription per URI assignment (3.15). In view is Visible, out of view is Background; the constants
are open question 9.2.

**Where the state lives.** Not on the control: a rung-3 package cannot add fields to `Image`. It goes
in a `ConditionalWeakTable<Image, ...>` keyed on the control, exactly as `EffectiveInViewState` keeps
`States`, `Watched` and `Hosts` off every `Control` (`EffectiveInViewState.cs`). That is also what
makes the state readable after the load has finished and the tables emptied of everything else.

**Declarable in a render, with no extra work - almost.** The VirtualDom generator already maps
extension properties onto node members (`EXTENSION-MEMBERS-ON-VNODES.md`), so `new VImage { SourceUri =
"..." }` needs no generator change, and `SourceLoadState` is readable in a render for free. One
condition from that document's section 3 applies to the URI: the property must have a *public getter*
as well as a setter, because the generator reads the pristine value back through it when a render drops
the property. A set-only `SourceUri` would be silently skipped - so the property stores what it was
set to and returns it.

### 6.1 The ordering rules the control has to follow

Each of these is a consequence of something the note already decided, and each is a bug if it is
inferred rather than written down:

- **`Pending` is set before `manager.LoadAsync` is called, not after it returns.** A synchronous start
  calls back from inside that call (2.4), so the control must already be waiting when it does.
- **A new URI cancels the old load before starting the new one**, so the state goes `Loading ->
  Pending` directly and never passes through `None`: the control still has a URI, it is only a
  different one.
- **A stale completion never writes `Loaded`.** The cancelled load's completion arrives after the new
  load's state was set, and it is ignored - the same rule as 3.9's "a late result is never delivered",
  reached here from the control's side. The cheapest way to obey it is to compare the completing
  request against the current one rather than to trust the cancellation.
- **Removal and disposal leave the state where it is today, and should reset it to `Pending` once the
  seam exists (6.2).** `Pending` is the right answer for a reparent - the same control goes back into
  a tree with the same URI and wants the same bytes, and leaving it `Loading` would leave a spinner
  turning over a load that is never coming back. Until the seam exists, nothing happens on removal and
  the state stays `Loading`, which is exactly 6.2's gap seen from the UI.
- **`Failed` carries the exception in `SourceLoadError`**, not in the state, because a state value
  cannot carry one and an exception type is not a state.
- **A failed load leaves `SourceUri` set**, which makes a retry a plain `SourceUri = uri` assignment
  rather than a new API. That resolves half of what was open question 9.3 - see 6.3.

### 6.2 Removal and disposal are separate, and neither is observable

Requirement 4 opens with "the image control is removed from the UI", and removal is not disposal. The
two are different events, and neither one reaches a rung-3 package:

- **Removal does not dispose.** `Reconciler.Unmount` takes the control out of its parent -
  `control.Parent?.Controls.Remove(control)` at
  `src/features/Kolvir.Feature.VirtualDom/common/Reconciler.cs:99` - and does not dispose it. So the
  virtual DOM, which is the main producer of these controls, removes them and leaves them alive: a
  control that scrolled out of a virtualised list is detached and still usable, which is correct for
  the reconciler and is exactly the state this feature cannot see.
- **Disposal is not observable either.** `_disposed` is private (`Control.cs:72`), `Dispose(bool)` is
  `protected virtual` (`Control.cs:3572`) and `Own<T>` is `protected internal` (`Control.cs:2725`).
  Core has no `AddParentChangedHandler`, `AddRemovedHandler` or `AddDisposedHandler`, and no
  `ParentChanged` notification of any kind.

The consequence is worse than a leaked `ImageData`. The completion continuation has to assign
`Image.Source`, so it closes over the control; the handle is held by the manager; so an in-flight load
keeps a removed control reachable and runs to completion, and nothing cancels it. The bytes are paid
for, the slot is held for the whole fetch, and the control cannot be collected until the load ends -
which is precisely the case requirement 4 exists to prevent, arriving through the one door the note
had left shut.

Three options, named and not decided:

- **A removal and disposal notification the tree raises.** The honest fix, and it cannot be an
  extension member: the notification has to be *raised by the tree*, so it lives on a retained type -
  the pattern `FEATURE-COST-POLICY.md` section 3's first table row is about, and the reason section 5
  exists. `FEATURE-COST-POLICY.md` section 5.5 lists the three exceptions that justify a core member.
  Its second exception is also the shape this option should take: an **interface** the control
  implements that the tree raises through, never a virtual or an event on `Control`, because an
  interface is retained only by the types that implement it. The wrinkle for this feature is that
  `Control` itself would implement it - every control is removable - so the retained-vtable argument
  lands differently here than it does for a feature only some subclasses implement, and the cost has to
  be measured rather than assumed to be small. A decision a feature cannot take on its own; open
  question 9.6.
- **The reconciler disposing what it detaches.** Cheap to write and wrong as a rule: an application
  that detaches a control to reattach it elsewhere would get a disposed control back, and `Unmount`
  is also the path a window's teardown takes. If it is considered at all, it is considered as a
  separate change with its own reason, and it is open question 9.7.
- **Accepted and documented.** The feature package cancels on a new URI, and on a removed control it
  lets the load finish and assigns a `Source` nobody sees. Written down with its cost - one wasted
  fetch per in-flight load per removed control, and the control held alive until that fetch ends - it
  is a defensible answer for a first cut. What is not defensible is shipping it silently, which is why
  it is here.

### 6.3 Gaps to name rather than paper over

- **What happens after `Failed`.** The error *surface* is now decided - the state and `SourceLoadError`
  - and so is the rule that the URI stays set. What is not decided is the retry policy: whether
  re-assigning the same URI is the documented way to retry, whether the feature retries by itself
  (and after what delay), and whether a failure that is obviously permanent (a malformed URI, a
  factory that throws) is treated differently from a transient one. Open question 9.3.
- **Disposal of a replaced `ImageData`.** The feature created it, so the feature owns it: `Image`
  never disposes its `Source` and assigning a new one does not dispose the old
  (`src/core/Kolvir/Controls/Image.cs`). Assigning a new URI must dispose the previous loaded
  `ImageData`, and that is a real rule, not a nicety - it is the same rule an application already
  follows when it assigns `Source` itself.
- **Disposal when the control is disposed.** The same missing seam as 6.2, seen from the ownership
  side: `Control.Own<T>` is exactly the core's answer to "dispose this with the control"
  (`Control.cs:2725`), but it is `protected internal`, `Dispose(bool)` is `protected virtual`
  (`Control.cs:3572`) and there is no disposal notification to subscribe to. So the `ImageData` this
  feature created cannot be disposed at the right moment either - it outlives the control that holds
  it. Same three options as 6.2, and this is the gap that may not be solvable inside the feature
  package at all.

## 7. Packages, rung, and sequencing

Two packages, in this order:

- **`Kolvir.Feature.AssetLoading`** - the manager, the two loader interfaces, the two factory
  interfaces, the provider, and `AddAssetLoading()`/`AddAssetLoaderFactory()`. No control, no
  visibility, no image format. It is the reusable core, and it is useful on its own: an application
  with a virtual list of its own can priority-order its own loads with it.
- **A follow-up package for the `Image` loading surface** (section 6: the URI, the load state, the
  error, and the priority mapping), which references AssetLoading *and* EffectiveInView. This split is
  why the reusable core does not hard-depend on a UI visibility feature: the dependency points from the
  control integration to both, not between the two. It also means 6.2's missing seam blocks the
  follow-up without blocking AssetLoading, which is a real reason to sequence the two packages rather
  than ship them together.

**Rung: 3 for each**, per `FEATURE-COST-POLICY.md` section 4. A feature package is rung 3 by
definition, and here rung 3 is also correct on the merits rather than merely available: an
application that does not load assets should have no manager, no queue, and no loader interfaces in its
deliverable, and an application that loads its images up front with `ImageDataFactory` should not have
any of it either. Rung 2 buys nothing here: rung 2 is for a feature that has to live in core and
gates the work it does at startup, and this one has no reason to be in core - so the package
reference is the opt-in, which is rung 3's own mechanism. Rung 1 is impossible: a package in another
assembly cannot add members to `Control` or `Image`, which is the standing rung-3 consequence
`FEATURE-COST-POLICY.md` section 4 records for every feature package.

The measurement `FEATURE-COST-POLICY.md` requires - the published AOT size delta between
`Kolvir.Minimal` with and without the package, and the trim warnings - **comes with the code, not with
this note.** Nothing here can be measured, and a number invented now would be the kind of assertion
the policy exists to replace.

## 8. The rejected alternatives, in one place

| Alternative | Why it lost |
| --- | --- |
| `ExecuteAssetLoadAsync` | The manager schedules and prioritises; it does not execute (2). "Execute" also implies the token runs the work, which it does not (3.9). |
| The draft factory (`new bool TryCreateAssetLoader<TResult>(...)`) | Does not compile: CS0693, then CS0535, and no `out` to hand back the loader (2.1). |
| A non-generic `TryCreateAssetLoader` with no `out` | Answers "one could be made" and creates nothing. |
| `MaxConcurrentLoads` on the manager | Policies differ per loader kind and per domain, and the manager counts handles while the factory counts fetches; with sharing the two counts disagree (3.1). |
| `CanLoadNow` as a manager rule or a reservation | Only the loader knows its policy, and reserving on request inverts priority - first asker wins, not the most visible (3.2, 4.2). |
| A manager-level cache, or a cache layer above the manager | The manager is handed an opaque handle and can key nothing; a layer above is a factory with an extra hop and the wrong count (3.3). |
| `SKImage` as the result type | Bypasses `ImageDataFactory` and its registered providers: a GIF would be a still, an SVG nothing (3.4). |
| Preemption of a running load on re-prioritisation | Needs a resumable fetch, which no loader contract offers; a restart throws away received bytes on a scroll (3.5). |
| Sorting on priority alone, trusting `List.Sort` | Not stable; equal priorities would reorder by array size rather than by submission (3.6). |
| A poll, or a per-frame scan of the queue | Costs UI-thread work at a rate the app did not choose, and the events already cover every state change (3.7). |
| Recursing the walk | Unbounded on a synchronous completion, and the inner walk reorders the queue the outer one is walking (3.8). |
| Trusting the loader to honour the token | A loader over an open stream has nothing to cancel; the caller must still be answered, and capacity must still be accounted (3.9). |
| Removing handlers only on the success path | A cancelled load keeps running, so its handler would fire for a vanished entry (3.10). |
| A loader as single use forever | Sharing needs a handle per caller that may be reused after it completes; "one outstanding" is the rule that gets both (3.11, 4.1). |
| Handing one loader object to two controls | They would fight over one `Priority` and one cancellation (4.1). |
| A new visibility notion for the control integration | `EffectiveInView` is occlusion-aware, invalidation-driven and already the reason the feature exists (3.15). |
| Two event pairs instead of one state property | A consumer wants the current answer; a synchronous failure is already terminal before a handler can subscribe; state plus a handler is what `Visible`, `Enabled` and `EffectiveInView` already use (6). |
| `IAssetLoadRequest<TResult>` instead of an `onStarted` callback | A new type in every call site and a second way to say a priority the loader already carries; it is what this becomes if per-request state or priority is ever wanted (2.4, 9.5). |
| One "not finished" state, with no started notification | Leaves a UI unable to distinguish a placeholder from a spinner, which is requirement 5 (1, 2.4). |
| Letting "disposed" stand in for "removed" | `Reconciler.Unmount` detaches without disposing (`Reconciler.cs:99`), so a removed control stays alive and its load runs on; requirement 4's first case would be unmet (6.2). |
| A set-only `SourceUri` extension property | The generator skips it: it reads the pristine value back through a public getter (6). |
| `SourceUri` on `Image` in core | A rung-3 package cannot add members to a core type; the surface has to be extension members (7). |

## 9. Open questions

Genuinely open. Each is something this note does not decide, with the reading I would start from, so a
reviewer can disagree with the reading rather than with a decision presented as made.

1. **Does the reusable gate helper ship, and in what shape?** Probably yes once a second https-style
   factory exists; the shape (a counter with a per-key limit and change notification, and whether it
   refcounts fetches or only counts them) is mixed up with 4.1's handle/shared-fetch split, so it
   should be written against a real factory rather than guessed here.
2. **Do `AssetLoadPriority` constants ship?** Two named values - `Visible` and `Background` - would
   keep call sites from inventing their own numbers, and a `Priority` that is a bare `int` invites
   one control to pick 5 and another 7 for the same meaning. Against: a constant set is an API surface
   that has to be right, and the manager only needs a total order.
3. **What is the retry policy after `Failed`?** The surface half is decided - `Failed` plus
   `SourceLoadError`, with `SourceUri` left set so that a retry is a plain re-assignment (6.1, 6.3).
   What is not: whether re-assignment is the documented retry and nothing more, whether the feature
   ever retries by itself, and whether an obviously permanent failure (a malformed URI, a factory that
   throws) is distinguished from a transient one. The reading to start from is the smallest: no
   automatic retry, because a retry policy is a policy the application should own and a feature that
   retries cannot be told to stop.
4. **What are the state's names and values?** `SourceLoadState` versus `SourceUriLoadState` for the
   property, `ImageSourceLoadState` for the type, and whether a cancellation should have its own value
   instead of landing in `None` or `Failed`. A cancelled load is not a failure, and a UI that shows a
   red error for a scroll that cancelled a fetch would be wrong; the reading to start from is a
   separate `Cancelled` value, or `None` if cancellation can only happen when the control is going
   away.
5. **Does the started notification stay a callback parameter?** The alternative shapes are a virtual
   the manager exposes for override, or an event pair on the manager that a caller subscribes to - the
   second re-creates the per-request lookup problem a callback avoids, and the first makes one manager
   per consumer. A callback is one parameter that most callers pass `null` for; the question is
   whether it is the right long-term shape or only the smallest one that works (2.4).
6. **Does the tree gain a way to say a control was removed or disposed?** This is the seam 6.2 needs,
   and the largest question in the note. The notification cannot be an extension member - the tree has
   to raise it - so it is a member on a retained type, paid for by every application
   (`FEATURE-COST-POLICY.md` sections 3 and 5), and section 5.5's three exceptions would have to be
   argued one way or the other. The reading to start from is section 5.5's second exception: an
   interface the control implements and the tree raises through, not a virtual or an event on
   `Control`, because an interface is retained only by the types that implement it - with the caveat
   that `Control` always implements this one, so the win is smaller here than for a feature only some
   subclasses implement. An `Add`/`Remove` pair over a table holding only the controls that asked -
   the way `EffectiveInView` keeps a separate `Watched` table and computes nothing for a control
   nobody watches - keeps the *runtime* cost off unwatched controls, but not the reachability cost.
   So the question is whether that cost is acceptable, and the answer is a measurement rather than a
   guess.
7. **Does `Reconciler.Unmount` dispose what it detaches?** A core behaviour change, not a seam: it
   would fix 6.2 without touching `Control`, and it would break any application that detaches a
   control to reattach it, which is a legitimate thing to do today. Decided separately or not at all.
8. **What are `IAssetLoaderProvider`'s exact members?** `TryCreateAssetLoader<TResult>(string uri, out
   loader)` is decided (2.3); whether it also exposes "can anything handle this URI", the registered
   factories for diagnostics, or a non-generic overload is not.
9. **Does `IAssetLoaderFactory<TResult>` forward the non-generic member as a default interface
   implementation, or does every factory write the three-line comparison?** A default implementation
   keeps factories short; a written one keeps every member visible in the source of the type that has
   to behave. Not decided (2.1).
10. **Does the walk need to be cheaper than O(n) per enqueue?** A scroll enqueues many loaders at
    once, so the first real queue is where this is answerable; a priority index that starts from the
    waiter that woke it is the obvious candidate if it is not.
11. **Does `AddAssetLoading()` register anything beyond the manager and the provider?** A default
    `HttpClientLoaderFactory`, an `IImageDataFactory`-backed decoder, or nothing at all - the last is
    the smallest surface and the one rung 3 argues for.
12. **Does the manager expose its queue for diagnostics?** A read-only snapshot (entry, priority,
    state) would make the demo's readout possible and make a priority bug visible; it is also a public
    surface that has to be kept truthful, and a snapshot type is a small API of its own.
13. **What is the follow-up package called?** Nothing in this note names it, and section 6's class name
    is a placeholder too. The other packages are named after the feature (`Kolvir.Feature.Svg`,
    `Kolvir.Feature.EffectiveInView`), which argues for `Kolvir.Feature.ImageLoading` and a static
    class that matches.
