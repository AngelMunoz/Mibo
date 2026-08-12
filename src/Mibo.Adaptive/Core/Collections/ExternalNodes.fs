namespace Mibo.Adaptive

open System
open System.Collections.Generic
open System.Threading

// =============================================================================
// External sources (MAPA-DESIGN §1.1): ofExternal — snapshot function +
// invalidate handle.
//
// Semantics: the user provides a snapshot function and receives an invalidate
// handle. The snapshot is re-read at most once per invalidate, on the next
// read, and diffed against the previous snapshot; the diff is delivered
// through the normal delta machinery. Not invalidated → zero cost: no re-read,
// no diff, no allocation.
//
// The invalidate handle is O(1) at call time and deferred (invariant 3: no
// evaluation during the write). Thread-safe via the post ring (the cval.Post
// pattern): an owner-thread call bumps the write generation directly; a
// foreign-thread call enqueues, and the bump happens at the next graph
// operation on the owner thread.
//
// The user never marks: the handle is the user-facing form of the same
// write-notification path the *A nodes use internally (it bumps the write
// generation, so the *A gates re-scan and the dirty caches invalidate).
// =============================================================================

/// <summary>
/// An adaptive set whose content is supplied by an external snapshot function,
/// re-read only when invalidated via the handle returned by
/// <c>ASet.ofExternal</c> (FDA <c>ASet.ofExternal</c> parity, MAPA-DESIGN
/// §1.1). The snapshot is materialized into a reused scratch set (the diff
/// helpers require the concrete <see cref="HashSet&lt;'T&gt;"/> so their
/// struct enumerators stay allocation-free); the scratch is refilled only on
/// invalidated polls.
/// </summary>
type ExternalSetNode<'T when 'T: equality>
  ([<InlineIfLambda>] snapshot: unit -> IReadOnlySet<'T>) =
  // The graph this node belongs to, captured at creation (the ambient graph
  // of the creating thread).
  let ctx = GraphContext.Current
  let mutable state = SetNodeState<'T, 'T>.Create(0)
  let scratch = HashSet<'T>()
  let mutable dirty = true
  // Foreign-thread invalidation goes through the post ring (the cval.Post
  // pattern): a queued flag, applied on the owner thread.
  let mutable posted = 0
  let ownerThread = Environment.CurrentManagedThreadId
  let mutable disposed = false

  member this.Invalidate() =
    if Environment.CurrentManagedThreadId = ownerThread then
      dirty <- true
      ctx.BumpWriteGeneration()
    else if Interlocked.CompareExchange(&posted, 1, 0) = 0 then
      ctx.PostRing.Enqueue(this :> obj)

  member private this.Poll() =
    if dirty && not disposed then
      dirty <- false
      scratch.Clear()

      for x in snapshot() do
        scratch.Add x |> ignore

      if Collections.rebuildSetDiff scratch &state then
        state.Version <- state.Version + 1L
        Collections.pushAndBumpSet ctx state.Out &state.Sinks
        state.Out.Clear()

  interface IPostSource with
    member this.ApplyPosted() =
      // Clear the queued flag before marking: an invalidate that lands
      // after the clear re-enqueues, so it cannot be lost.
      Interlocked.Exchange(&posted, 0) |> ignore
      this.Invalidate()

  interface IAdaptiveSet<'T> with
    member this.GetValue() =
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive set has been disposed."

        this.Poll()
        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Set.Data :> IReadOnlySet<'T>
      finally
        ctx.ReleaseOwner()

    member this.Version =
      this.Poll()
      state.Version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        Collections.clearSinks &state.Sinks

  interface ISetSinkRegistry with
    member this.AddSetSink(sink) = Collections.addSink &state.Sinks sink

    member this.RemoveSetSink(sink) =
      Collections.removeSink &state.Sinks sink

/// <summary>
/// An adaptive map whose content is supplied by an external snapshot function,
/// re-read only when invalidated via the handle returned by
/// <c>AMap.ofExternal</c> (FDA <c>AMap.ofExternal</c> parity, MAPA-DESIGN
/// §1.1). The snapshot is materialized into a reused scratch dictionary (the
/// diff helper requires the concrete <see cref="Dictionary&lt;'K,'V&gt;"/> so
/// its struct enumerator stays allocation-free); the scratch is refilled only
/// on invalidated polls.
/// </summary>
type ExternalMapNode<'K, 'V when 'K: equality>
  ([<InlineIfLambda>] snapshot: unit -> IReadOnlyDictionary<'K, 'V>) =
  // The graph this node belongs to, captured at creation (the ambient graph
  // of the creating thread).
  let ctx = GraphContext.Current
  let mutable state = MapNodeState<'K, 'V, 'V>.Create(0)
  let scratch = Dictionary<'K, 'V>()
  let mutable dirty = true
  let mutable posted = 0
  let ownerThread = Environment.CurrentManagedThreadId
  let mutable disposed = false

  member this.Invalidate() =
    if Environment.CurrentManagedThreadId = ownerThread then
      dirty <- true
      ctx.BumpWriteGeneration()
    else if Interlocked.CompareExchange(&posted, 1, 0) = 0 then
      ctx.PostRing.Enqueue(this :> obj)

  member private this.Poll() =
    if dirty && not disposed then
      dirty <- false
      scratch.Clear()

      for KeyValue(k, v) in snapshot() do
        scratch[k] <- v

      if Collections.rebuildMapDiff scratch &state then
        state.Version <- state.Version + 1L
        Collections.pushAndBumpMap ctx state.Out &state.Sinks
        state.Out.Clear()

  interface IPostSource with
    member this.ApplyPosted() =
      Interlocked.Exchange(&posted, 0) |> ignore
      this.Invalidate()

  interface IAdaptiveMap<'K, 'V> with
    member this.GetValue() =
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive map has been disposed."

        this.Poll()
        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Data :> IReadOnlyDictionary<'K, 'V>
      finally
        ctx.ReleaseOwner()

    member this.Version =
      this.Poll()
      state.Version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        Collections.clearSinks &state.Sinks

  interface IMapSinkRegistry with
    member this.AddMapSink(sink) = Collections.addSink &state.Sinks sink

    member this.RemoveMapSink(sink) =
      Collections.removeSink &state.Sinks sink

/// <summary>
/// An adaptive list whose content is supplied by an external snapshot function,
/// re-read only when invalidated via the handle returned by
/// <c>AList.ofExternal</c> (FDA <c>AList.ofExternal</c> parity, MAPA-DESIGN
/// §1.1). The re-read is diffed against the previous snapshot positionally
/// (prefix/suffix, the <c>ChangeableList.ApplyDiff</c> algorithm); the diff is
/// delivered as a <see cref="ListDelta&lt;'T&gt;"/> through the normal delta
/// machinery.
/// </summary>
type ExternalListNode<'T when 'T: equality>
  ([<InlineIfLambda>] snapshot: unit -> IReadOnlyList<'T>) =
  // The graph this node belongs to, captured at creation (the ambient graph
  // of the creating thread).
  let ctx = GraphContext.Current
  let mutable data = ResizeArray<'T>()
  let mutable out = ListDelta<'T>.Create()
  let mutable version = 0L
  let mutable sinks = SinkList.Create()
  let mutable dirty = true
  let mutable posted = 0
  let ownerThread = Environment.CurrentManagedThreadId
  let mutable disposed = false

  member this.Invalidate() =
    if Environment.CurrentManagedThreadId = ownerThread then
      dirty <- true
      ctx.BumpWriteGeneration()
    else if Interlocked.CompareExchange(&posted, 1, 0) = 0 then
      ctx.PostRing.Enqueue(this :> obj)

  member private this.Poll() =
    if dirty && not disposed then
      dirty <- false
      let next = snapshot()

      if Collections.rebuildListDiff next data &out then
        version <- version + 1L
        Collections.pushAndBumpList ctx out &sinks
        out.Clear()

  interface IPostSource with
    member this.ApplyPosted() =
      Interlocked.Exchange(&posted, 0) |> ignore
      this.Invalidate()

  interface IAdaptiveList<'T> with
    member this.GetValue() =
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive list has been disposed."

        this.Poll()
        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        data :> IReadOnlyList<'T>
      finally
        ctx.ReleaseOwner()

    member this.Version =
      this.Poll()
      version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        Collections.clearSinks &sinks

  interface IListSinkRegistry with
    member this.AddListSink(sink) = Collections.addSink &sinks sink

    member this.RemoveListSink(sink) = Collections.removeSink &sinks sink
