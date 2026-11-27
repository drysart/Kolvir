# Observable collections - design and implementation plan

`OBSERVABLES.md` section 7 recorded what a collection would need and left it out of scope. This is
the first of them: a list, in the same package (`Kolvir.Feature.Observables`, rung 3 of
`FEATURE-COST-POLICY.md`), under the same rules - one thread, no locks, no reflection, no reference
to Kolvir - and over the same dependency machinery, so an expression or a component that read a list
is asked to run again when the part it read changes.

The job is not the same as a value's. A value has only to say "this changed". A list has to say *what*
changed, well enough that an observer which never reads the list can build the same list from the
events alone. So the event carries **change records**: one per actual change, which an observer
applies in the order given. An insert says where the item went. A move is a move, not a removal and
an addition. That is what lets a row shift instead of being rebuilt, and it is the whole point of
doing this at all rather than shipping "something changed, re-read everything".

Status of this document: **living plan, no code yet.** Sections 2 to 4 are the list - its shape, its
decisions and its API in full; section 5 is the core the three collections share; sections 6 and 7
are the set and the dictionary; section 8 is the handful of things to confirm before code exists;
section 9 is the order of work.

The three can ship one at a time, and the list is first because it is the one whose addresses move: an
insert shifts every index at or past it, which is the hardest case the marking rule has to get right.
Once that is right, the set and the dictionary are the same design with a simpler address.

This file is deliberately ASCII-only, like `OBSERVABLES.md`: write "section 3.4" rather than a
section sign, and "<-" rather than an arrow.

## 1. Goals and non-goals

Goals:

- **Full fidelity, up to a stated bound.** An observer that began from the same contents and receives
  every event can reconstruct the collection exactly, without ever reading it. That is a contract, not
  an aspiration: every change is reported, positions are as of that moment, and delivery order is the
  order the changes happened in. The bound is one event: it carries at most the collection's
  `MaxCollectionBatchSize` records - fifty unless its owner chose otherwise, copied from
  `Observable.Default.MaxCollectionBatchSize` when the collection was created - and a batch that would
  carry more says so with an `UntrackedChanges` record that means "re-read the collection". A batch of a
  million changes should cost an observer one rescan, not a million queued records (3.11).
- **Granularity enough to do minimal work.** A move is one record. Setting an item is one record
  with the old and the new item. An insert is one record with an index. Nothing is reported as
  "everything changed" except an actual `Clear` - in any of the three collections.
- **Batches for the caller's sake, not the observer's.** `BeginChangeBatch`/`EndChangeBatch` turn N
  mutations into one notification while keeping every record, in order, up to the cap that keeps one
  event worth reading (3.11). The observer is called once instead of N times and loses nothing it could
  have used.
- **Cheap when nothing is watching.** A mutation nobody is listening to builds no records and
  allocates nothing beyond the storage change; a batch is the only thing that collects
  unconditionally (section 3.10).
- **Reads that can be depended on, at the granularity of what was read.** `Count`, `this[i]` and a
  scan of the list are three different questions with three different answers, so each publishes its
  own dependency and a change wakes only the readers whose answer it changes (section 3.6). That
  plugs into `CollectDependencies`, `ObservableExpression<T>` and the Virtual DOM component seam with
  no new concept.
- **No new package and no new initialization.** It extends `Kolvir.Feature.Observables`; there is
  still nothing to register.

Non-goals:

- Thread safety, cross-thread notification, or dispatching to the UI thread. Every collection is
  UI-thread state like every control, exactly as `OBSERVABLES.md` section 3.9 says of values.
- Undo/redo. The records say what changed, not how to reverse it: `ListCleared` deliberately carries
  no items (3.1, and `SetCleared` and `DictionaryCleared` the same way), so an undo history is a
  different object that records its own state.
- `INotifyCollectionChanged`, `ObservableCollection<T>`, `IBindingList`, or any WPF/XAML interop.
  Section 3.8 has the mapping, which is close but not the same contract.
- Sorting as a list operation. `IList<T>` has none, and a sort reported as moves is a diff problem
  rather than a mutation, which is open question 8.6.
- Coalescing or compacting records inside a batch (dropping an add that was removed again, merging
  two sets of one item). The batch keeps what happened. 8.5 records the question.
- Filtered or sorted views over a list (5.3), and ordered or sorted variants of any of the three (open
  question 8.11). Both need the record format to settle first. Projection is no longer on this list: the
  list's view, which never drops and never reorders an item, is 5.4; the set's view, which collapses
  inner items that map to one item, is 6.5; and the dictionary's view, which is record for record again
  because a key is unique, is 7.5.

## 2. The list: type model

```
ObservableListChangeType                       what a record says happened
  ItemAdded, ItemRemoved, ItemMoved, ItemChanged, ListCleared

ObservableListChange<T>                        one change, as a readonly struct
  ObservableListChangeType ChangeType { get; }
  int?   OldIndex { get; }
  T?     OldItem  { get; }
  int?   NewIndex { get; }
  T?     NewItem  { get; }

ObservableListChangedEventArgs<T> : EventArgs  one notification: one change, or a whole batch
  IReadOnlyList<ObservableListChange<T>> Changes { get; }
  IReadOnlyObservableList<T> List { get; }

IReadOnlyObservableList<T> : IReadOnlyList<T>, IDisposable
  bool IsDisposed { get; }
  IDisposable AddObservableListChangedHandler(Action<ObservableListChangedEventArgs<T>> handler)
  void RemoveObservableListChangedHandler(Action<ObservableListChangedEventArgs<T>> handler)

IObservableList<T> : IReadOnlyObservableList<T>, IList<T>
  new int Count { get; }                       redeclared - see below
  new T this[int index] { get; set; }          redeclared - see below
  int ChangeBatchDepth { get; }
  int MaxCollectionBatchSize { get; set; }     copied from the default when created (3.11)
  void BeginChangeBatch()
  void EndChangeBatch()
  void Move(int oldIndex, int newIndex)
```

| Piece | Shape | Purpose |
| --- | --- | --- |
| `ObservableListChange<T>` | a struct of five members | One change. A struct because a batch is a `List` of them and a mutation outside a batch should not allocate one. |
| `ObservableListChangedEventArgs<T>` | `Changes` + `List` | One delivery. `Changes` is length 1 outside a batch and N inside one; `List` is there so a handler can serve several lists and so disposal is self-describing (3.9). |
| `IReadOnlyObservableList<T>` | `IReadOnlyList<T>` + handlers + `IDisposable` | The read surface plus the event. What a model exposes; what a view holds. |
| `IObservableList<T>` | `IList<T>` + batches + `Move` | The write surface. `Move` is not on `IList<T>` and is the one change a plain list cannot express (3.4). |
| `Observable.CreateList<T>` | static factories | Where a list comes from, matching `CreateValue<T>`. |

The implementation is internal, as `ObservableValue<T>` is, and handed out as the interfaces - so the
class can change, and the only thing that can write is the type that has the writable interface. The
same trust model as the values: read-only is a compile-time promise, not a guard
(`OBSERVABLES.md` section 2).

`IsReadOnly` is `false`, `ChangeBatchDepth` is 0 outside a batch, and both are ordinary members rather
than anything clever.

The list is not itself an `IObservableEventSource` (3.8). What it publishes instead is a dependency
per kind of read - the count, an index, the contents - each of them internal and reachable only by
making the read (3.6).

**`Count` and the indexer are redeclared on `IObservableList<T>`, and that is a language requirement
rather than a style choice.** `IList<T>` brings `ICollection<T>.Count` and
`IReadOnlyObservableList<T>` brings `IReadOnlyCollection<T>.Count`; those are two different members
with the same name, so a caller writing `list.Count` through the mutable interface is told the call is
ambiguous, twice over (the indexer has the same shape). A concrete class does not have the problem
because it declares one property to satisfy all three, which is why `List<T>` implements both
interfaces and no such declaration exists there - the ambiguity is a property of an *interface* that
inherits both. Redeclaring the two members on `IObservableList<T>` makes the most-derived declaration
win, and the class still implements them once. This was found by building it, and the set and the
dictionary will need the same two lines each for `IReadOnlySet<T>`/`ISet<T>` and
`IReadOnlyDictionary<,>`/`IDictionary<,>`.

## 3. The list: decisions and why

### 3.1 One record per change, and the fields each type fills in

The five change types carry the same five members, and which ones mean something is a property of the
type. A handler switches on `ChangeType` and then reads the fields that type promises - so no type has
to be interpreted case by case:

| ChangeType | OldIndex | OldItem | NewIndex | NewItem | Raised by |
| --- | --- | --- | --- | --- | --- |
| `ItemAdded` | - | - | where it went | the item | `Add`, `Insert` |
| `ItemRemoved` | where it was | the item | - | - | `RemoveAt`, `Remove` that found it |
| `ItemMoved` | where it was | the item | where it is now | the item | `Move` |
| `ItemChanged` | the index | what was there | the same index | what is there now | `this[i] = value` |
| `ListCleared` | - | - | - | - | `Clear` of a non-empty list |
| `UntrackedChanges` | - | - | - | - | a batch that grew past the collection's `MaxCollectionBatchSize`, delivered in place of what it collected (3.11) |

Two details that follow from the table rather than from taste:

- **`ItemMoved` fills in the item twice.** `OldItem` and `NewItem` are the same item, and both are
  present because a handler that switches on the type should then read `OldItem` and `NewItem` the
  same way it does for `ItemChanged`. Filling one and nulling the other would make "the fields this
  type promises" type-dependent in a way that buys nothing.
- **`ListCleared` carries nothing.** The list is empty and one record says so; the items are the
  observer's business because the observer that cared already had them. This is the one place
  fidelity is cheap rather than free: an observer that needs the cleared contents (an undo stack, a
  log) has to keep its own copy, and section 1 says undo is a different object.

Absent indexes and items are `null` (`int?`, `T?`). For a value type `T` the item field is `default`
and null means "not applicable", so an observer switches on `ChangeType` and does not test the item
for null. A second record type per change would make that checkable at compile time and would double
the type count for a struct that is discarded in a frame's time; 8.2 keeps the question open.

### 3.2 Indexes are positions at the time of the change, and replay is the contract

Every index is the index that item had (or has) **at that moment**, not at the end of the batch. So
the fidelity contract is one sentence: *apply the records to a copy of the list in the order given,
each against the state left by the one before it, and the result equals the list* - with one record
that opts out of it. `UntrackedChanges` is not a change but an admission that the event stopped
describing them (3.11), so an observer that sees it rebuilds its copy by reading the list. Nothing in
the contract allows an observer to reorder, and nothing requires it to understand the list.

The consequence worth naming: an index shift that a change causes is *implied*, not reported. Insert
at 0 and every item that was there has shifted one along, and no record says so. That is deliberate -
it would be O(n) records for one change - and it is safe because the observer applies records in
order rather than patching items by key.

### 3.3 Delivery order is the order of the changes

An observer applies records in order, so the library must deliver them in order. That is a stronger
requirement than the values have, and it decides how a change made *from inside a handler* is
delivered:

- A raise nested inside a dispatch would arrive at a handler that has not yet been told about the
  change that caused it, and applying records out of order corrupts an observer's copy. So the list
  does not nest: a mutation made while a delivery is running is appended to the **delivery queue**,
  and the queue is drained before the outermost delivery returns.
- Order within that queue is the order of the mutations, so the last handler of one event and the
  first handler of the next see a consistent history.
- Every delivery in that run is a whole event with its own records, so a consumer that only wants to
  count changes still sees each one, and a consumer that applies records never has to look ahead.

The queue is also why such a chain is not a depth problem: a change to the list being delivered is
flat, so nothing bounds how many of them a drain makes (3.7).

### 3.4 What each mutation raises

| Call | Record | Nothing raised when |
| --- | --- | --- |
| `Add(item)` | `ItemAdded` at `Count - 1` | - |
| `Insert(i, item)` | `ItemAdded` at `i` | - |
| `RemoveAt(i)` | `ItemRemoved` at `i` | - |
| `Remove(item)` | `ItemRemoved` at the index found | the item is not there (returns `false`) |
| `this[i] = value` | `ItemChanged` at `i` | the new item equals the old one (3.5) |
| `Move(from, to)` | `ItemMoved`, `from` -> `to` | `from == to` |
| `Clear()` | `ListCleared` | the list is already empty |

`Move` follows `ObservableCollection<T>.Move`: the item is taken out and put back so that it ends up
at `to`, so moving 0 to 2 in `[a, b, c]` gives `[b, c, a]` and the record is `OldIndex` 0,
`NewIndex` 2. An index at or past `Count` after the removal is refused as out of range, exactly as an
insert there would be.

An index out of range throws `ArgumentOutOfRangeException` and raises nothing: a mutation that failed
is not a change, and a record for it would be a lie an observer could not undo.

`AddRange`/`InsertRange`/`RemoveRange` are deliberately absent. `BeginChangeBatch` expresses the same
thing without a second family of methods, and each element still gets its own record, which is what
the record format wants.

### 3.5 Equality decides whether an item changed

`this[i] = value` raises only when `EqualityComparer<T>.Default` says the items differ, matching
`OBSERVABLES.md` section 3.1: an event means something a listener cares about is different, so
assigning an equal item over itself is not one. A list created with its own `IEqualityComparer<T>`
uses it here and nowhere else (the list is not a set: it has no deduplication and no membership test
that the comparer affects - `Contains` and `IndexOf` stay `EqualityComparer<T>.Default`, which is
`IList<T>`'s contract, not ours to change).

### 3.6 Reads publish per-operation dependencies

The list is not one dependency. A read of `Count` and a read of `this[3]` are different questions,
and a change should answer exactly the ones it changes - not all of them, and not none of them:

| Read | Depends on | Raised when |
| --- | --- | --- |
| `Count` | the count dependency | an add, a removal or a clear - never a write to an item, and never a move |
| `this[i]` | the dependency for index `i` | the item that `i` would return may differ: a write at `i`, an add or removal at any `p <= i` (including one that brings `i` into range or takes it out), a move whose span covers `i`, a clear |
| `GetEnumerator`, `Contains`, `IndexOf`, `CopyTo` | the contents dependency | any change at all |

Why each row is the right question:

- **`Count`** is a number only structure can change. A write to an item and a move both leave it
  alone, so neither may wake a reader that asked only for the count - which is what makes a "12
  items" label never re-render when a row is edited.
- **`this[i]`** is about the item the caller would get back, not about the identity at that address
  and not about the item it got before. An add at `p` shifts every index from `p` on, so every index
  at or past `p` may now return a different item and every index before `p` returns exactly what it
  did. A move changes only the indexes it spans. A clear changes all of them, and a write changes
  one.
- **The index dependency is published before the bounds check.** A read past the end throws, and a
  throwing read is still a read: an expression `list[7]` over a three-item list has to re-run when
  the list grows to eight, and it can only do that if it subscribed to index 7 on the way to
  throwing. This is the rule the package already follows for a run that fails - a failed run still
  yields what it reached (`OBSERVABLES.md` section 13). It also means an index that was in range and
  is not any more is raised like any other, so a reader learns its answer became an exception.
- **Enumeration and the scans** look at every item, so any change can change their answer, and they
  take the one coarse dependency. It cannot be made finer without one dependency per item, which is
  the O(n) subscription `OBSERVABLES.md` section 7 already ruled out - and unlike an index read, a
  scan really does depend on every element.

#### 3.6.1 What the dependencies are

All internal, all reachable only through a read:

| Dependency | Lives in | Created | Dropped |
| --- | --- | --- | --- |
| count | a field on the list | with the list | never - it is one field |
| contents | a field on the list | with the list | never |
| index `i` | an entry in the list's own dictionary, keyed by the index | the first read of `i` inside a run | when its last handler goes, like a named key (`OBSERVABLES.md` section 3.2) - and put back by the entry itself as soon as something watches it again |

The index entry is the same instance for as long as anything watches it, which is what makes two
reads of `this[3]` in one run one subscription: the scope de-duplicates by equality and the instance
is canonical. It is a plain reference-equality source, so it needs no `Equals` of its own.

**Dropped is not dead.** The entry is told when it goes from unwatched to watched as well as the other
way, because a caller can keep one - a run reports the sources it read, and `IObservableEventSource` is
public - and register on it again later. Its first handler puts it back in the dictionary, and an entry
that comes back to find the index held by a newer one adopts that one: every handler moves to the entry
that returned, so a change raises all of them, and the adopted entry is left forwarding so the tokens
its readers still hold unregister from the right place. Without that pair of rules a source that
outlived its release would accept handlers and never be marked - silent in the worst way, because the
reader looks subscribed and is never woken.

A read outside any run allocates nothing. Publishing is a null check on the scope field
(`ReadScope.Publish`), and the index entry is only created when a scope exists, through an internal
`ReadScope.IsActive` - so a model read in a test, a console host or a server costs exactly what it
costs today.

#### 3.6.2 What a change costs

The watched set, not the list:

- A change marks the entries it affects with a per-entry flag, and the delivery raises each marked
  entry once. A batch of a hundred records touching one row wakes one reader; a batch that touches
  nothing watched walks nothing.
- Entries are marked from the records of the delivery rather than during the mutation, so a
  subscription taken in the middle of a batch is answered by that batch's own delivery.
- Marking is one pass over the watched entries per record: an add at `p` marks every entry whose
  index is at or past `p`. The watched set is the rows on screen, not the list - ten thousand items
  with twelve visible rows is twelve entries, and the insert into the storage is the expensive part
  of that mutation either way.
- A mutation with no run watching anything marks nothing and raises nothing.

Which record marks what, which is the whole of the mapping and the whole of what the tests assert:

| Record | count | contents | indexes marked |
| --- | --- | --- | --- |
| `ItemAdded` at `p` | yes | yes | every watched index at or past `p`, and below the size it grew to |
| `ItemRemoved` at `p` | yes | yes | every watched index at or past `p`, and below the size before it shrank |
| `ItemChanged` at `i` | no | yes | `i`, if it is watched |
| `ItemMoved` `p` -> `q` | no | yes | every watched index in `min(p, q)..max(p, q)` |
| `ListCleared` | yes | yes | every watched index, with no bound - the one conservative case |
| `UntrackedChanges` | yes | yes | every watched index - it does not say what changed, so everything may have |'

The bounds are what stop an index read that was already past the end from being woken by growth that
does not reach it: an insert at 7 does not make index 12 answerable, so a reader watching 12 waits for
the insert at 12. Recovering the size each record happened at is the one piece of arithmetic in the
marking: walk the records backwards from the size the delivery left, then forward again applying them.
A clear discards the size the records began with, so it marks every watched index and accepts the few
spurious wakes that costs.

A move marks the span rather than the two endpoints because the items in between shift: moving 1 to
4 in `[a, b, c, d, e, f]` gives `[a, c, d, e, b, f]`, so indexes 1, 2, 3 and 4 all return a
different item and 0 and 5 return the same ones.

#### 3.6.3 Order, and the guard

A delivery runs the typed handlers first and raises the dependencies afterwards, once for the whole
event. Both see the same, already-mutated list, so the order decides only what a handler that opens
its own batch sees - and "the listener that was told about this change, then the reads it
invalidated" is the order that keeps a handler's own changes behind the event it is hearing about.

Each of those raises is an ordinary raise and counts against `Observable.MaxEventStackSize` like any
other, so a dependency handler whose re-run changes the list again does descend a level. The typed
dispatch itself is counted too, because a handler can re-enter a delivery - by disposing a list, or by
changing a different one - while a change to the list being delivered only ever queues. 3.7 is the
whole of that distinction.

### 3.7 What the depth guard does and does not cover

`OBSERVABLES.md` section 3.8 bounds recursion with `Observable.MaxEventStackSize`, and it is worth
being exact about what that guard is for: **stack depth**. A step calls a handler or runs a function,
the handler may step again, and enough steps kill the process with no report at all. Everything below
follows from asking, for each thing a list does, whether it is one more frame.

**A change to the list being delivered is not one.** It is queued (3.3), so the drain is a loop:
`while (pending) deliver`. Nothing bounds that loop, and nothing should. A handler that adds an item
for every item added never leaves the loop, so it never consumes a frame - there is no stack to
protect - and a limit on how many deliveries a drain may make would be a *work* budget wearing a depth
guard's name: arbitrary in its number, and refusing a legitimate cascade longer than the number picked.
Refusing one could only be done badly too, since the records already applied cannot be taken back.
A handler that never stops changing things is an ordinary infinite loop in application code, the kind
`while (true)` is, and the library does not catch those either.

**A delivery call is one, and it is counted.** The queue flattens changes to *this* list and nothing
else, so a dispatch can still be re-entered:

- a handler that **disposes** a list raises that list's final event inside its own frame;
- a handler that changes a **different** list starts *that* list's drain inside its own frame, because
  the other list has no drain in progress to queue into - and its handler can do the same again.

Those nest, one frame per hop, and a chain of them is exactly the stack overflow the guard exists to
prevent. So every dispatch of a list's event - the ordinary one, and the final empty one a disposal
delivers - enters the evaluation depth as one step, and the step past `MaxEventStackSize` is refused
with the same exception a raise loop gets. The dependency raises at the end of a delivery are counted
the same way, so a render whose re-run changes the list again descends and is bounded too.

**A long flat drain is still unbounded.** The depth returns to where it started after every delivery,
so a chain of two hundred handler-caused changes - each a delivery of the same list - completes, and a
batch is one delivery however much it collected (up to its cap, 3.11). Both are tested, and both are the point: the guard counts
nesting, not work.
### 3.8 The list is not an `IObservableEventSource`, and why

The obvious move is to derive the list interfaces from `IObservableEventSource`, the way a value does.
It is the wrong fit here, for a reason that shows up the moment you write the code:

- `IObservableEventSource.AddObservableEventRaisedHandler` takes `Action<IObservableEventSource>` -
  an untyped notification with no argument to carry records. A list's whole value is the argument, so
  the typed pair (`AddObservableListChangedHandler`) has to exist anyway, and the untyped pair would
  be a second, strictly poorer channel onto the same event.
- `IObservableEventSource.RaiseEvent()` is public and means "announce a change". On a list that
  invites a caller to fabricate a change that never happened, which no observer can apply to a copy.
  The list's equivalent is a mutation, and it already exists.
- There is no single "the list changed" question to be. A read of `Count` and a read of `this[3]` are
  different dependencies (3.6), and one source raised by everything would have to be the coarse
  notification this design exists to avoid - so the list would be a source that nothing useful should
  subscribe to.

So: typed handlers for the changes, and a dependency per kind of read underneath them. That also
keeps `Dispose` free of a "what does a raise with no records mean" question (3.9). 8.1 asks to
confirm this, since it is the one place this design departs from the shape of the values.

For the record, the mapping to `INotifyCollectionChanged` is close but not the same, which is why
8.3 keeps interop out of step 1:

| This package | WPF |
| --- | --- |
| `ItemAdded` with an index | `Add` with `NewStartingIndex` |
| `ItemRemoved` with an index | `Remove` with `OldStartingIndex` |
| `ItemChanged` at one index | `Replace` with both indexes |
| `ItemMoved` | `Move` with both indexes |
| `ListCleared` (one record) | `Reset`, where both indexes are -1 and the items are empty |
| always an index on every change | an index of -1 means "not known" |
| one item per record | `IList` of items, so one notification can carry many |

### 3.9 Disposal: records dropped, one last event, reads throw

A list is `IDisposable` for the reason a value is (`OBSERVABLES.md` section 3.10): a long-lived model
whose list a dead screen's handler is still registered on would keep that screen alive for the life of
the process. Dropping the last reference is free either way - the handler list is all a list holds
beyond its items.

`Dispose` then does what a value's does, in an order that is the behaviour:

1. `IsDisposed` becomes true, so a read from a handler now throws.
2. Any pending records - a batch that was never ended, or a queued handler-caused event - are
   **dropped**. They
   describe changes to a list nobody may read, and the observer's copy is moot along with it.
3. One last `ObservableListChanged` is delivered, with no records, so a registrant that only wants to
   detach learns the list is gone rather than quietly ceasing to hear from it. `List.IsDisposed` in the
   arguments is what distinguishes this event from every other.
4. The handler list is cleared, in a `finally`, so a throwing handler cannot leave the closure behind.

The alternative - no final event - is what the `IObservableEventSource` shape would have forced
(3.8); the package already tells registrants when a source dies, and two different rules for two
similar objects is worse than one extra documented case.

### 3.10 Cheap when nothing is watching

- A mutation with no handlers and no open batch builds no arguments and allocates nothing: the record
  is a struct that never exists, and the dependency marking is a pass over an empty watched set.
- A batch appends a struct per mutation up to its `MaxCollectionBatchSize`, whether or not anybody is
  listening - because a handler may subscribe in the middle of the batch and must then receive the whole
  batch, and because "the event describes what happened while the batch was open" is a simpler rule than
  "what happened since the last subscriber arrived". Past the cap it is one record and no further
  appends (3.11), so a bulk load's memory is bounded by the same constant as its event.
- Enumeration is a struct enumerator over the list's own storage, with a version stamp so that
  mutating during enumeration throws `InvalidOperationException` - the `List<T>` behaviour, kept
  because a mutation during a render is a bug worth failing.

### 3.11 The batch cap, and `UntrackedChanges`

A batch collects a record per change, and nothing in the API bounds how many changes a caller makes
between `BeginChangeBatch` and `EndChangeBatch` - a bulk load, a refresh, a loop over a file. Held to
full fidelity, a hundred thousand changes would mean a hundred thousand records in one event and a
hundred thousand replay steps for every observer, which is worse for everyone than reading the list
again. So an event carries at most **its `MaxCollectionBatchSize` records** - fifty unless the
collection's owner set it otherwise:

- **Past the cap, the records are dropped and replaced.** When a collected batch reaches one record
  more than the cap, the records it holds are thrown away and a single
  `ObservableListChangeType.UntrackedChanges` record takes their place. Not appended to, replacing:
  fifty islands of detail followed by "and more" is fifty records nobody will apply and one that
  invalidates all of them.
- **Once that has happened, nothing more is queued.** Every later change in the same batch is already
  covered by the record that says "changes happened", so the batch stays at one record however long the
  loop runs. That is what keeps a bulk load's cost bounded rather than merely capped.
- **`UntrackedChanges` is not a change, and observers must treat it as a rescan signal.** It carries
  nothing - there is nothing truthful to carry - and it is the one record that cannot be applied to a
  copy. An observer that sees it drops its copy and reads the list. A dependency reader is woken for
  everything (3.6.2): the size, the contents and every watched index, because the event does not say
  which of them moved.
- **The cap is a property of the collection, copied from a default.** `Observable.Default.MaxCollectionBatchSize`
  (50) is what a collection is created with, and it copies the value rather than reading it: the
  collection keeps its own from then on and exposes it as `MaxCollectionBatchSize`, so an owner whose
  observers can afford more - or must be held to less - says so on its own collection and changes
  nothing for anybody else's. The default is where a process-wide choice would go, and it is
  deliberately not process-wide: two models with different observers should not be able to retune each
  other. It is also not `MaxEventStackSize`, which counts nesting (3.7) and has nothing to do with how
  many changes an event describes.
- **The cap applies wherever records accumulate, not only inside a batch.** Outside a batch a mutation
  is delivered on its own, so the case that matters is the drain: a handler that makes many changes has
  them delivered as the event after the one it is hearing, and that event is capped the same way.
- **Delivery order is untouched.** The cap changes what an event says, never when it says it: a capped
  batch is still one event, still after the changes it describes, still delivered once to every
  handler, and a batch smaller than the cap still replays exactly.

What this costs is worth stating plainly, because it is the one place the design trades a guarantee
away: **fidelity is now bounded by the cap.** An observer whose copy must be exact in the presence of
huge batches has to handle `UntrackedChanges` by re-reading, and nothing about a record stream can save
it from that - which is the point of having a record that says so rather than a stream that quietly
stops matching.
## 4. The list: API surface, in full

```csharp
namespace Kolvir.Feature.Observables;

public enum ObservableListChangeType { ItemAdded, ItemRemoved, ItemMoved, ItemChanged, ListCleared }

public readonly struct ObservableListChange<T>
{
    public ObservableListChangeType ChangeType { get; }
    public int? OldIndex { get; }
    public T? OldItem { get; }
    public int? NewIndex { get; }
    public T? NewItem { get; }
    public override string ToString();
}

public sealed class ObservableListChangedEventArgs<T> : EventArgs
{
    public IReadOnlyList<ObservableListChange<T>> Changes { get; }
    public IReadOnlyObservableList<T> List { get; }
}

public interface IReadOnlyObservableList<T> : IReadOnlyList<T>, IDisposable
{
    bool IsDisposed { get; }
    IDisposable AddObservableListChangedHandler(Action<ObservableListChangedEventArgs<T>> handler);
    void RemoveObservableListChangedHandler(Action<ObservableListChangedEventArgs<T>> handler);
}

public interface IObservableList<T> : IReadOnlyObservableList<T>, IList<T>
{
    new int Count { get; }                      // redeclared: two inherited Counts are ambiguous
    new T this[int index] { get; set; }         // and so are two inherited indexers
    int ChangeBatchDepth { get; }
    int MaxCollectionBatchSize { get; set; }    // a copy of the default, and this list's own (3.11)
    void BeginChangeBatch();
    void EndChangeBatch();
    void Move(int oldIndex, int newIndex);
}

public static partial class Observable
{
    public static IObservableList<T> CreateList<T>();
    public static IObservableList<T> CreateList<T>(params ReadOnlySpan<T> items);
    public static IObservableList<T> CreateList<T>(IEqualityComparer<T>? comparer);
    public static IObservableList<T> CreateList<T>(IEqualityComparer<T>? comparer, params ReadOnlySpan<T> items);
}
```

`params ReadOnlySpan<T>` is what `net10.0` supports, and it keeps the common `CreateList(1, 2, 3)`
from allocating an array to copy from. `ChangeBatchDepth` is on the interface rather than hidden
because a library that mutates on someone else's behalf needs to know whether it is already inside a
batch before opening one. `MaxCollectionBatchSize` is on it, and settable, because the cap decides what
a list's own observers have to be prepared for and that is a decision the list's owner makes - the
constructor copies `Observable.Default.MaxCollectionBatchSize` into it, so a list nobody tunes behaves
exactly as it would with a constant (3.11).

XML documentation is mandatory and CS1591 is an error for anything that ships, so every member above
needs a `<summary>`, and the members inherited from `IList<T>`/`IReadOnlyList<T>` need none until they
are implemented.

## 5. The family: the shared core

Sections 2 to 4 are the list's. The set (section 6) and the dictionary (section 7) answer the same
three questions with different records, and everything between a mutation and a handler is the same
code for all three. That is the part to build once:

```
ObservableChangeBatch<TChange>       one collection's pending records, its batch depth, its delivery
                                     queue, and the handlers it delivers to
ObservableKeyedDependencies<TKey>    the watched addresses: an entry per key holding that entry's
                                     handlers, created by a read inside a run, dropped when its last
                                     handler goes, and the thing a change walks to mark what moved
ObservableShapeDependencies          the two sources every collection has: its shape, and its contents
```

A collection supplies three things and nothing else:

1. **Its record**, and the enum that says which fields that record means.
2. **Its mapping**, mutation to record: which call raises which record, and when it raises nothing.
3. **Its marking rule**, record to dependency: an insert marks the indexes at or past it, a move marks
   the span it moves across, a key marks itself, a clear marks everything.

### 5.1 The three questions every collection answers

This is the invariant worth stating once, because it is what makes all three designs one design:

| Question a read asks | Dependency | Raised by |
| --- | --- | --- |
| **The shape** - how many items, or which keys exist | count, or structure | anything that adds or removes an item |
| **The address** - what is at index `i`, is `x` present, what is at key `k` | that index, item or key | anything that may change that address's answer |
| **The contents** - an enumeration or a scan | contents | any change at all |

The three collections name them differently - a list's address is an index, a set's is an item, a
dictionary's is a key, and a dictionary's shape is its key set rather than its count - but the split,
the reasons and the code are the same. The address is the one that earns the work: it is what lets a
row re-render without re-rendering the list, and it is why an address dependency is dropped when its
last watcher goes rather than kept per address ever read - put back by the entry itself when something
watches it again (3.6.1).

The batch policy is shared too, and it is the one piece of behaviour that is not per collection: every
collection caps one event at its own `MaxCollectionBatchSize`, copied from
`Observable.Default.MaxCollectionBatchSize` when it is created, and every collection delivers one
`UntrackedChanges` record past that cap. Only the record types differ (3.11).

### 5.2 What is not shared

- **The records, enums and event arguments** are per collection. One shared `ObservableChangeType`
  would have nine values of which a list handler can see five, and every handler would have to know
  which of the ones it was given are impossible (open question 8.9).
- **The marking rule.** Marking is where a collection's semantics live: a list's insert marks a suffix
  because positions shift, a dictionary's change marks one key, a set's bulk operation marks one entry
  per item.
- **The mutation surface.** `ISet<T>` has four bulk operations and no indexer, `IDictionary<,>` has an
  indexer and no order, `IList<T>` has both an indexer and a `Move`.

### 5.3 Still undesigned

- **Derived views** - `Filter` and `Sort` over an `IReadOnlyObservableList<T>`. `Select` is 5.4, and it
  is the easy one: it drops nothing and reorders nothing, so every record of the source has exactly one
  record in the view and the old and new items the records already carry are all the mapping needs. The
  hard ones are the ones that decide membership or order - an insert the filter drops is no record, and
  a re-sort is a diff rather than a mutation.
- **`AsReadOnly` wrappers** for each collection: pass records through and hide the write surface. What
  a model exposes when it wants to hand out a collection without letting anyone mutate it.
- **An ordered or sorted variant.** A `SortedSet`-shaped collection has to report where things went,
  which is `Move` with a different mutation surface (open question 8.11).

### 5.4 The mapped view

`ObservableMappedReadOnlyListView<TInner, TOuter>` is a read-only view over another observable list
that projects every item through a function - the `Select` of the family, and the one derived view that
turned out not to need a design of its own:

```csharp
namespace Kolvir.Feature.Observables;

public sealed class ObservableMappedReadOnlyListView<TInner, TOuter>
    : IReadOnlyObservableList<TOuter>, IObservableChangeTarget<ObservableListChange<TOuter>, ...>
{
    public ObservableMappedReadOnlyListView(
        IReadOnlyObservableList<TInner> inner,
        Func<TInner, TOuter> selector);

    public bool IsDisposed { get; }
    public int Count { get; }
    public TOuter this[int index] { get; }
    public bool Contains(TOuter item);
    public int IndexOf(TOuter item);
    public void CopyTo(TOuter[] array, int arrayIndex);
    public IEnumerator<TOuter> GetEnumerator();
    public IDisposable AddObservableListChangedHandler(Action<ObservableListChangedEventArgs<TOuter>> handler);
    public void RemoveObservableListChangedHandler(Action<ObservableListChangedEventArgs<TOuter>> handler);
    public void Dispose();
}
```

`Contains`, `IndexOf` and `CopyTo` are not on `IReadOnlyList<T>`; they are here because they are the
scans a list has and a view of one should have, and because each has to publish the contents
dependency. What is deliberately absent is the write surface, `ChangeBatchDepth` and
`MaxCollectionBatchSize`: a view is read-only to whoever is handed it, and its batching is its own
business.

The pieces are all section 5's. It implements `IObservableChangeTarget` exactly as the list does, so
`ObservableChangeBatch` gives it the queue, the batches, the cap, the final empty event and the
disposal; `ObservableListDependencies<TOuter>` gives it the same three questions; and every record it
delivers is built with the same `ObservableListChange<TOuter>` factories a list uses. Nothing is
widened out of `internal`, because the view lives in the same assembly.

**Eager, cached mapping.** A parallel `List<TOuter>` is kept beside the source, index for index, and
the function runs once per item that arrives. A read is then a list read: no projection on the read
path, no allocation per item, and no LINQ.

**Record for record, translated.** Every record the source raises has exactly one record here:

| Source record | The mapped list | The record the view delivers |
| --- | --- | --- |
| `ItemAdded` at `i` | `selector(NewItem)` inserted at `i` | `ItemAdded` at `i`, mapped |
| `ItemRemoved` at `i` | the mapped item at `i` dropped | `ItemRemoved` at `i`, the mapped item it held |
| `ItemMoved` `i` to `j` | the mapped item at `i` moved to `j` | `ItemMoved` `i` to `j`, that item on both sides |
| `ItemChanged` at `i` | `selector(NewItem)` replaces it | `ItemChanged` at `i`, both mapped values |
| `ListCleared` | emptied | `ListCleared`, carrying nothing |
| `UntrackedChanges` | rebuilt from the source | `UntrackedChanges` |

A move is a reposition and does not map anything again: the mapped value the view already has is the
mapped value the moved item has, which is the same reason `Move` is its own record in the first place.
A removal does not map at all - the record says what came out, and that is the value the view already
held. An add and a replacement map exactly once each. So the call count is a claim a test can assert
rather than an implementation detail.

An unknown change type falls into the untracked row rather than throwing, which is the same
conservative answer `ObservableListDependencies.Mark` gives for a record it does not know: rebuild, and
say that the change was not described.

**One source delivery is one view delivery.** `OnInnerChanged` opens one batch of the view's own around
the translation, so a source batch of three records leaves here as one event with three translated
records in the source's order. Without it the view would deliver once per record, and a consumer that
batched on the source would still be woken three times.

**The cap is the view's own, and it is the default.** The view's batch is a batch like any other, so it
carries `Observable.Default.MaxCollectionBatchSize` and knows nothing about whatever a source's owner
set on the source. A source left at the default cannot deliver more records in one event than the view
will describe, so that event arrives here intact - exactly the cap included, because the check is
strictly greater. A source whose owner raised its cap above the default can deliver more, and an event
longer than the view's cap collapses here to one `UntrackedChanges` record, like any other over-cap
batch (3.11). The mapped list is not affected by that collapse: every item is applied and mapped before
the record is written, so the view's contents and its count are complete and every dependency is marked
- only the description of the change is gone. It is the same fidelity-for-boundedness trade 3.11
documents for one list, one level down: an owner who decided the source's handlers could afford to
replay two hundred records did not make that decision for this view's handlers, which rescan instead.

There is no `MaxCollectionBatchSize` here on purpose. The source's value is not on
`IReadOnlyObservableList<TInner>` at all, so mirroring it would take a type test against the concrete
list that goes stale the moment that owner retunes the source.

**Reads publish the same three questions** (5.1), from the view's own dependency table: `Count` is the
count, `this[i]` is index `i`, and enumeration, `Contains`, `IndexOf` and `CopyTo` are the contents.
An expression over the view is therefore woken through the view and once per change, rather than
through the source and once per record - and a reader of one row is not woken by a change to another.

**Mutating an item in place is not observed.** If `TInner` is mutable and changes internally, `TOuter`
does not follow: nothing in the source list reports that, and this view only re-maps what the source
says was replaced. Replacing the item in the source (`list[i] = ...`) re-maps it. There is no
`Refresh` or `Remap`: re-mapping everything would invalidate every index and be an untracked event in
disguise, and mapping on read is the thing this type exists to avoid, so the boundary is documented
instead of papered over with an API that has no cheap correct shape.

**The source is not owned.** The view subscribes to it, reads it, and never disposes it - the same
source may be read directly and by other views, so a view that took the model's list down on teardown
would be a bug. `Dispose` unsubscribes, drops what had not been delivered, delivers one last event with
no changes, and lets its handlers go, exactly as 3.9 says of a list. Everything else here follows from
`ObservableChangeBatch` rather than from new code: `IsDisposed` is true from the start of disposal,
everything else throws `ObjectDisposedException`, and removal plus a repeated dispose stay safe.

**A disposed source is not a disposed view.** The source's one empty event is its disposal, which the
view ignores: there can never be another change, so it keeps the contents it last had and stays
readable. Its lifetime is its own - and the handler snapshot the source delivers to means a late call
can arrive after the view unsubscribed, which the view answers by doing nothing.

**The selector's contract.** It runs on the thread that changed the source, inside the source's
delivery, at most once per item that arrives. It must not throw and must not change the source list or
the view; a function that throws propagates out of the source list's delivery like any other handler's
exception, and leaves the view behind the source until something forces a resync.

**No factory.** There is no `Observable.Map` beside `Observable.CreateList`. Every `Create*` factory
returns something the caller does not dispose; this view is the one object in the family its holder
must dispose, and a factory shaped like its neighbours is where that obligation gets lost. The
constructor is the API, and a factory can be added later without breaking anything.

## 6. The observable set

The simplest of the three: no index, no value, so a change can only add an item or take one away, and
the only interesting decision is what the four bulk operations of `ISet<T>` do to a record stream.

Sections 3.1 to 3.5, 3.7, 3.9, 3.10 and 3.11 apply unchanged - one record per change as a readonly
struct,
addresses as of the moment of the change, delivery in the order of the changes through a queue rather
than a recursive raise, no limit on how much one drain delivers, disposal dropping pending records and
delivering one last empty event, and nothing allocated when nothing is watching.

### 6.1 The type model

```
ObservableSetChangeType                        what a set change can be
  ItemAdded, ItemRemoved, SetCleared, UntrackedChanges

ObservableSetChange<T>                         one change
  ObservableSetChangeType ChangeType { get; }
  T? Item { get; }

ObservableSetChangedEventArgs<T> : EventArgs
  IReadOnlyList<ObservableSetChange<T>> Changes { get; }
  IReadOnlyObservableSet<T> Set { get; }

IReadOnlyObservableSet<T> : IReadOnlySet<T>, IDisposable
  bool IsDisposed { get; }
  IEqualityComparer<T> Comparer { get; }
  IDisposable AddObservableSetChangedHandler(Action<ObservableSetChangedEventArgs<T>> handler)
  void RemoveObservableSetChangedHandler(Action<ObservableSetChangedEventArgs<T>> handler)

IObservableSet<T> : IReadOnlyObservableSet<T>, ISet<T>
  int ChangeBatchDepth { get; }
  int MaxCollectionBatchSize { get; set; }
  void BeginChangeBatch()
  void EndChangeBatch()
```

### 6.2 Decisions and why

#### 6.2.1 The comparer is the identity, so it is exposed

A set's `Contains`, `Add`'s "already there", `Remove` and every set-algebra operation mean what the
comparer says they mean: two items it calls equal are *the same item*. So `Comparer` is on the read
surface - a consumer mirroring the set has to build its mirror with the same comparer or it will
disagree about what is in it - and it is what the per-item dependencies are keyed by. `CreateHashSet<T>()`
without one uses `EqualityComparer<T>.Default`.

This is also where `Observable.GetNamed` stops being usable, and the reason 3.6.1 said entries are the
collection's own business: a named key compares by the key's own equality, while a set's comparer is a
property of the set and not of the item. The entry table therefore lives in the set, keyed with the
set's comparer.

Because the entry table is a dictionary whose key is the item, a set's item type carries `notnull` -
`CreateHashSet<T>()` and the class beneath it constrain it - so a set cannot hold null. `HashSet<T>`
allows one, and a null item could not be an address: nothing could key an entry for it. That is a
deliberate narrowing rather than an oversight, and it is the one place the observable set is stricter
than the BCL one.

#### 6.2.2 The bulk operations are implicitly batched

`UnionWith`, `ExceptWith`, `IntersectWith` and `SymmetricExceptWith` change a number of items nobody
knows in advance. They report **one record per item actually added or removed**, inside an implicit
batch, so the observer gets one event with all of them in order:

- That is full fidelity without a "many items changed" record an observer would have to interpret, and
  without a second notification shape for the bulk path.
- It is the same mechanism a caller uses for a hand-written run of changes: `BeginChangeBatch` and
  `EndChangeBatch` around the loop. Nested inside a caller's batch, the implicit one joins it rather
  than delivering an event of its own.
- A bulk operation that changes nothing raises nothing at all: `UnionWith` of a subset, `ExceptWith` of
  an empty set, `IntersectWith` of a superset, symmetric difference with an empty set. Corrected after
  review: an earlier draft said "symmetric difference with the set itself", which is the opposite - a
  set's symmetric difference with itself is the empty set, so it empties the set and is reported as one
  `SetCleared`.
- A bulk operation that changes more items than the set's `MaxCollectionBatchSize` is capped exactly as
  a caller's batch would be (3.11): it delivers one `UntrackedChanges` record, and the reader re-reads
  the set. `UnionWith` of ten thousand new items is one record.

#### 6.2.3 There is no `ItemChanged`, and no order

A set holds one item per equality class, so "the value changed but the item is the same" is not a
change a set can have: adding an item the comparer calls equal is a no-op that returns `false`, and
swapping one for an unequal one is a removal and an addition, which the caller expresses as two calls
or as `SymmetricExceptWith`. There is likewise no `Move`, because a set has no order to change - an
ordered set is a different collection (open question 8.11).

#### 6.2.4 Reads: the count, the item, the contents

| Read | Depends on | Raised when |
| --- | --- | --- |
| `Count` | count | an item is added, an item is removed, or the set is cleared - never by a no-op |
| `Contains(x)` | the entry for `x` | `x` is added or removed, by any call including the bulk operations and `Clear` |
| enumeration, `SetEquals`, `IsSubsetOf`, `IsSupersetOf`, `IsProperSubsetOf`, `IsProperSupersetOf`, `Overlaps` | contents | any change at all |

`Contains` earns its own entry because it is the read a rendered toggle makes - a chip per tag, each
one asking whether its own tag is in the set - and a change to one tag must not re-render the others.
The scans read every item, so they take the contents dependency for the reason 3.6 gives.

`Comparer` is a constant of the set and publishes nothing.

### 6.3 API surface, in full

```csharp
public enum ObservableSetChangeType { ItemAdded, ItemRemoved, SetCleared, UntrackedChanges }

public readonly struct ObservableSetChange<T>
{
    public ObservableSetChangeType ChangeType { get; }
    public T? Item { get; }
}

public sealed class ObservableSetChangedEventArgs<T> : EventArgs
{
    public IReadOnlyList<ObservableSetChange<T>> Changes { get; }
    public IReadOnlyObservableSet<T> Set { get; }
}

public interface IReadOnlyObservableSet<T> : IReadOnlySet<T>, IDisposable
{
    bool IsDisposed { get; }
    IEqualityComparer<T> Comparer { get; }
    IDisposable AddObservableSetChangedHandler(Action<ObservableSetChangedEventArgs<T>> handler);
    void RemoveObservableSetChangedHandler(Action<ObservableSetChangedEventArgs<T>> handler);
}

public interface IObservableSet<T> : IReadOnlyObservableSet<T>, ISet<T>
{
    new int Count { get; }                      // redeclared: two inherited Counts are ambiguous
    new bool Contains(T item);                  // and so are seven set-algebra members - see below
    new bool SetEquals(IEnumerable<T> other);
    new bool IsSubsetOf(IEnumerable<T> other);
    new bool IsSupersetOf(IEnumerable<T> other);
    new bool IsProperSubsetOf(IEnumerable<T> other);
    new bool IsProperSupersetOf(IEnumerable<T> other);
    new bool Overlaps(IEnumerable<T> other);
    int ChangeBatchDepth { get; }
    int MaxCollectionBatchSize { get; set; }
    void BeginChangeBatch();
    void EndChangeBatch();
}

public static partial class Observable
{
    public static IObservableSet<T> CreateHashSet<T>() where T : notnull;
    public static IObservableSet<T> CreateHashSet<T>(IEqualityComparer<T>? comparer) where T : notnull;
    public static IObservableSet<T> CreateHashSet<T>(params ReadOnlySpan<T> items) where T : notnull;
    public static IObservableSet<T> CreateHashSet<T>(IEqualityComparer<T>? comparer, params ReadOnlySpan<T> items) where T : notnull;
}
```

The concrete type is `ObservableHashSet<T>` and the factory is `CreateHashSet`, matching
`ObservableList`/`CreateList`: the interface names the shape and the method names the implementation.

**Eight redeclarations, and they are a language requirement rather than style.** `IReadOnlySet<T>` and
`ISet<T>` each declare the same eight members - `Count`, `Contains`, `SetEquals`, `IsSubsetOf`,
`IsSupersetOf`, `IsProperSubsetOf`, `IsProperSupersetOf`, `Overlaps` - so an interface inheriting both
would make every one of those calls ambiguous through it. The list needed two of these (`Count` and the
indexer) for the same reason; a set needs eight.

### 6.4 What each mutation raises

| Call | Record | Nothing raised when |
| --- | --- | --- |
| `Add(item)` | `ItemAdded` with the item | the comparer says it is already there, so `Add` returns `false` |
| `Remove(item)` | `ItemRemoved` with the item | the item is not there, so `Remove` returns `false` |
| `Clear()` | `SetCleared` | the set is already empty |
| `UnionWith(other)` | one `ItemAdded` per item taken, in `other`'s order | nothing was taken |
| `ExceptWith(other)` | one `ItemRemoved` per item removed | nothing was removed |
| `IntersectWith(other)` | one `ItemRemoved` per item removed | nothing was removed |
| `SymmetricExceptWith(other)` | `ItemRemoved` for what was there, then `ItemAdded` for what was not | nothing differs |

`SymmetricExceptWith` reports removals before additions so that a replay never has to "add an item
that is already there": an observer applying the records in order ends with the set it would have had.

Which record marks what:

| Record | count | contents | entries marked |
| --- | --- | --- | --- |
| `ItemAdded` with `x` | yes | yes | `x` |
| `ItemRemoved` with `x` | yes | yes | `x` |
| `SetCleared` | yes | yes | every watched entry |

### 6.5 The mapped view

`ObservableMappedReadOnlySetView<TInner, TOuter>` is a read-only view over another observable set that
projects every item through a function - the set's `Select`, the counterpart of 5.4, and the first view
whose translation is not record for record:

```csharp
namespace Kolvir.Feature.Observables;

public sealed class ObservableMappedReadOnlySetView<TInner, TOuter>
    : IReadOnlyObservableSet<TOuter>, IObservableChangeTarget<ObservableSetChange<TOuter>, ...>
    where TInner : notnull
    where TOuter : notnull
{
    public ObservableMappedReadOnlySetView(
        IReadOnlyObservableSet<TInner> inner,
        Func<TInner, TOuter> selector,
        IEqualityComparer<TOuter>? comparer = null);

    public bool IsDisposed { get; }
    public IEqualityComparer<TOuter> Comparer { get; }
    public int Count { get; }
    public bool Contains(TOuter item);
    public void CopyTo(TOuter[] array, int arrayIndex);
    public IEnumerator<TOuter> GetEnumerator();
    public bool SetEquals(IEnumerable<TOuter> other);
    public bool IsSubsetOf(IEnumerable<TOuter> other);
    public bool IsSupersetOf(IEnumerable<TOuter> other);
    public bool IsProperSubsetOf(IEnumerable<TOuter> other);
    public bool IsProperSupersetOf(IEnumerable<TOuter> other);
    public bool Overlaps(IEnumerable<TOuter> other);
    public IDisposable AddObservableSetChangedHandler(Action<ObservableSetChangedEventArgs<TOuter>> handler);
    public void RemoveObservableSetChangedHandler(Action<ObservableSetChangedEventArgs<TOuter>> handler);
    public void Dispose();
}
```

The comparer is a constructor parameter rather than a detail, because a set's identity is what its
comparer says it is (6.2.1): it decides that two mapped items are one item here, it is what
`ObservableSetDependencies<TOuter>` keys its entries by, and it is what a consumer builds its mirror with.
It defaults to `EqualityComparer<TOuter>.Default` and is exposed as `Comparer`. The write surface,
`ChangeBatchDepth` and `MaxCollectionBatchSize` are absent for the reasons 5.4 gives, and so are a
factory and `Refresh`/`Remap`.

**The projection is not injective, so the translation is not record for record.** Two inner items the
source's comparer calls different may map to outers this view's comparer calls equal. The view therefore
counts: a `HashSet<TOuter>` holds the distinct mapped items and a `Dictionary<TOuter, int>` holds how many
inner items currently map to each, both built with `Comparer`. The set is what makes the set-algebra scans
- `SetEquals`, `IsSubsetOf`, `IsSupersetOf`, `IsProperSubsetOf`, `IsProperSupersetOf` and `Overlaps` -
correct: they delegate to its own implementations, exactly as `ObservableHashSet` delegates to its
storage, so their duplicate and foreign-comparer rules are the BCL's and not a reimplementation. The
dictionary is the multiplicity ledger. The two are only ever touched through one private helper pair
(`AddMapped`, `RemoveMapped`) plus the clear, so they cannot drift. There is no order here either: the
view enumerates in the storage's order, which is the `HashSet`'s and carries no meaning - as 6.2.3 says
of the set itself - so it must not be read as the source's.

A single `Dictionary<TOuter, int>` was considered and rejected: it leaves the six scans to be
hand-rolled, including `SetEquals`'s collapse of duplicates in its argument and the proper-subset
question's answer for sets that are equal. A `Dictionary<TOuter, HashSet<TInner>>` of buckets was
considered too: it answers the count, but it still cannot answer the scans without the outer set, and it
keeps every inner item alive for the view's lifetime. No single structure was found that keeps the scans
correct without either a second structure or the BCL's code copied out.

The translation is therefore *at most* one record per source record, often none:

| Source record | The mapped set | The record the view delivers |
| --- | --- | --- |
| `ItemAdded` with `x` | `selector(x)`, counted | `ItemAdded` with it **only when it was the first inner item for that mapped item**; nothing when the mapped item was already present |
| `ItemRemoved` with `x` | `selector(x)`, uncounted - the record names an inner item and the view is keyed by outer, so the function has to run to find the bucket | `ItemRemoved` with the stored outer **only when it was the last inner item**; nothing while another inner item still maps to it; a mapping that finds no bucket at all resyncs and records `UntrackedChanges` |
| `SetCleared` | emptied | `SetCleared`, carrying nothing, **only when the view was non-empty**; nothing for a view already empty |
| `UntrackedChanges` | rebuilt from the source | `UntrackedChanges` |

The call count is a claim a test asserts rather than an implementation detail: once per source item at
construction, once per add, once per removal, never for a clear, once per source item on a resync. A
removal maps again because there is no cached inner-to-outer map to read, but the instance it reports is
the one the view already held, obtained from the storage - exactly as `ObservableHashSet.Remove` reports
the stored instance rather than the argument.

**A mapping function that is not deterministic fails visibly.** The removal row above maps again to find
its bucket; if that mapping produces an outer the view never took in - two calls for one item gave
different outers - the storage and the source disagree, and the view cannot describe what happened. It
answers exactly as it does for an unknown record: rebuild from the source and deliver one
`UntrackedChanges`, so every reader is told to re-read and the view heals rather than drifting silently.
If the batch already had records pending, the admission is appended to them and the consumer applies in
order and re-reads at the untracked record.

**A change that leaves the view unchanged raises no event at all.** A collapsing add only increments a
count; a non-last removal only decrements one; a clear of an empty view does nothing. The view records
only what it actually changed, and `ObservableChangeBatch` does not drain a batch with nothing pending,
so there is no event, no dependency mark and no wake-up. That is the same answer the set itself gives for
an add its comparer calls equal (6.4), and it is not a defect to be papered over with an empty delivery.
An unknown change type falls into the untracked row rather than throwing, as in 5.4.

**One source delivery is one view delivery**, with the records in the source's order, and the view's batch
is its own at the package default cap, exactly as 5.4 says of the list view's. A source left at the
default cannot deliver more view records in one event than the view will describe, so that event arrives
intact, exactly the cap included, because the check is strictly greater. A source whose owner raised its
cap can: the view's batch then drops what it collected and records one `UntrackedChanges`, while the
contents are already complete - every record was applied before it was recorded - so the count, the
contents and every watched item are marked even though the description is gone.

**Reads publish the same three questions** (5.1, 6.2.4) from the view's own dependency table: `Count` is
the count, `Contains(x)` is the entry for `x` keyed with `Comparer`, and enumeration, `CopyTo` and the six
scans are the contents. `IsDisposed` and `Comparer` publish nothing. Which of the view's own records marks
what:

| Record | count | contents | entries marked |
| --- | --- | --- | --- |
| `ItemAdded` with `x` | yes | yes | `x` |
| `ItemRemoved` with `x` | yes | yes | `x` |
| `SetCleared` | yes | yes | every watched entry |
| `UntrackedChanges` | yes | yes | every watched entry |

A source change that produced no record marks nothing, which is why a collapsing add wakes nobody.

**The source is not owned**, and everything else follows 5.4 and 3.9: `Dispose` unsubscribes, drops what
had not been delivered, delivers one last empty event and lets its handlers go, and a source disposed
first leaves the view readable with its last contents. The selector runs on the thread that changed the
source; it has to be deterministic - two calls for one item have to produce outers `Comparer` calls equal,
or the counts stop describing the view - must not throw and must not change either collection.
Single-threaded, and not an `IObservableEventSource` for the reason 3.8 and `IReadOnlyObservableSet<T>`
give.

**What is not here.** A filtered or sorted view of a set is not designed: membership is the whole of a
set's record, so a filter is a different membership rule rather than a translation, and an ordered variant
is 8.11. This section has no counterpart to 5.3's "Still undesigned" list; this paragraph is where its
boundary is recorded.

### 6.6 Where it differs from the list on purpose

- **No addresses to shift.** A list's insert marks a suffix; a set's change marks one item. That is the
  whole of the per-collection work, and it is why a set of ten thousand tags with twelve toggles
  rendered marks twelve entries at most.
- **A bulk operation is N records and one event**, where a list would need a batch from the caller. The
  set has no `AddRange`-shaped alternative to offer, so the batching is not optional.

## 7. The observable dictionary

The keyed collection: identity is the key, the payload is the value, and there is no index. Its record
carries what an observer needs to replay it - which key, and what the value was and is.

Sections 3.1 to 3.5, 3.7, 3.9, 3.10 and 3.11 apply unchanged, as section 6 says of them.

### 7.1 The type model

```
ObservableDictionaryChangeType                 what a dictionary change can be
  ItemAdded, ItemRemoved, ItemChanged, DictionaryCleared, UntrackedChanges

ObservableDictionaryChange<TKey, TValue>       one change
  ObservableDictionaryChangeType ChangeType { get; }
  TKey? Key { get; }
  TValue? OldValue { get; }
  TValue? NewValue { get; }

ObservableDictionaryChangedEventArgs<TKey, TValue> : EventArgs
  IReadOnlyList<ObservableDictionaryChange<TKey, TValue>> Changes { get; }
  IReadOnlyObservableDictionary<TKey, TValue> Dictionary { get; }

IReadOnlyObservableDictionary<TKey, TValue>
    : IReadOnlyDictionary<TKey, TValue>, IReadOnlyObservableSet<KeyValuePair<TKey, TValue>>
  new IEqualityComparer<TKey> Comparer { get; }    // hides the set base's pair comparer; explicit there
  IDisposable AddObservableDictionaryChangedHandler(Action<ObservableDictionaryChangedEventArgs<TKey, TValue>> handler)
  void RemoveObservableDictionaryChangedHandler(Action<ObservableDictionaryChangedEventArgs<TKey, TValue>> handler)
  // IsDisposed, IDisposable and the set surface - Count, Contains, the six algebra questions,
  // Comparer and the two set handlers - are the set base's (section 7.7)

IObservableDictionary<TKey, TValue> : IReadOnlyObservableDictionary<TKey, TValue>, IDictionary<TKey, TValue>
  int ChangeBatchDepth { get; }
  int MaxCollectionBatchSize { get; set; }
  void BeginChangeBatch()
  void EndChangeBatch()
  bool TryAdd(TKey key, TValue value)
```

| ChangeType | Key | OldValue | NewValue | Raised by |
| --- | --- | --- | --- | --- |
| `ItemAdded` | the key | - | the value | `Add`, `TryAdd`, an indexer write to an absent key |
| `ItemRemoved` | the key | the value that was there | - | `Remove` |
| `ItemChanged` | the key | what it was | what it is | an indexer write to a present key, when the value differs |
| `DictionaryCleared` | - | - | - | `Clear` of a non-empty dictionary |

`ItemAdded` and `ItemRemoved` fill one side each and `ItemChanged` fills both, which is 3.1's rule
again: a handler switches on `ChangeType` and reads the fields that type promises. `DictionaryCleared`
carries nothing, for 3.1's reason - the observer that cared about the contents had them.

`TryAdd` is on the interface because its absence is the one most felt: `IDictionary<,>` offers `Add`,
which throws on a present key, and the indexer, which replaces. "Add if absent" would otherwise be a
lookup, a decision and an exception to catch. It reports `ItemAdded` when it adds and nothing when the
key was there.

### 7.2 Decisions and why

#### 7.2.1 The key is the address, and the key comparer is the identity

`Comparer` is on the read surface for 6.2.1's reason one collection over: two keys the comparer calls
equal are the same key, and a consumer mirroring the dictionary has to agree. The per-key dependency
entries are keyed with it, in a table the dictionary owns. That is the *dictionary's* identity and the
member a caller of the interface sees; the set view's identity is the pair and is a second comparer,
reachable as the set base's `Comparer` (section 7.7).

#### 7.2.2 `ItemChanged` only when the value differs

An indexer write to a present key whose value `EqualityComparer<TValue>.Default` says is equal raises
nothing - the rule a value's write (3.5) and a list's item write already follow. An optional value
comparer is not in the first version (open question 8.10).

#### 7.2.3 `Remove(KeyValuePair)` is conditional, as its interface says

`ICollection<KeyValuePair<K, V>>.Remove` removes only when the key is present *and* its value matches
the pair's, and returns `false` otherwise, which raises nothing. `Remove(key)` removes whatever the
value is and reports it in the record.

#### 7.2.4 Reads: the structure, the key, the contents

| Read | Depends on | Raised when |
| --- | --- | --- |
| `Count`, `Keys` | structure | a key is added, a key is removed, or the dictionary is cleared |
| `this[k]`, `TryGetValue`, `ContainsKey` | the entry for `k` | `k` is added, is removed, or its value changes |
| `Values`, enumeration, `Contains(KeyValuePair)`, `CopyTo` | contents | any change at all |

Two notes on that table:

- **`ContainsKey` is over-raised, deliberately.** A value change does not change whether the key is
  there, so a strict reading gives presence and value a dependency each. It does not, because the
  entry is one thing to everyone who holds it: a `TryGetValue` caller cares about both halves, a
  `ContainsKey` caller is rare, and two entries per key would double the watched set to serve a
  distinction almost nobody makes. The over-raise costs a re-run, never a wrong answer.
- **`Keys` is structure and `Values` is contents**, because a key's identity cannot change without the
  key being a different key, while a value changes every time someone edits one. Reading the property
  publishes for the collection the property is a view of; enumerating what it returned afterwards is
  the reader's own read, and those are live views with `Dictionary<,>` semantics, so enumerating one
  during a mutation throws exactly as it does today.

### 7.3 API surface, in full

```csharp
public enum ObservableDictionaryChangeType { ItemAdded, ItemRemoved, ItemChanged, DictionaryCleared, UntrackedChanges }

public readonly struct ObservableDictionaryChange<TKey, TValue>
{
    public ObservableDictionaryChangeType ChangeType { get; }
    public TKey? Key { get; }                   // default for a record that names no key
    public TValue? OldValue { get; }
    public TValue? NewValue { get; }
}

public sealed class ObservableDictionaryChangedEventArgs<TKey, TValue> : EventArgs
{
    public IReadOnlyList<ObservableDictionaryChange<TKey, TValue>> Changes { get; }
    public IReadOnlyObservableDictionary<TKey, TValue> Dictionary { get; }
}

public interface IReadOnlyObservableDictionary<TKey, TValue>
    : IReadOnlyDictionary<TKey, TValue>, IReadOnlyObservableSet<KeyValuePair<TKey, TValue>>
{
    new IEqualityComparer<TKey> Comparer { get; }   // the set base's pair comparer is explicit (7.7)
    IDisposable AddObservableDictionaryChangedHandler(Action<ObservableDictionaryChangedEventArgs<TKey, TValue>> handler);
    void RemoveObservableDictionaryChangedHandler(Action<ObservableDictionaryChangedEventArgs<TKey, TValue>> handler);
    // Inherited from the set base: IsDisposed, the pair Comparer, Count, Contains,
    // the six algebra questions, and Add/RemoveObservableSetChangedHandler.
}

public interface IObservableDictionary<TKey, TValue>
    : IReadOnlyObservableDictionary<TKey, TValue>, IDictionary<TKey, TValue>
{
    new int Count { get; }                      // redeclared: the two inherited shapes collide
    new TValue this[TKey key] { get; set; }     // get-only on one side, settable on the other
    new ICollection<TKey> Keys { get; }         // and spelled with two different types
    new ICollection<TValue> Values { get; }
    new bool ContainsKey(TKey key);
    new bool TryGetValue(TKey key, out TValue value);
    int ChangeBatchDepth { get; }
    int MaxCollectionBatchSize { get; set; }
    void BeginChangeBatch();
    void EndChangeBatch();
    bool TryAdd(TKey key, TValue value);
}

public static partial class Observable
{
    public static IObservableDictionary<TKey, TValue> CreateDictionary<TKey, TValue>() where TKey : notnull;
    public static IObservableDictionary<TKey, TValue> CreateDictionary<TKey, TValue>(IEqualityComparer<TKey>? comparer) where TKey : notnull;
    public static IObservableDictionary<TKey, TValue> CreateDictionary<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> items) where TKey : notnull;
    public static IObservableDictionary<TKey, TValue> CreateDictionary<TKey, TValue>(IEqualityComparer<TKey>? comparer, IEnumerable<KeyValuePair<TKey, TValue>> items) where TKey : notnull;
}
```

`TKey : notnull` is `IDictionary<TKey, TValue>`'s own constraint, so the factories carry it. The comparer
comes first in the four-argument overload, as it does in `CreateList` and `CreateHashSet` - corrected
after review, where this section had it second.

**Six redeclarations**, for the reason the list needed two and the set eight: a dictionary's two shapes
overlap the most. `Count`, `ContainsKey` and `TryGetValue` are the same member declared twice; the indexer
is get-only in one and settable in the other; and `Keys`/`Values` are `IEnumerable<T>` in the read-only
interface and `ICollection<T>` in the writable one. The implementation answers the narrower spelling
explicitly, because an interface implementation has to match the member's type exactly.

### 7.4 What each mutation raises

| Call | Record | Nothing raised when |
| --- | --- | --- |
| `Add(k, v)` | `ItemAdded` | `k` is present, which throws `ArgumentException` |
| `TryAdd(k, v)` | `ItemAdded` | `k` is present, and it returns `false` |
| `this[k] = v`, `k` absent | `ItemAdded` | - |
| `this[k] = v`, `k` present | `ItemChanged` with old and new | the new value equals the old |
| `Add(KeyValuePair)` | `ItemAdded` | `k` is present, which throws |
| `Remove(k)` | `ItemRemoved` with the value that was there | `k` is absent, and it returns `false` |
| `Remove(KeyValuePair)` | `ItemRemoved` | `k` is absent, or its value differs from the pair's |
| `Clear()` | `DictionaryCleared` | the dictionary is already empty |

An indexer read of an absent key throws `KeyNotFoundException` and publishes nothing, which is the one
place this design departs from 3.6's "publish before the check": a list's index is a position the
caller can always ask about, while a dictionary's indexer is the wrong shape of question about a key
that is gone. `TryGetValue` and `ContainsKey` are the right shapes, and both publish the key's entry
before answering, so a reader that asked about an absent key is woken by the key appearing.

Which record marks what:

| Record | structure | contents | entries marked |
| --- | --- | --- | --- |
| `ItemAdded` with `k` | yes | yes | `k` |
| `ItemRemoved` with `k` | yes | yes | `k` |
| `ItemChanged` with `k` | no | yes | `k` |
| `DictionaryCleared` | yes | yes | every watched key |
| `UntrackedChanges` | yes | yes | every watched key |

The set view hears the same events as set changes - an add, a removal, and a replacement as two records
in one event - and section 7.7 gives that mapping.

### 7.5 The mapped view

`ObservableMappedReadOnlyDictionaryView<TKey, TInner, TOuter>` is a read-only view over another
observable dictionary that projects every value through a function - the dictionary's `Select`, the
counterpart of 5.4 and 6.5:

```csharp
namespace Kolvir.Feature.Observables;

public sealed class ObservableMappedReadOnlyDictionaryView<TKey, TInner, TOuter>
    : IReadOnlyObservableDictionary<TKey, TOuter>, IObservableChangeTarget<ObservableDictionaryChange<TKey, TOuter>, ...>
    where TKey : notnull
{
    public ObservableMappedReadOnlyDictionaryView(
        IReadOnlyObservableDictionary<TKey, TInner> inner,
        Func<TInner, TOuter> selector);

    public bool IsDisposed { get; }
    public IEqualityComparer<TKey> Comparer { get; }             // the key comparer, as on any dictionary
    IEqualityComparer<KeyValuePair<TKey, TOuter>>                   // explicit: the set base's pair comparer
        IReadOnlyObservableSet<KeyValuePair<TKey, TOuter>>.Comparer { get; }
    public int Count { get; }
    public IEnumerable<TKey> Keys { get; }
    public IEnumerable<TOuter> Values { get; }
    public TOuter this[TKey key] { get; }
    public bool ContainsKey(TKey key);
    public bool TryGetValue(TKey key, out TOuter value);
    public bool Contains(KeyValuePair<TKey, TOuter> item);
    public void CopyTo(KeyValuePair<TKey, TOuter>[] array, int arrayIndex);
    public IEnumerator<KeyValuePair<TKey, TOuter>> GetEnumerator();
    public bool SetEquals(IEnumerable<KeyValuePair<TKey, TOuter>> other);
    public bool IsSubsetOf(IEnumerable<KeyValuePair<TKey, TOuter>> other);
    public bool IsSupersetOf(IEnumerable<KeyValuePair<TKey, TOuter>> other);
    public bool IsProperSubsetOf(IEnumerable<KeyValuePair<TKey, TOuter>> other);
    public bool IsProperSupersetOf(IEnumerable<KeyValuePair<TKey, TOuter>> other);
    public bool Overlaps(IEnumerable<KeyValuePair<TKey, TOuter>> other);
    public IDisposable AddObservableDictionaryChangedHandler(Action<ObservableDictionaryChangedEventArgs<TKey, TOuter>> handler);
    public void RemoveObservableDictionaryChangedHandler(Action<ObservableDictionaryChangedEventArgs<TKey, TOuter>> handler);
    public IDisposable AddObservableSetChangedHandler(Action<ObservableSetChangedEventArgs<KeyValuePair<TKey, TOuter>>> handler);
    public void RemoveObservableSetChangedHandler(Action<ObservableSetChangedEventArgs<KeyValuePair<TKey, TOuter>>> handler);
    public void Dispose();
}
```

`Contains` and `CopyTo` are not on `IReadOnlyDictionary<TKey, TValue>` - nor is the set view's
`CopyTo` on `IReadOnlySet<T>` - but they are kept for the reason 5.4 gives about the list view's
scans: a consumer handed any of the three views should find the same scans in each. Both publish the
contents dependency. `CopyTo` delegates to the storage, so the BCL's argument rules are the ones that
answer; `Contains` is the set view's `Contains`, so it asks the source's key comparer which entry an
item names and compares the mapped value with `EqualityComparer<TOuter>.Default` (section 7.7) -
the BCL's default key equality would disagree with a source built on another comparer. The write
surface, `ChangeBatchDepth`, `MaxCollectionBatchSize`, a factory and `Refresh`/`Remap` are absent for
the reasons 5.4 gives.

**Record for record, exactly as the list's view is.** A key is unique, so every entry's value has
exactly one mapped counterpart: nothing collapses, nothing merges, and no entry can arrive already
present under another identity. The set's projection is not injective and has to count; this one has
nothing to count. The whole cache is one `Dictionary<TKey, TOuter>`, keyed by the source key and built
with the source's comparer, and every source record becomes exactly one view record:

| Source record | The mapped dictionary | The record the view delivers |
| --- | --- | --- |
| `ItemAdded` with `k` | `selector(NewValue)` added at `k` | `ItemAdded` with `k` and the mapped value |
| `ItemRemoved` with `k` | the cached value for `k` dropped | `ItemRemoved` with `k` and the cached value |
| `ItemChanged` with `k` | `selector(NewValue)` replaces the cached value | `ItemChanged` with `k`, the cached old value and the mapped new one |
| `DictionaryCleared` | emptied | `DictionaryCleared`, carrying nothing |
| `UntrackedChanges` | rebuilt from the source | `UntrackedChanges` |

An unknown change type falls into the untracked row rather than throwing, as in 5.4 and 6.5.

**There is no `TOuter` comparer, and no comparer argument.** The key is the identity and the value is
not, so a dictionary may hold the same value under two keys and that is a normal dictionary. The view
is built with the source's key comparer and `Comparer` answers that same comparer, which is what
`IReadOnlyObservableDictionary<TKey, TValue>` requires and what a consumer building a mirror has to
use. An outer comparer for symmetry with 6.5 would make two keys one entry, which is not a projection
- it is a different collection. For the same reason `TOuter` is unconstrained: a value may be null.

**The call count is the caching claim.** The function runs once per source entry at construction, once
per add, once per change, never on a removal, never on a clear, and once per source entry on a resync.
A removal costs nothing because the record names the key and the view is keyed by the key: what came
out is the value the cache already held, not something to derive again. A test asserts that count,
because it is the difference between a projection that is cached and one that re-derives on every
event.

**A record that does not fit the cache heals it.** If a change or removal names a key the view does
not hold, or an add names one it already holds, the cache and the source disagree and the view cannot
describe what happened. It rebuilds from the source and delivers one `UntrackedChanges`, rather than
mapping the record's `OldValue` - which would invent a removal for an entry the view never had - or
dropping the record. The disagreement is visible and the view heals. The add-shaped branch is
unreachable while the source keeps its own contract, which throws before it records a second add for
one key; it exists so that a broken caller cannot crash a delivery.

A throwing selector is how a view is left behind, and it has two shapes, only one of which is
announced. The source applies and records its own mutation first, so the exception escapes the
source's delivery with the source ahead of the view. If the view had never taken the key in - the
throw was on an add - there is no entry for it, so the next record naming that key rebuilds from the
source and admits `UntrackedChanges`. If it had taken the key in - the throw was on a change - the
entry is still there with its previous mapped value, so the next record naming that key re-maps and
reports that stale value as the old one, and no untracked record is delivered: a mirror that is stale
in the same way stays correct, but the failure is silent until the value changes again or an
untracked event arrives. A well-behaved selector never reaches either shape.

**One source delivery is one view delivery**, with the records in the source's order. The view's batch
is its own, at the package default cap, and knows nothing about a source whose owner raised its own,
so the three cases are 5.4's and 6.5's: a source left at the default cannot out-describe the view, so
its event arrives intact, exactly the cap included because the check is strictly greater; a default
source that goes past its own cap has already collapsed at the source and is passed on as one
`UntrackedChanges`; and a source tuned above the default collapses here to one `UntrackedChanges`
while the cache is complete - every record was applied before it was recorded - so the count, the
contents and every watched key are marked even though the description is gone. Unlike the set's, the
exact-cap case needs no distinct-outer care, because every source record has exactly one translated
record.

**Reads publish the same three questions** (5.1, 7.2.4), from the view's own dependency table:

| Read | Depends on |
| --- | --- |
| `Count`, `Keys` | structure |
| `this[k]`, `TryGetValue`, `ContainsKey` | the entry for `k` |
| `Values`, enumeration, `Contains(pair)`, `CopyTo` | contents |

The indexer publishes only when the key is there and throws `KeyNotFoundException` otherwise, and
`TryGetValue` and `ContainsKey` publish before answering, both exactly as 7.4 says of the dictionary -
including the departure from "publish before the check". `IsDisposed` and `Comparer` publish nothing,
and `Keys` and `Values` are the storage's live views, so enumerating one during a mutation throws.
Which of the view's own records marks what is 7.4's table, because the records are the dictionary's:

| Record | structure | contents | entries marked |
| --- | --- | --- | --- |
| `ItemAdded` with `k` | yes | yes | `k` |
| `ItemRemoved` with `k` | yes | yes | `k` |
| `ItemChanged` with `k` | no | yes | `k` |
| `DictionaryCleared` | yes | yes | every watched key |
| `UntrackedChanges` | yes | yes | every watched key |

**The source is not owned**, and everything else follows 5.4 and 3.9: `Dispose` unsubscribes, drops
what had not been delivered, delivers one last empty event and lets its handlers go, and a source
disposed first leaves the view readable with its last contents. The selector runs on the thread that
changed the source, at most once per value that arrives or changes; it must not throw and must not
change either dictionary or the view, and a throw propagates out of the source's delivery and leaves
the view behind the source until a record naming that key heals it. Single-threaded, and not an
`IObservableEventSource` for the reason 3.8 gives.

**What is not here.** A filtered or sorted view of a dictionary is not designed: a dictionary's
records carry no order, and a filter over one is a different membership rule rather than a
translation, as 6.5 says of a set. There is no `Observable.MapDictionary` factory, for 5.4's reason.

### 7.6 What is left for a later pass

- A sorted dictionary, or one whose enumeration order is a promise: both need an order to report,
  which is `Move`-shaped (open question 8.11).
- A value comparer per dictionary (open question 8.10).
- The `Dictionary<,>`-only conveniences (`Remove(k, out v)`, `GetValueOrDefault`). None of them is a
  change, and all of them are one line over the interface.

### 7.7 The dictionary as a set of its pairs

`IReadOnlyObservableDictionary<TKey, TValue>` is an `IReadOnlyObservableSet<KeyValuePair<TKey, TValue>>`,
and only the read-only half is: `IObservableDictionary<TKey, TValue>` does not extend `IObservableSet`,
because a writable set surface spelled in pairs would be a second way to change the same dictionary, while
the owner already has `Add(k, v)`, `TryAdd(k, v)` and the indexer.

**The element is the pair.** Identity includes the value, so `{"a" to 1}` and `{"a" to 2}` are two
elements, `Contains(pair)` is true only when the key is present *and* its value equals the pair's, and a
pair whose key has since moved on is not in the set. Two dictionaries with the same keys and different
values are not `SetEquals`. Identity by key would have made the set view a set of keys with a payload,
which says nothing the dictionary does not already say; the owner chose pair identity for that reason.
The event's `Set` is the dictionary itself, which is the set view.

**Two comparers, reachable from different places.** `Comparer` on the dictionary interface stays the key
comparer (`IEqualityComparer<TKey>`) and is what a caller of that interface sees; the set base's
`Comparer` is the pair comparer (`IEqualityComparer<KeyValuePair<TKey, TValue>>`), hidden by a `new`
declaration on the dictionary interface and reachable only by casting to `IReadOnlyObservableSet<...>`.
The pair comparer compares the key with the dictionary's own comparer and the value with
`EqualityComparer<TValue>.Default`, and its `GetHashCode` combines both: a comparer that hashed only the
key would call two pairs equal and still file them in different buckets, and every set operation would be
silently wrong. Both implementations take the comparer and all six algebra answers from
`ObservableDictionaryPairSet<TKey, TValue>`, so there is one definition of an element rather than two.

**A replacement is two changes in one event.** A dictionary record carries the key, the old value and the
new value, so `ItemAdded`, `ItemRemoved`, `DictionaryCleared` and `UntrackedChanges` translate one for one
(`Add(pair(k, NewValue))`, `Remove(pair(k, OldValue))`, `Clear()`, `Untracked()`), and `ItemChanged`
becomes `Remove(pair(k, OldValue))` followed by `Add(pair(k, NewValue))`. Those two records are delivered
in one `ObservableSetChangedEventArgs`, in that order, because the replacement is atomic to an observer:
one that saw the removal without the add would hold a set the dictionary never held, and no intermediate
pair is observable because the dictionary's own mutation completed before the records were made.
The set stream is derived from the *delivered* dictionary event rather than recorded beside the mutation,
so it cannot describe a change the dictionary event did not, and a dictionary event that went untracked
becomes an untracked set event instead of being advertised anyway.
`ObservableMappedReadOnlyDictionaryView` translates its own mapped records through the same helper, so it
raises the same set changes as the dictionary it maps.

## 8. Open questions - confirm before code

1. **Not an `IObservableEventSource`** (3.8). The alternative is to derive both interfaces from it and
   adapt the untyped handlers onto the typed event. The argument against is in 3.8; the argument for
   is uniformity with the values, and that a generic "watch anything observable" helper could then
   hold a list.
2. **One record type with nullable fields** rather than one type per change (3.1). A type per change
   would make "which fields does this type have" a compile-time question and would need a common base
   or an interface for `Changes` to be a homogeneous list.
3. **No `INotifyCollectionChanged`** (3.8's table). A later adapter is possible in either direction;
   nothing in step 1 depends on it.
4. **`Move` on the interface** rather than as an extension or an implementation detail (3.4), because a
   move cannot be expressed as an `IList<T>` call without the observer seeing two records.
5. **No coalescing inside a batch** (3.10). The batch reports what happened, including changes that
   cancel out. Coalescing would make the event smaller and the contract harder: an observer would have
   to know it may receive a net change rather than a history.
6. **No `Sort` in step 1.** A sort could be reported as moves, but computing them is a diff, and the
   diff belongs with the derived views in section 5 rather than in the first version of the list.
7. **An index entry is dropped when its last watcher goes** (3.6.1), and a source that is watched
   again puts it back. Corrected after review: the earlier version said a released dependency set was
   "not something to register on again", which is a rule nothing enforces - `IObservableEventSource` is
   public and a run reports the sources it read, so a caller can hold one and register on it whenever it
   likes. Keeping an entry per index ever read instead would grow with the rows scrolled past rather
   than with the rows on screen, so the drop stays and the entry re-registers itself, adopting whichever
   entry took the index meanwhile.
8. **The convenience token.** `BeginChangeBatch`/`EndChangeBatch` are the primitives; an
   `IDisposable EnterChangeBatch()` that pairs them is the shape a caller actually wants
   (`using var batch = list.EnterChangeBatch();`), including on an exception path. It is in the plan
   unless the pairing-token argument is unwanted.
9. **A shared enum, or one per collection** (5.2). One `ObservableChangeType` for all nine change
   types would let a single handler serve any collection, at the price of every handler having to
   know which of the values it can be given are impossible. This document assumes one per collection.
10. **A value comparer for the dictionary** (7.2.2). `ItemChanged` is decided with
    `EqualityComparer<TValue>.Default` in the first version; a per-dictionary `IEqualityComparer<TValue>`
    is the obvious follow-up if a model ever needs it, and it is one more field.
11. **Sorted and ordered variants are out of scope** (5.3, 6.2.3, 7.6). An ordered collection has to
    report where an item went, which is the list's `Move` with a different mutation surface; none of
    the three here promises an order, and the dictionary's enumeration order is the storage's.
12. **A shared non-generic marker.** `IObservableCollection` - `IsDisposed`, `Count`,
    `ChangeBatchDepth`, and nothing else - would let a tool hold "some observable collection" without
    knowing its shape. Nothing in this plan needs it yet, which is the reason to leave it out rather
    than to design it.
13. **`Comparer` on the list.** The set and the dictionary expose it because it defines their
    identity (6.2.1, 7.2.1); the list's comparer only decides whether a write changed an item (3.5),
    so exposing it there is symmetry without a use.

## 9. Steps

| # | Step | Done when |
| --- | --- | --- |
| 1 | The two interfaces, `ObservableListChange<T>`, `ObservableListChangedEventArgs<T>` | All four compile, are documented, and nothing implements them yet - this is the reviewable shape of the contract |
| 2 | `ObservableList<T>` storage and mutations | Every mutation in 3.4 produces the record its row names and nothing else; an equal write, an empty clear, a same-index move and a failed remove produce no event; enumeration throws when the list is mutated under it |
| 3 | Delivery: queue and batches | Records arrive in mutation order even when a handler mutates; a batch is one event carrying its records up to the list's `MaxCollectionBatchSize` and one `UntrackedChanges` record past that, with nothing queued after it; a list's cap is its own and does not move another list's; nesting is counted and only the outermost end delivers; an unmatched end throws; a flat chain of handler-caused deliveries to the same list is unbounded, while a delivery re-entered from a handler - through a disposal, or through another list's drain - is counted against `MaxEventStackSize` and refused at the limit (3.7) |
| 4 | Per-operation dependencies: count, index, contents | A body that reads `Count` and `this[0]` reports two dependencies; a write to an item wakes the index reader and not the count reader; a move wakes only the indexes it spans; an insert at `p` wakes every watched index at or past `p` and none before it; a read outside a scope allocates nothing and registers nothing; a batch raises each affected entry once; a source that outlived its release is put back in the table when something watches it again, adopting any entry that took its index meanwhile |
| 5 | `CreateList` factories and disposal | The four overloads; disposal drops pending records, delivers the empty final event, and makes reads, mutations and registration throw while removal and a second dispose stay safe |
| 6 | Tests | Section 10's claims, in `tests/features/Kolvir.Feature.Observables.Tests` |
| 7 | The demo page and the guide section | An **Observable list** page in `src/demo/Kolvir.Demo.App/Pages/` that renders a list from its records and shows a batch add as one notification, plus an `### Observable collections` passage in `DEVELOPER-GUIDE.md` |
| 8 | The shared core: `ObservableChangeBatch<TChange>`, `ObservableKeyedDependencies<TKey>`, `ObservableShapeDependencies` | The list is rewritten onto the three shared types and its suite passes unchanged - which is the evidence that the core is the core, rather than the list plus two copies |
| 9 | The set: interfaces, `ObservableSetChange<T>`, event arguments | The four types compile and are documented (section 6.1, 6.3) |
| 10 | `ObservableHashSet<T>`: mutations, the four bulk operations, the three dependencies | Section 6.4's table row for row, and 6.2.4's dependencies exactly: a `Contains` reader is not woken by a change to another item, and a bulk operation that changes nothing raises nothing |
| 11 | The dictionary: interfaces, `ObservableDictionaryChange<TKey, TValue>`, event arguments | The four types compile and are documented (section 7.1, 7.3) |
| 12 | `ObservableDictionary<TKey, TValue>`: mutations, `TryAdd`, the three dependencies | Section 7.4's table row for row, and 7.2.4's dependencies exactly: a value write wakes the key's reader and not the structure reader, and an absent key read through `TryGetValue` is woken by the key appearing |
| 13 | The demo pages and the guide for the set and the dictionary | A set page whose toggles re-render one chip each, and a dictionary page whose rows re-render one row each, both applying records to a mirror |
| 14 | The two suites | Section 10's claims for both, beside the list's |

Step 1 is a contract review and is worth stopping at: everything after it is implementation of the
shape rather than of the idea. Step 4 is the one that decides how much of a list a change re-renders,
and it is worth arriving at with the test names already written. Steps 9 and 11 are the same kind of
stopping point for the other two, and neither is worth starting before step 8 exists - three copies of
a delivery queue would be three chances for one of them to disagree about ordering.

### 9.1 What else has to change

1. **`src/features/Kolvir.Feature.Observables/common/`** - one file per type, in the existing
   one-type-per-file arrangement, `common/` only: this is portable object-graph work over the BCL and
   needs no platform folder.
2. **The package description** in `Kolvir.Feature.Observables.csproj` says "values, expressions and
   named dependencies"; it gains collections when they ship.
3. **`dev-docs/OBSERVABLES.md`** section 7 has already stopped being "next step, not this one" and
   points here; its section 14 (progress) gains a row when the list ships.
4. **`dev-docs/DEVELOPER-GUIDE.md`**'s `### Observables` section gains the collections passage.
5. **`Kolvir.slnx`, the workflow and the test project** need nothing: the package and its suite
   already exist.

## 10. Verification approach

Headless tests in `tests/features/Kolvir.Feature.Observables.Tests`, in the shape the other
Observables tests already have - no window, no frame, no timing. The claims that get special
treatment:

| Claim | How it is proven |
| --- | --- |
| **Replay reproduces the list** | A listener that only ever applies records to its own list ends equal to the list - element for element - after a scripted sequence that inserts, appends, removes by index, removes by value, sets an item, moves forward and back, and clears, with a batch in the middle. The listener never reads the list |
| **Each type fills the fields it promises** | One assertion per row of 3.1's table: an add has no old index and no old item, a move has the item on both sides and both indexes, a clear has neither |
| **Nothing that changed nothing raises** | An equal item write, a clear of an empty list, a move from an index to itself, and a remove that found nothing each leave the handler uncalled, and each is asserted to leave the list unchanged too |
| **Order survives a handler that mutates** | A handler of the first event adds an item; the second handler records the events it saw, and the assertion is that it saw both events, in the order the changes happened, with the first one's records intact |
| **A batch is one event with all the records** | Three mutations inside a batch deliver once, with three records in order; a nested batch delivers once at the outermost end; an unmatched `EndChangeBatch` throws and leaves the list usable |
| **The guard counts nesting, not work** | A terminating cascade of two hundred handler-caused deliveries to the same list completes and the list holds what the handler built, with nothing refused and no records dropped. A chain that *does* nest is refused at `MaxEventStackSize`: a handler that changes another list (whose drain then runs in its frame), a handler that disposes a list (whose final event runs in its frame), and a render whose re-run changes the list - and after the refusal an ordinary change still delivers, so the depth was restored |
| **Disposal is ordered** | A handler sees `IsDisposed` true during the final event, receives no records, and any read from there throws; a batch left open is dropped rather than delivered; removal and a second dispose stay safe afterwards |
| **A read is exactly what it asked for** | A body that reads `Count`, `this[1]` and enumerates reports three dependencies, in that order. A write to item 1 wakes the reader of index 1 and not the reader of `Count`. A move wakes the reader of an index it spans and not one outside it. An insert at 2 wakes the readers of indexes 2 and above and none below. A clear wakes all three |
| **A throwing index read still subscribes** | `list[7]` over three items throws, and the run still reports index 7 among its dependencies; growing the list to eight then re-runs it, which is the only way an expression that is currently failing can recover |
| **A kept source is watched again, not stranded** | A run's dependency is kept past the disposal that released it and registered on directly: a change to that index wakes it. So does the harder order - the kept one registered on after a later read had made a second entry for the index - and the reader whose entry was adopted keeps hearing until its own run is disposed, after which the entry still raises the one that came back |
| **A batch is one wake-up per affected read** | A body reading indexes 0, 1 and 2 reports three dependencies, and three mutations delivered as one event wake each of them exactly once, however many records touched that index - three records that all touch index 1 raise it once, not three times |
| **The batch cap trades fidelity for a bounded event** | Fifty records in a batch arrive as fifty; the fifty-first mutation replaces them with one `UntrackedChanges` record, and no later change in that batch is queued - five hundred changes still arrive as one record. The same cap applies to an event accumulated during a drain. The list itself holds every change regardless, an `UntrackedChanges` event wakes the count, the contents and every watched index, and an observer's copy is rebuilt by re-reading - which is what the demo page shows happening |
| **Nothing watched costs nothing to raise** | A mutation with no run in scope and no handler registered marks nothing, and the same mutation costs the same with ten thousand items as with ten; a read outside a run allocates nothing at all |
| **A set's bulk operation is N records and one event** | `UnionWith` of three new items delivers once, with three `ItemAdded` records in the argument's order; `SymmetricExceptWith` of the set itself raises nothing at all; a bulk operation inside the caller's batch joins that batch instead of delivering an event of its own; and a replay of the records ends equal to the set |
| **A set's `Contains` is its own dependency** | A body asking `Contains("a")` and `Contains("b")` is woken once when `a` is added and not at all when `c` is; a `Count` reader is woken by the bulk operation that changed the size, once for the whole event; `SetEquals` and the subset tests are woken by any change, including one that alters no size (8.5: records are what happened, not a diff) |
| **The dictionary's key is its own address** | A body reading `dict["a"]` and `dict["b"]` is woken once per change to that key and not by a change to the other; `Count` and `Keys` readers are not woken by a value write; `TryGetValue` on an absent key reports a dependency and is woken when the key appears |
| **A removal reports the value that was there** | `Remove(k)` records the value it removed; `Remove(pair)` with a mismatched value raises nothing and leaves the entry alone; a `Clear` of a non-empty dictionary is one record with nothing in it |
| **A mapped view's records are the source's records translated** | Applying a view's records to a mirror of the mapped type ends equal to the view, after inserts, appends, removals, replacements, moves and a clear, and `args.List` is the view itself; a move leaves the selector's call count alone while a replacement adds exactly one, and a removal adds none; a batch on the source arrives as one delivery with the records in order, a batch at exactly the view's cap arrives with every record, and a source whose cap was raised above the default collapses to one untracked record with the mapped list still complete; an untracked source event rebuilds the view from the source and is delivered as one `UntrackedChanges` record; an index reader is not woken by a change to another index; disposing the view unsubscribes from the source and leaves it usable, while a disposed source leaves the view readable with its last contents |
| **A mapped set view's records are the source's records translated, with duplicates collapsed** | A mirror built only from the view's records ends equal to the view after an add that introduces a mapped item, adds that collapse, partial removals, a last removal, a union, a symmetric difference and a clear - and `args.Set` is the view itself; a collapsing add and a removal that leaves another inner item on the same mapped item each raise nothing, while the last removal names the instance the view already held and a removal whose mapping finds nothing the view holds resyncs and ends the event with `UntrackedChanges`; a batch on the source arrives as one delivery in order, a source event at exactly the view's cap arrives with every record, a source left at the default that goes past its own cap collapses there and is passed on as one untracked record with the view rebuilt, and a source tuned above the default collapses here to one untracked record with the view complete; an untracked event wakes the count, the contents and every watched item; `Count` is not woken by a change that leaves the size alone, and a `Contains` read through `IReadOnlyObservableSet<TOuter>` is woken by exactly its own item; disposing the view unsubscribes from the source and leaves it usable, while a disposed source leaves the view readable with its last contents |
| **A mapped dictionary view's records are the source's records translated** | A mirror built only from the view's records ends equal to the view after adds, changes, removals and a clear, and `args.Dictionary` is the view itself; an add and a change map exactly once while a removal and a clear map none, and a removal names the value the view already held; a throwing selector leaves the view behind the source, and the shape of that depends on whether the view had taken the key in: a key it never took in leaves no entry, so the next record naming that key rebuilds the view and ends the event with `UntrackedChanges`, while a key it had taken in keeps its stale mapped value, and the next change delivers one `ItemChanged` naming that stale value as the old one with no untracked record; a batch on the source arrives as one delivery in order, a source event at exactly the view's cap arrives with every record, a source left at the default that goes past its own cap collapses there and is passed on as one untracked record with the view rebuilt, and a source tuned above the default collapses here with the contents intact; `Count` and `Keys` are the structure and are not woken by a value change, a `TryGetValue` read is woken by exactly its own key, and `Values`, enumeration, `Contains(pair)` and `CopyTo` are the contents; the indexer of an absent key throws and publishes nothing while `TryGetValue` of one is woken when the key appears; disposing the view unsubscribes from the source and leaves it usable, while a disposed source leaves the view readable with its last contents |
| **Enumeration is a snapshot of the version, not of the contents** | Mutating during `foreach` throws `InvalidOperationException`; reading after it throws is fine; a `CopyTo` during a mutation is the same failure rather than a torn array |

The demo pages are the other half: each applies records to a mirror and asserts equality against the
model's own collection on every frame, which is the fidelity claim proved by the consumer rather than
by the producer.

## 11. Progress

| Step | State | Notes |
| --- | --- | --- |
| 0 | Done | This document: the contract, the decisions, the plan, and the open questions. Revised after review: 3.6 specifies a dependency per kind of read - count, index, contents - instead of one per list, which also settles open question 7. Extended after review: sections 6 and 7 are the set and the dictionary, and section 5 is the core they share |
| 1 | Done | The two interfaces, `ObservableListChangeType`, `ObservableListChange<T>` with its five `Create*` factories, and `ObservableListChangedEventArgs<T>` - all documented, CS1591 clean, and pointing at the sections that decide them |
| 2 | Done | `ObservableList<T>` over a `List<T>`: every mutation in 3.4 raises the record its row names and nothing else; an equal write, an empty clear, a same-index move and a failed remove raise nothing; an out-of-range mutation throws without recording; enumeration is the storage's own, so mutating under it throws |
| 3 | Done | The batch cap and `UntrackedChanges` were added after review, and they are the one place fidelity is bounded on purpose (3.11): past the list's `MaxCollectionBatchSize` a batch drops what it collected, delivers the one record that means "re-read the list", and queues nothing more. Delivery is a queue rather than a recursive raise: a handler's own change is delivered after the event it is hearing, a batch is one event with every record in order, nesting delivers once at the outermost end, and an unmatched end throws. Nothing bounds the drain - revised after review: an earlier version counted deliveries against `MaxEventStackSize` in the loop, which is a stack-depth guard and has nothing to say about a flat loop; the dispatch itself *is* counted, because a handler can re-enter a delivery through a disposal or through another list. Disposal drops what a batch had not delivered, delivers one empty event, and then throws. Revised a second time after review: the cap is not a package constant but a property of each list, `MaxCollectionBatchSize`, copied from `Observable.Default.MaxCollectionBatchSize` when the list is created, so an owner tunes its own list's observers and no other list's |
| 4 | Done | `ObservableListDependencies<T>` and `ObservableDependency`: the count and contents are fields, an index entry is created by a read inside a run and dropped when its last handler goes (`ReadScope.IsActive` keeps a cold read from allocating), and marking is a pass over the watched entries. A write wakes its index and not the count; an insert wakes the indexes at or past it; a move wakes its span; a clear wakes all; a batch raises each once; a read past the end is still a read. Revised after review: the entry is told when it goes from unwatched to watched as well as the other way, so a source a caller kept is put back in the table when it is registered on again - and one that finds its index held by a newer entry adopts it, because both have readers and only one entry can be marked |
| 5 | Done | The four `CreateList` overloads and the disposal order |
| 6 | Done | 51 tests in three files - `ObservableListChangeTests`, `ObservableListDeliveryTests`, `ObservableListDependencyTests` - covering section 10's claims for the list, including a replay that reproduces the list from records alone, and the cap being each list's own |
| 7 | Done | An **Observable list** page in the sample (`ObservableListPage`) - a line that reads only `Count`, a rows component, one child component per row reading a single index, and a copy built only from the records that says whether it still matches - plus an `### Observable collections` section in the developer guide. Rendering it headlessly (`--render ... --page observable-list`) is what caught the two things a first frame shows and a test does not: a child component with no `DataModel` renders the empty fragment, and a slot whose child the list removed keeps a disposed instance unless the parent prunes it |
| 8 | Done | The shared core, after the list was behaving: `ObservableChangeBatch<TChange, TArgs>` (the pending records, the batch depth, the capped event, the drain loop and the disposal), `ObservableShapeDependencies` (count and contents) and `ObservableKeyedDependencies<TKey>` (an entry per address, with the re-registration and adoption from step 4), behind the internal `IObservableChangeTarget<TChange, TArgs>`. The list was rewritten onto them and its 129 tests passed unchanged, which is the evidence the core is the core rather than the list plus two copies; what is left in the list is its items, its comparer, and which record invalidates which answer |
| 9-10 | Done | `ObservableSetChangeType` (with `UntrackedChanges`), `ObservableSetChange<T>`, `ObservableSetChangedEventArgs<T>`, the two interfaces with their eight redeclarations, `ObservableSetDependencies<T>`, `ObservableHashSet<T>` and the four `CreateHashSet` overloads. 35 tests in three files cover section 10's claims for the set: one record per item with the bulk operations implicitly batched, removals before additions in a symmetric difference, the cap, disposal, and `Contains` as its own per-item dependency keyed by the set's comparer |
| 11-12 | Done | `ObservableDictionaryChangeType` (with `UntrackedChanges`), `ObservableDictionaryChange<TKey, TValue>` with the old and new value, `ObservableDictionaryChangedEventArgs<TKey, TValue>`, the two interfaces with their six redeclarations and `TryAdd`, `ObservableDictionaryDependencies<TKey, TValue>`, `ObservableDictionary<TKey, TValue>` and the four `CreateDictionary` overloads. 34 tests cover section 10's claims: the record table row for row - `TryAdd`, the conditional pair removal and the indexer's add-or-change included - a replay, the cap, disposal, and the three reads, where an `ItemChanged` wakes the key and the contents and leaves the structure alone |
| 13-14 | Done | Both pages, with the guide passages beside them. The set page renders a chip per tag from a constant list, so the board itself reads nothing and renders once; a chip asks `Contains(its own tag)` and nothing else, so a toggle moves one chip and leaves the others alone, while the line above it reads `Count` and moves with every change of size. Beside those two sits the third read: a contents line that enumerates the set, which is why it re-renders on every change and why an item no chip asks about - the sixty a bulk add brings - is visible at all; an earlier version showed only the five constant chips, so a bulk add moved the count with nothing on the page to account for it. Both pages also group their buttons into one row per kind of change, because a single row of seven docked left clipped instead of wrapping once the window was narrower. The dictionary page puts the three reads side by side: a line on `Count`, a key list on `Keys`, and a row per key on `TryGetValue`, so editing a value moves one row and a key arriving moves the list. Both keep a mirror under the buttons that is built only from the records and says whether it still matches, which is what makes `UntrackedChanges` visible rather than theoretical: `Add sixty at once` is one batch past `MaxCollectionBatchSize` (the set's own `UnionWith` opens the batch; the dictionary page opens one), and the mirror is rebuilt from the collection rather than from the record. Rendering both headlessly (`--page observable-set`, `--page observable-dictionary`) caught nothing this time, so the two composition tests were added to the Virtual DOM suite instead: a chip woken by its own membership question and by nothing else, and a row woken by its own key while its parent wakes for a key arriving or leaving - the arrangement the pages stand on, which no dependency test in the observables suite can prove. Suites: 198 in the observables suite - the list's 129 unchanged through the extraction of the shared core, which is the evidence that the core is the core, then 35 for the set and 34 for the dictionary - and 86 in the Virtual DOM suite, which is the list's composition test beside these two |
| 15 | Done | `ObservableMappedReadOnlyListView<TInner, TOuter>` (5.4), the first derived view: a `public sealed` class over `IReadOnlyObservableList<TOuter>` and `IObservableChangeTarget`, on the shared core unchanged - `ObservableChangeBatch` wired to itself for the queue, the cap and the disposal, `ObservableListDependencies` for the same three questions, and the existing `ObservableListChange` factories for the record - with a parallel cached `List<TOuter>` that maps an add and a replacement once each and never maps a move or a removal. One source delivery is one view delivery, an untracked source event rebuilds the cache, the source is never disposed, and a source disposed first leaves the view readable with its last contents. 22 tests in `ObservableMappedReadOnlyListViewTests`: the cache against every record kind, the call count across move, change and removal, one delivery per source batch and the cap's three cases (a source event at exactly the default arrives with every record, one above the default is the source collapsing and being passed on, and a source tuned above the default is the view collapsing with the contents intact), the three dependencies, unsubscribe on disposal, the full disposed contract, and the null arguments. Suites: 220 in the observables suite - the 198 from step 13-14 unchanged and 22 new - with the core and every other feature suite untouched |
| 16 | Done | `ObservableMappedReadOnlySetView<TInner, TOuter>` (6.5), the set's projection and the first view whose translation is not record for record: a set projection is not injective, so the view counts - a `HashSet<TOuter>` of distinct mapped items beside a `Dictionary<TOuter, int>` of multiplicities, both on the view's comparer and both touched only through one private pair of helpers - and emits `ItemAdded` only on the first inner item for a mapped item, `ItemRemoved` with the stored instance only on the last, `SetCleared` only for a view that was not empty, and `UntrackedChanges` after a rebuild. A source change that leaves the view unchanged raises no event at all. On the shared core unchanged: `ObservableChangeBatch` wired to itself, `ObservableSetDependencies<TOuter>` for the count, the item and the contents, the existing `ObservableSetChange` factories for the records, and the six scans delegated to the storage. 25 tests in `ObservableMappedReadOnlySetViewTests`: the counted cache against every record kind, the collapsing add and partial removal that raise nothing, the stored instance in the last removal, an impure selector whose removal resyncs with the admission that ends the event, one delivery per source batch and the cap's three cases (a source event at exactly the default arrives with every record, a source left at the default that goes past it collapses at the source and is passed on as one untracked record, and a source tuned above the default collapses here with the contents intact), a replay into a mirror, the three dependencies, the comparer's default, unsubscribe on disposal, the full disposed contract, and the null arguments. Suites: 245 in the observables suite - the 220 from step 15 unchanged and 25 new - with the core and every other feature suite untouched |
| 17 | Done | `ObservableMappedReadOnlyDictionaryView<TKey, TInner, TOuter>` (7.5), the dictionary's projection and the second record-for-record view: one cached `Dictionary<TKey, TOuter>` keyed with the source's own comparer, an add and a change each mapping exactly once, a removal and a clear mapping nothing because the record names the key and the view is keyed by it, and `UntrackedChanges` after a rebuild. No outer comparer and an unconstrained `TOuter`, because the key is the identity and the value is not, so equal and null values may sit under different keys. A record that does not fit the cache - a change or removal for a key not held, or an add for one held - rebuilds and admits it, which is the documented recovery a throwing selector reaches. `Contains` and `CopyTo` are kept for parity with the other two views and publish the contents. On the shared core unchanged: `ObservableChangeBatch` wired to itself, `ObservableDictionaryDependencies<TKey, TOuter>` for the structure, the key and the contents, and the existing `ObservableDictionaryChange` factories for the records. 31 tests in `ObservableMappedReadOnlyDictionaryViewTests`: the cached translation against every record kind with its call counts, both shapes of the throwing-selector failure - a key never taken in, which resyncs and admits it, and a held key whose stale value the next change reports, one delivery per source batch and the cap's three cases (a source event at exactly the default arrives with every record, one above the default is the source collapsing and being passed on, and a source tuned above the default collapses here with the contents intact), a replay into a mirror, the three dependencies, equal and null values under different keys, the source's key comparer, `Contains` and `CopyTo`, unsubscribe on disposal, the full disposed contract, and the null arguments. Suites: 276 in the observables suite - the 245 from step 16 unchanged and 31 new - with the core and every other feature suite untouched |
