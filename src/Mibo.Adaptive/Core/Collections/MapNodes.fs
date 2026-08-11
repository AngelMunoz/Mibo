namespace Mibo.Adaptive

open System
open System.Collections.Generic
open System.Collections.Frozen

// =============================================================================
// AdaptiveMap transform nodes (PLAN.md Section 6.9)
//
// Same journal/drain model as the set nodes. Registration happens through the
// IMapSinkRegistry interface on every node type, so derived maps compose freely.
// =============================================================================

/// <summary>An adaptive map over a fixed, immutable value. The value is computed once, at first read.</summary>
type ConstantMap<'K, 'V when 'K: equality>
  ([<InlineIfLambda>] create: unit -> FrozenDictionary<'K, 'V>) =
  let value = lazy create()

  interface IAdaptiveMap<'K, 'V> with
    member this.GetValue() =
      AdaptiveRuntime.addDependency (this :> IAdaptiveObject) 0L
      value.Value :> IReadOnlyDictionary<'K, 'V>

    member _.Version = 0L

  interface IDisposable with
    member _.Dispose() = ()

/// <summary>
/// Maps every entry of a map (or chooses, when the mapping returns
/// <c>ValueNone</c> to drop an entry).
/// </summary>
type MapMapNode<'K, 'V, 'U when 'K: equality>
  (
    source: IAdaptiveMap<'K, 'V>,
    [<InlineIfLambda>] mapping: 'K -> 'V -> 'U voption
  ) =
  let mutable state = MapNodeState<'K, 'V, 'U>.Create(1)
  let mutable initialized = false
  let mutable disposed = false

  member private this.Register() =
    match box source with
    | :? IMapSinkRegistry as r ->
      r.AddMapSink(box(this :> IMapDeltaSink<'K, 'V>))
    | _ -> ()

  member private this.Unregister() =
    match box source with
    | :? IMapSinkRegistry as r ->
      r.RemoveMapSink(box(this :> IMapDeltaSink<'K, 'V>))
    | _ -> ()

  member private this.EnsureInitialized() =
    if not initialized then
      // Snapshot first, register between, then run the mapping over the
      // snapshot (see MapSetNode.EnsureInitialized in SetNodes.fs): the
      // mapping is user code that may write to the source, and the write
      // must land in our journal. A dirty source draining during the
      // snapshot read pushes to nobody: no double-apply. The flag is set
      // last: an exception leaves the node uninitialized.
      let snapshot = Dictionary<'K, 'V>()

      for KeyValue(k, v) in source.GetValue() do
        snapshot[k] <- v

      this.Register()
      Collections.loadMap mapping snapshot &state
      state.DepVersions[0] <- source.Version
      initialized <- true

  interface IMapDeltaSink<'K, 'V> with
    member this.OnDeltas
      (sets: struct ('K * 'V)[], setCnt: int, rems: 'K[], remCnt: int)
      =
      if not disposed then
        Collections.journalAppendMap &state.Journal sets setCnt rems remCnt
        state.Version <- state.Version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveMap<'K, 'U> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive map has been disposed."

        this.EnsureInitialized()

        if source.Version <> state.DepVersions[0] then
          source.GetValue() |> ignore
          state.DepVersions[0] <- source.Version

        if not state.Journal.IsEmpty then
          Collections.drainMapPush mapping &state

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Data :> IReadOnlyDictionary<'K, 'U>
      finally
        ctx.ReleaseOwner()

    member _.Version =
      // Dirty indicator (the MapLookupNode pattern): while the source
      // has unprocessed changes, report version + 1 so version-checking
      // consumers re-read; the re-read drains the journal and settles
      // the chain recursively. Without it a tail-only read of a 2+
      // level chain serves the stale value forever: a transform pushes
      // downstream only at its own read, so a plain version hides
      // upstream dirt from every gated consumer.
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

  interface IMapSinkRegistry with
    member this.AddMapSink(sink) = Collections.addSink &state.Sinks sink

    member this.RemoveMapSink(sink) =
      Collections.removeSink &state.Sinks sink

/// <summary>Keeps the entries of a map that satisfy a predicate.</summary>
type FilterMapNode<'K, 'V when 'K: equality>
  (source: IAdaptiveMap<'K, 'V>, [<InlineIfLambda>] predicate: 'K -> 'V -> bool)
  =
  let mapOpt = fun k v -> if predicate k v then ValueSome v else ValueNone
  let mutable state = MapNodeState<'K, 'V, 'V>.Create(1)
  let mutable initialized = false
  let mutable disposed = false

  member private this.Register() =
    match box source with
    | :? IMapSinkRegistry as r ->
      r.AddMapSink(box(this :> IMapDeltaSink<'K, 'V>))
    | _ -> ()

  member private this.Unregister() =
    match box source with
    | :? IMapSinkRegistry as r ->
      r.RemoveMapSink(box(this :> IMapDeltaSink<'K, 'V>))
    | _ -> ()

  member private this.EnsureInitialized() =
    if not initialized then
      // Snapshot first, register between (see MapMapNode.EnsureInitialized).
      // The flag is set last: an exception leaves the node uninitialized.
      let snapshot = Dictionary<'K, 'V>()

      for KeyValue(k, v) in source.GetValue() do
        snapshot[k] <- v

      this.Register()
      Collections.loadMap mapOpt snapshot &state
      state.DepVersions[0] <- source.Version
      initialized <- true

  interface IMapDeltaSink<'K, 'V> with
    member this.OnDeltas
      (sets: struct ('K * 'V)[], setCnt: int, rems: 'K[], remCnt: int)
      =
      if not disposed then
        Collections.journalAppendMap &state.Journal sets setCnt rems remCnt
        state.Version <- state.Version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveMap<'K, 'V> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive map has been disposed."

        this.EnsureInitialized()

        if source.Version <> state.DepVersions[0] then
          source.GetValue() |> ignore
          state.DepVersions[0] <- source.Version

        if not state.Journal.IsEmpty then
          Collections.drainMapPush mapOpt &state

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Data :> IReadOnlyDictionary<'K, 'V>
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

  interface IMapSinkRegistry with
    member this.AddMapSink(sink) = Collections.addSink &state.Sinks sink

    member this.RemoveMapSink(sink) =
      Collections.removeSink &state.Sinks sink

/// <summary>
/// Merges two maps with a mapping over both side values (voptions). The
/// mapping decides the semantics: choose2, intersect(With), union(With) are
/// all this node with different mappings (FDA models them all on
/// Choose2VReader). The mapping is called only when at least one side has a
/// value; the sides' current values are tracked per key.
/// </summary>
type Choose2MapNode<'K, 'V1, 'V2, 'V3 when 'K: equality>
  (
    left: IAdaptiveMap<'K, 'V1>,
    right: IAdaptiveMap<'K, 'V2>,
    [<InlineIfLambda>] mapping: 'K -> 'V1 voption -> 'V2 voption -> 'V3 voption
  ) =
  let deps: IAdaptiveObject[] = [|
    left :> IAdaptiveObject
    right :> IAdaptiveObject
  |]

  let mutable state = Collections.Choose2State<'K, 'V1, 'V2, 'V3>.Create(2)
  let mutable leftSink: obj = null
  let mutable rightSink: obj = null
  let mutable initialized = false
  let mutable disposed = false

  member private this.Register() =
    leftSink <- box(Collections.SideMapSink<'K, 'V1>(this, 0))

    rightSink <- box(Collections.SideMapSink<'K, 'V2>(this, 1))

    match box left with
    | :? IMapSinkRegistry as r -> r.AddMapSink(leftSink)
    | _ -> ()

    match box right with
    | :? IMapSinkRegistry as r -> r.AddMapSink(rightSink)
    | _ -> ()

  member private this.Unregister() =
    match box left with
    | :? IMapSinkRegistry as r -> r.RemoveMapSink(leftSink)
    | _ -> ()

    match box right with
    | :? IMapSinkRegistry as r -> r.RemoveMapSink(rightSink)
    | _ -> ()

  member private this.OnLeftDeltas
    (sets: struct ('K * 'V1)[], setCnt: int, rems: 'K[], remCnt: int)
    =
    if not disposed then
      Collections.journalAppendMap &state.JournalL sets setCnt rems remCnt
      state.Version <- state.Version + 1L
      GraphContext.Default.BumpWriteGeneration()

  member private this.OnRightDeltas
    (sets: struct ('K * 'V2)[], setCnt: int, rems: 'K[], remCnt: int)
    =
    if not disposed then
      Collections.journalAppendMap &state.JournalR sets setCnt rems remCnt
      state.Version <- state.Version + 1L
      GraphContext.Default.BumpWriteGeneration()

  interface Collections.ISideMapSinkTarget with
    member this.OnSideDeltas
      (side: int, sets: obj, setCnt: int, rems: obj, remCnt: int)
      =
      if not disposed then
        if side = 0 then
          Collections.journalAppendMap
            &state.JournalL
            (unbox sets)
            setCnt
            (unbox rems)
            remCnt
        else
          Collections.journalAppendMap
            &state.JournalR
            (unbox sets)
            setCnt
            (unbox rems)
            remCnt

        state.Version <- state.Version + 1L
        GraphContext.Default.BumpWriteGeneration()

  member private this.EnsureInitialized() =
    if not initialized then
      // Snapshot first, register between (see MapMapNode.EnsureInitialized).
      // The flag is set last: an exception leaves the node uninitialized.
      let leftSnapshot = Dictionary<'K, 'V1>()

      for KeyValue(k, v) in left.GetValue() do
        leftSnapshot[k] <- v

      let rightSnapshot = Dictionary<'K, 'V2>()

      for KeyValue(k, v) in right.GetValue() do
        rightSnapshot[k] <- v

      this.Register()
      Collections.loadChoose2 mapping leftSnapshot rightSnapshot &state
      state.DepVersions[0] <- left.Version
      state.DepVersions[1] <- right.Version
      initialized <- true

  interface IAdaptiveMap<'K, 'V3> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive map has been disposed."

        this.EnsureInitialized()

        for j in 0..1 do
          if deps[j].Version <> state.DepVersions[j] then
            if j = 0 then
              left.GetValue() |> ignore
            else
              right.GetValue() |> ignore

            state.DepVersions[j] <- deps[j].Version

        if not state.JournalL.IsEmpty || not state.JournalR.IsEmpty then
          Collections.drainChoose2Push mapping &state

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Out :> IReadOnlyDictionary<'K, 'V3>
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

  interface IMapSinkRegistry with
    member this.AddMapSink(sink) = Collections.addSink &state.Sinks sink

    member this.RemoveMapSink(sink) =
      Collections.removeSink &state.Sinks sink

/// <summary>Internal. State of a set-to-map node (one value per key).</summary>
[<Struct>]
type internal SetToMapState<'K, 'V, 'T when 'K: equality> =
  val mutable Version: int64
  val mutable Sinks: SinkList
  val mutable DepVersions: int64[]
  val mutable Data: Dictionary<'K, 'V>
  val mutable Journal: SetDelta<'T>
  val mutable Out: MapDelta<'K, 'V>

  new
    (
      version: int64,
      sinks: SinkList,
      depVersions: int64[],
      data: Dictionary<'K, 'V>,
      journal: SetDelta<'T>,
      out: MapDelta<'K, 'V>
    ) =
    {
      Version = version
      Sinks = sinks
      DepVersions = depVersions
      Data = data
      Journal = journal
      Out = out
    }

  static member Create(depCount: int) =
    SetToMapState(
      0L,
      SinkList.Create(),
      Array.zeroCreate depCount,
      Dictionary<'K, 'V>(),
      SetDelta<_>.Create(),
      MapDelta<_, _>.Create()
    )

/// <summary>
/// A map from a set: every element maps to an entry. When multiple elements
/// map to one key, the last value wins (<c>ofASetIgnoreDuplicates</c>); a
/// removal of an entry whose value is not the current one is a no-op (gated).
/// <c>mapSet</c> uses an unconditional removal (a set key appears once).
/// </summary>
type SetToMapNode<'K, 'V, 'T when 'K: equality and 'T: equality>
  (
    source: IAdaptiveSet<'T>,
    [<InlineIfLambda>] toEntry: 'T -> 'K * 'V,
    gated: bool
  ) =
  let mutable state = SetToMapState<'K, 'V, 'T>.Create(1)
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
      // Snapshot first, register between (see MapMapNode.EnsureInitialized).
      // The flag is set last: an exception leaves the node uninitialized.
      let snapshot = HashSet<'T>(source.GetValue())
      this.Register()

      for item in snapshot do
        let (k, v) = toEntry item
        state.Data[k] <- v

      state.DepVersions[0] <- source.Version
      initialized <- true

  interface ISetDeltaSink<'T> with
    member this.OnDeltas(adds: 'T[], addCnt: int, rems: 'T[], remCnt: int) =
      if not disposed then
        Collections.journalAppendSet &state.Journal adds addCnt rems remCnt
        state.Version <- state.Version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveMap<'K, 'V> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive map has been disposed."

        this.EnsureInitialized()

        if source.Version <> state.DepVersions[0] then
          source.GetValue() |> ignore
          state.DepVersions[0] <- source.Version

        if not state.Journal.IsEmpty then
          let ctx2 = GraphContext.Default
          let wasActive = ctx2.TxActive
          ctx2.TxActive <- true

          try
            let rems = state.Journal.Rems
            let adds = state.Journal.Adds
            let remStart = rems.Count
            let addStart = adds.Count
            let mutable changed = false
            let mutable i = 0

            while i < remStart do
              let item = rems.Items[i]
              let (k, v) = toEntry item
              let mutable old = Unchecked.defaultof<'V>

              if
                state.Data.TryGetValue(k, &old)
                && ((not gated) || EqualityComparer<'V>.Default.Equals(old, v))
              then
                state.Data.Remove k |> ignore
                state.Out.Rems <- Collections.bufferAppend state.Out.Rems k
                changed <- true

              i <- i + 1

            i <- 0

            while i < addStart do
              let item = adds.Items[i]
              let (k, v) = toEntry item
              let mutable old = Unchecked.defaultof<'V>

              if
                state.Data.TryGetValue(k, &old)
                && EqualityComparer<'V>.Default.Equals(old, v)
              then
                ()
              else
                state.Data[k] <- v

                state.Out.Sets <-
                  Collections.bufferAppend state.Out.Sets (struct (k, v))

                changed <- true

              i <- i + 1

            let remLive = state.Journal.Rems.Count

            if remLive > remStart then
              Array.Copy(
                state.Journal.Rems.Items,
                remStart,
                state.Journal.Rems.Items,
                0,
                remLive - remStart
              )

              state.Journal.Rems.Count <- remLive - remStart
            else
              state.Journal.Rems.Count <- 0

            let addLive = state.Journal.Adds.Count

            if addLive > addStart then
              Array.Copy(
                state.Journal.Adds.Items,
                addStart,
                state.Journal.Adds.Items,
                0,
                addLive - addStart
              )

              state.Journal.Adds.Count <- addLive - addStart
            else
              state.Journal.Adds.Count <- 0

            if changed then
              Collections.pushMapDelta &state.Sinks state.Out
              state.Out.Clear()
          finally
            ctx2.TxActive <- wasActive

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Data :> IReadOnlyDictionary<'K, 'V>
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

  interface IMapSinkRegistry with
    member this.AddMapSink(sink) = Collections.addSink &state.Sinks sink

    member this.RemoveMapSink(sink) =
      Collections.removeSink &state.Sinks sink

/// <summary>Internal. State of a keep-all set-to-map node (per-key value sets).</summary>
[<Struct>]
type internal SetToMapKeepAllState<'K, 'V, 'T when 'K: equality> =
  val mutable Version: int64
  val mutable Sinks: SinkList
  val mutable DepVersions: int64[]
  val mutable Data: Dictionary<'K, HashSet<'V>>
  val mutable Journal: SetDelta<'T>
  val mutable Out: MapDelta<'K, HashSet<'V>>

  new
    (
      version: int64,
      sinks: SinkList,
      depVersions: int64[],
      data: Dictionary<'K, HashSet<'V>>,
      journal: SetDelta<'T>,
      out: MapDelta<'K, HashSet<'V>>
    ) =
    {
      Version = version
      Sinks = sinks
      DepVersions = depVersions
      Data = data
      Journal = journal
      Out = out
    }

  static member Create(depCount: int) =
    SetToMapKeepAllState(
      0L,
      SinkList.Create(),
      Array.zeroCreate depCount,
      Dictionary<'K, HashSet<'V>>(),
      SetDelta<_>.Create(),
      MapDelta<_, _>.Create()
    )

/// <summary>
/// A map from a set of entries: every key keeps ALL its values in a HashSet
/// (<c>ofASet</c>/<c>ofASetMapped</c> FDA parity). A changed value set emits a
/// fresh HashSet in the delta (reference identity: downstream nodes compare
/// stored values by equality).
/// </summary>
type SetToMapKeepAllNode<'K, 'V, 'T when 'K: equality and 'T: equality>
  (source: IAdaptiveSet<'T>, [<InlineIfLambda>] toEntry: 'T -> 'K * 'V) =
  let mutable state = SetToMapKeepAllState<'K, 'V, 'T>.Create(1)
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
      // Snapshot first, register between (see MapMapNode.EnsureInitialized).
      // The flag is set last: an exception leaves the node uninitialized.
      let snapshot = HashSet<'T>(source.GetValue())
      this.Register()

      for item in snapshot do
        let (k, v) = toEntry item
        let mutable set = Unchecked.defaultof<HashSet<'V>>

        if state.Data.TryGetValue(k, &set) then
          set.Add v |> ignore
        else
          let fresh = HashSet<'V>()
          fresh.Add v |> ignore
          state.Data[k] <- fresh

      state.DepVersions[0] <- source.Version
      initialized <- true

  interface ISetDeltaSink<'T> with
    member this.OnDeltas(adds: 'T[], addCnt: int, rems: 'T[], remCnt: int) =
      if not disposed then
        Collections.journalAppendSet &state.Journal adds addCnt rems remCnt
        state.Version <- state.Version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveMap<'K, HashSet<'V>> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive map has been disposed."

        this.EnsureInitialized()

        if source.Version <> state.DepVersions[0] then
          source.GetValue() |> ignore
          state.DepVersions[0] <- source.Version

        if not state.Journal.IsEmpty then
          let ctx2 = GraphContext.Default
          let wasActive = ctx2.TxActive
          ctx2.TxActive <- true

          try
            let rems = state.Journal.Rems
            let adds = state.Journal.Adds
            let remStart = rems.Count
            let addStart = adds.Count
            let mutable changed = false
            let mutable i = 0

            while i < remStart do
              let item = rems.Items[i]
              let (k, v) = toEntry item
              let mutable set = Unchecked.defaultof<HashSet<'V>>

              if state.Data.TryGetValue(k, &set) && set.Remove v then
                if set.Count = 0 then
                  state.Data.Remove k |> ignore
                  state.Out.Rems <- Collections.bufferAppend state.Out.Rems k
                else
                  // The value set changed: emit a fresh set.
                  state.Out.Sets <-
                    Collections.bufferAppend
                      state.Out.Sets
                      (struct (k, HashSet<'V>(set)))

                changed <- true

              i <- i + 1

            i <- 0

            while i < addStart do
              let item = adds.Items[i]
              let (k, v) = toEntry item
              let mutable set = Unchecked.defaultof<HashSet<'V>>

              if state.Data.TryGetValue(k, &set) then
                if set.Add v then
                  state.Out.Sets <-
                    Collections.bufferAppend
                      state.Out.Sets
                      (struct (k, HashSet<'V>(set)))

                  changed <- true
              else
                let fresh = HashSet<'V>()
                fresh.Add v |> ignore
                state.Data[k] <- fresh

                state.Out.Sets <-
                  Collections.bufferAppend state.Out.Sets (struct (k, fresh))

                changed <- true

              i <- i + 1

            let remLive = state.Journal.Rems.Count

            if remLive > remStart then
              Array.Copy(
                state.Journal.Rems.Items,
                remStart,
                state.Journal.Rems.Items,
                0,
                remLive - remStart
              )

              state.Journal.Rems.Count <- remLive - remStart
            else
              state.Journal.Rems.Count <- 0

            let addLive = state.Journal.Adds.Count

            if addLive > addStart then
              Array.Copy(
                state.Journal.Adds.Items,
                addStart,
                state.Journal.Adds.Items,
                0,
                addLive - addStart
              )

              state.Journal.Adds.Count <- addLive - addStart
            else
              state.Journal.Adds.Count <- 0

            if changed then
              Collections.pushMapDelta &state.Sinks state.Out
              state.Out.Clear()
          finally
            ctx2.TxActive <- wasActive

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Data :> IReadOnlyDictionary<'K, HashSet<'V>>
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

  interface IMapSinkRegistry with
    member this.AddMapSink(sink) = Collections.addSink &state.Sinks sink

    member this.RemoveMapSink(sink) =
      Collections.removeSink &state.Sinks sink

/// <summary>Internal. State of a map-to-set node (keys or distinct values).</summary>
[<Struct>]
type internal MapToSetState<'K, 'V, 'T when 'K: equality and 'T: equality> =
  val mutable Version: int64
  val mutable Sinks: SinkList
  val mutable DepVersions: int64[]
  val mutable Mirror: Dictionary<'K, 'T>
  val mutable Out: RefCountedSet<'T>
  val mutable Journal: MapDelta<'K, 'V>
  val mutable OutDelta: SetDelta<'T>

  new
    (
      version: int64,
      sinks: SinkList,
      depVersions: int64[],
      mirror: Dictionary<'K, 'T>,
      out: RefCountedSet<'T>,
      journal: MapDelta<'K, 'V>,
      outDelta: SetDelta<'T>
    ) =
    {
      Version = version
      Sinks = sinks
      DepVersions = depVersions
      Mirror = mirror
      Out = out
      Journal = journal
      OutDelta = outDelta
    }

  static member Create(depCount: int) =
    MapToSetState(
      0L,
      SinkList.Create(),
      Array.zeroCreate depCount,
      Dictionary<'K, 'T>(),
      RefCountedSet.Create(),
      MapDelta<_, _>.Create(),
      SetDelta<_>.Create()
    )

/// <summary>
/// A set from a map: every entry contributes the selected value (the key for
/// <c>toASet</c>, the value for <c>toASetValues</c>). Equal selections share
/// one reference count: an entry removal drops the output element only when
/// the last contributing entry disappears.
/// </summary>
type MapToSetNode<'K, 'V, 'T when 'K: equality and 'T: equality>
  (source: IAdaptiveMap<'K, 'V>, [<InlineIfLambda>] select: 'K -> 'V -> 'T) =
  let mutable state = MapToSetState<'K, 'V, 'T>.Create(1)
  let mutable initialized = false
  let mutable disposed = false

  member private this.Register() =
    match box source with
    | :? IMapSinkRegistry as r ->
      r.AddMapSink(box(this :> IMapDeltaSink<'K, 'V>))
    | _ -> ()

  member private this.Unregister() =
    match box source with
    | :? IMapSinkRegistry as r ->
      r.RemoveMapSink(box(this :> IMapDeltaSink<'K, 'V>))
    | _ -> ()

  member private this.EnsureInitialized() =
    if not initialized then
      // Snapshot first, register between (see MapMapNode.EnsureInitialized).
      // The flag is set last: an exception leaves the node uninitialized.
      let snapshot = Dictionary<'K, 'V>()

      for KeyValue(k, v) in source.GetValue() do
        snapshot[k] <- v

      this.Register()

      for KeyValue(k, v) in snapshot do
        let t = select k v
        state.Mirror[k] <- t
        let struct (out2, _) = Collections.refAdd state.Out t
        state.Out <- out2

      state.DepVersions[0] <- source.Version
      initialized <- true

  interface IMapDeltaSink<'K, 'V> with
    member this.OnDeltas
      (sets: struct ('K * 'V)[], setCnt: int, rems: 'K[], remCnt: int)
      =
      if not disposed then
        Collections.journalAppendMap &state.Journal sets setCnt rems remCnt
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
          state.DepVersions[0] <- source.Version

        if not state.Journal.IsEmpty then
          let ctx2 = GraphContext.Default
          let wasActive = ctx2.TxActive
          ctx2.TxActive <- true

          try
            let rems = state.Journal.Rems
            let sets = state.Journal.Sets
            let remStart = rems.Count
            let setStart = sets.Count
            let mutable changed = false
            let mutable i = 0

            while i < remStart do
              let k = rems.Items[i]
              let mutable t = Unchecked.defaultof<'T>

              if state.Mirror.TryGetValue(k, &t) then
                state.Mirror.Remove k |> ignore
                let struct (out2, removed) = Collections.refRemove state.Out t
                state.Out <- out2

                if removed then
                  state.OutDelta.Rems <-
                    Collections.bufferAppend state.OutDelta.Rems t

                  changed <- true

              i <- i + 1

            i <- 0

            while i < setStart do
              let struct (k, v) = sets.Items[i]
              let t = select k v
              let mutable old = Unchecked.defaultof<'T>

              if
                state.Mirror.TryGetValue(k, &old)
                && EqualityComparer<'T>.Default.Equals(old, t)
              then
                ()
              else
                if state.Mirror.TryGetValue(k, &old) then
                  let struct (out2, removed) =
                    Collections.refRemove state.Out old

                  state.Out <- out2

                  if removed then
                    state.OutDelta.Rems <-
                      Collections.bufferAppend state.OutDelta.Rems old

                    changed <- true

                state.Mirror[k] <- t
                let struct (out2, added) = Collections.refAdd state.Out t
                state.Out <- out2

                if added then
                  state.OutDelta.Adds <-
                    Collections.bufferAppend state.OutDelta.Adds t

                  changed <- true

              i <- i + 1

            let remLive = state.Journal.Rems.Count

            if remLive > remStart then
              Array.Copy(
                state.Journal.Rems.Items,
                remStart,
                state.Journal.Rems.Items,
                0,
                remLive - remStart
              )

              state.Journal.Rems.Count <- remLive - remStart
            else
              state.Journal.Rems.Count <- 0

            let setLive = state.Journal.Sets.Count

            if setLive > setStart then
              Array.Copy(
                state.Journal.Sets.Items,
                setStart,
                state.Journal.Sets.Items,
                0,
                setLive - setStart
              )

              state.Journal.Sets.Count <- setLive - setStart
            else
              state.Journal.Sets.Count <- 0

            if changed then
              Collections.pushSetDelta &state.Sinks state.OutDelta
              state.OutDelta.Clear()
          finally
            ctx2.TxActive <- wasActive

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Out.Data :> IReadOnlySet<'T>
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

/// <summary>
/// An adaptive map over an adaptive value of a sequence of entries. Every
/// change of the value replaces the whole state and emits the diff as the
/// delta (the rebuild boundary, like <see cref="OfAvalSetNode"/>).
/// </summary>
type OfAvalMapNode<'K, 'V, 'S when 'K: equality and 'S :> seq<'K * 'V>>
  (value: IAdaptiveValue<'S>) =
  let mutable state = MapNodeState<'K, 'V, 'V>.Create(1)
  let mutable initialized = false
  let mutable disposed = false

  member private this.EnsureInitialized() =
    if not initialized then
      // Initial load: materialize the value and build the state.
      // The flag is set last: an exception leaves the node uninitialized.
      // The init diff is not pushed: clear the out buffer so it cannot
      // pollute the first real delta.
      let next = Dictionary<'K, 'V>()

      for (k, v) in value.GetValue() do
        next[k] <- v

      Collections.rebuildMapDiff next &state |> ignore
      state.Out.Clear()
      state.DepVersions[0] <- value.Version
      initialized <- true

  /// Re-read the value when it changed and emit the diff. Called from
  /// GetValue and from the Version getter (poll model, like
  /// <see cref="CustomSetNode"/>): a downstream re-pulls only when this
  /// node's version moves, so the version must advance on the read path.
  member private this.Poll() =
    if value.Version <> state.DepVersions[0] then
      // The value may yield a transient seq: materialize it.
      let next = Dictionary<'K, 'V>()

      for (k, v) in value.GetValue() do
        next[k] <- v

      if Collections.rebuildMapDiff next &state then
        // The version must advance: downstream nodes re-pull the
        // source only when it changed (a stuck version makes
        // derived nodes stale forever).
        state.Version <- state.Version + 1L
        Collections.pushAndBumpMap GraphContext.Current state.Out &state.Sinks
        state.Out.Clear()

      state.DepVersions[0] <- value.Version

  interface IAdaptiveMap<'K, 'V> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive map has been disposed."

        this.EnsureInitialized()
        this.Poll()
        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Data :> IReadOnlyDictionary<'K, 'V>
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

  interface IMapSinkRegistry with
    member this.AddMapSink(sink) = Collections.addSink &state.Sinks sink

    member this.RemoveMapSink(sink) =
      Collections.removeSink &state.Sinks sink

/// <summary>
/// An adaptive map whose content is driven by a compute function (FDA
/// <c>AMap.custom</c> parity, pull model like <see cref="CustomSetNode"/>).
/// The compute receives the current view and a delta builder and appends the
/// operations that describe the change since the previous call.
/// </summary>
type CustomMapNode<'K, 'V when 'K: equality>
  ([<InlineIfLambda>] compute:
    IReadOnlyDictionary<'K, 'V> -> MapDeltaBuilder<'K, 'V> -> unit) =
  let mutable state = MapNodeState<'K, 'V, 'V>.Create(0)
  let writer = MapDeltaBuilder<'K, 'V>()
  let mutable disposed = false

  member private this.Poll() =
    if not disposed then
      writer.Clear()
      compute (state.Data :> IReadOnlyDictionary<'K, 'V>) writer

      if not writer.IsEmpty then
        let sets = writer.Sets
        let rems = writer.Rems

        for i in 0 .. sets.Count - 1 do
          let struct (k, v) = sets.Items[i]
          state.Data[k] <- v

        for i in 0 .. rems.Count - 1 do
          state.Data.Remove rems.Items[i] |> ignore

        state.Version <- state.Version + 1L

        Collections.pushAndBumpMap
          GraphContext.Current
          (writer.Snapshot())
          &state.Sinks

        writer.Clear()

  interface IAdaptiveMap<'K, 'V> with
    member this.GetValue() =
      let ctx = GraphContext.Default
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
/// An adaptive map bound to a scalar value (<c>AMap.bind</c>, PLAN.md Section
/// 7.4): <c>mapping value</c> selects the inner map; when the value changes, the
/// whole inner map is swapped (old content removed, new content added) and the
/// old inner sink is unregistered eagerly (FDA <c>BindReader</c> semantics;
/// ANALYSIS-FDA.md Pitfall 1). The inner map's own changes flow through a
/// journal. Registration is lazy (first read); disposal unregisters everything.
/// </summary>
type BindMapNode<'K, 'V, 'T when 'K: equality>
  (
    value: IAdaptiveValue<'T>,
    [<InlineIfLambda>] mapping: 'T -> IAdaptiveMap<'K, 'V>
  ) =
  let mutable state = Collections.BindMapState<'K, 'V>.Create(1)

  let mutable inner: IAdaptiveMap<'K, 'V> =
    Unchecked.defaultof<IAdaptiveMap<'K, 'V>>

  let mutable hasInner = false
  let mutable innerVersion = 0L
  let mutable current: 'T = Unchecked.defaultof<'T>
  let mutable initialized = false
  let mutable disposed = false

  member private this.UnregisterInner() =
    if hasInner then
      match box inner with
      | :? IMapSinkRegistry as r -> r.RemoveMapSink(box this)
      | _ -> ()

      hasInner <- false

  member private this.LoadInner() =
    // Read first, register after: the view is complete, and the sink sees
    // only deltas that follow this point in time.
    let view = inner.GetValue()

    for KeyValue(k, v) in view do
      state.Data[k] <- v

    match box inner with
    | :? IMapSinkRegistry as r -> r.AddMapSink(box this)
    | _ -> ()

    hasInner <- true
    innerVersion <- inner.Version

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
      state.DepVersions[0] <- value.Version
      initialized <- true

  interface IMapDeltaSink<'K, 'V> with
    member this.OnDeltas
      (sets: struct ('K * 'V)[], setCnt: int, rems: 'K[], remCnt: int)
      =
      if not disposed then
        Collections.journalAppendMap &state.Journal sets setCnt rems remCnt
        state.Version <- state.Version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveMap<'K, 'V> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive map has been disposed."

        this.EnsureInitialized()

        if value.Version <> state.DepVersions[0] then
          let next = value.GetValue()
          state.DepVersions[0] <- value.Version

          if not(EqualityComparer<'T>.Default.Equals(current, next)) then
            this.SwapTo(next)

        if inner.Version <> innerVersion then
          inner.GetValue() |> ignore
          innerVersion <- inner.Version

        if not state.Journal.IsEmpty then
          Collections.drainBindMapPush &state

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Data :> IReadOnlyDictionary<'K, 'V>
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

  interface IMapSinkRegistry with
    member this.AddMapSink(sink) = Collections.addSink &state.Sinks sink

    member this.RemoveMapSink(sink) =
      Collections.removeSink &state.Sinks sink

/// <summary>
/// An adaptive map of a list of entries (FDA <c>AMap.ofAList</c> parity). The
/// list deltas are converted to map deltas: an insert or update sets the key,
/// a remove drops the key. The mirror (key per input position) is aligned
/// with the source; a key update replaces the entry in place.
/// </summary>
type AListToMapNode<'K, 'V when 'K: equality>(source: IAdaptiveList<'K * 'V>) =
  let mutable version = 0L
  let mutable sinks = SinkList.Create()
  let mutable depVersion = 0L
  let mutable initialized = false
  let mutable disposed = false
  let mutable output = Dictionary<'K, 'V>()
  let mutable mirror = ResizeArray<'K * 'V>()
  let mutable journal = ListDelta<'K * 'V>.Create()
  let mutable out = MapDelta<'K, 'V>.Create()

  member private this.Register() =
    match box source with
    | :? IListSinkRegistry as r ->
      r.AddListSink(box(this :> IListDeltaSink<'K * 'V>))
    | _ -> ()

  member private this.Unregister() =
    match box source with
    | :? IListSinkRegistry as r ->
      r.RemoveListSink(box(this :> IListDeltaSink<'K * 'V>))
    | _ -> ()

  member private this.EnsureInitialized() =
    if not initialized then
      let snapshot = ResizeArray<'K * 'V>(source.GetValue())
      this.Register()

      for kv in snapshot do
        mirror.Add kv
        output[fst kv] <- snd kv

      depVersion <- source.Version
      initialized <- true

  member private this.Drain() =
    if not journal.IsEmpty then
      out.Clear()
      let ops = journal.Ops.Items
      let cnt = journal.Ops.Count

      for i in 0 .. cnt - 1 do
        let op = ops[i]
        let p = op.Position

        match op.Kind with
        | ListOpKind.Insert ->
          mirror.Insert(p, op.Value)
          output[fst op.Value] <- snd op.Value

          out.Sets <-
            Collections.bufferAppend
              out.Sets
              (struct (fst op.Value, snd op.Value))
        | ListOpKind.Remove ->
          let k = fst mirror[p]
          mirror.RemoveAt p
          output.Remove k |> ignore
          out.Rems <- Collections.bufferAppend out.Rems k
        | _ -> // Update
          let (oldK, _) = mirror[p]
          mirror[p] <- op.Value

          if EqualityComparer<'K>.Default.Equals(oldK, fst op.Value) then
            output[fst op.Value] <- snd op.Value

            out.Sets <-
              Collections.bufferAppend
                out.Sets
                (struct (fst op.Value, snd op.Value))
          else
            output.Remove oldK |> ignore
            out.Rems <- Collections.bufferAppend out.Rems oldK
            output[fst op.Value] <- snd op.Value

            out.Sets <-
              Collections.bufferAppend
                out.Sets
                (struct (fst op.Value, snd op.Value))

      journal.Ops.Count <- 0
      version <- version + 1L
      Collections.pushAndBumpMap GraphContext.Current out &sinks

  interface IListDeltaSink<'K * 'V> with
    member this.OnDeltas(ops: ListOp<'K * 'V>[], opCnt: int) =
      if not disposed then
        Collections.journalAppendList &journal ops opCnt
        version <- version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveMap<'K, 'V> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive map has been disposed."

        this.EnsureInitialized()

        if source.Version <> depVersion then
          source.GetValue() |> ignore
          depVersion <- source.Version

        if not journal.IsEmpty then
          this.Drain()

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        output :> IReadOnlyDictionary<'K, 'V>
      finally
        ctx.ReleaseOwner()

    member _.Version =
      // Dirty indicator (see MapMapNode.Version).
      if source.Version <> depVersion then
        version + 1L
      else
        version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        this.Unregister()
        Collections.clearSinks &sinks

  interface IMapSinkRegistry with
    member this.AddMapSink(sink) = Collections.addSink &sinks sink

    member this.RemoveMapSink(sink) = Collections.removeSink &sinks sink

/// <summary>
/// An adaptive list of a map's entries (FDA <c>AMap.toAList</c> parity, poll
/// node). The order is the map's iteration order, stable while the map does
/// not change; every read rebuilds and emits the positional diff.
/// </summary>
type MapToAListNode<'K, 'V when 'K: equality>(source: IAdaptiveMap<'K, 'V>) =
  let mutable data = ResizeArray<'K * 'V>()
  let mutable out = ListDelta<'K * 'V>.Create()
  let mutable version = 0L
  let mutable sinks = SinkList.Create()
  let mutable disposed = false

  member private this.Poll() =
    if not disposed then
      let next = ResizeArray<'K * 'V>()

      for KeyValue(k, v) in source.GetValue() do
        next.Add(k, v)

      if Collections.rebuildListDiff next data &out then
        version <- version + 1L
        Collections.pushAndBumpList GraphContext.Current out &sinks

      out.Clear()

  interface IAdaptiveList<'K * 'V> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive list has been disposed."

        this.Poll()
        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        data :> IReadOnlyList<'K * 'V>
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

/// <summary>
/// Maps every entry, disposing the mapped value when its key leaves (FDA
/// <c>AMap.mapUse</c> parity). The mapped values are stable (the mapping runs
/// once per key). Disposing the node disposes all live mapped values and
/// clears the output.
/// </summary>
type MapUseMapNode<'K, 'V, 'W
  when 'K: equality and 'W: equality and 'W :> IDisposable>
  (source: IAdaptiveMap<'K, 'V>, [<InlineIfLambda>] mapping: 'K -> 'V -> 'W) =
  let mutable state = MapNodeState<'K, 'V, 'W>.Create(1)
  // Key -> its mapped value.
  let mapped = Dictionary<'K, 'W>()
  let mutable initialized = false
  let mutable disposed = false

  member private this.Register() =
    match box source with
    | :? IMapSinkRegistry as r ->
      r.AddMapSink(box(this :> IMapDeltaSink<'K, 'V>))
    | _ -> ()

  member private this.Unregister() =
    match box source with
    | :? IMapSinkRegistry as r ->
      r.RemoveMapSink(box(this :> IMapDeltaSink<'K, 'V>))
    | _ -> ()

  member private this.EnsureInitialized() =
    if not initialized then
      // Snapshot first, register between, then map the snapshot (the
      // FilterMapNode convention): the mapping is user code that may
      // write to the source, and the write must land in our journal.
      let snapshot = Dictionary<'K, 'V>(source.GetValue())
      this.Register()

      for KeyValue(k, v) in snapshot do
        let w = mapping k v
        mapped[k] <- w
        state.Data[k] <- w

      state.DepVersions[0] <- source.Version
      initialized <- true

  member private this.Drain() =
    // Removals first: the keys are gone, their values are disposed.
    let rems = state.Journal.Rems

    for i in 0 .. rems.Count - 1 do
      let k = rems.Items[i]

      if mapped.TryGetValue k |> fst then
        let w = mapped[k]
        mapped.Remove k |> ignore
        w.Dispose()
        state.Data.Remove k |> ignore
        state.Out.Rems <- Collections.bufferAppend state.Out.Rems k

    let sets = state.Journal.Sets

    for i in 0 .. sets.Count - 1 do
      let struct (k, v) = sets.Items[i]
      let w = mapping k v
      mapped[k] <- w
      state.Data[k] <- w
      state.Out.Sets <- Collections.bufferAppend state.Out.Sets (struct (k, w))

    state.Journal.Clear()
    state.Version <- state.Version + 1L
    Collections.pushAndBumpMap GraphContext.Current state.Out &state.Sinks
    state.Out.Clear()

  interface IMapDeltaSink<'K, 'V> with
    member this.OnDeltas
      (sets: struct ('K * 'V)[], setCnt: int, rems: 'K[], remCnt: int)
      =
      if not disposed then
        Collections.journalAppendMap &state.Journal sets setCnt rems remCnt
        state.Version <- state.Version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveMap<'K, 'W> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive map has been disposed."

        this.EnsureInitialized()

        if source.Version <> state.DepVersions[0] then
          source.GetValue() |> ignore
          state.DepVersions[0] <- source.Version

        if not state.Journal.IsEmpty then
          this.Drain()

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) state.Version
        state.Data :> IReadOnlyDictionary<'K, 'W>
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

        for KeyValue(_, w) in mapped do
          w.Dispose()

        mapped.Clear()
        state.Data.Clear()

  interface IMapSinkRegistry with
    member this.AddMapSink(sink) = Collections.addSink &state.Sinks sink

    member this.RemoveMapSink(sink) =
      Collections.removeSink &state.Sinks sink

// =============================================================================
// Scalar escape hatches: per-key lookup and incremental count.
//
// Unlike the transform nodes above, these nodes produce an aval, not an amap.
// They register nothing: writes only bump the source version (O(1)) and the
// node re-syncs at its next read — the delta is never scanned at write time,
// so a churned node population (fresh node per operation per frame) costs
// writers nothing. The per-key gate runs at read time: the key's current
// value is re-read and the node advances its own version only when its
// output actually changed. This is the per-key precision that the plain
// AdaptiveNode-over-GetValue pattern cannot express (the whole-map version
// and the flat dependency carry no key information).
//
// Pull-only protocol: no parent edges, no write-time marking, no delivery.
// The version bump in the read-time re-sync is the only notification;
// consumers version-check the node and recompute only when the watched
// output moved. Direct changeable sources make the re-sync O(1) (the map is
// materialized); pull-lazy derived sources re-read at most once per
// upstream change (the source drain runs during the re-sync — the
// documented depth-2 rule), and the dirty indicator (version + 1) makes the
// consumer re-read exactly once and the gate run at the next read.
// =============================================================================

/// <summary>
/// A per-key lookup over an adaptive map (the node behind <c>AMap.tryFind</c>
/// and <c>AMap.find</c>). Registers nothing; the key's current value is
/// re-read at the next read after a write, and the version advances only when
/// the value at that key actually changed (read-time equality gate). Writes
/// to unrelated keys cost this node and its consumers nothing — and the
/// write itself costs nothing either (no sink registration, no delivery).
/// </summary>
/// <remarks>
/// The re-sync re-reads the source view and applies the key lookup; it
/// matches <c>MapReduceNode.Drain</c>'s application order trivially (a set
/// wins for a key that appears in both, which a net delta never produces).
/// </remarks>
type MapLookupNode<'K, 'V when 'K: equality>
  (source: IAdaptiveMap<'K, 'V>, key: 'K) =
  let mutable version = 0L
  let mutable depVersion = 0L
  let mutable initialized = false
  let mutable disposed = false
  let mutable value = ValueNone

  /// Lazy re-sync: the node registers nothing, so no write ever delivers
  /// to it. The key's current value is re-read and the per-key gate runs
  /// here, at read time — a write to an unrelated key costs nobody at all
  /// (the old write-time delivery iterated every registered sink, so a
  /// churned node population paid on every write).
  member private this.Resync() =
    let before = value
    let view = source.GetValue()
    let mutable v = Unchecked.defaultof<'V>

    value <- if view.TryGetValue(key, &v) then ValueSome v else ValueNone

    if not(EqualityComparer<'V voption>.Default.Equals(before, value)) then
      version <- version + 1L

    depVersion <- source.Version

  interface IAdaptiveValue<'V voption> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive value has been disposed."

        // Internal source reads (the init snapshot, the re-sync
        // below) are machinery of this node, not dependencies
        // of the consumer: suppress collection so the caller's frame
        // sees only this node. Without this, the whole-map
        // dependency leaks into the consumer's frame and defeats the
        // per-key gate.
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
            // Snapshot first: there is no registration anymore,
            // so there is nothing to order against.
            this.Resync()
            initialized <- true
          elif source.Version <> depVersion then
            this.Resync()
        finally
          if wasCollecting then
            collector.PopFrame() |> ignore

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        value
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
/// A count over an adaptive map, projected through <paramref name="view"/>
/// (the node behind <c>AMap.count</c> with <c>id</c> and <c>AMap.isEmpty</c>
/// with <c>fun c -&gt; c = 0</c>). Registers nothing: the count is re-read at
/// the next read after a write (O(1) for a materialized source); the version
/// advances only when the projected output changed, so an update of an
/// existing key (no count change) or a count change that the projection
/// collapses (2 -&gt; 3 under isEmpty) costs this node and its consumers
/// nothing.
/// </summary>
/// <remarks>
/// No mirror is kept: the source view's <c>Count</c> is exact (it already
/// distinguishes a Set on a new key from an update of an existing one).
/// The view projection runs at re-sync time: keep it cheap (the built-in
/// uses are <c>id</c> and <c>fun c -&gt; c = 0</c>).
/// </remarks>
type MapCountNode<'K, 'V, 'Out when 'K: equality>
  (source: IAdaptiveMap<'K, 'V>, [<InlineIfLambda>] view: int -> 'Out) =
  let mutable version = 0L
  let mutable depVersion = 0L
  let mutable initialized = false
  let mutable disposed = false
  let mutable out = Unchecked.defaultof<'Out>

  /// Lazy re-sync (see MapLookupNode): re-read the count, project, and
  /// bump only when the projected output moved.
  member private this.Resync() =
    let before = out
    let data = Collections.asDictionary(source.GetValue())
    let nextOut = view data.Count

    if not(EqualityComparer<'Out>.Default.Equals(before, nextOut)) then
      out <- nextOut
      version <- version + 1L

    depVersion <- source.Version

  interface IAdaptiveValue<'Out> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive value has been disposed."

        // Internal source reads are machinery of this node, not
        // dependencies of the consumer (see MapLookupNode.GetValue).
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
      // Dirty indicator (see MapLookupNode.Version).
      if source.Version <> depVersion then
        version + 1L
      else
        version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true

// =============================================================================
// The groupBy node family (docs/2026-08-10-JOIN-DESIGN.md)
//
// AMap.groupBy — every group is a live adaptive map (a GroupMapChildNode)
// owned by the GroupByMapNode. The groupBy drain routes source deltas into
// the children by computed group key; the children deliver their own deltas
// to their own consumers. A group disappears when it becomes empty (removed
// at the next drain). A key whose value changes group is moved (removed from
// the old child, added to the new one): the per-key group is tracked in a
// memberGroup map, because a remove delta carries no value to compute the
// group from.
// =============================================================================

/// <summary>
/// A live per-group map (the value of a <c>AMap.groupBy</c> output entry).
/// The owning <see cref="GroupByMapNode&lt;'K,'V,'G&gt;"/> is its only
/// writer: it applies one set/remove entry at a time (equal-value elision)
/// and delivers the delta to its own consumers. The node is garbage
/// collected with its group (weak sink references, no manual disposal).
/// </summary>
type internal GroupMapChildNode<'K, 'V when 'K: equality>() =
  let mutable data = Dictionary<'K, 'V>()
  let mutable version = 0L
  let mutable sinks = SinkList.Create()
  let mutable out = MapDelta<'K, 'V>.Create()
  let mutable disposed = false

  member _.Count = data.Count

  /// Apply one set entry (equal-value elision) and deliver the delta.
  member internal this.ApplySetOne(k: 'K, v: 'V) =
    if not disposed then
      out.Clear()
      let mutable old = Unchecked.defaultof<'V>

      if
        not(
          data.TryGetValue(k, &old)
          && EqualityComparer<'V>.Default.Equals(old, v)
        )
      then
        data[k] <- v
        out.Sets <- Collections.bufferAppend out.Sets (struct (k, v))

      if not out.IsEmpty then
        version <- version + 1L
        Collections.pushMapDelta &sinks out
        GraphContext.Default.BumpWriteGeneration()

  /// Apply one remove entry and deliver the delta.
  member internal this.ApplyRemOne(k: 'K) =
    if not disposed then
      out.Clear()

      if data.Remove k then
        out.Rems <- Collections.bufferAppend out.Rems k

      if not out.IsEmpty then
        version <- version + 1L
        Collections.pushMapDelta &sinks out
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveMap<'K, 'V> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive map has been disposed."

        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        data :> IReadOnlyDictionary<'K, 'V>
      finally
        ctx.ReleaseOwner()

    member _.Version = version

  interface IMapSinkRegistry with
    member this.AddMapSink(sink) = Collections.addSink &sinks sink

    member this.RemoveMapSink(sink) = Collections.removeSink &sinks sink

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        Collections.clearSinks &sinks

/// <summary>
/// Groups the entries of an adaptive map by a computed key (the node behind
/// <c>AMap.groupBy</c>). The output entries are live adaptive maps
/// (<see cref="GroupMapChildNode&lt;'K,'V&gt;"/>); group-content changes
/// reach the consumers through the children (their deltas and versions), so
/// the output map's version moves only for group add/remove. A group
/// disappears when it becomes empty (removed at the next drain); a key whose
/// value changes group is moved between children.
/// </summary>
type GroupByMapNode<'K, 'V, 'G when 'K: equality and 'G: equality>
  (source: IAdaptiveMap<'K, 'V>, [<InlineIfLambda>] keyOf: 'K -> 'V -> 'G) =
  let mutable journal = MapDelta<'K, 'V>.Create()
  let mutable output = Dictionary<'G, amap<'K, 'V>>()
  let mutable groups = Dictionary<'G, GroupMapChildNode<'K, 'V>>()
  let mutable memberGroup = Dictionary<'K, 'G>()
  let mutable out = MapDelta<'G, amap<'K, 'V>>.Create()
  let mutable sinks = SinkList.Create()
  let mutable version = 0L
  let mutable depVersion = 0L
  let mutable initialized = false
  let mutable disposed = false

  member private this.Register() =
    match box source with
    | :? IMapSinkRegistry as r ->
      r.AddMapSink(box(this :> IMapDeltaSink<'K, 'V>))
    | _ -> ()

  member private this.Unregister() =
    match box source with
    | :? IMapSinkRegistry as r ->
      r.RemoveMapSink(box(this :> IMapDeltaSink<'K, 'V>))
    | _ -> ()

  member private this.GetChild(g: 'G) =
    let mutable child = Unchecked.defaultof<GroupMapChildNode<'K, 'V>>

    if not(groups.TryGetValue(g, &child)) then
      child <- new GroupMapChildNode<'K, 'V>()
      groups[g] <- child

    child

  /// Drop a group that became empty: the child dies with the group (its
  /// consumers' entries are removed by the output rem delta).
  member private this.DropGroupIfEmpty
    (g: 'G, child: GroupMapChildNode<'K, 'V>)
    =
    if child.Count = 0 then
      groups.Remove g |> ignore
      output.Remove g |> ignore
      out.Rems <- Collections.bufferAppend out.Rems g

  member private this.ApplySet(k: 'K, v: 'V) =
    let newG = keyOf k v
    let mutable oldG = Unchecked.defaultof<'G>

    if memberGroup.TryGetValue(k, &oldG) then
      if oldG <> newG then
        // Group move: remove from the old child, add to the new one.
        let oldChild = groups[oldG]
        oldChild.ApplyRemOne k
        this.DropGroupIfEmpty(oldG, oldChild)

    // The key's current group, both on creation and on a move.
    memberGroup[k] <- newG

    let wasNew = not(output.ContainsKey newG)
    let child = this.GetChild newG

    if wasNew then
      output[newG] <- child

    child.ApplySetOne(k, v)

    if wasNew then
      out.Sets <-
        Collections.bufferAppend
          out.Sets
          (struct (newG, (child :> amap<'K, 'V>)))

  member private this.ApplyRem(k: 'K) =
    let mutable g = Unchecked.defaultof<'G>

    if memberGroup.TryGetValue(k, &g) then
      memberGroup.Remove k |> ignore
      let child = groups[g]
      child.ApplyRemOne k
      this.DropGroupIfEmpty(g, child)

  member private this.EnsureInitialized() =
    if not initialized then
      // Snapshot first, register between, then load (see ElementSetNode).
      let snapshot = Dictionary<'K, 'V>(source.GetValue())
      this.Register()
      let mutable e = snapshot.GetEnumerator()

      while e.MoveNext() do
        let kvp = e.Current
        this.ApplySet(kvp.Key, kvp.Value)

      depVersion <- source.Version
      initialized <- true

  /// Apply the source journal: route every entry into its group's child.
  member private this.Drain() =
    let rems = journal.Rems
    let sets = journal.Sets
    let remStart = rems.Count
    let setStart = sets.Count
    let mutable i = 0
    // Suspend the append-time cross-kind cancellation (see journalAppendMap).
    journal.InDrain[0] <- 1
    let mutable remsDone = 0
    let mutable setsDone = 0

    try
      while i < remStart do
        this.ApplyRem(rems.Items[i])
        i <- i + 1
        remsDone <- i

      i <- 0

      while i < setStart do
        let struct (k, v) = sets.Items[i]
        this.ApplySet(k, v)
        i <- i + 1
        setsDone <- i
    finally
      journal.InDrain[0] <- 0
      // Compact against the LIVE journal counts (see ElementSetNode).
      let remLive = journal.Rems.Count

      if remLive > remsDone then
        Array.Copy(
          journal.Rems.Items,
          remsDone,
          journal.Rems.Items,
          0,
          remLive - remsDone
        )

        journal.Rems.Count <- remLive - remsDone
      else
        journal.Rems.Count <- 0

      let setLive = journal.Sets.Count

      if setLive > setsDone then
        Array.Copy(
          journal.Sets.Items,
          setsDone,
          journal.Sets.Items,
          0,
          setLive - setsDone
        )

        journal.Sets.Count <- setLive - setsDone
      else
        journal.Sets.Count <- 0

  /// Drain the journal, then push the accumulated output delta once.
  member private this.Process() =
    let ctx = GraphContext.Default
    let wasActive = ctx.TxActive
    ctx.TxActive <- true

    try
      if not journal.IsEmpty then
        this.Drain()

      if not out.IsEmpty then
        version <- version + 1L
        Collections.pushMapDelta &sinks out
        out.Clear()
    finally
      ctx.TxActive <- wasActive

  interface IMapDeltaSink<'K, 'V> with
    member this.OnDeltas
      (
        setEntries: struct ('K * 'V)[],
        setCnt: int,
        removedKeys: 'K[],
        remCnt: int
      ) =
      if not disposed then
        Collections.journalAppendMap
          &journal
          setEntries
          setCnt
          removedKeys
          remCnt

        version <- version + 1L
        GraphContext.Default.BumpWriteGeneration()

  interface IAdaptiveMap<'G, amap<'K, 'V>> with
    member this.GetValue() =
      let ctx = GraphContext.Default
      ctx.ClaimOwner()

      try
        if disposed then
          invalidOp "This adaptive map has been disposed."

        this.EnsureInitialized()

        if source.Version <> depVersion then
          source.GetValue() |> ignore
          depVersion <- source.Version

        this.Process()
        AdaptiveRuntime.addDependency (this :> IAdaptiveObject) version
        output :> IReadOnlyDictionary<'G, amap<'K, 'V>>
      finally
        ctx.ReleaseOwner()

    member _.Version = version

  interface IDisposable with
    member this.Dispose() =
      if not disposed then
        disposed <- true
        this.Unregister()
        Collections.clearSinks &sinks

  interface IMapSinkRegistry with
    member this.AddMapSink(sink) = Collections.addSink &sinks sink

    member this.RemoveMapSink(sink) = Collections.removeSink &sinks sink
