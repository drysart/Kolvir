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

Status of this document: **living plan, no code yet.** Section 2 fixes the shape, section 3 records
the decisions and the reasons, section 4 is the API in full, section 5 sketches the sibling
collections (dictionary, set, derived views) that will be fleshed out once the list is settled, and
section 6 is the handful of things that should be confirmed before code exists.

This file is deliberately ASCII-only, like `OBSERVABLES.md`: write "section 3.4" rather than a
section sign, and "<-" rather than an arrow.

## 1. Goals and non-goals

Goals:

- **Full fidelity.** An observer that began from the same contents and receives every event can
  reconstruct the list exactly, without ever reading it. That is a contract, not an aspiration: every
  change is reported, indexes are positions as of that moment, and delivery order is the order the
  changes happened in.
- **Granularity enough to do minimal work.** A move is one record. Setting an item is one record
  with the old and the new item. An insert is one record with an index. Nothing is reported as
  "everything changed" except an actual `Clear`.
- **Batches for the caller's sake, not the observer's.** `BeginChangeBatch`/`EndChangeBatch` turn N
  mutations into one notification while keeping every record, in order. The observer is called once
  instead of N times and loses nothing.
- **Cheap when nothing is watching.** A mutation nobody is listening to builds no records and
  allocates nothing beyond the storage change; a batch is the only thing that collects
  unconditionally (section 3.10).
- **Reads that can be depended on.** Reading the list publishes a dependency, so it plugs into
  `CollectDependencies`, `ObservableExpression<T>` and the Virtual DOM component seam with no new
  concept (section 3.6).
- **No new package and no new initialization.** It extends `Kolvir.Feature.Observables`; there is
  still nothing to register.

Non-goals:

- Thread safety, cross-thread notification, or dispatching to the UI thread. Every collection is
  UI-thread state like every control, exactly as `OBSERVABLES.md` section 3.9 says of values.
- Undo/redo. The records say what changed, not how to reverse it: `ListCleared` deliberately carries
  no items (3.1), so an undo history is a different object that records its own state.
- `INotifyCollectionChanged`, `ObservableCollection<T>`, `IBindingList`, or any WPF/XAML interop.
  Section 3.8 has the mapping, which is close but not the same contract.
- Sorting as a list operation. `IList<T>` has none, and a sort reported as moves is a diff problem
  rather than a mutation, which is open question 6.6.
- Coalescing or compacting records inside a batch (dropping an add that was removed again, merging
  two sets of one item). The batch keeps what happened. 6.5 records the question.
- Filtered, sorted or projected views over a list. Those are section 5, and they need the record
  format to settle first.

## 2. The type model

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

## 3. Decisions and why

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
the type count for a struct that is discarded in a frame's time; 6.2 keeps the question open.

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

### 3.6 Reads publish a dependency, once per list to start

Every read that looks at the list publishes a dependency on it: `Count`, `this[i]`, enumeration,
`Contains`, `IndexOf`, `CopyTo`. Any delivered change raises it - structure and item values alike.

That is the coarse end of a ladder the existing machinery already supports, and the plan is to climb
it rather than to design for it now:

| Granularity | What a read depends on | Raised by | When |
| --- | --- | --- | --- |
| One source per list | the list | any change | **step 1**, this plan |
| Structure + item values | structure: adds, removes, moves, clears; values: item changes | accordingly | later, if a rendered list needs it |
| Structure + per-index | `this[i]` depends on the index and the structure | item change at `i`, or any structural change | later, if rows re-render too often |

The ladder exists because correctness does not depend on climbing it - only the amount of work a
re-render does. Step 1 is what makes a list usable at all; the finer steps are measured against the
demo page before they earn their keys, which is the same discipline `OBSERVABLES.md` section 7 used
when it said enumeration must not depend on every key.

A raise of that dependency is one `Observable.RaiseNamedEvent` per delivered event, not per record,
so a batch is one wake-up.

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
- What a *dependency* needs from the list is exactly what `OBSERVABLES.md` section 7 planned:
  `Observable.GetNamed(...)` over a key, raised by `Observable.RaiseNamedEvent(...)`. The list does not
  have to be the source for a reader to depend on it (3.6).

So: typed handlers only, and the dependency half is a named key. That also keeps `Dispose` free of a
"what does a raise with no records mean" question (3.9). 6.1 asks to confirm this, since it is the
one place this design departs from the shape of the values.

For the record, the mapping to `INotifyCollectionChanged` is close but not the same, which is why
6.3 keeps interop out of step 1:

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
  is a struct that never exists, and the dependency raise is a lookup that does nothing when nobody
  has registered on the key.
- A batch allocates one `List<ObservableListChange<T>>` when it begins, and appends a struct per
  mutation, whether or not anybody is listening - because a handler may subscribe in the middle of the
  batch and must then receive the whole batch, and because "the event describes what happened while
  the batch was open" is a simpler rule than "what happened since the last subscriber arrived". A
  batch is an explicit statement that many changes are coming, so its cost is not the hot path.
- Enumeration is a struct enumerator over the list's own storage, with a version stamp so that
  mutating during enumeration throws `InvalidOperationException` - the `List<T>` behaviour, kept
  because a mutation during a render is a bug worth failing.

## 4. API surface, in full

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

## 5. The family: what comes after the list

The list is the test case for a family, and the parts worth sharing are not the mutations - those are
different for every collection - but the **record plumbing**: the pending batch, the delivery queue,
the generation counting, the handler snapshot, the disposal order. That is one small type:

```
ObservableChangeBatch<TChange>        internal: pending records, depth, delivery queue, generations
  + a collection's job is to turn each mutation into a TChange and hand it over
```

Which gives the siblings a shape to fit rather than a design to redo:

| Collection | Interfaces | Record | Notes |
| --- | --- | --- | --- |
| List | `IReadOnlyObservableList<T>`, `IObservableList<T>` | 5 change types, one item | This document. `Move` is the only non-`IList<T>` operation |
| Dictionary | `IReadOnlyObservableDictionary<TKey, TValue>`, `IObservableDictionary<TKey, TValue>` | Added / Removed / ValueChanged / Cleared, key + old/new value | No indexes at all, so the record carries the key; per-key dependencies are the natural granularity here and are already `GetNamed`'s shape |
| Set | `IReadOnlyObservableSet<T>`, `IObservableSet<T>` | Added / Removed / Cleared | The one collection where an equality comparer is part of the identity, and where a "changed" record has no meaning |
| Derived views | `Filter`, `Select`, `Sort` over an `IReadOnlyObservableList<T>` | the view's own records, mapped from the source's | The hard one: an insert that the filter drops is no record, and a re-sort is a diff. These want the source's records as input, which is why the list's records carry old and new items |
| Read-only wrapper | `AsReadOnly()` | passes the source's records through | What a model exposes when it wants to hand out the list without the write surface |

Two things that must be settled for the dictionary and not for the list, and are therefore recorded
here rather than designed here: whether a key's dependency and the dictionary's shape dependency are
raised by one event or two, and whether a batch that ends with a key back at its original value still
reports both changes (yes, by 3.3's ordering rule, unless 6.5 says otherwise).

## 6. Open questions - confirm before code

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
7. **The list's dependency key** - `(list, index)` tuples, or a small struct with a `Kind` field so
   that structure, index and item-value dependencies are distinct without colliding. Step 1 needs one
   key per list, so this can be deferred to whichever section-3.6 rung is climbed.
8. **The convenience token.** `BeginChangeBatch`/`EndChangeBatch` are the primitives; an
   `IDisposable EnterChangeBatch()` that pairs them is the shape a caller actually wants
   (`using var batch = list.EnterChangeBatch();`), including on an exception path. It is in the plan
   unless the pairing-token argument is unwanted.

## 7. Steps

| # | Step | Done when |
| --- | --- | --- |
| 1 | The two interfaces, `ObservableListChange<T>`, `ObservableListChangedEventArgs<T>` | All four compile, are documented, and nothing implements them yet - this is the reviewable shape of the contract |
| 2 | `ObservableList<T>` storage and mutations | Every mutation in 3.4 produces the record its row names and nothing else; an equal write, an empty clear, a same-index move and a failed remove produce no event; enumeration throws when the list is mutated under it |
| 3 | Delivery: queue, generations, batches | Records arrive in mutation order even when a handler mutates; a batch is one event carrying all of them; nesting is counted and only the outermost end delivers; an unmatched end throws; a handler-caused loop is refused at `MaxEventStackSize` generations |
| 4 | Dependencies: one key per list | A read inside `CollectDependencies` reports one dependency; any change raises it, once per delivered event; a property read outside a scope still costs a null check |
| 5 | `CreateList` factories and disposal | The four overloads; disposal drops pending records, delivers the empty final event, and makes reads, mutations and registration throw while removal and a second dispose stay safe |
| 6 | Tests | Section 8's claims, in `tests/features/Kolvir.Feature.Observables.Tests` |
| 7 | The demo page and the guide section | An **Observable list** page in `src/demo/Kolvir.Demo.App/Pages/` that renders a list from its records and shows a batch add as one notification, plus an `### Observable collections` passage in `DEVELOPER-GUIDE.md` |
| 8 | The fine-grained dependency rung, if measured | The demo page shows row-level re-render granularity against the coarse version, and the ladder in 3.6 gets a row marked done |

Step 1 is a contract review and is worth stopping at: everything after it is implementation of the
shape rather than of the idea.

### 7.1 What else has to change

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

## 8. Verification approach

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
| **A read is a dependency, and one raise covers a batch** | A body that reads `Count` and `this[0]` reports one dependency on the list; a batch of three changes raises it once; a mutation outside a batch raises it once; a read outside a scope registers nothing |
| **Enumeration is a snapshot of the version, not of the contents** | Mutating during `foreach` throws `InvalidOperationException`; reading after it throws is fine; a `CopyTo` during a mutation is the same failure rather than a torn array |

The demo page is the other half: it applies records to a mirrored list and asserts equality against the
model's list on every frame, which is the fidelity claim proved by the consumer rather than by the
producer.

## 9. Progress

| Step | State | Notes |
| --- | --- | --- |
| 0 | Done | This document: the contract, the decisions, the plan, and the open questions |
| 1 | Not started | |
