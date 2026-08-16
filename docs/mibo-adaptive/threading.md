---
title: Transactions & Threading
category: Mibo.Adaptive
categoryindex: 4
index: 4
---

# Transactions and Cross-Thread Posting

## Transactions

```fsharp
Transaction.run (fun () ->
    width.Set(100.0)
    height.Set(50.0))
// both changes apply atomically at commit
```

Changes inside a transaction apply at commit — reads *inside* the transaction still see the pre-transaction values. Transactions are not strictly necessary (writes outside a transaction apply immediately); they are useful for batching multiple writes into one notification delivery.

On failure, a transaction aborts at the first exception: applied entries roll back, consumed reduction entries are not re-applied.

## A graph belongs to one owner thread

Only the owner thread reads and writes the graph. There are no locks inside the library — instead, every thread owns its ambient graph, and a node belongs to the graph of its creating thread. In debug builds, cross-thread misuse throws (the confinement checks); release builds strip the checks but the rule stays.

## Cross-thread posting

Foreign threads send changes with `Post`; the owner's next graph operation applies them automatically:

```fsharp
// worker thread
CVal.post (health - 1) health

// owner thread: no pump call needed
let h = AVal.getValue health
```

The posting rules:

* `Post` is lock-free and allocates nothing. It writes a typed pending field and, if the source is not queued yet, pushes the source onto a bounded preallocated ring.
* Pending posts are applied automatically at the start of the next graph operation on the owner thread, as one batch with one notification delivery. Several posts to one source collapse to one application of the last value.
* The source equality check still applies at application: posting an equal value marks nothing.
* `Posting.pump()` is optional: it forces application at a chosen boundary (for example, once per frame). It runs on the owner thread only and is cheap and allocation-free when the queue is empty.

In a Mibo adaptive program you rarely touch this directly — [`ctx.Intents.postTask` / `postAsync`](../adaptive/program.html) wrap the pattern, and the host pumps at the start of every step.

## The invariants, in short

1. **Pull-lazy only** — writes mark; recomputation happens exclusively on read.
2. **Recompute re-reads all dependencies** — dynamic dependencies (`bind`) and edge self-healing depend on it.
3. **No evaluation during marking** — notifications are deferred until marking completes.
4. **Owner-thread confinement** — cross-thread interaction goes through post/drain, never shared mutable access.
5. **Zero library-side allocation on hot paths** — clean reads, marks, static recomputes, delta delivery.
6. **Transactions defer application** — writes inside apply at commit.
