# Asset loading - design note

Status: **shipped feature; the manager, the `Image` integration and the demo page are all in the
tree.** Rung: **3** for `Kolvir.Feature.AssetLoading`: the manager and its interfaces (sections 2 and
5) and the `Image` integration (section 6) are `src/features/Kolvir.Feature.AssetLoading/`, and the
demo page of section 7 is the `asset-loading` page of `src/demo/Kolvir.Demo.App/`. So this is the
record of a shipped feature rather than a design with pieces outstanding; only section 9 is still
open. The measurement `FEATURE-COST-POLICY.md` asks for is in the pull request that added the
integration rather than in this note.

It rests on four things core now has, all merged: `Control`'s tree-lifetime notifications (`AddedToTree`,
`RemovedFromTree`, `Disposing`); `Image`'s `Source` change notification (#167); the virtual DOM's
reconciler disposing the controls it removes (#166); and `Window` disposing its tree when it tears down
(#168). Section 6.2 is where they meet.

This records a design that was settled in review before it was written down. Sections 2 to 4 are the
decided shape and the reasons for it; section 5 is what the manager does, in the order it does it;
section 6 is the control integration, as built; section 7 is the package; section 8
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
disposal are two different ways of withdrawing, and they are two different signals too - a control can
leave a tree without ending, and a control can end while it is still in one - so the note keeps them
apart instead of letting "disposed" stand in for both (6.2).

5 is the requirement that reaches back into the manager. A caller cannot tell a queued load from a
running one from the outside - both are "I have a task and it has not finished" - so the manager has
to say when a queued load starts, which is the one thing added to its signature for this (2.4).

Two things are deliberately *not* here. There is no cache and no in-flight sharing in the manager,
and there is no concurrency limit in the manager. Both are real and both belong to the loader
(sections 3.1 and 3.3), which is the same decision seen from two directions: the manager owns order,
the loader owns capacity and bytes.

A third responsibility is not the manager's either, though it is the feature's: the lifetime of a
request belongs to the control that made it. A control now hears that it left its tree, that it joined
one, and that it is being disposed (`Control`), so the control integration - not the manager -
is where a load is cancelled and restarted (6.2). The manager never learns what a control tree is.

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

    IReadOnlyList<AssetLoadSnapshot> Snapshot();   // 2.6
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

A typed factory therefore also has to answer the non-generic member. `IAssetLoaderFactory<TResult>`
supplies that as a default interface implementation - it answers `resultType == typeof(TResult)` and
forwards to the typed method - so a factory that only cares about URIs writes one method and is done.
A factory that spans result types, or whose rule is a type predicate rather than a URI match,
implements `IAssetLoaderFactory` directly and uses both `resultType` and `uri`; that is the case
`resultType` exists for, and nothing about the default stops it.

**What `resultType` is for, because it is not bookkeeping for the provider.** It is the second half of
the question a factory answers. A factory predicates on the *kind of data* a URI yields, not only on the
URI - configuration fetched over `https` might be refused while the same configuration from a local path
is fine - and it can only make that judgement if it is told what the caller asked for alongside where it
asked for it.

Two rules about the forwarding, because they are where this goes wrong later:

- **The default's test is exact type equality, not assignability.** An assignability test would let a
  factory producing `object` claim every request in the application, and evaluating one needs reflection
  over the type, which `FEATURE-COST-POLICY.md` section 3 lists among the patterns that keep code alive.
  `==` on `Type` is the cheap, trim-friendly answer, and it is a decision rather than an accident of the
  operator.
- **An override must not disagree with the typed member.** A factory that implements the non-generic
  member itself does so deliberately, and the two members must answer the same question, because the
  provider only ever asks the non-generic one (2.3).

### 2.2 Registration

```csharp
services.AddAssetLoading();
services.AddAssetLoaderFactory(new HttpClientLoaderFactory());   // a factory, not a loader
```

`AddAssetLoading()` registers the infrastructure and nothing else: the manager and the provider. It
registers no loaders, and no loader family - https, embedded resource, file, and the rest - belongs to
this note. Each of those gets its own registration method when it is built, in the shape the repository
already uses: `AddAnimations()`, `AddSvgImages()`, `AddAnimatedGifImages()`, `AddAudio()` and the others
are each one capability's opt-in, and this is the same shape rather than a bundle that drags in loaders
an application did not ask for.

`AddAssetLoaderFactory` takes a factory, because a factory is the thing that answers "can I make a loader
for this URI" for a whole scheme or format; a loader cannot, since a loader is one request. Registration
order is the provider's order (2.3).

Two consequences are worth stating rather than leaving to be discovered:

- **Calling `AddAssetLoading()` alone is a legitimate state, not a mistake.** The manager and the
  provider resolve, no factory is registered, and the provider answers false for every URI. An
  application that brings its own factories registers them through `AddAssetLoaderFactory` and never
  needs one of our loader packages at all.
- **The infrastructure call should not have to be remembered.** The reading to start from, for whichever
  loader registration is built first: `AddAssetLoading()` uses `TryAddSingleton`, so calling it twice -
  or from two call sites - does nothing the second time, and each loader registration calls it, so
  `services.AddHttpAssetLoaders()` would stand on its own. That is a rule to confirm with the first
  loader rather than something decided here; what must not happen today is a reader concluding that the
  infrastructure call is mandatory and ordering-sensitive.

**The manager and the factories are singletons.** The manager holds the queue across windows, and a
factory holds the counting state its loaders coordinate through, so both are process-wide by definition
rather than per window or per application session. `AddAssetLoaderFactory(instance)` is therefore the
natural registration, because a factory already is one. A scoped registration would be a captive
dependency - the container catches that with `ValidateScopes` only in `Debug`
(`src/core/Kolvir/Application/Application.cs:39-44`), so it is a development tripwire rather than a
guarantee.

Nothing here needs a window hook either: a window that closes disposes its tree, so a load started for a
closed window is cancelled by the same `Disposing` notification every other end uses (6.2). That is what
keeps `AddAssetLoading()` infrastructure-only rather than infrastructure plus a window lifecycle
register.

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

The question a factory is asked is the pair `(resultType, uri)`, not the URI alone: a factory may refuse
a URI for one kind of data and accept it for another, so the provider passes both and applies no rule of
its own beyond the ordering (2.1).

Not a dictionary keyed by scheme: a factory may claim by scheme, by host, by file extension, or by
nothing at all (an embedded-resource loader claims a single prefix), and a provider that enumerated
the possible keyings would be inventing a taxonomy that the factories already know. The exact member
set of the provider is open question 9.5.

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
way to express a priority the loader already carries. Open question 9.4 keeps that choice visible.

### 2.5 The priority constants

```csharp
public static class AssetLoadPriority
{
    public const int Background = 0;
    public const int Visible = 1000;
}
```

Two anchors a thousand apart, and both halves of that are the decision.

The values are far apart on purpose: future feature work and applications need room to slot intervening
priorities between "the user is looking at it" and "nothing is looking at it", and a dense pair - 0 and
1 - would force a renumbering the first time an application wanted a tier of its own. With this spacing
an application can define 900 for "about to scroll into view" without touching these constants or the
manager.

That same argument is why these are `int` constants rather than an `enum`: an enum is a closed set, and
a closed set cannot express "slightly less urgent than visible, more urgent than background", which is
exactly the case the spacing exists to allow. An enum would also make `Priority` an enum, and the
manager would then be generic over a set it does not own.

Two constants and no more. `Normal` or `Prefetch` would be guesses at tiers nobody has asked for, and a
constant set is API surface that has to be right; intervening values are legitimate and expected, naming
them before something needs them is not.

These are two anchors for the integration, not a default for loaders. `Priority` is a property on
`IAssetLoader`, so what a loader starts at is that loader's business, and this feature ships no
reference loader. If one ever chooses a default, the safe direction is `Background`: treating an
unclassified load as visible would let it compete with work the user is looking at, which is the one
mistake this feature exists to prevent.

The queue rules that consume these stay in 3.5 and 3.6 - higher wins, priority matters only while the
load is queued, and equal priorities keep submission order.

### 2.6 The diagnostics snapshot

```csharp
public readonly record struct AssetLoadSnapshot(
    IAssetLoader Loader,
    int Priority,
    long Sequence,
    AssetLoadPhase Phase,
    bool CanLoadNow);

public enum AssetLoadPhase { Queued, Running }

IReadOnlyList<AssetLoadSnapshot> Snapshot();
```

One method, for debugging, and deliberately minimal: the smallest thing that answers "why is this one not
starting".

- **Ordered the way the walk orders it** - priority descending, sequence ascending. The snapshot is a
  readable mirror of the decision rather than a bag of entries, so the answer is read down from the top.
- **`CanLoadNow` is read when the snapshot is taken**, not cached, which is what lets it explain a stall:
  an entry at the top of the list with `CanLoadNow = false` is the whole story, and it is the same answer
  the next walk would get.
- **Queued and running only.** A finished load is not in the manager any more, so the method reports the
  present rather than a log.
- **Its own phase enum, not the control's `SourceLoadState`.** The manager knows nothing about controls
  or images, and the two phases it acts on are exactly `Queued` and `Running` - `Loaded` and `Failed` are
  not missing from it, they are outside its subject.
- **It allocates only when it is called.** Nothing is maintained for it: the queue already exists and the
  snapshot is a copy of it, which is what keeps a debugging aid from becoming a cost.
- **UI thread only**, like the rest of the manager's state.

What is deliberately left out, because "do not over-engineer" is an instruction rather than a mood: no
history of finished loads, no durations or timings, no byte counters, no change notification when the
queue changes, and no `ToString` formatting contract. Each of those is something a diagnostics surface
grows when nobody says where it stops, and each would then have to be kept truthful and measured. A
caller that wants more can take the snapshot twice and diff it.

The `asset-loading` page of `src/demo/Kolvir.Demo.App/` is that consumer: it reads `Snapshot()`
beside its list, which is what the method was added for.

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

The walk is O(n) in the queued loaders, and that is decided rather than tolerated. The queue is what is
on screen plus a little prefetch - tens of entries, not thousands - and a linear scan of that, with the
insertion that keeps it ordered, is nothing next to a fetch. The number worth remembering is that the
cost is per *call* rather than per enqueue: a scroll that submits two hundred loaders in one pass is
O(n) walks of O(n) each. That is the shape that would break first if the assumption ever stops holding,
and it is cheaper to name it here than to rediscover it.

Two levers are recorded for that day, not as work: a priority index that starts the scan from the waiter
that woke it, and coalescing a batch of enqueues into one walk. The second is nearly free already - the
"walking / walk again" flag (3.8) is exactly the mechanism, and `CanLoadNowChanged` arrives through the
same path, so the same trick would cover both.

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
  asked to stop; the answer arrives when it asks. What decides that is the request's own token, not
  the order two registrations ran in: `CancellationToken.Register` invokes callbacks
  last-registered-first, and the manager registers when the request is created, so a loader that
  registers on the same token when it starts runs *before* the manager's callback does. A loader that
  completes its task inside that callback therefore hands the manager a completion while the
  cancellation is still on its way, and that completion is answered with the cancellation - and its
  result released - exactly as one that arrives after the manager has already been told. The answer
  belongs to the request, which is why it cannot rest on a callback order a loader can change.
- **A late result is never delivered, and if it is disposable the manager releases it.** If the
  underlying task completes after the caller's task was cancelled, the result goes nowhere. This is the
  case that matters: the token fired, the loader kept going, the bytes arrived, and the control that
  wanted them is gone. Nobody else can be holding that value - the caller's task was answered at
  cancellation time, and a factory hands each result to one caller rather than sharing it (4.1) - so
  the manager is the only holder, and it disposes the value there. Finalization is not a release
  policy: `ImageData` disposes the `IImageDataSource` it owns but has no finalizer of its own, so the
  native side would come back only whenever the source's own finalization happened to run.
- **The load keeps counting against availability until the underlying task actually ends.** The
  manager does not release anything when it stops waiting. If it told the gate the slot was free on
  cancellation, a cancelled-but-still-running load would let the app silently exceed whatever limit
  the loader is enforcing - the overrun would be invisible, because the only thing that would notice
  is the server. So the manager keeps a continuation on the underlying task purely to walk again when
  it really ends.

The guarantee is about what the manager does once it is *told*. It does not supply the telling: the
token is cancelled by whoever owns the consumer, and for the case requirement 4 opens with - the image
control removed from the UI - what tells it is `Control`'s removal notification, which the control
integration subscribes to and the manager never sees (section 6.2). Section 5 keeps the two
cancellation cases apart, because only one of them has a load to stop.

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
  its own. There are two hops and only the first is a queue. A load finishing on its own thread posts
  the manager's continuation, because the context it captured is not current there; when that
  continuation runs, the loop's context *is* current - it is installed for the loop's whole duration
  and nothing removes it inside a turn - so the caller's continuation runs inline, inside
  `ApplyCompletion` and therefore inside the walk. There is no second turn between "the result is
  delivered" and "the caller applies it", and nothing the loop already queued runs in between. That is
  what the manager's re-entrancy rules and the test for a completion that enqueues from inside a walk
  exist for: the caller's own code runs while the walk is in progress, and a request it makes from
  there is served by that same walk. Only a caller whose captured context is not the one current when
  the value is handed over - a caller awaiting under a context of its own, or a harness that takes the
  manager's turn with the context deliberately cleared (7.1) - gets the posted shape, and then its
  continuation waits behind whatever the loop has queued.

The one thing this must not be read as: "the manager marshals". The manager does not need a post of
its own for the walk it starts from `LoadAsync`, because it is already on the UI thread; it needs the
ambient context for the continuations, which is what the installed context gives it. Not needing a
post of its own and never being walked from a pool thread are different claims, and the second is not
a hedge on the first: what the manager does do is refuse to let a foreign thread mutate the queue, so
the notifications it receives are moved onto the UI thread before they touch anything, by an
`OnUiThread` helper that runs them inline when it is already on it and posts them through
`Application.BeginInvoke` when it is not.

**Calling `LoadAsync` from another thread throws.** The queue is UI-thread state, so a call from a pool
thread is a bug rather than something to marshal: it throws `InvalidOperationException`, checked with
`Application.CheckAccess` (`src/core/Kolvir/Application/Application.cs`), which is the same stance
`UiSynchronizationContext.Send` takes and for the same reason - a silent marshal would hide the race
between the caller and the walk rather than remove it, and the caller would then believe it had ordered
something it had not.

What that does not cover is the loader's own internals, which may of course run anywhere: a fetch is
somebody else's thread. The boundary is crossed in both directions, and by different things. The token
and the completion continuation go out, from the manager to the loader. The four notifications come
back in - `PriorityChanged`, `CanLoadNowChanged`, the `CancellationToken.Register` callback and the
completion of the task that continuation is watching - and the manager moves each onto the UI thread
before it touches the queue, so a loader may load anywhere and raise its notifications anywhere. The
asymmetry is deliberate: a call *to* the manager from a foreign thread is refused because there is a
caller to report the mistake to, while a notification *from* the loader is moved because the loader
cannot know which thread it is on and has nobody to report a refusal to.

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

The mapping is read at the start and then confirmed once on the next completed pass, rather than only
from the change handler. It is still the one mapping read twice: a load can start on a pass that has
no answer to give - the pass that attaches an unhosted tree, before the window has a client area - and
the pass that finally can answer raises nothing, because it is the pass that establishes the baseline
(6.1). Without the confirmation that load would keep the priority it was submitted with.

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
- All of this assumes each handle is actually cancelled. A control removed from the UI cancels nothing
  by itself - the count under a removed image never reaches zero on its own - so the removal
  notification is what turns "nobody can see it" into "nobody wants it" (6.2).

This is what lets loader-owned caching coexist with per-consumer priority, and it is why the rule is
"one outstanding request per instance" (3.11) rather than "an instance is used once" - a rule about the
handle, which the manager enforces by rejecting a second submission of a live one. An instance is a
handle; a handle is used once *at a time*, which is all the manager needs to promise.

One thing the diagram does not show: a handle's result belongs to that handle's caller alone - the
factory shares the fetch, never the decoded instance - which is what lets the manager dispose a result
nobody is left to receive (3.9).

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
  thing it can act on. It raises it from whatever thread freed the slot - the fetch's - and 3.12 is
  why that is legal rather than a bug.

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

## 6. The control integration

A URI to ask for, a placeholder to show until there is one, and a property, an error value and a change
handler pair to say where the request has got to, as C# 14 extension members in a feature package in
the shape `ControlEffectiveInViewExtensions` already uses:

```csharp
public static class ImageSourceLoadingExtensions
{
    extension(Image image)
    {
        public string? SourceUri { get; set; }
        public ImageData? PlaceholderSource { get; set; }
        public ImageSourceLoadState SourceLoadState { get; }
        public Exception? SourceLoadError { get; }
        public IDisposable AddSourceLoadStateChangedHandler(Action<object> handler);
        public void RemoveSourceLoadStateChangedHandler(Action<object> handler);
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

**`Source` and `SourceUri` are exclusive, and assigning either takes ownership.**

- **Assigning `SourceUri` puts the control into URI mode.** For as long as that relationship lasts the
  feature owns what `Source` holds: it writes the loaded image, it writes the placeholder, and it
  disposes the loaded image it replaces.
- **Assigning `Source` means the application has taken the control over.** The feature cancels the
  outstanding load, clears `SourceUri`, and leaves the state at `None`. The image the application
  assigned is left exactly where it is, and a load that was already in flight cannot replace it,
  because there is no longer a load to deliver.
- **The feature's own writes do not count.** It recognises an application assignment by suppressing the
  notification around its own writes, which is why this rule needs the notification below rather than a
  property read.

**Assigning `Source` and `SourceUri` in the same render is undefined.** Each is legitimate alone; a render
that assigns both is asking the feature two questions it cannot answer together, and that is the
application's bug rather than a case the feature resolves. What it does is whatever the assignment order
in the render produces, and that is deliberately not a contract - detecting it would mean a general
validation pass over generated nodes, which is a system this feature does not justify (section 8).

That last rule is the one that needed a core change, and it is the same argument 6.2 had to win for
`Control`, on one control instead of on the base class: `Image.Source` was a plain property with a
reference-equality guard and no notification to anything, so a package in another assembly could not
tell an application assignment from the one it had just made itself. `Image` now raises
`AddSourceChangedHandler`/`RemoveSourceChangedHandler` at the end of the `Source` setter, on an
assignment that changes the value (#167), and without it the exclusivity rule is not implementable from
a rung-3 package at all.

**`PlaceholderSource` is what an application shows when there is nothing loaded.** It pairs with the
control's existing `PlaceholderColor`, which fills the surface when there is no source at all:

```csharp
public ImageData? PlaceholderSource { get; set; }
```

It is drawn whenever the control has no loaded image for its URI - a shine while the load is pending, an
error image after `Failed`, or, when it is null, nothing at all and `PlaceholderColor` does the work as
it does today. There is no second drawing path: the feature writes the placeholder into `Source`, so
what an `Image` draws is always its `Source`, and `Image` itself needs no change to show one. One
property rather than one per state, for two reasons: an application that wants different visuals per
state sets a different image from the state change handler, and the obvious alternative - assigning
`Source` directly - would end the URI relationship, so an error image assigned that way would be an
error image that prevents the retry.

Four things about it are decided:

- **The feature writes it through the same guarded path as a loaded image**, so it never trips the
  ownership rule above.
- **`Loaded` wins automatically.** Assigning the loaded image replaces the placeholder, so the feature
  never has to clear it on the way out.
- **It is borrowed, not owned.** The application created it and the application disposes it; the feature
  never does. The loaded `ImageData` is the other way round - the feature created it, so the feature
  disposes it (6.3). They are the same property from two directions, and that difference is the whole
  point.
- **It is an `ImageData`, not a URI.** A placeholder that came from a URI would pend and fail on its
  own, and would recurse.

**Ordering, for a handler that assigns it.** On `Loaded` and on `Failed`, `Source` has already been
updated when the state change is raised, so a handler that assigns `PlaceholderSource` in response is
writing the image that stays on screen - the loaded image in the first case, the placeholder in the
second. 6.1 lists this with the ordering rules.

**Set.** The setter accepts the URI and goes no further than `Pending`. Before the control is in a tree
there is nothing to resolve a factory or a manager from - `Control.Services` is null - so the URI is
recorded and the load is not started. It starts when the control can reach its services, or immediately
if it already can, and from there: resolve `IAssetLoaderProvider` through `Control.Services` (which is
`Host?.Services`, `src/core/Kolvir/Core/Control.cs`) - not through a window, not through a static - ask
it for an `ImageData` loader for the URI, submit it to `IAssetLoadManager`, and on completion assign
`Image.Source` and raise the state change. A new URI cancels the outstanding load: the token source, the
loader, and the InView handler token all go. Removal and disposal are the other two ways a load stops, and
both are now notifications the package subscribes to rather than gaps (6.2).

**Retry is two assignments, and that is the whole API for it.** `SourceUri = null` ends the relationship:
the outstanding load is cancelled, the loaded image is disposed, and the control has no image of the
feature's - so the state is `None`. Setting the URI again starts a fresh load from `Pending`.

That form is the one that survives a re-rendering tree, which is why it is the form rather than a
method. The null is a real state change, so nothing that re-asserts a value it already holds can trigger
either half: the first assignment only takes effect because the value differs from what is held, and the
second only because it differs from null. A render that re-applies its properties, or an imperative
caller that sets the same URI every frame, therefore cannot restart a load or a failure by accident. A
`RetrySourceLoad()` method would be a second way to say "load this" on a surface that already has one,
and it is section 8's rejected option for exactly that reason.

The brief `None` between the two assignments is accurate rather than awkward: the control really is not
following a URI in that instant, and it is the same state it is in before any URI is set.

**Priority.** `EffectiveInView` maps to `Priority` through `AddEffectiveInViewChangedHandler`, one
subscription per URI assignment (3.15). In view is `AssetLoadPriority.Visible`, out of view is
`AssetLoadPriority.Background` (2.5).

**Where the state lives.** Not on the control: a rung-3 package cannot add fields to `Image`. It goes
in a `ConditionalWeakTable<Image, ...>` keyed on the control, exactly as `EffectiveInViewState` keeps
`States`, `Watched` and `Hosts` off every `Control` (`EffectiveInViewState.cs`). That is also what
makes the state readable after the load has finished and the tables emptied of everything else.

**Declarable in a render, with no extra work - almost.** The VirtualDom generator already maps
extension properties onto node members (`EXTENSION-MEMBERS-ON-VNODES.md`), so `new VImage { SourceUri =
"..." }` needs no generator change. `SourceLoadState` and `SourceLoadError` are get-only and so are not
node properties - the generator skips a property it cannot assign - but a render reads them from the
control it already has, which is the case they exist for. One condition from that document's section 3
applies to the URI and to the placeholder: the property must have a *public getter* as well as a
setter, because the generator reads the pristine value back through it when a render drops the
property. A set-only `SourceUri` would be silently skipped - so the property stores what it was set to
and returns it.

### 6.1 The ordering rules the control has to follow

Each of these is a consequence of something the note already decided, and each is a bug if it is
inferred rather than written down:

- **`Pending` is set before `manager.LoadAsync` is called, not after it returns.** A synchronous start
  calls back from inside that call (2.4), so the control must already be waiting when it does.
- **A URI assigned before the control is in a tree waits at `Pending`.** `Control.Services` is null until
  the control is attached, so the setter records the URI, sets `Pending` and starts nothing. It starts
  when the control can reach its services, and there are two moments that can be the first one:
  `AddedToTree` (the same event 6.2 leans on) for a control added to a tree that is already hosted, and a
  completed layout pass for a control added to a tree that is not. The second is not a refinement: a
  subtree's `Host` is set by `AttachHost`, which raises neither tree notification, so the ordinary
  imperative order - build a panel, add an image to it, put the panel in a window - reaches `AddedToTree`
  with no services and never hears about the window. The first completed pass is when that tree has
  reached a host, and it is when the load starts. This is the same rule as the re-attached control below
  rather than a special case: pending until the control can reach its services, then start or restart.
- **A load started before the visibility answer exists takes the priority the confirming pass gives
  it, not the one it was submitted with.** `Priority` is set at submission from `EffectiveInView`
  (2.5), and submission can *be* that first layout pass - which, arriving before the window has a
  client area, has nothing to say: every rectangle in it is empty, so the control reads out of view and
  the load is submitted at `Background`. The pass that finally can answer cannot correct it either: the
  first measurable pass establishes the baseline and deliberately raises no change ("registering
  establishes the baseline; only a later change is an event"), so the change handler never runs for it.
  The mapping is therefore confirmed once, on the next completed pass that can answer, and from then on
  the change handler is the only thing that moves the load. A pass that still has no client area
  consumes nothing - the confirmation waits for one that does, because applying the same unanswerable
  read would spend the only correction there is - and a control that is genuinely off screen at the
  confirming pass is confirmed at `Background`, because the confirmation is a read rather than a rule
  that everything a pass touches is visible. After that one read the load is on
  `AddEffectiveInViewChangedHandler` like every other.
- **A new URI cancels the old load before starting the new one**, so the state goes `Loading ->
  Pending` directly and never passes through `None`: the control still has a URI, it is only a
  different one.
- **A stale completion never writes `Loaded`.** The completing request is compared against the current
  one, and a result the control has moved past is disposed rather than written - the same rule as 3.9's
  "a late result is never delivered", reached here from the control's side. The comparison is not
  redundant with that rule, because 3.9 is about a *cancelled* caller and this is the successful one.
  On the delivered path as the design runs it, 3.12 has the caller's continuation inline inside the
  manager's walk, so the comparison is a no-op there: nothing can assign a new `SourceUri` between
  delivery and the write. What it covers is the shape where delivery is not inline - a caller awaiting
  under a context of its own - and the suite constructs that shape deliberately rather than relying on
  it (7.1): a result delivered, the turn that applies it still queued, a new URI assigned in between,
  and the stale image disposed rather than written. Without the comparison that result lands on a
  control following a different URI and the image it carried is never disposed. What the control
  cannot dispose is the image the manager drops: a load cancelled while running still produces one,
  and it goes nowhere - so the manager disposes it there (3.9), and the feature owns only what it is
  handed.
- **A control the reconciler removes is disposed, so its state stops mattering.** The mount created the
  control and disposes it on unmount (6.2), and the load is cancelled with it. There is no spinner left
  to correct and nothing left to keep, so this case needs no state rule of its own.
- **A control the application removes is only detached: cancel, and reset to `Pending`.** `RemovedFromTree`
  means the control left the tree and is still alive and reusable, and a reparent raises it before
  `AddedToTree`. `Pending` is the right state in between, because the control may come back with the
  same URI and want the same bytes; leaving it `Loading` would show a spinner over a load that is not
  coming back. `AddedToTree` is then where a detached image starts again - which is what that pair's own
  documentation says it is for: "an asset load, for one - starts here" (`Control.AddAddedToTreeHandler`).
  This runs inside the removal assignment, which `Control` documents as re-entrant, and cancelling
  completes the caller's task at once (3.9), so the continuation can run before the handler returns -
  which is one more reason the stale-completion rule below is a rule rather than a precaution. An image
  the load had already produced stays on screen: the control still has an image for its URI and nothing
  has failed, so `Source` is left alone and the restart replaces it when it arrives (6.3).
- **`Disposing` cancels and releases.** It is the last moment the control is usable - `Own<T>` resources
  go immediately afterwards - so the loaded `ImageData` is disposed there and nowhere later (6.3). It
  does not write `Source`, and the reason is not that the control is about to clear it: `Image.Dispose`
  has already stopped listening to its source and cleared its own reference *before* it raises this
  notification (`ReleaseImage` runs before `base.Dispose`, and it never disposes the data - 6.3), so
  `Source` is already null, and assigning to a disposing control would lay it out again and raise
  `SourceChanged` during teardown. Disposing the `ImageData` here leaves nothing pointing at it.
- **`Failed` carries the exception in `SourceLoadError`**, not in the state, because a state value
  cannot carry one and an exception type is not a state.
- **A failed load leaves `SourceUri` exactly as it was.** A failure touches none of the three: the URI
  stays, `SourceLoadState` becomes `Failed`, and `SourceLoadError` carries the reason. Clearing the URI
  would lose the only record of what failed and make a retry impossible.
- **A URI nothing claims is `Failed`, not `Pending`.** The provider answers false when no registered
  factory produces an `ImageData` for the pair, and a control left `Pending` for ever would be a
  spinner that never ends. `SourceLoadError` carries a `NotSupportedException` naming the URI, and the
  same is true of an application that assigned a URI without calling `AddAssetLoading()` - there is no
  provider to ask, and the failure is an `InvalidOperationException` that says so. Both leave
  `SourceUri` alone, so registering what was missing and assigning null and then the URI is the whole
  retry.
- **Assigning the value `SourceUri` already holds does nothing** - no restart, no state change, no
  `Source` write. The setter guards on equality like every other settable property in the library
  (`Control.Parent`, `Image.Source`, `SizeMode`), which is what makes a re-rendering tree safe: a render
  re-applies the properties that changed, and an imperative caller may well set the same value every
  frame. Without the guard, a permanent failure would become a retry storm the moment anything
  re-asserted the URI.
- **Retry is `SourceUri = null` and then the URI, in that order.** The first assignment ends the load the
  control has and takes it to `None`; the second starts a new one from `Pending`. Because the setter
  guards on equality, neither half fires unless the value actually changes, so nothing that re-asserts a
  URI it already holds can restart a load - a retry is a deliberate act, not something a render can do by
  accident.
- **`Loaded` and `Failed` are raised after `Source` is settled.** A handler that assigns
  `PlaceholderSource` in response is therefore writing the image that stays on screen, and one that
  reads `Source` reads the image the state describes. Raising the state first would let a handler see
  the previous image beside the new state.
- **A handler that takes the control over from inside that write wins.** The feature's own `Source`
  assignment raises `SourceChanged`, and a handler may assign `Source` from there. That assignment is
  the application's, so the takeover rule applies at once - URI cleared, state `None` - and the
  `Loaded` or `Failed` change that load was about to raise is dropped rather than overwriting it. The
  feature recognises its own write by the value it is writing rather than by a flag, which is what
  makes a nested assignment distinguishable from it.
- **Assigning `Source` from application code is `None`, not `Failed`.** Nothing failed: the application
  took the control over, so the URI is cleared, the error stays null, and the state says what is
  actually true.

### 6.2 Removal and disposal are separate, and both are observable

Requirement 4 opens with "the image control is removed from the UI", and removal is not disposal. Two
facts made that the hardest case in this note, and core answers both now:

- **Removal does not, by itself, dispose.** `Reconciler.Unmount` takes the control out of its parent -
  `control.Parent?.Controls.Remove(control)` at
  `src/features/Kolvir.Feature.VirtualDom/common/Reconciler.cs:113` - and that call alone ends nothing.
- **Disposal used to raise nothing.** `Dispose(bool)` is `protected virtual` and the disposal guard is
  private, so nothing outside a control could hear that it had ended.

The consequence was worse than a leaked `ImageData`: the completion continuation closes over the
control and the manager holds the handle, so an in-flight load kept a removed control reachable and ran
to completion with nothing to cancel it.

**The design rests on the notification, plus owners disposing what they own.** They were built for
different reasons and now overlap on purpose.

- **`Control` tells a subtree about all three moments** (#165): `AddRemovedFromTreeHandler` fires when
  `Parent` goes from a control to null, `AddAddedToTreeHandler` when it goes back, and
  `AddDisposingHandler` from `Dispose()` while the control is still usable and before `Own<T>`
  resources are released. Each returns an `IDisposable` token through `HandlerToken<Action<object>>`,
  the same shape as every other pair on the control. This is the notification a *package* subscribes
  to, and it is what makes the feature implementable from a rung-3 assembly at all.
- **The reconciler disposes what it removes** (#166): `Unmount` detaches and then disposes, in that
  order, so the removed control sees `RemovedFromTree` and then `Disposing` - the order `Control`'s own
  documentation promises. A control the reconciler unmounted is not reused by anything, because the next
  render that wants one creates a new one. A borrowed slot's control is an exception the reconciler
  already had: it belongs to the control that made the slot, so it is detached and not disposed.
- **A closing window disposes its tree** (#168). `Window` is the logical root, and `Control.Dispose`
  already disposes children recursively, so `TearDown` disposes `Content` and takes the subtree with it.
  The ordering is pinned rather than incidental: the backend goes first, then `Closed` handlers run, so
  they still see the controls they were shown; then the tree is disposed; then `Host.Root` is cleared;
  and only then is the window's service scope disposed - so a control's `Disposing` handler can still
  resolve a window-scoped service. `Content` reads null afterwards rather than handing back a disposed
  control, and `Show` throws `ObjectDisposedException`, so the ownership question is closed in both
  directions: a closed window cannot be shown again, and it gives back nothing to show.
  For this note the consequence is that a closed window's in-flight loads cancel through `Disposing` like
  any other, so the feature needs no window hook and `AddAssetLoading()` stays infrastructure-only (2.2).

That last change is a behaviour change for applications, and the suite found the shape of it on the first
run: two SVG size-mode tests reused one `Image` across two windows and drew nothing in the second, and now
build one image per scene. An application that keeps a control tree and moves it between windows has to
move it out before the first window closes - which is the documented rule, and cheaper to learn from a
test than from a blank window.

**Why disposing on unmount is safe.** The caveat sounds like there is a remove-and-add-elsewhere path a
disposal would break, and there is not:

- `Place` (`Reconciler.cs:943-955`) positions a control with `IndexOf` and then `Insert` or `Move`, both
  within one owner's `Controls`. A reorder therefore never unmounts anything - it is a move.
- When a slot matches but cannot be patched, `ReconcileChildren` unmounts the old subtree and creates a
  fresh control (`Reconciler.cs:197-203`). The new control is a different object; the old one is never
  reused under a different parent.
- Nothing else re-parents. A control under a different parent is a different slot in a different
  owner's list, which is a create, not a move.

So every `Unmount` is a permanent removal, disposing on it cannot take a control away from an application
that was about to use it again, and the objection this option once drew - "wrong for an application that
reattaches the control" - describes nothing the reconciler does.

**The Image integration needs both signals, for different reasons.** `Disposing` is where the loaded
`ImageData` is released and the load cancelled, because it is the last moment the control is usable. But
`RemovedFromTree` is the earlier signal that the control has left the tree and has not ended - and for a
control removed by application code rather than by a reconciler there is no `Disposing` at all, so
without the removal notification that control would keep its load and its state indefinitely. The pair
is also exactly what a reparent produces, in order, which is the case 6.1's `Pending` rule is written
for.

### 6.3 What is left to name, and what closed

- **What happens after `Failed`.** The surface and the mechanism are decided - `Failed`,
  `SourceLoadError`, `SourceUri` left exactly as it was, and a manual retry expressed as `SourceUri =
  null` followed by the URI (6, 6.1). What is still open is the *automatic* half: whether the feature
  ever retries by itself, after what delay, and whether a failure that is obviously permanent (a
  malformed URI, a factory that throws) is treated differently from a transient one. Open question 9.2.
  The reading to start from is no automatic retry at all, because a retry policy is a policy the
  application should own.
- **Disposal of a replaced `ImageData`.** The feature created it, so the feature owns it: `Image`
  never disposes its `Source` and assigning a new one does not dispose the old
  (`src/core/Kolvir/Controls/Image.cs`). Assigning a new URI must dispose the previous loaded
  `ImageData`, and that is a real rule, not a nicety - it is the same rule an application already
  follows when it assigns `Source` itself. The one image the feature does not release is the one the
  manager never hands it: a load cancelled while running is answered as cancelled, its result goes
  nowhere, and the manager disposes it (3.9, 6.1) - so there is nothing on this side to dispose, and
  the feature owns only what it is handed.
- **Owned and borrowed are two different things on one property.** A loaded `ImageData` is owned: the
  feature created it, so the feature disposes it when it is replaced, when the URI changes, and on
  `Disposing`. A `PlaceholderSource` is borrowed: the application created it, and the application
  disposes it - the feature writes it into `Source` and never disposes it, not even when the load
  succeeds and the placeholder stops being shown. The rule is the one `Image`'s own documentation
  states for `Source`, applied per writer rather than per property: whoever made the image disposes it.
- **Disposal when the control is disposed - closed.** `AddDisposingHandler` is the notification this
  needed (6.2): the feature disposes the `ImageData` it created and cancels the load there, while the
  control is still usable. Nothing else is owed here, because the notification is now the control's own
  contract rather than a seam a package has to ask for.

## 7. Package, rung, and sequencing

One package: **`Kolvir.Feature.AssetLoading`**. It ships the manager, the two loader interfaces, the
two factory interfaces, the provider, `AddAssetLoading()`/`AddAssetLoaderFactory()`, and the `Image`
integration described in section 6 - `SourceUri`, `PlaceholderSource`, `SourceLoadState`,
`SourceLoadError` and the state handler pair. One package to install and one thing to learn, and the
`Image` members are extension members, so they trim per member rather than costing every consumer of
the package.

**It references `Kolvir.Feature.EffectiveInView`, and that is the price of one package.** The priority
mapping is `EffectiveInView` -> `Priority` (3.15), so the dependency is not incidental: without it the
integration cannot ask the question the feature exists to ask. Keeping the control surface in its own
package was the earlier plan, and it existed to keep that edge off an application that only wants the
manager. Bundling trades that for one reference, and the trade is worth stating rather than glossing.

What it costs, exactly:

- **A package dependency in the build graph, not bytes in the deliverable.** An application that
  resolves only the manager still restores `EffectiveInView` and still builds it.
- **The `Image` code still trims.** The integration is extension members, which trim one member at a
  time (`FEATURE-COST-POLICY.md` section 5.1). An application that never names `SourceUri` keeps
  neither the integration code behind it nor the `AddEffectiveInViewChangedHandler` call that code
  makes reachable, so `EffectiveInView`'s own code goes with it if nothing else uses it.
- **What that costs a Virtual DOM consumer, measured.** The generator scans the consumer's whole
  reference closure (`EXTENSION-MEMBERS-ON-VNODES.md` section 2), so this package's reference to
  `EffectiveInView` puts `EffectiveInViewChanged` on 36 generated node classes - the same 36 that
  already carry `Control`'s own handler pairs, since only a `Control` node can reach a `Control`
  extension - and `SourceUri`/`PlaceholderSource` on every `VImage` node whether or not the render
  names them. In a `[VirtualDomAll]` consumer that renders one `VImage`, the generated node source grew
  by 31,976 bytes: 3,591 for the three `Image` members, 26,097 for `EffectiveInViewChanged`, and the
  rest actuator scaffolding. A hand-written application that names no member pays nothing; a Virtual
  DOM one pays for the surface, because the node's apply path names the members.
- **The exception to watch** is anything the package reaches from a path every consumer reaches. Today
  that is `AddAssetLoading()` alone, which registers the manager and the provider and nothing else
  (2.2); the extension shape is what keeps it that way, and a later addition that touches InView from a
  constructor or a registration would quietly undo the saving. That is the one thing to check when this
  package grows.

**Three PRs, one package.** They are separate for review size, not for packaging:

1. **The infrastructure** - the manager, its interfaces, the provider, the factory shape, the priority
   constants, the snapshot, and `AddAssetLoading()`/`AddAssetLoaderFactory()`. The queue and its tests are
   one argument.
2. **The `Image` integration** - the surface in section 6, in the same project. The control surface and
   its tests are a different argument.
3. **The demo page**, and it is a deliverable rather than a nicety: it is the only end-to-end proof of
   the two things no unit test can show. It is the `asset-loading` page of
   `src/demo/Kolvir.Demo.App/` - forty `Image` controls in a stack longer than the viewport, each
   following its own `SourceUri`, so scrolling, and only scrolling, moves `EffectiveInView` and with it
   the row's `Priority` between `Visible` and `Background`; each row's `SourceLoadState` and in-view
   answer beside it, updated from the handler pairs rather than a timer, so the pending/loading/loaded
   transition is watchable and a `Failed` row shows the `NotSupportedException` a developer will
   actually hit; and the manager's `Snapshot()` beside the list, the top of the queue in the manager's
   own order with each entry's priority, phase and live `CanLoadNow`. A top entry that says gated is why
   nothing below it started, which is the case 2.6 says the snapshot was designed for.

   The demo's factory is real rather than faked, and that is what makes the page prove something rather
   than illustrate it. It serves tiles the sample draws itself, so there is no network, behind **three
   concurrent fetches per host** with a 220 ms wait standing in for the transport: it counts its own
   fetches per host, answers `CanLoadNow` from that count while reserving nothing (4.2), and raises
   `CanLoadNowChanged` from the thread that finished the fetch, which is what re-walks the queued
   handles. The gate stays **demo-local** and is not proposed for the package: the reusable helper is
   open question 9.1, and its shape is worth writing against a real factory rather than guessing, so
   this is that factory and the demo is where it belongs until 9.1 settles. The page assigns its URIs
   while the tree is unhosted, the ordinary ordering 6.1 describes, so its first batch starts before any
   visibility answer exists and the confirming pass of 6.1 is what moves the rest.

   **What it measured.** Allowed to settle, the page started its rows in document order,
   `01 02 03 04 05 07 08 ... 40`, because the on-screen rows are the ones `EffectiveInView` names.
   Scrolled to row 29 while the first three fetches held the gate, the start order was
   `01 02 03 24 25 26 27 28 29 04 05 07 08 ... 23 30 ... 40`: the rows that came on screen mid-run
   were fetched ahead of the queued `Background` rows 4-5 and 7-23, and the three running loads were
   not preempted. That is "InView actually feeds the manager" observed rather than asserted - a fake
   loader proves the queue obeys a priority it is handed, and nothing else proves the priority was
   correct.

**Rung: 3**, per `FEATURE-COST-POLICY.md` section 4. A feature package is rung 3 by definition, and here
rung 3 is also correct on the merits rather than merely available: an application that does not load
assets should have no manager, no queue, and no loader interfaces in its deliverable, and an
application that loads its images up front with `ImageDataFactory` should not have any of it either.
Rung 2 buys nothing here: rung 2 is for a feature that has to live in core and gates the work it does
at startup, and this one has no reason to be in core - so the package reference is the opt-in, which is
rung 3's own mechanism. Rung 1 is impossible: a package in another assembly cannot add members to
`Control` or `Image`, which is the standing rung-3 consequence `FEATURE-COST-POLICY.md` section 4
records for every feature package.

The measurement `FEATURE-COST-POLICY.md` requires - the published AOT size delta and the trim warnings -
**is in the pull request that added the integration, not in this note.** The row the bundling decision
turned on was whether `EffectiveInView`'s code is in that binary at all: for a Virtual DOM consumer it
is, and the generated-node half of the number is in the cost list above. A `[VirtualDomAll]` fixture
that renders one `VImage` publishes at 4,068,864 bytes on win-x64, Skia loose, before this package's
surface exists and 4,143,104 with it, and naming `SourceUri`, `PlaceholderSource` and the handler pair
adds 7,168 more: the surface is what costs, not the use.

### 7.1 How the manager is tested

The queue's contract is timing, ordering and cancellation, so the tests supply all three by hand rather
than waiting for them. One fake loader type does the work: `CanLoadNow` is a field the test sets, `LoadAsync`
returns a task the test completes when it chooses (or completes synchronously, when the test needs that
case), and the handler pair is an ordinary list. No timer, no sleep, no `Task.Delay` - a test that waits is
a test that asserts about the clock instead of the walk.

What has to be pinned, because between them they *are* the contract:

- Priority order, and FIFO within a priority with the sequence tie-break (3.5, 3.6).
- A gate opening wakes exactly the entries whose gate opened, and not the rest of the queue.
- A synchronous completion does not corrupt the walk - the re-entrancy case in 3.8 - including the case
  where the completion enqueues another load.
- A queued entry that is cancelled never starts, and its `onStarted` never fires.
- A running entry that is cancelled completes its caller at once, and the slot stays counted until the
  underlying task ends (3.9).
- A late result is never delivered to a cancelled caller.
- A result nobody is left to receive is disposed when it is disposable, and a delivered one is not -
  the caller owns it from that moment, which is the half a fix could break (3.9).
- Every handler pair is released on every exit path (3.10).
- `Snapshot()` agrees with what the walk would do next - which is also the cheapest way to assert ordering
  without reaching into the manager's internals, and the reason the snapshot is worth having before there
  is a UI to look at it.

The control integration's set is in the same assembly and is driven the same way: a real application, a
real window and a real control tree on the headless backend, with a factory that hands out the fake
loader and an `ImageData` whose source records that it was disposed. It pins a URI accepted before the
control is in a tree staying `Pending` and starting on `AddedToTree` - and on the layout pass that
attaches an unhosted tree to a window - the equal assignment doing nothing, a new URI releasing the old
image and never passing through `None`, a detached control cancelling and resetting to `Pending`, a
re-attached one starting again, `Disposing` releasing the loaded `ImageData`, the placeholder being
shown while pending and replaced or restored on load and on failure, both stale-completion shapes (a
superseded request and a delivered result whose turn has not come), a failure carrying its exception
with `SourceUri` intact, `Loaded` and `Failed` being raised after `Source` is settled, and the priority
following `EffectiveInView` - including the case a change-only mapping cannot reach: a load started on a
pass that cannot answer taking the priority the next pass that can gives it, a further pass that cannot
answer leaving that confirmation waiting rather than spending it, a control still off screen at the
confirming pass staying `Background`, and a load that starts where the answer already exists being left
where it is.

One of those rules needs a turn held to be seen. On the delivered path the caller's continuation runs
inline inside the manager's turn (3.12), so a superseded completion reaches the control as the
cancellation the manager answered it with, and removing the stale-completion comparison changes
nothing - which is why the scene resumes awaits inline by default. It is not redundant, though: the
delivered result whose continuation has not run yet is the case 3.9 does not cover, and the suite
constructs that window rather than relying on it - a synchronization context it drains by hand, with the
manager's turn taken while that context is deliberately not current, so the control's turn is posted
behind it. Complete a load, run the manager's turn, assign a new URI before the control's turn, then let
it run and assert the old image was disposed rather than written. Removing the comparison makes exactly
that test fail.

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
| An `enum` for priority | A closed set cannot express a value between two named tiers, which is the case the thousand-level spacing exists for; an int keeps `Priority` a plain number the manager can order (2.5). |
| Two event pairs instead of one state property | A consumer wants the current answer; a synchronous failure is already terminal before a handler can subscribe; state plus a handler is what `Visible`, `Enabled` and `EffectiveInView` already use (6). |
| `IAssetLoadRequest<TResult>` instead of an `onStarted` callback | A new type in every call site and a second way to say a priority the loader already carries; it is what this becomes if per-request state or priority is ever wanted (2.4, 9.4). |
| One "not finished" state, with no started notification | Leaves a UI unable to distinguish a placeholder from a spinner, which is requirement 5 (1, 2.4). |
| Letting "disposed" stand in for "removed" | Removal and disposal are separate signals now (`RemovedFromTree` and `Disposing`), and a control removed by application code never sees the second; one cannot stand in for the other (6.2). |
| Accepting the gap and documenting it | A removed control's load would run to completion with nothing to cancel it - a wasted fetch per in-flight load, and the control held alive until it ended. The two mechanisms in 6.2 close it, so there is nothing left to accept. |
| A set-only `SourceUri` extension property | The generator skips it: it reads the pristine value back through a public getter (6). |
| A generator diagnostic for `Source` and `SourceUri` in one render | Detecting it means a general validation pass over generated nodes; the case is an application bug, and it is recorded as undefined rather than contracted (6). |
| A window hook to cancel loads when a window closes | A closing window disposes its tree, so `Disposing` already covers it and the package needs no lifecycle registration (6.2). |
| Leaving `Source` and `SourceUri` independent | A completion would silently overwrite an image the application assigned, and the application could not tell whether the control was still following the URI (6). |
| Restarting the load on every `SourceUri` assignment | A render re-applies changed properties and an imperative caller may set the same value every frame, so the setter has to be idempotent or a permanent failure becomes a retry storm; the guard is also what makes the two-assignment retry deliberate (6.1, 6). |
| `RetrySourceLoad()`, a method for retry | Two assignments already express it, and the null is a real state change so nothing can trigger either half by accident; a method would be a second way to say "load this" on a surface that already has one (6, 6.1). |
| A placeholder loaded from a URI | It would pend and fail on its own, including after it replaced itself, which recurses; a placeholder is an `ImageData`, not a request (6). |
| Separate `PendingSource` and `FailedSource` properties | One property plus the state change handler covers both, and a third state would want a third property; the state is the axis, so the handler picks the image per state (6). |
| `SourceUri` on `Image` in core | A rung-3 package cannot add members to a core type; the surface has to be extension members (7). |

## 9. Open questions

Genuinely open. Each is something this note does not decide, with the reading I would start from, so a
reviewer can disagree with the reading rather than with a decision presented as made.

1. **Does the reusable gate helper ship, and in what shape?** Probably yes once a second https-style
   factory exists; the shape (a counter with a per-key limit and change notification, and whether it
   refcounts fetches or only counts them) is mixed up with 4.1's handle/shared-fetch split, so it
   should be written against a real factory rather than guessed here.
2. **Does the feature ever retry a failed load by itself?** The manual case is decided: `SourceUri` is
   left exactly as it was, `Failed` and `SourceLoadError` say what happened, and a retry is `SourceUri =
   null` followed by the URI (6, 6.1). What is open is whether there is any automatic retry, after what
   delay, and whether an obviously permanent failure (a malformed URI, a factory that throws) is
   distinguished from a transient one. The reading to start from is none: a retry policy is a policy the
   application should own, and a feature that retries cannot be told to stop.
3. **What are the state's names and values?** `SourceLoadState` versus `SourceUriLoadState` for the
   property, `ImageSourceLoadState` for the type, and whether a cancellation should have its own value
   instead of landing in `None` or `Failed`. A cancelled load is not a failure, and a UI that shows a
   red error for a scroll that cancelled a fetch would be wrong; the reading to start from is a
   separate `Cancelled` value, or `None` if cancellation can only happen when the control is going
   away.
4. **Does the started notification stay a callback parameter?** The alternative shapes are a virtual
   the manager exposes for override, or an event pair on the manager that a caller subscribes to - the
   second re-creates the per-request lookup problem a callback avoids, and the first makes one manager
   per consumer. A callback is one parameter that most callers pass `null` for; the question is
   whether it is the right long-term shape or only the smallest one that works (2.4).
5. **What are `IAssetLoaderProvider`'s exact members?** `TryCreateAssetLoader<TResult>(string uri, out
   loader)` is decided (2.3); whether it also exposes "can anything handle this URI", the registered
   factories for diagnostics, or a non-generic overload is not.
6. **Do `PlaceholderSource` and `PlaceholderColor` interact when `SourceUri` is null?** Today
    `PlaceholderColor` fills a control with no source at all, and the placeholder is specified as drawn
    "whenever there is no loaded image for its URI" - so the two overlap exactly where there is no URI.
    The reading to start from is that a null `SourceUri` means the control is not this feature's to
    decorate, so `PlaceholderSource` is ignored there and `PlaceholderColor` keeps its current meaning;
    the opposite reading - a placeholder is a property of the control, not of the URI - is defensible
    and would need `PlaceholderColor` to lose the no-URI case.
7. **Does a `Source` assignment need its own state value?** It cancels the load and ends at `None`
    (6.1), which is accurate but loses the fact that a load was cancelled rather than never asked for.
    This is 9.3's question seen from a different door: if cancellation gets a `Cancelled` value for the
    URI cases, an application assignment is the one case that should probably stay `None`, because the
    control is no longer following a URI at all.
