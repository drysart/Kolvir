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

- **Full fidelity.** An observer that began from the same contents and receives every event can
  reconstruct the collection exactly, without ever reading it. That is a contract, not an aspiration: every
  change is reported, indexes are positions as of that moment, and delivery order is the order the
  changes happened in.
- **Granularity enough to do minimal work.** A move is one record. Setting an item is one record
  with the old and the new item. An insert is one record with an index. Nothing is reported as
  "everything changed" except an actual `Clear` - in any of the three collections.
- **Batches for the caller's sake, not the observer's.** `BeginChangeBatch`/`EndChangeBatch` turn N
  mutations into one notification while keeping every record, in order. The observer is called once
  instead of N times and loses nothing.
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
  no items (3.1, and `SetCleared` and `DictionaryCleared` the same way), so an undo history is a`r`n  different object that records its own state.
- `INotifyCollectionChanged`, `ObservableCollection<T>`, `IBindingList`, or any WPF/XAML interop.
  Section 3.8 has the mapping, which is close but not the same contract.
- Sorting as a list operation. `IList<T>` has none, and a sort reported as moves is a diff problem
  rather than a mutation, which is open question 8.6.
- Coalescing or compacting records inside a batch (dropping an add that was removed again, merging
  two sets of one item). The batch keeps what happened. 8.5 records the question.
- Filtered, sorted or projected views over a list (5.3), and ordered or sorted variants of any of the
  three (open question 8.11). Both need the record format to settle first.

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
each against the state left by the one before it, and the result equals the list.* Nothing in the
contract allows an observer to reorder, and nothing requires it to understand the list.

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
- Order within that queue is the order of the mutations, so the last handler of a generation and the
  first handler of the next see a consistent history.
- Every delivery in that run is a whole event with its own records, so a consumer that only wants to
  count changes still sees each one, and a consumer that applies records never has to look ahead.

The depth guard still applies to the queue, in generations rather than stack frames (3.7).

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
| index `i` | an entry in the list's own dictionary, keyed by the index | the first read of `i` inside a run | when its last handler goes, like a named key (`OBSERVABLES.md` section 3.2) |

The index entry is the same instance for as long as anything watches it, which is what makes two
reads of `this[3]` in one run one subscription: the scope de-duplicates by equality and the instance
is canonical. It is a plain reference-equality source, so it needs no `Equals` of its own.

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
other, so a dependency whose re-run mutates the list again descends one level. The loop that does not
descend - a handler adding an item for every item added - is what 3.7's generation count bounds.

### 3.7 Delivery goes through the depth guard, in generations

`OBSERVABLES.md` section 3.8 bounds recursion with `Observable.MaxEventStackSize`, and a list needs
the same protection for the case it actually has: a handler that mutates the list it is hearing
about. Because 3.3 queues instead of recursing, the stack does not grow - but the queue does, and a
handler that adds one item for every item added is an infinite loop that no stack overflows. So the
drain counts **generations**: a mutation made during a drain is one generation deeper than the drain
it interrupted, and a chain that reaches `Observable.MaxEventStackSize` generations is refused with
the same exception, naming the list, the step and the limit. The refusal is sticky until the
outermost delivery returns, exactly as it is for raises.

So "a loop is refused" holds for lists without inverting the ordering guarantee, and the existing
number keeps its meaning: about fifty is far above any legitimate cascade of view updates, since each
generation is a whole handler-caused change.

The cost is one `int` on each drain, which is the shape the evaluation depth already has.

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
2. Any pending records - a batch that was never ended, or a queued generation - are **dropped**. They
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
- A batch allocates one `List<ObservableListChange<T>>` when it begins, and appends a struct per
  mutation, whether or not anybody is listening - because a handler may subscribe in the middle of the
  batch and must then receive the whole batch, and because "the event describes what happened while
  the batch was open" is a simpler rule than "what happened since the last subscriber arrived". A
  batch is an explicit statement that many changes are coming, so its cost is not the hot path.
- Enumeration is a struct enumerator over the list's own storage, with a version stamp so that
  mutating during enumeration throws `InvalidOperationException` - the `List<T>` behaviour, kept
  because a mutation during a render is a bug worth failing.

## 4. The list: API surface, in full

```csharp
namespace Kolvir.Observables;

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
batch before opening one.

XML documentation is mandatory and CS1591 is an error for anything that ships, so every member above
needs a `<summary>`, and the members inherited from `IList<T>`/`IReadOnlyList<T>` need none until they
are implemented.

## 5. The family: the shared core

Sections 2 to 4 are the list's. The set (section 6) and the dictionary (section 7) answer the same
three questions with different records, and everything between a mutation and a handler is the same
code for all three. That is the part to build once:

```
ObservableChangeBatch<TChange>       one collection's pending records, its batch depth, its delivery
                                     queue and generations, and the handlers it delivers to
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
last watcher goes rather than kept per address ever read.

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

- **Derived views** - `Filter`, `Select`, `Sort` over an `IReadOnlyObservableList<T>`. The hard one: an
  insert the filter drops is no record, and a re-sort is a diff rather than a mutation. These need the
  source's records as input, which is why the list's records carry old and new items.
- **`AsReadOnly` wrappers** for each collection: pass records through and hide the write surface. What
  a model exposes when it wants to hand out a collection without letting anyone mutate it.
- **An ordered or sorted variant.** A `SortedSet`-shaped collection has to report where things went,
  which is `Move` with a different mutation surface (open question 8.11).

## 6. The observable set

The simplest of the three: no index, no value, so a change can only add an item or take one away, and
the only interesting decision is what the four bulk operations of `ISet<T>` do to a record stream.

Sections 3.1 to 3.5, 3.7, 3.9 and 3.10 apply unchanged - one record per change as a readonly struct,
addresses as of the moment of the change, delivery in the order of the changes through a queue rather
than a recursive raise, the depth guard in generations, disposal dropping pending records and
delivering one last empty event, and nothing allocated when nothing is watching.

### 6.1 The type model

```
ObservableSetChangeType                        what a set change can be
  ItemAdded, ItemRemoved, SetCleared

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
  void BeginChangeBatch()
  void EndChangeBatch()
```

### 6.2 Decisions and why

#### 6.2.1 The comparer is the identity, so it is exposed

A set's `Contains`, `Add`'s "already there", `Remove` and every set-algebra operation mean what the
comparer says they mean: two items it calls equal are *the same item*. So `Comparer` is on the read
surface - a consumer mirroring the set has to build its mirror with the same comparer or it will
disagree about what is in it - and it is what the per-item dependencies are keyed by. `CreateSet<T>()`
without one uses `EqualityComparer<T>.Default`.

This is also where `Observable.GetNamed` stops being usable, and the reason 3.6.1 said entries are the
collection's own business: a named key compares by the key's own equality, while a set's comparer is a
property of the set and not of the item. The entry table therefore lives in the set, keyed with the
set's comparer.

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
  an empty set, `IntersectWith` of a superset, symmetric difference with the set itself.

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
public enum ObservableSetChangeType { ItemAdded, ItemRemoved, SetCleared }

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

public static partial class Observable
{
    public static IObservableSet<T> CreateSet<T>();
    public static IObservableSet<T> CreateSet<T>(IEqualityComparer<T>? comparer);
    public static IObservableSet<T> CreateSet<T>(params ReadOnlySpan<T> items);
    public static IObservableSet<T> CreateSet<T>(IEqualityComparer<T>? comparer, params ReadOnlySpan<T> items);
}
```

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

### 6.5 Where it differs from the list on purpose

- **No addresses to shift.** A list's insert marks a suffix; a set's change marks one item. That is the
  whole of the per-collection work, and it is why a set of ten thousand tags with twelve toggles
  rendered marks twelve entries at most.
- **A bulk operation is N records and one event**, where a list would need a batch from the caller. The
  set has no `AddRange`-shaped alternative to offer, so the batching is not optional.

## 7. The observable dictionary

The keyed collection: identity is the key, the payload is the value, and there is no index. Its record
carries what an observer needs to replay it - which key, and what the value was and is.

Sections 3.1 to 3.5, 3.7, 3.9 and 3.10 apply unchanged, as section 6 says of them.

### 7.1 The type model

```
ObservableDictionaryChangeType                 what a dictionary change can be
  ItemAdded, ItemRemoved, ItemChanged, DictionaryCleared

ObservableDictionaryChange<TKey, TValue>       one change
  ObservableDictionaryChangeType ChangeType { get; }
  TKey Key { get; }
  TValue? OldValue { get; }
  TValue? NewValue { get; }

ObservableDictionaryChangedEventArgs<TKey, TValue> : EventArgs
  IReadOnlyList<ObservableDictionaryChange<TKey, TValue>> Changes { get; }
  IReadOnlyObservableDictionary<TKey, TValue> Dictionary { get; }

IReadOnlyObservableDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>, IDisposable
  bool IsDisposed { get; }
  IEqualityComparer<TKey> Comparer { get; }
  IDisposable AddObservableDictionaryChangedHandler(Action<ObservableDictionaryChangedEventArgs<TKey, TValue>> handler)
  void RemoveObservableDictionaryChangedHandler(Action<ObservableDictionaryChangedEventArgs<TKey, TValue>> handler)

IObservableDictionary<TKey, TValue> : IReadOnlyObservableDictionary<TKey, TValue>, IDictionary<TKey, TValue>
  int ChangeBatchDepth { get; }
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
entries are keyed with it, in a table the dictionary owns.

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
public enum ObservableDictionaryChangeType { ItemAdded, ItemRemoved, ItemChanged, DictionaryCleared }

public readonly struct ObservableDictionaryChange<TKey, TValue>
{
    public ObservableDictionaryChangeType ChangeType { get; }
    public TKey Key { get; }
    public TValue? OldValue { get; }
    public TValue? NewValue { get; }
}

public sealed class ObservableDictionaryChangedEventArgs<TKey, TValue> : EventArgs
{
    public IReadOnlyList<ObservableDictionaryChange<TKey, TValue>> Changes { get; }
    public IReadOnlyObservableDictionary<TKey, TValue> Dictionary { get; }
}

public static partial class Observable
{
    public static IObservableDictionary<TKey, TValue> CreateDictionary<TKey, TValue>() where TKey : notnull;
    public static IObservableDictionary<TKey, TValue> CreateDictionary<TKey, TValue>(IEqualityComparer<TKey>? comparer) where TKey : notnull;
    public static IObservableDictionary<TKey, TValue> CreateDictionary<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> items) where TKey : notnull;
    public static IObservableDictionary<TKey, TValue> CreateDictionary<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> items, IEqualityComparer<TKey>? comparer) where TKey : notnull;
}
```

`TKey : notnull` is `IDictionary<TKey, TValue>`'s own constraint, so the factories carry it.

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

### 7.5 What is left for a later pass

- A sorted dictionary, or one whose enumeration order is a promise: both need an order to report,
  which is `Move`-shaped (open question 8.11).
- A value comparer per dictionary (open question 8.10).
- The `Dictionary<,>`-only conveniences (`Remove(k, out v)`, `GetValueOrDefault`). None of them is a
  change, and all of them are one line over the interface.

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
7. **An index entry is dropped when its last watcher goes** (3.6.1), so a dependency set that has
   been released is not something to register on again - the entry it named is detached, and a later
   read makes a new one. The alternative is keeping an entry per index ever read, which grows with the
   rows scrolled past rather than with the rows on screen.
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
11. **Sorted and ordered variants are out of scope** (5.3, 6.2.3, 7.5). An ordered collection has to
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
| 3 | Delivery: queue, generations, batches | Records arrive in mutation order even when a handler mutates; a batch is one event carrying all of them; nesting is counted and only the outermost end delivers; an unmatched end throws; a handler-caused loop is refused at `MaxEventStackSize` generations |
| 4 | Per-operation dependencies: count, index, contents | A body that reads `Count` and `this[0]` reports two dependencies; a write to an item wakes the index reader and not the count reader; a move wakes only the indexes it spans; an insert at `p` wakes every watched index at or past `p` and none before it; a read outside a scope allocates nothing and registers nothing; a batch raises each affected entry once |
| 5 | `CreateList` factories and disposal | The four overloads; disposal drops pending records, delivers the empty final event, and makes reads, mutations and registration throw while removal and a second dispose stay safe |
| 6 | Tests | Section 10's claims, in `tests/features/Kolvir.Feature.Observables.Tests` |
| 7 | The demo page and the guide section | An **Observable list** page in `src/demo/Kolvir.Demo.App/Pages/` that renders a list from its records and shows a batch add as one notification, plus an `### Observable collections` passage in `DEVELOPER-GUIDE.md` |
| 8 | The shared core: `ObservableChangeBatch<TChange>`, `ObservableKeyedDependencies<TKey>`, `ObservableShapeDependencies` | The list is rewritten onto the three shared types and its suite passes unchanged - which is the evidence that the core is the core, rather than the list plus two copies |
| 9 | The set: interfaces, `ObservableSetChange<T>`, event arguments | The four types compile and are documented (section 6.1, 6.3) |
| 10 | `ObservableSet<T>`: mutations, the four bulk operations, the three dependencies | Section 6.4's table row for row, and 6.2.4's dependencies exactly: a `Contains` reader is not woken by a change to another item, and a bulk operation that changes nothing raises nothing |
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
| **A loop is refused at the limit** | A handler that adds an item for every item added stops at `MaxEventStackSize` generations with an `InvalidOperationException` naming the limit, the list is left consistent, and an ordinary change afterwards still delivers - so the count was restored rather than left poisoned |
| **Disposal is ordered** | A handler sees `IsDisposed` true during the final event, receives no records, and any read from there throws; a batch left open is dropped rather than delivered; removal and a second dispose stay safe afterwards |
| **A read is exactly what it asked for** | A body that reads `Count`, `this[1]` and enumerates reports three dependencies, in that order. A write to item 1 wakes the reader of index 1 and not the reader of `Count`. A move wakes the reader of an index it spans and not one outside it. An insert at 2 wakes the readers of indexes 2 and above and none below. A clear wakes all three |
| **A throwing index read still subscribes** | `list[7]` over three items throws, and the run still reports index 7 among its dependencies; growing the list to eight then re-runs it, which is the only way an expression that is currently failing can recover |
| **A batch is one wake-up per affected read** | A body reading indexes 0, 1 and 2 reports three dependencies, and three mutations delivered as one event wake each of them exactly once, however many records touched that index - three records that all touch index 1 raise it once, not three times |
| **Nothing watched costs nothing to raise** | A mutation with no run in scope and no handler registered marks nothing, and the same mutation costs the same with ten thousand items as with ten; a read outside a run allocates nothing at all |
| **A set's bulk operation is N records and one event** | `UnionWith` of three new items delivers once, with three `ItemAdded` records in the argument's order; `SymmetricExceptWith` of the set itself raises nothing at all; a bulk operation inside the caller's batch joins that batch instead of delivering an event of its own; and a replay of the records ends equal to the set |
| **A set's `Contains` is its own dependency** | A body asking `Contains("a")` and `Contains("b")` is woken once when `a` is added and not at all when `c` is; a `Count` reader is woken by the bulk operation that changed the size, once for the whole event; `SetEquals` and the subset tests are woken by any change, including one that alters no size (8.5: records are what happened, not a diff) |
| **The dictionary's key is its own address** | A body reading `dict["a"]` and `dict["b"]` is woken once per change to that key and not by a change to the other; `Count` and `Keys` readers are not woken by a value write; `TryGetValue` on an absent key reports a dependency and is woken when the key appears |
| **A removal reports the value that was there** | `Remove(k)` records the value it removed; `Remove(pair)` with a mismatched value raises nothing and leaves the entry alone; a `Clear` of a non-empty dictionary is one record with nothing in it |
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
| 3 | Done | Delivery is a queue with a per-drain generation count rather than a recursive raise: a handler's own change is delivered after the event it is hearing, a batch is one event with every record in order, nesting delivers once at the outermost end, an unmatched end throws, and a loop is refused at `MaxEventStackSize` generations with the rest of the chain dropped. Disposal drops what a batch had not delivered, delivers one empty event, and then throws |
| 4 | Done | `ObservableListDependencies<T>` and `ObservableDependency`: the count and contents are fields, an index entry is created by a read inside a run and dropped when its last handler goes (`ReadScope.IsActive` keeps a cold read from allocating), and marking is a pass over the watched entries. A write wakes its index and not the count; an insert wakes the indexes at or past it; a move wakes its span; a clear wakes all; a batch raises each once; a read past the end is still a read |
| 5 | Done | The four `CreateList` overloads and the disposal order |
| 6 | Done | 41 tests in three files - `ObservableListChangeTests`, `ObservableListDeliveryTests`, `ObservableListDependencyTests` - covering section 10's claims for the list, including a replay that reproduces the list from records alone |
| 7 | Not started | The demo page and the guide passage |
| 8 | Not started | The shared core, extracted once the list is behaving |
| 9-10 | Not started | The set |
| 11-12 | Not started | The dictionary |
| 13-14 | Not started | The pages, the guide and the two suites |
