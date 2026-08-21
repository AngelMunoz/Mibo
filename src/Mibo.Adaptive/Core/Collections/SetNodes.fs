namespace Mibo.Adaptive

open System
open System.Collections.Generic
open System.Collections.Frozen

// =============================================================================
// AdaptiveSet transform nodes (PLAN.md Section 6.9)
//
// A derived node registers with its dependencies on first read. A dependency
// push appends to the journal and advances the version and the write
// generation. Reads cascade over changed dependencies, drain the journal,
// and return a transient view of the internal state. `force` materializes a
// FrozenSet. Disposal unregisters and stops all delta processing.
// =============================================================================

/// <summary>
/// The identity mapping for union-style set drains. Module level and inline:
/// a class-level <c>let f = fun x -> ValueSome x</c> gets generalized and
/// materializes a fresh closure at every use site (measured 24 B per drain).
/// </summary>
module private Id =
  let inline identityV x = ValueSome x

/// <summary>An adaptive set over a fixed, immutable value. The value is computed once, at first read.</summary>
type ConstantSet<'T>([<InlineIfLambda>] create: unit -> FrozenSet<'T>) =
  let value = lazy create()

  interface IAdaptiveSet<'T> with
    member this.GetValue() =
      AdaptiveRuntime.addDependency (this :> IAdaptiveObject) 0L
      value.Value :> IReadOnlySet<'T>

    member _.Version = 0L

  interface IDisposable with
    member _.Dispose() = ()

/// <summary>
/// Maps every element of a set (or chooses, when the mapping returns
/// <c>ValueNone</c> to drop an element). Duplicate outputs share one reference
/// count.
/// </summary>
type MapSetNode<'T, 'U when 'T: equality and 'U: equality>
  (source: IAdaptiveSet<'T>, [<InlineIfLambda>] mapping: 'T -> 'U voption) =
  let mutable state = SetNodeState<'T, 'U>.Create(1)
  let mutable initialized = false
  let mutable disposed = false

  member private this.Register() =
    match box source with
    | :? ISetSinkRegistry as r -> r.AddSetSink(box(this :> ISetDeltaSink<'T>))
    | _ -> ()

  member private this.Unregister() =
    match box source with
    | :? ISetSinkRegistry as r ->
      r.RemoveSetSink(box(this :> ISetDeltaSink<'T>))
    | _ -> ()

  member private this.EnsureInitialized() =
    if not initialized then
      // Snapshot first, register between, then run the mapping over the
      // snapshot: the mapping is user code that may write to the source
      // (the transient view must not be iterated while it is mutated),
      // and the write must land in our journal (register before the
      // mapping runs). A dirty source draining during the snapshot read
      // pushes to nobody: no double-apply. The flag is set last: an
      // exception leaves the node uninitialized so the next read retries.
      let snapshot = HashSet<'T>(source.GetValue())
      this.Register()
      Collections.loadRefSet mapping snapshot &state
      state.DepVersions[0] <- Collections.committedVersion source
      initialized <- true

  interface ISetDeltaSink<'T> with
    member this.OnDeltas(adds: 'T[], addCnt: int, rems: 'T[], remCnt: int) =
      if not disposed then
        Collections.journalAppendSet &state.Journal adds addCnt rems remCnt
        state.Version <- state.Version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveSet<'U> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive set has been disposed."

        this.EnsureInitialized()

        if source.Version <> state.DepVersions[0] then
          source.GetValue() |> ignore
          state.DepVersions[0] <- Collections.committedVersion source

        if not state.Journal.IsEmpty then
          Collections.drainSetPush mapping &state

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Set.Data :> IReadOnlySet<'U>
      finally
        ctx.ReleaseOwner()

    member _.Version =
      // Dirty indicator (see MapMapNode.Version in MapNodes.fs): while
      // the source has unprocessed changes, report version + 1 so
      // version-checking consumers re-read; the re-read drains the
      // journal and settles the chain recursively.
      if source.Version <> state.DepVersions[0] then
        state.Version + 1L
      else
        state.Version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        this.Unregister()
        Collections.clearSinks &state.Sinks

  interface ISetSinkRegistry with
    member this.AddSetSink(sink) = Collections.addSink &state.Sinks sink

    member this.RemoveSetSink(sink) =
      Collections.removeSink &state.Sinks sink

/// <summary>Keeps the elements of a set that satisfy a predicate.</summary>
type FilterSetNode<'T when 'T: equality>
  (source: IAdaptiveSet<'T>, [<InlineIfLambda>] predicate: 'T -> bool) =
  let mapOpt = fun x -> if predicate x then ValueSome x else ValueNone
  let mutable state = SetNodeState<'T, 'T>.Create(1)
  let mutable initialized = false
  let mutable disposed = false

  member private this.Register() =
    match box source with
    | :? ISetSinkRegistry as r -> r.AddSetSink(box(this :> ISetDeltaSink<'T>))
    | _ -> ()

  member private this.Unregister() =
    match box source with
    | :? ISetSinkRegistry as r ->
      r.RemoveSetSink(box(this :> ISetDeltaSink<'T>))
    | _ -> ()

  member private this.EnsureInitialized() =
    if not initialized then
      // Snapshot first, register between (see MapSetNode.EnsureInitialized).
      // The flag is set last: an exception leaves the node uninitialized.
      let snapshot = HashSet<'T>(source.GetValue())
      this.Register()
      Collections.loadPlainSet mapOpt snapshot &state
      state.DepVersions[0] <- Collections.committedVersion source
      initialized <- true

  interface ISetDeltaSink<'T> with
    member this.OnDeltas(adds: 'T[], addCnt: int, rems: 'T[], remCnt: int) =
      if not disposed then
        Collections.journalAppendSet &state.Journal adds addCnt rems remCnt
        state.Version <- state.Version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveSet<'T> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive set has been disposed."

        this.EnsureInitialized()

        if source.Version <> state.DepVersions[0] then
          source.GetValue() |> ignore
          state.DepVersions[0] <- Collections.committedVersion source

        if not state.Journal.IsEmpty then
          Collections.drainPlainSetPush mapOpt &state

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Set.Data :> IReadOnlySet<'T>
      finally
        ctx.ReleaseOwner()

    member _.Version =
      // Dirty indicator (see MapMapNode.Version).
      if source.Version <> state.DepVersions[0] then
        state.Version + 1L
      else
        state.Version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        this.Unregister()
        Collections.clearSinks &state.Sinks

  interface ISetSinkRegistry with
    member this.AddSetSink(sink) = Collections.addSink &state.Sinks sink

    member this.RemoveSetSink(sink) =
      Collections.removeSink &state.Sinks sink

/// <summary>The union of two sets. One reference count per element across both sides.</summary>
type UnionSetNode<'T when 'T: equality>
  (left: IAdaptiveSet<'T>, right: IAdaptiveSet<'T>) =
  let deps = [| left; right |]
  let mutable state = SetNodeState<'T, 'T>.Create(2)
  let mutable initialized = false
  let mutable disposed = false

  member private this.RegisterSide(s: IAdaptiveSet<'T>) =
    match box s with
    | :? ISetSinkRegistry as r -> r.AddSetSink(box(this :> ISetDeltaSink<'T>))
    | _ -> ()

  member private this.UnregisterSide(s: IAdaptiveSet<'T>) =
    match box s with
    | :? ISetSinkRegistry as r ->
      r.RemoveSetSink(box(this :> ISetDeltaSink<'T>))
    | _ -> ()

  member private this.EnsureInitialized() =
    if not initialized then
      // Snapshot first, register between (see MapSetNode.EnsureInitialized).
      // The identity mapping cannot write; the snapshot is for uniformity.
      // The flag is set last: an exception leaves the node uninitialized.
      let leftSnapshot = HashSet<'T>(left.GetValue())
      let rightSnapshot = HashSet<'T>(right.GetValue())
      this.RegisterSide left
      this.RegisterSide right
      Collections.loadRefSet Id.identityV leftSnapshot &state
      Collections.loadRefSet Id.identityV rightSnapshot &state
      state.DepVersions[0] <- Collections.committedVersion left
      state.DepVersions[1] <- Collections.committedVersion right
      initialized <- true

  interface ISetDeltaSink<'T> with
    member this.OnDeltas(adds: 'T[], addCnt: int, rems: 'T[], remCnt: int) =
      if not disposed then
        Collections.journalAppendSet &state.Journal adds addCnt rems remCnt
        state.Version <- state.Version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveSet<'T> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive set has been disposed."

        this.EnsureInitialized()

        for j in 0..1 do
          if deps[j].Version <> state.DepVersions[j] then
            deps[j].GetValue() |> ignore
            state.DepVersions[j] <- Collections.committedVersion deps[j]

        if not state.Journal.IsEmpty then
          Collections.drainSetPush Id.identityV &state

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Set.Data :> IReadOnlySet<'T>
      finally
        ctx.ReleaseOwner()

    member _.Version =
      // Dirty indicator (see MapMapNode.Version): either side with
      // unprocessed changes trips it.
      if
        left.Version <> state.DepVersions[0]
        || right.Version <> state.DepVersions[1]
      then
        state.Version + 1L
      else
        state.Version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        this.UnregisterSide left
        this.UnregisterSide right
        Collections.clearSinks &state.Sinks

  interface ISetSinkRegistry with
    member this.AddSetSink(sink) = Collections.addSink &state.Sinks sink

    member this.RemoveSetSink(sink) =
      Collections.removeSink &state.Sinks sink

/// <summary>
/// A binary set operation over two sources: difference (left minus right),
/// intersect, or xor. Per-side reference counts drive the output membership.
/// </summary>
type TwoSourceSetNode<'T when 'T: equality>
  (op: TwoSetOp, left: IAdaptiveSet<'T>, right: IAdaptiveSet<'T>) =
  let deps: IAdaptiveObject[] = [|
    left :> IAdaptiveObject
    right :> IAdaptiveObject
  |]

  let mutable state = Collections.TwoSetState<'T>.Create(2)
  let mutable leftSink: obj = null
  let mutable rightSink: obj = null
  let mutable initialized = false
  let mutable disposed = false

  member private this.Register() =
    leftSink <- box(Collections.SideSetSink<'T>(this, 0))
    rightSink <- box(Collections.SideSetSink<'T>(this, 1))

    match box left with
    | :? ISetSinkRegistry as r -> r.AddSetSink(leftSink)
    | _ -> ()

    match box right with
    | :? ISetSinkRegistry as r -> r.AddSetSink(rightSink)
    | _ -> ()

  member private this.Unregister() =
    match box left with
    | :? ISetSinkRegistry as r -> r.RemoveSetSink(leftSink)
    | _ -> ()

    match box right with
    | :? ISetSinkRegistry as r -> r.RemoveSetSink(rightSink)
    | _ -> ()

  member private this.EnsureInitialized() =
    if not initialized then
      // Read first, register after (see MapSetNode.EnsureInitialized).
      // The flag is set last: an exception leaves the node uninitialized.
      Collections.loadTwoSet op left right &state
      this.Register()
      state.DepVersions[0] <- Collections.committedVersion left
      state.DepVersions[1] <- Collections.committedVersion right
      initialized <- true

  interface Collections.ITwoSetSinkTarget<'T> with
    member this.OnSideDeltas
      (side: int, adds: 'T[], addCnt: int, rems: 'T[], remCnt: int)
      =
      if not disposed then
        if side = 0 then
          Collections.journalAppendSet &state.JournalL adds addCnt rems remCnt
        else
          Collections.journalAppendSet &state.JournalR adds addCnt rems remCnt

        state.Version <- state.Version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveSet<'T> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive set has been disposed."

        this.EnsureInitialized()

        for j in 0..1 do
          if deps[j].Version <> state.DepVersions[j] then
            if j = 0 then
              left.GetValue() |> ignore
            else
              right.GetValue() |> ignore

            state.DepVersions[j] <- Collections.committedVersion deps[j]

        if not state.JournalL.IsEmpty || not state.JournalR.IsEmpty then
          Collections.drainTwoSetPush op &state

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Out :> IReadOnlySet<'T>
      finally
        ctx.ReleaseOwner()

    member _.Version =
      // Dirty indicator (see MapMapNode.Version): either side with
      // unprocessed changes trips it.
      if
        left.Version <> state.DepVersions[0]
        || right.Version <> state.DepVersions[1]
      then
        state.Version + 1L
      else
        state.Version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        this.Unregister()
        Collections.clearSinks &state.Sinks

  interface ISetSinkRegistry with
    member this.AddSetSink(sink) = Collections.addSink &state.Sinks sink

    member this.RemoveSetSink(sink) =
      Collections.removeSink &state.Sinks sink

/// <summary>
/// An adaptive set over an adaptive value of a sequence. Every change of the
/// value replaces the whole state and emits the diff as the delta (the value
/// carries no deltas; this node is the rebuild boundary, like FDA
/// <c>ASet.ofAVal</c>).
/// </summary>
type OfAvalSetNode<'T, 'S when 'T: equality and 'S :> seq<'T>>
  (value: IAdaptiveValue<'S>) =
  let mutable state = SetNodeState<'T, 'T>.Create(1)
  let mutable initialized = false
  let mutable disposed = false

  member private this.EnsureInitialized() =
    if not initialized then
      // Initial load: materialize the value and build the state.
      // The flag is set last: an exception leaves the node uninitialized.
      // The init diff is not pushed: clear the out buffer so it cannot
      // pollute the first real delta.
      let next = HashSet<'T>(value.GetValue())
      Collections.rebuildSetDiff next &state |> ignore
      state.Out.Clear()
      state.DepVersions[0] <- Collections.committedVersion value
      initialized <- true

  /// Re-read the value when it changed and emit the diff. Called from
  /// GetValue and from the Version getter (poll model, like
  /// <see cref="CustomSetNode"/>): a downstream re-pulls only when this
  /// node's version moves, so the version must advance on the read path.
  member private this.Poll() =
    if value.Version <> state.DepVersions[0] then
      // The value may yield a transient seq: materialize it.
      let next = HashSet<'T>(value.GetValue())

      if Collections.rebuildSetDiff next &state then
        // The version must advance: downstream nodes re-pull the
        // source only when it changed (a stuck version makes
        // derived nodes stale forever).
        state.Version <- state.Version + 1L
        Collections.pushAndBumpSet GraphContext.Current state.Out &state.Sinks
        state.Out.Clear()

      state.DepVersions[0] <- Collections.committedVersion value

  interface IAdaptiveSet<'T> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive set has been disposed."

        this.EnsureInitialized()
        this.Poll()
        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Set.Data :> IReadOnlySet<'T>
      finally
        ctx.ReleaseOwner()

    member this.Version =
      this.EnsureInitialized()
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
/// An adaptive set over an external reader function. The reader is called on
/// every read (poll); the node diffs the result against its state and emits
/// the diff as the delta. Pull-based: nothing marks this node, so consumers
/// must re-read it (FDA <c>ASet.ofReader</c> has the same pull model).
/// </summary>
type ReaderSetNode<'T when 'T: equality>
  ([<InlineIfLambda>] reader: unit -> HashSet<'T>) =
  let mutable state = SetNodeState<'T, 'T>.Create(0)
  let mutable disposed = false

  /// Re-read the external state and emit the diff. Called from GetValue and
  /// from the Version getter (consumers version-check before reading).
  member private this.Poll() =
    if not disposed then
      let next = reader()

      if Collections.rebuildSetDiff next &state then
        state.Version <- state.Version + 1L
        Collections.pushAndBumpSet GraphContext.Current state.Out &state.Sinks
        state.Out.Clear()

  interface IAdaptiveSet<'T> with
    member this.GetValue() =
      let ctx = GraphContext.Default
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
/// An adaptive set whose content is driven by a compute function. The compute
/// receives the current view and a delta builder; it appends the operations
/// that describe the change since the previous call (consuming its own event
/// queue, for example). Called on every read (poll), like
/// <see cref="ReaderSetNode"/>. FDA <c>ASet.custom</c> parity, pull model.
/// </summary>
type CustomSetNode<'T when 'T: equality>
  ([<InlineIfLambda>] compute: IReadOnlySet<'T> -> SetDeltaBuilder<'T> -> unit)
  =
  let mutable state = SetNodeState<'T, 'T>.Create(0)
  let writer = SetDeltaBuilder<'T>()
  // Reused scratch: prior presence of every element touched by one poll
  // (construction-time allocation; zero steady-state allocation).
  let scratch = Dictionary<'T, bool>()
  let mutable disposed = false

  member private this.Poll() =
    if not disposed then
      writer.Clear()
      compute (state.Set.Data :> IReadOnlySet<'T>) writer

      if not writer.IsEmpty then
        // Net-delta invariant: the delivered delta must not carry the
        // same element in both adds and rems (consumers apply the
        // buffers order-free). Adds apply first by convention; record
        // the prior presence of every touched element, apply the
        // batch, then deliver the net transition.
        scratch.Clear()

        for i in 0 .. writer.Adds.Count - 1 do
          let x = writer.Adds.Items[i]

          if not(scratch.ContainsKey x) then
            scratch[x] <- state.Set.Data.Contains x

          state.Set.Data.Add x |> ignore

        for i in 0 .. writer.Rems.Count - 1 do
          let x = writer.Rems.Items[i]

          if not(scratch.ContainsKey x) then
            scratch[x] <- state.Set.Data.Contains x

          state.Set.Data.Remove x |> ignore

        writer.Clear()
        let mutable e = scratch.GetEnumerator()

        while e.MoveNext() do
          let x = e.Current.Key
          let prior = e.Current.Value
          let final = state.Set.Data.Contains x

          if prior <> final then
            if final then writer.Add x else writer.Remove x

        if not writer.IsEmpty then
          state.Version <- state.Version + 1L

          Collections.pushAndBumpSet
            GraphContext.Current
            (writer.Snapshot())
            &state.Sinks

        writer.Clear()

  interface IAdaptiveSet<'T> with
    member this.GetValue() =
      let ctx = GraphContext.Default
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
/// An adaptive set that unions one inner adaptive set per source element
/// (<c>ASet.collect</c>, PLAN.md Section 7.4). The output is the refcounted
/// union of all contributions (the CountingHashSet role): an output element
/// disappears only when the last contributing inner set drops it. A removed
/// source element unregisters its inner sink eagerly (ANALYSIS-FDA.md
/// Pitfall 1). Registration is lazy (first read); disposal unregisters
/// everything.
/// </summary>
type CollectSetNode<'T, 'U when 'T: equality and 'U: equality>
  (source: IAdaptiveSet<'T>, [<InlineIfLambda>] mapping: 'T -> IAdaptiveSet<'U>)
  =
  let mutable state = Collections.CollectState<'T, 'U>.Create(1)
  let mutable initialized = false
  let mutable disposed = false

  member private this.EnsureInitialized() =
    if not initialized then
      initialized <- true
      // Snapshot the source view first, then register, then run the
      // mapping over the snapshot: the mapping is user code that may
      // write to the source (the transient view must not be iterated
      // while it is mutated), and the write must land in our journal.
      let snapshot = HashSet<'T>(source.GetValue())

      match box source with
      | :? ISetSinkRegistry as r -> r.AddSetSink(box this)
      | _ -> ()

      for x in snapshot do
        let inner = mapping x
        let innerView = inner.GetValue()
        let mutable entry = Collections.CollectEntry<'U>(inner)

        for u in innerView do
          let struct (g2, added) = Collections.refAdd state.Global u
          state.Global <- g2
          // The entry's own content is tracked unconditionally: only
          // the global output delta is conditional on newness.
          entry.Content.Add u |> ignore

        entry.Sink <- box(Collections.CollectSink<'T, 'U>(this, x))

        match box inner with
        | :? ISetSinkRegistry as r -> r.AddSetSink(entry.Sink)
        | _ -> ()

        entry.Version <- Collections.committedVersion inner
        state.Inner[x] <- entry

      state.DepVersions[0] <- Collections.committedVersion source
      initialized <- true

  interface Collections.ICollectTarget<'T, 'U> with
    member this.OnInnerDeltas
      (key: 'T, adds: 'U[], addCnt: int, rems: 'U[], remCnt: int)
      =
      if not disposed then
        let mutable entry = Unchecked.defaultof<Collections.CollectEntry<'U>>

        if state.Inner.TryGetValue(key, &entry) then
          Collections.journalAppendSet &entry.Journal adds addCnt rems remCnt
          state.Inner[key] <- entry
          state.Version <- state.Version + 1L
          GraphContext.Default.BumpWriteGeneration()

  interface ISetDeltaSink<'T> with
    member this.OnDeltas(adds: 'T[], addCnt: int, rems: 'T[], remCnt: int) =
      if not disposed then
        Collections.journalAppendSet &state.Journal adds addCnt rems remCnt
        state.Version <- state.Version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveSet<'U> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive set has been disposed."

        this.EnsureInitialized()

        let mutable hasPending = false

        if source.Version <> state.DepVersions[0] then
          source.GetValue() |> ignore
          state.DepVersions[0] <- Collections.committedVersion source

        hasPending <- not state.Journal.IsEmpty

        // Explicit struct enumerator: the F# KeyValue pattern over a
        // Dictionary field can allocate per element (measured 88 B/entry
        // in the version-check loop; zero with the explicit enumerator).
        let mutable ie = state.Inner.GetEnumerator()

        while ie.MoveNext() do
          let x = ie.Current.Key
          let e0 = ie.Current.Value
          let mutable entry = e0

          if entry.Node.Version <> entry.Version then
            entry.Node.GetValue() |> ignore
            // The pull (and the version getter itself, for poll
            // inners) may have delivered a delta into the dictionary
            // entry: re-read instead of clobbering the stale copy.
            let mutable e2 = Unchecked.defaultof<Collections.CollectEntry<'U>>

            if state.Inner.TryGetValue(x, &e2) then
              e2.Version <- Collections.committedVersion entry.Node
              state.Inner[x] <- e2
              entry <- e2
            else
              entry.Version <- Collections.committedVersion entry.Node

          if not hasPending && not entry.Journal.IsEmpty then
            hasPending <- true

        if hasPending then
          Collections.drainCollectPush this mapping &state

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Global.Data :> IReadOnlySet<'U>
      finally
        ctx.ReleaseOwner()

    member this.Version =
      // Dirty indicator (see MapMapNode.Version): the outer source OR any
      // inner set with unprocessed changes trips it. A changeable inner
      // delivers at write time (OnInnerDeltas bumps state.Version, so the
      // plain branch already reports a new version); a derived inner
      // (map|>filter, etc.) delivers only when this node is pulled, so
      // without scanning the inner entries its upstream dirt is hidden
      // from every gated consumer (a tail-only count/contains over the
      // collect serves the stale value forever).
      if source.Version <> state.DepVersions[0] then
        state.Version + 1L
      else
        let mutable dirty = false
        let mutable ie = state.Inner.GetEnumerator()

        while not dirty && ie.MoveNext() do
          if ie.Current.Value.Node.Version <> ie.Current.Value.Version then
            dirty <- true

        if dirty then state.Version + 1L else state.Version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true

        match box source with
        | :? ISetSinkRegistry as r -> r.RemoveSetSink(box this)
        | _ -> ()

        for KeyValue(_, entry) in state.Inner do
          match box entry.Node with
          | :? ISetSinkRegistry as r -> r.RemoveSetSink(entry.Sink)
          | _ -> ()

        state.Inner.Clear()
        Collections.clearSinks &state.Sinks

  interface ISetSinkRegistry with
    member this.AddSetSink(sink) = Collections.addSink &state.Sinks sink

    member this.RemoveSetSink(sink) =
      Collections.removeSink &state.Sinks sink

/// <summary>
/// An adaptive set bound to a scalar value (<c>ASet.bind</c>, PLAN.md Section
/// 7.4): <c>mapping value</c> selects the inner set; when the value changes, the
/// whole inner set is swapped (old content removed, new content added) and the
/// old inner sink is unregistered eagerly (FDA <c>BindReader</c> semantics;
/// ANALYSIS-FDA.md Pitfall 1). The inner set's own changes flow through a
/// journal. Registration is lazy (first read); disposal unregisters everything.
/// </summary>
type BindSetNode<'T, 'U when 'U: equality>
  (value: IAdaptiveValue<'T>, [<InlineIfLambda>] mapping: 'T -> IAdaptiveSet<'U>)
  =
  let mutable state = Collections.BindSetState<'U>.Create(1)
  let mutable inner: IAdaptiveSet<'U> = Unchecked.defaultof<IAdaptiveSet<'U>>
  let mutable hasInner = false
  let mutable innerVersion = 0L
  let mutable current: 'T = Unchecked.defaultof<'T>
  let mutable initialized = false
  let mutable disposed = false

  member private this.UnregisterInner() =
    if hasInner then
      match box inner with
      | :? ISetSinkRegistry as r -> r.RemoveSetSink(box this)
      | _ -> ()

      hasInner <- false

  member private this.LoadInner() =
    // Read first, register after: the view is complete, and the sink sees
    // only deltas that follow this point in time.
    let view = inner.GetValue()

    for u in view do
      state.Data.Add u |> ignore

    match box inner with
    | :? ISetSinkRegistry as r -> r.AddSetSink(box this)
    | _ -> ()

    hasInner <- true
    innerVersion <- Collections.committedVersion inner

  member private this.SwapTo(next: 'T) =
    // Eager edge removal (Pitfall 1): the old inner must not deliver after
    // the swap. Its pending journal is dropped with the content.
    this.UnregisterInner()
    state.Data.Clear()
    inner <- mapping next
    current <- next
    this.LoadInner()
    state.Journal.Clear()
    state.Version <- state.Version + 1L

  member private this.EnsureInitialized() =
    if not initialized then
      current <- value.GetValue()
      inner <- mapping current
      this.LoadInner()
      state.DepVersions[0] <- Collections.committedVersion value
      initialized <- true

  interface ISetDeltaSink<'U> with
    member this.OnDeltas(adds: 'U[], addCnt: int, rems: 'U[], remCnt: int) =
      if not disposed then
        Collections.journalAppendSet &state.Journal adds addCnt rems remCnt
        state.Version <- state.Version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveSet<'U> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive set has been disposed."

        this.EnsureInitialized()

        if value.Version <> state.DepVersions[0] then
          let next = value.GetValue()
          state.DepVersions[0] <- Collections.committedVersion value

          if not(EqualityComparer<'T>.Default.Equals(current, next)) then
            this.SwapTo(next)

        if inner.Version <> innerVersion then
          inner.GetValue() |> ignore
          innerVersion <- Collections.committedVersion inner

        if not state.Journal.IsEmpty then
          Collections.drainBindSetPush &state

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Data :> IReadOnlySet<'U>
      finally
        ctx.ReleaseOwner()

    member this.Version =
      // Dirty indicator (see MapMapNode.Version): the outer value or
      // the current inner with unprocessed changes trips it. Guarded by
      // the init flags: inner is null before the first read.
      if
        initialized
        && (value.Version <> state.DepVersions[0]
            || (hasInner && inner.Version <> innerVersion))
      then
        state.Version + 1L
      else
        state.Version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        this.UnregisterInner()
        Collections.clearSinks &state.Sinks

  interface ISetSinkRegistry with
    member this.AddSetSink(sink) = Collections.addSink &state.Sinks sink

    member this.RemoveSetSink(sink) =
      Collections.removeSink &state.Sinks sink

/// <summary>
/// Maps every element, disposing the mapped value when its last source
/// occurrence leaves (FDA <c>ASet.mapUse</c> parity). The mapped values are
/// stable (the mapping runs once per source element); the output is a set, so
/// two source elements mapping to one value are refcounted and the value is
/// disposed only when the last occurrence leaves. Disposing the node disposes
/// all live mapped values and clears the output.
/// </summary>
type MapUseSetNode<'A, 'B
  when 'A: equality and 'B: equality and 'B :> IDisposable>
  (source: IAdaptiveSet<'A>, [<InlineIfLambda>] mapping: 'A -> 'B) =
  let mutable state = SetNodeState<'A, 'B>.Create(1)
  // Source element -> its mapped value.
  let mapped = Dictionary<'A, 'B>()
  // Output value -> occurrence count (the output set dedups).
  let refs = Dictionary<'B, int>()
  let mutable initialized = false
  let mutable disposed = false

  member private this.Register() =
    match box source with
    | :? ISetSinkRegistry as r -> r.AddSetSink(box(this :> ISetDeltaSink<'A>))
    | _ -> ()

  member private this.Unregister() =
    match box source with
    | :? ISetSinkRegistry as r ->
      r.RemoveSetSink(box(this :> ISetDeltaSink<'A>))
    | _ -> ()

  member private this.EnsureInitialized() =
    if not initialized then
      // Snapshot first, register between, then map the snapshot (the
      // MapSetNode convention): the mapping is user code that may write
      // to the source, and the write must land in our journal.
      let snapshot = HashSet<'A>(source.GetValue())
      this.Register()

      for a in snapshot do
        let b = mapping a
        mapped[a] <- b

        match refs.TryGetValue b with
        | true, r -> refs[b] <- r + 1
        | false, _ -> refs[b] <- 1

        state.Set.Data.Add b |> ignore

      state.DepVersions[0] <- Collections.committedVersion source
      initialized <- true

  member private this.Drain() =
    // Removes first: the source elements are gone, their values may leave.
    let rems = state.Journal.Rems

    for i in 0 .. rems.Count - 1 do
      let a = rems.Items[i]

      if mapped.TryGetValue a |> fst then
        let b = mapped[a]
        mapped.Remove a |> ignore
        let r = refs[b] - 1

        if r = 0 then
          refs.Remove b |> ignore
          b.Dispose()
          state.Set.Data.Remove b |> ignore
          state.Out.Rems <- Collections.bufferAppend state.Out.Rems b
        else
          refs[b] <- r

    let adds = state.Journal.Adds

    for i in 0 .. adds.Count - 1 do
      let a = adds.Items[i]
      let b = mapping a
      mapped[a] <- b

      match refs.TryGetValue b with
      | true, r -> refs[b] <- r + 1
      | false, _ ->
        refs[b] <- 1
        state.Set.Data.Add b |> ignore
        state.Out.Adds <- Collections.bufferAppend state.Out.Adds b

    state.Journal.Clear()
    state.Version <- state.Version + 1L
    Collections.pushAndBumpSet GraphContext.Current state.Out &state.Sinks
    state.Out.Clear()

  interface ISetDeltaSink<'A> with
    member this.OnDeltas(adds: 'A[], addCnt: int, rems: 'A[], remCnt: int) =
      if not disposed then
        Collections.journalAppendSet &state.Journal adds addCnt rems remCnt
        state.Version <- state.Version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveSet<'B> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive set has been disposed."

        this.EnsureInitialized()

        if source.Version <> state.DepVersions[0] then
          source.GetValue() |> ignore
          state.DepVersions[0] <- Collections.committedVersion source

        if not state.Journal.IsEmpty then
          this.Drain()

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Set.Data :> IReadOnlySet<'B>
      finally
        ctx.ReleaseOwner()

    member _.Version =
      // Dirty indicator (see MapMapNode.Version).
      if source.Version <> state.DepVersions[0] then
        state.Version + 1L
      else
        state.Version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        this.Unregister()
        Collections.clearSinks &state.Sinks

        for KeyValue(_, b) in mapped do
          b.Dispose()

        mapped.Clear()
        refs.Clear()
        state.Set.Data.Clear()

  interface ISetSinkRegistry with
    member this.AddSetSink(sink) = Collections.addSink &state.Sinks sink

    member this.RemoveSetSink(sink) =
      Collections.removeSink &state.Sinks sink

// =============================================================================
// Scalar escape hatches: per-element contains and incremental count.
//
// Lazy scalar nodes (see the map escapes in MapNodes.fs): they register
// nothing, writes only bump the source version (O(1)), and the node
// re-syncs at its next read — the per-element precision that the plain
// AdaptiveNode-over-GetValue pattern cannot express (the whole-set version
// and the flat dependency carry no element information).
//
// Pull-only protocol: no parent edges, no write-time marking, no delivery.
// The version bump in the read-time re-sync is the only notification;
// consumers version-check the node and recompute only when the watched
// output moved. For a direct changeable source the re-sync is O(1). For a
// pull-lazy derived source the output delta exists only after
// its drain, so the source's version moves at write time and the dirty
// indicator (version + 1) makes the consumer re-read exactly once; the gate
// then runs at the next read (the documented depth-2 rule).
// =============================================================================

/// <summary>
/// A per-element membership test over an adaptive set (the node behind
/// <c>ASet.contains</c>). Registers nothing; the membership is re-read at
/// the next read after a write, and the version advances only when the
/// watched element's membership actually changed. Writes to unrelated
/// elements cost nobody anything.
/// </summary>
/// <remarks>
/// The re-sync re-reads the source view; it matches
/// <c>SetReduceNode.Drain</c>'s application order trivially (removals
/// first, then additions).
/// </remarks>
type SetContainsNode<'T when 'T: equality>
  (source: IAdaptiveSet<'T>, element: 'T) =
  let mutable version = 0L
  let mutable depVersion = 0L
  let mutable initialized = false
  let mutable disposed = false
  let mutable present = false

  /// Lazy re-sync (see MapLookupNode): re-read membership, bump only when
  /// it moved.
  member private this.Resync() =
    let before = present
    present <- source.GetValue().Contains element

    if present <> before then
      version <- version + 1L

    depVersion <- Collections.committedVersion source

  interface IAdaptiveValue<bool> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive value has been disposed."

        // Internal source reads (the init snapshot, the re-sync
        // below) are machinery of this node, not dependencies of the
        // consumer: suppress collection so the caller's frame sees
        // only this node. Without this, the whole-set dependency
        // leaks into the consumer's frame and defeats the
        // per-element gate.
        let collector = ctx.Collector
        let wasCollecting = ctx.CollectorActive

        // A throwaway frame, popped and discarded below: toggling
        // CollectorActive instead is NOT safe — a nested evaluation
        // inside the reads would reset the collector out from under
        // the caller's frame.
        if wasCollecting then
          collector.PushFrame()

        try
          if not initialized then
            // Snapshot first: there is no registration anymore.
            this.Resync()
            initialized <- true
          elif source.Version <> depVersion then
            this.Resync()
        finally
          if wasCollecting then
            collector.PopFrame() |> ignore

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        present
      finally
        ctx.ReleaseOwner()

    member this.Version =
      // Dirty indicator (the ExternalValueNode pattern): while the
      // source has unprocessed changes, report version + 1 so
      // version-checking consumers re-read exactly once; the re-sync
      // at GetValue applies the gate and decides the real version.
      if source.Version <> depVersion then
        version + 1L
      else
        version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true

/// <summary>
/// A count over an adaptive set, projected through <paramref name="view"/>
/// (the node behind <c>ASet.count</c> with <c>id</c> and <c>ASet.isEmpty</c>
/// with <c>fun c -&gt; c = 0</c>). Registers nothing: the count is re-read at
/// the next read after a write; the version advances only when the projected
/// output changed, so a count change that the projection collapses
/// (2 -&gt; 3 under isEmpty) costs the node and its consumers nothing.
/// </summary>
/// <remarks>
/// The view projection runs at re-sync time: keep it cheap (the built-in
/// uses are <c>id</c> and <c>fun c -&gt; c = 0</c>).
/// </remarks>
type SetCountNode<'T, 'Out when 'T: equality>
  (source: IAdaptiveSet<'T>, [<InlineIfLambda>] view: int -> 'Out) =
  let mutable version = 0L
  let mutable depVersion = 0L
  let mutable initialized = false
  let mutable disposed = false
  let mutable out = Unchecked.defaultof<'Out>

  /// Lazy re-sync (see SetContainsNode): re-read the count, project, and
  /// bump only when the projected output moved.
  member private this.Resync() =
    let before = out
    let nextOut = view(source.GetValue().Count)

    if not(EqualityComparer<'Out>.Default.Equals(before, nextOut)) then
      out <- nextOut
      version <- version + 1L

    depVersion <- Collections.committedVersion source

  interface IAdaptiveValue<'Out> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive value has been disposed."

        // Internal source reads are machinery of this node, not
        // dependencies of the consumer (see SetContainsNode.GetValue).
        let collector = ctx.Collector
        let wasCollecting = ctx.CollectorActive

        if wasCollecting then
          collector.PushFrame()

        try
          if not initialized then
            // Snapshot first: there is no registration anymore.
            this.Resync()
            initialized <- true
          elif source.Version <> depVersion then
            this.Resync()
        finally
          if wasCollecting then
            collector.PopFrame() |> ignore

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        out
      finally
        ctx.ReleaseOwner()

    member this.Version =
      // Dirty indicator (see SetContainsNode.Version).
      if source.Version <> depVersion then
        version + 1L
      else
        version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
