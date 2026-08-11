# Review findings — 2026-08-05

Source: hostile static review of `src/AdaptiveSlop.Core`. Read-only. No build,
test, or benchmark run. Every item was verified against the code on disk.
Items that were reported but did not survive verification are not listed here.

Order is by priority: correctness first, then exception safety, then
performance, then memory.

## Critical: wrong results

### 1. `ofAVal` nodes never advance their version

- **Where:** `SetNodes.fs:436-447` (`OfAvalSetNode`), `MapNodes.fs:1033-1051`
  (`OfAvalMapNode`).
- **Problem:** `GetValue` rebuilds the state and pushes deltas, but never
  increments `state.Version`. It stays `0L` forever. Downstream nodes pull the
  source only when `source.Version` changes (`SetNodes.fs:91`, `348-355`).
  Nothing else triggers the rebuild. Result: `ASet.ofAVal v |> ASet.map f`
  never updates after the first read.
- **Fix:** Increment `state.Version` when the rebuild changes the content.

### 2. Producers and consumers disagree on delta order

- **Where:** drains apply removals before additions (`Shared.fs:737-767`,
  `802-823`, `847+`, `1101-1234`). `CustomSetNode.Poll` applies additions
  before removals to its own state (`SetNodes.fs:549-553`).
- **Problem:** A delta that touches the same element twice in one batch
  diverges. Example: `Add x; Remove x` leaves the custom source empty, but
  every downstream node keeps `x`. `TwoSourceSetNode` with `Difference` or
  `Xor` emits `Add x` then `Rem x` when both sides gain `x` in one batch
  (`Shared.fs:1179-1183`); downstream drains apply the `Rem` first and keep
  `x`.
- **Fix:** Define one order. Apply it in producers and in all drains. Add a
  test with a same-element add+remove batch across a derived node.

### 3. `FilterMapListNode` corrupts `inputPositions` on `Update`

- **Where:** `ListNodes.fs:163-188`.
- **Problem:** The `Update` branch shifts the tail of `inputPositions` by +1
  or -1. Shifts are valid only for `Insert` and `Remove`. An `Update` never
  moves input positions. After the shift, positions are wrong, and later
  removes are ignored. The output stays stale.
- **Fix:** Delete the two tail-shift loops in the `Update` branch.

### 4. `ObserveMapNode.reduceJournal` miscounts keys with a `Set` and a `Rem`

- **Where:** `ObserveNodes.fs:177-215`.
- **Problem:** Each `Set` counts +1, each `Rem` counts -1, and all sets are
  processed before all rems. A key that is set and removed in one batch nets
  to zero. The observer is told nothing. Effects: lost removals, lost value
  changes, and a `KeyNotFoundException` at line 210 for a `Set; Set; Rem`
  batch.
- **Fix:** Reduce per key in journal order. Track presence and last value,
  not a +/- counter.

### 5. `ChangeableList` transaction replay is inconsistent and cannot abort

- **Where:** validation at `Changeable.fs:676,696,716`; replay at
  `Changeable.fs:593-634`; equality check at `Changeable.fs:719`.
- **Problems:**
  - Positional operations validate against pre-transaction state but replay
    sequentially. `[1;2]` with `removeAt 0; removeAt 1` in one transaction
    validates, then throws at commit.
  - `UpdateAt` compares equality against the committed value. `updateAt 0 5;
    updateAt 0 1` journals nothing for the second call. Commit gives
    `[5;2;3]`, not `[1;2;3]`.
  - `CommitJournal` mutates `data` during replay. If an operation throws,
    earlier operations stay applied, no delta is pushed, and the version is
    not bumped. The list is half-changed and downstream nodes are not told.
- **Fix:** Validate against replay state (track it in the journal pass), and
  do the equality check against the last journaled value. Make replay
  all-or-nothing: validate fully before the first mutation.

### 6. `ReduceNode` (scalar) misses the write-generation guard and the cache update

- **Where:** `Library.fs:1249-1269`.
- **Problem:** `checkedGen` is captured (line 1252) and never used. A write
  from user code inside the `reduce` callback marks the node `Dirty`, and
  `Recompute` then overwrites the mark with `Clean`. The stale value is served
  as fresh. `AdaptiveNode` and `MapNNode` have this guard
  (`Library.fs:831-834`, `1096-1098`). Also, `Recompute` never updates
  `lastCheckedWriteGen`/`dirtyCache` (compare `Library.fs:842-843`,
  `1104-1105`): an unobserved `ReduceNode` recomputes on every read until the
  next write.
- **Fix:** Copy the `AdaptiveNode` pattern: keep `Dirty` when the generation
  moved during compute; set the cache after recompute.

## Major: exception safety

### 7. `EnsureInitialized` sets the flag before the work

- **Where:** all collection nodes, e.g. `ListNodes.fs:105-112`,
  `MapNodes.fs:994-1009`.
- **Problem:** `initialized <- true` runs before the snapshot read,
  registration, and load. An exception leaves the node permanently
  half-initialized. Later reads return partial state with no error.
- **Fix:** Set the flag last, or reset it in a `catch`.

### 8. Exceptions skip journal compaction; entries are applied twice

- **Where:** `ListNodes.fs:195-201`, `Reductions.fs:197-212`,
  `ObserveNodes.fs:137-140`.
- **Problem:** Compaction runs only after the full drain loop. A throw from
  `mapping`, `reduction.sub`, or the user callback leaves consumed entries in
  the journal. The next drain applies them again: double subtract corrupts
  reductions; double removes corrupt lists.
- **Fix:** Compact in a `finally`, or track the consumed count during the
  loop.

### 9. Callback exceptions are not isolated

- **Where:** `Library.fs:531-536` (`DeliverNotifications`),
  `Shared.fs:667-719` (`pushAndMark*`), `Library.fs:1388-1390`.
- **Problem:** A throwing observer callback escapes `cval.Set` after the write
  applied, and strands the rest of the notification queue until some later,
  unrelated operation. `pushAndMark*` skips `DeliverNotifications` on
  exception for the same reason. `Observation.Deliver` consumes the version
  before the callback runs, so a throw loses that notification permanently.
- **Fix:** Decide the contract. Either document that callbacks must not throw
  and guard the queue with a `finally` that drains or clears it, or catch per
  sink and continue.

## Major: performance (invariant violations)

### 10. Boxed enumerators on hot paths

- **Where:** `Shared.fs:1267,1276,1789,1825` (`IReadOnlySet` enumeration),
  `Shared.fs:978,991,1002,1575,1582,1644`, `Reductions.fs:161`.
- **Problem:** `for x in <collection>` boxes the enumerator here. The code
  itself documents this cost at `ObserveNodes.fs:202-204` ("measured 24 B per
  delivery") and then repeats the pattern. This breaks invariant 5 (zero
  allocation on recompute and delta delivery).
- **Fix:** Use the explicit `GetEnumerator()` loop, as in
  `ObserveNodes.fs:204`.

### 11. `MapNNode`/`ReduceNode` never promote to `Clean`

- **Where:** `Library.fs:1053-1081`, `1219-1247`.
- **Problem:** `AdaptiveNode.IsDirty` promotes a verified-clean observed node
  to a flag check (`Library.fs:735-746`). The N-ary nodes do not. An observed
  wide node re-reads all N dependency versions on every read after every
  unrelated write.
- **Fix:** Add the same promotion to both nodes.

### 12. `MapReduceNode` rebuilds on same-value updates

- **Where:** `Reductions.fs:343-358`.
- **Problem:** For reductions without `sub` (`fold`, `tryMin`, `tryMax`), any
  `Set` on an existing key triggers a full `Rebuild`, even a no-op update.
  O(n) instead of O(1).
- **Fix:** Skip when old and new values are equal.

## Memory

### 13. `DependencyCollector` retains graph objects through a static root

- **Where:** `Library.fs:157-159`, `186-189`.
- **Problem:** `PopFrame` and `Reset` reset counts but never clear
  `depBuffer`. The collector lives on the static `GraphContext.Default`.
  Objects from the deepest evaluation stay referenced until a deeper one
  overwrites the slots.
- **Fix:** `Array.Clear` the used range in `PopFrame` (cheap, bounded by the
  frame size).

### 14. `Dispose()` is a no-op on all changeables

- **Where:** `Changeable.fs:209,436,842`.
- **Problem:** The interfaces inherit `IDisposable`, so `use` is the natural
  pattern. Disposal releases nothing: sinks, edges, and downstream nodes keep
  the source alive.
- **Fix:** Either implement real teardown (clear sinks and edges) or remove
  `IDisposable` from the changeables so `use` does not compile.

## Operational hazards

### 15. `PostRing.Enqueue` spins forever on a full ring

- **Where:** `Library.fs:316-336`.
- **Problem:** 1024 distinct posted sources between drains fill the ring. A
  posting thread then spins with `Thread.SpinWait(8)` and no backoff. If the
  owner thread is blocked, this is a deadlock that also burns a core.
- **Fix:** Add backoff (`Thread.Yield`/`Sleep(0)` after N spins) and document
  the full-ring behavior, or make the ring grow.

### 16. `Set` means two different things in transactions

- **Where:** `Changeable.fs:124-140,388-405` (set/map: replace first, later
  journaled operations still apply) vs `Changeable.fs:770-783` (list:
  supersedes the whole batch).
- **Problem:** Generic collection code cannot rely on one meaning.
- **Fix:** Pick one semantic, apply it to all three, and document it.

## Verified and excluded

Checked and not listed above: `ChangeableList.Append` in transactions (the
`-1` sentinel at `Changeable.fs:646` handles write order correctly), the
swap-pop edge storage, the speculative dirty-node version, the `Post`
single-slot handoff, and the write-generation dirty cache in `AdaptiveNode`.
These hold.
