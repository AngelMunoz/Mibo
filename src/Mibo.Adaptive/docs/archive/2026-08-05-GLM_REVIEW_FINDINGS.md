# Review Findings — 2026-08-05 (GLM)

This document lists the items from the hostile review that are real defects.
Small items and false alarms were removed.

The code was read. No build, test, or benchmark was run.
Each item shows the location, the problem, and the action.
Severity is given first.

## Correctness

### 1. `ReduceNode.Recompute` does not use the write-generation guard — HIGH

Location: `src/AdaptiveSlop.Core/Library.fs:1249-1269`.

The method captures `checkedGen` but never reads it again.
The comment says a write during the compute must keep the node `Dirty`.
The code does not do this.
`MapNNode.Recompute` (`Library.fs:1083-1105`) and `AdaptiveNode.Recompute` (`Library.fs:780-843`) both perform this check.

If a dependency recomputes user code during the loop, and that user code writes to the graph, the write is not seen.
The node sets itself `Clean`.
The value comes from a partial state.
The next read returns the stale value.

Action: add the same generation check that `MapNNode` uses.
Keep the node `Dirty` when `getWriteGeneration()` differs from `checkedGen`.

### 2. `ReduceNode.Recompute` does not update the dirty cache — MEDIUM

Location: `src/AdaptiveSlop.Core/Library.fs:1249-1269`.

`AdaptiveNode` and `MapNNode` set `lastCheckedWriteGen` and `dirtyCache` at the end of the recompute.
`ReduceNode` does not.
The next `IsDirty` call falls through to the version-check path.
It walks every dependency again.

Action: set `lastCheckedWriteGen <- checkedGen` and `dirtyCache <- false` at the end of the recompute, as in `MapNNode.Recompute`.

### 3. `AdaptiveNode` with a constant dependency never promotes to `Clean` — MEDIUM

Location: `src/AdaptiveSlop.Core/Library.fs:735-748`, `651-675`.

`ConstantValue` and `LazyConstantValue` do not implement `IEdgeTarget`.
`BuildEdges` stores `depSlot = -1` for a constant (`Library.fs:762-767`).
The promotion test at line 740 needs every `depSlot >= 0`.
A node that mixes one constant with one `cval` stays `MaybeDirty` forever.
Every read walks the dependency closure.
The code comment at line 730 says this walk is the cost the optimization must remove.
The optimization is silently disabled for the common case.

Action: give `ConstantValue` and `LazyConstantValue` an `IEdgeTarget` implementation with an empty `ParentEdges`.
`AddEdge` returns a real index.
No mark ever fires, because the version never changes.
Then `depSlot >= 0` and the promotion works.

### 4. `MapReduceNode.Drain` calls the mapping twice for a set on an existing key — LOW

Location: `src/AdaptiveSlop.Core/Collections/Reductions.fs:343-358`.

For a `Set` on a key that is in the mirror, the code calls `reduction.sub red (mapping k old)`.
Then it calls `red <- reduction.add red (mapping k v)`.
The mapping runs twice.
If the mapping is expensive or has side effects, the result is wrong or slow.

Action: store the mapped old value in the mirror, next to the source value.
Read the stored value.
Call the mapping once.

### 5. `TransactionBuffer.Commit` stops on the first exception — LOW

Location: `src/AdaptiveSlop.Core/Library.fs:125-133`.

The commit loop calls `buffer[i].Commit()` in order.
If one commit throws, the loop stops.
The remaining commits do not run.
The `finally` block in `Transaction.run` (`Library.fs:638-642`) clears `TxActive`.
The graph is left in a partial-commit state.

Action: decide and document the policy.
Either wrap each commit in `try ... with` and continue, or roll back the prior commits.
Add a test for a throwing commit.

## Allocations

### 6. `AdaptiveNode` rents from `ArrayPool` and does not return the arrays — HIGH

Location: `src/AdaptiveSlop.Core/Library.fs:806-815`.

`deps`, `depVersions`, and `depSlots` come from `ArrayPool.Shared.Rent`.
They are returned only when the node grows past the rented size.
The node has no `IDisposable` and no finalizer.
When the node is garbage-collected, the rented arrays leave with it.
They do not go back to the pool.

For graphs with churn, for example `AVal.bind` that swaps inner nodes, the pool becomes empty.
Later rents allocate new arrays.
The "zero allocation on hot paths" claim depends on a pool the same code depletes.

`MapNNode` and `ReduceNode` use plain arrays.
They do not have this problem.
The choice of array kind is not consistent across node types.

Action: make `AdaptiveNode` implement `IDisposable`.
Return the arrays in `Dispose`.
Or stop using the pool and use plain arrays everywhere.
Confirm the result with `GC.GetAllocatedBytesForCurrentThread` and a BenchmarkDotNet run, as the AGENTS.md asks.

## Concurrency

### 7. `ChangeableValue.Post` can tear the payload for large structs — MEDIUM

Location: `src/AdaptiveSlop.Core/Library.fs:959-970`.

`Post` writes `postedValue <- newValue` from a foreign thread.
`ApplyPostedValue` reads `postedValue` on the owner thread.
The `Interlocked` operations are on the `posted` flag, not on the payload.
For reference types the read is safe.
For a struct larger than a machine word, for example `struct (float * float * float)`, the read can tear.
The owner can apply a value built from old and new fields at the same time.

The XML doc says "Allocates nothing."
It does not say that the value type must be atomic.

Action: document the limit on the value type, or copy the payload through the bounded ring as a box.
The second option breaks the no-allocation rule for `Post`.
The first option is the cheaper one.
Pick one and write it in the doc.

### 8. `PostRing` spins with no backpressure when it is full — MEDIUM

Location: `src/AdaptiveSlop.Core/Library.fs:316-336`.

`Enqueue` does `Thread.SpinWait(8)` and retries while the ring is full.
The ring has 1024 slots.
If the owner thread is slow or blocked, every producer thread spins at full CPU.
There is no timeout, no exception, and no drop option.

Action: decide and document the policy.
Options are: block with a wait handle, drop the oldest item, drop the newest item, or throw.
Pick one and add it.

## Design and API

### 9. `GraphContext` is a process-wide singleton — MEDIUM

Location: `src/AdaptiveSlop.Core/Library.fs:391-394`.

The constructor is `internal`.
The only context is `static member Default`.
Every adaptive value in the process shares one `writeGeneration`, one `markStack`, one `DependencyCollector`, one `TransactionBuffer`, one `notifications` queue, and one `PostRing`.

A write anywhere bumps `writeGeneration`.
This invalidates the dirty cache everywhere.
A server cannot keep one graph per request.
Two user interfaces cannot keep separate graphs.

AGENTS.md invariant 4 says the shared state lives on a graph context object.
The implementation exposes only one.

Action: make the context a parameter of the graph, or expose a constructor.
Discuss the change before the work, because AGENTS.md calls threading and context work structural.

### 10. Derived collection nodes leak when `Dispose` is not called — MEDIUM

Location: `src/AdaptiveSlop.Core/Collections/SetNodes.fs`, all node types.

Every derived set node registers a sink with its source.
The sink is removed only in `Dispose`.
The node has no finalizer.
`ASet.map`, `ASet.filter`, `ASet.union`, `ASet.collect`, and the rest return `aset<'T>`.
The type `aset<'T>` is `IDisposable`, but the function name does not tell the caller.
A caller writes `let ys = ASet.map f xs` and does not dispose `ys`.
The source keeps the sink.
The node stays alive as long as the source.

This is a leak by default.
The contract is in the XML doc, not in the type.

Action: pick one.
Options are: weak-reference sinks, a finalizer that unregisters, a debug allocator that finds undisposed nodes, or a name change that shows the burden, for example `ASet.mapDisposable`.
The cheapest is a debug leak detector.

### 11. Notification delivery has no bound — LOW

Location: `src/AdaptiveSlop.Core/Library.fs:531-536`.

`DeliverNotifications` runs while `notifyCount > 0`.
A callback that writes to the graph can enqueue more notifications.
A callback that writes the value it observes can loop without end.
There is no depth limit and no cycle check.

Action: add a depth counter.
Throw when the counter passes a limit.
Log the path of the loop in debug builds.

## Items removed from the review

The `box (this :> ISetDeltaSink<'T>)` sink-key claim was wrong.
`box` on a reference type returns the same pointer.
Attach and dispose pass the same reference.
`removeSink` matches by `ReferenceEquals`.
The sink is removed.
This item is not a defect.

The `PostRing` sequence-number truncation claim was a small risk for the stated use case.
It is not in this list.
