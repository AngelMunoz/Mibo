namespace Mibo.Adaptive

open System
open System.Collections.Generic
open System.Threading
open System.Text.Json.Serialization

// =============================================================================
// Changeable collection sources (PLAN.md Section 6.9)
//
// A source write: updates the internal state, advances the version, and
// appends the net delta to the journal of every registered sink. Writes
// never process a delta; processing happens on read (drain).
//
// Cross-thread posting (the cval.Post handoff pattern, per node): a post from
// any thread lands in a per-node pending-op ring (PostedOpRing); the first op
// of a pending batch enqueues the node into the global post ring through a
// coalescing flag. At the next drain (auto-pump at the outermost claim of a
// graph operation, or Posting.pump) the owner applies all pending ops as one
// batch: one net delta, one notification delivery. The per-node rings are the
// only additional Interlocked/Volatile use in the core, inside the explicit
// handoff structure (PLAN.md §7.3).
// =============================================================================

/// Internal. One pending posted operation on a changeable set: an element
/// add/remove, or a full replace (the op carries the whole new content).
[<Struct>]
type internal SetPostOp<'T> =
  | Add of item: 'T
  | Remove of item: 'T
  | Replace of content: seq<'T>

/// Internal. One pending posted operation on a changeable map.
[<Struct>]
type internal MapPostOp<'K, 'V> =
  | AddOrUpdate of key: 'K * value: 'V
  | Remove of key: 'K
  | Replace of content: seq<'K * 'V>

/// Internal. One pending posted operation on a changeable list. Insert with
/// position -1 appends at the replay-time end of the batch.
[<Struct>]
type internal ListPostOp<'T> =
  | Insert of position: int * value: 'T
  | RemoveAt of position: int
  | UpdateAt of position: int * value: 'T
  | RemoveValue of value: 'T
  | Replace of content: seq<'T>

/// Internal. Capacity of a per-node posted-op ring. A producer spins when
/// the ring is full; the owner drains at its next graph operation or pump.
module internal PostedOps =
  [<Literal>]
  let RingCapacity = 1024

/// <summary>
/// Internal. Bounded multi-producer, single-consumer ring of pending posted
/// operations for one changeable node. Preallocated; a post allocates
/// nothing. The sequence-number discipline is identical to PostRing (PLAN.md
/// §7.3): a producer claims a slot by CAS on the head, writes the payload,
/// and publishes it with a release write of the slot sequence; the owner
/// consumes by sequence number and advances the tail. A slot is reused only
/// after the owner consumed it, so a live slot is never read and written
/// concurrently: posted operations cannot tear.
/// </summary>
/// <remarks>
/// Full-ring policy matches PostRing: the producer spins with bounded backoff
/// until the owner drains. A post blocks rather than drop; the owner drains
/// at its next graph operation or at <c>Posting.pump</c>.
/// </remarks>
[<AllowNullLiteral>]
type internal PostedOpRing<'P>(capacity: int) =
  let mask = capacity - 1
  // Sequence numbers and payloads in parallel arrays: the payload store
  // happens-before the sequence release, the sequence acquire
  // happens-before the payload load, so a published payload is never read
  // while it is written (no tearing).
  let seqs = Array.zeroCreate<int> capacity
  let payloads: 'P[] = Array.zeroCreate capacity

  do
    for i in 0 .. capacity - 1 do
      seqs[i] <- i

  // Producer claim position. The consumer position belongs to the owner thread.
  let mutable head = 0L
  let mutable tail = 0L

  /// Enqueue one op. Spins until a slot is free. Called by any thread.
  member this.Enqueue(payload: 'P) =
    let mutable h = Volatile.Read(&head)
    let mutable enqueued = false
    let mutable spins = 0

    while not enqueued do
      let idx = int(h &&& int64 mask)
      let seq = Volatile.Read(&seqs[idx])

      if int h = seq then
        if Interlocked.CompareExchange(&head, h + 1L, h) = h then
          payloads[idx] <- payload
          Volatile.Write(&seqs[idx], seq + 1)
          enqueued <- true
      elif seq < int h then
        // The ring is full: the slot has not been consumed yet. Wait
        // with bounded backoff (policy: a post blocks until the owner
        // drains; ops are never dropped).
        spins <- spins + 1

        if spins >= 32 then
          Thread.Yield() |> ignore
          spins <- 0
        else
          Thread.SpinWait(8)

        h <- Volatile.Read(&head)
      else
        // Claimed by another producer; retry at the new head.
        h <- Volatile.Read(&head)

  /// Dequeue one op, or none when empty. Called by the owner thread only.
  member this.TryDequeue() : 'P voption =
    let t = Volatile.Read(&tail)
    let idx = int(t &&& int64 mask)
    let seq = Volatile.Read(&seqs[idx])

    if seq = int t + 1 then
      let payload = payloads[idx]
      payloads[idx] <- Unchecked.defaultof<'P>
      Volatile.Write(&seqs[idx], int(t + int64 capacity))
      Volatile.Write(&tail, t + 1L)
      ValueSome payload
    else
      ValueNone

/// <summary>
/// A changeable set: the writable source of an adaptive set. Reads and writes
/// are confined to the owner thread.
/// </summary>
/// <remarks>
/// <para>
/// Writes inside a <c>Transaction.run</c> are journaled in order and applied at
/// commit as one net delta: adds and removes of the same element in one batch
/// cancel, and the last write wins. Reads inside a transaction see the
/// pre-transaction state.
/// </para>
/// <para>
/// <c>GetValue</c> returns a transient view of the internal state, valid only
/// until the next write. <c>CSet.force</c> materializes an immutable snapshot.
/// </para>
/// </remarks>
// Round-trips the node through System.Text.Json as its bare element array (see
// ChangeableConverterFactory).
[<JsonConverter(typeof<ChangeableConverterFactory>)>]
type ChangeableSet<'T>(initial: seq<'T>) =
  // The graph this node belongs to, captured at creation (the ambient graph
  // of the creating thread).
  let ctx = GraphContext.Current
  let mutable version = 0L
  let data = HashSet<'T>(initial)
  let mutable sinks = SinkList.Create()
  let mutable outDelta = SetDelta<'T>.Create()
  // Ordered transaction journal; replayed at commit for a net delta.
  let mutable journal: struct ('T * bool)[] = Array.zeroCreate 16
  let mutable journalCount = 0
  // Scratch sets for the net-delta computation. Reused; zero allocation.
  let mutable scratchAdds = HashSet<'T>()
  let mutable scratchRems = HashSet<'T>()
  let mutable flushEnqueued = false
  // Pending full replace for Set inside a transaction. Last write wins.
  let mutable pendingValue: seq<'T> voption = ValueNone
  // Posted ops (the cval.Post handoff pattern): a coalescing flag plus a
  // per-node ring, allocated lazily on the first post. Foreign-thread posts
  // land here; the owner applies them at the next drain as one batch.
  let mutable postedOps: PostedOpRing<SetPostOp<'T>> = null
  let mutable posted = 0

  member private this.PushAndBump() =
    if not outDelta.IsEmpty then
      version <- version + 1L
      Collections.pushAndBumpSet ctx outDelta &sinks
      outDelta.Clear()

  member private this.Apply(newValue: seq<'T>) =
    // Net delta: removed = old items not in newValue; added = new items.
    scratchAdds.Clear()

    for item in newValue do
      scratchAdds.Add item |> ignore

    outDelta.Clear()

    for item in data do
      if not(scratchAdds.Contains item) then
        outDelta.Rems <- Collections.bufferAppend outDelta.Rems item

    // Apply the removals (after the scan: data is not mutated while iterated).
    for i in 0 .. outDelta.Rems.Count - 1 do
      data.Remove outDelta.Rems.Items[i] |> ignore

    for item in scratchAdds do
      if data.Add item then
        outDelta.Adds <- Collections.bufferAppend outDelta.Adds item

    this.PushAndBump()

  member private this.ApplyAndFlush(item: 'T, isAdd: bool) =
    let mutable changed = if isAdd then data.Add item else data.Remove item

    if changed then
      outDelta.Clear()

      if isAdd then
        outDelta.Adds <- Collections.bufferAppend outDelta.Adds item
      else
        outDelta.Rems <- Collections.bufferAppend outDelta.Rems item

      this.PushAndBump()

  member private this.CommitJournal() =
    // The current batch's flush is already enqueued (we are running from
    // it); resetting now lets reentrant writes during the flush re-enqueue.
    flushEnqueued <- false

    if journalCount > 0 then
      // Replay in write order; the scratch sets hold the net delta.
      scratchAdds.Clear()
      scratchRems.Clear()
      let mutable i = 0

      while i < journalCount do
        let struct (item, isAdd) = journal[i]

        if isAdd then
          if not(scratchRems.Remove item) && not(data.Contains item) then
            scratchAdds.Add item |> ignore
        elif not(scratchAdds.Remove item) && data.Contains item then
          scratchRems.Add item |> ignore

        i <- i + 1

      outDelta.Clear()

      for item in scratchAdds do
        data.Add item |> ignore
        outDelta.Adds <- Collections.bufferAppend outDelta.Adds item

      for item in scratchRems do
        data.Remove item |> ignore
        outDelta.Rems <- Collections.bufferAppend outDelta.Rems item

      journalCount <- 0
      this.PushAndBump()

  /// <summary>Replaces the whole set. Supersedes the whole batch inside a
  /// transaction (later writes of the batch are discarded; matches the
  /// list, docs/ALIST-DESIGN.md §3.3).</summary>
  member this.Set(newValue: seq<'T>) =
    ctx.ClaimOwner()

    try
      if ctx.TxActive then
        pendingValue <- ValueSome newValue
        // A full replace discards the journaled deltas of this batch.
        journalCount <- 0

        if not flushEnqueued then
          flushEnqueued <- true
          ctx.TxBuffer.Enqueue(this :> ICommit)
      else
        this.Apply newValue
    finally
      ctx.ReleaseOwner()

  /// <summary>Adds an element. No-op when already present.</summary>
  member this.Add(item: 'T) =
    ctx.ClaimOwner()

    try
      if ctx.TxActive then
        Collections.ensureCapacity &journal (journalCount + 1)
        journal[journalCount] <- struct (item, true)
        journalCount <- journalCount + 1

        if not flushEnqueued then
          flushEnqueued <- true
          ctx.TxBuffer.Enqueue(this :> ICommit)
      else
        this.ApplyAndFlush(item, true)
    finally
      ctx.ReleaseOwner()

  /// <summary>Removes an element. No-op when absent.</summary>
  member this.Remove(item: 'T) =
    ctx.ClaimOwner()

    try
      if ctx.TxActive then
        Collections.ensureCapacity &journal (journalCount + 1)
        journal[journalCount] <- struct (item, false)
        journalCount <- journalCount + 1

        if not flushEnqueued then
          flushEnqueued <- true
          ctx.TxBuffer.Enqueue(this :> ICommit)
      else
        this.ApplyAndFlush(item, false)
    finally
      ctx.ReleaseOwner()

  // ---------------------------------------------------------------------
  // Cross-thread posting (the cval.Post handoff pattern)
  // ---------------------------------------------------------------------

  member private this.PostOp(op: SetPostOp<'T>) =
    let ring =
      match postedOps with
      | null ->
        let created = PostedOpRing<SetPostOp<'T>>(PostedOps.RingCapacity)

        if Interlocked.CompareExchange(&postedOps, created, null) = null then
          created
        else
          postedOps
      | existing -> existing

    ring.Enqueue op

    // Coalesce a burst into one enqueue: only the first op of a pending
    // batch puts the node into the global post ring. Ops that land while
    // a batch is pending join that batch.
    if Interlocked.CompareExchange(&posted, 1, 0) = 0 then
      ctx.PostRing.Enqueue(this :> obj)

  member private this.ApplyPostedBatch() =
    // Clear the queued flag before applying (the cval.Post pattern): a
    // post that lands after the clear re-enqueues, so its op cannot be
    // lost.
    Interlocked.Exchange(&posted, 0) |> ignore

    if not(isNull postedOps) then
      // Replay the pending ops in arrival order through the net-delta
      // scratch state (the same replay the transaction commit runs). A
      // full replace supersedes the earlier ops of the batch, matching
      // the transaction semantics of Set. Ops arriving while the drain
      // loop runs join the same batch; an op that lands after the loop
      // emptied the ring re-enqueues the node and applies at the next
      // drain.
      scratchAdds.Clear()
      scratchRems.Clear()
      let mutable hasReplace = false
      let mutable replaceValue = Unchecked.defaultof<seq<'T>>
      let mutable op = postedOps.TryDequeue()

      while op.IsSome do
        let o = op.Value

        match o with
        | SetPostOp.Replace content ->
          hasReplace <- true
          replaceValue <- content
          scratchAdds.Clear()
          scratchRems.Clear()
        | SetPostOp.Add item ->
          if not(scratchRems.Remove item) && not(data.Contains item) then
            scratchAdds.Add item |> ignore
        | SetPostOp.Remove item ->
          if not(scratchAdds.Remove item) && data.Contains item then
            scratchRems.Add item |> ignore

        op <- postedOps.TryDequeue()

      if hasReplace then
        this.Apply replaceValue
      elif scratchAdds.Count > 0 || scratchRems.Count > 0 then
        outDelta.Clear()

        for item in scratchAdds do
          data.Add item |> ignore
          outDelta.Adds <- Collections.bufferAppend outDelta.Adds item

        for item in scratchRems do
          data.Remove item |> ignore
          outDelta.Rems <- Collections.bufferAppend outDelta.Rems item

        this.PushAndBump()

  /// <summary>
  /// Posts an add. Safe from any thread: the operation is queued and
  /// returns immediately. The owner thread applies the queued operations at
  /// the next graph operation (reads and writes auto-drain) or at
  /// <c>Posting.pump</c>, as one batch: one net delta, one notification
  /// delivery, and a burst is coalesced into a single handoff.
  /// </summary>
  /// <example>
  /// <code>
  /// // worker thread
  /// CSet.postAdd item items
  /// // owner thread: the next read applies the post automatically
  /// let view = ASet.force items
  /// </code>
  /// </example>
  member this.PostAdd(item: 'T) = this.PostOp(SetPostOp.Add item)

  /// <summary>
  /// Posts a remove. Safe from any thread. See <see cref="PostAdd"/> for
  /// the application contract.
  /// </summary>
  /// <example>
  /// <code>
  /// // worker thread
  /// CSet.postRemove item items
  /// </code>
  /// </example>
  member this.PostRemove(item: 'T) = this.PostOp(SetPostOp.Remove item)

  /// <summary>
  /// Posts a full replace. Safe from any thread. See <see cref="PostAdd"/>
  /// for the application contract; a posted replace supersedes the other
  /// ops of the same pending batch (the transaction semantics of
  /// <see cref="Set"/>).
  /// </summary>
  /// <example>
  /// <code>
  /// // worker thread
  /// CSet.postSet (Set.ofList [ 1; 2; 3 ]) items
  /// </code>
  /// </example>
  member this.PostSet(newValue: seq<'T>) =
    this.PostOp(SetPostOp.Replace newValue)

  interface ICommit with
    member this.Commit() =
      // The pending full replace supersedes the whole batch: journaled
      // ops replay first, then the replace applies last (matches the
      // list, docs/ALIST-DESIGN.md §3.3).
      this.CommitJournal()

      match pendingValue with
      | ValueSome newValue ->
        pendingValue <- ValueNone
        this.Apply newValue
      | ValueNone -> ()

    member this.Abort() =
      pendingValue <- ValueNone
      journalCount <- 0
      flushEnqueued <- false

  interface IAdaptiveSet<'T> with
    member this.GetValue() =
      ctx.ClaimOwner()

      try
        ctx.AddDependency(this :> IAdaptiveObject, version)
        data :> IReadOnlySet<'T>
      finally
        ctx.ReleaseOwner()

    member _.Version = version

  interface IDisposable with
    member this.Dispose() =
      // Real teardown: detach every sink so derived nodes do not keep
      // this source alive.
      Collections.clearSinks &sinks

  /// Internal. Number of registered derived sinks (tests).
  member internal _.SinkCount = sinks.Count

  interface ISetSinkRegistry with
    member this.AddSetSink(sink) = Collections.addSink &sinks sink

    member this.RemoveSetSink(sink) = Collections.removeSink &sinks sink

  interface IPostSource with
    member this.ApplyPosted() = this.ApplyPostedBatch()

/// <summary>
/// A changeable map: the writable source of an adaptive map. Reads and writes
/// are confined to the owner thread. See <see cref="ChangeableSet&lt;'T&gt;"/>
/// for the transaction and view contracts.
/// </summary>
// Round-trips the node through System.Text.Json as its bare JSON object (see
// ChangeableConverterFactory).
[<JsonConverter(typeof<ChangeableConverterFactory>)>]
type ChangeableMap<'K, 'V when 'K: equality>(initial: seq<'K * 'V>) =
  // The graph this node belongs to, captured at creation (the ambient graph
  // of the creating thread).
  let ctx = GraphContext.Current
  let mutable version = 0L
  let data = Dictionary<'K, 'V>()
  let mutable sinks = SinkList.Create()
  let mutable outDelta = MapDelta<'K, 'V>.Create()
  // Ordered transaction journal; replayed at commit for a net delta.
  // A remove entry carries Unchecked.defaultof<'V>.
  let mutable journal: struct ('K * 'V * bool)[] = Array.zeroCreate 16
  let mutable journalCount = 0
  // Scratch state for the net-delta computation. Reused; zero allocation.
  let mutable scratchSets = Dictionary<'K, 'V>()
  let mutable scratchRems = HashSet<'K>()
  let mutable flushEnqueued = false
  // Pending full replace for Set inside a transaction. Last write wins.
  let mutable pendingValue: seq<'K * 'V> voption = ValueNone
  // Posted ops (the cval.Post handoff pattern): a coalescing flag plus a
  // per-node ring, allocated lazily on the first post. Foreign-thread posts
  // land here; the owner applies them at the next drain as one batch.
  let mutable postedOps: PostedOpRing<MapPostOp<'K, 'V>> = null
  let mutable posted = 0

  do
    for (k, v) in initial do
      data[k] <- v

  member private this.PushAndBump() =
    if not outDelta.IsEmpty then
      version <- version + 1L
      Collections.pushAndBumpMap ctx outDelta &sinks
      outDelta.Clear()

  member private this.Apply(newValue: seq<'K * 'V>) =
    scratchSets.Clear()

    for (k, v) in newValue do
      scratchSets[k] <- v

    outDelta.Clear()

    for KeyValue(k, _) in data do
      if not(scratchSets.ContainsKey k) then
        outDelta.Rems <- Collections.bufferAppend outDelta.Rems k

    // Apply the removals (after the scan: data is not mutated while iterated).
    for i in 0 .. outDelta.Rems.Count - 1 do
      data.Remove outDelta.Rems.Items[i] |> ignore

    for KeyValue(k, v) in scratchSets do
      let mutable old = Unchecked.defaultof<'V>

      if
        data.TryGetValue(k, &old) && EqualityComparer<'V>.Default.Equals(old, v)
      then
        ()
      else
        data[k] <- v
        outDelta.Sets <- Collections.bufferAppend outDelta.Sets (struct (k, v))

    this.PushAndBump()

  member private this.ApplyAndFlush(key: 'K, valueToSet: 'V, isRemove: bool) =
    let mutable changed = false

    if isRemove then
      changed <- data.Remove key
    else
      let mutable existing = Unchecked.defaultof<'V>

      if
        data.TryGetValue(key, &existing)
        && EqualityComparer<'V>.Default.Equals(existing, valueToSet)
      then
        ()
      else
        data[key] <- valueToSet
        changed <- true

    if changed then
      outDelta.Clear()

      if isRemove then
        outDelta.Rems <- Collections.bufferAppend outDelta.Rems key
      else
        outDelta.Sets <-
          Collections.bufferAppend outDelta.Sets (struct (key, valueToSet))

      this.PushAndBump()

  member private this.CommitJournal() =
    // The current batch's flush is already enqueued (we are running from
    // it); resetting now lets reentrant writes during the flush re-enqueue.
    flushEnqueued <- false

    if journalCount > 0 then
      // Replay in write order; the scratch state holds the net delta.
      scratchSets.Clear()
      scratchRems.Clear()
      let mutable i = 0

      while i < journalCount do
        let struct (k, v, isSet) = journal[i]

        if isSet then
          scratchRems.Remove k |> ignore
          scratchSets[k] <- v
        elif not(scratchSets.Remove k) && data.ContainsKey k then
          scratchRems.Add k |> ignore

        i <- i + 1

      outDelta.Clear()

      for KeyValue(k, v) in scratchSets do
        let mutable old = Unchecked.defaultof<'V>

        if
          data.TryGetValue(k, &old)
          && EqualityComparer<'V>.Default.Equals(old, v)
        then
          ()
        else
          data[k] <- v

          outDelta.Sets <-
            Collections.bufferAppend outDelta.Sets (struct (k, v))

      for k in scratchRems do
        data.Remove k |> ignore
        outDelta.Rems <- Collections.bufferAppend outDelta.Rems k

      journalCount <- 0
      this.PushAndBump()

  /// <summary>Adds or updates an entry. No-op when the value is unchanged.</summary>
  member this.AddOrUpdate (key: 'K) (valueToSet: 'V) =
    ctx.ClaimOwner()

    try
      if ctx.TxActive then
        Collections.ensureCapacity &journal (journalCount + 1)
        journal[journalCount] <- struct (key, valueToSet, true)
        journalCount <- journalCount + 1

        if not flushEnqueued then
          flushEnqueued <- true
          ctx.TxBuffer.Enqueue(this :> ICommit)
      else
        this.ApplyAndFlush(key, valueToSet, false)
    finally
      ctx.ReleaseOwner()

  /// <summary>Removes an entry. No-op when absent.</summary>
  member this.Remove(key: 'K) =
    ctx.ClaimOwner()

    try
      if ctx.TxActive then
        Collections.ensureCapacity &journal (journalCount + 1)
        journal[journalCount] <- struct (key, Unchecked.defaultof<'V>, false)
        journalCount <- journalCount + 1

        if not flushEnqueued then
          flushEnqueued <- true
          ctx.TxBuffer.Enqueue(this :> ICommit)
      else
        this.ApplyAndFlush(key, Unchecked.defaultof<'V>, true)
    finally
      ctx.ReleaseOwner()

  /// <summary>Replaces the whole map. Supersedes the whole batch inside a
  /// transaction (later writes of the batch are discarded; matches the
  /// list, docs/ALIST-DESIGN.md §3.3).</summary>
  member this.Set(newValue: seq<'K * 'V>) =
    ctx.ClaimOwner()

    try
      if ctx.TxActive then
        pendingValue <- ValueSome newValue
        // A full replace discards the journaled deltas of this batch.
        journalCount <- 0

        if not flushEnqueued then
          flushEnqueued <- true
          ctx.TxBuffer.Enqueue(this :> ICommit)
      else
        this.Apply newValue
    finally
      ctx.ReleaseOwner()

  // ---------------------------------------------------------------------
  // Cross-thread posting (the cval.Post handoff pattern)
  // ---------------------------------------------------------------------

  member private this.PostOp(op: MapPostOp<'K, 'V>) =
    let ring =
      match postedOps with
      | null ->
        let created = PostedOpRing<MapPostOp<'K, 'V>>(PostedOps.RingCapacity)

        if Interlocked.CompareExchange(&postedOps, created, null) = null then
          created
        else
          postedOps
      | existing -> existing

    ring.Enqueue op

    // Coalesce a burst into one enqueue: only the first op of a pending
    // batch puts the node into the global post ring. Ops that land while
    // a batch is pending join that batch.
    if Interlocked.CompareExchange(&posted, 1, 0) = 0 then
      ctx.PostRing.Enqueue(this :> obj)

  member private this.ApplyPostedBatch() =
    // Clear the queued flag before applying (the cval.Post pattern): a
    // post that lands after the clear re-enqueues, so its op cannot be
    // lost.
    Interlocked.Exchange(&posted, 0) |> ignore

    if not(isNull postedOps) then
      // Replay the pending ops in arrival order through the net-delta
      // scratch state (the same replay the transaction commit runs). A
      // full replace supersedes the earlier ops of the batch, matching
      // the transaction semantics of Set. Ops arriving while the drain
      // loop runs join the same batch; an op that lands after the loop
      // emptied the ring re-enqueues the node and applies at the next
      // drain.
      scratchSets.Clear()
      scratchRems.Clear()
      let mutable hasReplace = false
      let mutable replaceValue = Unchecked.defaultof<seq<'K * 'V>>
      let mutable op = postedOps.TryDequeue()

      while op.IsSome do
        let o = op.Value

        match o with
        | MapPostOp.Replace content ->
          hasReplace <- true
          replaceValue <- content
          scratchSets.Clear()
          scratchRems.Clear()
        | MapPostOp.AddOrUpdate(key, value) ->
          scratchRems.Remove key |> ignore
          scratchSets[key] <- value
        | MapPostOp.Remove key ->
          if not(scratchSets.Remove key) && data.ContainsKey key then
            scratchRems.Add key |> ignore

        op <- postedOps.TryDequeue()

      if hasReplace then
        this.Apply replaceValue
      elif scratchSets.Count > 0 || scratchRems.Count > 0 then
        outDelta.Clear()

        for KeyValue(k, v) in scratchSets do
          let mutable old = Unchecked.defaultof<'V>

          if
            data.TryGetValue(k, &old)
            && EqualityComparer<'V>.Default.Equals(old, v)
          then
            ()
          else
            data[k] <- v

            outDelta.Sets <-
              Collections.bufferAppend outDelta.Sets (struct (k, v))

        for k in scratchRems do
          data.Remove k |> ignore
          outDelta.Rems <- Collections.bufferAppend outDelta.Rems k

        this.PushAndBump()

  /// <summary>
  /// Posts an add or update. Safe from any thread: the operation is queued
  /// and returns immediately. The owner thread applies the queued
  /// operations at the next graph operation (reads and writes auto-drain)
  /// or at <c>Posting.pump</c>, as one batch: one net delta, one
  /// notification delivery, and a burst is coalesced into a single handoff.
  /// </summary>
  /// <example>
  /// <code>
  /// // worker thread
  /// CMap.postAddOrUpdate key value map
  /// // owner thread: the next read applies the post automatically
  /// let view = AMap.force map
  /// </code>
  /// </example>
  member this.PostAddOrUpdate (key: 'K) (valueToSet: 'V) =
    this.PostOp(MapPostOp.AddOrUpdate(key, valueToSet))

  /// <summary>
  /// Posts a remove. Safe from any thread. See <see cref="PostAddOrUpdate"/>
  /// for the application contract.
  /// </summary>
  /// <example>
  /// <code>
  /// // worker thread
  /// CMap.postRemove key map
  /// </code>
  /// </example>
  member this.PostRemove(key: 'K) = this.PostOp(MapPostOp.Remove key)

  /// <summary>
  /// Posts a full replace. Safe from any thread. See
  /// <see cref="PostAddOrUpdate"/> for the application contract; a posted
  /// replace supersedes the other ops of the same pending batch (the
  /// transaction semantics of <see cref="Set"/>).
  /// </summary>
  /// <example>
  /// <code>
  /// // worker thread
  /// CMap.postSet (Map.toSeq (Map.ofList [ 1, "a" ])) map
  /// </code>
  /// </example>
  member this.PostSet(newValue: seq<'K * 'V>) =
    this.PostOp(MapPostOp.Replace newValue)

  /// <summary>
  /// Posts a clear (a full replace with the empty map). Safe from any
  /// thread. See <see cref="PostAddOrUpdate"/> for the application
  /// contract.
  /// </summary>
  /// <example>
  /// <code>
  /// // worker thread
  /// CMap.postClear map
  /// </code>
  /// </example>
  member this.PostClear() =
    this.PostOp(MapPostOp.Replace Seq.empty)

  interface ICommit with
    member this.Commit() =
      // The pending full replace supersedes the whole batch: journaled
      // ops replay first, then the replace applies last (matches the
      // list, docs/ALIST-DESIGN.md §3.3).
      this.CommitJournal()

      match pendingValue with
      | ValueSome newValue ->
        pendingValue <- ValueNone
        this.Apply newValue
      | ValueNone -> ()

    member this.Abort() =
      pendingValue <- ValueNone
      journalCount <- 0
      flushEnqueued <- false

  interface IAdaptiveMap<'K, 'V> with
    member this.GetValue() =
      ctx.ClaimOwner()

      try
        ctx.AddDependency(this :> IAdaptiveObject, version)
        data :> IReadOnlyDictionary<'K, 'V>
      finally
        ctx.ReleaseOwner()

    member _.Version = version

  interface IDisposable with
    member this.Dispose() =
      // Real teardown: detach every sink so derived nodes do not keep
      // this source alive.
      Collections.clearSinks &sinks

  /// Internal. Number of registered derived sinks (tests).
  member internal _.SinkCount = sinks.Count

  interface IMapSinkRegistry with
    member this.AddMapSink(sink) = Collections.addSink &sinks sink

    member this.RemoveMapSink(sink) = Collections.removeSink &sinks sink

  interface IPostSource with
    member this.ApplyPosted() = this.ApplyPostedBatch()

/// <summary>An abbreviation for <see cref="ChangeableSet&lt;'T&gt;"/> (FDA <c>cset&lt;'T&gt;</c> parity).</summary>
type cset<'T> = ChangeableSet<'T>

/// <summary>An abbreviation for <see cref="ChangeableMap&lt;'K,'V&gt;"/> (FDA <c>cmap&lt;'K,'V&gt;</c> parity).</summary>
type cmap<'K, 'V when 'K: equality> = ChangeableMap<'K, 'V>

/// <summary>
/// A changeable list: the writable source of an adaptive list. Reads and writes
/// are confined to the owner thread. See <see cref="ChangeableSet&lt;'T&gt;"/>
/// for the view, transaction, and disposal contracts.
/// </summary>
/// <remarks>
/// <para>
/// Positions are 0-based and refer to the list as of the previous operation of
/// the same batch. A full replace (<see cref="Set"/>) inside a transaction is
/// last-wins over the whole batch: it supersedes all other writes of the batch
/// (docs/ALIST-DESIGN.md §3.3). Reads inside a transaction see the
/// pre-transaction list, so positions inside a transaction refer to the
/// pre-transaction list.
/// </para>
/// <para>
/// <c>GetValue</c> returns a transient view of the internal list, valid only
/// until the next write. <c>CList.force</c> materializes an immutable array
/// snapshot.
/// </para>
/// </remarks>
// Round-trips the node through System.Text.Json as its bare element array (see
// ChangeableConverterFactory).
[<JsonConverter(typeof<ChangeableConverterFactory>)>]
type ChangeableList<'T>(initial: seq<'T>) =
  // The graph this node belongs to, captured at creation (the ambient graph
  // of the creating thread).
  let ctx = GraphContext.Current
  let mutable version = 0L
  let data = ResizeArray<'T>(initial)
  let mutable sinks = SinkList.Create()
  let mutable outDelta = ListDelta<'T>.Create()
  // Ordered transaction journal; replayed in order at commit (no netting).
  // A Clear marker expands into descending removes at replay.
  let mutable journal: ListOp<'T>[] = Array.zeroCreate 16
  let mutable journalCount = 0
  let mutable flushEnqueued = false
  // Pending full replace for Set inside a transaction. Last-wins over the
  // whole batch; applied after the journal replay.
  let mutable pendingValue: seq<'T> voption = ValueNone
  // Posted ops (the cval.Post handoff pattern): a coalescing flag plus a
  // per-node ring, allocated lazily on the first post. Foreign-thread posts
  // land here; the owner applies them at the next drain as one batch.
  let mutable postedOps: PostedOpRing<ListPostOp<'T>> = null
  let mutable posted = 0
  // Virtual count of the replay state: data.Count at the first journaled op,
  // then maintained by every op that changes the length. Appends journal
  // the position at the replay-time end, so several appends in one batch
  // land in write order (the pre-transaction count is the same for all of
  // them; sequential replay would reverse them).
  //
  // The full replay state (a copy of the list plus every journaled op)
  // validates positional writes and the UpdateAt equality check against the
  // state the batch has actually built, so commit replay cannot throw and
  // the batch is all-or-nothing.
  let mutable journalReplay: ResizeArray<'T> voption = ValueNone

  member private this.EnsureJournalSession() =
    match journalReplay with
    | ValueNone ->
      // One O(n) copy per transaction session that writes the list.
      let copy = ResizeArray<'T>(data)
      journalReplay <- ValueSome copy
      copy
    | ValueSome r -> r

  member private this.PushAndBump() =
    if not outDelta.IsEmpty then
      version <- version + 1L
      Collections.pushAndBumpList ctx outDelta &sinks
      outDelta.Clear()

  member private this.JournalOp(op: ListOp<'T>) =
    let replay = this.EnsureJournalSession()

    Collections.ensureCapacity &journal (journalCount + 1)

    match op.Kind with
    | ListOpKind.Insert ->
      // Resolve the -1 sentinel ("append at the replay-time end") now:
      // several appends in one batch land in write order.
      let pos = if op.Position = -1 then replay.Count else op.Position
      replay.Insert(pos, op.Value)
      journal[journalCount] <- ListOp(ListOpKind.Insert, pos, op.Value, 0uy)
    | ListOpKind.Remove ->
      replay.RemoveAt op.Position
      journal[journalCount] <- op
    | ListOpKind.Update ->
      replay[op.Position] <- op.Value
      journal[journalCount] <- op
    | ListOpKind.Clear ->
      replay.Clear()
      journal[journalCount] <- op
    | _ -> ()

    journalCount <- journalCount + 1

    if not flushEnqueued then
      flushEnqueued <- true
      ctx.TxBuffer.Enqueue(this :> ICommit)

  /// The prefix/suffix-trim diff used by <see cref="Set"/>. Applies the
  /// change to <paramref name="data"/> and appends the operations to the
  /// <c>outDelta</c> field: the trimmed middle becomes updates when the
  /// lengths match, otherwise descending removes then ascending inserts.
  member private this.ApplyDiff
    (data: ResizeArray<'T>, newData: ResizeArray<'T>)
    =
    let oldCount = data.Count
    let newCount = newData.Count
    let mutable prefix = 0
    let limit = min oldCount newCount

    while prefix < limit
          && EqualityComparer<'T>.Default.Equals(data[prefix], newData[prefix]) do
      prefix <- prefix + 1

    let mutable suffix = 0
    let mutable trimming = true

    while trimming do
      if
        suffix < limit - prefix
        && EqualityComparer<'T>.Default
          .Equals(data[oldCount - 1 - suffix], newData[newCount - 1 - suffix])
      then
        suffix <- suffix + 1
      else
        trimming <- false

    let oldMid = oldCount - prefix - suffix
    let newMid = newCount - prefix - suffix

    if oldMid = newMid then
      for i in 0 .. oldMid - 1 do
        let v = newData[prefix + i]
        data[prefix + i] <- v

        outDelta.Ops <-
          Collections.bufferAppend
            outDelta.Ops
            (ListOp(ListOpKind.Update, prefix + i, v, 0uy))
    else
      for i in oldMid - 1 .. -1 .. 0 do
        data.RemoveAt(prefix + i)

        outDelta.Ops <-
          Collections.bufferAppend
            outDelta.Ops
            (ListOp(ListOpKind.Remove, prefix + i, Unchecked.defaultof<'T>, 0uy))

      for i in 0 .. newMid - 1 do
        let v = newData[prefix + i]
        data.Insert(prefix + i, v)

        outDelta.Ops <-
          Collections.bufferAppend
            outDelta.Ops
            (ListOp(ListOpKind.Insert, prefix + i, v, 0uy))

  /// Full replace (non-transactional path and the Set-at-commit path).
  member private this.Apply(newValues: seq<'T>) =
    outDelta.Clear()
    this.ApplyDiff(data, ResizeArray<'T>(newValues))
    this.PushAndBump()

  /// Apply one operation to the data and push it as a one-op delta.
  member private this.ApplyAndFlush(op: ListOp<'T>) =
    outDelta.Clear()
    outDelta.Ops <- Collections.bufferAppend outDelta.Ops op
    this.PushAndBump()

  /// Replay the transaction journal in order against the data and push the
  /// whole batch as one delta. Every journaled op was validated against the
  /// replay state at write time, so the replay cannot throw and the batch
  /// applies all-or-nothing. The journal replay runs before the pending
  /// full replace: journaled positions are pre-transaction-relative, so they
  /// are only valid against the pre-transaction data.
  member private this.CommitJournal() =
    flushEnqueued <- false

    if journalCount > 0 then
      outDelta.Clear()
      let mutable i = 0

      while i < journalCount do
        let op = journal[i]

        match op.Kind with
        | ListOpKind.Insert ->
          data.Insert(op.Position, op.Value)

          outDelta.Ops <-
            Collections.bufferAppend
              outDelta.Ops
              (ListOp(ListOpKind.Insert, op.Position, op.Value, 0uy))
        | ListOpKind.Remove ->
          data.RemoveAt(op.Position)

          outDelta.Ops <-
            Collections.bufferAppend
              outDelta.Ops
              (ListOp(
                ListOpKind.Remove,
                op.Position,
                Unchecked.defaultof<'T>,
                0uy
              ))
        | ListOpKind.Update ->
          data[op.Position] <- op.Value

          outDelta.Ops <-
            Collections.bufferAppend
              outDelta.Ops
              (ListOp(ListOpKind.Update, op.Position, op.Value, 0uy))
        | ListOpKind.Clear ->
          for p in data.Count - 1 .. -1 .. 0 do
            data.RemoveAt p

            outDelta.Ops <-
              Collections.bufferAppend
                outDelta.Ops
                (ListOp(ListOpKind.Remove, p, Unchecked.defaultof<'T>, 0uy))
        | _ -> ()

        i <- i + 1

      journalCount <- 0
      this.PushAndBump()

    journalReplay <- ValueNone

  /// <summary>Appends an element at the end of the list.</summary>
  member this.Append(value: 'T) =
    ctx.ClaimOwner()

    try
      if ctx.TxActive then
        // The -1 sentinel means "append at the replay-time end" (the
        // journal resolves it to a concrete position; several appends
        // in one batch land in write order).
        this.JournalOp(ListOp(ListOpKind.Insert, -1, value, 0uy))
      else
        data.Add value

        this.ApplyAndFlush(
          ListOp(ListOpKind.Insert, data.Count - 1, value, 0uy)
        )
    finally
      ctx.ReleaseOwner()

  /// <summary>Inserts an element at the start of the list.</summary>
  member this.Prepend(value: 'T) =
    ctx.ClaimOwner()

    try
      if ctx.TxActive then
        this.JournalOp(ListOp(ListOpKind.Insert, 0, value, 0uy))
      else
        data.Insert(0, value)
        this.ApplyAndFlush(ListOp(ListOpKind.Insert, 0, value, 0uy))
    finally
      ctx.ReleaseOwner()

  /// <summary>
  /// Inserts an element before the element currently at <c>position</c>.
  /// <c>position = Count</c> appends. Throws when out of range.
  /// </summary>
  member this.InsertAt(position: int, value: 'T) =
    ctx.ClaimOwner()

    try
      if ctx.TxActive then
        // Validate against the replay state: positions refer to the
        // state built by the earlier ops of the batch, not the
        // pre-transaction data.
        let replay = this.EnsureJournalSession()

        if position < 0 || position > replay.Count then
          raise(ArgumentOutOfRangeException(nameof position))

        // An insert at the replay-time end is an append: use the
        // sentinel so it lands at the replay-time end.
        let pos = if position = replay.Count then -1 else position
        this.JournalOp(ListOp(ListOpKind.Insert, pos, value, 0uy))
      else
        if position < 0 || position > data.Count then
          raise(ArgumentOutOfRangeException(nameof position))

        data.Insert(position, value)
        this.ApplyAndFlush(ListOp(ListOpKind.Insert, position, value, 0uy))
    finally
      ctx.ReleaseOwner()

  /// <summary>Removes the element currently at <c>position</c>. Throws when out of range.</summary>
  member this.RemoveAt(position: int) =
    ctx.ClaimOwner()

    try
      if ctx.TxActive then
        // Validate against the replay state: positions refer to the
        // state built by the earlier ops of the batch.
        let replay = this.EnsureJournalSession()

        if position < 0 || position >= replay.Count then
          raise(ArgumentOutOfRangeException(nameof position))

        this.JournalOp(
          ListOp(ListOpKind.Remove, position, Unchecked.defaultof<'T>, 0uy)
        )
      else
        if position < 0 || position >= data.Count then
          raise(ArgumentOutOfRangeException(nameof position))

        data.RemoveAt position

        this.ApplyAndFlush(
          ListOp(ListOpKind.Remove, position, Unchecked.defaultof<'T>, 0uy)
        )
    finally
      ctx.ReleaseOwner()

  /// <summary>
  /// Replaces the element currently at <c>position</c>. No-op when the value
  /// is equal (equality at the source). Throws when out of range.
  /// </summary>
  member this.UpdateAt(position: int, value: 'T) =
    ctx.ClaimOwner()

    try
      if ctx.TxActive then
        // The equality check runs against the replay state: an update
        // that restores the committed value of a position touched
        // earlier in the batch must still journal.
        let replay = this.EnsureJournalSession()

        if position < 0 || position >= replay.Count then
          raise(ArgumentOutOfRangeException(nameof position))

        if
          not(EqualityComparer<'T>.Default.Equals(replay[position], value))
        then
          this.JournalOp(ListOp(ListOpKind.Update, position, value, 0uy))
      else
        if position < 0 || position >= data.Count then
          raise(ArgumentOutOfRangeException(nameof position))

        if not(EqualityComparer<'T>.Default.Equals(data[position], value)) then
          data[position] <- value
          this.ApplyAndFlush(ListOp(ListOpKind.Update, position, value, 0uy))
    finally
      ctx.ReleaseOwner()

  /// <summary>Removes the first occurrence of the value. No-op when absent. O(n) write-time scan.</summary>
  member this.Remove(value: 'T) =
    ctx.ClaimOwner()

    try
      if ctx.TxActive then
        // Remove the first occurrence in the replay state: the value
        // may have been inserted by an earlier op of the batch.
        let replay = this.EnsureJournalSession()
        let mutable index = -1
        let mutable i = 0

        while index < 0 && i < replay.Count do
          if EqualityComparer<'T>.Default.Equals(replay[i], value) then
            index <- i
          else
            i <- i + 1

        if index >= 0 then
          this.JournalOp(
            ListOp(ListOpKind.Remove, index, Unchecked.defaultof<'T>, 0uy)
          )
      else
        let mutable index = -1
        let mutable i = 0

        while index < 0 && i < data.Count do
          if EqualityComparer<'T>.Default.Equals(data[i], value) then
            index <- i
          else
            i <- i + 1

        if index >= 0 then
          data.RemoveAt index

          this.ApplyAndFlush(
            ListOp(ListOpKind.Remove, index, Unchecked.defaultof<'T>, 0uy)
          )
    finally
      ctx.ReleaseOwner()

  /// <summary>Removes all elements. The delta carries descending removes.</summary>
  member this.Clear() =
    ctx.ClaimOwner()

    try
      if ctx.TxActive then
        pendingValue <- ValueNone

        this.JournalOp(
          ListOp(ListOpKind.Clear, 0, Unchecked.defaultof<'T>, 0uy)
        )
      elif data.Count > 0 then
        outDelta.Clear()

        for p in data.Count - 1 .. -1 .. 0 do
          data.RemoveAt p

          outDelta.Ops <-
            Collections.bufferAppend
              outDelta.Ops
              (ListOp(ListOpKind.Remove, p, Unchecked.defaultof<'T>, 0uy))

        this.PushAndBump()
    finally
      ctx.ReleaseOwner()

  /// <summary>
  /// Replaces the whole list (prefix/suffix-trim diff). Last-wins over the
  /// whole batch inside a transaction: it supersedes all other writes of the
  /// batch (docs/ALIST-DESIGN.md §3.3).
  /// </summary>
  member this.Set(newValues: seq<'T>) =
    ctx.ClaimOwner()

    try
      if ctx.TxActive then
        pendingValue <- ValueSome newValues
        journalCount <- 0
        journalReplay <- ValueNone
        flushEnqueued <- true
        ctx.TxBuffer.Enqueue(this :> ICommit)
      else
        this.Apply newValues
    finally
      ctx.ReleaseOwner()

  // ---------------------------------------------------------------------
  // Cross-thread posting (the cval.Post handoff pattern)
  // ---------------------------------------------------------------------

  member private this.PostOp(op: ListPostOp<'T>) =
    let ring =
      match postedOps with
      | null ->
        let created = PostedOpRing<ListPostOp<'T>>(PostedOps.RingCapacity)

        if Interlocked.CompareExchange(&postedOps, created, null) = null then
          created
        else
          postedOps
      | existing -> existing

    ring.Enqueue op

    // Coalesce a burst into one enqueue: only the first op of a pending
    // batch puts the node into the global post ring. Ops that land while
    // a batch is pending join that batch.
    if Interlocked.CompareExchange(&posted, 1, 0) = 0 then
      ctx.PostRing.Enqueue(this :> obj)

  member private this.ApplyPostedBatch() =
    // Clear the queued flag before applying (the cval.Post pattern): a
    // post that lands after the clear re-enqueues, so its op cannot be
    // lost.
    Interlocked.Exchange(&posted, 0) |> ignore

    if not(isNull postedOps) then
      // Consume the whole pending batch first: if a positional op is
      // invalid, the batch aborts as a whole — nothing applies and no
      // op is left in the ring. Positions refer to the state built by
      // the earlier ops of the batch and are validated here, at apply
      // time (the posting thread cannot know the owner's list).
      let pending = ResizeArray<ListPostOp<'T>>()
      let mutable op = postedOps.TryDequeue()

      while op.IsSome do
        pending.Add op.Value
        op <- postedOps.TryDequeue()

      if pending.Count > 0 then
        let replay = ResizeArray<'T>(data)
        let mutable hasReplace = false
        let mutable replaceValue = Unchecked.defaultof<seq<'T>>

        for o in pending do
          match o with
          | ListPostOp.Replace content ->
            hasReplace <- true
            replaceValue <- content
          | ListPostOp.Insert(position, value) ->
            // Insert; position -1 appends at the replay-time end.
            let pos = if position = -1 then replay.Count else position

            if pos < 0 || pos > replay.Count then
              raise(
                ArgumentOutOfRangeException(
                  "position",
                  "Posted insert is out of range when the batch applies."
                )
              )

            replay.Insert(pos, value)
          | ListPostOp.RemoveAt position ->
            if position < 0 || position >= replay.Count then
              raise(
                ArgumentOutOfRangeException(
                  "position",
                  "Posted remove is out of range when the batch applies."
                )
              )

            replay.RemoveAt position
          | ListPostOp.UpdateAt(position, value) ->
            if position < 0 || position >= replay.Count then
              raise(
                ArgumentOutOfRangeException(
                  "position",
                  "Posted update is out of range when the batch applies."
                )
              )

            replay[position] <- value
          | ListPostOp.RemoveValue value ->
            // Remove the first occurrence.
            let mutable index = -1
            let mutable i = 0

            while index < 0 && i < replay.Count do
              if EqualityComparer<'T>.Default.Equals(replay[i], value) then
                index <- i
              else
                i <- i + 1

            if index >= 0 then
              replay.RemoveAt index

        // A full replace supersedes the other ops of the batch
        // (the transaction semantics of Set). Otherwise the batch
        // applies as one prefix/suffix-trim diff; the diff is empty
        // when the batch changed nothing.
        if hasReplace then
          this.Apply replaceValue
        else
          this.Apply(replay :> seq<'T>)

  /// <summary>
  /// Posts an append. Safe from any thread: the operation is queued and
  /// returns immediately. The owner thread applies the queued operations at
  /// the next graph operation (reads and writes auto-drain) or at
  /// <c>Posting.pump</c>, as one batch: one delta, one notification
  /// delivery, and a burst is coalesced into a single handoff. Positions of
  /// the batch refer to the state built by its earlier ops.
  /// </summary>
  /// <example>
  /// <code>
  /// // worker thread
  /// CList.postAppend item items
  /// // owner thread: the next read applies the post automatically
  /// let view = AList.force items
  /// </code>
  /// </example>
  member this.PostAppend(value: 'T) =
    this.PostOp(ListPostOp.Insert(-1, value))

  /// <summary>
  /// Posts an insert at the start. Safe from any thread. See
  /// <see cref="PostAppend"/> for the application contract.
  /// </summary>
  /// <example>
  /// <code>
  /// // worker thread
  /// CList.postPrepend item items
  /// </code>
  /// </example>
  member this.PostPrepend(value: 'T) =
    this.PostOp(ListPostOp.Insert(0, value))

  /// <summary>
  /// Posts an insert before the element currently at the position. Safe
  /// from any thread. See <see cref="PostAppend"/> for the application
  /// contract; the position is validated when the batch applies and refers
  /// to the state built by the earlier ops of the batch.
  /// </summary>
  /// <example>
  /// <code>
  /// // worker thread
  /// CList.postInsertAt 0 item items
  /// </code>
  /// </example>
  member this.PostInsertAt(position: int, value: 'T) =
    this.PostOp(ListPostOp.Insert(position, value))

  /// <summary>
  /// Posts a remove at the position. Safe from any thread. See
  /// <see cref="PostAppend"/> for the application contract; the position is
  /// validated when the batch applies.
  /// </summary>
  /// <example>
  /// <code>
  /// // worker thread
  /// CList.postRemoveAt 0 items
  /// </code>
  /// </example>
  member this.PostRemoveAt(position: int) =
    this.PostOp(ListPostOp.RemoveAt position)

  /// <summary>
  /// Posts a replace at the position. Safe from any thread. See
  /// <see cref="PostAppend"/> for the application contract; the position is
  /// validated when the batch applies.
  /// </summary>
  /// <example>
  /// <code>
  /// // worker thread
  /// CList.postUpdateAt 0 item items
  /// </code>
  /// </example>
  member this.PostUpdateAt(position: int, value: 'T) =
    this.PostOp(ListPostOp.UpdateAt(position, value))

  /// <summary>
  /// Posts a remove of the first occurrence of the value. Safe from any
  /// thread. See <see cref="PostAppend"/> for the application contract; the
  /// scan runs when the batch applies.
  /// </summary>
  /// <example>
  /// <code>
  /// // worker thread
  /// CList.postRemove item items
  /// </code>
  /// </example>
  member this.PostRemove(value: 'T) =
    this.PostOp(ListPostOp.RemoveValue value)

  /// <summary>
  /// Posts a clear. Safe from any thread. See <see cref="PostAppend"/> for
  /// the application contract.
  /// </summary>
  /// <example>
  /// <code>
  /// // worker thread
  /// CList.postClear items
  /// </code>
  /// </example>
  member this.PostClear() = this.PostSet(Seq.empty)

  /// <summary>
  /// Posts a full replace. Safe from any thread. See <see cref="PostAppend"/>
  /// for the application contract; a posted replace supersedes the other ops
  /// of the same pending batch (the transaction semantics of <see cref="Set"/>).
  /// </summary>
  /// <example>
  /// <code>
  /// // worker thread
  /// CList.postSet [ 1; 2; 3 ] items
  /// </code>
  /// </example>
  member this.PostSet(newValues: seq<'T>) =
    this.PostOp(ListPostOp.Replace newValues)

  /// <summary>Gets the number of elements.</summary>
  member _.Count = data.Count
  /// <summary>Gets whether the list is empty.</summary>
  member _.IsEmpty = data.Count = 0

  /// <summary>Gets the element at the given position.</summary>
  member this.Item
    with get (position: int) = data[position]

  /// <summary>Gets the element at the given position, or <c>ValueNone</c> when out of range.</summary>
  member this.TryGet(position: int) =
    if position >= 0 && position < data.Count then
      ValueSome data[position]
    else
      ValueNone

  interface ICommit with
    member this.Commit() =
      this.CommitJournal()

      match pendingValue with
      | ValueSome newValue ->
        pendingValue <- ValueNone
        this.Apply newValue
      | ValueNone -> ()

    member this.Abort() =
      pendingValue <- ValueNone
      journalCount <- 0
      journalReplay <- ValueNone
      flushEnqueued <- false

  interface IAdaptiveList<'T> with
    member this.GetValue() =
      ctx.ClaimOwner()

      try
        ctx.AddDependency(this :> IAdaptiveObject, version)
        data :> IReadOnlyList<'T>
      finally
        ctx.ReleaseOwner()

    member _.Version = version

  interface IDisposable with
    member this.Dispose() =
      // Real teardown: detach every sink so derived nodes do not keep
      // this source alive.
      Collections.clearSinks &sinks

  /// Internal. Number of registered derived sinks (tests).
  member internal _.SinkCount = sinks.Count

  interface IListSinkRegistry with
    member this.AddListSink(sink) = Collections.addSink &sinks sink

    member this.RemoveListSink(sink) = Collections.removeSink &sinks sink

  interface IPostSource with
    member this.ApplyPosted() = this.ApplyPostedBatch()

/// <summary>An abbreviation for <see cref="ChangeableList&lt;'T&gt;"/> (FDA <c>clist&lt;'T&gt;</c> parity).</summary>
type clist<'T> = ChangeableList<'T>
