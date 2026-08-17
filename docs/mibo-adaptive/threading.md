---
title: Transactions & Threading
category: Mibo.Adaptive
categoryindex: 4
index: 4
---

# Transactions and Cross-Thread Posting

## Transactions

```fsharp
let resize () =
    width.Set(100.0)
    height.Set(50.0)

Transaction.run resize
// both changes apply atomically at commit
```

Changes inside a transaction apply at commit; reads *inside* the transaction still see the pre-transaction values. Transactions are not strictly necessary (writes outside a transaction apply immediately); they are useful for batching multiple writes so dependents learn about the change once, not once per write.

On failure, a transaction aborts at the first exception: applied entries roll back, and deltas already consumed from collections stay consumed (they are not re-applied).

## A graph belongs to one owner thread

Only the owner thread reads and writes the graph. There are no locks inside the library; instead, every thread carries its own graph (the ambient graph), and a node belongs to the graph of its creating thread. In debug builds, cross-thread misuse throws (the confinement checks); release builds strip the checks but the rule stays.

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
* Pending posts are applied automatically at the start of the next graph operation on the owner thread, as one batch. Several posts to one source collapse to one application of the last value.
* The source equality check still applies at application: posting an equal value marks nothing.
* `Posting.pump()` is optional: it forces application at a chosen boundary (for example, once per step). It runs on the owner thread only and is cheap and allocation-free when the queue is empty.

In a Mibo game you rarely touch this directly; [`ctx.Intents.postTask` / `postAsync`](../adaptive/program.html) do the posting for you, and the host applies pending posts at the start of every step.

## The invariants, in short

1. **Pull-lazy only**: a write marks nodes dirty; recomputation happens exclusively on read.
2. **Recompute re-reads all dependencies**: dynamic dependencies (`bind`) and self-repairing dependency links rely on it.
3. **No evaluation during marking**: while nodes are being marked, nothing recomputes; dependents are notified after marking completes.
4. **Owner-thread confinement**: cross-thread interaction goes through `Post` and the owner's automatic application, never shared mutable access.
5. **Zero library-side allocation on hot paths**: clean reads, marks, static recomputes, delta delivery.
6. **Transactions defer application**: writes inside apply at commit.
